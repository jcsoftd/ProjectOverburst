using UnityEngine;

// Executes the committed heavy discharge. Derived damage never charges energy or reapplies status.
// 60D: dark and light follow-ups are owned by DarkBarrageScheduler / LightTripleImpactScheduler,
// so they finish even when the heavy action is cancelled. The temporary dark pull and light afterglow were removed.
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

    private OverburstElementDischarge discharge;
    private MeleeHeavyAttackDefinition definition;
    private CombatTarget sourceTarget;
    private GameObject sourceActor;
    private Vector3 impactCenter;
    private Vector3 facing;
    private float blastRadius;
    private float blastVerticalTolerance;
    private bool pendingFireExplosion;

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
        batch.Capture(sourceTarget, discharge.Element, blastVerticalTolerance, discharge.GemAttack);
        if (fireVfx == null) fireVfx = PlayFireOrigin;
        if (chainVfx == null) chainVfx = PlayChainLink;
        pendingFireExplosion = discharge.Element == WeaponElement.Fire;
        pendingElectricChain = discharge.Element == WeaponElement.Electric;
        SubmitUpperElementFollowUps();
    }

    public void ResolvePendingArea()
    {
        if (!pendingFireExplosion && !pendingElectricChain) return;
        pendingFireExplosion = false;
        pendingElectricChain = false;
        batch = ElementChainScheduler.Submit(batch, impactCenter, discharge.FirstBlastDamage,
            discharge.NormalizedEnergy, sourceActor, definition.elementVfx.FireChainExplosion,
            definition.elementVfx.electricChainLink,
            definition.elementVfx.FireChainReferenceRadius, discharge.Energy);
    }

    // Kept for the action loop. Nothing runs per frame here since the 60D follow-ups moved to schedulers.
    public void Tick(float deltaTime) { }

    public void ResolveDirectHit(
        CombatHealth target,
        Vector3 hitPoint,
        OverburstDischargeResult result)
    {
        using var costScope = ElementCombatCostMarkers.Heavy_StatusReaction.Auto();
        if (discharge == null || target == null)
            return;

        float directBonus = result.BonusDamage;
        var resolvingDischarge = discharge;
        if (result.Element == WeaponElement.Ice && result.Shattered)
            ShatterWaveScheduler.Submit(target, directBonus, sourceActor,
                definition.elementVfx.iceShatter, impactCenter, hitPoint, facing, blastRadius, discharge.Energy, discharge.GemAttack);
        else
            DealDerivedDamage(target, directBonus, hitPoint, facing);
        if (discharge != resolvingDischarge) return;
        if (result.Element == WeaponElement.Fire || result.Element == WeaponElement.Electric)
            batch.ConfirmInitial(target);
        // Every enemy caught by the electric slam is struck from above at its feet.
        if (result.Element == WeaponElement.Electric && definition.elementVfx.electricDirectHit != null)
            Spawn(definition.elementVfx.electricDirectHit, target.transform.position, Quaternion.identity, 1f);
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
        blastRadius = 0f;
        blastVerticalTolerance = 0f;
    }

    private void SubmitUpperElementFollowUps()
    {
        if (discharge == null || sourceActor == null || sourceTarget == null) return;
        // Dark is submitted after the direct-hit loop has supplied its confirmed corrosion count.
        if (discharge.Element != WeaponElement.Light) return;
        int slamHit = discharge.LightFirstHitIndex;
        for (int hit = slamHit + 1; hit <= 2; hit++)
            LightTripleImpactScheduler.Submit(sourceActor, sourceTarget.Team, impactCenter,
                discharge.LightHitRadius(hit), discharge.LightHitDamage(hit), blastVerticalTolerance,
                LightTripleImpactScheduler.ResolveDelay(hit, slamHit), hit, discharge.Energy, discharge.GemAttack);
    }

    private void PlayImpact()
    {
        using var costScope = ElementCombatCostMarkers.Heavy_ImpactVfx.Auto();
        if (definition == null || discharge == null) return;
        WeaponElement element = discharge.Element;
        if (element == WeaponElement.Dark || element == WeaponElement.Light)
        {
            if (element == WeaponElement.Dark && !(discharge.Energy > 0f)) return;
            GameObject upper = element == WeaponElement.Light
                ? definition.elementVfx.GetLightImpact(discharge.LightTriple)
                : definition.elementVfx.darkBarrageSlam;
            // Both variants are authored so the full heavy radius matches their largest ring.
            float upperScale = definition.elementVfx.ImpactScale(element, discharge.Radius);
            float speed = element == WeaponElement.Light
                ? OverburstElementTuning.Current.SafeLightTripleVfxPlaybackSpeed : 1f;
            SpawnWithPlayback(upper, impactCenter, FacingRotation(), upperScale, speed);
            // 빛 1타(내려치기)·암흑 내려치기의 공간 왜곡 충격파. 뒤따르는 타·폭발은 각 스케줄러가 낸다.
            float slamRadius = element == WeaponElement.Light
                ? discharge.LightHitRadius(discharge.LightFirstHitIndex) : discharge.Radius;
            UpperHeavyImpactFeedback.PlayShockwave(impactCenter, slamRadius);
            return;
        }
        GameObject prefab = definition.elementVfx.GetImpact(element);
        float scale = definition.elementVfx.ImpactScale(element, blastRadius);
        Spawn(prefab, impactCenter, Quaternion.identity, scale);
    }

    private Quaternion FacingRotation() => facing.sqrMagnitude > 0.0001f
        ? Quaternion.LookRotation(facing, Vector3.up) : Quaternion.identity;

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

    private static void SpawnWithPlayback(GameObject prefab, Vector3 position, Quaternion rotation, float scale, float speed)
    {
        if (prefab == null) return;
        TransientVfxPool.Spawn(prefab, position, rotation, 0f, MeleeHeavyVfxPreparation.RetainedCapacity(prefab),
            prepareBeforeActivation: instance =>
            {
                instance.transform.localScale = prefab.transform.localScale * scale;
                UpperElementCombatUtility.ApplyPlaybackSpeed(prefab, instance, speed);
            },
            returnMode: TransientVfxReturnMode.NaturalParticleCompletion);
    }

    private void DealDerivedDamage(CombatHealth target, float damage, Vector3 point, Vector3 direction)
    {
        if (target == null || target.IsDead || damage <= 0f) return;
        target.TakeDamage(new DamageInfo(damage, point, sourceActor, direction,
            triggersOnHitEffects: false, suppressDefaultHitVfx: true,
            element: discharge.Element, playerAttackKind: PlayerAttackKind.Heavy | PlayerAttackKind.Elemental, gemAttack: discharge.GemAttack));
    }
}
