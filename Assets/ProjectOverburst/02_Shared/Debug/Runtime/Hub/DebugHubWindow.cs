#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Overburst.DebugTools
{
    /// <summary>
    /// 디버그 창 본체. 제목줄(검색·정보·투명도·닫기) / 왼쪽 탭 / 본문 스크롤 / 상태줄로 이루어진다.
    /// 등록부를 읽어 행을 만들고, 선택된 탭은 배경색과 굵은 글자로 표시한다(세로 강조선은 쓰지 않는다, 99 UI 시안 취향).
    /// </summary>
    internal sealed class DebugHubWindow
    {
        [Serializable]
        private sealed class State
        {
            public float x = -664f;
            public float y = -24f;
            public float width = 640f;
            public float height = 540f;
            public int opacity;
            public string tab = DebugTabs.Combat;
        }

        private sealed class TabView
        {
            public string Tab;
            public Button Button;
            public Image Background;
            public TextMeshProUGUI Label;
            public string LastText;
            public bool Selected;
        }

        private static readonly float[] Opacities = { 1f, 0.85f, 0.6f };
        private const float MinWidth = 480f;
        private const float MinHeight = 360f;
        private const float TooltipWidth = 320f;

        private readonly RectTransform canvasRect;
        private readonly List<DebugRowView> rows = new List<DebugRowView>();
        private readonly List<TabView> tabViews = new List<TabView>();
        private readonly List<string> tabScratch = new List<string>();
        private readonly StringBuilder builder = new StringBuilder(512);
        private readonly State state;
        private readonly string accountLabel;

        private RectTransform root;
        private CanvasGroup group;
        private RectTransform tabList;
        private RectTransform content;
        private RectTransform contentRoot;
        private readonly Dictionary<RectTransform, Dictionary<string, DebugRowView>> pageRows =
            new Dictionary<RectTransform, Dictionary<string, DebugRowView>>();
        private ScrollRect scroll;
        private TMP_InputField search;
        private TextMeshProUGUI info;
        private TextMeshProUGUI opacityLabel;
        private TextMeshProUGUI statusDot;
        private TextMeshProUGUI statusText;
        private RectTransform confirmBar;
        private TextMeshProUGUI confirmText;
        private Action confirmAction;
        private RectTransform historyPanel;
        private TextMeshProUGUI historyText;
        private RectTransform tooltip;
        private TextMeshProUGUI tooltipText;
        private LayoutElement tooltipLayout;

        private string query = string.Empty;
        private string tabSignature;
        private string lastInfo;
        private bool dirty = true;
        private bool resetScroll = true;
        private int builtRevision = -1;
        private int builtPrefsVersion = -1;
        private string pendingFocusId;
        private DebugRowView highlighted;
        private float highlightUntil;
        private bool tooltipVisible;

        public DebugHubWindow(RectTransform canvasRect)
        {
            this.canvasRect = canvasRect;
            state = LoadState();
            bool isolated = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY"));
            accountLabel = isolated
                ? "격리 계정"
                : $"<color={DebugHubStyle.Hex(DebugUi.Style.warn)}>실제 계정</color>";
            Build();
            DebugHoverTip.Shown += ShowTooltip;
            DebugHoverTip.Hidden += HideTooltip;
        }

        public bool Visible => root != null && root.gameObject.activeSelf;
        public bool ConfirmOpen => confirmBar != null && confirmBar.gameObject.activeSelf;

        public bool IsTyping
        {
            get
            {
                EventSystem system = EventSystem.current;
                GameObject selected = system != null ? system.currentSelectedGameObject : null;
                return selected != null
                    && selected.transform.IsChildOf(root)
                    && selected.TryGetComponent(out TMP_InputField field)
                    && field.isFocused;
            }
        }

        public bool Contains(Vector2 screenPoint)
        {
            return Visible && RectTransformUtility.RectangleContainsScreenPoint(root, screenPoint, null);
        }

        public void SetVisible(bool visible)
        {
            root.gameObject.SetActive(visible);
            if (visible)
            {
                ApplyRect(true);
                Refresh();
            }
            else
            {
                BlurInputs();
                HideTooltip();
                HideConfirm();
            }
        }

        public void Dispose()
        {
            DebugHoverTip.Shown -= ShowTooltip;
            DebugHoverTip.Hidden -= HideTooltip;
            DisposeRows();
            SaveState();
        }

        public void Refresh()
        {
            if (!Visible)
                return;
            ApplyRect(false);
            RefreshTabs();
            bool favoritesChanged = query.Length == 0 && state.tab == DebugTabs.Favorites
                && builtPrefsVersion != DebugPrefs.Version;
            if (dirty || builtRevision != DebugRegistry.Revision || favoritesChanged)
                RebuildContent();
            RefreshRows();
            RefreshInfo();
        }

        public void RefreshRows()
        {
            for (int i = 0; i < rows.Count; i++)
                rows[i].Refresh();
        }

        public void Tick()
        {
            if (tooltipVisible)
                PositionTooltip();
            if (pendingFocusId != null && Visible)
                FocusPending();
            if (highlighted != null && Time.unscaledTime > highlightUntil)
            {
                highlighted.SetHighlight(false);
                highlighted = null;
            }
        }

        public void SelectTab(string tab)
        {
            if (string.IsNullOrEmpty(tab))
                return;
            bool changed = state.tab != tab || query.Length > 0;
            state.tab = tab;
            if (query.Length > 0)
            {
                query = string.Empty;
                search.SetTextWithoutNotify(string.Empty);
            }
            if (!changed)
                return;
            dirty = true;
            resetScroll = true;
            SaveState();
            Refresh();
        }

        public void JumpTo(DebugItem item)
        {
            if (item?.Section == null)
                return;
            if (DebugPrefs.IsCollapsed(item.Section.Key))
            {
                DebugPrefs.SetCollapsed(item.Section.Key, false);
                dirty = true;
            }
            string tab = item.Section.Tab;
            if (tab != state.tab || query.Length > 0)
                SelectTab(tab);
            else
                Refresh();
            pendingFocusId = item.Id;
        }

        public void FocusSearch()
        {
            search.Select();
            search.ActivateInputField();
        }

        public void BlurInputs()
        {
            if (!IsTyping)
                return;
            EventSystem system = EventSystem.current;
            if (system != null)
                system.SetSelectedGameObject(null);
        }

        public void ShowRecord(DebugRuntime.Record record)
        {
            DebugHubStyle style = DebugUi.Style;
            statusDot.color = record.Success ? style.ok : style.warn;
            statusText.color = record.Success ? style.text : style.warn;
            statusText.text = record.At.ToString("HH:mm:ss") + "  " + DebugRuntime.Describe(record);
            if (historyPanel.gameObject.activeSelf)
                FillHistory();
        }

        public void ShowConfirm(string message, Action proceed)
        {
            confirmText.text = message;
            confirmAction = proceed;
            confirmBar.gameObject.SetActive(true);
        }

        public void ResetLayout()
        {
            State defaults = new State();
            state.x = defaults.x;
            state.y = defaults.y;
            state.width = defaults.width;
            state.height = defaults.height;
            state.opacity = 0;
            ApplyOpacity();
            ApplyRect(true);
            SaveState();
        }

        public void SaveState()
        {
            PlayerPrefs.SetString(DebugPrefs.WindowKey, JsonUtility.ToJson(state));
        }

        private void Build()
        {
            DebugHubStyle style = DebugUi.Style;
            root = DebugUi.Rect(canvasRect, "Debug Window");
            root.anchorMin = root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(0f, 1f);
            group = DebugUi.Component<CanvasGroup>(root.gameObject);
            DebugUi.Column(root, 0f);

            // 레이아웃 밖의 틀: 그림자 2겹 → 테두리 → 바탕. 먼저 만든 자식이 뒤에 그려진다.
            Image shadowSoft = DebugUi.Panel(root, "Shadow Soft", style.shadowSoft, 12);
            DebugUi.IgnoreLayout(shadowSoft);
            DebugUi.Stretch(shadowSoft.rectTransform, -10f, -16f, -10f, -4f);
            Image shadow = DebugUi.Panel(root, "Shadow", style.shadow, 10);
            DebugUi.IgnoreLayout(shadow);
            DebugUi.Stretch(shadow.rectTransform, -3f, -7f, -3f, -1f);
            Image border = DebugUi.Panel(root, "Border", style.border, 9);
            DebugUi.IgnoreLayout(border);
            DebugUi.Stretch(border.rectTransform, -1f, -1f, -1f, -1f);
            Image background = DebugUi.Panel(root, "Background", style.window, 8, true);
            DebugUi.IgnoreLayout(background);
            DebugUi.Stretch(background.rectTransform);

            BuildTitle(style);
            DebugUi.Separator(root, "Title Line");
            BuildBody(style);
            BuildConfirm(style);
            DebugUi.Separator(root, "Status Line");
            BuildStatus(style);
            BuildResizeHandle(style);
            BuildTooltip(style);
            ApplyOpacity();
            ApplyRect(true);
        }

        private void BuildTitle(DebugHubStyle style)
        {
            Image title = DebugUi.Image(root, "Title", new Color(1f, 1f, 1f, 0f), true);
            DebugUi.FixedHeight(title, 46f);
            DebugUi.Row(title, 8f, new RectOffset(16, 10, 9, 9));
            DebugDragHandle drag = DebugUi.Component<DebugDragHandle>(title.gameObject);
            drag.Dragged = delta => Move(delta);
            drag.Ended = SaveState;

            Image mark = DebugUi.Panel(title.transform, "Mark", style.accent, 3);
            DebugUi.Layout(mark, preferredWidth: 6f, flexibleWidth: 0f, minWidth: 6f);
            const string heading = "OVERBURST 디버그";
            TextMeshProUGUI headingText = DebugUi.Text(title.transform, "Heading", heading, style.bodySize + 1f,
                style.textStrong, TextAlignmentOptions.MidlineLeft, true);
            DebugUi.Layout(headingText, preferredWidth: headingText.GetPreferredValues(heading).x + 2f, flexibleWidth: 0f,
                minWidth: headingText.GetPreferredValues(heading).x + 2f);

            Image keyBadge = DebugUi.Panel(title.transform, "Key", style.badge, 4);
            DebugUi.Layout(keyBadge, preferredWidth: 26f, flexibleWidth: 0f, minWidth: 26f);
            TextMeshProUGUI keyText = DebugUi.Text(keyBadge.transform, "Text", "F1", style.smallSize - 1f, style.label,
                TextAlignmentOptions.Center, true);
            DebugUi.Stretch(keyText.rectTransform);

            RectTransform gap = DebugUi.Rect(title.transform, "Gap");
            DebugUi.Layout(gap, preferredWidth: 4f, flexibleWidth: 0f);

            search = DebugUi.Input(title.transform, "Search", "검색  Ctrl+F", style.bodySize - 1f);
            DebugUi.Layout(search, preferredWidth: 240f, flexibleWidth: 1f, minWidth: 90f);
            search.onValueChanged.AddListener(OnSearchChanged);

            Button opacity = DebugUi.Button(title.transform, "Opacity", "100%", CycleOpacity, out opacityLabel, 42f, 28f,
                style.smallSize, DebugButtonKind.Ghost, 5);
            DebugUi.AddTip(opacity.targetGraphic, "창 투명도 100 / 85 / 60%");
            Button close = DebugUi.Button(title.transform, "Close", "×", DebugHub.Close, out _, 28f, 28f,
                style.bodySize + 2f, DebugButtonKind.Icon, 5);
            DebugUi.AddTip(close.targetGraphic, "닫기 (F1)");
        }

        private void BuildBody(DebugHubStyle style)
        {
            RectTransform body = DebugUi.Rect(root, "Body");
            DebugUi.Layout(body, flexibleHeight: 1f, minHeight: 100f);
            DebugUi.Row(body, 0f, null, TextAnchor.UpperLeft);

            Image tabs = DebugUi.Image(body, "Tabs", style.tabs, true);
            tabList = tabs.rectTransform;
            DebugUi.Layout(tabs, preferredWidth: 136f, flexibleWidth: 0f, minWidth: 136f);
            DebugUi.Column(tabs, 2f, new RectOffset(8, 8, 10, 10));

            content = DebugUi.Scroll(body, "Scroll", out scroll);
            contentRoot = content;
            DebugUi.Layout(scroll, flexibleWidth: 1f, minWidth: 100f);
            DebugUi.Column(content, 1f, new RectOffset(14, 18, 0, 16));

            Image history = DebugUi.Image(body, "History", style.window, true);
            historyPanel = history.rectTransform;
            DebugUi.IgnoreLayout(history);
            DebugUi.Stretch(historyPanel, 136f, 0f, 0f, 0f);
            DebugUi.Column(history, 8f, new RectOffset(16, 16, 12, 12));
            RectTransform header = DebugUi.Rect(historyPanel, "Header");
            DebugUi.Row(header, 6f);
            DebugUi.FixedHeight(header, 26f);
            TextMeshProUGUI headerText = DebugUi.Text(header, "Title", "실행 기록", style.headerSize, style.textStrong,
                TextAlignmentOptions.MidlineLeft, true);
            DebugUi.Layout(headerText, preferredWidth: headerText.GetPreferredValues("실행 기록").x + 4f, flexibleWidth: 0f);
            TextMeshProUGUI headerNote = DebugUi.Text(header, "Note", $"최근 {DebugRuntime.HistoryLimit}건 · 새것부터",
                style.smallSize, style.muted);
            DebugUi.Layout(headerNote, flexibleWidth: 1f, minWidth: 0f);
            DebugUi.Button(header, "Close", "×", ToggleHistory, out _, 26f, 26f, style.bodySize + 2f, DebugButtonKind.Icon, 5);
            DebugUi.Separator(historyPanel);
            historyText = DebugUi.Text(historyPanel, "Lines", string.Empty, style.smallSize, style.text,
                TextAlignmentOptions.TopLeft, false, true);
            historyText.textWrappingMode = TextWrappingModes.Normal;
            historyText.overflowMode = TextOverflowModes.Truncate;
            historyText.lineSpacing = 12f;
            DebugUi.Layout(historyText, flexibleHeight: 1f, flexibleWidth: 1f);
            historyPanel.gameObject.SetActive(false);
        }

        private void BuildConfirm(DebugHubStyle style)
        {
            Image bar = DebugUi.Image(root, "Confirm", style.confirm, true);
            confirmBar = bar.rectTransform;
            DebugUi.FixedHeight(bar, 40f);
            DebugUi.Row(bar, 8f, new RectOffset(16, 12, 7, 7));
            confirmText = DebugUi.Text(confirmBar, "Message", string.Empty, style.bodySize - 1f, style.warn,
                TextAlignmentOptions.MidlineLeft, true);
            DebugUi.Layout(confirmText, flexibleWidth: 1f, minWidth: 0f);
            Button run = DebugUi.Button(confirmBar, "Run", "실행", () =>
            {
                Action proceed = confirmAction;
                HideConfirm();
                proceed?.Invoke();
            }, out TextMeshProUGUI runLabel, 60f, 26f);
            run.targetGraphic.color = style.warn;
            runLabel.color = new Color(0.1f, 0.07f, 0.03f, 1f);
            runLabel.fontStyle = FontStyles.Bold;
            DebugUi.Button(confirmBar, "Cancel", "취소", () =>
            {
                HideConfirm();
                DebugRuntime.Report("확인창", DebugResult.Ok("취소했어요"));
            }, out _, 60f, 26f, -1f, DebugButtonKind.Ghost, 5);
            confirmBar.gameObject.SetActive(false);
        }

        private void BuildStatus(DebugHubStyle style)
        {
            RectTransform status = DebugUi.Rect(root, "Status");
            DebugUi.FixedHeight(status, 32f);
            DebugUi.Row(status, 8f, new RectOffset(16, 20, 4, 4), TextAnchor.MiddleLeft, false);
            statusDot = DebugUi.Text(status, "Dot", "●", style.smallSize - 2f, style.muted, TextAlignmentOptions.Center);
            DebugUi.Layout(statusDot, preferredWidth: 10f, flexibleWidth: 0f);
            statusText = DebugUi.Text(status, "Text", "실행 기록 없음", style.smallSize, style.muted);
            DebugUi.Layout(statusText, flexibleWidth: 1f, minWidth: 0f);
            info = DebugUi.Text(status, "Info", string.Empty, style.smallSize, style.muted,
                TextAlignmentOptions.MidlineRight, false, true);
            DebugUi.Layout(info, preferredWidth: 150f, flexibleWidth: 0f, minWidth: 40f);
            Image divider = DebugUi.Image(status, "Divider", style.line);
            DebugUi.Layout(divider, preferredWidth: 1f, preferredHeight: 14f, flexibleWidth: 0f, flexibleHeight: 0f, minWidth: 1f);
            DebugUi.Button(status, "History", "실행 기록  ›", ToggleHistory, out _, 60f, 24f, style.smallSize,
                DebugButtonKind.Ghost, 5);
        }

        private void BuildResizeHandle(DebugHubStyle style)
        {
            Image handle = DebugUi.Image(root, "Resize", new Color(1f, 1f, 1f, 0f), true);
            DebugUi.IgnoreLayout(handle);
            RectTransform rect = handle.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(16f, 16f);
            rect.anchoredPosition = Vector2.zero;
            // 모서리 점 3개(오른쪽 아래 삼각형).
            Vector2[] dots = { new Vector2(-4f, 4f), new Vector2(-8f, 4f), new Vector2(-4f, 8f) };
            for (int i = 0; i < dots.Length; i++)
            {
                Image dot = DebugUi.Panel(rect, "Dot " + i, new Color(style.muted.r, style.muted.g, style.muted.b, 0.7f), 1);
                RectTransform dotRect = dot.rectTransform;
                dotRect.anchorMin = dotRect.anchorMax = new Vector2(1f, 0f);
                dotRect.pivot = new Vector2(1f, 0f);
                dotRect.sizeDelta = new Vector2(2f, 2f);
                dotRect.anchoredPosition = dots[i];
            }
            DebugDragHandle drag = DebugUi.Component<DebugDragHandle>(handle.gameObject);
            drag.Dragged = Resize;
            drag.Ended = SaveState;
            DebugUi.AddTip(handle, "끌어서 크기 조절");
        }

        private void BuildTooltip(DebugHubStyle style)
        {
            Image background = DebugUi.Panel(canvasRect, "Debug Tooltip", style.tooltip, 6);
            tooltip = background.rectTransform;
            tooltip.anchorMin = tooltip.anchorMax = new Vector2(0.5f, 0.5f);
            tooltip.pivot = new Vector2(0f, 1f);
            CanvasGroup tooltipGroup = DebugUi.Component<CanvasGroup>(background.gameObject);
            tooltipGroup.blocksRaycasts = false;
            tooltipGroup.interactable = false;
            DebugUi.Column(background, 0f, new RectOffset(10, 10, 7, 8), false);
            var fitter = DebugUi.Component<ContentSizeFitter>(background.gameObject);
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            tooltipText = DebugUi.Text(tooltip, "Text", string.Empty, style.smallSize + 1f, style.text);
            tooltipText.textWrappingMode = TextWrappingModes.Normal;
            tooltipText.overflowMode = TextOverflowModes.Overflow;
            tooltipLayout = DebugUi.Layout(tooltipText, preferredWidth: TooltipWidth);
            tooltip.gameObject.SetActive(false);
        }

        private void RefreshTabs()
        {
            tabScratch.Clear();
            for (int i = 0; i < DebugTabs.Order.Length; i++)
            {
                string tab = DebugTabs.Order[i];
                if (tab == DebugTabs.Favorites || DebugRegistry.HasTab(tab))
                    tabScratch.Add(tab);
            }
            IReadOnlyList<DebugSection> sections = DebugRegistry.Sections;
            for (int i = 0; i < sections.Count; i++)
            {
                string tab = sections[i].Tab;
                if (!tabScratch.Contains(tab) && sections[i].Items.Count > 0)
                    tabScratch.Add(tab);
            }

            if (!tabScratch.Contains(state.tab))
                state.tab = tabScratch.Count > 1 ? tabScratch[1] : DebugTabs.Favorites;
            string signature = string.Join("|", tabScratch);
            if (signature != tabSignature)
            {
                tabSignature = signature;
                RebuildTabs();
            }

            DebugHubStyle style = DebugUi.Style;
            int errors = DebugLogCapture.ErrorCount;
            for (int i = 0; i < tabViews.Count; i++)
            {
                TabView view = tabViews[i];
                bool selected = query.Length == 0 && view.Tab == state.tab;
                if (view.Selected != selected)
                {
                    view.Selected = selected;
                    view.Background.color = selected ? style.tabSelected : Color.white;
                    view.Button.colors = selected ? DebugUi.SolidColors() : DebugUi.GhostColors();
                    view.Label.color = selected ? style.textStrong : style.label;
                    view.Label.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
                }
                string text = view.Tab == DebugTabs.SystemTab && errors > 0
                    ? $"{view.Tab} <color={DebugHubStyle.Hex(style.error)}>●{errors}</color>"
                    : view.Tab;
                if (text == view.LastText)
                    continue;
                view.LastText = text;
                view.Label.text = text;
            }
        }

        private void RebuildTabs()
        {
            DebugUi.ClearChildren(tabList);
            tabViews.Clear();
            DebugHubStyle style = DebugUi.Style;
            bool searching = query.Length > 0;
            for (int i = 0; i < tabScratch.Count; i++)
            {
                string tab = tabScratch[i];
                bool selected = !searching && tab == state.tab;
                Image background = DebugUi.Panel(tabList, "Tab " + tab, selected ? style.tabSelected : Color.white, 5, true);
                DebugUi.FixedHeight(background, 32f);
                var button = DebugUi.Component<Button>(background.gameObject);
                button.onClick.RemoveAllListeners();
                button.targetGraphic = background;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                button.colors = selected ? DebugUi.SolidColors() : DebugUi.GhostColors();
                TextMeshProUGUI label = DebugUi.Text(background.transform, "Label", tab, style.bodySize - 1f,
                    selected ? style.textStrong : style.label, TextAlignmentOptions.MidlineLeft, selected, true);
                DebugUi.Stretch(label.rectTransform, 12f, 0f, 6f, 0f);
                button.onClick.AddListener(() =>
                {
                    DebugUi.Deselect();
                    SelectTab(tab);
                });
                tabViews.Add(new TabView
                {
                    Tab = tab, Button = button, Background = background, Label = label, Selected = selected
                });
            }
        }

        private void RebuildContent()
        {
            foreach (DebugRowView row in rows)
                row.ResetTransientState();
            rows.Clear();
            if (highlighted != null)
                highlighted.SetHighlight(false);
            DebugUi.ClearChildren(contentRoot);
            content = DebugUi.Rect(contentRoot, "Page " + (query.Length > 0 ? "Search" : state.tab));
            DebugUi.Column(content, 1f);
            DebugUi.ClearChildren(content);
            highlighted = null;

            if (query.Length > 0)
                BuildSearch();
            else if (state.tab == DebugTabs.Favorites)
                BuildFavorites();
            else
                BuildTab(state.tab);

            builtRevision = DebugRegistry.Revision;
            builtPrefsVersion = DebugPrefs.Version;
            dirty = false;
            if (resetScroll)
            {
                resetScroll = false;
                contentRoot.anchoredPosition = new Vector2(contentRoot.anchoredPosition.x, 0f);
            }
        }

        private void BuildTab(string tab)
        {
            bool any = false;
            IReadOnlyList<DebugSection> sections = DebugRegistry.Sections;
            for (int i = 0; i < sections.Count; i++)
            {
                DebugSection section = sections[i];
                if (section.Tab != tab || section.Items.Count == 0)
                    continue;
                any = true;
                AddSection(section);
            }
            if (!any)
                AddHint("이 탭에는 아직 항목이 없어요.");
        }

        private void BuildFavorites()
        {
            DebugSectionHeader.Build(content, "즐겨찾기", null, false, false, null);
            BuildFavoriteGroup("Favorites Rows", DebugPrefs.Favorites, "항목 오른쪽의 ☆를 누르면 여기에 모여요.");

            DebugSectionHeader.Build(content, "최근 사용", $"마지막으로 쓴 {DebugPrefs.RecentLimit}개", false, false, null);
            BuildFavoriteGroup("Recent Rows", DebugPrefs.Recent, "버튼이나 선택을 쓰면 여기에 남아요.");

            IReadOnlyList<DebugSection> sections = DebugRegistry.Sections;
            for (int i = 0; i < sections.Count; i++)
            {
                if (sections[i].Tab == DebugTabs.Favorites && sections[i].Items.Count > 0)
                    AddSection(sections[i]);
            }
        }

        private void BuildFavoriteGroup(string name, IReadOnlyList<string> ids, string hint)
        {
            RectTransform page = content;
            content = DebugUi.Rect(page, name);
            DebugUi.Column(content, 1f);
            DebugUi.ClearChildren(content);
            if (AddRowsById(ids) == 0)
                AddHint(hint);
            content = page;
        }

        // Editor 제작 단계에서 모든 탭, 검색과 즐겨찾기 행을 프리팹에 저장한다.
        public void BakePagesForAuthoring()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("프리팹 제작은 Edit 모드에서 실행하세요.");
            RefreshTabs();
            foreach (string tab in tabScratch)
            {
                content = DebugUi.Rect(contentRoot, "Page " + tab);
                DebugUi.Column(content, 1f);
                if (tab == DebugTabs.Favorites)
                {
                    BuildFavorites();
                    RectTransform page = content;
                    foreach (string name in new[] { "Favorites Rows", "Recent Rows" })
                    {
                        content = (RectTransform)page.Find(name);
                        foreach (DebugItem item in DebugRegistry.AllItems)
                            AddRow(item, item.Section.Tab + " › " + item.Section.Title);
                        AddHint(string.Empty);
                        content = page;
                    }
                }
                else
                    BuildTab(tab);
                AddHint(string.Empty);
                content.gameObject.SetActive(false);
            }
            content = DebugUi.Rect(contentRoot, "Page Search");
            DebugUi.Column(content, 1f);
            foreach (DebugSection section in DebugRegistry.Sections)
            {
                DebugSectionHeader.Build(content, section.Tab + " › " + section.Title, null, false, false, null);
                foreach (DebugItem item in section.Items)
                    AddRow(item, null);
            }
            AddHint(string.Empty);
            content.gameObject.SetActive(false);
            content = contentRoot;
            rows.Clear();
            root.gameObject.SetActive(false);
        }

        private void BuildSearch()
        {
            int matches = 0;
            var hits = new List<DebugItem>();
            IReadOnlyList<DebugSection> sections = DebugRegistry.Sections;
            for (int i = 0; i < sections.Count; i++)
            {
                DebugSection section = sections[i];
                hits.Clear();
                bool sectionHit = section.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                for (int j = 0; j < section.Items.Count; j++)
                {
                    DebugItem item = section.Items[j];
                    if (sectionHit || item.Matches(query))
                        hits.Add(item);
                }
                if (hits.Count == 0)
                    continue;
                DebugSectionHeader.Build(content, $"{section.Tab} › {section.Title}", null, false, false, null);
                for (int j = 0; j < hits.Count; j++)
                    AddRow(hits[j], null);
                matches += hits.Count;
            }
            if (matches == 0)
                AddHint($"'{query}' 검색 결과가 없어요.");
        }

        private void AddSection(DebugSection section)
        {
            bool collapsed = Application.isPlaying && DebugPrefs.IsCollapsed(section.Key);
            DebugSectionHeader.Build(content, section.Title, section.Note, true, collapsed, () =>
            {
                DebugPrefs.SetCollapsed(section.Key, !collapsed);
                dirty = true;
                Refresh();
            });
            if (collapsed)
                return;
            for (int i = 0; i < section.Items.Count; i++)
                AddRow(section.Items[i], null);
        }

        private int AddRowsById(IReadOnlyList<string> ids)
        {
            int shown = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                DebugItem item = DebugRegistry.Find(ids[i]);
                if (item == null)
                    continue;
                AddRow(item, $"{item.Section.Tab} › {item.Section.Title}");
                shown++;
            }
            return shown;
        }

        private void AddRow(DebugItem item, string context)
        {
            if (!pageRows.TryGetValue(content, out Dictionary<string, DebugRowView> cached))
            {
                cached = new Dictionary<string, DebugRowView>();
                pageRows.Add(content, cached);
            }
            if (cached.TryGetValue(item.Id, out DebugRowView existing))
            {
                existing.Root.SetAsLastSibling();
                existing.Refresh();
                rows.Add(existing);
                return;
            }
            DebugRowView view = DebugRowView.Create(item);
            try
            {
                view.Build(content, context);
                cached.Add(item.Id, view);
                rows.Add(view);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw new InvalidOperationException("디버그 프리팹 행 연결 실패: " + item.Id, exception);
            }
        }

        private void AddHint(string text)
        {
            DebugHubStyle style = DebugUi.Style;
            TextMeshProUGUI hint = DebugUi.Text(content, "Hint", text, style.smallSize, style.muted);
            hint.textWrappingMode = TextWrappingModes.Normal;
            hint.margin = new Vector4(2f, 4f, 0f, 2f);
        }

        private void DisposeRows()
        {
            foreach (Dictionary<string, DebugRowView> cached in pageRows.Values)
                foreach (DebugRowView row in cached.Values)
                    row.Dispose();
            pageRows.Clear();
            rows.Clear();
        }

        private void FocusPending()
        {
            string id = pendingFocusId;
            pendingFocusId = null;
            Canvas.ForceUpdateCanvases();
            DebugRowView row = null;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Item.Id == id)
                {
                    row = rows[i];
                    break;
                }
            }
            if (row == null || row.Root == null)
                return;

            float contentHeight = contentRoot.rect.height;
            float viewHeight = scroll.viewport.rect.height;
            if (contentHeight > viewHeight)
            {
                Vector3 top = contentRoot.InverseTransformPoint(row.Root.TransformPoint(new Vector3(0f, row.Root.rect.yMax, 0f)));
                float target = Mathf.Clamp(-top.y - 24f, 0f, contentHeight - viewHeight);
                contentRoot.anchoredPosition = new Vector2(contentRoot.anchoredPosition.x, target);
            }
            if (highlighted != null)
                highlighted.SetHighlight(false);
            row.SetHighlight(true);
            highlighted = row;
            highlightUntil = Time.unscaledTime + 1.5f;
        }

        private void RefreshInfo()
        {
            PersistentSceneFlow flow = PersistentSceneFlow.Instance;
            string scene = flow != null ? flow.CurrentSubSceneName : null;
            if (string.IsNullOrEmpty(scene))
                scene = SceneManager.GetActiveScene().name;
            if (flow != null && flow.IsSwitching)
                scene += " (전환 중)";
            string text = $"{scene} · {accountLabel} · {DebugPerf.ShortFps}";
            if (text == lastInfo)
                return;
            lastInfo = text;
            info.text = text;
            DebugUi.Layout(info, preferredWidth: Mathf.Min(240f, info.GetPreferredValues(text).x + 4f), flexibleWidth: 0f, minWidth: 40f);
        }

        private void OnSearchChanged(string value)
        {
            query = (value ?? string.Empty).Trim();
            dirty = true;
            resetScroll = true;
            Refresh();
        }

        private void CycleOpacity()
        {
            state.opacity = (state.opacity + 1) % Opacities.Length;
            ApplyOpacity();
            SaveState();
        }

        private void ApplyOpacity()
        {
            state.opacity = Mathf.Clamp(state.opacity, 0, Opacities.Length - 1);
            float alpha = Opacities[state.opacity];
            group.alpha = alpha;
            opacityLabel.text = Mathf.RoundToInt(alpha * 100f) + "%";
        }

        private void ToggleHistory()
        {
            bool show = !historyPanel.gameObject.activeSelf;
            historyPanel.gameObject.SetActive(show);
            if (show)
                FillHistory();
        }

        private void FillHistory()
        {
            DebugHubStyle style = DebugUi.Style;
            IReadOnlyList<DebugRuntime.Record> history = DebugRuntime.History;
            builder.Clear();
            for (int i = history.Count - 1; i >= 0; i--)
            {
                DebugRuntime.Record record = history[i];
                if (builder.Length > 0)
                    builder.Append('\n');
                builder.Append("<color=").Append(DebugHubStyle.Hex(style.muted)).Append('>')
                    .Append(record.At.ToString("HH:mm:ss")).Append("</color> <color=")
                    .Append(DebugHubStyle.Hex(record.Success ? style.ok : style.warn)).Append(">●</color> <noparse>")
                    .Append(DebugRuntime.Describe(record)).Append("</noparse>");
            }
            historyText.text = builder.Length > 0 ? builder.ToString() : "아직 실행한 항목이 없어요.";
        }

        private void HideConfirm()
        {
            confirmAction = null;
            if (confirmBar != null)
                confirmBar.gameObject.SetActive(false);
        }

        private void ShowTooltip(string text)
        {
            if (!Visible)
                return;
            tooltipText.text = text;
            tooltipLayout.preferredWidth = Mathf.Min(TooltipWidth, tooltipText.GetPreferredValues(text).x + 2f);
            tooltip.gameObject.SetActive(true);
            tooltip.SetAsLastSibling();
            tooltipVisible = true;
            PositionTooltip();
        }

        private void HideTooltip()
        {
            tooltipVisible = false;
            if (tooltip != null)
                tooltip.gameObject.SetActive(false);
        }

        private void PositionTooltip()
        {
            if (Mouse.current == null)
                return;
            Vector2 screen = Mouse.current.position.ReadValue();
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local))
                return;
            float halfWidth = canvasRect.rect.width * 0.5f;
            bool flip = local.x + tooltipLayout.preferredWidth + 40f > halfWidth;
            tooltip.pivot = new Vector2(flip ? 1f : 0f, 1f);
            tooltip.anchoredPosition = local + new Vector2(flip ? -14f : 16f, -18f);
        }

        private void Move(Vector2 delta)
        {
            state.x += delta.x;
            state.y += delta.y;
            ApplyRect(true);
        }

        private void Resize(Vector2 delta)
        {
            state.width += delta.x;
            state.height -= delta.y;
            ApplyRect(true);
        }

        private void ApplyRect(bool force)
        {
            Vector2 canvas = canvasRect.rect.size;
            if (canvas.x > 1f && canvas.y > 1f)
            {
                float maxWidth = Mathf.Max(MinWidth, canvas.x * 0.8f);
                float maxHeight = Mathf.Max(MinHeight, canvas.y * 0.8f);
                state.width = Mathf.Clamp(state.width, MinWidth, maxWidth);
                state.height = Mathf.Clamp(state.height, MinHeight, maxHeight);
                state.x = Mathf.Clamp(state.x, -canvas.x, -state.width);
                state.y = Mathf.Clamp(state.y, -(canvas.y - state.height), 0f);
            }
            var position = new Vector2(state.x, state.y);
            var size = new Vector2(state.width, state.height);
            if (!force && root.anchoredPosition == position && root.sizeDelta == size)
                return;
            root.anchoredPosition = position;
            root.sizeDelta = size;
        }

        private static State LoadState()
        {
            string json = PlayerPrefs.GetString(DebugPrefs.WindowKey, string.Empty);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    State loaded = JsonUtility.FromJson<State>(json);
                    if (loaded != null)
                        return loaded;
                }
                catch (Exception)
                {
                    // 깨진 값은 기본값으로 되돌린다.
                }
            }
            return new State();
        }
    }
}
#endif
