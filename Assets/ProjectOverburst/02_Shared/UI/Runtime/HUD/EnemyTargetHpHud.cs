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
    private CombatHealth lastHitTarget;
    private EnemyActor lastHitActor;
    private uint lastHitLease;
    private CombatHealth hoveredTarget;
    private EnemyActor hoveredActor;
    private uint hoveredLease;
    private bool healthDirty;
    public CombatHealth CurrentTarget => currentTarget;
    public CombatHealth LastHitTarget => lastHitTarget;
    public CombatHealth HoveredTarget => hoveredTarget;
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
            instance = null; pendingTarget = null; hasPendingTarget = false; pendingPreHitHp = -1f;
        }
    }
    private static bool IsAvailable(CombatHealth target) => target != null
        && target.isActiveAndEnabled && !target.IsDead;
    private static bool IsAvailable(CombatHealth target, EnemyActor actor, uint lease) =>
        IsAvailable(target) && (actor == null || actor.LeaseVersion == lease);

    private void LateUpdate()
    {
        CombatHealth freshHit = null;
        float preHit = -1f;
        if (hasPendingTarget)
        {
            freshHit = pendingTarget;
            preHit = pendingPreHitHp;
            pendingTarget = null; hasPendingTarget = false; pendingPreHitHp = -1f;
            RememberLastHit(freshHit);
        }
        if (!IsAvailable(lastHitTarget, lastHitActor, lastHitLease)) RememberLastHit(null);
        if (!IsAvailable(hoveredTarget, hoveredActor, hoveredLease)) SetHover(null);
        bool inspect = !PlayerCombatModeController.IsSharedCombatModeActive();
        CombatHealth next = inspect && IsAvailable(hoveredTarget) ? hoveredTarget : lastHitTarget;
        if (!IsAvailable(next))
        {
            if (!ReferenceEquals(currentTarget, null)) ClearPresentation();
            return;
        }
        // A delayed return to the last hit starts at current HP, not an old damage trail.
        if (currentTarget != next) ShowPresentation(next, next == freshHit ? preHit : -1f);
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
        if (instance == null) return;
        if (instance.lastHitTarget == target) instance.RememberLastHit(null);
        if (instance.hoveredTarget == target) instance.SetHover(null);
        if (instance.currentTarget == target) instance.ClearPresentation();
    }
    public static void SetExplorationHoverTarget(CombatHealth target)
    {
        if (instance == null || !instance.isActiveAndEnabled) return;
        instance.SetHover(IsAvailable(target) ? target : null);
    }
    private void SetHover(CombatHealth target)
    {
        if (hoveredTarget == target && (hoveredActor == null || hoveredActor.LeaseVersion == hoveredLease)) return;
        hoveredTarget = target;
        hoveredActor = target != null ? target.GetComponentInParent<EnemyActor>() : null;
        hoveredLease = hoveredActor != null ? hoveredActor.LeaseVersion : 0;
    }
    private void RememberLastHit(CombatHealth target)
    {
        if (!IsAvailable(target)) target = null;
        if (lastHitTarget == target && (lastHitActor == null || lastHitActor.LeaseVersion == lastHitLease)) return;
        if (lastHitTarget != null)
        {
            lastHitTarget.OnReset -= HandleLastHitReset;
            lastHitTarget.OnDead -= HandleLastHitDead;
        }
        lastHitTarget = target;
        lastHitActor = target != null ? target.GetComponentInParent<EnemyActor>() : null;
        lastHitLease = lastHitActor != null ? lastHitActor.LeaseVersion : 0;
        if (lastHitTarget != null)
        {
            lastHitTarget.OnReset += HandleLastHitReset;
            lastHitTarget.OnDead += HandleLastHitDead;
        }
    }
    public void ShowLastHitTarget(IReadOnlyList<CombatHealth> hitTargets)
    {
        if (hitTargets == null || hitTargets.Count == 0) return;
        ShowTarget(hitTargets[hitTargets.Count - 1]);
    }
    public void ShowTarget(CombatHealth target)
    {
        RememberLastHit(target);
        ShowPresentation(target, -1f);
    }

    private void ShowPresentation(CombatHealth target, float preHitHp)
    {
        if (!IsAvailable(target)) { ClearPresentation(); return; }
        if (currentTarget == target) { healthDirty = true; return; }
        Unsubscribe(currentTarget);
        currentTarget = target;
        currentRank = target.GetComponentInParent<EnemyRank>();
        currentTarget.OnHealthChanged += HandleHealthChanged;
        currentTarget.OnDead += HandleDead;
        currentTarget.OnReset += HandleReset;
        if (slot != null) { slot.Show(currentTarget, currentRank, preHitHp); PresentationRefreshCount++; }
        healthDirty = false;
    }
    public void Clear()
    {
        RememberLastHit(null);
        SetHover(null);
        ClearPresentation();
    }
    private void ClearPresentation()
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
        target.OnReset -= HandleReset;
    }
    private void HandleHealthChanged(CombatHealth source, float currentHp, float maxHp)
    {
        if (source == currentTarget) healthDirty = true;
    }
    private void HandleDead(CombatHealth source, DamageInfo info)
    {
        if (source == currentTarget) ClearPresentation();
    }
    private void HandleReset(CombatHealth source) => ForgetTarget(source);
    private void HandleLastHitReset(CombatHealth source) => ForgetTarget(source);
    private void HandleLastHitDead(CombatHealth source, DamageInfo info) => RememberLastHit(null);
}
