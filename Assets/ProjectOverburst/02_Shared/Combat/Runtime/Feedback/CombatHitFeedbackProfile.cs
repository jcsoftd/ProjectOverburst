using UnityEngine;

[CreateAssetMenu(fileName = "CombatHitFeedbackProfile", menuName = "OVERBURST/Combat/Hit Feedback Profile")]
public sealed class CombatHitFeedbackProfile : ScriptableObject
{
    [Header("Hit Stop")]
    [SerializeField, Min(0f)] private float hitStopDuration = 0.02f;
    [SerializeField, Range(0.01f, 1f)] private float hitStopTimeScale = 0.05f;

    [Header("Camera Impact")]
    [SerializeField, Min(0f)] private float cameraDuration = 0.08f;
    [SerializeField, Min(0f)] private float cameraPositionAmplitude = 0.04f;
    [SerializeField, Min(0f)] private float cameraRollAmplitude = 0.2f;
    [SerializeField, Range(0f, 1f)] private float cameraKickReturnRatio = 0.72f;
    [SerializeField, Min(0f)] private float cameraMicroShakeDuration = 0.045f;
    [SerializeField, Range(0f, 1f)] private float cameraMicroShakeAmplitude = 0.12f;
    [SerializeField, Min(0f)] private float cameraPriority = 1f;
    [SerializeField, Min(0f)] private float cameraPositionSafetyLimit = 0.16f;
    [SerializeField, Min(0f)] private float cameraRollSafetyLimit = 3f;

    [Header("Critical")]
    [SerializeField, Min(1f)] private float criticalStrengthMultiplier = 1.2f;
    [SerializeField, Min(0f)] private float maximumHitStopDuration = 0.06f;

    public float ResolveHitStopDuration(bool isCritical)
    {
        float multiplier = isCritical ? Mathf.Max(1f, criticalStrengthMultiplier) : 1f;
        return Mathf.Min(Mathf.Max(0f, maximumHitStopDuration), Mathf.Max(0f, hitStopDuration) * multiplier);
    }

    public float HitStopTimeScale => Mathf.Clamp(hitStopTimeScale, 0.01f, 1f);
    public float CameraDuration => Mathf.Max(0f, cameraDuration);
    public float CameraKickReturnRatio => Mathf.Clamp01(cameraKickReturnRatio);
    public float CameraMicroShakeDuration => Mathf.Max(0f, cameraMicroShakeDuration);
    public float CameraMicroShakeAmplitude => Mathf.Clamp01(cameraMicroShakeAmplitude);
    public float CameraPriority => Mathf.Max(0f, cameraPriority);
    public float CameraPositionSafetyLimit => Mathf.Max(0f, cameraPositionSafetyLimit);
    public float CameraRollSafetyLimit => Mathf.Max(0f, cameraRollSafetyLimit);

    public float ResolveCameraPositionAmplitude(bool isCritical)
    {
        return Mathf.Max(0f, cameraPositionAmplitude)
            * (isCritical ? Mathf.Max(1f, criticalStrengthMultiplier) : 1f);
    }

    public float ResolveCameraRollAmplitude(bool isCritical)
    {
        return Mathf.Max(0f, cameraRollAmplitude)
            * (isCritical ? Mathf.Max(1f, criticalStrengthMultiplier) : 1f);
    }
}
