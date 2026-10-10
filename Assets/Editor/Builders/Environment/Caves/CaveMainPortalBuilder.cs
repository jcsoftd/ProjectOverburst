using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Overburst.Caves;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class CaveMainPortalBuilder
{
    public const string Main = "Assets/ProjectOverburst/00_Scenes/MainScene.unity";
    public const string ResourceRoot = "Assets/ProjectOverburst/Resources/World/Caves";
    public const string PanelPath = ResourceRoot + "/PF_CavePortalPanel.prefab";
    public const string MapPath = "Assets/ProjectOverburst/Resources/Items/Maps/Map_Caves.asset";
    public static string RunPath(int count) => CavePlatformMapBuilder.SceneRoot + "/Caves_Run_" + count + ".unity";
    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    public static object Bake(int count)
    {
        CavePlatformMapBuilder.Guard();
        if (count != 9 && count != 12 && count != 15) throw new ArgumentOutOfRangeException(nameof(count));
        string path = RunPath(count);
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path)) throw new InvalidOperationException("Run scene already exists: " + path);
        if (count == 15)
        {
            if (!AssetDatabase.CopyAsset(CavePlatformMapBuilder.SceneRoot + "/Caves_WebNests_15_19216.unity", path)) throw new IOException(path);
        }
        else
        {
            var result = JObject.FromObject(CavePlatformMapBuilder.Generate(count, 19216));
            var source = (string)result["scenePath"];
            var error = AssetDatabase.MoveAsset(source, path);
            if (!string.IsNullOrEmpty(error)) throw new IOException(error);
        }
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var world = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CaveWorld>(true)).Single();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var explorer in root.GetComponentsInChildren<CaveExplorer>(true)) explorer.gameObject.SetActive(false);
                foreach (var camera in root.GetComponentsInChildren<CaveCamera>(true)) camera.gameObject.SetActive(false);
            }
            int ground = LayerMask.NameToLayer("Ground");
            foreach (var collider in world.generatedRoot.GetComponentsInChildren<Collider>(true))
                if (collider.GetComponentInParent<CaveWalkSurface>()) collider.gameObject.layer = ground;
            Physics.SyncTransforms();
            var entry = new GameObject("Cave Run Entry").transform; entry.SetParent(world.transform);
            entry.position = world.courts[0].center + Vector3.up * .12f;
            var run = world.gameObject.AddComponent<CaveRunWorld>(); run.entryPoint = entry;
            var portal = new GameObject("Cave Return Portal").AddComponent<CaveDungeonPortal>();
            portal.transform.SetParent(world.transform); portal.returnToTown = true;
            var at = entry.position + Vector3.right * 3;
            portal.transform.position = world.Ground(at, out var hit) ? hit.point + Vector3.up * .05f : entry.position;
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException(path);
            AddBuildScene(path);
            return new { path, count = world.courts.Count, connections = world.passages.Count, entry = entry.position.ToString() };
        }
        finally { EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(original); }
    }

    static void AddBuildScene(string path)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        var existing = scenes.Find(s => s.path == path);
        if (existing == null) scenes.Add(new EditorBuildSettingsScene(path, true)); else existing.enabled = true;
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    [MenuItem("Overburst/Caves/메인 마을 포탈 설치")]
    public static void Install()
    {
        CavePlatformMapBuilder.Guard();
        foreach (int count in new[] { 9, 12, 15 })
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(RunPath(count))) throw new InvalidOperationException("Bake the " + count + " platform run first.");
        Folder(ResourceRoot);
        var map = AssetDatabase.LoadAssetAtPath<MapItemData>(MapPath);
        if (!map)
        {
            map = Object.Instantiate(Resources.Load<MapItemData>("Items/Maps/Map_Diamond01"));
            map.name = "Map_Caves"; map.itemName = "깊은 동굴"; map.dungeonThemeId = "Caves";
            map.description = "바위 플랫폼과 다리가 이어진 깊은 동굴.";
            AssetDatabase.CreateAsset(map, MapPath);
        }
        const string catalogPath = "Assets/ProjectOverburst/Resources/Persistence/Supplemental/CaveContent.asset";
        Folder(Path.GetDirectoryName(catalogPath).Replace('\\', '/'));
        var catalog = AssetDatabase.LoadAssetAtPath<AccountContentRegistry>(catalogPath);
        if (!catalog) { catalog = ScriptableObject.CreateInstance<AccountContentRegistry>(); AssetDatabase.CreateAsset(catalog, catalogPath); }
        catalog.SetAuthoringEntries(new List<AccountContentEntry> { new AccountContentEntry { id = "map.caves", asset = map } });
        EditorUtility.SetDirty(catalog);
        var panel = BuildPanel();
        var original = SceneManager.GetActiveScene();
        var main = SceneManager.GetSceneByPath(Main); bool opened = !main.IsValid() || !main.isLoaded;
        if (opened) main = EditorSceneManager.OpenScene(Main, OpenSceneMode.Additive);
        if (main.isDirty) throw new InvalidOperationException("Save MainScene before installing its portal.");
        try
        {
            SceneManager.SetActiveScene(main); Physics.SyncTransforms();
            var portal = main.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CaveDungeonPortal>(true)).SingleOrDefault();
            if (!portal) portal = new GameObject("Cave Dungeon Portal · 플랫폼 수 선택").AddComponent<CaveDungeonPortal>();
            // The open aisle between the merchant tent and forge leaves the spawn and existing portals clear.
            var position = new Vector3(151f, 0f, 99f);
            if (Physics.Raycast(position + Vector3.up * 15, Vector3.down, out var hit, 40, LayerMask.GetMask("Ground"))) position.y = hit.point.y + .04f;
            portal.transform.position = position; portal.panelPrefab = panel; portal.mapDefinition = map;
            portal.destinations = new[] { 9, 12, 15 }.Select(c => new CaveDungeonPortal.Destination { platforms = c, sceneName = Path.GetFileNameWithoutExtension(RunPath(c)) }).ToArray();
            portal.SelectIndex(2);
            EditorUtility.SetDirty(portal); EditorSceneManager.MarkSceneDirty(main);
            if (!EditorSceneManager.SaveScene(main)) throw new IOException(Main);
            AssetDatabase.SaveAssets();
        }
        finally { if (opened) EditorSceneManager.CloseScene(main, true); SceneManager.SetActiveScene(original); }
    }

    static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); return rect;
    }
    static void CopyImage(UnityEngine.UI.Image source, UnityEngine.UI.Image target)
    { target.sprite = source.sprite; target.type = source.type; target.color = source.color; target.material = source.material; target.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier; }
    static UnityEngine.UI.Text Label(Transform parent, string name, string value, float y, int size, Color color)
    {
        var text = Rect(name, parent, 32, y, 456, 60).gameObject.AddComponent<UnityEngine.UI.Text>();
        text.font = Resources.Load<Font>("UI/Fonts/DamageFloating/Pretendard_Medium"); text.fontSize = size;
        text.color = color; text.text = value; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false; return text;
    }
    static UnityEngine.UI.Button Button(GameObject source, Transform parent, string name, string label, float x, float y, float w, float h)
    {
        var go = Object.Instantiate(source, parent, false); go.name = name;
        var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); rect.localScale = Vector3.one;
        var button = go.GetComponent<UnityEngine.UI.Button>(); button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        foreach (var text in go.GetComponentsInChildren<UnityEngine.UI.Text>(true))
        {
            text.text = label; text.font = Resources.Load<Font>("UI/Fonts/DamageFloating/Pretendard_Medium"); text.fontSize = 20;
            text.alignment = TextAnchor.MiddleCenter;
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(8, 0); text.rectTransform.offsetMax = new Vector2(-8, 0);
        }
        if (w < 100)
            foreach (var image in go.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                image.pixelsPerUnitMultiplier = 3;
                if (image.transform == go.transform) continue;
                image.rectTransform.anchorMin = Vector2.zero; image.rectTransform.anchorMax = Vector2.one;
                image.rectTransform.offsetMin = Vector2.one * 5; image.rectTransform.offsetMax = Vector2.one * -5;
            }
        return button;
    }
    static CavePortalPanel BuildPanel()
    {
        var original = SceneManager.GetActiveScene();
        var staging = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(staging);
        var root = new GameObject("Cave Portal Panel", typeof(RectTransform));
        try
        {
            var full = (RectTransform)root.transform; full.anchorMin = Vector2.zero; full.anchorMax = Vector2.one;
            full.offsetMin = full.offsetMax = Vector2.zero;
            root.AddComponent<UnityEngine.UI.Image>().color = new Color(0, 0, 0, .55f);
            var panel = root.AddComponent<CavePortalPanel>();
            var window = Rect("Cave Window", full, 0, 0, 520, 460);
            window.anchorMin = window.anchorMax = window.pivot = Vector2.one * .5f;
            var chrome = Rect("Window Chrome", window, 0, 0, 1040, 920); chrome.localScale = Vector3.one * .5f;
            var donor = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstInventory_Rpg11.prefab").transform;
            CopyImage(donor.GetComponent<UnityEngine.UI.Image>(), chrome.gameObject.AddComponent<UnityEngine.UI.Image>());
            foreach (var name in new[] { "Opaque Content Surface", "Borders", "Header" }) Object.Instantiate(donor.Find(name).gameObject, chrome, false).name = name;
            ((RectTransform)chrome.Find("Opaque Content Surface")).sizeDelta = new Vector2(1020, 762);
            var title = chrome.Find("Header/Text").GetComponent<UnityEngine.UI.Text>(); title.text = "동굴 탐험";
            title.rectTransform.anchoredPosition = new Vector2(96, -28); title.rectTransform.sizeDelta = new Vector2(848, 88);
            var header = chrome.Find("Header");
            var drag = header.GetComponent<DuloGames.UI.UIDragObject>(); if (drag) drag.target = window;
            panel.close = header.Find("Button (Close)").GetComponent<UnityEngine.UI.Button>();
            ((RectTransform)panel.close.transform).anchoredPosition = new Vector2(920, -40);
            panel.close.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            var mapPanel = new SerializedObject(Resources.Load<GameObject>("UI/MapDungeon/PF_MapDungeonPortal").GetComponent<MapDungeonPortalPanel>());
            var primary = (GameObject)mapPanel.FindProperty("primaryButtonArtwork").objectReferenceValue;
            var secondary = (GameObject)mapPanel.FindProperty("secondaryButtonArtwork").objectReferenceValue;
            var gold = new Color(.88f, .74f, .48f);
            Label(window, "Count Title", "전투 플랫폼 수", 82, 19, gold);
            panel.countLabel = Label(window, "Platform Count", "15개", 156, 42, new Color(.93f, .90f, .83f));
            panel.previous = Button(secondary, window, "Previous", "−", 65, 158, 70, 56);
            panel.next = Button(secondary, window, "Next", "+", 385, 158, 70, 56);
            panel.status = Label(window, "Entry Status", "", 255, 16, new Color(.74f, .71f, .66f));
            panel.enter = Button(primary, window, "Enter Cave", "동굴 입장", 134, 350, 252, 58);
            var saved = PrefabUtility.SaveAsPrefabAsset(root, PanelPath); return saved.GetComponent<CavePortalPanel>();
        }
        finally
        {
            Object.DestroyImmediate(root); EditorSceneManager.CloseScene(staging, true);
            SceneManager.SetActiveScene(original);
        }
    }
}
