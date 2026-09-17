using System;
using UnityEngine;

public sealed class AttackVfxCuePlayer
{
    private AttackVfxCueData[] cues;
    private bool[] played;
    private bool hasFirstTrace;
    private Vector3 firstTracePoint;
    private Vector3 lastTracePoint;
    private bool useBakedSwingSlope;
    private float bakedSwingSlopeDegrees;
    private AttackPhaseData phase;

    public void Begin(AttackPhaseData phase)
    {
        cues = phase.vfxCues;
        this.phase = phase;
        int count = cues != null ? cues.Length : 0;
        played = count > 0 ? new bool[count] : null;
        hasFirstTrace = false;
        firstTracePoint = default;
        lastTracePoint = default;
        useBakedSwingSlope = phase.useBakedVfxSwingSlope;
        bakedSwingSlopeDegrees = phase.bakedVfxSwingSlopeDegrees;
    }

    public void Tick(
        AttackPatternRuntimeData pattern,
        AttackPatternBasis basis,
        float progress,
        AttackProgressSample sample,
        WeaponElement element,
        float phaseVfxScale,
        float attackRangeScale)
    {
        SampleTrace(sample);
        if (cues == null || played == null)
            return;

        for (int i = 0; i < cues.Length; i++)
        {
            if (played[i] || progress + 0.0001f < Mathf.Clamp01(cues[i].triggerProgress))
                continue;

            played[i] = true;
            Spawn(cues[i], pattern, basis, element, phaseVfxScale, attackRangeScale);
        }
    }

    public void Reset()
    {
        cues = null;
        played = null;
        hasFirstTrace = false;
        firstTracePoint = default;
        lastTracePoint = default;
        useBakedSwingSlope = false;
        bakedSwingSlopeDegrees = 0f;
        phase = default;
    }

    private void SampleTrace(AttackProgressSample sample)
    {
        if (!sample.HasTrace)
            return;

        if (!hasFirstTrace)
        {
            hasFirstTrace = true;
            firstTracePoint = sample.TracePoint;
        }

        lastTracePoint = sample.TracePoint;
    }

    private void Spawn(
        AttackVfxCueData cue,
        AttackPatternRuntimeData pattern,
        AttackPatternBasis basis,
        WeaponElement element,
        float phaseVfxScale,
        float attackRangeScale)
    {
        MeleeAttackVfxDefinition definition = cue.definition;
        if (definition == null)
            return;

        Quaternion basisRotation = Quaternion.LookRotation(basis.Forward, Vector3.up);
        float runtimeSwingSlope = !useBakedSwingSlope && cue.autoSwingSlope
            ? ResolveSwingSlope(basis)
            : 0f;
        float attackSwingRotationOffset = phase.ResolveVfxSwingRotationOffset(
            useBakedSwingSlope ? bakedSwingSlopeDegrees : runtimeSwingSlope);
        float swingSlope = cue.ResolveSwingSlope(attackSwingRotationOffset);
        Quaternion rotation = basisRotation
            * Quaternion.Euler(0f, 0f, swingSlope)
            * Quaternion.Euler(definition.localEulerOffset + cue.localEulerOffset);
        Vector3 position = ResolvePosition(cue, definition, pattern, basis, basisRotation);
        if (MeleeElementSfxService.IsSlashCueKey(cue.elementOverrideKey))
            MeleeElementSfxService.TryPlaySlash(element, position); // 논리 Cue당 한 번

        GameObject prefab = MeleeAttackVfxResolver.Current.ResolvePrefab(
            definition,
            cue.elementOverrideKey);
        if (prefab == null)
            return;

        float rangeScale = ScalesWithAttackRange(cue.motionRole)
            ? Mathf.Max(0.01f, attackRangeScale)
            : 1f;
        float scale = Mathf.Max(0.01f, phaseVfxScale * cue.SafeScaleMultiplier * rangeScale);
        Vector3 resolvedScale = definition.baseScale * scale;
        bool sharedSlash = MeleeElementAttackVfxCatalog.IsSharedSlashKey(
            cue.elementOverrideKey);
        bool horizontalMirror = cue.mirrorAxis == AttackVfxMirrorAxis.Horizontal
            || (definition.mirrorRightToLeft
                && pattern.Direction == AttackFillDirection.RightToLeft);
        if (sharedSlash)
        {
            horizontalMirror = MeleeSharedSlashSpawnContract.ResolveHorizontalMirror(
                cue.elementOverrideKey,
                element,
                horizontalMirror); // 원소별 실제 VFX 방향 보정
        }
        bool verticalMirror = cue.mirrorAxis == AttackVfxMirrorAxis.Vertical;
        if (horizontalMirror)
        {
            switch (definition.horizontalMirrorScaleAxis)
            {
                case AttackVfxScaleMirrorAxis.Y:
                    resolvedScale.y = -resolvedScale.y;
                    break;
                case AttackVfxScaleMirrorAxis.Z:
                    resolvedScale.z = -resolvedScale.z;
                    break;
                default:
                    resolvedScale.x = -resolvedScale.x;
                    break;
            }
        }
        if (verticalMirror)
        {
            resolvedScale.y = -resolvedScale.y;
        }

        int instanceCount = MeleeSharedSlashSpawnContract.ResolveInstanceCount(
            cue.elementOverrideKey);
        for (int i = 0; i < instanceCount; i++)
        {
            Quaternion instanceRotation = MeleeSharedSlashSpawnContract.ResolveInstanceRotation(
                i,
                rotation,
                resolvedScale);
            SpawnResolvedInstance(
                prefab,
                position,
                instanceRotation,
                resolvedScale,
                definition,
                element,
                horizontalMirror,
                sharedSlash,
                MeleeSharedSlashSpawnContract.ResolveReturnMode(cue.elementOverrideKey));
        }
    }

    private static void SpawnResolvedInstance(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale,
        MeleeAttackVfxDefinition definition,
        WeaponElement element,
        bool horizontalMirror,
        bool sharedSlash,
        TransientVfxReturnMode returnMode)
    {
        Action<GameObject> prepare = instance =>
        {
            instance.transform.localScale = scale;
            instance.GetComponent<VfxMirrorCompensation>()?.Apply(horizontalMirror);
            if (sharedSlash)
            {
                MeleeElementSlashController controller =
                    instance.GetComponent<MeleeElementSlashController>();
                if (controller == null)
                    throw new InvalidOperationException("공용 슬래시 Controller가 없습니다.");
                controller.SetElement(element); // 활성화 전 인스턴스별 주입
            }
        };

        TransientVfxPool.Spawn(
            prefab,
            position,
            rotation,
            definition.lifetime,
            definition.SafePoolCapacity,
            null,
            prepare,
            returnMode);
    }

    private static bool ScalesWithAttackRange(AttackVfxMotionRole motionRole)
    {
        switch (motionRole)
        {
            case AttackVfxMotionRole.HorizontalSweep:
            case AttackVfxMotionRole.HorizontalCircular:
            case AttackVfxMotionRole.VerticalRising:
            case AttackVfxMotionRole.VerticalFalling:
            case AttackVfxMotionRole.Thrust:
            case AttackVfxMotionRole.GroundImpact:
                return true;
            default:
                return false;
        }
    }

    private Vector3 ResolvePosition(
        AttackVfxCueData cue,
        MeleeAttackVfxDefinition definition,
        AttackPatternRuntimeData pattern,
        AttackPatternBasis basis,
        Quaternion basisRotation)
    {
        Vector3 position;
        switch (cue.placementMode)
        {
            case AttackVfxPlacementMode.WeaponTracePoint:
                position = hasFirstTrace ? lastTracePoint : basis.GetPatternOrigin(pattern);
                break;
            case AttackVfxPlacementMode.PatternGround:
                position = ResolveGroundPosition(basis.GetPatternOrigin(pattern), definition);
                break;
            case AttackVfxPlacementMode.OwnerOrigin:
                position = basis.Origin;
                break;
            default:
                position = basis.GetPatternOrigin(pattern);
                break;
        }

        return position + basisRotation * definition.localPositionOffset;
    }

    private static Vector3 ResolveGroundPosition(
        Vector3 patternOrigin,
        MeleeAttackVfxDefinition definition)
    {
        float probeHeight = Mathf.Max(0f, definition.groundProbeHeight);
        float probeDistance = Mathf.Max(0.01f, definition.groundProbeDistance);
        Vector3 rayOrigin = patternOrigin + Vector3.up * probeHeight;
        if (Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out RaycastHit hit,
                probeDistance,
                definition.groundLayerMask.value,
                QueryTriggerInteraction.Ignore))
        {
            return hit.point + hit.normal * definition.groundSurfaceOffset;
        }

        return patternOrigin + Vector3.up * definition.groundSurfaceOffset;
    }

    private float ResolveSwingSlope(AttackPatternBasis basis)
    {
        if (!hasFirstTrace)
            return 0f;

        Vector3 delta = lastTracePoint - firstTracePoint;
        float lateral = Vector3.Dot(delta, basis.Right);
        if (Mathf.Abs(lateral) <= 0.0001f && Mathf.Abs(delta.y) <= 0.0001f)
            return 0f;

        return Mathf.Atan2(delta.y, Mathf.Abs(lateral)) * Mathf.Rad2Deg;
    }
}
