using System.Collections.Generic;
using UnityEngine;

public enum EarthZoneKind
{
    Basic = 0,
    LavaEruption = 1,
    Mud = 2,
    Crystallization = 3,
    MagneticField = 4
}

public static class EarthElementZoneTuning
{
    public const int MaximumZonesPerOwner = 3;
    public const float ExtraZoneChance = 0.1f;
    public const float ExtraZoneOffset = 0.75f;
    public const float Lifetime = 6f;
    public const float Radius = 1.75f;
    public const float TickInterval = 1f;
    public const float BasicCoefficient = 0.04f;
    public const float LavaBurstCoefficient = 0.2f;
    public const float LavaTickCoefficient = 0.05f;
    public const float MudTickCoefficient = 0.025f;
    public const float MudMoveSpeedMultiplier = 0.85f;
    public const float CrystallizationTickCoefficient = 0.025f;
    public const int CrystalCount = 3;
    public const float CrystalRadius = 0.9f;
    public const float CrystalDamageCoefficient = 0.06f;
    public const float MagneticTickCoefficient = 0.025f;
    public const float MagneticPulseInterval = 1f;
    public const float MagneticPullDistance = 0.25f;
    public static readonly float[] CrystalDelays = { 0.75f, 0.95f, 1.15f };
}

[DisallowMultipleComponent]
public sealed class EarthElementZoneRuntimeService : MonoBehaviour
{
    private sealed class AttackSequenceState
    {
        public long Key;
        public float LastSeenAt;
        public bool CreatedBasic;
        public readonly List<int> TargetIds = new List<int>(8);
    }

    private sealed class Zone
    {
        public int Id;
        public EarthZoneKind Kind;
        public GameObject CapOwner;
        public GameObject DamageOwner;
        public CombatTarget DamageOwnerTarget;
        public string WeaponRuntimeInstanceId;
        public Vector3 Center;
        public float DamageSnapshot;
        public float CreatedAt;
        public float ExpiresAt;
        public float NextTickAt;
        public float NextPulseAt;
    }

    private struct PendingCrystal
    {
        public int Id;
        public GameObject DamageOwner;
        public CombatTarget DamageOwnerTarget;
        public string WeaponRuntimeInstanceId;
        public Vector3 Center;
        public float DamageSnapshot;
        public float ExplodesAt;
    }

    private const float SequenceRetention = 2f;
    private const float MudMembershipInterval = 0.1f;
    private const float GroundRayHeight = 3f;
    private const float GroundRayDistance = 8f;
    private static EarthElementZoneRuntimeService instance;
    private static int nextZoneId = 1;
    private static int nextCrystalId = -1;
    private static readonly RaycastHit[] GroundHits = new RaycastHit[8];

    private readonly List<Zone> zones = new List<Zone>(32);
    private readonly List<PendingCrystal> crystals = new List<PendingCrystal>(96);
    private readonly Dictionary<long, AttackSequenceState> sequences = new Dictionary<long, AttackSequenceState>(64);
    private readonly List<long> expiredSequenceKeys = new List<long>(32);
    private readonly List<CombatTarget> targetBuffer = new List<CombatTarget>(256);
    private readonly HashSet<EnemyMovement> previousMudTargets = new HashSet<EnemyMovement>();
    private readonly HashSet<EnemyMovement> currentMudTargets = new HashSet<EnemyMovement>();
    private float nextMudMembershipAt;

    public static int ActiveZoneCount => instance != null ? instance.zones.Count : 0;
    public static int PendingCrystalCount => instance != null ? instance.crystals.Count : 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        nextZoneId = 1;
        nextCrystalId = -1;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeInstance()
    {
        EnsureInstance();
    }

    public static void ReportDirectHit(CombatHealth targetHealth, DamageInfo info, float actualDamage)
    {
        if (targetHealth == null || !IsEligibleEarthDirectHit(info, actualDamage, CombatTeam.Enemy))
        {
            return;
        }

        CombatTarget target = targetHealth.GetComponent<CombatTarget>();
        if (target == null || target.Team != CombatTeam.Enemy)
            return;

        GameObject capOwner = ResolveActor(info.source);
        if (capOwner == null)
            return;

        EarthElementZoneRuntimeService service = EnsureInstance();
        int sequenceId = info.sourceAttackSequenceId != 0 ? info.sourceAttackSequenceId : Time.frameCount;
        long key = ComposeSequenceKey(capOwner.GetInstanceID(), sequenceId);
        AttackSequenceState state = service.ResolveSequence(key);

        if (!state.CreatedBasic)
        {
            state.CreatedBasic = true;
            service.CreateZone(
                EarthZoneKind.Basic,
                capOwner,
                capOwner,
                info.sourceWeaponRuntimeInstanceId,
                ResolveGroundPoint(info.hitPoint, target.transform.position.y),
                actualDamage);
        }

        if (state.TargetIds.Contains(target.TargetId))
            return;

        state.TargetIds.Add(target.TargetId);
        if (!PassesExtraZoneRoll(key, target.TargetId))
            return;

        Vector3 extraPoint = ResolveExtraZonePoint(
            info.hitPoint,
            target.transform.position.y,
            key,
            target.TargetId);
        service.CreateZone(
            EarthZoneKind.Basic,
            capOwner,
            capOwner,
            info.sourceWeaponRuntimeInstanceId,
            extraPoint,
            actualDamage);
    }

    public static void ReportAttackRange(AttackRangeOverlapReport report)
    {
        if (!CanTransformWith(report.Element) || report.SourceActor == null)
            return;

        EnsureInstance().TransformOverlappingBasicZones(report);
    }

    public static bool PassesExtraZoneRollForValidation(int sourceId, int sequenceId, int targetId)
    {
        return PassesExtraZoneRoll(ComposeSequenceKey(sourceId, sequenceId), targetId);
    }

    public static bool IsEligibleEarthDirectHit(DamageInfo info, float actualDamage, CombatTeam targetTeam)
    {
        return actualDamage > 0f
            && info.element == WeaponElement.Earth
            && !info.isDamageOverTime
            && info.triggersOnHitEffects
            && info.elementalReactionType == ElementalReactionType.None
            && info.source != null
            && targetTeam == CombatTeam.Enemy;
    }

    public static bool DoesAttackRangeOverlapZone(
        AttackPatternRuntimeData pattern,
        AttackPatternBasis basis,
        float resolvedProgress,
        Vector3 zoneCenter,
        float zoneRadius)
    {
        return AttackPatternEvaluator.TryEvaluate(
                pattern,
                basis,
                new CombatTargetVolume(zoneCenter, Mathf.Max(0f, zoneRadius), 0f),
                out float requiredProgress)
            && requiredProgress <= Mathf.Clamp01(resolvedProgress) + 0.0001f;
    }

    public static void ResolveSpawnDecisionCountsForValidation(
        int[] targetIds,
        out int basicZoneCount,
        out int extraRollCount)
    {
        basicZoneCount = targetIds != null && targetIds.Length > 0 ? 1 : 0;
        extraRollCount = 0;
        if (targetIds == null)
            return;

        HashSet<int> uniqueTargets = new HashSet<int>();
        for (int i = 0; i < targetIds.Length; i++)
        {
            if (uniqueTargets.Add(targetIds[i]))
                extraRollCount++;
        }
    }

    private static EarthElementZoneRuntimeService EnsureInstance()
    {
        if (instance != null)
            return instance;

        GameObject owner = new GameObject(nameof(EarthElementZoneRuntimeService));
        DontDestroyOnLoad(owner);
        instance = owner.AddComponent<EarthElementZoneRuntimeService>();
        return instance;
    }

    private void Update()
    {
        float now = Time.time;
        UpdateZones(now);
        UpdateCrystals(now);
        if (now >= nextMudMembershipAt)
        {
            nextMudMembershipAt = now + MudMembershipInterval;
            UpdateMudMembership();
        }
        PruneSequences(now);
    }

    private AttackSequenceState ResolveSequence(long key)
    {
        if (!sequences.TryGetValue(key, out AttackSequenceState state))
        {
            state = new AttackSequenceState { Key = key };
            sequences.Add(key, state);
        }

        state.LastSeenAt = Time.time;
        return state;
    }

    private void CreateZone(
        EarthZoneKind kind,
        GameObject capOwner,
        GameObject damageOwner,
        string weaponRuntimeInstanceId,
        Vector3 center,
        float damageSnapshot)
    {
        RemoveOldestZoneIfOwnerIsAtCap(capOwner);
        float now = Time.time;
        Zone zone = new Zone
        {
            Id = nextZoneId++,
            Kind = kind,
            CapOwner = capOwner,
            DamageOwner = damageOwner,
            DamageOwnerTarget = ResolveCombatTarget(damageOwner),
            WeaponRuntimeInstanceId = weaponRuntimeInstanceId ?? string.Empty,
            Center = center,
            DamageSnapshot = Mathf.Max(0f, damageSnapshot),
            CreatedAt = now,
            ExpiresAt = now + EarthElementZoneTuning.Lifetime,
            NextTickAt = now + EarthElementZoneTuning.TickInterval,
            NextPulseAt = now + EarthElementZoneTuning.MagneticPulseInterval
        };
        zones.Add(zone);
        EarthZoneVfxRuntimeService.Show(zone.Id, ToVfxKind(zone.Kind), zone.Center);
    }

    private void RemoveOldestZoneIfOwnerIsAtCap(GameObject capOwner)
    {
        int count = 0;
        int oldestIndex = -1;
        float oldestTime = float.PositiveInfinity;
        for (int i = 0; i < zones.Count; i++)
        {
            Zone zone = zones[i];
            if (zone.CapOwner != capOwner)
                continue;

            count++;
            if (zone.CreatedAt < oldestTime)
            {
                oldestTime = zone.CreatedAt;
                oldestIndex = i;
            }
        }

        if (count >= EarthElementZoneTuning.MaximumZonesPerOwner && oldestIndex >= 0)
            RemoveZoneAt(oldestIndex);
    }

    private void UpdateZones(float now)
    {
        for (int i = zones.Count - 1; i >= 0; i--)
        {
            Zone zone = zones[i];
            if (zone.CapOwner == null || now >= zone.ExpiresAt)
            {
                RemoveZoneAt(i);
                continue;
            }

            if (now >= zone.NextTickAt)
            {
                zone.NextTickAt = now + EarthElementZoneTuning.TickInterval;
                ApplyZoneTick(zone);
            }

            if (zone.Kind == EarthZoneKind.MagneticField && now >= zone.NextPulseAt)
            {
                zone.NextPulseAt = now + EarthElementZoneTuning.MagneticPulseInterval;
                ApplyMagneticPulse(zone);
            }
        }
    }

    private void ApplyZoneTick(Zone zone)
    {
        float coefficient = ResolveTickCoefficient(zone.Kind);
        if (coefficient <= 0f)
            return;

        CollectTargets(zone.Center, EarthElementZoneTuning.Radius, zone.DamageOwnerTarget);
        WeaponElement damageElement = ResolveZoneElement(zone.Kind);
        for (int i = 0; i < targetBuffer.Count; i++)
        {
            CombatTarget target = targetBuffer[i];
            float damage = ElementalReactionRules.ResolveCircularAreaDamage(
                zone.DamageSnapshot * coefficient,
                zone.Center,
                target.WorldCenter,
                EarthElementZoneTuning.Radius);
            ApplyNonRecursiveDamage(zone, target, damage, damageElement);
            if (zone.Kind != EarthZoneKind.Basic)
                ApplyNonReactiveStatus(zone, target, damageElement, damage);
        }
    }

    private void ApplyMagneticPulse(Zone zone)
    {
        CollectTargets(zone.Center, EarthElementZoneTuning.Radius, zone.DamageOwnerTarget);
        for (int i = 0; i < targetBuffer.Count; i++)
        {
            CombatTarget target = targetBuffer[i];
            EnemyMovement movement = target != null ? target.GetComponent<EnemyMovement>() : null;
            if (movement == null)
                continue;

            Vector3 direction = zone.Center - target.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
                movement.RequestAreaDisplacement(direction.normalized * EarthElementZoneTuning.MagneticPullDistance);
        }
    }

    private void UpdateMudMembership()
    {
        currentMudTargets.Clear();
        for (int i = 0; i < zones.Count; i++)
        {
            Zone zone = zones[i];
            if (zone.Kind != EarthZoneKind.Mud)
                continue;

            CollectTargets(zone.Center, EarthElementZoneTuning.Radius, zone.DamageOwnerTarget);
            for (int targetIndex = 0; targetIndex < targetBuffer.Count; targetIndex++)
            {
                EnemyMovement movement = targetBuffer[targetIndex].GetComponent<EnemyMovement>();
                if (movement != null)
                    currentMudTargets.Add(movement);
            }
        }

        foreach (EnemyMovement previous in previousMudTargets)
        {
            if (previous != null && !currentMudTargets.Contains(previous))
                previous.SetEarthZoneMoveSpeedMultiplier(1f);
        }

        foreach (EnemyMovement current in currentMudTargets)
        {
            if (current != null)
                current.SetEarthZoneMoveSpeedMultiplier(EarthElementZoneTuning.MudMoveSpeedMultiplier);
        }

        previousMudTargets.Clear();
        foreach (EnemyMovement current in currentMudTargets)
            previousMudTargets.Add(current);
    }

    private void TransformOverlappingBasicZones(AttackRangeOverlapReport report)
    {
        for (int i = 0; i < zones.Count; i++)
        {
            Zone zone = zones[i];
            if (zone.Kind != EarthZoneKind.Basic
                || !DoesAttackRangeOverlapZone(
                    report.Pattern,
                    report.Basis,
                    report.ResolvedProgress,
                    zone.Center,
                    EarthElementZoneTuning.Radius))
            {
                continue;
            }

            zone.Kind = ResolveReactionKind(report.Element);
            zone.DamageOwner = ResolveActor(report.SourceActor);
            zone.DamageOwnerTarget = report.SourceTarget;
            zone.WeaponRuntimeInstanceId = report.SourceWeaponRuntimeInstanceId;
            zone.DamageSnapshot = report.DamageSnapshot;
            zone.ExpiresAt = Time.time + EarthElementZoneTuning.Lifetime;
            zone.NextTickAt = Time.time + EarthElementZoneTuning.TickInterval;
            zone.NextPulseAt = Time.time + EarthElementZoneTuning.MagneticPulseInterval;
            EarthZoneVfxRuntimeService.Replace(zone.Id, ToVfxKind(zone.Kind), zone.Center);

            if (zone.Kind == EarthZoneKind.LavaEruption)
                ApplyAreaBurst(zone, EarthElementZoneTuning.Radius, EarthElementZoneTuning.LavaBurstCoefficient, WeaponElement.Fire);
            else if (zone.Kind == EarthZoneKind.Crystallization)
                ScheduleCrystals(zone);
        }
    }

    private void ScheduleCrystals(Zone zone)
    {
        for (int i = 0; i < EarthElementZoneTuning.CrystalCount; i++)
        {
            float angle = i * (360f / EarthElementZoneTuning.CrystalCount) * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle))
                * (EarthElementZoneTuning.Radius * 0.55f);
            PendingCrystal crystal = new PendingCrystal
            {
                Id = nextCrystalId--,
                DamageOwner = zone.DamageOwner,
                DamageOwnerTarget = zone.DamageOwnerTarget,
                WeaponRuntimeInstanceId = zone.WeaponRuntimeInstanceId,
                Center = zone.Center + offset,
                DamageSnapshot = zone.DamageSnapshot,
                ExplodesAt = Time.time + EarthElementZoneTuning.CrystalDelays[i]
            };
            crystals.Add(crystal);
            EarthZoneVfxRuntimeService.Show(crystal.Id, EarthZoneVfxKind.CrystalPending, crystal.Center);
        }
    }

    private void UpdateCrystals(float now)
    {
        for (int i = crystals.Count - 1; i >= 0; i--)
        {
            PendingCrystal crystal = crystals[i];
            if (now < crystal.ExplodesAt)
                continue;

            ApplyCrystalExplosion(crystal);
            EarthZoneVfxRuntimeService.ShowOneShot(
                crystal.Id,
                EarthZoneVfxKind.CrystalExplosion,
                crystal.Center,
                1f);
            crystals.RemoveAt(i);
        }
    }

    private void ApplyCrystalExplosion(PendingCrystal crystal)
    {
        CollectTargets(crystal.Center, EarthElementZoneTuning.CrystalRadius, crystal.DamageOwnerTarget);
        for (int i = 0; i < targetBuffer.Count; i++)
        {
            CombatTarget target = targetBuffer[i];
            float damage = ElementalReactionRules.ResolveCircularAreaDamage(
                crystal.DamageSnapshot * EarthElementZoneTuning.CrystalDamageCoefficient,
                crystal.Center,
                target.WorldCenter,
                EarthElementZoneTuning.CrystalRadius);
            DamageInfo info = CreateZoneDamageInfo(
                damage,
                target.WorldCenter,
                crystal.DamageOwner,
                crystal.WeaponRuntimeInstanceId,
                WeaponElement.Ice);
            target.DamageReceiver.TakeDamage(info);
        }
    }

    private void ApplyAreaBurst(Zone zone, float radius, float coefficient, WeaponElement element)
    {
        CollectTargets(zone.Center, radius, zone.DamageOwnerTarget);
        for (int i = 0; i < targetBuffer.Count; i++)
        {
            CombatTarget target = targetBuffer[i];
            float damage = ElementalReactionRules.ResolveCircularAreaDamage(
                zone.DamageSnapshot * coefficient,
                zone.Center,
                target.WorldCenter,
                radius);
            ApplyNonRecursiveDamage(zone, target, damage, element);
            ApplyNonReactiveStatus(zone, target, element, damage);
        }
    }

    private static void ApplyNonRecursiveDamage(Zone zone, CombatTarget target, float damage, WeaponElement element)
    {
        if (target == null || target.DamageReceiver == null || damage <= 0f)
            return;

        target.DamageReceiver.TakeDamage(CreateZoneDamageInfo(
            damage,
            target.WorldCenter,
            zone.DamageOwner,
            zone.WeaponRuntimeInstanceId,
            element));
    }

    private static DamageInfo CreateZoneDamageInfo(
        float damage,
        Vector3 hitPoint,
        GameObject owner,
        string weaponRuntimeInstanceId,
        WeaponElement element)
    {
        return new DamageInfo(
            damage,
            hitPoint,
            owner,
            Vector3.zero,
            0f,
            false,
            false,
            true,
            default,
            true,
            element,
            weaponRuntimeInstanceId,
            ElementalReactionType.None,
            0);
    }

    private static void ApplyNonReactiveStatus(Zone zone, CombatTarget target, WeaponElement element, float damage)
    {
        ElementalStatusController status = target != null ? target.GetComponent<ElementalStatusController>() : null;
        status?.TryApplyDirectHit(new ElementalStatusApplication(
            element,
            Mathf.Max(0.001f, damage),
            zone.DamageOwner,
            zone.WeaponRuntimeInstanceId,
            true,
            false,
            target.WorldCenter,
            Vector3.zero));
    }

    private void CollectTargets(Vector3 center, float radius, CombatTarget sourceTarget)
    {
        CombatTargetRegistry.CollectPotentialTargets(center, radius, targetBuffer);
        int writeIndex = 0;
        float radiusSquared = radius * radius;
        for (int i = 0; i < targetBuffer.Count; i++)
        {
            CombatTarget target = targetBuffer[i];
            if (!CombatTargetFilter.CanDamage(sourceTarget, target))
                continue;

            Vector3 delta = target.WorldCenter - center;
            delta.y = 0f;
            float effectiveRadius = radius + target.CurrentVolume.Radius;
            if (delta.sqrMagnitude > effectiveRadius * effectiveRadius)
                continue;

            targetBuffer[writeIndex++] = target;
        }

        if (writeIndex < targetBuffer.Count)
            targetBuffer.RemoveRange(writeIndex, targetBuffer.Count - writeIndex);
    }

    private void RemoveZoneAt(int index)
    {
        if ((uint)index >= (uint)zones.Count)
            return;
        bool removedMud = zones[index].Kind == EarthZoneKind.Mud;
        EarthZoneVfxRuntimeService.Hide(zones[index].Id);
        zones.RemoveAt(index);
        if (removedMud)
            UpdateMudMembership();
    }

    private void PruneSequences(float now)
    {
        expiredSequenceKeys.Clear();
        foreach (KeyValuePair<long, AttackSequenceState> pair in sequences)
        {
            if (now - pair.Value.LastSeenAt > SequenceRetention)
                expiredSequenceKeys.Add(pair.Key);
        }
        for (int i = 0; i < expiredSequenceKeys.Count; i++)
            sequences.Remove(expiredSequenceKeys[i]);
    }

    private static EarthZoneKind ResolveReactionKind(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire: return EarthZoneKind.LavaEruption;
            case WeaponElement.Water: return EarthZoneKind.Mud;
            case WeaponElement.Ice: return EarthZoneKind.Crystallization;
            case WeaponElement.Electric: return EarthZoneKind.MagneticField;
            default: return EarthZoneKind.Basic;
        }
    }

    private static EarthZoneVfxKind ToVfxKind(EarthZoneKind kind)
    {
        switch (kind)
        {
            case EarthZoneKind.LavaEruption: return EarthZoneVfxKind.LavaEruption;
            case EarthZoneKind.Mud: return EarthZoneVfxKind.Mud;
            case EarthZoneKind.Crystallization: return EarthZoneVfxKind.Crystallization;
            case EarthZoneKind.MagneticField: return EarthZoneVfxKind.MagneticField;
            default: return EarthZoneVfxKind.BasicEarthZone;
        }
    }

    private static WeaponElement ResolveZoneElement(EarthZoneKind kind)
    {
        switch (kind)
        {
            case EarthZoneKind.LavaEruption: return WeaponElement.Fire;
            case EarthZoneKind.Mud: return WeaponElement.Water;
            case EarthZoneKind.Crystallization: return WeaponElement.Ice;
            case EarthZoneKind.MagneticField: return WeaponElement.Electric;
            default: return WeaponElement.Earth;
        }
    }

    private static float ResolveTickCoefficient(EarthZoneKind kind)
    {
        switch (kind)
        {
            case EarthZoneKind.Basic: return EarthElementZoneTuning.BasicCoefficient;
            case EarthZoneKind.LavaEruption: return EarthElementZoneTuning.LavaTickCoefficient;
            case EarthZoneKind.Mud: return EarthElementZoneTuning.MudTickCoefficient;
            case EarthZoneKind.Crystallization: return EarthElementZoneTuning.CrystallizationTickCoefficient;
            case EarthZoneKind.MagneticField: return EarthElementZoneTuning.MagneticTickCoefficient;
            default: return 0f;
        }
    }

    private static bool CanTransformWith(WeaponElement element)
    {
        return element == WeaponElement.Fire
            || element == WeaponElement.Water
            || element == WeaponElement.Ice
            || element == WeaponElement.Electric;
    }

    private static GameObject ResolveActor(GameObject source)
    {
        if (source == null)
            return null;
        CombatTarget target = source.GetComponentInParent<CombatTarget>();
        return target != null ? target.gameObject : source.transform.root.gameObject;
    }

    private static CombatTarget ResolveCombatTarget(GameObject actor)
    {
        return actor != null ? actor.GetComponent<CombatTarget>() : null;
    }

    private static Vector3 ResolveGroundPoint(Vector3 point, float fallbackY)
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        int mask = 1 << 0;
        if (groundLayer >= 0)
            mask |= 1 << groundLayer;

        Vector3 origin = new Vector3(point.x, Mathf.Max(point.y, fallbackY) + GroundRayHeight, point.z);
        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            GroundHits,
            GroundRayDistance,
            mask,
            QueryTriggerInteraction.Ignore);
        float bestDistance = float.PositiveInfinity;
        Vector3 resolved = new Vector3(point.x, fallbackY, point.z);
        for (int i = 0; i < hitCount && i < GroundHits.Length; i++)
        {
            RaycastHit hit = GroundHits[i];
            if (hit.collider == null || CombatTarget.Resolve(hit.collider) != null || hit.distance >= bestDistance)
                continue;
            bestDistance = hit.distance;
            resolved.y = hit.point.y;
        }
        return resolved;
    }

    private static Vector3 ResolveExtraZonePoint(
        Vector3 hitPoint,
        float fallbackY,
        long sequenceKey,
        int targetId)
    {
        uint hash = Hash(sequenceKey, targetId ^ unchecked((int)0x6D2B79F5));
        float angle = (hash % 3600u) * 0.1f * Mathf.Deg2Rad;
        float radius = ((hash >> 12) & 1023u) / 1023f * EarthElementZoneTuning.ExtraZoneOffset;
        return ResolveGroundPoint(
            hitPoint + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius,
            fallbackY);
    }

    private static bool PassesExtraZoneRoll(long sequenceKey, int targetId)
    {
        return Hash(sequenceKey, targetId) % 1000u
            < Mathf.RoundToInt(EarthElementZoneTuning.ExtraZoneChance * 1000f);
    }

    private static uint Hash(long sequenceKey, int targetId)
    {
        unchecked
        {
            uint hash = (uint)sequenceKey ^ (uint)(sequenceKey >> 32) ^ (uint)targetId;
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return hash;
        }
    }

    private static long ComposeSequenceKey(int sourceId, int sequenceId)
    {
        return ((long)sourceId << 32) ^ (uint)sequenceId;
    }
}
