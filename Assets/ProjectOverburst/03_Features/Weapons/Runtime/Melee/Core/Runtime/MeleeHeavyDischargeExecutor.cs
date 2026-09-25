using System.Collections.Generic;
using UnityEngine;

// Executes the committed heavy discharge. Derived damage never charges energy or reapplies status.
public sealed class MeleeHeavyDischargeExecutor
{
    private const float PullDuration = 0.24f;
    private const float MinimumPullSeparation = 0.35f;
    private readonly List<CombatTarget> candidates = new List<CombatTarget>(32);
    private readonly List<CombatTarget> fireTargets = new List<CombatTarget>(32);
    private readonly HashSet<int> chainVisited = new HashSet<int>();
    private readonly List<PullRequest> pulls = new List<PullRequest>(12);

    private OverburstElementDischarge discharge;
    private MeleeHeavyAttackDefinition definition;
    private CombatTarget sourceTarget;
    private GameObject sourceActor;
    private Vector3 impactCenter;
    private Vector3 facing;
    private bool chainExecuted;
    private bool pendingFireExplosion;

    private struct PullRequest
    {
        public CombatHealth Target;
        public float RemainingDistance;
        public float Speed;
        public float ExpiresAt;
        public float PendingDistance;
    }

    public void Begin(
        OverburstElementDischarge committedDischarge,
        MeleeHeavyAttackDefinition heavyDefinition,
        CombatTarget attacker,
        GameObject attackerObject,
        Vector3 center,
        Vector3 direction)
    {
        End();
        discharge = committedDischarge;
        definition = heavyDefinition;
        sourceTarget = attacker;
        sourceActor = attackerObject;
        impactCenter = center;
        facing = direction;
        facing.y = 0f;
        if (facing.sqrMagnitude > 0.0001f) facing.Normalize();
        PlayImpact();
        pendingFireExplosion = discharge.Element == WeaponElement.Fire;
        if (pendingFireExplosion) PrepareFireExplosionTargets();
    }

    public void ResolvePendingArea()
    {
        if (!pendingFireExplosion) return;
        pendingFireExplosion = false;
        ExecuteFireExplosion();
    }

    public void ResolveDirectHit(
        CombatHealth target,
        Vector3 hitPoint,
        OverburstDischargeResult result)
    {
        if (discharge == null || target == null)
            return;

        float directBonus = result.Element == WeaponElement.Fire
            ? Mathf.Max(0f, result.BonusDamage - discharge.BaseDamage)
            : result.BonusDamage;
        DealDerivedDamage(target, directBonus, hitPoint, facing);
        if (result.Element == WeaponElement.Ice && result.Shattered)
            Spawn(definition.elementVfx.iceShatter, hitPoint, Quaternion.identity, 1f);
        if (result.Element == WeaponElement.Water && !target.IsDead)
            SchedulePull(target, result.RequestedPullDistance);
        if (result.Element == WeaponElement.Electric && !chainExecuted)
        {
            chainExecuted = true;
            ExecuteElectricChain(target, hitPoint, result);
        }
    }

    public void Tick(float deltaTime)
    {
        for (int i = pulls.Count - 1; i >= 0; i--)
        {
            PullRequest pull = pulls[i];
            if (pull.Target == null || pull.Target.IsDead || Time.time >= pull.ExpiresAt)
            {
                pulls.RemoveAt(i);
                continue;
            }

            EnemyMovement movement = pull.Target.GetComponent<EnemyMovement>();
            if (movement == null)
            {
                pulls.RemoveAt(i);
                continue;
            }

            Vector3 offset = impactCenter - movement.transform.position;
            offset.y = 0f;
            float separation = offset.magnitude;
            if (separation <= MinimumPullSeparation)
            {
                pulls.RemoveAt(i);
                continue;
            }

            float available = Mathf.Min(pull.RemainingDistance,
                Mathf.Min(separation - MinimumPullSeparation, 0.25f));
            pull.PendingDistance = Mathf.Min(available,
                pull.PendingDistance + pull.Speed * Mathf.Max(0f, deltaTime));
            if (pull.PendingDistance < 0.01f)
            {
                pulls[i] = pull;
                continue;
            }
            float step = pull.PendingDistance;
            if (!movement.RequestAreaDisplacement(offset / separation * step))
            {
                pulls.RemoveAt(i);
                continue;
            }

            pull.RemainingDistance -= step;
            pull.PendingDistance = 0f;
            if (pull.RemainingDistance <= 0.001f) pulls.RemoveAt(i);
            else pulls[i] = pull;
        }
    }

    public void End()
    {
        discharge = null;
        definition = null;
        sourceTarget = null;
        sourceActor = null;
        chainExecuted = false;
        pendingFireExplosion = false;
        candidates.Clear();
        fireTargets.Clear();
        chainVisited.Clear();
        pulls.Clear();
    }

    private void ExecuteFireExplosion()
    {
        if (sourceTarget == null || discharge.BaseDamage <= 0f)
            return;
        for (int i = 0; i < fireTargets.Count; i++)
        {
            CombatTarget target = fireTargets[i];
            if (!IsValidEnemy(target)) continue;
            CombatHealth health = target.DamageReceiver;
            bool hasSnapshot = discharge.TryCaptureTarget(
                health, out OverburstElementDischarge.TargetSnapshot snapshot);
            float before = health.CurrentHp;
            DealDerivedDamage(health, discharge.BaseDamage, target.WorldCenter, facing);
            float actualDamage = Mathf.Max(0f, before - health.CurrentHp);
            if (!hasSnapshot || !discharge.TryResolveConfirmedHit(
                    snapshot, actualDamage, out OverburstDischargeResult result))
                continue;
            float statusBonus = Mathf.Max(0f, result.BonusDamage - discharge.BaseDamage);
            DealDerivedDamage(health, statusBonus, target.WorldCenter, facing);
        }
        fireTargets.Clear();
    }

    private void PrepareFireExplosionTargets()
    {
        CombatTargetRegistry.CollectPotentialTargets(impactCenter, discharge.Radius, candidates);
        for (int i = 0; i < candidates.Count; i++)
        {
            CombatTarget target = candidates[i];
            if (!IsValidEnemy(target) || !IsInRadius(target, impactCenter, discharge.Radius))
                continue;
            Vector3 fromOwner = target.WorldCenter - sourceActor.transform.position;
            fromOwner.y = 0f;
            if (facing.sqrMagnitude > 0.0001f && Vector3.Dot(fromOwner, facing) < -0.05f)
                continue;
            fireTargets.Add(target);
        }
        candidates.Clear();
    }

    private void ExecuteElectricChain(CombatHealth firstTarget, Vector3 firstPoint, OverburstDischargeResult result)
    {
        if (sourceTarget == null || result.ChainTargets <= 1 || result.Radius <= 0f)
            return;

        chainVisited.Clear();
        chainVisited.Add(firstTarget.GetInstanceID());
        Vector3 previous = firstPoint;
        for (int hop = 1; hop < result.ChainTargets; hop++)
        {
            CombatTargetRegistry.CollectPotentialTargets(previous, result.Radius, candidates);
            CombatTarget nearest = null;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < candidates.Count; i++)
            {
                CombatTarget candidate = candidates[i];
                if (!IsValidEnemy(candidate) || chainVisited.Contains(candidate.DamageReceiver.GetInstanceID())
                    || !IsInRadius(candidate, previous, result.Radius))
                    continue;
                float distance = (candidate.WorldCenter - previous).sqrMagnitude;
                if (distance < nearestDistance || Mathf.Approximately(distance, nearestDistance)
                    && (nearest == null || candidate.TargetId < nearest.TargetId))
                {
                    nearest = candidate;
                    nearestDistance = distance;
                }
            }
            candidates.Clear();
            if (nearest == null) break;

            chainVisited.Add(nearest.DamageReceiver.GetInstanceID());
            Vector3 next = nearest.WorldCenter;
            PlayChainLink(previous, next);
            float damage = result.BonusDamage * Mathf.Pow(0.7f, hop);
            DealDerivedDamage(nearest.DamageReceiver, damage, next, (next - previous).normalized);
            previous = next;
        }
        chainVisited.Clear();
    }

    private void SchedulePull(CombatHealth target, float requestedDistance)
    {
        EnemyRank rank = target.GetComponent<EnemyRank>();
        float resistance = 1f;
        if (rank != null)
        {
            switch (rank.GradeType)
            {
                case EnemyGradeType.Elite: resistance = 0.5f; break;
                case EnemyGradeType.GreaterElite: resistance = 0.25f; break;
                case EnemyGradeType.Boss: resistance = 0f; break;
            }
        }
        float distance = Mathf.Max(0f, requestedDistance) * resistance;
        if (distance <= 0.001f || target.GetComponent<EnemyMovement>() == null)
            return;
        pulls.Add(new PullRequest
        {
            Target = target,
            RemainingDistance = distance,
            Speed = distance / PullDuration,
            ExpiresAt = Time.time + PullDuration
        });
    }

    private void PlayImpact()
    {
        if (definition == null || discharge == null) return;
        GameObject prefab = definition.elementVfx.GetImpact(discharge.Element);
        float scale = Mathf.Lerp(0.6f, 1.2f, discharge.NormalizedEnergy);
        Spawn(prefab, impactCenter, Quaternion.identity, scale);
    }

    private void PlayChainLink(Vector3 from, Vector3 to)
    {
        GameObject prefab = definition != null ? definition.elementVfx.electricChainLink : null;
        Vector3 direction = to - from;
        float distance = direction.magnitude;
        if (prefab == null || distance <= 0.05f) return;
        TransientVfxPool.Spawn(prefab, (from + to) * 0.5f,
            Quaternion.LookRotation(direction / distance, Vector3.up), 0f, 12,
            prepareBeforeActivation: instance => instance.transform.localScale =
                Vector3.Scale(prefab.transform.localScale, new Vector3(1f, 1f, distance)),
            returnMode: TransientVfxReturnMode.NaturalParticleCompletion);
    }

    private static void Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float scale)
    {
        if (prefab == null) return;
        TransientVfxPool.Spawn(prefab, position, rotation, 0f, 12,
            prepareBeforeActivation: instance =>
                instance.transform.localScale = prefab.transform.localScale * scale);
    }

    private bool IsValidEnemy(CombatTarget target)
    {
        return target != null && target.IsAlive && target.DamageReceiver != null
            && target.Team != sourceTarget.Team;
    }

    private static bool IsInRadius(CombatTarget target, Vector3 center, float radius)
    {
        Vector3 delta = target.CurrentHurtVolume.Center - center;
        delta.y = 0f;
        float range = radius + target.CurrentHurtVolume.Radius;
        return delta.sqrMagnitude <= range * range;
    }

    private void DealDerivedDamage(CombatHealth target, float damage, Vector3 point, Vector3 direction)
    {
        if (target == null || target.IsDead || damage <= 0f) return;
        target.TakeDamage(new DamageInfo(damage, point, sourceActor, direction,
            triggersOnHitEffects: false, suppressDefaultHitVfx: true,
            element: discharge.Element, playerAttackKind: PlayerAttackKind.Elemental));
    }
}
