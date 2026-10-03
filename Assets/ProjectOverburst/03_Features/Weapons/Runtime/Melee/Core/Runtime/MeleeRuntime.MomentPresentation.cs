using UnityEngine;

public partial class MeleeRuntime
{
    private void TickHeavyMomentPreparation()
    {
        if(!CombatMomentPresentation.HeavyEnabled || !activeAttackIsHeavy || heavyDischargeCommitted || activeHeavyEnergy==null
            || activeHeavyEnergy.Normalized<1f-.0001f || !activeGemAttack.IsCurrent || activeAttackPhases==null || activeAttackPhases.Length==0)
        {CombatMomentPresentation.CancelPreparation(playerEquipment,activeActionId);return;}
        int phase=activeHeavyDefinition!=null?activeHeavyDefinition.SafeDischargePhaseIndex:0;
        float phaseElapsed=activeAttackStep.playbackAcceleration.ToElapsed(activeAttackPhases[phase].SafeStart)*attackDuration;
        float remaining=(phaseElapsed-(Time.time-attackStartTime))/Mathf.Max(.001f,Time.timeScale);
        CombatMomentPresentation.PrepareHeavy(playerEquipment,activeActionId,phase,remaining,activeDodgeFollowUp==PlayerDodgeFollowUpKind.Heavy);
    }
}
