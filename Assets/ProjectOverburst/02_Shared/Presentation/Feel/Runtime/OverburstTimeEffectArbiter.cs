using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum OverburstTimeEffectKind
{
    HitStop,
    PerfectEvade
}

[DefaultExecutionOrder(-950)]
public sealed class OverburstTimeEffectArbiter : MonoBehaviour
{
    private const float NormalTimeScaleThreshold = 0.999f;

    private sealed class RequestState
    {
        public UnityEngine.Object Owner;
        public int OwnerId;
        public OverburstTimeEffectKind Kind;
        public float Scale;
        public float EndUnscaledTime;
    }

    private static OverburstTimeEffectArbiter instance;
    private readonly List<RequestState> requests = new List<RequestState>(4);
    private float baselineTimeScale = 1f;
    private float baselineFixedDeltaTime = 0.02f;
    private float appliedTimeScale = 1f;
    private bool ownsTimeScale;

    public static bool IsActive => instance != null && instance.requests.Count > 0;
    public static int ActiveRequestCount => instance != null ? instance.requests.Count : 0;
    public static OverburstTimeEffectKind? ActiveKind => instance != null ? instance.ResolveWinner()?.Kind : null;
    public static bool OwnsTimeScale => instance != null && instance.ownsTimeScale;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
            return;

        GameObject root = new GameObject(nameof(OverburstTimeEffectArbiter));
        DontDestroyOnLoad(root);
        instance = root.AddComponent<OverburstTimeEffectArbiter>();
    }

    public static bool Request(
        UnityEngine.Object owner,
        OverburstTimeEffectKind kind,
        float scale,
        float duration)
    {
        if (owner == null || duration <= 0f)
            return false;

        if (instance == null)
            Bootstrap();
        return instance != null && instance.RequestInternal(owner, kind, scale, duration);
    }

    public static void ClearOwner(UnityEngine.Object owner)
    {
        if (instance == null || owner == null)
            return;

        int ownerId = owner.GetInstanceID();
        instance.requests.RemoveAll(request => request.OwnerId == ownerId);
        instance.ApplyWinnerOrRestore();
    }

    public static void ClearAll()
    {
        instance?.ClearAllInternal(true);
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
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void Update()
    {
        if (!ownsTimeScale)
            return;

        if (!Mathf.Approximately(Time.timeScale, appliedTimeScale))
        {
            requests.Clear();
            ownsTimeScale = false; // 외부 시스템이 값을 바꿨으면 그 값을 복구 대상으로 덮지 않는다.
            return;
        }

        float now = Time.unscaledTime;
        requests.RemoveAll(request => request.Owner == null || request.EndUnscaledTime <= now);
        ApplyWinnerOrRestore();
    }

    private void OnDisable()
    {
        ClearAllInternal(true);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        if (instance == this)
            instance = null;
    }

    private bool RequestInternal(
        UnityEngine.Object owner,
        OverburstTimeEffectKind kind,
        float scale,
        float duration)
    {
        scale = Mathf.Clamp(scale, 0.01f, 1f);
        duration = Mathf.Max(0f, duration);

        if (!ownsTimeScale)
        {
            if (Time.timeScale < NormalTimeScaleThreshold)
                return false; // Pause 등 중재자 밖의 시간 효과는 침범하지 않는다.

            baselineTimeScale = Time.timeScale;
            baselineFixedDeltaTime = Time.fixedDeltaTime;
            appliedTimeScale = baselineTimeScale;
            ownsTimeScale = true;
        }

        int ownerId = owner.GetInstanceID();
        RequestState request = requests.Find(entry => entry.OwnerId == ownerId && entry.Kind == kind);
        if (request == null)
        {
            request = new RequestState { Owner = owner, OwnerId = ownerId, Kind = kind };
            requests.Add(request);
        }

        request.Scale = request.EndUnscaledTime > Time.unscaledTime
            ? Mathf.Min(request.Scale, scale)
            : scale;
        request.EndUnscaledTime = Mathf.Max(request.EndUnscaledTime, Time.unscaledTime + duration);
        ApplyWinnerOrRestore();
        return true;
    }

    private void ApplyWinnerOrRestore()
    {
        RequestState winner = ResolveWinner();
        if (winner == null)
        {
            RestoreBaseline();
            return;
        }

        appliedTimeScale = Mathf.Min(baselineTimeScale, winner.Scale);
        Time.timeScale = appliedTimeScale;
        Time.fixedDeltaTime = baselineFixedDeltaTime
            * (appliedTimeScale / Mathf.Max(0.0001f, baselineTimeScale));
    }

    private RequestState ResolveWinner()
    {
        RequestState winner = null;
        for (int i = 0; i < requests.Count; i++)
        {
            RequestState candidate = requests[i];
            if (candidate == null || candidate.Owner == null)
                continue;

            if (winner == null
                || ResolvePriority(candidate.Kind) > ResolvePriority(winner.Kind)
                || (ResolvePriority(candidate.Kind) == ResolvePriority(winner.Kind)
                    && candidate.Scale < winner.Scale)
                || (ResolvePriority(candidate.Kind) == ResolvePriority(winner.Kind)
                    && Mathf.Approximately(candidate.Scale, winner.Scale)
                    && candidate.EndUnscaledTime > winner.EndUnscaledTime))
            {
                winner = candidate;
            }
        }
        return winner;
    }

    private static int ResolvePriority(OverburstTimeEffectKind kind)
    {
        return kind == OverburstTimeEffectKind.PerfectEvade ? 200 : 100;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ClearAllInternal(true);
    }

    private void ClearAllInternal(bool restore)
    {
        requests.Clear();
        if (restore)
            RestoreBaseline();
        else
            ownsTimeScale = false;
    }

    private void RestoreBaseline()
    {
        if (!ownsTimeScale)
            return;

        if (Mathf.Approximately(Time.timeScale, appliedTimeScale))
        {
            Time.timeScale = baselineTimeScale;
            Time.fixedDeltaTime = baselineFixedDeltaTime;
        }

        appliedTimeScale = baselineTimeScale;
        ownsTimeScale = false;
    }
}
