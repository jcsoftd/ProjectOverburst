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
    private Text levelText,xpText,xpPercentText;
    private Image hpFill,energyFill;
    private RectTransform xpFillMask;
    private float xpTrackWidth;
    private Text[] stats;
    private GearEquipmentPanelUI gearPanel;
    private float nextRefresh;
    private PlayerContext context;
    private StashSlotBridge stashBridge;
    private int lastTab=-1;
    private readonly ItemData[] shownFlasks=new ItemData[3];
    private readonly int[] shownKeys={-1,-1,-1};
    private readonly string[] shownKeyLabels=new string[3]; // 2026-10-01 설정에서 퀵슬롯 키를 바꾸면 물약 칸 글자도 바뀐다.
    private InventoryQuickSlotBindingController quickSlots;
    private ShopUI shop;
    private float nextShopLookup;
    private bool shopLayout;
    private Vector2 inventoryBeforeShop;
    private int minimapScene=int.MinValue;
    private PlayerInventory observedInventory;
    private bool inventoryDirty=true;
    private (int used,int capacity)? shownInventoryCapacity,shownStashCapacity;
    private (int level,int experience,int needed,bool gaining)? shownProgression;
    private (float current,float maximum)? shownHealth;
    private (bool light,float amount,float capacity,int radiance)? shownEnergy;
    private string energyValue;
    private PlayerEquipment energyOwner;
    private OverburstElementEnergy cachedEnergy;
    private bool? shownDungeon;
    private (string weapon,float elementalDamage)? shownCharacterDetail;
    private readonly (float? value,string format,string prefix,string suffix)?[] shownStats=new (float?,string,string,string)?[12];
    private void OnEnable()=>UnityEngine.SceneManagement.SceneManager.sceneUnloaded+=SceneUnloaded;
    private void SceneUnloaded(UnityEngine.SceneManagement.Scene scene){if(!inventory||!equipmentWindow)return;stash.Close();inventory.SetVisible(false);CloseEquipment();}
    private void Awake(){
        context=PlayerContext.GetOrCreate();quickSlots=GetComponent<InventoryQuickSlotBindingController>();
        stashBridge=stash.GetComponent<StashSlotBridge>();
        inventoryClose.onClick.AddListener(()=>{if(stash.IsOpen)stash.Close();else inventory.SetVisible(false);});
        stashClose.onClick.AddListener(stash.Close);equipmentClose.onClick.AddListener(CloseEquipment);
        equipmentButton.onClick.AddListener(ToggleEquipment);inventoryButton.onClick.AddListener(ToggleInventory);
        hpText=hud.Find("Action Bar Unit Frame/Bar (Health)/Text Group/Percentage Text").GetComponent<Text>();
        energyText=hud.Find("Action Bar Unit Frame/Bar (Power)/Text Group/Percentage Text").GetComponent<Text>();
        hud.Find("Action Bar Unit Frame/Bar (Health)/Text Group/Label Text").GetComponent<Text>().text="HEALTH";
        hud.Find("Action Bar Unit Frame/Bar (Power)/Text Group/Label Text").GetComponent<Text>().text="ENERGY";
        hpText.alignment=TextAnchor.MiddleRight;
        energyText.alignment=TextAnchor.MiddleLeft;
        hpFill=hud.Find("Action Bar Unit Frame/Bar (Health)/Fill").GetComponent<Image>();
        energyFill=hud.Find("Action Bar Unit Frame/Bar (Power)/Fill").GetComponent<Image>();
        region=hud.Find("Current Region/Region Name").GetComponent<Text>();regionDetails=hud.Find("Current Region/Region Details").GetComponent<Text>();
        levelText=hud.Find("Action Bar Unit Frame/Level Frame/Text").GetComponent<Text>();
        var xpBar=hud.Find("Action Bar/XP Bar");
        if(xpBar){
            xpBar.gameObject.SetActive(true);
            xpFillMask=xpBar.Find("Fill Rect/Fill Mask") as RectTransform;
            var fillRect=xpBar.Find("Fill Rect") as RectTransform;
            xpTrackWidth=fillRect?fillRect.sizeDelta.x:0f;
            xpText=xpBar.Find("Tooltip/XP Text")?.GetComponent<Text>();
            xpPercentText=xpBar.Find("Tooltip/Percentage Text")?.GetComponent<Text>(); // 원본 예시 "91%"가 남아 있던 칸
            // 원본 Demo_XPTooltip이 HUD 프리팹에서 빠져 마우스 오버 설명창이 뜨지 않았다.
            var xpTooltip=xpBar.GetComponent<ExperienceBarTooltip>();
            if(!xpTooltip)xpTooltip=xpBar.gameObject.AddComponent<ExperienceBarTooltip>();
            xpTooltip.Configure(xpBar.Find("Tooltip") as RectTransform,fillRect);
        }
        characterDetail=equipmentWindow.transform.Find("Layout/Character Detail").GetComponent<Text>();
        stats=new Text[12];for(int i=0;i<12;i++)stats[i]=equipmentWindow.transform.Find("Layout/Stat Value "+i/4+" "+i%4).GetComponent<Text>();
        string[] bonusLabels={"일반 몬스터 피해","약공 피해","강공 피해","정예·보스 피해"};
        for(int i=8;i<12;i++)equipmentWindow.transform.Find("Layout/Stat Label "+i/4+" "+i%4).GetComponent<Text>().text=bonusLabels[i-8];
        gearPanel=equipmentWindow.GetComponent<GearEquipmentPanelUI>();
        if(!gearPanel)gearPanel=equipmentWindow.gameObject.AddComponent<GearEquipmentPanelUI>();
        gearPanel.Bind();
        equipmentWindow.gameObject.SetActive(false);
        // The authored target HUD was disabled by the old overhead-bar presentation.
        var targetHud = hud.parent.GetComponentInChildren<EnemyTargetHpHud>(true);
        if (targetHud != null) { targetHud.enabled = true; targetHud.gameObject.SetActive(true); }
        // 2026-10-01 ESC 메뉴(일시정지·설정). 저작된 프리팹을 이 캔버스 아래에 한 번 놓는다.
        OverburstGameMenu.Install(transform);
        OverburstSkillTreeUI.Install(transform);
        OverburstHudMenu.Install(this);
        Overburst.Appearance.AppearanceCustomizationPanel.Install(transform);
        Refresh();
    }
    private void Update(){
        if(PersistentSceneFlow.Instance&&PersistentSceneFlow.Instance.IsSwitching&&equipmentWindow.gameObject.activeSelf)CloseEquipment();
        if(!shop&&Time.unscaledTime>=nextShopLookup){shop=FindFirstObjectByType<ShopUI>(FindObjectsInactive.Include);nextShopLookup=Time.unscaledTime+.5f;}
        bool trading=shop&&shop.IsOpen;
        if(trading!=shopLayout){
            shopLayout=trading;
            if(trading){inventoryBeforeShop=inventoryWindow.WindowRect.anchoredPosition;inventoryWindow.WindowRect.anchoredPosition=new Vector2(OverburstUIShopSkin.InventoryX,OverburstUIShopSkin.WindowY);CloseEquipment();}
            else inventoryWindow.WindowRect.anchoredPosition=inventoryBeforeShop;
        }
        var input=PlayerInputFacade.Current;
        if(input!=null&&!OverburstGameMenu.IsOpen&&!OverburstSkillTreeUI.IsWindowOpen&&!OverburstHudMenu.IsExpanded&&!Overburst.Appearance.AppearanceCustomizationPanel.IsOpen){if(input.EquipmentPressedThisFrame)ToggleEquipment();else if(input.UiCancelPressedThisFrame&&equipmentWindow.gameObject.activeSelf)CloseEquipment();}
        if(Time.unscaledTime>=nextRefresh){nextRefresh=Time.unscaledTime+.1f;Refresh();}
        RefreshWindowButtons();
        GameplayInputBlocker.SetBlocked(this,equipmentWindow.gameObject.activeInHierarchy);
    }
    // 장비창의 인벤토리 버튼과 인벤토리의 장비 버튼은 서로 여닫는 토글이다. 글자는 지금 상태의 반대 동작을 보인다.
    public void ToggleInventory(){if(inventory.IsVisible&&stash.IsOpen)stash.Close();else if(inventory.IsVisible&&shop&&shop.IsOpen)shop.Close();else inventory.TryToggleFromUser();}
    private bool? shownInventoryOpen,shownEquipmentOpen;
    private void RefreshWindowButtons(){
        bool inventoryOpen=inventory.IsVisible,equipmentOpen=equipmentWindow.gameObject.activeSelf;
        if(shownInventoryOpen!=inventoryOpen){shownInventoryOpen=inventoryOpen;SetButtonLabel(inventoryButton,inventoryOpen?"인벤토리 닫기":"인벤토리 열기");}
        if(shownEquipmentOpen!=equipmentOpen){shownEquipmentOpen=equipmentOpen;SetButtonLabel(equipmentButton,equipmentOpen?"장비창 닫기":"장비창 열기");}
    }
    private static void SetButtonLabel(Button button,string label){
        if(!button)return;
        var text=button.GetComponentInChildren<Text>(true);if(text){text.text=label;return;}
        var tmp=button.GetComponentInChildren<TMPro.TMP_Text>(true);if(tmp)tmp.text=label;
    }
    public void ToggleEquipment(){if(equipmentWindow.gameObject.activeSelf)CloseEquipment();else if(!PersistentSceneFlow.Instance||!PersistentSceneFlow.Instance.IsSwitching){equipmentWindow.Show();Refresh();GameplayInputBlocker.Block(this);}}
    public void CloseEquipment(){equipmentWindow.Close();TooltipManager.Instance?.HideTooltip();GameplayInputBlocker.Unblock(this);}
    private void OnDisable(){UnityEngine.SceneManagement.SceneManager.sceneUnloaded-=SceneUnloaded;UnbindInventory();GameplayInputBlocker.Unblock(this);TooltipManager.Instance?.HideTooltip();}
    private void OnDestroy()=>UnbindInventory();
    public void Refresh(){
        if(!context)context=PlayerContext.GetOrCreate();if(!context)return;
        var health=context.CurrentActorHealth;var equipment=context.CurrentActorEquipment;
        var progression=PlayerProgression.Current;
        int playerLevel=progression?progression.Level:1;
        var progressionState=(playerLevel,progression?progression.Experience:0,progression?progression.ExperienceToNext:0,progression&&playerLevel<OverburstGrowthRules.MaximumLevel);
        if(shownProgression!=progressionState){
            shownProgression=progressionState;levelText.text=playerLevel.ToString();
            if(xpFillMask)xpFillMask.sizeDelta=new Vector2(xpTrackWidth*(progression?progression.ExperienceProgress:0f),xpFillMask.sizeDelta.y);
            if(xpText)xpText.text=progressionState.Item4?$"{progression.Experience:N0} / {progression.ExperienceToNext:N0}":"MAX";
            if(xpPercentText)xpPercentText.text=progressionState.Item4?$"{Mathf.FloorToInt(progression.ExperienceProgress*1000f)/10f:0.0}%":"MAX";
        }
        if(equipment!=energyOwner||(equipment&&!cachedEnergy)){energyOwner=equipment;cachedEnergy=equipment?equipment.GetComponent<OverburstElementEnergy>():null;}
        var energy=cachedEnergy;
        if(health&&shownHealth!=(health.CurrentHp,health.MaxHp)){shownHealth=(health.CurrentHp,health.MaxHp);hpFill.fillAmount=health.NormalizedHp;hpText.text=$"{health.CurrentHp:N0} / {health.MaxHp:N0}";}
        energyFill.fillAmount=energy?energy.Normalized:0;
        // 60D light: text shows the 200 cap and radiance until the HUD overcharge layer is authored.
        bool light=energy&&energy.Element==WeaponElement.Light;
        var energyState=(light,energy?energy.Amount:0,light?energy.Capacity:OverburstElementTuning.Current.maximumEnergy,light?energy.RadianceStacks:0);
        if(shownEnergy!=energyState){
            shownEnergy=energyState;
            energyValue=$"{energyState.Item2:0} / {energyState.Item3:0}"+(energyState.Item4>0?$"  광휘 {energyState.Item4}":string.Empty);
            energyText.text=energyValue;
        }
        bool dungeon=WorldSessionState.Phase==WorldPhase.Run;
        var contentScene=WorldSessionState.ContentScene;
        var minimap=WorldMinimapController.Instance;
        var sceneFlow=PersistentSceneFlow.Instance;
        bool showMinimap=(WorldSessionState.IsHideout||dungeon)&&sceneFlow!=null&&!sceneFlow.IsSwitching;
        if(!showMinimap){
            if(minimapScene!=int.MinValue||(minimap!=null&&minimap.IsVisible)){
                minimap?.ForceHide();minimapScene=int.MinValue;
            }
        }
        else if(contentScene.isLoaded&&contentScene.handle!=minimapScene&&context.CurrentActor&&minimap&&sceneFlow){
            minimapScene=contentScene.handle;
            minimap.ShowForScene(context.CurrentActor.transform,minimapScene);
        }
        if(shownDungeon!=dungeon){shownDungeon=dungeon;region.text=dungeon?"던전":"은신처";regionDetails.text=dungeon?"던전 탐험 중":"상인 · 창고 · 던전 포탈";}
        RefreshInventoryCapacity(context.CurrentActorInventory);
        if(stashBridge&&stashWindow.gameObject.activeSelf){
            int tab=stashBridge.CurrentTabIndex;if(tab!=lastTab){lastTab=tab;var buttons=stashWindow.transform.Find("Tab Menu/Buttons Group");for(int i=0;i<3;i++)buttons.Find("Tab Button ("+(i+1)+")/Active").gameObject.SetActive(i==tab);}
            var capacity=(stashBridge.OccupiedSlotCount,63);if(shownStashCapacity!=capacity){shownStashCapacity=capacity;stashCapacity.text=$"{capacity.Item1} / {capacity.Item2}";}
        }
        if(!equipmentWindow.gameObject.activeSelf)return;
        gearPanel.Refresh(equipment);
        var weapon=equipment?equipment.CurrentWeaponItem:null;var calculated=equipment?equipment.CurrentWeaponStats:WeaponFinalStats.Empty;
        var gear=GearStatTotals.From(equipment);
        var detail=(weapon!=null?weapon.itemName:"무기 미장착",gear.ElementalDamage);
        if(shownCharacterDetail!=detail){shownCharacterDetail=detail;characterDetail.text=detail.Item1+$"  ·  원소 피해 +{detail.Item2:0.##}%";}
        SetStat(0,health?health.MaxHp:(float?)null,"N0");stats[1].text=energyValue;SetStat(2,progression?progression.Armor:0,"0.#");
        var movement=context.CurrentActorMovement;SetStat(3,movement?movement.RunMoveSpeed:(float?)null,"0.0");
        SetStat(4,weapon!=null?calculated.damage:(float?)null,"0.##");SetStat(5,weapon!=null?calculated.meleeAttackSpeedMultiplier:(float?)null,"0.##",suffix:"×");
        SetStat(6,weapon!=null?calculated.critChance:(float?)null,"0.##",suffix:"%");SetStat(7,weapon!=null?calculated.critDamageMultiplier*100:(float?)null,"0.##",suffix:"%");
        SetStat(8,gear.NormalDamage,"0.##","+","%");SetStat(9,gear.WeakDamage,"0.##","+","%");
        SetStat(10,gear.HeavyDamage,"0.##","+","%");SetStat(11,gear.EliteBossDamage,"0.##","+","%");
        var controller=PlayerFlaskController.Current;for(int i=0;i<flasks.Length;i++){var item=controller?controller.GetItem(i):null;int key=quickSlots?quickSlots.GetFlaskKey(item):0;string keyLabel=key>0?QuickSlotKeyLabels.Short(key):"";if(shownFlasks[i]!=item||shownKeys[i]!=key||shownKeyLabels[i]!=keyLabel){shownFlasks[i]=item;shownKeys[i]=key;shownKeyLabels[i]=keyLabel;flasks[i].Present(item,keyLabel);}}
    }
    private void MarkInventoryDirty()=>inventoryDirty=true;
    private void UnbindInventory(){if(observedInventory)observedInventory.Changed-=MarkInventoryDirty;observedInventory=null;inventoryDirty=true;}
    private void RefreshInventoryCapacity(PlayerInventory current){
        if(current!=observedInventory){UnbindInventory();observedInventory=current;if(observedInventory)observedInventory.Changed+=MarkInventoryDirty;}
        if(!current||!inventoryDirty)return;
        inventoryDirty=false;int used=0;var items=current.Items;for(int i=0;i<items.Count;i++)if(items[i]!=null)used++;
        var capacity=(used,current.UnlockedSlotCount);if(shownInventoryCapacity!=capacity){shownInventoryCapacity=capacity;inventoryCapacity.text=$"{used} / {capacity.Item2}";}
    }
    private void SetStat(int index,float? value,string format,string prefix="",string suffix=""){
        var state=(value,format,prefix,suffix);if(shownStats[index]==state)return;
        shownStats[index]=state;stats[index].text=value.HasValue?prefix+value.Value.ToString(format)+suffix:"—";
    }
}
