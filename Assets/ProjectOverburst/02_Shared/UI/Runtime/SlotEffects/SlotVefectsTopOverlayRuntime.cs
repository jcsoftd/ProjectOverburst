using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

internal sealed class SlotVefectsTopOverlayRuntime : MonoBehaviour
{
    private static readonly bool OverlayEnabled = true;
    private const string ManagerName = "Runtime_SlotVefectsTopOverlay";
    private const string CanvasName = "Runtime_SlotVefectsTopOverlayCanvas";
    private const string LegacyRawImageName = "Runtime_SlotVefectsTopOverlayRawImage";
    private const string LegacyWorldRootName = "Runtime_SlotVefectsTopOverlayWorld";
    private const string LegacyCameraName = "Runtime_SlotVefectsTopOverlayCamera";
    private const int SortingOrder = 12000;
    private const int FlipbookColumns = 8;
    private const int FlipbookRows = 8;
    private const int FlipbookFrameCount = 60;
    private const float FlipbookFramesPerSecond = 30f; // 60프레임 루프를 살짝 느리게 재생
    private const float RescanInterval = 0.35f;
    private const float DefaultOverlayOffset = 6f; // 슬롯 가장자리 정렬용 여백
    private const int MaxTargetCount = 96;
    private const int MaxRunnerCount = 4;
    private const int MaxTailLevel = 6;
    private const int MaxOverlayElementCount = MaxTargetCount * MaxRunnerCount * (MaxTailLevel + 1);
    private const float MinRuntimeOverlayOffset = -12f;
    private const float MaxRuntimeOverlayOffset = 32f;
    private const float RuntimeOverlayOffsetStep = 1f;
    private const float MinRuntimeThickness = 0.6f;
    private const float MaxRuntimeThickness = 1.25f;
    private const float RuntimeThicknessStep = 0.05f;
    private const float MinRuntimeFramesPerSecond = 12f;
    private const float MaxRuntimeFramesPerSecond = 72f;
    private const float RuntimeFramesPerSecondStep = 2f;
    private const ItemGrade MinimumVisibleGrade = ItemGrade.Artifact;
    private static readonly int AlphaCutoffId = Shader.PropertyToID("_AlphaCutoff");
    private static readonly int AlphaPowerId = Shader.PropertyToID("_AlphaPower");

    private static SlotVefectsTopOverlayRuntime instance;
    private static readonly SlotVefectsFlipbookDefinition[] FlipbookDefinitions =
    {
        new SlotVefectsFlipbookDefinition("Electric", "UI/SlotVefects/SlotVefects_Electric_Flipbook"),
        new SlotVefectsFlipbookDefinition("Fire", "UI/SlotVefects/SlotVefects_Fire_Flipbook"),
        new SlotVefectsFlipbookDefinition("Ice", "UI/SlotVefects/SlotVefects_Ice_Flipbook"),
        new SlotVefectsFlipbookDefinition("Water", "UI/SlotVefects/SlotVefects_Water_Flipbook"),
        new SlotVefectsFlipbookDefinition("Nature", "UI/SlotVefects/SlotVefects_Nature_Flipbook"),
        new SlotVefectsFlipbookDefinition("Earth", "UI/SlotVefects/SlotVefects_Earth_Flipbook"),
        new SlotVefectsFlipbookDefinition("Dark", "UI/SlotVefects/SlotVefects_Dark_Flipbook"),
        new SlotVefectsFlipbookDefinition("Void", "UI/SlotVefects/SlotVefects_Void_Flipbook"),
        new SlotVefectsFlipbookDefinition("Cosmos", "UI/SlotVefects/SlotVefects_Cosmos_Flipbook"),
        new SlotVefectsFlipbookDefinition("Sound", "UI/SlotVefects/SlotVefects_Sound_Flipbook")
    };

    private readonly List<RectTransform> targets = new List<RectTransform>();
    private readonly List<SlotVefectsFlipbookOverlayElement> overlays = new List<SlotVefectsFlipbookOverlayElement>();
    private readonly HashSet<string> warnedMissingFlipbooks = new HashSet<string>();
    private readonly Vector3[] worldCorners = new Vector3[4];

    private Canvas overlayCanvas;
    private RectTransform canvasRect;
    private Texture2D flipbookTexture;
    private SlotVefectsRuntimeMotionKind runtimeMotionKind = SlotVefectsRuntimeMotionKind.Clockwise;
    private SlotVefectsRuntimeControls debugControls;
    private float runtimeOverlayOffset = DefaultOverlayOffset;
    private float runtimeThickness = 1.2f;
    private float runtimeFramesPerSecond = FlipbookFramesPerSecond;
    private Material overlayMaterial;
    private float nextScanTime;
    private int runtimeFlipbookIndex = 3;
    private int runtimeRunnerCount = 2;
    private int runtimeTailLevel;
    private bool runtimeOverlayVisible = true;
    private bool runtimeFlipbookAvailable = true;
    private bool legacyCleanupDone;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (!IsAllowed())
            return;

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        EnsureRuntime();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SlotVefectsTopOverlayRuntime runtime = EnsureRuntime();
        if (runtime != null)
            runtime.RequestRescan();
    }

    private static SlotVefectsTopOverlayRuntime EnsureRuntime()
    {
        if (!OverlayEnabled)
            return null;

        if (instance != null)
            return instance;

        GameObject existing = GameObject.Find(ManagerName);
        if (existing != null)
        {
            SlotVefectsTopOverlayRuntime runtime = existing.GetComponent<SlotVefectsTopOverlayRuntime>();
            if (runtime == null)
                runtime = existing.AddComponent<SlotVefectsTopOverlayRuntime>();

            return runtime;
        }

        GameObject runtimeObject = new GameObject(ManagerName);
        DontDestroyOnLoad(runtimeObject);
        return runtimeObject.AddComponent<SlotVefectsTopOverlayRuntime>();
    }

    private static bool IsAllowed()
    {
        return OverlayEnabled;
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
        EnsurePresentation();
        RequestRescan();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;

        if (overlayMaterial != null)
            Destroy(overlayMaterial);
    }

    private void LateUpdate()
    {
        if (!IsAllowed())
            return;

        EnsurePresentation();
        if (!runtimeOverlayVisible || canvasRect == null || flipbookTexture == null)
        {
            HideUnusedOverlays(0);
            if (debugControls != null)
                debugControls.BringToFront();
            return;
        }

        if (Time.unscaledTime >= nextScanTime)
            RefreshTargets();

        ApplyRuntimeThicknessMaterial();

        int baseFrameIndex = Mathf.FloorToInt(Time.unscaledTime * runtimeFramesPerSecond);
        int visibleCount = 0;
        for (int i = 0; i < targets.Count && visibleCount < MaxOverlayElementCount; i++)
        {
            RectTransform target = targets[i];
            if (!TryResolveTarget(target, out Rect screenRect, out Color gradeTint))
                continue;

            visibleCount = RenderTargetOverlays(target, screenRect, gradeTint, baseFrameIndex, visibleCount);
        }

        HideUnusedOverlays(visibleCount);
        if (debugControls != null)
            debugControls.BringToFront();
    }

    private void RequestRescan()
    {
        nextScanTime = 0f;
    }

    private void RefreshTargets()
    {
        targets.Clear();
        nextScanTime = Time.unscaledTime + RescanInterval;

        SlotGradeEffect[] effects = Object.FindObjectsByType<SlotGradeEffect>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < effects.Length && targets.Count < MaxTargetCount; i++)
            AddTarget(effects[i] != null ? effects[i].transform as RectTransform : null);
    }

    private int RenderTargetOverlays(RectTransform target, Rect screenRect, Color gradeTint, int baseFrameIndex, int startIndex)
    {
        int visibleCount = startIndex;
        int runnerCount = Mathf.Clamp(runtimeRunnerCount, 1, MaxRunnerCount);
        int tailLevel = Mathf.Clamp(runtimeTailLevel, 0, MaxTailLevel);
        int tailFrameStep = ResolveTailFrameStep(tailLevel);

        for (int runner = 0; runner < runnerCount && visibleCount < MaxOverlayElementCount; runner++)
        {
            for (int tail = tailLevel; tail >= 0 && visibleCount < MaxOverlayElementCount; tail--)
            {
                SlotVefectsFlipbookOverlayElement overlay = EnsureOverlay(visibleCount);
                overlay.SetMaterial(overlayMaterial);
                overlay.BindTo(target);
                overlay.SetRect(target, ApplyRuntimeMotionScale(screenRect, runner, tail));
                overlay.SetFrame(
                    flipbookTexture,
                    ResolveRuntimeFrame(baseFrameIndex, runner, runnerCount, tail, tailFrameStep),
                    FlipbookColumns,
                    FlipbookRows,
                    ResolveRuntimeTint(gradeTint, runner, tail));
                overlay.Show();
                visibleCount++;
            }
        }

        return visibleCount;
    }

    private Rect ApplyRuntimeMotionScale(Rect screenRect, int runner, int tail)
    {
        float scale = 1f;
        if (runtimeMotionKind == SlotVefectsRuntimeMotionKind.Pulse)
        {
            float pulse = Mathf.Sin(Time.unscaledTime * 4f + runner * 1.7f - tail * 0.25f) * 0.5f + 0.5f;
            scale *= Mathf.Lerp(0.98f, 1.045f, pulse);
        }

        if (Mathf.Approximately(scale, 1f))
            return screenRect;

        Vector2 center = screenRect.center;
        Vector2 size = screenRect.size * scale;
        return new Rect(center - size * 0.5f, size);
    }

    private int ResolveRuntimeFrame(int baseFrameIndex, int runner, int runnerCount, int tail, int tailFrameStep)
    {
        int frame = baseFrameIndex + Mathf.RoundToInt(FlipbookFrameCount * runner / (float)Mathf.Max(1, runnerCount));
        frame -= tail * tailFrameStep;

        switch (runtimeMotionKind)
        {
            case SlotVefectsRuntimeMotionKind.CounterClockwise:
                return WrapFrame(-frame);
            case SlotVefectsRuntimeMotionKind.PingPong:
                return ResolvePingPongFrame(frame);
            default:
                return WrapFrame(frame);
        }
    }

    private Color ResolveRuntimeTint(Color gradeTint, int runner, int tail)
    {
        Color tint = gradeTint;
        if (tail > 0)
            tint.a *= Mathf.Pow(0.54f, tail);

        if (runtimeMotionKind == SlotVefectsRuntimeMotionKind.Pulse)
        {
            float pulse = Mathf.Sin(Time.unscaledTime * 4.6f + runner * 1.3f - tail * 0.4f) * 0.5f + 0.5f;
            tint.a *= Mathf.Lerp(0.72f, 1f, pulse);
        }

        return tint;
    }

    private static int ResolveTailFrameStep(int tailLevel)
    {
        if (tailLevel <= 0)
            return 1;

        return Mathf.RoundToInt(Mathf.Lerp(2f, 6f, tailLevel / (float)MaxTailLevel));
    }

    private static int WrapFrame(int frame)
    {
        int wrapped = frame % FlipbookFrameCount;
        return wrapped < 0 ? wrapped + FlipbookFrameCount : wrapped;
    }

    private static int ResolvePingPongFrame(int frame)
    {
        int lastFrame = FlipbookFrameCount - 1;
        int period = lastFrame * 2;
        int wrapped = frame % period;
        if (wrapped < 0)
            wrapped += period;

        return wrapped <= lastFrame ? wrapped : period - wrapped;
    }

    private void AddTarget(RectTransform target)
    {
        if (target == null || targets.Contains(target))
            return;

        targets.Add(target);
    }

    private bool TryResolveTarget(RectTransform candidate, out Rect screenRect, out Color gradeTint)
    {
        screenRect = default;
        gradeTint = Color.white;

        if (candidate == null || !candidate.gameObject.activeInHierarchy)
            return false;

        if (candidate.rect.width <= 1f || candidate.rect.height <= 1f)
            return false;

        SlotUI slot = candidate.GetComponent<SlotUI>();
        if (slot != null && slot.DisplayItem == null)
            return false;

        SlotGradeEffect effect = candidate.GetComponent<SlotGradeEffect>();
        if (effect == null)
            return false;

        if (!effect.TryGetCurrentGrade(out ItemGrade grade) || grade < MinimumVisibleGrade)
            return false;

        Color overlayColor = effect.GetOverlayColor();
        if (overlayColor.a <= 0.001f)
            return false;

        gradeTint = ResolveGradeTint(overlayColor);
        return TryBuildScreenRect(candidate, out screenRect);
    }

    private static Color ResolveGradeTint(Color overlayColor)
    {
        Color tint = overlayColor;
        if (Mathf.Max(tint.r, tint.g, tint.b) < 0.08f)
            tint = Color.white;

        tint.a = 0.96f;
        return tint;
    }

    private bool TryBuildScreenRect(RectTransform targetRect, out Rect screenRect)
    {
        screenRect = default;
        targetRect.GetWorldCorners(worldCorners);

        Canvas targetCanvas = targetRect.GetComponentInParent<Canvas>();
        Camera camera = targetCanvas != null && targetCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? targetCanvas.worldCamera : null;

        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, worldCorners[0]);
        Vector2 max = min;

        for (int i = 1; i < worldCorners.Length; i++)
        {
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(camera, worldCorners[i]);
            min = Vector2.Min(min, screenPoint);
            max = Vector2.Max(max, screenPoint);
        }

        if (max.x < 0f || max.y < 0f || min.x > Screen.width || min.y > Screen.height)
            return false;

        screenRect = Rect.MinMaxRect(
            min.x - runtimeOverlayOffset,
            min.y - runtimeOverlayOffset,
            max.x + runtimeOverlayOffset,
            max.y + runtimeOverlayOffset);
        return screenRect.width > 1f && screenRect.height > 1f;
    }

    private void EnsurePresentation()
    {
        EnsureCanvas();
        EnsureOverlayMaterial();
        CleanupLegacyRuntimeObjects();
        ResolveFlipbookTexture();
        EnsureDebugControls();
    }

    private void EnsureOverlayMaterial()
    {
        if (overlayMaterial != null)
            return;

        Shader shader = Resources.Load<Shader>("Shaders/SlotVefectsAlphaThickness");
        if (shader == null)
            shader = Shader.Find("OVERBURST/UI/SlotVefectsAlphaThickness");
        if (shader == null)
            return;

        overlayMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        ApplyRuntimeThicknessMaterial();
    }

    private void ApplyRuntimeThicknessMaterial()
    {
        if (overlayMaterial == null)
            return;

        float cutoff;
        float power;
        if (runtimeThickness < 1f)
        {
            float thinT = Mathf.InverseLerp(1f, MinRuntimeThickness, runtimeThickness);
            cutoff = Mathf.Lerp(0.06f, 0.24f, thinT);
            power = Mathf.Lerp(1f, 1.46f, thinT);
        }
        else
        {
            float thickT = Mathf.InverseLerp(1f, MaxRuntimeThickness, runtimeThickness);
            cutoff = Mathf.Lerp(0.06f, 0.005f, thickT);
            power = Mathf.Lerp(1f, 0.72f, thickT);
        }

        overlayMaterial.SetFloat(AlphaCutoffId, cutoff);
        overlayMaterial.SetFloat(AlphaPowerId, power);
    }

    private void EnsureDebugControls()
    {
        if (!SlotVefectsRuntimeControls.IsAllowed())
            return;

        RectTransform controlRoot = SlotEffectTuningControlsRegistry.GetRoot();
        if (controlRoot == null)
            return;

        if (debugControls == null || !debugControls.IsValid)
            debugControls = SlotVefectsRuntimeControls.CreateOrReuse(controlRoot, this);

        if (debugControls != null)
            debugControls.RefreshVisual();
    }

    private void EnsureGraphicRaycaster()
    {
        if (overlayCanvas == null)
            return;

        if (overlayCanvas.GetComponent<GraphicRaycaster>() == null)
            overlayCanvas.gameObject.AddComponent<GraphicRaycaster>();
    }

    private void CleanupLegacyRuntimeObjects()
    {
        if (legacyCleanupDone)
            return;

        legacyCleanupDone = true;

        if (canvasRect != null)
        {
            Transform legacyImage = canvasRect.Find(LegacyRawImageName);
            if (legacyImage != null)
                Destroy(legacyImage.gameObject);
        }

        DestroyLegacyObject(LegacyWorldRootName);
        DestroyLegacyObject(LegacyCameraName);
    }

    private static void DestroyLegacyObject(string objectName)
    {
        GameObject legacyObject = GameObject.Find(objectName);
        if (legacyObject != null)
            Destroy(legacyObject);
    }

    private void EnsureCanvas()
    {
        if (canvasRect != null)
            return;

        GameObject existing = GameObject.Find(CanvasName);
        if (existing != null)
        {
            CacheCanvas(existing);
            return;
        }

        GameObject canvasObject = new GameObject(CanvasName, typeof(Canvas), typeof(CanvasScaler));
        DontDestroyOnLoad(canvasObject);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = SortingOrder;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;
        scaler.referencePixelsPerUnit = 100f;

        CacheCanvas(canvasObject);
    }

    private void CacheCanvas(GameObject canvasObject)
    {
        overlayCanvas = canvasObject.GetComponent<Canvas>();
        if (overlayCanvas == null)
            overlayCanvas = canvasObject.AddComponent<Canvas>();

        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.overrideSorting = true;
        overlayCanvas.sortingOrder = SortingOrder;
        canvasRect = canvasObject.transform as RectTransform;
    }

    private void ResolveFlipbookTexture()
    {
        if (flipbookTexture != null)
            return;

        SlotVefectsFlipbookDefinition definition = GetCurrentFlipbookDefinition();
        flipbookTexture = Resources.Load<Texture2D>(definition.ResourcePath);
        runtimeFlipbookAvailable = flipbookTexture != null;
        if (flipbookTexture != null)
            return;

        if (warnedMissingFlipbooks.Add(definition.ResourcePath))
        {
            Debug.LogWarning("[SlotVefectsTopOverlay] baked flipbook을 찾지 못해 기본 Electric 시트로 표시합니다: Resources/" + definition.ResourcePath);
        }

        if (FlipbookDefinitions.Length > 0)
            flipbookTexture = Resources.Load<Texture2D>(FlipbookDefinitions[0].ResourcePath);
    }

    private SlotVefectsFlipbookDefinition GetCurrentFlipbookDefinition()
    {
        return GetFlipbookDefinition(runtimeFlipbookIndex);
    }

    private static SlotVefectsFlipbookDefinition GetFlipbookDefinition(int index)
    {
        if (FlipbookDefinitions.Length == 0)
            return new SlotVefectsFlipbookDefinition("None", string.Empty);

        int wrapped = index % FlipbookDefinitions.Length;
        if (wrapped < 0)
            wrapped += FlipbookDefinitions.Length;

        return FlipbookDefinitions[wrapped];
    }

    private SlotVefectsFlipbookOverlayElement EnsureOverlay(int index)
    {
        while (overlays.Count <= index)
            overlays.Add(null);

        if (overlays[index] == null || !overlays[index].IsValid)
        {
            GameObject overlayObject = new GameObject("SlotVefectsFlipbookOverlay_" + index, typeof(RectTransform), typeof(RawImage));
            overlayObject.transform.SetParent(canvasRect, false);

            RawImage image = overlayObject.GetComponent<RawImage>();
            image.raycastTarget = false;
            image.color = Color.white;

            overlays[index] = new SlotVefectsFlipbookOverlayElement(overlayObject, image);
        }

        return overlays[index];
    }

    private void HideUnusedOverlays(int startIndex)
    {
        for (int i = startIndex; i < overlays.Count; i++)
        {
            if (overlays[i] != null)
                overlays[i].Hide();
        }
    }

    internal string RuntimeMotionKindLabel
    {
        get
        {
            switch (runtimeMotionKind)
            {
                case SlotVefectsRuntimeMotionKind.CounterClockwise:
                    return "역방향";
                case SlotVefectsRuntimeMotionKind.PingPong:
                    return "왕복";
                case SlotVefectsRuntimeMotionKind.Pulse:
                    return "펄스";
                default:
                    return "정방향";
            }
        }
    }

    internal float RuntimeThickness
    {
        get { return runtimeThickness; }
    }

    internal float RuntimeOverlayOffset
    {
        get { return runtimeOverlayOffset; }
    }

    internal int RuntimeRunnerCount
    {
        get { return runtimeRunnerCount; }
    }

    internal int RuntimeTailLevel
    {
        get { return runtimeTailLevel; }
    }

    internal float RuntimeFramesPerSecond
    {
        get { return runtimeFramesPerSecond; }
    }

    internal bool RuntimeOverlayVisible
    {
        get { return runtimeOverlayVisible; }
    }

    internal string RuntimeVefectsKindLabel
    {
        get { return GetCurrentFlipbookDefinition().Label; }
    }

    internal bool RuntimeVefectsKindAvailable
    {
        get { return runtimeFlipbookAvailable; }
    }

    internal void ToggleRuntimeOverlayVisible()
    {
        runtimeOverlayVisible = !runtimeOverlayVisible;
        if (!runtimeOverlayVisible)
            HideUnusedOverlays(0);

        debugControls?.RefreshVisual();
    }

    internal void CycleRuntimeMotionKind()
    {
        int next = ((int)runtimeMotionKind + 1) % System.Enum.GetValues(typeof(SlotVefectsRuntimeMotionKind)).Length;
        runtimeMotionKind = (SlotVefectsRuntimeMotionKind)next;
        debugControls?.RefreshVisual();
    }

    internal void AdjustRuntimeThickness(float delta)
    {
        runtimeThickness = Mathf.Clamp(runtimeThickness + delta, MinRuntimeThickness, MaxRuntimeThickness);
        ApplyRuntimeThicknessMaterial();
        debugControls?.RefreshVisual();
    }

    internal void AdjustRuntimeOverlayOffset(float delta)
    {
        runtimeOverlayOffset = Mathf.Clamp(runtimeOverlayOffset + delta, MinRuntimeOverlayOffset, MaxRuntimeOverlayOffset);
        debugControls?.RefreshVisual();
    }

    internal void IncrementRuntimeRunnerCount()
    {
        runtimeRunnerCount++;
        if (runtimeRunnerCount > MaxRunnerCount)
            runtimeRunnerCount = 1;

        debugControls?.RefreshVisual();
    }

    internal void AdjustRuntimeTailLevel(int delta)
    {
        runtimeTailLevel = Mathf.Clamp(runtimeTailLevel + delta, 0, MaxTailLevel);
        debugControls?.RefreshVisual();
    }

    internal void AdjustRuntimeFramesPerSecond(float delta)
    {
        runtimeFramesPerSecond = Mathf.Clamp(runtimeFramesPerSecond + delta, MinRuntimeFramesPerSecond, MaxRuntimeFramesPerSecond);
        debugControls?.RefreshVisual();
    }

    internal void CycleRuntimeVefectsKind()
    {
        if (FlipbookDefinitions.Length == 0)
            return;

        int startIndex = runtimeFlipbookIndex;
        for (int i = 1; i <= FlipbookDefinitions.Length; i++)
        {
            int candidateIndex = (startIndex + i) % FlipbookDefinitions.Length;
            SlotVefectsFlipbookDefinition definition = GetFlipbookDefinition(candidateIndex);
            Texture2D candidateTexture = Resources.Load<Texture2D>(definition.ResourcePath);
            if (candidateTexture == null)
                continue;

            runtimeFlipbookIndex = candidateIndex;
            flipbookTexture = candidateTexture;
            runtimeFlipbookAvailable = true;
            break;
        }

        debugControls?.RefreshVisual();
    }

    internal static float ThicknessStep
    {
        get { return RuntimeThicknessStep; }
    }

    internal static float OverlayOffsetStep
    {
        get { return RuntimeOverlayOffsetStep; }
    }

    internal static float FramesPerSecondStep
    {
        get { return RuntimeFramesPerSecondStep; }
    }

    internal static int TailLevelMax
    {
        get { return MaxTailLevel; }
    }
}

internal enum SlotVefectsRuntimeMotionKind
{
    Clockwise,
    CounterClockwise,
    PingPong,
    Pulse
}

internal readonly struct SlotVefectsFlipbookDefinition
{
    public readonly string Label;
    public readonly string ResourcePath;

    public SlotVefectsFlipbookDefinition(string label, string resourcePath)
    {
        Label = label;
        ResourcePath = resourcePath;
    }
}

internal sealed class SlotVefectsFlipbookOverlayElement
{
    private readonly GameObject gameObject;
    private readonly RectTransform rectTransform;
    private readonly RawImage image;
    public bool IsValid => gameObject != null;

    public SlotVefectsFlipbookOverlayElement(GameObject gameObject, RawImage image)
    {
        this.gameObject = gameObject;
        this.image = image;
        rectTransform = gameObject.transform as RectTransform;
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
    }

    public void BindTo(RectTransform target)
    {
        if (rectTransform.parent != target)
            rectTransform.SetParent(target, false);
        if (rectTransform.GetSiblingIndex() != target.childCount - 1)
            rectTransform.SetAsLastSibling();
    }

    public void SetRect(RectTransform target, Rect screenRect)
    {
        Canvas canvas = target.GetComponentInParent<Canvas>();
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(target, screenRect.min, camera, out Vector2 min);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(target, screenRect.max, camera, out Vector2 max);
        rectTransform.anchoredPosition = (min + max) * 0.5f - target.rect.center;
        rectTransform.sizeDelta = max - min;
    }

    public void SetMaterial(Material material)
    {
        if (image.material != material)
            image.material = material;
    }

    public void SetFrame(Texture2D texture, int frameIndex, int columns, int rows, Color tint)
    {
        if (image.texture != texture)
            image.texture = texture;

        image.color = tint;

        int clampedIndex = Mathf.Max(0, frameIndex);
        int column = clampedIndex % columns;
        int row = clampedIndex / columns;
        float width = 1f / columns;
        float height = 1f / rows;
        image.uvRect = new Rect(column * width, 1f - (row + 1) * height, width, height);
    }

    public void Show()
    {
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (gameObject != null && gameObject.activeSelf)
            gameObject.SetActive(false);
    }
}
