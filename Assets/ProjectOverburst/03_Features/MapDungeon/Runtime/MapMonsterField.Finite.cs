using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public sealed partial class MapMonsterField
{
    sealed class Slot
    {
        public MapRunRoster.Member Member;
        public EnemyActor Actor;
        public uint ActorLease;
        public int RosterLease, Cursor, Failures;
        public bool Released, EverSpawned, Frozen, WasKinematic;
        public float HealthRatio = -1, RetryAt;
        public Vector3 Position, Safe;
        public float Radius, Height;
        public Rigidbody Body;
    }
    MapRunRoster finiteRoster;
    MapRunRoster.Area finiteArea;
    readonly List<Slot> slots = new List<Slot>();
    readonly Dictionary<CombatHealth, Slot> bindings = new Dictionary<CombatHealth, Slot>();
    readonly Collider[] spawnOverlaps = new Collider[32];
    NavMeshPath spawnPath;
    List<Vector3> approaches;
    System.Random finiteRandom;
    float areaRadius, nextPackAt;
    bool finiteAwake;
    public MapRunRoster.Area FiniteArea => finiteArea;
    public int LocalFightLimit { get; private set; }
    public int ReinforcementReleased { get; private set; }
    public bool PrepareAtEntry { get; internal set; }
    public bool InitialPlaced
    {
        get { foreach (var slot in slots) if (slot.Member.Initial && !slot.EverSpawned) return false; return true; }
    }
    public int FiniteActiveCount => bindings.Count;
    public IReadOnlyList<Vector3> FiniteApproaches => approaches;
    public void CollectLivingActors(List<EnemyActor> result)
    {
        foreach (var slot in slots)
            if (slot.Member.State == MapRunRoster.Life.Active && Valid(slot) && !slot.Actor.Health.IsDead) result.Add(slot.Actor);
    }
    public bool TryGetActor(int memberIndex, out EnemyActor actor)
    {
        foreach (var slot in slots)
            if (slot.Member.Index == memberIndex && slot.Member.State == MapRunRoster.Life.Active && Valid(slot)) { actor = slot.Actor; return true; }
        actor = null; return false;
    }
    internal void ConfigureFinite(MapRunRoster roster, MapRunRoster.Area area, MapSpawnBudget sharedBudget)
    {
        finiteRoster = roster; finiteArea = area; budget = sharedBudget;
        finiteRoster.DeathRecorded += ApplyNaturalProgress;
        spawnPath = new NavMeshPath();
        finiteRandom = new System.Random(unchecked(seed ^ 0x310AB));
        LocalFightLimit = area.BasePopulation <= 90 ? finiteRandom.Next(18, 25)
            : area.BasePopulation <= 170 ? finiteRandom.Next(28, 41) : finiteRandom.Next(40, 61);
        foreach (var point in area.Ground.Points) areaRadius = Mathf.Max(areaRadius, Vector3.Distance(point, area.Ground.Center));
        approaches = MapCombatGeometry.Approaches(area.Ground);
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
    }
    internal bool NearForInitial(Vector3 player) => FlatDistance(player, transform.position) <= areaRadius + 35;
    internal float Priority(Vector3 player) => FlatDistance(player, transform.position) - (InitialPlaced ? 0 : 40);
    static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y; return Vector3.Distance(a, b); }
    internal int CountNear(Vector3 player, float range)
    {
        int count = 0;
        foreach (var slot in slots) if (Valid(slot) && !slot.Actor.Health.IsDead && FlatDistance(slot.Actor.transform.position, player) <= range) count++;
        return count;
    }
    static bool Valid(Slot slot) => slot.Actor && slot.Actor.IsLeased && slot.Actor.LeaseVersion == slot.ActorLease
        && slot.Actor.gameObject.activeInHierarchy;
    internal void ReconcileFinite()
    {
        foreach (var slot in slots)
            if (slot.Member.State == MapRunRoster.Life.Active && !Valid(slot)) ReturnSlot(slot, false);
    }
    internal bool IsSeparated(Vector3 point, float radius)
    {
        foreach (var slot in slots)
            if (Valid(slot) && !slot.Actor.Health.IsDead && Mathf.Abs(slot.Actor.transform.position.y - point.y) < 3
                && !MapCombatGeometry.BodiesSeparated(slot.Actor.transform.position, slot.Radius, point, radius)) return false;
        return true;
    }
    internal void RefreshFinite(Vector3 player, bool preparing)
    {
        foreach (var slot in slots)
        {
            if (slot.Member.State != MapRunRoster.Life.Active) continue;
            if (!Valid(slot)) { ReturnSlot(slot, false); continue; }
            var actor = slot.Actor;
            slot.HealthRatio = actor.Health.CurrentHp / actor.Health.MaxHp;
            if (actor.Health.IsDead) continue;
            if (slot.HealthRatio < 1f || actor.AI && actor.AI.IsAggroActive) triggered = true;
            if (preparing) continue;
            var position = actor.transform.position;
            if (NavMesh.SamplePosition(position, out var nav, .75f, NavMesh.AllAreas) && Mathf.Abs(nav.position.y - position.y) < .75f)
                slot.Safe = nav.position;
            else if (position.y < slot.Safe.y - 3 || FlatDistance(position, slot.Safe) > 5)
            {
                // Recover the same living lease; no synthetic lethal damage or drop callback.
                ActorTeleportUtility.TeleportSafely(actor.transform, slot.Safe + Vector3.up * .06f, actor.transform.rotation);
                actor.AI?.SetHomePosition(slot.Safe); budget.BoundaryRecoveries++;
            }
            if (FlatDistance(actor.transform.position, player) > 80 && !budget.InView(actor.transform.position, .5f))
            { slot.Position = actor.transform.position; ReturnSlot(slot, true); budget.StreamReturns++; }
        }
        if (!preparing && MapCombatGeometry.Contains(finiteArea.Ground, player)) triggered = true;
    }
    internal void PrepareReinforcement(Vector3 player, int nearby, bool preparing)
    {
        foreach (var slot in slots)
            if (!slot.Member.Initial && Valid(slot) && !slot.Actor.Health.IsDead && FlatDistance(slot.Actor.transform.position, player) > 24) nearby++;
        if (preparing || !triggered || !NearForInitial(player) || !InitialPlaced || nearby >= LocalFightLimit * .6f) return;
        foreach (var slot in slots) if (!slot.Member.Initial && slot.Released && slot.Member.State == MapRunRoster.Life.Pending) return;
        if (nearby > 0 && Time.unscaledTime < nextPackAt) return;
        int pack = finiteRandom.Next(6, 13);
        int amount = Mathf.Min(LocalFightLimit - nearby, pack * (nearby == 0 ? 2 : 1));
        foreach (var slot in slots)
        {
            if (amount <= 0) break;
            if (slot.Released || slot.Member.State != MapRunRoster.Life.Pending) continue;
            slot.Released = true; amount--; ReinforcementReleased++;
        }
        nextPackAt = Time.unscaledTime + .8f;
    }
    internal bool TrySpawnFinite(Vector3 player, bool preparing, out bool spawned)
    {
        spawned = false;
        if (preparing ? !PrepareAtEntry : !NearForInitial(player)) return false;
        Slot chosen = null;
        foreach (var slot in slots)
        {
            if (slot.Released && slot.Member.State == MapRunRoster.Life.Pending && slot.RetryAt <= Time.unscaledTime)
            { chosen = slot; break; }
        }
        if (chosen == null) return false;
        if (!preparing && !chosen.Member.Initial && budget.LocalAlive(player) >= LocalFightLimit) return false;
        if (!TryFinitePosition(chosen, player, preparing, out var position))
        {
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
        chosen.Body = actor.GetComponent<Rigidbody>(); chosen.WasKinematic = chosen.Body && chosen.Body.isKinematic;
        actor.ApplyNaturalProgress(finiteRoster.NaturalProgressMultiplier);
        if (chosen.HealthRatio > 0) actor.Health.RestoreLivingHealth(chosen.HealthRatio * actor.Health.MaxHp);
        chosen.HealthRatio = actor.Health.CurrentHp / actor.Health.MaxHp; chosen.EverSpawned = true; chosen.Failures = 0;
        bindings.Add(actor.Health, chosen); actor.Health.OnDead += FiniteDead; actor.Health.OnHealthChanged += FiniteHealth;
        SpawnedCount++; spawned = true;
        Freeze(chosen, preparing || !finiteAwake);
        if (!preparing && !chosen.Member.Initial) actor.AI?.RequestAggro(PlayerContext.Instance?.CurrentActor?.transform);
        Physics.SyncTransforms();
        return true;
    }
    bool TryFinitePosition(Slot slot, Vector3 player, bool preparing, out Vector3 position)
    {
        bool Clear(Vector3 candidate, bool approach, out Vector3 result)
        {
            result = default;
            if (FlatDistance(candidate, player) < MapCombatGeometry.EntrySafety + (preparing ? 0 : slot.Radius)) return false;
            if (!preparing && budget.InView(candidate, .12f, slot.Radius, slot.Height)) return false;
            if (!MapCombatGeometry.TryRuntimePoint(finiteArea.Ground, slot.Member.Definition, candidate, approach, spawnOverlaps, spawnPath, out result)) return false;
            return budget.IsSeparated(result, slot.Radius);
        }
        if (slot.Member.Initial && slot.Cursor == 0)
        {
            // Retain G02's packed positions during entry; offsetting one can invalidate its neighbours.
            if (Clear(slot.Position, false, out position)) return true;
            if (preparing) return false;
        }
        var points = finiteArea.Ground.Points;
        int total = points.Count + approaches.Count;
        for (int i = 0; i < Mathf.Min(8, total); i++)
        {
            int index = (slot.Cursor++ + slot.Member.Index * 17) % total;
            bool approach = index >= points.Count;
            var candidate = approach ? approaches[index - points.Count] : points[index];
            if (Clear(candidate, approach, out position)) return true;
        }
        position = default; return false;
    }
    internal void SetFiniteAwake(bool awake)
    {
        if (finiteAwake == awake) return;
        finiteAwake = awake;
        foreach (var slot in slots) if (Valid(slot) && !slot.Actor.Health.IsDead) Freeze(slot, !awake);
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
        if (!slot.Actor) return;
        var health = slot.Actor.Health;
        if (health) { health.OnDead -= FiniteDead; health.OnHealthChanged -= FiniteHealth; bindings.Remove(health); }
    }
    void ReturnSlot(Slot slot, bool release)
    {
        bool valid = Valid(slot); Unbind(slot);
        if (valid) { slot.Actor.Health.SetDamageDeathPrevention(this, false); Freeze(slot, false); }
        finiteRoster.TryReturn(slot.Member.Index, slot.RosterLease);
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
        foreach (var slot in slots) if (slot.Member.State == MapRunRoster.Life.Active) ReturnSlot(slot, true);
        bindings.Clear();
    }
}
