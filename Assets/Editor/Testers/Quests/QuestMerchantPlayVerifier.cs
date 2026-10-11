using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Overburst.Appearance;
using Overburst.Quests;
using Overburst.Persistence;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

// Own session keys, input devices and output. Never changes another tester's callbacks or shared scene.
[InitializeOnLoad]
public static class QuestMerchantPlayVerifier
{
    const string K="Overburst.QuestMerchantPlayVerifier.";
    const string Destination="../개인파일/코덱스산출/Implementation/20261011_QuestMerchant/Evidence/Play/";
    static readonly List<string> checks=new List<string>();
    static readonly Stack<IEnumerator> work=new Stack<IEnumerator>();
    static readonly Dictionary<InputActionAsset,InputDevice[]> masks=new Dictionary<InputActionAsset,InputDevice[]>();
    static string output;static int frame=-1;static double deadline;
    static InputSettings originalInput,temporaryInput;
    static Mouse mouse;static Keyboard keyboard;static Gamepad gamepad;static MouseState pointer;
    static QuestMerchantPlayVerifier()
    {
        EditorApplication.playModeStateChanged+=State;
        if(!string.IsNullOrEmpty(SessionState.GetString(K+"pending","")))EditorApplication.update+=Begin;
        if(!string.IsNullOrEmpty(SessionState.GetString(K+"return","")))EditorApplication.update+=Return;
        if(SessionState.GetBool(K+"running",false))EditorApplication.update+=Interrupted;
    }
    public static void Check(bool condition,string message)
    {
        if(!condition)throw new InvalidOperationException(message);
        checks.Add(message);
        SessionState.SetString(K+"checks",JsonConvert.SerializeObject(checks));
        Write("progress.json",new{utc=DateTime.UtcNow,checks,last=message});
    }
    static string Dir(string name,string destination){string path=Path.GetFullPath(Path.Combine(destination,name));Directory.CreateDirectory(path);return path;}
    static void Write(string name,object data)=>File.WriteAllText(Path.Combine(output,name),JsonConvert.SerializeObject(data,Formatting.Indented));
    public static void Start(string label,string fixture=null,bool inputEdges=false)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY")))throw new InvalidOperationException("External settings directory active");
        SessionState.SetBool(K+"edges",inputEdges);SessionState.EraseString(K+"checks");checks.Clear();SessionState.SetBool(K+"replay",fixture!=null);string destination=Destination;
        output=Dir(label,destination);string save=Path.Combine(output,"isolated-save");Directory.CreateDirectory(save);
        if(fixture!=null)foreach(string name in new[]{"account-A.es3","account-B.es3"})if(File.Exists(Path.Combine(fixture,name)))File.Copy(Path.Combine(fixture,name),Path.Combine(save,name),false);
        if(fixture==null)
        {
            var registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);var seed=NewAccountFactory.Create(registry);seed.appearance=new CharacterAppearanceSnapshot();AccountCharacterCommands.EnsureLegacy(seed);
            var gold=new ItemData(CurrencyItemRegistry.Get(CurrencyType.Gold),1,ItemGrade.Common,2000);gold.EnsureAcquisitionOrder();var captured=ItemSnapshotCodec.Capture(gold,registry);captured.acquisitionOrder=seed.nextAcquisitionOrder++;seed.items.Add(captured);seed.inventory[0]=captured.instanceId;
            new EasySaveAccountStore(save).Save(seed,seed.lastTransactionId);
        }
        Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY",Path.Combine(output,"isolated-settings"));

        Write("edit-before.json",new{pid=System.Diagnostics.Process.GetCurrentProcess().Id,scenes=Scenes(),input=InputSystem.settings.GetInstanceID(),startScene=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)});
        SessionState.SetString(K+"pending",output);SessionState.SetString(K+"return",output);SessionState.SetString(K+"startScene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(K+"background",Application.runInBackground);SessionState.SetFloat(K+"deadline",(float)EditorApplication.timeSinceStartup+240);
        SessionState.SetFloat(K+"returnDeadline",(float)EditorApplication.timeSinceStartup+1200);
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        EditorApplication.update-=Begin;EditorApplication.update+=Begin;
        try{IsolatedSavePlayGuard.EnterIsolatedPlay(save);}catch(Exception error){Finish(error);}
    }
    public static IEnumerator Drag(RectTransform rect,Vector2 delta)
    {
        Canvas.ForceUpdateCanvases();var position=Point(rect);
        pointer=new MouseState{position=position};yield return Frames(2);
        pointer.buttons=1;yield return Frames(2);
        pointer.position=position+delta;pointer.delta=delta;yield return Frames(1);
        pointer.delta=Vector2.zero;yield return Frames(3);
        pointer.buttons=0;yield return Frames(3);
    }
    static object[] Scenes()=>Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return (object)new{s.path,s.isDirty};}).ToArray();
    static bool OwnPlay()=>string.Equals(IsolatedSavePlayGuard.ActiveDirectory,Path.Combine(SessionState.GetString(K+"return",""),"isolated-save"),StringComparison.OrdinalIgnoreCase);
    static void Begin()
    {
        var pending=SessionState.GetString(K+"pending","");if(string.IsNullOrEmpty(pending)){EditorApplication.update-=Begin;return;}
        output=pending;
        if(EditorApplication.timeSinceStartup>SessionState.GetFloat(K+"deadline",0)){Finish(new Exception("Boot timeout"));return;}
        if(!EditorApplication.isPlaying || !OwnPlay() || PlayerInputFacade.Current==null || AppearanceCustomizationPanel.Instance==null || !TitleCharacterPanel.Instance || !PlayerContext.Instance?.CurrentActor)return;
        EditorApplication.update-=Begin;SessionState.EraseString(K+"pending");SessionState.SetBool(K+"running",true);
        checks.Clear();Application.runInBackground=true;deadline=EditorApplication.timeSinceStartup+1200;frame=-1;work.Push(Verify());EditorApplication.update+=Tick;
    }
    static void Tick()
    {
        if(Time.frameCount==frame && EditorApplication.timeSinceStartup<deadline)return;frame=Time.frameCount;EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if(!EditorApplication.isPlaying || !OwnPlay() || EditorApplication.timeSinceStartup>deadline)throw new Exception("Owned Play exit or timeout");
            while(work.Count>0){var task=work.Peek();if(!task.MoveNext()){(work.Pop() as IDisposable)?.Dispose();continue;}if(task.Current is IEnumerator nested){work.Push(nested);continue;}return;}
            Finish(null);
        }catch(Exception error){Finish(error);}
    }
    public static IEnumerator Frames(int count){for(int i=0;i<count;i++)yield return null;}
    static void SetupInput()
    {
        originalInput=InputSystem.settings;temporaryInput=Object.Instantiate(originalInput);
        SessionState.SetInt(K+"input",originalInput.GetInstanceID());SessionState.SetInt(K+"temporaryInput",temporaryInput.GetInstanceID());
        temporaryInput.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;temporaryInput.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;InputSystem.settings=temporaryInput;
        mouse=InputSystem.AddDevice<Mouse>("QuestMerchantVerifierMouse");keyboard=InputSystem.AddDevice<Keyboard>("QuestMerchantVerifierKeyboard");SessionState.SetInt(K+"mouse",mouse.deviceId);SessionState.SetInt(K+"keyboard",keyboard.deviceId);
        gamepad=InputSystem.AddDevice<Gamepad>("QuestMerchantVerifierGamepad");SessionState.SetInt(K+"gamepad",gamepad.deviceId);
        var module=EventSystem.current.GetComponent<InputSystemUIInputModule>();
        foreach(var asset in new[]{PlayerInputFacade.Current.RuntimeAsset,module.actionsAsset}.Where(a=>a!=null).Distinct()){masks[asset]=asset.devices.HasValue?asset.devices.Value.ToArray():null;asset.devices=new InputDevice[]{mouse,keyboard,gamepad};}
        SessionState.SetString(K+"masks",JsonConvert.SerializeObject(masks.Select(p=>new{asset=p.Key.GetInstanceID(),devices=p.Value?.Select(d=>d.deviceId).ToArray()})));
        pointer=new MouseState();InputSystem.onBeforeUpdate+=Push;
    }
    static void Push(){if(mouse!=null && mouse.added)InputSystem.QueueStateEvent(mouse,pointer);}
    static Vector2 Point(RectTransform r,float x=.5f,float y=.5f)=>RectTransformUtility.WorldToScreenPoint(null,r.TransformPoint(new Vector3(Mathf.Lerp(r.rect.xMin,r.rect.xMax,x),Mathf.Lerp(r.rect.yMin,r.rect.yMax,y))));
    public static IEnumerator Click(Selectable control)
    {
        Canvas.ForceUpdateCanvases();var pos=Point((RectTransform)control.transform);var data=new PointerEventData(EventSystem.current){position=pos};var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(data,hits);
        bool reached=hits.Count>0 && hits[0].gameObject.GetComponentInParent<Selectable>()==control;
        if(!reached){File.WriteAllText(Path.Combine(output,"raycast-failure.json"),JsonConvert.SerializeObject(new{control=control.name,pos=new[]{pos.x,pos.y},hits=hits.Select(h=>new{h.gameObject.name,h.sortingOrder,h.depth,canvas=h.gameObject.GetComponentInParent<Canvas>()?.name,module=h.module.name,priority=h.module.sortOrderPriority}).ToArray()},Formatting.Indented));yield return Capture("raycast-failure");}
        Check(reached,"pointer raycast reaches "+control.name);
        pointer=new MouseState{position=pos};yield return Frames(2);pointer.buttons=1;yield return Frames(2);pointer.buttons=0;yield return Frames(3);
    }
    static void Scroll(ScrollRect s,float value){s.StopMovement();Canvas.ForceUpdateCanvases();s.verticalNormalizedPosition=value;}
    public static IEnumerator Capture(string name){ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return Frames(3);}


    static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static IEnumerator Wait(Func<bool> condition,string name,int maximum=2400)
    {while(!condition()&&maximum-->0)yield return null;Check(condition(),name);}
    static IEnumerator KeyPress(Key key)
    {InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return Frames(2);InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return Frames(4);}
    static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,Private).GetValue(owner);
    static QuestRowView FirstRow(QuestMerchantUI ui)=>ui.merchantRows.GetComponentsInChildren<QuestRowView>().First(r=>r.title.text=="첫 사냥");
    static GeneralGoodsMerchantInteractable WeaponMerchant()=>Object.FindObjectsByType<GeneralGoodsMerchantInteractable>(FindObjectsSortMode.None).Single(n=>MerchantReputationService.GetStableMerchantId(n.Definition)=="merchant.main-town.weapon");
    static IEnumerator Approach(GeneralGoodsMerchantInteractable npc,PlayerActorRuntime actor)
    {ActorTeleportUtility.TeleportSafely(actor.transform,npc.transform.position+npc.transform.forward*1.35f,Quaternion.LookRotation(-npc.transform.forward));yield return Frames(10);Check(actor.GetComponent<PlayerInteractionController>().Current==npc,"live interaction resolver selects merchant");Check(QuestMerchantUI.Current.approach.activeInHierarchy,"merchant approach menu appears in live Canvas");}
    static IEnumerator OpenQuests(GeneralGoodsMerchantInteractable npc,PlayerActorRuntime actor)
    {yield return Approach(npc,actor);yield return Click(QuestMerchantUI.Current.approachButtons[2]);Check(QuestMerchantUI.Current.Mode==QuestMerchantUI.WindowMode.Merchant,"direct click opens merchant quest list");}
    static IEnumerator Verify()
    {
        yield return Frames(10);SetupInput();yield return Frames(4);
        var title=TitleCharacterPanel.Instance;yield return Click(title.start);yield return Frames(4);yield return Click(title.enter);
        yield return Wait(()=>AccountBootstrap.Ready&&WorldSessionState.IsHideout&&!PersistentSceneFlow.Instance.IsSwitching,"isolated title-to-town boot completes");yield return Frames(10);
        var ui=QuestMerchantUI.Current;var account=AccountGameplaySession.Current;var registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);var actor=PlayerContext.Instance.CurrentActor;
        Check(ui && QuestSession.Current && QuestSession.Current.IsBoundTo(account),"one quest session binds to current runtime account");
        Check(Object.FindObjectsByType<QuestInventoryStyle>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==1,"one inventory style binds to existing inventory controls");
        var npc=WeaponMerchant();yield return Approach(npc,actor);Check(ui.SelectedOption==1,"approach defaults to trade");
        pointer.scroll=new Vector2(0,-120);yield return Frames(2);pointer.scroll=Vector2.zero;yield return Frames(3);Check(ui.SelectedOption==2,"actual mouse wheel selects quest");
        yield return Click(ui.approachButtons[2]);Check(ui.Mode==QuestMerchantUI.WindowMode.Merchant&&!ui.DetailIsOpen&&GameplayInputBlocker.IsGameplayInputBlocked,"quest click opens list first and blocks combat");
        var row=FirstRow(ui);yield return Click(row.button);Check(ui.DetailIsOpen,"quest row opens additional right detail");
        Check(ui.merchantList.GetComponent<RectTransform>().rect.size==new Vector2(608,776)&&ui.merchantDetail.GetComponent<RectTransform>().anchoredPosition.x-ui.merchantList.GetComponent<RectTransform>().anchoredPosition.x==624,"two merchant panels match approved size and 16px gap");
        if(SessionState.GetBool(K+"replay",false))
        {
            Check(account.Read().quests.Count==2&&account.Read().quests.All(q=>q.state==SavedQuestState.Completed),"second isolated boot reloads both completed quests");
            Check(!ui.merchantInfo.primary.interactable,"reloaded completed quest cannot report again");
            ui.Close();yield return VerifyMenuAndPortraits(ui,account,actor);yield return KeyPress(Key.J);Check(ui.Mode==QuestMerchantUI.WindowMode.Journal&&ui.journalRows.GetComponentsInChildren<QuestRowView>().Count(r=>r.gameObject.activeInHierarchy)==2,"personal quest list reloads accepted completions only");yield return Capture("journal-reloaded");ui.Close();if(SessionState.GetBool(K+"edges",false))yield return VerifyInputEdges(npc,actor,ui,account);yield break;
        }
        Check(ui.merchantRows.GetComponentsInChildren<QuestRowView>().Single(r=>r.title.text=="미지의 지도").button.interactable==false,"prerequisite quest is visibly locked");
        yield return Capture("merchant-before-accept");yield return Click(ui.merchantInfo.primary);
        Check(account.Read().quests.Count==1&&account.Read().quests[0].state==SavedQuestState.Active,"actual accept button commits quest");
        Check(new EasySaveAccountStore(AccountBootstrap.SaveDirectory).Load().quests.Count==1,"accept button checkpoint survives fresh disk reader");
        yield return KeyPress(Key.Escape);Check(ui.Mode==QuestMerchantUI.WindowMode.Merchant&&!ui.DetailIsOpen,"first Esc closes detail and keeps list");yield return KeyPress(Key.Escape);Check(ui.Mode==QuestMerchantUI.WindowMode.None&&!GameplayInputBlocker.IsGameplayInputBlocked,"second Esc releases modal owners");
        yield return KeyPress(Key.J);Check(ui.Mode==QuestMerchantUI.WindowMode.Journal&&ui.journalRows.GetComponentsInChildren<QuestRowView>().Count(r=>r.gameObject.activeInHierarchy)==1,"J opens accepted personal list only");yield return Capture("journal-accepted");yield return Click(ui.journalInfo.primary);Check(account.Read().trackedQuestId==null,"tracking toggle checkpoints untracked state");yield return Click(ui.journalInfo.primary);Check(account.Read().trackedQuestId=="quest.main-town.weapon.first-hunt","tracking toggle restores active quest");yield return KeyPress(Key.Escape);
        yield return Approach(npc,actor);yield return Click(ui.approachButtons[0]);Check(ui.Mode==QuestMerchantUI.WindowMode.Dialogue,"direct dialogue click opens correct mode");yield return Click(ui.dialoguePrimary);Check(ui.dialoguePage.text.StartsWith("02"),"dialogue advances to second page");yield return Click(ui.dialoguePrimary);Check(!GameplayInputBlocker.IsGameplayInputBlocked,"dialogue finish releases input owners");
        yield return Approach(npc,actor);ui.SelectOption(1);yield return KeyPress(Key.F);
        var shop=Object.FindObjectsByType<ShopUI>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(s=>s.IsOpen);var trade=Field<MerchantTradeService>(shop,"tradeService");
        Check(shop.IsOpenFor(npc.Definition),"actual F opens preserved trade window");Check(!Field<GameObject>(shop,"shopPanel").transform.Find("Approved Tabs").gameObject.activeInHierarchy,"obsolete shop top tabs stay hidden");
        var inventory=Object.FindObjectsByType<InventoryUI>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single();
        var dropdown=Field<TMP_Dropdown>(inventory,"sortDropdown");
        Check(!dropdown.captionText.gameObject.activeSelf,"legacy inventory caption stays hidden after live refresh");
        var direction=Field<Button>(inventory,"sortRefreshButton");var inventoryStyle=Object.FindFirstObjectByType<QuestInventoryStyle>();
        string oldDirection=inventoryStyle.directionLabel.text;yield return Click(direction);
        Check(inventoryStyle.directionLabel.text!=oldDirection,"original inventory direction button remains clickable");
        var capacity=Object.FindFirstObjectByType<OverburstGameUI>().inventoryCapacity.rectTransform;
        Check(capacity.parent.name=="Layout"&&capacity.anchoredPosition==new Vector2(466,-96),"inventory capacity aligns with toolbar");
        yield return Capture("trade-native");
        var stock=Field<SlotUI[]>(shop,"merchantInventorySlots").First(s=>s.DisplayItem!=null&&!(s.DisplayItem.baseData is CurrencyItemData));int beforeGold=trade.GetTotalGoldAmount();var calc=trade.GetComponent<MerchantTradeValueCalculator>();
        Check(shop.HandleShopSlotClicked(stock),"existing stock slot registration path remains connected");int ask=trade.GetMerchantOfferValue();Check(ask==calc.GetBaseValue(trade.Session.MerchantOffers[0].Item)*2,"native trade offer uses reference price times two");
        var confirm=Field<Button>(shop,"confirmButton");yield return Click(confirm);yield return Frames(4);Check(trade.Session.MerchantOffers.Count==0&&trade.GetTotalGoldAmount()==beforeGold-ask,"actual confirm button exchanges item and exact gold");
        stock=Field<SlotUI[]>(shop,"merchantInventorySlots").First(s=>s.DisplayItem!=null&&!(s.DisplayItem.baseData is CurrencyItemData));shop.HandleShopSlotClicked(stock);int quoted=trade.GetMerchantOfferValue();var beforeQuote=ES3.Serialize(account.Read());
        MerchantReputationService.AddReputationExperience(npc.Definition,30);shop.Refresh();var refreshed=ES3.Serialize(account.Read());var stale=trade.ExecuteTrade();
        Check(!stale.success&&stale.failureReason==MerchantTradeFailureReason.PriceChanged&&refreshed.SequenceEqual(ES3.Serialize(account.Read())),"changed reputation price rejects stale offer without transferring items");
        Check(trade.ExecuteTrade().success,"reconfirmation executes current discounted offer");shop.Close();yield return Frames(6);
        yield return OpenQuests(npc,actor);yield return Click(FirstRow(ui).button);ui.Close();
        var portal=Object.FindFirstObjectByType<MapDungeonPortal>();Check(portal.EnterLevelOne(),"existing level-one portal enters real run");yield return Wait(()=>WorldSessionState.Phase==WorldPhase.Run&&!PersistentSceneFlow.Instance.IsSwitching,"real dungeon run activates",3600);yield return Frames(20);
        actor=PlayerContext.Instance.CurrentActor;actor.Health.SetDamageDeathPrevention(actor,true);
        string run=account.ReadRun().runId;
        var field=Object.FindObjectsByType<MapMonsterField>(FindObjectsSortMode.None).OrderBy(f=>(f.transform.position-actor.transform.position).sqrMagnitude).First();
        ActorTeleportUtility.TeleportSafely(actor.transform,field.transform.position+Vector3.up*.1f,actor.transform.rotation);
        yield return Wait(()=>field.SpawnedCount>=10,"real proximity field spawns at least ten run enemies",3600);
        for(int i=0;i<10;i++)
        {
            var enemy=Object.FindObjectsByType<EnemyRank>(FindObjectsSortMode.None).FirstOrDefault(e=>e.GradeType!=EnemyGradeType.Boss&&e.Encounter.RunId==run&&e.GetComponent<CombatHealth>()&&!e.GetComponent<CombatHealth>().IsDead);
            Check(enemy!=null,"active run enemy exists for confirmed kill "+i);var health=enemy.GetComponent<CombatHealth>();var hit=new DamageInfo(1000000,health.transform.position,actor.gameObject,suppressDefaultHitVfx:true);
            health.TakeDamage(hit);health.TakeDamage(hit);yield return Frames(2);
        }
        Check(account.Read().quests[0].objectives[0].current==10,"ten real first-death callbacks produce exactly ten quest kills");
        Check(AccountQuestCommands.IsReady(new EasySaveAccountStore(AccountBootstrap.SaveDirectory).Load().quests[0]),"ready progress checkpoints before leaving run");
        var driver=PersistentSceneFlow.Instance.GetComponent<RunLifetimeDriver>();driver.RequestAbandon();yield return Wait(()=>WorldSessionState.IsHideout&&!PersistentSceneFlow.Instance.IsSwitching,"existing abandon flow returns to town",3600);
        Check(account.Read().quests[0].objectives[0].current==10,"failed run retains accepted quest progress");actor.Health.SetDamageDeathPrevention(actor,false);
        npc=WeaponMerchant();yield return OpenQuests(npc,actor);yield return Click(FirstRow(ui).button);yield return Click(ui.merchantInfo.primary);Check(ui.ReportIsOpen,"ready quest opens report confirmation");yield return Capture("report-native");
        int rewardGold=MerchantTradeItemUtility.GetInventoryGoldAmount(PlayerAccountInventoryService.SharedInventory);var beforeReward=account.Read();yield return Click(ui.popupConfirm);var completed=account.Read();
        Check(completed.quests[0].state==SavedQuestState.Completed&&completed.trackedQuestId==null&&MerchantTradeItemUtility.GetInventoryGoldAmount(PlayerAccountInventoryService.SharedInventory)==rewardGold+100,"actual report grants gold and completes once");
        Check(completed.merchants.Single(m=>m.contentId=="merchant.main-town.weapon").reputationTotalXp==beforeReward.merchants.Single(m=>m.contentId=="merchant.main-town.weapon").reputationTotalXp+10,"actual report adds exact canonical reputation reward");
        var mapRow=ui.merchantRows.GetComponentsInChildren<QuestRowView>().Single(r=>r.title.text=="미지의 지도");Check(mapRow.button.interactable,"completed report unlocks successor row");yield return Click(mapRow.button);yield return Click(ui.merchantInfo.primary);ui.Close();
        portal=Object.FindFirstObjectByType<MapDungeonPortal>();Check(portal.EnterLevelOne(),"map acquisition quest starts real level-one run");yield return Wait(()=>WorldSessionState.Phase==WorldPhase.Run&&!PersistentSceneFlow.Instance.IsSwitching,"map search run activates",3600);actor=PlayerContext.Instance.CurrentActor;actor.Health.SetDamageDeathPrevention(actor,true);
        var mapAsset=registry.ResolvedEntries.First(e=>e.asset is MapItemData);var mapItem=new ItemData((MapItemData)mapAsset.asset,1,ItemGrade.Common);mapItem.mapState=new MapInstanceState{mapContentId=mapAsset.id,monsterThemeId=MapThemeCatalog.RollThemeId(),level=1,grade=ItemGrade.Common};mapItem.originRunId=account.ReadRun().runId;
        Check(AccountGameplaySession.AcquireWorldItem(PlayerAccountInventoryService.SharedInventory,mapItem),"actual world-item acquisition adapter accepts map");Check(account.Read().quests[1].objectives[0].current==1,"runtime projection advances map acquisition objective");
        account.ExecuteState("owned-clear-map-search",s=>AccountRunCommands.ClearBoss(s,s.run.runId,DateTime.UtcNow.Ticks));driver.RequestPortalExit();yield return Wait(()=>WorldSessionState.IsHideout&&!PersistentSceneFlow.Instance.IsSwitching,"existing extraction flow retains acquired map",3600);
        portal=Object.FindFirstObjectByType<MapDungeonPortal>();
        ActorTeleportUtility.TeleportSafely(actor.transform,portal.transform.position+Vector3.forward*1.2f,Quaternion.LookRotation(Vector3.back));yield return Frames(10);
        Check(actor.GetComponent<PlayerInteractionController>().Current==portal,"nonmerchant F still resolves real dungeon portal");yield return KeyPress(Key.F);
        var portalPanel=Object.FindFirstObjectByType<MapDungeonPortalPanel>();Check(portalPanel!=null,"actual F opens portal with owned shared inventory");yield return Frames(6);
        var mapSlot=Field<SlotUI[]>(portalPanel,"inventorySlots").First(s=>s.DisplayItem?.runtimeInstanceId==mapItem.runtimeInstanceId);
        Canvas.ForceUpdateCanvases();pointer=new MouseState{position=Point((RectTransform)mapSlot.transform)};yield return Frames(2);pointer.buttons=2;yield return Frames(2);pointer.buttons=0;yield return Frames(4);
        Check(Field<string>(portalPanel,"selectedId")==mapItem.runtimeInstanceId,"actual inventory right-click inserts acquired map");Check(account.Read().quests[1].objectives[1].current==1,"portal selection advances second objective");
        yield return Capture("portal-map-selected");yield return Click(Field<Button>(portalPanel,"enter"));yield return Wait(()=>WorldSessionState.Phase==WorldPhase.Run&&!PersistentSceneFlow.Instance.IsSwitching,"selected map dungeon activates",3600);
        Check(AccountQuestCommands.IsReady(account.Read().quests[1]),"actual mapped run activation completes third objective");Check(!Object.FindFirstObjectByType<MapDungeonPortalPanel>(),"portal panel releases owner before dungeon activation");driver.RequestAbandon();yield return Wait(()=>WorldSessionState.IsHideout&&!PersistentSceneFlow.Instance.IsSwitching,"selected map run returns to town",3600);
        actor=PlayerContext.Instance.CurrentActor;actor.Health.SetDamageDeathPrevention(actor,false);npc=WeaponMerchant();yield return OpenQuests(npc,actor);mapRow=ui.merchantRows.GetComponentsInChildren<QuestRowView>().Single(r=>r.title.text=="미지의 지도");yield return Click(mapRow.button);yield return Click(ui.merchantInfo.primary);Check(ui.popupGold.text.StartsWith("150"),"report confirmation displays selected quest reward");yield return Click(ui.popupConfirm);ui.Close();
        Check(account.FlushPendingSave(),"all final quest and trade state flushed");var disk=new EasySaveAccountStore(AccountBootstrap.SaveDirectory).Load();Check(disk.quests.Count==2&&disk.quests.All(q=>q.state==SavedQuestState.Completed),"both completed quests survive final disk reload");
        AccountInvariants.Validate(disk,registry);yield return KeyPress(Key.J);yield return Capture("journal-completed");yield return KeyPress(Key.Escape);Check(!GameplayInputBlocker.IsGameplayInputBlocked,"final gameplay input owners released");
        Write("final-account.json",disk);
    }

    static IEnumerator VerifyMenuAndPortraits(QuestMerchantUI ui,AccountGameplaySession account,PlayerActorRuntime actor)
    {
        var menu=OverburstHudMenu.Instance;
        Check(menu && menu.entries.Length==5,"HUD has five destinations with quest list");
        Check(string.Join(",",menu.entries.Select(e=>e.label))=="인벤토리,장비,스킬트리,퀘스트 목록,설정","HUD quest list precedes settings without reordering old entries");
        var before=ES3.Serialize(account.Read());
        yield return Wait(()=>menu.trigger.gameObject.activeInHierarchy && menu.trigger.interactable,"HUD trigger returns after quest modal closes",120);
        yield return Click(menu.trigger);yield return new WaitForSecondsRealtime(.3f);
        Check(menu.IsOpen && menu.panel.rect.size==new Vector2(244,278),"HUD grows upward by one 52px row");
        var quest=menu.Rows.Single(row=>row.label.text=="퀘스트 목록");
        pointer=new MouseState{position=Point((RectTransform)quest.transform)};yield return Frames(5);
        Check(quest.highlight.enabled,"Quest menu row uses existing pointer hover style");
        yield return Capture("hud-quest-menu-native");
        yield return Click(quest.button);
        Check(!menu.IsOpen && ui.Mode==QuestMerchantUI.WindowMode.Journal && GameplayInputBlocker.IsGameplayInputBlocked,"Actual HUD quest click releases menu owner and opens personal quest list");
        Check(ES3.Serialize(account.Read()).SequenceEqual(before),"Opening personal list leaves quest/save state unchanged");
        yield return Capture("hud-quest-list-native");yield return KeyPress(Key.Escape);
        Check(ui.Mode==QuestMerchantUI.WindowMode.None && !OverburstGameMenu.IsOpen && !GameplayInputBlocker.IsGameplayInputBlocked,"Escape returns input without opening pause menu");
        for(int i=0;i<2;i++)
        {
            yield return Wait(()=>menu.trigger.gameObject.activeInHierarchy && menu.trigger.interactable,"HUD trigger returns on repeat "+i,120);
            yield return Click(menu.trigger);yield return new WaitForSecondsRealtime(.2f);yield return Click(menu.Rows[3].button);
            Check(ui.Mode==QuestMerchantUI.WindowMode.Journal && !menu.IsOpen,"HUD quest entry repeats "+i);
            yield return KeyPress(Key.Escape);
        }
        foreach(var npc in Object.FindObjectsByType<GeneralGoodsMerchantInteractable>(FindObjectsSortMode.None).Where(n=>n.Definition && n.Definition.name.StartsWith("TownMerchant_")).OrderBy(n=>n.Definition.name).ToArray())
        {
            yield return Approach(npc,actor);ui.SelectOption(1);yield return KeyPress(Key.F);
            var shop=Object.FindObjectsByType<ShopUI>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(s=>s.IsOpen);
            var portrait=shop.GetComponentsInChildren<RawImage>().Single(image=>image.name=="Merchant Portrait");
            Check(shop.IsOpenFor(npc.Definition) && portrait.texture==npc.Definition.Portrait,"Actual trade uses generated portrait "+npc.Definition.name);
            Check(portrait.uvRect==new Rect(0,0,1,1) && Mathf.Approximately(portrait.material.GetFloat("_Brightness"),1),"Generated portrait uses full image and neutral material "+npc.Definition.name);
            yield return Capture("portrait-"+npc.Definition.name);yield return Click(Field<Button>(shop,"closeButton"));
        }
        yield return Approach(WeaponMerchant(),actor);
        Check(!GameplayInputBlocker.IsGameplayInputBlocked,"All portrait trade windows return input");
    }

    static IEnumerator VerifyInputEdges(GeneralGoodsMerchantInteractable npc,PlayerActorRuntime actor,QuestMerchantUI ui,AccountGameplaySession account)
    {
        var controller=actor.GetComponent<PlayerInteractionController>();
        ActorTeleportUtility.TeleportSafely(actor.transform,npc.transform.position+Vector3.forward*15,actor.transform.rotation);yield return Frames(8);
        Check(!ui.approach.activeInHierarchy&&controller.ExecuteMerchantOption(npc,2)==InteractionExecutionResult.Rejected,"range loss hides menu and rejects stale pointer target");
        yield return Approach(npc,actor);
        var blocker=new GameObject("Owned Quest Input Blocker"){hideFlags=HideFlags.DontSave};GameplayInputBlocker.Block(blocker);
        try{yield return Frames(4);yield return KeyPress(Key.J);Check(!ui.approach.activeInHierarchy&&ui.Mode==QuestMerchantUI.WindowMode.None&&controller.ExecuteMerchantOption(npc,2)==InteractionExecutionResult.Rejected,"foreign blocker rejects F/click/J without changing owners");}
        finally{GameplayInputBlocker.Unblock(blocker);if(blocker)Object.Destroy(blocker);}
        yield return Frames(4);yield return Approach(npc,actor);
        int before=controller.ExecutionCount;var first=controller.ExecuteMerchantOption(npc,0);var second=controller.ExecuteMerchantOption(npc,2);
        Check(first==InteractionExecutionResult.Succeeded&&second==InteractionExecutionResult.Rejected&&controller.ExecutionCount==before+1,"two same-frame action sources execute one merchant command");
        yield return KeyPress(Key.J);Check(ui.Mode==QuestMerchantUI.WindowMode.Dialogue,"J cannot open behind dialogue");yield return KeyPress(Key.Escape);
        Check(ui.Mode==QuestMerchantUI.WindowMode.None&&!OverburstGameMenu.IsOpen&&!GameplayInputBlocker.IsGameplayInputBlocked,"Esc closes dialogue without opening pause menu");
        yield return Approach(npc,actor);ui.SelectOption(1);InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F));yield return Frames(8);
        var shop=Object.FindObjectsByType<ShopUI>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(s=>s.IsOpen);Check(controller.ExecutionCount==before+2,"held F opens trade exactly once");
        yield return Capture("trade-final-native");
        yield return Click(Field<Button>(shop,"closeButton"));yield return Frames(8);Check(!shop.IsOpen,"held F does not reopen closed trade");InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return Frames(4);
        var inventory=Object.FindObjectsByType<InventoryUI>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single();inventory.SetVisible(true);yield return Frames(4);yield return KeyPress(Key.J);Check(ui.Mode==QuestMerchantUI.WindowMode.None,"J cannot open behind inventory");
        var dropdown=Field<TMP_Dropdown>(inventory,"sortDropdown");yield return Click(dropdown);Check(dropdown.transform.Find("Dropdown List")!=null,"original sorting dropdown opens at revised toolbar");yield return new WaitForSecondsRealtime(.3f);
        var list=dropdown.transform.Find("Dropdown List");var listRect=(RectTransform)list;Check(Mathf.Abs(listRect.rect.width-154)<.1f,"sorting popup matches 154px toolbar width");
        Check(dropdown.GetComponentInChildren<QuestUiLineIcon>(true)==null && ui.GetComponentsInChildren<QuestUiLineIcon>(true).Length>0,"approach line icons remain owned by quest UI");
        var option=list.GetComponentsInChildren<Toggle>().First();Check(Mathf.Abs(((RectTransform)option.transform).rect.height-36)<.01f,"sorting option uses 36px row height");
        yield return Capture("inventory-dropdown-native");dropdown.Hide();yield return new WaitForSecondsRealtime(.3f);
        var wallet=inventory.GetComponentInChildren<QuestUiWalletLayout>(true);
        if(!wallet)wallet=Object.FindObjectsByType<QuestUiWalletLayout>(FindObjectsInactive.Include,FindObjectsSortMode.None).First(w=>w.rightEdge==576);
        string actualGold=wallet.value.text;wallet.value.text="2,147,483,647 <size=13>G</size>";wallet.Refresh();
        Check(wallet.value.rectTransform.rect.width>=wallet.value.preferredWidth&&wallet.value.horizontalOverflow==HorizontalWrapMode.Overflow,"largest wallet value fits on one line");
        Check(Mathf.Abs(wallet.value.rectTransform.anchoredPosition.x+wallet.value.rectTransform.rect.width-wallet.rightEdge)<.01f&&Mathf.Abs(wallet.coin.anchoredPosition.x+19+10-wallet.value.rectTransform.anchoredPosition.x)<.01f,"wallet amount and coin retain right alignment and 10px gap");
        yield return Capture("inventory-wallet-large");wallet.value.text=actualGold;wallet.Refresh();inventory.SetVisible(false);
        yield return Approach(npc,actor);yield return Click(ui.approachButtons[2]);yield return Click(FirstRow(ui).button);
        var revision=account.Revision;var bytes=ES3.Serialize(account.Read());ui.merchantInfo.primary.onClick.Invoke();yield return Frames(3);Check(account.Revision==revision&&bytes.SequenceEqual(ES3.Serialize(account.Read())),"completed quest callback cannot grant duplicate rewards");
        ActorTeleportUtility.TeleportSafely(actor.transform,npc.transform.position+Vector3.forward*15,actor.transform.rotation);yield return Frames(5);Check(ui.Mode==QuestMerchantUI.WindowMode.None&&!GameplayInputBlocker.IsGameplayInputBlocked,"range loss closes both panels and releases modal owner");
        yield return Approach(npc,actor);bool wasEnabled=npc.enabled;npc.enabled=false;
        try{yield return Frames(3);Check(!ui.approach.activeInHierarchy&&controller.ExecuteMerchantOption(npc,2)==InteractionExecutionResult.Rejected,"disabled NPC rejects retained command reference");}
        finally{if(npc)npc.enabled=wasEnabled;}
        yield return Approach(npc,actor);actor.Health.TakeDamage(new DamageInfo(1000000,actor.transform.position,null,suppressDefaultHitVfx:true));yield return Frames(6);yield return KeyPress(Key.J);
        Check(actor.Health.IsDead&&!ui.approach.activeInHierarchy&&ui.Mode==QuestMerchantUI.WindowMode.None,"dead actor cannot execute merchant or personal quest UI");
    }

    static void State(PlayModeStateChange state)
    {
        if(state==PlayModeStateChange.ExitingPlayMode && work.Count>0)Finish(new Exception("Play exited during verification"));
        if(state==PlayModeStateChange.EnteredEditMode && !string.IsNullOrEmpty(SessionState.GetString(K+"return",""))){EditorApplication.update-=Return;EditorApplication.update+=Return;}
    }
    static void RestoreInput()
    {
        InputSystem.onBeforeUpdate-=Push;foreach(var p in masks)if(p.Key)p.Key.devices=p.Value;masks.Clear();SessionState.EraseString(K+"masks");
        if(mouse!=null && mouse.added)InputSystem.RemoveDevice(mouse);if(keyboard!=null && keyboard.added)InputSystem.RemoveDevice(keyboard);if(gamepad!=null&&gamepad.added)InputSystem.RemoveDevice(gamepad);mouse=null;keyboard=null;gamepad=null;
        if(originalInput){InputSystem.settings=originalInput;var type=typeof(InputSettings).Assembly.GetType("UnityEngine.InputSystem.InputSystemObject");if(type!=null)foreach(var state in Resources.FindObjectsOfTypeAll(type))(state as ISerializationCallbackReceiver)?.OnBeforeSerialize();}
        if(temporaryInput)Object.DestroyImmediate(temporaryInput);originalInput=null;temporaryInput=null;
        foreach(string field in new[]{"input","temporaryInput","mouse","keyboard","gamepad"})SessionState.EraseInt(K+field);
    }
    static void Finish(Exception error)
    {
        EditorApplication.update-=Tick;EditorApplication.update-=Begin;EditorApplication.update-=Interrupted;while(work.Count>0)(work.Pop() as IDisposable)?.Dispose();
        bool own=OwnPlay();if(own && QuestMerchantUI.Current)QuestMerchantUI.Current.Close();RestoreInput();
        Application.runInBackground=SessionState.GetBool(K+"background",Application.runInBackground);SessionState.EraseBool(K+"background");SessionState.EraseString(K+"pending");SessionState.EraseBool(K+"running");SessionState.EraseBool(K+"replay");SessionState.EraseBool(K+"edges");SessionState.EraseFloat(K+"deadline");
        output=SessionState.GetString(K+"return",output);
        if(checks.Count==0){var saved=SessionState.GetString(K+"checks","");if(!string.IsNullOrEmpty(saved))checks.AddRange(JsonConvert.DeserializeObject<List<string>>(saved));}
        try{Write("play-result.json",new{success=error==null,checks,error=error?.ToString()});}
        finally
        {
            if(EditorApplication.isPlaying && own)EditorApplication.ExitPlaymode();if(!EditorApplication.isPlayingOrWillChangePlaymode){EditorApplication.update-=Return;EditorApplication.update+=Return;}
        }
    }
    static void Interrupted()
    {
        if(EditorApplication.isCompiling || EditorApplication.isUpdating)return;
        output=SessionState.GetString(K+"return","");if(string.IsNullOrEmpty(output)){EditorApplication.update-=Interrupted;return;}if(EditorApplication.isPlaying && !OwnPlay())return;
        var json=SessionState.GetString(K+"masks","");if(!string.IsNullOrEmpty(json))foreach(var r in Newtonsoft.Json.Linq.JArray.Parse(json)){var asset=EditorUtility.InstanceIDToObject((int)r["asset"]) as InputActionAsset;if(asset)masks[asset]=r["devices"].Type==Newtonsoft.Json.Linq.JTokenType.Null?null:r["devices"].Select(id=>InputSystem.GetDeviceById((int)id)).Where(d=>d!=null).ToArray();}
        temporaryInput=EditorUtility.InstanceIDToObject(SessionState.GetInt(K+"temporaryInput",0)) as InputSettings;if(temporaryInput && InputSystem.settings==temporaryInput)originalInput=EditorUtility.InstanceIDToObject(SessionState.GetInt(K+"input",0)) as InputSettings;
        var m=InputSystem.GetDeviceById(SessionState.GetInt(K+"mouse",0));if(m is Mouse a && a.name=="QuestMerchantVerifierMouse")mouse=a;
        var b=InputSystem.GetDeviceById(SessionState.GetInt(K+"keyboard",0));if(b is Keyboard c && c.name=="QuestMerchantVerifierKeyboard")keyboard=c;
        var g=InputSystem.GetDeviceById(SessionState.GetInt(K+"gamepad",0));if(g is Gamepad p&&p.name=="QuestMerchantVerifierGamepad")gamepad=p;
        Finish(new Exception("Verification interrupted by assembly reload"));
    }
    static void Return()
    {
        output=SessionState.GetString(K+"return","");if(string.IsNullOrEmpty(output)){EditorApplication.update-=Return;return;}
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory))return;
        var env=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable);var prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","");
        foreach(var path in new[]{env,prepared})if(!string.IsNullOrEmpty(path) && !Path.GetFullPath(path).StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return;
        bool timedOut=EditorApplication.timeSinceStartup>SessionState.GetFloat(K+"returnDeadline",0);
        string scene=SessionState.GetString(K+"startScene","");var settingsDirectory=Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY");if(!string.IsNullOrEmpty(settingsDirectory)&&Path.GetFullPath(settingsDirectory).StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY",null);
        EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(scene)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(scene);IsolatedSavePlayGuard.UseRealAccount();
        SessionState.EraseString(K+"return");SessionState.EraseString(K+"startScene");SessionState.EraseString(K+"checks");SessionState.EraseFloat(K+"returnDeadline");EditorApplication.update-=Return;
        Write("return-result.json",new{reason=timedOut?"return timeout":null,success=!timedOut && !IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)),guard=IsolatedSavePlayGuard.RequiresAccountChoice,active=IsolatedSavePlayGuard.ActiveDirectory,prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""),expires=SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires",""),input=InputSystem.settings.GetInstanceID(),startScene=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),scenes=Scenes()});
    }
}
