using System.Collections.Generic;
using UnityEngine;

// 입력 버튼을 읽지 않는다. 실제 회피 사건, 적중, 공격 기회와 위치만 1페이즈에서 관측한다.
public sealed class CrustaspikanStyleObserver
{
    public CrustaspikanTactic Tactic { get; private set; }
    public string Explanation { get; private set; } = "관측 중";
    public bool Frozen { get; private set; }
    public int Evades { get; private set; }
    public int Dashes { get; private set; }
    public int WeakHits { get; private set; }
    public int HeavyHits { get; private set; }
    public int DashAttackHits { get; private set; }
    public int ParryOpportunities { get; private set; }
    public int Parries { get; private set; }
    private int left, right, positions, rear, far;
    private float nextPositionSample;
    private PlayerActorRuntime player;
    private EnemyActor boss;
    private PlayerEvadeController evade;
    private CrustaspikanEncounter encounter;
    private readonly HashSet<long> hits = new HashSet<long>();
    public void Bind(PlayerActorRuntime player, EnemyActor boss, CrustaspikanEncounter encounter)
    {
        this.player = player; this.boss = boss; this.encounter = encounter;
        evade = player.GetComponent<PlayerEvadeController>();
        if (evade != null) evade.OnEvadeStarted += ObserveEvade;
    }
    public void Dispose() { if (evade != null) evade.OnEvadeStarted -= ObserveEvade; }
    public void Tick()
    {
        if (Frozen || player == null || boss == null || Time.time < nextPositionSample) return;
        nextPositionSample = Time.time + .5f;
        Vector3 delta = player.transform.position - boss.transform.position; delta.y = 0;
        if (delta.magnitude > 24f) return;
        positions++; if (Vector3.Dot(boss.transform.forward, delta.normalized) < -.5f) rear++;
        if (delta.magnitude > 12f) far++;
    }
    private void ObserveEvade(PlayerEvadeType type)
    {
        if (Frozen || boss == null || !boss.AbilityController.IsExecuting) return;
        // 벽 부근에서는 방향 선호를 학습하지 않는다.
        if (type == PlayerEvadeType.Dash) Dashes++; else Evades++;
        if ((player.transform.position - encounter.ArenaCenter).sqrMagnitude > Mathf.Pow(encounter.Settings.arenaRadius - 3f, 2)) return;
        float side = Vector3.Dot(evade.ActiveDirection, boss.transform.right);
        if (side < -.35f) left++; else if (side > .35f) right++;
    }
    public void ObserveHit(DamageInfo info, bool dashAttack)
    {
        if (Frozen || info.source == null || info.source.GetComponentInParent<PlayerActorRuntime>() != player) return;
        long key = ((long)info.source.GetInstanceID() << 32) ^ (uint)info.sourceAttackSequenceId;
        if (info.sourceAttackSequenceId != 0 && !hits.Add(key)) return;
        if (dashAttack) DashAttackHits++;
        if ((info.playerAttackKind & PlayerAttackKind.Heavy) != 0) HeavyHits++;
        else if ((info.playerAttackKind & PlayerAttackKind.Weak) != 0) WeakHits++;
    }
    public void Opportunity() { if (!Frozen) ParryOpportunities++; }
    public void Parried() { if (!Frozen) Parries++; }
    public void Freeze(bool enabled)
    {
        Frozen = true; Tactic = CrustaspikanTactic.Balanced; Explanation = "균형형 전술 · 확실한 습관이 드러나지 않았습니다";
        if (!enabled) { Explanation = "균형형 전술 · 학습 기능 꺼짐"; return; }
        float best = .6499f;
        Select(left, left + right, 6, CrustaspikanTactic.Left, "왼쪽 회피 경향을 노립니다", ref best);
        Select(right, left + right, 6, CrustaspikanTactic.Right, "오른쪽 회피 경향을 노립니다", ref best);
        Select(HeavyHits, WeakHits + HeavyHits, 6, CrustaspikanTactic.Heavy, "강공 적중 위주의 공방을 노립니다", ref best);
        Select(WeakHits, WeakHits + HeavyHits, 6, CrustaspikanTactic.Weak, "약공 적중 위주의 공방을 노립니다", ref best);
        Select(DashAttackHits, WeakHits + HeavyHits, 6, CrustaspikanTactic.DashAttack, "대시 공격 후 추격을 노립니다", ref best);
        Select(Dashes, Dashes + Evades, 6, CrustaspikanTactic.Dash, "대시로 거리를 벌리는 습관을 노립니다", ref best);
        Select(Evades, Dashes + Evades, 6, CrustaspikanTactic.Evade, "회피 이후의 후속 움직임을 노립니다", ref best);
        Select(Parries, ParryOpportunities, 6, CrustaspikanTactic.Parry, "패링을 기다리는 공방을 노립니다", ref best);
        Select(rear, positions, 12, CrustaspikanTactic.Rear, "후방에 머무는 위치를 견제합니다", ref best);
        Select(far, positions, 12, CrustaspikanTactic.Range, "원거리 유지에 압박을 가합니다", ref best);
    }
    private void Select(int count, int total, int minimum, CrustaspikanTactic tactic, string message, ref float best)
    {
        float confidence = total > 0 ? (float)count / total : 0f;
        if (total < minimum || confidence <= best) return;
        best = confidence; Tactic = tactic; Explanation = message;
    }
}
