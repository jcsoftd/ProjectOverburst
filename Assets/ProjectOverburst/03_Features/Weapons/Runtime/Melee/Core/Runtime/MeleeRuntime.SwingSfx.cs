using UnityEngine;

public partial class MeleeRuntime
{
    private bool usePhaseSwingSfx;
    private int handledSwingSfx;
    private float swingVoiceAt = -1f;

    private bool BeginSwingSfx(float entryProgress)
    {
        ResetSwingSfx();
        var cues = activeAttackStep.swingSfxCues;
        if (!activeAttackUsesCombo || activeAttackIsHeavy || activeDodgeFollowUp != PlayerDodgeFollowUpKind.None
            || cues == null || cues.Length == 0) return false;
        usePhaseSwingSfx = true;
        swingVoiceAt = OverburstGameClock.UnscaledTime + .03f;
        if (!activeAttackStep.TryValidateSwingSfx(out string error))
            Debug.LogError("[MeleeRuntime] " + error, this);
        if (activeAttackPhases == null) return true;
        for (int i = 0; i < Mathf.Min(32, cues.Length); i++)
        {
            if (i >= activeAttackPhases.Length || !cues[i].IsValid)
            { handledSwingSfx |= 1 << i; continue; }
            var phase = activeAttackPhases[i];
            float target = Mathf.Lerp(phase.SafeStart, phase.SafeEnd, cues[i].alignmentProgress);
            if (entryProgress > target || entryProgress >= phase.SafeEnd) handledSwingSfx |= 1 << i;
        }
        return true;
    }

    private void TickSwingSfx(float progress)
    {
        if (!usePhaseSwingSfx || !isAttacking || Time.timeScale <= 0f
            || OverburstTimeEffectArbiter.IsPaused || attackDuration <= 0f || activeAttackPhases == null) return;
        if (swingVoiceAt >= 0f && OverburstGameClock.UnscaledTime >= swingVoiceAt)
        { swingVoiceAt = -1f; PlayerVoiceService.Attack(); }
        var cues = activeAttackStep.swingSfxCues;
        if (cues == null) return;
        float elapsed = activeAttackStep.playbackAcceleration.ToElapsed(progress);
        for (int i = 0; i < Mathf.Min(32, Mathf.Min(cues.Length, activeAttackPhases.Length)); i++)
        {
            int bit = 1 << i;
            if ((handledSwingSfx & bit) != 0) continue;
            var phase = activeAttackPhases[i];
            var cue = cues[i];
            if (progress >= phase.SafeEnd || !cue.IsValid)
            { handledSwingSfx |= bit; continue; }
            float target = Mathf.Lerp(phase.SafeStart, phase.SafeEnd, cue.alignmentProgress);
            float startElapsed = activeAttackStep.playbackAcceleration.ToElapsed(target)
                - cue.peakSeconds * Time.timeScale / attackDuration;
            if (elapsed < startElapsed) continue;
            handledSwingSfx |= bit;
            CombatActionSfxService.PlayGreatswordLightSwing(cue.clip, transform.position);
        }
    }

    private void ResetSwingSfx()
    {
        usePhaseSwingSfx = false;
        handledSwingSfx = 0;
        swingVoiceAt = -1f;
    }
}
