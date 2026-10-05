using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum OverburstTimeEffectKind
{
    HitStop,
    PerfectEvade,
    ParryHitStop,
    ParrySlow,
    HeavyFocus
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
        public float RecoverSeconds; // 끝나기 전 이 시간 동안 정상 속도로 서서히 돌아온다(0 = 끝에서 바로 복귀)
    }

    private static OverburstTimeEffectArbiter instance;
    private readonly List<RequestState> requests = new List<RequestState>(4);
    private float baselineTimeScale = 1f;
    private float baselineFixedDeltaTime = 0.02f;
    private float appliedTimeScale = 1f;
    private bool ownsTimeScale;

    // 2026-10-01 ESC 메뉴 일시정지: 시간 효과보다 위에서 timeScale을 0으로 둔다.
    // 멈춘 동안 히트스톱·패링 슬로우의 남은 시간은 줄지 않고, 풀면 그대로 이어진다.
    private bool paused;
    private float pausedAtUnscaled;
    private float pausedTimeScale = 1f;
    private float totalPausedUnscaled;

    public static bool IsActive => instance != null && instance.requests.Count > 0;
    public static int ActiveRequestCount => instance != null ? instance.requests.Count : 0;
    public static OverburstTimeEffectKind? ActiveKind => instance != null ? instance.ResolveWinner()?.Kind : null;
    public static bool OwnsTimeScale => instance != null && instance.ownsTimeScale;
    public static bool IsPaused => instance != null && instance.paused;
    // 앱 시작부터 메뉴로 멈춰 있던 실제 시간(초). OverburstGameClock이 쓴다.
    public static float TotalPausedUnscaled => instance == null ? 0f
        : instance.totalPausedUnscaled + (instance.paused ? Mathf.Max(0f, Time.unscaledTime - instance.pausedAtUnscaled) : 0f);
    public static event Action<bool> PauseChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => PauseChanged = null;

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
        return Request(owner, kind, scale, duration, 0f);
    }

    // recoverSeconds: 요청이 끝나기 전 이 시간 동안 scale에서 정상 속도로 부드럽게 돌아온다(패링 슬로우).
    public static bool Request(
        UnityEngine.Object owner,
        OverburstTimeEffectKind kind,
        float scale,
        float duration,
        float recoverSeconds)
    {
        if (owner == null || duration <= 0f)
            return false;

        if (instance == null)
            Bootstrap();
        return instance != null && instance.RequestInternal(owner, kind, scale, duration, recoverSeconds);
    }

    // Sampled presentation envelopes may rise as well as fall. Other request kinds retain their latch semantics.
    public static bool SetContinuous(UnityEngine.Object owner, OverburstTimeEffectKind kind, float scale, float leaseSeconds)
    {
        if (owner == null || kind != OverburstTimeEffectKind.HeavyFocus || leaseSeconds <= 0f) return false;
        if (instance == null) Bootstrap();
        if (instance == null || instance.paused) return false;
        if (!instance.ownsTimeScale && !instance.RequestInternal(owner, kind, scale, leaseSeconds, 0f)) return false;
        RequestState request = null;
        for (int i = 0; i < instance.requests.Count; i++)
            if (instance.requests[i].Owner == owner && instance.requests[i].Kind == kind) { request = instance.requests[i]; break; }
        if (request == null)
        {
            request = new RequestState { Owner = owner, OwnerId = owner.GetInstanceID(), Kind = kind };
            instance.requests.Add(request);
        }
        request.Scale = Mathf.Clamp(scale, .01f, 1f); request.RecoverSeconds = 0f;
        request.EndUnscaledTime = Time.unscaledTime + leaseSeconds;
        instance.ApplyWinnerOrRestore(); return true;
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

    public static void SetPaused(bool value)
    {
        if (instance == null)
            Bootstrap();
        instance?.SetPausedInternal(value);
    }

    private void SetPausedInternal(bool value)
    {
        if (paused == value)
            return;

        if (value)
        {
            paused = true;
            pausedAtUnscaled = Time.unscaledTime;
            pausedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }
        else
        {
            float pausedFor = Mathf.Max(0f, Time.unscaledTime - pausedAtUnscaled);
            totalPausedUnscaled += pausedFor;
            for (int i = 0; i < requests.Count; i++)
                requests[i].EndUnscaledTime += pausedFor; // 남은 히트스톱·슬로우를 멈춘 만큼 뒤로 민다.
            paused = false;
            Time.timeScale = pausedTimeScale;
        }

        var subscribers = PauseChanged;
        if (subscribers != null)
            foreach (Action<bool> callback in subscribers.GetInvocationList())
                try { callback(paused); } catch (Exception exception) { Debug.LogException(exception); }
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
        if (paused)
        {
            if (Time.timeScale != 0f)
                Time.timeScale = 0f; // 메뉴가 열린 동안은 다른 시스템이 바꾼 값도 되돌린다.
            return;
        }

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
        float duration,
        float recoverSeconds)
    {
        scale = Mathf.Clamp(scale, 0.01f, 1f);
        duration = Mathf.Max(0f, duration);
        recoverSeconds = Mathf.Clamp(recoverSeconds, 0f, duration);

        if (paused)
            return false; // 메뉴 멈춤 중에는 새 시간 효과를 받지 않는다.

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

        bool live = request.EndUnscaledTime > Time.unscaledTime;
        request.Scale = live ? Mathf.Min(request.Scale, scale) : scale;
        request.RecoverSeconds = live ? Mathf.Max(request.RecoverSeconds, recoverSeconds) : recoverSeconds;
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

        appliedTimeScale = Mathf.Min(baselineTimeScale, EffectiveScale(winner, Time.unscaledTime));
        Time.timeScale = appliedTimeScale;
        Time.fixedDeltaTime = baselineFixedDeltaTime
            * (appliedTimeScale / Mathf.Max(0.0001f, baselineTimeScale));
    }

    private static float EffectiveScale(RequestState request, float now)
    {
        if (request.RecoverSeconds <= 0f)
            return request.Scale;
        float remaining = request.EndUnscaledTime - now;
        if (remaining >= request.RecoverSeconds)
            return request.Scale;
        float t = 1f - Mathf.Clamp01(remaining / request.RecoverSeconds);
        return Mathf.Lerp(request.Scale, 1f, t * t * (3f - 2f * t));
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
        if (kind == OverburstTimeEffectKind.HeavyFocus) return 25;
        if (kind == OverburstTimeEffectKind.ParryHitStop) return 300;
        if (kind == OverburstTimeEffectKind.PerfectEvade) return 200;
        return kind == OverburstTimeEffectKind.ParrySlow ? 50 : 100;
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

        if (paused)
            pausedTimeScale = baselineTimeScale; // 멈춘 동안 효과가 지워지면 풀 때 원래 속도로 돌아간다.

        if (Mathf.Approximately(Time.timeScale, appliedTimeScale))
        {
            Time.timeScale = baselineTimeScale;
            Time.fixedDeltaTime = baselineFixedDeltaTime;
        }

        appliedTimeScale = baselineTimeScale;
        ownsTimeScale = false;
    }
}
