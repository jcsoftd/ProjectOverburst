using UnityEngine;

// The Animator is the only source of attack progress. The entry timer can fail a
// missing motion; it never advances a strike or substitutes wall-clock damage.
public sealed class EnemyAttackClock
{
    public enum Phase { Idle, WaitingForMotion, Running, Completed, Failed, Cancelled }
    private const float EntryTimeoutSeconds = .75f;
    private int requestFrame;
    private int lastSampleFrame;
    private bool hadPreviousMotion;
    private float previousMotionTime;
    private float entryWait;
    public Phase State { get; private set; }
    public float NormalizedTime { get; private set; }
    public bool HasEntered => State == Phase.Running || State == Phase.Completed;
    public bool IsTerminal => State == Phase.Completed || State == Phase.Failed || State == Phase.Cancelled;

    public void Begin(int frame, bool previousMotionPresent, float previousNormalizedTime)
    {
        requestFrame = frame; lastSampleFrame = frame; entryWait = 0f;
        hadPreviousMotion = previousMotionPresent && Finite(previousNormalizedTime) && previousNormalizedTime >= 0f;
        previousMotionTime = previousNormalizedTime;
        NormalizedTime = 0f; State = Phase.WaitingForMotion;
    }

    public void Observe(int frame, float scaledDeltaTime, bool motionPresent, float normalizedTime)
    {
        if (State == Phase.Idle || IsTerminal || frame <= lastSampleFrame) return;
        lastSampleFrame = frame;
        if (!Finite(scaledDeltaTime) || scaledDeltaTime < 0f
            || motionPresent && (!Finite(normalizedTime) || normalizedTime < 0f))
        { State = Phase.Failed; return; }
        if (State == Phase.WaitingForMotion)
        {
            entryWait += scaledDeltaTime;
            // A trigger may be requested while the previous instance of the same
            // state is still visible. Require a restart, not its stale progress.
            bool newMotion = motionPresent && frame > requestFrame
                && (!hadPreviousMotion || normalizedTime < previousMotionTime - .0001f);
            if (!newMotion)
            {
                if (entryWait >= EntryTimeoutSeconds) State = Phase.Failed;
                return;
            }
            State = Phase.Running;
        }
        else if (!motionPresent || normalizedTime < NormalizedTime - .1f)
        { State = Phase.Failed; return; }
        NormalizedTime = Mathf.Max(NormalizedTime, Mathf.Clamp01(normalizedTime));
        if (NormalizedTime >= 1f) State = Phase.Completed;
    }

    public void Cancel() { State = Phase.Cancelled; NormalizedTime = 0f; }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
