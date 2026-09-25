using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Independent presentation root. Inventory moves, eligibility and persistence belong to the run.</summary>
public sealed class OverburstRunUi : MonoBehaviour
{
    public const string ResourcePath = "OverburstUI/Run/PF_OverburstRunUI";
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Sprite windowFrame;
    [SerializeField] private Sprite buttonSprite;
    [SerializeField] private OverburstRunCardView cardPrefab;
    [SerializeField] private GameObject transferShellPrefab, actionButtonPrefab, transferSlotPrefab;
    private GameObject modal, cardPanel, transferPanel;
    private Image dim, dropBorder, selectedIcon;
    private TMP_Text cardStatus, transferStatus, selectedName, transferNote, dropPlaceholder, hintTitle, hintBody;
    private Button confirm, transferCancel;
    private RectTransform transferRect;
    private readonly List<OverburstRunCardView> cards = new List<OverburstRunCardView>();
    private readonly Dictionary<string, RunTransferPresentation> items = new Dictionary<string, RunTransferPresentation>(StringComparer.Ordinal);
    private Func<int, bool> selectCard;
    private Func<string, RunTransferResult> transfer;
    private Action cancelled;
    private string selectedId;
    private bool busy, revealed, completed, showingCards;
    private InventoryUI inventory;
    private OverburstGameUI gameUi;
    private readonly List<GameObject> equipmentSources=new List<GameObject>();
    private Vector2 originalInventoryPosition,originalEquipmentPosition;
    private bool equipmentLayoutActive;
    private bool inventoryWasVisible, inventoryWasToggleLocked, inventoryCaptured;
    public bool IsTransferOpen => IsOpen && !showingCards;
    public bool IsOpen => modal != null && modal.activeSelf;

    public static OverburstRunUi Create(Transform parent = null)
    {
        var prefab = Resources.Load<OverburstRunUi>(ResourcePath);
        if (prefab == null) throw new InvalidOperationException("Run UI prefab missing: " + ResourcePath);
        return Instantiate(prefab, parent, false);
    }
    public void Configure(TMP_FontAsset bodyFont, Sprite frame, Sprite button, OverburstRunCardView card, GameObject shell, GameObject actionButton, GameObject slot)
    { font = bodyFont; windowFrame = frame; buttonSprite = button; cardPrefab = card; transferShellPrefab=shell; actionButtonPrefab=actionButton; transferSlotPrefab=slot; }

    private TMP_Text Label(Transform parent, string name, string text, float x, float y,
        float w, float h, float size, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
    { return RunUiLayout.Text(parent, name, text, font, x, y, w, h, size, color, alignment); }

    private Button ActionButton(Transform parent, string name, string text, float x, float y,
        float width, bool primary, Action action)
    {
        var go=Instantiate(actionButtonPrefab,parent,false);go.name=name;
        var rect=(RectTransform)go.transform;rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
        rect.anchoredPosition=new Vector2(x,y);rect.sizeDelta=new Vector2(width*2,88);rect.localScale=Vector3.one*.5f;
        var button=go.GetComponent<Button>();button.onClick=new Button.ButtonClickedEvent();
        if(action!=null)button.onClick.AddListener(()=>action());
        SetButtonLabel(button,text);
        if(!primary)foreach(var im in go.GetComponentsInChildren<Image>(true))
            if(im.name=="Foreground")im.color=new Color(.27f,.23f,.20f);        return button;
    }
    private static void SetButtonLabel(Button button,string text)
    {
        var label=button.GetComponentInChildren<Text>(true);if(label!=null)label.text=text;
        var tmp=button.GetComponentInChildren<TMP_Text>(true);if(tmp!=null)tmp.text=text;
    }
    private void EnsureLayout()
    {
        if (modal != null) return;
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Existing global slot overlays use 12000/12001. Modal content must remain readable above them.
        canvas.sortingOrder = 12020;
        var scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        var hint = RunUiLayout.Rect(transform, "EventHint", 0, -110, 580, 82);
        hint.anchorMin = hint.anchorMax = new Vector2(.5f, 1);
        RunUiLayout.Image(hint, "HintSurface", null, new Color(.04f,.035f,.03f,.86f), 0,0,580,82);
        RunUiLayout.Image(hint, "HintBorder", windowFrame, new Color(.64f,.54f,.42f,.8f),0,0,580,82,true);
        hintTitle = Label(hint, "Objective", "", 0, 18, 538, 30, 22, RunUiLayout.Gold);
        hintBody = Label(hint, "Progress", "", 0, -18, 538, 30, 18, RunUiLayout.Ivory);
        hint.gameObject.SetActive(false);
        dim = RunUiLayout.Image(transform, "Modal", null, new Color(0,0,0,.48f),0,0,0,0);
        dim.rectTransform.anchorMin = Vector2.zero; dim.rectTransform.anchorMax = Vector2.one;
        modal = dim.gameObject;
        cardPanel = RunUiLayout.Rect(modal.transform,"CardChoices",0,0,1400,900).gameObject;

        var cardHeading=Label(cardPanel.transform,"Heading","운명을 선택하세요",-28,328,600,42,30,RunUiLayout.Gold);
        cardHeading.outlineColor=Color.black;cardHeading.outlineWidth=.35f;
        var headingShadow=cardHeading.gameObject.AddComponent<Shadow>();headingShadow.effectColor=Color.black;headingShadow.effectDistance=new Vector2(2,-2);
        var cardSubtitle=Label(cardPanel.transform,"Subtitle","이벤트 보상 · 세 장 중 하나를 선택합니다",0,288,630,36,22,RunUiLayout.Ivory);
        cardSubtitle.outlineColor=Color.black;cardSubtitle.outlineWidth=.4f;
        foreach(var text in new[]{cardHeading,cardSubtitle}){var material=text.fontMaterial;material.EnableKeyword("UNDERLAY_ON");material.SetColor("_UnderlayColor",Color.black);material.SetFloat("_UnderlayOffsetX",.4f);material.SetFloat("_UnderlayOffsetY",-.4f);material.SetFloat("_UnderlayDilate",.8f);material.SetFloat("_UnderlaySoftness",.25f);text.UpdateMeshPadding();}
        var subtitleShadow=cardSubtitle.gameObject.AddComponent<Shadow>();subtitleShadow.effectColor=Color.black;subtitleShadow.effectDistance=new Vector2(2,-2);
        ActionButton(cardPanel.transform,"Close","닫기",220,328,68,false,Cancel);
        var choices = RunUiLayout.Rect(cardPanel.transform,"Cards",0,-25,1250,600);
        for(int i=0;i<3;i++)
        {
            var card = Instantiate(cardPrefab,choices,false);
            ((RectTransform)card.transform).anchoredPosition = new Vector2((i-1)*360,0);
            card.transform.localScale = Vector3.one;
            cards.Add(card);
        }
        cardStatus = Label(cardPanel.transform,"Status","",0,-325,1080,60,18,RunUiLayout.Ivory);

        transferRect = RunUiLayout.Rect(modal.transform,"SmallTransfer",-166,20,452,432);
        transferPanel = transferRect.gameObject;
        var shell=Instantiate(transferShellPrefab,transferRect,false);
        shell.name="InventoryStyleShell";
        var shellRect=(RectTransform)shell.transform;shellRect.anchorMin=shellRect.anchorMax=shellRect.pivot=new Vector2(.5f,.5f);
        shellRect.anchoredPosition=Vector2.zero;shellRect.localScale=Vector3.one*.5f;
        shell.transform.Find("Header/Button (Close)").GetComponent<Button>().onClick.AddListener(Cancel);        Label(transferRect,"Hint","이번 판에 획득한 아이템만 전송할 수 있습니다",0,110,394,38,17,RunUiLayout.Ivory);
        var dropGo=Instantiate(transferSlotPrefab,transferRect,false);dropGo.name="DropSlot";
        var dropRect=(RectTransform)dropGo.transform;dropRect.anchorMin=dropRect.anchorMax=dropRect.pivot=new Vector2(.5f,.5f);
        dropRect.anchoredPosition=new Vector2(-137,28);dropRect.sizeDelta=new Vector2(168,168);dropRect.localScale=Vector3.one*.5f;
        dropGo.AddComponent<RunTransferDropTarget>().Bind(this);
        var hit=dropGo.GetComponent<Image>();if(hit==null)hit=dropGo.AddComponent<Image>();hit.color=Color.clear;hit.raycastTarget=true;
        dropBorder=RunUiLayout.Image(dropRect,"DropHighlight",windowFrame,Color.white,0,0,168,168,true);
        selectedIcon=RunUiLayout.Image(dropRect,"SelectedIcon",null,Color.white,0,0,132,132);
        dropPlaceholder=Label(dropRect,"Placeholder","+",0,0,132,132,60,RunUiLayout.Gold);        selectedName = Label(transferRect,"SelectedItem","",58,28,260,90,19,RunUiLayout.Ivory,TextAlignmentOptions.Left);
        transferNote = Label(transferRect,"StackNote","",0,-56,402,68,17,RunUiLayout.Ivory);
        transferStatus = Label(transferRect,"Status","",0,-118,402,48,16,RunUiLayout.Gold);
        transferCancel = ActionButton(transferRect,"Cancel","취소",-94,-172,132,false,Cancel);
        confirm = ActionButton(transferRect,"ConfirmTransfer","전송",84,-172,180,true,ConfirmTransfer);
        modal.SetActive(false);
    }

    /// <summary>True means committed; false means definitely unapplied and safe to retry.</summary>
    public void ShowCards(IReadOnlyList<RunCardPresentation> offer, Func<int,bool> onSelect, Action onCancel=null)
    {
        if(offer==null || offer.Count!=3) throw new ArgumentException("Exactly three cards required.");
        if(onSelect==null) throw new ArgumentNullException(nameof(onSelect));
        foreach(var source in offer)
            if(source==null || (!source.IsReward && ((int)source.Grade<0 || (int)source.Grade>6)))
                throw new ArgumentException("Buff cards require an active grade.");
        Open(true,onCancel); selectCard=onSelect;
        cardStatus.text="카드를 공개하고 있습니다…";
        for(int i=0;i<3;i++)
        {
            int index=i; var source=offer[i];
            cards[i].Bind(new RunCardPresentation{Title=source.Title,Description=source.Description,Value=source.Value,
                Icon=source.Icon!=null?source.Icon:RunCardIconSet.Resolve(source.Title),Grade=source.Grade,IsReward=source.IsReward},()=>ChooseCard(index));
        }
        StartCoroutine(RevealCards());
    }
    private IEnumerator RevealCards()
    {
        var holder=cards[0].transform.parent;
        float entry=0;
        while(entry<.18f)
        {
            entry+=Time.unscaledDeltaTime;
            holder.localScale=Vector3.one*Mathf.Lerp(.94f,1f,Mathf.SmoothStep(0,1,entry/.18f));
            yield return null;
        }
        holder.localScale=Vector3.one;
        foreach(var card in cards){card.Reveal();yield return new WaitForSecondsRealtime(.20f);}
        yield return new WaitForSecondsRealtime(OverburstRunCardView.RevealDuration);
        revealed=true;
        foreach(var card in cards)card.SetSelectable(true);
        cardStatus.text="한 장을 선택하면 나머지 선택지는 사라집니다.";
        cards[0].Focus();
    }
    private void ChooseCard(int index)
    {
        if(!IsOpen || !showingCards || !revealed || busy || completed)return;
        busy=true; foreach(var card in cards)card.SetSelectable(false);
        try
        {
            bool success=selectCard(index);
            if(this==null || !IsOpen)return;
            busy=false;
            if(!success)
            {
                cardStatus.text="보상을 확정하지 못했습니다. 카드를 다시 선택해 주세요.";
                foreach(var card in cards)card.SetSelectable(true);
                cards[index].Focus();return;
            }
            completed=true;Close();
        }
        catch(Exception error)
        {
            Debug.LogException(error);busy=false;completed=true;
            cardStatus.text="선택 결과를 확인하지 못했습니다. 창을 닫고 다시 확인해 주세요.";
        }
    }

    /// <summary>Keep the eligible IDs supplied by the run; use the real inventory as the drag source.</summary>
    public void ShowTransfer(IReadOnlyList<RunTransferPresentation> eligibleItems,
        Func<string,RunTransferResult> command,Action onCancel=null)
    {
        if(eligibleItems==null)throw new ArgumentNullException(nameof(eligibleItems));
        if(command==null)throw new ArgumentNullException(nameof(command));
        var snapshots=new Dictionary<string,RunTransferPresentation>(StringComparer.Ordinal);
        foreach(var source in eligibleItems)
        {
            if(source==null || string.IsNullOrEmpty(source.ItemInstanceId) || source.Quantity<1 || snapshots.ContainsKey(source.ItemInstanceId))
                throw new ArgumentException("Transfer items require unique IDs and positive quantities.");
            snapshots.Add(source.ItemInstanceId,new RunTransferPresentation{ItemInstanceId=source.ItemInstanceId,
                Name=source.Name,Description=source.Description,Icon=source.Icon,Grade=source.Grade,
                Quantity=source.Quantity,IsEquipped=source.IsEquipped});
        }
        Open(false,onCancel);transfer=command;items.Clear();
        foreach(var pair in snapshots)items.Add(pair.Key,pair.Value);
        ResetTransferSlot();
        transferStatus.text="전송하면 사망해도 창고에 남습니다.";
        if(items.Count==0)transferStatus.text="이번 판에 획득한 전송 가능한 아이템이 없습니다.";
        var game=UnityEngine.Object.FindFirstObjectByType<OverburstGameUI>();
        gameUi=game;
        inventory=game!=null?game.inventory:UnityEngine.Object.FindFirstObjectByType<InventoryUI>();
        if(inventory!=null)
        {
            inventoryWasVisible=inventory.IsVisible;inventoryWasToggleLocked=inventory.InputToggleLocked;
            inventoryCaptured=true;inventory.InputToggleLocked=true;inventory.SetVisible(true);
            Canvas.ForceUpdateCanvases();
            PositionBesideInventory(game!=null?game.inventoryWindow.transform as RectTransform:null);
            if(game!=null)
            {
                originalInventoryPosition=game.inventoryWindow.WindowRect.anchoredPosition;
                originalEquipmentPosition=game.equipmentWindow.WindowRect.anchoredPosition;
                AttachEquipmentSources(game);
                RefreshEquipmentLayout();
            }
        }
        else transferStatus.text="인벤토리를 열 수 없습니다. 창을 닫고 다시 확인해 주세요.";
        transferCancel.Select();
    }
    private void AttachEquipmentSources(OverburstGameUI game)
    {
        string[] names={"Slot • 투구","Slot • 갑옷","Slot • 장갑","Slot • 신발","Slot • 귀걸이 1","Slot • 귀걸이 2","Slot • 목걸이"};
        for(int i=0;i<names.Length;i++)
        {
            int slotIndex=i;var target=game.equipmentWindow.transform.Find("Layout/"+names[i]);if(target==null)continue;
            var overlay=RunUiLayout.Image(target,"RunTransferSelection",null,Color.clear,0,0,0,0);
            overlay.rectTransform.anchorMin=Vector2.zero;overlay.rectTransform.anchorMax=Vector2.one;
            overlay.raycastTarget=true;
            overlay.gameObject.AddComponent<RunTransferEquipmentSource>().Bind(this,()=>PlayerContext.Instance?.CurrentActorEquipment?.GetGearSlotItem(slotIndex));
            equipmentSources.Add(overlay.gameObject);
        }
    }
    private void RefreshEquipmentLayout()
    {
        if(gameUi==null)return;
        bool visible=gameUi.equipmentWindow.gameObject.activeInHierarchy;
        if(visible==equipmentLayoutActive)return;
        equipmentLayoutActive=visible;
        gameUi.inventoryWindow.WindowRect.anchoredPosition=visible?new Vector2(612,64):originalInventoryPosition;
        gameUi.equipmentWindow.WindowRect.anchoredPosition=visible?new Vector2(-94,64):originalEquipmentPosition;
        Canvas.ForceUpdateCanvases();
        if(visible)
        {
            transferRect.anchoredPosition=new Vector2(-726,64);
            transferStatus.text="장비창의 아이템을 클릭하거나 끌어 넣으세요.";
        }
        else PositionBesideInventory(gameUi.inventoryWindow.WindowRect);
    }
    private void PositionBesideInventory(RectTransform inventoryRect)
    {
        transferRect.anchoredPosition=new Vector2(-166,20);
        if(inventoryRect==null)return;
        var corners=new Vector3[4];inventoryRect.GetWorldCorners(corners);
        var root=(RectTransform)transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root,new Vector2(corners[0].x,(corners[0].y+corners[1].y)*.5f),null,out var edge);
        float x=edge.x-30-transferRect.rect.width*.5f;
        x=Mathf.Clamp(x,root.rect.xMin+transferRect.rect.width*.5f+16,root.rect.xMax-transferRect.rect.width*.5f-16);
        float y=Mathf.Clamp(edge.y,root.rect.yMin+232,root.rect.yMax-232);
        transferRect.anchoredPosition=new Vector2(x,y);
    }
    private void ResetTransferSlot()
    {
        selectedId=null;selectedIcon.enabled=false;dropPlaceholder.gameObject.SetActive(true);
        selectedName.text="인벤토리에서 아이템을\n이 슬롯으로 끌어 넣으세요";
        selectedName.color=RunUiLayout.Ivory;
        transferNote.text="선택한 스택 전체를 창고에 자동 보관합니다.\n장착 중인 아이템은 전송 시 해제됩니다.";
        confirm.interactable=false;SetTransferHover(false);
    }
    public bool TryStageTransfer(string itemInstanceId)
    {
        if(!IsOpen || showingCards || busy || completed)return false;
        if(string.IsNullOrEmpty(itemInstanceId) || !items.TryGetValue(itemInstanceId,out var item))
        {
            ResetTransferSlot();transferStatus.color=new Color(1f,.65f,.5f);
            transferStatus.text="이번 판에 획득한 소지·장착 아이템만 넣을 수 있습니다.";return false;
        }
        selectedId=itemInstanceId;selectedIcon.sprite=item.Icon;selectedIcon.enabled=item.Icon!=null;selectedIcon.preserveAspect=true;
        dropPlaceholder.gameObject.SetActive(item.Icon==null);
        selectedName.text=item.Name+"\n"+ItemTooltipFormatter.GetGradeName(item.Grade)+" · 전체 "+item.Quantity+"개";
        selectedName.color=Color.Lerp(GradeConfig.GetGradeColor(item.Grade),RunUiLayout.Ivory,.35f);
        transferNote.text=item.IsEquipped?"장착 중인 아이템입니다. 전송 시 해제됩니다.\n스택 전체를 창고에 자동 보관합니다.":"이 스택 전체를 창고에 자동 보관합니다.\n전송 후에도 사망·중도 이탈로 잃지 않습니다.";
        transferStatus.color=RunUiLayout.Gold;transferStatus.text="확인하면 전송 오브젝트를 1회 사용합니다.";
        confirm.interactable=true;return true;
    }
    public void SetTransferHover(bool hovering)
    { if(dropBorder!=null)dropBorder.color=hovering?RunUiLayout.Gold:Color.white; }
    private void ConfirmTransfer()
    {
        if(busy || completed || string.IsNullOrEmpty(selectedId) || !items.TryGetValue(selectedId,out var item))return;
        busy=true;confirm.interactable=false;transferCancel.interactable=false;
        RunTransferResult result;
        try{result=transfer(selectedId);}
        catch(Exception error){Debug.LogException(error);result=RunTransferResult.Unavailable;}
        if(this==null || !IsOpen)return;
        busy=false;transferCancel.interactable=true;
        switch(result)
        {
            case RunTransferResult.Success:
                completed=true;transferStatus.text="전송 완료 · 창고에 안전하게 보관했습니다.";
                transferStatus.color=new Color(.55f,.92f,.71f);
                transferNote.text="전체 "+item.Quantity+"개를 전송했습니다.\n이 오브젝트는 사용을 마쳤습니다.";
                SetButtonLabel(transferCancel,"닫기");transferCancel.Select();break;
            case RunTransferResult.NoSpace:
                transferStatus.text="창고 공간이 부족합니다.\n아이템과 사용 기회는 유지됩니다.";break;
            case RunTransferResult.SaveFailed:
                transferStatus.text="저장에 실패했습니다.\n아이템과 사용 기회는 유지됩니다.";break;
            case RunTransferResult.StaleItem:
                completed=true;transferStatus.text="아이템이 변경되었습니다.\n창을 다시 열어 확인해 주세요.";break;
            default:
                completed=true;transferStatus.text="전송 결과를 확인할 수 없습니다.\n창을 다시 열어 확인해 주세요.";break;
        }
        confirm.interactable=!completed;
    }
    private void Open(bool cardsMode,Action onCancel)
    {
        EnsureLayout();Close();showingCards=cardsMode;cancelled=onCancel;completed=busy=revealed=false;
        modal.SetActive(true);cardPanel.SetActive(cardsMode);transferPanel.SetActive(!cardsMode);
        dim.enabled=cardsMode;dim.raycastTarget=cardsMode;
        transferStatus.color=RunUiLayout.Gold;transferCancel.interactable=true;
        SetButtonLabel(transferCancel,"취소");
        GameplayInputBlocker.Block(this);
    }
    public void SetEventHint(string title,string progress)
    {
        EnsureLayout();hintTitle.text=title??"";hintBody.text=progress??"";
        hintTitle.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(title));
    }
    public void Cancel()
    {
        if(!IsOpen || busy)return;
        var callback=completed?null:cancelled;Close();callback?.Invoke();
    }
    public void Close()
    {
        StopAllCoroutines();
        if(modal!=null)modal.SetActive(false);
        if(inventoryCaptured && inventory!=null)
        {
            DragSlot.ClearDragState();
            inventory.InputToggleLocked=inventoryWasToggleLocked;
            inventory.SetVisible(inventoryWasVisible);
        }
        foreach(var source in equipmentSources)if(source!=null){source.SetActive(false);Destroy(source);}
        equipmentSources.Clear();
        if(gameUi!=null && equipmentLayoutActive)
        {
            gameUi.inventoryWindow.WindowRect.anchoredPosition=originalInventoryPosition;
            gameUi.equipmentWindow.WindowRect.anchoredPosition=originalEquipmentPosition;
        }
        equipmentLayoutActive=false;gameUi=null;
        inventoryCaptured=false;inventory=null;
        GameplayInputBlocker.Unblock(this);selectCard=null;transfer=null;cancelled=null;busy=false;
    }
    private void Update()
    {
        if(!IsOpen)return;
        if(!showingCards)RefreshEquipmentLayout();
        if(PlayerInputFacade.Current?.UiCancelPressedThisFrame==true){Cancel();return;}
        if(!showingCards && inventoryCaptured && inventory!=null && !inventory.IsVisible)Cancel();
    }
    private void OnDisable(){Close();}
    private void OnDestroy(){GameplayInputBlocker.Unblock(this);}
}







