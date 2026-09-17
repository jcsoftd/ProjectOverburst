using System;
using System.Collections.Generic;

public static class MeleeAttackVfxCueTuningPreserver
{
    private readonly struct PhaseKey : IEquatable<PhaseKey>
    {
        private readonly string attackId;
        private readonly int phaseIndex;

        public PhaseKey(string attackId, int phaseIndex)
        {
            this.attackId = attackId ?? string.Empty;
            this.phaseIndex = phaseIndex;
        }

        public bool Equals(PhaseKey other)
        {
            return attackId == other.attackId && phaseIndex == other.phaseIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is PhaseKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (attackId.GetHashCode() * 397) ^ phaseIndex;
            }
        }
    }

    private readonly struct PhaseSlopeTuning
    {
        public readonly bool UseBaked;
        public readonly float BakedDegrees;
        public readonly float OffsetDegrees;
        public readonly AttackVfxSwingSettings SwingSettings;

        public PhaseSlopeTuning(AttackPhaseData phase)
        {
            UseBaked = phase.useBakedVfxSwingSlope;
            BakedDegrees = phase.bakedVfxSwingSlopeDegrees;
            OffsetDegrees = phase.vfxSwingSlopeOffsetDegrees;
            SwingSettings = phase.vfxSwingSettings;
        }

        public AttackPhaseData Apply(AttackPhaseData phase)
        {
            phase.useBakedVfxSwingSlope = UseBaked;
            phase.bakedVfxSwingSlopeDegrees = BakedDegrees;
            phase.vfxSwingSlopeOffsetDegrees = OffsetDegrees;
            phase.vfxSwingSettings = SwingSettings;
            return phase;
        }
    }

    public static void Restore(
        MeleeComboStepData[] existingSteps,
        MeleeComboStepData[] rebuiltSteps)
    {
        if (existingSteps == null || rebuiltSteps == null)
            return;

        Dictionary<PhaseKey, PhaseSlopeTuning> phaseTuning = CapturePhaseTuning(existingSteps);
        for (int stepIndex = 0; stepIndex < rebuiltSteps.Length; stepIndex++)
        {
            MeleeComboStepData step = rebuiltSteps[stepIndex];
            if (step.attackPhases == null)
                continue;

            for (int phaseIndex = 0; phaseIndex < step.attackPhases.Length; phaseIndex++)
            {
                AttackPhaseData phase = step.attackPhases[phaseIndex];
                PhaseKey key = new PhaseKey(step.attackId, phaseIndex);
                if (phaseTuning.TryGetValue(key, out PhaseSlopeTuning saved))
                    phase = saved.Apply(phase);

                AttackPhaseData existingPhase = FindPhase(
                    existingSteps,
                    step.attackId,
                    phaseIndex,
                    out bool found);
                if (found)
                    phase.vfxCues = Restore(existingPhase.vfxCues, phase.vfxCues);

                step.attackPhases[phaseIndex] = phase;
            }

            rebuiltSteps[stepIndex] = step;
        }
    }

    public static AttackVfxCueData[] Restore(
        AttackVfxCueData[] existingCues,
        AttackVfxCueData[] rebuiltCues)
    {
        if (existingCues == null || rebuiltCues == null)
            return rebuiltCues;

        for (int rebuiltIndex = 0; rebuiltIndex < rebuiltCues.Length; rebuiltIndex++)
        {
            AttackVfxCueData rebuilt = rebuiltCues[rebuiltIndex];
            for (int existingIndex = 0; existingIndex < existingCues.Length; existingIndex++)
            {
                AttackVfxCueData existing = existingCues[existingIndex];
                if (existing.elementOverrideKey != rebuilt.elementOverrideKey)
                    continue;

                rebuilt.triggerProgress = existing.triggerProgress;
                rebuilt.swingSlopeOffsetDegrees = existing.swingSlopeOffsetDegrees;
                rebuilt.localEulerOffset = existing.localEulerOffset;
                rebuiltCues[rebuiltIndex] = rebuilt;
                break;
            }
        }

        return rebuiltCues;
    }

    private static Dictionary<PhaseKey, PhaseSlopeTuning> CapturePhaseTuning(
        MeleeComboStepData[] steps)
    {
        Dictionary<PhaseKey, PhaseSlopeTuning> result =
            new Dictionary<PhaseKey, PhaseSlopeTuning>();
        for (int stepIndex = 0; stepIndex < steps.Length; stepIndex++)
        {
            MeleeComboStepData step = steps[stepIndex];
            if (step.attackPhases == null)
                continue;

            for (int phaseIndex = 0; phaseIndex < step.attackPhases.Length; phaseIndex++)
            {
                result[new PhaseKey(step.attackId, phaseIndex)] =
                    new PhaseSlopeTuning(step.attackPhases[phaseIndex]);
            }
        }

        return result;
    }

    private static AttackPhaseData FindPhase(
        MeleeComboStepData[] steps,
        string attackId,
        int phaseIndex,
        out bool found)
    {
        for (int stepIndex = 0; stepIndex < steps.Length; stepIndex++)
        {
            MeleeComboStepData step = steps[stepIndex];
            if (step.attackId != attackId
                || step.attackPhases == null
                || phaseIndex < 0
                || phaseIndex >= step.attackPhases.Length)
            {
                continue;
            }

            found = true;
            return step.attackPhases[phaseIndex];
        }

        found = false;
        return default;
    }
}
