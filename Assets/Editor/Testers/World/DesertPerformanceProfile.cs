using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

[Serializable]
public sealed class DesertPerformanceProfile
{
    public int schema = 1, level = 1, seed = 48310, repetitions = 2, pointsPerArea = 2, frameCapacity = 32768;
    public string theme = "PrimalHunt";
    public string[] candidateIds = Array.Empty<string>();
    public float warmupSeconds = 3, dwellSeconds = 5, entryTimeout = 180, travelTimeout = 120, stallSeconds = 12;
    public bool uncapped = true, screenshots;
    public int targetFps = 60;
    public int monsterMultiplier = 1;
    public int maximumAreas; // Zero visits every platform. A smoke result must retain its limited scope.

    public void Validate()
    {
        if (schema != 1 || level < 1 || level > 100 || repetitions < 1 || repetitions > 5
            || pointsPerArea < 1 || pointsPerArea > 9 || frameCapacity < 128 || frameCapacity > 65536 || maximumAreas < 0 || targetFps < 15 || targetFps > 240
            || monsterMultiplier != 1 && monsterMultiplier != 3 && monsterMultiplier != 5)
            throw new ArgumentException("사막 검사 프로필 범위를 확인하세요.");
        foreach (float value in new[] { warmupSeconds, dwellSeconds, entryTimeout, travelTimeout, stallSeconds })
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 600) throw new ArgumentException("잘못된 시간 설정");
        if (dwellSeconds < 1 || entryTimeout < 10 || travelTimeout < 10 || stallSeconds < 2 || stallSeconds > travelTimeout)
            throw new ArgumentException("측정/입장/이동/정체 시간 범위를 확인하세요.");
        if (MapThemeCatalog.Resolve(theme) == null) throw new ArgumentException("현재 테마가 없습니다: " + theme);
        if (candidateIds == null || candidateIds.Any(string.IsNullOrWhiteSpace) || candidateIds.Distinct().Count() != candidateIds.Length)
            throw new ArgumentException("맵 ID에 빈 값 또는 중복이 있습니다.");
        foreach (string id in candidateIds) MapTerrainCatalog.Resolve(id);
    }
    public static DesertPerformanceProfile Read(string path)
    {
        Overburst.DebugTools.Performance.CombatPerformancePaths.RequireOutput(path);
        var value = JsonConvert.DeserializeObject<DesertPerformanceProfile>(File.ReadAllText(path));
        if (value == null) throw new InvalidDataException("검사 프로필이 비었습니다.");
        value.Validate(); return value;
    }
}
