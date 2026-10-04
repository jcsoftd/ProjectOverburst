using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GroundIndicatorBuilder
{
    public const string Root = "Assets/ProjectOverburst/Resources/Combat/Indicators";
    public const string PrefabPath = Root + "/PF_Indicator_Sector.prefab";
    public const string SourceGuid = "a078c1a32abbe584393e91050e2dd214";
    public const string ConeGuid = "55a51c403edec12489b9aaf003b60ce5";
    public const string RectangleGuid = "bdf9b89da77863a4785bc0693ffbbe64";

    [MenuItem("JC Tool/VFX/인디케이터/기본 프리팹 생성")]
    public static void Build() => Build(false);
    public static void Build(bool rebuildExisting)
    {
        RequireIdle(); EnsureRadialMeshReadable(); EnsureFolder(Root);
        var cone = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(ConeGuid));
        var nova = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(SourceGuid));
        var rectangle = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(RectangleGuid));
        if (cone == null || nova == null || rectangle == null) throw new InvalidOperationException("Original Telegraph sources are missing.");
        foreach (GroundIndicatorShape kind in Enum.GetValues(typeof(GroundIndicatorShape)))
        {
            string path = Root + "/PF_Indicator_" + kind + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && EditorUtility.IsDirty(existing)) throw new InvalidOperationException("Unsaved indicator: " + path);
            if (!rebuildExisting && existing != null && existing.GetComponent<ProceduralGroundIndicator>()?.UsesApprovedDesign == true) continue;
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("PF_Indicator_" + kind);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                var indicator = root.AddComponent<ProceduralGroundIndicator>();
                indicator.SetVisible(false);
                indicator.InitializeSources(cone, nova, rectangle);
                indicator.Configure(kind, 4f, kind == GroundIndicatorShape.Sector ? .72f : kind == GroundIndicatorShape.Donut ? 2f : 0f, 80f, 2f, 4f);
                indicator.ReleaseRuntime();
                var saved = PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
                if (!ok || saved == null) throw new InvalidOperationException("Prefab save failed: " + path);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
    // RadialMesh reads the supplier's authored UV rings during Play.
    public static void EnsureRadialMeshReadable()
    {
        RequireIdle();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(SourceGuid));
        if (source == null) throw new InvalidOperationException("Original Nova source is missing.");
        var paths = new HashSet<string>();
        foreach (var renderer in source.GetComponentsInChildren<ParticleSystemRenderer>(true))
        {
            if (!renderer.name.Contains("fill_add_soft") && !renderer.name.Contains("border_add_soft")) continue;
            if (renderer.mesh == null) throw new InvalidOperationException("Nova radial mesh is missing.");
            string path = AssetDatabase.GetAssetPath(renderer.mesh);
            if (!paths.Add(path)) continue;
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Nova radial mesh must be an imported model: " + path);
            if (importer.isReadable) continue;
            importer.isReadable = true;
            importer.SaveAndReimport();
        }
        if (paths.Count == 0) throw new InvalidOperationException("Nova fill and border meshes are missing.");
    }
    public static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Edit mode required.");
    }
    public static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent); AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
