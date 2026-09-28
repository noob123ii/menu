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

        /// <summary>
        /// How far toward the viewer a fill sits from the face of its button. The menu puts
        /// its button text on a shallower z slope than the buttons themselves, so this is
        /// kept deliberately tiny: enough to win the depth test against the button it sits
        /// on, small enough not to creep in front of the label.
        /// </summary>
        private const float FillDepth = 0.004f;

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

        /// <summary>
        /// Finds the stage buttons the menu just drew and parents a fill onto each one.
        /// They are located by ButtonCollider.relatedText rather than by index, so this
        /// survives the page arrows, search results and alphabetising getting in the way.
        /// </summary>
        private static bool AttachToButtons()
        {
            // Reached again whenever the menu rebuilds itself, so the previous root has to
            // go or the fills pile up one orphaned set per page change.
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

        private static void Render()
        {
            if (stageFills == null)
                return;

            // The menu destroys and rebuilds itself on a page or category change, which
            // would take the fills with it, so they are re-acquired if that happens.
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
