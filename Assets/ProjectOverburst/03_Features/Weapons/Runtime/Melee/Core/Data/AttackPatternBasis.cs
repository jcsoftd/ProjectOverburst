using UnityEngine;

public readonly struct AttackPatternBasis
{
    public readonly Vector3 Origin;
    public readonly Vector3 Forward;
    public readonly Vector3 Right;

    public AttackPatternBasis(Vector3 origin, Vector3 forward)
    {
        Forward = FlattenDirection(forward);
        Right = Vector3.Cross(Vector3.up, Forward).normalized;
        Origin = origin;
    }

    public AttackPatternBasis WithOrigin(Vector3 origin)
    {
        return new AttackPatternBasis(origin, Forward);
    }

    public Vector3 GetPatternOrigin(AttackPatternRuntimeData pattern)
    {
        return Origin + Forward * pattern.ForwardOffset;
    }

    private static Vector3 FlattenDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            return Vector3.forward;

        return direction.normalized;
    }
}
