/*
 * ii Reborn
 * Portions Copyright (C) 2025–2026 Goldentrophy Software
 * Licensed under GNU GPL v3.0-or-later — see LICENSE and NOTICE.
 * This file is part of a derivative work; see NOTICE for attribution
 * and modification history. Do not remove this notice.
 */

using BepInEx;
using GorillaNetworking;
using iiMenu.Classes.Menu;
using iiMenu.Extensions;
using iiMenu.Managers;
using Photon.Pun;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static iiMenu.Menu.Main;
using static iiMenu.Utilities.AssetUtilities;

namespace iiMenu.Menu
{
    public class UI : MonoBehaviour
    {
        // TODO: Convert this class to the assetbundle during TMPro migration
        public static UI Instance;
        public static Texture2D watermarkImage;

        private void Awake()
        {
            Instance = this;

            if (File.Exists(hideGUIPath))
                isOpen = false;

            uiPrefab = LoadObject<GameObject>("UI");

            Transform canvas = uiPrefab.transform.Find("Canvas");
            prefabCanvas = canvas.GetComponent<Canvas>();
            prefabRaycaster = canvas.GetComponent<GraphicRaycaster>();
            LogManager.Log($"UI prefab canvas: raycaster={(prefabRaycaster != null ? "present" : "MISSING")}");
            watermark = canvas.Find("Watermark").GetComponent<Image>();
            versionLabel = canvas.Find("VersionLabel").GetComponent<TextMeshProUGUI>();
            roomStatus = canvas.Find("RoomStatus").GetComponent<TextMeshProUGUI>();
            arraylist = canvas.Find("Arraylist").GetComponent<TextMeshProUGUI>();
            controlBackground = canvas.Find("ControlUI").GetComponent<Image>();

            debugUI = canvas.Find("DebugUI")?.gameObject;
            debugUI.AddComponent<UIDragWindow>();

            templateLine = debugUI.transform.Find("Lines/Line")?.gameObject;

            r = canvas.Find("ControlUI/R").GetComponent<TMP_InputField>();
            g = canvas.Find("ControlUI/G").GetComponent<TMP_InputField>();
            b = canvas.Find("ControlUI/B").GetComponent<TMP_InputField>();
            textInput = canvas.Find("ControlUI/TextInput").GetComponent<TMP_InputField>();

            // The scene EventSystem is XR driven and does not deliver text, so these
            // fields are edited directly rather than through a TMP_InputField input module.
            WatchControlField(r, true);
            WatchControlField(g, true);
            WatchControlField(b, true);
            WatchControlField(textInput, false);

            LogManager.Log(canvas.Find("ControlUI/QueueButton"));
            canvas.Find("ControlUI/QueueButton").GetComponent<Button>().onClick.AddListener(() =>
            {
                Mods.Important.QueueRoom(textInput.text);
            });

            canvas.Find("ControlUI/JoinButton").GetComponent<Button>().onClick.AddListener(() =>
            {
                PhotonNetworkController.Instance.AttemptToJoinSpecificRoom(textInput.text, JoinType.Solo);
            });

            canvas.Find("ControlUI/ColorButton").GetComponent<Button>().onClick.AddListener(() =>
            {
                ChangeColor(new Color32(byte.Parse(r.text), byte.Parse(g.text), byte.Parse(b.text), 255));
            });

            canvas.Find("ControlUI/NameButton").GetComponent<Button>().onClick.AddListener(() =>
            {
                ChangeName(textInput.text);
            });

            TMP_InputField inputField = debugUI.transform.Find("TextInput").gameObject.GetComponent<TMP_InputField>();

            inputField.onSelect.AddListener(_ => focusedOnDebug = true);
            inputField.onDeselect.AddListener(_ => focusedOnDebug = false);

            inputField.onEndEdit.AddListener((string text) =>
            {
                if (focusedOnDebug && !inputField.text.IsNullOrEmpty())
                    HandleDebugCommand(text);

                inputField.text = string.Empty;
            });

            textObjects = new List<TextMeshProUGUI>
            {
                canvas.Find("ControlUI/TextInput/Text Area/Text").GetComponent<TextMeshProUGUI>(),
                canvas.Find("ControlUI/R/Text Area/Text").GetComponent<TextMeshProUGUI>(),
                canvas.Find("ControlUI/G/Text Area/Text").GetComponent<TextMeshProUGUI>(),
                canvas.Find("ControlUI/B/Text Area/Text").GetComponent<TextMeshProUGUI>(),
                canvas.Find("ControlUI/QueueButton/Text").GetComponent<TextMeshProUGUI>(),
                canvas.Find("ControlUI/JoinButton/Text").GetComponent<TextMeshProUGUI>(),
                canvas.Find("ControlUI/ColorButton/Text").GetComponent<TextMeshProUGUI>(),
                canvas.Find("ControlUI/NameButton/Text").GetComponent<TextMeshProUGUI>()
            };

            imageObjects = new List<Image>
            {
                canvas.Find("ControlUI/TextInput").GetComponent<Image>(),
                canvas.Find("ControlUI/R").GetComponent<Image>(),
                canvas.Find("ControlUI/G").GetComponent<Image>(),
                canvas.Find("ControlUI/B").GetComponent<Image>(),
                canvas.Find("ControlUI/QueueButton").GetComponent<Image>(),
                canvas.Find("ControlUI/JoinButton").GetComponent<Image>(),
                canvas.Find("ControlUI/ColorButton").GetComponent<Image>(),
                canvas.Find("ControlUI/NameButton").GetComponent<Image>(),
                debugUI.transform.Find("TextInput").GetComponent<Image>(),
                debugUI.transform.Find("Lines").GetComponent<Image>()
            };

            watermark.material = new Material(watermark.material);
            watermarkImage = LoadTextureFromResource($"{PluginInfo.ClientResourcePath}.icon.png");

            GameObject closeMessage = uiPrefab.transform.Find("Canvas")?.Find("HideMessage")?.gameObject;
            closeMessage?.SetActive(false);
            HideLegacyLtsPanel();

            Update();
        }

        private void HideLegacyLtsPanel()
        {
            if (uiPrefab == null)
                return;

            foreach (TMP_Text text in Resources.FindObjectsOfTypeAll<TMP_Text>())
                HideLegacyTextPanel(text.transform, text.text);

            foreach (UnityEngine.UI.Text text in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Text>())
                HideLegacyTextPanel(text.transform, text.text);

            Transform canvas = uiPrefab.transform.Find("Canvas");
            canvas?.Find("HideMessage")?.gameObject.SetActive(false);
        }

        private static bool legacyPanelWarningLogged;
        private static void HideLegacyTextPanel(Transform textTransform, string value)
        {
            value ??= string.Empty;
            if (value.IndexOf("LTS", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                value.IndexOf("Why LTS", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                value.IndexOf("Welcome to the ii", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                value.IndexOf("Sounds good", System.StringComparison.OrdinalIgnoreCase) < 0)
                return;

            Transform panel = textTransform;
            while (panel.parent != null && panel.parent.name != "Canvas" && !panel.name.Contains("Message"))
                panel = panel.parent;

            if (panel.parent == null)
            {
                if (!legacyPanelWarningLogged)
                {
                    legacyPanelWarningLogged = true;
                    LogManager.Log($"HideLegacyTextPanel: refused to hide the scene root {panel.name} for text \"{value}\"");
                }

                return;
            }

            panel.gameObject.SetActive(false);
        }

        private bool isOpen = true;
        private bool focusedOnDebug;

        private GameObject uiPrefab;
        private GameObject debugUI;

        private Image watermark;
        private TextMeshProUGUI versionLabel;
        private TextMeshProUGUI roomStatus;
        private TextMeshProUGUI arraylist;

        private TMP_InputField r;
        private TMP_InputField g;
        private TMP_InputField b;
        private TMP_InputField textInput;

        /// <summary>Which ControlUI field currently has focus, if any.</summary>
        private static TMP_InputField focusedControlField;

        /// <summary>Prefab canvas, raycast separately from the menu canvas.</summary>
        public static Canvas prefabCanvas;

        /// <summary>The prefab canvas' own raycaster.</summary>
        public static GraphicRaycaster prefabRaycaster;

        /// <summary>Drops the keyboard hook.</summary>
        private static void RemoveTextInputHook()
        {
            if (hookedKeyboard == null)
                return;

            hookedKeyboard.onTextInput -= OnControlFieldChar;
            hookedKeyboard = null;
        }

        /// <summary>Focuses a field, since the XR input module never sends select.</summary>
        public static void FocusControlField(TMP_InputField field)
        {
            if (focusedControlField == field)
                return;

            focusedControlField = field;
            field.ActivateInputField();
            field.caretPosition = field.text != null ? field.text.Length : 0;
        }

        private static void WatchControlField(TMP_InputField field, bool numeric)
        {
            if (numeric)
                numericControlFields.Add(field);

            field.onSelect.AddListener(_ => focusedControlField = field);
            field.onDeselect.AddListener(_ =>
            {
                if (focusedControlField == field)
                    focusedControlField = null;
            });
        }

        private static Keyboard hookedKeyboard;

        private static void HookTextInput()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null || hookedKeyboard == keyboard)
                return;

            if (hookedKeyboard != null)
                hookedKeyboard.onTextInput -= OnControlFieldChar;

            hookedKeyboard = keyboard;
            hookedKeyboard.onTextInput += OnControlFieldChar;
        }

        private static readonly HashSet<TMP_InputField> numericControlFields = new HashSet<TMP_InputField>();

        private static bool IsNumeric(TMP_InputField field) => numericControlFields.Contains(field);

        /// <summary>Selection state, tracked here as TMP_InputField does not expose it.</summary>
        private static int caretIndex;
        private static int selectionAnchor = -1;

        private static bool HasSelection() => selectionAnchor >= 0;

        private static int SelectionStart() => Mathf.Min(caretIndex, selectionAnchor);

        private static int SelectionEnd() => Mathf.Max(caretIndex, selectionAnchor);

        private static void ClearSelection() => selectionAnchor = -1;

        /// <summary>Applies a value, enforcing the numeric field limits.</summary>
        private static void ApplyValue(TMP_InputField field, string value, int caret)
        {
            if (IsNumeric(field))
            {
                if (value.Length > 3)
                    return;

                if (value.Length > 0 && int.TryParse(value, out int number) && number > 255)
                    value = "255";
            }

            field.text = value;

            caretIndex = Mathf.Clamp(caret, 0, value.Length);
            field.caretPosition = caretIndex;
            ClearSelection();
        }

        private static void DeleteSelection(TMP_InputField field)
        {
            if (!HasSelection())
                return;

            string value = field.text ?? "";
            int start = SelectionStart();

            ApplyValue(field, value.Substring(0, start) + value.Substring(SelectionEnd()), start);
        }

        private static void CopySelection(TMP_InputField field)
        {
            if (!HasSelection())
                return;

            string value = field.text ?? "";
            GUIUtility.systemCopyBuffer = value.Substring(SelectionStart(), SelectionEnd() - SelectionStart());
        }

        private static void SelectAll(TMP_InputField field)
        {
            string value = field.text ?? "";
            selectionAnchor = 0;
            caretIndex = value.Length;
            field.caretPosition = caretIndex;
        }

        private static void MoveCaret(TMP_InputField field, int direction, bool extend)
        {
            string value = field.text ?? "";

            if (extend)
            {
                if (!HasSelection())
                    selectionAnchor = Mathf.Clamp(caretIndex, 0, value.Length);
            }
            else
            {
                ClearSelection();
            }

            caretIndex = Mathf.Clamp(caretIndex + direction, 0, value.Length);
            field.caretPosition = caretIndex;
        }

        /// <summary>Inserts a typed character, already shifted by the input system.</summary>
        private static void OnControlFieldChar(char character)
        {
            TMP_InputField field = focusedControlField;

            if (field == null || inTextInput || Instance == null || !Instance.isOpen)
                return;

            string text = character.ToString();

            if (IsNumeric(field))
            {
                if (!char.IsDigit(character))
                    return;

                text = character.ToString();
            }

            string value = field.text ?? "";
            int start = HasSelection() ? SelectionStart() : Mathf.Clamp(caretIndex, 0, value.Length);
            int end = HasSelection() ? SelectionEnd() : start;

            ApplyValue(field, value.Substring(0, start) + text + value.Substring(end), start + text.Length);
        }

        /// <summary>Control shortcuts, deletion and caret movement for the focused field.</summary>
        private static void UpdateControlField()
        {
            TMP_InputField field = focusedControlField;

            if (field == null || inTextInput || Instance == null || !Instance.isOpen)
                return;

            HookTextInput();

            Keyboard keyboard = Keyboard.current;

            if (keyboard == null)
                return;

            bool control = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
            bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

            if (control)
            {
                if (keyboard.aKey.wasPressedThisFrame)
                {
                    SelectAll(field);
                    return;
                }

                if (keyboard.cKey.wasPressedThisFrame)
                {
                    CopySelection(field);
                    return;
                }

                if (keyboard.xKey.wasPressedThisFrame)
                {
                    CopySelection(field);
                    DeleteSelection(field);
                    return;
                }

                if (keyboard.vKey.wasPressedThisFrame)
                {
                    string clipboard = GUIUtility.systemCopyBuffer;

                    if (!string.IsNullOrEmpty(clipboard))
                    {
                        if (IsNumeric(field))
                            clipboard = new string(clipboard.Where(char.IsDigit).ToArray());

                        if (clipboard.Length > 0)
                        {
                            string value = field.text ?? "";
                            int start = HasSelection() ? SelectionStart() : Mathf.Clamp(caretIndex, 0, value.Length);
                            int end = HasSelection() ? SelectionEnd() : start;

                            ApplyValue(field, value.Substring(0, start) + clipboard + value.Substring(end), start + clipboard.Length);
                        }
                    }

                    return;
                }

                // Leave the remaining control combinations to whatever else wants them.
                return;
            }

            if (keyboard.backspaceKey.wasPressedThisFrame)
            {
                if (HasSelection())
                {
                    DeleteSelection(field);
                    return;
                }

                string value = field.text ?? "";
                int caret = Mathf.Clamp(field.caretPosition, 0, value.Length);

                if (caret > 0)
                    ApplyValue(field, value.Remove(caret - 1, 1), caret - 1);

                return;
            }

            if (keyboard.deleteKey.wasPressedThisFrame)
            {
                if (HasSelection())
                {
                    DeleteSelection(field);
                    return;
                }

                string value = field.text ?? "";
                int caret = Mathf.Clamp(field.caretPosition, 0, value.Length);

                if (caret < value.Length)
                    ApplyValue(field, value.Remove(caret, 1), caret);

                return;
            }

            if (keyboard.leftArrowKey.wasPressedThisFrame)
            {
                MoveCaret(field, -1, shift);
                return;
            }

            if (keyboard.rightArrowKey.wasPressedThisFrame)
            {
                MoveCaret(field, 1, shift);
                return;
            }

            if (keyboard.homeKey.wasPressedThisFrame)
            {
                field.MoveTextStart(shift);
                return;
            }

            if (keyboard.endKey.wasPressedThisFrame)
                field.MoveTextEnd(shift);
        }

        private void LateUpdate()
        {
            UpdateControlField();
        }

        /// <summary>
        /// Focuses a ControlUI field on click. Raycasts the prefab canvas directly because
        /// the XR input module handles pointer events but not text.
        /// </summary>
        private static void UpdateControlUiPointer()
        {
            if (prefabRaycaster == null || Instance == null || !Instance.isOpen)
                return;

            try
            {
                if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
                    return;

                var pointer = new PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                {
                    position = Mouse.current.position.ReadValue()
                };

                controlHits.Clear();
                prefabRaycaster.Raycast(pointer, controlHits);

                for (int i = 0; i < controlHits.Count; i++)
                {
                    // The ray lands on a child of the field, such as its text area.
                    TMP_InputField field = controlHits[i].gameObject.GetComponent<TMP_InputField>()
                        ?? controlHits[i].gameObject.GetComponentInParent<TMP_InputField>();

                    if (field != null)
                    {
                        FocusControlField(field);
                        return;
                    }
                }
            }
            catch (System.Exception exception)
            {
                LogManager.LogError($"Control UI pointer handling failed: {exception.Message}");
            }
        }

        private static readonly List<RaycastResult> controlHits = new List<RaycastResult>();

        private Image controlBackground;
        private List<TextMeshProUGUI> textObjects;
        private List<Image> imageObjects = new List<Image>();

        private float uiUpdateDelay;
        private float roomTickTime;

        private void Update()
        {
            UpdateControlUiPointer();

            if (Time.time >= roomTickTime)
            {
                roomTickTime = Time.time + 1f;
                Managers.IiServersManager.TickRoom();
            }

            if (UnityInput.Current.GetKeyDown(KeyCode.Backslash))
                ToggleGUI();

            if (isOpen)
            {
                uiPrefab.SetActive(true);

                if (UnityInput.Current.GetKeyDown(KeyCode.BackQuote))
                    ToggleDebug();

                Color guiColor = Buttons.GetIndex("Swap GUI Colors").enabled
                    ? textColors[1].GetCurrentColor()
                    : backgroundColor.GetCurrentColor();

                versionLabel.color = guiColor;
                roomStatus.color = guiColor;
                arraylist.color = guiColor;
                watermark.color = guiColor;

                versionLabel.SafeSetFont(activeFont);
                roomStatus.SafeSetFont(activeFont);
                arraylist.SafeSetFont(activeFont);

                versionLabel.SafeSetFontStyle(activeFontStyle);
                roomStatus.SafeSetFontStyle(activeFontStyle);
                arraylist.SafeSetFontStyle(activeFontStyle);

                controlBackground.color = backgroundColor.GetCurrentColor();

                foreach (var textObject in textObjects)
                {
                    textObject.color = textColors[1].GetCurrentColor();
                    textObject.SafeSetFont(activeFont);
                    textObject.SafeSetFontStyle(activeFontStyle);
                }

                foreach (var imageObject in imageObjects)
                    imageObject.color = buttonColors[0].GetCurrentColor();

                watermark.transform.rotation = Quaternion.Euler(0f, 0f, rockWatermark ? Mathf.Sin(Time.time * 2f) * 10f : 0f);
                versionLabel.SafeSetText(FollowMenuSettings("Build") + " " + PluginInfo.Version + "\n" +
                                    serverLink.Replace("https://", ""));

                roomStatus.SafeSetText(FollowMenuSettings(!PhotonNetwork.InRoom ? "Not connected to room" : "Connected to room ") +
                   (PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.Name : ""));

                if (debugUI.activeSelf)
                {
                    debugUI.GetComponent<Image>().color = backgroundColor.GetCurrentColor();

                    List<TextMeshProUGUI> debugTextObjects = new List<TextMeshProUGUI>
                    {
                        debugUI.transform.Find("Title").GetComponent<TextMeshProUGUI>(),
                        debugUI.transform.Find("TextInput/Text Area/Text").GetComponent<TextMeshProUGUI>(),
                        debugUI.transform.Find("TextInput/Text Area/Placeholder").GetComponent<TextMeshProUGUI>()
                    };

                    debugTextObjects.AddRange(debugUI.transform.Find("Lines").GetComponentsInChildren<TextMeshProUGUI>());

                    foreach (var textObject in debugTextObjects)
                    {
                        textObject.color = textColors[1].GetCurrentColor();
                        textObject.SafeSetFont(activeFont);
                        textObject.SafeSetFontStyle(activeFontStyle);
                    }

                    debugUI.transform.Find("Title").GetComponent<TextMeshProUGUI>().color = textColors[0].GetCurrentColor();
                }

                if (!(Time.time > uiUpdateDelay)) return;
                Texture2D watermarkTexture = customWatermark ?? watermarkImage;

                if (watermark.sprite == null || watermark.sprite.texture == null || watermark.sprite.texture != watermarkTexture)
                {
                    Sprite sprite = Sprite.Create(
                        watermarkTexture,
                        new Rect(0, 0, watermarkTexture.width, watermarkTexture.height),
                        new Vector2(0.5f, 0.5f),
                        100f
                    );

                    watermark.sprite = sprite;
                }
                   
                if (flipArraylist)
                {
                    controlBackground.rectTransform.anchoredPosition = new Vector2(10f, -10f);
                    controlBackground.rectTransform.anchorMin = new Vector2(0f, 1f);
                    controlBackground.rectTransform.anchorMax = new Vector2(0f, 1f);

                    arraylist.rectTransform.anchoredPosition = new Vector2(-837.5001f, -523f);
                    arraylist.rectTransform.anchorMin = new Vector2(1f, 1f);
                    arraylist.rectTransform.anchorMax = new Vector2(1f, 1f);

                    arraylist.alignment = TextAlignmentOptions.TopRight;
                }
                else
                {
                    controlBackground.rectTransform.anchoredPosition = new Vector2(-250f, -10f);
                    controlBackground.rectTransform.anchorMin = new Vector2(1f, 1f);
                    controlBackground.rectTransform.anchorMax = new Vector2(1f, 1f);

                    arraylist.rectTransform.anchoredPosition = new Vector2(837.5001f, -523f);
                    arraylist.rectTransform.anchorMin = new Vector2(0f, 1f);
                    arraylist.rectTransform.anchorMax = new Vector2(0f, 1f);

                    arraylist.alignment = TextAlignmentOptions.TopLeft;
                }

                uiUpdateDelay = Time.time + (advancedArraylist ? 0.1f : 0.5f);

                List<string> enabledMods = new List<string>();
                int categoryIndex = 0;

                foreach (ButtonInfo[] buttonList in Buttons.buttons)
                {
                    foreach (ButtonInfo button in buttonList)
                    {
                        try
                        {
                            if (!button.enabled || button.hideFromArraylist || (hideSettings && (!hideSettings ||
                                                                     Buttons.categoryNames[categoryIndex]
                                                                         .Contains("Settings")))) continue;
                            string buttonText = button.overlapText ?? button.buttonText;

                            if (inputTextColor != "green")
                                buttonText = buttonText.Replace(" <color=grey>[</color><color=green>", " <color=grey>[</color><color=" + inputTextColor + ">");

                            buttonText = FixTMProTags(buttonText);

                            buttonText = FollowMenuSettings(buttonText);
                            enabledMods.Add(buttonText);
                        }
                        catch { }
                    }
                    categoryIndex++;
                }

                string[] sortedMods = enabledMods
                    .OrderByDescending(s => arraylist.GetPreferredValues(NoRichtextTags(s)).x)
                    .ToArray();

                string modListText = "";
                for (int i = 0; i < sortedMods.Length; i++)
                {
                    if (advancedArraylist)
                        modListText += (flipArraylist ?
                            /* Flipped */ $"<mark=#{ColorToHex(backgroundColor.GetCurrentColor(i * -0.1f))}C0> {sortedMods[i]} </mark><mark=#{ColorToHex(buttonColors[1].GetCurrentColor(i * -0.1f))}> </mark>" :
                            /* Normal  */ $"<mark=#{ColorToHex(buttonColors[1].GetCurrentColor(i * -0.1f))}> </mark><mark=#{ColorToHex(backgroundColor.GetCurrentColor(i * -0.1f))}C0> {sortedMods[i]} </mark>") + "\n";
                    else
                        modListText += sortedMods[i] + "\n";
                }

                arraylist.SafeSetText(modListText);
            } else
                uiPrefab.SetActive(false);
        }

        private readonly string hideGUIPath = $"{PluginInfo.BaseDirectory}/iiMenu_HideGUI.txt";
        private void ToggleGUI()
        {
            isOpen = !isOpen;
            if (isOpen)
            {
                if (File.Exists(hideGUIPath))
                    File.Delete(hideGUIPath);
            }
            else
            {
                if (!File.Exists(hideGUIPath))
                    File.WriteAllText(hideGUIPath, "Text file generated with ii Reborn");
            }

            GameObject closeMessage = uiPrefab.transform.Find("Canvas")?.Find("HideMessage")?.gameObject;
            closeMessage?.SetActive(false);
        }

        private void ToggleDebug()
        {
            if (debugUI.activeSelf)
                debugUI.SetActive(false);
            else
            {
                if (dynamicSounds)
                    LoadSoundFromURL($"{PluginInfo.ServerResourcePath}/Audio/Menu/console.ogg", "Audio/Menu/console.ogg").Play(buttonClickVolume / 10f);

                debugUI.SetActive(true);
            }
        }

        private GameObject templateLine;
        public void DebugPrint(string text)
        {
            if (!debugUI.activeSelf)
                return;

            GameObject line = Instantiate(templateLine, debugUI.transform.Find("Lines"), false);
            line.SetActive(true);
            line.GetComponent<TextMeshProUGUI>().text = text;

            if (debugUI.transform.Find("Lines").childCount > 14)
                Destroy(debugUI.transform.Find("Lines").GetChild(1));
        }

        public void HandleDebugCommand(string command)
        {
            string[] args = command.Split(' ');
            string commandName = args[0].ToLower();
            switch (commandName)
            {
                case "print":
                    {
                        DebugPrint(args.Skip(1).Join(" "));
                        break;
                    }
                case "admin":
                    {
                        DebugPrint("The admin system has been removed from this build.");
                        break;
                    }
                case "beta":
                    {
                        PluginInfo.BetaBuild = args.Length > 1 && args[1].ToLower() == "true";
                        DebugPrint($"PluginInfo.BetaBuild is now {PluginInfo.BetaBuild}");
                        break;
                    }
                case "telemetry":
                    {
                        ServerData.DisableTelemetry = args.Length < 1 || args[1] == "false";
                        DebugPrint($"Telemetry is now {(ServerData.DisableTelemetry ? "disabled" : "enabled")}");
                        break;
                    }
                case "prompt":
                    {
                        MatchCollection matches = Regex.Matches(args.Skip(1).Join(" "), @"\[(.*?)\]");
                        List<string> results = matches.Select(matches => matches.Groups).SelectMany(group => group).Select(group => group.Value).ToList();

                        string promptText = args.Length > 1 ? args[1] : "Prompt text";
                        string acceptText = args.Length > 2 ? args[2] : "Accept";
                        string declineText = args.Length > 3 ? args[3] : "Decline";

                        Prompt(promptText, () => DebugPrint("Prompt accepted"), () => DebugPrint("Prompt declined"), acceptText, declineText);
                        DebugPrint($"Propted user {promptText} {acceptText} {declineText}");

                        break;
                    }
                case "exit":
                case "quit":
                case "close":
                    {
                        Application.Quit();
                        break;
                    }
                default:
                    {
                        DebugPrint($"Unknown command: '{commandName}'");
                        break;
                    }
            }
        }

        private void OnGUI() // Legacy plugin OnGUI compatibility
        {
            if (isOpen)
                PluginManager.ExecuteOnGUI();
        }
    }
}