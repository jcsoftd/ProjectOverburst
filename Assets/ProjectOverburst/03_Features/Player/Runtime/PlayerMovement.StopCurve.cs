using UnityEngine;

public partial class PlayerMovement
{
    private MeleeWeaponCombatAnimatorDriver stopCurveDriver;
    private CombatMoveMotion stopCurveMotion;
    private Vector3 stopCurveDirection;
    private float stopCurveElapsed, stopCurveDistance, stopCurveRate, stopCurveScale, stopCurveCorrection;
    private float stopCurveEntrySeconds;
    private bool stopCurveActive;

    public bool IsCombatStopCurveActive => stopCurveActive;
    public CombatMoveMotion CombatStopMotion => stopCurveMotion;
    public float CombatStopCurveElapsed => stopCurveElapsed;
    public float CombatStopCurveDistance => stopCurveDistance;
    public float CombatStopCurveRate => stopCurveRate;

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
            && stopCurveDriver.TryBeginFacingStop(out var motion, out float duration, out float humanScale)
            && motion.stopDistance != null && motion.stopDistance.length > 1 && duration > 0f)
        {
            Vector3 velocity = locomotion.HorizontalVelocity;
            stopCurveMotion = motion;
            stopCurveDirection = velocity.sqrMagnitude > .000001f ? velocity.normalized : Vector3.zero;
            stopCurveElapsed = stopCurveDistance = 0f;
            stopCurveRate = motion.stop.length / duration;
            stopCurveScale = humanScale / Mathf.Max(.001f, motion.stopSourceHumanScale);
            stopCurveEntrySeconds = set.stopEntrySeconds;
            float firstTime = motion.stopDistance.keys[1].time;
            float initialSpeed = (motion.stopDistance.Evaluate(firstTime) - motion.stopDistance.Evaluate(0))
                / Mathf.Max(.0001f, firstTime) * stopCurveRate * stopCurveScale;
            stopCurveCorrection = velocity.magnitude - initialSpeed;
            stopCurveActive = true;
        }
        if (!stopCurveActive) return false;
        combatStopWasMoving = false;
        combatStopDeceleration = 0f;
        if (stopCurveDriver.ActiveFacingStop != stopCurveMotion
            || stopCurveElapsed * stopCurveRate >= stopCurveMotion.stop.length)
        {
            CancelCombatStopCurve(true);
            return false;
        }
        if (deltaTime <= 0f) return true;
        stopCurveElapsed += deltaTime;
        float time = Mathf.Min(stopCurveElapsed * stopCurveRate, stopCurveMotion.stop.length);
        float distance = stopCurveMotion.stopDistance.Evaluate(time) * stopCurveScale;
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
}
