using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Local-only actual Gameplay input fixture for GOAL VFX-01.
public static class BloodGoalFieldRunner
{
    private static readonly Stack<IEnumerator> Work = new Stack<IEnumerator>();
    private static readonly string[] Metrics =
    {
        "PlayedCount", "DroppedCount", "ActiveCount", "PeakActiveCount",
        "RequestedCount", "OffscreenCount", "DuplicateCount", "DroppedQueueCount",
        "DroppedFrameCount", "DroppedPoolCount", "PreemptedCount",
        "SweepPlayedCount", "ThrustPlayedCount", "DownwardPlayedCount", "PeakQueuedCount"
    };
    private static readonly string[] GroundMetrics =
    {
        "RequestedCount", "ShownCount", "ActiveCount", "PeakActiveCount",
        "SkippedNoGroundCount", "SkippedSpatialCount", "SkippedQueueCount",
        "SkippedLateCount", "SkippedPoolCount", "MaterialVariantCount"
    };
    private static int lastFrame;
    private static readonly List<float> FrameMs = new List<float>();
    private static long peakAllocatedBytes;
    private static bool previousBackground;
    private static int previousFrameRate;
    private static double deadline;
    private static string output;
    public static string Result { get; private set; } = "IDLE";

    public static void Start(string label, string theme = "PrimalHunt", int count = 50)
    {
        if (!EditorApplication.isPlaying || Work.Count != 0)
            throw new InvalidOperationException("Requires idle Play Mode");
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("A unique result label is required", nameof(label));

        output = Path.GetFullPath("../개인파일/코덱스산출/CombatVfx/20260924_GoalVFX01/" + label);
        Directory.CreateDirectory(output);
        SessionState.SetString("MonsterThemePlayVerifier.output", output);
        SessionState.SetString("MonsterThemeCombatField.table", theme);
        SessionState.SetInt("MonsterThemeCombatField.count", count);
        SessionState.SetFloat("MonsterThemeCombatField.captureStart", 28f);
        SessionState.SetFloat("MonsterThemeCombatField.captureEnd", 31f);
        previousBackground = Application.runInBackground;
        previousFrameRate = Application.targetFrameRate;
        Application.runInBackground = true;
        Result = "RUNNING";
        lastFrame = -1;
        FrameMs.Clear();
        peakAllocatedBytes = 0;
        deadline = EditorApplication.timeSinceStartup + 240;
        Work.Push(Run(theme, count));
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += PlayChanged;
    }

    private static IEnumerator Run(string theme, int count)
    {
        var ui = EnemyThemeTrialHarness.Current;
        var player = PlayerInputFacade.Current;
        var blood = Object.FindFirstObjectByType<BloodHitVfxService>();
        var ground = Object.FindFirstObjectByType<BloodGroundDecalService>();
        if (!ui || !player || !blood || !ground)
            throw new InvalidOperationException("Missing theme UI, player, blood, or ground service");
        bool entered = !ui.InArena;
        try
        {
            float sceneDeadline = Time.time + 15f;
            while ((PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
                    || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
                   && Time.time < sceneDeadline)
                yield return null;
            if (PersistentSceneFlow.Instance == null
                || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
                throw new InvalidOperationException("Hideout did not finish loading");
            if (entered) ui.ToggleArena();
            if (!ui.InArena)
                throw new InvalidOperationException("Theme arena did not open");
            float readyAt = Time.time + .7f;
            while (Time.time < readyAt) yield return null;
            var before = Snapshot(blood);
            var groundBefore = Snapshot(ground, GroundMetrics);
            yield return MonsterThemeCombatFieldVerifier.Verify(ui, player);
            var after = Snapshot(blood);
            var groundAfter = Snapshot(ground, GroundMetrics);
            var delta = new Dictionary<string, int>();
            foreach (var entry in after)
                if (before.TryGetValue(entry.Key, out int start))
                    delta.Add(entry.Key, entry.Value - start);
            var groundDelta = new Dictionary<string, int>();
            foreach (var entry in groundAfter)
                if (groundBefore.TryGetValue(entry.Key, out int groundStart))
                    groundDelta.Add(entry.Key, entry.Value - groundStart);
            float[] frames = FrameMs.OrderBy(x => x).ToArray();
            var timing = new
            {
                samples = frames.Length,
                meanMs = frames.Length > 0 ? frames.Average() : 0f,
                p95Ms = frames.Length > 0 ? frames[Mathf.Min(frames.Length - 1, (int)(frames.Length * .95f))] : 0f,
                peakAllocatedMiB = peakAllocatedBytes / 1048576f
            };
            var result = new { theme, count, before, after, delta,
                groundBefore, groundAfter, groundDelta, timing };
            File.WriteAllText(Path.Combine(output, "blood-metrics.json"),
                JsonConvert.SerializeObject(result, Formatting.Indented));
            Result = "PASS " + theme + " " + count + " blood=" + delta["PlayedCount"]
                + " dropped=" + delta["DroppedCount"] + " peak=" + after["PeakActiveCount"]
                + " ground=" + groundDelta["ShownCount"];
        }
        finally
        {
            ui.Clear();
            if (entered && ui.InArena) ui.ToggleArena();
        }
    }

    private static Dictionary<string, int> Snapshot(BloodHitVfxService blood)
        => Snapshot(blood, Metrics);

    private static Dictionary<string, int> Snapshot(object service, string[] names)
    {
        var result = new Dictionary<string, int>();
        Type type = service.GetType();
        foreach (string name in names)
        {
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.PropertyType == typeof(int))
                result[name] = (int)property.GetValue(service);
        }
        return result;
    }

    private static void Tick()
    {
        try
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup >= deadline)
                throw new InvalidOperationException("Play or time limit");
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            if (Time.unscaledDeltaTime > 0f && Time.unscaledDeltaTime < 1f)
                FrameMs.Add(Time.unscaledDeltaTime * 1000f);
            peakAllocatedBytes = Math.Max(peakAllocatedBytes, UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong());
            while (Work.Count > 0)
            {
                IEnumerator current = Work.Peek();
                if (!current.MoveNext())
                {
                    (Work.Pop() as IDisposable)?.Dispose();
                    continue;
                }
                if (current.Current is IEnumerator nested)
                {
                    Work.Push(nested);
                    continue;
                }
                return;
            }
            Finish(Result);
        }
        catch (Exception error) { Finish("FAIL " + error); }
    }

    private static void PlayChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode && Work.Count > 0)
            Finish("FAIL interrupted by Play exit");
    }

    private static void Finish(string status)
    {
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayChanged;
        while (Work.Count > 0) (Work.Pop() as IDisposable)?.Dispose();
        Application.runInBackground = previousBackground;
        Application.targetFrameRate = previousFrameRate;
        Result = status;
        if (!string.IsNullOrEmpty(output))
            File.WriteAllText(Path.Combine(output, "runner-result.txt"), Result);
        Debug.Log("[BloodGoalFieldRunner] " + Result);
    }
}
