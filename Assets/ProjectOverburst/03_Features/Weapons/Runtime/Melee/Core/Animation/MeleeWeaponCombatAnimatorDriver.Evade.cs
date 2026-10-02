using UnityEngine;

public partial class MeleeWeaponCombatAnimatorDriver
{
    private float evadeAnimationBaseSpeed = 1f;
    private float evadeExitBlend = .08f;
    private bool IsEvadeAction => activeAction == DriverAction.Roll || activeAction == DriverAction.Dodge;

    public bool TryPlayDodge(AnimationClip clip, string stateName, float actionDuration, float entryBlend, float exitBlend)
    {
        if (clip == null || !CanPlayCombatAction() || !HasState(stateName)) return false;
        float duration = Mathf.Max(.01f, actionDuration);
        evadeAnimationBaseSpeed = clip.length / duration;
        evadeExitBlend = Mathf.Max(0f, exitBlend);
        PlayActionState(stateName, DriverAction.Dodge, duration, Mathf.Max(0f, entryBlend) * Time.timeScale);
        UpdateEvadePlaybackClock();
        return true;
    }

    private void UpdateEvadePlaybackClock()
    {
        if (!IsEvadeAction || targetAnimator == null) return;
        targetAnimator.SetFloat(actionSpeedParameterName, PlayerAnimation.EvadeStateSpeed(evadeAnimationBaseSpeed));
    }

    public void FinishEvade()
    {
        if (!IsEvadeAction) return;
        activeAction = DriverAction.None;
        activeActionEndTime = 0f;
        targetTransitionLowerLayerWeight = 0f;
        targetAnimator.SetFloat(actionSpeedParameterName, 1f);
        if (combatRequested && !legacySuppressed) PlayLocomotionByGuardState(evadeExitBlend, true);
    }
}
