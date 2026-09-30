using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Idempotent, prefab-only authoring of the approved E background + C layout.</summary>
public static class OverburstTooltipHybridApplier
{
    private const string PrefabPath = "Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstTooltip_Rpg11.prefab";
    private const string GlowPath = "Assets/ProjectOverburst/02_Shared/UI/Art/Tooltip/TooltipTopGlow.png";
    private static readonly string[] EdgeNames = { "Top", "Right", "Bottom", "Left" };

    [MenuItem("OVERBURST/UI/Apply Approved Item Tooltip E+C")]
    public static void ApplyMenu() => Debug.Log("[ApprovedTooltip] " + ApplyToPrefab());

    public static string ApplyToPrefab(string path = PrefabPath)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode before editing the tooltip prefab.");
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(path)) throw new FileNotFoundException("Tooltip prefab", path);
        Sprite glowSprite = EnsureGlowSprite();
        GameObject prefab = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Image panel = prefab.GetComponent<Image>();
            OverburstUITooltipView view = prefab.GetComponent<OverburstUITooltipView>();
            if (!panel || !view) throw new InvalidOperationException("The RPG11 tooltip root is incomplete.");
            panel.sprite = null;
            panel.type = Image.Type.Simple;
            panel.color = Html("#15191B");
            panel.raycastTarget = false;
            RectTransform root = (RectTransform)prefab.transform;
            root.sizeDelta = new Vector2(432f, Mathf.Max(root.sizeDelta.y, 440f));

            Image surface = prefab.transform.Find("Opaque Surface")?.GetComponent<Image>();
            if (!surface) throw new InvalidOperationException("Opaque Surface is missing.");
            Stretch(surface.rectTransform, 0f);
            surface.sprite = null;
            surface.color = Html("#15191B");
            surface.raycastTarget = false;
            surface.transform.SetSiblingIndex(0);

            Image glow = ImageAt(root, "Approved Top Glow", 0f, 0f, 432f, 220f,
                new Color(1f, 1f, 1f, .18f));
            glow.sprite = glowSprite;
            glow.preserveAspect = false;
            RectTransform glowRect = glow.rectTransform;
            glowRect.anchorMin = new Vector2(0f, 1f);
            glowRect.anchorMax = new Vector2(1f, 1f);
            glowRect.pivot = new Vector2(.5f, 1f);
            glowRect.anchoredPosition = Vector2.zero;
            glowRect.sizeDelta = new Vector2(0f, 220f);
            glow.transform.SetSiblingIndex(1);

            RectTransform outer = Frame(root, "Approved Outer Frame", 0f, 1f, Html("#6886A1"));
            outer.SetSiblingIndex(2);
            RectTransform darkInset = Frame(root, "Approved Dark Inset", 1f, 4f, Html("#171B1D"));
            darkInset.SetSiblingIndex(3);
            RectTransform inner = Frame(root, "Approved Inner Frame", 7f, 1f, Html("#32434B"));
            inner.SetSiblingIndex(4);

            Image iconFrame = ImageAt(root, "Approved Icon Frame", 26f, 24f, 72f, 72f, Html("#222629"));
            Frame(iconFrame.rectTransform, "Icon Border", 0f, 1f, Html("#7E776C"));
            Image icon = ImageAt(iconFrame.rectTransform, "Icon Display", 4f, 4f, 64f, 64f, Color.white);
            icon.preserveAspect = true;
            icon.sprite = null;
            iconFrame.transform.SetSiblingIndex(5);
            Transform oldSlot = root.Find("Item Icon");
            if (oldSlot) oldSlot.gameObject.SetActive(false);

            Image badge = ImageAt(root, "Approved Badge Fill", 366f, 26f, 40f, 24f, Html("#1C2B38"));
            Frame(badge.rectTransform, "Badge Frame", 0f, 1f, Html("#80B5E6"));
            badge.transform.SetSiblingIndex(6);

            TextMeshProUGUI title = RequireText(root, "Item Name");
            TextMeshProUGUI subtitle = RequireText(root, "Item Type");
            TextMeshProUGUI grade = RequireText(root, "Item Grade");
            TextMeshProUGUI body = RequireText(root, "Item Details");
            Image headerRule = root.Find("Header Rule")?.GetComponent<Image>();
            if (!headerRule) throw new InvalidOperationException("Header Rule is missing.");
            Place(title.rectTransform, 114f, 26f, 244f, 74f);
            title.fontSize = 23f;
            title.color = Html("#EAD6AC");
            title.textWrappingMode = TextWrappingModes.Normal;
            subtitle.fontSize = 13f;
            subtitle.color = Html("#ADA9A3");
            Place(subtitle.rectTransform, 114f, 65f, 274f, 46f);
            grade.fontSize = 12f;
            grade.alignment = TextAlignmentOptions.Center;
            Place(grade.rectTransform, 366f, 27f, 40f, 22f);
            headerRule.color = Html("#68635A");
            Place(headerRule.rectTransform, 26f, 115f, 380f, 1f);
            body.fontSize = 15f;
            body.lineSpacing = 8f;
            Place(body.rectTransform, 26f, 146f, 380f, 300f);

            RectTransform content = Rect(root, "Approved Content", 0f, 0f, 432f, 440f);
            Stretch(content, 0f);
            TMP_FontAsset font = body.font;
            TextAt(content, "Primary Heading", font, 12f, Html("#B6A789"), TextAlignmentOptions.Left);
            TextAt(content, "Quality Heading", font, 12f, Html("#8D9896"), TextAlignmentOptions.Right);
            TextAt(content, "Secondary Heading", font, 12f, Html("#B6A789"), TextAlignmentOptions.Left);
            ImageAt(content, "Secondary Rule", 26f, 0f, 380f, 1f, Html("#766B58"));
            TextAt(content, "Price Label", font, 14f, Html("#ABA49A"), TextAlignmentOptions.MidlineLeft);
            TextAt(content, "Price Value", font, 14f, Html("#E8D5AA"), TextAlignmentOptions.MidlineRight);
            ImageAt(content, "Price Rule", 26f, 0f, 380f, 1f, Html("#786E5E"));
            TextMeshProUGUI footer = TextAt(content, "Footer Text", font, 13f, Html("#AAB0AE"), TextAlignmentOptions.TopLeft);
            footer.textWrappingMode = TextWrappingModes.Normal;
            ImageAt(content, "Footer Rule", 26f, 0f, 380f, 1f, Html("#5C5D59"));

            RectTransform statRows = Rect(content, "Stat Rows", 0f, 0f, 432f, 440f);
            Stretch(statRows, 0f);
            for (int i = 0; i < 16; i++)
            {
                RectTransform row = Rect(statRows, "Row " + i.ToString("00"), 26f, 0f, 380f, 34f);
                TextAt(row, "Label", font, 15f, Html("#BCB7AD"), TextAlignmentOptions.MidlineLeft);
                Place(row.Find("Label") as RectTransform, 0f, 0f, 140f, 34f);
                TextAt(row, "Value", font, 15f, Html("#E8D9B9"), TextAlignmentOptions.MidlineRight);
                Place(row.Find("Value") as RectTransform, 70f, 0f, 160f, 40f);
                TextMeshProUGUI marks = TextAt(row, "Marks", font, 19f, Color.white, TextAlignmentOptions.MidlineRight);
                marks.spriteAsset = Resources.Load<TMP_SpriteAsset>("OverburstUI/QualityDiamonds");
                marks.textWrappingMode = TextWrappingModes.NoWrap;
                Place(marks.rectTransform, 244f, 0f, 136f, 40f);
                Image line = ImageAt(row, "Row Rule", 0f, 0f, 380f, 1f, Html("#262828"));
                RectTransform lr = line.rectTransform;
                lr.anchorMin = new Vector2(0f, 0f);
                lr.anchorMax = new Vector2(1f, 0f);
                lr.pivot = new Vector2(0f, 0f);
                lr.anchoredPosition = Vector2.zero;
                lr.sizeDelta = new Vector2(0f, 1f);
                row.gameObject.SetActive(false);
            }
            content.SetAsLastSibling();
            if (!prefab.GetComponent<OverburstTooltipHybridSkin>()) prefab.AddComponent<OverburstTooltipHybridSkin>();
            foreach (Graphic graphic in prefab.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            PrefabUtility.SaveAsPrefabAsset(prefab, path);
            AssetDatabase.SaveAssets();
            return "PASS " + path + " (432px, E surface, C sections, 16 reusable stat rows)";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefab);
        }
    }

    private static Sprite EnsureGlowSprite()
    {
        string absolutePath = Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", GlowPath));
        if (!File.Exists(absolutePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            const int width = 256, height = 128;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float horizontal = (x + .5f - width * .5f) / (width * .62f);
                float spread = Mathf.Pow(Mathf.Clamp01(1f - horizontal * horizontal), 1.35f);
                float vertical = Mathf.Pow((y + .5f) / height, 2.2f);
                pixels[y * width + x] = new Color(1f, 1f, 1f, .25f * spread * vertical);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(GlowPath, ImportAssetOptions.ForceSynchronousImport);
        }
        TextureImporter importer = AssetImporter.GetAtPath(GlowPath) as TextureImporter;
        if (!importer) throw new InvalidOperationException("Tooltip glow texture importer is missing.");
        if (importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled || !importer.alphaIsTransparency)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(GlowPath);
        if (!sprite) throw new InvalidOperationException("Tooltip glow sprite failed to import.");
        return sprite;
    }

    private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
    {
        Transform found = parent.Find(name);
        RectTransform rect;
        if (found) rect = found as RectTransform;
        else
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            rect = (RectTransform)go.transform;
        }
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        Place(rect, x, y, width, height);
        return rect;
    }

    private static Image ImageAt(Transform parent, string name, float x, float y, float width, float height, Color color)
    {
        RectTransform rect = Rect(parent, name, x, y, width, height);
        Image image = rect.GetComponent<Image>();
        if (!image) image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TextMeshProUGUI TextAt(Transform parent, string name, TMP_FontAsset font, float size,
        Color color, TextAlignmentOptions alignment)
    {
        RectTransform rect = Rect(parent, name, 0f, 0f, 100f, 24f);
        TextMeshProUGUI text = rect.GetComponent<TextMeshProUGUI>();
        if (!text) text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.richText = true;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static RectTransform Frame(Transform parent, string name, float inset, float thickness, Color color)
    {
        RectTransform frame = Rect(parent, name, 0f, 0f, 0f, 0f);
        Stretch(frame, inset);
        foreach (string edgeName in EdgeNames)
        {
            Image edge = ImageAt(frame, edgeName, 0f, 0f, 1f, 1f, color);
            RectTransform r = edge.rectTransform;
            if (edgeName == "Top")
            {
                r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f);
                r.pivot = new Vector2(0f, 1f); r.sizeDelta = new Vector2(0f, thickness);
            }
            else if (edgeName == "Bottom")
            {
                r.anchorMin = new Vector2(0f, 0f); r.anchorMax = new Vector2(1f, 0f);
                r.pivot = new Vector2(0f, 0f); r.sizeDelta = new Vector2(0f, thickness);
            }
            else if (edgeName == "Left")
            {
                r.anchorMin = new Vector2(0f, 0f); r.anchorMax = new Vector2(0f, 1f);
                r.pivot = new Vector2(0f, 0f); r.sizeDelta = new Vector2(thickness, 0f);
            }
            else
            {
                r.anchorMin = new Vector2(1f, 0f); r.anchorMax = new Vector2(1f, 1f);
                r.pivot = new Vector2(1f, 0f); r.sizeDelta = new Vector2(thickness, 0f);
            }
            r.anchoredPosition = Vector2.zero;
        }
        return frame;
    }

    private static TextMeshProUGUI RequireText(Transform root, string name)
    {
        TextMeshProUGUI text = root.Find(name)?.GetComponent<TextMeshProUGUI>();
        if (!text) throw new InvalidOperationException("Tooltip text missing: " + name);
        return text;
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(.5f, .5f);
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static Color Html(string hex)
    {
        if (!ColorUtility.TryParseHtmlString(hex, out Color result)) throw new ArgumentException(hex);
        return result;
    }
}
