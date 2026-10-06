using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Overburst.Persistence;

/// <summary>Native common-tree draft with explicit account commit and cached stat projection.</summary>
public sealed class OverburstSkillTreeUI : MonoBehaviour
{
    public const string PointHelp = "레벨업마다 1포인트 · 변경 후 적용하면 저장됩니다";
    public const string ResourcePath = "UI/SkillTree/PF_OverburstSkillTree_Rpg11";
    public static OverburstSkillTreeUI Instance { get; private set; }
    public static bool IsWindowOpen => Instance != null && Instance.IsOpen;
    public TextAsset catalogAsset;
    public Canvas rootCanvas;
    public GameObject surface;
    public RectTransform window, viewport;
    public OverburstSkillTreeRoutes routes;
    public OverburstSkillTreeNodeView[] nodes;
    public RectTransform[] areaLabels;
    public Button entry, close, zoomOut, zoomIn, center, action, cancel, apply, resetAllocation;
    public Text remaining, zoomLabel, elementLabel, kindLabel, nodeName, meta, trigger, prerequisites, actionLabel, feedback;
    public Text[] effectLabels, effectBodies, statValues;
    public Image equippedIcon, detailNodeIcon;
    public Image[] effectIcons, effectRowSurfaces;
    public Sprite[] elementSprites;
    public RectTransform effectContent;
    public RectTransform effectHeading, effectRule;
    public ScrollRect effectScroll;
    public RectTransform tooltip;
    public Text tipName, tipMeta, tipTrigger, tipEffect, tipPrerequisites, tipState;
    public bool IsOpen => surface != null && surface.activeSelf;
    public const float DefaultZoom = 1.2f;
    public float Zoom { get; private set; } = DefaultZoom;
    public Vector2 Pan { get; private set; }
    public string SelectedId { get; private set; } = "ROOT";
    public OverburstSkillTreeCatalog Catalog { get; private set; }
    public OverburstSkillTreePlan Plan { get; private set; }
    public float MapScale => .66f * Zoom;
    public float FitZoom => Mathf.Min(1f, (viewport.rect.width - 130) / (mapExtent.x * 1.32f), (viewport.rect.height - 130) / (mapExtent.y * 1.32f));
    public float MinimumZoom => Mathf.Min(.25f, FitZoom);
    Vector2 mapExtent;
    SkillTreeSnapshot expectedTree;
    long observedRevision = -1;
    AccountGameplaySession observedSession;
    bool staleDraft;
    public bool CanEdit => !Application.isPlaying || (AccountGameplaySession.Current != null && WorldSessionState.IsHideout && !AccountGameplaySession.Current.NeedsProjectionRecovery);
    public WeaponElement CurrentElement => PlayerContext.Instance?.CurrentActorEquipment != null ? PlayerContext.Instance.CurrentActorEquipment.ActiveElement : WeaponElement.None;
    Dictionary<string, OverburstSkillTreeCatalog.Node> index;
    Dictionary<string, OverburstSkillTreeNodeView> views;
    Dictionary<string, int> captionSizes;
    bool initialized, externalEntry;
    PlayerInputFacade lockedInput;
    GameObject returnSelection;
    WeaponElement shownElement = (WeaponElement)(-1);
    Vector2 lastViewport;
    string hoverId;
    float nextRefresh;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;
    public static OverburstSkillTreeUI Install(Transform canvasRoot)
    {
        if (Instance != null) return Instance;
        var prefab = Resources.Load<OverburstSkillTreeUI>(ResourcePath);
        if (!prefab) { Debug.LogWarning("[SkillTree] Missing authored UI prefab: " + ResourcePath); return null; }
        return Instantiate(prefab, canvasRoot, false);
    }
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this; Initialize(); surface.SetActive(false);
        rootCanvas.overrideSorting = true; rootCanvas.sortingOrder = 280;
    }
    void OnEnable() => UnityEngine.SceneManagement.SceneManager.sceneUnloaded += SceneUnloaded;
    void OnDisable() { UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= SceneUnloaded; Close(); }
    void OnDestroy() { Close(); if (Instance == this) Instance = null; }
    void SceneUnloaded(UnityEngine.SceneManagement.Scene scene) { Plan?.Cancel(); Close(); }
    public void Initialize()
    {
        if (initialized) return;
        Catalog = JsonUtility.FromJson<OverburstSkillTreeCatalog>(catalogAsset.text); Catalog.Validate(); Plan = new OverburstSkillTreePlan(Catalog);
        index = Catalog.nodes.ToDictionary(n => n.id); views = nodes.ToDictionary(n => n.nodeId);
        mapExtent = new Vector2(Mathf.Max(1, Catalog.nodes.Max(n => Mathf.Abs(n.x))), Mathf.Max(1, Catalog.nodes.Max(n => Mathf.Abs(n.y))));
        // Direction names belong to their guide nodes, so pan, zoom and selection keep them together.
        string[] guideIds = { "G_W", "G_H", "G_D", "G_Q" };
        Vector2[] guideOffsets = { new Vector2(80, 0), new Vector2(-32, 45), new Vector2(32, 45), new Vector2(80, 0) };
        for (int i = 0; i < guideIds.Length; i++)
        {
            var guide = views[guideIds[i]];
            guide.captionRect = areaLabels[i]; guide.caption = areaLabels[i].GetComponent<Text>(); guide.captionOffset = guideOffsets[i];
        }
        captionSizes = nodes.Where(n => n.caption != null).ToDictionary(n => n.nodeId, n => Mathf.RoundToInt(n.caption.fontSize * .8f));
        var chrome = window.Find("Shared Window Chrome");
        foreach (var text in window.GetComponentsInChildren<Text>(true))
            if (!text.transform.IsChildOf(viewport) && (chrome == null || !text.transform.IsChildOf(chrome)))
                text.fontSize = Mathf.Max(10, Mathf.RoundToInt(text.fontSize * .88f));
        foreach (var node in nodes) if (node.stateMark) node.stateMark.fontSize = 12;
        var legend = window.Find("Node Legend")?.GetComponent<Text>();
        if (legend)
        {
            legend.supportRichText = true;
            legend.text = "<color=#D6B478>● 습득 완료</color>    <color=#BCAF99>○ 습득 가능</color>    <color=#948A77>× 미습득</color>    <color=#B48D55>! 포인트 부족</color>    <color=#EDE3D1>+ 습득 예정</color>    <color=#CB9A87>− 환급 예정</color>    <color=#97856F>… 확장 예정</color>";
        }
        feedback.text = PointHelp;
        foreach (var node in nodes) node.Bind(this);
        entry.onClick.AddListener(Toggle); close.onClick.AddListener(Close);
        zoomOut.onClick.AddListener(() => ZoomAt(Zoom - .2f, Vector2.zero)); zoomIn.onClick.AddListener(() => ZoomAt(Zoom + .2f, Vector2.zero));
        center.onClick.AddListener(ResetMap);
        action.onClick.AddListener(EditPlan); cancel.onClick.AddListener(CancelPlan); apply.onClick.AddListener(ApplyPlan);
        if (resetAllocation) resetAllocation.onClick.AddListener(ResetAllocation);
        initialized = true; HideTooltip(); Refresh();
    }
    void Update()
    {
        if (!initialized) return;
        var flow = PersistentSceneFlow.Instance;
        entry.interactable = IsOpen || (!OverburstGameMenu.IsOpen && !GameplayInputBlocker.IsGameplayInputBlocked && (flow == null || !flow.IsSwitching));
        if (!IsOpen) return;
        SyncGameplayMapOwner();
        if (PlayerInputFacade.Current != null && PlayerInputFacade.Current.UiCancelPressedThisFrame) { Close(); return; }
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.numpadPlusKey.wasPressedThisFrame || keyboard.equalsKey.wasPressedThisFrame) ZoomAt(Zoom + .2f, Vector2.zero);
            if (keyboard.numpadMinusKey.wasPressedThisFrame || keyboard.minusKey.wasPressedThisFrame) ZoomAt(Zoom - .2f, Vector2.zero);
        }
        FitWindow();
        if (viewport.rect.size != lastViewport) { HideTooltip(); LayoutMap(); }
        if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + .15f; if (AccountGameplaySession.Current != null && observedRevision != AccountGameplaySession.Current.Revision) SyncAccount(false); if (shownElement != CurrentElement) { RefreshDetail(); if (!string.IsNullOrEmpty(hoverId)) ShowTooltip(hoverId); } }
    }
    public void UseExternalEntry(bool value) { externalEntry = value; if (entry) entry.gameObject.SetActive(!externalEntry && !IsOpen); }
    public void Toggle() { if (IsOpen) Close(); else Open(); }
    public void Open()
    {
        Initialize();
        if (IsOpen) return;
        if (Application.isPlaying && (OverburstGameMenu.IsOpen || GameplayInputBlocker.IsGameplayInputBlocked)) return;
        returnSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        entry.gameObject.SetActive(false);
        surface.SetActive(true); transform.SetAsLastSibling(); FitWindow(); Canvas.ForceUpdateCanvases(); SyncAccount(observedSession != AccountGameplaySession.Current || !CanEdit); ResetDefaultMap(); Refresh();
        if (Application.isPlaying)
        {
            TooltipManager.Instance?.HideTooltip(); GameplayInputBlocker.Block(this);
            SyncGameplayMapOwner();
        }
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(views[SelectedId].button.gameObject);
    }
    void SyncGameplayMapOwner()
    {
        var current = PlayerInputFacade.Current;
        if (current == null) return;
        lockedInput = current;
        if (lockedInput != null) lockedInput.SetGameplayMapDisabled(this, true);
    }
    public void Close()
    {
        var events = EventSystem.current;
        bool ownsSelection = surface && events != null && events.currentSelectedGameObject != null && events.currentSelectedGameObject.transform.IsChildOf(surface.transform);
        HideTooltip();
        if (surface) surface.SetActive(false);
        if (entry) entry.gameObject.SetActive(!externalEntry);
        GameplayInputBlocker.Unblock(this);
        PlayerInputFacade.ReleaseGameplayMapDisabled(this);
        lockedInput = null;
        if (ownsSelection && events != null)
            events.SetSelectedGameObject(returnSelection && returnSelection.activeInHierarchy ? returnSelection : entry && entry.gameObject.activeInHierarchy ? entry.gameObject : null);
        returnSelection = null;
    }
    public void FitWindow()
    {
        var root = (RectTransform)transform;
        float fit = Mathf.Min(1, (root.rect.width - 48) / window.sizeDelta.x, (root.rect.height - 48) / window.sizeDelta.y);
        window.localScale = Vector3.one * Mathf.Max(.1f, fit);
    }
    public void SelectNode(string id, bool bringIntoView)
    {
        if (!index.ContainsKey(id)) return;
        SelectedId = id;
        if (bringIntoView)
        {
            var view = views[id]; var rect = (RectTransform)view.transform;
            var p = index[id].Position * MapScale + Pan; var half = viewport.rect.size / 2;
            float left = Mathf.Max(rect.sizeDelta.x * Zoom / 2, view.captionRect ? view.captionRect.rect.width / 2 - view.captionOffset.x * Zoom : 0) + 18;
            float right = Mathf.Max(rect.sizeDelta.x * Zoom / 2, view.captionRect ? view.captionRect.rect.width / 2 + view.captionOffset.x * Zoom : 0) + 18;
            float top = Mathf.Max(rect.sizeDelta.y * Zoom / 2, view.captionRect ? view.captionOffset.y * Zoom + view.captionRect.rect.height / 2 : 0) + 18;
            float bottom = Mathf.Max(rect.sizeDelta.y * Zoom / 2, view.captionRect ? -view.captionOffset.y * Zoom + view.captionRect.rect.height / 2 : 0) + 18;
            Pan += new Vector2(Mathf.Clamp(p.x, -half.x + left, half.x - right) - p.x, Mathf.Clamp(p.y, -half.y + bottom, half.y - top) - p.y);
        }
        Refresh(); effectScroll.StopMovement(); effectScroll.verticalNormalizedPosition = 1;
    }
    public void MoveSelection(string from, Vector2 axis)
    {
        var source = index[from].Position;
        var nearest = Catalog.nodes.Where(n => n.id != from && Vector2.Dot(n.Position - source, axis) > 0).OrderBy(n => (n.Position - source).magnitude + Mathf.Abs(Vector2.Dot(n.Position - source, new Vector2(-axis.y, axis.x))) * 2).FirstOrDefault();
        if (nearest != null && EventSystem.current != null) { SelectNode(nearest.id, true); EventSystem.current.SetSelectedGameObject(views[nearest.id].button.gameObject); }
    }
    public void ZoomAt(float next, Vector2 anchor)
    {
        float value = Mathf.Clamp(next, MinimumZoom, 2.5f);
        if (Mathf.Approximately(value, Zoom)) return;
        HideTooltip(); Pan = anchor - (anchor - Pan) * value / Zoom; Zoom = value; LayoutMap();
    }
    public void SetPan(Vector2 pan) { HideTooltip(); Pan = pan; LayoutMap(); }
    public void ResetDefaultMap() { HideTooltip(); Zoom = Mathf.Clamp(DefaultZoom, MinimumZoom, 2.5f); Pan = Vector2.zero; LayoutMap(); }
    public void ResetMap() { HideTooltip(); Zoom = FitZoom; Pan = Vector2.zero; LayoutMap(); }
    public void LayoutMap()
    {
        if (!initialized) return;
        foreach (var node in Catalog.nodes)
        {
            var view = views[node.id]; var pos = node.Position * MapScale + Pan;
            ((RectTransform)view.transform).anchoredPosition = pos;
            view.transform.localScale = Vector3.one * Zoom;
            bool visible = Mathf.Abs(pos.x) < viewport.rect.width / 2 + 110 * Zoom && Mathf.Abs(pos.y) < viewport.rect.height / 2 + 70 * Zoom;
            view.gameObject.SetActive(visible); if (view.captionRect) view.captionRect.gameObject.SetActive(visible && (Zoom >= Mathf.Min(.65f, FitZoom) || node.id == SelectedId));
            if (view.captionRect)
            {
                // Text grows more slowly than the map, preserving room around enlarged nodes.
                view.caption.fontSize = Mathf.Max(10, Mathf.RoundToInt(captionSizes[node.id] * Mathf.Pow(Zoom, .75f)));
                view.captionRect.sizeDelta = new Vector2(Mathf.Ceil(view.caption.preferredWidth) + 6 * Zoom, Mathf.Ceil(view.caption.preferredHeight) + 4);
                view.captionRect.anchoredPosition = pos + view.captionOffset * Zoom;
            }
        }
        routes.Configure(Catalog, Plan, MapScale, Pan, rootCanvas.rootCanvas.scaleFactor * window.localScale.x); lastViewport = viewport.rect.size;
        zoomLabel.text = Mathf.RoundToInt(Zoom * 100) + "%"; zoomOut.interactable = Zoom > MinimumZoom + .001f; zoomIn.interactable = Zoom < 2.499f;
        ButtonOpacity(zoomOut);ButtonOpacity(zoomIn);
    }
    public void SyncAccount(bool discardDraft)
    {
        var session = AccountGameplaySession.Current;
        if (session == null) return;
        var tree = session.ReadSkillTree(); observedRevision = session.Revision; observedSession = session;
        if (tree == null) { feedback.text = "계정 준비 중"; return; }
        bool changed = expectedTree == null || tree.earnedPoints != expectedTree.earnedPoints || !tree.learnedNodeIds.SequenceEqual(expectedTree.learnedNodeIds);
        if (!discardDraft && Plan.Changed && changed) { staleDraft = true; feedback.text = "포인트가 변경되었습니다 · 변경 취소 또는 다시 열기로 갱신"; apply.interactable = false; return; }
        if (discardDraft || changed) { staleDraft = false; expectedTree = tree; Plan.Load(tree.earnedPoints, tree.learnedNodeIds); feedback.text = CanEdit ? PointHelp : "은신처에서 강화 변경을 적용할 수 있습니다"; Refresh(); }
    }
    void EditPlan()
    {
        HideTooltip(); if (!CanEdit || !Plan.Toggle(SelectedId)) return;
        feedback.text = "변경 미리보기 · 적용하면 능력치에 반영됩니다"; Refresh();
    }
    public void CancelPlan() { HideTooltip(); Plan.Cancel(); SyncAccount(true); feedback.text = "변경을 취소했습니다"; Refresh(); }
    public void ResetAllocation()
    {
        if (!CanEdit) return; HideTooltip(); Plan.ClearDraft(); feedback.text = "전체 환급 미리보기 · 적용하면 " + Plan.Remaining + "포인트를 사용할 수 있습니다"; Refresh();
    }
    public void ApplyPlan()
    {
        HideTooltip(); SyncAccount(false); if (!CanEdit || staleDraft || !Plan.Changed) return;
        if (!Application.isPlaying) { Plan.Apply(); feedback.text = "Editor 미리보기"; Refresh(); return; }
        try
        {
            if (!AccountGameplaySession.Current.ApplySkillTree(expectedTree, Plan.Planned)) return;
            SyncAccount(true); feedback.text = "강화 적용 및 저장 완료";
            if (AccountGameplaySession.Current.NeedsProjectionRecovery) feedback.text = "강화 저장 완료 · 화면 복구 중";
        }
        catch (System.IO.IOException) { feedback.text = "저장에 실패했습니다 · 변경을 유지했으니 다시 적용해 주세요"; }
        catch (InvalidOperationException error) { feedback.text = error.Message; }
        Refresh();
    }
    public void Refresh()
    {
        if (!initialized) return;
        remaining.text = Plan.Remaining + " / " + Plan.Budget;
        foreach (var node in Catalog.nodes) views[node.id].Present(StateFor(node.id), node.id == SelectedId);
        cancel.interactable = Plan.Changed; apply.interactable = Plan.Changed && CanEdit && !staleDraft;
        if (resetAllocation) { resetAllocation.interactable = CanEdit && Plan.Planned.Length > 0; ButtonOpacity(resetAllocation); }
        ButtonOpacity(cancel);ButtonOpacity(apply);
        for (int i = 0; i < statValues.Length; i++)
        {
            string stat = new[] { "attack", "defense", "hp", "move" }[i], unit = i == 1 ? "" : "%";
            float current = Plan.AppliedTotal(stat), preview = Plan.Total(stat); bool changed = !Mathf.Approximately(current, preview);
            statValues[i].text = changed ? current.ToString("0.#") + " → " + preview.ToString("0.#") + unit : "+" + preview.ToString("0.#") + unit;
            statValues[i].color = changed ? new Color(.96f,.80f,.49f) : new Color(.91f,.88f,.81f);
        }
        RefreshDetail(); LayoutMap();
    }
    public OverburstSkillTreeNodeView.NodeState StateFor(string id)
    {
        var n = index[id];
        if (n.IsReserved) return OverburstSkillTreeNodeView.NodeState.Reserved;
        if (n.cost == 0) return OverburstSkillTreeNodeView.NodeState.Guide;
        if (Plan.Has(id)) return Plan.IsApplied(id) ? OverburstSkillTreeNodeView.NodeState.Learned : OverburstSkillTreeNodeView.NodeState.PurchaseDraft;
        if (Plan.IsApplied(id)) return OverburstSkillTreeNodeView.NodeState.RefundDraft;
        return Plan.Available(n) ? Plan.Remaining >= n.cost ? OverburstSkillTreeNodeView.NodeState.Available : OverburstSkillTreeNodeView.NodeState.Insufficient : OverburstSkillTreeNodeView.NodeState.Locked;
    }
    public void RefreshDetail()
    {
        var n = index[SelectedId]; shownElement = CurrentElement;
        elementLabel.text = "장착 원소 · " + OverburstSkillTreeCatalog.ElementName(CurrentElement);
        int equippedIndex=OverburstSkillTreeCatalog.ElementIndex(CurrentElement);
        equippedIcon.sprite=equippedIndex<5?elementSprites[equippedIndex]:views["S_W1"].icon.sprite;equippedIcon.color=equippedIndex<5?Color.white:new Color(.65f,.61f,.53f);
        detailNodeIcon.sprite=views[n.id].icon.sprite;
        var state = StateFor(n.id); var statusColor = OverburstSkillTreeNodeView.StatusColor(state);
        detailNodeIcon.color = views[n.id].icon.color; nodeName.color = state == OverburstSkillTreeNodeView.NodeState.Learned ? statusColor : OverburstSkillTreePalette.Ivory; meta.color = statusColor;
        kindLabel.text = OverburstSkillTreeCatalog.AreaName(n.area) + " · " + OverburstSkillTreeCatalog.KindName(n.kind);
        nodeName.text = n.name; meta.text = n.cost > 0 ? OverburstSkillTreeNodeView.StatusName(state) + " · " + Meta(n) : Meta(n); trigger.text = n.kind == "stat" ? "모든 공격과 원소에 같은 능력치 보정" : string.IsNullOrEmpty(n.trigger) ? "중앙에서 연결된 능력치 노드를 선택해 강화합니다." : n.trigger;
        prerequisites.text = n.requires.Length > 0 ? "필수 선행  " + string.Join(" + ", n.requires.Select(id => index[id].name)) : n.cost == 0 ? "무료 안내 노드 · 포인트를 소비하지 않습니다" : "중앙과 연결된 습득 노드에 인접해야 합니다";
        effectHeading.GetComponent<Text>().text = n.IsReserved ? "예정 효과 · 원소별 방향" : "강화 효과";
        prerequisites.rectTransform.sizeDelta = new Vector2(396, 42);
        float effectsY=Mathf.Clamp(181+trigger.preferredHeight+24,220,260);effectHeading.anchoredPosition=new Vector2(22,-effectsY);effectRule.anchoredPosition=new Vector2(22,-effectsY-35);var effectViewport=effectScroll.viewport;effectViewport.anchoredPosition=new Vector2(22,-effectsY-48);effectViewport.sizeDelta=new Vector2(effectViewport.sizeDelta.x,610-effectsY-48);
        string[] elements = { "불", "얼음", "번개", "어둠", "빛", "미장착" }; float y = 0;
        for (int i = 0; i < effectBodies.Length; i++)
        {
            bool show = n.IsReserved || i == 0;
            effectBodies[i].transform.parent.gameObject.SetActive(show); if (!show) continue;
            effectLabels[i].text = n.IsReserved ? elements[i] : "공용";
            effectLabels[i].color = i == OverburstSkillTreeCatalog.ElementIndex(CurrentElement) || n.kind == "stat" ? new Color(.89f,.74f,.47f) : new Color(.67f,.63f,.55f);
            effectIcons[i].sprite=n.IsReserved && i < 5 ? elementSprites[i] : views[n.id].icon.sprite;effectIcons[i].color=n.kind=="stat"||i==5?new Color(.80f,.73f,.60f):Color.white;
            effectRowSurfaces[i].color=i==equippedIndex&&n.kind!="stat"?new Color(.22f,.17f,.10f,.5f):new Color(0,0,0,0);
            effectBodies[i].text = n.IsReserved ? n.Effect(i < 5 ? new[]{WeaponElement.Fire,WeaponElement.Ice,WeaponElement.Electric,WeaponElement.Dark,WeaponElement.Light}[i] : WeaponElement.None) : n.kind == "stat" ? n.name + " · 원소 교체 시에도 유지\n" + (n.stat == "defense" ? "방어력에 고정 수치를 더합니다." : n.stat == "hp" ? "기본 체력과 영구 보너스의 합에 적용합니다. 적용 시 체력을 회복하지 않습니다." : n.stat == "move" ? "걷기·달리기·전투 이동에 적용합니다." : "무기·장비·레벨 공격력을 합친 뒤 적용합니다.") : "무료 경로 안내\n연결된 능력치 노드부터 습득할 수 있습니다.";
            float h = Mathf.Max(48, effectBodies[i].preferredHeight + 16);
            var row = (RectTransform)effectBodies[i].transform.parent; row.anchoredPosition = new Vector2(0, -y); row.sizeDelta = new Vector2(row.sizeDelta.x, h); y += h;
        }
        effectContent.sizeDelta = new Vector2(effectContent.sizeDelta.x, y);
        if (effectScroll.verticalScrollbar) effectScroll.verticalScrollbar.gameObject.SetActive(y > effectScroll.viewport.rect.height);
        if (n.IsReserved) prerequisites.text = "확장 예정 · 현재 습득/포인트 소비 없음" + (n.requires.Length > 0 ? "\n최종 경로  " + string.Join(" + ", n.requires.Select(id => index[id].name)) : "");
        bool included = Plan.Has(n.id); var refundIds = n.cost > 0 && included ? Plan.Refunds(n.id) : Array.Empty<string>(); int count = refundIds.Length;
        if (count > 1) prerequisites.text = "환급 대상  " + string.Join(" · ", refundIds.Select(id => index[id].name)) + "\n총 " + refundIds.Sum(id => index[id].cost) + "포인트 · 적용 전 취소 가능";
        actionLabel.text = n.IsReserved ? "확장 예정 · 습득 불가" : n.cost == 0 ? "기본 경로" : included ? "환급 미리보기" + (count > 1 ? " · " + count + "개" : " · " + n.cost + "포인트") : !Plan.Available(n) ? "연결 노드 습득 필요" : Plan.Remaining < n.cost ? "포인트 부족" : "습득 미리보기 · " + n.cost + "포인트";
        action.interactable = CanEdit && n.cost > 0 && (included || (Plan.Available(n) && Plan.Remaining >= n.cost));
        ButtonOpacity(action);
    }
    static void ButtonOpacity(Button button){var group=button.GetComponent<CanvasGroup>();if(group)group.alpha=button.interactable?1:.45f;}
    static string Meta(OverburstSkillTreeCatalog.Node n) => n.IsReserved ? "확장 예정 · " + OverburstSkillTreeCatalog.KindName(n.kind) : n.cost == 0 ? "무료 · 공통 경로" : n.cost + "포인트 · " + (n.kind == "stat" ? "항상 적용" : n.cooldown > 0 ? "쿨타임 " + n.cooldown.ToString("0.#") + "초" : "조건마다 발동");
    public void HideTooltip() { if (tooltip) tooltip.gameObject.SetActive(false); hoverId = null; }
    public void ShowTooltip(string id)
    {
        if (!IsOpen || !index.TryGetValue(id, out var n)) return;
        hoverId = id; tipName.text = n.name; tipMeta.text = Meta(n);
        tipTrigger.text = n.kind == "stat" ? "모든 원소에 같은 능력치 보정" : string.IsNullOrEmpty(n.trigger) ? "공통 공격의 시작 경로" : n.trigger;
        tipEffect.text = n.IsReserved ? n.description : n.kind == "stat" ? n.name + " · 원소 교체 시에도 유지" : "무료 안내 노드 · 포인트를 소비하지 않습니다";
        tipPrerequisites.text = n.requires.Length == 0 ? (n.IsReserved ? "연결된 강화 경로의 확장 목표" : n.cost == 0 ? "공통 시작 경로" : "중앙과 연결된 습득 노드에 인접해야 합니다") : "선행  " + string.Join(" + ", n.requires.Select(p => index[p].name));
        tipState.text = n.IsReserved ? "확장 예정 · 현재 습득 불가" : n.cost == 0 ? "기본 경로" : Plan.Has(id) ? (Plan.IsApplied(id) ? "습득 완료" : "습득 예정 · 적용 필요") + " · 클릭하여 상세 고정" : Plan.IsApplied(id) ? "환급 예정 · 적용 필요" : Plan.Available(n) ? (Plan.Remaining >= n.cost ? "습득 가능 · " : "포인트 부족 · ") + "클릭하여 상세 고정" : "연결 노드 습득 필요 · 클릭하여 상세 고정";
        tipState.color = OverburstSkillTreeNodeView.StatusColor(StateFor(id));
        tooltip.gameObject.SetActive(true); tooltip.SetAsLastSibling();
        float y = 16; Text[] fields = { tipName, tipMeta, tipTrigger, tipEffect, tipPrerequisites, tipState };
        foreach (var field in fields)
        {
            float height = Mathf.Max(field.fontSize + 4, field.preferredHeight + 4);
            field.rectTransform.anchoredPosition = new Vector2(18, -y); field.rectTransform.sizeDelta = new Vector2(324, height); y += height + (field == tipMeta ? 12 : 7);
        }
        tooltip.sizeDelta = new Vector2(360, y + 10);
        var corners = new Vector3[4]; ((RectTransform)views[id].transform).GetWorldCorners(corners);
        var a = (Vector2)window.InverseTransformPoint(corners[2]); var left = (Vector2)window.InverseTransformPoint(corners[1]);
        Vector2 half = window.rect.size / 2;
        float x = a.x + 14; if (x + tooltip.rect.width > half.x - 12) x = left.x - tooltip.rect.width - 14;
        x = Mathf.Clamp(x, -half.x + 12, half.x - tooltip.rect.width - 12);
        float top = Mathf.Clamp(a.y + 12, -half.y + tooltip.rect.height + 12, half.y - 12);
        tooltip.anchoredPosition = new Vector2(x, top);
    }
}
