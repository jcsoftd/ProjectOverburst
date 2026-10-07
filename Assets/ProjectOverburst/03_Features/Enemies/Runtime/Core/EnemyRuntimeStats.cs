using UnityEngine;

public readonly struct EnemyRuntimeStats
{
    public EnemyRuntimeStats(
        float maxHealth,
        float damageMultiplier,
        float moveSpeedMultiplier,
        float attackSpeedMultiplier,
        Vector3 visualScale,
        Vector3 collisionScale,
        Vector3 anchorScale,
        Color tint,
        float attackInterval = 0f)
    {
        MaxHealth = Mathf.Max(1f, maxHealth);
        DamageMultiplier = Mathf.Max(0f, damageMultiplier);
        MoveSpeedMultiplier = Mathf.Max(0.01f, moveSpeedMultiplier);
        AttackSpeedMultiplier = Mathf.Max(0.01f, attackSpeedMultiplier);
        AttackInterval = float.IsNaN(attackInterval) || float.IsInfinity(attackInterval) ? 0f : Mathf.Max(0f, attackInterval);
        VisualScale = SanitizeScale(visualScale);
        CollisionScale = SanitizeScale(collisionScale);
        AnchorScale = SanitizeScale(anchorScale);
        Tint = tint;
    }

    public float MaxHealth { get; }
    public float DamageMultiplier { get; }
    public float MoveSpeedMultiplier { get; }
    public float AttackSpeedMultiplier { get; }
    public float AttackInterval { get; }
    // The authored motion rate is independent of the level-growth cap.
    public static float ResolveAuthoredAttackSpeed(float baseSpeed, float growthMultiplier, float growthCap)
        => Mathf.Max(.01f, baseSpeed) * Mathf.Min(Mathf.Max(.01f, growthCap), Mathf.Max(.01f, growthMultiplier));
    public Vector3 VisualScale { get; }
    public Vector3 CollisionScale { get; }
    public Vector3 AnchorScale { get; }
    public Color Tint { get; }

    private static Vector3 SanitizeScale(Vector3 value)
    {
        return new Vector3(
            Mathf.Max(0.01f, value.x),
            Mathf.Max(0.01f, value.y),
            Mathf.Max(0.01f, value.z));
    }
}
