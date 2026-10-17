using System;
using System.Collections;
using UnityEngine;

// One run owns the topology, visit bits and two views of those same visit bits.
public sealed class MinimapExplorationMap : IDisposable
{
    public const byte East = 1, West = 2, North = 4, South = 8;
    readonly byte[] cells, connections;
    readonly bool[] visited, queued;
    readonly Color32[] terrainPixels, outlinePixels;
    readonly Color32[] basePixels;
    int[] queue = Array.Empty<int>();
    readonly BitArray completedOrigins;
    int completedSteps = -1;
    public int Width { get; }
    public int Height { get; }
    public float CellSize { get; }
    public Rect Bounds { get; }
    public int RevealedCount { get; private set; }
    public int Revision { get; private set; }
    public int UploadCount { get; private set; }
    public Texture2D TerrainTexture { get; private set; }
    public Texture2D OutlineTexture { get; private set; }

    // Arrays are transferred to this owner; callers must not mutate them afterwards.
    public MinimapExplorationMap(int width, int height, float cellSize, Vector2 origin, byte[] kinds, byte[] links, Color32[] sourcePixels = null)
    {
        if (width < 1 || height < 1 || width > 4096 || height > 4096 || (long)width * height > 8388608
            || cellSize <= 0 || float.IsNaN(cellSize) || float.IsInfinity(cellSize)
            || kinds == null || links == null || kinds.Length != width * height || links.Length != kinds.Length
            || sourcePixels != null && sourcePixels.Length != kinds.Length)
            throw new ArgumentException("Invalid exploration map dimensions.");
        Width = width; Height = height; CellSize = cellSize;
        Bounds = new Rect(origin.x, origin.y, width * cellSize, height * cellSize);
        cells = kinds; connections = links;
        basePixels = sourcePixels;
        visited = new bool[cells.Length]; queued = new bool[cells.Length];
        completedOrigins = new BitArray(cells.Length);
        terrainPixels = new Color32[cells.Length]; outlinePixels = new Color32[cells.Length];
    }

    public int CellAt(Vector3 position)
    {
        int x = Mathf.FloorToInt((position.x - Bounds.xMin) / CellSize);
        int z = Mathf.FloorToInt((position.z - Bounds.yMin) / CellSize);
        return x < 0 || z < 0 || x >= Width || z >= Height ? -1 : z * Width + x;
    }
    public Vector3 Center(int index) => new Vector3(Bounds.xMin + (index % Width + .5f) * CellSize, 0,
        Bounds.yMin + (index / Width + .5f) * CellSize);
    public bool IsGround(int index) => index >= 0 && index < cells.Length && cells[index] == MinimapTerrainPixels.Ground;
    public bool IsRevealed(Vector3 position) { int i = CellAt(position); return i >= 0 && visited[i]; }
    public bool IsRevealedCell(int index) => index >= 0 && index < visited.Length && visited[index];
    public byte Connections(int index) => connections[index];

    // Four-neighbour floor distance, independent of zoom and the previous world position.
    public bool Reveal(Vector3 position, float radius)
    {
        int start = CellAt(position);
        if (!IsGround(start)) return false;
        int steps = Mathf.Clamp(Mathf.FloorToInt(radius / CellSize), 0, 256);
        if (completedSteps != steps)
        {
            completedOrigins.SetAll(false);
            completedSteps = steps;
        }
        if (completedOrigins[start]) return false;
        int capacity = Mathf.Min(cells.Length, 1 + 2 * steps * (steps + 1));
        if (queue.Length < capacity) queue = new int[capacity];
        int head = 0, tail = 1, layerEnd = 1, depth = 0, before = RevealedCount;
        queue[0] = start; queued[start] = true;
        while (head < tail)
        {
            int index = queue[head++];
            if (!visited[index])
            {
                visited[index] = true; RevealedCount++;
                bool edge = connections[index] != (East | West | North | South);
                terrainPixels[index] = basePixels != null ? basePixels[index] : edge ? MinimapTerrainPixels.EdgeColor : MinimapTerrainPixels.GroundColor;
                if (edge) outlinePixels[index] = MinimapTerrainPixels.EdgeColor;
            }
            if (depth < steps)
            {
                byte links = connections[index];
                Visit(index + 1, index % Width < Width - 1 && (links & East) != 0);
                Visit(index - 1, index % Width > 0 && (links & West) != 0);
                Visit(index + Width, (links & North) != 0); Visit(index - Width, (links & South) != 0);
            }
            if (head == layerEnd) { layerEnd = tail; depth++; }
        }
        for (int i = 0; i < tail; i++) queued[queue[i]] = false;
        // Topology is immutable; only fully searched origins may skip a revisit.
        completedOrigins[start] = true;
        if (before == RevealedCount) return false;
        Revision++;
        if (TerrainTexture) Upload();
        return true;

        void Visit(int index, bool linked)
        {
            if (!linked || !IsGround(index) || queued[index]) return;
            queued[index] = true; queue[tail++] = index;
        }
    }

    public void EnsureTextures()
    {
        if (TerrainTexture) return;
        TerrainTexture = Create("Explored terrain (run)");
        OutlineTexture = Create("Explored outline (run)");
        Upload();
    }
    Texture2D Create(string name) => new Texture2D(Width, Height, TextureFormat.RGBA32, false)
        { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
    void Upload()
    {
        TerrainTexture.SetPixels32(terrainPixels); TerrainTexture.Apply(false, false);
        OutlineTexture.SetPixels32(outlinePixels); OutlineTexture.Apply(false, false);
        UploadCount++;
    }
    public void Dispose()
    {
        Release(TerrainTexture); Release(OutlineTexture); TerrainTexture = null; OutlineTexture = null;
    }
    static void Release(Texture2D texture)
    {
        if (!texture) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(texture); else UnityEngine.Object.DestroyImmediate(texture);
    }
}
