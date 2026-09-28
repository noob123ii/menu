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
    /// A loading screen that looks like the PC menu but shares nothing with it.
    ///
    /// It builds its own root, background, canvas and buttons, parented to the presenting
    /// camera rather than to the real menu. Nothing here reads or writes Main.menu, no
    /// category is hijacked, and the real menu is left completely alone, so this cannot
    /// interfere with it no matter what state the menu is in.
    ///
    /// The layout is copied from Main.CreateMenu: same root scale of (0.1, 0.3, 0.3825),
    /// same background cube at x 0.50 scaled (0.1, 1.5, 1), same button position of
    /// (0.56, 0, 0.28 - offset) and scale of (0.09, 1.3, ButtonDistance * 0.8), and the
    /// same label recipe with its 180/90/90 rotation. That is what makes it read as the
    /// menu rather than as a black box, and it is why the earlier version that built all
    /// of this against a camera with hand-derived values rendered nothing.
    /// </summary>
    public static class LoadingScreenManager
    {
        /// <summary>True while the loading screen is up.</summary>
        public static bool Active { get; private set; }

        /// <summary>Plays the loading screen once automatically after boot.</summary>
        public static bool PlayOnceOnStartup = true;

        /// <summary>Seconds to wait after boot before it appears.</summary>
        public static float StartupDelay = 0.6f;

        /// <summary>Total seconds the whole sequence takes, spread evenly over the stages.</summary>
        public static float TotalDuration = 4f;

        /// <summary>Metres in front of the camera the screen sits.</summary>
        public static float ViewDistance = 0.5f;

        /// <summary>
        /// How far toward the viewer a fill sits from the face of its button. Kept tiny so
        /// it wins the depth test against its own button without creeping in front of the
        /// label, which the menu places on a shallower slope than the buttons.
        /// </summary>
        private const float FillDepth = 0.004f;

        private const float RootX = 0.1f;
        private const float RootY = 0.3f;
        private const float RootZ = 0.3825f;

        private static readonly string[] StageNames =
        {
            "Initializing",
            "Loading Assets",
            "Applying Patches",
            "Loading Preferences",
            "Finalizing",
            "Skip <color=green>[Esc]</color>"
        };

        private const int StageCount = 6;
        private const int FillableStages = 5; // everything except Skip

        private static readonly List<Material> materials = new List<Material>();

        private static GameObject runner;
        private static GameObject root;
        private static Canvas canvas;
        private static Transform headerFill;
        private static TextMeshPro headerText;
        private static TextMeshPro titleText;
        private static Transform[] fills;
        private static Camera presentCamera;

        private static int stageIndex;
        private static float stageProgress;

        /// <summary>Plays the screen once after boot, with no input required.</summary>
        public static IEnumerator PlayOnStartup()
        {
            if (!PlayOnceOnStartup)
                yield break;

            float until = Time.time + StartupDelay;

            while (Time.time < until)
                yield return null;

            // The camera only exists once the player has spawned, so wait for it rather
            // than building into nothing.
            int waited = 0;

            while (!ReadyToBuild() && waited < 900)
            {
                waited++;
                yield return null;
            }

            LogManager.Log("Loading screen: startup autoplay firing.");
            Show();
        }

        private static bool ReadyToBuild() => PresentingCamera() != null;

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
        /// Builds and shows the screen. BepInEx cannot write Unity's log on this install
        /// ("Unable to start Unity log writer"), so Unity level exceptions are invisible.
        /// Everything is caught and logged here for that reason.
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

            Build();

            stageIndex = 0;
            stageProgress = 0f;
            Active = true;

            runner = new GameObject("iiMenu_LoadingScreenRunner");
            runner.AddComponent<LoadingScreenTick>();

            LogManager.Log($"Loading screen: built on {presentCamera.name} with {StageCount} stages.");

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
            canvas = null;
            headerFill = null;
            headerText = null;
            titleText = null;

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

            if (stageIndex >= FillableStages)
            {
                // Deliberately stays up. It does not time out on its own.
                return;
            }

            float perStage = TotalDuration <= 0f ? 0f : TotalDuration / FillableStages;
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

        private static void Build()
        {
            root = new GameObject("iiMenu_LoadingScreen");

            // Parented to the camera, rotated exactly as the menu is in its PC presentation.
            // The labels carry the menu's own 180/90/90 rotation, so reproducing this
            // outer rotation is what makes them come out the right way up.
            root.transform.SetParent(presentCamera.transform, false);
            root.transform.localPosition = new Vector3(0f, 0f, ViewDistance);
            root.transform.localRotation = Quaternion.Euler(-90f, 90f, 0f);
            root.transform.localScale = new Vector3(RootX, RootY, RootZ) * menuScale;

            CreateBackground();
            CreateCanvas();

            fills = new Transform[StageCount];

            for (int i = 0; i < StageCount; i++)
            {
                float offset = i * Main.ButtonDistance;

                Vector3 position = new Vector3(0.56f, 0f, 0.28f - offset);
                Transform button = CreateButton(position);

                fills[i] = i < FillableStages ? CreateFill(button) : null;
                CreateLabel(offset, i);
            }

            CreateTitle(offset: FillableStages * Main.ButtonDistance);
        }

        private static void CreateBackground()
        {
            GameObject background = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(background.GetComponent<Collider>());

            background.name = "Background";
            background.transform.SetParent(root.transform, false);
            background.transform.localPosition = new Vector3(0.50f, 0f, 0f);
            background.transform.localRotation = Quaternion.identity;
            background.transform.localScale = new Vector3(0.1f, 1.5f, 1f);

            Material material = background.GetComponent<Renderer>().material;
            material.color = backgroundColor.GetCurrentColor();
            materials.Add(material);
        }

        private static void CreateCanvas()
        {
            GameObject canvasObject = new GameObject("Canvas");
            canvasObject.transform.SetParent(root.transform, false);

            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            canvasObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2500f;
            canvasObject.AddComponent<GraphicRaycaster>();
        }

        private static Transform CreateButton(Vector3 position)
        {
            GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(button.GetComponent<Collider>());

            button.name = "LoadingButton";
            button.transform.SetParent(root.transform, false);
            button.transform.localPosition = position;
            button.transform.localRotation = Quaternion.identity;
            button.transform.localScale = new Vector3(0.09f, 1.3f, Main.ButtonDistance * 0.8f);

            // The primitive's own material, coloured through Renderer.material.color. A custom
            // material is what rendered every button black in an earlier pass.
            Material material = button.GetComponent<Renderer>().material;
            material.color = buttonColors[0].GetCurrentColor();
            materials.Add(material);

            return button.transform;
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

            Material material = fill.GetComponent<Renderer>().material;
            material.color = buttonColors.Length > 1 ? buttonColors[1].GetCurrentColor() : Color.black;
            materials.Add(material);

            return fill.transform;
        }

        private static void CreateLabel(float offset, int index)
        {
            TextMeshPro label = new GameObject
            {
                transform =
                {
                    parent = canvas.transform
                }
            }.AddComponent<TextMeshPro>();

            label.font = activeFont;
            label.fontStyle = activeFontStyle;
            label.richText = true;
            label.alignment = TextAlignmentOptions.Center;
            label.text = StageNames[index];

            // The menu's own button text recipe: auto-sizing from 1 with a minimum of 0, no
            // fontSizeMax, and the same rotation.
            label.fontSize = 1;
            label.enableAutoSizing = true;
            label.fontSizeMin = 0;

            label.AddComponent<UIColorChanger>().colors = textColors[1];

            RectTransform rect = label.rectTransform;
            rect.sizeDelta = new Vector2(0.2f, 0.03f * (Main.ButtonDistance / 0.1f));
            rect.localPosition = new Vector3(0.064f, 0f, 0.111f - offset / 2.6f);
            rect.rotation = Quaternion.Euler(new Vector3(180f, 90f, 90f));

            FollowMenuSettings(label);
        }

        /// <summary>The "Loading n%" bar and the title, sitting above the first stage.</summary>
        private static void CreateTitle(float offset)
        {
            float z = 0.28f + offset + Main.ButtonDistance * 1.4f;

            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(bar.GetComponent<Collider>());

            bar.name = "LoadingBar";
            bar.transform.SetParent(root.transform, false);
            bar.transform.localPosition = new Vector3(0.56f, 0f, z);
            bar.transform.localRotation = Quaternion.identity;
            bar.transform.localScale = new Vector3(0.09f, 0.55f, Main.ButtonDistance * 0.8f);

            Material barMaterial = bar.GetComponent<Renderer>().material;
            barMaterial.color = buttonColors[0].GetCurrentColor();
            materials.Add(barMaterial);

            GameObject fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(fill.GetComponent<Collider>());

            fill.name = "LoadingBarFill";
            fill.transform.SetParent(bar.transform, false);
            fill.transform.localPosition = new Vector3(-0.09f, 0f, -FillDepth);
            fill.transform.localRotation = Quaternion.identity;
            fill.transform.localScale = new Vector3(0f, 0.55f, 0.08f);

            Material fillMaterial = fill.GetComponent<Renderer>().material;
            fillMaterial.color = buttonColors.Length > 1 ? buttonColors[1].GetCurrentColor() : Color.white;
            materials.Add(fillMaterial);

            headerFill = fill.transform;

            headerText = NewLabel(canvas.transform, new Vector3(0.064f, 0f, 0.111f + offset + Main.ButtonDistance * 1.4f), "Loading 0%");
            titleText = NewLabel(canvas.transform, new Vector3(0.064f, 0f, 0.111f + offset + Main.ButtonDistance * 2.8f), $"ii <b>Reborn</b>");
        }

        private static TextMeshPro NewLabel(Transform parent, Vector3 position, string text)
        {
            TextMeshPro label = new GameObject
            {
                transform =
                {
                    parent = parent
                }
            }.AddComponent<TextMeshPro>();

            label.font = activeFont;
            label.fontStyle = activeFontStyle;
            label.richText = true;
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
                float fill;

                if (fills[i] == null)
                    continue;

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
            float grown = full * progress;

            // The cube scales from its centre, so shifting it by half of what it has grown
            // keeps its left edge pinned to the left of the button.
            fill.localScale = new Vector3(grown, button != null ? button.localScale.y : 1.3f, 0.08f);
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
            (stageIndex + Mathf.Clamp01(stageProgress)) / FillableStages;
    }
}
