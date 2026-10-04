using UnityEngine;

// Clip seconds choose the gathering pose; the slow pulse lasts .2 pause-aware real seconds.
public readonly struct HeavyFocusWindow
{
    public const float PulseDuration = .2f, PulseRamp = .08f;
    public readonly float Start, End, MinimumScale;
    public HeavyFocusWindow(float start, float end)
    { Start = start; End = Mathf.Max(start + .01f, end); MinimumScale = .3f; }
    public static HeavyFocusWindow Dash => new HeavyFocusWindow(.28f, .45f);
    public static HeavyFocusWindow Ground(float clipLength) => new HeavyFocusWindow(clipLength * .16f, clipLength * .28f);
    public static HeavyFocusWindow Parried(float clipLength) => new HeavyFocusWindow(clipLength * .30f, clipLength * .38f);
    private static float Smooth(float value) { value = Mathf.Clamp01(value); return value * value * (3f - 2f * value); }
    public float Gather(float source) => Mathf.InverseLerp(Start, End, source);
    public float PulseScale(float elapsed)
    {
        if (elapsed < 0f || elapsed >= PulseDuration) return 1f;
        return 1f - (1f - MinimumScale) * Smooth(elapsed / PulseRamp)
            * (1f - Smooth((elapsed - (PulseDuration - PulseRamp)) / PulseRamp));
    }
    public float ScaledPulseDuration => PulseDuration * MinimumScale + PulseRamp * (1f - MinimumScale);
    public float PulseScaledAt(float elapsed)
    {
        elapsed = Mathf.Max(0f, elapsed);
        if (elapsed >= PulseDuration) return ScaledPulseDuration + elapsed - PulseDuration;
        if (elapsed < PulseRamp)
        {
            float q = elapsed / PulseRamp;
            return elapsed - (1f - MinimumScale) * PulseRamp * (q*q*q - .5f*q*q*q*q);
        }
        float first = PulseRamp * (1f + MinimumScale) * .5f;
        if (elapsed <= PulseDuration - PulseRamp) return first + (elapsed - PulseRamp) * MinimumScale;
        float tail = elapsed - (PulseDuration - PulseRamp), t = tail / PulseRamp;
        return first + (PulseDuration - 2f * PulseRamp) * MinimumScale + tail * MinimumScale
            + (1f - MinimumScale) * PulseRamp * (t*t*t - .5f*t*t*t*t);
    }
    private float PulseRealAt(float scaled)
    {
        if (scaled <= 0f) return 0f;
        if (scaled >= ScaledPulseDuration) return PulseDuration + scaled - ScaledPulseDuration;
        float low = 0f, high = PulseDuration;
        for (int i = 0; i < 24; i++)
        { float middle = (low + high) * .5f; if (PulseScaledAt(middle) < scaled) low = middle; else high = middle; }
        return (low + high) * .5f;
    }
    private float BaseAt(float source, bool dash, float speed) => (dash ? DashHeavyFocusClock.RealAt(source) : Mathf.Max(0f, source)) / Mathf.Max(.01f, speed);
    private float UnscaledAt(float source, bool dash, float speed)
    {
        float start = BaseAt(Start, dash, speed), current = BaseAt(source, dash, speed);
        return current <= start ? current : start + PulseRealAt(current - start);
    }
    public float TimeScale(float source) => source < Start ? 1f : PulseScale(PulseRealAt(source - Start));
    public float Zoom(float source) => .045f * Smooth((source - Start) / .12f) * (1f - Smooth((source - End) / .20f));
    public float UnscaledDuration(float from, float to, bool dash, float speed = 1f)
        => Mathf.Max(0f, UnscaledAt(to, dash, speed) - UnscaledAt(from, dash, speed));
    public float SourceAtAudioLead(float contact, float lead, bool dash, float speed = 1f)
    {
        float low = 0f, high = contact;
        for (int i = 0; i < 20; i++)
        {
            float middle = (low + high) * .5f;
            if (UnscaledDuration(middle, contact, dash, speed) > lead) low = middle; else high = middle;
        }
        return (low + high) * .5f;
    }
}
