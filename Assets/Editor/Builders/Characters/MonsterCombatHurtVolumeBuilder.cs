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
    // 2026-10-01 판정 전수조사 개정: 몸을 70% 미만 덮던 16종은 반경을 몸 정점 90% 반경까지(기존의 1.4배 상한),
    // 윗면을 몸 꼭대기까지 키웠다. 중심 z는 기존 값을 유지했고 줄인 값은 없다.
    private static readonly Spec[] Specs =
    {
        new Spec("SpiderBrood_RostrokarckLarvae", .29f, -.05f, .56f, .65f),
        new Spec("SpiderBrood_Horridomorph", .27f, .03f, .50f, .57f),
        new Spec("SpiderBrood_Scolokarck_Tint3", .92f, -.05f, 1.30f, 1.80f),
        new Spec("SpiderBrood_Carcinoptera", .95f, .10f, .90f, 1.80f),
        new Spec("SpiderBrood_Rostrokarck", 1.27f, .22f, 2.05f, 2.55f),
        new Spec("VenomBrood_Venodonte_Tint1", .23f, .08f, .38f, .50f),
        new Spec("VenomBrood_Venodonte_Tint3", .23f, .08f, .38f, .50f),
        new Spec("VenomBrood_Arathrox", 1.04f, -.04f, 1.25f, 2.08f),
        new Spec("VenomBrood_Kupolojuve_Tint_Orange", 1.04f, .28f, .85f, 2.09f),
        new Spec("VenomBrood_Kupolobrach_Tint_Orange", 1.89f, .48f, 1.90f, 3.60f),
        new Spec("PrimalHunt_Caniathrox", .27f, 0f, .48f, .56f),
        new Spec("PrimalHunt_Dimaxillosaurus", 1.05f, .03f, 1.03f, 2.15f),
        new Spec("PrimalHunt_Venosaur_Tint_Brown", .89f, .02f, 1.10f, 1.82f),
        new Spec("PrimalHunt_Occisodonte", 1.17f, .32f, 2.30f, 2.40f),
        new Spec("CavernMutants_Ceratoferox", .42f, .04f, .35f, .79f),
        new Spec("CavernMutants_Cephalonops", .30f, .06f, .43f, .62f),
        new Spec("CavernMutants_Gasterobrach", 1.05f, -.10f, 1.15f, 2.16f),
        new Spec("CavernMutants_Limadon", .49f, .10f, 1.10f, .89f),
        new Spec("CavernMutants_Gorhorrid", 1.01f, .36f, 1.00f, 2.03f),
        new Spec("CavernMutants_Ursacetus", 1.85f, -.28f, 1.68f, 3.55f),
        // 2026-10-01: 전용 판정 없이 물리 몸체(반경 0.30)를 그대로 판정으로 쓰던 4종.
        new Spec("DeathHarvest_DeathKnight", .92f, 0f, .45f, 1.81f),
        new Spec("DeathHarvest_RakeBrute", 1.01f, 0f, .45f, 1.98f),
        new Spec("DeathHarvest_RakeSkulker", .70f, 0f, .35f, 1.37f),
        new Spec("DeathHarvest_RakeStalker", .71f, 0f, .35f, 1.37f)
    };

    // 화상 불이 몸 위 허공에서 나오던 몬스터의 불 위치 보정(몬스터 방향 기준, m).
    // 정상 몬스터들의 불 시작 높이 가운데 값(몸 높이의 93%)에 맞췄다.
    private static readonly (string Id, Vector3 Offset)[] BurnOffsets =
    {
        ("SpiderBrood_Carcinoptera", new Vector3(0f, -.45f, 0f)),
        ("SpiderBrood_RostrokarckLarvae", new Vector3(0f, -.10f, 0f)),
        ("SpiderBrood_Scolokarck_Tint3", new Vector3(0f, -.24f, 0f))
    };

    // 2026-10-01: 이미 저장된 전투 피격 볼륨은 조정값으로 보존한다. 표의 볼륨은 아직 볼륨이 없는 액터의 기본값이다.
    [MenuItem("OVERBURST/Enemies/Themes/Apply Combat Hurt Volumes (Missing Only)")]
    public static void Apply() => ApplyTable(false);

    // 표를 고친 뒤 한 번 반영할 때만 쓴다. 표에 있는 액터의 저장된 판정과 화상 불 보정을 표 값으로 덮어쓴다.
    [MenuItem("OVERBURST/Enemies/Themes/Overwrite Combat Hurt Volumes From Table")]
    public static void Overwrite() => ApplyTable(true);

    private static void ApplyTable(bool overwrite)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before changing prefabs.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        int applied = 0, preserved = 0;
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

                if (!overwrite && target.HasCustomHurtVolume) { preserved++; continue; }
                target.RefreshVolumeFromCollider(body);
                if (spec.Radius < target.CurrentVolume.Radius || spec.Height < target.CurrentVolume.HalfHeight * 2f * .45f)
                    throw new InvalidOperationException("Hurt volume smaller than body: " + path);
                target.ConfigureHurtVolume(spec.Center, spec.Radius, spec.Height);
                EditorUtility.SetDirty(target);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                applied++;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        int burns = overwrite ? ApplyBurnOffsets() : 0;
        Debug.Log($"[MonsterCombatHurtVolumeBuilder] applied={applied} preserved={preserved} burnOffsets={burns}; body colliders and crowd profiles unchanged.");
    }

    private static int ApplyBurnOffsets()
    {
        int applied = 0;
        foreach (var (id, offset) in BurnOffsets)
        {
            string path = $"{ActorFolder}/PF_{id}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                throw new InvalidOperationException("Actor missing: " + path);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var placement = root.GetComponent<CombatTargetVfxPlacement>();
                if (placement == null)
                    throw new InvalidOperationException("VFX placement missing: " + path);
                var serialized = new SerializedObject(placement);
                serialized.FindProperty("burnOffset").vector3Value = offset;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                applied++;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        return applied;
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
                || Mathf.Abs(hurt.HalfHeight * 2f - spec.Height) > .001f
                || Vector3.Distance(prefab.transform.InverseTransformPoint(hurt.Center), spec.Center) > .002f)
                throw new InvalidOperationException("Mismatch: " + path);
        }
        foreach (var (id, offset) in BurnOffsets)
        {
            string path = $"{ActorFolder}/PF_{id}.prefab";
            var placement = AssetDatabase.LoadAssetAtPath<GameObject>(path)?.GetComponent<CombatTargetVfxPlacement>();
            if (placement == null)
                throw new InvalidOperationException("VFX placement missing: " + path);
            Vector3 saved = new SerializedObject(placement).FindProperty("burnOffset").vector3Value;
            if ((saved - offset).sqrMagnitude > 1e-6f)
                throw new InvalidOperationException("Burn offset mismatch: " + path);
        }
        Debug.Log($"[MonsterCombatHurtVolumeBuilder] PASS: {Specs.Length} authored volumes, {BurnOffsets.Length} burn offsets.");
    }
}
