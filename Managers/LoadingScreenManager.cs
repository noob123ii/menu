/*
 * ii Reborn
 * Portions Copyright (C) 2025–2026 Goldentrophy Software
 * Licensed under GNU GPL v3.0-or-later — see LICENSE and NOTICE.
 * This file is part of a derivative work; see NOTICE for attribution
 * and modification history. Do not remove this notice.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using iiMenu.Classes.Menu;
using iiMenu.Extensions;
using iiMenu.Menu;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static iiMenu.Menu.Main;
using Object = UnityEngine.Object;

namespace iiMenu.Managers
{
    /// <summary>
    /// Drives <see cref="LoadingScreenManager"/>. Top level rather than nested so Unity is
    /// happy to attach it to a GameObject.
    /// </summary>
    internal sealed class LoadingScreenTick : MonoBehaviour
    {
        private void Update()
        {
            if (LoadingScreenManager.Active)
                LoadingScreenManager.Tick();
        }
    }

    /// <summary>
    /// A loading screen that looks exactly like the PC menu, built by the menu's own code.
    ///
    /// Rather than reimplementing what the menu does, this points Main.menu and
    /// Main.canvasObj at a private set of objects and then calls Main's own private
    /// AddButton(offset, index, buttonInfo) through reflection for each stage. The cubes,
    /// the ColorChanger, the ButtonCollider, the label placement and the 180/90/90 label
    /// rotation are therefore produced by exactly the same code that produces the real
    /// menu, which is the only way this stopped looking wrong. Every earlier attempt
    /// reimplemented those pieces and each one looked wrong in a different way.
    ///
    /// The real menu's statics are saved and restored around the build, so nothing outside
    /// this screen is affected and no category is hijacked.
    /// </summary>
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

        /// <summary>Metres in front of the camera the screen sits.</summary>
        public static float ViewDistance = 0.5f;

        private const float FillDepth = 0.004f;

        private static readonly string[] StageNames =
        {
            "Initializing",
            "Loading Assets",
            "Applying Patches",
            "Loading Preferences",
            "Finalizing"
        };

        private const string SkipText = "Skip Loading";
        private const string SkipLabel = "Skip <color=green>[Esc]</color>";

        private static readonly List<Material> materials = new List<Material>();

        private static GameObject runner;
        private static GameObject root;
        private static GameObject background;
        private static GameObject canvasObject;
        private static Transform headerFill;
        private static TextMeshPro headerText;
        private static Transform[] fills;
        private static Camera presentCamera;

        private static int stageIndex;
        private static float stageProgress;

        private static MethodInfo addButtonMethod;

        /// <summary>Plays the screen once after boot, with no input required.</summary>
        public static IEnumerator PlayOnStartup()
        {
            if (!PlayOnceOnStartup)
                yield break;

            float until = Time.time + StartupDelay;

            while (Time.time < until)
                yield return null;

            int waited = 0;

            while (PresentingCamera() == null && waited < 900)
            {
                waited++;
                yield return null;
            }

            LogManager.Log("Loading screen: startup autoplay firing.");
            Show();
        }

        private static Camera PresentingCamera()
        {
            Camera firstPerson = null;

            try
            {
                if (GorillaTagger.Instance != null && GorillaTagger.Instance.mainCamera != null)
                    firstPerson = GorillaTagger.Instance.mainCamera.GetComponent<Camera>();
            }
            catch { }

            if (firstPerson != null && firstPerson.isActiveAndEnabled)
                return firstPerson;

            if (TPC != null && TPC.isActiveAndEnabled)
                return TPC;

            try
            {
                if (Camera.main != null && Camera.main.isActiveAndEnabled)
                    return Camera.main;
            }
            catch { }

            return null;
        }

        /// <summary>
        /// BepInEx cannot write Unity's log on this install ("Unable to start Unity log
        /// writer"), so Unity level exceptions are invisible. Everything is caught and
        /// logged here for that reason.
        /// </summary>
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

            presentCamera = PresentingCamera();

            if (presentCamera == null)
            {
                LogManager.LogError("Loading screen: no presenting camera, cannot build.");
                return;
            }

            if (!Build())
            {
                Hide();
                return;
            }

            stageIndex = 0;
            stageProgress = 0f;
            Active = true;

            runner = new GameObject("iiMenu_LoadingScreenRunner");
            runner.AddComponent<LoadingScreenTick>();

            LogManager.Log($"Loading screen: built on {presentCamera.name}, " +
                           $"{StageNames.Length} stages plus skip, total {TotalDuration}s.");

            Render();
        }

        /// <summary>Tears the screen down and releases everything it allocated.</summary>
        public static void Hide()
        {
            Active = false;
            stageIndex = 0;
            stageProgress = 0f;
            fills = null;
            presentCamera = null;
            headerFill = null;
            headerText = null;

            if (root != null)
            {
                Object.Destroy(root);
                root = null;
            }

            background = null;
            canvasObject = null;

            foreach (Material material in materials)
            {
                if (material != null)
                    Object.Destroy(material);
            }

            materials.Clear();

            if (runner != null)
            {
                Object.Destroy(runner);
                runner = null;
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

            // Deliberately no timeout. Once every stage has filled it stays up.
            if (stageIndex >= StageNames.Length)
                return;

            float perStage = TotalDuration <= 0f ? 0f : TotalDuration / StageNames.Length;
            stageProgress += perStage <= 0f ? 1f : Time.deltaTime / perStage;

            if (stageProgress >= 1f)
            {
                stageProgress = 0f;
                stageIndex++;
            }

            Render();
        }

        private static bool EscapePressed()
        {
            try
            {
                Keyboard keyboard = Keyboard.current;
                return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Builds the screen. The menu's own statics are pointed at private objects for the
        /// duration so its button builder draws into this screen, then restored.
        /// </summary>
        private static bool Build()
        {
            if (addButtonMethod == null)
            {
                addButtonMethod = typeof(Main).GetMethod(
                    "AddButton",
                    BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[] { typeof(float), typeof(int), typeof(ButtonInfo) },
                    null);

                if (addButtonMethod == null)
                {
                    LogManager.LogError("Loading screen: could not find Main.AddButton, aborting.");
                    return false;
                }
            }

            GameObject savedMenu = Main.menu;
            GameObject savedBackground = Main.menuBackground;
            GameObject savedCanvas = Main.canvasObj;
            int savedIndex = Buttons.CurrentCategoryIndex;

            try
            {
                CreateRoot();
                CreateBackground();
                CreateCanvas();

                fills = new Transform[StageNames.Length + 1];

                for (int i = 0; i < StageNames.Length; i++)
                {
                    var info = new ButtonInfo
                    {
                        buttonText = StageNames[i],
                        isTogglable = false,
                        method = () => { },
                        toolTip = "Loading."
                    };

                    if (!AddStage(i, info, out Transform button))
                        return false;

                    fills[i] = CreateFill(button);
                }

                var skipInfo = new ButtonInfo
                {
                    buttonText = SkipText,
                    overlapText = SkipLabel,
                    isTogglable = false,
                    method = Hide,
                    toolTip = "Ends the loading screen early."
                };

                AddStage(StageNames.Length, skipInfo, out _);

                CreateHeader();
            }
            finally
            {
                // Always put the menu back exactly as it was, even if the build threw.
                Main.menu = savedMenu;
                Main.menuBackground = savedBackground;
                Main.canvasObj = savedCanvas;
                Buttons.CurrentCategoryIndex = savedIndex;
            }

            return true;
        }

        /// <summary>
        /// Hands one button to the menu's own builder, then finds the cube it made so a fill
        /// can be parented onto it. Buttons are found by ButtonCollider.relatedText.
        /// </summary>
        private static bool AddStage(int index, ButtonInfo info, out Transform button)
        {
            float offset = index * Main.ButtonDistance;

            addButtonMethod.Invoke(null, new object[] { offset, index, info });

            button = null;

            foreach (Transform child in root.transform)
            {
                ButtonCollider collider = child.GetComponent<ButtonCollider>();

                if (collider != null && collider.relatedText == info.buttonText)
                {
                    button = child;
                    break;
                }
            }

            if (button == null)
            {
                LogManager.LogError($"Loading screen: the menu builder did not produce a button for '{info.buttonText}'.");
                return false;
            }

            return true;
        }

        private static void CreateRoot()
        {
            // Same cube the menu makes for itself, and the same scale, with the renderer
            // destroyed exactly as the menu does.
            root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(root.GetComponent<BoxCollider>());
            Object.Destroy(root.GetComponent<Renderer>());

            root.name = "iiMenu_LoadingScreen";
            root.transform.SetParent(presentCamera.transform, false);
            root.transform.localPosition = new Vector3(0f, 0f, ViewDistance);
            root.transform.localRotation = Quaternion.Euler(-90f, 90f, 0f);
            root.transform.localScale = new Vector3(0.1f, 0.3f, 0.3825f) * menuScale;

            Main.menu = root;
        }

        private static void CreateBackground()
        {
            background = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(background.GetComponent<Collider>());

            background.name = "Background";
            background.transform.SetParent(root.transform, false);
            background.transform.localPosition = new Vector3(0.50f, 0f, 0f);
            background.transform.rotation = Quaternion.identity;
            background.transform.localScale = Main.thinMenu
                ? new Vector3(0.1f, 1f, 1f)
                : new Vector3(0.1f, 1.5f, 1f);

            ColorChanger colorChanger = background.AddComponent<ColorChanger>();
            colorChanger.colors = backgroundColor;

            Main.menuBackground = background;
        }

        private static void CreateCanvas()
        {
            canvasObject = new GameObject("Canvas");
            canvasObject.transform.SetParent(root.transform, false);

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            canvas.renderMode = RenderMode.WorldSpace;
            scaler.dynamicPixelsPerUnit = 2500f;

            canvasObject.AddComponent<GraphicRaycaster>();

            Main.canvasObj = canvasObject;
        }

        private static Transform CreateFill(Transform button)
        {
            GameObject fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(fill.GetComponent<Collider>());

            fill.name = "LoadingFill";
            fill.transform.SetParent(button, false);
            fill.transform.localPosition = new Vector3(0f, 0f, -FillDepth);
            fill.transform.localRotation = Quaternion.identity;
            fill.transform.localScale = new Vector3(0f, 1.3f, 0.08f);

            ColorChanger colorChanger = fill.AddComponent<ColorChanger>();
            colorChanger.colors = buttonColors.Length > 1 ? buttonColors[1] : buttonColors[0];

            return fill.transform;
        }

        /// <summary>
        /// The "Loading n%" bar, placed above the first stage using the menu's own label
        /// placement maths so it lines up with the button column.
        /// </summary>
        private static void CreateHeader()
        {
            float extra = Main.ButtonDistance * 1.4f;

            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(bar.GetComponent<Collider>());

            bar.name = "LoadingBar";
            bar.transform.SetParent(root.transform, false);
            bar.transform.localPosition = new Vector3(0.56f, 0f, 0.28f + extra);
            bar.transform.localRotation = Quaternion.identity;
            bar.transform.localScale = new Vector3(0.09f, 0.55f, Main.ButtonDistance * 0.8f);

            ColorChanger barChanger = bar.AddComponent<ColorChanger>();
            barChanger.colors = buttonColors[0];

            GameObject fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(fill.GetComponent<Collider>());

            fill.name = "LoadingBarFill";
            fill.transform.SetParent(bar.transform, false);
            fill.transform.localPosition = new Vector3(-0.09f, 0f, -FillDepth);
            fill.transform.localRotation = Quaternion.identity;
            fill.transform.localScale = new Vector3(0f, 0.55f, 0.08f);

            ColorChanger fillChanger = fill.AddComponent<ColorChanger>();
            fillChanger.colors = buttonColors.Length > 1 ? buttonColors[1] : buttonColors[0];

            headerFill = fill.transform;

            headerText = NewLabel(new Vector3(0.064f, 0f, 0.111f + extra), "Loading 0%");

            TextMeshPro title = NewLabel(new Vector3(0.064f, 0f, 0.111f + extra * 2f), "ii <b>Reborn</b>");
            RectTransform titleRect = title.rectTransform;
            titleRect.sizeDelta = new Vector2(0.28f, 0.05f);
        }

        private static TextMeshPro NewLabel(Vector3 position, string text)
        {
            TextMeshPro label = new GameObject
            {
                transform =
                {
                    parent = canvasObject.transform
                }
            }.AddComponent<TextMeshPro>();

            label.font = activeFont;
            label.richText = true;
            label.fontStyle = activeFontStyle;
            label.alignment = TextAlignmentOptions.Center;
            label.text = text;
            label.fontSize = 1;
            label.enableAutoSizing = true;
            label.fontSizeMin = 0;

            label.AddComponent<UIColorChanger>().colors = textColors[0];

            RectTransform rect = label.rectTransform;
            rect.sizeDelta = new Vector2(0.24f, 0.05f);
            rect.localPosition = position;
            rect.rotation = Quaternion.Euler(new Vector3(180f, 90f, 90f));

            FollowMenuSettings(label);

            return label;
        }

        private static void Render()
        {
            if (fills == null)
                return;

            for (int i = 0; i < fills.Length; i++)
            {
                if (fills[i] == null)
                    continue;

                float fill;

                if (i < stageIndex)
                    fill = 1f;
                else if (i == stageIndex)
                    fill = Mathf.Clamp01(stageProgress);
                else
                    fill = 0f;

                SetFill(fills[i], fill);
            }

            SetHeaderFill(OverallProgress());
        }

        private static void SetFill(Transform fill, float progress)
        {
            if (fill == null)
                return;

            Transform button = fill.parent;
            float full = button != null ? button.localScale.x : 0.09f;
            float height = button != null ? button.localScale.y : 1.3f;
            float grown = full * progress;

            // The cube scales from its centre, so shifting it by half of what it has grown
            // keeps its left edge pinned to the left of the button.
            fill.localScale = new Vector3(grown, height, 0.08f);
            fill.localPosition = new Vector3(-full / 2f + grown / 2f, 0f, -FillDepth);
        }

        private static void SetHeaderFill(float progress)
        {
            if (headerFill == null || headerFill.parent == null)
                return;

            Transform bar = headerFill.parent;
            float full = bar.localScale.x;
            float grown = full * progress;

            headerFill.localScale = new Vector3(grown, bar.localScale.y, 0.08f);
            headerFill.localPosition = new Vector3(-full / 2f + grown / 2f, 0f, -FillDepth);

            if (headerText != null)
                headerText.text = $"Loading {Mathf.RoundToInt(progress * 100f)}%";
        }

        private static float OverallProgress() =>
            (stageIndex + Mathf.Clamp01(stageProgress)) / StageNames.Length;
    }
}
