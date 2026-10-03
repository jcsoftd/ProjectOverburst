using UnityEngine;

// Source-clip seconds keep the gathering pose aligned at every attack speed.
public readonly struct HeavyFocusWindow
{
    public readonly float Start, End;
    public HeavyFocusWindow(float start, float end) { Start = start; End = Mathf.Max(start + .01f, end); }
    public static HeavyFocusWindow Dash => new HeavyFocusWindow(.28f, .45f);
    public static HeavyFocusWindow Ground(float clipLength) => new HeavyFocusWindow(clipLength * .16f, clipLength * .28f);
    public static HeavyFocusWindow Parried(float clipLength) => new HeavyFocusWindow(clipLength * .30f, clipLength * .38f);
    private static float Smooth(float value) { value = Mathf.Clamp01(value); return value * value * (3f - 2f * value); }
    public float Gather(float source) => Mathf.InverseLerp(Start, End, source);
    public float TimeScale(float source) => 1f - .25f * Smooth((source - (Start - .04f)) / .08f) * (1f - Smooth((source - End) / .08f));
    public float Zoom(float source) => .045f * Smooth((source - Start) / .12f) * (1f - Smooth((source - End) / .20f));
    public float UnscaledDuration(float from, float to, bool dash)
    {
        const int samples = 48;
        float step = Mathf.Max(0f, to - from) / samples, duration = 0f;
        for (int i = 0; i < samples; i++)
        {
            float a = from + step * i, b = a + step;
            float scaled = dash ? DashHeavyFocusClock.RealAt(b) - DashHeavyFocusClock.RealAt(a) : step;
            duration += scaled / TimeScale((a + b) * .5f);
        }
        return duration;
    }
    public float SourceAtAudioLead(float contact, float lead, bool dash)
    {
        float low = 0f, high = contact;
        for (int i = 0; i < 20; i++)
        {
            float middle = (low + high) * .5f;
            if (UnscaledDuration(middle, contact, dash) > lead) low = middle; else high = middle;
        }
        return (low + high) * .5f;
    }
}
