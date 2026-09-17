using UnityEngine;

public readonly struct WeaponAttackWindow
{
    public WeaponAttackWindow(
        float minStartDistance,
        float preferredDistance,
        float maxStartDistance,
        float verticalTolerance)
    {
        MinStartDistance = Mathf.Max(0f, minStartDistance);
        MaxStartDistance = Mathf.Max(MinStartDistance, maxStartDistance);
        PreferredDistance = Mathf.Clamp(
            preferredDistance,
            MinStartDistance,
            MaxStartDistance);
        VerticalTolerance = Mathf.Max(0.1f, verticalTolerance);
    }

    public float MinStartDistance { get; }
    public float PreferredDistance { get; }
    public float MaxStartDistance { get; }
    public float VerticalTolerance { get; }
}
