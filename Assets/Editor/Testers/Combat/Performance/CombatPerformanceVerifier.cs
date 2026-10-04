using System;
using System.Collections.Generic;
using System.IO;
using Overburst.DebugTools.Performance;
using UnityEditor;
using UnityEngine;

public static class CombatPerformanceVerifier
{
    public static object VerifyCore(string output)
    {
        output = CombatPerformancePaths.RequireOutput(output); Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool valid, string name) { if (!valid) throw new InvalidOperationException(name); checks.Add(name); }
        void Reject(Action action, string name) { bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } Check(rejected, name); }
        new CombatPerformanceProfile().Validate(); CombatPerformanceProfile.Standard().Validate(); CombatPerformanceProfile.Soak().Validate(); CombatPerformanceProfile.Smoke().Validate();
        Check(true, "4 preset profiles valid");
        Reject(() => new CombatPerformanceProfile { elements = new[] { WeaponElement.Water } }.Validate(), "retired element rejected");
        Reject(() => new CombatPerformanceProfile { measureSeconds = float.NaN }.Validate(), "NaN rejected");
        Reject(() => new CombatPerformanceProfile { frameCapacity = int.MaxValue }.Validate(), "unbounded buffer rejected");
        Reject(() => CombatPerformancePaths.RequireOutput(Path.GetFullPath(Path.Combine(Application.dataPath, "../Accounts"))), "protected output rejected");
        Reject(() => CombatPerformancePaths.RequireOutput(Path.Combine(CombatPerformancePaths.OutputRoot, "../../../../escape")), "path traversal rejected");
        Reject(() => new CombatPerformanceProfile { segmentLimit = int.MaxValue }.Validate(), "unbounded summary rejected");
        var stats = CombatPerformanceStats.Calculate(new double[] { 4, double.NaN, -1, 1, 3, 2, double.PositiveInfinity }, 7);
        Check(stats.samples == 4 && stats.mean == 2.5 && stats.median == 2.5 && stats.p95 == 4 && stats.p99 == 4, "finite samples and nearest rank quantiles");
        Check(CombatPerformanceStats.Calculate(Array.Empty<double>(), 0).samples == 0, "unavailable has zero samples");
        var a = new CombatPerformanceProfile(); var b = new CombatPerformanceProfile { label = "Renamed", regressionPercent = 20 };
        Check(a.WorkloadJson() == b.WorkloadJson(), "label and decision threshold excluded from workload identity");
        b.seed++; Check(a.WorkloadJson() != b.WorkloadJson(), "seed affects workload identity");
        CombatPerformanceRun Fixture(double time)
        {
            var run = new CombatPerformanceRun { id = "fixture", status = "COMPLETE", profile = new CombatPerformanceProfile(), workloadSignature = "same" };
            for (int i = 0; i < 3; i++) run.segments.Add(new CombatPerformanceSegment { key = "sustained", rosterSignature = "same",
                requests = 10, accepted = 8, enemyHits = 40, bloodPlayed = 20, aliveAtEnd = 50,
                wall = CombatPerformanceStats.Calculate(new[] { time, time, time }, 3) });
            return run;
        }
        Check(CombatPerformanceReport.Compare(Fixture(10), Fixture(13)).rows[0].status == "REGRESSION", "P95 regression identified");
        Check(CombatPerformanceReport.Compare(Fixture(13), Fixture(10)).rows[0].status == "IMPROVED", "P95 improvement identified");
        Check(CombatPerformanceReport.Compare(Fixture(10), Fixture(10.5)).rows[0].status == "WITHIN_THRESHOLD", "measurement threshold applied");
        var changed = Fixture(10); changed.workloadSignature = "different";
        Check(CombatPerformanceReport.Compare(Fixture(10), changed).status == "INCOMPARABLE", "different environment or recipe rejected");
        changed = Fixture(10); changed.logErrors = 1; Check(CombatPerformanceReport.Compare(Fixture(10), changed).status == "INCOMPARABLE", "error run rejected");
        changed = Fixture(10); changed.status = "CANCELLED"; Check(CombatPerformanceReport.Compare(Fixture(10), changed).status == "INCOMPARABLE", "cancelled run rejected");
        changed = Fixture(10); foreach (var s in changed.segments) s.bloodPlayed = 2;
        Check(CombatPerformanceReport.Compare(Fixture(10), changed).rows[0].status == "INCOMPARABLE", "reduced actual effect workload rejected");
        changed = Fixture(10); changed.segments[0].validity = "INCOMPLETE";
        Check(CombatPerformanceReport.Compare(Fixture(10), changed).rows[0].status == "INCOMPARABLE", "truncated sample rejected");
        changed = Fixture(10); changed.segments[0].rosterSignature = "different";
        Check(CombatPerformanceReport.Compare(Fixture(10), changed).rows[0].status == "INCOMPARABLE", "actual roster change rejected");
        changed = Fixture(10); changed.segments.RemoveRange(1, 2);
        Check(CombatPerformanceReport.Compare(Fixture(10), changed).rows[0].status.StartsWith("CANDIDATE_"), "single repetition stays candidate");
        changed = Fixture(10); changed.profile.mode = CombatPerformanceMode.Observation;
        Check(CombatPerformanceReport.Compare(Fixture(10), changed).status == "INCOMPARABLE", "manual observation cannot establish same workload");
        changed = Fixture(10); foreach (var s in changed.segments) s.inFlightAtEnd = 1;
        Check(CombatPerformanceReport.Compare(Fixture(10), changed).rows[0].status == "INCOMPARABLE", "unfinished action workload changed");
        string path = Path.Combine(output, "CoreValidation.json");
        CombatPerformancePaths.SaveJson(path, new Validation { status = "PASS", checks = checks.ToArray(), unityVersion = Application.unityVersion });
        return new { status = "PASS", checks = checks.Count, output = path };
    }
    [Serializable] sealed class Validation { public string status, unityVersion; public string[] checks; }
    public static string StartSmoke() => CombatPerformanceSession.Start(CombatPerformanceProfile.Smoke());
    public static string StartFiveElementSmoke()
    {
        var profile = CombatPerformanceProfile.Smoke(); profile.label = "FiveElementValidation";
        profile.elements = new[] { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light };
        profile.measureSeconds = 5; return CombatPerformanceSession.Start(profile);
    }
    public static string StartBoundedSoak()
    {
        var profile = CombatPerformanceProfile.Smoke(); profile.label = "LifecycleValidation"; profile.soakSeconds = 120;
        profile.elements = new[] { WeaponElement.Fire, WeaponElement.Electric }; return CombatPerformanceSession.Start(profile);
    }
}
