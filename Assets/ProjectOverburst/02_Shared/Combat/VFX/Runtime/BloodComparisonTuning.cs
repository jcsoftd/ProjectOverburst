using System;
using UnityEngine;

// All styles use the PC settings file; the gameplay pools consume the selected values.
public static class BloodComparisonTuning
{
    public enum Control { Scale, SprayBrightness, GroundScale, GroundBrightness }
    [Serializable]
    public sealed class Values
    {
        public float scale, sprayBrightness, groundScale = 1f, groundBrightness;
        public Values() : this(false) { }
        public Values(bool pack)
        {
            scale = pack ? 1.5f : 1f;
            sprayBrightness = groundBrightness = pack ? .8f : 1f;
        }
        public void Normalize(bool pack)
        {
            var defaults = new Values(pack);
            scale = NormalizeValue(Control.Scale, scale, defaults.scale);
            sprayBrightness = NormalizeValue(Control.SprayBrightness, sprayBrightness, defaults.sprayBrightness);
            groundScale = NormalizeValue(Control.GroundScale, groundScale, 1f);
            groundBrightness = NormalizeValue(Control.GroundBrightness, groundBrightness, defaults.groundBrightness);
        }
    }
    static Values Current => OverburstGameSettings.BloodValues(BloodHitVfxService.CurrentStyle);
    public static int Revision { get; private set; }
    public static float Scale => Current.scale;
    public static float SprayBrightness => Current.sprayBrightness;
    public static float GroundScale => Current.groundScale;
    public static float GroundBrightness => Current.groundBrightness;
    // The approved floor palette is fixed; legacy saved RGB fields are ignored.
    static Vector3 GroundTint => BloodHitVfxService.CurrentStyle == BloodEffectStyle.EffectsPack ? new Vector3(1.7f, 1f, 1f) : Vector3.one;
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

            default: throw new ArgumentOutOfRangeException(nameof(control));
        }
    }
    public static float Minimum(Control control) => .1f;
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

            default: throw new ArgumentOutOfRangeException(nameof(control));
        }
        OverburstGameSettings.NotifyBloodTuning();
    }
    public static void ResetCurrent() => OverburstGameSettings.ResetBloodStyle(BloodHitVfxService.CurrentStyle);
    public static Color SprayColor(Color source) => LinearTint(source, Vector3.one, SprayBrightness);
    public static Color GroundColor(Color source) => LinearTint(source, GroundTint, GroundBrightness);
    // Same-camera calibration compensates each floor shader's baked lighting/opacity.
    // Keep the saved brightness independent of this material response.
    const float LegacyGroundResponse = .75f, ParticleGroundResponse = .13f, VolumetricGroundResponse = .15f;
    public static Color LegacyGroundColor(Color source) => LinearTint(source, GroundTint, GroundBrightness * LegacyGroundResponse);
    // Particle property blocks receive profile RGB directly; Material colors need encoding.
    public static Color ParticleGroundColor(Color source) => LinearTint(source.gamma, GroundTint, GroundBrightness * ParticleGroundResponse);
    public static Color VolumetricSprayColor(Color source) => SprayColor(source).gamma * 2f;
    public static Color VolumetricGroundColor(Color source)
    {
        Color color = GroundColor(source).gamma * 2f;
        color.r *= VolumetricGroundResponse; color.g *= VolumetricGroundResponse; color.b *= VolumetricGroundResponse;
        return color;
    }
    static Color LinearTint(Color source, Vector3 rgb, float brightness)
    {
        Color linear = source.linear;
        linear.r *= rgb.x * brightness; linear.g *= rgb.y * brightness; linear.b *= rgb.z * brightness;
        return linear;
    }
}
