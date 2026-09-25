using System;
using UnityEngine;

public enum DiamondCorner { East, North, West, South }

public static class DiamondDungeonLayout
{
    public const float Radius = 88f;

    public static DiamondCorner Opposite(DiamondCorner corner)
        => (DiamondCorner)(((int)corner + 2) % 4);

    public static Vector3 Direction(DiamondCorner corner)
    {
        switch (corner)
        {
            case DiamondCorner.East: return Vector3.right;
            case DiamondCorner.North: return Vector3.forward;
            case DiamondCorner.West: return Vector3.left;
            default: return Vector3.back;
        }
    }

    public static Vector3 InsetPoint(DiamondCorner corner, float inset)
        => Direction(corner) * (Radius - Mathf.Max(0f, inset));

    public static bool Contains(Vector3 position, float margin = 0f)
        => Mathf.Abs(position.x) + Mathf.Abs(position.z) <= Radius - margin;

    public static float Progress(Vector3 position, DiamondCorner start)
        => Mathf.Clamp01((Radius - Vector3.Dot(position, Direction(start))) / (2f * Radius));

    public static Vector3 RollPoint(System.Random random, DiamondCorner start,
        float minimumProgress, float maximumProgress, Vector3[] existing, float minimumSpacing)
    {
        if (random == null) throw new ArgumentNullException(nameof(random));
        for (int attempt = 0; attempt < 500; attempt++)
        {
            float x = ((float)random.NextDouble() * 2f - 1f) * Radius;
            float z = ((float)random.NextDouble() * 2f - 1f) * Radius;
            var point = new Vector3(x, .05f, z);
            float progress = Progress(point, start);
            if (!Contains(point, 9f) || progress < minimumProgress || progress > maximumProgress)
                continue;
            bool near = false;
            if (existing != null)
                foreach (var other in existing)
                    if ((other - point).sqrMagnitude < minimumSpacing * minimumSpacing)
                    { near = true; break; }
            if (!near) return point;
        }
        throw new InvalidOperationException("몬스터 조우 위치를 찾지 못했습니다.");
    }

    public static RunWalkableArea CreateWalkableArea(float cellSize = 2f)
    {
        int side = Mathf.CeilToInt(Radius * 2f / cellSize);
        var cells = new bool[side, side];
        float origin = -Radius;
        for (int x = 0; x < side; x++)
            for (int z = 0; z < side; z++)
            {
                float px = origin + (x + .5f) * cellSize;
                float pz = origin + (z + .5f) * cellSize;
                cells[x, z] = Mathf.Abs(px) + Mathf.Abs(pz) <= Radius - 1.5f;
            }
        return new RunWalkableArea(cells, side, side, origin, origin, cellSize);
    }
}
