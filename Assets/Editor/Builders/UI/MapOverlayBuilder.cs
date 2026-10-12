using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

public static class MapOverlayBuilder
{
    public const string MinimapPath = "Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstWorldMinimap_Rpg11.prefab";
    public static readonly string[] Actions = { "MapToggle", "MapZoomIn", "MapZoomOut" };
    static readonly string[] Labels = { "지도 펼쳐보기", "펼친 지도 확대", "펼친 지도 축소" };
    static readonly string[] Bindings = { "<Keyboard>/tab", "<Keyboard>/equals", "<Keyboard>/minus" };

    public static string Apply(string output)
    {
        OverburstSettingsGothicBuilder.RequireIdle();
        string allowed = Path.GetFullPath("../개인파일/코덱스산출") + Path.DirectorySeparatorChar;
        output = Path.GetFullPath(output);
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output)) throw new ArgumentException("Fresh owned output required");
        Directory.CreateDirectory(output);
        foreach (string path in new[] { MinimapPath, OverburstGameMenuBuilder.PrefabPath, PlayerInputFacade.AssetPath })
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (!asset || EditorUtility.IsDirty(asset)) throw new InvalidOperationException("Missing or dirty asset: " + path);
            foreach (string file in new[] { path, path + ".meta" })
            { string target = Path.Combine(output, "Before", file); Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(file, target, false); }
        }
        ApplyInput();
        Edit(MinimapPath, ApplyMap);
        Edit(OverburstGameMenuBuilder.PrefabPath, ApplySettings);
        return "PASS: shared-outline overlay, three input actions and existing-style settings rows";
    }
    static void ApplyInput()
    {
        var asset = InputActionAsset.FromJson(File.ReadAllText(PlayerInputFacade.AssetPath));
        try
        {
            var map = asset.FindActionMap(PlayerInputFacade.GameplayMapName, true);
            for (int i = 0; i < Actions.Length; i++)
                if (map.FindAction(Actions[i]) == null)
                {
                    if (map.bindings.Any(b => b.path == Bindings[i])) throw new InvalidOperationException("Map key already in use: " + Bindings[i]);
                    map.AddAction(Actions[i], InputActionType.Button, Bindings[i], expectedControlLayout: "Button");
                }
            File.WriteAllText(PlayerInputFacade.AssetPath, asset.ToJson());
            AssetDatabase.ImportAsset(PlayerInputFacade.AssetPath, ImportAssetOptions.ForceSynchronousImport);
        }
        finally { Object.DestroyImmediate(asset); }
    }
    public static string ApplyGuidance(string output)
    {
        OverburstSettingsGothicBuilder.RequireIdle();
        output = Path.GetFullPath(output);
        if (!output.StartsWith(Path.GetFullPath("../개인파일/코덱스산출") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || Directory.Exists(output)) throw new ArgumentException("Fresh owned output required");
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(MinimapPath);
        if (!asset || EditorUtility.IsDirty(asset)) throw new InvalidOperationException("Missing or dirty minimap");
        Directory.CreateDirectory(output);
        foreach (string p in new[] {MinimapPath, MinimapPath + ".meta"}) File.Copy(p, Path.Combine(output, Path.GetFileName(p)));
        Edit(MinimapPath, root =>
        {
            var controller = root.GetComponent<WorldMinimapController>();
            var player = root.transform.Find("MinimapView/CircleViewport/PlayerMarker").GetComponent<UnityEngine.UI.Image>();
            MinimapGuideView Add(Transform parent)
            {
                var t = Rect("ObjectiveGuides", parent, Vector2.one * 1024);
                var guides = t.gameObject.AddComponent<MinimapGuideView>();
                guides.icons = Rect("KnownObjectives", t, Vector2.one * 1024).gameObject.AddComponent<MinimapMarkerGraphic>();
                guides.arrows = Rect("Directions", t, Vector2.one * 1024).gameObject.AddComponent<MinimapMarkerGraphic>();
                guides.icons.raycastTarget = guides.arrows.raycastTarget = false;
                Set(guides.icons, "atlas", controller.View.Markers.Atlas); Set(guides.arrows, "atlas", player.sprite.texture);
                var so = new SerializedObject(guides.arrows); so.FindProperty("singleIcon").boolValue = true; so.ApplyModifiedPropertiesWithoutUndo();
                return guides;
            }
            if (!controller.View.Guides) Set(controller.View, "guides", Add(controller.View.Markers.transform.parent));
            if (!controller.Overlay.guides) controller.Overlay.guides = Add(controller.Overlay.viewport);
            if (controller.View.Guides.transform.GetSiblingIndex() > player.transform.GetSiblingIndex())
                controller.View.Guides.transform.SetSiblingIndex(player.transform.GetSiblingIndex());
            if (controller.Overlay.guides.transform.GetSiblingIndex() > controller.Overlay.playerMarker.GetSiblingIndex())
                controller.Overlay.guides.transform.SetSiblingIndex(controller.Overlay.playerMarker.GetSiblingIndex());
        });
        return "PASS: objective and direction batches reuse the existing marker and player textures";
    }
    static void Edit(string path, Action<GameObject> action)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try { action(root); PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved); if (!saved) throw new InvalidOperationException("Save failed: " + path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    static RectTransform Rect(string name, Transform parent, Vector2 size)
    {
        var t = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        t.SetParent(parent, false); t.gameObject.layer = parent.gameObject.layer;
        t.anchorMin = t.anchorMax = t.pivot = Vector2.one * .5f; t.sizeDelta = size;
        return t;
    }
    static void ApplyMap(GameObject root)
    {
        var controller = root.GetComponent<WorldMinimapController>();
        if (root.GetComponentInChildren<MinimapOverlayView>(true)) return;
        var viewport = Rect("ExploredMapOverlay", root.transform, new Vector2(1080, 680)); viewport.SetAsFirstSibling();
        viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        var overlay = viewport.gameObject.AddComponent<MinimapOverlayView>(); overlay.viewport = viewport;
        overlay.outline = Rect("ExploredOutline", viewport, Vector2.one * 1280).gameObject.AddComponent<UnityEngine.UI.RawImage>();
        overlay.outline.raycastTarget = false; overlay.outline.color = new Color(1, 1, 1, .35f);
        overlay.markers = Rect("NearbyEnemies", viewport, Vector2.one * 1024).gameObject.AddComponent<MinimapMarkerGraphic>();
        overlay.markers.raycastTarget = false;
        Set(overlay.markers, "atlas", controller.View.Markers.Atlas);
        var existingPlayer = root.transform.Find("MinimapView/CircleViewport/PlayerMarker").GetComponent<UnityEngine.UI.Image>();
        var player = Object.Instantiate(existingPlayer, viewport, false); player.name = "Player";
        player.color = new Color(existingPlayer.color.r, existingPlayer.color.g, existingPlayer.color.b, .75f);
        player.raycastTarget = false; player.rectTransform.sizeDelta = new Vector2(12, 12);
        overlay.playerMarker = player.rectTransform; Set(controller, "overlay", overlay);
        viewport.gameObject.SetActive(false);
    }
    static void ApplySettings(GameObject root)
    {
        var panel = root.GetComponentInChildren<OverburstSettingsPanel>(true);
        var view = panel.GetComponent<OverburstSettingsGothicView>();
        if (!view) throw new InvalidOperationException("Existing settings presentation required");
        var rows = view.rows.ToList(); var keys = panel.keyRows.ToList();
        var template = panel.cameraShake.GetComponentInParent<OverburstSettingsGothicRow>(true);
        if (!template) throw new InvalidOperationException("Existing camera setting row missing");
        UnityEngine.UI.Slider Slider(string name, string description, float min, float max, float value)
        {
            var row = Object.Instantiate(template, panel.combatScroll.content, false); row.name = "Row • " + name;
            row.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
            row.title = name; row.description = description; row.descriptionSource = null; row.tip = "이 탭의 초기화로 기본값을 되돌립니다.";
            row.transform.Find("Label").GetComponent<UnityEngine.UI.Text>().text = name;
            row.transform.Find("Description").GetComponent<UnityEngine.UI.Text>().text = description;
            row.slider.minValue = min; row.slider.maxValue = max; row.slider.SetValueWithoutNotify(value);
            rows.Add(row); return row.slider;
        }
        if (!panel.mapOverlayScale) panel.mapOverlayScale = Slider("펼친 지도 배율", "지도 키로 펼친 화면의 배율입니다. 전투 화면은 계속 움직입니다.", .1f, 2.5f, 1f);
        if (!panel.mapOverlayOpacity) panel.mapOverlayOpacity = Slider("펼친 지도 불투명도", "지형선의 진하기를 조절합니다. 바닥 면은 채우지 않습니다.", .1f, .8f, .35f);
        for (int i = 0; i < Actions.Length; i++)
        {
            if (keys.Any(k => k.actionName == Actions[i])) continue;
            var key = Object.Instantiate(panel.keyRows[0], panel.keyScroll.content, false);
            key.name = "Key • " + Labels[i]; key.actionName = Actions[i]; key.compositePart = key.mirrorUiAction = "";
            key.label.text = Labels[i]; key.keyText.text = OverburstKeyBindingRow.DisplayName(Bindings[i]);
            var row = key.GetComponent<OverburstSettingsGothicRow>(); row.title = Labels[i]; row.descriptionSource = null;
            row.description = i == 0 ? "탐색한 길을 반투명 지도로 펼치거나 닫습니다." : "펼친 지도에만 적용합니다. 카메라 휠 줌은 그대로 사용할 수 있습니다.";
            row.tip = "다른 동작과 같은 키를 고르면 두 키가 교환됩니다.";
            rows.Add(row); keys.Add(key);
        }
        panel.keyRows = keys.ToArray(); view.rows = rows.ToArray();
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(panel.combatScroll.content);
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(panel.keyScroll.content);
    }
    static void Set(Object target, string field, Object value)
    {
        var so = new SerializedObject(target); so.FindProperty(field).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo();
    }
}
