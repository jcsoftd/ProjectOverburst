using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The approved E surface and C information layout. The existing tooltip formatter remains
/// the source of item values; this component only arranges their presentation.
/// </summary>
public sealed class OverburstTooltipHybridSkin : MonoBehaviour
{
    private const int RowCapacity = 16;
    private const float ContentWidth = 380f;
    private const int MarksPerLine = 6;
    private const float ExtraMarkLineHeight = 20f;
    private const float ValueColumnX = 70f;
    private const float ValueColumnWidth = 160f;
    private const float MarksColumnX = 244f;
    private const float MarksColumnWidth = 136f;
    private static readonly Regex Tags = new Regex("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex NumericRow = new Regex(@"^(.{1,14}?)\s*:?\s+([(+\-−]?\d.*)$", RegexOptions.Compiled);
    private static readonly Regex Stars = new Regex(@"(?:<color=([^>]+)>)?[★◆]+(?:</color>)?", RegexOptions.Compiled);
    private static readonly Regex VisibleStars = new Regex(@"[★◆].*$", RegexOptions.Compiled);
    private static readonly Regex SpriteMark = new Regex(@"<sprite index=[0-3] tint=0>", RegexOptions.Compiled);

    private sealed class Row
    {
        public RectTransform root;
        public TextMeshProUGUI label;
        public TextMeshProUGUI value;
        public TextMeshProUGUI marks;
    }

    private sealed class Stat
    {
        public string label;
        public string value;
        public string delta;
        public string marks;
        public bool improved;
    }

    private RectTransform panel;
    private Image glow;
    private Image icon;
    private Image badge;
    private Image[] outerEdges;
    private Image[] innerEdges;
    private Image[] badgeEdges;
    private Image headerRule;
    private Image secondaryRule;
    private Image priceRule;
    private Image footerRule;
    private TextMeshProUGUI title;
    private TextMeshProUGUI subtitle;
    private TextMeshProUGUI grade;
    private TextMeshProUGUI primaryHeading;
    private TextMeshProUGUI qualityHeading;
    private TextMeshProUGUI secondaryHeading;
    private TextMeshProUGUI priceLabel;
    private TextMeshProUGUI priceValue;
    private TextMeshProUGUI footer;
    private Row[] rows;
    private bool resolved;

    public bool TryPresent(ItemData item, string priceOverride, string[] rawLines, TextMeshProUGUI legacyBody)
    {
        if (item == null || !item.baseData || !Resolve())
            return false;

        Color rarity = TooltipTone(item.grade);
        glow.color = new Color(rarity.r, rarity.g, rarity.b, .18f);
        Color border = Color.Lerp(Html("#454441"), rarity, .62f);
        SetEdges(outerEdges, border);
        SetEdges(innerEdges, new Color(rarity.r, rarity.g, rarity.b, .08f));
        SetEdges(badgeEdges, rarity);
        badge.color = item.grade == ItemGrade.Rare ? Html("#1C2B38")
            : item.grade == ItemGrade.Epic ? Html("#2B2034")
            : Color.Lerp(Html("#15191B"), rarity, .15f);
        icon.sprite = item.icon;
        icon.enabled = item.icon != null;
        grade.color = rarity;
        title.color = new Color(.92f, .84f, .68f);

        SetRect(title.rectTransform, 114f, 26f, 244f, 74f);
        title.ForceMeshUpdate();
        float nameHeight = Mathf.Max(31f, title.GetPreferredValues(title.text, 244f, 0f).y);
        float subtitleY = 26f + nameHeight + 8f;
        SetRect(subtitle.rectTransform, 114f, subtitleY, 274f, 46f);
        subtitle.ForceMeshUpdate();
        float subtitleHeight = Mathf.Max(18f, subtitle.GetPreferredValues(subtitle.text, 274f, 0f).y);
        float headerBottom = Mathf.Max(95f, subtitleY + subtitleHeight);
        float ruleY = headerBottom + 20f;
        SetRect(headerRule.rectTransform, 26f, ruleY, ContentWidth, 1f);

        HideStructuredContent();
        if (item.baseData is WeaponItemData || item.baseData is FlaskItemData || item.baseData is GearItemData)
        {
            List<Stat> stats = new List<Stat>(12);
            string notes = string.Empty;
            bool isFlask = item.baseData is FlaskItemData;
            if (item.baseData is GearItemData)
                CollectGearStats(item, rawLines, stats);
            else if (isFlask)
                CollectFlaskStats(item, rawLines, stats, out notes);
            else
                CollectWeaponStats(item, rawLines, stats);

            if (stats.Count > 0 && stats.Count <= RowCapacity)
            {
                legacyBody.enabled = false;
                RenderStats(item, isFlask, stats, notes, priceOverride, ruleY, legacyBody.spriteAsset);
                return true;
            }
        }

        RenderFallback(legacyBody, ruleY);
        return true;
    }

    private bool Resolve()
    {
        if (resolved) return true;
        panel = (RectTransform)transform;
        glow = Find<Image>("Approved Top Glow");
        icon = Find<Image>("Approved Icon Frame/Icon Display");
        badge = Find<Image>("Approved Badge Fill");
        outerEdges = FindEdges("Approved Outer Frame");
        innerEdges = FindEdges("Approved Inner Frame");
        badgeEdges = FindEdges("Approved Badge Fill/Badge Frame");
        title = Find<TextMeshProUGUI>("Item Name");
        subtitle = Find<TextMeshProUGUI>("Item Type");
        grade = Find<TextMeshProUGUI>("Item Grade");
        headerRule = Find<Image>("Header Rule");
        primaryHeading = Find<TextMeshProUGUI>("Approved Content/Primary Heading");
        qualityHeading = Find<TextMeshProUGUI>("Approved Content/Quality Heading");
        secondaryHeading = Find<TextMeshProUGUI>("Approved Content/Secondary Heading");
        secondaryRule = Find<Image>("Approved Content/Secondary Rule");
        priceLabel = Find<TextMeshProUGUI>("Approved Content/Price Label");
        priceValue = Find<TextMeshProUGUI>("Approved Content/Price Value");
        priceRule = Find<Image>("Approved Content/Price Rule");
        footer = Find<TextMeshProUGUI>("Approved Content/Footer Text");
        footerRule = Find<Image>("Approved Content/Footer Rule");
        rows = new Row[RowCapacity];
        for (int i = 0; i < rows.Length; i++)
        {
            string path = "Approved Content/Stat Rows/Row " + i.ToString("00", CultureInfo.InvariantCulture);
            RectTransform root = Find<RectTransform>(path);
            if (!root) return false;
            rows[i] = new Row
            {
                root = root,
                label = Find<TextMeshProUGUI>(path + "/Label"),
                value = Find<TextMeshProUGUI>(path + "/Value"),
                marks = Find<TextMeshProUGUI>(path + "/Marks")
            };
            if (!rows[i].label || !rows[i].value || !rows[i].marks) return false;
        }
        resolved = glow && icon && badge && title && subtitle && grade && headerRule &&
            primaryHeading && qualityHeading && secondaryHeading && secondaryRule &&
            priceLabel && priceValue && priceRule && footer && footerRule &&
            outerEdges.All(x => x) && innerEdges.All(x => x) && badgeEdges.All(x => x);
        return resolved;
    }

    private T Find<T>(string path) where T : Component
    {
        Transform child = transform.Find(path);
        return child ? child.GetComponent<T>() : null;
    }

    private Image[] FindEdges(string root)
    {
        return new[] { Find<Image>(root + "/Top"), Find<Image>(root + "/Right"),
            Find<Image>(root + "/Bottom"), Find<Image>(root + "/Left") };
    }

    private static void SetEdges(Image[] edges, Color color)
    {
        foreach (Image edge in edges) edge.color = color;
    }

    private static Color TooltipTone(ItemGrade rarity)
    {
        switch (rarity)
        {
            case ItemGrade.Rare: return Html("#80B5E6");
            case ItemGrade.Epic: return Html("#C994E7");
            case ItemGrade.Legendary: return Html("#D7AD69");
            default: return Color.Lerp(Html("#B8B4AB"), GradeConfig.GetGradeColor(rarity), .65f);
        }
    }

    private static Color Html(string hex)
    {
        if (!ColorUtility.TryParseHtmlString(hex, out Color color)) throw new ArgumentException(hex);
        return color;
    }

    private void HideStructuredContent()
    {
        primaryHeading.gameObject.SetActive(false);
        qualityHeading.gameObject.SetActive(false);
        secondaryHeading.gameObject.SetActive(false);
        secondaryRule.gameObject.SetActive(false);
        priceLabel.gameObject.SetActive(false);
        priceValue.gameObject.SetActive(false);
        priceRule.gameObject.SetActive(false);
        footer.gameObject.SetActive(false);
        footerRule.gameObject.SetActive(false);
        foreach (Row row in rows) row.root.gameObject.SetActive(false);
    }

    private void RenderStats(ItemData item, bool isFlask, List<Stat> stats, string notes,
        string priceOverride, float ruleY, TMP_SpriteAsset spriteAsset)
    {
        bool isGear = item.baseData is GearItemData;
        int primaryCount = Mathf.Min(isGear ? 1 : isFlask ? 2 : 5, stats.Count);
        float rowHeight = isFlask ? 41f : 34f;
        float y = ruleY + 19f;
        primaryHeading.text = isFlask && item.baseData is FlaskItemData flask &&
            (flask.kind == FlaskKind.Life || flask.kind == FlaskKind.Regeneration)
            ? "회복 성능" : isFlask ? "주요 효과" : isGear ? "주능력치" : "전투 성능";
        qualityHeading.text = "품질 각인";
        SetRect(primaryHeading.rectTransform, 26f, y, 180f, 24f);
        SetRect(qualityHeading.rectTransform, 286f, y, 120f, 24f);
        primaryHeading.gameObject.SetActive(true);
        qualityHeading.gameObject.SetActive(true);
        y += 31f;

        for (int i = 0; i < primaryCount; i++)
        {
            y += RenderRow(rows[i], stats[i], isFlask, y, rowHeight, spriteAsset);
        }
        if (stats.Count > primaryCount)
        {
            y += 17f;
            SetRect(secondaryRule.rectTransform, 26f, y, ContentWidth, 1f);
            secondaryRule.gameObject.SetActive(true);
            y += 18f;
            secondaryHeading.text = isFlask ? "사용 주기" : isGear ? "보조능력치" : "보조 성능";
            SetRect(secondaryHeading.rectTransform, 26f, y, ContentWidth, 24f);
            secondaryHeading.gameObject.SetActive(true);
            y += 30f;
            for (int i = primaryCount; i < stats.Count; i++)
            {
                y += RenderRow(rows[i], stats[i], isFlask, y, rowHeight, spriteAsset);
            }
        }

        bool showPrice = !isFlask || priceOverride != null;
        if (showPrice)
        {
            y += 17f;
            SetRect(priceRule.rectTransform, 26f, y, ContentWidth, 1f);
            priceRule.gameObject.SetActive(true);
            y += 14f;
            priceLabel.text = priceOverride == null ? "가치" : "거래 가격";
            priceValue.text = priceOverride ?? item.baseData.sellPrice.ToString("N0", CultureInfo.InvariantCulture) + "G";
            SetRect(priceLabel.rectTransform, 26f, y, 180f, 27f);
            SetRect(priceValue.rectTransform, 266f, y, 140f, 27f);
            priceLabel.gameObject.SetActive(true);
            priceValue.gameObject.SetActive(true);
            y += 27f;
        }

        string footerValue = isFlask ? notes : item.baseData.description;
        if (!string.IsNullOrWhiteSpace(footerValue))
        {
            y += 13f;
            SetRect(footerRule.rectTransform, 26f, y, ContentWidth, 1f);
            footerRule.gameObject.SetActive(true);
            y += 14f;
            footer.text = footerValue;
            footer.ForceMeshUpdate();
            float height = Mathf.Max(22f, footer.GetPreferredValues(footer.text, ContentWidth, 0f).y);
            SetRect(footer.rectTransform, 26f, y, ContentWidth, height + 3f);
            footer.gameObject.SetActive(true);
            y += height;
        }
        panel.sizeDelta = new Vector2(432f, Mathf.Ceil(y + 25f));
    }

    private static float RenderRow(Row view, Stat stat, bool isFlask, float y, float baseHeight,
        TMP_SpriteAsset spriteAsset)
    {
        MatchCollection sprites = SpriteMark.Matches(stat.marks);
        int markLines = Mathf.Max(1, Mathf.CeilToInt(sprites.Count / (float)MarksPerLine));
        float height = baseHeight + (markLines - 1) * ExtraMarkLineHeight;
        view.root.gameObject.SetActive(true);
        SetRect(view.root, 26f, y, ContentWidth, height);
        view.label.text = stat.label;
        view.value.text = stat.value;
        if (!string.IsNullOrEmpty(stat.delta) && stat.delta != "—")
        {
            string tint = stat.improved ? "#9BC8A7" : "#E29A8E";
            string delta = "<size=80%><color=" + tint + ">(" + stat.delta + ")</color></size>";
            view.value.text += isFlask ? "\n" + delta : " " + delta;
        }
        SetRect(view.value.rectTransform, ValueColumnX, 0f, ValueColumnWidth, 40f);
        view.value.fontSize = 15f;
        if (isFlask)
        {
            float valueWidth = view.value.GetPreferredValues(view.value.text, 1000f, 0f).x;
            if (valueWidth > 155f) view.value.fontSize = Mathf.Max(12f, 15f * 155f / valueWidth);
        }
        view.marks.spriteAsset = spriteAsset;
        SetRect(view.marks.rectTransform, MarksColumnX, 0f, MarksColumnWidth, height + 6f);
        if (markLines == 1)
            view.marks.text = stat.marks;
        else
        {
            string[] lines = new string[markLines];
            for (int i = 0; i < sprites.Count; i++)
                lines[i / MarksPerLine] += sprites[i].Value;
            view.marks.text = string.Join("\n", lines);
        }
        return height;
    }

    private void RenderFallback(TextMeshProUGUI body, float ruleY)
    {
        float y = ruleY + 19f;
        primaryHeading.text = "아이템 정보";
        SetRect(primaryHeading.rectTransform, 26f, y, ContentWidth, 24f);
        primaryHeading.gameObject.SetActive(true);
        y += 32f;
        body.enabled = true;
        body.ForceMeshUpdate();
        float height = Mathf.Max(28f, body.GetPreferredValues(body.text, ContentWidth, 0f).y);
        SetRect(body.rectTransform, 26f, y, ContentWidth, height + 4f);
        panel.sizeDelta = new Vector2(432f, Mathf.Ceil(y + height + 28f));
    }

    private static void CollectWeaponStats(ItemData item, string[] lines, List<Stat> stats)
    {
        for (int i = 2; i < lines.Length; i++)
        {
            string plain = Tags.Replace(lines[i], string.Empty).Trim();
            if (plain.StartsWith("가치", StringComparison.Ordinal)
                || plain.StartsWith("아이템 레벨", StringComparison.Ordinal)) continue;
            if (TryParseStat(item, lines[i], out Stat stat)) stats.Add(stat);
        }
    }

    private static void CollectGearStats(ItemData item, string[] lines, List<Stat> stats)
    {
        for (int i = 2; i < lines.Length; i++)
        {
            string raw = lines[i].Trim();
            if (raw.StartsWith("주능력치  ", StringComparison.Ordinal)) raw = raw.Substring("주능력치  ".Length);
            else if (raw.StartsWith("보조능력치  ", StringComparison.Ordinal)) raw = raw.Substring("보조능력치  ".Length);
            else continue;
            if (TryParseStat(item, raw, out Stat stat)) stats.Add(stat);
        }
    }

    private static void CollectFlaskStats(ItemData item, string[] lines, List<Stat> stats, out string notes)
    {
        string status = string.Empty;
        List<string> extra = new List<string>();
        bool inDetails = false;
        for (int i = 2; i < lines.Length; i++)
        {
            string raw = lines[i].Trim();
            if (raw.Length == 0) continue;
            string plain = Tags.Replace(raw, string.Empty).Trim();
            if (plain.StartsWith("아이템 레벨", StringComparison.Ordinal)) continue;
            if (plain == "최종 효과") { inDetails = true; continue; }
            if (!inDetails) { status = raw; continue; }
            if (stats.Count < 4 && TryParseStat(item, raw, out Stat stat)) stats.Add(stat);
            else extra.Add(raw);
        }
        notes = status;
        if (extra.Count > 0)
            notes += (notes.Length > 0 ? "\n" : string.Empty) + string.Join("\n", extra);
    }

    private static bool TryParseStat(ItemData item, string raw, out Stat stat)
    {
        stat = null;
        string plain = Tags.Replace(raw, string.Empty).Trim();
        Match match = NumericRow.Match(plain);
        if (!match.Success) return false;
        string label = match.Groups[1].Value.Trim().TrimEnd(':');
        string value = VisibleStars.Replace(match.Groups[2].Value, string.Empty).Trim();
        bool improved = true;
        string delta = string.Empty;
        if (OverburstUIQualityBreakdown.TryGet(item, label, out _, out string exact, out _,
                out string change, out bool isImproved))
        {
            value = item.baseData is FlaskItemData ? exact : exact.TrimStart('+');
            delta = change;
            improved = isImproved;
        }
        stat = new Stat { label = label, value = value, delta = delta,
            marks = ConvertMarks(raw), improved = improved };
        return true;
    }

    private static string ConvertMarks(string raw)
    {
        return string.Concat(Stars.Matches(raw).Cast<Match>().Select(m =>
        {
            string color = m.Groups[1].Value.ToUpperInvariant();
            int index = color == "#59FF59" || color == "#68AA84" ? 1
                : color == "#FFD84A" || color == "#D2A85D" ? 2
                : color == "#FF4A4A" || color == "#E29A8E" || color == "#D76A63" ? 3 : 0;
            int count = m.Value.Count(c => c == '★' || c == '◆');
            return string.Concat(Enumerable.Repeat("<sprite index=" + index + " tint=0>", count));
        }));
    }

    private static void SetRect(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }
}
