#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace Overburst.DebugTools.Performance
{
    // The runner supplies fixed health bindings. No scene searches, strings, or disk writes while sampling.
    public sealed class CombatPerformanceRecorder : IDisposable
    {
        struct Frame
        {
            public int id, targets, alive, bloodRequests, bloodPlayed, hits, kills, requests, accepted, parries;
            public float scale; public double seconds, wall, observer;
        }
        struct PerformanceEvent
        { public int frame, kind, target, attack, element, fatal; public double seconds; public float damage; }
        struct Timing
        { public int observedFrame; public ulong timestamp; public double cpu, gpu; }
        const int MaximumMetrics = 72;
        static readonly string[] EngineNames = { "Main Thread", "Render Thread", "PlayerLoop", "GC Allocated In Frame", "GC Used Memory",
            "GC Reserved Memory", "Total Used Memory", "GC.Collect", "Draw Calls Count", "SetPass Calls Count", "Triangles Count",
            "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForRenderThread", "WaitForTargetFPS", "WaitForLastPresentation",
            "Animators.Update", "Physics.Simulate", "Audio.Update", "Resources.Load", "Shader.CreateGPUProgram",
            "ParticleSystem.UpdateJob", "ParticleSystem.GeometryJob", "ParticleSystem.Draw", "VFX.Update", "VFX.Prepare" };
        readonly List<ProfilerRecorder> recorders = new List<ProfilerRecorder>();
        readonly List<CombatPerformanceMetric> descriptors = new List<CombatPerformanceMetric>();
        readonly FrameTiming[] timingBuffer = new FrameTiming[1];
        readonly Frame[] frames; readonly PerformanceEvent[] events; readonly Timing[] timings;
        double[] metricValues;
        int count, eventCount, timingCount, droppedEvents, tracked = -1, hitchStreak;
        long start, previous;
        ulong lastTiming;
        bool running;
        Frame current;
        int bloodRequestStart, bloodPlayStart;
        readonly BloodHitVfxService blood;
        readonly int[] gcStart = new int[3];
        public CombatPerformanceSegment Result { get; }
        public bool CapacityReached => count >= frames.Length;

        public CombatPerformanceRecorder(CombatPerformanceProfile profile, CombatPerformanceSegment segment, BloodHitVfxService bloodService)
        {
            Result = segment; blood = bloodService;
            frames = new Frame[profile.frameCapacity]; events = new PerformanceEvent[profile.eventCapacity]; timings = new Timing[profile.frameCapacity];
            var handles = new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(handles);
            var used = new HashSet<string>();
            foreach (var handle in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(handle);
                if (descriptors.Count >= MaximumMetrics || (!d.Name.StartsWith("Overburst.", StringComparison.Ordinal)
                    && !d.Name.StartsWith("OVERBURST.", StringComparison.Ordinal) && Array.IndexOf(EngineNames, d.Name) < 0)) continue;
                if (!used.Add(d.Category.Name + "/" + d.Name)) continue;
                var recorder = ProfilerRecorder.StartNew(d.Category, d.Name, 1,
                    ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.WrapAroundWhenCapacityReached | ProfilerRecorderOptions.SumAllSamplesInFrame);
                recorders.Add(recorder);
                descriptors.Add(new CombatPerformanceMetric { name = d.Name, category = d.Category.Name, unit = d.UnitType.ToString(), nanoseconds = d.UnitType == ProfilerMarkerDataUnit.TimeNanoseconds });
            }
            metricValues = new double[frames.Length * recorders.Count];
            bloodRequestStart = blood != null ? blood.RequestedCount : 0;
            bloodPlayStart = blood != null ? blood.PlayedCount : 0;
            for (int i = 0; i < 3; i++) gcStart[i] = GC.CollectionCount(i);
            Result.memoryStartBytes = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            start = Stopwatch.GetTimestamp(); running = true;
        }
        static double Milliseconds(long a, long b) => (b - a) * 1000d / Stopwatch.Frequency;
        public void Tick()
        {
            if (!running || tracked == Time.frameCount) return;
            long began = Stopwatch.GetTimestamp();
            if (tracked >= 0 && count < frames.Length)
            {
                current.wall = Milliseconds(previous, began);
                int row = count++;
                for (int m = 0; m < recorders.Count; m++)
                {
                    var rec = recorders[m];
                    metricValues[row * recorders.Count + m] = rec.Valid && rec.Count > 0
                        ? rec.LastValue / (descriptors[m].nanoseconds ? 1000000d : 1d) : double.NaN;
                }
                frames[row] = current;
                if (current.wall > 33.333333) Result.hitches33++;
                if (current.wall > 50) { Result.hitches50++; hitchStreak++; } else hitchStreak = 0;
                if (current.wall > 100) Result.hitches100++;
                Result.longestHitchFrames = Math.Max(Result.longestHitchFrames, hitchStreak);
            }
            tracked = Time.frameCount; previous = began;
            current = new Frame { id = tracked, seconds = Milliseconds(start, began) / 1000d };
            if (FrameTimingManager.GetLatestTimings(1, timingBuffer) > 0)
            {
                FrameTiming t = timingBuffer[0];
                if (t.frameStartTimestamp != 0 && t.frameStartTimestamp != lastTiming && timingCount < timings.Length)
                {
                    lastTiming = t.frameStartTimestamp;
                    // Exclude delayed samples that could still belong to the previous phase.
                    if (count >= 8) timings[timingCount++] = new Timing { observedFrame = tracked, timestamp = lastTiming,
                        cpu = t.cpuFrameTime > 0 ? t.cpuFrameTime : double.NaN, gpu = t.gpuFrameTime > 0 ? t.gpuFrameTime : double.NaN };
                }
            }
            FrameTimingManager.CaptureFrameTimings();
            current.observer += Milliseconds(began, Stopwatch.GetTimestamp());
        }
        public void Observe(int alive)
        {
            if (!running || tracked != Time.frameCount) return;
            current.scale = Time.timeScale; current.targets = CombatTargetRegistry.RegisteredCount; current.alive = alive;
            current.hits = Result.enemyHits; current.kills = Result.kills; current.requests = Result.requests;
            current.accepted = Result.accepted; current.parries = Result.parries;
            current.bloodRequests = blood != null ? blood.RequestedCount - bloodRequestStart : -1;
            current.bloodPlayed = blood != null ? blood.PlayedCount - bloodPlayStart : -1;
        }
        public void AddObserverTicks(long began)
        { if (running) current.observer += Milliseconds(began, Stopwatch.GetTimestamp()); }
        // kind: 1=request, 2=accepted, 3=rejected, 4=enemy hit, 5=player hit, 6=fixture death, 7=fixture preparation.
        public void Event(int kind, int target = 0, float damage = 0, int attack = 0, int element = 0, bool fatal = false)
        {
            if (!running) return;
            if (eventCount == events.Length) { droppedEvents++; return; }
            events[eventCount++] = new PerformanceEvent { frame = Time.frameCount, kind = kind, target = target, damage = damage, attack = attack,
                element = element, fatal = fatal ? 1 : 0, seconds = Milliseconds(start, Stopwatch.GetTimestamp()) / 1000d };
        }
        public void End(string folder)
        {
            if (!running) return;
            running = false; DisposeRecorders();
            Result.frames = count; Result.droppedEvents = droppedEvents; Result.capacityReached = CapacityReached;
            Result.memoryEndBytes = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            Result.gc0 = GC.CollectionCount(0) - gcStart[0]; Result.gc1 = GC.CollectionCount(1) - gcStart[1]; Result.gc2 = GC.CollectionCount(2) - gcStart[2];
            Result.bloodRequests = blood != null ? blood.RequestedCount - bloodRequestStart : -1;
            Result.bloodPlayed = blood != null ? blood.PlayedCount - bloodPlayStart : -1;
            Result.bloodUnavailable = blood != null ? Math.Max(0, Result.bloodRequests - Result.bloodPlayed) : -1;
            var values = new double[Math.Max(count, timingCount)];
            for (int i = 0; i < count; i++) { values[i] = frames[i].wall; Result.seconds += frames[i].wall / 1000d; }
            Result.wall = CombatPerformanceStats.Calculate(values, count);
            for (int i = 0; i < count; i++) values[i] = frames[i].observer;
            Result.observer = CombatPerformanceStats.Calculate(values, count);
            for (int i = 0; i < timingCount; i++) values[i] = timings[i].cpu;
            Result.delayedCpu = CombatPerformanceStats.Calculate(values, timingCount);
            for (int i = 0; i < timingCount; i++) values[i] = timings[i].gpu;
            Result.delayedGpu = CombatPerformanceStats.Calculate(values, timingCount);
            Result.metrics = new CombatPerformanceMetricResult[descriptors.Count];
            for (int m = 0; m < descriptors.Count; m++)
            {
                for (int i = 0; i < count; i++) values[i] = metricValues[i * descriptors.Count + m];
                Result.metrics[m] = new CombatPerformanceMetricResult { metric = descriptors[m], stats = CombatPerformanceStats.Calculate(values, count) };
            }
            Result.hitch50PerMinute = Result.seconds > 0 ? Result.hitches50 * 60d / Result.seconds : 0;
            if (count < 2 || droppedEvents > 0 || CapacityReached)
            { Result.validity = "INCOMPLETE"; Result.reason = "샘플 부족 또는 기록 버퍼 포화"; }
            else if (Result.observer.p95 > Math.Max(.5, Result.wall.median * .1))
            { Result.validity = "OBSERVER_HEAVY"; Result.reason = "기록기/실행기 P95 비용이 기준을 초과했습니다."; }
            Directory.CreateDirectory(folder);
            WriteFrames(Path.Combine(folder, "frames.csv")); WriteEvents(Path.Combine(folder, "events.csv")); WriteTimings(Path.Combine(folder, "timings.csv"));
            CombatPerformancePaths.SaveJson(Path.Combine(folder, "segment.json"), Result);
        }
        static string N(double value) => double.IsNaN(value) || double.IsInfinity(value) ? "" : value.ToString("0.######", CultureInfo.InvariantCulture);
        static string Q(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        void WriteFrames(string path)
        {
            using (var w = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                w.Write("frame,seconds,wall_ms,time_scale,observer_ms,targets,alive,hits_total,kills_total,requests_total,accepted_total,parries_total,blood_requested_total,blood_played_total");
                foreach (var d in descriptors) w.Write("," + Q(d.category + "/" + d.name + " [" + (d.nanoseconds ? "ms" : d.unit) + "]"));
                w.WriteLine();
                for (int i = 0; i < count; i++)
                {
                    Frame f = frames[i];
                    w.Write(string.Join(",", new[] { N(f.id), N(f.seconds), N(f.wall), N(f.scale), N(f.observer), N(f.targets), N(f.alive), N(f.hits),
                        N(f.kills), N(f.requests), N(f.accepted), N(f.parries), N(f.bloodRequests), N(f.bloodPlayed) }));
                    for (int m = 0; m < descriptors.Count; m++) w.Write("," + N(metricValues[i * descriptors.Count + m]));
                    w.WriteLine();
                }
            }
        }
        void WriteEvents(string path)
        {
            using (var w = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                w.WriteLine("frame,seconds,kind,target,damage,attack_kind,element,fatal");
                for (int i = 0; i < eventCount; i++) { var e = events[i]; w.WriteLine(string.Join(",", new[] { N(e.frame), N(e.seconds), N(e.kind), N(e.target), N(e.damage), N(e.attack), N(e.element), N(e.fatal) })); }
            }
        }
        void WriteTimings(string path)
        {
            using (var w = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                w.WriteLine("observed_frame,source_timestamp,delayed_cpu_ms,delayed_gpu_ms");
                for (int i = 0; i < timingCount; i++) { var t = timings[i]; w.WriteLine(t.observedFrame + "," + t.timestamp + "," + N(t.cpu) + "," + N(t.gpu)); }
            }
        }
        void DisposeRecorders() { for (int i = 0; i < recorders.Count; i++) recorders[i].Dispose(); recorders.Clear(); }
        public void Dispose() { running = false; DisposeRecorders(); }
    }
}
#endif
