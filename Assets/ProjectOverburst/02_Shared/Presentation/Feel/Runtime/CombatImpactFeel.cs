using System;
using System.Collections;
using System.Collections.Generic;
using MoreMountains.Feedbacks;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum CombatImpactSurface { Flesh, Shell, Venom, Ground }
public enum CombatImpactShape { Sweep, Downward, Thrust }

/// <summary>Bounded Feel players for local contact details and corpse landing, never damage or time ownership.</summary>
public sealed class CombatImpactFeel : MonoBehaviour
{
    [Serializable]
    public sealed class Slot
    {
        public CombatImpactSurface surface;
        public MMF_Player player;
        public ParticleSystem particles;
        public ParticleSystem criticalFlash;
        public AudioSource audio;
        [NonSerialized] public float availableAt;
    }

    [SerializeField] private Slot[] slots = Array.Empty<Slot>();
    private static CombatImpactFeel instance;
    private static bool preparing;
    private static int preparationGeneration;
    private static readonly ProfilerMarker LoadMarker = new ProfilerMarker("Overburst.Feel.Load");
    private static readonly ProfilerMarker CreateMarker = new ProfilerMarker("Overburst.Feel.Create");
    private static readonly ProfilerMarker PrepareMarker = new ProfilerMarker("Overburst.Feel.PrepareSlot");
    private static readonly ProfilerMarker AudioMarker = new ProfilerMarker("Overburst.Feel.PrepareAudio");
    private static readonly ProfilerMarker PlayInitializeMarker = new ProfilerMarker("Overburst.Feel.PlayInitialize");
    private bool cpuPrepared;
    public int PreparedSlotCount { get; private set; }
    public static bool IsCpuPrepared => instance != null && instance.cpuPrepared;
    public static string PreparationStatus { get; private set; } = "NotStarted";
    private float nextLandingAt;
    public int PlayedCount { get; private set; }
    public int DroppedCount { get; private set; }
    public int PreemptedCount { get; private set; }
    public int Capacity => slots.Length;
    public void Configure(Slot[] value) { slots = value; cpuPrepared = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPreparation()
    {
        preparing = false; preparationGeneration++;
        PreparationStatus = "NotStarted";
        if (instance != null) { instance.cpuPrepared = false; instance.PreparedSlotCount = 0; }
    }

    // Loading boundary only. No synthetic hit, sound, particle play, or per-hit reset removal.
    public static IEnumerator PrepareForGameplay(float maximumSeconds, Func<bool> cancelled = null)
    {
        if (!Application.isPlaying) yield break;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Overburst.DebugTools.CombatPreparationDiagnostics.Skip(0)) yield break;
        if (!Overburst.DebugTools.CombatEffectDiagnosticControls.Allowed(Overburst.DebugTools.CombatDiagnosticEffect.ImpactFeel))
        { PreparationStatus = "SkippedByDiagnostic"; yield break; }
#endif
        double begin = Time.realtimeSinceStartupAsDouble;
        double deadline = begin + Mathf.Max(0f, maximumSeconds);
        if (preparing)
        {
            while (preparing && Time.realtimeSinceStartupAsDouble < deadline && !(cancelled?.Invoke() ?? false)) yield return null;
            yield break; // Join once; do not restart an interrupted owner's preparation over a live hit.
        }
        if (IsCpuPrepared || preparing || Time.realtimeSinceStartupAsDouble >= deadline || (cancelled?.Invoke() ?? false)) yield break;
        // Scene/Domain reload disabled may retain a live pool. Search once at this loading boundary.
        if (instance == null) instance = FindFirstObjectByType<CombatImpactFeel>(FindObjectsInactive.Include);
        int token = ++preparationGeneration;
        preparing = true; PreparationStatus = "Preparing";
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Overburst.DebugTools.CombatPreparationDiagnostics.Begin(0);
#endif
        bool Current() => token == preparationGeneration && Application.isPlaying && !(cancelled?.Invoke() ?? false)
            && Time.realtimeSinceStartupAsDouble < deadline;
        try
        {
            if (instance == null)
            {
                ResourceRequest request = null;
                try { using (LoadMarker.Auto()) request = Resources.LoadAsync<CombatImpactFeel>("Feel/PF_CombatImpactFeel"); }
                catch (Exception error) { PreparationFailed(error.Message); }
                if (request == null) yield break;
                while (!request.isDone && Current()) yield return null;
                if (!Current()) yield break;
                if (instance == null && request.asset is CombatImpactFeel prefab)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    long createBegan = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                    var random = UnityEngine.Random.state;
                    try
                    {
                        using (CreateMarker.Auto()) instance = Instantiate(prefab);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        Overburst.DebugTools.CombatPreparationDiagnostics.Created(0);
#endif
                    }
                    catch (Exception error) { PreparationFailed(error.Message); }
                    finally
                    {
                        UnityEngine.Random.state = random;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        Overburst.DebugTools.CombatPreparationDiagnostics.Work(0, createBegan);
#endif
                    }
                }
                if (instance == null) { PreparationFailed("Missing Feel pool prefab."); yield break; }
            }
            var target = instance;
            var clips = new HashSet<AudioClip>();
            int operations = 0;
            long slice = System.Diagnostics.Stopwatch.GetTimestamp();
            target.PreparedSlotCount = 0;
            foreach (var slot in target.slots)
            {
                if (!Current() || instance != target) yield break;
                if (!target.PrepareSlot(slot, clips)) yield break;
                target.PreparedSlotCount++;
                operations++;
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - slice) * 1000d / System.Diagnostics.Stopwatch.Frequency;
                if (operations >= 8 || ms >= 2d)
                { yield return null; operations = 0; slice = System.Diagnostics.Stopwatch.GetTimestamp(); }
            }
            foreach (var clip in clips)
            {
                if (!Current() || instance != target) yield break;
                bool requested = false;
                try { using (AudioMarker.Auto()) requested = clip.loadState == AudioDataLoadState.Loaded || clip.LoadAudioData(); }
                catch (Exception error) { PreparationFailed(error.Message); }
                if (!requested) { PreparationFailed("Landing clip data could not be loaded."); yield break; }
                while (clip != null && clip.loadState == AudioDataLoadState.Loading && Current()) yield return null;
                if (!Current() || clip == null) yield break;
                if (clip.loadState != AudioDataLoadState.Loaded) { PreparationFailed("Landing clip data is not ready."); yield break; }
            }
            if (!Current() || instance != target) yield break;
            target.cpuPrepared = target.PreparedSlotCount == target.slots.Length && target.slots.Length > 0;
            PreparationStatus = target.cpuPrepared ? "CpuReady" : "Degraded";
        }
        finally
        {
            if (token == preparationGeneration)
            {
                preparing = false;
                if (!IsCpuPrepared && PreparationStatus == "Preparing") PreparationStatus = "Degraded";
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Overburst.DebugTools.CombatPreparationDiagnostics.End(0, PreparationStatus,
                    instance != null ? instance.PreparedSlotCount : 0, (Time.realtimeSinceStartupAsDouble - begin) * 1000d);
#endif
            }
        }
    }

    private bool PrepareSlot(Slot slot, HashSet<AudioClip> clips)
    {
        if (slot == null || slot.player == null || slot.particles == null)
        { PreparationFailed("Incomplete Feel slot binding."); return false; }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        long slotBegan = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
        var random = UnityEngine.Random.state;
        try
        {
            using (PrepareMarker.Auto())
            {
                slot.player.Initialization(true);
                slot.player.StopFeedbacks();
                slot.particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (slot.criticalFlash != null) slot.criticalFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (slot.audio != null) { slot.audio.Stop(); if (slot.audio.clip != null) clips.Add(slot.audio.clip); }
                foreach (var feedback in slot.player.FeedbacksList)
                    if (feedback is MMF_AudioSource audio && audio.RandomSfx != null)
                        foreach (var clip in audio.RandomSfx) if (clip != null) clips.Add(clip);
                slot.availableAt = 0;
            }
            return true;
        }
        catch (Exception error) { PreparationFailed(error.Message); return false; }
        finally
        {
            UnityEngine.Random.state = random;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Overburst.DebugTools.CombatPreparationDiagnostics.Work(0, slotBegan);
#endif
        }
    }

    private static void PreparationFailed(string reason)
    {
        if (PreparationStatus != "Degraded") Debug.LogWarning("[CombatImpactFeel] Preparation fallback: " + reason);
        PreparationStatus = "Degraded";
    }

    public static bool Play(CombatImpactSurface surface, CombatImpactShape shape, Vector3 point,
        Vector3 direction, bool critical = false, float intensity = 1f, bool lethal = false)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!Overburst.DebugTools.CombatEffectDiagnosticControls.Allowed(Overburst.DebugTools.CombatDiagnosticEffect.ImpactFeel)) return false;
#endif
        if (!Application.isPlaying) return false;
        // An actual hit supersedes loading preparation; a late continuation must never clear it.
        if (preparing)
        {
            preparing = false; preparationGeneration++; PreparationStatus = "InterruptedByPlay";
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Overburst.DebugTools.CombatPreparationDiagnostics.End(0, PreparationStatus, instance != null ? instance.PreparedSlotCount : 0, 0d);
#endif
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!IsCpuPrepared) Overburst.DebugTools.CombatPreparationDiagnostics.UnpreparedUse(0);
#endif
        if (instance == null)
        {
            var prefab = Resources.Load<CombatImpactFeel>("Feel/PF_CombatImpactFeel");
            if (prefab == null) return false;
            using (CreateMarker.Auto()) instance = Instantiate(prefab);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Overburst.DebugTools.CombatPreparationDiagnostics.Created(0);
#endif
        }
        return instance.PlayInternal(surface, shape, point, direction, critical, intensity, lethal);
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += SceneLoaded;
    }

    private bool PlayInternal(CombatImpactSurface surface, CombatImpactShape shape, Vector3 point,
        Vector3 direction, bool critical, float intensity, bool lethal)
    {
        // Simultaneous heavy corpses share one audible landing accent.
        if (surface == CombatImpactSurface.Ground && Time.unscaledTime < nextLandingAt) return false;
        Slot selected = null;
        foreach (var slot in slots)
        {
            if (slot.surface != surface || slot.player == null || slot.particles == null) continue;
            if (slot.availableAt <= Time.unscaledTime) { selected = slot; break; }
            // A kill must remain readable even if every ordinary contact slot is busy.
            if (lethal && (selected == null || slot.availableAt < selected.availableAt)) selected = slot;
        }
        if (selected == null)
        {
            DroppedCount++;
            return false;
        }
        if (selected.availableAt > Time.unscaledTime) PreemptedCount++;
        var selectedSlot = selected;
        selectedSlot.player.StopFeedbacks();
        selectedSlot.particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (selectedSlot.audio != null) selectedSlot.audio.Stop();
        direction.y = 0;
        if (direction.sqrMagnitude < .001f) direction = Vector3.forward;
        Vector3 emissionDirection = surface == CombatImpactSurface.Ground ? Vector3.up
            : shape == CombatImpactShape.Downward ? (direction.normalized + Vector3.down * .7f).normalized
            : direction.normalized;
        selectedSlot.player.transform.SetPositionAndRotation(point, Quaternion.LookRotation(emissionDirection));
        var particleShape = selectedSlot.particles.shape;
        particleShape.angle = surface == CombatImpactSurface.Ground ? 78 : shape == CombatImpactShape.Thrust ? 12 : 34;
        var main = selectedSlot.particles.main;
        main.startSizeMultiplier = surface == CombatImpactSurface.Ground ? .22f : lethal ? .10f : critical ? .085f : .055f;
        if (selectedSlot.criticalFlash != null)
        {
            var flash = selectedSlot.criticalFlash.main;
            flash.startRotation = shape == CombatImpactShape.Downward ? Mathf.PI * .5f : 0;
            foreach (var feedback in selectedSlot.player.FeedbacksList)
                if (feedback is MMF_Particles effect && effect.BoundParticleSystem == selectedSlot.criticalFlash)
                    effect.Active = lethal; // 2026-09-30: 치명타 추가 섬광은 끄고 처치 섬광만 남긴다.
        }
        using (PlayInitializeMarker.Auto()) selectedSlot.player.Initialization(true);
        selectedSlot.player.PlayFeedbacks(point, Mathf.Clamp(lethal ? intensity * 1.12f : intensity, .5f, 1.3f));
        selectedSlot.availableAt = Time.unscaledTime + (surface == CombatImpactSurface.Ground ? .7f : .3f);
        if (surface == CombatImpactSurface.Ground) nextLandingAt = Time.unscaledTime + .18f;
        PlayedCount++;
        return true;
    }

    private void SceneLoaded(Scene scene, LoadSceneMode mode) => StopAll();
    private void StopAll()
    {
        foreach (var slot in slots)
        {
            slot.player.StopFeedbacks();
            slot.particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (slot.audio != null) slot.audio.Stop();
            slot.availableAt = 0;
        }
        nextLandingAt = 0;
    }
    private void OnDisable() => StopAll();
    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        if (instance == this)
        { instance = null; preparing = false; preparationGeneration++; PreparationStatus = "NotStarted"; }
    }
}
