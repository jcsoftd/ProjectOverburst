using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEditor;
using Object=UnityEngine.Object;

public static class OverburstGameUIValidation
{
    public static readonly string Output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../개인파일/코덱스산출/UI/20260923_GameIntegration"));
    static OverburstGameUI UI=>Object.FindFirstObjectByType<OverburstGameUI>();
    static InventoryContextMenuController Menu=>UI.GetComponent<InventoryContextMenuController>();
    static PointerEventData Pointer()=>new PointerEventData(EventSystem.current){position=new Vector2(1350,680),button=PointerEventData.InputButton.Right};
    static void Check(bool result,string message){File.AppendAllText(Path.Combine(Output,"interaction-validation.txt"),(result?"PASS ":"FAIL ")+message+"\n");if(!result)throw new Exception(message);}
    static Button FindButton(string label)=>UI.transform.Find("InventoryContextBlocker/InventoryContextMenu").GetComponentsInChildren<Button>().First(x=>x.GetComponentInChildren<TMP_Text>().text==label);
    static void Open(ItemData item){var slot=UI.inventoryWindow.GetComponentsInChildren<SlotUI>(true).First(x=>x.DisplayItem==item);Menu.OpenForSlot(slot,Pointer());}
    public static void ItemInteractions(){
        if(!Application.isPlaying)throw new Exception("Play required");Application.runInBackground=true;Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output,"interaction-validation.txt"),"RPG11 gameplay UI regression, temporary Play data only\n");
        var ui=UI;var inv=PlayerContext.Instance.CurrentActorInventory;ui.inventory.SetVisible(true);if(!ui.equipmentWindow.gameObject.activeSelf)ui.ToggleEquipment();
        var all=AssetDatabase.FindAssets("t:BaseItemData",new[]{"Assets/ProjectOverburst"}).Select(g=>AssetDatabase.LoadAssetAtPath<BaseItemData>(AssetDatabase.GUIDToAssetPath(g))).Where(x=>x&&x.icon).ToArray();
        var weapon=new ItemData(all.OfType<WeaponItemData>().First(),1,ItemGrade.Epic);var flask=new ItemData(all.OfType<FlaskItemData>().First(),1,ItemGrade.Epic);var potion=new ItemData(all.OfType<ConsumableItemData>().First(x=>!(x is FlaskItemData)&&!x.IsPermanentSingleItem),1,ItemGrade.Rare);potion.stackCount=8;
        Check(inv.AddItem(weapon)&&inv.AddItem(flask)&&inv.AddItem(potion),"real item ownership added");
        Open(weapon);FindButton("장착").onClick.Invoke();Check(PlayerContext.Instance.CurrentActorEquipment.CurrentWeaponItem==weapon,"right click weapon equip");Check(!Menu.IsOpen,"context closes after equip");
        Open(flask);FindButton("장착").onClick.Invoke();Check(PlayerFlaskController.Current.GetItem(0)==flask,"right click flask equip");
        ui.flasks[0].GetComponent<Button>().onClick.Invoke();var panel=ui.flaskEquipment.transform.Find("FlaskKeyPicker");Check(panel.gameObject.activeSelf,"equipped flask opens key picker");
        panel.GetComponentsInChildren<Button>().First(x=>x.GetComponentInChildren<TMP_Text>()?.text=="0번").onClick.Invoke();Check(ui.GetComponent<InventoryQuickSlotBindingController>().GetBoundFlask(10)==flask,"0 key binds slot ten");
        Open(potion);FindButton("퀵슬롯 등록").onClick.Invoke();var keyChoices=ui.transform.Find("InventoryContextBlocker/InventoryContextMenu").GetComponentsInChildren<Button>();Check(keyChoices.Length>=11,"ten quick slot choices plus return");keyChoices.First(x=>x.GetComponentInChildren<TMP_Text>().text.StartsWith("8 :")).onClick.Invoke();Check(ui.GetComponent<InventoryQuickSlotBindingController>().GetBoundConsumable(8)==potion.baseData,"consumable binds slot eight");
        Open(potion);FindButton("나누기").onClick.Invoke();var so=new SerializedObject(Menu);var input=(TMP_InputField)so.FindProperty("splitAmountInput").objectReferenceValue;input.text="2";((Button)so.FindProperty("splitOkButton").objectReferenceValue).onClick.Invoke();Check(inv.CountItemsByBaseData(potion.baseData)==8&&inv.Items.Count(x=>x!=null&&x.baseData==potion.baseData)==2,"split preserves quantity and creates second stack");
        var slots=ui.inventoryWindow.GetComponentsInChildren<SlotUI>(true);var source=slots.First(x=>x.DisplayItem==potion);var target=slots.First(x=>x.DisplayItem==null&&!x.IsLocked&&!x.IsBagSlot);var pointer=Pointer();pointer.button=PointerEventData.InputButton.Left;source.GetComponent<DragSlot>().OnBeginDrag(pointer);target.GetComponent<DropSlot>().OnDrop(pointer);source.GetComponent<DragSlot>().OnEndDrag(pointer);Check(target.DisplayItem==potion&&!DragSlot.IsDragging,"real drag/drop moves stack and clears drag state");
        Open(potion);Menu.Close();Check(!Menu.IsOpen,"context cancel closes without mutation");
        ui.CloseEquipment();ui.inventory.SetVisible(false);Check(!GameplayInputBlocker.IsGameplayInputBlocked,"all windows closed releases input");ui.GetComponent<InventoryItemActionService>().UseQuickSlot(10);Check(PlayerFlaskController.Current.CooldownRemaining(0)>0,"slot ten uses real flask and starts cooldown");
        ui.inventory.SetVisible(true);ui.ToggleEquipment();ui.Refresh();
        foreach(var v in ui.inventoryWindow.GetComponentsInChildren<OverburstUIItemSlotView>(true)){var corners=new Vector3[4];((RectTransform)v.transform).GetWorldCorners(corners);Check(Mathf.Abs(corners[2].x-corners[0].x-84)<.1f,"inventory slot 84px");}
        TooltipManager.Instance.ShowTooltip(flask);var tooltip=ui.GetComponentInChildren<OverburstGameTooltip>();Check(tooltip.view.gameObject.activeSelf&&tooltip.view.Body.text.Contains("sprite index="),"real flask tooltip uses generated quality sprites");TooltipManager.Instance.HideTooltip();
        Check(ui.equipmentWindow.GetComponentInChildren<OverburstUICharacterPreview>().Texture!=null,"live character render texture");
    }
    public static void StashInteractions(){
        var ui=UI;ui.stash.Close();ui.CloseEquipment();ui.inventory.SetVisible(false);var actor=PlayerContext.Instance.CurrentActor;var chest=Object.FindFirstObjectByType<StashInteractable>();actor.transform.position=chest.transform.position+Vector3.right;Physics.SyncTransforms();ui.stash.Open();Check(ui.stash.IsOpen&&ui.inventory.IsVisible&&GameplayInputBlocker.IsGameplayInputBlocked,"stash opens inventory and blocks combat");
        var inv=PlayerContext.Instance.CurrentActorInventory;var slot=ui.inventoryWindow.GetComponentsInChildren<SlotUI>(true).First(x=>x.DisplayItem?.baseData is ConsumableItemData&&!(x.DisplayItem.baseData is FlaskItemData));var item=slot.DisplayItem;var bridge=ui.stash.GetComponent<StashSlotBridge>();Check(bridge.TryMoveInventorySlotToFirstAvailableStashSlot(slot.SlotIndex),"deposit existing stack");var stored=ui.stashWindow.GetComponentsInChildren<SlotUI>(true).First(x=>x.DisplayItem==item);Check(!inv.ContainsItem(item),"deposit removes inventory ownership");Check(bridge.HandleSlotClick(SlotClickContext.Create(stored,Pointer(),true)),"double click withdraw");Check(inv.ContainsItem(item),"withdraw restores inventory ownership");
        for(int tab=0;tab<3;tab++){Check(bridge.SwitchTab(tab),"stash tab "+tab);ui.stash.UpdateTabVisuals();}
        bridge.SwitchTab(0);ui.stash.Close();Check(!ui.inventory.IsVisible&&!GameplayInputBlocker.IsGameplayInputBlocked,"stash close restores previous inventory state and input");
        for(int i=0;i<3;i++){ui.stash.Open();ui.stash.Close();ui.ToggleEquipment();ui.CloseEquipment();}Check(!GameplayInputBlocker.IsGameplayInputBlocked,"three repeated stash/equipment cycles");
        ui.stash.Open();ui.Refresh();
    }
    public static void ShopInteractions(){
        var ui=UI;ui.stash.Close();ui.CloseEquipment();ui.inventory.SetVisible(false);
        var npc=Object.FindFirstObjectByType<GeneralGoodsMerchantInteractable>();var actor=PlayerContext.Instance.CurrentActor;actor.transform.position=npc.transform.position+Vector3.right;Physics.SyncTransforms();
        var definition=(MerchantDefinition)new SerializedObject(npc).FindProperty("merchantDefinition").objectReferenceValue;var shop=Object.FindFirstObjectByType<ShopUI>(FindObjectsInactive.Include);shop.Open(definition);Check(shop.IsOpen&&ui.inventory.IsVisible,"merchant opens inventory");
        var so=new SerializedObject(shop);var stock=so.FindProperty("merchantInventorySlots");var slot=Enumerable.Range(0,stock.arraySize).Select(i=>(SlotUI)stock.GetArrayElementAtIndex(i).objectReferenceValue).First(x=>x.DisplayItem!=null);
        Check(shop.HandleShopSlotRightClicked(slot,Pointer()),"merchant right click opens trading menu");
        var context=(ShopContextMenuController)so.FindProperty("contextMenu").objectReferenceValue;context.Close();
        Check(shop.HandleShopSlotClicked(slot),"merchant item adds to trade offer");((Button)so.FindProperty("clearButton").objectReferenceValue).onClick.Invoke();
        TooltipManager.Instance.ShowTooltip(slot.DisplayItem,definition,true);var tip=ui.GetComponentInChildren<OverburstGameTooltip>();Check(tip.view.Body.text.Contains("거래 가격"),"merchant price reaches RPG11 tooltip");TooltipManager.Instance.HideTooltip();
        shop.ShowQuestTab();shop.ShowTradeTab();shop.Close();Check(!ui.inventory.IsVisible&&!GameplayInputBlocker.IsGameplayInputBlocked,"shop closes and restores inventory/input");
    }
    public static void LiveNumbers(){
        var ui=UI;var c=PlayerContext.Instance;var health=c.CurrentActorHealth;health.ResetHealth();health.TakeDamage(new DamageInfo(17,c.CurrentActor.transform.position));ui.Refresh();
        var hp=ui.hud.Find("Action Bar Unit Frame/Bar (Health)/Fill").GetComponent<Image>();Check(Mathf.Abs(hp.fillAmount-health.NormalizedHp)<.001f,"HUD follows actual damage");health.Heal(17);ui.Refresh();Check(Mathf.Abs(hp.fillAmount-health.NormalizedHp)<.001f,"HUD follows actual healing");
        var item=c.CurrentActorEquipment.CurrentWeaponItem;var energy=c.CurrentActorEquipment.GetComponent<OverburstElementEnergy>()??c.CurrentActorEquipment.gameObject.AddComponent<OverburstElementEnergy>();Check(energy.RecordConfirmedHit(item.runtimeInstanceId,item.ResolvedElement,92001,10),"real energy accepts confirmed hit");ui.Refresh();Check(Mathf.Abs(ui.hud.Find("Action Bar Unit Frame/Bar (Power)/Fill").GetComponent<Image>().fillAmount-energy.Normalized)<.001f,"HUD follows actual element energy");
        foreach(var grade in new[]{"Normal","Elite"}){
            var go=Object.Instantiate(Resources.Load<GameObject>("UI/World/MonsterHpBars/PF_EnemyHpBar_"+grade),ui.transform,false);var v=go.GetComponent<EnemyHpBarView>();var target=new GameObject("UI validation target").AddComponent<CombatHealth>();target.SetMaxHp(100,true);v.Bind(target);v.SetProjectionVisible(true);target.TakeDamage(new DamageInfo(35,Vector3.zero));
            var view=go.GetComponentInChildren<OverburstEnemyHealthBarView>(true);var so=new SerializedObject(view);var fill=(Image)so.FindProperty("healthFill").objectReferenceValue;Check(Mathf.Abs(fill.fillAmount-.65f)<.001f,grade+" enemy prefab reflects actual damage");v.Unbind();v.Bind(health);health.TakeDamage(new DamageInfo(10,c.CurrentActor.transform.position));Check(Mathf.Abs(fill.fillAmount-health.NormalizedHp)<.001f,grade+" enemy bar rebind updates health");v.Unbind();Object.Destroy(go);Object.Destroy(target.gameObject);health.Heal(10);
        }
    }
    public static void Capture(string name){ScreenCapture.CaptureScreenshot(Path.Combine(Output,name+".png"));}
}
