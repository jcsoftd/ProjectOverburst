using UnityEngine;

public partial class MeleeRuntime
{
    public bool IsHeavyMovementAfterimageWindow
    {
        get
        {
            if (!IsHeavyAttackInProgress || heavyDischargeCommitted || activeAttackPhases == null || activeAttackPhases.Length == 0) return false;
            int impact = activeHeavyDefinition != null ? activeHeavyDefinition.SafeDischargePhaseIndex : 0;
            return GetAttackNormalizedTime() < activeAttackPhases[impact].SafeStart;
        }
    }

    private DashHeavyFocusPresentation groundHeavyFocus;
    private HeavyFocusWindow groundFocusWindow;
    private bool groundGatherPlayed, groundReleasePlayed;

    private void BeginHeavyFocusPresentation()
    {
        if (!activeAttackIsHeavy || activeAttackAnimationClip == null || activeWeaponData?.weaponClass != WeaponClass.Greatsword) return;
        if (activeDodgeFollowUp == PlayerDodgeFollowUpKind.Heavy)
        {
            if (dashHeavyPresentation == null && !dashHeavyWindup)
            {
                dashHeavyPlaybackSpeed = activeAttackAnimationSpeed;
                dashHeavyGatherPlayed = dashHeavyReleasePlayed = dashHeavySwingPlayed = false;
                dashHeavyPresentation = DashHeavyFocusPresentation.CanBegin(playerEquipment, activeGemAttack)
                    ? DashHeavyFocusPresentation.Create(playerEquipment, activeGemAttack.Element) : null;
                ResolveDashHeavySwingCue();
            }
            return;
        }
        EndGroundHeavyFocus();
        if (!DashHeavyFocusPresentation.CanBegin(playerEquipment, activeGemAttack)) return;
        bool parried = activeHeavyDefinition == activeWeaponData.GetMeleeDefinition()?.parriedHeavyAttackDefinition;
        groundFocusWindow = parried ? HeavyFocusWindow.Parried(activeAttackAnimationClip.length)
            : HeavyFocusWindow.Ground(activeAttackAnimationClip.length);
        groundGatherPlayed = groundReleasePlayed = false;
        groundHeavyFocus = DashHeavyFocusPresentation.Create(playerEquipment, activeGemAttack.Element, groundFocusWindow);
    }
    private void TickGroundHeavyFocus(float progress)
    {
        if (!activeAttackIsHeavy || groundHeavyFocus == null || activeAttackAnimationClip == null) return;
        float source = progress * activeAttackAnimationClip.length;
        groundHeavyFocus.Tick(source);
        if (!groundGatherPlayed && source >= groundFocusWindow.Start)
        {
            groundGatherPlayed = true;
            if (source < groundFocusWindow.End)
                groundHeavyFocus.PlayGather(groundFocusWindow.UnscaledDuration(source, groundFocusWindow.End, false, activeAttackAnimationSpeed));
        }
        if (!groundReleasePlayed && source >= groundFocusWindow.End)
        { groundReleasePlayed = true; groundHeavyFocus.PlayRelease(); }
    }
    private void EndGroundHeavyFocus() { groundHeavyFocus?.Dispose(); groundHeavyFocus = null; }
}
