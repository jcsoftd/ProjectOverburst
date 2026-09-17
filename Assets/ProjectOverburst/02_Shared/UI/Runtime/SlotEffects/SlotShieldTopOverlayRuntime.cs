using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

internal sealed class SlotShieldTopOverlayRuntime : MonoBehaviour
{
    private static readonly bool OverlayEnabled = true;
    private const string ManagerName = "Runtime_SlotShieldTopOverlay";
    private const string CanvasName = "Runtime_SlotShieldTopOverlayCanvas";
    private const string LegacyControlPanelName = "SlotShieldRuntimeControlPanel";
    private const string LegacyShield2ControlPanelName = "SlotShield2RuntimeControlPanel";
    private const string LegacyOverlayPrefix = "SlotShieldFlipbookOverlay_";
    private const string LegacyShield2OverlayPrefix = "Shield2FlipbookOverlay_";
    private const int SortingOrder = 12001;
    private const float RescanInterval = 0.35f;
    private const int MaxTargetCount = 96;
    private const int MaxOverlayElementCount = MaxTargetCount * MaxRunnerCount * (MaxAfterImageLevel + 1) * LayerCount;
    internal const int FlipbookColumns = 8;
    internal const int FlipbookRows = 8;
    internal const int FlipbookFrameCount = 60;
    internal const int MaxRunnerCount = 4;
    internal const int MaxAfterImageLevel = 6;
    internal const float MinRuntimeOverlayOffset = -12f;
    internal const float MaxRuntimeOverlayOffset = 36f;
    internal const float RuntimeOverlayOffsetStep = 1f;
    internal const float MinRuntimeThickness = 0.55f;
    internal const float MaxRuntimeThickness = 1.35f;
    internal const float RuntimeThicknessStep = 0.05f;
    internal const float MinRuntimeFramesPerSecond = 10f;
    internal const float MaxRuntimeFramesPerSecond = 72f;
    internal const float RuntimeFramesPerSecondStep = 2f;
    internal const float MinRuntimeAlpha = 0.1f;
    internal const float MaxRuntimeAlpha = 1.35f;
    internal const float RuntimeAlphaStep = 0.05f;
    internal const float MinRuntimeVisualScale = 0.75f;
    internal const float MaxRuntimeVisualScale = 1.4f;
    internal const float RuntimeVisualScaleStep = 0.025f;
    private const int LayerCount = 1;

    internal static readonly int AlphaCutoffId = Shader.PropertyToID("_AlphaCutoff");
    internal static readonly int AlphaPowerId = Shader.PropertyToID("_AlphaPower");

    private static SlotShieldTopOverlayRuntime instance;
    private static readonly SlotShieldFlipbookDefinition[] FlipbookDefinitions =
    {
        new SlotShieldFlipbookDefinition("Barrier", "UI/SlotShield/SlotShield_Flipbook"),
        new SlotShieldFlipbookDefinition("Hex", "UI/SlotShield/SlotShield_Hex_Flipbook"),
        new SlotShieldFlipbookDefinition("Pulse", "UI/SlotShield/SlotShield_Pulse_Flipbook"),
        new SlotShieldFlipbookDefinition("Rune", "UI/SlotShield/SlotShield_Rune_Flipbook"),
        new SlotShieldFlipbookDefinition("Prism", "UI/SlotShield/SlotShield_Prism_Flipbook")
    };

    private readonly List<RectTransform> targets = new List<RectTransform>();
    private readonly Vector3[] worldCorners = new Vector3[4];
    private readonly SlotShieldLayerState[] layers =
    {
        new SlotShieldLayerState(
            0,
            "Shield1",
            "SlotShield1RuntimeControlPanel",
            new Vector2(326f, -54f),
            new Color(0.055f, 0.045f, 0.075f, 0.86f),
            4,
            SlotShieldMotionKind.Pulse,
            ItemGrade.Epic,
            -1f,
            0.9f,
            34f,
            2,
            1,
            1f,
            0.96f,
            0.97f,
            1.085f)
    };

    private Canvas overlayCanvas;
    private RectTransform canvasRect;
    private float nextScanTime;
    private bool legacyCleanupDone;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (!OverlayEnabled)
            return;

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        EnsureRuntime();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SlotShieldTopOverlayRuntime runtime = EnsureRuntime();
        if (runtime != null)
            runtime.RequestRescan();
    }

    private static SlotShieldTopOverlayRuntime EnsureRuntime()
    {
        if (!OverlayEnabled)
            return null;

        if (instance != null)
            return instance;

        GameObject existing = GameObject.Find(ManagerName);
        if (existing != null)
        {
            SlotShieldTopOverlayRuntime runtime = existing.GetComponent<SlotShieldTopOverlayRuntime>();
            if (runtime == null)
                runtime = existing.AddComponent<SlotShieldTopOverlayRuntime>();

            return runtime;
        }

        GameObject runtimeObject = new GameObject(ManagerName);
        DontDestroyOnLoad(runtimeObject);
        return runtimeObject.AddComponent<SlotShieldTopOverlayRuntime>();
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

        for (int i = 0; i < layers.Length; i++)
            layers[i].Dispose();
    }

    private void LateUpdate()
    {
        EnsurePresentation();
        if (canvasRect == null)
            return;

        if (Time.unscaledTime >= nextScanTime)
            RefreshTargets();

        for (int i = 0; i < layers.Length; i++)
        {
            SlotShieldLayerState layer = layers[i];
            layer.ApplyRuntimeThicknessMaterial();
            layer.ResolveFlipbookTexture();

            if (!layer.RuntimeOverlayVisible || layer.FlipbookTexture == null)
            {
                layer.HideUnusedOverlays(0);
                continue;
            }

            int baseFrameIndex = Mathf.FloorToInt(Time.unscaledTime * layer.RuntimeFramesPerSecond);
            int visibleCount = 0;
            for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
            {
                RectTransform target = targets[targetIndex];
                if (!TryResolveTarget(layer, target, out Rect screenRect, out Color gradeTint))
                    continue;

                visibleCount = RenderTargetOverlays(layer, screenRect, gradeTint, baseFrameIndex, visibleCount);
            }

            layer.HideUnusedOverlays(visibleCount);
        }

        for (int i = 0; i < layers.Length; i++)
            layers[i].BringControlsToFront();
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

    private int RenderTargetOverlays(SlotShieldLayerState layer, Rect screenRect, Color gradeTint, int baseFrameIndex, int startIndex)
    {
        int visibleCount = startIndex;
        int runnerCount = Mathf.Clamp(layer.RuntimeRunnerCount, 1, MaxRunnerCount);
        int afterImageLevel = Mathf.Clamp(layer.RuntimeAfterImageLevel, 0, MaxAfterImageLevel);
        int afterImageFrameStep = ResolveAfterImageFrameStep(afterImageLevel);
        Rect layerRect = layer.ApplyOverlayOffset(screenRect);

        for (int runner = 0; runner < runnerCount; runner++)
        {
            for (int afterImage = afterImageLevel; afterImage >= 0; afterImage--)
            {
                SlotShieldFlipbookOverlayElement overlay = layer.EnsureOverlay(visibleCount, canvasRect);
                overlay.SetMaterial(layer.OverlayMaterial);
                overlay.SetRect(canvasRect, ApplyRuntimeScale(layer, layerRect, runner, afterImage));
                overlay.SetFrame(
                    layer.FlipbookTexture,
                    ResolveRuntimeFrame(layer, baseFrameIndex, runner, runnerCount, afterImage, afterImageFrameStep),
                    FlipbookColumns,
                    FlipbookRows,
                    ResolveRuntimeTint(layer, gradeTint, runner, afterImage));
                overlay.Show();
                visibleCount++;
            }
        }

        return visibleCount;
    }

    private static Rect ApplyRuntimeScale(SlotShieldLayerState layer, Rect screenRect, int runner, int afterImage)
    {
        float scale = layer.RuntimeVisualScale;
        if (layer.RuntimeMotionKind == SlotShieldMotionKind.Pulse)
        {
            float pulse = Mathf.Sin(Time.unscaledTime * 3.8f + runner * 1.3f - afterImage * 0.22f) * 0.5f + 0.5f;
            scale *= Mathf.Lerp(layer.RuntimePulseScaleMin, layer.RuntimePulseScaleMax, pulse);
        }

        if (Mathf.Approximately(scale, 1f))
            return screenRect;

        Vector2 center = screenRect.center;
        Vector2 size = screenRect.size * scale;
        return new Rect(center - size * 0.5f, size);
    }

    private static int ResolveRuntimeFrame(SlotShieldLayerState layer, int baseFrameIndex, int runner, int runnerCount, int afterImage, int afterImageFrameStep)
    {
        int frame = baseFrameIndex + Mathf.RoundToInt(FlipbookFrameCount * runner / (float)Mathf.Max(1, runnerCount));
        frame -= afterImage * afterImageFrameStep;

        switch (layer.RuntimeMotionKind)
        {
            case SlotShieldMotionKind.CounterClockwise:
                return WrapFrame(-frame);
            case SlotShieldMotionKind.PingPong:
                return ResolvePingPongFrame(frame);
            default:
                return WrapFrame(frame);
        }
    }

    private static Color ResolveRuntimeTint(SlotShieldLayerState layer, Color gradeTint, int runner, int afterImage)
    {
        Color tint = ResolveColorModeTint(layer.RuntimeColorMode, gradeTint);
        tint.a *= layer.RuntimeAlpha;
        if (afterImage > 0)
            tint.a *= Mathf.Pow(0.58f, afterImage);

        if (layer.RuntimeMotionKind == SlotShieldMotionKind.Pulse)
        {
            float pulse = Mathf.Sin(Time.unscaledTime * 4.2f + runner * 1.1f - afterImage * 0.35f) * 0.5f + 0.5f;
            tint.a *= Mathf.Lerp(0.74f, 1f, pulse);
        }

        return tint;
    }

    private static Color ResolveColorModeTint(SlotShieldColorMode mode, Color gradeTint)
    {
        switch (mode)
        {
            case SlotShieldColorMode.White:
                return new Color(1f, 1f, 1f, 0.95f);
            case SlotShieldColorMode.Cyan:
                return new Color(0.35f, 0.95f, 1f, 0.95f);
            case SlotShieldColorMode.Violet:
                return new Color(0.88f, 0.45f, 1f, 0.95f);
            default:
                return gradeTint;
        }
    }

    private static int ResolveAfterImageFrameStep(int afterImageLevel)
    {
        if (afterImageLevel <= 0)
            return 1;

        return Mathf.RoundToInt(Mathf.Lerp(2f, 6f, afterImageLevel / (float)MaxAfterImageLevel));
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

    private bool TryResolveTarget(SlotShieldLayerState layer, RectTransform candidate, out Rect screenRect, out Color gradeTint)
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

        if (!effect.TryGetCurrentGrade(out ItemGrade grade) || grade < layer.MinimumVisibleGrade)
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

        tint.a = 0.92f;
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

        screenRect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return screenRect.width > 1f && screenRect.height > 1f;
    }

    private void EnsurePresentation()
    {
        EnsureCanvas();
        CleanupLegacyRuntimeObjects();
        EnsureGraphicRaycaster();

        for (int i = 0; i < layers.Length; i++)
        {
            layers[i].EnsureOverlayMaterial();
            layers[i].ResolveFlipbookTexture();
            EnsureDebugControls(layers[i]);
        }
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

    private void CleanupLegacyRuntimeObjects()
    {
        if (legacyCleanupDone || canvasRect == null)
            return;

        legacyCleanupDone = true;
        for (int i = canvasRect.childCount - 1; i >= 0; i--)
        {
            Transform child = canvasRect.GetChild(i);
            if (child == null)
                continue;

            if (child.name == LegacyControlPanelName ||
                child.name == LegacyShield2ControlPanelName ||
                child.name.StartsWith(LegacyOverlayPrefix) ||
                child.name.StartsWith(LegacyShield2OverlayPrefix))
            {
                Destroy(child.gameObject);
            }
        }
    }

    private void EnsureDebugControls(SlotShieldLayerState layer)
    {
        if (!SlotShieldRuntimeControls.IsAllowed() || layer == null)
            return;

        RectTransform controlRoot = SlotEffectTuningControlsRegistry.GetRoot();
        if (controlRoot == null)
            return;

        if (layer.DebugControls == null || !layer.DebugControls.IsValid)
            layer.DebugControls = SlotShieldRuntimeControls.CreateOrReuse(controlRoot, layer);

        if (layer.DebugControls != null)
            layer.DebugControls.RefreshVisual();
    }

    private void EnsureGraphicRaycaster()
    {
        if (overlayCanvas == null)
            return;

        if (overlayCanvas.GetComponent<GraphicRaycaster>() == null)
            overlayCanvas.gameObject.AddComponent<GraphicRaycaster>();
    }

    internal static SlotShieldFlipbookDefinition GetShieldFlipbookDefinition(int index)
    {
        if (FlipbookDefinitions.Length == 0)
            return new SlotShieldFlipbookDefinition("None", string.Empty);

        int wrapped = index % FlipbookDefinitions.Length;
        if (wrapped < 0)
            wrapped += FlipbookDefinitions.Length;

        return FlipbookDefinitions[wrapped];
    }

    internal static int ShieldFlipbookCount { get { return FlipbookDefinitions.Length; } }
    internal static float ThicknessStep { get { return RuntimeThicknessStep; } }
    internal static float OverlayOffsetStep { get { return RuntimeOverlayOffsetStep; } }
    internal static float FramesPerSecondStep { get { return RuntimeFramesPerSecondStep; } }
    internal static float AlphaStep { get { return RuntimeAlphaStep; } }
    internal static float VisualScaleStep { get { return RuntimeVisualScaleStep; } }
    internal static int AfterImageLevelMax { get { return MaxAfterImageLevel; } }
}

internal sealed class SlotShieldLayerState
{
    private readonly List<SlotShieldFlipbookOverlayElement> overlays = new List<SlotShieldFlipbookOverlayElement>();
    private readonly HashSet<string> warnedMissingFlipbooks = new HashSet<string>();
    private SlotShieldMotionKind runtimeMotionKind;
    private SlotShieldColorMode runtimeColorMode = SlotShieldColorMode.Grade;
    private float runtimeOverlayOffset;
    private float runtimeThickness = 1f;
    private float runtimeFramesPerSecond = 36f;
    private float runtimeAlpha;
    private float runtimeVisualScale;
    private readonly float runtimePulseScaleMin;
    private readonly float runtimePulseScaleMax;
    private int runtimeFlipbookIndex;
    private int runtimeRunnerCount = 1;
    private int runtimeAfterImageLevel;
    private bool runtimeOverlayVisible = true;
    private bool runtimeFlipbookAvailable = true;

    public readonly int Index;
    public readonly string Label;
    public readonly string ControlRootName;
    public readonly Vector2 PanelPosition;
    public readonly Color PanelBackground;
    public readonly ItemGrade MinimumVisibleGrade;

    public Texture2D FlipbookTexture { get; private set; }
    public Material OverlayMaterial { get; private set; }
    public SlotShieldRuntimeControls DebugControls { get; set; }

    public SlotShieldLayerState(
        int index,
        string label,
        string controlRootName,
        Vector2 panelPosition,
        Color panelBackground,
        int defaultFlipbookIndex,
        SlotShieldMotionKind defaultMotionKind,
        ItemGrade minimumVisibleGrade,
        float defaultOverlayOffset,
        float defaultThickness,
        float defaultFramesPerSecond,
        int defaultRunnerCount,
        int defaultAfterImageLevel,
        float defaultVisualScale,
        float defaultAlpha,
        float defaultPulseScaleMin,
        float defaultPulseScaleMax)
    {
        Index = index;
        Label = label;
        ControlRootName = controlRootName;
        PanelPosition = panelPosition;
        PanelBackground = panelBackground;
        MinimumVisibleGrade = minimumVisibleGrade;
        runtimeFlipbookIndex = defaultFlipbookIndex;
        runtimeMotionKind = defaultMotionKind;
        runtimeOverlayOffset = defaultOverlayOffset;
        runtimeThickness = Mathf.Clamp(defaultThickness, SlotShieldTopOverlayRuntime.MinRuntimeThickness, SlotShieldTopOverlayRuntime.MaxRuntimeThickness);
        runtimeFramesPerSecond = Mathf.Clamp(defaultFramesPerSecond, SlotShieldTopOverlayRuntime.MinRuntimeFramesPerSecond, SlotShieldTopOverlayRuntime.MaxRuntimeFramesPerSecond);
        runtimeRunnerCount = Mathf.Clamp(defaultRunnerCount, 1, SlotShieldTopOverlayRuntime.MaxRunnerCount);
        runtimeAfterImageLevel = Mathf.Clamp(defaultAfterImageLevel, 0, SlotShieldTopOverlayRuntime.MaxAfterImageLevel);
        runtimeVisualScale = defaultVisualScale;
        runtimeAlpha = defaultAlpha;
        runtimePulseScaleMin = defaultPulseScaleMin;
        runtimePulseScaleMax = defaultPulseScaleMax;
    }

    public void Dispose()
    {
        if (OverlayMaterial != null)
            Object.Destroy(OverlayMaterial);
    }

    public void EnsureOverlayMaterial()
    {
        if (OverlayMaterial != null)
            return;

        Shader shader = Resources.Load<Shader>("Shaders/SlotVefectsAlphaThickness");
        if (shader == null)
            shader = Shader.Find("OVERBURST/UI/SlotVefectsAlphaThickness");
        if (shader == null)
            return;

        OverlayMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        ApplyRuntimeThicknessMaterial();
    }

    public void ApplyRuntimeThicknessMaterial()
    {
        if (OverlayMaterial == null)
            return;

        float cutoff;
        float power;
        if (runtimeThickness < 1f)
        {
            float thinT = Mathf.InverseLerp(1f, SlotShieldTopOverlayRuntime.MinRuntimeThickness, runtimeThickness);
            cutoff = Mathf.Lerp(0.055f, 0.22f, thinT);
            power = Mathf.Lerp(1f, 1.42f, thinT);
        }
        else
        {
            float thickT = Mathf.InverseLerp(1f, SlotShieldTopOverlayRuntime.MaxRuntimeThickness, runtimeThickness);
            cutoff = Mathf.Lerp(0.055f, 0.004f, thickT);
            power = Mathf.Lerp(1f, 0.7f, thickT);
        }

        OverlayMaterial.SetFloat(SlotShieldTopOverlayRuntime.AlphaCutoffId, cutoff);
        OverlayMaterial.SetFloat(SlotShieldTopOverlayRuntime.AlphaPowerId, power);
    }

    public void ResolveFlipbookTexture()
    {
        if (FlipbookTexture != null)
            return;

        SlotShieldFlipbookDefinition definition = GetCurrentFlipbookDefinition();
        FlipbookTexture = Resources.Load<Texture2D>(definition.ResourcePath);
        runtimeFlipbookAvailable = FlipbookTexture != null;
        if (FlipbookTexture != null)
            return;

        if (warnedMissingFlipbooks.Add(definition.ResourcePath))
            Debug.LogWarning("[SlotShieldTopOverlay] " + Label + " flipbook을 찾지 못해 기본 Barrier 시트로 표시합니다: Resources/" + definition.ResourcePath);

        if (SlotShieldTopOverlayRuntime.ShieldFlipbookCount > 0)
            FlipbookTexture = Resources.Load<Texture2D>(SlotShieldTopOverlayRuntime.GetShieldFlipbookDefinition(0).ResourcePath);
    }

    public SlotShieldFlipbookOverlayElement EnsureOverlay(int index, RectTransform canvasRect)
    {
        while (overlays.Count <= index)
        {
            GameObject overlayObject = new GameObject(Label + "FlipbookOverlay_" + overlays.Count, typeof(RectTransform), typeof(RawImage));
            overlayObject.transform.SetParent(canvasRect, false);

            RawImage image = overlayObject.GetComponent<RawImage>();
            image.raycastTarget = false;
            image.color = Color.white;

            overlays.Add(new SlotShieldFlipbookOverlayElement(overlayObject, image));
        }

        return overlays[index];
    }

    public void HideUnusedOverlays(int startIndex)
    {
        for (int i = startIndex; i < overlays.Count; i++)
        {
            if (overlays[i] != null)
                overlays[i].Hide();
        }
    }

    public void BringControlsToFront()
    {
        if (DebugControls != null)
            DebugControls.BringToFront();
    }

    public bool RuntimeOverlayVisible { get { return runtimeOverlayVisible; } }
    public SlotShieldMotionKind RuntimeMotionKind { get { return runtimeMotionKind; } }
    public SlotShieldColorMode RuntimeColorMode { get { return runtimeColorMode; } }
    public string RuntimeMotionKindLabel { get { return ResolveMotionLabel(runtimeMotionKind); } }
    public float RuntimeThickness { get { return runtimeThickness; } }
    public float RuntimeOverlayOffset { get { return runtimeOverlayOffset; } }
    public int RuntimeRunnerCount { get { return runtimeRunnerCount; } }
    public int RuntimeAfterImageLevel { get { return runtimeAfterImageLevel; } }
    public float RuntimeFramesPerSecond { get { return runtimeFramesPerSecond; } }
    public string RuntimeShieldKindLabel { get { return GetCurrentFlipbookDefinition().Label; } }
    public bool RuntimeShieldKindAvailable { get { return runtimeFlipbookAvailable; } }
    public string RuntimeColorModeLabel { get { return ResolveColorModeLabel(runtimeColorMode); } }
    public float RuntimeAlpha { get { return runtimeAlpha; } }
    public float RuntimeVisualScale { get { return runtimeVisualScale; } }
    public float RuntimePulseScaleMin { get { return runtimePulseScaleMin; } }
    public float RuntimePulseScaleMax { get { return runtimePulseScaleMax; } }

    public Rect ApplyOverlayOffset(Rect screenRect)
    {
        return Rect.MinMaxRect(
            screenRect.xMin - runtimeOverlayOffset,
            screenRect.yMin - runtimeOverlayOffset,
            screenRect.xMax + runtimeOverlayOffset,
            screenRect.yMax + runtimeOverlayOffset);
    }

    public void ToggleRuntimeOverlayVisible()
    {
        runtimeOverlayVisible = !runtimeOverlayVisible;
        if (!runtimeOverlayVisible)
            HideUnusedOverlays(0);

        RefreshControls();
    }

    public void CycleRuntimeMotionKind()
    {
        int next = ((int)runtimeMotionKind + 1) % System.Enum.GetValues(typeof(SlotShieldMotionKind)).Length;
        runtimeMotionKind = (SlotShieldMotionKind)next;
        RefreshControls();
    }

    public void AdjustRuntimeThickness(float delta)
    {
        runtimeThickness = Mathf.Clamp(runtimeThickness + delta, SlotShieldTopOverlayRuntime.MinRuntimeThickness, SlotShieldTopOverlayRuntime.MaxRuntimeThickness);
        ApplyRuntimeThicknessMaterial();
        RefreshControls();
    }

    public void AdjustRuntimeOverlayOffset(float delta)
    {
        runtimeOverlayOffset = Mathf.Clamp(runtimeOverlayOffset + delta, SlotShieldTopOverlayRuntime.MinRuntimeOverlayOffset, SlotShieldTopOverlayRuntime.MaxRuntimeOverlayOffset);
        RefreshControls();
    }

    public void IncrementRuntimeRunnerCount()
    {
        runtimeRunnerCount++;
        if (runtimeRunnerCount > SlotShieldTopOverlayRuntime.MaxRunnerCount)
            runtimeRunnerCount = 1;

        RefreshControls();
    }

    public void AdjustRuntimeAfterImageLevel(int delta)
    {
        runtimeAfterImageLevel = Mathf.Clamp(runtimeAfterImageLevel + delta, 0, SlotShieldTopOverlayRuntime.MaxAfterImageLevel);
        RefreshControls();
    }

    public void AdjustRuntimeFramesPerSecond(float delta)
    {
        runtimeFramesPerSecond = Mathf.Clamp(runtimeFramesPerSecond + delta, SlotShieldTopOverlayRuntime.MinRuntimeFramesPerSecond, SlotShieldTopOverlayRuntime.MaxRuntimeFramesPerSecond);
        RefreshControls();
    }

    public void CycleRuntimeShieldKind()
    {
        if (SlotShieldTopOverlayRuntime.ShieldFlipbookCount == 0)
            return;

        int startIndex = runtimeFlipbookIndex;
        for (int i = 1; i <= SlotShieldTopOverlayRuntime.ShieldFlipbookCount; i++)
        {
            int candidateIndex = (startIndex + i) % SlotShieldTopOverlayRuntime.ShieldFlipbookCount;
            SlotShieldFlipbookDefinition definition = SlotShieldTopOverlayRuntime.GetShieldFlipbookDefinition(candidateIndex);
            Texture2D candidateTexture = Resources.Load<Texture2D>(definition.ResourcePath);
            if (candidateTexture == null)
            {
                warnedMissingFlipbooks.Add(definition.ResourcePath);
                continue;
            }

            runtimeFlipbookIndex = candidateIndex;
            FlipbookTexture = candidateTexture;
            runtimeFlipbookAvailable = true;
            RefreshControls();
            return;
        }

        runtimeFlipbookIndex = (startIndex + 1) % SlotShieldTopOverlayRuntime.ShieldFlipbookCount;
        FlipbookTexture = null;
        runtimeFlipbookAvailable = false;
        RefreshControls();
    }

    public void CycleRuntimeColorMode()
    {
        int next = ((int)runtimeColorMode + 1) % System.Enum.GetValues(typeof(SlotShieldColorMode)).Length;
        runtimeColorMode = (SlotShieldColorMode)next;
        RefreshControls();
    }

    public void AdjustRuntimeAlpha(float delta)
    {
        runtimeAlpha = Mathf.Clamp(runtimeAlpha + delta, SlotShieldTopOverlayRuntime.MinRuntimeAlpha, SlotShieldTopOverlayRuntime.MaxRuntimeAlpha);
        RefreshControls();
    }

    public void AdjustRuntimeVisualScale(float delta)
    {
        runtimeVisualScale = Mathf.Clamp(runtimeVisualScale + delta, SlotShieldTopOverlayRuntime.MinRuntimeVisualScale, SlotShieldTopOverlayRuntime.MaxRuntimeVisualScale);
        RefreshControls();
    }

    private SlotShieldFlipbookDefinition GetCurrentFlipbookDefinition()
    {
        return SlotShieldTopOverlayRuntime.GetShieldFlipbookDefinition(runtimeFlipbookIndex);
    }

    private void RefreshControls()
    {
        if (DebugControls != null)
            DebugControls.RefreshVisual();
    }

    private static string ResolveMotionLabel(SlotShieldMotionKind kind)
    {
        switch (kind)
        {
            case SlotShieldMotionKind.CounterClockwise:
                return "역방향";
            case SlotShieldMotionKind.PingPong:
                return "왕복";
            case SlotShieldMotionKind.Pulse:
                return "펄스";
            default:
                return "정방향";
        }
    }

    private static string ResolveColorModeLabel(SlotShieldColorMode mode)
    {
        switch (mode)
        {
            case SlotShieldColorMode.White:
                return "흰색";
            case SlotShieldColorMode.Cyan:
                return "시안";
            case SlotShieldColorMode.Violet:
                return "보라";
            default:
                return "등급";
        }
    }
}

internal enum SlotShieldMotionKind
{
    Clockwise,
    CounterClockwise,
    PingPong,
    Pulse
}

internal enum SlotShieldColorMode
{
    Grade,
    White,
    Cyan,
    Violet
}

internal readonly struct SlotShieldFlipbookDefinition
{
    public readonly string Label;
    public readonly string ResourcePath;

    public SlotShieldFlipbookDefinition(string label, string resourcePath)
    {
        Label = label;
        ResourcePath = resourcePath;
    }
}

internal sealed class SlotShieldRuntimeControls
{
    private const float PanelWidth = 304f;
    private const float PanelHeight = 360f;
    private const float RowHeight = 26f;
    private const float RowSpacing = 5f;
    private const float LabelWidth = 68f;
    private const float ButtonHeight = 24f;

    private static bool warnedMissingEventSystem;

    private readonly GameObject root;
    private readonly SlotShieldLayerState layer;
    private TextMeshProUGUI activeButtonLabel;
    private TextMeshProUGUI motionButtonLabel;
    private TextMeshProUGUI thicknessValueLabel;
    private TextMeshProUGUI offsetValueLabel;
    private TextMeshProUGUI countButtonLabel;
    private TextMeshProUGUI afterImageValueLabel;
    private TextMeshProUGUI speedValueLabel;
    private TextMeshProUGUI kindButtonLabel;
    private TextMeshProUGUI colorButtonLabel;
    private TextMeshProUGUI alphaValueLabel;
    private TextMeshProUGUI visualScaleValueLabel;

    private SlotShieldRuntimeControls(GameObject root, SlotShieldLayerState layer)
    {
        this.root = root;
        this.layer = layer;
    }

    public bool IsValid { get { return root != null; } }

    public static bool IsAllowed()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return true;
#else
        return false;
#endif
    }

    public static SlotShieldRuntimeControls CreateOrReuse(RectTransform parent, SlotShieldLayerState layer)
    {
        if (parent == null || layer == null)
            return null;

        Transform existing = parent.Find(layer.ControlRootName);
        if (existing != null)
            Object.Destroy(existing.gameObject);

        GameObject rootObject = new GameObject(layer.ControlRootName, typeof(RectTransform), typeof(Image));
        rootObject.transform.SetParent(parent, false);

        RectTransform rootRect = rootObject.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0f, 1f);
        rootRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        rootRect.anchoredPosition = layer.PanelPosition;

        Image background = rootObject.GetComponent<Image>();
        background.color = layer.PanelBackground;
        background.raycastTarget = true;

        SlotShieldFontReference fontReference = ResolveFontReference(parent);
        SlotShieldRuntimeControls controls = new SlotShieldRuntimeControls(rootObject, layer);
        controls.BuildRows(rootRect);
        ApplyFont(rootRect, fontReference);
        controls.RefreshVisual();
        controls.BringToFront();
        if (rootObject.activeInHierarchy)
            WarnIfMissingEventSystem();
        return controls;
    }

    public void BringToFront()
    {
        if (root != null)
            root.transform.SetAsLastSibling();
    }

    public void RefreshVisual()
    {
        if (layer == null)
            return;

        if (activeButtonLabel != null)
            activeButtonLabel.text = layer.RuntimeOverlayVisible ? layer.Label + " ON" : layer.Label + " OFF";
        if (motionButtonLabel != null)
            motionButtonLabel.text = "모션 " + layer.RuntimeMotionKindLabel;
        if (thicknessValueLabel != null)
            thicknessValueLabel.text = layer.RuntimeThickness.ToString("0.00");
        if (offsetValueLabel != null)
            offsetValueLabel.text = layer.RuntimeOverlayOffset.ToString("+0;-0;0") + "px";
        if (countButtonLabel != null)
            countButtonLabel.text = "개수 + " + layer.RuntimeRunnerCount + "/4";
        if (afterImageValueLabel != null)
            afterImageValueLabel.text = layer.RuntimeAfterImageLevel.ToString("0") + "/" + SlotShieldTopOverlayRuntime.AfterImageLevelMax.ToString("0");
        if (speedValueLabel != null)
            speedValueLabel.text = layer.RuntimeFramesPerSecond.ToString("0") + "fps";
        if (kindButtonLabel != null)
            kindButtonLabel.text = layer.RuntimeShieldKindAvailable
                ? "종류 " + layer.RuntimeShieldKindLabel
                : "종류 " + layer.RuntimeShieldKindLabel + " 없음";
        if (colorButtonLabel != null)
            colorButtonLabel.text = "색 " + layer.RuntimeColorModeLabel;
        if (alphaValueLabel != null)
            alphaValueLabel.text = layer.RuntimeAlpha.ToString("0.00");
        if (visualScaleValueLabel != null)
            visualScaleValueLabel.text = layer.RuntimeVisualScale.ToString("0.000");
    }

    private void BuildRows(RectTransform rootRect)
    {
        float y = 10f;
        CreateLabel(rootRect, "ActiveLabel", 10f, y, LabelWidth, ButtonHeight, "1 표시");
        activeButtonLabel = CreateButton(rootRect, "ActiveButton", 86f, y, 190f, ButtonHeight, layer.Label + " ON", layer.ToggleRuntimeOverlayVisible);

        y += RowHeight + RowSpacing;
        CreateLabel(rootRect, "MotionLabel", 10f, y, LabelWidth, ButtonHeight, "2 모션");
        motionButtonLabel = CreateButton(rootRect, "MotionButton", 86f, y, 190f, ButtonHeight, "모션", layer.CycleRuntimeMotionKind);

        y += RowHeight + RowSpacing;
        CreateLabel(rootRect, "ThicknessLabel", 10f, y, LabelWidth, ButtonHeight, "3 두께");
        CreateButton(rootRect, "ThicknessDownButton", 86f, y, 34f, ButtonHeight, "-", () => layer.AdjustRuntimeThickness(-SlotShieldTopOverlayRuntime.ThicknessStep));
        thicknessValueLabel = CreateValue(rootRect, "ThicknessValue", 126f, y, 92f, ButtonHeight);
        CreateButton(rootRect, "ThicknessUpButton", 224f, y, 34f, ButtonHeight, "+", () => layer.AdjustRuntimeThickness(SlotShieldTopOverlayRuntime.ThicknessStep));

        y += RowHeight + RowSpacing;
        CreateLabel(rootRect, "OffsetLabel", 10f, y, LabelWidth, ButtonHeight, "4 오프셋");
        CreateButton(rootRect, "OffsetDownButton", 86f, y, 34f, ButtonHeight, "-", () => layer.AdjustRuntimeOverlayOffset(-SlotShieldTopOverlayRuntime.OverlayOffsetStep));
        offsetValueLabel = CreateValue(rootRect, "OffsetValue", 126f, y, 92f, ButtonHeight);
        CreateButton(rootRect, "OffsetUpButton", 224f, y, 34f, ButtonHeight, "+", () => layer.AdjustRuntimeOverlayOffset(SlotShieldTopOverlayRuntime.OverlayOffsetStep));

        y += RowHeight + RowSpacing;
        CreateLabel(rootRect, "CountLabel", 10f, y, LabelWidth, ButtonHeight, "5 수량");
        countButtonLabel = CreateButton(rootRect, "CountButton", 86f, y, 190f, ButtonHeight, "개수 +", layer.IncrementRuntimeRunnerCount);

        y += RowHeight + RowSpacing;
        CreateLabel(rootRect, "AfterImageLabel", 10f, y, LabelWidth, ButtonHeight, "6 잔상");
        CreateButton(rootRect, "AfterImageDownButton", 86f, y, 34f, ButtonHeight, "-", () => layer.AdjustRuntimeAfterImageLevel(-1));
        afterImageValueLabel = CreateValue(rootRect, "AfterImageValue", 126f, y, 92f, ButtonHeight);
        CreateButton(rootRect, "AfterImageUpButton", 224f, y, 34f, ButtonHeight, "+", () => layer.AdjustRuntimeAfterImageLevel(1));

        y += RowHeight + RowSpacing;
        CreateLabel(rootRect, "SpeedLabel", 10f, y, LabelWidth, ButtonHeight, "7 속도");
        CreateButton(rootRect, "SpeedDownButton", 86f, y, 34f, ButtonHeight, "-", () => layer.AdjustRuntimeFramesPerSecond(-SlotShieldTopOverlayRuntime.FramesPerSecondStep));
        speedValueLabel = CreateValue(rootRect, "SpeedValue", 126f, y, 92f, ButtonHeight);
        CreateButton(rootRect, "SpeedUpButton", 224f, y, 34f, ButtonHeight, "+", () => layer.AdjustRuntimeFramesPerSecond(SlotShieldTopOverlayRuntime.FramesPerSecondStep));

        y += RowHeight + RowSpacing;
        CreateLabel(rootRect, "KindLabel", 10f, y, LabelWidth, ButtonHeight, "8 종류");
        kindButtonLabel = CreateButton(rootRect, "KindButton", 86f, y, 190f, ButtonHeight, "종류", layer.CycleRuntimeShieldKind);

        y += RowHeight + RowSpacing;
        CreateLabel(rootRect, "ColorLabel", 10f, y, LabelWidth, ButtonHeight, "9 색상");
        colorButtonLabel = CreateButton(rootRect, "ColorButton", 86f, y, 190f, ButtonHeight, "색", layer.CycleRuntimeColorMode);

        y += RowHeight + RowSpacing;
        CreateLabel(rootRect, "AlphaLabel", 10f, y, LabelWidth, ButtonHeight, "10 투명");
        CreateButton(rootRect, "AlphaDownButton", 86f, y, 34f, ButtonHeight, "-", () => layer.AdjustRuntimeAlpha(-SlotShieldTopOverlayRuntime.AlphaStep));
        alphaValueLabel = CreateValue(rootRect, "AlphaValue", 126f, y, 92f, ButtonHeight);
        CreateButton(rootRect, "AlphaUpButton", 224f, y, 34f, ButtonHeight, "+", () => layer.AdjustRuntimeAlpha(SlotShieldTopOverlayRuntime.AlphaStep));

        y += RowHeight + RowSpacing;
        CreateLabel(rootRect, "VisualScaleLabel", 10f, y, LabelWidth, ButtonHeight, "11 크기");
        CreateButton(rootRect, "VisualScaleDownButton", 86f, y, 34f, ButtonHeight, "-", () => layer.AdjustRuntimeVisualScale(-SlotShieldTopOverlayRuntime.VisualScaleStep));
        visualScaleValueLabel = CreateValue(rootRect, "VisualScaleValue", 126f, y, 92f, ButtonHeight);
        CreateButton(rootRect, "VisualScaleUpButton", 224f, y, 34f, ButtonHeight, "+", () => layer.AdjustRuntimeVisualScale(SlotShieldTopOverlayRuntime.VisualScaleStep));
    }

    private static TextMeshProUGUI CreateLabel(RectTransform parent, string name, float x, float y, float width, float height, string text)
    {
        TextMeshProUGUI label = CreateText(parent, name, x, y, width, height, text);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.fontSize = 12f;
        label.color = new Color(0.84f, 0.82f, 0.95f, 1f);
        return label;
    }

    private static TextMeshProUGUI CreateValue(RectTransform parent, string name, float x, float y, float width, float height)
    {
        TextMeshProUGUI label = CreateText(parent, name, x, y, width, height, string.Empty);
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 12.5f;
        label.color = new Color(0.98f, 0.97f, 1f, 1f);
        return label;
    }

    private static TextMeshProUGUI CreateButton(RectTransform parent, string name, float x, float y, float width, float height, string text, UnityAction action)
    {
        GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        ConfigureRect(buttonObject.GetComponent<RectTransform>(), x, y, width, height);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.12f, 0.1f, 0.19f, 0.96f);
        image.raycastTarget = true;

        Button button = buttonObject.GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
        ConfigureButtonColors(button);

        TextMeshProUGUI label = CreateText(buttonObject.transform as RectTransform, "Label", 0f, 0f, width, height, text);
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = width <= 40f ? 15f : 12.5f;
        label.color = new Color(0.96f, 0.98f, 1f, 1f);
        return label;
    }

    private static TextMeshProUGUI CreateText(RectTransform parent, string name, float x, float y, float width, float height, string text)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        ConfigureRect(textObject.GetComponent<RectTransform>(), x, y, width, height);

        TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        return label;
    }

    private static void ConfigureRect(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, -y);
    }

    private static void ConfigureButtonColors(Button button)
    {
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.12f, 0.1f, 0.19f, 0.96f);
        colors.highlightedColor = new Color(0.2f, 0.16f, 0.32f, 1f);
        colors.pressedColor = new Color(0.34f, 0.14f, 0.48f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;
    }

    private static SlotShieldFontReference ResolveFontReference(RectTransform parent)
    {
        SlotShieldFontReference reference = FindFontReferenceInScene(parent != null ? parent.root : null);
        if (reference.IsValid)
            return reference;

        return FindFontReferenceInScene(null);
    }

    private static SlotShieldFontReference FindFontReferenceInScene(Transform root)
    {
        TextMeshProUGUI[] labels = root != null
            ? root.GetComponentsInChildren<TextMeshProUGUI>(true)
            : Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int i = 0; i < labels.Length; i++)
        {
            TextMeshProUGUI source = labels[i];
            if (source == null || source.font == null)
                continue;

            if (!SupportsKorean(source.font))
                continue;

            return new SlotShieldFontReference(source.font, source.fontSharedMaterial);
        }

        return default;
    }

    private static bool SupportsKorean(TMP_FontAsset font)
    {
        if (font == null)
            return false;

        return font.HasCharacter('한') || font.name.Contains("Noto") || font.name.Contains("KR");
    }

    private static void ApplyFont(RectTransform root, SlotShieldFontReference fontReference)
    {
        if (root == null || !fontReference.IsValid)
            return;

        TextMeshProUGUI[] labels = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < labels.Length; i++)
        {
            TextMeshProUGUI target = labels[i];
            if (target == null)
                continue;

            target.font = fontReference.Font;
            if (fontReference.Material != null)
                target.fontSharedMaterial = fontReference.Material;
        }
    }

    private static void WarnIfMissingEventSystem()
    {
        if (warnedMissingEventSystem || EventSystem.current != null)
            return;

        warnedMissingEventSystem = true;
        Debug.LogWarning("[SlotShieldControls] EventSystem이 없어 Shield 런타임 버튼 클릭 입력을 받을 수 없습니다.");
    }

    private readonly struct SlotShieldFontReference
    {
        public readonly TMP_FontAsset Font;
        public readonly Material Material;

        public SlotShieldFontReference(TMP_FontAsset font, Material material)
        {
            Font = font;
            Material = material;
        }

        public bool IsValid { get { return Font != null; } }
    }
}

internal sealed class SlotShieldFlipbookOverlayElement
{
    private readonly GameObject gameObject;
    private readonly RectTransform rectTransform;
    private readonly RawImage image;

    public SlotShieldFlipbookOverlayElement(GameObject gameObject, RawImage image)
    {
        this.gameObject = gameObject;
        this.image = image;
        rectTransform = gameObject.transform as RectTransform;
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
    }

    public void SetRect(RectTransform canvasRect, Rect screenRect)
    {
        Vector2 center = screenRect.center;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, center, null, out Vector2 localCenter);
        rectTransform.anchoredPosition = localCenter;
        rectTransform.sizeDelta = new Vector2(screenRect.width, screenRect.height);
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
        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }
}
