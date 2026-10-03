using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.Persistence;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Product interaction -> equipment pointer source -> transfer button -> durable account command.
[InitializeOnLoad]
public static class ElementGemTransferPlayVerifier
{
    const string Key = "Overburst.ElementGemTransferVerifier.";
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    static ElementGemTransferPlayVerifier() { EditorApplication.update += Tick; }
    static object Get(object value, string field) => value.GetType().GetField(field, Fields).GetValue(value);
    static void Set(object value, string field, object data) => value.GetType().GetField(field, Fields).SetValue(value, data);
    static void SetWorldPhase(WorldPhase phase) => typeof(WorldSessionState).GetMethod("SetPhase", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { phase });
    static string Hash(string path) { using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    static void Save(string output, string file, object value) => File.WriteAllText(Path.Combine(output,file), JsonConvert.SerializeObject(value,Formatting.Indented));

    public static string Queue(string directory)
    {
        directory=IsolatedSavePlayGuard.ValidateDirectory(directory); Directory.CreateDirectory(directory);
        if (!string.IsNullOrEmpty(SessionState.GetString(Key+"output",""))) throw new InvalidOperationException("Transfer verifier already pending.");
        SessionState.SetString(Key+"output",directory); SessionState.SetInt(Key+"stage",1);
        SessionState.SetFloat(Key+"deadline",(float)EditorApplication.timeSinceStartup+300);
        return "Queued isolated equipped-gem transfer verification.";
    }

    public static void CancelWaiting()
    {
        if(SessionState.GetInt(Key+"stage",0)!=1) throw new InvalidOperationException("Only an unstarted verification can be cancelled.");
        ClearPending();
    }

    public static void RetryReturn()
    {
        string output=SessionState.GetString(Key+"output","");
        if(string.IsNullOrEmpty(output) || !File.Exists(Path.Combine(output,"BeforePlay.json"))) throw new InvalidOperationException("No owned Play return to resume.");
        SessionState.SetInt(Key+"stage",3);
        SessionState.SetFloat(Key+"deadline",(float)EditorApplication.timeSinceStartup+60);
    }

    static void ClearPending()
    {
        SessionState.EraseString(Key+"output");SessionState.EraseString(Key+"start");SessionState.EraseInt(Key+"stage");SessionState.EraseFloat(Key+"deadline");
    }

    static void Tick()
    {
        string output=SessionState.GetString(Key+"output",""); if(string.IsNullOrEmpty(output))return;
        int stage=SessionState.GetInt(Key+"stage",0); if(stage==4)return;
        if(EditorApplication.timeSinceStartup>SessionState.GetFloat(Key+"deadline",0))
        {
            Save(output,"LifecycleTimeout.json",new { status="BLOCKED",stage });
            if(stage==2)
            {
                SessionState.SetInt(Key+"stage",3);
                SessionState.SetFloat(Key+"deadline",(float)EditorApplication.timeSinceStartup+60);
                if(EditorApplication.isPlaying && string.Equals(AccountBootstrap.SaveDirectory,Path.Combine(output,"IsolatedAccount"),StringComparison.OrdinalIgnoreCase)) EditorApplication.ExitPlaymode();
            }
            else if(stage==1) ClearPending();
            else SessionState.SetInt(Key+"stage",4);
            return;
        }
        if(EditorApplication.isCompiling || EditorApplication.isUpdating)return;
        if(stage==1)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
                || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","")))return;
            string start=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene);
            var real=Path.Combine(Application.persistentDataPath,"Account");
            var files=Directory.Exists(real)?Directory.GetFiles(real).OrderBy(p=>p).Select(p=>new {path=p,hash=Hash(p)}).ToArray():null;
            Save(output,"BeforePlay.json",new { pid=System.Diagnostics.Process.GetCurrentProcess().Id,scenes=HideoutCatalogLayoutBuilder.EditorSnapshot(),start,
                optionsEnabled=EditorSettings.enterPlayModeOptionsEnabled,options=EditorSettings.enterPlayModeOptions.ToString(),actualAccountFiles=files });
            SessionState.SetString(Key+"start",start); SessionState.SetInt(Key+"stage",2);
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
            try { IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output,"IsolatedAccount")); }
            catch(Exception error) { Save(output,"Play.json",new { status="FAIL",failure=error.ToString() }); SessionState.SetInt(Key+"stage",3); }
            return;
        }
        if(stage==2)
        {
            if(!EditorApplication.isPlaying)
            {
                if(!EditorApplication.isPlayingOrWillChangePlaymode) { Save(output,"Play.json",new {status="FAIL",failure="Owned Play cancelled before verification."}); SessionState.SetInt(Key+"stage",3); }
                return;
            }
            if(!string.Equals(AccountBootstrap.SaveDirectory,Path.Combine(output,"IsolatedAccount"),StringComparison.OrdinalIgnoreCase))return;
            if(!AccountBootstrap.Ready || PersistentSceneFlow.Instance==null || PersistentSceneFlow.Instance.IsSwitching)return;
            SessionState.SetInt(Key+"stage",3);
            try { Verify(output); }
            catch(Exception error) { Save(output,"Play.json",new {status="FAIL",failure=error.ToString()}); }
            finally { EditorApplication.ExitPlaymode(); }
            return;
        }
        if(stage==3 && !EditorApplication.isPlayingOrWillChangePlaymode
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","")))
        {
            try { Return(output); }
            catch(Exception error) { Save(output,"ReturnFailure.json",new {status="BLOCKED",failure=error.ToString()});SessionState.SetInt(Key+"stage",4); }
        }
    }

    static void Return(string output)
    {
        var baseline=JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(File.ReadAllText(Path.Combine(output,"BeforePlay.json")));
        string start=SessionState.GetString(Key+"start","");
        EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(start)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
        IsolatedSavePlayGuard.UseRealAccount();
        bool real=baseline["actualAccountFiles"] is Newtonsoft.Json.Linq.JArray files ? files.All(f=>File.Exists((string)f["path"]) && Hash((string)f["path"])==(string)f["hash"]) : !Directory.Exists(Path.Combine(Application.persistentDataPath,"Account"));
        string before=HideoutCatalogLayoutBuilder.EditorSnapshot(); int desired=AssetDatabase.DesiredWorkerCount,standby=EditorUserSettings.standbyImportWorkerCount;
        try { EditorUtility.UnloadUnusedAssetsImmediate(true);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();AssetDatabase.DesiredWorkerCount=0;EditorUserSettings.standbyImportWorkerCount=0;AssetDatabase.ForceToDesiredWorkerCount(); }
        finally { AssetDatabase.DesiredWorkerCount=desired;EditorUserSettings.standbyImportWorkerCount=standby; }
        bool scenes=before==(string)baseline["scenes"] && before==HideoutCatalogLayoutBuilder.EditorSnapshot();
        bool options=EditorSettings.enterPlayModeOptionsEnabled==(bool)baseline["optionsEnabled"] && EditorSettings.enterPlayModeOptions.ToString()==(string)baseline["options"];
        bool normal=IsolatedSavePlayGuard.CanEnter(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""),0,EditorApplication.timeSinceStartup,IsolatedSavePlayGuard.RequiresAccountChoice);
        ClearPending();
        Save(output,"ReturnCleanup.json",new { status=scenes&&options&&real&&normal?"PASS":"FAIL",scenes,options,real,normal,
            choice=IsolatedSavePlayGuard.RequiresAccountChoice,env=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),active=IsolatedSavePlayGuard.ActiveDirectory,
            prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""),expiry=SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires",""),pending=SessionState.GetString(Key+"output",""),
            desiredBefore=desired,desiredAfter=AssetDatabase.DesiredWorkerCount,standbyBefore=standby,standbyAfter=EditorUserSettings.standbyImportWorkerCount,
            startScene=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),utc=DateTime.UtcNow });
    }

    static void Verify(string output)
    {
        var checks=new List<string>(); var observations=new List<object>(); var expectedErrors=new List<string>(); var unexpectedErrors=new List<string>();
        void Check(bool value,string label) { if(!value)throw new InvalidOperationException(label);checks.Add(label); }
        void Log(string message,string stack,LogType type)
        {
            if(type!=LogType.Error && type!=LogType.Exception)return;
            if(message.Contains("GEM_TRANSFER_TEST:before-write"))expectedErrors.Add(message);else unexpectedErrors.Add(message+"\n"+stack);
        }
        var original=AccountGameplaySession.Current; var account=PlayerAccountInventoryService.Instance;
        original.FlushPendingSave();
        var registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var baseline=AccountGameplayProjection.Capture(original.Read(),account,registry);
        var actor=PlayerContext.Instance.CurrentActor; var equipment=actor.Equipment;
        var game=UnityEngine.Object.FindFirstObjectByType<OverburstGameUI>();
        Check(game!=null && EventSystem.current!=null,"actual equipment UI and event system ready");
        Check(!UnityEngine.Object.FindObjectsByType<OverburstRunUi>(FindObjectsSortMode.None).Any(p=>p.IsOpen),"no other run panel open");
        var data=Resources.LoadAll<ElementGemItemData>("Items/ElementGems").First(g=>g.element==WeaponElement.Fire && g.fixedGrade==ItemGrade.Common);
        var phase=WorldSessionState.Phase; var random=UnityEngine.Random.state; bool equipmentVisible=game.equipmentWindow.gameObject.activeSelf;
        Application.logMessageReceived+=Log;
        string failure=null;
        try
        {
            foreach(string scenario in new[]{"success","prior","stash-full","save-failed","inventory-full"})
            {
                var initial=ItemSnapshotCodec.CopyValues(baseline);
                var store=new EasySaveAccountStore(Path.Combine(output,"Cases",scenario)); store.Save(initial,"baseline-"+scenario);
                var session=new AccountGameplaySession(account,registry,store,initial);session.Attach();
                MapDungeonEventDirector director=null; OverburstRunUi panel=null;
                var overlays=new List<GameObject>();
                try
                {
                    string run="gem-transfer-"+scenario; var gem=new ItemData(data,1,data.fixedGrade);
                    if(scenario=="prior")Check(account.Inventory.AddItem(gem),scenario+" prior gem in inventory");
                    Check(session.ExecuteState(run+"-entry",s=>{
                        AccountRunCommands.PrepareEntry(s,run,new MapInstanceState{level=1,grade=ItemGrade.Common},null,true);AccountRunCommands.Activate(s,run);
                        if(scenario!="prior")AccountRunCommands.Acquire(s,run,ItemSnapshotCodec.Capture(gem,registry));
                    }),scenario+" active run / acquire");
                    SetWorldPhase(WorldPhase.Run);
                    Check(ElementGemEquipmentService.EquipFromInventorySlot(account.Inventory.FindFirstMatchingItemIndex(gem)),scenario+" real gem equip command");
                    Check(equipment.EquippedElementGem?.runtimeInstanceId==gem.runtimeInstanceId && !session.Read().inventory.Contains(gem.runtimeInstanceId),scenario+" scalar equipped ownership");
                    if(scenario=="inventory-full" || scenario=="stash-full")
                        Check(session.ExecuteState(run+"-fill",s=>{
                            if(scenario=="inventory-full")
                            {
                                for(int i=0;i<s.unlockedSlots;i++)if(string.IsNullOrEmpty(s.inventory[i])) { var item=ItemSnapshotCodec.Capture(new ItemData(data,1,data.fixedGrade),registry);s.items.Add(item);s.inventory[i]=item.instanceId; }
                            }
                            else
                            {
                                foreach(var tab in s.stashTabs)for(int i=0;i<tab.slots.Count;i++)if(string.IsNullOrEmpty(tab.slots[i])) { var item=ItemSnapshotCodec.Capture(new ItemData(data,1,data.fixedGrade),registry);s.items.Add(item);tab.slots[i]=item.instanceId; }
                            }
                        }),scenario+" full container fixture");
                    if(scenario=="inventory-full")Check(account.Inventory.FindFirstEmptySlot()<0,"inventory-full fixture has no unequip space");

                    director=new GameObject("GemTransferTest_Director").AddComponent<MapDungeonEventDirector>();director.enabled=false;
                    Set(director,"encounter",new EncounterContext(run,1,ItemGrade.Common));
                    var transfer=(MapRunTransferObject)typeof(MapDungeonEventDirector).GetMethod("CreateTransferObject",Fields).Invoke(director,new object[]{"transfer-"+scenario,actor.transform.position});
                    game.equipmentWindow.gameObject.SetActive(true);
                    Check(transfer.TryInteract(actor)==InteractionExecutionResult.Succeeded,scenario+" public object interaction opens transfer");
                    panel=(OverburstRunUi)Get(director,"ui");Check(panel!=null && panel.IsTransferOpen,scenario+" product transfer panel open");
                    overlays.AddRange((List<GameObject>)Get(panel,"equipmentSources"));
                    var source=overlays.Select(g=>g.GetComponent<RunTransferEquipmentSource>()).Single(p=>p.Item?.runtimeInstanceId==gem.runtimeInstanceId);
                    Check(source.Item==equipment.EquippedElementGem,scenario+" equipment source resolves live scalar gem");
                    var confirm=(Button)Get(panel,"confirm");var status=(TMP_Text)Get(panel,"transferStatus");
                    string before=JsonConvert.SerializeObject(session.Read());string quality=JsonConvert.SerializeObject(session.Read().items.Single(i=>i.instanceId==gem.runtimeInstanceId).gemState);
                    source.OnPointerClick(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left});
                    if(scenario=="prior")
                    {
                        Check(!confirm.interactable && Get(panel,"selectedId")==null && status.text.Contains("이번 판"),"prior gem rejected by real equipment click");
                        confirm.onClick.Invoke();
                        Check(JsonConvert.SerializeObject(session.Read())==before && !transfer.Used && transfer.gameObject.activeSelf,"prior rejection preserves account and object");
                        observations.Add(new {scenario,status=status.text}); continue;
                    }
                    Check(confirm.interactable && (string)Get(panel,"selectedId")==gem.runtimeInstanceId,scenario+" equipment click stages equipped run gem");
                    Check(((TMP_Text)Get(panel,"transferNote")).text.Contains("장착 중"),scenario+" equipped presentation note");
                    if(scenario=="save-failed")store.FaultInjector=p=>{if(p=="before-write")throw new IOException("GEM_TRANSFER_TEST:before-write");};
                    confirm.onClick.Invoke();
                    if(scenario=="stash-full" || scenario=="save-failed")
                    {
                        string expected=scenario=="stash-full"?"창고 공간":"저장에 실패";
                        Check(status.text.Contains(expected) && confirm.interactable,scenario+" UI explains failure and allows retry");
                        Check(JsonConvert.SerializeObject(session.Read())==before && JsonConvert.SerializeObject(store.Load())==before,scenario+" account memory and durable save unchanged");
                        Check(equipment.EquippedElementGem?.runtimeInstanceId==gem.runtimeInstanceId && !transfer.Used && transfer.gameObject.activeSelf,scenario+" equipped gem and transfer object preserved");
                        observations.Add(new {scenario,failureMessage=status.text,preserved=true});
                        store.FaultInjector=null;
                        if(scenario=="stash-full")Check(session.ExecuteState(run+"-free-stash",s=>{string id=s.stashTabs[0].slots[0];s.stashTabs[0].slots[0]=null;s.items.RemoveAll(i=>i.instanceId==id);}),"stash-full release one owned fixture slot");
                        confirm.onClick.Invoke();
                    }
                    var committed=session.Read(); var saved=store.Load();
                    Check(transfer.Used && !transfer.gameObject.activeSelf && !confirm.interactable && status.text.Contains("전송 완료"),scenario+" success spends object once");
                    Check(committed.elementalGemInstanceId==null && equipment.EquippedElementGem==null && committed.stashTabs.Sum(t=>t.slots.Count(id=>id==gem.runtimeInstanceId))==1,scenario+" exactly one stash owner / live gem unequipped");
                    var stored=saved.items.Single(i=>i.instanceId==gem.runtimeInstanceId);
                    Check(saved.elementalGemInstanceId==null && saved.run.transferredObjects.Count(id=>id==transfer.ObjectId)==1 && stored.originRunId==null && JsonConvert.SerializeObject(stored.gemState)==quality,scenario+" durable origin / spent marker / quality preserved");
                    string once=JsonConvert.SerializeObject(committed);confirm.onClick.Invoke();
                    Check(JsonConvert.SerializeObject(session.Read())==once && transfer.TryInteract(actor)==InteractionExecutionResult.Rejected,scenario+" repeat confirm / interaction cannot duplicate transfer");
                }
                finally
                {
                    store.FaultInjector=null;
                    if(panel!=null){panel.Close();UnityEngine.Object.DestroyImmediate(panel.gameObject);}
                    foreach(var overlay in overlays)if(overlay!=null)UnityEngine.Object.DestroyImmediate(overlay);
                    if(director!=null)UnityEngine.Object.DestroyImmediate(director.gameObject);
                    session.Detach();AccountGameplayProjection.Restore(baseline,account,registry);original.Attach();
                    SetWorldPhase(phase);game.equipmentWindow.gameObject.SetActive(equipmentVisible);
                }
            }
            Check(expectedErrors.Count==1,"one deliberate failed-write diagnostic");
            Check(unexpectedErrors.Count==0,"no unexpected UI or transaction errors");
        }
        catch(Exception error){failure=error.ToString();}
        finally
        {
            Application.logMessageReceived-=Log;UnityEngine.Random.state=random;
            original.Attach();SetWorldPhase(phase);game.equipmentWindow.gameObject.SetActive(equipmentVisible);
        }
        Save(output,"Play.json",new {status=failure==null?"PASS":"FAIL",checks,observations,expectedErrors,unexpectedErrors,failure,account=AccountBootstrap.SaveDirectory});
    }
}
