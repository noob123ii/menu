/*
 * ii Reborn
 * Portions Copyright (C) 2025–2026 Goldentrophy Software
 * Licensed under GNU GPL v3.0-or-later — see LICENSE and NOTICE.
 * This file is part of a derivative work; see NOTICE for attribution
 * and modification history. Do not remove this notice.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using iiMenu.Classes.Menu;
using iiMenu.Menu;
using UnityEngine;
using static iiMenu.Menu.Main;
using Object = UnityEngine.Object;

namespace iiMenu.Managers
{
    /// <summary>
    /// A loading screen styled like the menu, drawn with IMGUI.
    ///
    /// Every previous version built world space geometry parented to a camera and came out
    /// sideways, wrongly scaled, or invisible, because the menu's own layout only makes
    /// sense once its VR presentation rotation is applied and that chain could not be
    /// reproduced reliably without being able to see the result. IMGUI is drawn by Unity
    /// in screen space, so there is no camera, no parent, no rotation and no frustum
    /// arithmetic involved and the orientation cannot be wrong. It also appears in every
    /// view rather than needing one copy per camera.
    ///
    /// The colours still come from the menu's own gradients, so it reads as the menu even
    /// though the text is not the menu's TextMeshPro.
    /// </summary>
    public class LoadingScreenGUI : MonoBehaviour
    {
        private static readonly string[] StageNames =
        {
            "Initializing",
            "Loading Assets",
            "Applying Patches",
            "Loading Preferences",
            "Finalizing",
            "Skip <color=green>[Esc]</color>"
        };

        private const int FillableStages = 5;

        private GUIStyle labelStyle;
        private GUIStyle titleStyle;
        private Texture2D backgroundTexture;
        private Texture2D buttonTexture;
        private Texture2D fillTexture;
        private bool stylesReady;

        public float Progress { get; set; }
        public int Stage { get; set; }
        public int StageCount => FillableStages;

        private void OnGUI()
        {
            if (!LoadingScreenManager.Active || !stylesReady)
                return;

            Draw();
        }

        private void Draw()
        {
            float width = Mathf.Min(Screen.width * 0.42f, 520f);
            float rowHeight = 38f;
            float padding = 14f;

            int rows = StageNames.Length + 2; // stages, skip, plus a gap for the header
            float height = padding * 2f + rows * rowHeight + 56f;

            Rect panel = new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f,
                width,
                height);

            GUI.DrawTexture(panel, backgroundTexture);

            float y = panel.y + padding;
            float innerWidth = width - padding * 2f;
            float innerX = panel.x + padding;

            // Title, then the overall progress bar, then the stages.
            Rect titleRect = new Rect(innerX, y, innerWidth, 34f);
            GUI.Label(titleRect, "ii <b>Reborn</b>", titleStyle);
            y += 40f;

            float overall = (Stage + Mathf.Clamp01(Progress)) / FillableStages;
            DrawBar(new Rect(innerX, y, innerWidth, 26f), overall, $"Loading {Mathf.RoundToInt(overall * 100f)}%");
            y += 40f;

            for (int i = 0; i < StageNames.Length; i++)
            {
                float fill;

                if (i < Stage)
                    fill = 1f;
                else if (i == Stage)
                    fill = Mathf.Clamp01(Progress);
                else
                    fill = 0f;

                DrawBar(new Rect(innerX, y, innerWidth, rowHeight - 8f), fill, StageNames[i]);
                y += rowHeight;
            }
        }

        /// <summary>A button face with a fill sweeping left to right across it.</summary>
        private void DrawBar(Rect rect, float fill, string text)
        {
            GUI.DrawTexture(rect, buttonTexture);

            if (fill > 0f)
            {
                Rect filled = new Rect(rect.x, rect.y, rect.width * fill, rect.height);
                GUI.DrawTexture(filled, fillTexture);
            }

            Rect textRect = new Rect(rect.x, rect.y, rect.width, rect.height);
            GUI.Label(textRect, text, labelStyle);
        }

        private void EnsureStyles()
        {
            if (stylesReady)
                return;

            backgroundTexture = Solid(new Color(0.09f, 0.05f, 0.02f, 0.96f));
            buttonTexture = Solid(new Color(0.30f, 0.16f, 0.03f, 1f));
            fillTexture = Solid(new Color(0.66f, 0.36f, 0.06f, 1f));

            Color text = textColors.Length > 1 ? textColors[1].GetColor(0) : Color.white;
            Color heading = textColors.Length > 0 ? textColors[0].GetColor(0) : Color.white;

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Italic,
                richText = true,
                wordWrap = false
            };
            labelStyle.normal.textColor = text;

            titleStyle = new GUIStyle(labelStyle)
            {
                fontSize = 30
            };
            titleStyle.normal.textColor = heading;

            stylesReady = true;
        }

        private static Texture2D Solid(Color color)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            texture.SetPixel(0, 0, color);
            texture.Apply();

            return texture;
        }

        private void OnDestroy()
        {
            DestroyTexture(ref backgroundTexture);
            DestroyTexture(ref buttonTexture);
            DestroyTexture(ref fillTexture);
        }

        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture == null)
                return;

            Destroy(texture);
            texture = null;
        }
    }

    /// <summary>Owns the loading screen's state and the object that draws it.</summary>
    public static class LoadingScreenManager
    {
        /// <summary>True while the loading screen is up.</summary>
        public static bool Active { get; private set; }

        /// <summary>Plays the loading screen once automatically after boot.</summary>
        public static bool PlayOnceOnStartup = true;

        /// <summary>Seconds to wait after boot before it appears.</summary>
        public static float StartupDelay = 0.6f;

        /// <summary>Total seconds the sequence takes, spread evenly over the stages.</summary>
        public static float TotalDuration = 4f;

        private static LoadingScreenGUI gui;
        private static float stageProgress;
        private static int stageIndex;

        /// <summary>Plays the screen once after boot, with no input required.</summary>
        public static System.Collections.IEnumerator PlayOnStartup()
        {
            if (!PlayOnceOnStartup)
                yield break;

            float until = Time.time + StartupDelay;

            while (Time.time < until)
                yield return null;

            LogManager.Log("Loading screen: startup autoplay firing.");
            Show();
        }

        public static void Show()
        {
            try
            {
                ShowInternal();
            }
            catch (Exception exception)
            {
                LogManager.LogError($"Loading screen: threw while showing. {exception}");
            }
        }

        private static void ShowInternal()
        {
            Hide();

            GameObject host = new GameObject("iiMenu_LoadingScreen");
            Object.DontDestroyOnLoad(host);

            gui = host.AddComponent<LoadingScreenGUI>();
            host.AddComponent<LoadingScreenTick>();

            stageIndex = 0;
            stageProgress = 0f;
            Active = true;

            LogManager.Log($"Loading screen: shown, {gui.StageCount} stages, total {TotalDuration}s.");
        }

        public static void Hide()
        {
            Active = false;
            stageIndex = 0;
            stageProgress = 0f;

            if (gui != null)
            {
                Object.Destroy(gui.gameObject);
                gui = null;
            }
        }

        internal static void Tick()
        {
            if (!Active)
                return;

            try
            {
                TickInternal();
            }
            catch (Exception exception)
            {
                LogManager.LogError($"Loading screen: threw while ticking. {exception}");
            }
        }

        private static void TickInternal()
        {
            if (EscapePressed())
            {
                LogManager.Log("Loading screen: dismissed by Escape.");
                Hide();
                return;
            }

            if (stageIndex >= gui.StageCount)
                return;

            float perStage = TotalDuration <= 0f ? 0f : TotalDuration / gui.StageCount;
            stageProgress += perStage <= 0f ? 1f : Time.deltaTime / perStage;

            if (stageProgress >= 1f)
            {
                stageProgress = 0f;
                stageIndex++;
            }

            if (gui != null)
            {
                gui.Stage = stageIndex;
                gui.Progress = stageProgress;
            }
        }

        private static bool EscapePressed()
        {
            try
            {
                return UnityEngine.InputSystem.Keyboard.current != null &&
                       UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Advances the loading screen. A separate component so the IMGUI component can stay
    /// purely about drawing.
    /// </summary>
    internal sealed class LoadingScreenTick : MonoBehaviour
    {
        private void Update()
        {
            LoadingScreenManager.Tick();
        }
    }
}
