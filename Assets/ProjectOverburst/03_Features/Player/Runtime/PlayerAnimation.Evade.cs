using UnityEngine;

public partial class PlayerAnimation
{
    private int explorationEvadeLayer = -1;
    private float explorationEvadeTargetWeight;
    private float explorationEvadeExitBlend = .08f;
    private float explorationEvadeBaseSpeed = 1f;

    public bool TryPlayConfiguredDodge(PlayerEvadeType kind, AnimationClip clip, string stateName,
        float duration, float entryBlend, float exitBlend)
    {
        if (targetAnimator == null || clip == null) return false;
        if (kind == PlayerEvadeType.CombatDodge)
        {
            ResolveWeaponCombatAnimatorRouter();
            return weaponCombatAnimatorRouter != null
                && weaponCombatAnimatorRouter.TryPlayCombatDodge(clip, stateName, duration, entryBlend, exitBlend);
        }
        explorationEvadeLayer = targetAnimator.GetLayerIndex(PlayerEvadeProfile.ExplorationLayer);
        if (explorationEvadeLayer < 0 || !targetAnimator.HasState(explorationEvadeLayer, Animator.StringToHash(stateName)))
            return false;
        explorationEvadeBaseSpeed = clip.length / Mathf.Max(.01f, duration);
        explorationEvadeExitBlend = Mathf.Max(.001f, exitBlend);
        explorationEvadeTargetWeight = 1f;
        targetAnimator.SetLayerWeight(explorationEvadeLayer, 1f);
        targetAnimator.SetFloat(PlayerEvadeProfile.ExplorationSpeed, EvadeStateSpeed(explorationEvadeBaseSpeed));
        targetAnimator.CrossFadeInFixedTime(stateName, Mathf.Max(0f, entryBlend) * Time.timeScale, explorationEvadeLayer, 0f);
        return true;
    }

    public void UpdateDodgeLightWindupClock(AnimationClip clip, float duration)
    {
        var profile = playerEquipment?.CurrentWeaponData?.GetCombatAnimationProfile();
        if (targetAnimator == null || clip == null || profile == null) return;
        targetAnimator.SetFloat(profile.actionSpeedParameterName, EvadeStateSpeed(clip.length / Mathf.Max(.01f, duration)));
    }

    public void ResumeDodgeVisual(AnimationClip clip, string state, float duration, float progress, float entryBlend, float exitBlend)
    {
        if (!TryPlayConfiguredDodge(PlayerEvadeType.CombatDodge, clip, state, duration, entryBlend, exitBlend)) return;
        var profile = playerEquipment.CurrentWeaponData.GetCombatAnimationProfile();
        int layer = targetAnimator.GetLayerIndex(profile.animatorLayerName);
        if (layer >= 0) targetAnimator.Play(state, layer, progress);
    }

    internal static float EvadeStateSpeed(float baseSpeed)
    {
        float scaled = Time.deltaTime;
        return scaled > .0000001f ? baseSpeed * OverburstGameClock.UnscaledDeltaTime / scaled : 0f;
    }

    private void UpdateExplorationEvadeLayer()
    {
        if (explorationEvadeLayer < 0 || targetAnimator == null) return;
        if (playerController != null && playerController.IsKnockedDown) explorationEvadeTargetWeight = 0f;
        if (explorationEvadeTargetWeight > 0f)
            targetAnimator.SetFloat(PlayerEvadeProfile.ExplorationSpeed, EvadeStateSpeed(explorationEvadeBaseSpeed));
        float weight = targetAnimator.GetLayerWeight(explorationEvadeLayer);
        targetAnimator.SetLayerWeight(explorationEvadeLayer, Mathf.MoveTowards(weight, explorationEvadeTargetWeight,
            OverburstGameClock.UnscaledDeltaTime / explorationEvadeExitBlend));
    }

    public void BlendDodgeLightRecoveryToLocomotion(float transitionDuration)
    {
        if (weaponCombatAnimatorRouter != null
            && weaponCombatAnimatorRouter.TryBlendDodgeLightRecoveryToLocomotion(transitionDuration)) return;
        CancelWeaponRuntimeState();
    }

    public void FinishEvadeAnimation(PlayerEvadeType kind, bool completed)
    {
        if (kind == PlayerEvadeType.ExplorationDodge)
        {
            explorationEvadeTargetWeight = 0f;
            if (!completed && explorationEvadeLayer >= 0 && targetAnimator != null)
                targetAnimator.SetLayerWeight(explorationEvadeLayer, 0f);
        }
        else weaponCombatAnimatorRouter?.FinishCombatEvade();
    }
}
