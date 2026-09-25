using UnityEngine;
using UnityEngine.UI;

/// <summary>Game data presenter for the approved RPG11 prefab views. Inventory policies stay in their existing services.</summary>
public sealed class OverburstGameUI : MonoBehaviour
{
    public InventoryUI inventory;
    public StashUI stash;
    public OverburstUIWindow inventoryWindow,equipmentWindow,stashWindow;
    public Transform hud;
    public Button inventoryClose,stashClose,equipmentClose,equipmentButton,inventoryButton;
    public OverburstUIItemSlotView[] flasks;
    public FlaskEquipmentPanelUI flaskEquipment;
    public Text inventoryCapacity,stashCapacity;
    private Text hpText,energyText,region,regionDetails,characterDetail;
    private Image hpFill,energyFill;
    private Text[] stats;
    private float nextRefresh;
    private PlayerContext context;
    private StashSlotBridge stashBridge;
    private int lastTab=-1;
    private readonly ItemData[] shownFlasks=new ItemData[3];
    private readonly int[] shownKeys={-1,-1,-1};
    private InventoryQuickSlotBindingController quickSlots;
    private ShopUI shop;
    private bool shopLayout;
    private Vector2 inventoryBeforeShop;
    private int minimapScene=int.MinValue;
    private void OnEnable()=>UnityEngine.SceneManagement.SceneManager.sceneUnloaded+=SceneUnloaded;
    private void SceneUnloaded(UnityEngine.SceneManagement.Scene scene){if(!inventory||!equipmentWindow)return;stash.Close();inventory.SetVisible(false);CloseEquipment();}
    private void Awake(){
        context=PlayerContext.GetOrCreate();quickSlots=GetComponent<InventoryQuickSlotBindingController>();
        stashBridge=stash.GetComponent<StashSlotBridge>();
        inventoryClose.onClick.AddListener(()=>{if(stash.IsOpen)stash.Close();else inventory.SetVisible(false);});
        stashClose.onClick.AddListener(stash.Close);equipmentClose.onClick.AddListener(CloseEquipment);
        equipmentButton.onClick.AddListener(ToggleEquipment);inventoryButton.onClick.AddListener(()=>inventory.SetVisible(true));
        hpText=hud.Find("Action Bar Unit Frame/Bar (Health)/Text Group/Percentage Text").GetComponent<Text>();
        energyText=hud.Find("Action Bar Unit Frame/Bar (Power)/Text Group/Percentage Text").GetComponent<Text>();
        hud.Find("Action Bar Unit Frame/Bar (Health)/Text Group/Label Text").GetComponent<Text>().text="HEALTH";
        hud.Find("Action Bar Unit Frame/Bar (Power)/Text Group/Label Text").GetComponent<Text>().text="ENERGY";
        hpText.alignment=TextAnchor.MiddleRight;
        energyText.alignment=TextAnchor.MiddleLeft;
        hpFill=hud.Find("Action Bar Unit Frame/Bar (Health)/Fill").GetComponent<Image>();
        energyFill=hud.Find("Action Bar Unit Frame/Bar (Power)/Fill").GetComponent<Image>();
        region=hud.Find("Current Region/Region Name").GetComponent<Text>();regionDetails=hud.Find("Current Region/Region Details").GetComponent<Text>();
        // Level progression does not exist yet; never ship workshop example levels as live data.
        hud.Find("Action Bar Unit Frame/Level Frame/Text").GetComponent<Text>().text="—";
        var xpBar=hud.Find("Action Bar/XP Bar");
        if(xpBar){
            xpBar.gameObject.SetActive(true);
            var fillMask=xpBar.Find("Fill Rect/Fill Mask") as RectTransform;
            if(fillMask)fillMask.sizeDelta=new Vector2(0,fillMask.sizeDelta.y);
        }
        characterDetail=equipmentWindow.transform.Find("Layout/Character Detail").GetComponent<Text>();
        stats=new Text[12];for(int i=0;i<12;i++)stats[i]=equipmentWindow.transform.Find("Layout/Stat Value "+i/4+" "+i%4).GetComponent<Text>();
        equipmentWindow.gameObject.SetActive(false);
        Refresh();
    }
    private void Update(){
        if(!shop)shop=FindFirstObjectByType<ShopUI>(FindObjectsInactive.Include);
        bool trading=shop&&shop.IsOpen;
        if(trading!=shopLayout){
            shopLayout=trading;
            if(trading){inventoryBeforeShop=inventoryWindow.WindowRect.anchoredPosition;inventoryWindow.WindowRect.anchoredPosition=new Vector2(622,64);CloseEquipment();}
            else inventoryWindow.WindowRect.anchoredPosition=inventoryBeforeShop;
        }
        var input=PlayerInputFacade.Current;
        if(input!=null){if(input.EquipmentPressedThisFrame)ToggleEquipment();else if(input.UiCancelPressedThisFrame&&equipmentWindow.gameObject.activeSelf)CloseEquipment();}
        if(Time.unscaledTime>=nextRefresh){nextRefresh=Time.unscaledTime+.1f;Refresh();}
        GameplayInputBlocker.SetBlocked(this,equipmentWindow.gameObject.activeInHierarchy);
    }
    public void ToggleEquipment(){if(equipmentWindow.gameObject.activeSelf)CloseEquipment();else{equipmentWindow.Show();Refresh();GameplayInputBlocker.Block(this);}}
    public void CloseEquipment(){equipmentWindow.Close();TooltipManager.Instance?.HideTooltip();GameplayInputBlocker.Unblock(this);}
    private void OnDisable(){UnityEngine.SceneManagement.SceneManager.sceneUnloaded-=SceneUnloaded;GameplayInputBlocker.Unblock(this);TooltipManager.Instance?.HideTooltip();}
    public void Refresh(){
        if(!context)context=PlayerContext.GetOrCreate();if(!context)return;
        var health=context.CurrentActorHealth;var equipment=context.CurrentActorEquipment;
        var energy=equipment?equipment.GetComponent<OverburstElementEnergy>():null;
        if(health){hpFill.fillAmount=health.NormalizedHp;hpText.text=$"{health.CurrentHp:N0} / {health.MaxHp:N0}";}
        energyFill.fillAmount=energy?energy.Normalized:0;
        string energyValue=$"{(energy?energy.Amount:0):0} / {OverburstElementTuning.Current.maximumEnergy:0}";
        energyText.text=energyValue;
        bool dungeon=WorldSessionState.Phase==WorldPhase.Run;
        var contentScene=WorldSessionState.ContentScene;
        if(contentScene.isLoaded&&contentScene.handle!=minimapScene&&context.CurrentActor&&WorldMinimapController.Instance&&PersistentSceneFlow.Instance&&!PersistentSceneFlow.Instance.IsSwitching){
            minimapScene=contentScene.handle;
            WorldMinimapController.Instance.ShowForScene(context.CurrentActor.transform,minimapScene);
        }
        region.text=dungeon?"던전":"은신처";regionDetails.text=dungeon?"던전 탐험 중":"상인 · 창고 · 던전 포탈";
        var inv=context.CurrentActorInventory;if(inv){int used=0;foreach(var item in inv.Items)if(item!=null)used++;inventoryCapacity.text=$"{used} / {inv.UnlockedSlotCount}";}
        if(stashBridge&&stashWindow.gameObject.activeSelf){int tab=stashBridge.CurrentTabIndex;if(tab!=lastTab){lastTab=tab;var buttons=stashWindow.transform.Find("Tab Menu/Buttons Group");for(int i=0;i<3;i++)buttons.Find("Tab Button ("+(i+1)+")/Active").gameObject.SetActive(i==tab);}int used=0;foreach(var slot in stashWindow.GetComponentsInChildren<SlotUI>(true))if(slot.DisplayItem!=null)used++;stashCapacity.text=$"{used} / 63";}
        if(!equipmentWindow.gameObject.activeSelf)return;
        var weapon=equipment?equipment.CurrentWeaponItem:null;var calculated=WeaponStatCalculator.Calculate(weapon);
        characterDetail.text=weapon!=null?weapon.itemName:"무기 미장착";
        stats[0].text=health?health.MaxHp.ToString("N0"):"—";stats[1].text=energyValue;stats[2].text="—";
        stats[3].text=context.CurrentActorMovement?context.CurrentActorMovement.RunMoveSpeed.ToString("0.0"):"—";
        stats[4].text=weapon!=null?calculated.damage.ToString("0.##"):"—";stats[5].text=weapon!=null?calculated.meleeAttackSpeedMultiplier.ToString("0.##")+"×":"—";
        stats[6].text=weapon!=null?calculated.critChance.ToString("0.##")+"%":"—";stats[7].text=weapon!=null?(calculated.critDamageMultiplier*100).ToString("0.##")+"%":"—";
        for(int i=8;i<12;i++)stats[i].text="—";
        var controller=PlayerFlaskController.Current;for(int i=0;i<flasks.Length;i++){var item=controller?controller.GetItem(i):null;int key=quickSlots?quickSlots.GetFlaskKey(item):0;if(shownFlasks[i]!=item||shownKeys[i]!=key){shownFlasks[i]=item;shownKeys[i]=key;flasks[i].Present(item?.icon,item!=null?item.grade:ItemGrade.Common,key>0?(key%10).ToString():"");}}
    }
}
