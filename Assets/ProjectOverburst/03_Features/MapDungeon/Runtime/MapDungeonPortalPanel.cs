using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class MapDungeonPortalPanel : MonoBehaviour, ISlotInteractionBridge,
    ISlotSingleClickInteractionBridge, ISlotRightClickInteractionBridge
{
    [SerializeField] private GameObject primaryButtonArtwork;
    [SerializeField] private GameObject secondaryButtonArtwork;
    private static readonly Color Ivory = new Color(.93f,.90f,.83f);
    private static readonly Color Muted = new Color(.74f,.71f,.66f);
    private static readonly Color Gold = new Color(.88f,.74f,.48f);
    private static readonly Color Rule = new Color(.29f,.26f,.22f);
    private static readonly Color Reward = new Color(.43f,.82f,.66f);
    public static MapDungeonPortalPanel Active { get; private set; }
    public static bool IsOpen => Active != null;
    public SlotUI MapSocket => socket;
    public ItemData SelectedMap => ResolveSelected();
    private MapDungeonPortal owner;
    private OverburstGameUI gameUI;
    private PlayerInventory inventory;
    private SlotUI socket;
    private SlotUI[] inventorySlots;
    private RectTransform window;
    private UnityEngine.UI.Text socketName, socketSummary, status;
    private UnityEngine.UI.Button enter, remove;
    private RectTransform details;
    private Font bodyFont;
    private string selectedId;
    private Vector2 inventoryHome;
    private bool equipmentButtonWasEnabled;
    private bool restored, submitting;
    private readonly Dictionary<CanvasGroup,float> priorAlphas = new Dictionary<CanvasGroup,float>();
    private readonly List<CanvasGroup> addedGroups = new List<CanvasGroup>();

    public static MapDungeonPortalPanel Open(MapDungeonPortal portal)
    {
        if (portal == null || IsOpen) return null;
        var ui = FindFirstObjectByType<OverburstGameUI>(FindObjectsInactive.Include);
        var prefab = Resources.Load<GameObject>("UI/MapDungeon/PF_MapDungeonPortal");
        if (!ui || !ui.inventory || !ui.inventoryWindow || !prefab) return null;
        var root = Instantiate(prefab, ui.inventoryWindow.transform.parent, false);
        var view = root.GetComponent<MapDungeonPortalPanel>();
        view.owner = portal;
        view.gameUI = ui;
        view.inventory = PlayerAccountInventoryService.SharedInventory;
        if (!view.inventory) { Destroy(root); return null; }
        Active = view;
        try { view.Build(); root.SetActive(true); return view; }
        catch { view.RestoreInventory(); Destroy(root); throw; }
    }

    private void Build()
    {
        bodyFont = Resources.Load<Font>("UI/Fonts/DamageFloating/Pretendard_Medium");
        if (!bodyFont || !primaryButtonArtwork || !secondaryButtonArtwork)
            throw new InvalidOperationException("던전 포탈의 공용 UI 자산이 없습니다.");
        var root = (RectTransform)transform;
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero; root.localScale = Vector3.one;
        var blocker = gameObject.AddComponent<UnityEngine.UI.Image>();
        blocker.color = Color.clear; blocker.raycastTarget = true;
        inventoryHome = gameUI.inventoryWindow.WindowRect.anchoredPosition;
        equipmentButtonWasEnabled = gameUI.equipmentButton && gameUI.equipmentButton.interactable;
        gameUI.CloseEquipment();
        if (gameUI.equipmentButton) gameUI.equipmentButton.interactable = false;
        gameUI.inventory.SetInputToggleLocked(this, true);
        gameUI.inventoryClose.onClick.AddListener(ClosePair);
        gameUI.inventory.SetVisible(true);
        gameUI.inventoryWindow.WindowRect.anchoredPosition = new Vector2(462f,64f);
        inventorySlots = gameUI.inventoryWindow.GetComponentsInChildren<SlotUI>(true);
        foreach (var slot in inventorySlots)
        {
            var group = slot.GetComponent<CanvasGroup>();
            if (!group) { group = slot.gameObject.AddComponent<CanvasGroup>(); addedGroups.Add(group); }
            priorAlphas[group] = group.alpha;
        }
        BuildPresentation();
        inventory.Changed += Refresh;
        Refresh();
        gameUI.inventoryWindow.Focus();
    }

    // Kept separate from account/input binding so owned previews use the same layout.
    private void BuildPresentation()
    {
        BuildChrome();
        Label("사용 아이템", "입장에 사용할 아이템",33,100,354,22,14,Muted,true);
        Label("슬롯 제목", "지도 슬롯",33,129,354,30,20,Gold,true);
        var source = Array.Find(inventorySlots,s=>!s.IsWeaponSlot&&!s.IsBagSlot);
        if (!source) throw new InvalidOperationException("공용 인벤토리 슬롯이 없습니다.");
        socket = Instantiate(source.gameObject, window, false).GetComponent<SlotUI>();
        socket.name = "MapSocket";
        // The donor can already have a live rarity effect; the new slot owns its own effect.
        var copiedEffect = socket.transform.Find("Production Grade Effects");
        if (copiedEffect) { copiedEffect.gameObject.SetActive(false); Destroy(copiedEffect.gameObject); }
        // The donor's artwork is already 84px; scaling it again halves the frame.
        Place((RectTransform)socket.transform,168,223,84,84);
        socket.transform.localScale = Vector3.one;
        socket.Init(this,-1,false); socket.SetLocked(false); socket.SetNewItemMarker(false);
        socketName = Label("지도 이름", "아직 지도가 없습니다",33,370,354,30,18,Muted,true);
        socketSummary = Label("슬롯 안내", "오른쪽 인벤토리의 지도를\n우클릭하거나 이 슬롯에 끌어 넣으세요.",33,407,354,54,14,Muted,true);
        remove = Button("RemoveMap", "지도 빼기",139,466,142,36,false,ClearSelection);
        status = Label("입장 안내", "지도를 넣거나 1레벨 무료 입장을 선택하세요.",33,645,354,38,13,Muted,true);
        enter = Button("Enter", "던전 입장",94,675,232,52,true,TryEnter);
        Label("소모 안내", "입장 시 선택한 지도가 소모됩니다.",33,736,354,22,12,Muted,true);
        var divider = Rect("Divider",window); Place(divider,418,100,1,647);
        divider.gameObject.AddComponent<UnityEngine.UI.Image>().color = Rule;
        divider.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
        details = Rect("Details",window); Place(details,448,100,409,647);
    }

    private void BuildChrome()
    {
        window = Rect("MapWindow",transform);
        window.anchorMin = window.anchorMax = window.pivot = Vector2.one * .5f;
        window.sizeDelta = new Vector2(890,776); window.anchoredPosition = new Vector2(-321,64);
        var chrome = Rect("Window Chrome",window);
        chrome.anchorMin = chrome.anchorMax = chrome.pivot = Vector2.one * .5f;
        chrome.sizeDelta = window.sizeDelta * 2; chrome.localScale = Vector3.one * .5f;
        var donor = gameUI.inventoryWindow.transform;
        CopyImage(donor.GetComponent<UnityEngine.UI.Image>(),chrome.gameObject.AddComponent<UnityEngine.UI.Image>());
        foreach (string part in new[]{"Opaque Content Surface","Borders","Header"})
            Instantiate(donor.Find(part).gameObject,chrome,false).name = part;
        Place((RectTransform)chrome.Find("Opaque Content Surface"),10,148,1760,1392);
        var header = chrome.Find("Header");
        var headerDrag = header.GetComponent<DuloGames.UI.UIDragObject>();
        if (headerDrag) headerDrag.target = window;
        var title = header.Find("Text").GetComponent<UnityEngine.UI.Text>();
        Place(title.rectTransform,128,28,1524,88);
        title.text = "던전 포탈"; title.alignment = TextAnchor.MiddleCenter;
        var close = header.Find("Button (Close)").GetComponent<UnityEngine.UI.Button>();
        close.name = "Close";
        Place((RectTransform)close.transform,1664,40,72,72);
        close.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        close.onClick.AddListener(ClosePair);
    }

    private void Refresh()
    {
        if (restored || !inventory) return;
        var map = ResolveSelected();
        bool unavailable = selectedId != null && map == null;
        enter.interactable = !submitting && !unavailable;
        if (unavailable) status.text = "선택한 지도가 변경되었습니다. 다른 지도를 넣어 주세요.";
        socket.SetDisplayItem(map);
        socket.SetContextSelected(false);
        socketName.text = map == null ? "아직 지도가 없습니다" : map.itemName;
        socketName.color = map == null ? Muted : GradeConfig.GetGradeColor(map.grade);
        socketSummary.text = map == null ? "오른쪽 인벤토리의 지도를\n우클릭하거나 이 슬롯에 끌어 넣으세요."
            : ItemTooltipFormatter.GetGradeName(map.grade)+" · Lv."+map.mapState.level+"\n선택한 지도 · 입장 대기";
        remove.gameObject.SetActive(selectedId != null);
        SetButtonText(enter,map == null ? "Lv.1 무료 입장" : "던전 입장");
        foreach (var slot in inventorySlots)
        {
            var group=slot.GetComponent<CanvasGroup>();
            if (group) group.alpha = slot.DisplayItem == null || CanUse(slot) ? priorAlphas[group] : priorAlphas[group]*.23f;
        }
        foreach (Transform child in details) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        if (map == null)
        {
            Detail("지도 정보",0,55,409,34,22,Gold);
            Detail("왼쪽 슬롯에 지도를 넣으면\n던전 정보와 특성이 표시됩니다.",0,105,409,70,17,Muted);
            Detail("던전 레벨   ·   몬스터 테마   ·   보상과 위험 특성",0,218,409,45,14,Muted);
            Detail("지도는 입장하기 전까지 소모되지 않습니다.\n다른 지도로 자유롭게 교체할 수 있습니다.",0,490,409,60,14,Muted);
            return;
        }
        var state=map.mapState;
        Detail("선택한 지도",0,0,240,24,14,Muted);
        Detail("입장 준비 완료",265,0,144,24,12,new Color(.60f,.70f,.48f),true);
        Detail(map.itemName,0,30,409,36,24,GradeConfig.GetGradeColor(map.grade));
        Detail(ItemTooltipFormatter.GetGradeName(map.grade)+"  /  지도 아이템  /  1회 입장",0,71,409,26,14,Muted);
        int level=PlayerProgression.Current?PlayerProgression.Current.Level:1;
        Detail("던전 레벨",0,122,190,24,14,Muted);
        Detail(state.level+"  <size=13>내 레벨 "+level+" · "+(state.level-level>=0?"+":"")+(state.level-level)+"</size>",0,147,195,32,24,Ivory);
        Detail("몬스터 테마",214,122,195,24,14,Muted);
        Detail(MapThemeCatalog.DisplayName(state.monsterThemeId),214,150,195,30,18,Ivory);
        Detail("지형 · 규모",0,192,195,24,14,Muted);
        Detail(MapGenerationPolicy.Describe(state),0,218,195,30,16,Ivory);
        Detail("몬스터 · 장비 레벨",214,192,195,24,14,Muted);
        Detail("Lv."+state.level,214,218,195,30,18,Ivory);
        DetailRule(261);
        if (MapFarmingLoot.Applies(state))
        {
            var maximum = MapFarmingLoot.MaximumGrade(state.grade);
            Detail("보상 등급 상한",0,274,210,28,16,Ivory);
            Detail("최대 "+ItemTooltipFormatter.GetGradeName(maximum),214,274,195,28,18,GradeConfig.GetGradeColor(maximum),true);
            Detail("아이템·자연 지도·완료 지도에 적용",0,306,409,24,12,Muted);
        }
        else
        {
            Detail("획득 경험치",0,274,270,28,16,Ivory);
            Detail("+"+Mathf.RoundToInt((MapOptionPolicy.ExperienceMultiplier(state)-1)*100)+"%",280,274,129,28,18,Reward,true);
            Detail("장비 · 물약 고등급 보정",0,306,270,24,14,Ivory);
            Detail("+"+(MapOptionPolicy.HighGradeRollBias(state.grade)*100).ToString("0.#")+"%",280,306,129,24,16,Reward,true);
        }
        DetailRule(342);
        Detail("지도 특성",0,356,300,28,18,Gold);
        int count=state.options==null?0:state.options.Count;
        Detail(count+"개 적용",305,356,104,28,12,Muted,true);
        int y=395;
        if(count==0) Detail("추가 특성 없음",0,y,409,26,15,Muted);
        else foreach(var option in state.options)
        { Detail("◆  "+MapOptionPolicy.Describe(option),0,y,409,26,15,MapOptionPolicy.IsReward(option?.optionId)?Reward:new Color(.79f,.57f,.50f)); y+=28; }
        Detail("이동·공격 속도 보정은 성장 상한 내에서 적용됩니다.",0,610,409,28,12,Muted);
    }

    private ItemData ResolveSelected()
    {
        if (!inventory || string.IsNullOrEmpty(selectedId)) return null;
        foreach(var item in inventory.Items)
            if(item!=null&&item.runtimeInstanceId==selectedId&&IsMap(item)) return item;
        return null;
    }
    private static bool IsMap(ItemData item) => item!=null&&item.baseData is MapItemData&&item.mapState!=null&&item.mapState.level>=1;
    private bool Owns(SlotUI slot) => slot != null && Array.IndexOf(inventorySlots,slot)>=0;
    private bool CanUse(SlotUI slot) => Owns(slot)&&!slot.IsWeaponSlot&&!slot.IsBagSlot&&!slot.IsLocked&&IsMap(slot.DisplayItem);
    public bool TryInsert(SlotUI source)
    {
        if (!CanUse(source)||submitting) return false;
        var item=inventory.GetItemAt(source.SlotIndex);
        if (item==null||!item.IsSameRuntimeItem(source.DisplayItem)||!IsMap(item)) return false;
        selectedId=item.runtimeInstanceId; status.text="선택한 지도를 사용해 입장합니다.";
        Overburst.Quests.QuestSession.Current?.RecordMapSelected(selectedId);
        TooltipManager.Instance?.HideTooltip(); Refresh(); return true;
    }
    private void ClearSelection()
    { if(submitting)return; selectedId=null; status.text="지도를 넣거나 1레벨 무료 입장을 선택하세요."; Refresh(); }
    public static bool HandleInventoryClick(SlotClickContext context)
    {
        if(!Active)return false;
        if(context?.EventData?.button == PointerEventData.InputButton.Right)
            Active.TryInsert(context.Slot);
        return true;
    }
    public static bool HandleInventoryDrop(SlotDropContext context)
    {
        if(!Active)return false;
        if(context!=null&&context.OriginSlot==Active.socket&&Active.Owns(context.TargetSlot)&&!context.TargetSlot.IsLocked)
            Active.ClearSelection();
        return true;
    }
    public static bool AllowsSlotDrag(SlotUI slot) => !Active || slot==Active.socket || Active.CanUse(slot);
    public void BeginDragPreview(SlotUI originSlot) { }
    public void ShowDragPreviewForTarget(SlotUI targetSlot) { }
    public void ClearDragPreview() { }
    public bool HandleSlotClick(SlotClickContext context) { ClearSelection(); return true; }
    public bool HandleSlotSingleClick(SlotClickContext context) => true;
    public bool HandleSlotRightClick(SlotClickContext context) { ClearSelection(); return true; }
    public bool HandleSlotDrop(SlotDropContext context) => context!=null&&context.TargetSlot==socket&&TryInsert(context.OriginSlot);
    public bool HandleExternalDrop(SlotUI sourceSlot,Vector2 screenPosition) => true;

    private void TryEnter()
    {
        if(!owner||submitting)return;
        var selected=ResolveSelected();
        if(selectedId!=null&&selected==null){selectedId=null;Refresh();status.text="선택한 지도를 다시 확인해 주세요.";return;}
        submitting=true; enter.interactable=false;
        try
        {
            bool started=selected==null?owner.EnterLevelOne():owner.EnterSelectedMap(selected.runtimeInstanceId);
            if(!started) status.text=PersistentSceneFlow.Instance?.RunEntryError??"입장을 시작하지 못했습니다.";
        }
        catch(Exception error){status.text=error.Message;}
        finally { if(this){submitting=false;enter.interactable=true;} }
    }
    private void Update()
    {
        if(restored)return;
        if(!gameUI||!gameUI.inventory.IsVisible){ClosePair();return;}
    }
    private void ClosePair(){owner?.ClosePanel();}
    public void RestoreInventory()
    {
        if(restored)return;
        restored=true;
        if(Active==this)Active=null;
        if(inventory)inventory.Changed-=Refresh;
        DragSlot.ClearDragState(); TooltipManager.Instance?.HideTooltip();
        foreach(var prior in priorAlphas)if(prior.Key)prior.Key.alpha=prior.Value;
        foreach(var group in addedGroups)if(group)Destroy(group);
        addedGroups.Clear(); priorAlphas.Clear();
        if(gameUI)
        {
            gameUI.inventoryClose.onClick.RemoveListener(ClosePair);
            gameUI.inventory.SetInputToggleLocked(this,false);
            gameUI.inventory.SetVisible(false);
            gameUI.inventoryWindow.WindowRect.anchoredPosition=inventoryHome;
            if(gameUI.equipmentButton)gameUI.equipmentButton.interactable=equipmentButtonWasEnabled;
        }
    }
    private void OnDisable(){if(owner!=null){RestoreInventory();owner.ClosePanel();}}
    private void OnDestroy(){RestoreInventory();}

    private static RectTransform Rect(string name,Transform parent)
    {var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return (RectTransform)go.transform;}
    private static void Place(RectTransform rect,float x,float y,float w,float h)
    {rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(w,h);}
    private static void CopyImage(UnityEngine.UI.Image from,UnityEngine.UI.Image to)
    {to.sprite=from.sprite;to.type=from.type;to.color=from.color;to.material=from.material;to.pixelsPerUnitMultiplier=from.pixelsPerUnitMultiplier;}
    private UnityEngine.UI.Text Label(string name,string value,float x,float y,float w,float h,int size,Color color,bool centered=false)
    {return TextAt(window,name,value,x,y,w,h,size,color,centered?TextAnchor.MiddleCenter:TextAnchor.MiddleLeft);}
    private UnityEngine.UI.Text TextAt(Transform parent,string name,string value,float x,float y,float w,float h,int size,Color color,TextAnchor alignment)
    {
        var rect=Rect(name,parent);Place(rect,x,y,w,h);
        var text=rect.gameObject.AddComponent<UnityEngine.UI.Text>();text.font=bodyFont;text.fontSize=size;text.color=color;
        text.alignment=alignment;text.supportRichText=true;text.horizontalOverflow=HorizontalWrapMode.Wrap;
        text.verticalOverflow=VerticalWrapMode.Overflow;text.raycastTarget=false;text.text=value;return text;
    }
    private void Detail(string value,float x,float y,float w,float h,int size,Color color,bool right=false)
    {TextAt(details,"Map Detail",value,x,y,w,h,size,color,right?TextAnchor.MiddleRight:TextAnchor.MiddleLeft);}
    private void DetailRule(float y)
    {var rect=Rect("Rule",details);Place(rect,0,y,409,1);var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=Rule;image.raycastTarget=false;}
    private UnityEngine.UI.Button Button(string name,string label,float x,float y,float w,float h,bool primary,Action action)
    {
        var go=Instantiate(primary?primaryButtonArtwork:secondaryButtonArtwork,window,false);go.name=name;
        var rect=(RectTransform)go.transform;Place(rect,x,y,w,h);rect.localScale=Vector3.one;
        var button=go.GetComponent<UnityEngine.UI.Button>();button.onClick=new UnityEngine.UI.Button.ButtonClickedEvent();
        button.onClick.AddListener(()=>action());
        foreach(var text in go.GetComponentsInChildren<UnityEngine.UI.Text>(true))
        {
            if(!primary)text.font=bodyFont;text.fontSize=primary?20:16;text.color=Gold;text.alignment=TextAnchor.MiddleCenter;
            var labelRect=text.rectTransform;labelRect.anchorMin=Vector2.zero;labelRect.anchorMax=Vector2.one;
            labelRect.offsetMin=new Vector2(8,0);labelRect.offsetMax=new Vector2(-8,0);
        }
        if(!primary)foreach(var image in go.GetComponentsInChildren<UnityEngine.UI.Image>(true)){image.pixelsPerUnitMultiplier=3;if(image.sprite)image.type=UnityEngine.UI.Image.Type.Sliced;}
        var edge=go.transform.Find("Reviewed Border")?.GetComponent<UnityEngine.UI.Image>();if(edge){edge.pixelsPerUnitMultiplier=2;edge.color=new Color(1,1,1,.72f);}
        var texture=go.transform.Find("Reviewed Texture")?.GetComponent<UnityEngine.UI.Image>();if(texture)texture.pixelsPerUnitMultiplier=1;
        SetButtonText(button,label);return button;
    }
    private static void SetButtonText(UnityEngine.UI.Button button,string value)
    {foreach(var text in button.GetComponentsInChildren<UnityEngine.UI.Text>(true))text.text=value;}
}
