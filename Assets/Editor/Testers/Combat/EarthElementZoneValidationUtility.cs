#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class EarthElementZoneValidationUtility
{
    private const string Root = "Assets/ProjectOverburst/02_Shared/Combat/ElementalReactions/VFX/Prefabs/EarthZones";
    private const string CatalogPath = "Assets/ProjectOverburst/Resources/Combat/VFX/EarthZoneVfxCatalog.asset";
    private const string LegacyRoot = "Assets/Prefabs/VFX/Combat/ElementalReaction/EarthZones";
    private const string LegacyCatalogPath = "Assets/Resources/Combat/VFX/EarthZoneVfxCatalog.asset";

    [MenuItem("Tools/ProjectVTP/Combat/Validate Earth Element Zones")]
    public static void Run()
    {
        List<string> errors = new List<string>();
        ValidateIdentifiers(errors);
        ValidateSequenceContract(errors);
        ValidateDirectHitAndOverlapContract(errors);
        ValidateTuningAndFalloff(errors);
        ValidateWrappers(errors);
        if (errors.Count > 0)
            throw new InvalidOperationException("[EarthElementZoneValidation] " + string.Join("\n", errors));
        Debug.Log("[EarthElementZoneValidation] PASS - sequence/cap/tuning/falloff/wrapper/catalog 계약");
    }

    public static void RunOnceFromCommandLine()
    {
        Run();
    }

    private static void ValidateIdentifiers(List<string> errors)
    {
        Require((int)WeaponElement.Wind == 5, "Wind=5 예약 식별자 불일치", errors);
        Require((int)WeaponElement.Earth == 6, "Earth=6 식별자 불일치", errors);
        Require((int)WeaponElement.Nature == 7, "Nature=7 식별자 불일치", errors);
    }

    private static void ValidateSequenceContract(List<string> errors)
    {
        int[] fiveUniqueWithDuplicates = { 101, 102, 103, 104, 105, 101, 103 };
        EarthElementZoneRuntimeService.ResolveSpawnDecisionCountsForValidation(
            fiveUniqueWithDuplicates,
            out int basicCount,
            out int rollCount);
        Require(basicCount == 1, "1타수 기본 장판은 정확히 1개여야 함", errors);
        Require(rollCount == 5, "1타수 5대상은 추가 10% 판정 5회여야 함", errors);
        Require(EarthElementZoneTuning.MaximumZonesPerOwner == 3, "캐릭터별 cap=3 불일치", errors);
    }

    private static void ValidateTuningAndFalloff(List<string> errors)
    {
        Require(Mathf.Approximately(EarthElementZoneTuning.Lifetime, 6f), "장판 수명 6초 불일치", errors);
        Require(Mathf.Approximately(EarthElementZoneTuning.Radius, 1.75f), "장판 반경 1.75m 불일치", errors);
        Require(EarthElementZoneTuning.CrystalCount == 3, "결정 개수 3 불일치", errors);

        float radiusSquared = 100f;
        float[] normalizedDistances = { 0f, 0.15f, 0.25f, 0.35f, 0.45f, 0.55f, 0.65f, 0.75f, 1f };
        for (int i = 0; i < normalizedDistances.Length; i++)
        {
            float distanceSquared = normalizedDistances[i] * normalizedDistances[i] * radiusSquared;
            float actual = ElementalReactionRules.ResolveCircularAreaDamageMultiplier(distanceSquared, radiusSquared);
            float expected = Mathf.Lerp(
                1f,
                ElementalReactionRules.CircularAreaMinimumDamageMultiplier,
                Mathf.Clamp01(distanceSquared / radiusSquared));
            Require(Mathf.Approximately(actual, expected), "원형 피해 연속 감쇠 불일치 index=" + i, errors);
        }
        Require(Mathf.Approximately(
                ElementalReactionRules.ResolveCircularAreaDamageMultiplier(0f, radiusSquared),
                1f),
            "원형 피해 중심 배율은 100%여야 함", errors);
        Require(Mathf.Approximately(
                ElementalReactionRules.ResolveCircularAreaDamageMultiplier(radiusSquared, radiusSquared),
                0.3f),
            "원형 피해 가장자리 배율은 30%여야 함", errors);
        float before = ElementalReactionRules.ResolveCircularAreaDamageMultiplier(24.99f, radiusSquared);
        float after = ElementalReactionRules.ResolveCircularAreaDamageMultiplier(25.01f, radiusSquared);
        Require(before > after && Mathf.Abs(before - after) < 0.001f,
            "원형 피해 중간점 감쇠가 계단 없이 연속이어야 함", errors);
    }

    private static void ValidateDirectHitAndOverlapContract(List<string> errors)
    {
        GameObject source = new GameObject("EarthValidationSource");
        try
        {
            DamageInfo valid = new DamageInfo(
                10f,
                Vector3.zero,
                source,
                element: WeaponElement.Earth,
                sourceAttackSequenceId: 77);
            Require(EarthElementZoneRuntimeService.IsEligibleEarthDirectHit(valid, 10f, CombatTeam.Enemy),
                "유효 Earth 직접 Hit가 거부됨", errors);

            DamageInfo dot = valid;
            dot.isDamageOverTime = true;
            Require(!EarthElementZoneRuntimeService.IsEligibleEarthDirectHit(dot, 10f, CombatTeam.Enemy),
                "Earth DoT가 장판 생성 권한을 얻음", errors);
            DamageInfo noTrigger = valid;
            noTrigger.triggersOnHitEffects = false;
            Require(!EarthElementZoneRuntimeService.IsEligibleEarthDirectHit(noTrigger, 10f, CombatTeam.Enemy),
                "triggersOnHitEffects=false가 장판 생성 권한을 얻음", errors);
            Require(!EarthElementZoneRuntimeService.IsEligibleEarthDirectHit(valid, 0f, CombatTeam.Enemy),
                "0 실피해가 장판 생성 권한을 얻음", errors);
            Require(!EarthElementZoneRuntimeService.IsEligibleEarthDirectHit(valid, 10f, CombatTeam.PlayerParty),
                "몬스터가 아닌 대상이 장판 생성 권한을 얻음", errors);

            AttackPatternRuntimeData pattern = new AttackPatternRuntimeData(
                AttackAreaShape.Circle,
                AttackFillMode.RadialExpand,
                AttackFillDirection.LeftToRight,
                3f,
                360f,
                0f,
                0f,
                0f,
                2f,
                0f,
                null);
            AttackPatternBasis basis = new AttackPatternBasis(Vector3.zero, Vector3.forward);
            Require(EarthElementZoneRuntimeService.DoesAttackRangeOverlapZone(
                    pattern, basis, 1f, new Vector3(3.5f, 0f, 0f), 1f),
                "몬스터 없는 실제 공격범위와 장판 원 교차가 누락됨", errors);
            Require(!EarthElementZoneRuntimeService.DoesAttackRangeOverlapZone(
                    pattern, basis, 1f, new Vector3(5f, 0f, 0f), 1f),
                "범위 밖 장판이 교차로 판정됨", errors);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
        }
    }

    private static void ValidateWrappers(List<string> errors)
    {
        Require(!AssetDatabase.IsValidFolder(LegacyRoot), "이전 EarthZones 루트가 남아 있음", errors);
        Require(AssetDatabase.LoadMainAssetAtPath(LegacyCatalogPath) == null,
            "이전 EarthZoneVfxCatalog 경로가 남아 있음", errors);
        EarthZoneVfxCatalog catalog = AssetDatabase.LoadAssetAtPath<EarthZoneVfxCatalog>(CatalogPath);
        Require(catalog != null, "EarthZoneVfxCatalog 누락", errors);
        if (catalog == null)
            return;
        Require(catalog.Entries.Count == 7, "Earth wrapper catalog는 7슬롯이어야 함", errors);
        for (int i = 0; i < catalog.Entries.Count; i++)
        {
            EarthZoneVfxCatalogEntry entry = catalog.Entries[i];
            Require(entry != null && entry.Prefab != null, "catalog prefab 참조 누락 index=" + i, errors);
            if (entry == null || entry.Prefab == null)
                continue;

            Require(AssetDatabase.GetAssetPath(entry.Prefab).StartsWith(Root, StringComparison.Ordinal),
                "wrapper 경로가 EarthZones 밖임: " + entry.Prefab.name, errors);
            EarthZoneVfxAuthoring authoring = entry.Prefab.GetComponent<EarthZoneVfxAuthoring>();
            Require(authoring != null && authoring.Kind == entry.Kind, "authoring kind 불일치: " + entry.Prefab.name, errors);
            Transform content = entry.Prefab.transform.Find("VFX_CONTENT");
            Require(content != null && content.childCount == 0, "VFX_CONTENT는 비어 있어야 함: " + entry.Prefab.name, errors);
            Require(HasNoMissingScripts(entry.Prefab), "Missing Script: " + entry.Prefab.name, errors);
            if (entry.Kind == EarthZoneVfxKind.CrystalPending)
                Require(entry.Prefab.transform.Find("TEMP_CRYSTAL_GUIDE") != null, "결정 임시 mesh 누락", errors);
            else
            {
                Transform guide = entry.Prefab.transform.Find("TEMP_RANGE_GUIDE");
                Require(guide != null, "TEMP_RANGE_GUIDE 누락: " + entry.Prefab.name, errors);
                if (guide != null && authoring != null)
                {
                    Require(Mathf.Approximately(guide.localScale.x, authoring.AuthoredRadius * 2f)
                            && Mathf.Approximately(guide.localScale.z, authoring.AuthoredRadius * 2f),
                        "가이드 scale과 authoredRadius 불일치: " + entry.Prefab.name, errors);
                }
            }
        }
    }

    private static bool HasNoMissingScripts(GameObject root)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Component[] components = transforms[i].GetComponents<Component>();
            for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
            {
                if (components[componentIndex] == null)
                    return false;
            }
        }
        return true;
    }

    private static void Require(bool condition, string message, List<string> errors)
    {
        if (!condition)
            errors.Add(message);
    }
}
#endif
