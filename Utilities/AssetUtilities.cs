/*
 * ii Reborn
 * Portions Copyright (C) 2025–2026 Goldentrophy Software
 * Licensed under GNU GPL v3.0-or-later — see LICENSE and NOTICE.
 * This file is part of a derivative work; see NOTICE for attribution
 * and modification history. Do not remove this notice.
 */

using iiMenu.Managers;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using static iiMenu.Utilities.FileUtilities;

namespace iiMenu.Utilities
{
    public class AssetUtilities
    {
        private static AssetBundle assetBundle;
        private static void LoadAssetBundle()
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{PluginInfo.ClientResourcePath}.iimenu");
            if (stream != null)
                assetBundle = AssetBundle.LoadFromStream(stream);
            else
                LogManager.LogError("Failed to load assetbundle");
        }

        public static T LoadObject<T>(string assetName) where T : Object
        {
            if (assetBundle == null)
                LoadAssetBundle();

            T gameObject = Object.Instantiate(assetBundle.LoadAsset<T>(assetName));
            return gameObject;
        }

        public static T LoadAsset<T>(string assetName) where T : Object
        {
            if (assetBundle == null)
                LoadAssetBundle();

            T gameObject = assetBundle.LoadAsset(assetName) as T;
            return gameObject;
        }

        public static readonly Dictionary<string, AudioClip> audioFilePool = new Dictionary<string, AudioClip>();

        public const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

        private static readonly HashSet<string> soundsLoading = new HashSet<string>();
        private static readonly Dictionary<string, List<System.Action<AudioClip>>> soundWaiters = new Dictionary<string, List<System.Action<AudioClip>>>();

        /// <summary>
        /// Loads a clip off disk, decoding it without blocking the caller.
        ///
        /// The decode used to be done by spinning on 'while (!newvar.isDone) { }'. That
        /// froze the main thread for the whole decode and was a deadlock hazard too, since
        /// Unity pumps UnityWebRequestAsyncOperation on the very thread that has to advance
        /// it. It runs as a coroutine now, which means the clip does not exist yet when
        /// this returns and the first call hands back null. That is fine for the menu's
        /// own click sounds, but for a sound the player explicitly asked to hear, a null
        /// return meant the click was silently dropped. Callers that need the clip now
        /// pass onLoaded and get called back with it once the decode finishes.
        /// </summary>
        public static AudioClip LoadSoundFromFile(string fileName, System.Action<AudioClip> onLoaded = null) // Thanks to ShibaGT for help with loading the audio from file
        {
            if (audioFilePool.TryGetValue(fileName, out var cached))
            {
                onLoaded?.Invoke(cached);
                return cached;
            }

            lock (soundsLoading)
            {
                if (onLoaded != null)
                {
                    if (!soundWaiters.TryGetValue(fileName, out var waiting))
                        soundWaiters[fileName] = waiting = new List<System.Action<AudioClip>>();
                    waiting.Add(onLoaded);
                }

                if (!soundsLoading.Add(fileName))
                    return null;
            }

            if (CoroutineManager.instance == null)
            {
                // Nothing will ever advance the decode, so release the waiters rather than
                // leaving them hanging on a clip that is never coming.
                lock (soundsLoading)
                    soundsLoading.Remove(fileName);

                CompleteWaiters(fileName, null);
                return null;
            }

            CoroutineManager.instance.StartCoroutine(LoadSoundFromFileAsync(fileName));

            return null;
        }

        private static void CompleteWaiters(string fileName, AudioClip clip)
        {
            List<System.Action<AudioClip>> waiting = null;

            lock (soundsLoading)
            {
                if (soundWaiters.TryGetValue(fileName, out waiting))
                    soundWaiters.Remove(fileName);
            }

            if (waiting == null)
                return;

            foreach (System.Action<AudioClip> waiter in waiting)
            {
                try { waiter(clip); }
                catch (System.Exception exception)
                {
                    LogManager.LogError($"Sound callback for {fileName} threw: {exception.Message}");
                }
            }
        }

        private static System.Collections.IEnumerator LoadSoundFromFileAsync(string fileName)
        {
            AudioClip loaded = null;

            try
            {
                string filePath = $"{GetGamePath()}/{PluginInfo.BaseDirectory}/{fileName}";

                UnityWebRequest request = null;

                // Kept outside the yield so the iterator does not sit in a try/catch,
                // which C# does not allow.
                try
                {
                    request = UnityWebRequestMultimedia.GetAudioClip($"file://{filePath}", GetAudioType(GetFileExtension(fileName)));
                }
                catch (System.Exception exception)
                {
                    LogManager.LogError($"Failed to load sound {fileName}: {exception.Message}");
                }

                if (request == null)
                {
                    LogManager.LogError($"Failed to load sound {fileName}: could not open {filePath}.");
                    yield break;
                }

                using (request)
                {
                    yield return request.SendWebRequest();

                    try
                    {
                        if (request.result != UnityWebRequest.Result.Success)
                        {
                            LogManager.LogError($"Failed to load sound {fileName}: {request.error} ({filePath})");
                        }
                        else
                        {
                            loaded = DownloadHandlerAudioClip.GetContent(request);

                            if (loaded == null)
                                LogManager.LogError($"Failed to decode sound {fileName}: {filePath} is not decodable audio.");
                            else
                                audioFilePool[fileName] = loaded;
                        }
                    }
                    catch (System.Exception exception)
                    {
                        LogManager.LogError($"Failed to decode sound {fileName}: {exception.Message}");
                    }
                }
            }
            finally
            {
                lock (soundsLoading)
                    soundsLoading.Remove(fileName);

                CompleteWaiters(fileName, loaded);
            }
        }

        private static readonly HashSet<string> soundsDownloading = new HashSet<string>();

        public static AudioClip LoadSoundFromURL(string resourcePath, string fileName)
        {
            string filePath = $"{PluginInfo.BaseDirectory}/{fileName}";
            string directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
                // ReSharper disable once AssignNullToNotNullAttribute
                Directory.CreateDirectory(directory);

            if (File.Exists(filePath)) return LoadSoundFromFile(fileName);

            BeginSoundDownload(resourcePath, filePath, fileName);
            return null;
        }

        private static void BeginSoundDownload(string resourcePath, string filePath, string fileName)
        {
            lock (soundsDownloading)
            {
                if (!soundsDownloading.Add(fileName))
                    return;
            }

            LogManager.Log("Downloading " + fileName);

            Thread worker = new Thread(() =>
            {
                string temporaryPath = filePath + ".part";

                try
                {
                    using HttpClient http = new HttpClient { Timeout = System.TimeSpan.FromSeconds(8) };
                    http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
                    byte[] data = http.GetByteArrayAsync(resourcePath).GetAwaiter().GetResult();
                    File.WriteAllBytes(temporaryPath, data);

                    if (File.Exists(filePath))
                        File.Delete(filePath);

                    File.Move(temporaryPath, filePath);
                }
                catch (System.Exception e)
                {
                    LogManager.LogError($"Failed to download {fileName} from {resourcePath}: {e.Message}");

                    try
                    {
                        if (File.Exists(temporaryPath))
                            File.Delete(temporaryPath);
                    }
                    catch
                    {
                    }
                }
                finally
                {
                    lock (soundsDownloading)
                        soundsDownloading.Remove(fileName);
                }
            })
            {
                IsBackground = true,
                Name = $"ii Reborn sound download ({fileName})"
            };

            worker.Start();
        }

        public static readonly Dictionary<string, Texture2D> textureResourceDictionary = new Dictionary<string, Texture2D>();
        public static Texture2D LoadTextureFromResource(string resourcePath)
        {
            if (textureResourceDictionary.TryGetValue(resourcePath, out Texture2D existingTexture))
                return existingTexture;

            Texture2D texture = new Texture2D(2, 2);

            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourcePath);
            if (stream != null)
            {
                byte[] fileData = new byte[stream.Length];
                // ReSharper disable once MustUseReturnValue
                stream.Read(fileData, 0, (int)stream.Length);
                texture.LoadImage(fileData);
            }
            else
                LogManager.LogError("Failed to load texture from resource: " + resourcePath);

            textureResourceDictionary[resourcePath] = texture;

            return texture;
        }

        public static readonly Dictionary<string, Texture2D> textureUrlDictionary = new Dictionary<string, Texture2D>();
        public static Texture2D LoadTextureFromURL(string resourcePath, string fileName)
        {
            if (textureUrlDictionary.TryGetValue(resourcePath, out Texture2D existingTexture))
                return existingTexture;

            string filePath = $"{PluginInfo.BaseDirectory}/{fileName}";
            string directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
                // ReSharper disable once AssignNullToNotNullAttribute
                Directory.CreateDirectory(directory);

            Texture2D texture;
            if (!TryLoadTextureFile(filePath, out texture))
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);

                try
                {
                    LogManager.Log("Downloading " + fileName);
                    using HttpClient http = new HttpClient { Timeout = System.TimeSpan.FromSeconds(15) };
                    http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
                    byte[] bytes = http.GetByteArrayAsync(resourcePath).GetAwaiter().GetResult();
                    if (!TryCreateTexture(bytes, out texture))
                        throw new InvalidDataException("The response was not a valid image.");

                    File.WriteAllBytes(filePath, bytes);
                }
                catch (System.Exception ex)
                {
                    LogManager.LogError($"Failed to download texture {fileName} from {resourcePath}: {ex.Message}");
                    texture = CreateFallbackTexture();
                }
            }

            textureUrlDictionary[resourcePath] = texture;
            textureFileDirectory[fileName] = texture;
            return texture;
        }

        public static readonly Dictionary<string, Texture2D> textureFileDirectory = new Dictionary<string, Texture2D>();
        public static Texture2D LoadTextureFromFile(string fileName)
        {
            if (textureFileDirectory.TryGetValue(fileName, out Texture2D existingTexture))
                return existingTexture;

            string filePath = $"{PluginInfo.BaseDirectory}/{fileName}";
            if (!TryLoadTextureFile(filePath, out Texture2D texture))
            {
                LogManager.LogError("Failed to load texture file: " + fileName);
                texture = CreateFallbackTexture();
            }

            textureFileDirectory[fileName] = texture;
            return texture;
        }

        private static bool TryLoadTextureFile(string filePath, out Texture2D texture)
        {
            texture = null;
            try
            {
                if (!File.Exists(filePath))
                    return false;

                return TryCreateTexture(File.ReadAllBytes(filePath), out texture);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryCreateTexture(byte[] bytes, out Texture2D texture)
        {
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (bytes == null || bytes.Length == 0 || !texture.LoadImage(bytes))
            {
                Object.Destroy(texture);
                texture = null;
                return false;
            }

            return true;
        }

        private static Texture2D CreateFallbackTexture()
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            texture.Apply();
            return texture;
        }

        public static void ReleaseAll()
        {
            foreach (var kv in audioFilePool) { if (kv.Value != null) Object.Destroy(kv.Value); }
            audioFilePool.Clear();
            foreach (var kv in textureResourceDictionary) { if (kv.Value != null) Object.Destroy(kv.Value); }
            textureResourceDictionary.Clear();
            foreach (var kv in textureUrlDictionary) { if (kv.Value != null) Object.Destroy(kv.Value); }
            textureUrlDictionary.Clear();
            foreach (var kv in textureFileDirectory) { if (kv.Value != null && !textureUrlDictionary.ContainsValue(kv.Value)) Object.Destroy(kv.Value); }
            textureFileDirectory.Clear();
            if (assetBundle != null) { try { assetBundle.Unload(false); } catch { } assetBundle = null; }
        }
    }
}
