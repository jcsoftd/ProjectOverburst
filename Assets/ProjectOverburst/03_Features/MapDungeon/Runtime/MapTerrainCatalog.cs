using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Overburst.Persistence;

// Selected, baked content. Level and grade deliberately do not participate in selection.
public static class MapTerrainCatalog
{
    public const string CaveScene = "Assets/ProjectOverburst/00_Scenes/World/Caves/Caves_Run_12.unity";
    [Serializable] public sealed class DesertCandidate
    {
        public string id, scene;
        public int seed, areaCount, width, depth;
    }
    [Serializable] private sealed class Catalog
    {
        public int version;
        public DesertCandidate[] candidates;
    }
    private static Catalog catalog;
    public static IReadOnlyList<DesertCandidate> DesertCandidates
    {
        get
        {
            if (catalog == null)
            {
                var asset = Resources.Load<TextAsset>("World/MapRuns/DesertCandidates");
                if (!asset) throw new InvalidDataException("사막 지도 후보 목록이 없습니다.");
                var loaded = JsonUtility.FromJson<Catalog>(asset.text);
                if (loaded == null || loaded.version != 1 || loaded.candidates == null || loaded.candidates.Length == 0)
                    throw new InvalidDataException("사막 지도 후보 목록을 읽지 못했습니다.");
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var row in loaded.candidates)
                    if (row == null || string.IsNullOrWhiteSpace(row.id) || !ids.Add(row.id)
                        || string.IsNullOrWhiteSpace(row.scene) || row.areaCount < 2 || row.width < 126 || row.depth < 126)
                        throw new InvalidDataException("사막 지도 후보 정보가 올바르지 않습니다.");
                catalog = loaded;
            }
            return catalog.candidates;
        }
    }
    public static DesertCandidate Resolve(string id)
    {
        foreach (var row in DesertCandidates) if (row.id == id) return row;
        throw new InvalidDataException("저장된 사막 지도 후보를 찾지 못했습니다: " + id);
    }
    public static string SceneFor(MapInstanceState map)
    {
        MapGenerationPolicy.Validate(map, false);
        if (map.terrain == MapTerrainKind.Cave) return CaveScene;
        var row = Resolve(map.desertCandidateId);
        if (row.areaCount != map.areaCount || row.width != map.width || row.depth != map.depth)
            throw new InvalidDataException("저장된 지도와 사막 후보의 규모가 다릅니다.");
        return row.scene;
    }
    public static string SceneNameFor(MapInstanceState map) => Path.GetFileNameWithoutExtension(SceneFor(map));
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => catalog = null;
}
