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
using TMPro;
using UnityEngine;
using static iiMenu.Menu.Main;
using Object = UnityEngine.Object;

namespace iiMenu.Managers
{
    /// <summary>
    /// The loading screen, drawn with IMGUI so the orientation and the position cannot be
    /// wrong. Every earlier version built world space geometry parented to a camera and came
    /// out sideways, mis-scaled or invisible, because the menu's own layout only makes
    /// sense once its VR presentation rotation is applied and that chain could not be
    /// reproduced reliably without being able to see the result.
    ///
    /// The look is taken from the menu rather than from hardcoded values: the panel, the
    /// buttons, the borders and the text all come from the same gradients the menu uses,
    /// and the typeface is the menu's own font handed to IMGUI through
    /// TMP_FontAsset.sourceFontFile. It follows whatever theme is active, so it stays
    /// consistent with the menu instead of guessing at the default palette.
    /// </summary>
    public class LoadingScreenGUI : MonoBehaviour
    {
        public static readonly string[] StageNames =
        {
            "Initializing",
            "Loading Assets",
            "Applying Patches",
            "Loading Preferences",
            "Finalizing",
            "Skip <color=green>[Esc]</color>"
        };

        /// <summary>Everything except the skip row actually fills.</summary>
        public const int FillableStages = 5;

        public float Progress { get; set; }
        public int Stage { get; set; }
        public int StageCount => FillableStages;

        private const int Border = 2;
        private const int Gap = 5;

        private GUIStyle titleStyle;
        private GUIStyle subtitleStyle;
        private GUIStyle buttonStyle;
        private GUIStyle percentStyle;
        private readonly Dictionary<int, Texture2D> solidCache = new Dictionary<int, Texture2D>();
        private bool stylesReady;
        private bool drewOnce;

        private Color panel;
        private Color backdrop;
        private Color button;
        private Color accent;
        private Color fill;
        private Color heading;
        private Color body;
        private Color dim;

        private void Start() => EnsureStyles();

        private void OnGUI()
        {
            if (!LoadingScreenManager.Active)
                return;

            if (!stylesReady)
                EnsureStyles();

            if (!stylesReady)
                return;

            Draw();
        }

        private void EnsureStyles()
        {
            if (stylesReady)
                return;

            // Straight from the menu's own gradients, so the screen tracks the active theme.
            // backgroundColor is a single gradient on the menu, the rest are pairs.
            panel = Darken(backgroundColor);
            button = Colour(buttonColors, 0);
            accent = Colour(buttonColors, 1);
            heading = Colour(textColors, 0);
            body = Colour(textColors, 1);
            dim = new Color(heading.r, heading.g, heading.b, 0.55f);

            // The fill reads as the button being pressed: the same hue pushed brighter, so
            // it stays inside whatever palette the menu is using.
            fill = Color.Lerp(button, Color.white, 0.32f);
            if (fill == button)
                fill = Color.Lerp(button, accent, 0.5f);

            // Full screen backdrop: the panel's own hue taken almost to black, so it
            // belongs to the theme but lets the panel and the accent carry the screen.
            backdrop = new Color(panel.r * 0.3f, panel.g * 0.3f, panel.b * 0.3f, 0.94f);

            Font font = MenuFont();

            titleStyle = Style(font, 34, heading, FontStyle.Italic);
            subtitleStyle = Style(font, 15, dim, FontStyle.Italic);
            buttonStyle = Style(font, 19, body, FontStyle.Italic);
            percentStyle = Style(font, 15, dim, FontStyle.Italic);

            stylesReady = true;
        }

        /// <summary>
        /// The menu's own font, handed to IMGUI as a legacy Font.
        ///
        /// The menu does not load its fonts until Main.OnLaunch runs, which is when the
        /// user presses Q. This screen plays by itself before that, so at the moment it
        /// draws, activeFont is still the LiberationSans default and AgencyFB is still
        /// null. Reading activeFont directly here would hand IMGUI the wrong typeface and
        /// the screen would not look like the menu. Initialize the fonts first, then
        /// prefer the font the menu is actually using.
        /// </summary>
        private static Font MenuFont()
        {
            try
            {
                if (AgencyFB == null)
                    InitializeFonts();

                // On launch the menu sets activeFont to AgencyFB, but the player can change
                // it in settings, and that choice is applied on launch too. So once
                // activeFont is no longer the default it is the font the menu is showing
                // and it wins; otherwise fall back to the menu's default typeface.
                TMP_FontAsset asset =
                    (activeFont != null && activeFont != LiberationSans) ? activeFont :
                    (AgencyFB != null ? AgencyFB : activeFont);

                return asset != null ? asset.sourceFontFile : null;
            }
            catch
            {
                return null;
            }
        }

        private static Color Colour(ExtGradient[] gradients, int index)
        {
            try
            {
                if (gradients != null && gradients.Length > index && gradients[index] != null)
                    return gradients[index].GetColor(0);
            }
            catch { }

            return Color.gray;
        }

        /// <summary>The menu's panel colour, forced dark so the text stays readable.</summary>
        private static Color Darken(ExtGradient gradient)
        {
            try
            {
                if (gradient == null)
                    return new Color(0.11f, 0.11f, 0.13f);

                Color color = gradient.GetColor(0);
                float luma = color.r * 0.299f + color.g * 0.587f + color.b * 0.114f;

                return Color.Lerp(color, Color.black, Mathf.Clamp01(0.3f + luma * 0.5f));
            }
            catch
            {
                return new Color(0.11f, 0.11f, 0.13f);
            }
        }

        private static GUIStyle Style(Font font, int size, Color color, FontStyle fontStyle)
        {
            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = size,
                fontStyle = fontStyle,
                richText = true,
                wordWrap = false
            };

            if (font != null)
                style.font = font;

            style.normal.textColor = color;

            return style;
        }

        private void Draw()
        {
            if (!drewOnce)
            {
                drewOnce = true;
                LogManager.Log($"Loading screen: first draw at {Screen.width}x{Screen.height}.");
            }

            // A full screen backdrop so this reads as an overlay that has taken over the
            // view, not as a stray panel, while the panel itself keeps the menu's look.
            Fill(new Rect(0f, 0f, Screen.width, Screen.height), backdrop);

            float width = Mathf.Clamp(Screen.width * 0.30f, 340f, 520f);
            float rowHeight = 42f;
            float headHeight = 78f;
            float padding = 16f;

            float height = padding * 2f + headHeight + StageNames.Length * (rowHeight + Gap);

            Rect outer = new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f,
                width,
                height);

            // Panel with an accent border, matching the menu's outlined look.
            Fill(outer, accent);
            Fill(Inset(outer, Border), panel);

            float x = outer.x + padding;
            float inner = width - padding * 2f;
            float y = outer.y + padding;

            GUI.Label(new Rect(x, y, inner, 40f), "ii <b>Reborn</b>", titleStyle);
            y += 40f;

            float overall = (Stage + Mathf.Clamp01(Progress)) / FillableStages;
            GUI.Label(new Rect(x, y, inner, 20f), "Loading", subtitleStyle);
            GUI.Label(new Rect(x, y, inner, 20f), $"{Mathf.RoundToInt(overall * 100f)}%", percentStyle);
            y += 22f;

            DrawBar(new Rect(x, y, inner, 10f), overall);
            y += 22f;

            for (int i = 0; i < StageNames.Length; i++)
            {
                float fill;

                if (i < Stage)
                    fill = 1f;
                else if (i == Stage)
                    fill = Mathf.Clamp01(Progress);
                else
                    fill = 0f;

                DrawRow(new Rect(x, y, inner, rowHeight), StageNames[i], fill, i == Stage);
                y += rowHeight + Gap;
            }
        }

        /// <summary>A stage row: accent outlined, dark inside, with the fill sweeping left.</summary>
        private void DrawRow(Rect rect, string text, float amount, bool active)
        {
            Fill(rect, active ? accent : Fade(accent, 0.45f));
            Fill(Inset(rect, 1f), button);

            if (amount > 0f)
                Fill(new Rect(rect.x + 1f, rect.y + 1f, (rect.width - 2f) * amount, rect.height - 2f), fill);

            GUI.Label(rect, text, buttonStyle);
        }

        private void DrawBar(Rect rect, float progress)
        {
            Fill(rect, Fade(accent, 0.5f));
            Fill(Inset(rect, 1f), button);

            float width = (rect.width - 2f) * Mathf.Clamp01(progress);

            if (width > 0f)
                Fill(new Rect(rect.x + 1f, rect.y + 1f, width, rect.height - 2f), fill);
        }

        private static Rect Inset(Rect rect, float amount) =>
            new Rect(rect.x + amount, rect.y + amount, rect.width - amount * 2f, rect.height - amount * 2f);

        private static Color Fade(Color color, float amount) =>
            new Color(color.r, color.g, color.b, color.a * (1f - amount));

        private void Fill(Rect rect, Color color) =>
            GUI.DrawTexture(rect, Solid(color), ScaleMode.StretchToFill, true);

        private Texture2D Solid(Color color)
        {
            int key = ((Color32)color).GetHashCode();
            key = key * 397 ^ (int)(color.r * 255f) ^ (int)(color.a * 255f);

            if (solidCache.TryGetValue(key, out Texture2D cached) && cached != null)
                return cached;

            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp
            };

            texture.SetPixel(0, 0, color);
            texture.Apply();

            solidCache[key] = texture;

            return texture;
        }

        private void OnDestroy()
        {
            foreach (Texture2D texture in solidCache.Values)
            {
                if (texture != null)
                    Destroy(texture);
            }

            solidCache.Clear();
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
    /// Advances the loading screen. Separate from the drawing component so that one can
    /// stay purely about IMGUI.
    /// </summary>
    internal sealed class LoadingScreenTick : MonoBehaviour
    {
        private void Update()
        {
            LoadingScreenManager.Tick();
        }
    }
}
