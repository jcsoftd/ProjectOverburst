using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Overburst.EditorTools.BossMaker
{
    public sealed partial class BossMakerWindow : EditorWindow
    {
        const string StylePath = "Assets/Editor/Tools/Bosses/BossMaker/BossMaker.uss";
        [SerializeField] EnemyBossMaterialCollection selectedCollection;
        [SerializeField] int attackIndex, strikeIndex, tab;
        [SerializeField] List<BossMakerRecovery> recovery = new List<BossMakerRecovery>();
        readonly Dictionary<EnemyBossMaterialCollection, BossMakerSession> sessions = new Dictionary<EnemyBossMaterialCollection, BossMakerSession>();
        BossMakerSession session;
        BossMakerPreview preview;
        List<BossMakerDraft> filtered = new List<BossMakerDraft>();
        ListView attackList;
        VisualElement previewHost, overlay, review;
        Image image;
        ScrollView fields;
        DropdownField bossPicker, motionPicker;
        ObjectField collectionField;
        Slider scrub;
        FloatField frameInput;
        Label status, attackTitle, timeLabel, previewLabel;
        Button playButton, saveButton;
        BossMakerTimeline timeline;
        string query = "", message = "";
        double tickAt, renderAt, sourceCheckAt;
        bool renderDirty = true, rebuilding;
        bool sourceView;
        BossMakerDraft sourceDraft;
        public static int OpenPreviews { get; private set; }
        internal BossMakerSession Session => session;
        internal BossMakerDraft Draft => session != null && session.Drafts.Count > 0 ? session.Drafts[Mathf.Clamp(attackIndex, 0, session.Drafts.Count - 1)] : null;
        internal BossMakerPreview Preview => preview;
        [MenuItem("OVERBURST/Bosses/보스 메이커")]
        public static void Open() { var window = GetWindow<BossMakerWindow>("보스 메이커"); window.minSize = new Vector2(1140, 780); }
        void OnEnable()
        {
            tickAt = EditorApplication.timeSinceStartup;
            saveChangesMessage = "변경한 공격이 있습니다. 저장하면 이 창에서 편집한 모든 보스의 사본을 원본에 반영합니다. 버리기를 선택하면 미저장 변경을 취소합니다.";
            EditorApplication.update += Tick; Undo.undoRedoPerformed += UndoChanged;
            EditorApplication.playModeStateChanged += PlayChanged; AssemblyReloadEvents.beforeAssemblyReload += CaptureRecovery;
        }
        void OnDisable()
        {
            CaptureRecovery(); EditorApplication.update -= Tick; Undo.undoRedoPerformed -= UndoChanged;
            EditorApplication.playModeStateChanged -= PlayChanged; AssemblyReloadEvents.beforeAssemblyReload -= CaptureRecovery;
            DisposePreview(); foreach (var value in sessions.Values) value.Dispose(); sessions.Clear(); session = null;
        }
        public void CreateGUI()
        {
            DisposePreview(); rootVisualElement.Clear(); var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            if (style != null) rootVisualElement.styleSheets.Add(style); rootVisualElement.AddToClassList("bm-root");
            var header = Row("bm-header"); rootVisualElement.Add(header);
            var brand = new Label("BOSS MAKER"); brand.AddToClassList("bm-brand"); header.Add(brand);
            var catalog = AssetDatabase.FindAssets("t:EnemyBossMaterialCollection").Select(g => AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(c => c != null && c.actorDefinition != null).OrderBy(c => c.name).ToList();
            bossPicker = new DropdownField("보스", catalog.Select(c => c.name.Replace("BMC_", "")).ToList(), 0); bossPicker.name = "boss-picker";
            bossPicker.RegisterValueChangedCallback(_ => { if (!rebuilding && bossPicker.index >= 0 && bossPicker.index < catalog.Count) LoadCollection(catalog[bossPicker.index]); }); header.Add(bossPicker);
            collectionField = new ObjectField { objectType = typeof(EnemyBossMaterialCollection), allowSceneObjects = false, name = "boss-collection" };
            collectionField.style.flexGrow = 1; collectionField.RegisterValueChangedCallback(e => { if (!rebuilding && e.newValue is EnemyBossMaterialCollection c) LoadCollection(c); }); header.Add(collectionField);
            saveButton = Button("변경 검토", OpenReview, "boss-review"); saveButton.AddToClassList("bm-primary"); header.Add(saveButton);
            header.Add(Button("다시 불러오기", ReloadCurrent, "boss-reload"));
            var body = Row("bm-body"); rootVisualElement.Add(body);
            var left = new VisualElement(); left.AddToClassList("bm-library"); body.Add(left);
            left.Add(Heading("공격 재료")); var search = new ToolbarSearchField { name = "boss-search" }; search.RegisterValueChangedCallback(e => { query = e.newValue; BuildLibrary(); }); left.Add(search);
            attackList = new ListView { name = "boss-attacks", fixedItemHeight = 58, selectionType = SelectionType.Single, virtualizationMethod = CollectionVirtualizationMethod.FixedHeight };
            attackList.makeItem = () => { var card = new VisualElement(); card.AddToClassList("bm-attack"); card.Add(new Label { name = "name" }); var sub = new Label { name = "sub" }; sub.AddToClassList("bm-muted"); card.Add(sub); return card; };
            attackList.bindItem = (element, i) => { var d = filtered[i]; element.Q<Label>("name").text = (d.Dirty ? "● " : "") + d.Material.displayName; element.Q<Label>("sub").text = $"{d.Material.strikes.Length}타 · {DeliveryLabel(d.Material.delivery)} · {ShapeLabel(d.Material.strikes[0].shape)}"; element.tooltip = d.Material.runtimeClip.name; };
            attackList.selectionChanged += items => { if (!rebuilding && items.FirstOrDefault() is BossMakerDraft d) SelectAttack(session.Drafts.IndexOf(d)); }; left.Add(attackList);
            var hint = new Label("공격별 사본 편집\n변경 검토 → 선택한 보스에 저장\nRM 제외 · 게임 카메라와 분리"); hint.AddToClassList("bm-note"); left.Add(hint);
            var center = new VisualElement(); center.AddToClassList("bm-center"); body.Add(center); BuildPreviewUI(center);
            var right = new VisualElement(); right.AddToClassList("bm-editor"); body.Add(right);
            attackTitle = Heading(""); right.Add(attackTitle);
            var tabs = Row("bm-tabs"); right.Add(tabs);
            var names = new[] { "판정", "타격·패링", "피해·속도", "이동·발사" };
            for (int i = 0; i < names.Length; i++) { int at = i; tabs.Add(Button(names[i], () => { tab = at; BuildFields(); }, "boss-tab-" + i)); }
            fields = new ScrollView { name = "boss-fields" }; right.Add(fields);
            review = new VisualElement { name = "boss-save-review" }; review.AddToClassList("bm-review"); review.style.display = DisplayStyle.None; rootVisualElement.Add(review);
            status = new Label { name = "boss-status" }; status.AddToClassList("bm-status"); rootVisualElement.Add(status);
            rootVisualElement.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.actionKey && e.keyCode == KeyCode.S) { saveButton.Focus(); OpenReview(); e.PreventDefault(); e.StopPropagation(); return; }
                if (e.target is TextField || e.target is FloatField || e.target is IntegerField || e.target is TextElement) return;
                if (e.keyCode == KeyCode.Space) { TogglePlay(); e.StopPropagation(); }
                if (e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.RightArrow) { StepFrame(e.keyCode == KeyCode.LeftArrow ? -1 : 1); e.StopPropagation(); }
                if (e.keyCode == KeyCode.F) { preview?.Fit(); renderDirty = true; }
            });
            if (selectedCollection == null) selectedCollection = catalog.FirstOrDefault();
            if (selectedCollection != null) LoadCollection(selectedCollection); else SetMessage("프로젝트의 Boss Material Collection을 선택하세요.");
        }
        internal void LoadCollection(EnemyBossMaterialCollection collection)
        {
            if (collection == null) return;
            try
            {
                CaptureRecovery(); DisposePreview(); selectedCollection = collection;
                if (!sessions.TryGetValue(collection, out session))
                {
                    string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(collection));
                    session = new BossMakerSession(collection, recovery.Where(r => r.collectionGuid == guid)); sessions.Add(collection, session);
                }
                attackIndex = Mathf.Clamp(attackIndex, 0, session.Drafts.Count - 1); strikeIndex = 0;
                rebuilding = true; collectionField.SetValueWithoutNotify(collection);
                bossPicker.SetValueWithoutNotify(collection.name.Replace("BMC_", "")); rebuilding = false;
                BuildLibrary(); RebuildMotionPicker(); BuildFields(); EnsurePreview();
                review.style.display = DisplayStyle.None; SetMessage("공격을 선택해 모션과 판정을 확인하세요.");
            }
            catch (Exception e) { rebuilding = false; SetMessage(e.Message); }
        }
        void BuildLibrary()
        {
            if (session == null || attackList == null) return;
            filtered = session.Drafts.Where(d => string.IsNullOrEmpty(query) || (d.Material.displayName + " " + d.Material.materialId + " " + d.Material.runtimeClip?.name).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            rebuilding = true; attackList.itemsSource = filtered; attackList.Rebuild();
            int selected = filtered.IndexOf(Draft); if (selected >= 0) attackList.SetSelectionWithoutNotify(new[] { selected }); rebuilding = false;
        }
        internal void SelectAttack(int index)
        {
            if (session == null || index < 0 || index >= session.Drafts.Count) return;
            if (sourceView) SetSourceView(false);
            attackIndex = index; strikeIndex = 0; motionPicker.SetValueWithoutNotify("공격 모션");
            preview?.Load(Draft); renderDirty = true; review.style.display = DisplayStyle.None; BuildLibrary(); BuildFields(); UpdateStatus();
        }
        void EnsurePreview()
        {
            if (preview != null || Draft == null || previewHost == null || previewHost.contentRect.width < 2 || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            preview = new BossMakerPreview(selectedCollection); OpenPreviews++; preview.Load(Draft); renderDirty = true;
        }
        void DisposePreview()
        {
            if (image != null) image.image = null;
            if (preview != null) { preview.Dispose(); preview = null; OpenPreviews--; }
            sourceDraft?.Dispose(); sourceDraft = null; sourceView = false;
            rootVisualElement.Q<Toggle>("boss-show-source")?.SetValueWithoutNotify(false);
        }
        void PlayChanged(PlayModeStateChange state) { CaptureRecovery(); DisposePreview(); renderDirty = true; if (state != PlayModeStateChange.EnteredEditMode) SetMessage("Play 종료 후 독립 프리뷰를 재개합니다."); }
        void Tick()
        {
            double now = EditorApplication.timeSinceStartup; if (now - tickAt < 1d / 20d) return; float delta = (float)(now - tickAt); tickAt = now;
            if (image == null) return;
            bool busy = EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating;
            fields?.SetEnabled(!busy && !sourceView); saveButton?.SetEnabled(!busy && session != null && session.Dirty);
            if (busy) { DisposePreview(); return; }
            try
            {
                if (now - sourceCheckAt > 1d) { sourceCheckAt = now; RefreshSources(); }
                EnsurePreview(); if (preview == null) return;
                if (preview.Playing) { preview.Tick(delta); renderDirty = true; }
                if (renderDirty && now - renderAt >= 1d / 20d && previewHost.contentRect.width > 2 && previewHost.contentRect.height > 2)
                {
                    renderAt = now; renderDirty = false; float pixels = EditorGUIUtility.pixelsPerPoint;
                    image.image = preview.Render(Mathf.Clamp(Mathf.RoundToInt(previewHost.contentRect.width * pixels), 2, 1600), Mathf.Clamp(Mathf.RoundToInt(previewHost.contentRect.height * pixels), 2, 1200));
                    overlay.MarkDirtyRepaint(); UpdateTransport();
                }
            }
            catch (Exception e) { if (preview != null) preview.Playing = false; renderDirty = false; SetMessage("프리뷰: " + e.Message); }
            hasUnsavedChanges = sessions.Values.Any(s => s.Dirty);
        }
        void UndoChanged()
        {
            if (session == null) return;
            RefreshSources();
            foreach (var s in sessions.Values) foreach (var d in s.Drafts) d.EnsureParries();
            BuildFields(); BuildLibrary(); Changed(false);
        }
        void RefreshSources()
        {
            if (session == null) return;
            bool refreshed = false;
            foreach (var s in sessions.Values) foreach (var d in s.Drafts) refreshed |= d.RefreshCleanSource();
            if (!refreshed) return;
            float at = preview?.Progress ?? 0f;
            sourceDraft?.Dispose(); sourceDraft = null; sourceView = false;
            rootVisualElement.Q<Toggle>("boss-show-source")?.SetValueWithoutNotify(false);
            if (preview != null)
            {
                preview.Load(Draft, motionPicker.index > 0 && motionPicker.index <= supportMotions.Count ? supportMotions[motionPicker.index - 1].runtime : null);
                preview.Seek(at);
            }
            BuildFields(); BuildLibrary(); renderDirty = true; SetMessage("변경된 원본을 편집 사본에 새로 반영했습니다.");
        }
        void Changed(bool refreshLibrary = true)
        {
            if (Draft == null) return; CaptureRecovery();
            if (Draft.Validate().Count == 0) preview?.Changed(); else if (preview != null) preview.Playing = false;
            renderDirty = true; review.style.display = DisplayStyle.None; if (refreshLibrary) BuildLibrary(); UpdateStatus(); UpdateTransport();
        }
        void SetMessage(string text) { message = text; UpdateStatus(); }
        void UpdateStatus()
        {
            if (status == null) return; var errors = session?.Validate(true) ?? new List<string>();
            status.text = errors.Count > 0 ? "저장 전 수정: " + errors[0] : $"{(session?.Drafts.Count(d => d.Dirty) ?? 0)}개 공격 변경 · " + message;
            status.EnableInClassList("bm-error", errors.Count > 0); hasUnsavedChanges = sessions.Values.Any(s => s.Dirty);
            saveButton?.SetEnabled(session != null && session.Dirty && !EditorApplication.isPlayingOrWillChangePlaymode);
        }
        internal void OpenReview()
        {
            if (session == null || !session.Dirty) { SetMessage("저장할 변경이 없습니다."); return; }
            review.Clear(); review.style.display = DisplayStyle.Flex; review.Add(Heading("변경 검토"));
            var errors = session.Validate(true);
            var scroll = new ScrollView(); scroll.style.maxHeight = 160; review.Add(scroll);
            foreach (var d in session.Drafts.Where(d => d.Dirty))
            {
                var line = new Label(d.Material.displayName + "\n" + string.Join("\n", BossMakerReview.Changes(d)));
                line.AddToClassList("bm-review-line"); scroll.Add(line);
            }
            foreach (var error in errors) scroll.Add(new HelpBox(error, HelpBoxMessageType.Error));
            var actions = Row("bm-actions"); var commit = Button("선택한 보스에 저장", CommitReview, "boss-save-confirm"); commit.SetEnabled(errors.Count == 0); commit.AddToClassList("bm-primary"); actions.Add(commit);
            actions.Add(Button("계속 편집", () => review.style.display = DisplayStyle.None)); review.Add(actions);
        }
        internal void CommitReview()
        {
            try { session.Apply(); CaptureRecovery(); review.style.display = DisplayStyle.None; BuildLibrary(); BuildFields(); SetMessage("저장 완료. 실제 공격 재료와 능력에 반영했습니다."); }
            catch (Exception e) { SetMessage(e.Message); OpenReview(); }
        }
        void ReloadCurrent()
        {
            if (session == null) return;
            if (session.Dirty && !EditorUtility.DisplayDialog("편집 사본 되돌리기", "선택한 보스의 미저장 변경을 버리고 현재 원본을 다시 불러올까요?", "되돌리기", "계속 편집")) return;
            DisposePreview(); sessions.Remove(selectedCollection); session.Dispose(); session = new BossMakerSession(selectedCollection); sessions.Add(selectedCollection, session);
            CaptureRecovery(); BuildFields(); BuildLibrary(); EnsurePreview(); review.style.display = DisplayStyle.None; SetMessage("현재 원본을 다시 불러왔습니다.");
        }
        void CaptureRecovery()
        {
            if (sessions.Count == 0) return;
            var loaded = new HashSet<string>(sessions.Keys.Select(c => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c))));
            recovery.RemoveAll(r => loaded.Contains(r.collectionGuid));
            foreach (var pair in sessions) foreach (var item in pair.Value.Capture()) { item.collectionGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(pair.Key)); recovery.Add(item); }
        }
        public override void SaveChanges()
        {
            try { foreach (var s in sessions.Values.Where(s => s.Dirty)) s.Apply(); CaptureRecovery(); hasUnsavedChanges = false; base.SaveChanges(); }
            catch (Exception e) { SetMessage(e.Message); }
        }
        public override void DiscardChanges() { DisposePreview(); foreach (var s in sessions.Values) s.Dispose(); sessions.Clear(); session = null; recovery.Clear(); hasUnsavedChanges = false; base.DiscardChanges(); }
        internal void ClearVerificationDrafts() { DisposePreview(); foreach (var s in sessions.Values) s.Dispose(); sessions.Clear(); session = null; recovery.Clear(); hasUnsavedChanges = false; }
        static VisualElement Row(string cls = null) { var row = new VisualElement(); row.AddToClassList("bm-row"); if (cls != null) row.AddToClassList(cls); return row; }
        static Label Heading(string text) { var label = new Label(text); label.AddToClassList("bm-heading"); return label; }
        static Button Button(string text, Action action, string name = null) => new Button(action) { text = text, name = name };
        static string DeliveryLabel(EnemyBossMaterialDelivery delivery) => delivery == EnemyBossMaterialDelivery.Melee ? "근접" : delivery == EnemyBossMaterialDelivery.Spit ? "직선 발사" : "포물선 투척";
        static string ShapeLabel(GroundIndicatorShape shape) => shape == GroundIndicatorShape.Sector ? "부채꼴" : shape == GroundIndicatorShape.Circle ? "원형" : shape == GroundIndicatorShape.Donut ? "도넛" : "직사각형";
    }
}
