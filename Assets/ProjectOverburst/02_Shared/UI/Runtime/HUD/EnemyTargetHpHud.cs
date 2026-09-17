using System.Collections.Generic;
using UnityEngine;

public sealed class EnemyTargetHpHud : MonoBehaviour
{
    private static readonly List<CombatHealth> PendingHitTargets = new List<CombatHealth>(8);
    private static EnemyTargetHpHud instance;
    private static int pendingFrame = -1;

    [SerializeField] private EnemyTargetHpSlotUI slot;

    private CombatHealth currentTarget;
    private EnemyRank currentRank;

    private void Awake()
    {
        instance = this;
        if (slot == null)
            slot = GetComponentInChildren<EnemyTargetHpSlotUI>(true);
        if (slot != null)
            slot.Hide();
    }

    private void OnEnable()
    {
        instance = this;
        Subscribe(currentTarget);
    }

    private void OnDisable()
    {
        Unsubscribe(currentTarget);
        if (instance == this)
            instance = null;
    }

    private void LateUpdate()
    {
        if (currentTarget != null && currentTarget.IsDead)
            Clear(); // 사망 대상

        if (pendingFrame == Time.frameCount && PendingHitTargets.Count > 0)
        {
            ShowLastHitTarget(PendingHitTargets);
            PendingHitTargets.Clear();
            pendingFrame = -1;
        }

        if (currentTarget != null && slot != null)
            slot.Refresh(currentTarget);
    }

    public static void ReportPlayerDamage(CombatHealth target, DamageInfo info)
    {
        if (instance == null || target == null || target.IsDead || !IsPlayerDamage(info))
            return;

        if (pendingFrame != Time.frameCount)
        {
            PendingHitTargets.Clear();
            pendingFrame = Time.frameCount;
        }

        PendingHitTargets.Remove(target); // 마지막 순서
        PendingHitTargets.Add(target);
    }

    public void ShowLastHitTarget(IReadOnlyList<CombatHealth> hitTargets)
    {
        CombatHealth selected = SelectTarget(hitTargets);
        if (selected != null)
            ShowTarget(selected);
    }

    public void ShowTarget(CombatHealth target)
    {
        if (target == null)
            return;

        if (currentTarget != target)
        {
            Unsubscribe(currentTarget);
            currentTarget = target;
            currentRank = target.GetComponentInParent<EnemyRank>();
            Subscribe(currentTarget);
        }

        if (slot != null)
            slot.Show(currentTarget, currentRank);
    }

    public void Clear()
    {
        Unsubscribe(currentTarget);
        currentTarget = null;
        currentRank = null;
        if (slot != null)
            slot.Hide();
    }

    private CombatHealth SelectTarget(IReadOnlyList<CombatHealth> hitTargets)
    {
        CombatHealth selected = null;
        EnemyRankType selectedRank = EnemyRankType.Normal;

        if (hitTargets == null)
            return null;

        for (int i = 0; i < hitTargets.Count; i++)
        {
            CombatHealth candidate = hitTargets[i];
            if (candidate == null || candidate.IsDead)
                continue;

            EnemyRank rank = candidate.GetComponentInParent<EnemyRank>();
            EnemyRankType rankType = rank != null ? rank.Rank : EnemyRankType.Normal;
            if (selected == null || rankType > selectedRank || rankType == selectedRank)
            {
                selected = candidate;
                selectedRank = rankType;
            }
        }

        return selected;
    }

    private void Subscribe(CombatHealth target)
    {
        if (target == null)
            return;

        target.OnHealthChanged += HandleHealthChanged;
        target.OnDead += HandleDead;
    }

    private void Unsubscribe(CombatHealth target)
    {
        if (target == null)
            return;

        target.OnHealthChanged -= HandleHealthChanged;
        target.OnDead -= HandleDead;
    }

    private void HandleHealthChanged(CombatHealth source, float currentHp, float maxHp)
    {
        if (source == currentTarget && slot != null)
            slot.Refresh(source);
    }

    private void HandleDead(CombatHealth source, DamageInfo info)
    {
        if (source == currentTarget)
            Clear(); // 사망 대상
    }

    private static bool IsPlayerDamage(DamageInfo info)
    {
        return CombatTeamUtility.IsPlayerActorDamage(info);
    }
}
