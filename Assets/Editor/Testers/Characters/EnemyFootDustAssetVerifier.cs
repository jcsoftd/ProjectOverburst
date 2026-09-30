using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class EnemyFootDustAssetVerifier
{
    private const string ThemeRoot = "Assets/ProjectOverburst/Resources/Enemies/Themes";
    private const string DustRoot = "Assets/ProjectOverburst/Resources/Enemies/FootDust";

    [MenuItem("OVERBURST/Enemies/Foot Dust/Verify Assets")]
    public static void Verify()
    {
        var failures = new List<string>();
        string[] names = { "Light", "Standard", "Heavy" };
        Material shared = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/ProjectOverburst/03_Features/Enemies/VFX/FootDust/MAT_EnemyFootDust_Unlit.mat");
        if (shared == null || shared.GetTexture("_BaseMap") == null)
            failures.Add("missing shared material or texture");
        foreach (string name in names)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                DustRoot + "/PF_EnemyFootDust_" + name + ".prefab");
            ParticleSystem system = prefab != null ? prefab.GetComponent<ParticleSystem>() : null;
            if (system == null) { failures.Add("missing particle prefab: " + name); continue; }
            ParticleSystemRenderer renderer = prefab.GetComponent<ParticleSystemRenderer>();
            if (renderer == null || renderer.sharedMaterial != shared
                || system.main.simulationSpace != ParticleSystemSimulationSpace.World
                || system.main.cullingMode != ParticleSystemCullingMode.AlwaysSimulate
                || system.emission.enabled || system.main.maxParticles < 96)
                failures.Add("invalid shared particle setup: " + name);
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab) != 0)
                failures.Add("missing script on particle prefab: " + name);
        }

        EnemyDefinition[] definitions = AssetDatabase.FindAssets("t:EnemyDefinition",
                new[] { ThemeRoot + "/Definitions" })
            .Select(guid => AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(definition => definition != null).ToArray();
        if (definitions.Length != 21) failures.Add("expected 21 theme definitions: " + definitions.Length);
        int detailed = 0;
        foreach (EnemyDefinition definition in definitions)
        {
            string id = definition.EnemyId;
            EnemyFootfallProfile profile = AssetDatabase.LoadAssetAtPath<EnemyFootfallProfile>(
                ThemeRoot + "/Footfalls/" + id + ".asset");
            if (profile == null || !profile.IsValid || !profile.HasDetailedContacts
                || profile.EnemyId != id || profile.LocomotionClip != definition.AnimationProfile.Walk
                || profile.RunClip != definition.AnimationProfile.Run)
            {
                failures.Add("missing/invalid contact profile: " + id);
                continue;
            }
            detailed++;
            EnemyHitWeightProfile weight = definition.MovementProfile?.HitWeightProfile;
            if (weight != null && profile.VisualWeight != weight.Weight)
                failures.Add("visual weight mismatch: " + id);
            foreach (bool running in new[] { false, true })
            {
                float previous = -1f;
                int count = profile.GetContactCount(running);
                if (count < 2 || count > 6) failures.Add("contact count: " + id);
                for (int i = 0; i < count; i++)
                {
                    EnemyFootfallContact contact = profile.GetContact(running, i);
                    Vector3 point = contact.LocalPosition;
                    if (contact.Phase <= previous || contact.Phase < 0f || contact.Phase >= 1f
                        || float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z)
                        || point.sqrMagnitude > 100f)
                        failures.Add("invalid contact: " + id + " / " + running + " / " + i);
                    previous = contact.Phase;
                }
            }
        }

        string[] eliteIds =
        {
            "SpiderBrood_Rostrokarck", "VenomBrood_Kupolobrach_Tint_Orange",
            "PrimalHunt_Occisodonte", "CavernMutants_Ursacetus"
        };
        foreach (string id in eliteIds)
        {
            EnemyDefinition definition = definitions.FirstOrDefault(candidate => candidate.EnemyId == id);
            EnemyEliteFootstepEmitter emitter = definition?.ActorPrefab != null
                ? definition.ActorPrefab.GetComponent<EnemyEliteFootstepEmitter>() : null;
            EnemyFootfallProfile profile = AssetDatabase.LoadAssetAtPath<EnemyFootfallProfile>(
                ThemeRoot + "/Footfalls/" + id + ".asset");
            if (emitter == null || emitter.Profile != profile)
                failures.Add("elite emitter profile reference: " + id);
        }

        string result = failures.Count == 0 ? "PASS" : "FAIL";
        string report = "result=" + result + "\nprofiles=" + detailed + "/21\n"
            + "particlePrefabs=3\n" + string.Join("\n", failures);
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..",
            "개인파일", "코덱스산출", "MonsterFootDust", "20260923_111748",
            "asset-verification.txt"));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, report);
        if (failures.Count > 0) throw new InvalidOperationException(report);
        Debug.Log("[EnemyFootDustAssetVerifier] " + report);
    }
}
