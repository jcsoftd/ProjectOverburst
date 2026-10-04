using UnityEngine;

public partial class PlayerMovement
{
    private MeleeWeaponCombatAnimatorDriver stopCurveDriver;
    private CombatMoveMotion stopCurveMotion;
    private Vector3 stopCurveDirection;
    private float stopCurveElapsed, stopCurveDistance, stopCurveRate, stopCurveScale, stopCurveCorrection;
    private float stopCurveEntrySeconds;
    private float stopCurveStartTime, stopCurveStartDistance, stopCurveTargetDistance, stopCurveMomentum;
    private bool stopCurveActive;

    public bool IsCombatStopCurveActive => stopCurveActive;
    public CombatMoveMotion CombatStopMotion => stopCurveMotion;
    public float CombatStopCurveElapsed => stopCurveElapsed;
    public float CombatStopCurveDistance => stopCurveDistance;
    public float CombatStopCurveRate => stopCurveRate;
    public float CombatStopCurveStartTime => stopCurveStartTime;
    public float CombatStopCurveTargetDistance => stopCurveTargetDistance;
    public float CombatStopMomentum => stopCurveMomentum;

    private void CancelCombatStopCurve(bool clearVelocity)
    {
        if (stopCurveActive && clearVelocity) locomotion?.Stop();
        stopCurveActive = false;
        stopCurveMotion = null;
    }

    private bool StepCombatStopCurve(CombatLocomotionSet set, float deltaTime)
    {
        if (stopCurveDriver == null) stopCurveDriver = GetComponent<MeleeWeaponCombatAnimatorDriver>();
        if (combatStopWasMoving && stopCurveDriver != null
            && stopCurveDriver.TryGetFacingStop(out var motion, out float duration, out float humanScale)
            && motion.stopDistance != null && motion.stopDistance.length > 1 && duration > 0f)
        {
            Vector3 velocity = locomotion.HorizontalVelocity;
            stopCurveMotion = motion;
            stopCurveDirection = velocity.sqrMagnitude > .000001f ? velocity.normalized : Vector3.zero;
            stopCurveElapsed = stopCurveDistance = 0f;
            stopCurveRate = motion.stop.length / duration;
            stopCurveScale = humanScale / Mathf.Max(.001f, motion.stopSourceHumanScale);
            stopCurveMomentum = set.scaleStopWithMoveDuration
                ? Mathf.Lerp(set.shortStopTravelMultiplier, 1f, Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(set.shortMoveSeconds, Mathf.Max(set.shortMoveSeconds + .01f, set.fullMomentumSeconds), combatMoveSeconds)))
                : 1f;
            float ratio = Mathf.Clamp(motion.stopTravelMultiplier, .05f, 1f) * stopCurveMomentum;
            float speed = velocity.magnitude;
            stopCurveTargetDistance = PredictStopTravel(motion, 0f, speed, set.stopEntrySeconds) * ratio;
            stopCurveStartTime = ratio >= .99999f ? 0f
                : FindStopStartTime(motion, stopCurveTargetDistance, speed, set.stopEntrySeconds);
            if (!stopCurveDriver.TryBeginFacingStop(out _, out _, out _, stopCurveStartTime)) return false;
            stopCurveStartDistance = motion.stopDistance.Evaluate(stopCurveStartTime) * stopCurveScale;
            stopCurveEntrySeconds = Mathf.Min(set.stopEntrySeconds, (motion.stop.length - stopCurveStartTime) / stopCurveRate);
            stopCurveCorrection = speed - StopSourceSpeed(motion, stopCurveStartTime);
            stopCurveActive = true;
            combatMoveSeconds = 0f;
        }
        if (!stopCurveActive) return false;
        combatStopWasMoving = false;
        combatStopDeceleration = 0f;
        if (stopCurveDriver.ActiveFacingStop != stopCurveMotion
            || stopCurveStartTime + stopCurveElapsed * stopCurveRate >= stopCurveMotion.stop.length)
        {
            CancelCombatStopCurve(true);
            return false;
        }
        if (deltaTime <= 0f) return true;
        stopCurveElapsed += deltaTime;
        float time = Mathf.Min(stopCurveStartTime + stopCurveElapsed * stopCurveRate, stopCurveMotion.stop.length);
        float distance = stopCurveMotion.stopDistance.Evaluate(time) * stopCurveScale - stopCurveStartDistance;
        if (stopCurveMotion.stopMatchEntrySpeed)
        {
            float p = stopCurveEntrySeconds > 0 ? Mathf.Clamp01(stopCurveElapsed / stopCurveEntrySeconds) : 1f;
            distance += stopCurveCorrection * stopCurveEntrySeconds / 3f * (1f - Mathf.Pow(1f - p, 3));
            distance = Mathf.Max(stopCurveDistance, distance);
        }
        // Set the per-frame average velocity; the normal motor performs the only Move,
        // including gravity, slopes, platforms and collision. Consume the endpoint once.
        locomotion.SetHorizontalVelocity(stopCurveDirection * ((distance - stopCurveDistance) / deltaTime));
        stopCurveDistance = distance;
        return true;
    }

    private float StopSourceSpeed(CombatMoveMotion motion, float start)
    {
        float step = Mathf.Min(motion.stop.length / 120f, motion.stop.length - start);
        return step > .000001f ? (motion.stopDistance.Evaluate(start + step) - motion.stopDistance.Evaluate(start))
            / step * stopCurveRate * stopCurveScale : 0f;
    }
    private float PredictStopTravel(CombatMoveMotion motion, float start, float entrySpeed, float entrySeconds)
    {
        float origin = motion.stopDistance.Evaluate(start) * stopCurveScale;
        if (!motion.stopMatchEntrySpeed)
            return Mathf.Max(0f, motion.stopDistance.Evaluate(motion.stop.length) * stopCurveScale - origin);
        entrySeconds = Mathf.Min(entrySeconds, (motion.stop.length - start) / stopCurveRate);
        float correction = entrySpeed - StopSourceSpeed(motion, start);
        float maximum = 0f;
        // The C policy consumes a monotone envelope, including its entry-velocity correction.
        for (int i = 1; i <= 120; i++)
        {
            float sourceTime = Mathf.Lerp(start, motion.stop.length, i / 120f);
            float elapsed = (sourceTime - start) / stopCurveRate;
            float p = entrySeconds > 0f ? Mathf.Clamp01(elapsed / entrySeconds) : 1f;
            float distance = motion.stopDistance.Evaluate(sourceTime) * stopCurveScale - origin
                + correction * entrySeconds / 3f * (1f - Mathf.Pow(1f - p, 3));
            maximum = Mathf.Max(maximum, distance);
        }
        return maximum;
    }
    private float FindStopStartTime(CombatMoveMotion motion, float target, float speed, float entrySeconds)
    {
        // One release-time solve; the motor and Animator then consume the same unscaled tail.
        float low = 0f, high = motion.stop.length;
        for (int i = 0; i < 22; i++)
        {
            float middle = (low + high) * .5f;
            if (PredictStopTravel(motion, middle, speed, entrySeconds) > target) low = middle;
            else high = middle;
        }
        return (low + high) * .5f;
    }
}
