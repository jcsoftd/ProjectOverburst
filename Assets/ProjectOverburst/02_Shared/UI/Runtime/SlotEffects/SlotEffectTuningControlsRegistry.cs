using UnityEngine;
using UnityEngine.UI;

internal static class SlotEffectTuningControlsRegistry
{
    private const string CanvasName = "Runtime_SlotEffectTuningControlsCanvas";
    private const string RootName = "SlotEffectTuningControls_DisabledRoot";
    private const int SortingOrder = 12050;

    public static bool IsAllowed()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return true;
#else
        return false;
#endif
    }

    public static RectTransform GetRoot()
    {
        if (!IsAllowed())
            return null;

        Canvas canvas = EnsureCanvas();
        if (canvas == null)
            return null;

        RectTransform canvasRect = canvas.transform as RectTransform;
        if (canvasRect == null)
            return null;

        Transform existingRoot = canvasRect.Find(RootName);
        if (existingRoot is RectTransform existingRootRect)
            return existingRootRect;

        GameObject rootObject = new GameObject(RootName, typeof(RectTransform));
        rootObject.transform.SetParent(canvasRect, false);

        RectTransform rootRect = rootObject.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        rootObject.SetActive(false);
        return rootRect;
    }

    private static Canvas EnsureCanvas()
    {
        GameObject existing = GameObject.Find(CanvasName);
        if (existing != null)
            return ConfigureCanvas(existing);

        GameObject canvasObject = new GameObject(CanvasName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Object.DontDestroyOnLoad(canvasObject);
        return ConfigureCanvas(canvasObject);
    }

    private static Canvas ConfigureCanvas(GameObject canvasObject)
    {
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        if (canvas == null)
            canvas = canvasObject.AddComponent<Canvas>();

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = SortingOrder;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = canvasObject.AddComponent<CanvasScaler>();

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (canvasObject.GetComponent<GraphicRaycaster>() == null)
            canvasObject.AddComponent<GraphicRaycaster>();

        return canvas;
    }
}
