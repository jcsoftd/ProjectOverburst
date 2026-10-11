using System.Linq;
using Overburst.Persistence;
using Overburst.Quests;
using UnityEngine;
using UnityEngine.UI;

// Management-scene sample state only. Both scenes share the production prefab; no account commands.
[DefaultExecutionOrder(-32000)]
public sealed class OverburstUIQuestMerchantPreview : MonoBehaviour
{
    public enum Screen { Approach, Dialogue, MerchantList, MerchantDetail, Journal, EmptyJournal, Report, HudMenu, StylistApproach }
    public QuestMerchantUI view;
    public OverburstHudMenu menu;
    public OverburstUIWorkshop workshop;
    public QuestRowView availableRow, lockedRow, personalRow;
    public bool galleryOnly;
    public Screen sample;
    bool accepted, completed;
    public bool IsVisible => menu && menu.IsOpen || view && new[]{view.approach,view.dialogue,view.merchantList,view.merchantDetail,view.journal,view.reportPopup}.Any(o=>o.activeSelf);
    void Awake()
    {
        // Hide static Scene View boards before their runtime components Awake.
        if(galleryOnly){gameObject.SetActive(false);return;}
        view.gameObject.SetActive(true);view.enabled=false;
        menu.gameObject.SetActive(true);menu.Initialize();menu.CloseImmediate();
        availableRow.button.onClick.AddListener(()=>Show(Screen.MerchantDetail));
        personalRow.button.onClick.AddListener(()=>Show(Screen.Journal));
        for(int i=0;i<3;i++){int option=i;view.approachButtons[i].onClick.AddListener(()=>{if(sample==Screen.StylistApproach){Hide();Overburst.Appearance.AppearanceCustomizationPanel.Install(workshop.transform)?.OpenExhibition();return;}if(option==0)Show(Screen.Dialogue);else if(option==1)workshop.ShowShop();else Show(Screen.MerchantList);});}
        foreach(var button in new[]{view.listClose,view.listHeaderClose,view.journalInfo.secondary,view.journalInfo.headerClose,view.dialogueSecondary})button.onClick.AddListener(Close);
        view.merchantInfo.secondary.onClick.AddListener(()=>Show(Screen.MerchantList));view.merchantInfo.headerClose.onClick.AddListener(()=>Show(Screen.MerchantList));
        view.merchantInfo.primary.onClick.AddListener(()=>{accepted=true;Show(Screen.MerchantDetail);});
        view.popupConfirm.onClick.AddListener(()=>{completed=true;Show(Screen.Journal);});view.popupCancel.onClick.AddListener(()=>Show(Screen.MerchantDetail));
        view.dialoguePrimary.onClick.AddListener(()=>Show(Screen.MerchantList));
        view.journalInfo.primary.onClick.AddListener(()=>view.journalInfo.primaryLabel.text=view.journalInfo.primaryLabel.text=="목표 표시"?"표시 해제":"목표 표시");
        ConnectMenu(0,workshop.ShowInventory);ConnectMenu(1,workshop.ShowEquipment);ConnectMenu(3,()=>Show(Screen.Journal));
        foreach(var style in workshop.GetComponentsInChildren<QuestInventoryStyle>(true))
        {
            var inventory=style.GetComponentInParent<OverburstUIWindow>(true);var sort=inventory.GetComponentInChildren<TMPro.TMP_Dropdown>(true);
            var direction=inventory.GetComponentsInChildren<Button>(true).First(b=>b.name=="InventorySortRefreshButton");
            var equipment=inventory.GetComponentsInChildren<Button>(true).First(b=>b.name=="Equipment Button");
            var sampleCapacity=style.transform.Find("Layout/Sample Capacity");if(sampleCapacity)sampleCapacity.gameObject.SetActive(false);var capacity=inventory.GetComponentsInChildren<Text>(true).First(c=>c.name=="Capacity"&&c.gameObject.activeSelf);capacity.enabled=true;style.Bind(sort,direction,equipment,capacity);
            bool ascending=false;System.Action refresh=()=>style.PresentSort(sort.options[sort.value].text,ascending,true);
            sort.onValueChanged.AddListener(_=>refresh());direction.onClick.AddListener(()=>{ascending=!ascending;refresh();});refresh();
        }
        Hide();
    }
    void ConnectMenu(int index,UnityEngine.Events.UnityAction action)
    {var button=menu.Rows[index].button;button.onClick.RemoveAllListeners();button.onClick.AddListener(()=>{menu.CloseImmediate();action();});}
    public void Close(){if(workshop)workshop.ShowHud();else Hide();}
    public void Hide()
    {
        if(menu)menu.CloseImmediate();
        if(!view)return;
        foreach(var part in new[]{view.approach,view.dialogue,view.merchantList,view.merchantDetail,view.journal,view.reportPopup,view.tracker})part.SetActive(false);
    }
    public void Show(Screen screen)
    {
        Hide();sample=screen;
        var definition=QuestCatalog.Find("quest.main-town.weapon.first-hunt");
        if(!definition)throw new System.InvalidOperationException("Management sample quest is missing");
        var state=new AccountSnapshot();
        var saved=new QuestSnapshot{questId=definition.id,title=definition.title,state=completed?SavedQuestState.Completed:SavedQuestState.Active,description=definition.description,journalDescription=definition.journalDescription,readyDescription=definition.readyDescription,completedDescription=definition.completedDescription,rewardGold=definition.rewardGold,rewardExperience=definition.rewardExperience,objectives=definition.objectives.Select(o=>new QuestObjectiveSnapshot{kind=o.kind,targetId=o.targetId,label=o.label,required=o.required,current=screen==Screen.Report?o.required:3}).ToList()};
        if(accepted || screen==Screen.Journal || screen==Screen.Report)state.quests.Add(saved);
        view.PresentPreview(state,definition.id,screen==Screen.Journal || screen==Screen.EmptyJournal);
        availableRow.Present(definition.title,"야외 몬스터 처치 · 무기상인에게 보고","100 G  ·  경험치 30",accepted?"진행 중":"수락 가능",true,screen==Screen.MerchantDetail);
        lockedRow.Present("미지의 지도","첫 사냥을 완료하면 받을 수 있습니다.","150 G  ·  경험치 40","선행 의뢰 필요",false,false);
        personalRow.Present(definition.title,"","",completed?"완료":"진행 중",true,true);
        availableRow.gameObject.SetActive(true);lockedRow.gameObject.SetActive(true);personalRow.gameObject.SetActive(screen!=Screen.EmptyJournal);
        view.ConfigureApproach(screen==Screen.StylistApproach);view.approachName.text=screen==Screen.StylistApproach?"치장사":"무기상인";view.approachLocation.text=screen==Screen.StylistApproach?"치장 천막":"대장간";view.SelectOption(1);
        view.dialogueBody.text="마을 밖으로 나갈 생각인가?\n무기부터 점검하게. 밖에서는 자네를 지켜줄 사람이 없으니.";
        view.popupMessage.text="첫 사냥의 목표를 달성했습니다.\n무기상인에게 보고하고 보상을 받으시겠습니까?";
        view.popupGold.text="100 G";view.popupExperience.text="30 XP";view.popupRewards.SetActive(true);
        if(screen==Screen.Approach || screen==Screen.StylistApproach)view.approach.SetActive(true);
        else if(screen==Screen.Dialogue)view.dialogue.SetActive(true);
        else if(screen==Screen.MerchantList || screen==Screen.MerchantDetail){view.merchantList.SetActive(true);view.merchantDetail.SetActive(screen==Screen.MerchantDetail);}
        else if(screen==Screen.Journal || screen==Screen.EmptyJournal){view.journal.SetActive(true);if(screen==Screen.EmptyJournal)view.PresentPreview(state,null,true);}
        else if(screen==Screen.Report)view.reportPopup.SetActive(true);
        else if(menu){menu.gameObject.SetActive(true);menu.Open();menu.popup.alpha=1;}
        Canvas.ForceUpdateCanvases();
    }
}
