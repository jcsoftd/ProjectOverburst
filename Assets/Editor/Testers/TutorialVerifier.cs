using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Overburst.Appearance;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class TutorialVerifier
{
    private const string Key = "Overburst.TutorialVerifier.";
    private static readonly List<string> checks = new List<string>();
    private static readonly Stack<IEnumerator> tasks = new Stack<IEnumerator>();
    private static string output;
    private static int frame;
    private static Keyboard keyboard, previousKeyboard;
    private static Mouse mouse, previousMouse;
    private static InputSettings inputSettings, temporarySettings;
    private static OptionalTutorialController tutorial;
    private static PlayerActorRuntime actor;
    private static double deadline;
    private const BindingFlags ViewFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static object ViewGet(object target, string name) => target.GetType().GetProperty(name, ViewFlags).GetValue(target);
    private static object ViewCall(object target, string name, params object[] args) => target.GetType().GetMethod(name, ViewFlags).Invoke(target, args);
    private static object SizeGroup(EditorWindow view)
    {
        var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizes");
        return ViewCall(type.BaseType.GetProperty("instance", ViewFlags).GetValue(null), "GetGroup", ViewGet(view, "currentSizeGroupType"));
    }
    private static void RestoreResolution()
    {
        string owner = SessionState.GetString(Key+"resolutionOwner", ""); if (owner == "") return;
        var view = EditorUtility.InstanceIDToObject(SessionState.GetInt(Key+"view", 0)) as EditorWindow;
        if (!view) throw new InvalidOperationException("Owned Game view unavailable for restoration");
        var group = SizeGroup(view); ViewCall(view, "SizeSelectionCallback", SessionState.GetInt(Key+"size", 0), null);
        for (int i = (int)ViewCall(group, "GetTotalCount") - 1; i >= 0; i--)
            if (((string)ViewGet(ViewCall(group, "GetGameViewSize", i), "baseText")).StartsWith(owner, StringComparison.Ordinal)) ViewCall(group, "RemoveCustomSize", i);
        if (Enumerable.Range(0, (int)ViewCall(group, "GetTotalCount")).Any(i => ((string)ViewGet(ViewCall(group, "GetGameViewSize", i), "baseText")).StartsWith(owner, StringComparison.Ordinal)))
            throw new InvalidOperationException("Owned Game resolution entries remain after restoration");
        view.Repaint(); SessionState.EraseString(Key+"resolutionOwner"); SessionState.EraseInt(Key+"view"); SessionState.EraseInt(Key+"size");
    }
    static TutorialVerifier()
    {
        EditorApplication.playModeStateChanged += State;
        if (SessionState.GetString(Key+"pending", "") != "") EditorApplication.update += Begin;
        if (SessionState.GetString(Key+"return", "") != "") EditorApplication.update += Return;
        if (SessionState.GetString(Key+"return", "") != "" && SessionState.GetString(Key+"pending", "") == "") EditorApplication.update += EndInterruptedPlay;
    }
    private static void EndInterruptedPlay()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        EditorApplication.update-=EndInterruptedPlay;
        if(EditorApplication.isPlaying&&OwnPlay)Finish(new InvalidOperationException("Verification interrupted by domain reload"));
    }
    private static string Hash(string p) { using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(p))).Replace("-", ""); }
    private static object Scenes() => Enumerable.Range(0,SceneManager.sceneCount).Select(i=>new {SceneManager.GetSceneAt(i).path,SceneManager.GetSceneAt(i).isDirty,roots=SceneManager.GetSceneAt(i).GetRootGameObjects().Select(g=>g.GetInstanceID()).OrderBy(x=>x).ToArray()}).ToArray();
    private static void Write(string file,object value)=>File.WriteAllText(Path.Combine(output,file),JsonConvert.SerializeObject(value,Formatting.Indented));
    private static void Check(bool pass,string label) {if(!pass)throw new InvalidOperationException(label);checks.Add(label);Write("progress.json",new{count=checks.Count,last=label});}
    public static object Native(string directory)
    {
        AppearanceCustomizationBuilder.RequireIdle(); output=Path.GetFullPath(directory);Directory.CreateDirectory(output);checks.Clear();
        var prefab=Resources.Load<OptionalTutorialController>(OptionalTutorialController.ResourcePath);
        Check(prefab && prefab.view && prefab.arenaPrefab,"Tutorial UI and arena load from formal resources");
        Check(prefab.GetComponentsInChildren<Transform>(true).All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),"UI has no missing script");
        var arena=prefab.arenaPrefab;
        Check(arena.definition && arena.strongAbility && arena.strongAbility.IsParryable && arena.pool.IsAuthoringValid && arena.targets.Length==3,"Production enemy, attack and pool references resolve");
        var floor=arena.GetComponentInChildren<MeshCollider>().sharedMesh;
        Check(floor.vertices.Length==4 && floor.vertices.All(v=>Mathf.Approximately(Mathf.Abs(v.x)+Mathf.Abs(v.z),12)),"Playable floor uses four diamond corners, 24m diagonals");
        var floorRenderer=arena.transform.Find("Diamond floor").GetComponent<MeshRenderer>();
        Check(floorRenderer.sharedMaterial==AssetDatabase.LoadAssetAtPath<Material>("Assets/ProjectOverburst/Resources/Enemies/Themes/Arena/Floor.mat"),"Tutorial reuses exact theme trial floor material");
        var platform=arena.transform.Find("Platform body");
        Check(platform&&platform.localScale.y>=1f&&platform.localPosition.y+platform.localScale.y*.5f<0,"Diamond platform has visible thickness below walkable surface");
        Check(arena.skyMaterial&&arena.skyMaterial.shader.isSupported&&arena.skyMaterial.GetTexturePropertyNames().All(n=>!arena.skyMaterial.GetTexture(n)),"Existing sky material is supported and requires no textures");
        Check(prefab.view.chapterButtons.Length==7 && prefab.view.energyButtons.Length==3,"Approved chapters and practice controls authored");
        Check(((RectTransform)prefab.view.goal.transform).sizeDelta.x==720 && ((RectTransform)prefab.view.comparison.transform).sizeDelta.x==900,"Approved revision 2 compact panel widths preserved");
        Check(PlayerParryController.ResolveGrade(.299f)==ParryGrade.Incomplete && PlayerParryController.ResolveGrade(.3f)==ParryGrade.Normal && PlayerParryController.ResolveGrade(.799f)==ParryGrade.Normal && PlayerParryController.ResolveGrade(.8f)==ParryGrade.Perfect,"Actual parry thresholds retained");
        var registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var state=OptionalTutorialController.CreateLoadout(registry,NewAccountFactory.Create(registry));
        Check(state.elementalGemInstanceId!=null && state.items.Any(i=>i.contentId==OptionalTutorialController.PotionId),"Training loadout validates with fire gem and healing consumables");
        var files=Directory.GetFiles(output,"*",SearchOption.AllDirectories).OrderBy(x=>x).ToArray();
        var store=EasySaveAccountStore.CreateMemoryOnly();store.Save(state,"memory-copy");var first=store.Load();first.level=33;
        Check(store.IsMemoryOnly && store.Load().level==1 && Directory.GetFiles(output,"*",SearchOption.AllDirectories).OrderBy(x=>x).SequenceEqual(files),"Memory store copies snapshots without creating disk saves");
        string records=Path.Combine(output,"Records");var record=new TutorialProgress{offered=true,completed=1,skipped=2,lastLesson=2};record.Save(records);
        var reloaded=TutorialProgress.Load(records);Check(reloaded.offered&&reloaded.completed==1&&reloaded.skipped==2,"Offer, completion and skip records round trip separately");
        string corrupt=Path.Combine(output,"Corrupt");Directory.CreateDirectory(corrupt);string path=Path.Combine(corrupt,"tutorial.es3");File.WriteAllText(path,"deliberately malformed training record");string hash=Hash(path);bool rejected=false;
        try{TutorialProgress.Load(corrupt);}catch{rejected=true;}
        Check(rejected&&hash==Hash(path),"Corrupted tutorial record is preserved and rejected");
        var result=new {status="PASS",scope="Native",count=checks.Count,checks=checks.ToArray()};Write("native-results.json",result);return result;
    }
    public static void Start(string directory)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        if(IsolatedSavePlayGuard.ActiveDirectory!="" || SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")!="" || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY")) || SessionState.GetString("Overburst.UIPrefabPlayBoot.account", "")!="" || SessionState.GetString("Overburst.VisualPlay.Session.plan", "")!="")throw new InvalidOperationException("Another Editor session is prepared");
        output=Path.GetFullPath(directory);if(Directory.Exists(output))throw new InvalidOperationException("Fresh Play evidence folder required");Directory.CreateDirectory(output);
        string real=Path.Combine(Application.persistentDataPath,"Account");
        Write("before.json",new{pid=System.Diagnostics.Process.GetCurrentProcess().Id,scenes=Scenes(),start=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),input=InputSystem.settings.GetInstanceID(),background=Application.runInBackground,account=real,files=Directory.Exists(real)?Directory.GetFiles(real,"*",SearchOption.AllDirectories).OrderBy(p=>p).Select(p=>new{path=p,hash=Hash(p)}).ToArray():null});
        var registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);var catalog=Resources.Load<CharacterAppearanceCatalog>(CharacterAppearanceCatalog.ResourcePath);
        var seed=AccountCharacterCommands.First(Guid.NewGuid().ToString("N"),"튜토리얼검증","knight",catalog.defaultAppearance,registry);
        new EasySaveAccountStore(Path.Combine(output,"Account")).Save(seed,"tutorial-fixture");
        SessionState.SetString(Key+"pending",output);SessionState.SetString(Key+"return",output);SessionState.SetFloat(Key+"deadline",(float)EditorApplication.timeSinceStartup+480);
        Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY",Path.Combine(output,"Settings"));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        EditorApplication.update-=Begin;EditorApplication.update+=Begin;
        try{IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output,"Account"));}catch(Exception error){Finish(error);throw;}
    }
    private static bool OwnPlay=>IsolatedSavePlayGuard.ActiveDirectory==Path.Combine(SessionState.GetString(Key+"return", ""),"Account");
    private static void Begin()
    {
        output=SessionState.GetString(Key+"pending", "");if(output==""){EditorApplication.update-=Begin;return;}
        if(EditorApplication.timeSinceStartup>SessionState.GetFloat(Key+"deadline",0)){Finish(new TimeoutException("Tutorial boot timeout"));return;}
        if(!EditorApplication.isPlaying||!OwnPlay||!TitleCharacterPanel.Instance)return;
        EditorApplication.update-=Begin;SessionState.EraseString(Key+"pending");checks.Clear();Application.runInBackground=true;deadline=EditorApplication.timeSinceStartup+420;frame=-1;tasks.Push(Verify());EditorApplication.update+=Tick;
    }
    private static void Tick()
    {
        if(Time.frameCount==frame && EditorApplication.timeSinceStartup<deadline)return;frame=Time.frameCount;EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if(!EditorApplication.isPlaying||!OwnPlay||EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Owned tutorial Play ended or timed out");
            while(tasks.Count>0){var task=tasks.Peek();if(!task.MoveNext()){(tasks.Pop() as IDisposable)?.Dispose();continue;}if(task.Current is IEnumerator next){tasks.Push(next);continue;}return;}Finish(null);
        }
        catch(Exception error){Finish(error);}
    }
    private static IEnumerator Frames(int n){while(n-->0)yield return null;}
    private static IEnumerator Seconds(float s){float until=Time.unscaledTime+s;while(Time.unscaledTime<until)yield return null;}
    private static IEnumerator Wait(Func<bool> predicate,string label,float seconds=20){float until=Time.unscaledTime+seconds;while(!predicate()&&Time.unscaledTime<until)yield return null;Check(predicate(),label);}
    private static IEnumerator Capture(string name){ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return Frames(5);}
    private static bool Done(TutorialLesson lesson)=>(tutorial.CompletedMask&(1<<(int)lesson))!=0;
    private static void FaceTarget(float distance=1.5f)
    {
        var enemy=tutorial.Arena.Actors[0];Vector3 forward=tutorial.Arena.transform.forward;
        ActorTeleportUtility.TeleportSafely(actor.transform,enemy.transform.position-forward*distance,Quaternion.LookRotation(forward));
        Vector3 screen=Camera.main.WorldToScreenPoint(enemy.transform.position);InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(screen.x,screen.y)});
    }
    private static IEnumerator Attack(bool heavy)
    {
        var melee=actor.GetComponent<MeleeRuntime>();FaceTarget();yield return Frames(3);
        WeaponActionResult result=heavy?melee.TryStartHeavyAttack(tutorial.Arena.transform.forward):melee.TryStartAction(new WeaponActionRequest(WeaponActionSource.PlayerInput,null,tutorial.Arena.transform.forward),out _);
        Check(result==WeaponActionResult.Accepted,heavy?"Actual heavy accepted":"Actual weak attack accepted");
        yield return Wait(()=>!melee.IsAttackInProgress,"Attack recovery finishes",10);yield return Frames(3);
    }
    private static IEnumerator Verify()
    {
        yield return Frames(5);TitleCharacterPanel.Instance.start.onClick.Invoke();yield return Frames(3);TitleCharacterPanel.Instance.enter.onClick.Invoke();
        yield return Wait(()=>AccountBootstrap.Ready&&WorldSessionState.IsHideout&&!PersistentSceneFlow.Instance.IsSwitching,"Title enters town through production flow",100);yield return Frames(12);
        tutorial=OptionalTutorialController.Instance;actor=PlayerContext.Instance.CurrentActor;
        Check(tutorial&&tutorial.view.modal.activeSelf,"First account entry offers optional tutorial");yield return Capture("01-invitation");
        tutorial.view.secondary.onClick.Invoke();Check(!tutorial.IsTraining&&!GameplayInputBlocker.IsGameplayInputBlocked,"Decline immediately continues normal game");
        Check(!tutorial.view.footer.activeSelf,"Declining removes all tutorial guidance from normal gameplay");
        tutorial.OfferIfNeeded();Check(!tutorial.view.modal.activeSelf,"Already offered account is not prompted again");
        var original=AccountGameplaySession.Current;string before=JsonConvert.SerializeObject(original.Read());var position=actor.transform.position;float hp=actor.Health.CurrentHp;
        var originalClearFlags=Camera.main.clearFlags;var originalSky=RenderSettings.skybox;
        OverburstGameMenu.Instance.Open();OverburstGameMenu.Instance.returnButton.onClick.Invoke();yield return Frames(4);Check(tutorial.view.chapters.activeSelf,"Town menu reopens chapter selection");yield return Capture("02-chapters");
        previousKeyboard=Keyboard.current;previousMouse=Mouse.current;keyboard=InputSystem.AddDevice<Keyboard>("TutorialVerifierKeyboard");mouse=InputSystem.AddDevice<Mouse>("TutorialVerifierMouse");
        inputSettings=InputSystem.settings;temporarySettings=Object.Instantiate(inputSettings);SessionState.SetInt(Key+"inputClone",temporarySettings.GetInstanceID());temporarySettings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;temporarySettings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;InputSystem.settings=temporarySettings;
        tutorial.view.primary.onClick.Invoke();yield return Frames(15);
        Check(tutorial.IsTraining&&WorldSessionState.Phase==WorldPhase.Tutorial&&AccountGameplaySession.Current!=original,"Training uses separate account and tutorial world");
        Check(tutorial.Arena.Actors.Count==0&&AccountGameplaySession.Current.Read().elementalGemInstanceId!=null,"Movement starts safely with temporary fire gem equipped");yield return Capture("03-diamond-move");
        var moved=actor.transform.position;InputSystem.QueueStateEvent(keyboard,new KeyboardState(UnityEngine.InputSystem.Key.W));yield return Frames(8);InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return Frames(2);
        Check(Vector3.Distance(moved,actor.transform.position)>.05f,"Actual bound movement input moves player on diamond floor");
        ActorTeleportUtility.TeleportSafely(actor.transform,tutorial.Arena.moveTarget.position+Vector3.up*.09f,tutorial.Arena.playerSpawn.rotation);
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(UnityEngine.InputSystem.Key.X));yield return Frames(2);InputSystem.QueueStateEvent(keyboard,new KeyboardState());
        yield return Wait(()=>Done(TutorialLesson.Move),"Move target and actual combat toggle complete lesson");
        tutorial.StartLesson((int)TutorialLesson.Charge);yield return Frames(5);
        var melee=actor.GetComponent<MeleeRuntime>();var energy=actor.GetComponent<OverburstElementEnergy>();
        ActorTeleportUtility.TeleportSafely(actor.transform,tutorial.Arena.transform.TransformPoint(new Vector3(0,.12f,-8)),tutorial.Arena.transform.rotation);
        melee.TryStartAction(new WeaponActionRequest(WeaponActionSource.PlayerInput,null,tutorial.Arena.transform.forward),out _);yield return Seconds(2);
        Check(!Done(TutorialLesson.Charge)&&energy.Amount==0,"Whiff cannot complete charging lesson");
        for(int i=0;i<6&&!Done(TutorialLesson.Charge);i++)yield return Attack(false);
        Check(Done(TutorialLesson.Charge),"Actual weak hits charge and complete lesson");
        tutorial.StartLesson((int)TutorialLesson.Discharge);yield return Frames(5);
        typeof(OverburstElementEnergy).GetMethod("PrepareTutorialEnergy",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(energy,new object[]{0f});
        yield return Attack(true);Check(!Done(TutorialLesson.Discharge),"Empty heavy cannot complete elemental discharge");
        tutorial.Retry();yield return Frames(4);yield return Attack(true);Check(Done(TutorialLesson.Discharge),"Heavy hit with actual discharge completes lesson");
        foreach(var lesson in new[]{TutorialLesson.Dash,TutorialLesson.Roll,TutorialLesson.DashLight,TutorialLesson.DashHeavy})
        {
            tutorial.StartLesson((int)lesson);yield return Seconds(.4f);FaceTarget(4f);yield return Frames(4);
            var evade=actor.GetComponent<PlayerEvadeController>();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(lesson==TutorialLesson.Roll?UnityEngine.InputSystem.Key.S:UnityEngine.InputSystem.Key.W,UnityEngine.InputSystem.Key.LeftShift));
            yield return Frames(2);
            if(lesson==TutorialLesson.DashLight||lesson==TutorialLesson.DashHeavy)
            {Vector3 screen=Camera.main.WorldToScreenPoint(tutorial.Arena.Actors[0].transform.position);InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(screen.x,screen.y),buttons=(ushort)(lesson==TutorialLesson.DashHeavy?2:1)});yield return Frames(2);}
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.QueueStateEvent(mouse,new MouseState{position=mouse.position.ReadValue()});
            yield return Wait(()=>Done(lesson),"Actual input completes "+lesson,10);
        }
        foreach(var lesson in new[]{TutorialLesson.IncompleteParry,TutorialLesson.NormalParry,TutorialLesson.PerfectParry})
        {
            tutorial.StartLesson((int)lesson);yield return Frames(5);FaceTarget();yield return Frames(4);
            var enemy=tutorial.Arena.Actors[0];float beforeHp=actor.Health.CurrentHp;
            yield return Wait(()=>enemy.AbilityController.IsParryThreatTo(actor.GetComponent<CombatTarget>()),"Real strong attack enters parry threat");
            Check(melee.TryStartHeavyAttack(tutorial.Arena.transform.forward)==WeaponActionResult.Accepted,"Actual heavy accepts prepared parry");
            yield return Wait(()=>Done(lesson),"Actual parry completes "+lesson,6);
            Check(actor.GetComponent<PlayerParryController>().ActionGrade==TutorialLessons.Grade(lesson),"Observed parry grade equals "+lesson);
            Check(lesson==TutorialLesson.IncompleteParry?actor.Health.CurrentHp<beforeHp:Mathf.Approximately(actor.Health.CurrentHp,beforeHp),"Residual/no-damage behavior matches "+lesson);
            yield return Capture("parry-"+lesson);
        }
        tutorial.StartLesson((int)TutorialLesson.Combat);yield return Frames(5);
        for(int i=0;i<3;i++)
        {
            var enemy=tutorial.Arena.Actors.First(e=>!e.Health.IsDead);
            enemy.Health.TakeDamage(new DamageInfo(100000,enemy.transform.position,source:actor.gameObject,playerAttackKind:PlayerAttackKind.Weak));
        }
        Check(Done(TutorialLesson.Combat),"Three real deaths complete combat without rewards");
        tutorial.StartLesson((int)TutorialLesson.Equipment);yield return Seconds(1.5f);
        var pickup=Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).First(p=>p.gameObject.scene==WorldSessionState.ContentScene);
        var inventory=PlayerAccountInventoryService.Instance.Inventory;
        Check(pickup.TryPickup(inventory),"Training armor uses actual world pickup transaction");
        int slot=Enumerable.Range(0,inventory.Capacity).First(i=>inventory.GetItemAt(i)?.baseData is GearItemData);
        Check(GearEquipmentService.EquipFromInventorySlot(slot),"Training armor equips through production inventory service");
        yield return Wait(()=>Done(TutorialLesson.Equipment),"Actual equipment projection completes lesson");
        tutorial.StartLesson((int)TutorialLesson.Potion);yield return Frames(4);
        var bindings=Object.FindFirstObjectByType<InventoryQuickSlotBindingController>();ItemData item;
        item=inventory.Items.First(i=>i!=null&&i.baseData is ConsumableItemData);
        Check(bindings.Bind(1,item),"Potion binds through real quickslot transaction");yield return Seconds(.3f);
        Check(Object.FindFirstObjectByType<InventoryItemActionService>().UseQuickSlot(1),"Registered quickslot consumes and heals");
        yield return Wait(()=>Done(TutorialLesson.Potion),"Actual bind/use/heal completes lesson");
        tutorial.StartLesson((int)TutorialLesson.World);tutorial.view.worldConfirm.onClick.Invoke();yield return Seconds(2);
        Check(tutorial.view.modal.activeSelf,"Final information opens completion summary");yield return Capture("04-complete");
        tutorial.view.tertiary.onClick.Invoke();yield return Frames(4);Check(tutorial.FreePractice,"Completion opens free practice");tutorial.view.energyButtons[0].onClick.Invoke();Check(Mathf.Abs(energy.Normalized-.2f)<.001f,"Free practice can explicitly prepare incomplete gauge");
        OverburstGameMenu.Instance.Open();float pausedTime=Time.time;yield return Seconds(.25f);
        Check(Time.time==pausedTime&&GameplayInputBlocker.IsGameplayInputBlocked,"Menu pauses training time and gameplay input");OverburstGameMenu.Instance.Close();
        yield return VerifyLayout();
        tutorial.Exit();yield return Frames(12);
        Check(!tutorial.IsTraining&&WorldSessionState.IsHideout&&AccountGameplaySession.Current==original,"Exit restores original session and town");
        Check(before==JsonConvert.SerializeObject(original.Read()),"Original account snapshot is unchanged by training");
        Check(Vector3.Distance(position,actor.transform.position)<.3f&&Mathf.Approximately(hp,actor.Health.CurrentHp),"Original position and HP restored");
        Check(!GameplayInputBlocker.IsGameplayInputBlocked,"Tutorial releases gameplay input ownership");
        Check(QuarterViewCamera.ActiveInstance.CurrentTarget==actor.transform&&Mathf.Approximately(QuarterViewCamera.ActiveInstance.FramingDistanceScale,1f),"Exit restores normal camera tracking and framing");
        Check(Camera.main.clearFlags==originalClearFlags&&RenderSettings.skybox==originalSky,"Exit restores original sky and camera background");
        Check(!tutorial.view.footer.activeSelf&&!SceneManager.GetSceneByName("Overburst_Tutorial_"+tutorial.GetInstanceID()).IsValid(),"Exit removes tutorial guidance and unloads temporary scene");
        tutorial.ShowChapters();tutorial.StartLesson((int)TutorialLesson.Charge);yield return Frames(4);tutorial.Skip();Check(tutorial.Lesson==TutorialLesson.Discharge,"Skip advances without replay requirement");tutorial.Retry();yield return Frames(3);tutorial.Exit();yield return Frames(8);
        Check(AccountGameplaySession.Current==original&&!tutorial.IsTraining,"Repeated entry retry skip exit returns cleanly");yield return Capture("05-town-return");
        Write("runtime-scope.json",new{screen=new[]{Screen.width,Screen.height},completed=tutorial.CompletedMask,scope="Real title/menu/account, keyboard/mouse combat, production pickup/equip/use; combat death tally uses damage injection; no Player build"});
    }
    private static IEnumerator VerifyLayout()
    {
        tutorial.StartLesson((int)TutorialLesson.PerfectParry);tutorial.Arena.PauseCombat();yield return Frames(15);
        var playerScreen=Camera.main.WorldToViewportPoint(actor.transform.position);
        var targetScreen=Camera.main.WorldToViewportPoint(tutorial.Arena.Actors[0].transform.position);
        Check(playerScreen.x<.5f&&targetScreen.x>.5f&&playerScreen.y<targetScreen.y,"Authored composition places player lower left and target upper right");
        Check(QuarterViewCamera.ActiveInstance.CurrentTarget==tutorial.Arena.transform,"Training camera keeps the arena composition");
        Check(Camera.main.clearFlags==CameraClearFlags.Skybox&&RenderSettings.skybox==tutorial.Arena.skyMaterial,"Training uses authored sky rather than solid blue background");
        Check(tutorial.Arena.Actors.All(e=>!e.GetComponent<EnemyOverheadHpBar>().enabled&&!e.GetComponent<EnemyTargetHpReporter>().enabled),"Training monsters suppress overhead and hit target HP bars");
        var hover=Object.FindFirstObjectByType<OverburstWorldHighlightController>();
        InputSystem.QueueStateEvent(mouse,new MouseState{position=(Vector2)Camera.main.WorldToScreenPoint(tutorial.Arena.Actors[0].transform.position)});yield return Frames(5);
        Check(hover&&typeof(OverburstWorldHighlightController).GetField("hoveredEnemy",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(hover)==null,"Mouse over training monster does not display hover highlight or name");
        Check(tutorial.view.heading.alignment==TMPro.TextAlignmentOptions.Left&&string.IsNullOrEmpty(tutorial.view.feedback.text)&&!tutorial.view.footer.activeSelf,"Left aligned objective has no preparation or footer clutter");
        var type=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        var view=(EditorWindow)Resources.FindObjectsOfTypeAll(type).First();var group=SizeGroup(view);
        int count=(int)ViewCall(group,"GetTotalCount"), original=(int)ViewGet(view,"selectedSizeIndex");
        // Unity shortens custom size labels; keep the ownership prefix within that limit.
        string owner="__OBTut_"+Guid.NewGuid().ToString("N").Substring(0,8);
        SessionState.SetString(Key+"resolutionOwner",owner);SessionState.SetInt(Key+"view",view.GetInstanceID());SessionState.SetInt(Key+"size",original);
        try
        {
            var kind=Enum.Parse(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizeType"),"FixedResolution");
            foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720)})
            {
                ViewCall(group,"AddCustomSize",Activator.CreateInstance(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSize"),kind,size.x,size.y,owner+size.x));
                ViewCall(view,"SizeSelectionCallback",(int)ViewCall(group,"GetTotalCount")-1,null);view.Repaint();
                yield return Wait(()=>Screen.width==size.x&&Screen.height==size.y,"Actual Game resolution "+size,12);yield return Frames(10);
                foreach(var lesson in new[]{TutorialLesson.PerfectParry,TutorialLesson.Equipment,TutorialLesson.Potion,TutorialLesson.World})
                {
                    tutorial.view.ShowLesson(lesson);Canvas.ForceUpdateCanvases();yield return Frames(3);
                    var labels=tutorial.view.goal.GetComponentsInChildren<TMPro.TMP_Text>();
                    Check(labels.All(t=>{t.ForceMeshUpdate();return !t.isTextTruncated&&t.textInfo.characterInfo.Take(t.textInfo.characterCount).Count(c=>c.isVisible)>0||string.IsNullOrEmpty(t.text);}),"Goal text renders without truncation "+lesson+" "+size);
                    if(lesson==TutorialLesson.PerfectParry)Check(tutorial.view.comparison.GetComponentsInChildren<TMPro.TMP_Text>().All(t=>{t.ForceMeshUpdate();return !t.isTextTruncated;}),"All three parry labels render "+size);
                    yield return Capture("layout-"+lesson+"-"+size.x);
                }
            }
        }
        finally{RestoreResolution();}
        yield return Frames(8);Check((int)ViewGet(view,"selectedSizeIndex")==original&&(int)ViewCall(group,"GetTotalCount")==count,"Original Game resolution and list restored");
    }
    private static void Finish(Exception error)
    {
        EditorApplication.update-=Tick;EditorApplication.update-=Begin;SessionState.EraseString(Key+"pending");
        try
        {
            foreach(var task in tasks)(task as IDisposable)?.Dispose();tasks.Clear();
            if(inputSettings){InputSystem.settings=inputSettings;inputSettings=null;}if(temporarySettings){Object.Destroy(temporarySettings);temporarySettings=null;SessionState.EraseInt(Key+"inputClone");}
            RestoreResolution();
            if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);keyboard=null;mouse=null;
            if(previousKeyboard!=null&&previousKeyboard.added)previousKeyboard.MakeCurrent();if(previousMouse!=null&&previousMouse.added)previousMouse.MakeCurrent();
        }
        catch(Exception cleanup){error=error==null?cleanup:new AggregateException(error,cleanup);}
        try
        {
            output=SessionState.GetString(Key+"return",output);Write("play-results.json",new{status=error==null?"PASS":"FAIL",count=checks.Count,checks=checks.ToArray(),error=error?.ToString(),phase=WorldSessionState.Phase.ToString(),tutorialError=tutorial?.LastError});
        }
        finally
        {
            if(EditorApplication.isPlaying&&OwnPlay)EditorApplication.ExitPlaymode();EditorApplication.update-=Return;EditorApplication.update+=Return;
        }
    }
    private static void State(PlayModeStateChange state){if(state==PlayModeStateChange.EnteredEditMode&&SessionState.GetString(Key+"return", "")!=""){EditorApplication.update-=Return;EditorApplication.update+=Return;}}
    private static void Return()
    {
        output=SessionState.GetString(Key+"return", "");if(output==""){EditorApplication.update-=Return;return;}
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||IsolatedSavePlayGuard.ActiveDirectory!="")return;
        RestoreResolution();
        var before=JObject.Parse(File.ReadAllText(Path.Combine(output,"before.json")));string env=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)??"";
        if(env!=""&&env!=Path.Combine(output,"Account")){Write("return-results.json",new{status="BLOCKED_FOREIGN_ACCOUNT"});EditorApplication.update-=Return;return;}
        string start=(string)before["start"];EditorSceneManager.playModeStartScene=start==""?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
        int cloneId=SessionState.GetInt(Key+"inputClone",0);
        if(cloneId!=0)
        {
            var originalInput=EditorUtility.InstanceIDToObject((int)before["input"]) as InputSettings;
            var clone=EditorUtility.InstanceIDToObject(cloneId) as InputSettings;
            if(originalInput&&InputSystem.settings==clone)InputSystem.settings=originalInput;
            if(clone&&InputSystem.settings!=clone)Object.DestroyImmediate(clone);
            SessionState.EraseInt(Key+"inputClone");
        }
        if(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY")==Path.Combine(output,"Settings"))Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY",null);
        Application.runInBackground=(bool)before["background"];IsolatedSavePlayGuard.UseRealAccount();
        var files=before["files"]as JArray;bool account=files!=null?files.All(f=>File.Exists((string)f["path"])&&Hash((string)f["path"])==(string)f["hash"])&&Directory.GetFiles((string)before["account"],"*",SearchOption.AllDirectories).Length==files.Count:!Directory.Exists((string)before["account"]);
        bool scenes=JToken.DeepEquals(before["scenes"],JToken.FromObject(Scenes())),input=(int)before["input"]==InputSystem.settings.GetInstanceID();
        Write("return-results.json",new{status=account&&scenes&&input&&!IsolatedSavePlayGuard.RequiresAccountChoice?"PASS":"FAIL",realAccountUnchanged=account,scenes,input,playing=EditorApplication.isPlaying,guardReady=!IsolatedSavePlayGuard.RequiresAccountChoice,environment=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),start=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")});
        SessionState.EraseString(Key+"return");SessionState.EraseFloat(Key+"deadline");EditorApplication.update-=Return;
    }
}
