using System.Collections.Generic;
using UnityEngine;

// Executes the committed heavy discharge. Derived damage never charges energy or reapplies status.
public sealed class MeleeHeavyDischargeExecutor
{
    private ElementDischargeBatch batch = new ElementDischargeBatch();
    private System.Action<Vector3, float> fireVfx;
    private System.Action<Vector3, Vector3> chainVfx;
    private bool pendingElectricChain;
    public int CandidateChecks => batch.CandidateChecks;
    public int SecondaryHits => batch.SecondaryHits;
    public int OriginCount => batch.OriginCount;
    public int SnapshotCount => batch.Count;
    public CombatTarget FirstBlastTarget(int index) => batch.FirstBlastTarget(index, impactCenter, blastRadius);
    private readonly List<CombatTarget> candidates = new List<CombatTarget>(32);
    private readonly List<CombatTarget> darkTargets = new List<CombatTarget>(24);
    private readonly HashSet<int> lightVisited = new HashSet<int>();

    private OverburstElementDischarge discharge;
    private MeleeHeavyAttackDefinition definition;
    private CombatTarget sourceTarget;
    private GameObject sourceActor;
    private Vector3 impactCenter;
    private Vector3 facing;
    private float blastRadius;
    private float blastVerticalTolerance;
    private float darkPullRemaining;
    private float lightElapsed;
    private float lightPreviousRadius;
    private bool pendingFireExplosion;
    private bool pendingDarkPull;
    private bool pendingLightAfterglow;
    private bool lightStarted;

    public void Begin(
        OverburstElementDischarge committedDischarge,
        MeleeHeavyAttackDefinition heavyDefinition,
        CombatTarget attacker,
        GameObject attackerObject,
        Vector3 center,
        Vector3 direction,
        float firstBlastRadius,
        float firstBlastVerticalTolerance)
    {
        using var costScope = ElementCombatCostMarkers.Heavy_Begin.Auto();
        End();
        discharge = committedDischarge;
        definition = heavyDefinition;
        sourceTarget = attacker;
        sourceActor = attackerObject;
        impactCenter = center;
        blastRadius = Mathf.Max(0f, firstBlastRadius);
        blastVerticalTolerance = Mathf.Max(0f, firstBlastVerticalTolerance);
        facing = direction;
        facing.y = 0f;
        if (facing.sqrMagnitude > 0.0001f) facing.Normalize();
        PlayImpact();
        batch.Capture(sourceTarget, discharge.Element, blastVerticalTolerance);
        if (fireVfx == null) fireVfx = PlayFireOrigin;
        if (chainVfx == null) chainVfx = PlayChainLink;
        pendingFireExplosion = discharge.Element == WeaponElement.Fire;
        pendingElectricChain = discharge.Element == WeaponElement.Electric;
        pendingDarkPull = discharge.Element == WeaponElement.Dark && discharge.Energy > 0f;
        pendingLightAfterglow = discharge.Element == WeaponElement.Light && discharge.Energy > 0f;
    }

    public void ResolvePendingArea()
    {
        if (pendingFireExplosion || pendingElectricChain)
        {
            pendingFireExplosion = false;
            pendingElectricChain = false;
            batch = ElementChainScheduler.Submit(batch, impactCenter, discharge.FirstBlastDamage,
                discharge.NormalizedEnergy, sourceActor, definition.elementVfx.FireChainExplosion,
                definition.elementVfx.electricChainLink, definition.elementVfx.electricChainProc,
                definition.elementVfx.FireChainReferenceRadius);
        }
        if (pendingDarkPull)
        {
            pendingDarkPull = false;
            PrepareDarkPullTargets();
            darkPullRemaining = OverburstElementTuning.Current.SafeDarkPullDuration;
        }
        if (pendingLightAfterglow)
        {
            pendingLightAfterglow = false;
            lightStarted = true;
            lightElapsed = 0f;
            lightPreviousRadius = 0f;
        }
    }

    public void Tick(float deltaTime)
    {
        if (discharge == null || deltaTime <= 0f) return;
        if (darkPullRemaining > 0f) AdvanceDarkPull(deltaTime);
        if (lightStarted) AdvanceLightAfterglow(deltaTime);
    }

    public void ResolveDirectHit(
        CombatHealth target,
        Vector3 hitPoint,
        OverburstDischargeResult result)
    {
        using var costScope = ElementCombatCostMarkers.Heavy_StatusReaction.Auto();
        if (discharge == null || target == null)
            return;

        float directBonus = result.BonusDamage;
        if (result.Element == WeaponElement.Ice && result.Shattered)
            ShatterWaveScheduler.Submit(target, directBonus, sourceActor,
                definition.elementVfx.iceShatter, impactCenter, hitPoint, facing, blastRadius);
        else
            DealDerivedDamage(target, directBonus, hitPoint, facing);
        if (result.Element == WeaponElement.Fire || result.Element == WeaponElement.Electric)
            batch.ConfirmInitial(target);
        if (result.Element == WeaponElement.Electric && result.ConsumedStacks > 0)
            Spawn(definition.elementVfx.electricChainStart, hitPoint, Quaternion.identity, 1f);
    }

    public void End()
    {
        discharge = null;
        batch.Clear();
        pendingElectricChain = false;
        definition = null;
        sourceTarget = null;
        sourceActor = null;
        pendingFireExplosion = false;
        pendingDarkPull = false;
        pendingLightAfterglow = false;
        lightStarted = false;
        darkPullRemaining = 0f;
        lightElapsed = 0f;
        lightPreviousRadius = 0f;
        blastRadius = 0f;
        blastVerticalTolerance = 0f;
        candidates.Clear();
        darkTargets.Clear();
        lightVisited.Clear();
    }

    private void PrepareDarkPullTargets()
    {
        darkTargets.Clear();
        if (sourceTarget == null || blastRadius <= 0f) return;
        CombatTargetRegistry.CollectPotentialTargets(impactCenter, blastRadius, candidates);
        int limit = OverburstElementTuning.Current.SafeDarkPullMaxTargets;
        for (int i = 0; i < candidates.Count; i++)
        {
            CombatTarget target = candidates[i];
            if (!IsValidEnemy(target) || !IsInRadius(target, impactCenter, blastRadius)
                || !IsInBlastHeight(target)
                || target.GetComponent<EnemyMovement>() == null) continue;

            if (darkTargets.Count < limit)
            {
                darkTargets.Add(target);
                continue;
            }

            int farthest = 0;
            float farthestDistance = -1f;
            for (int j = 0; j < darkTargets.Count; j++)
            {
                float distance = PlanarDistanceSquared(darkTargets[j].WorldCenter, impactCenter);
                if (distance <= farthestDistance) continue;
                farthest = j;
                farthestDistance = distance;
            }
            if (PlanarDistanceSquared(target.WorldCenter, impactCenter) < farthestDistance)
                darkTargets[farthest] = target;
        }
        candidates.Clear();
    }

    private void AdvanceDarkPull(float deltaTime)
    {
        OverburstElementTuning tuning = OverburstElementTuning.Current;
        float step = Mathf.Min(deltaTime, darkPullRemaining);
        darkPullRemaining -= step;
        float movement = tuning.SafeDarkPullDistance * step / tuning.SafeDarkPullDuration;
        for (int i = 0; i < darkTargets.Count; i++)
        {
            CombatTarget target = darkTargets[i];
            if (!IsValidEnemy(target)) continue;
            EnemyMovement motor = target.GetComponent<EnemyMovement>();
            if (motor == null) continue;
            EnemyRank rank = target.GetComponent<EnemyRank>();
            float resistance = ResolvePullResistance(rank);
            if (resistance <= 0f) continue;
            Vector3 towardCenter = impactCenter - motor.transform.position;
            towardCenter.y = 0f;
            float gap = towardCenter.magnitude - 0.35f;
            if (gap <= 0f) continue;
            motor.RequestAreaDisplacement(towardCenter.normalized * Mathf.Min(gap, movement * resistance));
        }
        if (darkPullRemaining <= 0f) darkTargets.Clear();
    }

    private static float ResolvePullResistance(EnemyRank rank)
    {
        if (rank == null) return 0.5f;
        switch (rank.GradeType)
        {
            case EnemyGradeType.Normal: return 1f;
            case EnemyGradeType.Elite: return 0.5f;
            case EnemyGradeType.GreaterElite: return 0.25f;
            default: return 0f;
        }
    }

    private void AdvanceLightAfterglow(float deltaTime)
    {
        OverburstElementTuning tuning = OverburstElementTuning.Current;
        float previousTime = lightElapsed;
        lightElapsed += deltaTime;
        float delay = tuning.SafeLightAfterglowDelay;
        if (lightElapsed < delay) return;
        if (previousTime < delay) PlayLightAfterglow();

        float duration = tuning.SafeLightAfterglowDuration;
        float outerRadius = blastRadius * tuning.SafeLightAfterglowRadiusMultiplier;
        float currentRadius = outerRadius * Mathf.Clamp01((lightElapsed - delay) / duration);
        CombatTargetRegistry.CollectPotentialTargets(impactCenter, currentRadius, candidates);
        for (int i = 0; i < candidates.Count; i++)
        {
            CombatTarget target = candidates[i];
            if (!IsValidEnemy(target) || !IsInRing(target, lightPreviousRadius, currentRadius)
                || !IsInBlastHeight(target)) continue;
            CombatHealth health = target.DamageReceiver;
            if (!lightVisited.Add(health.GetInstanceID())) continue;

            bool captured = discharge.TryCaptureTarget(health, out OverburstElementDischarge.TargetSnapshot snapshot);
            float before = health.CurrentHp;
            DealDerivedDamage(health, discharge.BaseDamage * tuning.SafeLightAfterglowDamageFraction,
                target.WorldCenter, (target.WorldCenter - impactCenter).normalized);
            if (!captured || !discharge.TryResolveConfirmedHit(snapshot,
                    Mathf.Max(0f, before - health.CurrentHp), out OverburstDischargeResult result)) continue;
            float statusBonus = Mathf.Max(0f, result.BonusDamage);
            DealDerivedDamage(health, statusBonus, target.WorldCenter, facing);
        }
        candidates.Clear();
        lightPreviousRadius = currentRadius;
        if (lightElapsed < delay + duration) return;
        lightStarted = false;
        lightVisited.Clear();
    }

    private bool IsInRing(CombatTarget target, float innerRadius, float outerRadius)
    {
        Vector3 delta = target.CurrentHurtVolume.Center - impactCenter;
        delta.y = 0f;
        float distance = delta.magnitude;
        float targetRadius = target.CurrentHurtVolume.Radius;
        return distance - targetRadius <= outerRadius && distance + targetRadius >= innerRadius;
    }

    private bool IsInBlastHeight(CombatTarget target)
    {
        CombatTargetVolume volume = target.CurrentHurtVolume;
        return Mathf.Max(0f, Mathf.Abs(volume.Center.y - impactCenter.y) - volume.HalfHeight)
            <= blastVerticalTolerance;
    }

    private void PlayLightAfterglow()
    {
        if (definition == null || definition.attack.attackPhases == null
            || definition.attack.attackPhases.Length == 0) return;
        AttackVfxCueData[] cues = definition.attack.attackPhases[0].vfxCues;
        if (cues == null) return;
        for (int i = 0; i < cues.Length; i++)
        {
            AttackVfxCueData cue = cues[i];
            MeleeAttackVfxDefinition visual = cue.definition;
            if (visual == null || cue.motionRole != AttackVfxMotionRole.HorizontalCircular) continue;
            GameObject prefab = MeleeAttackVfxResolver.Current.ResolvePrefab(visual, cue.elementOverrideKey);
            if (prefab == null) continue;
            float speed = Mathf.Clamp(0.28f / OverburstElementTuning.Current.SafeLightAfterglowDuration, 0.1f, 4f);
            float scale = cue.SafeScaleMultiplier * OverburstElementTuning.Current.SafeLightAfterglowRadiusMultiplier;
            Quaternion rotation = facing.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(facing, Vector3.up) : Quaternion.identity;
            TransientVfxPool.Spawn(prefab, impactCenter + rotation * visual.localPositionOffset,
                rotation * Quaternion.Euler(visual.localEulerOffset + cue.localEulerOffset),
                SwordShockwavePlayback.ResolveCueLifetime(prefab, visual.lifetime, speed),
                visual.SafePoolCapacity, null,
                instance =>
                {
                    instance.transform.localScale = visual.baseScale * scale;
                    instance.GetComponent<SwordShockwavePlayback>()?.Configure(cue.SafeShockwaveIntensity, speed);
                }, MeleeSharedSlashSpawnContract.ResolveReturnMode(cue.elementOverrideKey));
            return;
        }
    }

    private static float PlanarDistanceSquared(Vector3 a, Vector3 b)
    {
        Vector3 delta = a - b;
        delta.y = 0f;
        return delta.sqrMagnitude;
    }

    private void PlayImpact()
    {
        using var costScope = ElementCombatCostMarkers.Heavy_ImpactVfx.Auto();
        if (definition == null || discharge == null) return;
        GameObject prefab = definition.elementVfx.GetImpact(discharge.Element);
        float scale = definition.elementVfx.ImpactScale(discharge.Element, blastRadius);
        Spawn(prefab, impactCenter, Quaternion.identity, scale);
    }
    private void PlayFireOrigin(Vector3 point, float radius)
    {
        if (definition == null) return;
        Spawn(definition.elementVfx.FireChainExplosion, point, Quaternion.identity,
            radius / definition.elementVfx.FireChainReferenceRadius);
    }

    private void PlayChainLink(Vector3 from, Vector3 to)
    {
        GameObject prefab = definition != null ? definition.elementVfx.electricChainLink : null;
        Vector3 direction = to - from;
        float distance = direction.magnitude;
        if (prefab == null || distance <= 0.05f) return;
        Spawn(definition.elementVfx.electricChainProc, to, Quaternion.identity, 1f);
        if (ChainElectricityBatchRenderer.TrySpawn(prefab, from, to)) return;
        TransientVfxPool.Spawn(prefab, (from + to) * 0.5f,
            Quaternion.LookRotation(direction / distance, Vector3.up), 0f, 12,
            prepareBeforeActivation: instance => instance.transform.localScale =
                Vector3.Scale(prefab.transform.localScale, new Vector3(1f, 1f, distance)),
            returnMode: TransientVfxReturnMode.NaturalParticleCompletion);
    }

    private static void Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float scale)
    {
        if (prefab == null) return;
        TransientVfxPool.Spawn(prefab, position, rotation, 0f, MeleeHeavyVfxPreparation.RetainedCapacity(prefab),
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
