using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class PlayerScreenSpaceVfxBuilder
{
    public const string PrefabPath = "Assets/ProjectOverburst/Resources/Combat/VFX/PF_PlayerScreenSpaceVfx.prefab";
    public const string SourceRoot = "Assets/ThirdParty/06_VFX/Piloto Studio/ScreenSpaceFX/Prefabs/";
    public static string Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle EditMode required");
        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject root = null, source = null;
        try
        {
            root = new GameObject("Player Screen Space VFX");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var presenter = root.AddComponent<PlayerScreenSpaceVfx>();
            var serialized = new SerializedObject(presenter);
            var names = new[] { "Blood_Quick", "Healing_Infinity", "Blood_Infinity" };
            var fields = new[] { "damage", "healing", "lowHealth" };
            for (int i = 0; i < names.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourceRoot + names[i] + ".prefab");
                if (prefab == null) throw new InvalidOperationException("Missing approved source " + names[i]);
                source = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var visible = source.GetComponentsInChildren<ParticleSystem>(true).Single(p => p.emission.enabled);
                var child = UnityEngine.Object.Instantiate(visible.gameObject, root.transform, false);
                child.name = names[i];
                child.transform.localPosition = Vector3.forward;
                var effect = child.GetComponent<ParticleSystem>();
                var main = effect.main; main.playOnAwake = false; main.loop = false;
                main.maxParticles = 1; main.startDelay = 0f; main.useUnscaledTime = true;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                main.stopAction = ParticleSystemStopAction.None;
                main.startLifetime = i == 0 ? PlayerScreenSpaceVfx.DamageDuration : float.PositiveInfinity;
                // The low-HP source expires after .5s; retain its mature border until the presenter clears it.
                if (i == 2)
                {
                    var data = effect.customData;
                    var erosion = data.GetVector(ParticleSystemCustomData.Custom1, 1);
                    data.SetVector(ParticleSystemCustomData.Custom1, 1, erosion.Evaluate(.8f));
                }
                var renderer = child.GetComponent<ParticleSystemRenderer>();
                renderer.enabled = false; renderer.maxParticleSize = 2f;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                renderer.sortingOrder = 2000 + (i == 0 ? 2 : i == 1 ? 1 : 0);
                serialized.FindProperty(fields[i]).objectReferenceValue = effect;
                UnityEngine.Object.DestroyImmediate(source); source = null;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
            return PrefabPath;
        }
        finally
        {
            if (source != null) UnityEngine.Object.DestroyImmediate(source);
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
