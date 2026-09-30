using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

public static class BloodHitVfxBuilder
{
    private const string Root = "Assets/ProjectOverburst/Resources/Combat/Blood";
    private const string Vendor = "Assets/ThirdParty/06_VFX/HIVEMIND/RealisticBloodVFX/URP/RealisticBlood/VFXGraphs/";
    [MenuItem("OVERBURST/Combat/Build Blood Hit VFX")]
    public static void BuildFromMenu() => Debug.Log(Build());
    public static string Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before authoring.");
        Directory.CreateDirectory(Root);
        AssetDatabase.Refresh();
        string[] themes = { "SpiderBrood", "VenomBrood", "PrimalHunt", "CavernMutants" };
        string[] main = { "#78968B", "#86A557", "#A48B58", "#88758E" };
        string[] secondary = { "#354840", "#3B5635", "#504329", "#40354B" };
        float[] sizes = { 4.2f, 4.8f, 5.2f, 4.8f };
        var profiles = new BloodHitProfile[4];
        for (int i = 0; i < 4; i++)
        {
            string profilePath = Root + "/" + themes[i] + ".asset";
            bool exists = AssetDatabase.LoadAssetAtPath<BloodHitProfile>(profilePath) != null;
            profiles[i] = Get<BloodHitProfile>(profilePath);
            if (exists) continue; // Preserve approved color/size tuning on rebuild.
            ColorUtility.TryParseHtmlString(main[i], out profiles[i].mainColor);
            ColorUtility.TryParseHtmlString(secondary[i], out profiles[i].secondaryColor);
            profiles[i].specularColor = Color.Lerp(profiles[i].mainColor, Color.white, .25f);
            profiles[i].specular = .65f;
            profiles[i].size = sizes[i];
            EditorUtility.SetDirty(profiles[i]);
        }
        var catalog = Get<BloodHitCatalog>("Assets/ProjectOverburst/Resources/Combat/BloodHitCatalog.asset");
        if (catalog.slash == null) catalog.slash = BloodHitGraphBuilder.Create("Slash"); // 2026-10-01: 연결된 그래프 유지
        if (catalog.stab == null) catalog.stab = BloodHitGraphBuilder.Create("Stab"); // 2026-10-01: 연결된 그래프 유지
        if (catalog.burst == null) catalog.burst = BloodHitGraphBuilder.Create("Burst"); // 2026-10-01: 연결된 그래프 유지
        if (catalog.slash == null || catalog.stab == null || catalog.burst == null) throw new Exception("Restore URP vendor pack first.");
        if (catalog.lifetime <= 0f) catalog.lifetime = 2.5f; // 2026-10-01: 조정한 수명 유지
        EditorUtility.SetDirty(catalog);
        int count = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/ProjectOverburst/Resources/Enemies/Themes/Actors" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            int theme = Array.FindIndex(themes, x => path.Contains(x));
            if (theme < 0) continue; // 2026-10-01: 매핑 없는 테마(DeathHarvest 등)는 피 효과를 새로 붙이지 않고 그대로 둔다(중간 저장 뒤 예외 방지).
            string backup = Path.GetFullPath("../개인파일/코덱스산출/CombatVfx/20260923_BloodGoal/Before/" + path);
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if (!File.Exists(backup)) { File.Copy(path, backup); File.Copy(path + ".meta", backup + ".meta"); }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<CombatHealth>() == null) throw new Exception("Health must be on actor root " + path);
                // 2026-10-01: 이미 피 효과 프로필이 연결된 액터는 그대로 둔다(저장하지 않음).
                var target = root.GetComponent<BloodHitTarget>();
                var serialized = target != null ? new SerializedObject(target) : null;
                if (serialized == null || serialized.FindProperty("profile").objectReferenceValue == null)
                {
                    if (target == null) { target = root.AddComponent<BloodHitTarget>(); serialized = new SerializedObject(target); }
                    serialized.FindProperty("profile").objectReferenceValue = profiles[theme];
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                count++;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        // 2026-10-01: 전체 SaveAssets 대신 이 도구의 프로필·카탈로그만 저장(프리팹은 SaveAsPrefabAsset이 저장)
        foreach (var profile in profiles) AssetDatabase.SaveAssetIfDirty(profile);
        AssetDatabase.SaveAssetIfDirty(catalog);
        return "Configured " + count + " theme actors; 4 palettes; 3 Low graphs.";
    }
    private static T Get<T>(string path) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }
}
