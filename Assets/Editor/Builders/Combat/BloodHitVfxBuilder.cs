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
        catalog.slash = BloodHitGraphBuilder.Create("Slash");
        catalog.stab = BloodHitGraphBuilder.Create("Stab");
        catalog.burst = BloodHitGraphBuilder.Create("Burst");
        if (catalog.slash == null || catalog.stab == null || catalog.burst == null) throw new Exception("Restore URP vendor pack first.");
        catalog.lifetime = 2.5f;
        EditorUtility.SetDirty(catalog);
        int count = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/ProjectOverburst/Resources/Enemies/Themes/Actors" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            int theme = Array.FindIndex(themes, x => path.Contains(x));
            if (theme < 0) throw new Exception("Unmapped actor " + path);
            string backup = Path.GetFullPath("../개인파일/코덱스산출/CombatVfx/20260923_BloodGoal/Before/" + path);
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if (!File.Exists(backup)) { File.Copy(path, backup); File.Copy(path + ".meta", backup + ".meta"); }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<CombatHealth>() == null) throw new Exception("Health must be on actor root " + path);
                var target = root.GetComponent<BloodHitTarget>() ?? root.AddComponent<BloodHitTarget>();
                var serialized = new SerializedObject(target);
                serialized.FindProperty("profile").objectReferenceValue = profiles[theme];
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                count++;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
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
