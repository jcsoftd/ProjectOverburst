using System;
using UnityEngine;

// Both styles use the PC settings file; the gameplay pools consume the selected values.
public static class BloodComparisonTuning
{
    public enum Control { Scale, SprayBrightness, GroundScale, GroundBrightness, GroundRed, GroundGreen, GroundBlue }
    [Serializable]
    public sealed class Values
    {
        public float scale, sprayBrightness, groundScale = 1f, groundBrightness;
        public Vector3 groundRgb = Vector3.one;
        public Values() : this(false) { }
        public Values(bool pack)
        {
            scale = pack ? 1.5f : 1f;
            sprayBrightness = groundBrightness = pack ? .8f : 1f;
            groundRgb = new Vector3(pack ? 1.7f : 1f, 1f, 1f);
        }
        public void Normalize(bool pack)
        {
            var defaults = new Values(pack);
            scale = NormalizeValue(Control.Scale, scale, defaults.scale);
            sprayBrightness = NormalizeValue(Control.SprayBrightness, sprayBrightness, defaults.sprayBrightness);
            groundScale = NormalizeValue(Control.GroundScale, groundScale, 1f);
            groundBrightness = NormalizeValue(Control.GroundBrightness, groundBrightness, defaults.groundBrightness);
            groundRgb = new Vector3(NormalizeValue(Control.GroundRed, groundRgb.x, defaults.groundRgb.x),
                NormalizeValue(Control.GroundGreen, groundRgb.y, 1f), NormalizeValue(Control.GroundBlue, groundRgb.z, 1f));
        }
    }
    static Values Current => OverburstGameSettings.BloodValues(BloodHitVfxService.PackEnabled);
    public static int Revision { get; private set; }
    public static float Scale => Current.scale;
    public static float SprayBrightness => Current.sprayBrightness;
    public static float GroundScale => Current.groundScale;
    public static float GroundBrightness => Current.groundBrightness;
    public static Vector3 GroundRgb => Current.groundRgb;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() => Revision = 0;
    internal static void Invalidate() => Revision++;
    public static float Value(Control control)
    {
        switch (control)
        {
            case Control.Scale: return Current.scale;
            case Control.SprayBrightness: return Current.sprayBrightness;
            case Control.GroundScale: return Current.groundScale;
            case Control.GroundBrightness: return Current.groundBrightness;
            case Control.GroundRed: return Current.groundRgb.x;
            case Control.GroundGreen: return Current.groundRgb.y;
            default: return Current.groundRgb.z;
        }
    }
    public static float Minimum(Control control) => control >= Control.GroundRed ? 0f : .1f;
    public static float Maximum(Control control) => control == Control.Scale ? 4f : control == Control.GroundScale ? 3f : 2f;
    static float NormalizeValue(Control control, float value, float fallback) =>
        Mathf.Clamp(Mathf.Round((float.IsNaN(value) || float.IsInfinity(value) ? fallback : value) * 10f) / 10f, Minimum(control), Maximum(control));
    public static void Adjust(Control control, int steps) => Set(control, Value(control) + steps * .1f);
    public static void Set(Control control, float amount)
    {
        amount = NormalizeValue(control, amount, Value(control));
        if (Mathf.Approximately(Value(control), amount)) return;
        var value = Current;
        switch (control)
        {
            case Control.Scale: value.scale = amount; break;
            case Control.SprayBrightness: value.sprayBrightness = amount; break;
            case Control.GroundScale: value.groundScale = amount; break;
            case Control.GroundBrightness: value.groundBrightness = amount; break;
            case Control.GroundRed: value.groundRgb.x = amount; break;
            case Control.GroundGreen: value.groundRgb.y = amount; break;
            default: value.groundRgb.z = amount; break;
        }
        OverburstGameSettings.NotifyBloodTuning();
    }
    public static void ResetCurrent() => OverburstGameSettings.ResetBloodStyle(BloodHitVfxService.PackEnabled);
    public static Color SprayColor(Color source) => LinearTint(source, Vector3.one, SprayBrightness);
    public static Color GroundColor(Color source) => LinearTint(source, GroundRgb, GroundBrightness);
    static Color LinearTint(Color source, Vector3 rgb, float brightness)
    {
        Color linear = source.linear;
        linear.r *= rgb.x * brightness; linear.g *= rgb.y * brightness; linear.b *= rgb.z * brightness;
        return linear;
    }
}
