using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class OverburstMotionBlurPreviewBuilder
{
    public const string PrefabPath = "Assets/ProjectOverburst/Resources/Debug/PF_OverburstMotionBlurToggle.prefab";

    [MenuItem("OVERBURST/Camera/Build Temporary Motion Blur Toggle")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle EditMode required.");
        GameObject root = null;
        try
        {
            root = new GameObject("Temporary_MotionBlurToggle", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.layer = 5;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 12061;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            var font = Resources.Load<TMP_FontAsset>("UI/Fonts/ProjectMT/FontAssets/TMP_SpoqaHanSansNeo_Body");
            if (font == null) throw new InvalidOperationException("Project UI font missing.");
            var button = RunUiLayout.Button(root.transform, "MotionBlurToggle", "모션블러: 켜짐",
                font, null, 0, 0, 232, 40, null);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(16, -168);
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.targetGraphic.color = new Color(.20f, .31f, .27f, .94f);
            var caption = button.GetComponentInChildren<TMP_Text>();
            caption.fontSize = 19;
            var decrease = IntensityButton(root.transform, "DecreaseIntensity", "−", font, 256, -168);
            var increase = IntensityButton(root.transform, "IncreaseIntensity", "+", font, 440, -168);
            var intensityCaption = RunUiLayout.Text(root.transform, "Intensity", "강도 0.01", font,
                0, 0, 128, 40, 19, RunUiLayout.Ivory);
            Place((RectTransform)intensityCaption.transform, 304, -168);
            var preview = root.AddComponent<OverburstMotionBlurPreview>();
            var fields = new SerializedObject(preview);
            fields.FindProperty("toggleButton").objectReferenceValue = button;
            fields.FindProperty("caption").objectReferenceValue = caption;
            fields.FindProperty("decreaseButton").objectReferenceValue = decrease;
            fields.FindProperty("increaseButton").objectReferenceValue = increase;
            fields.FindProperty("intensityCaption").objectReferenceValue = intensityCaption;
            fields.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { if (root != null) Object.DestroyImmediate(root); }
        AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceSynchronousImport);
    }

    private static Button IntensityButton(Transform parent, string name, string label, TMP_FontAsset font, float x, float y)
    {
        var button = RunUiLayout.Button(parent, name, label, font, null, 0, 0, 40, 40, null);
        Place((RectTransform)button.transform, x, y);
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.targetGraphic.color = new Color(.16f, .17f, .19f, .94f);
        button.GetComponentInChildren<TMP_Text>().fontSize = 24;
        return button;
    }

    private static void Place(RectTransform rect, float x, float y)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, y);
    }
}
