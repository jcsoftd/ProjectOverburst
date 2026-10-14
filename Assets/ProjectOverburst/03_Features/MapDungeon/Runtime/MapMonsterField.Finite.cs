using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Profiling;

public sealed partial class MapMonsterField
{
    static readonly ProfilerMarker RefreshGroundMarker = new ProfilerMarker("OVERBURST.MapSpawnFinite.Refresh.Detail.Ground");
    static readonly ProfilerMarker RefreshReturnMarker = new ProfilerMarker("OVERBURST.MapSpawnFinite.Refresh.Detail.Return");
    sealed class SpawnGroup
    {
        public Vector3 Point;
        public MapCombatGeometry.Area Ground;
    }
    sealed class Slot
    {
        public MapRunRoster.Member Member;
        public EnemyActor Actor;
        public CombatHealth BoundHealth;
        public uint ActorLease;
        public int RosterLease, Cursor, Failures, SpawnAreaIndex;
        public int Order, ActiveIndex = -1;
        public bool SpawnQueued;
        public bool PositionDeferred;
        public LinkedListNode<Slot> ReserveNode;
        public bool Released, EverSpawned, Frozen, WasKinematic;
        public float HealthRatio = -1, RetryAt;
        public Vector3 Position, Safe;
        public float Radius, Height;
        public Rigidbody Body;
        public SpawnGroup Group;
        public int GroupCursor;
    }
    MapRunRoster finiteRoster;
    MapRunRoster.Area finiteArea;
    readonly List<Slot> slots = new List<Slot>();
    readonly List<Slot> activeSlots = new List<Slot>();
    readonly List<Slot> spawnSlots = new List<Slot>();
    readonly List<Slot> roadSlots = new List<Slot>();
    readonly LinkedList<Slot> reserves = new LinkedList<Slot>();
    readonly Dictionary<EnemyDefinition, Queue<Slot>> reservesByDefinition = new Dictionary<EnemyDefinition, Queue<Slot>>();
    readonly Dictionary<CombatHealth, Slot> bindings = new Dictionary<CombatHealth, Slot>();
    readonly Collider[] spawnOverlaps = new Collider[32];
    NavMeshPath spawnPath;
    List<Vector3> approaches;
    readonly List<MapCombatGeometry.Area> reinforcementNeighbours = new List<MapCombatGeometry.Area>();
    System.Random finiteRandom;
    float areaRadius, nextPackAt;
    bool finiteAwake;
    int nextFiniteSlot;
    int initialRemaining, releasedReserves, refreshCursor;
    float nextSpawnAttempt;
    public MapRunRoster.Area FiniteArea => finiteArea;
    public int LocalFightLimit { get; private set; }
    public int ReinforcementReleased { get; private set; }
    public int NeighbourSpawnedCount { get; private set; }
    public bool PrepareAtEntry { get; internal set; }
    public bool InitialPlaced => initialRemaining == 0;
    internal float FinitePriority { get; private set; }
    internal bool HasFiniteSpawnWork => !finiteArea.Cleared && spawnSlots.Count > 0 && Time.unscaledTime >= nextSpawnAttempt;
    public int FiniteActiveCount => bindings.Count;
    public IReadOnlyList<Vector3> FiniteApproaches => approaches;
    public void CollectLivingActors(List<EnemyActor> result)
    {
        foreach (var slot in activeSlots)
            if (slot.Member.State == MapRunRoster.Life.Active && Valid(slot) && !slot.Actor.Health.IsDead) result.Add(slot.Actor);
    }
    public bool TryGetActor(int memberIndex, out EnemyActor actor)
    {
        foreach (var slot in activeSlots)
            if (slot.Member.Index == memberIndex && slot.Member.State == MapRunRoster.Life.Active && Valid(slot)) { actor = slot.Actor; return true; }
        actor = null; return false;
    }
    internal void ConfigureFinite(MapRunRoster roster, MapRunRoster.Area area, MapSpawnBudget sharedBudget)
    {
        finiteRoster = roster; finiteArea = area; budget = sharedBudget;
        finiteRoster.DeathRecorded += ApplyNaturalProgress;
        spawnPath = new NavMeshPath();
        finiteRandom = new System.Random(unchecked(seed ^ 0x310AB));
        LocalFightLimit = area.BasePopulation <= 60 ? finiteRandom.Next(12, 17)
            : area.BasePopulation <= 110 ? finiteRandom.Next(18, 27) : finiteRandom.Next(28, 41);
        foreach (var point in area.Ground.Points) areaRadius = Mathf.Max(areaRadius, Vector3.Distance(point, area.Ground.Center));
        approaches = MapCombatGeometry.Approaches(area.Ground);
        // Adjacent courts come from this generated terrain's links, never fixed coordinates.
        void AddNeighbour(int index)
        {
            foreach (var other in roster.Areas)
                if (other.Ground.Index == index && other.Count > 0 && !reinforcementNeighbours.Contains(other.Ground))
                { reinforcementNeighbours.Add(other.Ground); break; }
        }
        var source = area.Ground.Source;
        if (source != null && source.cave)
            foreach (var passage in source.cave.passages)
                if (passage.a == area.Ground.Index) AddNeighbour(passage.b);
                else if (passage.b == area.Ground.Index) AddNeighbour(passage.a);
        if (source != null && source.desert)
            foreach (var trail in source.desert.layout.trails)
                if (trail.from == area.Ground.Index) AddNeighbour(trail.to);
                else if (trail.to == area.Ground.Index) AddNeighbour(trail.from);
        reinforcementNeighbours.Sort((a,b) => (a.Center-area.Ground.Center).sqrMagnitude.CompareTo((b.Center-area.Ground.Center).sqrMagnitude));
        for (int i = area.FirstMember; i < area.FirstMember + area.Count; i++)
        {
            var member = roster.Members[i];
            var body = MapCombatGeometry.BodyFor(member.Definition);
            slots.Add(new Slot { Member = member, Released = member.Initial, Position = member.PlannedPosition,
                Safe = member.PlannedPosition, Radius = body.Radius, Height = body.Height });
        }
        // Keep neighbours together in the limited initial queue; reserves retain their seeded order.
        slots.Sort((a, b) => a.Member.Initial != b.Member.Initial ? (a.Member.Initial ? -1 : 1)
            : a.Member.Initial ? a.Position.z.CompareTo(b.Position.z) : a.Member.Index.CompareTo(b.Member.Index));
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i]; slot.Order = i;
            if (slot.Member.PlannedOnRoad) roadSlots.Add(slot);
            if (slot.Member.Initial) { initialRemaining++; QueueSpawn(slot); }
            else
            {
                slot.ReserveNode = reserves.AddLast(slot);
                if (!reservesByDefinition.TryGetValue(slot.Member.Definition, out var queue))
                    reservesByDefinition.Add(slot.Member.Definition, queue = new Queue<Slot>());
                queue.Enqueue(slot);
            }
        }
    }
    internal bool NearForInitial(Vector3 player)
    {
        if (FlatDistance(player, transform.position) <= areaRadius + 35) return true;
        foreach (var slot in roadSlots)
            if (slot.Member.PlannedOnRoad && FlatDistance(player, slot.Member.PlannedPosition) <= 35) return true;
        return false;
    }
    internal float Priority(Vector3 player) => FlatDistance(player, transform.position) - (InitialPlaced ? 0 : 40);
    internal void CacheFinitePriority(Vector3 player) => FinitePriority = Priority(player);
    static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y; return Vector3.Distance(a, b); }
    internal int CountNear(Vector3 player, float range)
    {
        int count = 0;
        foreach (var slot in activeSlots) if (Valid(slot) && !slot.Actor.Health.IsDead && FlatDistance(slot.Actor.transform.position, player) <= range) count++;
        return count;
    }
    static bool Valid(Slot slot) => slot.Actor && slot.Actor.IsLeased && slot.Actor.LeaseVersion == slot.ActorLease
        && slot.Actor.gameObject.activeInHierarchy;
    internal void ReconcileFinite()
    {
        for (int i = activeSlots.Count - 1; i >= 0; i--)
        {
            var slot = activeSlots[i];
            if (!Valid(slot)) { ReturnSlot(slot, false); continue; }
            if (!slot.Actor.Health.IsDead)
            {
                var position = slot.Actor.transform.position;
                // An urgent fall cannot wait behind the distributed routine ground checks.
                if (position.y < slot.Safe.y - 3 || FlatDistance(position, slot.Safe) > 5) CheckFiniteGround(slot);
            }
        }
    }
    internal bool IsSeparated(Vector3 point, float radius)
    {
        foreach (var slot in activeSlots)
            if (Valid(slot) && !slot.Actor.Health.IsDead && Mathf.Abs(slot.Actor.transform.position.y - point.y) < 3
                && !MapCombatGeometry.BodiesSeparated(slot.Actor.transform.position, slot.Radius, point, radius)) return false;
        return true;
    }
    void CheckFiniteGround(Slot slot)
    {
        var actor = slot.Actor; var position = actor.transform.position;
        if (NavMesh.SamplePosition(position, out var nav, .75f, NavMesh.AllAreas) && Mathf.Abs(nav.position.y - position.y) < .75f)
            slot.Safe = nav.position;
        else if (position.y < slot.Safe.y - 3 || FlatDistance(position, slot.Safe) > 5)
        {
            ActorTeleportUtility.TeleportSafely(actor.transform, slot.Safe + Vector3.up * .06f, actor.transform.rotation);
            actor.AI?.SetHomePosition(slot.Safe); budget.BoundaryRecoveries++;
        }
    }
    internal bool RefreshFinite(Vector3 player, bool preparing, long started)
    {
        if (!preparing && MapCombatGeometry.Contains(finiteArea.Ground, player)) triggered = true;
        while (refreshCursor < activeSlots.Count)
        {
            if (!MapSpawnBudget.HasFiniteTime(started)) return false;
            var slot = activeSlots[refreshCursor++];
            if (!Valid(slot)) { using (RefreshReturnMarker.Auto()) ReturnSlot(slot, false); continue; }
            var actor = slot.Actor;
            slot.HealthRatio = actor.Health.CurrentHp / actor.Health.MaxHp;
            if (actor.Health.IsDead) continue;
            if (slot.HealthRatio < 1f || actor.AI && actor.AI.IsAggroActive) triggered = true;
            if (preparing) continue;
            using (RefreshGroundMarker.Auto()) CheckFiniteGround(slot);
            if (FlatDistance(actor.transform.position, player) > 80 && !budget.InView(actor.transform.position, .5f))
            { slot.Position = actor.transform.position; using (RefreshReturnMarker.Auto()) ReturnSlot(slot, true); budget.StreamReturns++; }
        }
        refreshCursor = 0; return true;
    }
    internal void PrepareReinforcement(Vector3 player, int nearby, bool preparing)
    {
        foreach (var slot in activeSlots)
            if (!slot.Member.Initial && Valid(slot) && !slot.Actor.Health.IsDead && FlatDistance(slot.Actor.transform.position, player) > 24) nearby++;
        if (preparing || finiteArea.Cleared || !triggered || !NearForInitial(player) || !InitialPlaced || nearby >= LocalFightLimit * .6f) return;
        if (releasedReserves > 0) return;
        if (nearby > 0 && Time.unscaledTime < nextPackAt) return;
        Slot first = reserves.First?.Value;
        if (first == null) return;
        var group = new SpawnGroup();
        int remaining = Mathf.Min(LocalFightLimit - nearby, MapCombatGeometry.GroupSize(first.Member.Tier));
        var queue = reservesByDefinition[first.Member.Definition];
        while (remaining > 0 && queue.Count > 0)
        {
            var slot = queue.Dequeue(); reserves.Remove(slot.ReserveNode); slot.ReserveNode = null;
            slot.Group = group; slot.Released = true; QueueSpawn(slot); remaining--; ReinforcementReleased++;
        }
        nextPackAt = Time.unscaledTime + .8f;
    }
    internal bool TrySpawnFinite(Vector3 player, bool preparing, long started, out bool spawned)
    {
        spawned = false;
        if (finiteArea.Cleared || (preparing ? !PrepareAtEntry : !NearForInitial(player))) return false;
        if (!HasFiniteSpawnWork) return false;
        Slot chosen = null, wrapped = null; float earliest = float.PositiveInfinity;
        // A visible returning actor must not monopolize the frame budget ahead of reserves.
        foreach (var slot in spawnSlots)
        {
            earliest = Mathf.Min(earliest, slot.RetryAt);
            if (slot.RetryAt > Time.unscaledTime) continue;
            if (wrapped == null) wrapped = slot;
            if (slot.Order >= nextFiniteSlot) { chosen = slot; break; }
        }
        chosen = chosen ?? wrapped;
        if (chosen == null) { nextSpawnAttempt = earliest; return false; }
        nextFiniteSlot = (chosen.Order + 1) % slots.Count;
        if (!preparing && !chosen.Member.Initial && budget.LocalAlive(player) >= LocalFightLimit) return false;
        if (!TryFinitePositionWithinBudget(chosen, player, preparing, started, out var position))
        {
            if (chosen.PositionDeferred) return false;
            chosen.RetryAt = Time.unscaledTime + .15f; chosen.Failures++; budget.SpawnRetries++;
            return true;
        }
        Vector3 direction = player - position; direction.y = 0;
        var request = new EnemySpawnRequest(chosen.Member.Definition, position + Vector3.up * .06f,
            direction.sqrMagnitude > .01f ? Quaternion.LookRotation(direction) : Quaternion.identity,
            PlayerContext.Instance?.CurrentActor?.transform, gameObject, transform, transform, 1, 1,
            unchecked(seed + chosen.Member.Index * 7919), context);
        if (!service.TrySpawn(request, out var actor))
        {
            chosen.RetryAt = Time.unscaledTime + .5f; chosen.Failures++; budget.SpawnRetries++;
            return true;
        }
        if (!finiteRoster.TryActivate(chosen.Member.Index, out chosen.RosterLease)) { service.Release(actor); return true; }
        chosen.Actor = actor; chosen.ActorLease = actor.LeaseVersion; chosen.Safe = position;
        if (chosen.Group != null && chosen.Group.Ground == null)
        {
            chosen.Group.Point = position;
            chosen.Group.Ground = chosen.SpawnAreaIndex == finiteArea.Ground.Index ? finiteArea.Ground
                : reinforcementNeighbours.Find(area => area.Index == chosen.SpawnAreaIndex);
        }
        chosen.Body = actor.GetComponent<Rigidbody>(); chosen.WasKinematic = chosen.Body && chosen.Body.isKinematic;
        actor.ApplyNaturalProgress(finiteRoster.NaturalProgressMultiplier);
        if (chosen.HealthRatio > 0) actor.Health.RestoreLivingHealth(chosen.HealthRatio * actor.Health.MaxHp);
        chosen.HealthRatio = actor.Health.CurrentHp / actor.Health.MaxHp;
        if (!chosen.EverSpawned && chosen.Member.Initial) initialRemaining--;
        chosen.EverSpawned = true; chosen.Failures = 0;
        DequeueSpawn(chosen); chosen.ActiveIndex = activeSlots.Count; activeSlots.Add(chosen);
        chosen.BoundHealth = actor.Health; budget.InvalidateFiniteCounts();
        bindings.Add(actor.Health, chosen); actor.Health.OnDead += FiniteDead; actor.Health.OnHealthChanged += FiniteHealth;
        SpawnedCount++; spawned = true;
        if (chosen.SpawnAreaIndex != finiteArea.Ground.Index) NeighbourSpawnedCount++;
        Freeze(chosen, preparing || !finiteAwake);
        if (!preparing && !chosen.Member.Initial) actor.AI?.RequestAggro(PlayerContext.Instance?.CurrentActor?.transform);
        Physics.SyncTransforms();
        return true;
    }
    bool TryFinitePosition(Slot slot, Vector3 player, bool preparing, out Vector3 position)
        => TryFinitePositionWithinBudget(slot, player, preparing, 0, out position);
    bool TryFinitePositionWithinBudget(Slot slot, Vector3 player, bool preparing, long started, out Vector3 position)
    {
        position = default; slot.PositionDeferred = false;
        if (finiteArea.Cleared) return false;
        bool Expired()
        {
            slot.PositionDeferred = started > 0 && !MapSpawnBudget.HasFiniteTime(started);
            return slot.PositionDeferred;
        }
        bool Clear(MapCombatGeometry.Area ground, Vector3 candidate, bool approach, out Vector3 result)
        {
            result = default;
            if (finiteRoster.Areas[ground.Index].Cleared) return false;
            if (FlatDistance(candidate, player) < MapCombatGeometry.EntrySafety + (preparing ? 0 : slot.Radius)) return false;
            if (!preparing && budget.InView(candidate, .12f, slot.Radius, slot.Height)) return false;
            if (!MapCombatGeometry.TryRuntimePoint(ground, slot.Member.Definition, candidate, approach, spawnOverlaps, spawnPath, out result)) return false;
            // Approach points and restored actors can belong to a different platform than their roster owner.
            foreach (var area in finiteRoster.Areas)
                if ((area.Ground.Index == finiteRoster.EntryAreaIndex || area.Cleared)
                    && MapCombatGeometry.Contains(area.Ground, result)) return false;
            if (ground != finiteArea.Ground && (!NavMesh.CalculatePath(result, finiteArea.Ground.Center, NavMesh.AllAreas, spawnPath)
                || spawnPath.status != NavMeshPathStatus.PathComplete)) return false;
            if (!budget.IsSeparated(result, slot.Radius)) return false;
            slot.SpawnAreaIndex = ground.Index;
            return true;
        }
        if (!slot.EverSpawned && slot.Group?.Ground != null)
            for (int i = 0; i < 24; i++)
            {
                if (Expired()) return false;
                int n = slot.GroupCursor++;
                float angle = (slot.Member.Index * 2.399963f + n * Mathf.PI * .25f);
                float distance = (slot.Radius * 2 + MapCombatGeometry.BodyGap + .12f) * (1 + n % 24 / 8);
                var candidate = slot.Group.Point + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * distance;
                if (Clear(slot.Group.Ground, candidate, false, out position)) return true;
            }
        if (slot.Member.Initial && slot.Cursor == 0)
        {
            if (Expired()) return false;
            // Retain G02's packed positions during entry; offsetting one can invalidate its neighbours.
            if (Clear(finiteArea.Ground, slot.Position, slot.Member.PlannedOnRoad, out position)) return true;
            if (preparing) return false;
        }
        var points = finiteArea.Ground.Points;
        int total = points.Count + approaches.Count;
        for (int i = 0; i < Mathf.Min(8, total); i++)
        {
            if (Expired()) return false;
            int index = (slot.Cursor++ + slot.Member.Index * 17) % total;
            bool approach = index >= points.Count;
            var candidate = approach ? approaches[index - points.Count] : points[index];
            if (Clear(finiteArea.Ground, candidate, approach, out position)) return true;
        }
        if (!preparing && !slot.Member.Initial)
            foreach (var neighbour in reinforcementNeighbours)
                for (int i = 0; i < Mathf.Min(8, neighbour.Points.Count); i++)
                {
                    if (Expired()) return false;
                    var candidate = neighbour.Points[(slot.Cursor++ + slot.Member.Index * 17) % neighbour.Points.Count];
                    if (FlatDistance(candidate, finiteArea.Ground.Center) > 65) continue;
                    if (Clear(neighbour, candidate, false, out position)) return true;
                }
        position = default; return false;
    }
    internal void SetFiniteAwake(bool awake)
    {
        if (finiteAwake == awake) return;
        finiteAwake = awake;
        foreach (var slot in activeSlots) if (Valid(slot) && !slot.Actor.Health.IsDead) Freeze(slot, !awake);
    }
    void Freeze(Slot slot, bool frozen)
    {
        if (slot.Frozen == frozen) return;
        slot.Frozen = frozen; var actor = slot.Actor;
        actor.Health.SetDamageDeathPrevention(this, frozen);
        if (actor.AI) actor.AI.enabled = !frozen;
        if (actor.AbilityController) actor.AbilityController.enabled = !frozen;
        if (actor.Melee) actor.Melee.enabled = !frozen;
        if (actor.Movement) { actor.Movement.StopMovement(); actor.Movement.enabled = !frozen; }
        if (actor.Animator) actor.Animator.enabled = !frozen;
        if (slot.Body) slot.Body.isKinematic = frozen || slot.WasKinematic;
    }
    void FiniteHealth(CombatHealth health, float current, float maximum)
    {
        if (bindings.TryGetValue(health, out var slot) && Valid(slot)) slot.HealthRatio = current / maximum;
    }
    void FiniteDead(CombatHealth health, DamageInfo hit)
    {
        if (!bindings.TryGetValue(health, out var slot) || !Valid(slot) || !health.IsDead
            || !finiteRoster.TryRecordDeath(slot.Member.Index, slot.RosterLease)) return;
        Unbind(slot); triggered = true;
        if (!context.CanGrantRewards || !hit.source || !hit.source.GetComponentInParent<PlayerActorRuntime>()) return;
        var item = MapDropPolicy.Roll(mapDefinition, registry, map, slot.Member.Tier, context.RunId);
        if (item != null) WorldItemDropFactory.CreateWorldPickup(item, health.transform.position + Vector3.up * .4f,
            PlayerAccountInventoryService.SharedInventory, PlayerContext.Instance?.CurrentActor?.transform);
    }
    void Unbind(Slot slot)
    {
        var health = slot.BoundHealth;
        if (health) { health.OnDead -= FiniteDead; health.OnHealthChanged -= FiniteHealth; }
        if (!ReferenceEquals(health, null)) bindings.Remove(health);
        slot.BoundHealth = null;
        if (slot.ActiveIndex >= 0)
        {
            int index = slot.ActiveIndex, last = activeSlots.Count - 1;
            if (index != last) { activeSlots[index] = activeSlots[last]; activeSlots[index].ActiveIndex = index; }
            activeSlots.RemoveAt(last); slot.ActiveIndex = -1;
            if (refreshCursor > index) refreshCursor = index;
            budget.InvalidateFiniteCounts();
        }
    }
    void ReturnSlot(Slot slot, bool release)
    {
        bool valid = Valid(slot); Unbind(slot);
        if (valid) { slot.Actor.Health.SetDamageDeathPrevention(this, false); Freeze(slot, false); }
        if (finiteRoster.TryReturn(slot.Member.Index, slot.RosterLease)) QueueSpawn(slot);
        if (release && valid && service) service.Release(slot.Actor);
        slot.Actor = null; slot.Body = null; slot.Frozen = false;
    }
    void ApplyNaturalProgress()
    {
        foreach (var pair in bindings)
            if (Valid(pair.Value) && !pair.Key.IsDead) pair.Value.Actor.ApplyNaturalProgress(finiteRoster.NaturalProgressMultiplier);
    }
    void OnDestroy()
    {
        if (finiteRoster != null) finiteRoster.DeathRecorded -= ApplyNaturalProgress;
    }
    void ReleaseFinite()
    {
        if (finiteRoster == null) return;
        while (activeSlots.Count > 0) ReturnSlot(activeSlots[activeSlots.Count - 1], true);
        bindings.Clear();
    }
    void QueueSpawn(Slot slot)
    {
        if (slot.SpawnQueued) return;
        int low = 0, high = spawnSlots.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (spawnSlots[middle].Order < slot.Order) low = middle + 1; else high = middle;
        }
        spawnSlots.Insert(low, slot); slot.SpawnQueued = true; nextSpawnAttempt = 0;
        if (!slot.Member.Initial) releasedReserves++;
    }
    void DequeueSpawn(Slot slot)
    {
        if (!slot.SpawnQueued) return;
        spawnSlots.Remove(slot); slot.SpawnQueued = false; nextSpawnAttempt = 0;
        if (!slot.Member.Initial) releasedReserves--;
    }
}
