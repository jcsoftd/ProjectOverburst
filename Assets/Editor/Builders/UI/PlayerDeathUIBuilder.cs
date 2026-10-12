using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TextCore.LowLevel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class PlayerDeathUIBuilder
{
    public const string Root = "Assets/ProjectOverburst/Resources/UI/Death";
    public const string PrefabPath = Root + "/PF_OverburstDeathScreen.prefab";
    const string Evidence = "../개인파일/코덱스산출/Implementation/20261013_PlayerDeath";
    const string Approved = "../개인파일/코덱스산출/UI/UIWorkbench";
    static TMP_FontAsset serif, body;
    static Color C(string value) { ColorUtility.TryParseHtmlString(value, out var color); return color; }
    static RectTransform At(Transform parent, string name, float x, float y, float w, float h)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.gameObject.layer = 5;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); return rect;
    }
    static RectTransform Stretch(Transform parent, string name)
    { var r = At(parent, name, 0, 0, 0, 0); r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; return r; }
    static TMP_Text Text(Transform p, string name, string value, float x, float y, float w, float h, float size, bool heading, string color)
    {
        var t = At(p, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        t.font = heading ? serif : body; t.text = value; t.fontSize = size; t.color = C(color);
        t.alignment = TextAlignmentOptions.Center; t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow; t.raycastTarget = false; return t;
    }
    static CanvasGroup Group(Transform root, string name) => Stretch(root, name).gameObject.AddComponent<CanvasGroup>();
    static void Folder(string path)
    { if (AssetDatabase.IsValidFolder(path)) return; Folder(Path.GetDirectoryName(path).Replace('\\', '/')); AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path)); }
    static TMP_FontAsset FontAsset(string name, string source)
    {
        string path = Root + "/" + name + ".asset";
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font) return font;
        font = TMP_FontAsset.CreateFontAsset(AssetDatabase.LoadAssetAtPath<Font>(source), 96, 8, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, false);
        font.name = name; AssetDatabase.CreateAsset(font, path);
        foreach (var texture in font.atlasTextures) if (texture && !AssetDatabase.Contains(texture)) AssetDatabase.AddObjectToAsset(texture, font);
        if (!AssetDatabase.Contains(font.material)) AssetDatabase.AddObjectToAsset(font.material, font);
        font.TryAddCharacters("DEFEATED0123456789% ·….마을로 돌아가기정산 저장 중원정 결과를 저장하고 있습니다.정산을 저장하지 못했습니다. 다시 시도해 주세요.마을로 이동하지 못했습니다. 다시 시도해 주세요.이번 원정이 끝났습니다.미반출 전리품은 잃습니다.기존 보유품 · 전송품 · 획득 경험치는 유지됩니다.야외마을에서 다시 준비할 수 있습니다.보유한 전리품은 유지됩니다.마을로 돌아가 체력을 회복합니다.사막 · 지도 런동굴 · 지도 런마을에서 체력을 회복하고 다시 준비할 수 있습니다.");
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        EditorUtility.SetDirty(font); AssetDatabase.SaveAssetIfDirty(font); return font;
    }
    [MenuItem("OVERBURST/UI/사망 화면/승인안 만들기")]
    public static void Build()
    {
        AppearanceCustomizationBuilder.RequireIdle();
        string before = HideoutCatalogLayoutBuilder.EditorSnapshot(); Folder(Root);
        serif = FontAsset("DeathSerif", "Assets/ProjectOverburst/Resources/UI/Fonts/Quest/NotoSerifKR_Semibold.ttf");
        body = FontAsset("DeathBody", "Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/Pretendard_Medium.ttf");
        string audioPath = Root + "/LowBoom_150.wav";
        if (!File.Exists(audioPath)) File.Copy(Path.GetFullPath(Approved + "/assets/death/audio/low-boom-150.wav"), audioPath);
        AssetDatabase.ImportAsset(audioPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (AudioImporter)AssetImporter.GetAtPath(audioPath); var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad; settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate; importer.defaultSampleSettings = settings;
        importer.forceToMono = false; importer.SaveAndReimport();
        string materialPath = Root + "/DeathWorldBackdrop.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (!material) { material = new Material(Shader.Find("Overburst/UI/Title Backdrop")); AssetDatabase.CreateAsset(material, materialPath); }
        material.SetFloat("_Saturation", .35f); material.SetFloat("_Brightness", .65f); material.SetFloat("_Vignette", 0);
        EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
        var scene = EditorSceneManager.NewPreviewScene(); GameObject root = null;
        try
        {
            root = new GameObject("PF_OverburstDeathScreen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(root, scene); root.SetActive(false); root.layer = 5;
            var screen = root.AddComponent<OverburstDeathScreen>(); screen.canvas = root.GetComponent<Canvas>();
            screen.canvas.renderMode = RenderMode.ScreenSpaceOverlay; screen.canvas.sortingOrder = 900;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var letterbox = Stretch(root.transform, "Letterbox").gameObject.AddComponent<Image>(); letterbox.color = new Color(9 / 255f, 11 / 255f, 8 / 255f, .35f); letterbox.raycastTarget = true;
            var stage = At(root.transform, "Stage", 0, 0, 1920, 1080); stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(.5f, .5f); stage.anchoredPosition = Vector2.zero;
            var live = Stretch(stage, "World Snapshot").gameObject.AddComponent<RawImage>(); live.material = material; live.color = Color.white; live.raycastTarget = false; live.enabled = false; screen.worldBackdrop = live;
            for (int i = 0; i < 3; i++)
            {
                float[] y = { 0, 324, 734.4f, 1080 }; float[] a = { .56f, .74f, .78f, .89f };
                var veil = At(stage, "Veil " + i, 0, y[i], 1920, y[i + 1] - y[i]).gameObject.AddComponent<QuestUiGradient>();
                veil.top = new Color(3 / 255f, 5 / 255f, 3 / 255f, a[i]); veil.bottom = new Color(3 / 255f, 5 / 255f, 3 / 255f, a[i + 1]); veil.raycastTarget = false;
            }
            screen.headingGroup = Group(stage, "Heading");
            screen.location = Text(screen.headingGroup.transform, "Location", "사막 · 지도 런", 280, 265, 1360, 22, 18, false, "#B49B69");
            screen.location.characterSpacing = 5f / 18 * 100;
            var title = Text(screen.headingGroup.transform, "DEFEATED", "DEFEATED", 280, 331, 1360, 145.6f, 112, true, "#BD6857"); title.characterSpacing = 10f / 112 * 100;
            screen.message = Text(screen.headingGroup.transform, "Message", "이번 원정이 끝났습니다.", 280, 491, 1360, 35, 24, true, "#D5C6B0");
            screen.detailsGroup = Group(stage, "Details");
            var separator = At(screen.detailsGroup.transform, "Separator", 620, 576, 680, 3).gameObject.AddComponent<Image>();
            separator.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/ThirdParty/RPG and MMO UI 11/Textures/Controls/Separators/Separator_Horizontal.png"); separator.color = new Color(1, 1, 1, .5f); separator.raycastTarget = false;
            screen.loss = Text(screen.detailsGroup.transform, "Loss", "미반출 전리품은 잃습니다.", 280, 613, 1360, 33, 22, false, "#BDA78E");
            screen.keep = Text(screen.detailsGroup.transform, "Keep", "기존 보유품 · 전송품 · 획득 경험치는 유지됩니다.", 280, 658, 1360, 30, 20, false, "#9F9E8F");
            screen.actionGroup = Group(stage, "Actions");
            var action = At(screen.actionGroup.transform, "Return", 800, 730, 320, 52);
            var hit = action.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            var fill = Stretch(action, "Reviewed Fill").gameObject.AddComponent<QuestUiGradient>(); fill.top = C("#4F2721"); fill.bottom = C("#24130F"); fill.raycastTarget = false;
            var texture = Stretch(action, "Reviewed Texture").gameObject.AddComponent<Image>(); texture.sprite = Resources.Load<Sprite>("UI/Reviewed/simple"); texture.type = Image.Type.Tiled; texture.raycastTarget = false;
            var border = Stretch(action, "Reviewed Border").gameObject.AddComponent<Image>(); border.sprite = Resources.Load<Sprite>("UI/Reviewed/border"); border.type = Image.Type.Sliced; border.pixelsPerUnitMultiplier = 2; border.color = new Color(1, 1, 1, .72f); border.raycastTarget = false;
            screen.returnButton = action.gameObject.AddComponent<Button>(); screen.returnButton.targetGraphic = fill;
            var colors = screen.returnButton.colors; colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f); colors.pressedColor = new Color(.85f, .85f, .85f); colors.selectedColor = new Color(1.2f, 1.14f, .95f); screen.returnButton.colors = colors;
            action.gameObject.AddComponent<CanvasGroup>(); action.gameObject.AddComponent<QuestUiButtonOpacity>();
            screen.buttonLabel = Text(action, "Label", "마을로 돌아가기", 10, 0, 300, 52, 20, true, "#E3CFAF");
            screen.status = Text(screen.actionGroup.transform, "Status", "", 280, 798, 1360, 30, 18, false, "#C5B791");
            Text(stage, "Footer", "마을에서 체력을 회복하고 다시 준비할 수 있습니다.", 280, 1008, 1360, 27, 17, false, "#817F70");
            screen.sound = root.AddComponent<AudioSource>(); screen.sound.playOnAwake = false; screen.sound.spatialBlend = 0; screen.sound.volume = .4041f; screen.sound.ignoreListenerPause = true;
            screen.lowBoom150 = AssetDatabase.LoadAssetAtPath<AudioClip>(audioPath);
            if (!separator.sprite || !texture.sprite || !border.sprite || !screen.lowBoom150) throw new InvalidOperationException("승인 자산 참조 누락");
            root.SetActive(true); PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { if (root) Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
        if (before != HideoutCatalogLayoutBuilder.EditorSnapshot()) throw new InvalidOperationException("사용자 씬 변경됨");
        Directory.CreateDirectory(Path.GetFullPath(Evidence));
        File.WriteAllText(Path.GetFullPath(Evidence + "/build.json"), "{\"status\":\"PASS\",\"prefab\":\"" + PrefabPath + "\",\"userScenesUnchanged\":true}");
    }
}
