using UnityEngine;

public sealed class RunWalkableArea // 런 보행 영역
{
    private readonly bool[,] cells;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public float OriginX { get; private set; }
    public float OriginZ { get; private set; }
    public float CellSize { get; private set; }

    public RunWalkableArea(
        bool[,] cells,
        int width,
        int height,
        float originX,
        float originZ,
        float cellSize)
    {
        this.cells = cells;
        Width = width;
        Height = height;
        OriginX = originX;
        OriginZ = originZ;
        CellSize = Mathf.Max(0.01f, cellSize);
    }

    public bool IsWalkable(Vector3 worldPosition)
    {
        return IsWalkable(new Vector2(worldPosition.x, worldPosition.z));
    }

    public bool IsWalkable(Vector2 position)
    {
        return TryGetCell(position, out int x, out int z) && IsWalkableCell(x, z);
    }

    public bool TryGetCell(Vector3 worldPosition, out int x, out int z)
    {
        return TryGetCell(new Vector2(worldPosition.x, worldPosition.z), out x, out z);
    }

    public bool TryGetCell(Vector2 position, out int x, out int z)
    {
        x = Mathf.FloorToInt((position.x - OriginX) / CellSize);
        z = Mathf.FloorToInt((position.y - OriginZ) / CellSize);
        return x >= 0 && x < Width && z >= 0 && z < Height;
    }

    public bool IsWalkableCell(int x, int z)
    {
        if (x < 0 || x >= Width || z < 0 || z >= Height)
            return false; // grid 밖

        return cells[x, z];
    }

    public bool TryFindNearestWalkable(Vector2 position, out Vector2 nearest)
    {
        nearest = position;
        if (!TryFindNearestWalkableCell(position, out int nearestX, out int nearestZ))
            return false;

        nearest = GetCellCenter(nearestX, nearestZ);
        return true;
    }

    public bool TryFindNearestWalkableCell(Vector3 worldPosition, out int nearestX, out int nearestZ)
    {
        return TryFindNearestWalkableCell(
            new Vector2(worldPosition.x, worldPosition.z),
            out nearestX,
            out nearestZ);
    }

    public bool TryFindNearestWalkableCell(Vector2 position, out int nearestX, out int nearestZ)
    {
        nearestX = -1;
        nearestZ = -1;
        bool found = false;
        float bestSqrDistance = float.PositiveInfinity;

        for (int x = 0; x < Width; x++)
        {
            for (int z = 0; z < Height; z++)
            {
                if (!cells[x, z])
                    continue; // 보행 불가

                Vector2 center = GetCellCenter(x, z);
                float sqrDistance = (center - position).sqrMagnitude;
                if (sqrDistance >= bestSqrDistance)
                    continue; // 더 먼 후보

                bestSqrDistance = sqrDistance;
                nearestX = x;
                nearestZ = z;
                found = true;
            }
        }

        return found;
    }

    public Vector2 GetCellCenter(int x, int z)
    {
        return new Vector2(
            OriginX + (x + 0.5f) * CellSize,
            OriginZ + (z + 0.5f) * CellSize);
    }
}
