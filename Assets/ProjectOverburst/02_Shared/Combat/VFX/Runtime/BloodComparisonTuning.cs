using UnityEngine;

// Session-only comparison values. A and B retain their own values until the next Play.
public static class BloodComparisonTuning
{
    public enum Control { Scale, SprayBrightness, GroundScale, GroundBrightness, GroundRed, GroundGreen, GroundBlue }
    sealed class Values
    {
        public float scale, sprayBrightness, groundScale = 1f, groundBrightness = 1f;
        public Vector3 groundRgb = Vector3.one;
        public Values(bool pack) { scale = pack ? 2f : 1f; sprayBrightness = groundBrightness = pack ? .8f : 1f; }
    }
    static Values legacy = new Values(false), packValues = new Values(true);
    static Values Current => BloodHitVfxService.PackEnabled ? packValues : legacy;
    public static int Revision { get; private set; }
    public static float Scale => Current.scale;
    public static float SprayBrightness => Current.sprayBrightness;
    public static float GroundScale => Current.groundScale;
    public static float GroundBrightness => Current.groundBrightness;
    public static Vector3 GroundRgb => Current.groundRgb;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { legacy = new Values(false); packValues = new Values(true); Revision = 0; }
    public static float Value(Control control)
    {
        var value = Current;
        switch (control)
        {
            case Control.Scale: return value.scale;
            case Control.SprayBrightness: return value.sprayBrightness;
            case Control.GroundScale: return value.groundScale;
            case Control.GroundBrightness: return value.groundBrightness;
            case Control.GroundRed: return value.groundRgb.x;
            case Control.GroundGreen: return value.groundRgb.y;
            default: return value.groundRgb.z;
        }
    }
    public static void Adjust(Control control, int steps) => Set(control, Value(control) + steps * .1f);
    public static void Set(Control control, float amount)
    {
        float minimum = control >= Control.GroundRed ? 0f : .1f;
        float maximum = control == Control.Scale ? 4f : control == Control.GroundScale ? 3f : 2f;
        amount = Mathf.Clamp(Mathf.Round(amount * 10f) / 10f, minimum, maximum);
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
        Revision++;
    }
    public static void ResetCurrent()
    {
        if (BloodHitVfxService.PackEnabled) packValues = new Values(true);
        else legacy = new Values(false);
        Revision++;
    }
    public static Color SprayColor(Color source) => LinearTint(source, Vector3.one, SprayBrightness);
    public static Color GroundColor(Color source) => LinearTint(source, GroundRgb, GroundBrightness);
    static Color LinearTint(Color source, Vector3 rgb, float brightness)
    {
        Color linear = source.linear;
        linear.r *= rgb.x * brightness; linear.g *= rgb.y * brightness; linear.b *= rgb.z * brightness;
        return linear;
    }
}
