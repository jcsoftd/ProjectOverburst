using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// 2026-10-01: 남긴 제작 도구의 부분 누락·실패 경로 검증(쓰기 없음).
// HP 바: 실제 자산 존재표를 바꾼 가상 존재표로 계획만 세워, 체급 하나가 없을 때 그 체급만 만드는지와 원본이 없으면 쓰기 전에 멈추는지 본다.
// 혼성 분대: 제품 정의·능력 세트의 메모리 사본(저장 안 함)으로 검사 실패가 쓰기 전에 멈추는지와 편집한 전술 프로필이 유지되는지 본다.
public static class AuthoringToolSafetyVerifier
{
    [MenuItem("OVERBURST/Codex/Validate/Authoring Tool Safety (In-Memory)")]
    public static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required");
        var log = new List<string>();
        int failures = 0;
        void Check(bool ok, string what) { if (!ok) failures++; log.Add((ok ? "PASS " : "FAIL ") + what); }
        var clones = new List<Object>();
        try
        {
            VerifyHpBarPlan(Check);
            VerifyMixedSquadPlan(Check, clones);
        }
        finally
        {
            foreach (var clone in clones) if (clone != null) Object.DestroyImmediate(clone);
        }
        log.Insert(0, "[AuthoringToolSafetyVerifier] " + (failures == 0 ? "PASS" : "FAIL failures=" + failures));
        return string.Join("\n", log);
    }

    private static void VerifyHpBarPlan(Action<bool, string> check)
    {
        Func<string, bool> real = path => AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
        var plan = EnemyHpBarGameApplier.PlanMissingTiers(real);
        check(plan.Count == 0, "HP bars: all three live tiers present -> nothing to build");

        plan = EnemyHpBarGameApplier.PlanMissingTiers(p => !p.EndsWith("/PF_EnemyHpBar_Medium.prefab", StringComparison.Ordinal) && real(p));
        check(plan.Count == 1 && plan[0] == "Medium", "HP bars: only Medium missing -> only Medium is built, Small/Elite untouched");

        check(Refuses(() => EnemyHpBarGameApplier.PlanMissingTiers(p =>
                !p.EndsWith("/PF_EnemyHpBar_Elite_Tier.prefab", StringComparison.Ordinal)
                && !p.EndsWith("/PF_EnemyHpBar_Elite.prefab", StringComparison.Ordinal) && real(p))),
            "HP bars: missing Elite with its source missing -> stops before any write");
        check(Refuses(() => EnemyHpBarGameApplier.PlanMissingTiers(p =>
                !p.EndsWith("/PF_EnemyHpBar_Small.prefab", StringComparison.Ordinal)
                && !p.EndsWith("/PF_EnemyHpBar_Concept_Small.prefab", StringComparison.Ordinal) && real(p))),
            "HP bars: missing Small with its concept missing -> stops before any write");
    }

    private static void VerifyMixedSquadPlan(Action<bool, string> check, List<Object> clones)
    {
        string root = MonsterThemeCombatBuilder.Root;
        var definitions = AssetDatabase.FindAssets("t:EnemyDefinition", new[] { root + "/Definitions" })
            .Select(guid => AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(guid))).ToList();
        var tables = MonsterMixedSquadBuilder.OriginalThemeIds
            .Select(id => AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(root + "/Tables/" + id + ".asset")).ToArray();

        var plan = MonsterMixedSquadBuilder.Plan(definitions, tables, out int roster);
        check(plan.Count == 0 && roster > 0, "mixed squad: current roster passes every check and nothing is reassigned (" + roster + " species)");

        // 전술 프로필이 빈 사본 하나: 그 정의만 연결 대상이 된다.
        var melee = definitions.First(d => d.EnemyId == "SpiderBrood_Rostrokarck");
        var emptyCopy = Clone(melee, clones);
        emptyCopy.SetTacticalProfile(null);
        var withEmpty = definitions.Select(d => d == melee ? emptyCopy : d).ToList();
        plan = MonsterMixedSquadBuilder.Plan(withEmpty, tables, out _);
        check(plan.Count == 1 && plan[0].definition == emptyCopy && plan[0].role == EnemyTacticalRole.MeleePressure,
            "mixed squad: only the definition with an empty tactical profile is assigned");

        // 편집값: 근접 종에 다른 프로필을 직접 정해 두면 다시 실행해도 바꾸지 않는다.
        var ranged = definitions.First(d => d.EnemyId == "VenomBrood_Arathrox");
        var editedCopy = Clone(melee, clones);
        editedCopy.SetTacticalProfile(ranged.TacticalProfile);
        plan = MonsterMixedSquadBuilder.Plan(definitions.Select(d => d == melee ? editedCopy : d), tables, out _);
        check(plan.Count == 0 && editedCopy.TacticalProfile == ranged.TacticalProfile, "mixed squad: an edited tactical profile is kept on rerun");

        // 검사 실패: 원거리 종의 투사체 능력을 뺀 사본 + 빈 프로필 사본을 함께 넣으면 아무것도 연결하지 않고 멈춘다.
        var setCopy = Clone(ranged.AbilitySet, clones);
        var nonShots = Enumerable.Range(0, setCopy.Count).Select(setCopy.GetAbility)
            .Where(a => a != null && a.ExecutionMode != EnemyAbilityExecutionMode.Projectile).ToArray();
        setCopy.Configure(setCopy.AbilitySetId, nonShots);
        var rangedCopy = Clone(ranged, clones);
        var so = new SerializedObject(rangedCopy);
        so.FindProperty("abilitySet").objectReferenceValue = setCopy;
        so.ApplyModifiedPropertiesWithoutUndo();
        var broken = definitions.Select(d => d == ranged ? rangedCopy : d == melee ? emptyCopy : d).ToList();
        string message = RefusalMessage(() => MonsterMixedSquadBuilder.Plan(broken, tables, out _));
        check(message != null && message.Contains("nothing was written") && message.Contains("missing ranged ability: VenomBrood_Arathrox"),
            "mixed squad: a ranged species without a projectile stops the whole run before any write");

        // 로스터 불일치: 테이블에 있는 정의 하나가 없으면 멈춘다(예전 고정 14종 검사를 대신한다).
        message = RefusalMessage(() => MonsterMixedSquadBuilder.Plan(definitions.Where(d => d != melee), tables, out _));
        check(message != null && message.Contains("table entry without definition: SpiderBrood_Rostrokarck"),
            "mixed squad: roster that no longer matches the theme tables stops before any write");
    }

    private static T Clone<T>(T source, List<Object> clones) where T : Object
    {
        var copy = Object.Instantiate(source);
        copy.hideFlags = HideFlags.HideAndDontSave;
        clones.Add(copy);
        return copy;
    }

    private static bool Refuses(Action run) => RefusalMessage(run) != null;

    private static string RefusalMessage(Action run)
    {
        try { run(); return null; }
        catch (InvalidOperationException ex) { return ex.Message; }
    }
}
