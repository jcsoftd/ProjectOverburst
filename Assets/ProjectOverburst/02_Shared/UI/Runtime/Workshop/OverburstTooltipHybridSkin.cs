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
    // 2026-10-01 장착 무기 비교: 폭은 그대로 두고, 다른 무기와 비교할 때만 값 칸을 줄여 값과 품질 각인 사이에 "장착 대비" 열을 넣는다.
    private const float CompareValueWidth = 122f;
    private const float CompareColumnX = 196f;
    private const float CompareColumnWidth = 54f;
    private const float CompareMarksColumnX = 256f;
    private const float CompareMarksColumnWidth = 124f;
    private const float SecondaryCompareWidth = 56f;
    // 방어구·장신구는 소수 둘째 자리 %p까지 보여 장착 대비가 더 길고, 각인이 6개까지 차 각인 칸이 꽉 찬다.
    // 값 칸을 줄이고 장착 대비 열을 넓혀 왼쪽으로 옮겨, 각인과 12px 이상 떨어뜨린다(열 오른쪽 끝 244, 제목도 같은 끝).
    private const float GearCompareValueWidth = 102f;
    private const float GearCompareColumnX = 176f;
    private const float GearCompareColumnWidth = 68f;
    private const string LostStatColor = "#8C857C";
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
        public TextMeshProUGUI compare; // 프리팹에 비교 오브젝트가 없으면 null(비교 없이 기존 화면)
    }

    private sealed class Stat
    {
        public string label;
        public string value;
        public string delta;
        public string marks;
        public bool improved;
        public string compare; // 이 행의 장착 대비를 직접 정할 때(빠지는 능력치 행). null이면 라벨로 찾는다.
    }

    private RectTransform panel;
    private Image glow;
    private Image icon;
    private WeaponElementIconView weaponElementIcon;
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
    private TextMeshProUGUI compareHeading;
    private GameObject equippedTag;
    private bool compareReady;
    private EquippedWeaponComparison.Result comparison;
    private bool compareColumns;

    // Auto = 장착 무기와 비교, EquippedReference = Alt 나란히 보기의 "장착 중" 쪽.
    public TooltipCompareMode CompareMode { get; set; }

    public bool TryPresent(ItemData item, string priceOverride, string[] rawLines, TextMeshProUGUI legacyBody)
    {
        if (item == null || !item.baseData || !Resolve())
            return false;

        comparison = compareReady && (item.baseData is ElementGemItemData || item.baseData is WeaponItemData || item.baseData is GearItemData)
            ? EquippedWeaponComparison.Compare(item, CompareMode) : null;
        compareColumns = comparison != null && comparison.IsComparable;
        if (equippedTag) equippedTag.SetActive(comparison != null && comparison.IsEquippedItem);

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
        weaponElementIcon?.Present(item);
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
        float contentTop = ruleY + 19f;
        if (item.baseData is ElementGemItemData || item.baseData is WeaponItemData || item.baseData is FlaskItemData || item.baseData is GearItemData || item.baseData is BagItemData)
        {
            List<Stat> stats = new List<Stat>(12);
            string notes = string.Empty;
            bool isFlask = item.baseData is FlaskItemData;
            if (item.baseData is ElementGemItemData)
            {
                foreach(var row in ElementGemTooltip.Rows(item)) stats.Add(new Stat { label=row.Label,value=row.Formatted,marks=row.Fixed?string.Empty:ConvertMarks(row.Marks) });
                notes=ElementGemTooltip.Notes(item);
                if(compareColumns) foreach(var lost in comparison.Lost) stats.Add(new Stat {label=lost.Label,value="없음",marks=string.Empty,compare=lost.Text});
            }
            else if (item.baseData is BagItemData)
            {
                CollectBagStats(item, rawLines, stats);
                if (PlayerProgression.CurrentLevel >= OverburstGrowthRules.MaximumLevel && item.bagState.rows.Any(x => x.stat == BagStat.KillExperience))
                    notes = "최대 레벨에서는 처치 경험치 보너스가 적용되지 않습니다.";
            }
            else if (item.baseData is GearItemData)
            {
                CollectGearStats(item, rawLines, stats);
                // 바꿔 끼면 빠지는 능력치: 보조능력치 끝에 흐린 행으로 붙이고 장착 대비에 ▼를 보인다.
                if (compareColumns && stats.Count > 0)
                    foreach (EquippedWeaponComparison.StatDelta lost in comparison.Lost)
                        stats.Add(new Stat
                        {
                            label = "<color=" + LostStatColor + ">" + lost.Label + "</color>",
                            value = "<color=" + LostStatColor + ">없음</color>",
                            delta = string.Empty, marks = string.Empty, compare = lost.Text
                        });
            }
            else if (isFlask)
                CollectFlaskStats(item, rawLines, stats, out notes);
            else
                CollectWeaponStats(item, rawLines, stats);

            if (stats.Count > 0 && stats.Count <= RowCapacity)
            {
                legacyBody.enabled = false;
                RenderStats(item, isFlask, stats, notes, priceOverride, contentTop, legacyBody.spriteAsset);
                return true;
            }
        }

        RenderFallback(legacyBody, contentTop);
        return true;
    }

    private bool Resolve()
    {
        if (resolved) return true;
        panel = (RectTransform)transform;
        glow = Find<Image>("Approved Top Glow");
        icon = Find<Image>("Approved Icon Frame/Icon Display");
        weaponElementIcon = Find<WeaponElementIconView>("Approved Icon Frame/Weapon Element Badge");
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
                marks = Find<TextMeshProUGUI>(path + "/Marks"),
                compare = Find<TextMeshProUGUI>(path + "/Compare")
            };
            if (!rows[i].label || !rows[i].value || !rows[i].marks) return false;
        }
        resolved = glow && icon && badge && title && subtitle && grade && headerRule &&
            primaryHeading && qualityHeading && secondaryHeading && secondaryRule &&
            priceLabel && priceValue && priceRule && footer && footerRule &&
            outerEdges.All(x => x) && innerEdges.All(x => x) && badgeEdges.All(x => x);
        if (!resolved) return false;
        // 비교 오브젝트는 TooltipCompareObjectizer가 저작한다. 없으면 비교만 끄고 기존 화면을 그대로 쓴다.
        compareHeading = Find<TextMeshProUGUI>("Approved Content/Compare Heading");
        Transform tag = transform.Find("Equipped Tag");
        equippedTag = tag != null ? tag.gameObject : null;
        compareReady = compareHeading && rows.All(x => x.compare);
        return true;
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
        foreach (Row row in rows)
        {
            row.root.gameObject.SetActive(false);
            if (row.compare) row.compare.gameObject.SetActive(false);
        }
        if (compareHeading) compareHeading.gameObject.SetActive(false);
    }

    private void RenderStats(ItemData item, bool isFlask, List<Stat> stats, string notes,
        string priceOverride, float contentTop, TMP_SpriteAsset spriteAsset)
    {
        bool isGem = item.baseData is ElementGemItemData;
        bool isBag = item.baseData is BagItemData;
        bool isGear = item.baseData is GearItemData || isBag || isGem;
        bool isWeapon = item.baseData is WeaponItemData;
        // 무기·장비는 품질 각인 변화를 값 아래 줄로 내린다(물약과 같은 두 줄, 행 높이 41).
        bool twoLine = isFlask || isWeapon || isGear;
        int primaryCount = Mathf.Min(isGem ? ElementGemTooltip.Rows(item).Count(x=>x.Fixed) : isGear ? 1 : isFlask ? 2 : 5, stats.Count);
        float rowHeight = twoLine ? 41f : 34f;
        float y = contentTop;
        primaryHeading.text = isFlask && item.baseData is FlaskItemData flask &&
            (flask.kind == FlaskKind.Life || flask.kind == FlaskKind.Regeneration)
            ? "회복 성능" : isGem ? "고정 효과" : isFlask ? "주요 효과" : isBag ? "수납" : isGear ? "주능력치" : "전투 성능";
        qualityHeading.text = "품질 각인";
        SetRect(primaryHeading.rectTransform, 26f, y, 180f, 24f);
        SetRect(qualityHeading.rectTransform, 286f, y, 120f, 24f);
        primaryHeading.gameObject.SetActive(true);
        qualityHeading.gameObject.SetActive(true);
        if (compareColumns)
        {
            compareHeading.text = "장착 대비";
            float headingRight = isGear ? GearCompareColumnX + GearCompareColumnWidth : CompareColumnX + CompareColumnWidth;
            SetRect(compareHeading.rectTransform, 26f + headingRight - 70f, y, 70f, 24f);
            compareHeading.gameObject.SetActive(true);
        }
        y += 31f;

        for (int i = 0; i < primaryCount; i++)
        {
            y += RenderRow(rows[i], stats[i], isFlask, isGear, twoLine, false, y, rowHeight, spriteAsset);
        }
        if (stats.Count > primaryCount)
        {
            y += 17f;
            SetRect(secondaryRule.rectTransform, 26f, y, ContentWidth, 1f);
            secondaryRule.gameObject.SetActive(true);
            y += 18f;
            secondaryHeading.text = isGem ? "랜덤 능력치" : isFlask ? "사용 주기" : isBag ? "파밍 옵션" : isGear ? "보조능력치" : "보조 성능";
            SetRect(secondaryHeading.rectTransform, 26f, y, ContentWidth, 24f);
            secondaryHeading.gameObject.SetActive(true);
            y += 30f;
            for (int i = primaryCount; i < stats.Count; i++)
            {
                y += RenderRow(rows[i], stats[i], isFlask, isGear, twoLine, isWeapon, y, rowHeight, spriteAsset);
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
            priceValue.text = priceOverride ?? ElementGemLootPolicy.Value(item).ToString("N0", CultureInfo.InvariantCulture) + "G";
            SetRect(priceLabel.rectTransform, 26f, y, 180f, 27f);
            SetRect(priceValue.rectTransform, 266f, y, 140f, 27f);
            priceLabel.gameObject.SetActive(true);
            priceValue.gameObject.SetActive(true);
            y += 27f;
        }

        string footerValue = isFlask || isGem ? notes : item.baseData.description;
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

    private float RenderRow(Row view, Stat stat, bool isFlask, bool isGear, bool twoLine, bool rightAligned, float y, float baseHeight,
        TMP_SpriteAsset spriteAsset)
    {
        MatchCollection sprites = SpriteMark.Matches(stat.marks);
        int markLines = Mathf.Max(1, Mathf.CeilToInt(sprites.Count / (float)MarksPerLine));
        float height = baseHeight + (markLines - 1) * ExtraMarkLineHeight;
        view.root.gameObject.SetActive(true);
        SetRect(view.root, 26f, y, ContentWidth, height);
        view.label.text = stat.label;
        view.value.text = stat.value;
        bool hasDelta = !string.IsNullOrEmpty(stat.delta) && stat.delta != "—";
        if (hasDelta)
        {
            string tint = stat.improved ? "#9BC8A7" : "#E29A8E";
            string delta = "<size=80%><color=" + tint + ">(" + stat.delta + ")</color></size>";
            view.value.text += twoLine ? "\n" + delta : " " + delta;
        }
        // 모든 행: 이름·값·장착 대비·품질 각인을 행 높이의 가운데에 맞춘다(두 줄 행은 최소 40).
        // 각인이 두 줄로 넘어가 행이 높아지면 그 높이의 가운데에 모두 맞춘다.
        float lineBox = twoLine ? Mathf.Max(40f, height) : height;
        SetRect(view.label.rectTransform, 0f, 0f, 140f, lineBox);
        // 무기 보조 성능은 품질 각인 칸이 없으므로 값과 장착 대비를 오른쪽 끝(가치 줄과 같은 끝)에 붙인다.
        float compareX = rightAligned ? ContentWidth - SecondaryCompareWidth : isGear ? GearCompareColumnX : CompareColumnX;
        float compareWidth = rightAligned ? SecondaryCompareWidth : isGear ? GearCompareColumnWidth : CompareColumnWidth;
        float valueRight = rightAligned
            ? (compareColumns ? compareX - 4f : ContentWidth)
            : ValueColumnX + (!compareColumns ? ValueColumnWidth : isGear ? GearCompareValueWidth : CompareValueWidth);
        SetRect(view.value.rectTransform, ValueColumnX, 0f, valueRight - ValueColumnX, lineBox);
        view.value.fontSize = 15f;
        if (isFlask)
        {
            float valueWidth = view.value.GetPreferredValues(view.value.text, 1000f, 0f).x;
            if (valueWidth > 155f) view.value.fontSize = Mathf.Max(12f, 15f * 155f / valueWidth);
        }
        view.marks.spriteAsset = spriteAsset;
        SetRect(view.marks.rectTransform, compareColumns ? CompareMarksColumnX : MarksColumnX, 0f,
            compareColumns ? CompareMarksColumnWidth : MarksColumnWidth, lineBox);
        if (view.compare)
        {
            string compareText = stat.compare;
            if (compareText == null && compareColumns
                && comparison.Stats.TryGetValue(stat.label, out EquippedWeaponComparison.StatDelta delta))
                compareText = delta.Text;
            bool show = compareColumns && compareText != null;
            if (show)
            {
                view.compare.text = compareText;
                SetRect(view.compare.rectTransform, compareX, 0f, compareWidth, lineBox);
            }
            view.compare.gameObject.SetActive(show);
        }
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

    private void RenderFallback(TextMeshProUGUI body, float contentTop)
    {
        float y = contentTop;
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

    private static void CollectBagStats(ItemData item, string[] lines, List<Stat> stats)
    {
        foreach (var row in item.bagState.rows)
        {
            string label = BagTooltip.Label(row.stat);
            string raw = lines.FirstOrDefault(x => Tags.Replace(x, string.Empty).StartsWith(label + " ", StringComparison.Ordinal));
            if (raw != null && TryParseStat(item, raw, out Stat stat)) stats.Add(stat);
        }
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
            value = item.baseData is FlaskItemData || item.baseData is GearItemData || item.baseData is BagItemData ? exact : exact.TrimStart('+');
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
                : color == "#FF4A4A" || color == "#E29A8E" || color == "#D76A63" || color == "#DB6868" ? 3 : 0;
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
