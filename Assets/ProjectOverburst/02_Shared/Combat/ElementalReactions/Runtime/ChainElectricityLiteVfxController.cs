using UnityEngine;

[DisallowMultipleComponent]
public sealed class ChainElectricityLiteVfxController : MonoBehaviour,
    ITransientVfxPlayback,
    ITransientVfxCompletion
{
    [SerializeField] private LineRenderer mainGlow;
    [SerializeField] private LineRenderer mainCore;
    [SerializeField] private LineRenderer[] branchGlows;
    [SerializeField] private LineRenderer[] branchCores;
    [SerializeField, Min(3)] private int mainPointCount = 33;
    [SerializeField, Min(3)] private int branchPointCount = 11;
    [SerializeField, Min(0.05f)] private float lifetime = 0.28f;
    [SerializeField, Min(0.01f)] private float shapeRefreshInterval = 0.04f;
    [SerializeField, Min(0f)] private float mainAmplitude = 0.075f;
    [SerializeField, Min(0f)] private float branchAmplitude = 0.11f;

    private Vector3[] mainPositions;
    private Vector3[][] branchPositions;
    private float[] branchGlowWidths;
    private float[] branchCoreWidths;
    private float mainGlowWidth;
    private float mainCoreWidth;
    private float elapsed;
    private float nextShapeRefresh;
    private float flickerPhase;
    private uint randomState;
    private bool alive;
    private bool initialized;

    public bool IsPlaybackAlive => alive;
    public int MainPointCount => mainPointCount;
    public int BranchPointCount => branchPointCount;
    public int BranchCount => Mathf.Min(
        branchGlows != null ? branchGlows.Length : 0,
        branchCores != null ? branchCores.Length : 0);
    public float Lifetime => lifetime;

    private void Awake()
    {
        InitializeOnce();
        StopAndClearVfx();
    }

    private void OnEnable()
    {
        if (!initialized)
            InitializeOnce();
    }

    private void OnDisable()
    {
        StopAndClearVfx();
    }

    private void Update()
    {
        if (!alive)
            return;

        elapsed += Time.deltaTime;
        if (elapsed >= lifetime)
        {
            alive = false;
            SetRenderersEnabled(false);
            return;
        }

        if (elapsed >= nextShapeRefresh)
        {
            RebuildShape();
            nextShapeRefresh += shapeRefreshInterval;
        }

        float normalizedTime = Mathf.Clamp01(elapsed / lifetime);
        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1f, normalizedTime));
        float flicker = 0.92f + Mathf.Sin(elapsed * 96f + flickerPhase) * 0.08f;
        ApplyWidths(Mathf.Max(0.015f, fade * flicker));
    }

    public void RestartVfx()
    {
        InitializeOnce();
        elapsed = 0f;
        nextShapeRefresh = shapeRefreshInterval;
        randomState = unchecked((uint)(GetInstanceID() * 747796405) ^ (uint)Time.frameCount * 2891336453u);
        if (randomState == 0u)
            randomState = 0x9E3779B9u;
        flickerPhase = NextFloat() * Mathf.PI * 2f;
        SetRenderersEnabled(true);
        RebuildShape();
        ApplyWidths(1f);
        alive = true;
    }

    public void StopAndClearVfx()
    {
        alive = false;
        elapsed = 0f;
        SetRenderersEnabled(false);
    }

    public void ConfigureForAuthoring(
        LineRenderer configuredMainGlow,
        LineRenderer configuredMainCore,
        LineRenderer[] configuredBranchGlows,
        LineRenderer[] configuredBranchCores,
        int configuredMainPointCount,
        int configuredBranchPointCount,
        float configuredLifetime,
        float configuredRefreshInterval,
        float configuredMainAmplitude,
        float configuredBranchAmplitude)
    {
        mainGlow = configuredMainGlow;
        mainCore = configuredMainCore;
        branchGlows = configuredBranchGlows;
        branchCores = configuredBranchCores;
        mainPointCount = Mathf.Max(3, configuredMainPointCount);
        branchPointCount = Mathf.Max(3, configuredBranchPointCount);
        lifetime = Mathf.Max(0.05f, configuredLifetime);
        shapeRefreshInterval = Mathf.Max(0.01f, configuredRefreshInterval);
        mainAmplitude = Mathf.Max(0f, configuredMainAmplitude);
        branchAmplitude = Mathf.Max(0f, configuredBranchAmplitude);
        initialized = false;
    }

    private void InitializeOnce()
    {
        if (initialized)
            return;

        mainPointCount = Mathf.Max(3, mainPointCount);
        branchPointCount = Mathf.Max(3, branchPointCount);
        lifetime = Mathf.Max(0.05f, lifetime);
        shapeRefreshInterval = Mathf.Max(0.01f, shapeRefreshInterval);
        mainPositions = new Vector3[mainPointCount];

        int branchCount = BranchCount;
        branchPositions = new Vector3[branchCount][];
        branchGlowWidths = new float[branchCount];
        branchCoreWidths = new float[branchCount];
        for (int i = 0; i < branchCount; i++)
        {
            branchPositions[i] = new Vector3[branchPointCount];
            branchGlowWidths[i] = branchGlows[i] != null ? branchGlows[i].widthMultiplier : 0f;
            branchCoreWidths[i] = branchCores[i] != null ? branchCores[i].widthMultiplier : 0f;
        }

        mainGlowWidth = mainGlow != null ? mainGlow.widthMultiplier : 0f;
        mainCoreWidth = mainCore != null ? mainCore.widthMultiplier : 0f;
        initialized = true;
    }

    private void RebuildShape()
    {
        float phaseX = NextFloat() * Mathf.PI * 2f;
        float phaseY = NextFloat() * Mathf.PI * 2f;
        for (int i = 0; i < mainPositions.Length; i++)
        {
            float t = i / (float)(mainPositions.Length - 1);
            float taper = Mathf.Sin(t * Mathf.PI);
            float coarseX = Mathf.Sin(t * 13.5f + phaseX) * 0.72f;
            float fineX = Mathf.Sin(t * 31f + phaseY) * 0.28f;
            float coarseY = Mathf.Sin(t * 16.5f + phaseY) * 0.68f;
            float fineY = Mathf.Sin(t * 37f + phaseX) * 0.32f;
            mainPositions[i] = new Vector3(
                (coarseX + fineX) * mainAmplitude * taper,
                (coarseY + fineY) * mainAmplitude * taper,
                t - 0.5f);
        }

        SetPositions(mainGlow, mainPositions);
        SetPositions(mainCore, mainPositions);

        for (int branchIndex = 0; branchIndex < branchPositions.Length; branchIndex++)
            RebuildBranch(branchIndex);
    }

    private void RebuildBranch(int branchIndex)
    {
        Vector3[] positions = branchPositions[branchIndex];
        float startT = Mathf.Clamp01(0.25f + branchIndex * 0.38f + NextRange(-0.08f, 0.08f));
        int startIndex = Mathf.Clamp(
            Mathf.RoundToInt(startT * (mainPositions.Length - 1)),
            1,
            mainPositions.Length - 2);
        Vector3 start = mainPositions[startIndex];
        float direction = NextFloat() < 0.2f ? -1f : 1f;
        float endZ = Mathf.Clamp(start.z + NextRange(0.11f, 0.22f) * direction, -0.48f, 0.48f);
        Vector2 radial = new Vector2(NextRange(-1f, 1f), NextRange(-1f, 1f));
        if (radial.sqrMagnitude < 0.1f)
            radial = Vector2.right;
        radial.Normalize();
        radial *= branchAmplitude * NextRange(0.72f, 1.15f);
        float phase = NextFloat() * Mathf.PI * 2f;

        for (int i = 0; i < positions.Length; i++)
        {
            float t = i / (float)(positions.Length - 1);
            float bend = Mathf.Sin(t * Mathf.PI) * branchAmplitude * 0.18f;
            float jag = Mathf.Sin(t * 24f + phase) * branchAmplitude * 0.12f * Mathf.Sin(t * Mathf.PI);
            positions[i] = new Vector3(
                start.x + radial.x * t + bend + jag,
                start.y + radial.y * t - bend + jag * 0.7f,
                Mathf.Lerp(start.z, endZ, t));
        }

        SetPositions(branchGlows[branchIndex], positions);
        SetPositions(branchCores[branchIndex], positions);
    }

    private void ApplyWidths(float multiplier)
    {
        if (mainGlow != null)
            mainGlow.widthMultiplier = mainGlowWidth * multiplier;
        if (mainCore != null)
            mainCore.widthMultiplier = mainCoreWidth * multiplier;

        for (int i = 0; i < BranchCount; i++)
        {
            if (branchGlows[i] != null)
                branchGlows[i].widthMultiplier = branchGlowWidths[i] * multiplier;
            if (branchCores[i] != null)
                branchCores[i].widthMultiplier = branchCoreWidths[i] * multiplier;
        }
    }

    private void SetRenderersEnabled(bool enabled)
    {
        SetEnabled(mainGlow, enabled);
        SetEnabled(mainCore, enabled);
        int branchCount = BranchCount;
        for (int i = 0; i < branchCount; i++)
        {
            SetEnabled(branchGlows[i], enabled);
            SetEnabled(branchCores[i], enabled);
        }
    }

    private static void SetPositions(LineRenderer renderer, Vector3[] positions)
    {
        if (renderer == null)
            return;
        renderer.positionCount = positions.Length;
        renderer.SetPositions(positions);
    }

    private static void SetEnabled(LineRenderer renderer, bool enabled)
    {
        if (renderer != null)
            renderer.enabled = enabled;
    }

    private float NextRange(float min, float max)
    {
        return Mathf.LerpUnclamped(min, max, NextFloat());
    }

    private float NextFloat()
    {
        randomState ^= randomState << 13;
        randomState ^= randomState >> 17;
        randomState ^= randomState << 5;
        return (randomState & 0x00FFFFFFu) / 16777216f;
    }
}
