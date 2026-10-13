using System;
using System.Collections.Generic;
using Overburst.Persistence;
using UnityEngine;

// The finite natural population belongs to this run, never to pooled Actor instance IDs.
public sealed class MapRunRoster
{
    public enum Life { Pending, Active, Dead }
    public sealed class Member
    {
        public string Id { get; internal set; }
        public int Index { get; internal set; }
        public int AreaIndex { get; internal set; }
        public EnemyDefinition Definition { get; internal set; }
        public EnemyThemeTier Tier { get; internal set; }
        public bool Initial { get; internal set; }
        public Vector3 PlannedPosition { get; internal set; }
        public bool PlannedOnRoad { get; internal set; }
        public Life State { get; internal set; }
        public int Lease { get; internal set; }
    }
    public sealed class Area
    {
        public MapCombatGeometry.Area Ground { get; internal set; }
        public int BasePopulation { get; internal set; }
        public int FirstMember { get; internal set; }
        public int Count { get; internal set; }
        public int InitialCount { get; internal set; }
        public int Small { get; internal set; }
        public int Medium { get; internal set; }
        public int Elite { get; internal set; }
    }
    public string RunId { get; }
    public string ThemeId { get; }
    public int Seed { get; }
    public MapTerrainKind Terrain { get; }
    public IReadOnlyList<Member> Members { get; }
    public IReadOnlyList<Area> Areas { get; }
    public int Total => Members.Count;
    public int Target { get; }
    public int Kills { get; private set; }
    public int Active { get; private set; }
    public int Pending => Total - Kills - Active;
    public bool PlacementReady { get; internal set; }
    public bool CaveTargetReached => Terrain == MapTerrainKind.Cave && Kills >= Target;
    public float NaturalProgressMultiplier => Terrain == MapTerrainKind.Cave ? 1f + .15f * Mathf.Clamp01(Kills / (float)Target) : 1f;
    public event Action DeathRecorded;

    MapRunRoster(string runId, MapInstanceState map, List<Member> members, List<Area> areas)
    {
        RunId = runId; Seed = map.seed; ThemeId = map.monsterThemeId; Terrain = map.terrain;
        Members = members.AsReadOnly(); Areas = areas.AsReadOnly();
        Target = Terrain == MapTerrainKind.Cave ? CaveTarget(members.Count) : 0;
    }
    public static int CaveTarget(int count)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        return (int)((95L * count + 99) / 100);
    }
    // Continuous initial tuning; anchors are measured connected square metres, not tile width.
    public static int BasePopulation(MapTerrainKind terrain, float squareMetres, float density)
    {
        if (squareMetres <= 0 || float.IsNaN(squareMetres) || float.IsInfinity(squareMetres)
            || float.IsNaN(density) || density < .9f || density > 1.1f) throw new ArgumentOutOfRangeException(nameof(squareMetres));
        float anchor = terrain == MapTerrainKind.Cave ? 85f : terrain == MapTerrainKind.Desert ? 130f
            : throw new ArgumentException("Product terrain required.");
        int minimum = terrain == MapTerrainKind.Cave ? 50 : 60;
        float count = minimum * Mathf.Pow(squareMetres / anchor, .75f) * density;
        return Mathf.Max(minimum, 5 * Mathf.RoundToInt(count / 5f));
    }
    public static MapRunRoster Build(string runId, MapInstanceState map, IReadOnlyList<MapCombatGeometry.Area> ground, int bossArea = -1)
    {
        MapGenerationPolicy.Validate(map, false);
        if (string.IsNullOrWhiteSpace(runId) || ground == null || ground.Count != map.areaCount)
            throw new ArgumentException("Map areas and run identity are required.");
        var theme = MapThemeCatalog.Resolve(map.monsterThemeId);
        if (bossArea < -1 || bossArea >= ground.Count || bossArea >= 0 && map.terrain != MapTerrainKind.Desert)
            throw new ArgumentException("Only a desert may reserve a boss area.");
        if (theme == null) throw new InvalidOperationException("저장된 지도 테마를 찾지 못했습니다.");
        var random = new System.Random(unchecked(map.seed ^ 0x45AD01));
        var areas = new List<Area>(); var counts = new int[ground.Count, 3];
        for (int i = 0; i < ground.Count; i++)
        {
            if (ground[i] == null || ground[i].Index != i || ground[i].Points.Count < 6)
                throw new InvalidOperationException("전투 구역의 안전한 배치 공간이 부족합니다.");
            float density = .9f + (float)random.NextDouble() * .2f;
            int count = i == bossArea ? 0 : BasePopulation(map.terrain, ground[i].SquareMetres, density);
            int band = ground[i].Band;
            int[] weights = band == 0 ? new[] { 85, 13, 2 } : band == 1 ? new[] { 80, 16, 4 } : new[] { 75, 19, 6 };
            var portions = Allocate(count, weights);
            for (int tier = 0; tier < 3; tier++) counts[i, tier] = portions[tier];
            areas.Add(new Area { Ground = ground[i], BasePopulation = count });
        }
        string[] optionIds = { MapOptionPolicy.MoreSmall, MapOptionPolicy.MoreMedium, MapOptionPolicy.MoreElite };
        for (int tier = 0; tier < 3; tier++)
        {
            var weights = new int[areas.Count]; int total = 0;
            for (int a = 0; a < areas.Count; a++) { weights[a] = counts[a, tier]; total += weights[a]; }
            double extra = total * (double)MapOptionPolicy.Value(map, optionIds[tier]);
            if (extra > int.MaxValue - total) throw new InvalidOperationException("지도 스폰 수량이 유효 범위를 초과했습니다.");
            int additions = (int)Math.Round(extra, MidpointRounding.AwayFromZero);
            var allocated = Allocate(additions, weights);
            for (int a = 0; a < areas.Count; a++) counts[a, tier] += allocated[a];
        }
        var members = new List<Member>();
        for (int a = 0; a < areas.Count; a++)
        {
            var area = areas[a];
            if (a == bossArea) { area.FirstMember = members.Count; continue; }
            // Retain the existing fixed-count affix until its G05 migration; it does not replace small enemies.
            if (MapOptionPolicy.Value(map, MapOptionPolicy.MoreMediumElite) > 0)
            { counts[a, 1]++; if (area.Ground.Band > 0) counts[a, 2]++; }
            var roster = new List<EnemyDefinition>();
            for (int tier = 0; tier < 3; tier++)
                for (int left = counts[a, tier]; left > 0;)
                {
                    int batch = Math.Min(500, left); left -= batch;
                    roster.AddRange(theme.BuildRoster(tier == 0 ? batch : 0, tier == 1 ? batch : 0, tier == 2 ? batch : 0, random.Next()));
                }
            for (int i = roster.Count - 1; i > 0; i--) { int j = random.Next(i + 1); (roster[i], roster[j]) = (roster[j], roster[i]); }
            area.Small = counts[a, 0]; area.Medium = counts[a, 1]; area.Elite = counts[a, 2];
            area.Count = roster.Count; area.FirstMember = members.Count;
            area.InitialCount = Mathf.Clamp(Mathf.RoundToInt(area.Count * (.3f + (float)random.NextDouble() * .2f)),
                Mathf.CeilToInt(area.Count * .3f), Mathf.FloorToInt(area.Count * .5f));
            for (int i = 0; i < roster.Count; i++)
                members.Add(new Member { Id = runId + "/" + a + "/" + i, Index = members.Count, AreaIndex = a,
                    Definition = roster[i], Tier = MapSpawnPolicy.Tier(theme, roster[i]), Initial = i < area.InitialCount });
        }
        return new MapRunRoster(runId, map, members, areas);
    }
    static int[] Allocate(int count, int[] weights)
    {
        long total = 0; foreach (int w in weights) total += w;
        var result = new int[weights.Length]; if (count == 0) return result;
        if (total <= 0) throw new InvalidOperationException("Empty spawn allocation.");
        var remainder = new double[weights.Length]; int assigned = 0;
        for (int i = 0; i < weights.Length; i++)
        { double exact = count * (double)weights[i] / total; result[i] = (int)Math.Floor(exact); remainder[i] = exact - result[i]; assigned += result[i]; }
        while (assigned < count)
        { int best = 0; for (int i = 1; i < weights.Length; i++) if (remainder[i] > remainder[best]) best = i; result[best]++; remainder[best] = -1; assigned++; }
        return result;
    }
    public bool TryActivate(int index, out int lease)
    {
        lease = 0;
        if (!PlacementReady || index < 0 || index >= Total || Members[index].State != Life.Pending) return false;
        var member = Members[index]; member.State = Life.Active; lease = ++member.Lease; Active++; return true;
    }
    public bool TryReturn(int index, int lease)
    {
        if (!Current(index, lease)) return false;
        Members[index].State = Life.Pending; Active--; return true;
    }
    public bool TryRecordDeath(int index, int lease)
    {
        if (!Current(index, lease)) return false;
        Members[index].State = Life.Dead; Active--; Kills++;
        DeathRecorded?.Invoke(); return true;
    }
    bool Current(int index, int lease) => index >= 0 && index < Total && lease > 0
        && Members[index].State == Life.Active && Members[index].Lease == lease;
}
