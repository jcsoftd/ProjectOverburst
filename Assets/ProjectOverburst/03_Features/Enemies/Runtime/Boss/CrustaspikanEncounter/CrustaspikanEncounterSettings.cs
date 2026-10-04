using System;
using UnityEngine;

public enum CrustaspikanStepKind { Attack, Motion, Move, Wait, LiftElite, ThrowElite }
public enum CrustaspikanTactic { Balanced, Evade, Dash, Weak, Heavy, DashAttack, Parry, Rear, Range, Left, Right }

// 재료 원본은 읽기만 한다. 전투별 배속/피해/패링 규칙은 실행 때 복제한 재료에 적용한다.
[CreateAssetMenu(menuName = "OVERBURST/Enemies/Crustaspikan Encounter")]
public sealed class CrustaspikanEncounterSettings : ScriptableObject
{
    [Serializable] public sealed class MaterialRule
    {
        public string clip;
        [Min(.1f)] public float speed = 1f;
        [Min(0f)] public float damage = 1f;
        public bool finalHitParry = true;
    }
    [Serializable] public sealed class Step
    {
        public CrustaspikanStepKind kind;
        public string materialOrMotion;
        [Min(0f)] public float seconds;
        public Vector3 localDisplacement;
    }
    [Serializable] public sealed class Pattern
    {
        public string id;
        public string label;
        public string family;
        public int phaseMask = 3;
        [Min(.01f)] public float weight = 1f;
        [Min(0f)] public float cooldown = 6f;
        [Min(0f)] public float minimumDistance;
        [Min(0f)] public float maximumDistance = 24f;
        public bool rearOnly;
        public CrustaspikanTactic counters;
        public Step[] steps = Array.Empty<Step>();
    }
    public EnemyBossMaterialCollection materials;
    public EnemyBossCompositePatternSet composites;
    public EnemyThemeTable adds;
    public MaterialRule[] materialRules = Array.Empty<MaterialRule>();
    public Pattern[] patterns = Array.Empty<Pattern>();
    [Header("첫 전투 시험값")]
    [Min(16f)] public float arenaRadius = 20f;
    [Min(1f)] public float bossHp = 3000f;
    [Range(.1f, .9f)] public float phaseTwoHp = .5f;
    [Min(0f)] public float betweenPatterns = .7f;
    [Min(0f)] public float phaseTwoBetweenPatterns = .45f;
    [Min(1f)] public float groggyMax = 300f;
    public float weakPoise = 2f, heavyPoise = 10f, perfectParryPoise = 35f;
    public float backPoiseMultiplier = 1.25f;
    public float groggySeconds = 4.5f, groggyProtection = 8f;
    public float poiseDecayDelay = 10f, poiseDecayPerSecond = 5f;
    [Range(4, 12)] public int maximumAdds = 6;
    [Range(1f, 2f)] public float tacticWeightMultiplier = 1.35f;
    public bool protectPlayerFromDeath = true;
    public bool enableAdaptiveTactics = true;
    public bool enableBossEvasion = true;

    public bool Validate(out string reason)
    {
        if (materials == null || composites == null || !composites.IsValid || materials.actorDefinition == null || !materials.actorDefinition.IsValid
            || adds == null || !adds.Validate(out reason)) { reason = "보스 재료 또는 소환 테마가 없습니다."; return false; }
        if (bossHp <= 0 || groggyMax <= 0 || arenaRadius < 16 || maximumAdds < 4
            || !Finite(bossHp) || !Finite(groggyMax) || !Finite(arenaRadius)) { reason = "전투 수치가 유효하지 않습니다."; return false; }
        var ids = new System.Collections.Generic.HashSet<string>();
        foreach (var p in patterns)
        {
            if (p == null || string.IsNullOrEmpty(p.id) || !ids.Add(p.id) || p.steps == null || p.steps.Length == 0
                || !Finite(p.weight) || p.weight <= 0 || p.maximumDistance < p.minimumDistance)
            { reason = "패턴 ID/가중치/거리/단계를 확인하세요."; return false; }
            foreach (var s in p.steps)
            {
                if (s == null || !Finite(s.seconds) || s.seconds < 0) { reason = "잘못된 조립 단계입니다."; return false; }
                if (s.kind == CrustaspikanStepKind.Attack && FindMaterial(s.materialOrMotion) == null)
                { reason = "공격 재료가 없습니다: " + s.materialOrMotion; return false; }
                if (s.kind == CrustaspikanStepKind.Motion && (materials.FindMotion(s.materialOrMotion)?.IsPlayable != true))
                { reason = "RM 또는 없는 모션입니다: " + s.materialOrMotion; return false; }
            }
        }
        reason = ""; return patterns.Length > 0;
    }
    public EnemyBossAttackMaterial FindMaterial(string clip)
    {
        foreach (var m in materials.attacks) if (m != null && m.runtimeClip != null && m.runtimeClip.name == clip) return m;
        return null;
    }
    public MaterialRule Rule(string clip)
    {
        foreach (var r in materialRules) if (r != null && r.clip == clip) return r;
        return null;
    }
    private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
}
