using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Overburst.EditorBalance
{
    public sealed partial class OverburstBalanceTableWindow
    {
        ToolbarSearchField searchField;
        Toggle changedFilter, levelFilter;
        bool changedOnly;
        VisualElement workspace, details, emptyState, detailFields, playerCondition, qualityCondition, seedCondition;
        Label sheetTitle, sheetNote, selectionTitle, selectionPath, draftSummary, saveSummary;
        Button applyButton, saveButton, discardButton, resetRowButton, pingButton;
        readonly List<Button> navigation = new List<Button>();
        readonly List<Button> sortHeaders = new List<Button>();
        IVisualElementScheduledItem stateSchedule;
        readonly string[] viewNames = { "무기 원본", "공통 성장", "공통 10구간", "장비 도감", "몬스터 능력치", "공격 패턴" };
        readonly string[] viewNotes = { "무기별 기본 수치와 공통 공격 속도", "플레이어·아이템·몬스터의 공통 성장", "아이템 레벨별 장비의 기본 수치", "장비 외형의 등장 레벨 구간", "몬스터 체력과 공통 이동 프로필", "피해 예산·공격 속성·판정 소유권" };

        static T Styled<T>(T element, string style) where T : VisualElement { element.AddToClassList(style); return element; }
        static Label Text(string text, string style) => Styled(new Label(text), style);
        void BuildWorkspaceUI()
        {
            var root = rootVisualElement; stateSchedule?.Pause(); root.UnregisterCallback<GeometryChangedEvent>(HandleGeometry); root.UnregisterCallback<KeyDownEvent>(HandleShortcut); root.Clear(); root.AddToClassList("balance-root");
            var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Editor/Tools/Balance/OverburstBalanceTableWindow.uss");
            if (stylesheet != null && !root.styleSheets.Contains(stylesheet)) root.styleSheets.Add(stylesheet);
            var header = Styled(new VisualElement(), "balance-header");
            var heading = new VisualElement(); heading.Add(Text("OVERBURST  /  DATA & TUNING", "balance-eyebrow"));
            heading.Add(Text("밸런스 테이블", "balance-title")); header.Add(heading);
            header.Add(Text("원본을 비교하고, 초안으로 조절합니다", "balance-subtitle")); root.Add(header);
            var body = Styled(new VisualElement(), "balance-body"); root.Add(body);
            var sidebar = Styled(new VisualElement(), "balance-sidebar"); body.Add(sidebar); navigation.Clear();
            for (int i = 0; i < 6; i++)
            {
                if (i % 2 == 0) sidebar.Add(Text(new[] { "무기", "장비", "몬스터" }[i / 2], "balance-nav-heading"));
                int index = i;
                var button = Styled(new Button(() => Navigate(index)) { text = viewNames[i], name = "navigate-" + i }, "balance-nav");
                sidebar.Add(button); navigation.Add(button);
            }
            var reload = Styled(new Button(() => { if (ConfirmDiscard()) Reload(); }) { text = "원본 다시 읽기" }, "balance-secondary");
            reload.tooltip = "현재 초안을 확인한 뒤 원본을 다시 읽습니다. 적용한 자산의 저장 대기는 유지합니다.";
            sidebar.Add(Styled(new VisualElement(), "balance-spacer")); sidebar.Add(reload);
            sidebar.Add(Text("● 초안에서 바뀐 값\n읽기 전용 값은 해당 제작 도구에서 조절하세요.", "balance-sidebar-note"));
            workspace = Styled(new VisualElement(), "balance-workspace"); body.Add(workspace);
            var center = Styled(new VisualElement(), "balance-center"); workspace.Add(center);
            var sheetHeading = Styled(new VisualElement(), "balance-sheet-heading");
            var sheetLabels = new VisualElement(); sheetTitle = Text("", "balance-sheet-title"); sheetNote = Text("", "balance-muted");
            sheetLabels.Add(sheetTitle); sheetLabels.Add(sheetNote); sheetHeading.Add(sheetLabels);
            count = Text("", "balance-count"); sheetHeading.Add(count); center.Add(sheetHeading);
            // Kept as the single view state control for scripts and change callbacks.
            subView = new DropdownField(new List<string> { "무기 원본", "공통 성장" }, 0); subView.style.display = DisplayStyle.None;
            subView.RegisterValueChangedCallback(_ => { if (!rebuilding) ChooseView(); }); center.Add(subView);
            var filters = Styled(new VisualElement(), "balance-filters"); center.Add(filters);
            searchField = Styled(new ToolbarSearchField { name = "row-search" }, "balance-search");
            searchField.tooltip = "이름 또는 ID 검색 · Ctrl+F";
            searchField.RegisterValueChangedCallback(e => { query = e.newValue ?? ""; Filter(); }); filters.Add(searchField);
            groupFilter = Styled(new DropdownField(new List<string> { "전체" }, 0), "balance-group");
            groupFilter.RegisterValueChangedCallback(e => { if (!rebuilding) { group = e.newValue; Filter(); } }); filters.Add(groupFilter);
            changedFilter = new Toggle("변경만") { name = "changed-only" };
            changedFilter.RegisterValueChangedCallback(e => { changedOnly = e.newValue; Filter(); }); filters.Add(changedFilter);
            var conditions = Styled(new VisualElement(), "balance-conditions"); center.Add(conditions);
            var level = new IntegerField("미리보기 Lv") { value = itemLevel, isDelayed = true };
            level.RegisterValueChangedCallback(e => { itemLevel = Mathf.Clamp(e.newValue, 1, 100); level.SetValueWithoutNotify(itemLevel); Filter(); RefreshPreview(); }); conditions.Add(level);
            var player = new IntegerField("플레이어 Lv") { value = playerLevel, isDelayed = true };
            player.RegisterValueChangedCallback(e => { playerLevel = Mathf.Clamp(e.newValue, 1, 100); player.SetValueWithoutNotify(playerLevel); RefreshPreview(); });
            playerCondition = player; conditions.Add(player);
            var quality = new EnumField("품질", grade); quality.RegisterValueChangedCallback(e => { grade = (ItemGrade)e.newValue; RefreshPreview(); });
            qualityCondition = quality; conditions.Add(quality);
            var seedField = new IntegerField("표본 시드") { value = seed, isDelayed = true };
            seedField.RegisterValueChangedCallback(e => { seed = e.newValue; RefreshPreview(); }); seedCondition = seedField; conditions.Add(seedField);
            levelFilter = new Toggle("현재 레벨에 등장하는 외형만") { value = onlyAvailableLevel };
            levelFilter.RegisterValueChangedCallback(e => { onlyAvailableLevel = e.newValue; Filter(); }); center.Add(levelFilter);
            tableHost = Styled(new VisualElement(), "balance-table"); center.Add(tableHost);
            emptyState = Styled(new VisualElement { name = "empty-state" }, "balance-empty");
            emptyState.Add(Text("조건에 맞는 행이 없습니다", "balance-sheet-title"));
            emptyState.Add(Text("검색어와 그룹, 변경 필터를 확인하세요.", "balance-muted"));
            emptyState.Add(Styled(new Button(ClearFilters) { text = "필터 초기화", name = "clear-filters" }, "balance-secondary")); tableHost.Add(emptyState);
            details = Styled(new VisualElement { name = "selection-panel" }, "balance-details"); workspace.Add(details);
            var detailScroll = Styled(new ScrollView(), "balance-detail-scroll"); details.Add(detailScroll);
            detailScroll.Add(Text("선택한 행", "balance-eyebrow"));
            selectionTitle = Text("행을 선택하세요", "balance-selection-title"); selectionTitle.name = "selection-title"; detailScroll.Add(selectionTitle);
            selectionPath = Text("", "balance-path"); detailScroll.Add(selectionPath);
            var detailActions = Styled(new VisualElement(), "balance-detail-actions");
            pingButton = Styled(new Button(() => { if (selected != null) EditorGUIUtility.PingObject(selected.Source); }) { text = "원본 위치" }, "balance-secondary");
            resetRowButton = Styled(new Button(ResetSelectedRow) { text = "이 행 초안 되돌리기", name = "reset-row" }, "balance-secondary");
            detailActions.Add(pingButton); detailActions.Add(resetRowButton); detailScroll.Add(detailActions);
            detailFields = new VisualElement { name = "field-comparison" }; detailScroll.Add(detailFields);
            detailScroll.Add(Text("레벨·품질 계산", "balance-section-title"));
            preview = Text("", "balance-preview"); detailScroll.Add(preview);
            var footer = Styled(new VisualElement(), "balance-footer"); root.Add(footer);
            var state = Styled(new VisualElement(), "balance-state"); footer.Add(state);
            var badges = Styled(new VisualElement(), "balance-state-badges"); state.Add(badges);
            draftSummary = Text("초안 없음", "balance-state-badge"); draftSummary.name = "draft-summary";
            saveSummary = Text("저장 대기 없음", "balance-state-badge"); badges.Add(draftSummary); badges.Add(saveSummary);
            status = Text("", "balance-status"); state.Add(status);
            var actions = Styled(new VisualElement(), "balance-actions"); footer.Add(actions);
            discardButton = Styled(new Button(DiscardDraftOnly) { text = "초안 버리기", name = "discard-draft" }, "balance-secondary");
            actions.Add(discardButton); actions.Add(Styled(new Button(ValidateDraft) { text = "검증" }, "balance-secondary"));
            applyButton = Styled(new Button(ApplyDraft) { text = "1  초안 적용", name = "apply-draft", tooltip = "원본 메모리에 반영 · Ctrl+Enter" }, "balance-primary");
            saveButton = Styled(new Button(SaveApplied) { text = "2  적용분 저장", name = "save-applied", tooltip = "적용한 자산을 디스크에 저장 · Ctrl+S" }, "balance-save");
            actions.Add(applyButton); actions.Add(saveButton);
            root.RegisterCallback<GeometryChangedEvent>(HandleGeometry);
            root.RegisterCallback<KeyDownEvent>(HandleShortcut);
            stateSchedule = root.schedule.Execute(UpdateWorkspaceState).Every(500);
            ChooseCategory(category); SetWorkspaceSize(position.width);
        }
        void HandleGeometry(GeometryChangedEvent _) => SetWorkspaceSize(rootVisualElement.resolvedStyle.width);
        void HandleShortcut(KeyDownEvent e)
        { if (!(e.ctrlKey || e.commandKey)) return; if (e.keyCode == KeyCode.F) searchField.Focus(); else if (e.keyCode == KeyCode.S) SaveApplied(); else if (e.keyCode == KeyCode.Return) ApplyDraft(); else return; e.StopPropagation(); }

        void Navigate(int index)
        {
            ChooseCategory(index / 2);
            if (index % 2 == 1) subView.value = subView.choices[1];
        }
        int CurrentNavigation => view == View.Weapons ? 0 : view == View.Growth ? 1 : view == View.GearTiers ? 2 : view == View.GearItems ? 3 : view == View.Enemies ? 4 : 5;
        void SetWorkspaceSize(float width) => rootVisualElement.EnableInClassList("balance-root--compact", width < 1220);
        void ClearFilters()
        {
            query = ""; group = "전체"; changedOnly = false; onlyAvailableLevel = false;
            searchField.SetValueWithoutNotify(""); groupFilter.SetValueWithoutNotify("전체"); changedFilter.SetValueWithoutNotify(false); levelFilter.SetValueWithoutNotify(false); Filter();
        }
        void BuildDataTable()
        {
            tableHost.Clear(); sortHeaders.Clear();
            float width = NameWidth + headings.Length * CellWidth;
            var horizontal = Styled(new ScrollView(ScrollViewMode.Horizontal), "balance-table-scroll");
            var body = new VisualElement { style = { width = width, flexGrow = 1 } };
            var header = Styled(new VisualElement(), "balance-table-header");
            for (int i = -1; i < headings.Length; i++)
            {
                int column = i; var button = Styled(new Button(() => Sort(column)), "balance-column");
                button.style.width = i < 0 ? NameWidth : CellWidth; sortHeaders.Add(button); header.Add(button);
            }
            body.Add(header);
            list = Styled(new ListView { itemsSource = filtered, fixedItemHeight = 36, selectionType = SelectionType.Single }, "balance-list");
            list.makeItem = MakeRow; list.bindItem = BindRow;
            list.selectionChanged += items => { selected = items.OfType<Row>().FirstOrDefault(); RefreshPreview(); UpdateWorkspaceState(); };
            body.Add(list); horizontal.Add(body); tableHost.Add(horizontal); tableHost.Add(emptyState);
            RefreshSortHeaders();
        }
        void RefreshSortHeaders()
        {
            for (int i = 0; i < sortHeaders.Count; i++)
            { int column = i - 1; sortHeaders[i].text = (column < 0 ? "이름 / ID" : headings[column]) + (sortColumn == column ? ascending ? "  ↑" : "  ↓" : "  ↕"); sortHeaders[i].EnableInClassList("balance-column--sorted", sortColumn == column); }
        }
        void UpdateWorkspaceState()
        {
            if (applyButton == null) return;
            int changed = documents.Values.Count(d => d.IsChanged);
            draftSummary.text = changed == 0 ? "초안 없음" : "초안 변경 " + changed + "개";
            saveSummary.text = pendingSave.Count == 0 ? "저장 대기 없음" : "저장 대기 " + pendingSave.Count + "개";
            draftSummary.EnableInClassList("balance-state-badge--changed", changed > 0); saveSummary.EnableInClassList("balance-state-badge--pending", pendingSave.Count > 0);
            applyButton.SetEnabled(changed > 0 && CanWrite()); saveButton.SetEnabled(pendingSave.Count > 0 && CanWrite()); discardButton.SetEnabled(changed > 0);
            int index = CurrentNavigation; sheetTitle.text = viewNames[index]; sheetNote.text = viewNotes[index];
            for (int i = 0; i < navigation.Count; i++) navigation[i].EnableInClassList("balance-nav--selected", i == index);
            playerCondition.style.display = view == View.Weapons ? DisplayStyle.Flex : DisplayStyle.None;
            bool quality = view == View.Weapons || view == View.GearItems;
            qualityCondition.style.display = seedCondition.style.display = quality ? DisplayStyle.Flex : DisplayStyle.None;
            levelFilter.style.display = view == View.GearItems ? DisplayStyle.Flex : DisplayStyle.None;
            resetRowButton.SetEnabled(selected != null && selected.Fields.Any(f => f != null && f.Changed)); pingButton.SetEnabled(selected != null);
        }
        void RefreshSelectionDetails()
        {
            if (detailFields == null) return;
            detailFields.Clear(); selectionTitle.text = selected?.Name ?? "행을 선택하세요";
            if (selected == null) { selectionPath.text = "표에서 행을 고르면 변경 전후를 비교합니다."; return; }
            selectionPath.text = AssetDatabase.GetAssetPath(selected.Source); selectionPath.tooltip = selectionPath.text;
            detailFields.Add(Text("수치 비교  ·  원본 → 초안", "balance-section-title"));
            for (int i = 0; i < selected.Fields.Count; i++)
            {
                var field = selected.Fields[i]; var row = Styled(new VisualElement(), "balance-comparison");
                row.Add(Text(headings[i], "balance-comparison-name"));
                string value = selected.ReadOnly.TryGetValue(i, out var readOnly) ? (readOnly.value?.ToString("0.###") ?? readOnly.text) + " · 읽기 전용"
                    : field == null ? "연결 없음" : field.Type == SerializedPropertyType.Boolean ? (field.OriginalValue > .5f ? "켬" : "끔") + " → " + (field.Value > .5f ? "켬" : "끔")
                    : (field.OriginalValue * field.DisplayFactor).ToString("0.###") + " → " + (field.Value * field.DisplayFactor).ToString("0.###");
                var number = Text(value, "balance-comparison-value"); number.EnableInClassList("balance-comparison-value--changed", field != null && field.Changed); row.Add(number); detailFields.Add(row);
            }
            UpdateWorkspaceState();
        }
        void ResetSelectedRow()
        {
            if (selected == null) return;
            foreach (var field in selected.Fields.Where(f => f != null && f.Changed)) field.Value = field.OriginalValue;
            Changed();
        }
        void DiscardDraftOnly()
        {
            foreach (var document in documents.Values) foreach (var field in document.Fields.Where(f => f.Changed)) field.Value = field.OriginalValue;
            Changed(); RefreshStatus("적용 전 초안을 버렸습니다. 적용한 자산의 저장 대기는 유지합니다.");
        }
    }
}
