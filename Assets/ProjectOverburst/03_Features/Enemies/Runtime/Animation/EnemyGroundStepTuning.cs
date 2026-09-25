using UnityEngine;

public enum EnemyGroundStepTier { None, Medium, Elite }

// Player-relative distance keeps the perceived step strength stable while zooming.
public static class EnemyGroundStepTuning
{
    public static float AudibleDistance(EnemyGroundStepTier tier)
        => tier == EnemyGroundStepTier.Elite ? 12f
            : tier == EnemyGroundStepTier.Medium ? 8f : 0f;

    public static float CameraAmplitude(EnemyGroundStepTier tier, float distance)
    {
        distance = Mathf.Max(0f, distance);
        if (tier == EnemyGroundStepTier.Elite)
        {
            if (distance >= 8f) return 0f;
            return distance <= 4f
                ? Mathf.Lerp(.035f, .05f, Mathf.SmoothStep(0f, 1f, (4f - distance) / 4f))
                : .035f * Mathf.SmoothStep(0f, 1f, (8f - distance) / 4f);
        }
        if (tier != EnemyGroundStepTier.Medium || distance >= 6f) return 0f;
        return distance <= 4f
            ? Mathf.Lerp(.01f, .015f, Mathf.SmoothStep(0f, 1f, (4f - distance) / 4f))
            : .01f * Mathf.SmoothStep(0f, 1f, (6f - distance) / 2f);
    }

    public static float AudioVolume(EnemyGroundStepTier tier, float distance)
    {
        float range = AudibleDistance(tier);
        if (range <= 0f || distance >= range) return 0f;
        float closeness = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Clamp01(distance / range));
        return tier == EnemyGroundStepTier.Elite
            ? Mathf.Lerp(.035f, .48f, closeness)
            : Mathf.Lerp(.02f, .22f, closeness);
    }
}
