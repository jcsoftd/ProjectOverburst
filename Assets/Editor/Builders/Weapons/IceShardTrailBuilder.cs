using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class IceShardTrailBuilder
{
    public const string PrefabPath = "Assets/ProjectOverburst/03_Features/Weapons/Shared/VFX/WeaponEffects2Variants/PF_WeaponIceShardTrail.prefab";
    public const string ProfilePath = "Assets/ProjectOverburst/Resources/Weapons/GreatswordElementFxProfile.asset";
    public const string ShatterPath = "Assets/ProjectOverburst/02_Shared/Combat/ElementalReactions/VFX/Prefabs/FreezeShatter/PF_VFX_Reaction_Shatter_Proc.prefab";

    public static void Build(string directory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle EditMode required.");
        directory = IsolatedSavePlayGuard.ValidateDirectory(directory);
        Directory.CreateDirectory(directory);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ShatterPath);
        var flecks = source.GetComponentsInChildren<ParticleSystem>(true).Single(p => p.name == "Flecks_Clipped_Alpha");
        var profile = AssetDatabase.LoadAssetAtPath<GreatswordElementFxProfile>(ProfilePath);
        string profileFile = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ProfilePath));
        var beforeProfile = StableProfileLines(profileFile);
        string profileGuid = AssetDatabase.AssetPathToGUID(ProfilePath);
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        try
        {
            root = new GameObject("PF_WeaponIceShardTrail"); root.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var child = UnityEngine.Object.Instantiate(flecks.gameObject, root.transform, false);
            child.name = "ShatterFragments";
            child.transform.localPosition = Vector3.zero; child.transform.localRotation = Quaternion.identity; child.transform.localScale = Vector3.one;
            foreach (var script in child.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(script);
            var particles = child.GetComponent<ParticleSystem>();
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.loop = true; main.duration = 3f; main.playOnAwake = false; main.startDelay = 0f;
            main.startSpeed = 0f; main.gravityModifier = .015f;
            main.startSize3D = false; main.startSize = new ParticleSystem.MinMaxCurve(.045f, .135f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(.32f, .44f); main.maxParticles = 184;
            main.startRotation3D = false; main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = Color.white;
            var emission = particles.emission; emission.enabled = false; emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
            var shape = particles.shape; shape.enabled = false;
            var velocity = particles.velocityOverLifetime; velocity.enabled = false;
            var force = particles.forceOverLifetime; force.enabled = false;
            var inherit = particles.inheritVelocity; inherit.enabled = false;
            var external = particles.externalForces; external.enabled = false;
            var noise = particles.noise; noise.enabled = false;
            var collision = particles.collision; collision.enabled = false;
            var size = particles.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, .65f));
            var gradient = new Gradient(); Color color = flecks.main.startColor.color;
            gradient.SetKeys(new[] { new GradientColorKey(color, 0), new GradientColorKey(color, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.9f, .45f), new GradientAlphaKey(0, 1) });
            var overColor = particles.colorOverLifetime; overColor.enabled = true; overColor.color = new ParticleSystem.MinMaxGradient(gradient);
            particles.useAutoRandomSeed = false; particles.randomSeed = 17;
            var controller = root.AddComponent<WeaponIceShardTrail>();
            var binding = new SerializedObject(controller); binding.FindProperty("particles").objectReferenceValue = particles;
            binding.ApplyModifiedPropertiesWithoutUndo();
            child.SetActive(true); root.SetActive(true);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (prefab == null) throw new InvalidOperationException("Shard prefab save failed.");
            var serialized = new SerializedObject(profile); serialized.FindProperty("iceShardTrail").objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
            if (!beforeProfile.SequenceEqual(StableProfileLines(profileFile)) || profileGuid != AssetDatabase.AssetPathToGUID(ProfilePath))
                throw new InvalidOperationException("Unrelated profile values changed.");
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab) != 0 || profile.IceShardTrail != prefab)
                throw new InvalidOperationException("Saved shard binding invalid.");
            File.WriteAllText(Path.Combine(directory, "Builder.json"), JsonConvert.SerializeObject(new {
                status = "PASS", prefab = PrefabPath, profile = ProfilePath, profileGuid,
                prefabGuid = AssetDatabase.AssetPathToGUID(PrefabPath), sourceMaterial = AssetDatabase.GetAssetPath(flecks.GetComponent<Renderer>().sharedMaterial),
                unrelatedProfileValuesPreserved = true
            }, Formatting.Indented));
        }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    // Compare saved values, rather than transient instance IDs which change during imports.
    static string[] StableProfileLines(string path) => File.ReadAllLines(path)
        .Where(line => !line.TrimStart().StartsWith("iceShardTrail:", StringComparison.Ordinal)).ToArray();

    public static void ValidateSaved(string directory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle EditMode required.");
        directory = IsolatedSavePlayGuard.ValidateDirectory(directory);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var profile = AssetDatabase.LoadAssetAtPath<GreatswordElementFxProfile>(ProfilePath);
        if (prefab == null || profile == null || profile.IceShardTrail != prefab || prefab.GetComponent<WeaponIceShardTrail>() == null)
            throw new InvalidOperationException("Saved shard binding missing.");
        var controller = new SerializedObject(prefab.GetComponent<WeaponIceShardTrail>());
        var particles = controller.FindProperty("particles").objectReferenceValue as ParticleSystem;
        if (particles == null || particles.main.simulationSpace != ParticleSystemSimulationSpace.World ||
            particles.main.scalingMode != ParticleSystemScalingMode.Shape || particles.main.maxParticles != 184 || particles.emission.enabled ||
            prefab.GetComponentsInChildren<Transform>(true).Any(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0))
            throw new InvalidOperationException("Saved shard modules invalid.");
        string backup = Path.Combine(directory, "Before", ProfilePath);
        string current = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ProfilePath));
        bool preserved = File.Exists(backup) && StableProfileLines(backup).SequenceEqual(StableProfileLines(current));
        if (!preserved) throw new InvalidOperationException("Unrelated saved profile values changed.");
        File.WriteAllText(Path.Combine(directory, "Builder.json"), JsonConvert.SerializeObject(new {
            status = "PASS", prefab = PrefabPath, profile = ProfilePath,
            prefabGuid = AssetDatabase.AssetPathToGUID(PrefabPath), profileGuid = AssetDatabase.AssetPathToGUID(ProfilePath),
            sourceMaterial = AssetDatabase.GetAssetPath(particles.GetComponent<Renderer>().sharedMaterial),
            unrelatedProfileValuesPreserved = true, validatedExistingSavedAsset = true
        }, Formatting.Indented));
    }
}
