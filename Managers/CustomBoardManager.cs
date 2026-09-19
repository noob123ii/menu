/*
 * ii's Stupid Menu  Managers/CustomBoardManager.cs
 * A mod menu for Gorilla Tag with over 1000+ mods
 *
 * Copyright (C) 2026  Goldentrophy Software
 * https://github.com/iireborn/iis.Stupid.Menu
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using GorillaNetworking;
using iiMenu.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using static iiMenu.Menu.Main;

namespace iiMenu.Managers
{
    public class CustomBoardManager : MonoBehaviour
    {
        public static CustomBoardManager instance;
        public void Awake()
        {
            instance = this;
            SceneManager.sceneLoaded += SceneLoaded;
            SceneManager.sceneUnloaded += SceneUnloaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneUnloaded -= SceneUnloaded;
            foreach (GameObject board in objectBoards.Values)
            {
                if (board != null)
                    Destroy(board);
            }
            objectBoards.Clear();
            textMeshPro.RemoveAll(t => t == null);
            foreach (var k in characterDistanceArchive.Keys.Where(k => k == null).ToList()) characterDistanceArchive.Remove(k);
            foreach (var k in textColorArchive.Keys.Where(k => k == null).ToList()) textColorArchive.Remove(k);
            foreach (var k in boardPanelColors.Keys.Where(k => k == null).ToList()) boardPanelColors.Remove(k);
            if (ownsBoardMaterial && _boardMaterial != null)
            {
                Destroy(_boardMaterial);
                _boardMaterial = null;
            }
            instance = null;
        }

        private static bool _customBoardsEnabled = true;
        public static bool CustomBoardsEnabled
        {
            get => _customBoardsEnabled;
            set
            {
                if (_customBoardsEnabled == value)
                    return;

                _customBoardsEnabled = value;

                if (value)
                {
                    if (instance == null)
                        return;

                    instance.ReloadBoards();
                    if (instance.motdTitle != null)
                        instance.motdTitle.SetActive(true);
                    if (instance.motdText != null)
                        instance.motdText.SetActive(true);

                    GetObject("Environment Objects/LocalObjects_Prefab/TreeRoom/motdBodyText").SetActive(false);
                    GetObject("Environment Objects/LocalObjects_Prefab/TreeRoom/motdHeadingText").SetActive(false);
                } else
                {
                    foreach (GorillaNetworkJoinTrigger joinTrigger in PhotonNetworkController.Instance.allJoinTriggers)
                    {
                        try
                        {
                            JoinTriggerUI ui = joinTrigger.ui;
                            JoinTriggerUITemplate temp = ui.template;

                            if (_screenRed == null)
                            {
                                _screenRed = new Material(Shader.Find("GorillaTag/UberShader"))
                                {
                                    color = new Color32(226, 73, 41, 255)
                                };
                            }

                            if (_screenBlack == null)
                            {
                                _screenBlack = new Material(Shader.Find("GorillaTag/UberShader"))
                                {
                                    color = new Color32(39, 34, 28, 255)
                                };
                            }

                            temp.ScreenBG_AbandonPartyAndSoloJoin = _screenRed;
                            temp.ScreenBG_AlreadyInRoom = _screenBlack;
                            temp.ScreenBG_Error = _screenRed;
                        }
                        catch { }
                    }

                    foreach (GameObject board in instance.objectBoards.Values)
                        Destroy(board);

                    instance.objectBoards.Clear();

                    instance.motdTitle.SetActive(false);
                    instance.motdText.SetActive(false);

                    GetObject("Environment Objects/LocalObjects_Prefab/TreeRoom/motdHeadingText").SetActive(true);
                    GetObject("Environment Objects/LocalObjects_Prefab/TreeRoom/motdBodyText").SetActive(true);
                }
            }
        }

        private static readonly Dictionary<TextMeshPro, float> characterDistanceArchive = new Dictionary<TextMeshPro, float>();

        private static readonly Dictionary<TextMeshPro, Color> textColorArchive = new Dictionary<TextMeshPro, Color>();

        private static bool _customBoardFonts;
        public static bool CustomBoardFonts
        {
            get => _customBoardFonts;
            set
            {
                if (!value && _customBoardFonts)
                {
                    foreach (TextMeshPro txt in instance.textMeshPro.Where(text => text.isActiveAndEnabled))
                    {
                        txt.SafeSetFont(instance.archiveGorillaTagFont);
                        txt.SafeSetFontStyle(FontStyles.Normal);

                        if (characterDistanceArchive.TryGetValue(txt, out float charDistance))
                            txt.characterSpacing = charDistance;
                    }
                }

                _customBoardFonts = value;
            }
        }

        private static Material _screenRed;
        private static Material _screenBlack;

        public static bool CustomBoardTextEnabled = true;
        public static bool CustomBoardOrange = true;
        private static readonly Color32 boardOrange = new Color32(255, 138, 0, 255);
        private static readonly Dictionary<Renderer, Color> boardPanelColors = new Dictionary<Renderer, Color>();
        private static Material _boardMaterial = new Material(Shader.Find("GorillaTag/UberShader"));
        private static bool ownsBoardMaterial = true;
        public static Material BoardMaterial
        {
            get => _boardMaterial;
            set
            {
                bool ownsNextMaterial = value == null;
                Material nextMaterial = value ?? new Material(Shader.Find("GorillaTag/UberShader"));

                if (_boardMaterial != null && ownsBoardMaterial && _boardMaterial != nextMaterial)
                {
                    try { Destroy(_boardMaterial); } catch { }
                }

                _boardMaterial = nextMaterial;
                ownsBoardMaterial = ownsNextMaterial;
                instance?.ReloadBoards();
            }
        }

        #region Game Boards
        public const int StumpLeaderboardIndex = 3;
        public const int ForestLeaderboardIndex = 2;

        public static string motdTemplate = "You are using build {0}. This menu was created by iiDk (@crimsoncauldron) on Discord. " +
        "This menu is completely free and open sourced, if you paid for this menu you have been scammed. " +
        "There are a total of <b>{1}</b> mods on this menu. " +
        "<color=red>I, iiDk, am not responsible for any bans using this menu.</color> " +
        "If you get banned while using this, it's your responsibility.\n\nCurrent menu status: <b>Loading...</b>\nMade with <3 by iiDk, kingofnetflix, and others\n\n<alpha=128>{2} {0} {3}<alpha=255>";

        public Material forestMaterial;
        public Material stumpMaterial;
        private Material originalComputerMonitorMaterial;

        public GameObject motdTitle;
        public GameObject motdText;

        private TMP_FontAsset archiveGorillaTagFont;

        private bool hasFoundAllBoards;
        private bool loggedMissingBoardObjects;
        public void ReloadBoards()
        {
            hasFoundAllBoards = false;
            loggedMissingBoardObjects = false;
        }

        public void Update()
        {
            if (!hasFoundAllBoards)
            {
                try
                {
                    foreach (GameObject board in objectBoards.Values)
                        Destroy(board);

                    objectBoards.Clear();

                    foreach (GorillaNetworkJoinTrigger joinTrigger in PhotonNetworkController.Instance.allJoinTriggers)
                    {
                        try
                        {
                            JoinTriggerUI ui = joinTrigger.ui;
                            JoinTriggerUITemplate temp = ui.template;

                            temp.ScreenBG_AbandonPartyAndSoloJoin = BoardMaterial;
                            temp.ScreenBG_AlreadyInRoom = BoardMaterial;
                            temp.ScreenBG_ChangingGameModeSoloJoin = BoardMaterial;
                            temp.ScreenBG_Error = BoardMaterial;
                            temp.ScreenBG_InPrivateRoom = BoardMaterial;
                            temp.ScreenBG_LeaveRoomAndGroupJoin = BoardMaterial;
                            temp.ScreenBG_LeaveRoomAndSoloJoin = BoardMaterial;
                            temp.ScreenBG_NotConnectedSoloJoin = BoardMaterial;

                            TextMeshPro text = ui.screenText;
                            if (!textMeshPro.Contains(text))
                                textMeshPro.Add(text);
                        }
                        catch { }
                    }
                    PhotonNetworkController.Instance.UpdateTriggerScreens();

                    string[] objectsWithTMPro = {
                            "Environment Objects/LocalObjects_Prefab/TreeRoom/CodeOfConductHeadingText",
                            "Environment Objects/LocalObjects_Prefab/TreeRoom/COCBodyText_TitleData",
                            "Environment Objects/LocalObjects_Prefab/TreeRoom/Data",
                            "Environment Objects/LocalObjects_Prefab/TreeRoom/FunctionSelect"
                        };
                    foreach (string objectName in objectsWithTMPro)
                    {
                        GameObject obj = GetObject(objectName);
                        if (obj != null)
                        {
                            TextMeshPro text = obj.GetComponent<TextMeshPro>();
                            if (!textMeshPro.Contains(text))
                                textMeshPro.Add(text);
                        }
                        else if (!loggedMissingBoardObjects)
                            LogManager.Log("Could not find " + objectName);
                    }

                    GameObject forestBoard = GetObject("Environment Objects/LocalObjects_Prefab/Forest/ForestScoreboardAnchor/GorillaScoreBoard");
                    if (forestBoard == null)
                    {
                        hasFoundAllBoards = true;
                        loggedMissingBoardObjects = true;
                    }
                    else
                    {
                        Transform forestTransform = forestBoard.transform;
                        for (int i = 0; i < forestTransform.transform.childCount; i++)
                    {
                        GameObject v = forestTransform.GetChild(i).gameObject;
                        if ((!v.name.Contains("Board Text") && !v.name.Contains("Scoreboard_OfflineText")) ||
                            !v.activeSelf) continue;
                        TextMeshPro text = v.GetComponent<TextMeshPro>();
                            if (!textMeshPro.Contains(text))
                                textMeshPro.Add(text);
                        }

                        hasFoundAllBoards = true;
                    }
                    loggedMissingBoardObjects = true;
                }
                catch (Exception exc)
                {
                    LogManager.LogError($"Error with board colors at {exc.StackTrace}: {exc.Message}");
                    hasFoundAllBoards = false;
                }
            }

            Renderer computerMonitorRenderer = computerMonitor?.GetComponent<Renderer>();
            if (computerMonitorRenderer != null)
            {
                originalComputerMonitorMaterial ??= computerMonitorRenderer.sharedMaterial;
                if (CustomBoardsEnabled)
                    computerMonitorRenderer.material = BoardMaterial;
            }

            try
            {
                if (CustomBoardsEnabled)
                {
                BoardMaterial.color = CustomBoardOrange && ownsBoardMaterial ? (Color)boardOrange : backgroundColor.GetCurrentColor();

                if (motdTitle == null)
                {
                    GameObject motdObject = GetObject("Environment Objects/LocalObjects_Prefab/TreeRoom/motdHeadingText");
                    motdTitle = Instantiate(motdObject, motdObject.transform.parent);
                    motdObject.SetActive(false);
                }

                TextMeshPro motdHeadingText = motdTitle.GetComponent<TextMeshPro>();
                if (!textMeshPro.Contains(motdHeadingText))
                    textMeshPro.Add(motdHeadingText);

                motdHeadingText.richText = true;
                motdHeadingText.SafeSetFontSize(100);
                motdHeadingText.SafeSetText($"Thanks for using {(doCustomName ? customMenuName : "ii's <b>Stupid</b> Menu")}!");
                motdHeadingText.SafeSetFontStyle(activeFontStyle);
                motdHeadingText.SafeSetFont(activeFont);
                FollowMenuSettings(motdHeadingText, -4f);

                if (doCustomName)
                    motdHeadingText.SafeSetText("Thanks for using " + NoRichtextTags(customMenuName) + "!");

                motdHeadingText.SafeSetText(FollowMenuSettings(motdHeadingText.text));

                motdHeadingText.color = textColors[0].GetCurrentColor();
                motdHeadingText.overflowMode = TextOverflowModes.Overflow;

                if (motdText == null)
                {
                    GameObject motdObject = GetObject("Environment Objects/LocalObjects_Prefab/TreeRoom/motdBodyText");
                    motdText = Instantiate(motdObject, motdObject.transform.parent);
                    motdObject.SetActive(false);

                    motdText.GetComponent<PlayFabTitleDataTextDisplay>().enabled = false;
                }

                TextMeshPro motdBodyText = motdText.GetComponent<TextMeshPro>();
                if (!textMeshPro.Contains(motdBodyText))
                    textMeshPro.Add(motdBodyText);

                motdBodyText.richText = true;
                motdBodyText.SafeSetFontSize(100);
                motdBodyText.color = textColors[0].GetCurrentColor();
                motdBodyText.SafeSetFontStyle(activeFontStyle);
                motdBodyText.SafeSetFont(activeFont);
                FollowMenuSettings(motdBodyText, -4f);

                motdBodyText.SafeSetText(FollowMenuSettings(string.Format(motdTemplate, PluginInfo.Version, fullModAmount, PluginInfo.BetaBuild ? "Beta" : "Release", PluginInfo.BuildTimestamp )));
                }
                else
                    RestoreOriginalBoardScreens();
            }
            catch { }
            
            try
            {
                bool tintBoardText = CustomBoardsEnabled && CustomBoardTextEnabled;
                Color targetColor = textColors[0].GetCurrentColor();

                textMeshPro.RemoveAll(t => t == null);
                var deadKeys = characterDistanceArchive.Keys.Where(k => k == null).ToList();
                foreach (var k in deadKeys) characterDistanceArchive.Remove(k);
                var deadColorKeys = textColorArchive.Keys.Where(k => k == null).ToList();
                foreach (var k in deadColorKeys) textColorArchive.Remove(k);
                var deadPanelKeys = boardPanelColors.Keys.Where(k => k == null).ToList();
                foreach (var k in deadPanelKeys) boardPanelColors.Remove(k);

                foreach (TextMeshPro txt in textMeshPro.Where(text => text.isActiveAndEnabled))
                {
                    if (tintBoardText)
                    {
                        if (!textColorArchive.ContainsKey(txt))
                            textColorArchive[txt] = txt.color;

                        txt.color = targetColor;
                    }
                    else if (textColorArchive.TryGetValue(txt, out Color archivedColor))
                        txt.color = archivedColor;

                    if (!CustomBoardFonts) continue;
                    archiveGorillaTagFont ??= txt.font;

                    if (!characterDistanceArchive.ContainsKey(txt))
                        characterDistanceArchive[txt] = txt.characterSpacing;

                    txt.characterSpacing = 0f;

                    txt.SafeSetFont(activeFont);
                    txt.SafeSetFontStyle(activeFontStyle);
                }
            }
            catch { }

            try
            {
                bool tintBoardPanels = CustomBoardsEnabled && CustomBoardOrange;

                TintBoardSurfaces(tintBoardPanels);

                if (GorillaScoreboardTotalUpdater.allScoreboards != null)
                {
                    foreach (GorillaScoreBoard scoreboard in GorillaScoreboardTotalUpdater.allScoreboards)
                    {
                        if (scoreboard == null)
                            continue;

                        TintBoardPanels(scoreboard.transform, tintBoardPanels, 0);

                        if (scoreboard.leftPanel != null)
                            TintBoardPanels(scoreboard.leftPanel.transform, tintBoardPanels, 0);

                        if (scoreboard.rightPanel != null)
                            TintBoardPanels(scoreboard.rightPanel.transform, tintBoardPanels, 0);
                    }
                }
            }
            catch { }
        }

        private static readonly HashSet<string> loggedSurfaceHits = new HashSet<string>();

        private static readonly string[] BoardPanelNames =
        {
            "board", "code of conduct", "codeofconduct", "codeofconduct_group", "motd", "motdscreen",
            "monitor", "monitorscreen", "terminalmonitor", "screen", "screengreen", "screenred",
            "wallmonitorscreen_small", "wallmonitorforest", "wallmonitorforestbg"
        };

        private static readonly string[] BoardGroupDumps =
        {
            "Environment Objects/LocalObjects_Prefab/TreeRoom/TreeRoomInteractables",
            "Environment Objects/LocalObjects_Prefab/TreeRoom/TreeRoomBoundaryStones/BoundaryStoneSet_Forest"
        };

        private static readonly HashSet<string> dumpedBoardGroups = new HashSet<string>();

        private static Renderer[] boardSurfaces;
        private static float boardSurfaceRefresh;
        private static float nextSurfacePass;

        private static void RefreshBoardSurfaces()
        {
            if (boardSurfaces != null && boardSurfaces.Length > 0 && Time.time < boardSurfaceRefresh)
                return;

            boardSurfaceRefresh = Time.time + 5f;

            List<Renderer> surfaces = new List<Renderer>();

            try
            {
                GameObject treeRoom = GetObject("Environment Objects/LocalObjects_Prefab/TreeRoom");
                if (treeRoom != null)
                    surfaces.AddRange(treeRoom.GetComponentsInChildren<Renderer>(true));
            }
            catch { }

            boardSurfaces = surfaces
                .Where(renderer => renderer != null && renderer.GetComponent<TMP_Text>() == null)
                .ToArray();
        }

        private static void TintBoardSurfaces(bool tint)
        {
            if (Time.time < nextSurfacePass)
                return;

            nextSurfacePass = Time.time + 1f;

            RefreshBoardSurfaces();

            if (boardSurfaces == null || boardSurfaces.Length == 0)
                return;

            foreach (Renderer surface in boardSurfaces)
            {
                if (surface == null)
                    continue;

                bool nameMatch = IsBoardPanel(surface.transform);
                bool materialMatch = IsBoardMaterial(surface);

                if (!nameMatch && !materialMatch)
                    continue;

                ApplyBoardPanelColor(surface, tint);

                if (tint && loggedSurfaceHits.Add(PathOf(surface.transform)))
                    LogManager.Log($"Orange board: tinted {PathOf(surface.transform)} material={surface.sharedMaterial.name} shader={surface.sharedMaterial.shader.name}{(materialMatch && !nameMatch ? " (by material)" : "")}");
            }

            if (tint)
            {
                foreach (string group in BoardGroupDumps)
                    DumpBoardGroup(group);

                DumpTreeRoomSurfaces();
            }
        }

        private static bool IsBoardMaterial(Renderer surface)
        {
            Material material = surface.sharedMaterial;
            if (material == null)
                return false;

            return material.name.IndexOf("green", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void DumpTreeRoomSurfaces()
        {
            if (!dumpedBoardGroups.Add("tree-room-surfaces"))
                return;

            GameObject treeRoom = GetObject("Environment Objects/LocalObjects_Prefab/TreeRoom");
            if (treeRoom == null)
            {
                dumpedBoardGroups.Remove("tree-room-surfaces");
                return;
            }

            foreach (TextMeshPro text in instance.textMeshPro.ToArray())
            {
                if (text == null)
                    continue;

                Vector3 position = text.transform.position;
                LogManager.Log($"TreeRoomText: {PathOf(text.transform)} pos=({position.x:0.00},{position.y:0.00},{position.z:0.00}) forward=({text.transform.forward.x:0.00},{text.transform.forward.y:0.00},{text.transform.forward.z:0.00})");
            }

            int dumped = 0;

            foreach (Renderer renderer in treeRoom.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || dumped >= 90)
                    break;

                if (renderer.GetComponent<TMP_Text>() != null)
                    continue;

                Bounds bounds = renderer.bounds;
                if (bounds.size.magnitude > 30f || bounds.size.magnitude < 0.4f)
                    continue;

                Material material = renderer.sharedMaterial;
                dumped++;

                LogManager.Log($"TreeRoomSurface: {PathOf(renderer.transform)} material={(material == null ? "none" : material.name)} size=({bounds.size.x:0.00},{bounds.size.y:0.00},{bounds.size.z:0.00}) center=({bounds.center.x:0.0},{bounds.center.y:0.0},{bounds.center.z:0.0})");
            }

            LogManager.Log($"TreeRoomSurface: dumped {dumped} renderers");
        }

        private static void DumpBoardGroup(string path)
        {
            if (!dumpedBoardGroups.Add(path))
                return;

            GameObject group = GetObject(path);
            if (group == null)
            {
                dumpedBoardGroups.Remove(path);
                return;
            }

            string children = "";

            foreach (Renderer renderer in group.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.GetComponent<TMP_Text>() != null)
                    continue;

                Material material = renderer.sharedMaterial;
                children += $"{renderer.gameObject.name}[{(material == null ? "no material" : material.name)}] ";
            }

            LogManager.Log($"BoardGroup[{path}]: {children}");
        }

        private static bool IsBoardPanel(Transform target)
        {
            Transform current = target;

            for (int depth = 0; depth < 4 && current != null; depth++)
            {
                string name = current.name;
                int suffix = name.LastIndexOf(" (", StringComparison.Ordinal);

                if (suffix > 0 && name.EndsWith(")"))
                    name = name.Substring(0, suffix);

                foreach (string panelName in BoardPanelNames)
                    if (string.Equals(name, panelName, StringComparison.OrdinalIgnoreCase))
                        return true;

                current = current.parent;
            }

            return false;
        }

        private static string PathOf(Transform target)
        {
            if (target == null)
                return null;

            string path = target.name;

            while (target.parent != null)
            {
                target = target.parent;
                path = target.name + "/" + path;
            }

            return path;
        }

        private static void TintBoardPanels(Transform target, bool tint, int depth)
        {
            if (target == null || depth > 2)
                return;

            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer != null && renderer.GetComponent<TMP_Text>() == null && !IsBoardControl(target.name))
                ApplyBoardPanelColor(renderer, tint);

            for (int i = 0; i < target.childCount; i++)
                TintBoardPanels(target.GetChild(i), tint, depth + 1);
        }

        private static bool IsBoardControl(string name) =>
            name.IndexOf("text", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("button", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("toggle", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0;

        private static void ApplyBoardPanelColor(Renderer renderer, bool tint)
        {
            Material material = renderer.material;
            if (material == null)
                return;

            bool hasColor = material.HasProperty("_Color");
            bool hasBaseColor = !hasColor && material.HasProperty("_BaseColor");

            if (!hasColor && !hasBaseColor)
                return;

            if (tint)
            {
                if (!boardPanelColors.ContainsKey(renderer))
                    boardPanelColors[renderer] = hasColor ? material.GetColor("_Color") : material.GetColor("_BaseColor");

                if (hasColor)
                    material.color = boardOrange;
                else
                    material.SetColor("_BaseColor", boardOrange);
            }
            else if (boardPanelColors.TryGetValue(renderer, out Color original))
            {
                if (hasColor)
                    material.color = original;
                else
                    material.SetColor("_BaseColor", original);

                boardPanelColors.Remove(renderer);
            }
        }
        #endregion

        private void SceneUnloaded(Scene scene)
        {
            LogManager.Log($"SceneUnloaded: {scene.name}");
        }

        private void RestoreOriginalBoardScreens()
        {
            Renderer renderer = computerMonitor?.GetComponent<Renderer>();
            if (renderer != null && originalComputerMonitorMaterial != null)
                renderer.material = originalComputerMonitorMaterial;
        }

        #region Object Boards
        public readonly Dictionary<string, GameObject> objectBoards = new Dictionary<string, GameObject>();
        public List<GorillaNetworkJoinTrigger> triggers = new List<GorillaNetworkJoinTrigger>();
        public readonly List<TextMeshPro> textMeshPro = new List<TextMeshPro>();
        public GameObject computerMonitor;

        public void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            LogManager.Log($"SceneLoaded: {scene.name} ({mode})");

            if (!CustomBoardsEnabled) return;
            if (!BoardInformations.TryGetValue(scene.name, out var config)) return;

            CreateObjectBoard(scene.name, config.GameObjectPath, config.Position, config.Rotation, config.Scale);
        }

        public void CreateObjectBoard(string scene, string gameObject, Vector3? position = null, Vector3? rotation = null, Vector3? scale = null)
        {
            try
            {
                if (objectBoards.TryGetValue(scene, out GameObject existingBoard))
                {
                    if (existingBoard != null)
                        Destroy(existingBoard);

                    objectBoards.Remove(scene);
                }

                GameObject board = GameObject.CreatePrimitive(PrimitiveType.Plane);
                board.transform.parent = GetObject(gameObject).transform;
                board.transform.localPosition = position ?? new Vector3(-22.1964f, -34.9f, 0.57f);
                board.transform.localRotation = Quaternion.Euler(rotation ?? new Vector3(270f, 0f, 0f));
                board.transform.localScale = scale ?? new Vector3(21.6f, 2.4f, 22f);

                Destroy(board.GetComponent<Collider>());
                board.GetComponent<Renderer>().material = BoardMaterial;

                objectBoards.Add(scene, board);
            }
            catch (Exception e)
            {
                LogManager.LogError($"Failed to create object board for scene {scene}: {e}");
            }
        }

        private readonly struct BoardInformation
        {
            public readonly string GameObjectPath;
            public readonly Vector3 Position;
            public readonly Vector3 Rotation;
            public readonly Vector3 Scale;

            public BoardInformation(string path, Vector3 pos, Vector3 rot, Vector3 scale)
            {
                GameObjectPath = path;
                Position = pos;
                Rotation = rot;
                Scale = scale;
            }
        }

        private static readonly Dictionary<string, BoardInformation> BoardInformations = new Dictionary<string, BoardInformation>
        {
            ["Canyon2"] = new BoardInformation(
                "Canyon/CanyonScoreboardAnchor/GorillaScoreBoard",
                new Vector3(-24.5019f, -28.7746f, 0.1f),
                new Vector3(270f, 0f, 0f),
                new Vector3(21.5946f, 1f, 22.1782f)
            ),
            ["Skyjungle"] = new BoardInformation(
                "skyjungle/UI/Scoreboard/GorillaScoreBoard",
                new Vector3(-21.2764f, -32.1928f, 0f),
                new Vector3(270.2987f, 0.2f, 359.9f),
                new Vector3(21.6f, 0.1f, 20.4909f)
            ),
            ["Mountain"] = new BoardInformation(
                "Mountain/MountainScoreboardAnchor/GorillaScoreBoard",
                Vector3.zero,
                Vector3.zero,
                Vector3.one
            ),
            ["Metropolis"] = new BoardInformation(
                "MetroMain/ComputerArea/Scoreboard/GorillaScoreBoard",
                new Vector3(-25.1f, -31f, 0.1502f),
                new Vector3(270.1958f, 0.2086f, 0f),
                new Vector3(21f, 102.9727f, 21.4f)
            ),
            ["Bayou"] = new BoardInformation(
                "BayouMain/ComputerArea/GorillaScoreBoardPhysical",
                new Vector3(-28.3419f, -26.851f, 0.3f),
                new Vector3(270f, 0f, 0f),
                new Vector3(21.3636f, 38f, 21f)
            ),
            ["Beach"] = new BoardInformation(
                "BeachScoreboardAnchor/GorillaScoreBoard",
                new Vector3(-22.1964f, -33.7126f, 0.1f),
                new Vector3(270.056f, 0f, 0f),
                new Vector3(21.2f, 2f, 21.6f)
            ),
            ["Cave"] = new BoardInformation(
                "Cave_Main_Prefab/CrystalCaveScoreboardAnchor/GorillaScoreBoard",
                new Vector3(-22.1964f, -33.7126f, 0.1f),
                new Vector3(270.056f, 0f, 0f),
                new Vector3(21.2f, 2f, 21.6f)
            ),
            ["Rotating"] = new BoardInformation(
                "RotatingPermanentEntrance/UI (1)/RotatingScoreboard/RotatingScoreboardAnchor/GorillaScoreBoard",
                new Vector3(-22.1964f, -33.7126f, 0.1f),
                new Vector3(270.056f, 0f, 0f),
                new Vector3(21.2f, 2f, 21.6f)
            ),
            ["MonkeBlocks"] = new BoardInformation(
                "Environment Objects/MonkeBlocksRoomPersistent/AtticScoreBoard/AtticScoreboardAnchor/GorillaScoreBoard",
                new Vector3(-22.1964f, -24.5091f, 0.57f),
                new Vector3(270.1856f, 0.1f, 0f),
                new Vector3(21.6f, 1.2f, 20.8f)
            ),
            ["Basement"] = new BoardInformation(
                "Basement/BasementScoreboardAnchor/GorillaScoreBoard/",
                new Vector3(-22.1964f, -24.5091f, 0.57f),
                new Vector3(270.1856f, 0.1f, 0f),
                new Vector3(21.6f, 1.2f, 20.8f)
            ),
            ["City"] = new BoardInformation(
                "City_Pretty/CosmeticsScoreboardAnchor/GorillaScoreBoard",
                new Vector3(-22.1964f, -34.9f, 0.57f),
                new Vector3(270f, 0f, 0f),
                new Vector3(21.6f, 2.4f, 22f)
            )
        };
        #endregion
    }
}
