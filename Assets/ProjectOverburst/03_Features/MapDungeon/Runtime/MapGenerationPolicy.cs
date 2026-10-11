using System;
using System.Collections.Generic;
using System.IO;
using Overburst.Persistence;

public static class MapGenerationPolicy
{
    public const int Version = 1;
    // Initial tuning from 80A. All sizes remain available at every level/grade.
    private static readonly int[] CaveWeights = { 4, 5, 6, 7, 8, 9, 10, 9, 8, 7, 6, 5, 4, 4, 3, 3, 2 };
    private static readonly int[] DesertWeights = { 20, 25, 25, 20, 10 };

    public static ItemData CreateItem(MapItemData definition, AccountContentRegistry registry,
        int level, ItemGrade grade, string runId = null, IReadOnlyList<MapOptionRoll> options = null)
    {
        if (definition == null || registry == null) throw new ArgumentException("Registered map content is required.");
        var item = new ItemData(definition, level, grade) { originRunId = runId };
        item.mapState = new MapInstanceState { mapContentId = registry.IdFor(definition),
            level = level, grade = grade };
        foreach (var option in options ?? MapOptionPolicy.Roll(grade))
            item.mapState.options.Add(ItemSnapshotCodec.CopyValues(option));
        Initialize(item.mapState, item.runtimeInstanceId);
        return item;
    }

    // Identity-derived draws allow a failed migration save to retry without rerolling.
    public static bool Initialize(MapInstanceState map, string identity)
    {
        if (map == null) throw new ArgumentNullException(nameof(map));
        if (map.generationVersion != 0) { Validate(map, false); return false; }
        Validate(map, true);
        if (string.IsNullOrWhiteSpace(identity)) throw new ArgumentException("Stable map identity is required.");
        var rng = new Random(Seed(identity));
        var terrain = rng.Next(2) == 0 ? MapTerrainKind.Desert : MapTerrainKind.Cave;
        int seed = rng.Next(1, int.MaxValue);
        int count, width = 0, depth = 0;
        string candidate = null;
        if (terrain == MapTerrainKind.Cave) count = 4 + Pick(rng, CaveWeights);
        else
        {
            int group = Pick(rng, DesertWeights);
            var choices = new List<MapTerrainCatalog.DesertCandidate>();
            foreach (var row in MapTerrainCatalog.DesertCandidates)
                if (DesertGroup(row.areaCount) == group) choices.Add(row);
            if (choices.Count == 0) throw new InvalidDataException("선택한 규모의 사막 후보가 없습니다.");
            var chosen = choices[rng.Next(choices.Count)];
            candidate = chosen.id; count = chosen.areaCount; width = chosen.width; depth = chosen.depth;
        }
        string theme = map.monsterThemeId;
        if (string.IsNullOrWhiteSpace(theme))
            theme = MapThemeCatalog.RollThemeId(terrain, rng);
        // An existing map keeps its saved theme, including old regional assignments.
        map.terrain = terrain; map.seed = seed; map.areaCount = count;
        map.desertCandidateId = candidate; map.width = width; map.depth = depth;
        map.monsterThemeId = theme;
        map.monsterThemes = new List<MapMonsterTheme> { new MapMonsterTheme { themeId = theme, weight = 100 } };
        map.generationVersion = Version;
        Validate(map, false);
        return true;
    }
    public static void Validate(MapInstanceState map, bool allowLegacy = true)
    {
        if (map == null || map.level < 1 || map.level > 100 || !Enum.IsDefined(typeof(ItemGrade), map.grade) || map.options == null)
            throw new InvalidDataException("Invalid map level, grade or options.");
        foreach (var option in map.options)
            if (option == null || string.IsNullOrWhiteSpace(option.optionId) || float.IsNaN(option.value) || float.IsInfinity(option.value))
                throw new InvalidDataException("Invalid saved map option.");
        if (allowLegacy && map.generationVersion == 0) return;
        if (map.generationVersion != Version || map.seed <= 0 || !Enum.IsDefined(typeof(MapTerrainKind), map.terrain)
            || map.terrain == MapTerrainKind.Legacy || map.monsterThemes == null || map.monsterThemes.Count != 1)
            throw new InvalidDataException("Unsupported map generation data.");
        if (map.terrain == MapTerrainKind.Cave
            ? map.areaCount < 4 || map.areaCount > 20 || !string.IsNullOrEmpty(map.desertCandidateId) || map.width != 0 || map.depth != 0
            : string.IsNullOrWhiteSpace(map.desertCandidateId) || DesertGroup(map.areaCount) < 0 || map.width < 126 || map.depth < 126)
            throw new InvalidDataException("Invalid saved map terrain/size.");
        // Retain the G01A storage field for save compatibility; maps have one theme.
        var savedTheme = map.monsterThemes[0];
        if (savedTheme == null || string.IsNullOrWhiteSpace(savedTheme.themeId)
            || savedTheme.weight != 100 || savedTheme.themeId != map.monsterThemeId)
            throw new InvalidDataException("Saved map must contain its single monster theme at weight 100.");
    }
    public static string Describe(MapInstanceState map)
        => map == null || map.generationVersion == 0 ? "기존 지도"
            : map.terrain == MapTerrainKind.Cave ? "동굴 · " + map.areaCount + "개 플랫폼"
            : "사막 · " + map.areaCount + "개 전장";
    private static int DesertGroup(int count) => count >= 2 && count <= 4 ? 0 : count == 6 ? 1 : count == 9 ? 2 : count == 12 ? 3 : count == 15 ? 4 : -1;
    private static int Pick(Random rng, int[] weights)
    {
        int total = 0; foreach (int weight in weights) total += weight;
        int pick = rng.Next(total);
        for (int i = 0; i < weights.Length; i++) { pick -= weights[i]; if (pick < 0) return i; }
        throw new InvalidOperationException("Empty map size distribution.");
    }
    private static int Seed(string identity)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char value in "map-generation-v1/" + identity) { hash ^= value; hash *= 16777619; }
            return (int)(hash & 0x7fffffff);
        }
    }
}
