#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Overburst.DebugTools;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class PlayerVoiceAuditionBuilder
{
    private const string Source = "Assets/ThirdParty/11_사운드/Action RPG Characters/Vocal Files/";
    private static readonly string[] Names = { "Karen", "Karen2", "Kimlinh", "Rina" };
    private static readonly string[] Folders = { "KarenH/Regal/AA_GeneralAttackSounds", "KarenH/Young/AA_GenericBattleSounds",
        "Kimlinh/AA_GenericBattleSounds", "Rina_Chan/AA_GenericBattleSounds" };

    [MenuItem("OVERBURST/Builders/Audio/Temporary Player Voice Audition")]
    public static void Build()
    {
        AppearanceCustomizationBuilder.RequireIdle();
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerVoiceAudition.PrefabPath) != null)
            throw new InvalidOperationException("The owned audition prefab already exists; inspect before rebuilding.");
        string folder = Path.GetDirectoryName(PlayerVoiceAudition.PrefabPath).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Editor/Testers", "Audio");
        DebugHubStyle style = Resources.Load<DebugHubStyle>(DebugHubStyle.ResourcePath);
        GameObject debug = Resources.Load<GameObject>("Debug/PF_OverburstDebugHub");
        Button original = debug.GetComponentsInChildren<Button>(true).First(b => b.targetGraphic is Image image
            && image.sprite != null && b.colors.normalColor.a == 1f && b.colors.highlightedColor.r > 1f);
        Image originalImage = (Image)original.targetGraphic;
        Image originalPanel = debug.GetComponentsInChildren<Image>(true).First(i => i.name == "Debug Pin Overlay");
        var sets = new PlayerVoiceAudition.VoiceSet[Names.Length];
        for (int i = 0; i < sets.Length; i++)
            sets[i] = new PlayerVoiceAudition.VoiceSet { name = Names[i], attack = Clips(i, "Attack"),
                hurt = Clips(i, "Hurt"), death = Clips(i, "Death"), jump = Clips(i, "Jump") };

        var preview = EditorSceneManager.NewPreviewScene();
        GameObject root = new GameObject("PF_PlayerVoiceAudition", typeof(RectTransform));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
        root.SetActive(false);
        try
        {
            Canvas canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 29000;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            root.AddComponent<GraphicRaycaster>();
            AudioSource source = root.AddComponent<AudioSource>(); source.playOnAwake = false; source.loop = false;
            source.spatialBlend = 0f; source.volume = .8f; source.pitch = 1f; source.priority = 40;
            RectTransform panel = Rect(root.transform, "Voice Audition", new Vector2(640, 126));
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, 1f); panel.anchoredPosition = new Vector2(0, -18);
            Image background = panel.gameObject.AddComponent<Image>(); background.sprite = originalPanel.sprite;
            background.type = originalPanel.type; background.color = originalPanel.color; background.raycastTarget = true;
            Text(panel, "Title", "임시 캐릭터 음성 비교", style, new Vector2(0, -12), new Vector2(608, 22));
            var selectors = new Button[5];
            for (int i = 0; i < selectors.Length; i++)
                selectors[i] = Button(panel, i < 4 ? Names[i] : "음성 끄기", style, original, originalImage, new Vector2(-244 + i * 122, -48), new Vector2(114, 28));
            var samples = new Button[4]; string[] kinds = { "공격", "피격", "사망", "점프" };
            for (int i = 0; i < samples.Length; i++)
                samples[i] = Button(panel, kinds[i] + " 듣기", style, original, originalImage, new Vector2(-228 + i * 152, -82), new Vector2(144, 26));
            var status = Text(panel, "Status", "", style, new Vector2(0, -113), new Vector2(608, 20));
            status.fontSize = 12f; status.color = style.label;
            root.AddComponent<PlayerVoiceAudition>().Configure(sets, panel, selectors, samples, status, source, style.button, style.tabSelected);
            root.SetActive(true);
            PrefabUtility.SaveAsPrefabAsset(root, PlayerVoiceAudition.PrefabPath);
        }
        finally { Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(preview); }
    }

    private static AudioClip[] Clips(int candidate, string kind)
    {
        string folder = Source + Folders[candidate];
        var clips = AssetDatabase.FindAssets("t:AudioClip", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => Path.GetFileName(p).StartsWith("Female_" + kind + "_", StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal).Select(AssetDatabase.LoadAssetAtPath<AudioClip>).ToArray();
        if (clips.Length == 0 || clips.Any(c => c == null)) throw new InvalidOperationException(Names[candidate] + " " + kind + " is missing");
        return clips;
    }

    private static RectTransform Rect(Transform parent, string name, Vector2 size)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.gameObject.layer = 5; rect.SetParent(parent, false); rect.sizeDelta = size;
        return rect;
    }
    private static TextMeshProUGUI Text(Transform parent, string name, string value, DebugHubStyle style, Vector2 position, Vector2 size)
    {
        RectTransform rect = Rect(parent, name, size); rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
        rect.anchoredPosition = position;
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>(); label.font = style.font; label.fontSize = 14;
        label.color = style.text; label.text = value; label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false; label.textWrappingMode = TextWrappingModes.NoWrap; label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }
    private static Button Button(Transform parent, string name, DebugHubStyle style, Button original, Image originalImage, Vector2 position, Vector2 size)
    {
        RectTransform rect = Rect(parent, name, size); rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f); rect.anchoredPosition = position;
        Image image = rect.gameObject.AddComponent<Image>(); image.sprite = originalImage.sprite; image.type = originalImage.type;
        image.color = style.button; image.raycastTarget = true;
        Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.transition = original.transition;
        button.colors = original.colors; button.navigation = new Navigation { mode = Navigation.Mode.None };
        TextMeshProUGUI label = Text(rect, "Label", name, style, Vector2.zero, size);
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        return button;
    }
}
#endif
