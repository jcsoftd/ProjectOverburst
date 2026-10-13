using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.DebugTools.Performance;

public static class DesertPerformanceVerifier
{
    public static object VerifyNative(string output)
    {
        CombatPerformancePaths.RequireOutput(output); Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks.Add(label); }
        void Reject(DesertPerformanceProfile profile, string label)
        { bool rejected = false; try { profile.Validate(); } catch (ArgumentException) { rejected = true; } Check(rejected, label); }
        var ids = DesertPerformanceSuite.Representatives();
        Check(ids.Length == 5, "Exactly five representative maps, not all thirty");
        Check(ids.Select(id => MapTerrainCatalog.Resolve(id).areaCount).SequenceEqual(new[] { 4, 6, 9, 12, 15 }), "Every requested size covered once");
        Check(ids.All(id => id.StartsWith("SQ", StringComparison.Ordinal)), "Current square representatives selected deterministically");
        Check(DesertPerformanceSuite.FullRepresentativeCoverage(ids.Reverse()), "Full coverage is independent of execution order, including largest map first");
        Check(!DesertPerformanceSuite.FullRepresentativeCoverage(ids.Take(4)), "Omitted representative remains limited coverage");
        Check(!DesertPerformanceSuite.FullRepresentativeCoverage(ids.Take(4).Concat(new[] { ids[0] })), "Duplicate representative cannot substitute for a missing size");
        new DesertPerformanceProfile().Validate(); Check(true, "Default traversal profile is valid");
        Reject(new DesertPerformanceProfile { candidateIds = new[] { ids[0], ids[0] } }, "Duplicate map rejected");
        Reject(new DesertPerformanceProfile { dwellSeconds = float.NaN }, "Non-finite duration rejected");
        Reject(new DesertPerformanceProfile { frameCapacity = 1 }, "Undersized recorder buffer rejected");
        Reject(new DesertPerformanceProfile { repetitions = 0 }, "Missing traversal rejected");
        Reject(new DesertPerformanceProfile { stallSeconds = 121, travelTimeout = 120 }, "Inconsistent stall timeout rejected");
        Reject(new DesertPerformanceProfile { targetFps = 0 }, "Invalid analysis frame budget rejected");
        Reject(new DesertPerformanceProfile { monsterMultiplier = 2 }, "Unsupported monster multiplier is rejected");
        new DesertPerformanceProfile { monsterMultiplier = 5 }.Validate(); Check(true, "Requested 5x stress profile is accepted");
        var shortTravel = new CombatPerformanceSegment { frames = 1, validity = "INCOMPLETE" };
        Check(DesertPerformanceProbe.IsShortTravel("travel", shortTravel), "One-frame real travel is labelled separately rather than failing completed combat-area coverage");
        shortTravel.frames = 0;
        Check(DesertPerformanceProbe.IsShortTravel("travel", shortTravel), "Zero-sample travel retains raw evidence without inventing a frame distribution");
        Check(!DesertPerformanceProbe.IsShortTravel("dwell", shortTravel), "Insufficient dwell is still rejected");
        shortTravel.capacityReached = true;
        Check(!DesertPerformanceProbe.IsShortTravel("travel", shortTravel), "Buffer saturation is not excused as short travel");
        shortTravel.capacityReached = false; shortTravel.droppedEvents = 1;
        Check(!DesertPerformanceProbe.IsShortTravel("travel", shortTravel), "Dropped events remain incomplete");
        shortTravel.droppedEvents = 0; shortTravel.validity = "ENVIRONMENT_CHANGED";
        Check(!DesertPerformanceProbe.IsShortTravel("travel", shortTravel), "Changed resolution or time scale is not excused as short travel");
        shortTravel.validity = "INCOMPLETE"; shortTravel.frames = 2;
        Check(!DesertPerformanceProbe.IsShortTravel("travel", shortTravel), "Other incomplete segments remain rejected");
        shortTravel.frames = -1;
        Check(!DesertPerformanceProbe.IsShortTravel("travel", shortTravel), "Invalid negative sample counts remain rejected");
        Check(DesertPerformanceDetails.ParseCsv("a,\"b,c\",\"d\"\"e\",").SequenceEqual(new[] { "a", "b,c", "d\"e", "" }), "CSV quoted comma, escaped quote and empty cell preserved");
        string fixture = Path.Combine(output, "DetailedFixture"); Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "frames.csv"), "frame,wall_ms,observer_ms,\"Internal/Main Thread [ms]\",\"Internal/Main Thread samples [count]\"\n1,10,0.1,2,1\n2,20,0.2,5,0\n3,100,0.3,8,2\n4,,0.1,,\n");
        File.WriteAllText(Path.Combine(fixture, "positions.csv"), "frame,x,y,z,input_x,input_y,active,pending,time_scale\n1,1,2,3,0,0,2,10,1\n2,4,5,6,0,1,3,9,1\n");
        var analysis = DesertPerformanceDetails.Analyze(fixture, 60, 4096);
        Check(analysis.frames == 3 && analysis.overBudget == 2 && analysis.over50 == 1 && analysis.over100 == 0, "Frame budget excludes unavailable samples and preserves threshold boundaries");
        Check(analysis.onePercentLowFps == 10 && analysis.longestOverBudgetStreak == 2, "Slowest one percent average FPS and contiguous budget streak are correct");
        Check(analysis.matchedPositions == 2 && analysis.missingPositions == 1 && analysis.worstFrames[0].frame == 3 && analysis.worstFrames[0].workloadRow == null,
            "Worst frames join by exact frame ID and never fabricate missing coordinates");
        Check(analysis.missingMetrics.Contains("OVERBURST.MapSpawnFinite.Total") && !analysis.missingMetrics.Contains("Main Thread") && analysis.memoryDeltaBytes == 4096,
            "Missing markers remain N/A and memory units retained");
        Check(analysis.markerSampleTotals["Internal/Main Thread samples [count]"] == 3, "Accumulated profiler sample totals preserve valid zero and exclude unavailable rows");
        DesertPerformanceDetails.ArchiveReference(output);
        var reference = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(output, "Reference/reference.json")));
        Check(reference["candidates"]?.Count() == 33, "All 33 static audit candidate IDs are archived independently of runtime confirmation");
        var stats = CombatPerformanceStats.Calculate(new[] { 10d, 20d, 30d, 100d, double.NaN, double.PositiveInfinity, -1d }, 7);
        Check(stats.samples == 4 && stats.mean == 40 && stats.p95 == 100 && stats.p99 == 100 && stats.median == 25, "Percentiles exclude unavailable/invalid samples and retain real hitches");
        var unavailable = CombatPerformanceStats.Calculate(new[] { double.NaN }, 1);
        Check(unavailable.samples == 0, "Unavailable timing remains no samples, not valid zero milliseconds");
        var coordinates = new DesertPerformanceProbe.Result { visits = new List<DesertPerformanceProbe.Visit> {
            new DesertPerformanceProbe.Visit { target = new UnityEngine.Vector3(12.5f, -2, 37) } } };
        string encoded = DesertPerformanceProbe.Serialize(coordinates);
        var decoded = JsonConvert.DeserializeObject<DesertPerformanceProbe.Result>(encoded);
        Check(!encoded.Contains("normalized") && decoded.visits[0].target == coordinates.visits[0].target,
            "Runtime Vector3 coordinate round trip without derived-property recursion");
        var particleFixture = new UnityEngine.GameObject("Owned desert census particle regression") { hideFlags = UnityEngine.HideFlags.HideAndDontSave };
        try
        {
            particleFixture.AddComponent<UnityEngine.ParticleSystem>();
            Check(DesertPerformanceProbe.CountTriangles(particleFixture.GetComponent<UnityEngine.ParticleSystemRenderer>()) == 0,
                "Particle renderer without MeshFilter safely excluded from mesh triangle estimate");
        }
        finally { UnityEngine.Object.DestroyImmediate(particleFixture); }
        var limited = new DesertPerformanceProbe.Result { candidate = ids[0], status = "INTERRUPTED", expectedAreas = 4, visitedAreas = 1 };
        limited.segments.Add(new DesertPerformanceProbe.Segment { label = "fixture", analysis = analysis,
            performance = new CombatPerformanceSegment { frames = 3, wall = stats, metrics = Array.Empty<CombatPerformanceMetricResult>() } });
        DesertPerformanceReport.WriteMap(output, limited);
        string report = File.ReadAllText(Path.Combine(output, "summary.md"));
        Check(report.Contains("INTERRUPTED") && report.Contains("1/4") && report.Contains("NOT_RUN"), "Partial coverage and Player scope preserved in report");
        Check(report.Contains("N/A") && report.Contains("S04") && report.Contains("A03") && report.Contains("CONTROLLED_COMBAT_NOT_RUN")
            && report.Contains("1% low"), "Planning report preserves missing metrics, audit IDs, controlled combat scope and tail FPS definition");
        var result = new { status = "PASS_NATIVE", checks, representatives = ids, scope = "Selection, input validation, statistics and incomplete report. Real traversal tested separately." };
        File.WriteAllText(Path.Combine(output, "native-result.json"), JsonConvert.SerializeObject(result, Formatting.Indented)); return result;
    }
}
