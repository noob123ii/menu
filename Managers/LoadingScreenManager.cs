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
    /// A loading screen built inside the menu's own transform space, so it looks and
    /// behaves exactly like the menu does: it is a child of the menu and its labels live
    /// on the menu's canvas. That means it shows up in every view the menu shows up in,
    /// which is how you get the first person and third person copies without building two
    /// separate things.
    ///
    /// Every button is a plain cube on its default material with the colour assigned
    /// through Renderer.material.color, and every label is a TextMeshPro built with the
    /// same recipe the menu uses for its own button text. Both of those are copied
    /// deliberately: hand-rolled custom materials and hand-derived world space font sizes
    /// both produced a screen that rendered as empty black boxes.
    ///
    /// The button names are the things being loaded, and each one keeps the pressed
    /// colour while it fills left to right, one at a time.
    /// </summary>
    public static class LoadingScreenManager
    {
        /// <summary>True while the loading screen is on screen.</summary>
        public static bool Active { get; private set; }

        /// <summary>Plays the loading screen automatically every time the menu is opened.</summary>
        public static bool PlayOnMenuOpen = true;

        /// <summary>How long a single entry takes to fill, in seconds.</summary>
        public static float EntryDuration = 0.08f;

        /// <summary>How long the finished screen lingers before it tears itself down.</summary>
        public static float LingerDuration = 0.6f;

        /// <summary>Entries per row.</summary>
        public static int Columns = 3;

        /// <summary>Button height, matching the menu's own buttons.</summary>
        private const float ButtonHeight = 1.3f;

        private const float StartX = 0.56f;
        private const float StartY = 0.28f;
        private const float ColumnStep = 0.15f;

        private static readonly List<Material> materials = new List<Material>();

        private static GameObject runner;
        private static GameObject root;
        private static Transform[] buttons;
        private static Transform[] fills;
        private static string[] entries = Array.Empty<string>();
        private static int entryIndex;
        private static float entryProgress;
        private static float lingerTimer;
        private static bool completed;

        /// <summary>Builds the loading screen. Safe to call while one is already up.</summary>
        public static void Show()
        {
            Hide();

            entries = CollectEntries();

            // Logged before anything else happens. A missing "showing" line below then means
            // the screen was built and then rejected, while no line at all means the request
            // never arrived, which is a completely different problem.
            LogManager.Log($"Loading screen: requested, main category index {MainCategoryIndex()}, " +
                           $"{entries.Length} usable entries.");

            if (entries.Length == 0)
            {
                LogManager.LogError("Loading screen: the main category has no usable entries to display.");
                return;
            }

            if (Main.menu == null)
                Main.CreateMenu();

            if (Main.menu == null || Main.canvasObj == null)
            {
                LogManager.LogError("Loading screen: the menu or its canvas is missing, cannot build.");
                return;
            }

            Build(entries);

            if (buttons == null)
            {
                LogManager.LogError("Loading screen: build produced nothing, cannot show.");
                Hide();
                return;
            }

            entryIndex = 0;
            entryProgress = 0f;
            lingerTimer = 0f;
            completed = false;
            Active = true;

            runner = new GameObject("iiMenu_LoadingScreenRunner");
            runner.AddComponent<LoadingScreenTick>();

            Render();

            LogManager.Log($"Loading screen: showing {entries.Length} entries.");
        }

        /// <summary>Tears the loading screen down and releases everything it allocated.</summary>
        public static void Hide()
        {
            Active = false;
            completed = false;
            entryIndex = 0;
            entryProgress = 0f;
            entries = Array.Empty<string>();
            buttons = null;
            fills = null;

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

            entryProgress += EntryDuration <= 0f ? 1f : Time.deltaTime / EntryDuration;

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

        private static void Build(string[] labels)
        {
            int columns = Mathf.Clamp(Columns, 1, labels.Length);

            root = new GameObject("iiMenu_LoadingScreen");

            // A child of the menu, at the menu's own scale and orientation, so everything
            // below inherits the transform chain the real menu already renders correctly
            // through.
            root.transform.SetParent(Main.menu.transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            buttons = new Transform[labels.Length];
            fills = new Transform[labels.Length];

            float rowStep = Main.ButtonDistance * 0.8f;

            for (int i = 0; i < labels.Length; i++)
            {
                int column = i % columns;
                int row = i / columns;

                float x = StartX + column * ColumnStep;
                float y = StartY - row * rowStep;

                var position = new Vector3(x, y, 0.28f);

                buttons[i] = CreateButton(position);
                fills[i] = CreateFill(position);
                CreateLabel(x, y, labels[i]);
            }
        }

        /// <summary>
        /// Pushes progress out to every fill. One shared progress value means the copies in
        /// each view can never drift apart.
        /// </summary>
        private static void Render()
        {
            if (fills == null)
                return;

            for (int i = 0; i < fills.Length; i++)
            {
                float fill;

                if (i < entryIndex)
                    fill = 1f;
                else if (i == entryIndex)
                    fill = Mathf.Clamp01(entryProgress);
                else
                    fill = 0f;

                SetFill(fills[i], fill);
            }
        }

        private static void SetFill(Transform fill, float progress)
        {
            if (fill == null)
                return;

            // The cube scales from its centre, so shifting it by half of whatever it has
            // grown keeps its left edge pinned to the left of the button.
            float full = 0.09f;
            float grown = full * progress;

            fill.localScale = new Vector3(grown, ButtonHeight, Main.ButtonDistance * 0.8f);
            fill.localPosition = new Vector3(StartX - full / 2f + grown / 2f, fill.localPosition.y, 0.28f);
        }

        private static Transform CreateButton(Vector3 position)
        {
            GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(button.GetComponent<Collider>());

            button.name = "LoadingButton";
            button.transform.SetParent(root.transform, false);
            button.transform.localPosition = position;
            button.transform.localRotation = Quaternion.identity;
            button.transform.localScale = new Vector3(0.09f, ButtonHeight, Main.ButtonDistance * 0.8f);

            // Deliberately the primitive's own material. Assigning a custom material here
            // was what rendered every button black: the colour was being written to
            // Material.color, which targets _Color, and the shaders that were being handed
            // over do not read _Color.
            Color tint = Main.buttonColors.Length > 0 ? Main.buttonColors[0].GetCurrentColor() : Color.gray;

            Material material = button.GetComponent<Renderer>().material;
            material.color = tint;
            materials.Add(material);

            return button.transform;
        }

        private static Transform CreateFill(Vector3 position)
        {
            GameObject fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(fill.GetComponent<Collider>());

            fill.name = "LoadingFill";
            fill.transform.SetParent(root.transform, false);
            fill.transform.localPosition = position;
            fill.transform.localRotation = Quaternion.identity;
            fill.transform.localScale = new Vector3(0f, ButtonHeight, Main.ButtonDistance * 0.8f);

            Color tint = Main.buttonColors.Length > 1 ? Main.buttonColors[1].GetCurrentColor() : Color.black;

            Material material = fill.GetComponent<Renderer>().material;
            material.color = tint;
            materials.Add(material);

            return fill.transform;
        }

        private static void CreateLabel(float x, float y, string text)
        {
            TextMeshPro label = new GameObject
            {
                transform =
                {
                    parent = Main.canvasObj.transform
                }
            }.AddComponent<TextMeshPro>();

            label.font = activeFont;
            label.fontStyle = activeFontStyle;
            label.richText = true;
            label.alignment = TextAlignmentOptions.Center;
            label.text = text;

            // The menu's own button text recipe, copied rather than re-derived: auto sizing
            // from a starting size of 1 with a minimum of 0, the same rect size, and the
            // same 180/90/90 rotation so the label faces the same way the real one does.
            label.fontSize = 1;
            label.enableAutoSizing = true;
            label.fontSizeMin = 0;

            label.AddComponent<UIColorChanger>().colors = Main.textColors.Length > 1
                ? Main.textColors[1]
                : Main.textColors[0];

            RectTransform rect = label.rectTransform;
            rect.sizeDelta = new Vector2(0.2f, 0.03f * (Main.ButtonDistance / 0.1f));
            rect.localPosition = new Vector3(x + 0.064f, y, 0.111f);
            rect.rotation = Quaternion.Euler(new Vector3(180f, 90f, 90f));

            FollowMenuSettings(label);
        }

        /// <summary>
        /// The labels shown, taken from the main category of the menu itself so the screen
        /// always lists whatever the menu actually contains. Section labels and this
        /// button are skipped.
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

                if (label.IndexOf("Loading Screen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    label.IndexOf("Auto Loading Screen", StringComparison.OrdinalIgnoreCase) >= 0)
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
