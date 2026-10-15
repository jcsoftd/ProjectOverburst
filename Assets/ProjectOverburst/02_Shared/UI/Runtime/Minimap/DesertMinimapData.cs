using System;
using Overburst.Mojave;
using UnityEngine;

[PreferBinarySerialization]
public sealed class DesertMinimapData : ScriptableObject
{
    public const int CurrentVersion = 1;
    public const string ResourceRoot = "UI/Minimap/Desert/";
    public string candidateId, sourceScene, sourceDependencyHash, layoutSignature;
    public MojavePatch[] combatTiles;
    public int version, seed, layoutVersion, width, height;
    public float cellSize;
    public float boundaryAllowance;
    public Rect bounds;
    public Vector3 terrainPosition, terrainSize;
    public Texture2D image;
    [HideInInspector] public byte[] cells, connections;

    public static DesertMinimapData Load(MapTerrainCatalog.DesertCandidate candidate, MojaveWorld world)
    {
        var data = Resources.Load<DesertMinimapData>(ResourceRoot + candidate.id);
        if (!data) throw new InvalidOperationException("사막 탐색 지도 베이크가 없습니다: " + candidate.id);
        data.Validate(candidate, world);
        return data;
    }

    public void Validate(MapTerrainCatalog.DesertCandidate candidate, MojaveWorld world)
    {
        if (!world || !world.surface || !world.surface.terrainData || candidate == null
            || version != CurrentVersion || candidateId != candidate.id || sourceScene != candidate.scene
            || seed != candidate.seed || seed != world.bakedSeed || world.seed != seed
            || layoutVersion != MojaveLayout.Version || layoutVersion != world.bakedLayoutVersion
            || string.IsNullOrEmpty(sourceDependencyHash) || cellSize <= 0 || !float.IsFinite(cellSize)
            || boundaryAllowance < 0 || !float.IsFinite(boundaryAllowance)
            || width < 3 || height < 3 || width > 4096 || height > 4096 || (long)width * height > 8388608
            || cells == null || connections == null || cells.Length != width * height || cells.Length != connections.Length
            || !image || !image.isReadable || image.width != width || image.height != height
            || !Mathf.Approximately(bounds.width, width * cellSize) || !Mathf.Approximately(bounds.height, height * cellSize)
            || Vector3.Distance(terrainPosition, world.surface.transform.position) > .001f
            || Vector3.Distance(terrainSize, world.surface.terrainData.size) > .001f
            || combatTiles == null)
            throw new InvalidOperationException("사막 탐색 지도와 선택된 지형의 후보·버전·범위가 다릅니다: " + candidate?.id);
        world.EnsureLayout();
        if ((world.layout.bounds - new Vector2(candidate.width, candidate.depth)).sqrMagnitude > .0001f
            || world.layout.places.Count != candidate.areaCount || combatTiles.Length != world.layout.places.Count
            || layoutSignature != LayoutSignature(world) || bounds != SamplingBounds(world, cellSize, Mathf.Max(0, boundaryAllowance - .5f)))
            throw new InvalidOperationException("사막 전투 타일·연결길이 바뀌었습니다. 탐색 지도를 다시 베이크해야 합니다.");
        for (int i = 0; i < combatTiles.Length; i++)
            if (combatTiles[i] != world.catalog.patches[world.layout.places[i].patch])
                throw new InvalidOperationException("사막 전투 타일 원본이 바뀌었습니다. 탐색 지도를 다시 베이크해야 합니다.");
    }

    [Serializable] sealed class LayoutState
    {
        public MojavePlace[] places;
        public MojaveTrail[] trails;
        public float junctionRounding;
    }
    // Coordinates are generated tile poses/trail samples, never a table of scene-specific positions.
    public static string LayoutSignature(MojaveWorld world)
    {
        world.EnsureLayout();
        return Hash128.Compute(JsonUtility.ToJson(new LayoutState { places = world.layout.places.ToArray(),
            trails = world.layout.trails.ToArray(), junctionRounding = world.layout.junctionRounding })).ToString();
    }
    public static Rect SamplingBounds(MojaveWorld world, float cell, float outset = 0)
    {
        world.EnsureLayout();
        if (world.layout.places.Count == 0 || cell <= 0 || !float.IsFinite(cell)) throw new ArgumentException("Valid combat tiles and cell size required.");
        Vector2 low = new Vector2(float.PositiveInfinity, float.PositiveInfinity), high = -low;
        void Include(Vector2 p, float margin = 0)
        { low = Vector2.Min(low, p - Vector2.one * margin); high = Vector2.Max(high, p + Vector2.one * margin); }
        foreach (var place in world.layout.places)
        {
            var patch = world.catalog.patches[place.patch];
            var half = Vector2.Max(place.radius * 1.5f, Vector2.one * (patch.size * .5f));
            for (int i = 0; i < 4; i++) Include(place.World(new Vector2((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y)));
        }
        foreach (var trail in world.layout.trails)
            for (int i = 0; i < trail.points.Length; i++)
                Include(trail.points[i], trail.widths != null && trail.widths.Length == trail.points.Length ? trail.widths[i] : trail.width);
        float padding = cell + Mathf.Max(0, world.layout.junctionRounding) + Mathf.Max(0, outset);
        low = Vector2.Max(low - Vector2.one * padding, -world.layout.bounds * .5f - Vector2.one * cell);
        high = Vector2.Min(high + Vector2.one * padding, world.layout.bounds * .5f + Vector2.one * cell);
        low = new Vector2(Mathf.Floor(low.x / cell), Mathf.Floor(low.y / cell)) * cell;
        high = new Vector2(Mathf.Ceil(high.x / cell), Mathf.Ceil(high.y / cell)) * cell;
        return new Rect(low, high - low);
    }

    public MinimapExplorationMap CreateExploration() => new MinimapExplorationMap(width, height, cellSize, bounds.position,
        (byte[])cells.Clone(), (byte[])connections.Clone(), image.GetPixels32());
}
