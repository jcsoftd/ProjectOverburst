using System.Collections.Generic;
using UnityEngine;

public sealed class EnemyTargetHpHud : MonoBehaviour
{
    private static EnemyTargetHpHud instance;
    private static CombatHealth pendingTarget;
    private static bool hasPendingTarget;
    private static float pendingPreHitHp = -1f; // 2026-10-01: 새 대상 첫 타격의 잔상 시작점(맞기 전 체력)
    [SerializeField] private EnemyTargetHpSlotUI slot;
    private CombatHealth currentTarget;
    private EnemyRank currentRank;
    private bool healthDirty;
    public CombatHealth CurrentTarget => currentTarget;
    public int PresentationRefreshCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null; pendingTarget = null; hasPendingTarget = false; pendingPreHitHp = -1f;
    }

    private void Awake()
    {
        if (slot == null) slot = GetComponentInChildren<EnemyTargetHpSlotUI>(true);
        if (slot != null) slot.Hide();
    }
    private void OnEnable() { instance = this; }
    private void OnDisable()
    {
        Clear();
        if (instance == this)
        {
            instance = null; pendingTarget = null; hasPendingTarget = false;
        }
    }
    private static bool IsAvailable(CombatHealth target) => target != null
        && target.isActiveAndEnabled && !target.IsDead;

    private void LateUpdate()
    {
        if (hasPendingTarget)
        {
            var next = pendingTarget;
            float preHit = pendingPreHitHp;
            pendingTarget = null; hasPendingTarget = false; pendingPreHitHp = -1f;
            ShowTarget(next, preHit);
        }
        if (!IsAvailable(currentTarget))
        {
            if (!ReferenceEquals(currentTarget, null)) Clear();
            return;
        }
        if (healthDirty && slot != null)
        {
            slot.Refresh(currentTarget);
            healthDirty = false;
            PresentationRefreshCount++;
        }
    }

    public static void ReportPlayerDamage(CombatHealth target, DamageInfo info, float appliedDamage = 0f)
    {
        if (instance == null || !instance.isActiveAndEnabled || target == null
            || !CombatTeamUtility.IsPlayerActorDamage(info)) return;
        // O(1) per hit, including a lethal last hit: it clears the previous target.
        // 같은 프레임에 여러 번 맞으면 처음 맞기 전 체력을 남긴다(대상이 바뀌면 새로 잡는다).
        float preHit = target.CurrentHp + Mathf.Max(0f, appliedDamage);
        if (!hasPendingTarget || pendingTarget != target) pendingPreHitHp = preHit;
        else pendingPreHitHp = Mathf.Max(pendingPreHitHp, preHit);
        pendingTarget = target;
        hasPendingTarget = true;
    }
    public static void ForgetTarget(CombatHealth target)
    {
        if (target == null) return;
        if (pendingTarget == target) { pendingTarget = null; hasPendingTarget = true; }
        if (instance != null && instance.currentTarget == target) instance.Clear();
    }
    public void ShowLastHitTarget(IReadOnlyList<CombatHealth> hitTargets)
    {
        if (hitTargets == null || hitTargets.Count == 0) return;
        ShowTarget(hitTargets[hitTargets.Count - 1]);
    }
    public void ShowTarget(CombatHealth target)
    {
        ShowTarget(target, -1f);
    }

    private void ShowTarget(CombatHealth target, float preHitHp)
    {
        if (!IsAvailable(target)) { Clear(); return; }
        if (currentTarget == target) { healthDirty = true; return; }
        Unsubscribe(currentTarget);
        currentTarget = target;
        currentRank = target.GetComponentInParent<EnemyRank>();
        currentTarget.OnHealthChanged += HandleHealthChanged;
        currentTarget.OnDead += HandleDead;
        if (slot != null) { slot.Show(currentTarget, currentRank, preHitHp); PresentationRefreshCount++; }
        healthDirty = false;
    }
    public void Clear()
    {
        Unsubscribe(currentTarget);
        currentTarget = null; currentRank = null; healthDirty = false;
        if (slot != null) slot.Hide();
    }
    private void Unsubscribe(CombatHealth target)
    {
        if (target == null) return;
        target.OnHealthChanged -= HandleHealthChanged;
        target.OnDead -= HandleDead;
    }
    private void HandleHealthChanged(CombatHealth source, float currentHp, float maxHp)
    {
        if (source == currentTarget) healthDirty = true;
    }
    private void HandleDead(CombatHealth source, DamageInfo info)
    {
        if (source == currentTarget) Clear();
    }
}
