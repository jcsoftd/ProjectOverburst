using UnityEngine;

public partial class MeleeRuntime
{
    private static readonly float DashHeavySwingCue = HeavyFocusWindow.Dash.SourceAtAudioLead(.5f, .375f, true);
    private bool dashHeavyWindup, dashHeavyGatherPlayed, dashHeavyReleasePlayed, dashHeavySwingPlayed;
    private PlayerDodgeFollowUpRequest dashHeavyRequest;
    private ElementGemAttackSnapshot dashHeavyPreviewGem;
    private float dashHeavyPreviewStart, dashHeavyPlaybackSpeed, dashHeavyMovedDistance, dashHeavyHandoffElapsed;
    private DashHeavyTravelPlan dashHeavyTravel;
    private DashHeavyFocusPresentation dashHeavyPresentation;
    private int dashHeavyDarkBarrageId;
    public bool IsDashHeavyWindupActive => dashHeavyWindup;
    public float DashHeavyTravelDistance(float dashElapsed) => dashHeavyTravel?.Position(dashHeavyTravel.Start + Mathf.Max(0f, Time.time - dashHeavyPreviewStart)) ?? 0f;

    public void PreviewDashHeavy(in PlayerDodgeFollowUpRequest request, float dashElapsed,
        float distance, float duration, float moveEase)
    {
        ResolveReferences();
        var inputs = ResolveFacade()?.CombatInputs;
        var definition = playerEquipment?.CurrentWeaponData?.GetMeleeDefinition();
        var heavy = definition?.dashHeavyAttackDefinition;
        if (isAttacking || inputs == null || !inputs.IsDodgeFollowUpRequestValid(request)
            || heavy == null || !heavy.IsConfigured || playerAnimatorController == null
            || (dashHeavyWindup&&!dashHeavyPreviewGem.IsCurrent))
        { inputs?.ClearDodgeFollowUp(); CancelDodgeLightWindup(); return; }
        if (!dashHeavyWindup)
        {
            CancelDodgeLightWindup();
            var stats = UpperElementCombatUtility.ApplyRadianceAttackSpeed(
                FlaskCombatModifiers.Apply(playerEquipment.CurrentWeaponStats,gameObject),gameObject);
            dashHeavyPlaybackSpeed = MeleeAttackSpeedPolicy.ToPlaybackMultiplier(
                Mathf.Max(.01f,stats.meleeAttackSpeedMultiplier),definition.baseSettings.SafeAnimationPlaybackBaseline)
                * Mathf.Max(.01f,heavy.attack.animationSpeedMultiplier);
            // High attack speed must not show the contact pose before the dash can
            // transfer damage ownership. Early input waits; late input still starts now.
            if(duration-dashElapsed>DashHeavyFocusClock.RealAt(.5f)/dashHeavyPlaybackSpeed)return;
            float fullDuration = heavy.attack.animationClip.length/dashHeavyPlaybackSpeed;
            float remaining = fullDuration*heavy.attack.playbackAcceleration.ToElapsed(1f);
            if (!playerAnimatorController.PlayMeleeCombatAttack(0,heavy.attack.animationClip,dashHeavyPlaybackSpeed,
                remaining,.08f*Time.timeScale,true,0f,heavy.attack.playbackAcceleration)) return;
            dashHeavyPreviewStart = Time.time;
            dashHeavyRequest = request;
            dashHeavyPreviewGem = new ElementGemAttackSnapshot(playerEquipment);
            dashHeavyTravel = new DashHeavyTravelPlan(dashElapsed,distance,duration,moveEase,dashHeavyPlaybackSpeed);
            dashHeavyWindup = true;
            dashHeavyGatherPlayed = dashHeavyReleasePlayed = dashHeavySwingPlayed = false;
            dashHeavyPresentation = DashHeavyFocusPresentation.Create(playerEquipment,
                dashHeavyPreviewGem.Element);
        }
        float elapsed = (Time.time-dashHeavyPreviewStart)*dashHeavyPlaybackSpeed;
        TickDashHeavyFocus(DashHeavyFocusClock.Sample(elapsed));
    }
    private float DashHeavyHandoffProgress(in PlayerDodgeFollowUpRequest request)
    {
        if (!dashHeavyWindup || request.EvadeExecutionId != dashHeavyRequest.EvadeExecutionId
            || request.InputRevision != dashHeavyRequest.InputRevision) return 0f;
        dashHeavyHandoffElapsed = Mathf.Max(0f,Time.time-dashHeavyPreviewStart);
        return Mathf.Clamp(DashHeavyFocusClock.Sample(dashHeavyHandoffElapsed
            *dashHeavyPlaybackSpeed)/DashHeavyFocusClock.ClipLength,0f,.95f);
    }
    private bool CancelDashHeavyWindup()
    {
        if (!dashHeavyWindup) return false;
        dashHeavyWindup = false;
        EndDashHeavyPresentation();
        playerAnimatorController?.CancelWeaponRuntimeState();
        return true;
    }
    private void AdoptDashHeavyTravel(float entryProgress)
    {
        if (activeDodgeFollowUp != PlayerDodgeFollowUpKind.Heavy || dashHeavyTravel == null) return;
        float elapsed = dashHeavyHandoffElapsed;
        dashHeavyMovedDistance = dashHeavyTravel.Position(dashHeavyTravel.Start+elapsed);
    }
    private void TickDashHeavyTravelAndFocus(float progress)
    {
        if (activeDodgeFollowUp != PlayerDodgeFollowUpKind.Heavy) { TickGroundHeavyFocus(progress); return; }
        if (dashHeavyTravel != null)
        {
            float t=dashHeavyTravel.Start+Mathf.Max(0f,Time.time-attackStartTime);
            float target=dashHeavyTravel.Position(t);
            float delta=Mathf.Max(0f,target-dashHeavyMovedDistance);
            dashHeavyMovedDistance=target;
            if(delta>0f) playerController?.CombatMotion?.ApplyEvadeDisplacement(activeAttackDirection*delta);
        }
        TickDashHeavyFocus(progress*DashHeavyFocusClock.ClipLength);
    }
    private void TickDashHeavyFocus(float sourceSeconds)
    {
        dashHeavyPresentation?.Tick(sourceSeconds);
        if (!dashHeavyGatherPlayed && sourceSeconds >= HeavyFocusWindow.Dash.Start)
        {
            dashHeavyGatherPlayed=true;
            dashHeavyPresentation?.PlayGather(HeavyFocusWindow.Dash.UnscaledDuration(sourceSeconds, HeavyFocusWindow.Dash.End, true)/Mathf.Max(.01f,dashHeavyPlaybackSpeed));
        }
        if (!dashHeavyReleasePlayed && sourceSeconds >= HeavyFocusWindow.Dash.End)
        { dashHeavyReleasePlayed=true; dashHeavyPresentation?.PlayRelease(); }
        // PCM RMS peak of the selected sweep is .375s. Align that peak with source frame30.
        if (!dashHeavySwingPlayed && sourceSeconds >= DashHeavySwingCue)
        {
            dashHeavySwingPlayed=true;
            CombatActionSfxService.PlayDashHeavySwing(dashHeavyPlaybackSpeed,transform.position);
        }
    }
    private void CommitDashHeavyDischarge(AttackPhaseData phase)
    {
        heavyDischargeCommitted=true;
        bool committed=activeHeavyEnergy != null && activeHeavyEnergy.TryCommitDischarge(
            activeStats.damage*phase.impact.SafeDamageMultiplier,out activeDischarge,activeGemAttack);
        if(!committed)return;
        if(heavyParried)activeDischarge.TryRefundParried();
        var melee=activeWeaponData.GetMeleeDefinition();
        var pattern=phase.ResolvePattern(activeStats.range,activeStats.meleeSlashAngle,melee.baseSettings.hitWidth);
        Vector3 center=transform.position+activeAttackDirection*pattern.ForwardOffset;
        CombatMomentPresentation.Heavy(playerEquipment,activeActionId,0,activeDischarge,center,activeAttackDirection,pattern.Width*.5f,true);
        heavyDischargeExecutor.Begin(activeDischarge,activeHeavyDefinition,combatTarget,gameObject,
            center,activeAttackDirection,pattern.Width*.5f,pattern.VerticalTolerance,true);
        if(activeDischarge.Element==WeaponElement.Dark)
            dashHeavyDarkBarrageId=DarkBarrageScheduler.PrepareSlam(activeDischarge,gameObject,combatTarget.Team,
                center,pattern.Range,pattern.VerticalTolerance,activeHeavyDefinition.elementVfx);
    }
    private void CompleteDashHeavyWave(float progress)
    {
        if(activeDodgeFollowUp != PlayerDodgeFollowUpKind.Heavy || activeAttackPhases==null
            || progress<activeAttackPhases[0].SafeEnd)return;
        CompleteDashHeavyDarkBarrage();
    }
    private void CompleteDashHeavyDarkBarrage()
    {
        if(dashHeavyDarkBarrageId==0)return;
        int id=dashHeavyDarkBarrageId; dashHeavyDarkBarrageId=0;
        DarkBarrageScheduler.CompleteSlam(id);
    }
    private void EndDashHeavyPresentation()
    {
        CompleteDashHeavyDarkBarrage();
        EndGroundHeavyFocus();
        dashHeavyPresentation?.Dispose(); dashHeavyPresentation=null;
        dashHeavyTravel=null; dashHeavyMovedDistance=0f;
    }
}
