#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Overburst.DebugTools.Performance
{
    public enum CombatPerformanceMode { Automated, Observation }
    public enum CombatPerformanceLayout { Dense, Spread }

    [Serializable]
    public sealed class CombatPerformanceProfile
    {
        public int schema = 1;
        public string label = "Quick";
        public CombatPerformanceMode mode;
        public string[] themes = { "SpiderBrood" };
        public int[] enemyCounts = { 50 };
        public CombatPerformanceLayout[] layouts = { CombatPerformanceLayout.Dense };
        public WeaponElement[] elements = { WeaponElement.Fire };
        public int repetitions = 1, seed = 27100, spawnPerFrame = 10;
        public float baselineSeconds = 3, approachSeconds = 4, warmupSeconds = 3;
        public float measureSeconds = 10, tailSeconds = 5, attackInterval = .55f;
        public float observationSeconds = 120, soakSeconds;
        public bool preparedElementStress = true, deathBurst = true, uncapped;
        public int frameCapacity = 32768, eventCapacity = 32768, segmentLimit = 1024;
        public float enemyHp = 100000, regressionPercent = 10, regressionAbsoluteMs = 1;
        public string weaponAssetPath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset";
        public string weaponResourcePath = "";

        public static CombatPerformanceProfile Standard() => new CombatPerformanceProfile {
            label = "Standard", enemyCounts = new[] { 50, 100, 200 },
            layouts = new[] { CombatPerformanceLayout.Dense, CombatPerformanceLayout.Spread },
            elements = new[] { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light },
            repetitions = 3
        };
        public static CombatPerformanceProfile Soak() => new CombatPerformanceProfile {
            label = "Soak", enemyCounts = new[] { 100 },
            elements = new[] { WeaponElement.Fire, WeaponElement.Electric, WeaponElement.Dark }, soakSeconds = 1800
        };
        public static CombatPerformanceProfile Smoke() => new CombatPerformanceProfile {
            label = "Validation", enemyCounts = new[] { 8 }, baselineSeconds = 1, approachSeconds = 1,
            warmupSeconds = 1, measureSeconds = 3, tailSeconds = 1, frameCapacity = 4096, eventCapacity = 4096
        };
        public void Validate()
        {
            if (schema != 1) throw new ArgumentException("지원하지 않는 프로필 버전입니다.");
            if (!Enum.IsDefined(typeof(CombatPerformanceMode), mode)) throw new ArgumentException("실행 모드가 잘못됐습니다.");
            if (themes == null || themes.Length == 0 || themes.Length > 16) throw new ArgumentException("테마는 1~16개입니다.");
            foreach (string theme in themes) if (string.IsNullOrWhiteSpace(theme)) throw new ArgumentException("빈 테마 ID입니다.");
            if (enemyCounts == null || enemyCounts.Length == 0 || enemyCounts.Length > 8) throw new ArgumentException("수량 설정이 없습니다.");
            foreach (int count in enemyCounts) if (count < 1 || count > 500) throw new ArgumentException("적 수는 1~500입니다.");
            if (layouts == null || layouts.Length == 0 || layouts.Length > 2) throw new ArgumentException("배치 설정이 없습니다.");
            foreach (var layout in layouts) if (!Enum.IsDefined(typeof(CombatPerformanceLayout), layout)) throw new ArgumentException("잘못된 배치입니다.");
            if (elements == null || elements.Length == 0 || elements.Length > 5) throw new ArgumentException("원소는 1~5개입니다.");
            foreach (var element in elements) if (element != WeaponElement.Fire && element != WeaponElement.Ice && element != WeaponElement.Electric
                && element != WeaponElement.Dark && element != WeaponElement.Light) throw new ArgumentException("현행 5원소만 사용하세요.");
            if (repetitions < 1 || repetitions > 10 || spawnPerFrame < 1 || spawnPerFrame > 100) throw new ArgumentException("반복/프레임당 스폰 범위를 확인하세요.");
            if (frameCapacity < 128 || frameCapacity > 65536 || eventCapacity < 128 || eventCapacity > 65536) throw new ArgumentException("기록 버퍼 범위를 확인하세요.");
            if (segmentLimit < 16 || segmentLimit > 2048) throw new ArgumentException("최대 구간 수는 16~2048입니다.");
            Range(baselineSeconds, 1, 60); Range(approachSeconds, 1, 60); Range(warmupSeconds, 0, 30);
            Range(measureSeconds, 1, 120); Range(tailSeconds, 1, 60); Range(attackInterval, .1f, 5);
            Range(observationSeconds, 1, 86400); Range(soakSeconds, 0, 86400); Range(enemyHp, 100, 10000000);
            Range(regressionPercent, 0, 100); Range(regressionAbsoluteMs, 0, 100);
        }
        static void Range(float value, float minimum, float maximum)
        { if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum) throw new ArgumentException("시간/수치 설정 범위를 확인하세요: " + value); }
        public string WorkloadJson()
        {
            var copy = JsonUtility.FromJson<CombatPerformanceProfile>(JsonUtility.ToJson(this));
            copy.label = ""; copy.regressionPercent = 0; copy.regressionAbsoluteMs = 0;
            return JsonUtility.ToJson(copy);
        }
    }

    [Serializable] public sealed class CombatPerformanceMetric
    { public string name, category, unit; public bool nanoseconds; }
    [Serializable] public sealed class CombatPerformanceStats
    {
        public int samples; public double mean, median, p95, p99, maximum, minimum;
        public static CombatPerformanceStats Calculate(double[] source, int count)
        {
            var values = new List<double>(count);
            for (int i = 0; i < count; i++) if (!double.IsNaN(source[i]) && !double.IsInfinity(source[i]) && source[i] >= 0) values.Add(source[i]);
            if (values.Count == 0) return new CombatPerformanceStats();
            values.Sort(); double sum = 0; foreach (double v in values) sum += v;
            int n = values.Count;
            return new CombatPerformanceStats { samples = n, mean = sum / n,
                median = n % 2 == 0 ? (values[n / 2 - 1] + values[n / 2]) / 2 : values[n / 2],
                p95 = values[Math.Max(0, (int)Math.Ceiling(n * .95) - 1)], p99 = values[Math.Max(0, (int)Math.Ceiling(n * .99) - 1)],
                maximum = values[n - 1], minimum = values[0] };
        }
    }
    [Serializable] public sealed class CombatPerformanceMetricResult
    { public CombatPerformanceMetric metric; public CombatPerformanceStats stats; }
    [Serializable] public sealed class CombatPerformanceSegment
    {
        public string key, folder, scenario, theme, layout, element, inputMode, rosterSignature, validity = "VALID", reason = "";
        public int repetition, cycle, expectedEnemies, aliveAtStart, aliveAtEnd, frames, droppedEvents, inFlightAtEnd;
        public int requests, accepted, rejected, completed, cancelled, enemyHits, playerHits, dotHits, derivedHits, criticalHits, kills, parries;
        public int bloodRequests, bloodPlayed, bloodUnavailable, poolCreated, poolPendingEnd, evades, evadeCompleted, pickupsRequested, pickupsCollected;
        public int hitches33, hitches50, hitches100, longestHitchFrames, gc0, gc1, gc2;
        public double seconds, hitch50PerMinute, memoryStartBytes, memoryEndBytes;
        public bool capacityReached;
        public CombatPerformanceStats wall, observer, delayedCpu, delayedGpu;
        public CombatPerformanceMetricResult[] metrics;
    }
    [Serializable] public sealed class CombatPerformanceRun
    {
        public int schema = 1;
        public string id, status = "RUNNING", reason = "", startedUtc, endedUtc, output;
        public string origin, unityVersion, cpu, gpu, os, graphicsApi, scene, quality, renderPipeline, camera, effectSettings;
        public string profileHash, environmentHash, sourceRevision, contentFingerprint, workloadSignature, weapon, gameSettings, inputBindings;
        public int width, height, vSync, targetFrameRate, logErrors;
        public string[] rosterIds;
        public CombatPerformanceProfile profile;
        public List<CombatPerformanceSegment> segments = new List<CombatPerformanceSegment>();
        public string measurementContract = "Wall=completed early callback intervals. Profiler=latest completed samples; nested/all-thread markers overlap and must not be summed. GPU/CPU FrameTiming samples are a separate delayed channel, never assigned to a combat event frame. Fixture status/energy/death injection is explicitly labelled. High enemy HP changes survival. First-use means first in this run, not OS/shader-cache cold. No performance acceptance is inferred from test completion.";
    }
    [Serializable] public sealed class CombatPerformanceComparisonRow
    { public string key, status, reason; public int baselineRuns, currentRuns; public double beforeMedianP95, afterMedianP95, deltaMs, deltaPercent; }
    [Serializable] public sealed class CombatPerformanceComparison
    { public string status, reason; public string baseline, current; public List<CombatPerformanceComparisonRow> rows = new List<CombatPerformanceComparisonRow>(); }

    public static class CombatPerformancePaths
    {
        static string FindRoot()
        {
            for (var d = new DirectoryInfo(Application.dataPath); d != null; d = d.Parent)
            {
                if (d.Name == "코덱스산출" && d.Parent?.Name == "개인파일") return d.FullName;
                string candidate = Path.Combine(d.FullName, "개인파일", "코덱스산출");
                if (Directory.Exists(candidate)) return candidate;
            }
            return null;
        }
        public static string OutputRoot => Path.Combine(FindRoot() ?? throw new IOException("개인파일/코덱스산출을 찾지 못했습니다. Player 실행 시 산출 폴더를 지정하세요."), "Performance", "CombatPerformance");
        public static string RequireOutput(string path)
        {
            string root = FindRoot();
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (root == null && !Application.isEditor)
            {
                for (var d = new DirectoryInfo(full); d != null; d = d.Parent)
                    if (d.Name == "코덱스산출" && d.Parent?.Name == "개인파일" && d.Exists) { root = d.FullName; break; }
            }
            if (root == null || !full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("결과는 개인파일/코덱스산출 하위에 저장하세요.");
            for (var d = new DirectoryInfo(full); d != null; d = d.Parent)
            { if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("산출 경로에 링크가 있습니다."); if (d.FullName == root) break; }
            return full;
        }
        public static string Hash(string value)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
        public static void SaveJson(string path, object data)
        {
            RequireOutput(path); Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".writing";
            File.WriteAllText(temporary, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
    }
}
#endif
