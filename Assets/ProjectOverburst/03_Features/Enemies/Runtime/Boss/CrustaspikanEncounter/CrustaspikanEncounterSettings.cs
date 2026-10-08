using System;
using UnityEngine;

public enum CrustaspikanStepKind { Attack, Motion, Move, Wait, LiftElite, ThrowElite }

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
        public bool firstHitParry;
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
    [Range(4, 48)] public int maximumAdds = 26;
    public bool protectPlayerFromDeath = true;
    [Header("현재 상황 기반 기본 AI")]
    [Min(1f)] public float approachDistance = 8f;
    [Min(.1f)] public float approachSpeed = 3f;
    [Range(1, 8), Tooltip("근접 거리 밖에서 실제 실행한 행동 수. 도달 가능한 접근 조립은 별도로 허용합니다.")] public int maximumFarActions = 1;
    [Range(.5f, 10f), Tooltip("최초 회전 뒤 실제 보행 시작부터 계산하는 추격 제한 시간입니다.")] public float pursuitWalkSeconds = 3f;
    [Range(1f, 15f)] public float attackFacingTolerance = 8f;
    [Min(.5f)] public float attackPreparationTimeout = 4f;
    public bool enableBossEvasion = true;
    [Min(1f)] public float evasionTriggerDistance = 7f;
    [Min(.5f)] public float evasionDistance = 3f;
    [Min(.1f)] public float evasionSeconds = 1f;
    [Min(1f)] public float evasionCooldown = 12f;
    [Header("포탈 입장 연출")]
    public CrustaspikanEntranceCinematic.Settings entrance = new CrustaspikanEntranceCinematic.Settings();

    public CrustaspikanParryRecoilProfile ParryRecoilProfile => materials?.actorDefinition?.ActorPrefab != null
        ? materials.actorDefinition.ActorPrefab.GetComponent<CrustaspikanTemporaryReaction>()?.ParryRecoilProfile : null;

    public bool Validate(out string reason)
    {
        if (materials == null || composites == null || !composites.IsValid || materials.actorDefinition == null || !materials.actorDefinition.IsValid
            || adds == null || !adds.Validate(out reason)) { reason = "보스 재료 또는 소환 테마가 없습니다."; return false; }
        if (!CrustaspikanEncounterMaterialResolver.ValidateCollection(this, materials, ParryRecoilProfile, out reason)) return false;
        if (bossHp <= 0 || groggyMax <= 0 || arenaRadius < 16 || maximumAdds < 4
            || !Finite(bossHp) || !Finite(groggyMax) || !Finite(arenaRadius)) { reason = "전투 수치가 유효하지 않습니다."; return false; }
        if (!Finite(betweenPatterns) || betweenPatterns < 0f || !Finite(approachDistance) || approachDistance < 1f
            || !Finite(approachSpeed) || approachSpeed < .1f
            || maximumFarActions < 1 || maximumFarActions > 8 || !Finite(pursuitWalkSeconds) || pursuitWalkSeconds < .5f || pursuitWalkSeconds > 10f
            || !Finite(attackFacingTolerance) || attackFacingTolerance < 1f || attackFacingTolerance > 15f
            || !Finite(attackPreparationTimeout) || attackPreparationTimeout < .5f
            || !Finite(evasionTriggerDistance) || evasionTriggerDistance < 1f || !Finite(evasionDistance) || evasionDistance < .5f
            || !Finite(evasionSeconds) || evasionSeconds < .1f || !Finite(evasionCooldown) || evasionCooldown < 1f)
        { reason = "기본 AI 이동/대기 수치가 유효하지 않습니다."; return false; }
        if (entrance != null && entrance.enabled && (materials.FindMotion(entrance.roarMotion)?.IsPlayable != true
            || materials.FindMotion(entrance.arrivalMotion)?.IsPlayable != true
            || !Finite(entrance.emergenceSeconds) || entrance.emergenceSeconds < .1f
            || !Finite(entrance.arrivalImpactNormalized) || entrance.arrivalImpactNormalized < .05f || entrance.arrivalImpactNormalized > .95f
            || !Finite(entrance.arrivalRecoveryNormalized) || entrance.arrivalRecoveryNormalized <= entrance.arrivalImpactNormalized || entrance.arrivalRecoveryNormalized > .95f
            || !Finite(entrance.motionBlendSeconds) || entrance.motionBlendSeconds < .05f || entrance.motionBlendSeconds > 1f
            || !Finite(entrance.detailSeconds) || entrance.detailSeconds < .25f || !Finite(entrance.riseSeconds) || entrance.riseSeconds < .25f
            || !Finite(entrance.revealSeconds) || entrance.revealSeconds < .25f || !Finite(entrance.returnSeconds) || entrance.returnSeconds < .1f
            || !Finite(entrance.combatGraceSeconds) || entrance.combatGraceSeconds < 0f || !Finite(entrance.roarVolume)))
        { reason = "등장 모션 또는 컷 길이가 유효하지 않습니다."; return false; }
        if (patterns == null) { reason = "조립 패턴 배열이 없습니다."; return false; }
        var ids = new System.Collections.Generic.HashSet<string>();
        foreach (var p in patterns)
        {
            if (p == null || string.IsNullOrEmpty(p.id) || !ids.Add(p.id) || p.steps == null || p.steps.Length == 0
                || !Finite(p.weight) || p.weight <= 0 || p.maximumDistance < p.minimumDistance)
            { reason = "패턴 ID/가중치/거리/단계를 확인하세요."; return false; }
            foreach (var s in p.steps)
            {
                if (s == null || !Finite(s.seconds) || s.seconds < 0) { reason = "잘못된 조립 단계입니다."; return false; }
                if (s.kind == CrustaspikanStepKind.Move && (!Finite(s.localDisplacement.x) || !Finite(s.localDisplacement.y) || !Finite(s.localDisplacement.z)
                    || s.localDisplacement.sqrMagnitude > .000001f && s.seconds <= 0f))
                { reason = "이동량/실제 보행 제한 시간이 유효하지 않습니다: " + p.id; return false; }
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
        if (materialRules == null) return null;
        foreach (var r in materialRules) if (r != null && r.clip == clip) return r;
        return null;
    }
    private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
}
