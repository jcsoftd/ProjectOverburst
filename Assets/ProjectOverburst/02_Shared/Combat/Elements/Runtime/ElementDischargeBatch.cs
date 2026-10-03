using System;
using System.Collections.Generic;
using UnityEngine;

// One reusable position snapshot per cast. X-sorted interval queries avoid per-hop physics,
// per-cast grids and per-target allocations. Each victim has one shared secondary-hit counter.
public sealed partial class ElementDischargeBatch
{
    private struct Node
    {
        public CombatTarget Target;
        public CombatHealth Health;
        public ElementalStatusController Status;
        public Vector3 Point;
        public float Radius, HalfHeight;
        public int Life, Stacks, Hits;
        public bool Queued;
    }
    private sealed class PositionComparer : IComparer<Node>
    {
        public int Compare(Node a, Node b)
        {
            int x = a.Point.x.CompareTo(b.Point.x);
            return x != 0 ? x : a.Target.TargetId.CompareTo(b.Target.TargetId);
        }
    }
    private static readonly PositionComparer Comparer = new PositionComparer();
    private readonly List<CombatTarget> targets = new List<CombatTarget>(128);
    private readonly Dictionary<int, int> indices = new Dictionary<int, int>(128);
    private Node[] nodes = new Node[128];
    // Successor links skip victims that are dead or have exhausted this cast's hit budget.
    // Positions/origins stay intact, including lethal origins that still need to propagate.
    private int[] successor = new int[129];
    private int[] roots = new int[128], paths = new int[128 * 8], pathLengths = new int[128];
    private int count, rootCount;
    private float largestRadius, heightTolerance;
    private WeaponElement element;
    private ElementGemAttackSnapshot gemAttack;
    public int CandidateChecks { get; private set; }
    public int SecondaryHits { get; private set; }
    public int OriginCount => rootCount;
    public int Count => count;
    public void Clear()
    {
        Array.Clear(nodes, 0, count);
        count = rootCount = CandidateChecks = SecondaryHits = 0;
        indices.Clear(); targets.Clear(); gemAttack = default;
    }
    public void Capture(CombatTarget source, WeaponElement sourceElement, float verticalTolerance, ElementGemAttackSnapshot snapshot = default)
    {
        using var costScope = ElementCombatCostMarkers.Heavy_TargetSnapshot.Auto();
        Clear(); gemAttack = snapshot; element = sourceElement; heightTolerance = verticalTolerance; largestRadius = 0f;
        if (source == null) return;
        CombatTargetRegistry.CollectTeamTargets(source.Team == CombatTeam.Enemy ? CombatTeam.PlayerParty : CombatTeam.Enemy, targets);
        if (targets.Count > nodes.Length)
        {
            int capacity = Mathf.NextPowerOfTwo(targets.Count);
            Array.Resize(ref nodes, capacity); Array.Resize(ref roots, capacity);
            Array.Resize(ref successor, capacity + 1);
            Array.Resize(ref paths, capacity * 8); Array.Resize(ref pathLengths, capacity);
        }
        foreach (CombatTarget target in targets)
        {
            if (target == null || !target.IsAlive || target.DamageReceiver == null) continue;
            ElementalStatusController status = target.GetComponent<ElementalStatusController>();
            CombatTargetVolume volume = target.CurrentHurtVolume;
            int stacks = status != null ? status.GetStackCount(element) : 0;
            if (!target.IsAlive) continue;
            nodes[count++] = new Node { Target = target, Health = target.DamageReceiver, Status = status,
                Point = volume.Center, Radius = volume.Radius, HalfHeight = volume.HalfHeight,
                Life = status != null ? status.LifecycleVersion : 0, Stacks = stacks };
            largestRadius = Mathf.Max(largestRadius, volume.Radius);
        }
        Array.Sort(nodes, 0, count, Comparer);
        for (int i = 0; i < count; i++) { indices[nodes[i].Health.GetInstanceID()] = i; successor[i] = i; }
        successor[count] = count;
        targets.Clear();
    }
    public CombatTarget FirstBlastTarget(int index, Vector3 center, float radius)
    {
        return Valid(index) && InRange(index, center, radius) ? nodes[index].Target : null;
    }
    public void ConfirmInitial(CombatHealth health)
    {
        if (health != null && indices.TryGetValue(health.GetInstanceID(), out int index)) Queue(index);
    }
    private void Queue(int index)
    {
        if (nodes[index].Queued || nodes[index].Stacks <= 0) return;
        nodes[index].Queued = true;
        roots[rootCount++] = index;
    }
    private bool SameLife(int i) => nodes[i].Health != null && (nodes[i].Status == null || nodes[i].Status.LifecycleVersion == nodes[i].Life);
    private bool Valid(int i) => SameLife(i) && nodes[i].Target != null && nodes[i].Target.IsAlive;
    private bool InRange(int i, Vector3 point, float radius)
    {
        Vector3 delta = nodes[i].Point - point;
        float reach = radius + nodes[i].Radius;
        return delta.x * delta.x + delta.z * delta.z <= reach * reach
            && Mathf.Max(0f, Mathf.Abs(delta.y) - nodes[i].HalfHeight) <= heightTolerance;
    }
    private int LowerBound(float x)
    {
        int lo = 0, hi = count;
        while (lo < hi) { int mid = (lo + hi) >> 1; if (nodes[mid].Point.x < x) lo = mid + 1; else hi = mid; }
        return lo;
    }
    private int NextCandidate(int i)
    {
        int next = i;
        while (successor[next] != next) next = successor[next];
        while (successor[i] != i) { int old = successor[i]; successor[i] = next; i = old; }
        return next;
    }
    private void RetireCandidate(int i) => successor[i] = NextCandidate(i + 1);
    private bool Damage(int i, float damage, GameObject source)
    {
        using var costScope = ElementCombatCostMarkers.Chain_Damage.Auto();
        if (!gemAttack.IsCurrent || !Valid(i) || nodes[i].Hits >= 2 || damage <= 0f) return false;
        float before = nodes[i].Health.CurrentHp;
        nodes[i].Health.TakeDamage(new DamageInfo(damage, nodes[i].Point, source,
            triggersOnHitEffects: false, suppressDefaultHitVfx: true,
            element: element, playerAttackKind: PlayerAttackKind.Elemental, gemAttack: gemAttack));
        if (!SameLife(i) || nodes[i].Health.CurrentHp >= before) return false;
        nodes[i].Hits++; SecondaryHits++;
        if (nodes[i].Hits >= 2 || !Valid(i)) RetireCandidate(i);
        return true;
    }
    public void ExecuteFire(float blastDamage, GameObject source, Action<Vector3, float> explosionVfx)
    {
        for (int head = 0; head < rootCount; head++)
        {
            int origin = roots[head];
            if (!SameLife(origin)) continue;
            int stack = Mathf.Clamp(nodes[origin].Stacks, 1, 5);
            Vector3 point = nodes[origin].Point;
            float radius = 1f + .2f * (stack - 1);
            nodes[origin].Status?.ConsumeForDischarge(WeaponElement.Fire, out _);
            explosionVfx?.Invoke(point, radius); // Same radius contract as delayed propagation.
            float right = point.x + radius + largestRadius;
            for (int i = NextCandidate(LowerBound(point.x - radius - largestRadius)); i < count && nodes[i].Point.x <= right; i = NextCandidate(i + 1))
            {
                CandidateChecks++;
                if (!Valid(i)) { RetireCandidate(i); continue; }
                if (i == origin || nodes[i].Hits >= 2 || !InRange(i, point, radius)) continue;
                if (Damage(i, blastDamage * (.1f + .1f * stack) * (1f + gemAttack.Modifiers.ExplosionDamage / 100f), source)) Queue(i);
            }
        }
    }
    public void ExecuteLightning(float blastDamage, float energy, GameObject source, Action<Vector3, Vector3> linkVfx)
    {
        int extra = gemAttack.Modifiers.ChainHops + (energy >= .99999f ? 2 : energy >= .5f ? 1 : 0);
        float radius = Mathf.Lerp(2.5f, 4f, energy);
        for (int r = 0; r < rootCount; r++) { pathLengths[r] = 1; paths[r * 8] = roots[r]; }
        float damage = blastDamage;
        for (int hop = 0; hop < 7; hop++, damage *= .8f)
            for (int r = 0; r < rootCount; r++)
            {
                int origin = roots[r], length = pathLengths[r];
                if (length == 0 || hop >= nodes[origin].Stacks + extra || !SameLife(origin)) continue;
                Vector3 point = nodes[paths[r * 8 + length - 1]].Point;
                int nearest = -1;
                float best = float.PositiveInfinity, right = point.x + radius + largestRadius;
                for (int i = NextCandidate(LowerBound(point.x - radius - largestRadius)); i < count && nodes[i].Point.x <= right; i = NextCandidate(i + 1))
                {
                    CandidateChecks++;
                    float distance = (nodes[i].Point - point).sqrMagnitude;
                    if (distance >= best) continue;
                    if (!Valid(i)) { RetireCandidate(i); continue; }
                    if (nodes[i].Hits >= 2 || !InRange(i, point, radius)) continue;
                    bool visited = false;
                    for (int p = 0; p < length; p++) if (paths[r * 8 + p] == i) { visited = true; break; }
                    if (visited) continue;
                    nearest = i; best = distance;
                }
                if (nearest < 0) { pathLengths[r] = 0; continue; }
                paths[r * 8 + length] = nearest; pathLengths[r]++;
                float fraction = OverburstElementTuning.Current.LightningChainFraction(nodes[origin].Stacks);
                if (Damage(nearest, damage * fraction * (1f + gemAttack.Modifiers.ChainDamage / 100f), source))
                {
                    linkVfx?.Invoke(point, nodes[nearest].Point);
                    PlayLightningHopFeedback(nearest, point);
                }
            }
    }
}
