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
using iiMenu.Extensions;
using iiMenu.Menu;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
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
    /// A fake loading screen that is the real menu, with a Loading category open.
    ///
    /// The stage buttons are genuine menu buttons added through the normal Buttons API, so
    /// the menu itself draws them: same font, same colours, same italics, same everything.
    /// A progress fill is then parented onto each one and grown from left to right, one
    /// stage at a time, plus a "Loading n%" bar across the top.
    ///
    /// Nothing here builds its own text or its own materials, which is the only reason this
    /// version works. Earlier attempts hand-rolled a world space canvas and hand-built
    /// materials, and both rendered as a black rectangle with no text.
    /// </summary>
    public static class LoadingScreenManager
    {
        /// <summary>True while the loading screen is up.</summary>
        public static bool Active { get; private set; }

        /// <summary>Plays the loading screen automatically every time the menu is opened.</summary>
        public static bool PlayOnMenuOpen = true;

        /// <summary>Seconds each stage takes to fill.</summary>
        public static float StageDuration = 0.55f;

        /// <summary>Seconds the finished screen stays up before it tears itself down.</summary>
        public static float LingerDuration = 0.8f;

        /// <summary>The category the fake stage buttons live in.</summary>
        public const string CategoryName = "Loading";

        private const string SkipButtonText = "Skip Loading";

        private static readonly string[] StageNames =
        {
            "Initializing",
            "Loading Assets",
            "Applying Patches",
            "Loading Preferences",
            "Ready"
        };

        /// <summary>Seconds to wait after boot before playing the loading screen once.</summary>
        public static float StartupDelay = 4f;

        /// <summary>Whether the loading screen plays once automatically after boot.</summary>
        public static bool PlayOnceOnStartup = true;

        /// <summary>
        /// Plays the loading screen once after a short delay, so it is reachable without
        /// any input at all.
        /// </summary>
        public static System.Collections.IEnumerator PlayOnStartup()
        {
            if (!PlayOnceOnStartup)
                yield break;

            float until = Time.time + StartupDelay;

            while (Time.time < until)
                yield return null;

            // The menu is built from a patch that runs later, so wait for it rather than
            // assuming it already exists.
            int waited = 0;

            while (Main.menu == null && waited < 600)
            {
                waited++;
                yield return null;
            }

            LogManager.Log("Loading screen: startup autoplay firing.");
            Show();
        }

        /// <summary>
        /// How far toward the viewer a fill sits from the face of its button. The menu puts
        /// its button text on a shallower z-slope than the buttons themselves, so this is
        /// kept deliberately tiny: enough to win the depth test against the button it sits
        /// on, small enough not to creep in front of the label.
        /// </summary>
        private const float FillDepth = 0.004f;

        /// <summary>Metres in front of each camera that the fullscreen cover sits.</summary>
        public static float CoverDistance = 0.55f;

        private static readonly List<Material> materials = new List<Material>();

        private static GameObject runner;
        private static GameObject root;
        private static Transform header;
        private static Transform headerFill;
        private static TextMeshPro headerText;
        private static Transform[] stageButtons;
        private static Transform[] stageFills;
        private static int previousCategory = -1;
        private static int stageIndex;
        private static float stageProgress;
        private static float lingerTimer;
        private static bool completed;
        private static bool categoryAdded;

        /// <summary>Opens the loading screen. Safe to call while one is already up.</summary>
        public static void Show()
        {
            // BepInEx reported "Unable to start Unity log writer" on this install, which
            // means Unity level exceptions are never written to the log at all. Anything
            // thrown in here would fail completely silently, so it is caught and logged
            // with its stack trace instead.
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

            if (Main.menu == null)
                Main.CreateMenu();

            if (Main.menu == null || Main.canvasObj == null)
            {
                LogManager.LogError("Loading screen: the menu or its canvas is missing, cannot build.");
                return;
            }

            previousCategory = Buttons.CurrentCategoryIndex;
            AddLoadingCategory();

            // Hand the menu a category it knows nothing about and it would clamp back to a
            // valid index, so the new category is appended before the index is set.
            Buttons.CurrentCategoryIndex = Buttons.GetCategory(CategoryName);
            Main.ReloadMenu();

            CreateCovers();

            if (!AttachToButtons())
            {
                LogManager.LogError("Loading screen: could not find the stage buttons after the reload.");
                Hide();
                return;
            }

            stageIndex = 0;
            stageProgress = 0f;
            lingerTimer = 0f;
            completed = false;
            Active = true;

            runner = new GameObject("iiMenu_LoadingScreenRunner");
            runner.AddComponent<LoadingScreenTick>();

            LogManager.Log($"Loading screen: showing {StageNames.Length} stages.");

            Render();
        }

        /// <summary>Tears everything down and puts the menu back where it was.</summary>
        public static void Hide()
        {
            Active = false;
            completed = false;
            stageIndex = 0;
            stageProgress = 0f;
            stageButtons = null;
            stageFills = null;
            header = null;
            headerFill = null;
            headerText = null;

            // The menu was re-parented onto a camera to hold it in view, so undo that and
            // let the menu's own positioning take over again.
            if (presentCamera != null && Main.menu != null &&
                ReferenceEquals(Main.menu.transform.parent, presentCamera.transform))
            {
                Main.menu.transform.SetParent(null, true);
            }

            presentCamera = null;

            if (root != null)
            {
                Object.Destroy(root);
                root = null;
            }

            foreach (Material material in materials)
            {
                if (material != null)
                    Object.Destroy(material);
            }

            materials.Clear();

            foreach (GameObject cover in covers)
            {
                if (cover != null)
                    Object.Destroy(cover);
            }

            covers.Clear();

            if (runner != null)
            {
                Object.Destroy(runner);
                runner = null;
            }

            RestoreCategory();
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
                Hide();
            }
        }

        private static void TickInternal()
        {
            ForceMenuOnScreen();

            // Escape skips, read off Unity's InputSystem because the game's own UnityInput
            // wrapper is VR backed and reports nothing when no VR runtime is present.
            if (EscapePressed())
            {
                Hide();
                return;
            }

            if (completed)
            {
                lingerTimer += Time.deltaTime;

                if (lingerTimer >= LingerDuration)
                    Hide();

                return;
            }

            stageProgress += StageDuration <= 0f ? 1f : Time.deltaTime / StageDuration;

            if (stageProgress < 1f)
            {
                Render();
                return;
            }

            stageProgress = 0f;
            stageIndex++;

            if (stageIndex >= StageNames.Length)
                completed = true;

            Render();
        }

        private static readonly List<GameObject> covers = new List<GameObject>();

        /// <summary>
        /// Drops a black plane in front of every camera that is actually rendering, sized
        /// from that camera's own frustum, so the menu is not floating over the game world.
        /// A plain cube on its default material is used rather than a canvas, because
        /// cubes are known to render here where hand built world space UI did not.
        /// </summary>
        private static void CreateCovers()
        {
            foreach (GameObject cover in covers)
            {
                if (cover != null)
                    Object.Destroy(cover);
            }

            covers.Clear();

            foreach (Camera camera in CoverCameras())
            {
                float visibleHeight = 2f * CoverDistance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float visibleWidth = visibleHeight * Mathf.Max(camera.aspect, 0.1f);

                GameObject cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(cover.GetComponent<Collider>());

                cover.name = "LoadingCover";
                cover.transform.SetParent(camera.transform, false);
                cover.transform.localPosition = new Vector3(0f, 0f, CoverDistance);
                cover.transform.localRotation = Quaternion.identity;

                // Ten percent over the frustum so the edges cannot show the game behind.
                cover.transform.localScale = new Vector3(visibleWidth * 1.1f, visibleHeight * 1.1f, 0.02f);

                Material material = cover.GetComponent<Renderer>().material;
                material.color = Color.black;
                materials.Add(material);

                covers.Add(cover);
            }

            LogManager.Log($"Loading screen: fullscreen cover on {covers.Count} camera(s).");
        }

        private static IEnumerable<Camera> CoverCameras()
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
        /// Finds the stage buttons the menu just drew and parents a fill onto each one.
        /// They are located by ButtonCollider.relatedText rather than by index, so this
        /// survives the page arrows, search results and alphabetising getting in the way.
        /// </summary>
        private static bool AttachToButtons()
        {
            // Reached again whenever the menu rebuilds itself, and a rebuild in progress
            // nulls these out before it puts them back. Both have to be checked.
            if (Main.menu == null || Main.canvasObj == null)
                return false;

            // The previous root has to go or the fills pile up one orphaned set per rebuild.
            if (root != null)
                Object.Destroy(root);

            root = new GameObject("iiMenu_LoadingScreen");
            root.transform.SetParent(Main.menu.transform, false);

            stageButtons = new Transform[StageNames.Length];
            stageFills = new Transform[StageNames.Length];

            Dictionary<string, Transform> found = new Dictionary<string, Transform>();

            foreach (Transform child in Main.menu.transform)
            {
                ButtonCollider collider = child.GetComponent<ButtonCollider>();

                if (collider == null || string.IsNullOrEmpty(collider.relatedText))
                    continue;

                found[collider.relatedText] = child;
            }

            for (int i = 0; i < StageNames.Length; i++)
            {
                if (!found.TryGetValue(StageNames[i], out Transform button))
                    return false;

                stageButtons[i] = button;
                stageFills[i] = CreateFill(button);
            }

            BuildHeader();

            return true;
        }

        /// <summary>
        /// The "Loading n%" bar across the top, sitting just above the first stage button.
        /// </summary>
        private static void BuildHeader()
        {
            if (Main.canvasObj == null)
                return;

            if (stageButtons.Length == 0 || stageButtons[0] == null)
                return;

            Transform anchor = stageButtons[0];
            float step = Main.ButtonDistance * 0.8f;

            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(bar.GetComponent<Collider>());

            bar.name = "LoadingHeader";
            bar.transform.SetParent(anchor.parent, false);
            bar.transform.localRotation = Quaternion.identity;
            bar.transform.localScale = new Vector3(0.09f, 1.3f, step);
            bar.transform.localPosition = anchor.localPosition + new Vector3(0f, 0f, step * 1.6f);

            Color tint = Main.backgroundColor.GetCurrentColor();
            Material barMaterial = bar.GetComponent<Renderer>().material;
            barMaterial.color = tint;
            materials.Add(barMaterial);

            header = bar.transform;

            GameObject fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(fill.GetComponent<Collider>());

            fill.name = "LoadingHeaderFill";
            fill.transform.SetParent(bar.transform, false);
            fill.transform.localRotation = Quaternion.identity;
            fill.transform.localScale = new Vector3(0f, 1.3f, 0.08f);
            fill.transform.localPosition = new Vector3(-0.09f, 0f, -FillDepth);

            Color fillTint = Main.buttonColors.Length > 0
                ? Main.buttonColors[0].GetCurrentColor()
                : Color.white;

            Material fillMaterial = fill.GetComponent<Renderer>().material;
            fillMaterial.color = fillTint;
            materials.Add(fillMaterial);

            headerFill = fill.transform;

            headerText = new GameObject
            {
                transform =
                {
                    parent = Main.canvasObj.transform
                }
            }.AddComponent<TextMeshPro>();

            headerText.font = activeFont;
            headerText.fontStyle = activeFontStyle;
            headerText.richText = true;
            headerText.alignment = TextAlignmentOptions.Center;
            headerText.text = "Loading 0%";
            headerText.fontSize = 1;
            headerText.enableAutoSizing = true;
            headerText.fontSizeMin = 0;

            headerText.AddComponent<UIColorChanger>().colors = Main.textColors[0];

            RectTransform rect = headerText.rectTransform;
            rect.sizeDelta = new Vector2(0.24f, 0.05f);
            rect.localPosition = bar.transform.localPosition + new Vector3(0.064f, 0f, -0.111f);
            rect.rotation = Quaternion.Euler(new Vector3(180f, 90f, 90f));

            FollowMenuSettings(headerText);
        }

        private static Transform CreateFill(Transform button)
        {
            GameObject fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(fill.GetComponent<Collider>());

            fill.name = "LoadingFill";
            fill.transform.SetParent(button, false);
            fill.transform.localRotation = Quaternion.identity;
            fill.transform.localPosition = new Vector3(0f, 0f, -FillDepth);
            fill.transform.localScale = new Vector3(0f, 1.3f, 0.08f);

            // A slightly different shade to the button it sits on, so a filled stage reads
            // as the pressed colour. The button's own material drives this one.
            Renderer buttonRenderer = button.GetComponent<Renderer>();
            Color tint = Main.buttonColors.Length > 1
                ? Main.buttonColors[1].GetCurrentColor()
                : buttonRenderer != null ? buttonRenderer.material.color : Color.white;

            Material material = fill.GetComponent<Renderer>().material;
            material.color = tint;
            materials.Add(material);

            return fill.transform;
        }

        private static Camera presentCamera;

        /// <summary>
        /// Holds the menu in front of the camera for as long as the screen is up.
        ///
        /// The menu only ever positions itself for a viewer when it is in its keyboard or PC
        /// presentation, and that path is gated on UnityInput reporting the menu key. With
        /// no VR runtime UnityInput reports nothing, so the menu is never placed in front of
        /// anything and the black cover is all that renders. This is the same transform
        /// Main.ReceneterMenu applies in keyboard mode, copied verbatim, minus the parts
        /// that depend on input or on the third person camera existing.
        /// </summary>
        private static void ForceMenuOnScreen()
        {
            if (Main.menu == null)
                return;

            if (presentCamera == null || !presentCamera.isActiveAndEnabled)
            {
                presentCamera = CoverCameras().FirstOrDefault();

                if (presentCamera == null)
                    return;
            }

            Transform menu = Main.menu.transform;

            if (menu.parent != presentCamera.transform)
                menu.SetParent(presentCamera.transform, true);

            // Just inside the cover, so the cover stays behind the menu and the game stays
            // behind the cover.
            menu.localPosition = new Vector3(0f, 0f, Mathf.Max(0.05f, CoverDistance - 0.05f));
            menu.localRotation = Quaternion.Euler(-90f, 90f, 0f);
        }

        private static void Render()
        {
            if (stageFills == null || stageButtons == null)
                return;

            // The menu destroys and rebuilds itself on a page or category change, and also
            // whenever the boot scene loads, which takes the buttons and their fills with
            // it. When that happens the fills are re-acquired, but a rebuild in progress
            // leaves Main.menu null, so the re-acquire is allowed to simply fail and this
            // frame is skipped rather than dereferencing a menu that is not there.
            if (stageButtons.Any(button => button == null) && !AttachToButtons())
                return;

            for (int i = 0; i < stageFills.Length; i++)
            {
                float fill;

                if (i < stageIndex)
                    fill = 1f;
                else if (i == stageIndex)
                    fill = Mathf.Clamp01(stageProgress);
                else
                    fill = 0f;

                SetFill(stageFills[i], fill);
            }

            SetHeaderFill(OverallProgress());
        }

        private static void SetFill(Transform fill, float progress)
        {
            if (fill == null)
                return;

            // The cube scales from its centre, so nudging it by half of whatever it has
            // grown keeps its left edge pinned where the button starts.
            Transform button = fill.parent;
            float full = button != null ? button.localScale.x : 0.09f;
            float grown = full * progress;

            fill.localScale = new Vector3(grown, 1.3f, 0.08f);
            fill.localPosition = new Vector3(-full / 2f + grown / 2f, 0f, -FillDepth);
        }

        private static void SetHeaderFill(float progress)
        {
            if (headerFill == null || header == null)
                return;

            float full = header.localScale.x;
            float grown = full * progress;

            headerFill.localScale = new Vector3(grown, 1.3f, 0.08f);
            headerFill.localPosition = new Vector3(-full / 2f + grown / 2f, 0f, 0.08f);

            if (headerText != null)
                headerText.text = $"Loading {Mathf.RoundToInt(progress * 100f)}%";
        }

        private static float OverallProgress() =>
            (stageIndex + Mathf.Clamp01(stageProgress)) / StageNames.Length;

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
        /// Appends the Loading category. Done once and kept, because the menu tears its
        /// category data down on unload anyway and rebuilding it every time is pointless.
        /// </summary>
        private static void AddLoadingCategory()
        {
            if (categoryAdded && Buttons.GetCategory(CategoryName) >= 0)
                return;

            List<ButtonInfo> stages = StageNames
                .Select(name => new ButtonInfo
                {
                    buttonText = name,
                    isTogglable = false,
                    method = () => { },
                    toolTip = "Loading."
                })
                .ToList();

            stages.Add(new ButtonInfo
            {
                buttonText = SkipButtonText,
                overlapText = "Skip <color=green>[Esc]</color>",
                isTogglable = false,
                method = Hide,
                toolTip = "Ends the loading screen early."
            });

            List<ButtonInfo[]> categories = Buttons.buttons.ToList();
            categories.Add(stages.ToArray());
            Buttons.buttons = categories.ToArray();

            List<string> names = Buttons.categoryNames.ToList();

            if (!names.Contains(CategoryName))
                names.Add(CategoryName);

            Buttons.categoryNames = names.ToArray();
            categoryAdded = true;
        }

        private static void RestoreCategory()
        {
            if (previousCategory < 0)
                return;

            int target = previousCategory;
            previousCategory = -1;

            if (Main.menu == null)
                return;

            Buttons.CurrentCategoryIndex = target;
            Main.ReloadMenu();
        }
    }
}
