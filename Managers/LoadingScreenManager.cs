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
using GorillaTag;
using iiMenu.Classes.Menu;
using iiMenu.Extensions;
using iiMenu.Menu;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static iiMenu.Menu.Main;
using Object = UnityEngine.Object;

namespace iiMenu.Managers
{
    /// <summary>
    /// Drives <see cref="LoadingScreenManager"/>. A top level type rather than a nested one
    /// so Unity is happy to attach it to a GameObject.
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
    /// A loading screen styled after the PC menu (the layout you get by holding Q).
    /// Two copies are built at once, one parented to the first person camera and one to
    /// the third person camera, so it is readable in the headset and on the monitor at
    /// the same time and both stay in step.
    ///
    /// Every entry is a button whose label is the name of the thing being loaded. The
    /// button keeps its "pressed" colour and fills left to right as that entry loads,
    /// and only one entry loads at a time.
    /// </summary>
    public static class LoadingScreenManager
    {
        /// <summary>True while the loading screen exists on screen.</summary>
        public static bool Active { get; private set; }

        /// <summary>Plays the loading screen automatically every time the menu is opened.</summary>
        public static bool PlayOnMenuOpen = true;

        /// <summary>How long a single entry takes to fill, in seconds.</summary>
        public static float EntryDuration = 0.08f;

        /// <summary>How long the completed screen lingers before it tears itself down.</summary>
        public static float LingerDuration = 0.6f;

        /// <summary>Entries per row.</summary>
        public static int Columns = 3;

        /// <summary>Metres in front of the camera the screen is placed.</summary>
        public static float ViewDistance = 0.75f;

        /// <summary>Fraction of the camera's vertical field the grid is allowed to fill.</summary>
        public static float ViewFill = 0.8f;

        private const float CellWidth = 0.2f;
        private const float CellHeight = 0.055f;
        private const float ButtonThickness = 0.008f;
        private const float FillInset = 0.9f;
        private const float ButtonFill = 0.8f;
        private const float DynamicPixelsPerUnit = 2500f;

        private sealed class Screen
        {
            public GameObject Root;
            public GameObject Canvas;
            public Transform[] Buttons;
            public Transform[] Fills;
        }

        private static readonly List<Screen> screens = new List<Screen>();
        private static readonly List<Material> materials = new List<Material>();

        private static GameObject runner;
        private static string[] entries = System.Array.Empty<string>();
        private static int entryIndex;
        private static float entryProgress;
        private static float lingerTimer;
        private static bool completed;
        private static Shader unlitShader;

        /// <summary>Builds the loading screen. Safe to call while one is already up.</summary>
        public static void Show()
        {
            Hide();

            entries = CollectEntries();

            // Logged first and unconditionally. A missing "showing" line below then means
            // the screen was built and then rejected, while no line at all means the
            // request never arrived, which is a completely different problem.
            LogManager.Log($"Loading screen: requested. Main category resolved to index " +
                           $"{MainCategoryIndex()} with {entries.Length} usable entries.");

            if (entries.Length == 0)
            {
                LogManager.LogError("Loading screen: the main category has no usable entries to display.");
                return;
            }

            entryIndex = 0;
            entryProgress = 0f;
            lingerTimer = 0f;
            completed = false;
            Active = true;

            foreach (Camera camera in TargetCameras())
            {
                Screen screen = BuildScreen(camera, entries);
                if (screen != null)
                    screens.Add(screen);
            }

            if (screens.Count == 0)
            {
                LogManager.LogError("Loading screen: no usable camera was found, nothing to show.");
                Hide();
                return;
            }

            runner = new GameObject("iiMenu_LoadingScreenRunner");
            runner.AddComponent<LoadingScreenTick>();

            Render();

            LogManager.Log($"Loading screen: showing {entries.Length} entries on {screens.Count} camera(s).");
        }

        /// <summary>Tears the loading screen down and releases everything it allocated.</summary>
        public static void Hide()
        {
            Active = false;
            completed = false;
            entryIndex = 0;
            entryProgress = 0f;
            entries = System.Array.Empty<string>();

            foreach (Screen screen in screens)
            {
                if (screen.Root != null)
                    Object.Destroy(screen.Root);
            }

            screens.Clear();

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

            if (completed)
            {
                lingerTimer += Time.deltaTime;

                if (lingerTimer >= LingerDuration)
                    Hide();

                return;
            }

            if (EntryDuration <= 0f)
                entryProgress = 1f;
            else
                entryProgress += Time.deltaTime / EntryDuration;

            if (entryProgress < 1f)
            {
                Render();
                return;
            }

            entryProgress = 0f;
            entryIndex++;

            if (entryIndex >= entries.Length)
                completed = true;

            Render();
        }

        /// <summary>
        /// Pushes the shared progress out to both screens. Progress lives in one place so
        /// the two copies can never drift apart.
        /// </summary>
        private static void Render()
        {
            for (int i = 0; i < screens.Count; i++)
            {
                Screen screen = screens[i];

                for (int e = 0; e < entries.Length; e++)
                {
                    float fill;

                    if (e < entryIndex)
                        fill = 1f;
                    else if (e == entryIndex)
                        fill = Mathf.Clamp01(entryProgress);
                    else
                        fill = 0f;

                    SetFill(screen.Fills[e], fill);
                }
            }
        }

        private static void SetFill(Transform fill, float progress)
        {
            if (fill == null)
                return;

            float width = CellWidth * FillInset;
            float height = CellHeight * ButtonFill * FillInset;
            float grown = width * progress;

            // The quad's pivot sits in the middle, so shifting it right by half of whatever
            // it has grown keeps its left edge pinned where the button starts.
            fill.localScale = new Vector3(grown, height, 1f);
            fill.localPosition = new Vector3(-width / 2f + grown / 2f, 0f, 0f);
        }

        /// <summary>
        /// The cameras to build a copy on: the first person one, then the third person one.
        /// A camera is only used if it is actually enabled and has a camera component, and
        /// if that leaves nothing, Camera.main is used as a last resort so there is always
        /// at least one copy on screen. On a flat screen install only one of these will
        /// actually be the view being rendered, which is the same situation the PC menu is
        /// in when Q is held.
        /// </summary>
        private static IEnumerable<Camera> TargetCameras()
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

            LogManager.Log($"Loading screen: cameras considered = {found.Count}" +
                           (found.Count > 0 ? $" ({string.Join(", ", found.Select(c => c.name))})" : " (none usable)"));

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

        private static Screen BuildScreen(Camera camera, string[] labels)
        {
            if (camera == null)
                return null;

            int columns = Mathf.Clamp(Columns, 1, labels.Length);
            int rows = Mathf.CeilToInt(labels.Length / (float)columns);

            float gridWidth = columns * CellWidth;
            float gridHeight = rows * CellHeight;

            GameObject root = new GameObject($"iiMenu_LoadingScreen_{camera.name}");
            Transform rootTransform = root.transform;

            // Parented to the camera and sitting straight ahead of it, which is the same
            // trick the PC menu uses when Q is held. The layer is copied from the camera so
            // it lands inside that camera's culling mask rather than relying on the default.
            root.layer = camera.gameObject.layer;
            rootTransform.SetParent(camera.transform, false);
            rootTransform.localPosition = new Vector3(0f, 0f, ViewDistance);
            rootTransform.localRotation = Quaternion.identity;

            // Fit the grid inside the camera's view on BOTH axes, otherwise a wide grid
            // spills off the sides. One uniform scale carries it; children sit at z = 0 so
            // the distance is unaffected.
            float visibleHeight = 2f * ViewDistance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float visibleWidth = visibleHeight * Mathf.Max(camera.aspect, 0.1f);

            float scale = Mathf.Min(
                gridHeight > 0f ? visibleHeight * ViewFill / gridHeight : 1f,
                gridWidth > 0f ? visibleWidth * ViewFill / gridWidth : 1f);

            rootTransform.localScale = Vector3.one * scale;

            CreateBackdrop(rootTransform, gridWidth, gridHeight);

            GameObject canvasObject = new GameObject("Canvas");
            canvasObject.transform.SetParent(rootTransform, false);
            canvasObject.layer = root.layer;

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = DynamicPixelsPerUnit;
            canvasObject.AddComponent<GraphicRaycaster>();

            // A world space canvas gets a default rect that is nowhere near the grid, so
            // the labels end up outside their own canvas. Stretch it over the grid.
            RectTransform canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.sizeDelta = new Vector2(gridWidth, gridHeight);
            canvasRect.localPosition = Vector3.zero;
            canvasRect.localRotation = Quaternion.identity;

            Screen screen = new Screen
            {
                Root = root,
                Canvas = canvasObject,
                Buttons = new Transform[labels.Length],
                Fills = new Transform[labels.Length]
            };

            Material idleMaterial = CreateMaterial(Main.buttonColors.Length > 0
                ? Main.buttonColors[0].GetCurrentColor()
                : Color.gray);

            Material fillMaterial = CreateMaterial(Main.buttonColors.Length > 1
                ? Main.buttonColors[1].GetCurrentColor()
                : Color.black);

            Color textColor = Main.textColors.Length > 1
                ? Main.textColors[1].GetColor(0)
                : Color.white;

            // Label sizing is left entirely to TextMeshPro's auto-sizing, exactly the way
            // the menu's own button text does it: start at fontSize 1 with a minimum of 0
            // and let it grow to fill the box. Working the size out by hand from the root
            // scale and dynamicPixelsPerUnit capped it so low the text collapsed to
            // nothing, which is why the first version rendered as empty boxes.
            float buttonHeight = CellHeight * ButtonFill * FillInset;

            float firstX = -gridWidth / 2f + CellWidth / 2f;
            float firstY = gridHeight / 2f - CellHeight / 2f;

            for (int i = 0; i < labels.Length; i++)
            {
                int column = i % columns;
                int row = i / columns;

                Vector3 position = new Vector3(
                    firstX + column * CellWidth,
                    firstY - row * CellHeight,
                    0f);

                screen.Buttons[i] = CreateButton(rootTransform, position, idleMaterial);
                screen.Fills[i] = CreateFill(rootTransform, position, fillMaterial);
                CreateLabel(canvasObject.transform, position, labels[i], textColor, buttonHeight);
            }

            // CreatePrimitive and new GameObject both land on the default layer, so the
            // whole tree is stamped onto the camera's layer once at the end rather than
            // remembering to do it at every construction site.
            SetLayerRecursive(rootTransform, root.layer);

            return screen;
        }

        private static void SetLayerRecursive(Transform target, int layer)
        {
            target.gameObject.layer = layer;

            for (int i = 0; i < target.childCount; i++)
                SetLayerRecursive(target.GetChild(i), layer);
        }

        private static void CreateBackdrop(Transform parent, float gridWidth, float gridHeight)
        {
            GameObject backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(backdrop.GetComponent<Collider>());

            backdrop.name = "Backdrop";
            backdrop.transform.SetParent(parent, false);
            backdrop.transform.localPosition = new Vector3(0f, 0f, -ButtonThickness);
            backdrop.transform.localScale = new Vector3(gridWidth * 1.12f, gridHeight * 1.12f, 1f);

            Color color = Color.black;
            color.a = 0.6f;

            backdrop.GetComponent<Renderer>().sharedMaterial = CreateMaterial(color);
        }
        private static Transform CreateButton(Transform parent, Vector3 position, Material material)
        {
            GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(button.GetComponent<Collider>());

            button.name = "Button";
            button.transform.SetParent(parent, false);
            button.transform.localPosition = position;
            button.transform.localRotation = Quaternion.identity;
            button.transform.localScale = new Vector3(CellWidth * FillInset, CellHeight * ButtonFill * FillInset, ButtonThickness);

            ApplyTint(button.GetComponent<Renderer>(), material);

            return button.transform;
        }

        private static Transform CreateFill(Transform parent, Vector3 position, Material material)
        {
            GameObject fill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(fill.GetComponent<Collider>());

            fill.name = "Fill";
            fill.transform.SetParent(parent, false);

            // A hair in front of the button face so it is not fighting it for depth.
            fill.transform.localPosition = position + new Vector3(0f, 0f, ButtonThickness * 0.6f);
            fill.transform.localRotation = Quaternion.identity;
            fill.transform.localScale = new Vector3(0f, CellHeight * ButtonFill * FillInset, 1f);

            ApplyTint(fill.GetComponent<Renderer>(), material);

            return fill.transform;
        }

        /// <summary>
        /// Tints a renderer with the shared material, and adopts it as shared so Unity does
        /// not hand back a per-renderer copy every time the property is read. Assigning a
        /// freshly built material to sharedMaterial is what the first pass did wrong: the
        /// tint never showed up on screen at all.
        /// </summary>
        private static void ApplyTint(Renderer target, Material material)
        {
            if (target == null || material == null)
                return;

            target.sharedMaterial = material;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", material.color);

            target.SetPropertyBlock(null);
        }

        private static void CreateLabel(Transform canvas, Vector3 position, string text, Color color, float buttonHeight)
        {
            TextMeshPro label = new GameObject
            {
                transform =
                {
                    parent = canvas
                }
            }.AddComponent<TextMeshPro>();

            label.font = activeFont;
            label.fontStyle = activeFontStyle;
            label.richText = true;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.overflowMode = TextOverflowModes.Overflow;

            // Box first, because auto-sizing measures against the rect as soon as text
            // lands in it.
            RectTransform rect = label.rectTransform;
            rect.sizeDelta = new Vector2(CellWidth * FillInset, buttonHeight);
            rect.localPosition = position + new Vector3(0f, 0f, ButtonThickness * 1.2f);
            rect.localRotation = Quaternion.identity;

            // This mirrors how the menu builds its own button text: start at 1 with a
            // minimum of 0 and let auto-sizing grow it to whatever fits. No fontSizeMax,
            // because pinning the cap is what made the text vanish.
            label.enableAutoSizing = true;
            label.fontSizeMin = 0;
            label.fontSize = 1;

            label.SafeSetText(text);

            // Auto-sizing resolves during the next layout pass, so the atlas and the mesh
            // have to be built now or the label sits invisible until something dirties it.
            label.ForceMeshUpdate();
        }

        private static Material CreateMaterial(Color color)
        {
            Material material = new Material(ResolveShader())
            {
                color = color
            };

            materials.Add(material);

            return material;
        }

        private static Shader ResolveShader()
        {
            if (unlitShader != null)
                return unlitShader;

            // URP Unlit first, because that is what the menu already uses for unlit coloured
            // surfaces and it definitely reads _BaseColor. "GUI/Text Shader" is only a UI
            // shader and is not guaranteed to tint a 3D mesh.
            foreach (string name in new[]
            {
                "Universal Render Pipeline/Unlit",
                "Sprites/Default",
                "GUI/Text Shader",
                "Standard"
            })
            {
                Shader found = Shader.Find(name);

                if (found != null)
                {
                    unlitShader = found;
                    return unlitShader;
                }
            }

            unlitShader = Shader.Find("Standard");
            return unlitShader;
        }

        /// <summary>
        /// The labels shown, taken from the main category of the menu itself so the screen
        /// always lists whatever the menu actually contains. Labels (section headings) and
        /// this button are skipped.
        /// </summary>
        private static string[] CollectEntries()
        {
            List<string> collected = new List<string>();
            HashSet<string> seen = new HashSet<string>();

            ButtonInfo[] category = MainCategory();

            if (category == null)
                return collected.ToArray();

            foreach (ButtonInfo button in category)
            {
                if (button == null || button.label)
                    continue;

                string label = NoRichtextTags(button.overlapText ?? button.buttonText).Trim();

                if (string.IsNullOrEmpty(label))
                    continue;

                if (label.IndexOf("Loading Screen", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                if (!seen.Add(label))
                    continue;

                collected.Add(label);
            }

            return collected.ToArray();
        }

        private static ButtonInfo[] MainCategory()
        {
            if (Buttons.buttons == null || Buttons.buttons.Length == 0)
                return null;

            int index = MainCategoryIndex();

            if (index < 0 || index >= Buttons.buttons.Length)
                index = 0;

            return Buttons.buttons[index];
        }

        private static int MainCategoryIndex() =>
            Buttons.GetCategory("Main");
    }
}
