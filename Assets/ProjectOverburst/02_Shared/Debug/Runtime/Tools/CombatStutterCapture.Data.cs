#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overburst.DebugTools
{
    public sealed partial class CombatStutterCapture
    {
        const int MaximumFrames = 18000, MaximumEvents = 16384, MaximumBindings = 512, MaximumMetrics = 96;
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        static readonly string[] EngineMetrics = {
            "Main Thread", "Render Thread", "PlayerLoop", "GC.Collect", "GC Allocated In Frame",
            "GC Used Memory", "GC Reserved Memory", "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForRenderThread",
            "WaitForTargetFPS", "WaitForLastPresentation", "Semaphore.WaitForSignal",
            "ParticleSystem.UpdateJob", "ParticleSystem.GeometryJob", "ParticleSystem.Draw",
            "ParticleSystem.WaitForPreviousRenderingToFinish", "VFX.Update", "VFX.Prepare",
            "RenderLoop.DrawSRPBatcher", "Animators.Update", "Physics.Simulate",
            "Audio.Update", "AudioClip.LoadAudioData", "Resources.Load", "Resources.UnloadUnusedAssets",
            "Shader.CreateGPUProgram", "CreateGPUProgram", "Gfx.CreateGraphicsPipeline", "Draw Calls Count"
        };
        struct Metric
        {
            public string name, category, unit;
            public bool timeNanoseconds;
            public ProfilerRecorder recorder;
        }
        struct AudioState { public int clip, position; public bool playing; }
        [Serializable] struct Hit
        {
            public int frame, target, source, element, attackKind, sequence, phase;
            public double seconds;
            public float damage;
            public bool critical, dot, fatal;
        }
        [Serializable] struct Sound
        {
            public int frame, source, clipId, observedSamples;
            public string clip, loadState;
            public bool newlyBound;
            public double seconds;
        }
        [Serializable] struct Frame
        {
            public int frame, hitCount, criticalCount, dotCount, audioStarts;
            public int gc0, gc1, gc2, maintenanceOps, targetCount, healthBindings, audioBindings;
            public int bloodPlayed, bloodRequests, bloodPlayedDelta, bloodRequestDelta, bloodVariation, bloodGraphId, bloodVariationDelta;
            public int auraControllers, auraLeases, saveWrites, saveWriteDelta, element;
            public float energy, timeScale;
            public double seconds, wallMs, unscaledDeltaMs, observerMs, gpuMs, cpuTimingMs;
            public ulong frameTimingStamp;
            public bool endObserved, registryScan, pendingSave;
        }
        [Serializable] sealed class MetricInfo { public string name, category, unit; public bool nanoseconds; }
        [Serializable] sealed class Summary
        {
            public string schema = "overburst-combat-stutter-v1";
            public string stopReason, unityVersion, platform, graphicsApi, activeScene, recordedUtc, sampleOrigin;
            public string frameTimingOrigin, audioOrigin, hitOrigin, bloodOrigin;
            public string[] unavailableEngineMetrics;
            public MetricInfo[] metrics;
            public int frames, hits, audioObservations, hitches, droppedHits, droppedSounds, bindingLimitHits;
            public int hitsBeforeFrameCallback;
            public int maxFrames, maxEvents, maxBindings;
            public float thresholdMs;
            public bool binaryProfilerRequested, binaryProfilerFilePresent, profilerOwnershipLost, includesIncompleteLastFrame;
            public double maxWallMs, maxObserverMs;
            public Frame[] worstFrames;
        }

        static bool WantedMetric(string name)
        {
            return name.StartsWith("Overburst.", StringComparison.Ordinal)
                || Array.IndexOf(EngineMetrics, name) >= 0;
        }

        static double Milliseconds(long begin, long end) => TicksToMs(begin, end, Stopwatch.Frequency);
        static double TicksToMs(long begin, long end, long frequency)
        {
            if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
            return (end - begin) * (1000d / frequency);
        }
        static string Number(double value) => double.IsNaN(value) || double.IsInfinity(value)
            ? string.Empty : value.ToString("0.######", Invariant);
        static string Csv(string value)
        {
            value = value ?? string.Empty;
            return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0
                ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        static string MetricNumber(long raw, bool available, bool nanoseconds)
            => available ? Number(nanoseconds ? raw / 1000000d : raw) : string.Empty;

        static string CreateFolder(string explicitRoot)
        {
            string root = explicitRoot;
            if (string.IsNullOrWhiteSpace(root))
            {
                var directory = new DirectoryInfo(Application.dataPath);
                for (int i = 0; i < 12 && directory != null; i++, directory = directory.Parent)
                {
                    string candidate = Path.Combine(directory.FullName, "개인파일", "코덱스산출");
                    if (Directory.Exists(candidate)) { root = candidate; break; }
                }
            }
            if (string.IsNullOrWhiteSpace(root))
                throw new IOException("결과 폴더를 찾지 못했어요. --overburst-stutter-output으로 개인파일/코덱스산출 폴더를 지정하세요");
            var resolved = new DirectoryInfo(Path.GetFullPath(root));
            if (resolved.Name != "코덱스산출" || resolved.Parent == null || resolved.Parent.Name != "개인파일")
                throw new IOException("결과 루트는 개인파일/코덱스산출 폴더여야 해요");
            string folder = Path.Combine(resolved.FullName, "Performance", "CombatStutterDiagnostics",
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", Invariant) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(folder);
            return folder;
        }

        static bool[] HitchWindows(Frame[] samples, int count, double thresholdMs, int radius)
        {
            var selected = new bool[count];
            for (int i = 0; i < count; i++)
            {
                if (samples[i].wallMs < thresholdMs) continue;
                for (int j = Math.Max(0, i - radius); j <= Math.Min(count - 1, i + radius); j++) selected[j] = true;
            }
            return selected;
        }

        void Export()
        {
            // All CSV/JSON I/O happens after subscriptions and recorders have stopped.
            var info = new MetricInfo[metrics.Count];
            for (int i = 0; i < metrics.Count; i++) info[i] = new MetricInfo {
                name = metrics[i].name, category = metrics[i].category, unit = metrics[i].unit,
                nanoseconds = metrics[i].timeNanoseconds };
            var unavailable = new List<string>();
            foreach (string wanted in EngineMetrics)
            {
                bool found = false;
                foreach (Metric metric in metrics) if (metric.name == wanted) { found = true; break; }
                if (!found) unavailable.Add(wanted);
            }
            var worst = new List<Frame>(frameCount);
            int hitches = 0;
            double maxWall = 0, maxObserver = 0;
            for (int i = 0; i < frameCount; i++)
            {
                Frame frame = frames[i];
                maxWall = Math.Max(maxWall, frame.wallMs);
                maxObserver = Math.Max(maxObserver, frame.observerMs);
                if (frame.wallMs >= threshold) hitches++;
                worst.Add(frame);
            }
            worst.Sort((a, b) => b.wallMs.CompareTo(a.wallMs));
            if (worst.Count > 32) worst.RemoveRange(32, worst.Count - 32);
            var summary = new Summary {
                stopReason = stopReason, unityVersion = Application.unityVersion, platform = Application.platform.ToString(),
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), activeScene = SceneManager.GetActiveScene().name,
                recordedUtc = DateTime.UtcNow.ToString("O", Invariant), thresholdMs = threshold,
                frames = frameCount, hits = hitCount, audioObservations = soundCount, hitches = hitches,
                maxWallMs = maxWall, maxObserverMs = maxObserver, worstFrames = worst.ToArray(),
                droppedHits = droppedHits, droppedSounds = droppedSounds, bindingLimitHits = bindingLimitHits,
                hitsBeforeFrameCallback = hitsBeforeFrameCallback,
                maxFrames = MaximumFrames, maxEvents = MaximumEvents, maxBindings = MaximumBindings,
                metrics = info, unavailableEngineMetrics = unavailable.ToArray(),
                binaryProfilerRequested = binaryRequested, binaryProfilerFilePresent = File.Exists(Path.Combine(folder, "profiler.raw")),
                profilerOwnershipLost = profilerOwnershipLost, includesIncompleteLastFrame = false,
                sampleOrigin = "Wall=first early FixedUpdate/Update callback to the next frame's first callback; context/events=completed game frame. Profiler columns=latest completed recorder sample, not a guaranteed frame ID. Nested/all-thread marker totals overlap; never sum them into wall time.",
                frameTimingOrigin = "FrameTimingManager is delayed; only a new nonzero timestamp is reported. -1 means unavailable. GPU timing is not attributed to the context frame.",
                audioOrigin = "AudioSource state observation at late frame; binding scan each second. newlyBound is not proof of first-ever playback. Very short or same-frame retriggers and newly-created voices may be missed; use profiler.raw for CPU call hierarchy.",
                hitOrigin = "OnDamageResolved subscribers; no damage/input/status injection. Targets appearing between binding scans may be missed. Events share actual Time.frameCount. Events before our first frame callback remain in hits.csv and are counted separately from frame-context hits.",
                bloodOrigin = "Existing service counters and last sweep variation only; multiple blood plays in a frame cannot be mapped individually. -1 is unavailable variation." };
            File.WriteAllText(Path.Combine(folder, "summary.json"), JsonUtility.ToJson(summary, true), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "effect-ab.json"), JsonUtility.ToJson(CombatEffectDiagnosticControls.Snapshot(), true), new UTF8Encoding(false));
            bool[] windows = HitchWindows(frames, frameCount, threshold, 8);
            WriteFrames(Path.Combine(folder, "frames.csv"), null);
            WriteFrames(Path.Combine(folder, "hitch-windows.csv"), windows);
            WriteHits();
            WriteSounds();
        }

        void WriteFrames(string path, bool[] selected)
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                writer.Write("completed_frame,seconds,wall_ms,unscaled_delta_ms,time_scale,observer_ms,registry_scan,end_observed,hits,critical,dot,energy,element,gc0,gc1,gc2,maintenance_ops,targets,health_bindings,audio_bindings,aura_controllers,aura_leases,blood_requests,blood_request_delta,blood_played,blood_played_delta,last_blood_variation,blood_variation_delta,last_new_sweep_graph_id,save_writes,save_write_delta,pending_save,audio_starts,delayed_cpu_ms,delayed_gpu_ms,timing_timestamp");
                foreach (Metric metric in metrics) writer.Write("," + Csv(metric.category + "/" + metric.name + (metric.timeNanoseconds ? " [ms]" : " [" + metric.unit + "]")));
                writer.WriteLine();
                for (int i = 0; i < frameCount; i++)
                {
                    if (selected != null && !selected[i]) continue;
                    Frame f = frames[i];
                    writer.Write(string.Join(",", new[] { f.frame.ToString(Invariant), Number(f.seconds), Number(f.wallMs),
                        Number(f.unscaledDeltaMs), Number(f.timeScale), Number(f.observerMs), f.registryScan ? "1" : "0",
                        f.endObserved ? "1" : "0", f.hitCount.ToString(Invariant), f.criticalCount.ToString(Invariant),
                        f.dotCount.ToString(Invariant), Number(f.energy), f.element.ToString(Invariant), f.gc0.ToString(Invariant),
                        f.gc1.ToString(Invariant), f.gc2.ToString(Invariant), f.maintenanceOps.ToString(Invariant), f.targetCount.ToString(Invariant),
                        f.healthBindings.ToString(Invariant), f.audioBindings.ToString(Invariant), f.auraControllers.ToString(Invariant),
                        f.auraLeases.ToString(Invariant), f.bloodRequests.ToString(Invariant), f.bloodRequestDelta.ToString(Invariant),
                        f.bloodPlayed.ToString(Invariant), f.bloodPlayedDelta.ToString(Invariant), f.bloodVariation.ToString(Invariant),
                        f.bloodVariationDelta.ToString(Invariant), f.bloodGraphId.ToString(Invariant),
                        f.saveWrites.ToString(Invariant), f.saveWriteDelta.ToString(Invariant), f.pendingSave ? "1" : "0", f.audioStarts.ToString(Invariant),
                        f.cpuTimingMs < 0 ? "" : Number(f.cpuTimingMs), f.gpuMs < 0 ? "" : Number(f.gpuMs), f.frameTimingStamp.ToString(Invariant) }));
                    for (int m = 0; m < metrics.Count; m++)
                    {
                        int cell = i * metrics.Count + m;
                        writer.Write("," + MetricNumber(metricValues[cell], metricAvailable[cell] != 0, metrics[m].timeNanoseconds));
                    }
                    writer.WriteLine();
                }
            }
        }

        void WriteHits()
        {
            using (var writer = new StreamWriter(Path.Combine(folder, "hits.csv"), false, new UTF8Encoding(false)))
            {
                writer.WriteLine("frame,seconds,target,source,actual_damage,critical,dot,fatal,element,attack_kind,sequence,phase");
                for (int i = 0; i < hitCount; i++)
                {
                    Hit h = hits[i];
                    writer.WriteLine(string.Join(",", new[] { h.frame.ToString(Invariant), Number(h.seconds), h.target.ToString(Invariant),
                        h.source.ToString(Invariant), Number(h.damage), h.critical ? "1" : "0", h.dot ? "1" : "0", h.fatal ? "1" : "0",
                        h.element.ToString(Invariant), h.attackKind.ToString(Invariant), h.sequence.ToString(Invariant), h.phase.ToString(Invariant) }));
                }
            }
        }
        void WriteSounds()
        {
            using (var writer = new StreamWriter(Path.Combine(folder, "audio.csv"), false, new UTF8Encoding(false)))
            {
                writer.WriteLine("observed_frame,seconds,source,clip_id,clip,load_state,time_samples,newly_bound");
                for (int i = 0; i < soundCount; i++)
                {
                    Sound s = sounds[i];
                    writer.WriteLine(string.Join(",", new[] { s.frame.ToString(Invariant), Number(s.seconds), s.source.ToString(Invariant),
                        s.clipId.ToString(Invariant), Csv(s.clip), Csv(s.loadState), s.observedSamples.ToString(Invariant), s.newlyBound ? "1" : "0" }));
                }
            }
        }

        // Pure checks for the failure modes that would mislabel a hitch; does not enter Play or touch files.
        public static string ValidateCore()
        {
            int checks = 0;
            Require(Math.Abs(TicksToMs(10, 1093, 1000) - 1083d) < .00001, "long wall interval"); checks++;
            Require(MetricNumber(2500000, true, true) == "2.5", "nanoseconds to ms"); checks++;
            Require(MetricNumber(2500000, true, false) == "2500000", "bytes stay bytes"); checks++;
            Require(MetricNumber(123, false, true) == "", "missing sample is unavailable"); checks++;
            Require(Csv("clip,\"one\"\nline") == "\"clip,\"\"one\"\"\nline\"", "CSV round-trip quoting"); checks++;
            Require(Csv(null) == string.Empty, "null CSV"); checks++;
            var sample = new Frame[6];
            for (int i = 0; i < sample.Length; i++) sample[i] = new Frame { frame = 100 + i, wallMs = 16 };
            sample[0].wallMs = 1083;
            sample[5].wallMs = 50;
            bool[] windows = HitchWindows(sample, 6, 50, 1);
            Require(windows[0] && windows[1] && !windows[2] && !windows[3] && windows[4] && windows[5], "bounded hitch windows and threshold equality"); checks++;
            Require(sample[0].frame == 100 && sample[5].frame == 105, "completed-frame IDs preserved"); checks++;
            Require(!WantedMetric("unrelated.marker") && WantedMetric("Overburst.Account.DiskSave"), "save marker selection"); checks++;
            Require(Number(double.NaN) == "" && Number(double.PositiveInfinity) == "", "unavailable numbers"); checks++;
            return "PASS " + checks + " checks; no Play, scene or account changes";
        }
        static void Require(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("CombatStutterCapture core check failed: " + label);
        }
    }
}
#endif
