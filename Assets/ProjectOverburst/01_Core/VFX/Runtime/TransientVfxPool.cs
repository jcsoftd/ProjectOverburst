using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;
using Object = UnityEngine.Object;

public interface ITransientVfxPlayback
{
    void RestartVfx();
    void StopAndClearVfx();
}

public interface ITransientVfxCompletion
{
    bool IsPlaybackAlive { get; }
}

public enum TransientVfxReturnMode
{
    FixedLifetime,
    NaturalParticleCompletion
}

public static class TransientVfxReturnPolicy
{
    public static bool ShouldReturn(
        TransientVfxReturnMode mode,
        bool isPlaybackAlive,
        float now,
        float earliestReturnTime,
        float safetyReturnTime)
    {
        if (now >= safetyReturnTime)
            return true; // 비정상 무한 재생 안전망

        return mode == TransientVfxReturnMode.NaturalParticleCompletion
            && now >= earliestReturnTime
            && !isPlaybackAlive;
    }
}

public static class TransientVfxPool
{
    private const float MinimumLifetime = 0.1f;
    private const float LifetimePadding = 0.25f;
    private const float LoopingFallbackLifetime = 5f;
    private const float NaturalCompletionSafetyLifetime = 30f;

    private static readonly Dictionary<GameObject, Queue<GameObject>> Pools =
        new Dictionary<GameObject, Queue<GameObject>>();
    private static PoolHost host;
    private static bool shuttingDown;
    private static readonly Dictionary<GameObject, Counters> Diagnostics = new Dictionary<GameObject, Counters>();
    private static readonly ProfilerMarker CreateMarker = new ProfilerMarker("Overburst.TransientVfx.Create");

    private sealed class Counters
    {
        public long Created, Destroyed, Requests, Misses, Returns;
        public int Active, PeakActive;
        public int IdleValidatedFrame = -1;
    }

    public readonly struct PoolStatistics
    {
        public readonly long Created, Destroyed, Requests, Misses, Returns;
        public readonly int Active, PeakActive, Idle;
        public PoolStatistics(long created, long destroyed, long requests, long misses, long returns, int active, int peakActive, int idle)
        { Created = created; Destroyed = destroyed; Requests = requests; Misses = misses; Returns = returns; Active = active; PeakActive = peakActive; Idle = idle; }
    }

    public static PoolStatistics GetStatistics(GameObject prefab)
    {
        if (prefab == null || !Diagnostics.TryGetValue(prefab, out Counters c)) return default;
        return new PoolStatistics(c.Created, c.Destroyed, c.Requests, c.Misses, c.Returns,
            c.Active, c.PeakActive, Pools.TryGetValue(prefab, out Queue<GameObject> pool) ? pool.Count : 0);
    }

    // Called by loading/maintenance, never a new search through the scene or active leases.
    public static int GetValidIdleCount(GameObject prefab)
    {
        PruneDestroyedIdle(prefab, true);
        return prefab != null && Pools.TryGetValue(prefab, out var queue) ? queue.Count : 0;
    }

    public static void PruneDestroyedIdle(GameObject prefab, bool force = false)
    {
        if (prefab == null || !Pools.TryGetValue(prefab, out var queue)) return;
        var counters = GetCounters(prefab);
        if (!force && counters.IdleValidatedFrame == Time.frameCount) return;
        counters.IdleValidatedFrame = Time.frameCount;
        int count = queue.Count;
        for (int i = 0; i < count; i++)
        { var item = queue.Dequeue(); if (item != null) queue.Enqueue(item); }
    }

    private static Counters GetCounters(GameObject prefab)
    {
        if (!Diagnostics.TryGetValue(prefab, out Counters c)) Diagnostics.Add(prefab, c = new Counters());
        return c;
    }

    private static GameObject CreateInstance(GameObject prefab, Transform parent = null)
    {
        using (CreateMarker.Auto())
        {
            GameObject instance = Object.Instantiate(prefab, parent);
            GetCounters(prefab).Created++;
            return instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Pools.Clear();
        Diagnostics.Clear();
        host = null;
        shuttingDown = false;
    }

    public static GameObject Spawn(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        float explicitLifetime,
        int poolCapacity,
        Transform parent = null,
        Action<GameObject> prepareBeforeActivation = null,
        TransientVfxReturnMode returnMode = TransientVfxReturnMode.FixedLifetime,
        bool useUnscaledTime = false,
        int contentSceneHandle = 0)
    {
        using var costScope = ElementCombatCostMarkers.Pool_Spawn.Auto();
        if (prefab == null || shuttingDown)
            return null;

        EnsureHost();
        GetCounters(prefab).Requests++;
        GameObject instance = Acquire(prefab);
        if (instance == null)
            return null;

        Transform instanceTransform = instance.transform;
        instance.SetActive(false); // 주입 전 재생 차단
        instanceTransform.SetParent(parent, false);
        instanceTransform.SetPositionAndRotation(position, rotation);
        instanceTransform.localScale = prefab.transform.localScale;
        try
        {
            prepareBeforeActivation?.Invoke(instance);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Release(instance, prefab, Mathf.Max(1, poolCapacity));
            return null;
        }

        instance.SetActive(true);
        RestartPlayback(instance);

        float lifetime = ResolveLifetime(
            prefab,
            returnMode == TransientVfxReturnMode.NaturalParticleCompletion
                ? 0f
                : explicitLifetime);
        host.Schedule(
            instance,
            prefab,
            lifetime,
            Mathf.Max(1, poolCapacity),
            returnMode,
            useUnscaledTime,
            contentSceneHandle);
        return instance;
    }

    // Prepare during loading, before contact effects can exhaust the idle queue.
    // Warm instances through Awake/OnEnable once, then keep them inactive.
    public static int Prewarm(GameObject prefab, int count)
    {
        if (prefab == null || shuttingDown || count <= 0) return 0;
        EnsureHost();
        if (!Pools.TryGetValue(prefab, out Queue<GameObject> pool))
        {
            pool = new Queue<GameObject>(count);
            Pools.Add(prefab, pool);
        }
        // Destroyed scene objects must not count as ready instances.
        int existing = pool.Count;
        for (int i = 0; i < existing; i++)
        {
            GameObject instance = pool.Dequeue();
            if (instance != null) pool.Enqueue(instance);
        }
        int created = 0;
        while (pool.Count < count)
        {
            GameObject instance = CreateInstance(prefab, host.transform);
            StopAndClearPlayback(instance);
            instance.SetActive(false);
            pool.Enqueue(instance);
            created++;
        }
        return created;
    }

    // One indivisible preparation unit; callers own their frame/time budget.
    public static bool PrepareOne(GameObject prefab, int targetTotal)
    {
        if (prefab == null || shuttingDown || targetTotal <= 0) return false;
        PruneDestroyedIdle(prefab);
        var stats = GetStatistics(prefab);
        if (stats.Active + stats.Idle >= targetTotal) return false;
        EnsureHost();
        if (!Pools.TryGetValue(prefab, out var pool))
            Pools.Add(prefab, pool = new Queue<GameObject>());
        GameObject instance;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        long unitBegan = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
        var random = UnityEngine.Random.state;
        try
        {
            instance = CreateInstance(prefab, host.transform);
            StopAndClearPlayback(instance);
            instance.SetActive(false);
        }
        finally
        {
            UnityEngine.Random.state = random;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Overburst.DebugTools.CombatPreparationDiagnostics.Work(2, unitBegan);
#endif
        }
        pool.Enqueue(instance);
        return true;
    }

    // Never touches active leases. Destruction is amortized by the maintenance owner.
    public static bool TrimOne(GameObject prefab, int retainedTotal)
    {
        if (prefab == null || !Pools.TryGetValue(prefab, out var pool) || pool.Count == 0) return false;
        var stats = GetStatistics(prefab);
        if (stats.Active + stats.Idle <= Mathf.Max(0, retainedTotal)) return false;
        var instance = pool.Dequeue();
        if (instance != null) { GetCounters(prefab).Destroyed++; Object.Destroy(instance); }
        return true;
    }

    public static float ResolveLifetime(GameObject prefab, float explicitLifetime)
    {
        using var costScope = ElementCombatCostMarkers.Pool_ResolveLifetime.Auto();
        if (explicitLifetime > 0f)
            return Mathf.Max(MinimumLifetime, explicitLifetime);

        if (prefab == null)
            return MinimumLifetime;

        ParticleSystem[] particleSystems = prefab.GetComponentsInChildren<ParticleSystem>(true);
        float maximum = 0f;
        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem particleSystem = particleSystems[i];
            if (particleSystem == null)
                continue;

            ParticleSystem.MainModule main = particleSystem.main;
            float systemLifetime = ResolveCurveMaximum(main.startDelay)
                + Mathf.Max(0f, main.duration)
                + ResolveCurveMaximum(main.startLifetime);
            if (main.loop)
                systemLifetime = Mathf.Max(systemLifetime, LoopingFallbackLifetime);
            maximum = Mathf.Max(maximum, systemLifetime);
        }

        return Mathf.Max(MinimumLifetime, maximum + LifetimePadding);
    }

    private static GameObject Acquire(GameObject prefab)
    {
        using var costScope = ElementCombatCostMarkers.Pool_Acquire.Auto();
        if (Pools.TryGetValue(prefab, out Queue<GameObject> pool))
        {
            while (pool.Count > 0)
            {
                GameObject instance = pool.Dequeue();
                if (instance != null)
                    return instance;
            }
        }

        GetCounters(prefab).Misses++;
        return CreateInstance(prefab, host.transform);
    }

    private static void Release(GameObject instance, GameObject prefab, int poolCapacity)
    {
        using var costScope = ElementCombatCostMarkers.Pool_Release.Auto();
        if (instance == null)
            return;

        StopAndClearPlayback(instance);
        instance.SetActive(false);
        if (host != null)
            instance.transform.SetParent(host.transform, false);

        if (!Pools.TryGetValue(prefab, out Queue<GameObject> pool))
        {
            pool = new Queue<GameObject>();
            Pools.Add(prefab, pool);
        }

        if (pool.Count >= poolCapacity)
        {
            GetCounters(prefab).Destroyed++;
            Object.Destroy(instance);
            return;
        }

        pool.Enqueue(instance);
        GetCounters(prefab).Returns++;
    }

    private static void EnsureHost()
    {
        if (host != null)
            return;

        GameObject hostObject = new GameObject("TransientVfxPool");
        Object.DontDestroyOnLoad(hostObject);
        host = hostObject.AddComponent<PoolHost>();
    }

    private static void RestartParticles(GameObject instance)
    {
        using var costScope = ElementCombatCostMarkers.Pool_RestartParticles.Auto();
        ParticleSystem[] particleSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            if (particleSystems[i] == null)
                continue;

            particleSystems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            particleSystems[i].Play(false);
        }
    }

    private static void RestartPlayback(GameObject instance)
    {
        using var costScope = ElementCombatCostMarkers.Pool_RestartPlayback.Auto();
        if (TryGetCustomPlayback(instance, out ITransientVfxPlayback playback))
        {
            playback.RestartVfx();
            return;
        }

        RestartParticles(instance);
    }

    private static void StopAndClearParticles(GameObject instance)
    {
        using var costScope = ElementCombatCostMarkers.Pool_StopAndClearParticles.Auto();
        ParticleSystem[] particleSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            if (particleSystems[i] != null)
                particleSystems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private static void StopAndClearPlayback(GameObject instance)
    {
        using var costScope = ElementCombatCostMarkers.Pool_StopAndClearPlayback.Auto();
        if (TryGetCustomPlayback(instance, out ITransientVfxPlayback playback))
        {
            playback.StopAndClearVfx();
            return;
        }

        StopAndClearParticles(instance);
    }

    private static bool TryGetCustomPlayback(
        GameObject instance,
        out ITransientVfxPlayback playback)
    {
        // Keep existing root controllers and particle/mixed profiles authoritative.
        if (instance.TryGetComponent(out playback))
            return true;

        playback = null;
        if (instance.TryGetComponent(out ITransientVfxCompletion _)
            || instance.GetComponentInChildren<ParticleSystem>(true) != null)
            return false;

        ITransientVfxPlayback[] children = instance.GetComponentsInChildren<ITransientVfxPlayback>(true);
        if (children.Length != 1 || !(children[0] is ITransientVfxCompletion))
            return false;

        Component controller = children[0] as Component;
        if (controller == null || (controller is Behaviour behaviour && !behaviour.enabled))
            return false;

        // Root inactivity belongs to pool preparation; hidden child content stays hidden.
        for (Transform current = controller.transform; current != instance.transform; current = current.parent)
            if (current == null || !current.gameObject.activeSelf)
                return false;

        playback = children[0];
        return true;
    }

    private static bool IsPlaybackAlive(GameObject instance)
    {
        if (instance == null || !instance.activeInHierarchy)
            return false;

        if (instance.TryGetComponent(out ITransientVfxCompletion completion))
            return completion.IsPlaybackAlive;

        ParticleSystem[] particleSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            if (particleSystems[i] != null && particleSystems[i].IsAlive(false))
                return true;
        }

        if (particleSystems.Length == 0
            && TryGetCustomPlayback(instance, out ITransientVfxPlayback playback)
            && playback is ITransientVfxCompletion childCompletion)
            return childCompletion.IsPlaybackAlive;

        return false;
    }

    private static float ResolveCurveMaximum(ParticleSystem.MinMaxCurve curve)
    {
        switch (curve.mode)
        {
            case ParticleSystemCurveMode.Constant:
                return Mathf.Max(0f, curve.constant);
            case ParticleSystemCurveMode.TwoConstants:
                return Mathf.Max(0f, curve.constantMax);
            case ParticleSystemCurveMode.Curve:
                return Mathf.Max(0f, ResolveAnimationCurveMaximum(curve.curve) * curve.curveMultiplier);
            case ParticleSystemCurveMode.TwoCurves:
                return Mathf.Max(
                    0f,
                    Mathf.Max(
                        ResolveAnimationCurveMaximum(curve.curveMin),
                        ResolveAnimationCurveMaximum(curve.curveMax)) * curve.curveMultiplier);
            default:
                return 0f;
        }
    }

    private static float ResolveAnimationCurveMaximum(AnimationCurve curve)
    {
        if (curve == null || curve.length == 0)
            return 0f;

        float maximum = 0f;
        Keyframe[] keys = curve.keys;
        for (int i = 0; i < keys.Length; i++)
            maximum = Mathf.Max(maximum, keys[i].value);
        return maximum;
    }

    private sealed class PoolHost : MonoBehaviour
    {
        private readonly List<ActiveLease> activeLeases = new List<ActiveLease>(32);

        public void Schedule(
            GameObject instance,
            GameObject prefab,
            float lifetime,
            int poolCapacity,
            TransientVfxReturnMode returnMode,
            bool useUnscaledTime,
            int contentSceneHandle)
        {
            Counters counters = GetCounters(prefab);
            counters.Active++;
            counters.PeakActive = Mathf.Max(counters.PeakActive, counters.Active);
            float now = useUnscaledTime ? Time.unscaledTime : Time.time;
            float safetyLifetime = returnMode == TransientVfxReturnMode.NaturalParticleCompletion
                ? Mathf.Max(NaturalCompletionSafetyLifetime, lifetime * 4f)
                : Mathf.Max(MinimumLifetime, lifetime);
            activeLeases.Add(new ActiveLease(
                instance,
                prefab,
                now + MinimumLifetime,
                now + safetyLifetime,
                poolCapacity,
                returnMode,
                useUnscaledTime,
                contentSceneHandle));
        }

        private void Update()
        {
            for (int i = activeLeases.Count - 1; i >= 0; i--)
            {
                ActiveLease lease = activeLeases[i];
                float now = lease.UseUnscaledTime ? Time.unscaledTime : Time.time;
                bool isPlaybackAlive = lease.ReturnMode
                    == TransientVfxReturnMode.NaturalParticleCompletion
                    && IsPlaybackAlive(lease.Instance);
                if (lease.Instance != null
                    && !TransientVfxReturnPolicy.ShouldReturn(
                        lease.ReturnMode,
                        isPlaybackAlive,
                        now,
                        lease.EarliestReturnTime,
                        lease.SafetyReturnTime))
                {
                    continue;
                }

                ReturnLeaseAt(i);
            }
        }

        private void OnEnable() => UnityEngine.SceneManagement.SceneManager.sceneUnloaded += ReleaseScene;
        private void OnDisable() => UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= ReleaseScene;
        private void ReleaseScene(UnityEngine.SceneManagement.Scene scene)
        {
            for (int i = activeLeases.Count - 1; i >= 0; i--)
                if (activeLeases[i].ContentSceneHandle != 0 && activeLeases[i].ContentSceneHandle == scene.handle)
                    ReturnLeaseAt(i);
        }
        private void ReturnLeaseAt(int index)
        {
            var lease = activeLeases[index];
            activeLeases.RemoveAt(index);
            GetCounters(lease.Prefab).Active--;
            Release(lease.Instance, lease.Prefab, lease.PoolCapacity);
        }

        private void OnApplicationQuit()
        {
            shuttingDown = true;
        }

        private void OnDestroy()
        {
            if (host == this)
                host = null;
        }
    }

    private readonly struct ActiveLease
    {
        public readonly GameObject Instance;
        public readonly GameObject Prefab;
        public readonly float EarliestReturnTime;
        public readonly float SafetyReturnTime;
        public readonly int PoolCapacity;
        public readonly TransientVfxReturnMode ReturnMode;
        public readonly bool UseUnscaledTime;
        public readonly int ContentSceneHandle;

        public ActiveLease(
            GameObject instance,
            GameObject prefab,
            float earliestReturnTime,
            float safetyReturnTime,
            int poolCapacity,
            TransientVfxReturnMode returnMode,
            bool useUnscaledTime,
            int contentSceneHandle)
        {
            Instance = instance;
            Prefab = prefab;
            EarliestReturnTime = earliestReturnTime;
            SafetyReturnTime = safetyReturnTime;
            PoolCapacity = poolCapacity;
            ReturnMode = returnMode;
            UseUnscaledTime = useUnscaledTime;
            ContentSceneHandle = contentSceneHandle;
        }
    }
}
