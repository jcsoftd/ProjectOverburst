using UnityEngine;

// Supplier landing and reveal curves; the common ground pool owns playback and materials.
public sealed class VolumetricBloodGroundData : MonoBehaviour
{
    public AnimationCurve cutout = AnimationCurve.EaseInOut(0, 1, .3f, 0);
    public AnimationCurve landing = AnimationCurve.Linear(0, 0, 1, 1);
    public float curveSeconds = 15f, intensity = 1f, heightMax = 5.1f;
    public Vector3 size = Vector3.one, offset, scaleMin = Vector3.one, scaleMax = Vector3.one;
    public Vector3 offsetMin, offsetMax;
    public float RevealSeconds => Mathf.Max(.01f, curveSeconds * .3f / VolumetricBloodAnimationData.PlaybackSpeed);
    public float CutoutAt(float age) => Mathf.Clamp01(cutout.Evaluate(Mathf.Min(.3f, Mathf.Max(0f, age) * VolumetricBloodAnimationData.PlaybackSpeed / Mathf.Max(.01f, curveSeconds))) * intensity);
    public float HeightFraction(float height, float worldScale) => Mathf.Clamp01(Mathf.Abs(height) / Mathf.Max(.01f, heightMax * worldScale));
    public Vector3 LandingOffset(float fraction) => offset + Vector3.Lerp(offsetMin, offsetMax, fraction);
    public Vector3 LandingSize(float fraction) { var scale = Vector3.Lerp(scaleMin, scaleMax, fraction); return new Vector3(Mathf.Abs(size.x * scale.x), Mathf.Abs(size.y * scale.z), size.z); }
}
