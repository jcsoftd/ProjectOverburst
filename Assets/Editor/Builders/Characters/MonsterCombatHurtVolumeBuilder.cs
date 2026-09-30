using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Local authoring tool. Body collision and crowd radius are never enlarged here.
public static class MonsterCombatHurtVolumeBuilder
{
    private const string ActorFolder = "Assets/ProjectOverburst/Resources/Enemies/Themes/Actors";

    private readonly struct Spec
    {
        public readonly string Id;
        public readonly Vector3 Center;
        public readonly float Radius;
        public readonly float Height;

        public Spec(string id, float y, float z, float radius, float height)
        {
            Id = id;
            Center = new Vector3(0f, y, z);
            Radius = radius;
            Height = height;
        }
    }

    // Hand-tuned core silhouettes. Tail, wing and feeler tips are deliberately excluded.
    private static readonly Spec[] Specs =
    {
        new Spec("SpiderBrood_RostrokarckLarvae", .29f, -.05f, .56f, .65f),
        new Spec("SpiderBrood_Horridomorph", .26f, .03f, .34f, .54f),
        new Spec("SpiderBrood_Scolokarck_Tint3", .92f, -.05f, .95f, 1.80f),
        new Spec("SpiderBrood_Carcinoptera", .95f, .10f, .90f, 1.80f),
        new Spec("SpiderBrood_Rostrokarck", .95f, .22f, 1.45f, 1.90f),
        new Spec("VenomBrood_Venodonte_Tint1", .23f, .08f, .38f, .50f),
        new Spec("VenomBrood_Venodonte_Tint3", .23f, .08f, .38f, .50f),
        new Spec("VenomBrood_Arathrox", .98f, -.04f, .88f, 1.95f),
        new Spec("VenomBrood_Kupolojuve_Tint_Orange", .85f, .28f, .68f, 1.70f),
        new Spec("VenomBrood_Kupolobrach_Tint_Orange", 1.82f, .48f, 1.35f, 3.45f),
        new Spec("PrimalHunt_Caniathrox", .27f, 0f, .48f, .56f),
        new Spec("PrimalHunt_Dimaxillosaurus", 1.05f, .03f, 1.03f, 2.15f),
        new Spec("PrimalHunt_Venosaur_Tint_Brown", .88f, .02f, .87f, 1.80f),
        new Spec("PrimalHunt_Occisodonte", 1.02f, .32f, 1.85f, 2.10f),
        new Spec("CavernMutants_Ceratoferox", .30f, .04f, .33f, .56f),
        new Spec("CavernMutants_Cephalonops", .30f, .06f, .43f, .62f),
        new Spec("CavernMutants_Gasterobrach", 1.00f, -.10f, .98f, 2.05f),
        new Spec("CavernMutants_Limadon", .50f, .10f, .78f, .85f),
        new Spec("CavernMutants_Gorhorrid", .80f, .36f, .70f, 1.60f),
        new Spec("CavernMutants_Ursacetus", 1.85f, -.28f, 1.68f, 3.55f)
    };

    [MenuItem("OVERBURST/Enemies/Themes/Apply Combat Hurt Volumes")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before changing prefabs.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Spec spec in Specs)
        {
            string path = $"{ActorFolder}/PF_{spec.Id}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null || !seen.Add(path))
                throw new InvalidOperationException("Actor missing or duplicated: " + path);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var actor = root.GetComponent<EnemyActor>();
                var target = root.GetComponent<CombatTarget>();
                var body = actor != null && actor.CollisionRoot != null
                    ? actor.CollisionRoot.GetComponentInChildren<CapsuleCollider>(true)
                    : null;
                if (target == null || body == null)
                    throw new InvalidOperationException("Body or target missing: " + path);

                target.RefreshVolumeFromCollider(body);
                if (spec.Radius < target.CurrentVolume.Radius || spec.Height < target.CurrentVolume.HalfHeight * 2f * .45f)
                    throw new InvalidOperationException("Hurt volume smaller than body: " + path);
                target.ConfigureHurtVolume(spec.Center, spec.Radius, spec.Height);
                EditorUtility.SetDirty(target);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[MonsterCombatHurtVolumeBuilder] Saved {seen.Count} authored hurt volumes; body colliders and crowd profiles unchanged.");
    }

    [MenuItem("OVERBURST/Enemies/Themes/Validate Combat Hurt Volumes")]
    public static void Validate()
    {
        foreach (Spec spec in Specs)
        {
            string path = $"{ActorFolder}/PF_{spec.Id}.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new InvalidOperationException("Missing: " + path);
            var target = prefab.GetComponent<CombatTarget>();
            if (target == null || !target.HasCustomHurtVolume)
                throw new InvalidOperationException("Unconfigured: " + path);
            CombatTargetVolume hurt = target.CurrentHurtVolume;
            if (Mathf.Abs(hurt.Radius - spec.Radius) > .001f
                || Mathf.Abs(hurt.HalfHeight * 2f - spec.Height) > .001f)
                throw new InvalidOperationException("Mismatch: " + path);
        }
        Debug.Log($"[MonsterCombatHurtVolumeBuilder] PASS: {Specs.Length} authored volumes.");
    }
}
