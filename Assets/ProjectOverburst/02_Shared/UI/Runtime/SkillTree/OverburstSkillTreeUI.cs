using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Native common-tree window and ephemeral build planner. Combat/account ownership stays outside this UI.</summary>
public sealed class OverburstSkillTreeUI : MonoBehaviour
{
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
    public Button entry, close, zoomOut, zoomIn, center, action, cancel, apply;
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
    public float Zoom { get; private set; } = 1;
    public Vector2 Pan { get; private set; }
    public string SelectedId { get; private set; } = "W01";
    public OverburstSkillTreeCatalog Catalog { get; private set; }
    public OverburstSkillTreePlan Plan { get; private set; }
    public float MapScale => Mathf.Max(.44f, Mathf.Min((viewport.rect.width - 100) / 1600, (viewport.rect.height - 100) / 1600)) * Zoom;
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
    void SceneUnloaded(UnityEngine.SceneManagement.Scene scene) => Close();
    public void Initialize()
    {
        if (initialized) return;
        Catalog = JsonUtility.FromJson<OverburstSkillTreeCatalog>(catalogAsset.text); Catalog.Validate(); Plan = new OverburstSkillTreePlan(Catalog);
        index = Catalog.nodes.ToDictionary(n => n.id); views = nodes.ToDictionary(n => n.nodeId);
        captionSizes = nodes.Where(n => n.caption != null).ToDictionary(n => n.nodeId, n => n.caption.fontSize);
        foreach (var node in nodes) node.Bind(this);
        entry.onClick.AddListener(Toggle); close.onClick.AddListener(Close);
        zoomOut.onClick.AddListener(() => ZoomAt(Zoom - .2f, Vector2.zero)); zoomIn.onClick.AddListener(() => ZoomAt(Zoom + .2f, Vector2.zero));
        center.onClick.AddListener(ResetMap);
        action.onClick.AddListener(EditPlan); cancel.onClick.AddListener(CancelPlan); apply.onClick.AddListener(ApplyPlan);
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
        if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + .15f; if (shownElement != CurrentElement) { RefreshDetail(); if (!string.IsNullOrEmpty(hoverId)) ShowTooltip(hoverId); } }
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
        surface.SetActive(true); transform.SetAsLastSibling(); FitWindow(); Canvas.ForceUpdateCanvases(); Refresh();
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
            float side = Mathf.Max(rect.sizeDelta.x * Zoom / 2, view.captionRect ? view.captionRect.rect.width / 2 : 0) + 18;
            float top = rect.sizeDelta.y * Zoom / 2 + 18;
            float bottom = Mathf.Max(rect.sizeDelta.y * Zoom / 2, view.captionRect ? -view.captionOffset.y * Zoom + view.captionRect.rect.height / 2 : 0) + 18;
            Pan += new Vector2(Mathf.Clamp(p.x, -half.x + side, half.x - side) - p.x, Mathf.Clamp(p.y, -half.y + bottom, half.y - top) - p.y);
        }
        Refresh(); effectScroll.StopMovement(); effectScroll.verticalNormalizedPosition = 1;
    }
    public void MoveSelection(string from, Vector2 axis)
    {
        var source = index[from].Position;
        var nearest = Catalog.nodes.Where(n => n.id != from && Vector2.Dot(n.Position - source, axis) > 0).OrderBy(n => (n.Position - source).magnitude + Mathf.Abs(Vector2.Dot(n.Position - source, new Vector2(-axis.y, axis.x))) * 2).FirstOrDefault();
        if (nearest != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(views[nearest.id].button.gameObject);
    }
    public void ZoomAt(float next, Vector2 anchor)
    {
        float value = Mathf.Clamp(next, 1, 2.5f);
        if (Mathf.Approximately(value, Zoom)) return;
        HideTooltip(); Pan = anchor - (anchor - Pan) * value / Zoom; Zoom = value; LayoutMap();
    }
    public void SetPan(Vector2 pan) { HideTooltip(); Pan = pan; LayoutMap(); }
    public void ResetMap() { HideTooltip(); Zoom = 1; Pan = Vector2.zero; LayoutMap(); }
    public void LayoutMap()
    {
        if (!initialized) return;
        foreach (var node in Catalog.nodes)
        {
            var view = views[node.id]; var pos = node.Position * MapScale + Pan;
            ((RectTransform)view.transform).anchoredPosition = pos;
            view.transform.localScale = Vector3.one * Zoom;
            if (view.captionRect)
            {
                view.caption.fontSize = Mathf.RoundToInt(captionSizes[node.id] * Zoom);
                view.captionRect.sizeDelta = new Vector2(Mathf.Ceil(view.caption.preferredWidth) + 6 * Zoom, (captionSizes[node.id] * 1.25f + 2) * Zoom);
                view.captionRect.anchoredPosition = pos + view.captionOffset * Zoom;
            }
        }
        Vector2[] regions = { new Vector2(0, 220), new Vector2(285, 0), new Vector2(-285, 0), new Vector2(0, -220) };
        for (int i = 0; i < areaLabels.Length; i++)
        {
            var label = areaLabels[i].GetComponent<Text>(); label.fontSize = Mathf.RoundToInt(15 * Zoom);
            areaLabels[i].sizeDelta = new Vector2(Mathf.Ceil(label.preferredWidth) + 10 * Zoom, 24 * Zoom);
            areaLabels[i].anchoredPosition = regions[i] * MapScale + Pan;
        }
        routes.Configure(Catalog, Plan, MapScale, Pan); lastViewport = viewport.rect.size;
        zoomLabel.text = Mathf.RoundToInt(Zoom * 100) + "%"; zoomOut.interactable = Zoom > 1.001f; zoomIn.interactable = Zoom < 2.499f;
        ButtonOpacity(zoomOut);ButtonOpacity(zoomIn);
    }
    void EditPlan()
    {
        HideTooltip(); if (!Plan.Toggle(SelectedId)) return;
        feedback.text = "강화 계획 변경 · 전투 효과 준비 중"; Refresh();
    }
    public void CancelPlan() { HideTooltip(); Plan.Cancel(); feedback.text = "직전 강화 계획으로 돌아갔습니다"; Refresh(); }
    public void ApplyPlan() { HideTooltip(); Plan.Apply(); feedback.text = "강화 계획 적용 · 전투 효과 준비 중"; Refresh(); }
    public void Refresh()
    {
        if (!initialized) return;
        remaining.text = Plan.Remaining + " / " + Catalog.planningBudget;
        foreach (var node in Catalog.nodes) views[node.id].Present(Plan.Has(node.id), node.id == SelectedId, Plan.Available(node));
        cancel.interactable = apply.interactable = Plan.Changed;
        ButtonOpacity(cancel);ButtonOpacity(apply);
        for (int i = 0; i < statValues.Length; i++) statValues[i].text = "+" + Plan.Total(new[] { "attack", "defense", "hp", "move" }[i]).ToString("0.#") + "%";
        RefreshDetail(); LayoutMap();
    }
    public void RefreshDetail()
    {
        var n = index[SelectedId]; shownElement = CurrentElement;
        elementLabel.text = "장착 원소 · " + OverburstSkillTreeCatalog.ElementName(CurrentElement);
        int equippedIndex=OverburstSkillTreeCatalog.ElementIndex(CurrentElement);
        equippedIcon.sprite=equippedIndex<5?elementSprites[equippedIndex]:views["S_W1"].icon.sprite;equippedIcon.color=equippedIndex<5?Color.white:new Color(.65f,.61f,.53f);
        detailNodeIcon.sprite=views[n.id].icon.sprite;
        kindLabel.text = OverburstSkillTreeCatalog.AreaName(n.area) + " · " + OverburstSkillTreeCatalog.KindName(n.kind);
        nodeName.text = n.name; meta.text = Meta(n); trigger.text = n.kind == "stat" ? "모든 공격과 원소에 같은 능력치 보정" : string.IsNullOrEmpty(n.trigger) ? "기본 공격은 강화 계획 없이 사용할 수 있습니다." : n.trigger;
        prerequisites.text = n.requires.Length > 0 ? "선행  " + string.Join(" + ", n.requires.Select(id => index[id].name)) : "모든 공격의 공통 시작점";
        float effectsY=Mathf.Clamp(181+trigger.preferredHeight+24,220,260);effectHeading.anchoredPosition=new Vector2(22,-effectsY);effectRule.anchoredPosition=new Vector2(22,-effectsY-35);var effectViewport=effectScroll.viewport;effectViewport.anchoredPosition=new Vector2(22,-effectsY-48);effectViewport.sizeDelta=new Vector2(effectViewport.sizeDelta.x,610-effectsY-48);
        string[] elements = { "불", "얼음", "번개", "어둠", "빛", "미장착" }; float y = 0;
        for (int i = 0; i < effectBodies.Length; i++)
        {
            bool show = n.kind != "stat" || i == 0;
            effectBodies[i].transform.parent.gameObject.SetActive(show); if (!show) continue;
            effectLabels[i].text = n.kind == "stat" ? "공용" : elements[i];
            effectLabels[i].color = i == OverburstSkillTreeCatalog.ElementIndex(CurrentElement) || n.kind == "stat" ? new Color(.89f,.74f,.47f) : new Color(.67f,.63f,.55f);
            effectIcons[i].sprite=n.kind=="stat"?views[n.id].icon.sprite:i<5?elementSprites[i]:views["S_W1"].icon.sprite;effectIcons[i].color=n.kind=="stat"||i==5?new Color(.80f,.73f,.60f):Color.white;
            effectRowSurfaces[i].color=i==equippedIndex&&n.kind!="stat"?new Color(.22f,.17f,.10f,.5f):new Color(0,0,0,0);
            effectBodies[i].text = n.kind == "stat" ? n.name + " · 보석 교체 시에도 유지" : n.effects != null && n.effects.Length == 6 ? n.effects[i] : "모든 원소가 같은 기본 공격과 강화 경로를 사용합니다.";
            float h = Mathf.Max(48, effectBodies[i].preferredHeight + 16);
            var row = (RectTransform)effectBodies[i].transform.parent; row.anchoredPosition = new Vector2(0, -y); row.sizeDelta = new Vector2(row.sizeDelta.x, h); y += h;
        }
        effectContent.sizeDelta = new Vector2(effectContent.sizeDelta.x, y);
        bool included = Plan.Has(n.id); int count = n.cost > 0 && included ? Plan.Refunds(n.id).Length : 0;
        actionLabel.text = n.cost == 0 ? "기본 경로" : included ? "계획에서 제거" + (count > 1 ? " · 연결 " + count + "개" : "") : !Plan.Available(n) ? "선행 계획 필요" : "계획에 추가 · " + n.cost + "포인트";
        action.interactable = n.cost > 0 && (included || (Plan.Available(n) && Plan.Remaining >= n.cost));
        ButtonOpacity(action);
    }
    static void ButtonOpacity(Button button){var group=button.GetComponent<CanvasGroup>();if(group)group.alpha=button.interactable?1:.45f;}
    static string Meta(OverburstSkillTreeCatalog.Node n) => n.cost == 0 ? "무료 · 공통 경로" : n.cost + "포인트 · " + (n.kind == "stat" ? "항상 적용" : n.cooldown > 0 ? "쿨타임 " + n.cooldown.ToString("0.#") + "초" : "조건마다 발동");
    public void HideTooltip() { if (tooltip) tooltip.gameObject.SetActive(false); hoverId = null; }
    public void ShowTooltip(string id)
    {
        if (!IsOpen || !index.TryGetValue(id, out var n)) return;
        hoverId = id; tipName.text = n.name; tipMeta.text = Meta(n);
        tipTrigger.text = n.kind == "stat" ? "모든 원소에 같은 능력치 보정" : string.IsNullOrEmpty(n.trigger) ? "공통 공격의 시작 경로" : n.trigger;
        tipEffect.text = n.kind == "stat" ? n.name + " · 보석 교체 시에도 유지" : n.Effect(CurrentElement);
        tipPrerequisites.text = n.requires.Length == 0 ? "공통 시작점" : "선행  " + string.Join(" + ", n.requires.Select(p => index[p].name));
        tipState.text = n.cost == 0 ? "기본 경로" : Plan.Has(id) ? "계획에 포함 · 클릭하여 상세 고정" : Plan.Available(n) ? "계획에 추가 가능 · 클릭하여 상세 고정" : "선행 계획 필요 · 클릭하여 상세 고정";
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
