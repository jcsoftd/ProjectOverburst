#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Overburst.DebugTools;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

/// <summary>F1 디버그 UI를 영구 자산으로 제작한다. 열린 게임 씬을 수정하지 않는다.</summary>
public static class DebugHubPrefabBuilder
{
    public const string Folder = "Assets/ProjectOverburst/Resources/Debug";
    public const string PrefabPath = Folder + "/PF_OverburstDebugHub.prefab";

    [MenuItem("OVERBURST/Builders/UI/Debug Hub Prefab")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Edit 모드에서 디버그 프리팹을 제작하세요.");
        EnsureFolder(Folder + "/Shapes");
        BakeShapes();
        DebugHubStyle style = AssetDatabase.LoadAssetAtPath<DebugHubStyle>(Folder + "/DebugHubStyle.asset");
        if (style == null)
        {
            style = ScriptableObject.CreateInstance<DebugHubStyle>();
            style.font = Resources.Load<TMP_FontAsset>(DebugHubStyle.DefaultFontPath);
            AssetDatabase.CreateAsset(style, Folder + "/DebugHubStyle.asset");
        }
        if (style.font == null)
            throw new InvalidOperationException("DebugHubStyle의 정식 글꼴을 연결하세요.");

        Assembly runtime = typeof(DebugHub).Assembly;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        typeof(DebugRegistry).GetMethod("ResetState", flags).Invoke(null, null);
        typeof(DebugPresets).GetMethod("ResetState", flags).Invoke(null, null);
        typeof(DebugPresets).GetMethod("RegisterSection", flags).Invoke(null, null);
        foreach (Type type in runtime.GetTypes().Where(t => t.Name.EndsWith("DebugModule", StringComparison.Ordinal)).OrderBy(t => t.FullName))
        {
            MethodInfo register = type.GetMethod("Register", flags);
            if (register != null && register.GetParameters().Length == 0)
                register.Invoke(null, null);
        }

        GameObject root = new GameObject("PF_OverburstDebugHub", typeof(RectTransform));
        UnityEngine.SceneManagement.Scene preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
        root.SetActive(false);
        root.layer = 5;
        try
        {
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();
            GameObject eventObject = new GameObject("Fallback EventSystem");
            eventObject.transform.SetParent(root.transform, false);
            eventObject.SetActive(false);
            EventSystem fallback = eventObject.AddComponent<EventSystem>();
            InputSystemUIInputModule module = eventObject.AddComponent<InputSystemUIInputModule>();
            const string actionsPath = "Packages/com.unity.inputsystem/InputSystem/Plugins/PlayerInput/DefaultInputActions.inputactions";
            module.actionsAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(actionsPath);
            InputActionReference[] actions = AssetDatabase.LoadAllAssetsAtPath(actionsPath).OfType<InputActionReference>().ToArray();
            InputActionReference Action(string name) => actions.First(action => action.action.actionMap.name == "UI" && action.action.name == name);
            module.point = Action("Point");
            module.leftClick = Action("Click");
            module.middleClick = Action("MiddleClick");
            module.rightClick = Action("RightClick");
            module.scrollWheel = Action("ScrollWheel");
            module.move = Action("Navigate");
            module.submit = Action("Submit");
            module.cancel = Action("Cancel");
            module.trackedDevicePosition = Action("TrackedDevicePosition");
            module.trackedDeviceOrientation = Action("TrackedDeviceOrientation");
            RectTransform rect = (RectTransform)root.transform;
            Type ui = runtime.GetType("Overburst.DebugTools.DebugUi", true);
            ui.GetMethod("Initialize", flags).Invoke(null, new object[] { style });
            Type windowType = runtime.GetType("Overburst.DebugTools.DebugHubWindow", true);
            object window = Activator.CreateInstance(windowType, new object[] { rect });
            windowType.GetMethod("BakePagesForAuthoring").Invoke(window, null);
            Type overlayType = runtime.GetType("Overburst.DebugTools.DebugOverlay", true);
            object overlay = Activator.CreateInstance(overlayType, new object[] { null, rect });
            overlayType.GetMethod("BakeLines").Invoke(overlay, null);

            DebugHubView view = root.AddComponent<DebugHubView>();
            var serialized = new SerializedObject(view);
            Transform windowRoot = rect.Find("Debug Window");
            Transform content = windowRoot.Find("Body/Scroll/Viewport/Content");
            SetReference(serialized, "style", style);
            SetReference(serialized, "window", windowRoot);
            SetReference(serialized, "tabs", windowRoot.Find("Body/Tabs"));
            SetReference(serialized, "content", content);
            SetReference(serialized, "overlay", rect.Find("Debug Pin Overlay"));
            SetReference(serialized, "fallbackEventSystem", fallback);
            SerializedProperty pages = serialized.FindProperty("pages");
            pages.arraySize = content.childCount;
            for (int i = 0; i < content.childCount; i++)
                pages.GetArrayElementAtIndex(i).objectReferenceValue = content.GetChild(i);
            string[] ids = DebugRegistry.AllItems.Select(item => item.Id).OrderBy(id => id).ToArray();
            SerializedProperty itemIds = serialized.FindProperty("itemIds");
            itemIds.arraySize = ids.Length;
            for (int i = 0; i < ids.Length; i++)
                itemIds.GetArrayElementAtIndex(i).stringValue = ids[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<DebugHub>();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            // 제작용 관리 객체가 정적 툴팁 이벤트에 남지 않도록 정리한다.
            // Dispose는 창 설정을 저장하므로 이벤트 구독만 해제한다.
            Type tips = typeof(DebugHoverTip);
            foreach (string fieldName in new[] { "Shown", "Hidden" })
                tips.GetField(fieldName, flags)?.SetValue(null, null);
            AssetDatabase.SaveAssetIfDirty(style);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
        }
        AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
        Debug.Log("[DebugHub] 정식 프리팹 제작 완료: " + PrefabPath);
    }

    private static void SetReference(SerializedObject target, string name, UnityEngine.Object value)
    {
        if (value is Transform transform)
            value = transform.GetComponent<RectTransform>();
        if (value == null)
            throw new InvalidOperationException("디버그 프리팹 참조 누락: " + name);
        target.FindProperty(name).objectReferenceValue = value;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
    }

    private static void BakeShapes()
    {
        for (int radius = 1; radius <= 16; radius++)
        {
            string path = Folder + "/Shapes/Rounded" + radius + ".png";
            if (AssetDatabase.LoadAssetAtPath<Sprite>(path) != null)
                continue;
            int size = radius * 2 + 4;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try
            {
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                        Vector2 center = new Vector2(Mathf.Clamp(point.x, radius, size - radius), Mathf.Clamp(point.y, radius, size - radius));
                        float alpha = Mathf.Clamp01(radius - Vector2.Distance(point, center) + 0.5f);
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                    }
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = Vector4.one * (radius + 1);
            importer.spritePixelsPerUnit = 100;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }
}
#endif
