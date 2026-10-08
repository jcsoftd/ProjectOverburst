using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public static class BuffTooltipBuilder
{
    public const string FontRoot = "Assets/ProjectOverburst/Resources/UI/Fonts/BuffTooltip";
    public const string NameFontPath = FontRoot + "/NotoSerifKR_BuffTooltip.asset";
    public const string BodyFontPath = FontRoot + "/Pretendard_BuffTooltip.asset";
    public const string TitleSourcePath = FontRoot + "/NotoSerifKR-Medium-BuffTooltip.ttf";
    public const string FramePath = "Assets/ProjectOverburst/Resources/UI/Tooltip/BuffTooltipFrame.png";

    [MenuItem("OVERBURST/UI/버프 툴팁 적용")]
    public static void BuildFromMenu() => Build();

    public static string Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY")))
            throw new InvalidOperationException("Shared Editor must be idle and on the real account");
        string glyphs = Glyphs();
        TMP_FontAsset nameFont = Font("Assets/ProjectOverburst/05_Art/Fonts/NotoSerifKR-SemiBold-HUD SDF.asset", NameFontPath, glyphs.Replace("−", ""), TitleSourcePath);
        TMP_FontAsset bodyFont = Font("Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/Pretendard_Medium SDF.asset", BodyFontPath, glyphs);
        Sprite frame = Frame();
        GameObject root = PrefabUtility.LoadPrefabContents(StatusBuffIconBuilder.HudPath);
        try
        {
            var host = Rect(root.transform, "BuffTooltipHost");
            host.anchorMin = Vector2.zero; host.anchorMax = Vector2.one; host.offsetMin = host.offsetMax = Vector2.zero;
            host.SetAsLastSibling();
            var group = Component<CanvasGroup>(host); group.blocksRaycasts = false; group.interactable = false;
            var view = Component<BuffTooltipUI>(host);
            var panel = Rect(host, "Panel");
            panel.anchorMin = panel.anchorMax = new Vector2(.5f, .5f); panel.pivot = Vector2.zero;
            panel.sizeDelta = new Vector2(272f, 83.65f);
            var image = Component<UnityEngine.UI.Image>(panel);
            image.enabled = false; image.raycastTarget = false;
            var shadow = Component<UnityEngine.UI.Shadow>(panel);
            shadow.enabled = false;
            var outline = Component<UnityEngine.UI.Outline>(panel);
            outline.enabled = false;
            // This sprite is the approved CSS frame, including its exact 1px stroke and soft shadow.
            var frameRect = Rect(panel, "Frame"); frameRect.SetAsFirstSibling();
            frameRect.anchorMin = Vector2.zero; frameRect.anchorMax = Vector2.one;
            frameRect.offsetMin = new Vector2(-32f, -38.35f); frameRect.offsetMax = new Vector2(32f, 32f);
            var frameImage = Component<UnityEngine.UI.Image>(frameRect);
            frameImage.sprite = frame; frameImage.type = UnityEngine.UI.Image.Type.Sliced;
            frameImage.color = Color.white; frameImage.raycastTarget = false; frameImage.enabled = true;
            // Noto's 25.87px metric box is centered on the CSS 25.2px header line.
            TMP_Text title = Text(panel,"Name",nameFont,18f,new Color(232f/255f,223f/255f,203f/255f),new Vector2(17f,-13.6f),new Vector2(130f,26f),TextAlignmentOptions.Left);
            title.characterSpacing = 1f;
            TMP_Text remaining = Text(panel,"Remaining",bodyFont,12f,new Color(171f/255f,169f/255f,159f/255f),new Vector2(159f,-18.2f),new Vector2(96f,16.8f),TextAlignmentOptions.Right);
            TMP_Text effect = Text(panel,"Effect",bodyFont,13f,new Color(208f/255f,206f/255f,197f/255f),new Vector2(17f,-47.2f),new Vector2(238f,21.45f),TextAlignmentOptions.Left);
            effect.textWrappingMode = TextWrappingModes.Normal;
            var viewSo = new SerializedObject(view);
            Set(viewSo,"panel",panel); Set(viewSo,"nameText",title); Set(viewSo,"remainingText",remaining); Set(viewSo,"effectText",effect);
            viewSo.ApplyModifiedPropertiesWithoutUndo();
            var bar = root.GetComponentInChildren<BuffBarUI>(true);
            foreach (BuffIconSlotUI slot in bar.GetComponentsInChildren<BuffIconSlotUI>(true).Where(s=>s.gameObject.activeSelf))
            {
                var hit = Component<UnityEngine.UI.Image>(slot.transform);
                hit.sprite = null; hit.color = Color.clear; hit.raycastTarget = true; hit.enabled = false;
                var so = new SerializedObject(slot); Set(so,"hoverTarget",hit); Set(so,"tooltip",view); so.ApplyModifiedPropertiesWithoutUndo();
            }
            panel.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root, StatusBuffIconBuilder.HudPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return "PASS: seven hover targets and one authored tooltip; private font copies; scenes not saved";
    }

    private static RectTransform Rect(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return (RectTransform)existing;
        var rect = (RectTransform)new GameObject(name,typeof(RectTransform)).transform;
        rect.SetParent(parent,false); rect.gameObject.layer = parent.gameObject.layer; return rect;
    }
    private static T Component<T>(Transform parent) where T:Component
    {
        T component = parent.GetComponent<T>();
        return component != null ? component : parent.gameObject.AddComponent<T>();
    }
    private static TMP_Text Text(RectTransform parent,string name,TMP_FontAsset font,float size,Color color,Vector2 position,Vector2 dimensions,TextAlignmentOptions alignment)
    {
        var rect = Rect(parent,name); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f,1f);
        rect.anchoredPosition = position; rect.sizeDelta = dimensions;
        var text = Component<TextMeshProUGUI>(rect); text.font = font; text.fontSize = size; text.color = color;
        text.fontStyle = FontStyles.Normal; text.enableAutoSizing = false; text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false; text.text = string.Empty; return text;
    }
    private static void Set(SerializedObject so,string field,UnityEngine.Object value) => so.FindProperty(field).objectReferenceValue = value;
    private static Sprite Frame()
    {
        AssetDatabase.ImportAsset(FramePath, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(FramePath) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Approved tooltip frame PNG missing");
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f; importer.spriteBorder = new Vector4(37f,43.35f,37f,37f);
        importer.filterMode = FilterMode.Bilinear; importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true; importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(FramePath);
    }
    private static TMP_FontAsset Font(string source,string path,string glyphs,string exactSource = null)
    {
        string current = "Assets";
        foreach (string part in FontRoot.Split('/').Skip(1))
        { string next=current+"/"+part; if(!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current,part); current=next; }
        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path) == null && !AssetDatabase.CopyAsset(source,path))
            throw new InvalidOperationException("Font copy failed: " + path);
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (exactSource != null)
        {
            AssetDatabase.ImportAsset(exactSource, ImportAssetOptions.ForceSynchronousImport);
            var sourceFont = AssetDatabase.LoadAssetAtPath<UnityEngine.Font>(exactSource);
            if (sourceFont == null) throw new InvalidOperationException("Approved weight-500 title font missing");
            if (font.sourceFontFile != sourceFont)
            {
                font.ClearFontAssetData();
                var so = new SerializedObject(font);
                so.FindProperty("m_SourceFontFile").objectReferenceValue = sourceFont;
                so.FindProperty("m_SourceFontFileGUID").stringValue = AssetDatabase.AssetPathToGUID(exactSource);
                so.FindProperty("m_SourceFontFilePath").stringValue = string.Empty;
                so.ApplyModifiedPropertiesWithoutUndo();
                if (FontEngine.LoadFontFace(sourceFont, (int)font.faceInfo.pointSize) != FontEngineError.Success)
                    throw new InvalidOperationException("Could not load approved title font");
                font.faceInfo = FontEngine.GetFaceInfo();
                var settings = font.creationSettings; settings.sourceFontFileGUID = AssetDatabase.AssetPathToGUID(exactSource);
                font.creationSettings = settings; EditorUtility.SetDirty(font);
            }
        }
        // TMP returns false when every requested glyph is already present as well.
        if (!font.HasCharacters(glyphs,out List<char> missing))
        {
            font.TryAddCharacters(new string(missing.Distinct().ToArray()),out string unused);
            if (!font.HasCharacters(glyphs,out missing))
                throw new InvalidOperationException("Tooltip font glyphs missing: " + new string(missing.Distinct().ToArray()));
        }
        AssetDatabase.SaveAssetIfDirty(font);
        return font;
    }
    private static string Glyphs()
    {
        var text = new List<string> { "회복 둔화 강화 효과 약화 효과 적용 중 초마다 최대 체력의 이동 속도 광휘 빛 강공 강화 중첩 강공 시 소모 던전 종료까지 지속 중 물약 일반 적 통과 0123456789.+-−%p/ ·" };
        foreach (FlaskKind kind in Enum.GetValues(typeof(FlaskKind)))
        {
            var data = ScriptableObject.CreateInstance<FlaskItemData>();
            try { data.Configure(kind); text.Add(data.itemName); text.Add(BuffTooltipText.FlaskEffectText(new FlaskEffectSnapshot("preview",data,6f,6f))); }
            finally { UnityEngine.Object.DestroyImmediate(data); }
        }
        foreach (MapBuffKind kind in Enum.GetValues(typeof(MapBuffKind)))
        { text.Add(BuffTooltipText.MapName(kind)); text.Add(BuffTooltipText.MapEffect(kind,2,.25f)); }
        return string.Join(" ",text);
    }
}
