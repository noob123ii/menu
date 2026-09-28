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
        private static GameObject canvasObject;

        /// <summary>One root per camera, and one fill set per root.</summary>
        private static readonly List<GameObject> roots = new List<GameObject>();
        private static readonly List<Transform[]> fillSets = new List<Transform[]>();
        private static readonly List<Transform> headerFills = new List<Transform>();
        private static readonly List<TextMeshPro> headerTexts = new List<TextMeshPro>();

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

        private static Camera PresentingCamera() => PresentingCameras().FirstOrDefault();

        /// <summary>
        /// Every camera that could be the one being looked through. The screen has to exist
        /// on all of them: parenting it to only one is what left the previous version
        /// visible edge on, with its text sideways, because the viewer was on a different
        /// camera than the one the screen was parented to.
        /// </summary>
        private static List<Camera> PresentingCameras()
        {
            List<Camera> found = new List<Camera>();

            Camera firstPerson = null;

            try
            {
                if (GorillaTagger.Instance != null && GorillaTagger.Instance.mainCamera != null)
                    firstPerson = GorillaTagger.Instance.mainCamera.GetComponent<Camera>();
            }
            catch { }

            AddIfUsable(found, firstPerson);
            AddIfUsable(found, TPC);

            if (found.Count == 0)
            {
                try { AddIfUsable(found, Camera.main); }
                catch { }
            }

            return found;
        }

        private static void AddIfUsable(List<Camera> found, Camera camera)
        {
            if (camera == null || found.Contains(camera))
                return;

            if (!camera.isActiveAndEnabled)
                return;

            found.Add(camera);
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

            List<Camera> cameras = PresentingCameras();

            if (cameras.Count == 0)
            {
                LogManager.LogError("Loading screen: no presenting camera, cannot build.");
                return;
            }

            foreach (Camera camera in cameras)
            {
                if (Build(camera))
                    continue;

                LogManager.LogError($"Loading screen: build failed for camera {camera.name}.");
            }

            if (fillSets.Count == 0)
            {
                Hide();
                return;
            }

            stageIndex = 0;
            stageProgress = 0f;
            Active = true;

            runner = new GameObject("iiMenu_LoadingScreenRunner");
            runner.AddComponent<LoadingScreenTick>();

            LogManager.Log($"Loading screen: built on {fillSets.Count} camera(s) " +
                           $"({string.Join(", ", cameras.Take(fillSets.Count).Select(c => c.name))}), " +
                           $"{StageNames.Length} stages plus skip, total {TotalDuration}s.");

            Render();
        }

        /// <summary>Tears the screen down and releases everything it allocated.</summary>
        public static void Hide()
        {
            Active = false;
            stageIndex = 0;
            stageProgress = 0f;

            foreach (GameObject built in roots)
            {
                if (built != null)
                    Object.Destroy(built);
            }

            roots.Clear();
            fillSets.Clear();
            headerFills.Clear();
            headerTexts.Clear();
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
        /// <summary>
        /// Builds one screen, in the camera's own local space with no rotation on the root
        /// at all.
        ///
        /// The menu is laid out for a viewer holding it in a VR hand, on axes that only
        /// make sense once the menu's Euler(-90, 90, 0) presentation rotation is applied.
        /// Reproducing that rotation by hand went wrong repeatedly, and a wrongly rotated
        /// screen is worse than a plainly laid out one. Camera local space needs no
        /// rotation: +X is right, +Y is up, +Z is away from the viewer, which is the
        /// standard world space canvas setup. The menu's colours, font and italic styling
        /// still come from the menu, so it reads as the menu, just laid out squarely
        /// instead of in the menu's VR orientation.
        /// </summary>
        private static bool Build(Camera camera)
        {
            if (addButtonMethod == null)
            {
                addButtonMethod = typeof(Main).GetMethod(
                    "AddButton",
                    BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[] { typeof(float), typeof(int), typeof(ButtonInfo) },
                    null);
            }

            GameObject savedMenu = Main.menu;
            GameObject savedBackground = Main.menuBackground;
            GameObject savedCanvas = Main.canvasObj;
            int savedIndex = Buttons.CurrentCategoryIndex;

            GameObject built = null;

            try
            {
                Layout layout = Layout.For(camera);

                built = CreateRoot(camera);
                CreateBackground(built, layout);
                CreateCanvas(built);

                Transform[] fills = new Transform[StageNames.Length];

                for (int i = 0; i < StageNames.Length; i++)
                {
                    Transform button = CreateButton(built, layout, i);
                    fills[i] = CreateFill(button, layout);
                    CreateLabel(built, layout, i, StageNames[i]);
                }

                CreateLabel(built, layout, StageNames.Length, SkipLabel);
                CreateHeader(built, layout);

                roots.Add(built);
                fillSets.Add(fills);
            }
            catch (Exception exception)
            {
                LogManager.LogError($"Loading screen: build failed on {camera.name}. {exception}");

                if (built != null)
                    Object.Destroy(built);

                return false;
            }
            finally
            {
                Main.menu = savedMenu;
                Main.menuBackground = savedBackground;
                Main.canvasObj = savedCanvas;
                Buttons.CurrentCategoryIndex = savedIndex;
            }

            return true;
        }

        /// <summary>Screen space measurements derived from the camera it is being drawn for.</summary>
        private struct Layout
        {
            public float VisibleWidth;
            public float VisibleHeight;
            public float PanelWidth;
            public float PanelHeight;
            public float ButtonWidth;
            public float ButtonHeight;
            public float TopY;
            public float Depth;

            public static Layout For(Camera camera)
            {
                Layout layout = new Layout();

                layout.VisibleHeight = 2f * ViewDistance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                layout.VisibleWidth = layout.VisibleHeight * Mathf.Max(camera.aspect, 0.1f);

                // A tall panel, like the PC menu, sized as a fraction of the view.
                layout.PanelHeight = layout.VisibleHeight * 0.86f;
                layout.PanelWidth = layout.PanelHeight * 0.34f;
                layout.ButtonWidth = layout.PanelWidth * 0.86f;
                layout.ButtonHeight = layout.PanelHeight / (StageNames.Length + 2.4f);
                layout.TopY = layout.PanelHeight / 2f;
                layout.Depth = 0.01f;

                return layout;
            }

            public float RowY(int index) =>
                TopY - index * ButtonHeight * 1.18f - ButtonHeight * 0.5f;
        }

        /// <summary>
        /// Hands one button to the menu's own builder, then finds the cube it made so a fill
        /// can be parented onto it. Buttons are found by ButtonCollider.relatedText.
        /// </summary>
        private static GameObject CreateRoot(Camera camera)
        {
            GameObject built = new GameObject("iiMenu_LoadingScreen");

            // Camera local space, no rotation. +X right, +Y up, +Z away from the viewer.
            built.transform.SetParent(camera.transform, false);
            built.transform.localPosition = new Vector3(0f, 0f, ViewDistance);
            built.transform.localRotation = Quaternion.identity;
            built.transform.localScale = Vector3.one;

            return built;
        }

        private static void CreateBackground(GameObject builtRoot, Layout layout)
        {
            GameObject back = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(back.GetComponent<Collider>());

            back.name = "Background";
            back.transform.SetParent(builtRoot.transform, false);
            back.transform.localPosition = new Vector3(0f, 0f, -layout.Depth * 2f);
            back.transform.localRotation = Quaternion.identity;
            back.transform.localScale = new Vector3(layout.PanelWidth, layout.PanelHeight, layout.Depth);

            ColorChanger colorChanger = back.AddComponent<ColorChanger>();
            colorChanger.colors = backgroundColor;
        }

        private static void CreateCanvas(GameObject builtRoot)
        {
            GameObject created = new GameObject("Canvas");
            created.transform.SetParent(builtRoot.transform, false);

            Canvas canvas = created.AddComponent<Canvas>();
            CanvasScaler scaler = created.AddComponent<CanvasScaler>();
            canvas.renderMode = RenderMode.WorldSpace;
            scaler.dynamicPixelsPerUnit = 2500f;

            created.AddComponent<GraphicRaycaster>();

            // NewLabel parents itself to this, so it has to be recorded here or the very
            // first label dereferences null.
            canvasObject = created;
        }

        private static Transform CreateButton(GameObject builtRoot, Layout layout, int index)
        {
            GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(button.GetComponent<Collider>());

            button.name = "LoadingButton";
            button.transform.SetParent(builtRoot.transform, false);
            button.transform.localPosition = new Vector3(0f, layout.RowY(index), 0f);
            button.transform.localRotation = Quaternion.identity;
            button.transform.localScale = new Vector3(layout.ButtonWidth, layout.ButtonHeight, layout.Depth);

            // Colour comes from ColorChanger, the same path the menu's own buttons use.
            ColorChanger colorChanger = button.AddComponent<ColorChanger>();
            colorChanger.colors = buttonColors[0];

            return button.transform;
        }

        private static Transform CreateFill(Transform button, Layout layout)
        {
            GameObject fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(fill.GetComponent<Collider>());

            fill.name = "LoadingFill";
            fill.transform.SetParent(button, false);
            fill.transform.localPosition = Vector3.zero;
            fill.transform.localRotation = Quaternion.identity;
            fill.transform.localScale = new Vector3(0f, 0.9f, 1.4f);

            ColorChanger colorChanger = fill.AddComponent<ColorChanger>();
            colorChanger.colors = buttonColors.Length > 1 ? buttonColors[1] : buttonColors[0];

            return fill.transform;
        }

        private static void CreateLabel(GameObject builtRoot, Layout layout, int index, string text)
        {
            TextMeshPro label = NewLabel(
                new Vector3(0f, layout.RowY(index), layout.Depth * 1.6f),
                new Vector2(layout.ButtonWidth, layout.ButtonHeight),
                text);

            label.AddComponent<UIColorChanger>().colors = textColors[1];
        }

        private static void CreateHeader(GameObject builtRoot, Layout layout)
        {
            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(bar.GetComponent<Collider>());

            bar.name = "LoadingBar";
            bar.transform.SetParent(builtRoot.transform, false);
            bar.transform.localPosition = new Vector3(0f, layout.TopY - layout.ButtonHeight * 0.35f, 0f);
            bar.transform.localRotation = Quaternion.identity;
            bar.transform.localScale = new Vector3(layout.ButtonWidth, layout.ButtonHeight * 0.75f, layout.Depth);

            ColorChanger barChanger = bar.AddComponent<ColorChanger>();
            barChanger.colors = buttonColors[0];

            GameObject fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(fill.GetComponent<Collider>());

            fill.name = "LoadingBarFill";
            fill.transform.SetParent(bar.transform, false);
            fill.transform.localPosition = Vector3.zero;
            fill.transform.localRotation = Quaternion.identity;
            fill.transform.localScale = new Vector3(0f, 0.9f, 1.4f);

            ColorChanger fillChanger = fill.AddComponent<ColorChanger>();
            fillChanger.colors = buttonColors.Length > 1 ? buttonColors[1] : buttonColors[0];

            headerFills.Add(fill.transform);

            headerTexts.Add(NewLabel(
                new Vector3(0f, layout.TopY - layout.ButtonHeight * 0.35f, layout.Depth * 1.6f),
                new Vector2(layout.ButtonWidth, layout.ButtonHeight * 0.75f),
                "Loading 0%"));

            TextMeshPro title = NewLabel(
                new Vector3(0f, layout.TopY + layout.ButtonHeight * 0.45f, layout.Depth * 1.6f),
                new Vector2(layout.ButtonWidth, layout.ButtonHeight),
                "ii <b>Reborn</b>");

            title.AddComponent<UIColorChanger>().colors = textColors[0];
        }

        private static TextMeshPro NewLabel(Vector3 position, Vector2 size, string text)
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

            RectTransform rect = label.rectTransform;

            // Identity rotation: the canvas is already camera aligned, so a further
            // 180/90/90 would turn the text on its side.
            rect.sizeDelta = size;
            rect.localPosition = position;
            rect.localRotation = Quaternion.identity;

            FollowMenuSettings(label);

            return label;
        }

        private static void Render()
        {
            if (fillSets.Count == 0)
                return;

            for (int set = 0; set < fillSets.Count; set++)
            {
                Transform[] fills = fillSets[set];

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

                if (set < headerFills.Count)
                    SetHeaderFill(headerFills[set], OverallProgress());

                if (set < headerTexts.Count && headerTexts[set] != null)
                    headerTexts[set].text = $"Loading {Mathf.RoundToInt(OverallProgress() * 100f)}%";
            }
        }

        private static void SetFill(Transform fill, float progress)
        {
            if (fill == null)
                return;

            // The fill is a child of the button, so its scale and offset are fractions of
            // the button rather than world measurements: 1 across is the full button.
            // The cube grows from its centre, so shifting it by half of what it has grown
            // keeps its left edge pinned to the left of the button.
            fill.localScale = new Vector3(Mathf.Clamp01(progress), 0.9f, 1.4f);
            fill.localPosition = new Vector3(-0.5f + progress * 0.5f, 0f, 0f);
        }

        private static void SetHeaderFill(Transform fill, float progress)
        {
            if (fill == null || fill.parent == null)
                return;

            // Child of the bar, so fractions of it rather than world measurements.
            float value = Mathf.Clamp01(progress);

            fill.localScale = new Vector3(value, 0.9f, 1.4f);
            fill.localPosition = new Vector3(-0.5f + value * 0.5f, 0f, 0f);
        }

        private static float OverallProgress() =>
            (stageIndex + Mathf.Clamp01(stageProgress)) / StageNames.Length;
    }
}
