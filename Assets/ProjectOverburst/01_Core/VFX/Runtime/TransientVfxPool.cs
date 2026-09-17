using System;
using System.Collections.Generic;
using UnityEngine;
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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Pools.Clear();
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
        TransientVfxReturnMode returnMode = TransientVfxReturnMode.FixedLifetime)
    {
        if (prefab == null || shuttingDown)
            return null;

        EnsureHost();
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
            returnMode);
        return instance;
    }

    public static float ResolveLifetime(GameObject prefab, float explicitLifetime)
    {
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
        if (Pools.TryGetValue(prefab, out Queue<GameObject> pool))
        {
            while (pool.Count > 0)
            {
                GameObject instance = pool.Dequeue();
                if (instance != null)
                    return instance;
            }
        }

        return Object.Instantiate(prefab);
    }

    private static void Release(GameObject instance, GameObject prefab, int poolCapacity)
    {
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
            Object.Destroy(instance);
            return;
        }

        pool.Enqueue(instance);
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
        if (TryGetCustomPlayback(instance, out ITransientVfxPlayback playback))
        {
            playback.RestartVfx();
            return;
        }

        RestartParticles(instance);
    }

    private static void StopAndClearParticles(GameObject instance)
    {
        ParticleSystem[] particleSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            if (particleSystems[i] != null)
                particleSystems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private static void StopAndClearPlayback(GameObject instance)
    {
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
        MonoBehaviour[] behaviours = instance.GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is ITransientVfxPlayback candidate)
            {
                playback = candidate;
                return true;
            }
        }

        playback = null;
        return false;
    }

    private static bool IsPlaybackAlive(GameObject instance)
    {
        if (instance == null || !instance.activeInHierarchy)
            return false;

        MonoBehaviour[] behaviours = instance.GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is ITransientVfxCompletion completion)
                return completion.IsPlaybackAlive;
        }

        ParticleSystem[] particleSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            if (particleSystems[i] != null && particleSystems[i].IsAlive(false))
                return true;
        }

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
            TransientVfxReturnMode returnMode)
        {
            float now = Time.time;
            float safetyLifetime = returnMode == TransientVfxReturnMode.NaturalParticleCompletion
                ? Mathf.Max(NaturalCompletionSafetyLifetime, lifetime * 4f)
                : Mathf.Max(MinimumLifetime, lifetime);
            activeLeases.Add(new ActiveLease(
                instance,
                prefab,
                now + MinimumLifetime,
                now + safetyLifetime,
                poolCapacity,
                returnMode));
        }

        private void Update()
        {
            float now = Time.time;
            for (int i = activeLeases.Count - 1; i >= 0; i--)
            {
                ActiveLease lease = activeLeases[i];
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

                activeLeases.RemoveAt(i);
                Release(lease.Instance, lease.Prefab, lease.PoolCapacity);
            }
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

        public ActiveLease(
            GameObject instance,
            GameObject prefab,
            float earliestReturnTime,
            float safetyReturnTime,
            int poolCapacity,
            TransientVfxReturnMode returnMode)
        {
            Instance = instance;
            Prefab = prefab;
            EarliestReturnTime = earliestReturnTime;
            SafetyReturnTime = safetyReturnTime;
            PoolCapacity = poolCapacity;
            ReturnMode = returnMode;
        }
    }
}
