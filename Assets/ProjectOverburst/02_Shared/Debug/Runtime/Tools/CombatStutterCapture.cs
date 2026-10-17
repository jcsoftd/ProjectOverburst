#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Overburst.Persistence;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UDebug = UnityEngine.Debug;
using UObject = UnityEngine.Object;

namespace Overburst.DebugTools
{
    // Explicit opt-in only. Reads existing state; never injects damage, energy, input or save requests.
    [DefaultExecutionOrder(-32000)]
    public sealed partial class CombatStutterCapture : MonoBehaviour
    {
        public const string ArmKey = "OVERBURST.CombatStutterCapture.NextPlay";
        public const string LaunchFlag = "--overburst-stutter-diagnostic";
        static CombatStutterCapture instance;
        static readonly ProfilerMarker ExportMarker = new ProfilerMarker("Overburst.Diagnostics.Stutter.Export");
        static readonly ProfilerMarker SampleMarker = new ProfilerMarker("Overburst.Diagnostics.Stutter.Sample");
        static readonly ProfilerMarker ObserveMarker = new ProfilerMarker("Overburst.Diagnostics.Stutter.Observe");
        static readonly ProfilerMarker BindMarker = new ProfilerMarker("Overburst.Diagnostics.Stutter.Bind");
        static readonly ProfilerMarker ReadMetricsMarker = new ProfilerMarker("Overburst.Diagnostics.Stutter.ReadMetrics");
        static readonly ProfilerMarker ContextMarker = new ProfilerMarker("Overburst.Diagnostics.Stutter.Context");
        static readonly ProfilerMarker AudioMarker = new ProfilerMarker("Overburst.Diagnostics.Stutter.Audio");
        static readonly ProfilerMarker TimingMarker = new ProfilerMarker("Overburst.Diagnostics.Stutter.Timing");
        static readonly ProfilerMarker PoolMarker = new ProfilerMarker("Overburst.Diagnostics.Stutter.Pool");
        static readonly ProfilerMarker HitMarker = new ProfilerMarker("Overburst.Diagnostics.Stutter.Hit");
        readonly List<string> omittedMetrics = new List<string>();
        static readonly FieldInfo ActorField = typeof(PlayerContext).GetField("actor", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo BloodCatalogField = typeof(BloodHitVfxService).GetField("catalog", BindingFlags.NonPublic | BindingFlags.Instance);
        readonly HashSet<AudioSource> audioBindingSet = new HashSet<AudioSource>();
        readonly List<AudioSource> audioBindings = new List<AudioSource>(128);
        readonly Dictionary<int, AudioState> audioStates = new Dictionary<int, AudioState>(128);
        readonly List<Metric> metrics = new List<Metric>(MaximumMetrics);
        Frame[] frames;
        long[] metricValues;
        byte[] metricAvailable;
        Hit[] hits;
        Sound[] sounds;
        PoolEvent[] poolEvents;
        int poolEventCount, droppedPoolEvents;
        readonly Dictionary<(string pool, string reason), int> poolTotals = new Dictionary<(string, string), int>(64);
        readonly Dictionary<int, string> poolAssetNames = new Dictionary<int, string>(512);
        int frameCount, hitCount, soundCount, droppedHits, droppedSounds, bindingLimitHits, hitsBeforeFrameCallback;
        Frame current;
        int trackedFrame = -1;
        long frameStarted, captureStarted;
        DateTime captureStartedUtc;
        double duration;
        bool rebindObjects = true;
        string[] includedMetricPrefixes;
        float threshold;
        string folder, stopReason, previousProfilerPath, ownedProfilerPath;
        bool active, binaryOwned, binaryRequested, previousProfilerEnabled, previousBinaryEnabled, profilerOwnershipLost;
        bool deferExport, samplingComplete;
        BloodHitVfxService blood;
        BloodHitCatalog bloodCatalog;
        AccountAutosave autosave;
        RunWorldGate mapGate;
        OverburstElementEnergy playerEnergy;
        int previousBloodPlayed, previousBloodRequested, previousBloodVariations, previousSaveWrites;
        readonly FrameTiming[] timings = new FrameTiming[1];
        ulong lastTimingStamp;

        public static bool Running => instance != null && instance.active;
        public static string LastFolder { get; private set; }
        public static string LastStatus { get; private set; } = "아직 기록하지 않았어요";
        public static string Status => Running
            ? "전투 관찰 중 · " + instance.frameCount + "프레임 · " + instance.hitCount + "적중"
            : LastStatus;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState()
        {
            if (instance != null) instance.Finish("domain_reset");
            instance = null;
            LastFolder = null;
            LastStatus = "아직 기록하지 않았어요";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            bool armed = false;
#if UNITY_EDITOR
            armed = UnityEditor.SessionState.GetBool(ArmKey, false);
            UnityEditor.SessionState.EraseBool(ArmKey);
#endif
            string[] args = Environment.GetCommandLineArgs();
            bool requested = Array.IndexOf(args, LaunchFlag) >= 0;
            if (!armed && !requested) return;
            string outputRoot = Argument(args, "--overburst-stutter-output");
            bool binary = Array.IndexOf(args, "--overburst-stutter-profiler") >= 0;
            DebugResult result = Start(120f, 50f, binary, outputRoot);
            if (!result.Success) UDebug.LogWarning("[CombatStutterCapture] " + LastStatus);
        }

        static string Argument(string[] args, string key)
        {
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        public static DebugResult Start(float seconds = 120f, float hitchMs = 50f,
            bool binaryProfiler = false, string outputRoot = null, bool rebindObjects = true, string[] metricPrefixes = null,
            bool deferExport = false)
        {
            if (!Application.isPlaying) return DebugResult.Fail("Play 중에 시작하세요");
            if (Running) return DebugResult.Fail("이미 전투를 기록 중이에요");
            if (binaryProfiler && (Profiler.enabled || Profiler.enableBinaryLog))
                return DebugResult.Fail("기존 Profiler를 종료한 뒤 상세 기록을 시작하세요");
            GameObject root = null;
            try
            {
                string destination = CreateFolder(outputRoot);
                root = new GameObject("CombatStutterCapture (diagnostic)") { hideFlags = HideFlags.DontSave };
                DontDestroyOnLoad(root);
                var capture = root.AddComponent<CombatStutterCapture>();
                instance = capture;
                capture.folder = destination;
                capture.duration = Mathf.Clamp(seconds, 10f, MaximumSeconds);
                capture.threshold = Mathf.Clamp(hitchMs, 16f, 2000f);
                capture.binaryRequested = binaryProfiler;
                capture.deferExport = deferExport;
                capture.rebindObjects = rebindObjects;
                capture.includedMetricPrefixes = metricPrefixes == null ? null : (string[])metricPrefixes.Clone();
                capture.frames = new Frame[FrameCapacity(capture.duration)];
                capture.hits = new Hit[EventCapacity(capture.duration)];
                capture.sounds = new Sound[EventCapacity(capture.duration)];
                capture.poolEvents = new PoolEvent[EventCapacity(capture.duration)];
                capture.BindMetrics();
                capture.metricValues = new long[capture.frames.Length * capture.metrics.Count];
                capture.metricAvailable = new byte[capture.metricValues.Length];
                capture.BindObjects();
                if (binaryProfiler) capture.BeginBinaryProfiler();
                root.AddComponent<CombatStutterLateObserver>();
                // Setup allocations/imports/binding are outside the sampled interval.
                capture.captureStarted = Stopwatch.GetTimestamp();
                capture.captureStartedUtc = DateTime.UtcNow;
                SceneManager.sceneLoaded += capture.OnSceneLoaded;
                capture.active = true;
                LastFolder = destination;
                LastStatus = "기록 시작";
                return DebugResult.Ok("전투 관찰 시작 · 최대 " + capture.duration + "초");
            }
            catch (Exception error)
            {
                if (instance != null) instance.Release();
                instance = null;
                if (root != null) Destroy(root);
                LastStatus = "시작 실패: " + error.Message;
                return DebugResult.Fail(LastStatus);
            }
        }

        public static DebugResult Stop()
        {
            if (!Running) return DebugResult.Fail("기록 중이 아니에요");
            instance.Finish(instance.samplingComplete ? instance.stopReason : "manual_stop");
            return LastStatus.StartsWith("저장 실패", StringComparison.Ordinal)
                ? DebugResult.Fail(LastStatus) : DebugResult.Ok(LastStatus);
        }

        void Update()
        {
            BeginFrame();
        }

        void FixedUpdate()
        {
            BeginFrame();
            if (active && !samplingComplete) current.fixedTicks++;
        }

        void BeginFrame()
        {
            if (!active || samplingComplete || trackedFrame == Time.frameCount) return;
            long begin = Stopwatch.GetTimestamp();
            try
            {
                using (SampleMarker.Auto())
                {
                    if (trackedFrame >= 0) CompleteFrame(begin);
                    if (frameCount >= frames.Length || Milliseconds(captureStarted, begin) >= duration * 1000d)
                    {
                        string reason = frameCount >= frames.Length ? "frame_capacity" : "duration";
                        // Owned measurement exports after its combat recorder stops, outside the sampled window.
                        if (deferExport) { samplingComplete = true; trackedFrame = -1; stopReason = reason; }
                        else Finish(reason);
                        return;
                    }
                    trackedFrame = Time.frameCount;
                    frameStarted = begin;
                    current = new Frame { frame = trackedFrame, startTimestamp = begin, seconds = Milliseconds(captureStarted, begin) / 1000d,
                        timeScale = Time.timeScale, gpuMs = -1d, cpuTimingMs = -1d,
                        energy = -1f, element = -1, bloodVariation = -1, bloodGraphId = 0 };
                }
                current.observerMs += Milliseconds(begin, Stopwatch.GetTimestamp());
            }
            catch (Exception error) { LastStatus = "관찰 오류: " + error.Message; Finish("sampling_error"); }
        }

        void CompleteFrame(long now)
        {
            using var diagnostic = ReadMetricsMarker.Auto();
            current.wallMs = Milliseconds(frameStarted, now);
            current.endTimestamp = now;
            frames[frameCount] = current;
            for (int i = 0; i < metrics.Count; i++)
            {
                int cell = frameCount * metrics.Count + i;
                ProfilerRecorder recorder = metrics[i].recorder;
                if (!recorder.Valid || recorder.Count == 0) continue;
                metricValues[cell] = recorder.GetSample(recorder.Count - 1).Value;
                metricAvailable[cell] = 1;
            }
            frameCount++;
        }

        internal static void ObserveEndOfFrame()
        {
            if (!Running || instance.trackedFrame != Time.frameCount) return;
            instance.Observe();
        }

        void Observe()
        {
            long begin = Stopwatch.GetTimestamp();
            try
            {
                using (ObserveMarker.Auto())
                {
                    using (ContextMarker.Auto())
                    {
                    current.endObserved = true;
                    current.applicationFocused = Application.isFocused;
                    current.targetFrameRate = Application.targetFrameRate;
                    current.vsyncCount = QualitySettings.vSyncCount;
                    current.unscaledDeltaMs = Time.unscaledDeltaTime * 1000d;
                    current.timeScale = Time.timeScale;
                    current.fixedDeltaMs = Time.fixedDeltaTime * 1000f;
                    current.targetCount = CombatTargetRegistry.RegisteredCount;
                    var spawn = mapGate != null ? mapGate.SpawnBudget : null;
                    current.mapArea = spawn != null ? spawn.CurrentPlayerArea : -1;
                    current.mapAlive = spawn != null ? spawn.AliveCount : -1;
                    current.mapSpawned = spawn != null ? spawn.SpawnedThisFrame : 0;
                    current.mapKills = mapGate != null && mapGate.Roster != null ? mapGate.Roster.Kills : -1;
                    current.healthBindings = 0; // Source-level observation; no per-target subscriptions.
                    current.audioBindings = audioBindings.Count;
                    current.gc0 = GC.CollectionCount(0);
                    current.gc1 = GC.CollectionCount(1);
                    current.gc2 = GC.CollectionCount(2);
                    current.maintenanceOps = MeleeElementPoolMaintenance.LastOperations;
                    current.auraControllers = MeleeElementStatusAuraVisibilityScheduler.RegisteredControllerCount;
                    current.auraLeases = MeleeElementStatusAuraVisibilityScheduler.LeasedPresentationCount;
                    if (playerEnergy != null) { current.energy = playerEnergy.Amount; current.element = (int)playerEnergy.Element; }
                    if (blood != null)
                    {
                        current.bloodPlayed = blood.PlayedCount;
                        current.bloodRequests = blood.RequestedCount;
                        current.bloodPlayedDelta = Mathf.Max(0, blood.PlayedCount - previousBloodPlayed);
                        current.bloodRequestDelta = Mathf.Max(0, blood.RequestedCount - previousBloodRequested);
                        current.bloodVariation = blood.LastSweepVariation;
                        current.bloodVariationDelta = Mathf.Max(0, blood.SweepVariationPlayedCount - previousBloodVariations);
                        int variation = current.bloodVariation;
                        if (current.bloodVariationDelta > 0 && bloodCatalog != null && bloodCatalog.sweepVariations != null
                            && variation >= 0 && variation < bloodCatalog.sweepVariations.Length)
                        {
                            var graph = bloodCatalog.sweepVariations[variation]?.graph;
                            if (graph != null) current.bloodGraphId = graph.GetInstanceID();
                        }
                        previousBloodPlayed = blood.PlayedCount;
                        previousBloodRequested = blood.RequestedCount;
                        previousBloodVariations = blood.SweepVariationPlayedCount;
                    }
                    if (autosave != null)
                    {
                        current.saveWrites = autosave.SuccessfulWrites;
                        current.saveWriteDelta = Mathf.Max(0, autosave.SuccessfulWrites - previousSaveWrites);
                        previousSaveWrites = autosave.SuccessfulWrites;
                    }
                    AccountGameplaySession session = AccountGameplaySession.Current;
                    current.pendingSave = session != null && session.HasPendingSave;
                    }
                    ObserveAudio();
                    using (TimingMarker.Auto())
                    {
                    if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].frameStartTimestamp != 0
                        && timings[0].frameStartTimestamp != lastTimingStamp)
                    {
                        current.frameTimingStamp = timings[0].frameStartTimestamp;
                        lastTimingStamp = timings[0].frameStartTimestamp;
                        if (timings[0].gpuFrameTime > 0) current.gpuMs = timings[0].gpuFrameTime;
                        if (timings[0].cpuFrameTime > 0) current.cpuTimingMs = timings[0].cpuFrameTime;
                    }
                    FrameTimingManager.CaptureFrameTimings();
                    }
                }
            }
            catch (Exception error) { LastStatus = "관찰 오류: " + error.Message; Finish("observation_error"); }
            current.observerMs += Milliseconds(begin, Stopwatch.GetTimestamp());
        }

        void BindObjects()
        {
            if (mapGate == null) mapGate = UObject.FindFirstObjectByType<RunWorldGate>();
            audioBindings.Clear(); audioBindingSet.Clear(); audioStates.Clear();
            AudioSource[] sources = UObject.FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < sources.Length && i < MaximumBindings; i++) { audioBindings.Add(sources[i]); audioBindingSet.Add(sources[i]); }
            if (sources.Length > MaximumBindings) bindingLimitHits++;
            var nextBlood = UObject.FindFirstObjectByType<BloodHitVfxService>();
            if (blood != nextBlood) { blood = nextBlood; previousBloodPlayed = blood != null ? blood.PlayedCount : 0;
                previousBloodRequested = blood != null ? blood.RequestedCount : 0;
                previousBloodVariations = blood != null ? blood.SweepVariationPlayedCount : 0;
                bloodCatalog = blood != null ? BloodCatalogField?.GetValue(blood) as BloodHitCatalog : null; }
            var nextSave = UObject.FindFirstObjectByType<AccountAutosave>();
            if (autosave != nextSave) { autosave = nextSave; previousSaveWrites = autosave != null ? autosave.SuccessfulWrites : 0; }
            // Prefer the actor in PlayerContext without invoking its GetOrCreate bootstrap.
            PlayerActorRuntime actor = PlayerContext.Instance != null ? ActorField?.GetValue(PlayerContext.Instance) as PlayerActorRuntime : null;
            if (actor == null)
                foreach (var candidate in UObject.FindObjectsByType<PlayerActorRuntime>(FindObjectsSortMode.None))
                    if (candidate.ActorIndex == 0 && candidate.isActiveAndEnabled) { actor = candidate; break; }
            playerEnergy = actor != null ? actor.GetComponent<OverburstElementEnergy>() : null;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (active && rebindObjects) using (BindMarker.Auto()) BindObjects();
        }

        public static void ObserveHit(CombatHealth health, DamageInfo info, float damage, bool fatal)
        {
            if (!Running || instance.samplingComplete) return;
            try { instance.OnHit(health, info, damage, fatal); }
            catch (Exception error) { instance.Finish("hit_observation_error"); UDebug.LogException(error); }
        }

        public static void TrackAudioSource(AudioSource source)
        {
            if (!Running || instance.samplingComplete || source == null) return;
            var capture = instance;
            if (capture.audioBindingSet.Contains(source)) return;
            if (capture.audioBindings.Count >= MaximumBindings)
            {
                for (int i = capture.audioBindings.Count - 1; i >= 0; i--)
                    if (capture.audioBindings[i] == null) { capture.audioBindingSet.Remove(capture.audioBindings[i]); capture.audioBindings.RemoveAt(i); }
                if (capture.audioBindings.Count >= MaximumBindings) { capture.bindingLimitHits++; return; }
            }
            capture.audioBindingSet.Add(source); capture.audioBindings.Add(source);
        }

        public static void ObservePool(string pool, string reason, int used, int capacity, int cue = -1, UObject asset = null)
        {
            if (!Running || instance.samplingComplete) return;
            var capture = instance;
            using var diagnostic = PoolMarker.Auto();
            long began = Stopwatch.GetTimestamp();
            var key = (pool, reason);
            capture.poolTotals.TryGetValue(key, out int total);
            capture.poolTotals[key] = total + 1;
            int assetId = asset ? asset.GetInstanceID() : 0;
            if (assetId != 0 && capture.poolAssetNames.Count < 512 && !capture.poolAssetNames.ContainsKey(assetId))
                capture.poolAssetNames.Add(assetId, asset.name);
            if (capture.poolEventCount < capture.poolEvents.Length)
                capture.poolEvents[capture.poolEventCount++] = new PoolEvent { frame = Time.frameCount,
                    seconds = Milliseconds(capture.captureStarted, began) / 1000d, pool = pool, reason = reason,
                    used = used, capacity = capacity, cue = cue, asset = assetId };
            else capture.droppedPoolEvents++;
            capture.current.observerMs += Milliseconds(began, Stopwatch.GetTimestamp());
        }

        void OnHit(CombatHealth health, DamageInfo info, float damage, bool fatal)
        {
            if (!active || damage <= 0f) return;
            using var diagnostic = HitMarker.Auto();
            long begin = Stopwatch.GetTimestamp();
            if (trackedFrame == Time.frameCount)
            {
                current.hitCount++;
                if (info.isCritical) current.criticalCount++;
                if (info.isDamageOverTime) current.dotCount++;
            }
            else hitsBeforeFrameCallback++;
            if (hitCount < hits.Length)
                hits[hitCount++] = new Hit { frame = Time.frameCount,
                    seconds = Milliseconds(captureStarted, begin) / 1000d,
                    target = health.GetInstanceID(), source = info.source != null ? info.source.GetInstanceID() : 0,
                    damage = damage, critical = info.isCritical, dot = info.isDamageOverTime, fatal = fatal,
                    element = (int)info.element, attackKind = (int)info.playerAttackKind,
                    sequence = info.sourceAttackSequenceId, phase = info.sourceAttackPhaseIndex };
            else droppedHits++;
            current.observerMs += Milliseconds(begin, Stopwatch.GetTimestamp());
        }

        void ObserveAudio()
        {
            using var diagnostic = AudioMarker.Auto();
            foreach (AudioSource source in audioBindings)
            {
                if (source == null) continue;
                int id = source.GetInstanceID();
                AudioClip clip = source.clip;
                bool playing = clip != null && source.isPlaying;
                int clipId = clip != null ? clip.GetInstanceID() : 0;
                if (source.isPlaying) { current.audioPlaying++; if (source.isVirtual) current.audioVirtual++; }
                int position = playing ? source.timeSamples : -1;
                bool known = audioStates.TryGetValue(id, out AudioState old);
                bool restarted = playing && (!known || !old.playing || old.clip != clipId || position < old.position);
                if (restarted)
                {
                    current.audioStarts++;
                    if (soundCount < sounds.Length)
                        sounds[soundCount++] = new Sound { frame = Time.frameCount, source = id, clipId = clipId,
                            clip = clip.name, loadState = clip.loadState.ToString(), observedSamples = position,
                            newlyBound = !known, seconds = current.seconds };
                    else droppedSounds++;
                }
                if (known || audioStates.Count < MaximumBindings * 4)
                    audioStates[id] = new AudioState { clip = clipId, playing = playing, position = position };
                else bindingLimitHits++;
            }
        }

        bool IncludeMetric(string name)
        {
            if (includedMetricPrefixes == null || Array.IndexOf(EngineMetrics, name) >= 0) return true;
            foreach (string prefix in includedMetricPrefixes)
                if (name.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        void BindMetrics()
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            handles.Sort((a, b) => {
                var left = ProfilerRecorderHandle.GetDescription(a); var right = ProfilerRecorderHandle.GetDescription(b);
                int order = MetricPriority(left.Name).CompareTo(MetricPriority(right.Name));
                return order != 0 ? order : string.CompareOrdinal(left.Name, right.Name);
            });
            foreach (var handle in handles)
            {
                var description = ProfilerRecorderHandle.GetDescription(handle);
                if (!WantedMetric(description.Name) || !IncludeMetric(description.Name)) continue;
                if (metrics.Count >= MaximumMetrics) { omittedMetrics.Add(description.Name); continue; }
                var recorder = ProfilerRecorder.StartNew(description.Category, description.Name, 1);
                metrics.Add(new Metric { name = description.Name, unit = description.UnitType.ToString(),
                    timeNanoseconds = description.UnitType == ProfilerMarkerDataUnit.TimeNanoseconds,
                    category = description.Category.Name, recorder = recorder });
            }
        }

        void BeginBinaryProfiler()
        {
            previousProfilerEnabled = Profiler.enabled;
            previousBinaryEnabled = Profiler.enableBinaryLog;
            previousProfilerPath = Profiler.logFile;
            binaryOwned = true;
            Profiler.logFile = Path.Combine(folder, "profiler.raw");
            Profiler.enableBinaryLog = true;
            Profiler.enabled = true;
            // Unity normalizes logFile paths; retain its actual value for the ownership check.
            ownedProfilerPath = Profiler.logFile;
        }

        void Finish(string reason)
        {
            if (!active) return;
            active = false;
            stopReason = reason;
            Release();
            try { using (ExportMarker.Auto()) Export(); LastFolder = folder; LastStatus = "기록 저장 · " + frameCount + "프레임"; }
            catch (Exception error) { LastStatus = "저장 실패: " + error.Message; UDebug.LogWarning("[CombatStutterCapture] " + LastStatus); }
            if (instance == this) instance = null;
            Destroy(gameObject);
        }

        void Release()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            audioBindings.Clear(); audioBindingSet.Clear(); audioStates.Clear();
            foreach (Metric metric in metrics) if (metric.recorder.Valid) metric.recorder.Dispose();
            if (binaryOwned)
            {
                if (Profiler.logFile == ownedProfilerPath)
                {
                    Profiler.enabled = false;
                    Profiler.enableBinaryLog = previousBinaryEnabled;
                    Profiler.logFile = previousProfilerPath;
                    Profiler.enabled = previousProfilerEnabled;
                }
                else profilerOwnershipLost = true;
                binaryOwned = false;
            }
        }

        void OnApplicationQuit() { Finish("application_quit"); }
        void OnDestroy() { if (active) Finish("destroyed_or_play_stopped"); }
    }

    [DefaultExecutionOrder(32000)]
    public sealed class CombatStutterLateObserver : MonoBehaviour
    {
        void LateUpdate() { CombatStutterCapture.ObserveEndOfFrame(); }
    }
}
#endif
