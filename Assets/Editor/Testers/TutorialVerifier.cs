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
        Check(prefab.view.primary && prefab.view.secondary && prefab.view.primary.transform.parent.childCount==2 && !prefab.transform.Find("Stage/Practice Energy") && !prefab.view.modal.transform.Find("Chapters"),"Initial invitation has two choices without chapter or free-practice controls");
        Check(((RectTransform)prefab.view.goal.transform).sizeDelta.x==720 && ((RectTransform)prefab.view.comparison.transform).sizeDelta.x==720,"Objective and comparison share compact left column");
        var panelSkin=Resources.Load<Sprite>("UI/HudMenu/Skins/panel");
        Check(new[]{prefab.view.goal,prefab.view.comparison,prefab.view.modal}.All(g=>g.transform.Find("Existing HUD Panel").GetComponent<UnityEngine.UI.Image>().sprite==panelSkin),"Tutorial panels reuse current HUD menu skin");
        Check(prefab.view.gradeBackgrounds.All(g=>g.sprite==Resources.Load<Sprite>("UI/HudMenu/Skins/row-hover")),"Selected parry uses current HUD hover skin");
        var guide=prefab.view.keyGuide;
        Check(guide&&guide.slots.Length==8&&guide.icons.Length==8&&guide.glyphs.Length==8&&guide.captions.Length==8,"Key guide uses eight authored reusable entries");
        Check(guide.keycap==Resources.Load<Sprite>("Tutorial/Keys/Keycap")&&guide.mouseLeft==Resources.Load<Sprite>("Tutorial/Keys/MouseLeft")&&guide.mouseRight==Resources.Load<Sprite>("Tutorial/Keys/MouseRight"),"Approved three key images load from formal tutorial resources");
        Check(new[]{guide.keycap,guide.mouseLeft,guide.mouseRight}.All(s=>{var i=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(s));return i.textureType==TextureImporterType.Sprite&&i.maxTextureSize==128&&!i.mipmapEnabled&&i.alphaIsTransparency&&!i.isReadable;}),"Guide textures preserve alpha with small non-readable UI imports");
        Check(((RectTransform)guide.transform).anchoredPosition==new Vector2(20,-134)&&((RectTransform)guide.transform).sizeDelta==new Vector2(680,36)&&guide.GetComponent<UnityEngine.UI.RectMask2D>(),"Guide stays in the approved 36px objective key line");
        Check(PlayerParryController.ResolveGrade(.299f)==ParryGrade.Incomplete && PlayerParryController.ResolveGrade(.3f)==ParryGrade.Normal && PlayerParryController.ResolveGrade(.799f)==ParryGrade.Normal && PlayerParryController.ResolveGrade(.8f)==ParryGrade.Perfect,"Actual parry thresholds retained");
        var registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var starter=NewAccountFactory.Create(registry);
        var starterGem=starter.items.Single(i=>i.instanceId==starter.elementalGemInstanceId);
        Check(starterGem.contentId==NewAccountFactory.StarterElementGemId&&starterGem.level==1&&starterGem.grade==ItemGrade.Common&&starterGem.count==1,"New character starts with one equipped level 1 common fire gem");
        string starterBefore=JsonConvert.SerializeObject(starter);
        var state=OptionalTutorialController.CreateLoadout(registry,starter);
        Check(state.elementalGemInstanceId!=null && state.items.Any(i=>i.contentId==OptionalTutorialController.PotionId),"Training loadout validates with fire gem and healing consumables");
        Check(state.inventory.Where(id=>!string.IsNullOrEmpty(id)).Any(id=>state.items.Single(i=>i.instanceId==id).contentId==TesterSupportItemData.ContentId),"Training consumables preserve starter support ownership");
        Check(JsonConvert.SerializeObject(starter)==starterBefore,"Training loadout does not mutate the original account");
        var files=Directory.GetFiles(output,"*",SearchOption.AllDirectories).OrderBy(x=>x).ToArray();
        var store=EasySaveAccountStore.CreateMemoryOnly();store.Save(state,"memory-copy");var first=store.Load();first.level=33;
        Check(store.IsMemoryOnly && store.Load().level==1 && Directory.GetFiles(output,"*",SearchOption.AllDirectories).OrderBy(x=>x).SequenceEqual(files),"Memory store copies snapshots without creating disk saves");
        string records=Path.Combine(output,"Records");var record=new TutorialProgress{offered=true,completed=1,skipped=2,lastLesson=2};record.Save(records);
        var reloaded=TutorialProgress.Load(records);Check(reloaded.offered&&reloaded.completed==1&&reloaded.skipped==2,"Offer, completion and skip records round trip separately");
        string corrupt=Path.Combine(output,"Corrupt");Directory.CreateDirectory(corrupt);string path=Path.Combine(corrupt,"tutorial.es3");File.WriteAllText(path,"deliberately malformed training record");string hash=Hash(path);bool rejected=false;
        try{TutorialProgress.Load(corrupt);}catch{rejected=true;}
        Check(rejected&&hash==Hash(path),"Corrupted tutorial record is preserved and rejected");
        string firstDirectory=Path.Combine(output,"FirstCharacter");string firstId=Guid.NewGuid().ToString("N");
        Check(TutorialProgress.Load(firstDirectory).firstCharacterId==null,"Legacy/missing records do not imply a new character");
        TutorialProgress.RecordFirstCharacter(firstDirectory,firstId);
        Check(TutorialProgress.Load(firstDirectory).firstCharacterId==firstId,"First creation eligibility survives record reload");
        TutorialProgress.RecordFirstCharacter(firstDirectory,Guid.NewGuid().ToString("N"));
        Check(TutorialProgress.Load(firstDirectory).firstCharacterId==firstId,"Additional creation cannot replace the first character");
        var consumed=TutorialProgress.Load(firstDirectory);consumed.offered=true;consumed.Save(firstDirectory);
        TutorialProgress.RecordFirstCharacter(firstDirectory,Guid.NewGuid().ToString("N"));
        Check(TutorialProgress.Load(firstDirectory).offered&&TutorialProgress.Load(firstDirectory).firstCharacterId==firstId,"Consumed account offer cannot reset on character creation");
        var perCharacter=TutorialProgress.Load(firstDirectory);string additionalId=Guid.NewGuid().ToString("N");
        Check(perCharacter.WasOfferedTo(firstId)&&!perCharacter.WasOfferedTo(additionalId),"Legacy offer migrates only its known character");
        perCharacter.offeredCharacterIds.Add(additionalId);perCharacter.Save(firstDirectory);
        var characterReload=TutorialProgress.Load(firstDirectory);
        Check(characterReload.WasOfferedTo(firstId)&&characterReload.WasOfferedTo(additionalId),"Character invitation history survives ES3 reload independently");
        string legacyDirectory=Path.Combine(directory,"LegacyCompleted");
        new TutorialProgress{offered=true,completed=1}.Save(legacyDirectory);
        var legacyCompleted=TutorialProgress.Load(legacyDirectory);
        Check(legacyCompleted.offered&&legacyCompleted.completed==1,"Legacy account offer remains consumed without a character id");
        var result=new {status="PASS",scope="Native",count=checks.Count,checks=checks.ToArray()};Write("native-results.json",result);return result;
    }
    public static void Start(string directory, bool firstEntryOnly = false)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        if(IsolatedSavePlayGuard.ActiveDirectory!="" || SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")!="" || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY")) || SessionState.GetString("Overburst.UIPrefabPlayBoot.account", "")!="" || SessionState.GetString("Overburst.VisualPlay.Session.plan", "")!="")throw new InvalidOperationException("Another Editor session is prepared");
        output=Path.GetFullPath(directory);if(Directory.Exists(output))throw new InvalidOperationException("Fresh Play evidence folder required");Directory.CreateDirectory(output);
        string real=Path.Combine(Application.persistentDataPath,"Account");
        Write("before.json",new{pid=System.Diagnostics.Process.GetCurrentProcess().Id,scenes=Scenes(),start=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),input=InputSystem.settings.GetInstanceID(),background=Application.runInBackground,account=real,files=Directory.Exists(real)?Directory.GetFiles(real,"*",SearchOption.AllDirectories).OrderBy(p=>p).Select(p=>new{path=p,hash=Hash(p)}).ToArray():null});
        SessionState.SetBool(Key+"firstEntryOnly",firstEntryOnly);
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
        EditorApplication.update-=Begin;SessionState.EraseString(Key+"pending");checks.Clear();Application.runInBackground=true;deadline=EditorApplication.timeSinceStartup+420;frame=-1;tasks.Push(SessionState.GetBool(Key+"firstEntryOnly",false)?VerifyFirstEntry():Verify());EditorApplication.update+=Tick;
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
    private static IEnumerator CreateCharacter(string name)
    {
        var title=TitleCharacterPanel.Instance;title.create.onClick.Invoke();yield return Frames(3);
        title.classNext.onClick.Invoke();title.nameInput.text=name;title.nameNext.onClick.Invoke();yield return Frames(3);
        Check(AppearanceCustomizationPanel.Instance?.IsCreation==true,"Actual name/appearance creation opens: "+name);
        AppearanceCustomizationPanel.Instance.apply.onClick.Invoke();yield return Frames(5);
        Check(title.Screen=="characters"&&title.SelectedId!=null,"Actual creation saves and selects: "+name);
    }
    private static IEnumerator EnterTown()
    {
        TitleCharacterPanel.Instance.enter.onClick.Invoke();
        yield return Wait(()=>AccountBootstrap.Ready&&WorldSessionState.IsHideout&&!PersistentSceneFlow.Instance.IsSwitching,"Production title selection enters town",100);
        yield return Frames(12);tutorial=OptionalTutorialController.Instance;actor=PlayerContext.Instance.CurrentActor;
    }
    private static IEnumerator ReturnToTitle()
    {
        Check(PersistentSceneFlow.Instance.TryReturnToTitle(out var error),"Production return to title accepted: "+error);
        yield return Wait(()=>TitleCharacterPanel.Instance&&TitleCharacterPanel.Instance.Screen=="title","Production title ready after account flush",100);
        TitleCharacterPanel.Instance.start.onClick.Invoke();yield return Frames(3);
    }
    private static IEnumerator VerifyFirstEntry()
    {
        yield return Frames(5);TitleCharacterPanel.Instance.start.onClick.Invoke();yield return Frames(3);
        string directory=Path.Combine(output,"Account");
        Check(new EasySaveAccountStore(directory).Load()==null,"First run begins without an account");
        yield return CreateCharacter("첫캐릭터검증");string firstId=TitleCharacterPanel.Instance.SelectedId;
        Check(TutorialProgress.Load(directory).firstCharacterId==firstId&&!TutorialProgress.Load(directory).offered,"Successful first creation persists pending eligibility");
        TitleCharacterPanel.Instance.Open(directory);TitleCharacterPanel.Instance.start.onClick.Invoke();yield return Frames(3);
        yield return EnterTown();
        Check(AccountGameplaySession.Current.Read().level==1&&tutorial.view.modal.activeSelf,"Level 1 first created character receives invitation after title reload");
        var equippedGem=PlayerContext.Instance.CurrentActorEquipment.EquippedElementGem;
        Check(equippedGem!=null&&equippedGem.level==1&&equippedGem.baseData is ElementGemItemData starterGem&&starterGem.element==WeaponElement.Fire,"Created character projects its level 1 fire gem into live equipment");
        yield return Capture("first-created-level1-invitation");
        tutorial.view.secondary.onClick.Invoke();
        Check(TutorialProgress.Load(directory).offered&&!tutorial.view.modal.activeSelf&&!GameplayInputBlocker.IsGameplayInputBlocked,"Decline is persisted and resumes normal gameplay");
        tutorial.OfferIfNeeded();Check(!tutorial.view.modal.activeSelf,"Duplicate offer callback does not prompt again");
        yield return ReturnToTitle();yield return EnterTown();
        Check(!tutorial.view.modal.activeSelf,"Level 1 relogin after decline does not prompt again");
        yield return ReturnToTitle();
        yield return CreateCharacter("추가캐릭터검증");yield return EnterTown();
        string additionalId=AccountGameplaySession.Current.Read().activeCharacterId;
        Check(AccountGameplaySession.Current.Read().level==1&&!tutorial.view.modal.activeSelf,"Additional level 1 character does not repeat the account invitation");
        Check(TutorialProgress.Load(directory).offered,"Account invitation remains consumed after additional creation");
        yield return ReturnToTitle();
        TitleCharacterPanel.Instance.rosterContent.Find("Character "+firstId).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();yield return Frames(3);
        yield return EnterTown();Check(!tutorial.view.modal.activeSelf,"Returning to an already offered character does not prompt again");
        yield return ReturnToTitle();
        var store=new EasySaveAccountStore(directory);var state=store.Load();state.level=2;state.skillTree=AccountSkillTree.GrantEligible(state.skillTree,2);store.Save(state,"gate-level2-fixture");
        new TutorialProgress{firstCharacterId=firstId}.Save(directory);
        TitleCharacterPanel.Instance.Open(directory);TitleCharacterPanel.Instance.start.onClick.Invoke();yield return Frames(3);yield return EnterTown();
        Check(AccountGameplaySession.Current.Read().level==2&&!tutorial.view.modal.activeSelf,"First-entry eligibility at level 2 suppresses invitation");
        Check(TutorialProgress.Load(directory).offered,"Ineligible first entry is consumed instead of deferred");
        OverburstGameMenu.Instance.Open();
        Check(OverburstGameMenu.Instance.returnButton.gameObject.activeSelf,"Town menu retains direct tutorial entry");
        OverburstGameMenu.Instance.returnButton.onClick.Invoke();yield return Frames(15);
        Check(tutorial.IsTraining&&tutorial.Lesson==TutorialLesson.Move&&!tutorial.view.modal.activeSelf,"Manual entry starts the first lesson without a selection screen");
        Check(tutorial.TryExit(out var exitError),"Manual entry restores account: "+exitError);yield return Frames(4);
        yield return ReturnToTitle();yield return EnterTown();
        Check(!tutorial.view.modal.activeSelf,"Consumed level 2 entry remains quiet on relogin");
    }
    private static IEnumerator Verify()
    {
        yield return Frames(5);TitleCharacterPanel.Instance.start.onClick.Invoke();yield return Frames(3);
        yield return CreateCharacter("튜토리얼검증");TitleCharacterPanel.Instance.enter.onClick.Invoke();
        yield return Wait(()=>AccountBootstrap.Ready&&WorldSessionState.IsHideout&&!PersistentSceneFlow.Instance.IsSwitching,"Title enters town through production flow",100);yield return Frames(12);
        tutorial=OptionalTutorialController.Instance;actor=PlayerContext.Instance.CurrentActor;
        Check(tutorial&&tutorial.view.modal.activeSelf,"First account entry offers optional tutorial");yield return Capture("01-invitation");
        var original=AccountGameplaySession.Current;string before=JsonConvert.SerializeObject(original.Read());var position=actor.transform.position;float hp=actor.Health.CurrentHp;
        var originalClearFlags=Camera.main.clearFlags;var originalSky=RenderSettings.skybox;
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
        Check(tutorial.view.keyGuide.glyphs.Any(t=>t.gameObject.activeInHierarchy&&t.text==QuickSlotKeyLabels.Short(1)),"Potion guide shows the actual registered quickslot key");yield return Capture("potion-registered-key");
        Check(Object.FindFirstObjectByType<InventoryItemActionService>().UseQuickSlot(1),"Registered quickslot consumes and heals");
        yield return Wait(()=>Done(TutorialLesson.Potion),"Actual bind/use/heal completes lesson");
        yield return VerifyLayout();
        tutorial.StartLesson((int)TutorialLesson.World);tutorial.view.worldConfirm.onClick.Invoke();yield return Seconds(2);
        Check(tutorial.view.modal.activeSelf,"Final information opens completion summary");yield return Capture("04-complete");
        OverburstGameMenu.Instance.Open();float pausedTime=Time.time;yield return Seconds(.25f);
        Check(Time.time==pausedTime&&GameplayInputBlocker.IsGameplayInputBlocked,"Menu pauses training time and gameplay input");OverburstGameMenu.Instance.Close();
        Check(!tutorial.view.secondary.gameObject.activeSelf,"Completion presents only return to game");
        tutorial.view.primary.onClick.Invoke();yield return Frames(12);
        Check(!tutorial.IsTraining&&WorldSessionState.IsHideout&&AccountGameplaySession.Current==original,"Exit restores original session and town");
        Check(before==JsonConvert.SerializeObject(original.Read()),"Original account snapshot is unchanged by training");
        Check(Vector3.Distance(position,actor.transform.position)<.3f&&Mathf.Approximately(hp,actor.Health.CurrentHp),"Original position and HP restored");
        Check(!GameplayInputBlocker.IsGameplayInputBlocked,"Tutorial releases gameplay input ownership");
        Check(QuarterViewCamera.ActiveInstance.CurrentTarget==actor.transform&&Mathf.Approximately(QuarterViewCamera.ActiveInstance.FramingDistanceScale,1f),"Exit restores normal camera tracking and framing");
        Check(Camera.main.clearFlags==originalClearFlags&&RenderSettings.skybox==originalSky,"Exit restores original sky and camera background");
        Check(!tutorial.view.footer.activeSelf&&!SceneManager.GetSceneByName("Overburst_Tutorial_"+tutorial.GetInstanceID()).IsValid(),"Exit removes tutorial guidance and unloads temporary scene");
        tutorial.OfferIfNeeded();Check(!tutorial.view.modal.activeSelf,"Completed tutorial is not offered again");
        yield return Capture("05-town-return");
        Write("runtime-scope.json",new{screen=new[]{Screen.width,Screen.height},completed=tutorial.CompletedMask,scope="Real title/menu/account, keyboard/mouse combat, production pickup/equip/use; combat death tally uses damage injection; no Player build"});
    }
    private static Rect ScreenBounds(RectTransform rect)
    {
        var canvas=rect.GetComponentInParent<Canvas>();
        var camera=canvas&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
        var corners=new Vector3[4];rect.GetWorldCorners(corners);
        var points=corners.Select(v=>RectTransformUtility.WorldToScreenPoint(camera,v)).ToArray();
        return Rect.MinMaxRect(points.Min(v=>v.x),points.Min(v=>v.y),points.Max(v=>v.x),points.Max(v=>v.y));
    }
    private static void CheckGuidancePlacement(string label)
    {
        Canvas.ForceUpdateCanvases();
        var visibleHud=Object.FindFirstObjectByType<OverburstGameUI>().hud.GetComponentsInChildren<UnityEngine.UI.Graphic>()
            .Where(g=>g.isActiveAndEnabled&&g.color.a>.01f&&!g.canvasRenderer.cull&&g.canvasRenderer.GetInheritedAlpha()>.01f).Select(g=>ScreenBounds(g.rectTransform)).ToArray();
        if(visibleHud.Length==0)throw new InvalidOperationException("Production HUD missing during layout verification");
        var guidance=new[]{tutorial.view.goal,tutorial.view.comparison}.Where(g=>g.activeSelf).Select(g=>ScreenBounds((RectTransform)g.transform)).ToArray();
        bool overlap=guidance.Any(a=>visibleHud.Any(b=>Mathf.Min(a.xMax,b.xMax)-Mathf.Max(a.xMin,b.xMin)>.5f&&Mathf.Min(a.yMax,b.yMax)-Mathf.Max(a.yMin,b.yMin)>.5f));
        if(overlap)Write("hud-overlap.json",new{label,guidance=guidance.Select(r=>new[]{r.x,r.y,r.width,r.height}).ToArray(),hud=Object.FindFirstObjectByType<OverburstGameUI>().hud.GetComponentsInChildren<UnityEngine.UI.Graphic>().Where(g=>g.isActiveAndEnabled&&g.color.a>.01f&&!g.canvasRenderer.cull&&g.canvasRenderer.GetInheritedAlpha()>.01f).Select(g=>new{name=g.name,parent=g.transform.parent.name,type=g.GetType().Name,alpha=g.canvasRenderer.GetInheritedAlpha(),bounds=new[]{ScreenBounds(g.rectTransform).x,ScreenBounds(g.rectTransform).y,ScreenBounds(g.rectTransform).width,ScreenBounds(g.rectTransform).height}}).ToArray()});
        Check(!overlap,"Tutorial guidance does not overlap visible production HUD "+label);
        var goal=ScreenBounds((RectTransform)tutorial.view.goal.transform);
        var next=tutorial.view.comparison.activeSelf?tutorial.view.comparison:null;
        if(next){var below=ScreenBounds((RectTransform)next.transform);Check(below.yMax<goal.yMin&&Mathf.Abs(below.xMin-goal.xMin)<1,"Supplement stays below and left aligned with objective "+label);}
    }
    private static void CheckKeyGuide(string label)
    {
        var guide=tutorial.view.keyGuide;Canvas.ForceUpdateCanvases();
        if(!guide.gameObject.activeSelf){Check(tutorial.view.keys.gameObject.activeSelf,"World information keeps its text line "+label);return;}
        Check(!tutorial.view.keys.gameObject.activeSelf&&guide.VisibleWidth>0&&guide.VisibleWidth<=680.1f,"Image guide fits approved objective width "+label);
        Check(guide.GetComponentsInChildren<TMPro.TMP_Text>().All(t=>{t.ForceMeshUpdate();return !t.isTextTruncated&&t.font.HasCharacters(t.text);}),"Key glyphs and action text render without truncation or missing characters "+label);
    }
    private static IEnumerator VerifyGuideBindings()
    {
        var asset=PlayerInputFacade.Current.RuntimeAsset;string before=asset.SaveBindingOverridesAsJson();
        var guide=tutorial.view.keyGuide;
        try
        {
            tutorial.view.ShowLesson(TutorialLesson.Dash);
            var evade=asset.FindAction(PlayerInputFacade.GameplayMapName+"/Evade");int ei=OverburstKeyBindingRow.ResolveBindingIndex(evade,null);
            evade.ApplyBindingOverride(ei,"<Keyboard>/q");yield return Frames(4);
            Check(guide.glyphs.Any(t=>t.gameObject.activeInHierarchy&&t.text=="Q"),"Changing evade binding updates the visible keycap immediately");CheckKeyGuide("Rebound evade");
            tutorial.view.ShowLesson(TutorialLesson.Charge);
            var attack=asset.FindAction(PlayerInputFacade.GameplayMapName+"/Attack");int ai=OverburstKeyBindingRow.ResolveBindingIndex(attack,null);
            attack.ApplyBindingOverride(ai,"<Keyboard>/e");yield return Frames(4);
            Check(guide.icons[0].sprite==guide.keycap&&guide.glyphs[0].text=="E","Keyboard attack override replaces mouse with labeled keycap");CheckKeyGuide("Rebound attack");
            tutorial.view.ShowLesson(TutorialLesson.PerfectParry);
            var aim=asset.FindAction(PlayerInputFacade.GameplayMapName+"/Aim");int mi=OverburstKeyBindingRow.ResolveBindingIndex(aim,null);
            aim.ApplyBindingOverride(mi,"<Mouse>/leftButton");yield return Frames(4);
            Check(guide.icons[0].sprite==guide.mouseLeft&&!guide.glyphs[0].gameObject.activeSelf,"Mouse-side override uses the matching click image");
            aim.ApplyBindingOverride(mi,string.Empty);yield return Frames(4);
            Check(guide.icons[0].sprite==guide.keycap&&guide.glyphs[0].text=="미지정","Unassigned binding displays explicit fallback");CheckKeyGuide("Unassigned parry");
            tutorial.view.ShowLesson(TutorialLesson.Move);
            var move=asset.FindAction(PlayerInputFacade.GameplayMapName+"/Move");int up=OverburstKeyBindingRow.ResolveBindingIndex(move,"Up");
            move.ApplyBindingOverride(up,"<Keyboard>/upArrow");yield return Frames(4);
            Check(guide.glyphs[0].text=="↑","Movement composite shows changed part binding");CheckKeyGuide("Rebound move");
            tutorial.view.ShowLesson(TutorialLesson.Potion);
            var quick=asset.FindAction(PlayerInputFacade.GameplayMapName+"/QuickSlot1");int qi=OverburstKeyBindingRow.ResolveBindingIndex(quick,null);
            quick.ApplyBindingOverride(qi,"<Keyboard>/r");yield return Frames(4);
            Check(guide.glyphs.Any(t=>t.gameObject.activeInHierarchy&&t.text=="R"),"Registered potion key follows quickslot rebind");CheckKeyGuide("Rebound potion");
            yield return Capture("keyguide-rebound-potion");
        }
        finally{asset.LoadBindingOverridesFromJson(before,true);tutorial.view.ShowLesson(TutorialLesson.PerfectParry);}
        Check(asset.SaveBindingOverridesAsJson()==before,"All temporary key overrides restored before exit");
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
                foreach(var lesson in new[]{TutorialLesson.Move,TutorialLesson.Charge,TutorialLesson.Discharge,TutorialLesson.Dash,TutorialLesson.Roll,TutorialLesson.DashLight,TutorialLesson.DashHeavy,TutorialLesson.Combat,TutorialLesson.PerfectParry,TutorialLesson.Equipment,TutorialLesson.Potion,TutorialLesson.World})
                {
                    tutorial.view.ShowLesson(lesson);Canvas.ForceUpdateCanvases();yield return Frames(3);
                    var labels=tutorial.view.goal.GetComponentsInChildren<TMPro.TMP_Text>();
                    Check(labels.All(t=>{t.ForceMeshUpdate();return !t.isTextTruncated&&t.textInfo.characterInfo.Take(t.textInfo.characterCount).Count(c=>c.isVisible)>0||string.IsNullOrEmpty(t.text);}),"Goal text renders without truncation "+lesson+" "+size);
                    if(lesson==TutorialLesson.PerfectParry)Check(tutorial.view.comparison.GetComponentsInChildren<TMPro.TMP_Text>().All(t=>{t.ForceMeshUpdate();return !t.isTextTruncated;}),"All three parry labels render "+size);
                    yield return Capture("layout-"+lesson+"-"+size.x);
                    CheckGuidancePlacement(lesson+" "+size);CheckKeyGuide(lesson+" "+size);
                    if(lesson==TutorialLesson.World)Check(ScreenBounds((RectTransform)tutorial.view.goal.transform).Contains(ScreenBounds((RectTransform)tutorial.view.worldConfirm.transform).center),"World confirm button remains inside its panel "+size);
                    if(lesson==TutorialLesson.PerfectParry)
                    {
                        tutorial.view.Feedback("다른 등급입니다. 다시 받아쳐보세요.");yield return Frames(3);
                        CheckGuidancePlacement("Parry feedback "+size);yield return Capture("layout-ParryFeedback-"+size.x);
                        tutorial.view.Feedback("");
                    }
                }
            }
        }
        finally{RestoreResolution();}
        yield return VerifyGuideBindings();
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
        SessionState.EraseString(Key+"return");SessionState.EraseFloat(Key+"deadline");SessionState.EraseBool(Key+"firstEntryOnly");EditorApplication.update-=Return;
    }
}
