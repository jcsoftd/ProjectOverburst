using UnityEngine;

public enum EarthZoneVfxKind
{
    BasicEarthZone = 0,
    LavaEruption = 1,
    Mud = 2,
    Crystallization = 3,
    MagneticField = 4,
    CrystalPending = 5,
    CrystalExplosion = 6
}

[DisallowMultipleComponent]
public sealed class EarthZoneVfxAuthoring : MonoBehaviour
{
    [SerializeField] private EarthZoneVfxKind kind;
    [SerializeField, Min(0f)] private float authoredRadius;
    [SerializeField, Min(0.1f)] private float lifetime = 6f;
    [SerializeField, Min(1)] private int poolCapacity = 12;

    public EarthZoneVfxKind Kind => kind;
    public float AuthoredRadius => authoredRadius;
    public float Lifetime => lifetime;
    public int PoolCapacity => poolCapacity;

    public void Configure(EarthZoneVfxKind value, float radius, float safeLifetime, int capacity)
    {
        kind = value;
        authoredRadius = Mathf.Max(0f, radius);
        lifetime = Mathf.Max(0.1f, safeLifetime);
        poolCapacity = Mathf.Max(1, capacity);
    }
}
