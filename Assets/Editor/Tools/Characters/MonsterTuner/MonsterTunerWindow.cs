using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Overburst.EditorTools.MonsterTuner
{
    public sealed partial class MonsterTunerWindow : EditorWindow
    {
        private const string Root = "Assets/Editor/Tools/Characters/MonsterTuner/";
        private readonly MonsterTunerCatalog catalog = new MonsterTunerCatalog();
        private readonly Dictionary<string, MonsterTunerSession> sessions = new Dictionary<string, MonsterTunerSession>();
        private readonly MonsterTunerPreviewStage stage = new MonsterTunerPreviewStage();
        private List<MonsterTunerCatalog.Entry> filtered = new List<MonsterTunerCatalog.Entry>();
        private MonsterTunerSession session;
        private MonsterTunerViewport viewport;
        private ListView list;
        private ScrollView fields;
        private Label status, selection, timeLabel;
        private Button saveButton, playButton;
        private ToolbarSearchField search;
        private DropdownField theme, grade;
        private Slider timeline;
        private MonsterTunerTimeline phaseTimeline;
        [SerializeField] private int tab;
        [SerializeField] private string selectedDefinitionGuid;
        private double lastTick;
        private Vector2 renderedViewportSize;
        private bool rebuilding, draggingPoint;
        private string dragJson;
        private EnemyAbilityDefinition workingAbility;
        private int abilityIndex;
        private string selectedPointKey;
        internal bool VerificationOnly;
        internal void CloseVerification() { if (!VerificationOnly) throw new InvalidOperationException(); hasUnsavedChanges = false; Close(); }

        [MenuItem("OVERBURST/Monsters/몬스터 튜너")]
        public static void Open()
        {
            var window = GetWindow<MonsterTunerWindow>();
            window.titleContent = new GUIContent("몬스터 튜너");
            window.minSize = new Vector2(1100f, 720f);
            window.Show();
        }
        public void CreateGUI()
        {
            rootVisualElement.Clear();
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Root + "MonsterTunerWindow.uxml");
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(Root + "MonsterTunerWindow.uss");
            if (tree == null || sheet == null) { rootVisualElement.Add(new Label("몬스터 튜너 화면 자산이 없습니다.")); return; }
            tree.CloneTree(rootVisualElement); rootVisualElement.styleSheets.Add(sheet);
            status = rootVisualElement.Q<Label>("status");
            var header = rootVisualElement.Q("header");
            var title = new Label("몬스터 튜너"); title.AddToClassList("mt-title"); header.Add(title);
            selection = new Label("기존 몬스터 관리 · 튜닝"); selection.AddToClassList("mt-selection"); header.Add(selection);
            header.Add(new Button(ShowChanges) { text = "변경 보기" });
            header.Add(new Button(RestorePrevious) { text = "직전 저장 복원" });
            saveButton = new Button(SaveSelected) { text = "검증 후 저장" }; saveButton.AddToClassList("mt-save"); header.Add(saveButton);
            var body = rootVisualElement.Q("body");
            var outer = new TwoPaneSplitView(0, EditorPrefs.GetFloat("Overburst.MonsterTuner.ListWidth", 240f), TwoPaneSplitViewOrientation.Horizontal);
            outer.style.flexGrow = 1; body.Add(outer);
            var left = Pane("mt-left"); var centerAndRight = new VisualElement(); centerAndRight.style.flexGrow = 1;
            outer.Add(left); outer.Add(centerAndRight);
            var inner = new TwoPaneSplitView(1, EditorPrefs.GetFloat("Overburst.MonsterTuner.DetailWidth", 360f), TwoPaneSplitViewOrientation.Horizontal);
            inner.style.flexGrow = 1; centerAndRight.Add(inner);
            var center = Pane("mt-center"); var right = Pane("mt-right"); inner.Add(center); inner.Add(right);
            left.style.minWidth = 220f; right.style.minWidth = 320f;
            left.RegisterCallback<GeometryChangedEvent>(e => EditorPrefs.SetFloat("Overburst.MonsterTuner.ListWidth", e.newRect.width));
            right.RegisterCallback<GeometryChangedEvent>(e => EditorPrefs.SetFloat("Overburst.MonsterTuner.DetailWidth", e.newRect.width));
            BuildList(left); BuildPreview(center); BuildTabs(right);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Undo.undoRedoPerformed -= UndoRedo; Undo.undoRedoPerformed += UndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload; AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            EditorApplication.playModeStateChanged -= PlayState; EditorApplication.playModeStateChanged += PlayState;
            RefreshCatalog();
            if (session != null && stage.Actor != null) { RefreshPoints(); BuildFields(); UpdateHeader(); }
            lastTick = EditorApplication.timeSinceStartup;
        }
        private static VisualElement Pane(string css)
        {
            var element = new VisualElement(); element.AddToClassList("mt-pane"); element.AddToClassList(css); return element;
        }
        private void BuildList(VisualElement left)
        {
            search = new ToolbarSearchField(); search.RegisterValueChangedCallback(_ => Filter()); left.Add(search);
            theme = new DropdownField("테마", new List<string> { "전체" }, 0);
            grade = new DropdownField("체급", new List<string> { "전체" }, 0);
            theme.RegisterValueChangedCallback(_ => Filter()); grade.RegisterValueChangedCallback(_ => Filter());
            left.Add(theme); left.Add(grade);
            left.Add(new Button(RefreshCatalog) { text = "목록 새로고침" });
            list = new ListView { fixedItemHeight = 46f, selectionType = SelectionType.Single, virtualizationMethod = CollectionVirtualizationMethod.FixedHeight };
            list.AddToClassList("mt-list");
            list.makeItem = () =>
            {
                var row = new VisualElement(); row.AddToClassList("mt-item");
                var icon = new Image { name = "icon", scaleMode = ScaleMode.ScaleToFit }; icon.style.width = 40; icon.style.height = 40; row.Add(icon);
                var labels = new VisualElement(); labels.AddToClassList("mt-item__text");
                var headline = new VisualElement(); headline.style.flexDirection = FlexDirection.Row; labels.Add(headline);
                var name = new Label { name = "name" }; name.AddToClassList("mt-item__name"); name.style.flexGrow = 1; headline.Add(name);
                var state = new Label { name = "state" }; state.AddToClassList("mt-item__state"); headline.Add(state);
                var meta = new Label { name = "meta" }; meta.AddToClassList("mt-item__meta"); labels.Add(meta); row.Add(labels); return row;
            };
            list.bindItem = (row, index) =>
            {
                if (index >= filtered.Count) return;
                var entry = filtered[index]; bool dirty = sessions.TryGetValue(entry.Guid, out var draft) && draft.Dirty;
                row.Q<Label>("name").text = entry.Label;
                row.Q<Label>("meta").text = entry.Definition.EnemyId;
                row.Q<Label>("state").text = (dirty ? "미저장" : "") + (!entry.Registered ? (dirty ? " · " : "") + "미등록" : "");
                row.tooltip = entry.Label + " · " + entry.Theme + " · " + entry.Grade + "\n" + row.Q<Label>("meta").text + " · " + row.Q<Label>("state").text;
                BindThumbnail(row, entry);
            };
            list.unbindItem = (row, _) => UnbindThumbnail(row);
            list.selectionChanged += items => { if (!rebuilding) SelectEntry(items.OfType<MonsterTunerCatalog.Entry>().FirstOrDefault()); };
            left.Add(list);
        }
        private void RefreshCatalog()
        {
            try
            {
                catalog.Refresh();
                theme.choices = new[] { "전체" }.Concat(catalog.Entries.Select(e => e.Theme).Distinct()).ToList();
                grade.choices = new[] { "전체" }.Concat(catalog.Entries.Select(e => e.Grade).Distinct()).ToList();
                Filter();
                if (session == null && filtered.Count > 0)
                {
                    int index = filtered.FindIndex(e => e.Guid == selectedDefinitionGuid);
                    list.SetSelection(Mathf.Max(0, index));
                }
                SetStatus("몬스터 " + catalog.Entries.Count + "종 · 선택한 몬스터만 저장합니다.");
            }
            catch (Exception e) { SetStatus(e.Message, true); }
        }
        private void Filter()
        {
            string query = search.value ?? string.Empty;
            filtered = catalog.Entries.Where(e => (theme.value == "전체" || e.Theme == theme.value)
                && (grade.value == "전체" || e.Grade == grade.value)
                && (e.Label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || e.Definition.EnemyId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            rebuilding = true; list.itemsSource = filtered; list.Rebuild(); rebuilding = false;
        }
        private void SelectEntry(MonsterTunerCatalog.Entry entry)
        {
            if (entry == null || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            try
            {
                if (!sessions.TryGetValue(entry.Guid, out session))
                {
                    session = MonsterTunerSession.Create(entry.Definition, !VerificationOnly); sessions.Add(entry.Guid, session);
                    if (!VerificationOnly && System.IO.File.Exists(session.RecoveryPath) && EditorUtility.DisplayDialog("편집 사본 복원", entry.Label + "의 미저장 편집을 복원할까요?", "복원", "폐기")) session.Restore();
                    else session.Persist();
                }
                CloseSaveReview(); stage.Load(session); selectedPointKey = null; viewport.Select(null, false); abilityIndex = 0;
                selectedDefinitionGuid = entry.Guid;
                BuildFields(); RefreshPoints(); UpdateHeader();
                RenderNow();
                SetStatus(stage.Message);
            }
            catch (Exception e) { stage.Dispose(); SetStatus("프리뷰를 열지 못했습니다: " + e.Message, true); }
        }
        private void BuildPreview(VisualElement center)
        {
            var controls = new VisualElement(); controls.AddToClassList("mt-row"); controls.AddToClassList("mt-toolbar");
            string[] views = { "정면", "측면", "게임 쿼터뷰" };
            for (int i = 0; i < views.Length; i++) { int view = i; controls.Add(new Button(() => { stage.SetView(view); RenderNow(); }) { text = views[i] }); }
            controls.Add(new Button(() => { stage.Fit(); RenderNow(); }) { text = "맞춤 F" });
            var points = new Toggle("점") { value = true }; points.RegisterValueChangedCallback(e => { viewport.ShowPoints = e.newValue; viewport.Refresh(); }); controls.Add(points);
            var volumes = new Toggle("범위") { value = true }; volumes.RegisterValueChangedCallback(e => { viewport.ShowVolumes = e.newValue; viewport.Refresh(); }); controls.Add(volumes);
            center.Add(controls);
            BuildQuickMotions(center);
            viewport = new MonsterTunerViewport(stage); center.Add(viewport);
            BuildLegend(controls);
            var snap = new DropdownField("스냅", new List<string> { "0.01m", "0.05m", "0.1m", "없음" }, 0);
            snap.style.width = 125;
            snap.RegisterValueChangedCallback(_ => viewport.Snap = new[] { .01f, .05f, .1f, 0f }[snap.index]); controls.Add(snap);
            viewport.Changed += RenderNow;
            viewport.SelectionChanged += point =>
            {
                selectedPointKey = point?.Key;
                if (point != null)
                {
                    tab = point.Key == "attack-volume" ? 3 : point.Label.Contains("오라") ? 2 : point.Label.Contains("패링") || point.Label.Contains("머즐") ? 3 : 1;
                    BuildFields();
                }
                RefreshLegend(); RefreshPointCard();
                SetStatus(point != null ? point.Label + " · 축 드래그 또는 오른쪽 숫자 입력" : stage.Message);
            };
            viewport.EditBegan += () => { draggingPoint = true; dragJson = JsonUtility.ToJson(session); Undo.RecordObject(session, "기준점 이동"); };
            viewport.EditEnded += () => { draggingPoint = false; session?.Persist(); BuildFields(); UpdateHeader(); };
            viewport.EditCancelled += () => { draggingPoint = false; if (session != null && dragJson != null) { JsonUtility.FromJsonOverwrite(dragJson, session); session.Persist(); stage.RefreshValues(); BuildFields(); } };
            viewport.PlaybackRequested += TogglePlayback;
            var playback = new VisualElement(); playback.AddToClassList("mt-row"); playback.AddToClassList("mt-toolbar");
            playButton = new Button(TogglePlayback) { text = "▶ 재생" }; playback.Add(playButton);
            playback.Add(new Button(() => { stage.Playing = false; stage.Sample(stage.Time - 1f / Mathf.Max(1f, stage.Clip != null ? stage.Clip.frameRate : 30f)); RenderNow(); }) { text = "◀ 1프레임" });
            playback.Add(new Button(() => { stage.Playing = false; stage.Sample(stage.Time + 1f / Mathf.Max(1f, stage.Clip != null ? stage.Clip.frameRate : 30f)); RenderNow(); }) { text = "1프레임 ▶" });
            var speed = new DropdownField(new List<string> { "0.25×", "0.5×", "1×", "1.5×", "2×" }, 2);
            speed.RegisterValueChangedCallback(_ => stage.Speed = new[] { .25f, .5f, 1f, 1.5f, 2f }[speed.index]); playback.Add(speed);
            var loop = new Toggle("반복") { value = stage.Loop };
            loop.RegisterValueChangedCallback(e => stage.Loop = e.newValue); playback.Add(loop);
            timeLabel = new Label(); timeLabel.style.flexGrow = 1; playback.Add(timeLabel); center.Add(playback);
            timeline = new Slider(0f, 1f); timeline.AddToClassList("mt-timeline");
            timeline.RegisterValueChangedCallback(e => { stage.Playing = false; stage.Sample(e.newValue * stage.Duration); RenderNow(); }); center.Add(timeline);
            phaseTimeline = new MonsterTunerTimeline(stage); center.Add(phaseTimeline);
            var hint = new Label("점 선택 · 축 이동 · 우클릭 회전 · 중간 버튼 이동 · 휠 확대/축소 · Space 재생");
            hint.AddToClassList("mt-note"); center.Add(hint);
        }
        private void BuildTabs(VisualElement right)
        {
            var row = new VisualElement(); row.AddToClassList("mt-row");
            string[] labels = { "크기", "위치·범위", "효과", "공격", "모션" };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i; var button = new Button(() => { tab = index; BuildFields(); }) { text = labels[i], name = "tab" + i };
                button.AddToClassList("mt-tab"); row.Add(button);
            }
            right.Add(row); BuildPointCard(right);
            fields = new ScrollView(); fields.AddToClassList("mt-fields"); right.Add(fields);
        }
        private void BuildFields()
        {
            fields?.Clear(); RefreshPointCard(); if (session == null || stage.Actor == null) return;
            for (int i = 0; i < 5; i++) rootVisualElement.Q<Button>("tab" + i)?.EnableInClassList("selected", i == tab);
            if (tab == 0) BuildScaleFields(); else if (tab == 1) BuildPointFields();
            else if (tab == 2) BuildAuraFields(); else if (tab == 3) BuildAttackFields(); else BuildAnimationFields();
        }
        private void Tick()
        {
            if (viewport == null) return;
            double now = EditorApplication.timeSinceStartup;
            if (now - lastTick < 1d / 30d) return;
            float delta = (float)(now - lastTick); lastTick = now;
            bool locked = EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling;
            fields?.SetEnabled(!locked); viewport.SetEnabled(!locked); list?.SetEnabled(!locked);
            pointCard?.SetEnabled(!locked);
            rootVisualElement.Q("quick-motions")?.SetEnabled(!locked);
            saveButton.SetEnabled(session != null && session.Dirty && !locked);
            if (locked || !rootVisualElement.visible || stage.Actor == null) return;
            stage.Advance(delta);
            TickThumbnails();
            if (stage.Playing || stage.NeedsRender || viewport.contentRect.size != renderedViewportSize) RenderNow();
        }
        private void RenderNow()
        {
            if (viewport == null || stage.Actor == null) return;
            try
            {
                float dpi = EditorGUIUtility.pixelsPerPoint;
                renderedViewportSize = viewport.contentRect.size;
                stage.Render(Mathf.RoundToInt(viewport.contentRect.width * dpi), Mathf.RoundToInt(viewport.contentRect.height * dpi)); viewport.Refresh();
                timeline.SetValueWithoutNotify(stage.Time / Mathf.Max(.001f, stage.Duration));
                phaseTimeline?.Refresh();
                timeLabel.text = stage.Time.ToString("F2") + " / " + stage.Duration.ToString("F2") + "s  ·  " + stage.ZoomPercent.ToString("F0") + "%";
                playButton.text = stage.Playing ? "Ⅱ 정지" : "▶ 재생";
                RefreshPointCard();
                RefreshQuickMotionStatus();
            }
            catch (Exception e) { stage.Playing = false; SetStatus("프리뷰: " + e.Message, true); }
        }
        private void TogglePlayback() { stage.Playing = !stage.Playing; RenderNow(); }
        private void UpdateHeader()
        {
            RefreshQuickMotions();
            if (selection != null) selection.text = session != null ? session.Definition.DisplayName + " · 변경 " + session.edits.Count + "개" : "몬스터를 선택하세요.";
            hasUnsavedChanges = sessions.Values.Any(s => s != null && s.Dirty);
            saveChangesMessage = "미저장 " + sessions.Values.Count(s => s != null && s.Dirty) + "마리의 편집이 있습니다. 저장은 선택 몬스터에만 적용합니다. 다른 사본은 창을 다시 열 때 복원할 수 있습니다. 폐기는 모든 사본을 지웁니다.";
            list?.RefreshItems();
        }
        private void SetStatus(string message, bool error = false) { if (status != null) { status.text = message; status.EnableInClassList("mt-error", error); } }
        private void ShowChanges()
        {
            if (session == null) return;
            OpenSaveReview(false);
        }
        private void RestorePrevious()
        {
            if (session == null) return;
            var history = MonsterTunerHistory.Read(session.definitionGuid);
            if (history == null) { SetStatus("이 몬스터의 직전 저장 기록이 없습니다."); return; }
            string error = history.RestoreDraft(session);
            if (!string.IsNullOrEmpty(error)) { SetStatus(error, true); return; }
            stage.Load(session); RefreshPoints(); BuildFields(); UpdateHeader();
            SetStatus("직전 저장 전의 값을 편집 사본으로 불러왔습니다. 변경 보기와 검증 후 저장에서 확인하세요.");
        }
        private void SaveSelected()
        {
            OpenSaveReview(false);
        }
        public override void SaveChanges() { OpenSaveReview(true); if (session != null && !session.Dirty) { foreach (var draft in sessions.Values) draft.Persist(); base.SaveChanges(); } }
        public override void DiscardChanges() { foreach (var draft in sessions.Values) draft.Discard(); base.DiscardChanges(); }
        private void UndoRedo() { session?.Persist(); if (stage.Actor != null) { stage.Load(session); RefreshPoints(); BuildFields(); UpdateHeader(); RenderNow(); } }
        private void BeforeReload() { CloseSaveReview(); DisposeThumbnails(); foreach (var draft in sessions.Values) if (draft != null) draft.Persist(); stage.Dispose(); }
        private void PlayState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) { list?.SetEnabled(false); BeforeReload(); }
            if (state == PlayModeStateChange.EnteredEditMode && session != null)
            {
                stage.Load(session); RefreshPoints(); BuildFields(); UpdateHeader(); list?.RefreshItems();
                int selected = filtered.FindIndex(entry => entry.Guid == selectedDefinitionGuid);
                list?.SetSelectionWithoutNotify(selected < 0 ? Array.Empty<int>() : new[] { selected });
                list?.SetEnabled(true);
            }
        }
        private void OnDisable()
        {
            EditorApplication.update -= Tick; Undo.undoRedoPerformed -= UndoRedo;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload; EditorApplication.playModeStateChanged -= PlayState;
            BeforeReload();
            foreach (var draft in sessions.Values) if (draft != null) Object.DestroyImmediate(draft);
            sessions.Clear(); session = null;
            if (workingAbility != null) Object.DestroyImmediate(workingAbility); workingAbility = null;
        }
    }
}
