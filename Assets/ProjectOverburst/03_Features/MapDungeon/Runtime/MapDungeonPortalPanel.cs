using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class MapDungeonPortalPanel : MonoBehaviour
{
    private const int PageSize = 7;
    private static readonly Color TextColor = new Color(.89f, .86f, .77f);
    private static readonly Color Gold = new Color(.77f, .62f, .35f);
    private static readonly Color Slate = new Color(.13f, .19f, .23f);
    private MapDungeonPortal owner;
    private TMP_FontAsset bodyFont;
    private TMP_Text details;
    private TMP_Text status;
    private TMP_Text pageLabel;
    private Button[] mapRows;
    private readonly List<ItemData> maps = new List<ItemData>();
    private int page;
    private string selectedId;

    public static MapDungeonPortalPanel Open(MapDungeonPortal portal)
    {
        if (portal == null) return null;
        var root = new GameObject("MapDungeonPortalPanel", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(root, portal.gameObject.scene);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        var view = root.AddComponent<MapDungeonPortalPanel>();
        view.owner = portal;
        view.Build();
        return view;
    }

    private void Build()
    {
        bodyFont = Resources.Load<TMP_FontAsset>(
            "UI/Fonts/ProjectMT/FontAssets/TMP_SpoqaHanSansNeo_Body");
        var overlay = Box(transform, "Dim", Vector2.zero, Vector2.zero,
            new Color(0f, 0f, 0f, .67f), true);
        var window = Box(overlay, "MapWindow", Vector2.zero, new Vector2(790f, 660f),
            new Color(.075f, .105f, .13f, .99f));
        Box(window, "HeaderRule", new Vector2(0f, 231f), new Vector2(738f, 2f), Gold);
        Text(window, "지도 포탈", new Vector2(-224f, 285f), new Vector2(295f, 48f), 30, Gold,
            TextAlignmentOptions.Left);
        Text(window, "던전 입장", new Vector2(174f, 285f), new Vector2(330f, 45f), 22, TextColor,
            TextAlignmentOptions.Left);
        CreateButton(window, "Close", new Vector2(355f, 292f), new Vector2(54f, 42f),
            "✕", () => owner?.ClosePanel());
        Text(window, "보유 지도", new Vector2(-220f, 242f), new Vector2(315f, 32f), 19, TextColor,
            TextAlignmentOptions.Left);
        Text(window, "선택한 지도", new Vector2(175f, 242f), new Vector2(337f, 32f), 19, TextColor,
            TextAlignmentOptions.Left);

        CreateButton(window, "FreeLevelOne", new Vector2(-221f, 190f), new Vector2(315f, 48f),
            "Lv.1 무료 입장", () => { selectedId = null; UpdateDetails(); });
        mapRows = new Button[PageSize];
        for (int i = 0; i < mapRows.Length; i++)
            mapRows[i] = CreateButton(window, "MapRow_" + i,
                new Vector2(-221f, 135f - i * 50f), new Vector2(315f, 44f),
                string.Empty, null);
        CreateButton(window, "Previous", new Vector2(-315f, -247f), new Vector2(110f, 38f),
            "이전", () => { if (page > 0) { page--; RefreshRows(); } });
        CreateButton(window, "Next", new Vector2(-128f, -247f), new Vector2(110f, 38f),
            "다음", () => { if ((page + 1) * PageSize < maps.Count) { page++; RefreshRows(); } });
        pageLabel = Text(window, "1 / 1", new Vector2(-220f, -247f), new Vector2(76f, 38f),
            16, TextColor, TextAlignmentOptions.Center);
        Box(window, "Divider", new Vector2(-20f, -4f), new Vector2(2f, 476f),
            new Color(.45f, .48f, .47f, .65f));
        details = Text(window, string.Empty, new Vector2(179f, 8f), new Vector2(338f, 430f),
            18, TextColor, TextAlignmentOptions.TopLeft);
        status = Text(window, string.Empty, new Vector2(177f, -242f), new Vector2(334f, 42f),
            15, Gold, TextAlignmentOptions.Center);
        CreateButton(window, "Enter", new Vector2(179f, -292f), new Vector2(335f, 54f),
            "포탈 활성화", TryEnter);
        RefreshRows();
        UpdateDetails();
    }

    private void RefreshRows()
    {
        maps.Clear();
        var inventory = PlayerAccountInventoryService.SharedInventory;
        var definition = Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
        if (inventory != null && definition != null)
            maps.AddRange(inventory.Items.Where(item => item != null && item.baseData == definition
                && item.mapState != null && item.mapState.level >= 1)
                .OrderByDescending(item => item.mapState.level)
                .ThenByDescending(item => item.grade));
        page = Mathf.Clamp(page, 0, Mathf.Max(0, (maps.Count - 1) / PageSize));
        for (int i = 0; i < mapRows.Length; i++)
        {
            int index = page * PageSize + i;
            var button = mapRows[i];
            button.gameObject.SetActive(index < maps.Count);
            button.onClick.RemoveAllListeners();
            if (index >= maps.Count) continue;
            var item = maps[index];
            string id = item.runtimeInstanceId;
            button.GetComponentInChildren<TMP_Text>().text =
                "Lv." + item.mapState.level + "  " + MapThemeCatalog.DisplayName(item.mapState.monsterThemeId)
                + "  ·  " + ItemTooltipFormatter.GetGradeName(item.grade);
            button.GetComponentInChildren<TMP_Text>().color = GradeConfig.GetGradeColor(item.grade);
            button.onClick.AddListener(() => { selectedId = id; UpdateDetails(); });
        }
        pageLabel.text = (page + 1) + " / " + Mathf.Max(1, Mathf.CeilToInt(maps.Count / (float)PageSize));
    }

    private void UpdateDetails()
    {
        status.text = string.Empty;
        if (string.IsNullOrEmpty(selectedId))
        {
            details.text = "<color=#D7BA77>레벨 1 · 무료 입장</color>\n\n"
                + "지도가 없어도 입장할 수 있습니다.\n"
                + "몬스터 테마는 입장 때 무작위로 정해집니다.\n\n"
                + "입장할 때마다 시작 모서리와 보스 위치가 바뀝니다.";
            return;
        }
        var selected = maps.FirstOrDefault(item => item.runtimeInstanceId == selectedId);
        if (selected == null) { selectedId = null; UpdateDetails(); return; }
        var map = selected.mapState;
        var text = new System.Text.StringBuilder();
        text.Append("<color=#D7BA77>레벨 ").Append(map.level).Append("</color> · ")
            .Append(ItemTooltipFormatter.GetGradeName(map.grade)).AppendLine()
            .Append("몬스터 테마: ").Append(MapThemeCatalog.DisplayName(map.monsterThemeId)).AppendLine()
            .Append("획득 경험치 +")
            .Append(Mathf.RoundToInt((MapOptionPolicy.ExperienceMultiplier(map) - 1f) * 100f))
            .AppendLine("%")
            .Append("장비·물약 고등급 보정 +")
            .Append((MapOptionPolicy.HighGradeRollBias(map.grade) * 100f).ToString("0.#"))
            .AppendLine("%").AppendLine();
        if (map.options != null && map.options.Count > 0)
            foreach (var option in map.options)
                text.Append("• ").AppendLine(MapOptionPolicy.Describe(option));
        else text.AppendLine("추가 위험 옵션 없음");
        text.AppendLine().Append("입장 시 이 지도 1장을 소비합니다.");
        details.text = text.ToString();
    }

    private void TryEnter()
    {
        if (owner == null) return;
        try
        {
            bool started = string.IsNullOrEmpty(selectedId)
                ? owner.EnterLevelOne() : owner.EnterSelectedMap(selectedId);
            if (!started) status.text = PersistentSceneFlow.Instance?.RunEntryError ?? "입장을 시작하지 못했습니다.";
        }
        catch (Exception error) { status.text = error.Message; }
    }

    private RectTransform Box(Transform parent, string name, Vector2 position, Vector2 size,
        Color color, bool stretch = false)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = stretch ? Vector2.zero : new Vector2(.5f, .5f);
        rect.anchorMax = stretch ? Vector2.one : new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        go.GetComponent<Image>().color = color;
        return rect;
    }

    private TMP_Text Text(Transform parent, string value, Vector2 position, Vector2 size,
        float fontSize, Color color, TextAlignmentOptions alignment)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var label = go.GetComponent<TextMeshProUGUI>();
        label.font = bodyFont;
        label.fontSize = fontSize;
        label.color = color;
        label.alignment = alignment;
        label.enableWordWrapping = true;
        label.text = value;
        return label;
    }

    private Button CreateButton(Transform parent, string name, Vector2 position, Vector2 size,
        string label, Action action)
    {
        var rect = Box(parent, name, position, size, Slate);
        var button = rect.gameObject.AddComponent<Button>();
        if (action != null) button.onClick.AddListener(() => action());
        Text(rect, label, Vector2.zero, size - new Vector2(12f, 4f), 17f,
            TextColor, TextAlignmentOptions.Center);
        return button;
    }
}
