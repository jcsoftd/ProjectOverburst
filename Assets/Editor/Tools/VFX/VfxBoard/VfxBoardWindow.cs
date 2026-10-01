using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Overburst.EditorTools.Vfx
{
    /// <summary>
    /// 게임 전체 VFX 연결 자리를 카테고리별로 보여 주고, 고르면 미리보기에서 재생한다.
    /// 프리팹 교체는 초안으로 보관하고 현재 선택 항목 하나씩 저장한다. UI는 UI Toolkit(UXML/USS)만 쓴다.
    /// </summary>
    public sealed class VfxBoardWindow : EditorWindow
    {
        private const string WindowTitle = "VFX 관리 보드";
        private const string MenuPath = "JC Tool/VFX/VFX 관리 보드";
        private const string DraftPrefsPrefix = "Overburst.VfxBoard.Drafts.";
        private const double FocusedFrameInterval = 1d / 60d;
        private const double BackgroundFrameInterval = 1d / 30d;
        private const int DialogLineLimit = 18;

        private static readonly (string id, string label)[] Filters =
        {
            ("all", "전체"), ("assigned", "적용됨"), ("empty", "비어 있음"), ("missing", "참조 깨짐"), ("draft", "변경 대기")
        };

        private static readonly (string label, float value)[] Speeds = { ("¼×", .25f), ("½×", .5f), ("1×", 1f), ("2×", 2f) };

        [SerializeField] private List<VfxDraft> drafts = new List<VfxDraft>();
        [SerializeField] private List<VfxDraft> lastApplied = new List<VfxDraft>();
        [SerializeField] private List<VfxExtraSlotSpec> extraSpecs = new List<VfxExtraSlotSpec>();
        [SerializeField] private List<string> collapsedGroups = new List<string>();
        [SerializeField] private string selectedKey;
        [SerializeField] private string filter = "all";
        [SerializeField] private string search = string.Empty;
        [SerializeField] private int viewIndex;
        [SerializeField] private int environmentIndex = 3;
        [SerializeField] private bool floorVisible = true;
        [SerializeField] private bool bloomEnabled = true;
        [SerializeField] private bool loopEnabled = true;
        [SerializeField] private bool previewDraft = true;
        [SerializeField] private bool draftListOpen;
        [SerializeField] private float speed = 1f;
        [SerializeField] private bool draftsLoaded;

        private readonly List<VfxSlot> slots = new List<VfxSlot>();
        private readonly Dictionary<string, VfxSlot> slotsByKey = new Dictionary<string, VfxSlot>();
        private readonly Dictionary<string, int> treeIdsByKey = new Dictionary<string, int>();
        private readonly Dictionary<int, string> groupPathsById = new Dictionary<int, string>();
        private readonly Dictionary<string, VfxDraftCheck> checks = new Dictionary<string, VfxDraftCheck>();

        private VfxBoardPreviewStage stage;
        private VfxBoardViewport viewport;
        private VfxSlot selected;
        private string stageSourceKey;
        private double lastTick;
        private double staleSince = -1d;
        private bool rebuildingTree;

        private TreeView tree;
        private ToolbarSearchField searchField;
        private ObjectField draftField;
        private Label summaryLabel, treeEmptyLabel, breadcrumbLabel, titleLabel, stateChip;
        private Label stageMessage, stageHint, clockLabel, statsLabel;
        private Label currentName, currentPath, draftStateLabel, ownerLabel, propertyLabel, readOnlyLabel, noteLabel;
        private Label draftCountLabel;
        private Image currentIcon;
        private VisualElement filtersRow, compareGroup, environmentGroup, speedGroup, viewGroup, messages, draftCard;
        private VisualElement currentAsset, draftBar;
        private ScrollView draftList;
        private Button playButton, loopButton, floorButton, bloomButton, draftClearButton, draftRemoveButton;
        private Button pingOwnerButton, openOwnerButton, draftToggleButton, revertButton, discardButton, applyButton;
        private Slider timeSlider;

        private sealed class Node
        {
            public string Title;
            public string GroupPath;
            public VfxSlot Slot;
            public int Depth;
            public int Total, Assigned, Empty, Missing, Drafts;
        }

        private sealed class GroupBuilder
        {
            public string Title;
            public string Path;
            public int Depth;
            public int Order;
            public readonly List<GroupBuilder> Groups = new List<GroupBuilder>();
            public readonly List<VfxSlot> Slots = new List<VfxSlot>();
        }

        [MenuItem(MenuPath)]
        private static void Open()
        {
            var window = GetWindow<VfxBoardWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(1120f, 640f);
            window.Show();
        }

        // ---------- 수명 ----------

        private void OnEnable()
        {
            if (!draftsLoaded)
            {
                LoadDraftPrefs();
                draftsLoaded = true;
            }

            EditorApplication.update += Tick;
            EditorApplication.projectChanged += MarkStale;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            EditorApplication.projectChanged -= MarkStale;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            stage?.Dispose();
            stage = null;
            stageSourceKey = null;
        }

        private void MarkStale() => staleSince = EditorApplication.timeSinceStartup;

        private void OnPlayModeChanged(PlayModeStateChange change) => UpdateDraftBar();

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();
            string folder = ResolveFolder();
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(folder + "/VfxBoardWindow.uxml");
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(folder + "/VfxBoard.uss");
            if (layout == null)
            {
                root.Add(new Label("VfxBoardWindow.uxml을 찾지 못했습니다: " + folder));
                return;
            }

            layout.CloneTree(root);
            if (sheet != null)
                root.styleSheets.Add(sheet);
            root.Q("board").style.flexGrow = 1f;

            stage?.Dispose();
            stage = new VfxBoardPreviewStage { Loop = loopEnabled, Speed = speed };
            stage.SetEnvironment(environmentIndex);
            stage.SetFloor(floorVisible);
            stage.SetBloom(bloomEnabled);
            stage.SetView(viewIndex, false);
            stageSourceKey = null;

            BindHeader(root);
            BindList(root);
            BindDetail(root);
            BindDraftBar(root);
            root.RegisterCallback<KeyDownEvent>(OnKeyDown);
            RefreshCatalog();
        }

        private string ResolveFolder()
        {
            string path = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
            return string.IsNullOrEmpty(path)
                ? "Assets/Editor/Tools/VFX/VfxBoard"
                : Path.GetDirectoryName(path)?.Replace('\\', '/');
        }

        // ---------- UI 연결 ----------

        private void BindHeader(VisualElement root)
        {
            summaryLabel = root.Q<Label>("summary");
            root.Q<Button>("refresh").clicked += RefreshCatalog;
            root.Q<Button>("deep-scan").clicked += RunDeepScan;
        }

        private void BindList(VisualElement root)
        {
            searchField = new ToolbarSearchField();
            searchField.SetValueWithoutNotify(search);
            searchField.RegisterValueChangedCallback(evt =>
            {
                search = evt.newValue ?? string.Empty;
                RebuildTree();
            });
            root.Q("search-host").Add(searchField);

            filtersRow = root.Q("filters");
            foreach (var (id, label) in Filters)
            {
                string filterId = id;
                var button = new Button(() =>
                {
                    filter = filterId;
                    RebuildTree();
                }) { name = "filter-" + id, text = label, userData = label };
                button.AddToClassList("vb-filter");
                filtersRow.Add(button);
            }

            tree = new TreeView
            {
                fixedItemHeight = 24,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.Single,
                makeItem = MakeRow,
                bindItem = BindRow
            };
            tree.AddToClassList("vb-tree");
            tree.selectionChanged += OnTreeSelection;
            tree.itemExpandedChanged += OnItemExpanded;
            treeEmptyLabel = root.Q<Label>("tree-empty");
            root.Q("tree-host").Insert(0, tree);

            root.Q<Button>("expand-all").clicked += () =>
            {
                collapsedGroups.Clear();
                tree.ExpandAll();
            };
            root.Q<Button>("collapse-all").clicked += () =>
            {
                collapsedGroups = groupPathsById.Values.ToList();
                tree.CollapseAll();
            };
        }

        private void BindDetail(VisualElement root)
        {
            breadcrumbLabel = root.Q<Label>("breadcrumb");
            titleLabel = root.Q<Label>("slot-title");
            stateChip = root.Q<Label>("state-chip");
            stageMessage = root.Q<Label>("stage-message");
            stageHint = root.Q<Label>("stage-hint");
            clockLabel = root.Q<Label>("clock");
            statsLabel = root.Q<Label>("stats");

            viewport = new VfxBoardViewport(() => stage != null ? stage.CameraRotation : Quaternion.identity);
            viewport.Orbited += delta => { stage.Orbit(delta); RenderNow(); };
            viewport.Zoomed += delta => { stage.Zoom(delta); RenderNow(); };
            viewport.FitRequested += () => { stage.Fit(); RenderNow(); };
            viewport.SurfaceResized += (width, height) =>
            {
                stage.Resize(width, height);
                viewport.SetTexture(stage.Surface);
                RenderNow();
            };
            root.Q("viewport-host").Add(viewport);
            viewport.StretchToParentSize();

            compareGroup = root.Q("compare");
            BuildSegment(compareGroup, new[] { "현재", "교체안" }, index =>
            {
                previewDraft = index == 1;
                UpdateCompareButtons();
                UpdatePreviewSource(true);
            });

            environmentGroup = root.Q("environments");
            BuildSegment(environmentGroup, VfxBoardPreviewStage.Environments.Select(e => e.Label).ToArray(), index =>
            {
                environmentIndex = index;
                stage.SetEnvironment(index);
                SetSegmentState(environmentGroup, index);
                RenderNow();
            });
            SetSegmentState(environmentGroup, environmentIndex);

            playButton = root.Q<Button>("play");
            playButton.clicked += TogglePlay;
            root.Q<Button>("restart").clicked += () =>
            {
                stage.Restart(true);
                RenderNow();
            };
            loopButton = root.Q<Button>("loop");
            loopButton.clicked += () =>
            {
                loopEnabled = !loopEnabled;
                stage.Loop = loopEnabled;
                SyncTransport();
            };

            speedGroup = root.Q("speeds");
            BuildSegment(speedGroup, Speeds.Select(s => s.label).ToArray(), index =>
            {
                speed = Speeds[index].value;
                stage.Speed = speed;
                SyncTransport();
            });

            timeSlider = root.Q<Slider>("time");
            timeSlider.RegisterValueChangedCallback(evt =>
            {
                stage.Scrub(evt.newValue);
                RenderNow();
            });

            viewGroup = root.Q("views");
            BuildSegment(viewGroup, VfxBoardPreviewStage.Views.Select(v => v.Label).ToArray(), index =>
            {
                viewIndex = index;
                stage.SetView(index, true);
                SetSegmentState(viewGroup, index);
                RenderNow();
            });
            SetSegmentState(viewGroup, viewIndex);

            floorButton = root.Q<Button>("floor");
            floorButton.clicked += () =>
            {
                floorVisible = !floorVisible;
                stage.SetFloor(floorVisible);
                SyncTransport();
                RenderNow();
            };
            bloomButton = root.Q<Button>("bloom");
            bloomButton.clicked += () =>
            {
                bloomEnabled = !bloomEnabled;
                stage.SetBloom(bloomEnabled);
                SyncTransport();
                RenderNow();
            };
            root.Q<Button>("fit").clicked += () =>
            {
                stage.Fit();
                RenderNow();
            };

            currentAsset = root.Q("current-asset");
            currentIcon = root.Q<Image>("current-icon");
            currentName = root.Q<Label>("current-name");
            currentPath = root.Q<Label>("current-path");
            currentAsset.RegisterCallback<ClickEvent>(_ => PingCurrent());

            draftCard = root.Q("draft-card");
            draftField = new ObjectField { objectType = typeof(GameObject), allowSceneObjects = false };
            draftField.RegisterValueChangedCallback(OnDraftFieldChanged);
            root.Q("draft-host").Add(draftField);
            draftClearButton = root.Q<Button>("draft-clear");
            draftClearButton.clicked += () =>
            {
                if (selected != null)
                    SetDraft(selected, null, true);
            };
            draftRemoveButton = root.Q<Button>("draft-remove");
            draftRemoveButton.clicked += () =>
            {
                if (selected != null)
                    RemoveDraft(selected.Key);
            };
            draftStateLabel = root.Q<Label>("draft-state");
            messages = root.Q("messages");

            ownerLabel = root.Q<Label>("owner");
            propertyLabel = root.Q<Label>("property");
            readOnlyLabel = root.Q<Label>("readonly");
            noteLabel = root.Q<Label>("note");
            pingOwnerButton = root.Q<Button>("ping-owner");
            pingOwnerButton.clicked += () => PingAsset(OwnerAsset(selected));
            openOwnerButton = root.Q<Button>("open-owner");
            openOwnerButton.clicked += () =>
            {
                Object owner = OwnerAsset(selected);
                if (owner != null)
                    AssetDatabase.OpenAsset(owner);
            };
        }

        private void BindDraftBar(VisualElement root)
        {
            draftBar = root.Q("draft-bar");
            draftCountLabel = root.Q<Label>("draft-count");
            draftList = root.Q<ScrollView>("draft-list");
            draftToggleButton = root.Q<Button>("draft-toggle");
            draftToggleButton.clicked += () =>
            {
                draftListOpen = !draftListOpen;
                UpdateDraftBar();
            };
            revertButton = root.Q<Button>("revert-last");
            revertButton.clicked += CreateRevertDrafts;
            discardButton = root.Q<Button>("discard-all");
            discardButton.clicked += DiscardAllDrafts;
            applyButton = root.Q<Button>("apply-selected");
            applyButton.clicked += ApplySelected;
        }

        private static void BuildSegment(VisualElement group, IReadOnlyList<string> labels, Action<int> onClick)
        {
            group.Clear();
            for (int i = 0; i < labels.Count; i++)
            {
                int index = i;
                var button = new Button(() => onClick(index)) { text = labels[i] };
                button.AddToClassList("vb-seg__item");
                if (i == 0) button.AddToClassList("vb-seg__item--first");
                if (i == labels.Count - 1) button.AddToClassList("vb-seg__item--last");
                group.Add(button);
            }
        }

        private static void SetSegmentState(VisualElement group, int activeIndex)
        {
            for (int i = 0; i < group.childCount; i++)
                group[i].EnableInClassList("is-on", i == activeIndex);
        }

        // ---------- 카탈로그 ----------

        private void RefreshCatalog()
        {
            staleSince = -1d;
            slots.Clear();
            slotsByKey.Clear();
            checks.Clear();
            try
            {
                foreach (VfxSlot slot in VfxSlotCatalog.Collect())
                    AddSlot(slot);
                foreach (VfxExtraSlotSpec spec in extraSpecs)
                    AddSlot(VfxSlotCatalog.CreateExtraSlot(spec));
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }

            RebuildTree();
            UpdateDraftBar();
            slotsByKey.TryGetValue(selectedKey ?? string.Empty, out VfxSlot restored);
            ShowSlot(restored, true);
            if (restored != null && treeIdsByKey.TryGetValue(restored.Key, out int id))
            {
                rebuildingTree = true;
                tree.SetSelectionById(id);
                rebuildingTree = false;
            }
        }

        private void AddSlot(VfxSlot slot)
        {
            if (slot == null || slotsByKey.ContainsKey(slot.Key))
                return;
            slots.Add(slot);
            slotsByKey.Add(slot.Key, slot);
        }

        private void RunDeepScan()
        {
            var known = new HashSet<string>(slots.Where(s => !s.Unregistered).Select(s => s.Key));
            List<VfxExtraSlotSpec> found = VfxSlotCatalog.DeepScan(known, out bool cancelled);
            if (cancelled)
            {
                ShowNotification(new GUIContent("미분류 검사를 취소했습니다."));
                return;
            }

            extraSpecs = found;
            RefreshCatalog();
            ShowNotification(new GUIContent(found.Count == 0
                ? "등록 밖 VFX 참조가 없습니다."
                : "미분류 " + found.Count + "건을 목록 맨 아래에 추가했습니다."));
        }

        // ---------- 목록 ----------

        private bool IsVisible(VfxSlot slot)
        {
            switch (filter)
            {
                case "assigned": if (slot.State != VfxSlotState.Assigned) return false; break;
                case "empty": if (slot.State != VfxSlotState.Empty) return false; break;
                case "missing": if (slot.State != VfxSlotState.Missing) return false; break;
                case "draft": if (FindDraft(slot.Key) == null) return false; break;
            }

            if (string.IsNullOrWhiteSpace(search))
                return true;

            VfxDraft draft = FindDraft(slot.Key);
            string haystack = draft != null ? slot.SearchText + " " + VfxDraftOperations.DraftName(draft).ToLowerInvariant() : slot.SearchText;
            return search.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).All(haystack.Contains);
        }

        private void RebuildTree()
        {
            if (tree == null)
                return;

            var roots = new List<GroupBuilder>();
            int order = 0;
            List<VfxSlot> visible = slots.Where(IsVisible).ToList();
            foreach (VfxSlot slot in visible)
            {
                List<GroupBuilder> level = roots;
                GroupBuilder group = null;
                string path = string.Empty;
                for (int depth = 0; depth < slot.Category.Length; depth++)
                {
                    string title = slot.Category[depth];
                    path = depth == 0 ? title : path + "/" + title;
                    group = level.FirstOrDefault(g => g.Title == title);
                    if (group == null)
                    {
                        group = new GroupBuilder { Title = title, Path = path, Depth = depth, Order = order++ };
                        level.Add(group);
                    }

                    level = group.Groups;
                }

                group?.Slots.Add(slot);
            }

            treeIdsByKey.Clear();
            groupPathsById.Clear();
            int nextId = 1;
            var items = new List<TreeViewItemData<Node>>();
            foreach (GroupBuilder group in Ordered(roots))
                items.Add(BuildItem(group, ref nextId));

            rebuildingTree = true;
            tree.SetRootItems(items);
            tree.Rebuild();
            tree.ExpandAll();
            if (string.IsNullOrWhiteSpace(search))
                foreach (KeyValuePair<int, string> pair in groupPathsById)
                    if (collapsedGroups.Contains(pair.Value))
                        tree.CollapseItem(pair.Key);
            if (selected != null && treeIdsByKey.TryGetValue(selected.Key, out int id))
                tree.SetSelectionById(id);
            else
                tree.ClearSelection();
            rebuildingTree = false;

            treeEmptyLabel.EnableInClassList("vb-hidden", visible.Count > 0);
            UpdateFilterButtons();
            UpdateSummary();
        }

        private static IEnumerable<GroupBuilder> Ordered(IEnumerable<GroupBuilder> groups) =>
            groups.OrderBy(g => VfxSlotCatalog.GroupRank(g.Title)).ThenBy(g => g.Order);

        private TreeViewItemData<Node> BuildItem(GroupBuilder group, ref int nextId)
        {
            int id = nextId++;
            groupPathsById[id] = group.Path;
            var children = new List<TreeViewItemData<Node>>();
            foreach (GroupBuilder child in Ordered(group.Groups))
                children.Add(BuildItem(child, ref nextId));
            foreach (VfxSlot slot in group.Slots)
            {
                int slotId = nextId++;
                treeIdsByKey[slot.Key] = slotId;
                children.Add(new TreeViewItemData<Node>(slotId, new Node { Title = slot.Label, Slot = slot, Depth = group.Depth + 1 }));
            }

            var node = new Node { Title = group.Title, GroupPath = group.Path, Depth = group.Depth };
            CountGroup(group, node);
            return new TreeViewItemData<Node>(id, node, children);
        }

        private void CountGroup(GroupBuilder group, Node node)
        {
            foreach (VfxSlot slot in group.Slots)
            {
                node.Total++;
                if (slot.State == VfxSlotState.Assigned) node.Assigned++;
                else if (slot.State == VfxSlotState.Empty) node.Empty++;
                else node.Missing++;
                if (FindDraft(slot.Key) != null) node.Drafts++;
            }

            foreach (GroupBuilder child in group.Groups)
                CountGroup(child, node);
        }

        private static VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("vb-row");
            var dot = new VisualElement { name = "dot" };
            dot.AddToClassList("vb-dot");
            var texts = new VisualElement();
            texts.AddToClassList("vb-row__texts");
            var title = new Label { name = "title" };
            title.AddToClassList("vb-row__title");
            var sub = new Label { name = "sub" };
            sub.AddToClassList("vb-row__sub");
            texts.Add(title);
            texts.Add(sub);
            var tag = new Label { name = "tag" };
            tag.AddToClassList("vb-tag");
            var badge = new Label { name = "badge" };
            badge.AddToClassList("vb-badge");
            var count = new Label { name = "count" };
            count.AddToClassList("vb-row__count");
            row.Add(dot);
            row.Add(texts);
            row.Add(tag);
            row.Add(badge);
            row.Add(count);
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            var node = tree.GetItemDataForIndex<Node>(index);
            var dot = row.Q("dot");
            var title = row.Q<Label>("title");
            var sub = row.Q<Label>("sub");
            var tag = row.Q<Label>("tag");
            var badge = row.Q<Label>("badge");
            var count = row.Q<Label>("count");
            bool isGroup = node.Slot == null;
            row.EnableInClassList("vb-row--group", isGroup);
            row.EnableInClassList("vb-row--depth0", isGroup && node.Depth == 0);
            title.text = node.Title;

            VfxSlotState state;
            if (isGroup)
            {
                state = node.Missing > 0 ? VfxSlotState.Missing : node.Empty > 0 ? VfxSlotState.Empty : VfxSlotState.Assigned;
                sub.text = string.Empty;
                tag.text = string.Empty;
                count.text = node.Assigned + "/" + node.Total;
                badge.text = node.Drafts > 0 ? "변경 " + node.Drafts : string.Empty;
                badge.RemoveFromClassList("vb-badge--error");
                row.tooltip = node.GroupPath;
            }
            else
            {
                VfxSlot slot = node.Slot;
                state = slot.State;
                VfxDraft draft = FindDraft(slot.Key);
                sub.text = draft != null ? slot.CurrentName + " → " + VfxDraftOperations.DraftName(draft) : slot.CurrentName;
                tag.text = slot.Binding == VfxSlotBinding.Resource ? "코드 경로"
                    : slot.Binding == VfxSlotBinding.Module ? "래퍼 모듈"
                    : slot.Unregistered ? "미확인" : string.Empty;
                bool blocked = draft != null && GetCheck(slot, draft).Blocked;
                badge.text = draft == null ? string.Empty : blocked ? "적용 불가" : "변경 대기";
                badge.EnableInClassList("vb-badge--error", blocked);
                count.text = string.Empty;
                row.tooltip = slot.Breadcrumb + " › " + slot.Label + "\n" + slot.OwnerPath
                    + (string.IsNullOrEmpty(slot.PropertyPath) ? string.Empty : "\n" + slot.PropertyPath);
            }

            dot.EnableInClassList("vb-dot--group", isGroup);
            dot.EnableInClassList("vb-dot--assigned", state == VfxSlotState.Assigned);
            dot.EnableInClassList("vb-dot--empty", state == VfxSlotState.Empty);
            dot.EnableInClassList("vb-dot--missing", state == VfxSlotState.Missing);
            sub.EnableInClassList("vb-hidden", string.IsNullOrEmpty(sub.text));
            tag.EnableInClassList("vb-hidden", string.IsNullOrEmpty(tag.text));
            badge.EnableInClassList("vb-hidden", string.IsNullOrEmpty(badge.text));
            count.EnableInClassList("vb-hidden", string.IsNullOrEmpty(count.text));
        }

        private void OnTreeSelection(IEnumerable<object> items)
        {
            if (rebuildingTree)
                return;

            if (items.FirstOrDefault() is Node node && node.Slot != null)
                ShowSlot(node.Slot, false);
        }

        private void OnItemExpanded(TreeViewExpansionChangedArgs args)
        {
            if (rebuildingTree || !groupPathsById.TryGetValue(args.id, out string path))
                return;

            if (tree.IsExpanded(args.id))
                collapsedGroups.Remove(path);
            else if (!collapsedGroups.Contains(path))
                collapsedGroups.Add(path);
        }

        private void UpdateFilterButtons()
        {
            foreach (var (id, label) in Filters)
            {
                Button button = filtersRow.Q<Button>("filter-" + id);
                int count = slots.Count(s => MatchesFilter(s, id));
                button.text = label + " " + count;
                button.EnableInClassList("is-on", filter == id);
            }
        }

        private bool MatchesFilter(VfxSlot slot, string id)
        {
            switch (id)
            {
                case "assigned": return slot.State == VfxSlotState.Assigned;
                case "empty": return slot.State == VfxSlotState.Empty;
                case "missing": return slot.State == VfxSlotState.Missing;
                case "draft": return FindDraft(slot.Key) != null;
                default: return true;
            }
        }

        private void UpdateSummary()
        {
            if (summaryLabel == null) return;
            int assigned = slots.Count(s => s.State == VfxSlotState.Assigned);
            int empty = slots.Count(s => s.State == VfxSlotState.Empty);
            int missing = slots.Count(s => s.State == VfxSlotState.Missing);
            summaryLabel.text = "슬롯 " + slots.Count + "개  ·  적용 " + assigned + "  ·  비어 있음 " + empty
                + "  ·  참조 깨짐 " + missing + "  ·  변경 대기 " + drafts.Count
                + (EditorApplication.isPlaying ? "  ·  플레이 중(적용 잠김)" : string.Empty);
        }

        // ---------- 상세 ----------

        private void ShowSlot(VfxSlot slot, bool keepCamera)
        {
            selected = slot;
            if (slot != null)
                selectedKey = slot.Key;

            bool has = slot != null;
            breadcrumbLabel.text = has ? slot.Breadcrumb : string.Empty;
            titleLabel.text = has ? slot.Label : "왼쪽 목록에서 VFX 슬롯을 고르세요";
            currentAsset.SetEnabled(has && slot.Current != null);
            currentName.text = has ? slot.CurrentName : "—";
            currentPath.text = !has ? string.Empty
                : slot.Current != null ? AssetDatabase.GetAssetPath(slot.Current)
                : slot.Binding == VfxSlotBinding.Resource ? "Resources/" + slot.ResourcePath : "연결된 프리팹이 없습니다";
            currentIcon.image = has && slot.Current != null ? AssetPreview.GetMiniThumbnail(slot.Current) : null;

            ownerLabel.text = !has ? string.Empty
                : slot.Binding == VfxSlotBinding.Resource ? "코드가 Resources.Load로 불러옴"
                : Path.GetFileName(slot.OwnerPath) + (slot.IsPrefabOwner ? "  ›  " + slot.ComponentType : string.Empty);
            propertyLabel.text = !has ? string.Empty
                : slot.Binding == VfxSlotBinding.Resource ? slot.ResourcePath
                : slot.PropertyPath + (slot.Binding == VfxSlotBinding.Module ? "  (모듈: " + slot.ModulePath + ")" : string.Empty);
            pingOwnerButton.SetEnabled(OwnerAsset(slot) != null);
            openOwnerButton.SetEnabled(OwnerAsset(slot) != null);
            readOnlyLabel.text = has ? slot.ReadOnlyReason : string.Empty;
            readOnlyLabel.EnableInClassList("vb-hidden", string.IsNullOrEmpty(readOnlyLabel.text));
            noteLabel.text = has ? slot.Note : string.Empty;
            noteLabel.EnableInClassList("vb-hidden", string.IsNullOrEmpty(noteLabel.text));

            UpdateDraftCard();
            UpdateDraftBar();
            UpdatePreviewSource(keepCamera);
        }

        private void UpdateDraftCard()
        {
            VfxSlot slot = selected;
            VfxDraft draft = slot != null ? FindDraft(slot.Key) : null;
            bool canSwap = slot != null && slot.CanSwap;
            draftCard.SetEnabled(canSwap);
            draftField.SetValueWithoutNotify(VfxDraftOperations.LoadPrefab(draft));
            draftClearButton.SetEnabled(canSwap && slot.Binding == VfxSlotBinding.Field && slot.State != VfxSlotState.Empty && (draft == null || !draft.clear));
            draftRemoveButton.SetEnabled(draft != null);
            draftStateLabel.text = draft == null
                ? (canSwap ? "프로젝트 창에서 프리팹을 끌어 놓거나 ◎ 버튼으로 고르세요. 아래 '선택 항목 저장'은 현재 항목 하나만 저장합니다." : string.Empty)
                : draft.clear ? "적용하면 이 슬롯을 비웁니다." : "교체 예정: " + slot.CurrentName + " → " + VfxDraftOperations.DraftName(draft);

            messages.Clear();
            if (slot != null && draft != null)
            {
                VfxDraftCheck check = GetCheck(slot, draft);
                foreach (string error in check.Errors) AddMessage(error, "vb-msg--error");
                foreach (string warning in check.Warnings) AddMessage(warning, "vb-msg--warn");
            }

            UpdateStateChip(slot, draft);
            UpdateCompareButtons();
        }

        private void AddMessage(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList("vb-msg");
            label.AddToClassList(className);
            messages.Add(label);
        }

        private void UpdateStateChip(VfxSlot slot, VfxDraft draft)
        {
            stateChip.RemoveFromClassList("vb-state--assigned");
            stateChip.RemoveFromClassList("vb-state--empty");
            stateChip.RemoveFromClassList("vb-state--missing");
            stateChip.RemoveFromClassList("vb-state--draft");
            if (slot == null)
            {
                stateChip.text = string.Empty;
                stateChip.AddToClassList("vb-hidden");
                return;
            }

            stateChip.RemoveFromClassList("vb-hidden");
            if (draft != null)
            {
                stateChip.text = "변경 대기";
                stateChip.AddToClassList("vb-state--draft");
                return;
            }

            stateChip.text = slot.State == VfxSlotState.Assigned ? "적용됨" : slot.State == VfxSlotState.Empty ? "비어 있음" : "참조 깨짐";
            stateChip.AddToClassList(slot.State == VfxSlotState.Assigned ? "vb-state--assigned"
                : slot.State == VfxSlotState.Empty ? "vb-state--empty" : "vb-state--missing");
        }

        private void UpdateCompareButtons()
        {
            VfxDraft draft = selected != null ? FindDraft(selected.Key) : null;
            bool comparable = VfxDraftOperations.LoadPrefab(draft) != null;
            compareGroup.EnableInClassList("vb-hidden", !comparable);
            SetSegmentState(compareGroup, previewDraft ? 1 : 0);
        }

        private void PingCurrent()
        {
            if (selected?.Current != null)
                PingAsset(selected.Current);
        }

        private static void PingAsset(Object asset)
        {
            if (asset == null) return;
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private static Object OwnerAsset(VfxSlot slot) =>
            slot == null || string.IsNullOrEmpty(slot.OwnerPath) ? null : AssetDatabase.LoadMainAssetAtPath(slot.OwnerPath);

        // ---------- 미리보기 ----------

        private void UpdatePreviewSource(bool keepCamera)
        {
            if (stage == null)
                return;

            VfxSlot slot = selected;
            GameObject draftPrefab = slot != null ? VfxDraftOperations.LoadPrefab(FindDraft(slot.Key)) : null;
            bool useDraft = draftPrefab != null && previewDraft;
            Object source = slot == null ? null : useDraft ? draftPrefab : slot.PreviewSource;
            string key = (slot?.Key ?? string.Empty) + "|" + (source != null ? source.GetHashCode() : 0);
            if (key != stageSourceKey)
            {
                stageSourceKey = key;
                stage.SetSource(source, slot?.PreviewRotation ?? Quaternion.identity, slot?.PreviewScale, keepCamera);
                stage.Playing = source != null;
            }

            if (slot == null)
                stageMessage.text = "왼쪽 목록에서 슬롯을 고르면 여기서 재생됩니다.";
            else if (source == null)
                stageMessage.text = slot.State == VfxSlotState.Missing
                    ? "참조가 깨진 슬롯입니다. 오른쪽 교체안에 프리팹을 넣으면 여기서 미리볼 수 있습니다."
                    : "비어 있는 슬롯입니다. 오른쪽 교체안에 프리팹을 넣으면 여기서 미리볼 수 있습니다.";
            else
                stageMessage.text = stage.Message;
            stageMessage.EnableInClassList("vb-hidden", string.IsNullOrEmpty(stageMessage.text));
            RenderNow();
        }

        private void RenderNow()
        {
            if (stage == null || viewport == null)
                return;

            stage.Render();
            viewport.SetTexture(stage.Surface);
            viewport.Refresh();
            SyncTransport();
        }

        private void TogglePlay()
        {
            if (stage == null || !stage.HasContent) return;
            if (!stage.Playing && stage.CurrentTime >= stage.Duration - .01f)
                stage.Restart(true);
            else
                stage.Playing = !stage.Playing;
            lastTick = EditorApplication.timeSinceStartup;
            SyncTransport();
        }

        private void SyncTransport()
        {
            if (stage == null || playButton == null)
                return;

            bool content = stage.HasContent;
            playButton.SetEnabled(content);
            timeSlider.SetEnabled(content);
            playButton.text = stage.Playing ? "Ⅱ 정지" : "▶ 재생";
            loopButton.EnableInClassList("is-on", loopEnabled);
            floorButton.EnableInClassList("is-on", floorVisible);
            bloomButton.EnableInClassList("is-on", bloomEnabled);
            SetSegmentState(speedGroup, Array.FindIndex(Speeds, s => Mathf.Approximately(s.value, speed)));
            timeSlider.highValue = Mathf.Max(.01f, stage.Duration);
            timeSlider.SetValueWithoutNotify(stage.CurrentTime);
            clockLabel.text = stage.CurrentTime.ToString("0.00") + " / " + stage.Duration.ToString("0.00") + "초";
            statsLabel.text = content
                ? "ParticleSystem " + stage.ParticleSystemCount
                    + (stage.GraphCount > 0 ? "  ·  VFX Graph " + stage.GraphCount : string.Empty)
                    + "  ·  보이는 입자 " + stage.VisibleParticles + "/" + stage.LiveParticles
                    + "  ·  " + (stage.Looping ? "반복 효과" : "원샷")
                : string.Empty;
            stageHint.text = stage.Environment.Label + "  ·  줌 " + stage.ZoomPercent.ToString("0") + "%  ·  드래그 회전  ·  휠 확대  ·  더블클릭 맞춤  ·  Space 재생";
        }

        private void Tick()
        {
            if (stage == null || viewport == null)
                return;

            double now = EditorApplication.timeSinceStartup;
            if (staleSince > 0d && now - staleSince > .75d)
                RefreshCatalog();

            double interval = hasFocus ? FocusedFrameInterval : BackgroundFrameInterval;
            if (now - lastTick < interval)
                return;

            float deltaTime = Mathf.Clamp((float)(now - lastTick), 0f, .1f);
            lastTick = now;
            if (stage.Tick(deltaTime))
                RenderNow();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Space)
                return;
            if (evt.target is VisualElement target && (target is TextField || target.GetFirstAncestorOfType<TextField>() != null
                || target.GetFirstAncestorOfType<ToolbarSearchField>() != null))
                return;

            TogglePlay();
            evt.StopPropagation();
        }

        // ---------- 초안 ----------

        private VfxDraft FindDraft(string key) => drafts.FirstOrDefault(d => d.slotKey == key);

        private VfxDraftCheck GetCheck(VfxSlot slot, VfxDraft draft)
        {
            string key = draft.slotKey;
            if (!checks.TryGetValue(key, out VfxDraftCheck check))
            {
                check = VfxDraftOperations.Check(slot, draft);
                checks[key] = check;
            }

            return check;
        }

        private void OnDraftFieldChanged(ChangeEvent<Object> evt)
        {
            if (selected == null)
                return;

            var prefab = evt.newValue as GameObject;
            if (prefab == null)
            {
                RemoveDraft(selected.Key);
                return;
            }

            if (!VfxDraftOperations.IsPrefabAssetRoot(prefab))
            {
                draftField.SetValueWithoutNotify(VfxDraftOperations.LoadPrefab(FindDraft(selected.Key)));
                ShowNotification(new GUIContent("프로젝트의 프리팹 에셋만 넣을 수 있습니다."));
                return;
            }

            previewDraft = true;
            SetDraft(selected, prefab, false);
        }

        private void SetDraft(VfxSlot slot, GameObject prefab, bool clear)
        {
            drafts.RemoveAll(d => d.slotKey == slot.Key);
            checks.Remove(slot.Key);
            string guid = prefab != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab)) : null;
            bool same = clear ? slot.State == VfxSlotState.Empty : guid != null && guid == VfxDraftOperations.CurrentGuid(slot);
            if (!same)
                drafts.Add(new VfxDraft { slotKey = slot.Key, prefabGuid = guid, clear = clear });
            else
                ShowNotification(new GUIContent("지금 연결된 것과 같아서 초안을 만들지 않았습니다."));
            DraftsChanged();
        }

        private void RemoveDraft(string key)
        {
            if (drafts.RemoveAll(d => d.slotKey == key) == 0)
                return;
            checks.Remove(key);
            DraftsChanged();
        }

        private void DraftsChanged()
        {
            SaveDraftPrefs();
            tree.RefreshItems();
            if (filter == "draft")
                RebuildTree();
            else
            {
                UpdateFilterButtons();
                UpdateSummary();
            }

            UpdateDraftBar();
            if (selected != null)
            {
                UpdateDraftCard();
                UpdatePreviewSource(true);
            }
        }

        private void DiscardAllDrafts()
        {
            if (drafts.Count == 0) return;
            if (!EditorUtility.DisplayDialog(WindowTitle, "변경 대기 " + drafts.Count + "건을 모두 취소할까요? 에셋은 바뀌지 않습니다.", "모두 취소", "그대로 두기"))
                return;
            drafts.Clear();
            checks.Clear();
            DraftsChanged();
        }

        private void CreateRevertDrafts()
        {
            if (lastApplied.Count == 0) return;
            int created = 0;
            foreach (VfxDraft reverse in lastApplied)
            {
                if (!slotsByKey.TryGetValue(reverse.slotKey, out VfxSlot slot)) continue;
                drafts.RemoveAll(d => d.slotKey == reverse.slotKey);
                checks.Remove(reverse.slotKey);
                drafts.Add(new VfxDraft { slotKey = reverse.slotKey, prefabGuid = reverse.prefabGuid, clear = reverse.clear });
                created++;
            }

            lastApplied.Clear();
            DraftsChanged();
            ShowNotification(new GUIContent("되돌리기 초안 " + created + "건을 만들었습니다. 확인 뒤 '선택 항목 저장'을 누르세요."));
        }

        private void UpdateDraftBar()
        {
            if (draftBar == null) return;
            bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
            int blocked = drafts.Count(d => !slotsByKey.TryGetValue(d.slotKey, out VfxSlot slot) || GetCheck(slot, d).Blocked);
            draftBar.EnableInClassList("has-drafts", drafts.Count > 0);
            draftCountLabel.text = drafts.Count == 0
                ? "변경 대기 없음"
                : "변경 대기 " + drafts.Count + "건" + (blocked > 0 ? " (적용 불가 " + blocked + "건)" : string.Empty);
            draftToggleButton.text = draftListOpen ? "목록 닫기" : "목록 보기";
            draftToggleButton.SetEnabled(drafts.Count > 0);
            revertButton.SetEnabled(lastApplied.Count > 0);
            discardButton.SetEnabled(drafts.Count > 0);
            VfxDraft currentDraft = selected != null ? FindDraft(selected.Key) : null;
            bool canSaveSelected = currentDraft != null && !GetCheck(selected, currentDraft).Blocked;
            applyButton.SetEnabled(canSaveSelected && !playing);
            applyButton.tooltip = playing ? "플레이 중에는 저장하지 않습니다."
                : currentDraft == null ? "현재 선택 항목에 변경 초안이 없습니다." : "현재 선택 항목 하나만 저장합니다.";

            draftList.Clear();
            draftList.EnableInClassList("vb-hidden", !draftListOpen || drafts.Count == 0);
            foreach (VfxDraft draft in drafts)
            {
                slotsByKey.TryGetValue(draft.slotKey, out VfxSlot slot);
                var row = new VisualElement();
                row.AddToClassList("vb-draft-row");
                var path = new Label(slot != null ? slot.Breadcrumb + " › " + slot.Label : "(찾을 수 없는 슬롯)");
                path.AddToClassList("vb-draft-row__path");
                var change = new Label((slot != null ? slot.CurrentName : "?") + "  →  " + VfxDraftOperations.DraftName(draft));
                change.AddToClassList("vb-draft-row__change");
                row.Add(path);
                row.Add(change);
                if (slot == null || GetCheck(slot, draft).Blocked)
                {
                    var badge = new Label("적용 불가");
                    badge.AddToClassList("vb-badge");
                    badge.AddToClassList("vb-badge--error");
                    row.Add(badge);
                }

                string key = draft.slotKey;
                var view = new Button(() => SelectSlot(key)) { text = "보기" };
                view.AddToClassList("vb-button");
                view.AddToClassList("vb-button--small");
                var remove = new Button(() => RemoveDraft(key)) { text = "취소" };
                remove.AddToClassList("vb-button");
                remove.AddToClassList("vb-button--small");
                row.Add(view);
                row.Add(remove);
                draftList.Add(row);
            }

            UpdateSummary();
        }

        private void SelectSlot(string key)
        {
            if (!slotsByKey.TryGetValue(key, out VfxSlot slot)) return;
            if (!treeIdsByKey.ContainsKey(key))
            {
                filter = "all";
                search = string.Empty;
                searchField.SetValueWithoutNotify(string.Empty);
                selected = slot;
                RebuildTree();
            }

            if (treeIdsByKey.TryGetValue(key, out int id))
            {
                tree.SetSelectionById(id);
                tree.ScrollToItemById(id);
            }

            ShowSlot(slot, false);
        }

        private void ApplySelected()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(WindowTitle, "플레이 중에는 적용하지 않습니다. 플레이를 끝낸 뒤 다시 누르세요.", "확인");
                return;
            }

            VfxSlot slot = selected;
            VfxDraft draft = slot != null ? FindDraft(slot.Key) : null;
            if (draft == null) return;
            VfxDraftCheck check = GetCheck(slot, draft);
            if (check.Blocked)
            {
                EditorUtility.DisplayDialog(WindowTitle, "현재 항목을 저장할 수 없습니다.\n\n" + string.Join("\n", check.Errors), "확인");
                return;
            }

            string text = VfxDraftOperations.Describe(slot) + "\n" + slot.CurrentName + " → " + VfxDraftOperations.DraftName(draft)
                + "\n\n현재 선택 항목 하나를 저장합니다. 다른 변경 초안은 대기 목록에 남습니다.";
            if (!EditorUtility.DisplayDialog("선택 항목 저장", text, "저장", "취소")) return;
            var ready = new List<(VfxSlot slot, VfxDraft draft)> { (slot, draft) };

            VfxApplyReport report;
            try
            {
                EditorUtility.DisplayProgressBar(WindowTitle, "변경을 적용하는 중…", .5f);
                report = VfxDraftOperations.Apply(ready);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            drafts.RemoveAll(d => report.AppliedKeys.Contains(d.slotKey));
            if (report.Reverse.Count > 0)
                lastApplied = report.Reverse.ToList();
            SaveDraftPrefs();
            stageSourceKey = null;
            RefreshCatalog();

            Debug.Log("[VFX 관리 보드] 적용 " + report.Applied.Count + "건\n" + string.Join("\n", report.Applied)
                + (report.Failed.Count > 0 ? "\n실패 " + report.Failed.Count + "건\n" + string.Join("\n", report.Failed) : string.Empty));
            if (report.Failed.Count > 0)
                EditorUtility.DisplayDialog(WindowTitle, "적용 " + report.Applied.Count + "건, 실패 " + report.Failed.Count + "건\n\n"
                    + string.Join("\n", report.Failed.Take(DialogLineLimit)) + "\n\n실패한 항목은 초안으로 남아 있습니다.", "확인");
            else
                ShowNotification(new GUIContent("변경 " + report.Applied.Count + "건을 적용했습니다."));
        }

        // ---------- 초안 보관 ----------

        private static string DraftPrefsKey()
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in Application.dataPath)
                    hash = (hash ^ c) * 16777619;
                return DraftPrefsPrefix + hash.ToString("x8");
            }
        }

        private void SaveDraftPrefs()
        {
            var list = new VfxDraftList { items = drafts };
            EditorPrefs.SetString(DraftPrefsKey(), JsonUtility.ToJson(list));
        }

        private void LoadDraftPrefs()
        {
            string json = EditorPrefs.GetString(DraftPrefsKey(), string.Empty);
            if (string.IsNullOrEmpty(json))
                return;
            try
            {
                VfxDraftList list = JsonUtility.FromJson<VfxDraftList>(json);
                if (list?.items != null && drafts.Count == 0)
                    drafts = list.items.Where(d => d != null && !string.IsNullOrEmpty(d.slotKey)).ToList();
            }
            catch (Exception)
            {
                // 깨진 보관본은 무시하고 빈 초안으로 시작한다.
            }
        }
    }
}
