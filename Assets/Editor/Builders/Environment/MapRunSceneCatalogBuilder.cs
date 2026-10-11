using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

public static class MapRunSceneCatalogBuilder
{
    // Registers selected existing scenes only. Does not bake terrain or run a Player build.
    public static int RegisterSelectedScenes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Use an idle Editor.");
        var required = new List<string> { MapTerrainCatalog.CaveScene };
        required.AddRange(MapTerrainCatalog.DesertCandidates.Select(c => c.scene));
        foreach (var path in required)
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(path)) throw new InvalidOperationException("Missing selected map scene: " + path);
        var scenes = EditorBuildSettings.scenes.ToList();
        int changed = 0;
        foreach (var path in required)
        {
            var existing = scenes.Find(s => s.path == path);
            if (existing != null) { if (!existing.enabled) { existing.enabled = true; changed++; } }
            else { scenes.Add(new EditorBuildSettingsScene(path, true)); changed++; }
        }
        if (changed > 0) EditorBuildSettings.scenes = scenes.ToArray();
        // ProjectSettings are not AssetDatabase assets; SaveAssetIfDirty does not persist this list.
        var settings = UnityEngine.Resources.FindObjectsOfTypeAll<EditorBuildSettings>().Single();
        if (EditorUtility.IsDirty(settings))
        {
            var copy = UnityEngine.Object.Instantiate(settings);
            try
            {
                UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(
                    new UnityEngine.Object[] { copy }, "ProjectSettings/EditorBuildSettings.asset", true);
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }
        return changed;
    }
}
