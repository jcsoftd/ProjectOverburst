using System;
using UnityEngine;

// 2026-10-01 대표 보스 전용 시험값. 전역 전투 규칙·다른 몬스터 값과 분리하며 사용자 확정 밸런스가 아니다.
[CreateAssetMenu(menuName = "OVERBURST/Enemies/Boss Combat Profile", fileName = "EBCP_Boss")]
public sealed class EnemyBossCombatProfile : ScriptableObject
{
    [Serializable]
    public sealed class Pattern
    {
        public string patternId;
        public string displayName;
        public EnemyAbilityDefinition ability;
        [Tooltip("bit0=1페이즈, bit1=2페이즈")] public int phaseMask = 3;
        [Min(.01f)] public float weight = 1f;
        [Tooltip("패링 불가 광역: 붉은 장판과 낮은 으르렁을 따로 띄운다")] public bool unparryableDangerCue;
        [Tooltip("2페이즈에서 이 패턴이 끝난 직후 이어 쓰는 후속 패턴 ID")] public string phaseTwoFollowUp;

        public bool AllowsPhase(int phaseIndex) => phaseIndex >= 0 && phaseIndex < 31 && (phaseMask & (1 << phaseIndex)) != 0;
    }

    [Header("패턴 [시험]")]
    public Pattern[] patterns = Array.Empty<Pattern>();
    [Min(.1f)] public float decisionInterval = .25f;
    [Min(0f), Tooltip("후보가 직전 패턴뿐일 때 반복을 허용하기 전 대기")] public float repeatDelay = .8f;
    [Min(.1f), Tooltip("앞 공격이 끝난 뒤 후속 패턴을 쓸 수 있는 시간")] public float followUpWindow = 1.6f;

    [Header("그로기 [시험]")]
    [Min(1f)] public float groggyMax = 100f;
    [Min(0f)] public float parryGain = 35f;
    [Min(0f)] public float heavyHitGain = 10f;
    // 감쇠 시작은 실제 패링 간격(강공 빈도 잠금+쿨다운 ≈ 8~12초)보다 길어야 패링이 쌓인다.
    [Min(0f)] public float decayDelay = 10f;
    [Min(0f)] public float decayPerSecond = 5f;
    [Min(.1f)] public float groggyDuration = 4.5f;
    [Min(0f)] public float groggyProtection = 8f;
    [Min(.1f), Tooltip("그로기 미만 패링 경직. 일반 몬스터 패링 기절(중형 1.2·정예 0.8초)과 별도")] public float parryRecoil = 1.0f;
    [Tooltip("그로기 루프 상태 이름(보스 전용 Animator)")] public string groggyState = "Boss_Groggy";

    [Header("페이즈 전환 [시험]")]
    [Min(.1f)] public float phaseTransitionDuration = 2.4f;
    public string transitionState = "Boss_Roar";
    [Tooltip("페이즈 인덱스별 행동 프로필(공격 사이 대기 등). 비우면 정의값 유지")]
    public EnemyBehaviorProfile[] phaseBehaviors = Array.Empty<EnemyBehaviorProfile>();

    [Header("표시·소리")]
    public Color dangerTint = new Color(1f, .16f, .10f, 1f);
    public AudioClip dangerClip;
    public AudioClip roarClip;
    public AudioClip groggyClip;
    [Range(0f, 1f)] public float sfxVolume = .8f;

    public Pattern Find(EnemyAbilityDefinition ability)
    {
        if (ability == null || patterns == null) return null;
        for (int i = 0; i < patterns.Length; i++)
            if (patterns[i] != null && patterns[i].ability == ability) return patterns[i];
        return null;
    }

    public Pattern Find(string patternId)
    {
        if (string.IsNullOrEmpty(patternId) || patterns == null) return null;
        for (int i = 0; i < patterns.Length; i++)
            if (patterns[i] != null && patterns[i].patternId == patternId) return patterns[i];
        return null;
    }

    public EnemyBehaviorProfile BehaviorForPhase(int phaseIndex) => phaseBehaviors != null
        && phaseIndex >= 0 && phaseIndex < phaseBehaviors.Length ? phaseBehaviors[phaseIndex] : null;
}
