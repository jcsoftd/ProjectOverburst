using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CrustaspikanTemporaryReactionBuilder
{
    public const string PrefabPath = CrustaspikanMaterialBuilder.Root + "/PF_CrustaspikanMaterials.prefab";
    public static string Apply(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorUtility.scriptCompilationFailed
            || IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)))
            throw new InvalidOperationException("Idle, unoccupied Editor required.");
        var collection = AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(CrustaspikanMaterialBuilder.CollectionPath);
        if (collection?.FindMotion("Death")?.IsPlayable != true || collection.FindMotion("IdleBreathe")?.IsPlayable != true)
            throw new InvalidOperationException("Non-RM death and idle clips are required.");
        var getUp = CrustaspikanGetUpBuilder.Build(Path.Combine(Path.GetDirectoryName(output), "getup-build.json"));
        string guid = AssetDatabase.AssetPathToGUID(PrefabPath);
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            if (root.GetComponent<CrustaspikanTemporaryReaction>() == null) root.AddComponent<CrustaspikanTemporaryReaction>();
            var tuning = new SerializedObject(root.GetComponent<CrustaspikanTemporaryReaction>());
            tuning.FindProperty("rewindSeconds").floatValue = .7f;
            tuning.FindProperty("reboundHoldSeconds").floatValue = .14f; tuning.FindProperty("collapseBlendSeconds").floatValue = .32f;
            tuning.FindProperty("deathProneStart").floatValue = .78f;
            tuning.FindProperty("collapseSeconds").floatValue = 1.05f; tuning.FindProperty("recoverySeconds").floatValue = CrustaspikanGetUpBuilder.Duration;
            tuning.FindProperty("getUpClip").objectReferenceValue = getUp;
            tuning.FindProperty("maximumRewindClipSeconds").floatValue = .45f; tuning.FindProperty("deathFallStart").floatValue = .42f;
            tuning.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var animator = prefab.GetComponentInChildren<Animator>(true);
        var result = new JObject { ["status"] = "APPLIED", ["guidPreserved"] = guid == AssetDatabase.AssetPathToGUID(PrefabPath),
            ["component"] = prefab.GetComponent<CrustaspikanTemporaryReaction>() != null,
            ["rewindSeconds"] = new SerializedObject(prefab.GetComponent<CrustaspikanTemporaryReaction>()).FindProperty("rewindSeconds").floatValue,
            ["rootMotion"] = animator.applyRootMotion,
            ["missingScripts"] = prefab.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)),
            ["deathState"] = collection.FindMotion("Death").state, ["deathLength"] = collection.FindMotion("Death").runtime.length,
            ["getUpClip"] = AssetDatabase.GetAssetPath(getUp), ["getUpLength"] = getUp.length,
            ["scenes"] = new JArray(Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i))
                .Select(s => new JObject { ["path"] = s.path, ["dirty"] = s.isDirty, ["roots"] = s.rootCount })) };
        Directory.CreateDirectory(Path.GetDirectoryName(output)); File.WriteAllText(output, result.ToString()); return result.ToString();
    }
}
