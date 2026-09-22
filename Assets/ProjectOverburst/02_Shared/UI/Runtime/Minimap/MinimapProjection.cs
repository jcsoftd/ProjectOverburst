using UnityEngine;

public static class MinimapProjection
{
    public static Vector2 Project(Vector3 world, Vector3 origin, float yaw, float worldRadius, float uiRadius)
    {
        float angle = yaw * Mathf.Deg2Rad;
        return ProjectBasis(world, origin, Mathf.Cos(angle), Mathf.Sin(angle), uiRadius / Mathf.Max(1f, worldRadius));
    }

    public static Vector2 ProjectBasis(Vector3 world, Vector3 origin, float cos, float sin, float scale)
    {
        float x = world.x - origin.x, z = world.z - origin.z;
        return new Vector2(cos * x - sin * z, sin * x + cos * z) * scale;
    }

    public static float DistanceSquared(Vector3 a, Vector3 b)
    {
        float x = a.x - b.x, z = a.z - b.z;
        return x * x + z * z;
    }
}
