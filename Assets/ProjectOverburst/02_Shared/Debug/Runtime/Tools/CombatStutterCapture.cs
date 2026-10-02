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
        static readonly ProfilerMarker SampleMarker = new ProfilerMarker("Overburst.Diagnostics.Stutter.Sample");
        static readonly ProfilerMarker ObserveMarker = new ProfilerMarker("Overburst.Diagnostics.Stutter.Observe");
        static readonly ProfilerMarker BindMarker = new ProfilerMarker("Overburst.Diagnostics.Stutter.Bind");
        static readonly FieldInfo ActorField = typeof(PlayerContext).GetField("actor", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo BloodCatalogField = typeof(BloodHitVfxService).GetField("catalog", BindingFlags.NonPublic | BindingFlags.Instance);
        readonly List<CombatHealth> healthBindings = new List<CombatHealth>(256);
        readonly List<AudioSource> audioBindings = new List<AudioSource>(128);
        readonly Dictionary<int, AudioState> audioStates = new Dictionary<int, AudioState>(128);
        readonly List<Metric> metrics = new List<Metric>(MaximumMetrics);
        Frame[] frames;
        long[] metricValues;
        byte[] metricAvailable;
        Hit[] hits;
        Sound[] sounds;
        int frameCount, hitCount, soundCount, droppedHits, droppedSounds, bindingLimitHits, hitsBeforeFrameCallback;
        Frame current;
        int trackedFrame = -1;
        long frameStarted, captureStarted;
        double duration, nextBind;
        float threshold;
        string folder, stopReason, previousProfilerPath;
        bool active, binaryOwned, binaryRequested, previousProfilerEnabled, previousBinaryEnabled, profilerOwnershipLost;
        BloodHitVfxService blood;
        BloodHitCatalog bloodCatalog;
        AccountAutosave autosave;
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
            bool binaryProfiler = false, string outputRoot = null)
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
                capture.duration = Mathf.Clamp(seconds, 10f, 300f);
                capture.threshold = Mathf.Clamp(hitchMs, 16f, 2000f);
                capture.binaryRequested = binaryProfiler;
                capture.frames = new Frame[MaximumFrames];
                capture.hits = new Hit[MaximumEvents];
                capture.sounds = new Sound[MaximumEvents];
                capture.BindMetrics();
                capture.metricValues = new long[MaximumFrames * capture.metrics.Count];
                capture.metricAvailable = new byte[capture.metricValues.Length];
                capture.BindObjects();
                if (binaryProfiler) capture.BeginBinaryProfiler();
                root.AddComponent<CombatStutterLateObserver>();
                // Setup allocations/imports/binding are outside the sampled interval.
                capture.captureStarted = Stopwatch.GetTimestamp();
                capture.nextBind = 1d;
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
            instance.Finish("manual_stop");
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
        }

        void BeginFrame()
        {
            if (!active || trackedFrame == Time.frameCount) return;
            long begin = Stopwatch.GetTimestamp();
            try
            {
                using (SampleMarker.Auto())
                {
                    if (trackedFrame >= 0) CompleteFrame(begin);
                    if (frameCount >= MaximumFrames || Milliseconds(captureStarted, begin) >= duration * 1000d)
                    {
                        Finish(frameCount >= MaximumFrames ? "frame_capacity" : "duration");
                        return;
                    }
                    trackedFrame = Time.frameCount;
                    frameStarted = begin;
                    current = new Frame { frame = trackedFrame, seconds = Milliseconds(captureStarted, begin) / 1000d,
                        timeScale = Time.timeScale, gpuMs = -1d, cpuTimingMs = -1d,
                        energy = -1f, element = -1, bloodVariation = -1, bloodGraphId = 0 };
                }
                current.observerMs += Milliseconds(begin, Stopwatch.GetTimestamp());
            }
            catch (Exception error) { LastStatus = "관찰 오류: " + error.Message; Finish("sampling_error"); }
        }

        void CompleteFrame(long now)
        {
            current.wallMs = Milliseconds(frameStarted, now);
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
                    double elapsed = Milliseconds(captureStarted, begin) / 1000d;
                    if (elapsed >= nextBind)
                    {
                        current.registryScan = true;
                        using (BindMarker.Auto()) BindObjects();
                        nextBind = elapsed + 1d;
                    }
                    current.endObserved = true;
                    current.unscaledDeltaMs = Time.unscaledDeltaTime * 1000d;
                    current.timeScale = Time.timeScale;
                    current.targetCount = CombatTargetRegistry.RegisteredCount;
                    current.healthBindings = healthBindings.Count;
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
                    ObserveAudio();
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
            catch (Exception error) { LastStatus = "관찰 오류: " + error.Message; Finish("observation_error"); }
            current.observerMs += Milliseconds(begin, Stopwatch.GetTimestamp());
        }

        void BindObjects()
        {
            CombatHealth[] healths = UObject.FindObjectsByType<CombatHealth>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (CombatHealth health in healths)
            {
                if (healthBindings.Contains(health)) continue;
                if (healthBindings.Count >= MaximumBindings) { bindingLimitHits++; break; }
                health.OnDamageResolved += OnHit;
                healthBindings.Add(health);
            }
            audioBindings.Clear();
            AudioSource[] sources = UObject.FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < sources.Length && i < MaximumBindings; i++) audioBindings.Add(sources[i]);
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

        void OnHit(CombatHealth health, DamageInfo info, float damage, bool fatal)
        {
            if (!active || damage <= 0f) return;
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
            foreach (AudioSource source in audioBindings)
            {
                if (source == null) continue;
                int id = source.GetInstanceID();
                AudioClip clip = source.clip;
                bool playing = clip != null && source.isPlaying;
                int clipId = clip != null ? clip.GetInstanceID() : 0;
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

        void BindMetrics()
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            foreach (var handle in handles)
            {
                var description = ProfilerRecorderHandle.GetDescription(handle);
                if (!WantedMetric(description.Name) || metrics.Count >= MaximumMetrics) continue;
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
        }

        void Finish(string reason)
        {
            if (!active) return;
            active = false;
            stopReason = reason;
            Release();
            try { Export(); LastFolder = folder; LastStatus = "기록 저장 · " + frameCount + "프레임"; }
            catch (Exception error) { LastStatus = "저장 실패: " + error.Message; UDebug.LogWarning("[CombatStutterCapture] " + LastStatus); }
            if (instance == this) instance = null;
            Destroy(gameObject);
        }

        void Release()
        {
            foreach (CombatHealth health in healthBindings) if (health != null) health.OnDamageResolved -= OnHit;
            healthBindings.Clear();
            foreach (Metric metric in metrics) if (metric.recorder.Valid) metric.recorder.Dispose();
            if (binaryOwned)
            {
                if (Profiler.logFile == Path.Combine(folder, "profiler.raw"))
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
