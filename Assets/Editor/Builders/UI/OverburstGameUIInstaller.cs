using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class OverburstGameUIInstaller
{
    const string Root="Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/";
    static TMP_FontAsset font;
    static void Set(Object o,string name,Object value){var s=new SerializedObject(o);s.FindProperty(name).objectReferenceValue=value;s.ApplyModifiedPropertiesWithoutUndo();}
    static Object Get(Object o,string name)=>new SerializedObject(o).FindProperty(name).objectReferenceValue;
    static void Array(Object o,string name,Object[] values){var s=new SerializedObject(o);var p=s.FindProperty(name);p.arraySize=values.Length;for(int i=0;i<values.Length;i++)p.GetArrayElementAtIndex(i).objectReferenceValue=values[i];s.ApplyModifiedPropertiesWithoutUndo();}
    static GameObject Instance(string name,Transform parent)=> (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root+name+".prefab"),parent);
    static RectTransform Rect(Transform t,float x,float y,float width,float height){var r=(RectTransform)t;r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.localScale=Vector3.one;r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(width,height);return r;}
    static void Stretch(Transform t){var r=(RectTransform)t;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;r.localScale=Vector3.one;}
    static TextMeshProUGUI Tmp(Transform parent,string name){var g=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));g.transform.SetParent(parent,false);var t=g.GetComponent<TextMeshProUGUI>();t.font=font;t.fontSize=28;t.color=new Color(.93f,.90f,.83f);t.raycastTarget=false;return t;}
    static TextMeshProUGUI ReplaceText(Text old){var rect=old.rectTransform;var p=rect.parent;var name=old.name;var a=rect.anchorMin;var b=rect.anchorMax;var pivot=rect.pivot;var pos=rect.anchoredPosition;var size=rect.sizeDelta;var color=old.color;var value=old.text;var alignment=old.alignment;Object.DestroyImmediate(old);var t=rect.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.fontSize=28;t.color=color;t.text=value;t.raycastTarget=false;t.alignment=alignment==TextAnchor.MiddleRight?TextAlignmentOptions.MidlineRight:TextAlignmentOptions.MidlineLeft;return t;}
    static void ClearButton(Button b){b.onClick=new Button.ButtonClickedEvent();}
    static Button Button(Transform p,string name,string title,float x,float y,float w){var g=new GameObject(name,typeof(RectTransform),typeof(Image),typeof(Button));g.transform.SetParent(p,false);Rect(g.transform,x,y,w,56);g.GetComponent<Image>().color=new Color(.2f,.1f,.07f,.95f);var t=Tmp(g.transform,"Label");Stretch(t.transform);t.text=title;t.fontSize=28;t.alignment=TextAlignmentOptions.Center;return g.GetComponent<Button>();}
    static SlotUI Slot(OverburstUIItemSlotView view){var g=view.gameObject;var preview=g.GetComponent<OverburstUISlotTooltip>();if(preview)Object.DestroyImmediate(preview);view.Present(null,ItemGrade.Common);var slot=g.GetComponent<SlotUI>()??g.AddComponent<SlotUI>();Set(slot,"iconImage",g.transform.Find("Icon").GetComponent<Image>());Set(slot,"backgroundImage",g.GetComponent<Image>());return slot;}
    static void MoveControl(Component c,Transform p,float x,float y,float w,float h){if(!c)return;c.transform.SetParent(p,false);Rect(c.transform,x,y,w,h);foreach(var t in c.GetComponentsInChildren<TMP_Text>(true)){t.font=font;t.fontSize=28;}}
    [MenuItem("OVERBURST/UI/Install Approved UI In Game")]
    public static void Install(){
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit mode required");
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByName("PersistentScene");if(!scene.isLoaded)scene=EditorSceneManager.OpenScene("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity",OpenSceneMode.Additive);
        var roots=scene.GetRootGameObjects();var canvas=roots.First(x=>x.name=="PlayerInventoryCanvas");var hudCanvas=roots.First(x=>x.name=="HUDCanvas");
        if(canvas.GetComponent<OverburstGameUI>())throw new InvalidOperationException("Already installed: use targeted updates.");
        var inventory=canvas.GetComponent<InventoryUI>();font=inventory.KoreanFontAsset;
        var oldInv=(GameObject)Get(inventory,"inventoryPanel");var bridge=canvas.GetComponent<InventorySlotBridge>();
        var stash=canvas.GetComponentInChildren<StashUI>(true);var stashBridge=stash.GetComponent<StashSlotBridge>();var oldStash=(GameObject)Get(stash,"stashPanel");
        var equipmentPanel=oldInv.GetComponentInChildren<FlaskEquipmentPanelUI>(true);var bags=(SlotUI[])oldInv.GetComponentsInChildren<SlotUI>(true).Where(x=>x.name.Contains("Bag")).ToArray();
        var inv=Instance("PF_OverburstInventory_Rpg11",canvas.transform);var equip=Instance("PF_OverburstEquipment_Rpg11",canvas.transform);var storage=Instance("PF_OverburstStash_Rpg11",canvas.transform);var hud=Instance("PF_OverburstHUD_Rpg11",hudCanvas.transform);
        var game=canvas.AddComponent<OverburstGameUI>();game.inventory=inventory;game.stash=stash;game.inventoryWindow=inv.GetComponent<OverburstUIWindow>();game.equipmentWindow=equip.GetComponent<OverburstUIWindow>();game.stashWindow=storage.GetComponent<OverburstUIWindow>();game.hud=hud.transform;
        game.inventoryClose=inv.transform.Find("Header/Button (Close)").GetComponent<Button>();game.stashClose=storage.transform.Find("Header/Button (Close)").GetComponent<Button>();game.equipmentClose=equip.transform.Find("Header/Button (Close)").GetComponent<Button>();foreach(var b in new[]{game.inventoryClose,game.stashClose,game.equipmentClose})ClearButton(b);
        Set(inventory,"inventoryPanel",inv);var invSlots=inv.GetComponentsInChildren<OverburstUIItemSlotView>(true).Select(Slot).ToArray();Array(bridge,"inventorySlots",invSlots);
        var currency=ReplaceText(inv.transform.Find("Layout/Currency Value").GetComponent<Text>());Set(inventory,"goldSummaryText",currency);game.inventoryCapacity=inv.transform.Find("Layout/Capacity").GetComponent<Text>();
        inv.transform.Find("Layout/Section").gameObject.SetActive(false);
        MoveControl((Component)Get(inventory,"sortDropdown"),inv.transform,64,188,300,56);MoveControl((Component)Get(inventory,"sortRefreshButton"),inv.transform,380,188,180,56);
        game.equipmentButton=Button(inv.transform,"Equipment Button","장비",584,188,160);
        var status=(Component)Get(inventory,"sortStatusText");MoveControl(status,inv.transform,64,1394,1000,30);
        if(bags.Length>0){var bag=bags[0];bag.transform.SetParent(inv.transform,false);Rect(bag.transform,1096,1450,64,64);}
        var weapon=Slot(equip.transform.Find("Layout/Weapon Slot").GetComponent<OverburstUIItemSlotView>());Array(bridge,"weaponSlots",new Object[]{weapon});
        game.inventoryButton=equip.transform.Find("Layout/Inventory Button").GetComponent<Button>();ClearButton(game.inventoryButton);
        equipmentPanel.transform.SetParent(equip.transform,false);Stretch(equipmentPanel.transform);foreach(Transform child in equipmentPanel.transform)child.gameObject.SetActive(false);
        var picker=(GameObject)Get(equipmentPanel,"keyPicker");Rect(picker.transform,270,700,700,400);
        var flaskViews=Enumerable.Range(1,3).Select(i=>equip.transform.Find("Layout/Flask Slot "+i).GetComponent<OverburstUIItemSlotView>()).ToArray();game.flasks=flaskViews;game.flaskEquipment=equipmentPanel;
        var flaskButtons=flaskViews.Select(x=>x.GetComponent<Button>()??x.gameObject.AddComponent<Button>()).ToArray();foreach(var b in flaskButtons)ClearButton(b);
        Array(equipmentPanel,"flaskButtons",flaskButtons);Array(equipmentPanel,"flaskIcons",flaskViews.Select(x=>x.transform.Find("Icon").GetComponent<Image>()).ToArray());Array(equipmentPanel,"flaskNames",new Object[3]);Array(equipmentPanel,"flaskKeys",new Object[3]);
        for(int i=0;i<3;i++){var trigger=flaskViews[i].gameObject.AddComponent<OverburstEquippedFlaskTooltip>();trigger.index=i;var sample=flaskViews[i].GetComponent<OverburstUISlotTooltip>();if(sample)Object.DestroyImmediate(sample);flaskViews[i].Present(null,ItemGrade.Common);}
        // Keep existing stash controller identity: merchant/world references continue pointing to it.
        foreach(Transform child in oldStash.transform)child.gameObject.SetActive(false);var oldImage=oldStash.GetComponent<Image>();if(oldImage)oldImage.enabled=false;Stretch(oldStash.transform);
        Set(stash,"stashPanel",storage);var storageSlots=storage.GetComponentsInChildren<OverburstUIItemSlotView>(true).Select(Slot).ToArray();Array(stash,"stashSlots",storageSlots);Array(stashBridge,"stashSlots",storageSlots);Set(stash,"slotContainer",storageSlots[0].transform.parent);
        var sampleStash=storage.GetComponent<OverburstUIStashPreview>();if(sampleStash)Object.DestroyImmediate(sampleStash);
        var tabs=Enumerable.Range(1,3).Select(i=>storage.transform.Find("Tab Menu/Buttons Group/Tab Button ("+i+")").GetComponent<Button>()).ToArray();foreach(var b in tabs)ClearButton(b);Array(stash,"tabButtons",tabs);Array(stash,"tabButtonTexts",new Object[3]);
        MoveControl((Component)Get(stash,"sortDropdown"),storage.transform,76,1444,280,56);MoveControl((Component)Get(stash,"sortRefreshButton"),storage.transform,372,1444,180,56);MoveControl((Component)Get(stash,"storeAllButton"),storage.transform,568,1444,230,56);
        foreach(var name in new[]{"sortDropdown","sortRefreshButton","storeAllButton"})((Component)Get(stash,name)).gameObject.SetActive(true);
        storage.transform.Find("Storage Caption").gameObject.SetActive(false);game.stashCapacity=storage.transform.Find("Storage Hint").GetComponent<Text>();MoveControl((Component)Get(stash,"actionStatusText"),storage.transform,76,1394,1100,30);
        var summary=(Component)Get(stash,"currencySummaryUI");if(summary){summary.transform.SetParent(storage.transform,false);Rect(summary.transform,820,1444,510,56);summary.gameObject.SetActive(true);}
        oldInv.SetActive(false);oldStash.SetActive(true);inv.SetActive(false);storage.SetActive(false);equip.SetActive(false);
        // New shared tooltip is fed only by real item instances through TooltipManager.
        var host=new GameObject("RPG11 Game Tooltip",typeof(RectTransform),typeof(OverburstGameTooltip));host.transform.SetParent(canvas.transform,false);Stretch(host.transform);
        var tip=Instance("PF_OverburstTooltip_Rpg11",host.transform);host.GetComponent<OverburstGameTooltip>().view=tip.GetComponent<OverburstUITooltipView>();tip.SetActive(false);Set(canvas.GetComponent<TooltipManager>(),"rpgTooltip",host.GetComponent<OverburstGameTooltip>());
        InstallHud(hudCanvas,hud);
        EditorUtility.SetDirty(game);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
    }
    static void InstallHud(GameObject canvas,GameObject hud){
        var old=canvas.transform.Find("ActionSlotHud");var controller=old.GetComponent<ActionSlotHudUI>();var slots=hud.transform.Find("Action Bar/Main Slots/Grid").GetComponentsInChildren<OverburstUIItemSlotView>(true);var live=new ActionSlotHudSlotUI[slots.Length];
        for(int i=0;i<slots.Length;i++){var s=slots[i];s.Present(null,ItemGrade.Common,((i+1)%10).ToString());var sample=s.GetComponent<OverburstUISlotTooltip>();if(sample)Object.DestroyImmediate(sample);var a=s.gameObject.AddComponent<ActionSlotHudSlotUI>();Set(a,"itemIcon",s.transform.Find("Icon").GetComponent<Image>());Set(a,"slotBackground",s.GetComponent<Image>());Set(a,"legacyKeyText",s.GetComponentInChildren<Text>(true));var count=Tmp(s.transform,"CountText");Rect(count.transform,4,4,70,22);count.fontSize=14;Set(a,"countText",count);var cd=Tmp(s.transform,"CooldownText");Stretch(cd.transform);cd.fontSize=20;cd.alignment=TextAlignmentOptions.Center;Set(a,"cooldownText",cd);var overlay=new GameObject("CooldownOverlay",typeof(RectTransform),typeof(Image));overlay.transform.SetParent(s.transform,false);ActionSlotHudSlotUI.ApplyCooldownOverlayDefaults(overlay.GetComponent<Image>());overlay.SetActive(false);Set(a,"cooldownOverlay",overlay.GetComponent<Image>());live[i]=a;}
        Array(controller,"quickSlots",live);foreach(Transform child in old)child.gameObject.SetActive(false);
        var oldStatus=canvas.transform.Find("PlayerStatusRoot");var buffs=oldStatus.GetComponentInChildren<BuffBarUI>(true);if(buffs){buffs.transform.SetParent(hud.transform,false);Rect(buffs.transform,420,850,400,42);}
        oldStatus.gameObject.SetActive(false);hud.transform.Find("Buffs • Above Health").gameObject.SetActive(false);
        var tracker=hud.transform.Find("Quest Tracker");tracker.gameObject.SetActive(true);
        var objectiveView=tracker.gameObject.AddComponent<OverburstObjectiveTracker>();
        objectiveView.Configure(
            tracker.Find("Header/Button (Toggle)").GetComponent<Toggle>(),
            tracker.Find("Body").gameObject,
            tracker.Find("Body/Quests Group/Quest (1)/Quest Name Text").GetComponent<Text>(),
            tracker.Find("Body/Quests Group/Quest (1)/Objectives").gameObject,
            (RectTransform)tracker.Find("Objective Backdrop"));
        var xp=hud.transform.Find("Action Bar/XP Bar");if(xp)xp.gameObject.SetActive(true);
        // Keep the real minimap controller and marker projection; replace only its frame.
        var map=canvas.transform.Find("WorldMinimapRoot");var frame=hud.transform.Find("Circular Minimap");frame.gameObject.SetActive(false);
        var realFrame=map.Find("MinimapView/CircleFrame").GetComponent<Image>();var decorative=frame.GetComponentsInChildren<Image>(true).FirstOrDefault(x=>x.name=="Frame");if(decorative)realFrame.sprite=decorative.sprite;
    }
    public static void PolishIntegration(){
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByName("PersistentScene");var canvas=scene.GetRootGameObjects().First(x=>x.name=="PlayerInventoryCanvas");var game=canvas.GetComponent<OverburstGameUI>();var hud=scene.GetRootGameObjects().First(x=>x.name=="HUDCanvas");
        hud.transform.Find("PlayerHealthHud").gameObject.SetActive(false);hud.transform.Find("EnemyTargetHpHud").gameObject.SetActive(false);
        canvas.GetComponent<Canvas>().sortingOrder=100;
        font=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"PF_OverburstTooltip_Rpg11.prefab").GetComponent<OverburstUITooltipView>().Body.font;
        var so=new SerializedObject(game.inventory);so.FindProperty("compactGoldSummary").boolValue=true;so.ApplyModifiedPropertiesWithoutUndo();
        ((Component)Get(game.inventory,"sortStatusText")).gameObject.SetActive(false);
        var sort=(Button)Get(game.inventory,"sortRefreshButton");var text=sort.GetComponentInChildren<TextMeshProUGUI>(true);Stretch(text.transform);text.alignment=TextAlignmentOptions.Center;text.fontSize=28;
        var caption=game.inventoryWindow.transform.Find("Layout/Currency Value").GetComponent<TextMeshProUGUI>();caption.font=font;caption.fontSize=36;caption.alignment=TextAlignmentOptions.MidlineRight;caption.textWrappingMode=TextWrappingModes.NoWrap;
        var bridge=canvas.GetComponent<InventorySlotBridge>();var bags=new SerializedObject(bridge).FindProperty("bagSlots");if(bags.arraySize>0&&bags.GetArrayElementAtIndex(0).objectReferenceValue){((SlotUI)bags.GetArrayElementAtIndex(0).objectReferenceValue).gameObject.SetActive(false);}
        var parent=game.equipmentWindow.transform.Find("Layout");var bagInstance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"Slots/PF_OverburstItemSlot_Rpg11.prefab"),parent);bagInstance.name="Bag Slot";var bagTarget=(RectTransform)parent.Find("Slot • 가방");Rect(bagInstance.transform,bagTarget.anchoredPosition.x,-bagTarget.anchoredPosition.y,84,84);bagTarget.gameObject.SetActive(false);bagInstance.transform.localScale=Vector3.one*2;var bag=Slot(bagInstance.GetComponent<OverburstUIItemSlotView>());Array(bridge,"bagSlots",new Object[]{bag});Array(game.inventory,"bagSlots",new Object[]{bag});
        // Frame and cardinal details are shared with the approved circular mockup.
        var map=hud.transform.Find("WorldMinimapRoot/MinimapView");map.Find("CircleFrame").gameObject.SetActive(false);
        var source=game.hud.Find("Circular Minimap");foreach(Transform child in source)if(child.name.Contains("Rim")||child.name=="Cardinal Ornament"){
            var copy=Object.Instantiate(child.gameObject,map,false);var r=(RectTransform)copy.transform;r.anchoredPosition*=.5f;r.sizeDelta*=.5f;r.localScale=Vector3.one;copy.SetActive(true);foreach(var im in copy.GetComponentsInChildren<Image>())im.raycastTarget=false;
        }
        // Real enemy bars retain their pooling, visibility and elemental-status policies.
        foreach(var grade in new[]{"Normal","Elite"}){string path="Assets/ProjectOverburst/Resources/UI/World/MonsterHpBars/PF_EnemyHpBar_"+grade+".prefab";var root=PrefabUtility.LoadPrefabContents(path);var view=root.GetComponent<EnemyHpBarView>();var oldBar=(RectTransform)Get(view,"barRoot");foreach(Transform child in oldBar)if(!child.name.Contains("Elemental"))child.gameObject.SetActive(false);var bg=oldBar.GetComponent<Image>();if(bg)bg.enabled=false;
            var bar=Instance("PF_OverburstEnemyHealthBar_Rpg11",oldBar);var r=(RectTransform)bar.transform;r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=Vector2.zero;r.localScale=Vector3.one*(grade=="Elite"?.4f:.34f);Set(view,"rpgView",bar.GetComponent<OverburstEnemyHealthBarView>());oldBar.sizeDelta=new Vector2(grade=="Elite"?240:204,38);
            foreach(var t in bar.GetComponentsInChildren<Text>(true)){t.fontSize=t.transform.parent.name=="Level Frame"?36:32;}
            OverburstUIWorkshopBuilder.CenterLevelNumber(bar.transform.Find("Level Frame/Text").GetComponent<Text>());
            PrefabUtility.SaveAsPrefabAsset(root,path);PrefabUtility.UnloadPrefabContents(root);
        }
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
    }
    public static void RefinePopups(){
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByName("PersistentScene");var canvas=scene.GetRootGameObjects().First(x=>x.name=="PlayerInventoryCanvas");var game=canvas.GetComponent<OverburstGameUI>();font=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"PF_OverburstTooltip_Rpg11.prefab").GetComponent<OverburstUITooltipView>().Body.font;
        var picker=(GameObject)Get(game.flaskEquipment,"keyPicker");foreach(Transform old in picker.transform)old.gameObject.SetActive(false);
        Rect(picker.transform,410,600,700,470);var title=Tmp(picker.transform,"RPG11 Title");Rect(title.transform,36,24,540,48);title.fontSize=34;title.text="퀵슬롯 번호 선택";title.color=new Color(.86f,.7f,.43f);
        var buttons=new Button[10];for(int i=0;i<10;i++){buttons[i]=Button(picker.transform,"RPG11 Key "+i,((i+1)%10)+"번",36+(i%2)*324,100+(i/2)*66,304);buttons[i].GetComponentInChildren<TMP_Text>().fontSize=30;}
        var close=Button(picker.transform,"RPG11 Close","×",612,22,52);Array(game.flaskEquipment,"keyButtons",buttons);Array(game.flaskEquipment,"keyLabels",buttons.Select(x=>x.GetComponentInChildren<TMP_Text>()).ToArray());Set(game.flaskEquipment,"pickerCloseButton",close);
        Rect(game.inventoryButton.transform,1264,1432,200,88);game.inventoryButton.GetComponentInChildren<Text>().fontSize=26;
        // Popup is authored as a reusable prefab while the controller retains all gameplay policy.
        var blocker=canvas.transform.Find("InventoryContextBlocker");PrefabUtility.SaveAsPrefabAssetAndConnect(blocker.gameObject,Root+"PF_OverburstInventoryContext_Rpg11.prefab",InteractionMode.AutomatedAction);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
    }
    public static void RefineToolbarAndSplit(){
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByName("PersistentScene");var canvas=scene.GetRootGameObjects().First(x=>x.name=="PlayerInventoryCanvas");var game=canvas.GetComponent<OverburstGameUI>();font=game.inventory.KoreanFontAsset;
        foreach(var window in new[]{game.inventoryWindow,game.stashWindow}){
            foreach(var dropdown in window.GetComponentsInChildren<TMP_Dropdown>(true)){
                if(dropdown.targetGraphic)dropdown.targetGraphic.color=new Color(.18f,.13f,.09f,1);
                foreach(var t in dropdown.GetComponentsInChildren<TMP_Text>(true)){t.font=font;t.fontSize=28;t.color=new Color(.9f,.86f,.75f);}
            }
            foreach(var button in window.GetComponentsInChildren<Button>(true)){
                if(button.name.Contains("Sort")||button.name.Contains("StoreAll")){
                    if(button.targetGraphic)button.targetGraphic.color=Color.white;
                    var c=button.colors;c.normalColor=new Color(.2f,.14f,.09f,1);c.highlightedColor=new Color(.4f,.28f,.14f,1);c.pressedColor=new Color(.5f,.35f,.16f,1);button.colors=c;
                }
            }
        }
        var stash=game.stash;MoveControl((Component)Get(stash,"sortDropdown"),game.stashWindow.transform,76,1400,280,56);MoveControl((Component)Get(stash,"sortRefreshButton"),game.stashWindow.transform,372,1400,180,56);MoveControl((Component)Get(stash,"storeAllButton"),game.stashWindow.transform,568,1400,230,56);
        var status=(TMP_Text)Get(stash,"actionStatusText");Rect(status.transform,830,1400,860,56);status.fontSize=24;status.alignment=TextAlignmentOptions.MidlineRight;
        var summary=(Component)Get(stash,"currencySummaryUI");Rect(summary.transform,76,1472,1330,44);foreach(var t in summary.GetComponentsInChildren<TMP_Text>(true)){t.font=font;t.fontSize=28;t.enableAutoSizing=false;}foreach(var im in summary.GetComponentsInChildren<Image>(true))im.color=Color.clear;
        var menu=canvas.GetComponent<InventoryContextMenuController>();var split=(RectTransform)Get(menu,"splitPopupRoot");split.sizeDelta=new Vector2(280,180);
        Rect(split.Find("Title"),18,16,244,26);Rect(split.Find("HintText"),18,46,244,24);Rect(split.Find("InputRow"),18,80,244,36);Rect(split.Find("ButtonRow"),18,128,244,36);
        split.Find("Title").GetComponent<TMP_Text>().color=new Color(.86f,.7f,.43f);
        foreach(var t in split.GetComponentsInChildren<TMP_Text>(true)){t.font=font;t.fontSize=16;t.enableAutoSizing=false;t.alignment=TextAlignmentOptions.Center;}
        PrefabUtility.ApplyPrefabInstance(canvas.transform.Find("InventoryContextBlocker").gameObject,InteractionMode.AutomatedAction);
        string path="Assets/ProjectOverburst/01_Core/Settings/Input/InputSystem_Actions.inputactions";var input=AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);var map=input.FindActionMap("UI",true);var action=map.FindAction("Equipment")??map.AddAction("Equipment",InputActionType.Button);if(action.bindings.Count==0)action.AddBinding("<Keyboard>/c");System.IO.File.WriteAllText(path,input.ToJson());AssetDatabase.ImportAsset(path);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
    }
    public static void FinishControls(){
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByName("PersistentScene");var canvas=scene.GetRootGameObjects().First(x=>x.name=="PlayerInventoryCanvas");var game=canvas.GetComponent<OverburstGameUI>();
        font=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"PF_OverburstTooltip_Rpg11.prefab").GetComponent<OverburstUITooltipView>().Body.font;
        Set(game.inventory,"koreanFontAsset",font);Set(game.stash,"koreanFontAsset",font);
        var menu=canvas.GetComponent<InventoryContextMenuController>();var menuRect=(RectTransform)Get(menu,"menuRoot");
        var buttons=menuRect.GetComponentsInChildren<Button>(true).ToList();while(buttons.Count<12){var b=Object.Instantiate(buttons[0],menuRect);b.name="InventoryContextButton_"+(buttons.Count+1).ToString("00");buttons.Add(b);}
        Array(menu,"menuButtons",buttons.ToArray());Array(menu,"menuButtonTexts",buttons.Select(x=>x.GetComponentInChildren<TextMeshProUGUI>(true)).ToArray());
        var frame=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"PF_OverburstTooltip_Rpg11.prefab").GetComponent<Image>().sprite;
        foreach(var panel in new[]{menuRect,(RectTransform)Get(menu,"splitPopupRoot"),(RectTransform)((GameObject)Get(game.flaskEquipment,"keyPicker")).transform}){
            var image=panel.GetComponent<Image>();if(image){image.sprite=frame;image.type=Image.Type.Sliced;image.color=Color.white;}
            foreach(var t in panel.GetComponentsInChildren<TMP_Text>(true)){t.font=font;t.fontSize=16;t.color=new Color(.93f,.90f,.83f);t.fontStyle=FontStyles.Normal;}
            foreach(var b in panel.GetComponentsInChildren<Button>(true)){var colors=b.colors;colors.normalColor=new Color(.19f,.14f,.10f,.95f);colors.highlightedColor=new Color(.40f,.30f,.18f,1);colors.pressedColor=new Color(.55f,.4f,.22f,1);colors.disabledColor=new Color(.12f,.12f,.12f,.7f);b.colors=colors;}
        }
        var layout=menuRect.GetComponent<VerticalLayoutGroup>();layout.padding=new RectOffset(14,14,14,14);layout.spacing=3;
        foreach(var b in buttons){var l=b.GetComponent<LayoutElement>();l.preferredHeight=38;l.minHeight=38;var t=b.GetComponentInChildren<TextMeshProUGUI>(true);t.alignment=TextAlignmentOptions.MidlineLeft;t.margin=new Vector4(12,0,12,0);}
        var picker=(GameObject)Get(game.flaskEquipment,"keyPicker");var so=new SerializedObject(game.flaskEquipment);var keys=so.FindProperty("keyButtons");var existing=Enumerable.Range(0,keys.arraySize).Select(i=>(Button)keys.GetArrayElementAtIndex(i).objectReferenceValue).ToList();
        while(existing.Count<10){var b=Object.Instantiate(existing[0],existing[0].transform.parent);b.name="QuickKey_"+(existing.Count+1);existing.Add(b);}
        Array(game.flaskEquipment,"keyButtons",existing.ToArray());Array(game.flaskEquipment,"keyLabels",existing.Select(x=>x.GetComponentInChildren<TMP_Text>(true)).ToArray());
        // Ten choices fit a two-column picker at the same typography scale as the window.
        var parent=existing[0].transform.parent;var grid=parent.GetComponent<GridLayoutGroup>();if(grid){grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=2;grid.cellSize=new Vector2(310,48);grid.spacing=new Vector2(10,6);}
        foreach(var b in existing)foreach(var t in b.GetComponentsInChildren<TMP_Text>(true)){t.font=font;t.fontSize=28;}
        string path="Assets/ProjectOverburst/01_Core/Settings/Input/InputSystem_Actions.inputactions";var input=AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);var map=input.FindActionMap("Gameplay",true);
        for(int i=8;i<=10;i++){var a=map.FindAction("QuickSlot"+i)??map.AddAction("QuickSlot"+i,InputActionType.Button);if(a.bindings.Count==0){a.AddBinding("<Keyboard>/"+(i%10));a.AddBinding("<Keyboard>/numpad"+(i%10));}}
        var equipment=map.FindAction("Equipment")??map.AddAction("Equipment",InputActionType.Button);if(equipment.bindings.Count==0)equipment.AddBinding("<Keyboard>/c");System.IO.File.WriteAllText(path,input.ToJson());AssetDatabase.ImportAsset(path);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
    }
}
