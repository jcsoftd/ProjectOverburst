using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Local-only authoring tool. The 21 project-owned theme prefabs are the exact batch boundary.
public static class EnemyHitResponsePrefabBuilder
{
    private const string ActorFolder = "Assets/ProjectOverburst/Resources/Enemies/Themes/Actors";

    [MenuItem("OVERBURST/Enemies/Poise/Attach Hit Response Coordinator")]
    public static void Attach()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Poise authoring requires Edit Mode.");

        string[] files = Directory.GetFiles(ActorFolder, "*.prefab", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.Ordinal);
        if (files.Length != 21)
            throw new InvalidOperationException($"Expected exactly 21 theme actors, found {files.Length}.");

        // Validate the complete batch before changing any prefab.
        foreach (string path in files)
        {
            string assetPath = path.Replace('\\', '/');
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null || prefab.GetComponent<EnemyActor>() == null
                || prefab.GetComponent<CombatHealth>() == null
                || prefab.GetComponent<EnemyRank>() == null
                || prefab.GetComponent<EnemyMovementReaction>() == null
                || prefab.GetComponent<EnemyAnimationBridge>() == null
                || prefab.GetComponent<EnemyAbilityController>() == null
                || prefab.GetComponent<EnemyMeleeAttackController>() == null)
                throw new InvalidOperationException($"Missing theme actor contract: {assetPath}");
        }

        int modified = 0;
        foreach (string path in files)
        {
            string assetPath = path.Replace('\\', '/');
            GameObject root = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                if (root.GetComponent<EnemyHitResponseCoordinator>() != null)
                    continue;
                root.AddComponent<EnemyHitResponseCoordinator>();
                if (PrefabUtility.SaveAsPrefabAsset(root, assetPath) == null)
                    throw new InvalidOperationException($"Prefab save failed: {assetPath}");
                modified++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[EnemyHitResponsePrefabBuilder] PASS: 21 theme actors, modified={modified}");
    }
}
