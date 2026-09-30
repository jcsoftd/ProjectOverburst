using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Local authoring tool: regenerate after changing the shared source or element modules.
public static class CombatElementHitPoolBuilder
{
    private const string Folder = "Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/VFX/ElementHit/RuntimePools";
    private static readonly WeaponElement[] Elements = { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light };
    private static readonly string[] Fields = { "fireHit", "iceHit", "electricHit", "darkHit", "lightHit" };
    private static readonly int[] WarmCounts = { 768, 1280, 1024, 768, 384 };

    [MenuItem("OVERBURST/Build/Element Hit Runtime Pools")]
    public static void BuildMenu() => Build();

    public static string Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Build in Edit mode.");
        var catalog = Resources.Load<MeleeElementHitVfxCatalog>(MeleeElementHitVfxCatalog.ResourcePath);
        if (catalog == null || catalog.sharedHitPrefab == null) throw new InvalidOperationException("Missing authoring catalog.");
        if (EditorUtility.IsDirty(catalog)) throw new InvalidOperationException("Catalog has unsaved edits; preserve them before rebuilding runtime entries.");
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder(Folder.Substring(0, Folder.LastIndexOf('/')), "RuntimePools");
        var entries = new MeleeElementHitVfxCatalog.RuntimePool[Elements.Length];
        var report = new List<string>();
        for (int i = 0; i < Elements.Length; i++)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(catalog.sharedHitPrefab, scene);
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                root.SetActive(false);
                var controller = root.GetComponent<MeleeElementHitVfxController>();
                if (controller == null || controller.GetElementObject(Elements[i]) == null)
                    throw new InvalidOperationException("Missing module " + Elements[i]);
                for (int j = 0; j < Elements.Length; j++)
                    if (j != i && controller.GetElementObject(Elements[j]) != null)
                        UnityEngine.Object.DestroyImmediate(controller.GetElementObject(Elements[j]));
                var serialized = new SerializedObject(controller);
                for (int j = 0; j < Fields.Length; j++)
                    if (i != j) serialized.FindProperty(Fields[j]).objectReferenceValue = null;
                serialized.FindProperty("selectedElement").intValue = (int)Elements[i];
                serialized.ApplyModifiedPropertiesWithoutUndo();
                controller.SetElement(Elements[i]);
                root.name = "PF_VFX_MeleeElementHit_" + Elements[i] + "_Runtime";
                root.SetActive(true);
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                    if (component == null) throw new InvalidOperationException("Missing script in " + root.name);
                string path = Folder + "/" + root.name + ".prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                if (prefab == null) throw new IOException("Prefab save failed: " + path);
                entries[i] = new MeleeElementHitVfxCatalog.RuntimePool { element = Elements[i], prefab = prefab, prewarmCount = WarmCounts[i] };
                report.Add(Elements[i] + " particles=" + prefab.GetComponentsInChildren<ParticleSystem>(true).Length + " warm=" + WarmCounts[i]);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        catalog.runtimePools = entries;
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssetIfDirty(catalog);
        string text = string.Join("\n", report);
        string output = Path.GetFullPath("../개인파일/코덱스산출/Combat/Scale100Implementation20260927");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "RuntimePoolBuild.txt"), text);
        return text;
    }
}
