using System;
using System.Collections.Generic;
using MoreMountains.Feedbacks;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum OverburstFeelCue
{
    WeakHit,
    StrongHit,
    Death,
    Evade,
    Interaction,
    UiConfirm
}

[DefaultExecutionOrder(-850)]
[DisallowMultipleComponent]
public sealed class OverburstFeelFeedbackHub : MonoBehaviour
{
    private const string ResourcePath = "Feel/PF_OverburstFeelHub";

    private static OverburstFeelFeedbackHub instance;
    [SerializeField] private OverburstFeelEmitter[] emitters = Array.Empty<OverburstFeelEmitter>();
    private readonly Dictionary<OverburstFeelCue, List<OverburstFeelEmitter>> pools
        = new Dictionary<OverburstFeelCue, List<OverburstFeelEmitter>>();
    private readonly Dictionary<OverburstFeelCue, int> nextIndices
        = new Dictionary<OverburstFeelCue, int>();
    private readonly Dictionary<OverburstFeelCue, int> cuePlayCounts
        = new Dictionary<OverburstFeelCue, int>();

    public static OverburstFeelFeedbackHub Instance => instance;
    public static int InstanceCount => FindObjectsByType<OverburstFeelFeedbackHub>(FindObjectsSortMode.None).Length;
    public int TotalPlayCount { get; private set; }
    public IReadOnlyList<OverburstFeelEmitter> Emitters => emitters;

    public void Configure(OverburstFeelEmitter[] configuredEmitters)
    {
        emitters = configuredEmitters ?? Array.Empty<OverburstFeelEmitter>();
        RebuildPools();
    }

    public static bool Request(OverburstFeelCue cue, Vector3 worldPosition, float intensity = 1f)
    {
        if (!Application.isPlaying)
            return false;

        if (instance == null && !TryBootstrap())
            return false;
        return instance.PlayInternal(cue, worldPosition, intensity);
    }

    public static void StopAllActive()
    {
        instance?.StopAll();
    }

    public int GetPlayCount(OverburstFeelCue cue)
    {
        return cuePlayCounts.TryGetValue(cue, out int count) ? count : 0;
    }

    public int GetPoolSize(OverburstFeelCue cue)
    {
        return pools.TryGetValue(cue, out List<OverburstFeelEmitter> pool) ? pool.Count : 0;
    }

    public int CountLiveParticles()
    {
        int count = 0;
        for (int i = 0; i < emitters.Length; i++)
        {
            ParticleSystem particles = emitters[i] != null ? emitters[i].ParticleSystem : null;
            if (particles != null)
                count += particles.particleCount;
        }
        return count;
    }

    public void ResetCountersForValidation()
    {
        TotalPlayCount = 0;
        cuePlayCounts.Clear();
        foreach (OverburstFeelCue cue in Enum.GetValues(typeof(OverburstFeelCue)))
            cuePlayCounts[cue] = 0;
    }

    private static bool TryBootstrap()
    {
        OverburstFeelFeedbackHub prefab = Resources.Load<OverburstFeelFeedbackHub>(ResourcePath);
        if (prefab == null)
        {
            Debug.LogError($"[OverburstFeel] Missing Resources/{ResourcePath}.prefab");
            return false;
        }

        OverburstFeelFeedbackHub created = Instantiate(prefab);
        created.name = nameof(OverburstFeelFeedbackHub);
        instance = created;
        return true;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        RebuildPools();
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        StopAll();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        StopAll();
        if (instance == this)
            instance = null;
    }

    private bool PlayInternal(OverburstFeelCue cue, Vector3 worldPosition, float intensity)
    {
        if (!pools.TryGetValue(cue, out List<OverburstFeelEmitter> pool) || pool.Count == 0)
            return false;

        int index = nextIndices.TryGetValue(cue, out int next) ? next % pool.Count : 0;
        nextIndices[cue] = (index + 1) % pool.Count;
        pool[index].Play(worldPosition, intensity);
        TotalPlayCount++;
        cuePlayCounts[cue] = GetPlayCount(cue) + 1;
        return true;
    }

    private void RebuildPools()
    {
        pools.Clear();
        nextIndices.Clear();
        cuePlayCounts.Clear();
        foreach (OverburstFeelCue cue in Enum.GetValues(typeof(OverburstFeelCue)))
        {
            pools[cue] = new List<OverburstFeelEmitter>();
            nextIndices[cue] = 0;
            cuePlayCounts[cue] = 0;
        }

        OverburstFeelEmitter[] discovered = GetComponentsInChildren<OverburstFeelEmitter>(true);
        if (discovered.Length > 0)
            emitters = discovered; // 프리팹 자식 참조는 인스턴스 계층에서 다시 수집한다.
        for (int i = 0; i < emitters.Length; i++)
        {
            OverburstFeelEmitter emitter = emitters[i];
            if (emitter != null)
                pools[emitter.Cue].Add(emitter);
        }
    }

    private void StopAll()
    {
        if (emitters == null)
            return;
        for (int i = 0; i < emitters.Length; i++)
            emitters[i]?.StopAndReset();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        StopAll();
    }
}
