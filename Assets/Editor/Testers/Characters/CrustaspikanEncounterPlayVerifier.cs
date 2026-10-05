using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// 기존 Editor의 미저장 씬을 건드리지 않고 한 번의 격리 Play에서 실제 왕복·판정·조립을 확인한다.
[InitializeOnLoad]
public static class CrustaspikanEncounterPlayVerifier
{
    private const string Key="Overburst.CrustaspikanFirstEncounterVerifier.";
    private static readonly List<string> passed=new List<string>();
    private static int stage;
    private static float stageAt;
    private static bool started,early,last;
    private static string output;
    private static PlayerActorRuntime player;
    private static CrustaspikanEncounter encounter;
    private static Vector3 entryPosition;
    private static Camera oldCamera;
    private static int originalLeases,parries,impacts;
    private static float originalHp,poise;
    private static Vector3 dodgeStart;
    private static bool dodgeRequested;
    private static bool movedOut;
    private static int dropCount;
    private static QuarterViewCamera gameplayCamera;
    private static float entryDistance, entryYaw, entryPitch, entryFov, zoomBefore;
    private static bool wheelQueued, wheelChecked, groggyHudChecked;
    private static Collider entryConfiner;
    private static bool entranceChecked;
    private static float entranceFinishedAt, nextEntranceFrame;
    private static int entranceFrame;
    private static readonly HashSet<int> entranceShots = new HashSet<int>();
    private static readonly List<object> entranceFrames = new List<object>();
    private static CrustaspikanEntranceCinematic firstEntrance, skippedEntrance;
    private static bool skipQueued;
    private static bool movieOnly;
    private static CrustaspikanEncounterMovieRecorder movie;
    private static EnemyActor interruptedActor;
    private static Vector3 interruptedVisualRest;
    private static CameraClearFlags entryClearFlags;
    private static Color entryBackground;
    private static Unity.Cinemachine.CinemachineBlendDefinition entryBlend;
    private static int entryPriority;
    private static GameObject externalInputOwner;
    private static readonly Dictionary<Canvas,bool> gameplayHudCanvases = new Dictionary<Canvas,bool>();
    private static float entryConfinerSlowing;
    private static EnemyActor legacyBoss;
    private static EnemyBossHudView legacyHud;
    private static EnemySpawnService legacySpawns;
    private static GameObject legacyServiceRoot;
    private static readonly List<KeyValuePair<EnemyTargetHpHud,bool>> targetHuds=new List<KeyValuePair<EnemyTargetHpHud,bool>>();
    private static readonly List<KeyValuePair<Behaviour,bool>> cameraDrivers=new List<KeyValuePair<Behaviour,bool>>();
    static CrustaspikanEncounterPlayVerifier(){EditorApplication.update+=Update;EditorApplication.playModeStateChanged+=Changed;}
    public static string Start(string directory,bool recordMovie=false)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)throw new InvalidOperationException("Editor가 유휴 상태여야 합니다.");
        if(SessionState.GetBool(Key+"returnPending",false) || IsolatedSavePlayGuard.RequiresAccountChoice
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""))
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires","")))
            throw new InvalidOperationException("이전 격리 검증의 실제 계정 반환이 먼저 완료돼야 합니다.");
        directory=IsolatedSavePlayGuard.ValidateDirectory(directory);Directory.CreateDirectory(directory);
        var settings=AssetDatabase.LoadAssetAtPath<CrustaspikanEncounterSettings>(CrustaspikanEncounterBuilder.AssetPath);
        if(settings==null || !settings.Validate(out string reason))throw new InvalidOperationException("설정 자산이 유효하지 않습니다.");
        wheelQueued=false;wheelChecked=false;groggyHudChecked=false;legacyBoss=null;legacyHud=null;legacySpawns=null;legacyServiceRoot=null;
        entranceChecked=false;entranceFinishedAt=-1f;nextEntranceFrame=0f;entranceFrame=0;entranceShots.Clear();entranceFrames.Clear();
        firstEntrance=null;skippedEntrance=null;externalInputOwner=null;skipQueued=false;interruptedActor=null;
        movie=null;SessionState.SetBool(Key+"movie",recordMovie);
        SessionState.SetString(Key+"output",directory);SessionState.SetBool(Key+"pending",true);
        File.WriteAllText(Path.Combine(directory,"play-start.json"),JsonConvert.SerializeObject(new{status="STARTING",utc=DateTime.UtcNow,
            sourceMaterialsDirty=EditorUtility.IsDirty(settings.materials),startScene=AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene)},Formatting.Indented));
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(directory,"IsolatedSave"));return "Started isolated Crustaspikan encounter verification";
    }
    private static void Changed(PlayModeStateChange change)
    {
        if(change==PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key+"pending",false))
        {
            SessionState.SetBool(Key+"pending",false);
            SessionState.SetBool(Key+"returnPending",true);
            SessionState.SetFloat(Key+"returnDeadline",(float)EditorApplication.timeSinceStartup+120f);
        }
    }
    private static void TryReturnAccount()
    {
        if(!SessionState.GetBool(Key+"returnPending",false))return;
        var path=SessionState.GetString(Key+"output","");
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            if(EditorApplication.timeSinceStartup>SessionState.GetFloat(Key+"returnDeadline",0f))
            {
                SessionState.SetBool(Key+"returnPending",false);
                SessionState.SetBool(Key+"movie",false);
                File.WriteAllText(Path.Combine(path,"editor-return.json"),JsonConvert.SerializeObject(new{status="DEFERRED_EDITOR_BUSY"},Formatting.Indented));
            }
            return;
        }
        var owned=Path.Combine(path,"IsolatedSave");
        string[] directories={IsolatedSavePlayGuard.ActiveDirectory,Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),
            SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","")};
        if(directories.Any(d=>!string.IsNullOrEmpty(d) && !string.Equals(Path.GetFullPath(d).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(owned).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)))
        {
            if(EditorApplication.timeSinceStartup>SessionState.GetFloat(Key+"returnDeadline",0f))
            {
                SessionState.SetBool(Key+"returnPending",false);
                SessionState.SetBool(Key+"movie",false);
                File.WriteAllText(Path.Combine(path,"editor-return.json"),JsonConvert.SerializeObject(new{status="DEFERRED_OTHER_OWNER",directories},Formatting.Indented));
            }
            return;
        }
        try
        {
                IsolatedSavePlayGuard.UseRealAccount();
                SessionState.SetBool(Key+"returnPending",false);
                SessionState.SetBool(Key+"movie",false);
                File.WriteAllText(Path.Combine(path,"editor-return.json"),JsonConvert.SerializeObject(new{
                    status="RETURNED",returnPending=false,
                    playing=EditorApplication.isPlaying,save=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),
                    active=IsolatedSavePlayGuard.ActiveDirectory,blocked=IsolatedSavePlayGuard.RequiresAccountChoice,
                    prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""),expires=SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires",""),
                    scenes=Enumerable.Range(0,UnityEngine.SceneManagement.SceneManager.sceneCount).Select(i=>{var s=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);return new{s.name,s.path,s.isDirty,roots=s.rootCount};}).ToArray(),
                    startScene=AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene),
                    backgroundBehavior=InputSystem.settings.backgroundBehavior.ToString(),runInBackground=Application.runInBackground},Formatting.Indented));
        }
        catch(Exception error)
        {
            SessionState.SetBool(Key+"returnPending",false);
            Debug.LogException(error);
        }
    }
    private static void Update()
    {
        TryReturnAccount();
        if(!EditorApplication.isPlaying || !SessionState.GetBool(Key+"pending",false))return;
        if(!started){started=true;output=SessionState.GetString(Key+"output","");movieOnly=SessionState.GetBool(Key+"movie",false);stage=0;stageAt=Time.realtimeSinceStartup;passed.Clear();}
        try{Tick();}catch(Exception error){Finish("FAIL",error.ToString());}
    }
    private static float Age=>Time.realtimeSinceStartup-stageAt;
    private static void Next(){stage++;stageAt=Time.realtimeSinceStartup;Write("RUNNING","");}
    private static void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);passed.Add(label);}
    private static void Tick()
    {
        if(Age>(movieOnly && stage==1?300f:65f))throw new TimeoutException("Stage "+stage+" timed out");
        var host=CrustaspikanEncounterHost.Current;
        switch(stage)
        {
            case 0:
                if(host?.Entrance==null || !host.CanEnter)return;
                player=PlayerContext.Instance.CurrentActor;oldCamera=Camera.main;originalHp=player.Health.CurrentHp;
                gameplayCamera=QuarterViewCamera.ActiveInstance;
                Check(gameplayCamera!=null && gameplayCamera.UsesCinemachine,"existing gameplay camera is ready");
                entryDistance=gameplayCamera.CurrentDistance;entryYaw=gameplayCamera.CurrentYaw;
                entryPitch=gameplayCamera.CurrentPitch;entryFov=oldCamera.fieldOfView;
                entryClearFlags=oldCamera.clearFlags;entryBackground=oldCamera.backgroundColor;
                entryBlend=gameplayCamera.CinemachineRig.Brain.DefaultBlend;entryPriority=gameplayCamera.CinemachineRig.VirtualCamera.Priority.Value;
                entryConfiner=gameplayCamera.CinemachineRig.Confiner.BoundingVolume;
                entryConfinerSlowing=gameplayCamera.CinemachineRig.Confiner.SlowingDistance;
                gameplayHudCanvases.Clear();
                foreach(var gameUi in UnityEngine.Object.FindObjectsByType<OverburstGameUI>(FindObjectsSortMode.None))
                {
                    var hudCanvas=gameUi.hud!=null?gameUi.hud.GetComponentInParent<Canvas>()?.rootCanvas:null;
                    if(hudCanvas==null)continue;
                    foreach(var canvas in hudCanvas.GetComponentsInChildren<Canvas>(true))
                        if(canvas.renderMode!=RenderMode.WorldSpace)gameplayHudCanvases[canvas]=canvas.enabled;
                }
                Check(gameplayHudCanvases.Count>0,"actual RPG player HUD and nested minimap canvases captured");
                targetHuds.Clear();
                foreach(var view in UnityEngine.Object.FindObjectsByType<EnemyTargetHpHud>(FindObjectsSortMode.None))
                    targetHuds.Add(new KeyValuePair<EnemyTargetHpHud,bool>(view,view.enabled));
                cameraDrivers.Clear();
                var oldBrain=oldCamera.GetComponent<Unity.Cinemachine.CinemachineBrain>();
                if(oldBrain!=null)cameraDrivers.Add(new KeyValuePair<Behaviour,bool>(oldBrain,oldBrain.enabled));
                foreach(var quarter in UnityEngine.Object.FindObjectsByType<QuarterViewCamera>(FindObjectsSortMode.None))
                    if(quarter.GetComponent<Camera>()==oldCamera || quarter.CinemachineRig?.Brain==oldBrain && oldBrain!=null)
                        cameraDrivers.Add(new KeyValuePair<Behaviour,bool>(quarter,quarter.enabled));
                originalLeases=EnemySpawnService.Current!=null?EnemySpawnService.Current.Pool.LeasedCount:0;
                Teleport(host.Entrance.transform.position+Vector3.back);entryPosition=player.transform.position;
                Check(host.Entrance.TryInteract(player)==InteractionExecutionResult.StartedTransition,"entrance uses shared interaction contract");
                encounter=host.ActiveEncounter;Check(encounter?.Brain!=null,"boss lease and encounter created");
                Check(encounter.Brain.Actor.Health.MaxHp==3000f,"boss HP 3000");
                Check(!encounter.Brain.Actor.GetComponent<EnemyTargetHpReporter>().enabled,"boss suppresses duplicate target HP display");
                Check(encounter.Brain.RuntimeMaterials.attacks.Length==16 && encounter.Brain.RuntimeMaterials.attacks.All(m=>m.IsValid),"16 current material clones valid");
                Check(encounter.Settings.patterns.Where(p=>p.id=="strong_spit" || p.id=="elite_throw" || p.id=="retreat_counter").All(p=>p.phaseMask==2),"strong spit elite and delayed counter introduced in phase two");
                Check(encounter.Brain.Composite.Patterns.IsValid && encounter.Brain.Composite.Patterns.spitPatterns.All(p=>encounter.Brain.RuntimeMaterials.Find(p.material.ability)==p.material),"native composite routes reference encounter clones");
                Check(player.Health.IsDeathFromDamagePrevented,"practice player death protection active");
                Check(Physics.Raycast(encounter.ArenaCenter+Vector3.right*15f+Vector3.up*10,Vector3.down,out var floor,20f) && floor.collider.name=="Arena Floor","circular arena has upward floor collider");
                Check(Camera.main==oldCamera && oldCamera.enabled,"same gameplay camera remains selected");
                Check(cameraDrivers.All(pair=>pair.Key!=null && pair.Key.enabled==pair.Value),"camera drivers remain enabled during battle");
                Check(Mathf.Abs(gameplayCamera.CurrentDistance-entryDistance)<.001f && Mathf.Abs(gameplayCamera.CurrentYaw-entryYaw)<.001f
                    && Mathf.Abs(gameplayCamera.CurrentPitch-entryPitch)<.001f && Mathf.Abs(oldCamera.fieldOfView-entryFov)<.001f,"entry preserves zoom yaw pitch and lens");
                Check(encounter.GetComponentsInChildren<Camera>(true).Length==0 && GameObject.Find("Crustaspikan Practice HUD")==null,"no extra battle camera or temporary health HUD");
                Check(encounter.BossHud!=null && ReferenceEquals(encounter.BossHud.BoundEncounterSource,encounter.Brain),"authored boss HUD binds current BT");
                Check(encounter.BossHud.IsVisible && Mathf.Abs(encounter.BossHud.DisplayedHealth01-1f)<.001f
                    && encounter.BossHud.DisplayedLitPhaseGems==2,"existing HP bar and phase gems show first phase");
                Check(targetHuds.All(pair=>!pair.Key.enabled),"overlapping ordinary target HUD is suspended");
                var portalKey=host.Entrance.GetComponentInChildren<WorldInteractionKeyPrompt>(true);
                Check(portalKey!=null && portalKey.HasUsableView && portalKey.KeyLabel=="F","portal reuses authored shared F keycap");
                firstEntrance=encounter.EntranceCinematic;
                Check(firstEntrance!=null && firstEntrance.IsPlaying,"portal entry starts actual entrance cinematic");
                if(movieOnly)
                {
                    var movieObject=new GameObject("Crustaspikan Entrance Movie Recorder");
                    movie=movieObject.AddComponent<CrustaspikanEncounterMovieRecorder>();
                    movie.Begin(Path.Combine(output,"Movie"),firstEntrance.Duration+1.2f);
                }
                Next();break;
            case 1:
                if(!entranceChecked)
                {
                    if(encounter.IsIntroducing)
                    {
                        CaptureEntrance();
                        return;
                    }
                    if(entranceFinishedAt<0f){entranceFinishedAt=Time.realtimeSinceStartup;return;}
                    if(Time.realtimeSinceStartup-entranceFinishedAt<.3f)return;
                    Check(firstEntrance.RoarStarted && !firstEntrance.WasSkipped,"full entrance completes authored roar without skipping");
                    Check(firstEntrance.RoarAudioStarted,"entrance uses existing authored roar audio");
                    Check(entranceShots.Contains(0) && entranceShots.Contains(1) && entranceShots.Contains(2) && entranceShots.Contains(3),"all four entrance camera shots captured");
                    Check(!GameplayInputBlocker.IsGameplayInputBlocked,"natural entrance completion returns gameplay input");
                    Check(encounter.BossHud.GetComponentInParent<Canvas>()!=null && encounter.BossHud.GetComponentInParent<Canvas>().enabled
                        && gameplayHudCanvases.All(pair=>pair.Key!=null && pair.Key.enabled==pair.Value),"authored boss player and nested minimap canvases return after entrance");
                    Check(firstEntrance.ArrivalStarted && firstEntrance.ImpactStarted,"boss breaches the ground and reaches actual arrival impact");
                    Check(firstEntrance.VisualRestored && firstEntrance.VisualOffset.sqrMagnitude<.00001f,"natural completion restores boss visual before combat");
                    Check(firstEntrance.RumbleAudioStarted && firstEntrance.ImpactAudioStarted,"arrival runs low rumble and synchronized heavy slam audio");
                    Check(encounter.GetComponentsInChildren<Renderer>().Where(r=>r.name=="1m Grid" || r.name=="5m Grid").All(r=>r.enabled)
                        && Mathf.Abs(encounter.GetComponentsInChildren<Light>().First(l=>l.name=="Arena Fill Light").intensity-18f)<.001f,"cinematic restores arena grid and original fill light");
                    var floorBlock=new MaterialPropertyBlock();encounter.GetComponentsInChildren<Renderer>().First(r=>r.name=="Arena Floor").GetPropertyBlock(floorBlock);
                    Check(floorBlock.isEmpty,"cinematic returns floor appearance without material edits");
                    var floorObject=encounter.GetComponentsInChildren<Renderer>().First(r=>r.name=="Arena Floor");
                    Check(firstEntrance.FloorGeometryRestored && floorObject.GetComponent<MeshFilter>().sharedMesh==floorObject.GetComponent<MeshCollider>().sharedMesh,"cutscene returns original visible floor and retains original collision mesh");
                    Check(Mathf.Abs(gameplayCamera.CurrentDistance-entryDistance)<.001f && Mathf.Abs(gameplayCamera.CurrentYaw-entryYaw)<.001f
                        && Mathf.Abs(gameplayCamera.CurrentPitch-entryPitch)<.001f && Mathf.Abs(oldCamera.fieldOfView-entryFov)<.001f,"entrance returns original combat distance yaw pitch and lens");
                    Check(oldCamera.clearFlags==entryClearFlags && Vector4.Distance(oldCamera.backgroundColor,entryBackground)<.001f,"cinematic backdrop restores original camera appearance");
                    Check(gameplayCamera.CinemachineRig.Brain.DefaultBlend.Style==entryBlend.Style
                        && Mathf.Abs(gameplayCamera.CinemachineRig.Brain.DefaultBlend.Time-entryBlend.Time)<.001f
                        && gameplayCamera.CinemachineRig.VirtualCamera.Priority.Value==entryPriority,"cinematic leaves authored blend policy and combat camera priority intact");
                    Check(UnityEngine.Object.FindObjectsByType<Unity.Cinemachine.CinemachineCamera>(FindObjectsSortMode.None)
                        .All(c=>!c.name.StartsWith("Crustaspikan Entrance")),"completed entrance returns temporary Cinemachine camera and lights");
                    File.WriteAllText(Path.Combine(output,"entrance-frames.json"),JsonConvert.SerializeObject(entranceFrames,Formatting.Indented));
                    entranceChecked=true;stageAt=Time.realtimeSinceStartup;
                }
                if(movieOnly)
                {
                    if(movie.Busy)return;
                    Check(string.IsNullOrEmpty(movie.Error),"high quality movie capture completes without frame or audio error");
                    Check(movie.FrameCount>=290 && movie.Width==1920 && movie.Height==1080,"movie contains consecutive actual Full HD frames at 30 fps");
                    Check(movie.AudioSamples>0 && movie.AudioPeak>.001f,"movie records actual native game audio output with sound");
                    Check(movie.ResourcesRestored,"movie returns native AudioRenderer and original capture timing");
                    Finish("PASS","");return;
                }
                if(!wheelQueued)
                {
                    Check(Mouse.current!=null,"mouse device available for real zoom input");
                    zoomBefore=gameplayCamera.CurrentDistance;
                    InputSystem.QueueDeltaStateEvent(Mouse.current.scroll,new Vector2(0,120));wheelQueued=true;return;
                }
                if(!wheelChecked && Age>1.5f)
                {
                    Check(gameplayCamera.CurrentDistance<zoomBefore-.05f,"mouse wheel changes existing camera distance in arena");
                    InputSystem.QueueDeltaStateEvent(Mouse.current.scroll,new Vector2(0,-120));wheelChecked=true;
                }
                if(Age>1f && Age<1.2f)ScreenCapture.CaptureScreenshot(Path.Combine(output,"arena-phase-one.png"));
                var executor=encounter.Brain.Actor.GetComponent<EnemyBossMaterialExecutor>();
                if(Age<10f || executor.ImpactCount+encounter.Brain.Composite.ReleaseCount<1 && Age<24f)return;
                Check(encounter.Brain.PatternCount>0 && executor.ImpactCount+encounter.Brain.Composite.ReleaseCount>0,"automatic BT starts and releases real attacks");
                var cachedAim = new SerializedObject(player.GetComponent<MeleeRuntime>()).FindProperty("attackCamera").objectReferenceValue as Camera;
                foreach (var offset in new[] {Vector3.forward*8f,Vector3.right*8f,Vector3.left*8f})
                {
                    var pointer=Camera.main.WorldToScreenPoint(player.transform.position+offset);
                    Check(MeleeAimCalculator.TryGetDirectionFromPlayer(player.transform,cachedAim,new Vector2(pointer.x,pointer.y),.01f,out var direction)
                        && Vector3.Dot(direction,offset.normalized)>.999f,"cached melee camera matches battle pointer direction "+offset);
                }
                var cachedMovement = new SerializedObject(player.Movement).FindProperty("movementCamera").objectReferenceValue as Transform;
                Check(Vector3.Dot(cachedMovement.forward,Camera.main.transform.forward)>.999f,"cached movement camera matches battle axes");
                // 자동 첫 공격이 남긴 재료별 쿨다운도 격리한다. 게임의 쿨다운 규칙은 우회하지 않는다.
                encounter.Restart();
                player.GetComponent<MeleeRuntime>().CancelCurrentAction();
                var sword=AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
                Check(player.Equipment.EquipWeaponItem(new ItemData(sword,1,ItemGrade.Common)),"isolated player equips actual greatsword");
                PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);player.GetComponent<MeleeRuntime>().SetManualInputEnabled(true);
                encounter.Brain.ReviewMode=true;
                Teleport(encounter.Brain.Actor.transform.position+encounter.Brain.Actor.transform.forward*9f);
                Check(encounter.Brain.StartPatternForReview("combo"),"review starts two-hit assembly");
                early=false;last=false;parries=player.GetComponent<PlayerParryController>()?.ParriedAttackCount??0;Next();break;
            case 2:
                var e=encounter.Brain.Actor.GetComponent<EnemyBossMaterialExecutor>();
                if(e.CurrentMaterial?.runtimeClip.name!="2HitComboAttack")return;
                var target=player.GetComponent<CombatTarget>();
                if(e.NormalizedTime>.2f && e.NormalizedTime<.34f){Check(!e.IsParryThreatTo(target),"first combo hit has no parry threat");early=true;}
                if(e.NormalizedTime>.35f && e.IsParryThreatTo(target))
                {
                    last=true;Check(early,"early-hit window sampled before final parry");
                    var result=player.GetComponent<MeleeRuntime>().TryStartHeavyAttack((encounter.Brain.Actor.transform.position-player.transform.position).normalized);
                    Check(result==WeaponActionResult.Accepted,"actual player heavy opens parry action");Next();
                }
                break;
            case 3:
                if(Age<1.2f)return;
                Check(last && player.GetComponent<PlayerParryController>().ParriedAttackCount>parries,"actual final-hit parry cancels boss execution");
                Check(encounter.Brain.Actor.GetComponent<EnemyBossCombatDirector>().ParryCount>0,"boss parry reception recorded");
                player.GetComponent<MeleeRuntime>().CancelCurrentAction();
                poise=encounter.Brain.Poise;
                Teleport(encounter.Brain.Actor.transform.position-encounter.Brain.Actor.transform.forward*5f);
                var heavy=new DamageInfo(1f,encounter.Brain.Actor.transform.position,player.gameObject,playerAttackKind:PlayerAttackKind.Heavy,sourceAttackSequenceId:20001);
                encounter.Brain.Actor.Health.TakeDamage(heavy);encounter.Brain.Actor.Health.TakeDamage(heavy);
                Check(Mathf.Abs(encounter.Brain.Poise-poise-12.5f)<.1f,"back heavy gains 12.5 poise once per attack sequence");
                for(int i=0;i<30;i++)encounter.Brain.Actor.Health.TakeDamage(new DamageInfo(1f,encounter.Brain.Actor.transform.position,player.gameObject,playerAttackKind:PlayerAttackKind.Heavy,sourceAttackSequenceId:21000+i));
                Check(encounter.Brain.IsGroggy && encounter.Brain.GroggyCount==1,"300 poise triggers one groggy and cancels assembly");
                Check(!encounter.Brain.Actor.AbilityController.IsExecuting,"groggy clears in-flight boss execution");Next();break;
            case 4:
                if(!groggyHudChecked && Age>.2f && encounter.Brain.IsGroggy)
                {
                    Check(Mathf.Abs(encounter.BossHud.DisplayedHealth01-encounter.Brain.Actor.Health.NormalizedHp)<.001f,"existing boss HP fill follows actual damage");
                    Check(encounter.BossHud.DisplayedGroggy01>.999f && encounter.BossHud.DisplayedState=="그로기","existing groggy UI shows 300 and groggy state");
                    ScreenCapture.CaptureScreenshot(Path.Combine(output,"arena-groggy-native-hud.png"));groggyHudChecked=true;
                }
                if(encounter.Brain.IsGroggy || Age<5.5f)return;
                Check(groggyHudChecked,"groggy HUD captured during actual opening");
                Check(encounter.Brain.Poise==0,"groggy resets after 4.5 seconds");
                encounter.Brain.Actor.Health.TakeDamage(new DamageInfo(1800f,encounter.Brain.Actor.transform.position,player.gameObject,triggersOnHitEffects:false));Next();break;
            case 5:
                if(encounter.Brain.Phase!=2 || encounter.Brain.IsTransitioning)return;
                Check(encounter.Brain.Observer.Frozen && encounter.Brain.Observer.Tactic==CrustaspikanTactic.Heavy,"phase two freezes observed heavy-hit tactic");
                Check(encounter.BossHud.DisplayedPhaseIndex==1 && encounter.BossHud.DisplayedLitPhaseGems==1,"existing phase gems follow BT phase two");
                Check(Mathf.Abs(encounter.BossHud.DisplayedGroggy01)<.001f,"existing groggy UI clears after opening");
                encounter.ClearSummons();
                Teleport(encounter.Brain.Actor.transform.position+encounter.Brain.Actor.transform.forward*10f);
                Check(encounter.Brain.StartPatternForReview("weak_spit"),"weak spit assembly starts");impacts=encounter.TotalAddsSpawned;Next();break;
            case 6:
                if(encounter.TotalAddsSpawned<impacts+3 || Age<5.5f)return;
                Check(encounter.AliveAdds<=encounter.Settings.maximumAdds,"summon cap holds");
                Check(encounter.GetComponentsInChildren<EnemyActor>().Count(a=>a!=encounter.Brain.Actor && a.AI.enabled)>=3,"ejected small actors land and enable actual AI");
                Check(encounter.GetComponentsInChildren<EnemyActor>().Where(a=>a!=encounter.Brain.Actor).All(a=>!a.GetComponent<EnemyLootDropper>().enabled),"native landed adds have loot disabled");
                encounter.ClearSummons();
                Check(encounter.Brain.StartPatternForReview("strong_spit"),"strong spit assembly starts");impacts=encounter.TotalAddsSpawned;movedOut=false;Next();break;
            case 7:
                if(!movedOut && encounter.Brain.Composite.CurrentMaterial?.runtimeClip.name=="SpitterShot1" && encounter.Brain.Composite.NormalizedTime>.05f)
                {Teleport(encounter.ArenaCenter+Vector3.right*16f+Vector3.up*.1f);movedOut=true;}
                if(encounter.TotalAddsSpawned<impacts+3 || Age<5.5f)return;
                Check(encounter.TotalAddsSpawned>=5,"strong spit adds small and medium actors");
                Check(encounter.GetComponentsInChildren<EnemyActor>().Any(a=>a.Definition.EnemyId=="CavernMutants_Gasterobrach"),"strong native spit includes authored medium species");
                Check(encounter.Brain.StartPatternForReview("elite_throw"),"phase two elite assembly starts");Next();break;
            case 8:
                if(encounter.EliteThrows<1 || Age<10f)return;
                Check(encounter.EliteThrows==1,"held elite releases on actual throw impact");
                Check(encounter.GetComponentsInChildren<EnemyActor>().Any(a=>a!=encounter.Brain.Actor && a.Definition.EnemyId=="CavernMutants_Ursacetus" && a.AI.enabled),"thrown elite lands and fights");
                Check(encounter.AliveAdds<=6,"small medium elite retain cap six");
                var settings=host.Settings;Check(!EditorUtility.IsDirty(settings.materials) && settings.materials.attacks.All(m=>!EditorUtility.IsDirty(m)),"source material assets remain clean");
                ScreenCapture.CaptureScreenshot(Path.Combine(output,"arena-phase-two.png"));
                Next();break;
            case 9:
                // 캡처를 위한 5초 여유. 이후 재시작/왕복의 풀 수명을 검사한다.
                if(Age<5f)return;
                encounter.Restart();Check(encounter.Brain.Phase==1 && encounter.Brain.Poise==0 && !encounter.Brain.Observer.Frozen,"restart resets phase poise observation");
                Check(ReferenceEquals(encounter.BossHud.BoundEncounterSource,encounter.Brain) && encounter.BossHud.IsVisible
                    && encounter.BossHud.DisplayedHealth01>.999f && encounter.BossHud.DisplayedLitPhaseGems==2,"restart rebinds existing boss HUD to new lease");
                Check(encounter.AliveAdds==0 && encounter.Brain.Actor.Health.CurrentHp==3000,"restart releases summons and refills boss");
                player.GetComponent<MeleeRuntime>().CancelCurrentAction();
                dodgeStart=encounter.Brain.Actor.transform.position;dodgeRequested=false;
                Teleport(dodgeStart+encounter.Brain.Actor.transform.forward*6f);Next();break;
            case 10:
                if(Age<1.7f)return;
                if(!dodgeRequested)
                {
                    var result=player.GetComponent<MeleeRuntime>().TryStartHeavyAttack((encounter.Brain.Actor.transform.position-player.transform.position).normalized);
                    Check(result==WeaponActionResult.Accepted,"visible heavy starts after landing during idle boundary: "+result);dodgeRequested=true;return;
                }
                if(Age<4f)return;
                Check(encounter.Brain.DodgeCount>0 && Vector3.Distance(encounter.Brain.Actor.transform.position,dodgeStart)>.3f,"boss observes heavy and physically retreats");
                player.GetComponent<MeleeRuntime>().CancelCurrentAction();
                dropCount=UnityEngine.Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Length+UnityEngine.Object.FindObjectsByType<CurrencyWorldPickup>(FindObjectsSortMode.None).Length;
                encounter.Brain.Actor.Health.TakeDamage(new DamageInfo(10000f,encounter.Brain.Actor.transform.position,player.gameObject,triggersOnHitEffects:false));Next();break;
            case 11:
                if(Age<.6f)return;
                Check(encounter.Defeated,"boss death ends selection and announces defeat");
                Check(!encounter.BossHud.IsVisible && encounter.BossHud.DisplayedHealth01==0 && encounter.BossHud.DisplayedGroggy01==0,"defeated HUD stays hidden after pooled health reset");
                ScreenCapture.CaptureScreenshot(Path.Combine(output,"arena-defeated-native-hud.png"));
                Check(UnityEngine.Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Length+UnityEngine.Object.FindObjectsByType<CurrencyWorldPickup>(FindObjectsSortMode.None).Length==dropCount,"practice boss death grants no item or currency drops");
                encounter.Exit(true);Next();break;
            case 12:
                if(Age<1.5f)return;
                Check(host.ActiveEncounter==null && host.Entrance.gameObject.activeSelf,"return reactivates entrance");
                Check(Vector3.Distance(player.transform.position,entryPosition)<.2f,"return restores entry position");
                Check(Camera.main==oldCamera && oldCamera.enabled,"return restores main camera");
                Check(cameraDrivers.All(pair=>pair.Key!=null && pair.Key.enabled==pair.Value),"return restores original camera drivers");
                Check(gameplayCamera.CinemachineRig.Confiner.BoundingVolume==entryConfiner
                    && Mathf.Abs(gameplayCamera.CinemachineRig.Confiner.SlowingDistance-entryConfinerSlowing)<.001f,"return restores hideout camera volume");
                Check(targetHuds.All(pair=>pair.Key!=null && pair.Key.enabled==pair.Value),"return restores ordinary target HUD state");
                Check(UnityEngine.Object.FindObjectsByType<EnemyBossHudView>(FindObjectsSortMode.None).All(v=>v.BoundEncounterSource==null),"return clears BT HUD subscriptions");
                Check(!player.Health.IsDeathFromDamagePrevented,"encounter death protection released");
                Check((EnemySpawnService.Current!=null?EnemySpawnService.Current.Pool.LeasedCount:0)==originalLeases,"owned actors returned to shared pool");
                Check(player.Health.CurrentHp>=originalHp,"practice damage healed on return");
                legacySpawns=EnemySpawnService.Current;
                if(legacySpawns==null)
                {
                    legacyServiceRoot=new GameObject("Crustaspikan Legacy HUD Verification Services");
                    var inactive=new GameObject("Inactive Actors");inactive.transform.SetParent(legacyServiceRoot.transform,false);inactive.SetActive(false);
                    var pool=legacyServiceRoot.AddComponent<EnemyPoolService>();pool.Configure(inactive.transform,0);
                    legacySpawns=legacyServiceRoot.AddComponent<EnemySpawnService>();legacySpawns.Configure(host.Settings.materials.catalog,pool);
                }
                Check(legacySpawns.TrySpawn(new EnemySpawnRequest(host.Settings.materials.actorDefinition,
                    player.transform.position+Vector3.forward*8f,Quaternion.identity,targetTransform:player.transform,context:EncounterContext.Test),out legacyBoss),"legacy boss spawns with restored native phase controller");
                legacyHud=UnityEngine.Object.FindObjectsByType<EnemyBossHudView>(FindObjectsSortMode.None).FirstOrDefault(v=>v.BoundBoss==legacyBoss.BossPhaseController);
                Check(legacyHud!=null && legacyHud.BoundEncounterSource==null,"existing registry still binds ordinary boss HUD");
                Next();break;
            case 13:
                if(Age<.5f)return;
                Check(Mathf.Abs(legacyHud.DisplayedHealth01-legacyBoss.Health.NormalizedHp)<.001f,"ordinary boss HUD displays native health after BT encounter");
                legacyBoss.Health.TakeDamage(new DamageInfo(1f,legacyBoss.transform.position,player.gameObject,triggersOnHitEffects:false));
                Check(Mathf.Abs(legacyHud.DisplayedHealth01-legacyBoss.Health.NormalizedHp)<.001f,"ordinary boss HUD retains health event updates");
                legacySpawns.Release(legacyBoss);legacyBoss=null;
                Check(legacyHud.BoundBoss==null && !legacyHud.IsVisible,"ordinary boss registry unbinds HUD on pool return");
                Check(legacySpawns.Pool.LeasedCount==originalLeases,"legacy HUD check returns its native boss lease");
                Next();break;
            case 14:
                if(Age<.5f)return;
                Teleport(host.Entrance.transform.position+Vector3.back);
                Check(host.Entrance.TryInteract(player)==InteractionExecutionResult.StartedTransition,"second portal entry starts cinematic again");
                encounter=host.ActiveEncounter;skippedEntrance=encounter.EntranceCinematic;
                Check(skippedEntrance!=null && skippedEntrance.IsPlaying,"repeat entry owns fresh entrance director");
                Check(Keyboard.current!=null,"keyboard available for actual cinematic skip input");
                Next();break;
            case 15:
                if(!skipQueued)
                {
                    if(Age<encounter.Settings.entrance.detailSeconds+.45f)return;
                    Check(skippedEntrance.ArrivalStarted && skippedEntrance.VisualOffset.y<-1f,"skip exercised while boss is partway out of ground");
                    InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(UnityEngine.InputSystem.Key.Space));skipQueued=true;stageAt=Time.realtimeSinceStartup;return;
                }
                InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());
                if(Age<.6f)return;
                Check(skippedEntrance.WasSkipped && !encounter.IsIntroducing,"actual space input skips entrance");
                Check(skippedEntrance.VisualRestored && skippedEntrance.VisualOffset.sqrMagnitude<.00001f,"mid-emergence skip restores visible boss at ground level");
                Check(skippedEntrance.FloorGeometryRestored,"mid-emergence skip closes cinematic floor opening");
                Check(!GameplayInputBlocker.IsGameplayInputBlocked && encounter.BossHud.GetComponentInParent<Canvas>()!=null
                    && encounter.BossHud.GetComponentInParent<Canvas>().enabled
                    && gameplayHudCanvases.All(pair=>pair.Key!=null && pair.Key.enabled==pair.Value),"skip returns input and actual authored HUD including minimap");
                Check(Camera.main==oldCamera && oldCamera.clearFlags==entryClearFlags
                    && gameplayCamera.CinemachineRig.Brain.ActiveVirtualCamera==gameplayCamera.CinemachineRig.VirtualCamera,"skip returns same output camera and native combat virtual camera");
                encounter.Exit(true);Next();break;
            case 16:
                if(Age<.5f)return;
                Teleport(host.Entrance.transform.position+Vector3.back);
                Check(host.Entrance.TryInteract(player)==InteractionExecutionResult.StartedTransition,"third entry starts interruption check");
                encounter=host.ActiveEncounter;
                interruptedActor=encounter.Brain.Actor;interruptedVisualRest=interruptedActor.VisualRoot.localPosition-encounter.EntranceCinematic.VisualOffset;
                externalInputOwner=new GameObject("Crustaspikan External Input Owner Verification");GameplayInputBlocker.Block(externalInputOwner);
                InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(UnityEngine.InputSystem.Key.F9));Next();break;
            case 17:
                InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());
                if(Age<.6f)return;
                Check(host.ActiveEncounter==null,"actual F9 returns during entrance without waiting for movie");
                Check(interruptedActor!=null && Vector3.Distance(interruptedActor.VisualRoot.localPosition,interruptedVisualRest)<.001f,"buried boss returns original visual pose before pool release");
                Check(GameplayInputBlocker.IsGameplayInputBlocked,"interrupted entrance preserves another input owner's blocker");
                GameplayInputBlocker.Unblock(externalInputOwner);UnityEngine.Object.Destroy(externalInputOwner);externalInputOwner=null;
                Check(!GameplayInputBlocker.IsGameplayInputBlocked,"own and verification input leases are returned");
                Check(oldCamera.clearFlags==entryClearFlags && Vector4.Distance(oldCamera.backgroundColor,entryBackground)<.001f,"interrupted entrance restores camera appearance");
                Check(UnityEngine.Object.FindObjectsByType<Unity.Cinemachine.CinemachineCamera>(FindObjectsSortMode.None)
                    .All(c=>!c.name.StartsWith("Crustaspikan Entrance")),"interruption destroys cinematic virtual cameras");
                Check(UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).All(l=>!l.name.StartsWith("Entrance "))
                    && GameObject.Find("Crustaspikan Entrance Letterbox")==null,"interruption returns cinematic lights and title canvas");
                Check(UnityEngine.Resources.FindObjectsOfTypeAll<Material>().All(m=>!m.name.StartsWith("Crustaspikan Entrance"))
                    && UnityEngine.Resources.FindObjectsOfTypeAll<Texture2D>().All(t=>t.name!="Crustaspikan Entrance Soft Dust"),"repeated entrances return owned particle materials and texture");
                Check(UnityEngine.Resources.FindObjectsOfTypeAll<Mesh>().All(m=>!m.name.StartsWith("Crustaspikan Entrance"))
                    && GameObject.Find("Entrance Flying Stone 0")==null,"repeat entrances release torn-ground mesh and flying stones");
                Check(gameplayHudCanvases.All(pair=>pair.Key!=null && pair.Key.enabled==pair.Value),"interruption restores all authored gameplay HUD canvases");
                Check(UnityEngine.Object.FindObjectsByType<EnemyBossHudView>(FindObjectsSortMode.None).All(v=>v.BoundEncounterSource==null),"repeat entrances leave no stale BT HUD binding");
                Finish("PASS","");break;
        }
    }
    private static void CaptureEntrance()
    {
        var intro=encounter.EntranceCinematic;
        Check(GameplayInputBlocker.IsGameplayInputBlocked,"entrance owns gameplay input lock");
        Check(intro.HiddenCanvasCount>0 && gameplayHudCanvases.All(pair=>pair.Key!=null && !pair.Key.enabled),"entrance suppresses actual player HUD and nested minimap canvas");
        Check(encounter.Brain.PatternCount==0 && encounter.AliveAdds==0,"BT attacks and summons stay paused throughout entrance");
        Check(Camera.main==oldCamera && cameraDrivers.All(pair=>pair.Key!=null && pair.Key.enabled==pair.Value),"entrance uses same output camera with native drivers enabled");
        Check(Mathf.Abs(gameplayCamera.CurrentDistance-entryDistance)<.001f && Mathf.Abs(gameplayCamera.CurrentYaw-entryYaw)<.001f,"entrance never rewrites combat zoom or yaw");
        if(intro.Elapsed>=nextEntranceFrame)
        {
            string folder=Path.Combine(output,"EntranceFrames");Directory.CreateDirectory(folder);
            string name="intro-"+(entranceFrame++).ToString("D4")+".png";
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,name));
            entranceFrames.Add(new{name,elapsed=intro.Elapsed,shot=intro.ShotIndex,visualOffset=intro.VisualOffset.y,
                arrival=intro.ArrivalStarted,impact=intro.ImpactStarted,impactAt=intro.ImpactElapsed,roar=intro.RoarStarted,
                roarAudio=intro.RoarAudioStarted,rumbleAudio=intro.RumbleAudioStarted,impactAudio=intro.ImpactAudioStarted,
                audioPlaying=intro.GetComponentsInChildren<AudioSource>().Count(s=>s.isPlaying)});nextEntranceFrame=intro.Elapsed+.125f;
        }
        float roarLength=encounter.Brain.RuntimeMaterials.FindMotion(encounter.Settings.entrance.roarMotion).runtime.length;
        float shotAge=intro.ShotIndex==0?intro.Elapsed:intro.ShotIndex==1?intro.Elapsed-encounter.Settings.entrance.detailSeconds
            :intro.ShotIndex==2?intro.Elapsed-intro.RoarStart:intro.Elapsed-intro.RevealStart;
        if(intro.ShotIndex<4 && shotAge>(intro.ShotIndex==2?1.15f:.6f) && entranceShots.Add(intro.ShotIndex))
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"entrance-shot-"+intro.ShotIndex+".png"));
        if(intro.ShotIndex==2 && shotAge>.3f && shotAge<roarLength-.3f)
        {
            Check(intro.RoarStarted && encounter.Brain.Actor.Animator.GetCurrentAnimatorStateInfo(0).IsName("Material_"+encounter.Settings.entrance.roarMotion),"cinematic actually runs authored non-RM roar animation");
            Check(encounter.Brain.Actor.GetComponent<EnemyBossMaterialExecutor>().DamageCount==0,"entrance roar releases no attack damage");
        }
        if(intro.ShotIndex==0 && intro.Elapsed>.5f)
            Check(intro.VisualOffset.y<-8f,"boss remains buried during initial ground tremor");
        if(intro.ArrivalStarted && !intro.ImpactStarted && intro.Elapsed>encounter.Settings.entrance.detailSeconds+.1f)
            Check(encounter.Brain.Actor.Animator.GetCurrentAnimatorStateInfo(0).IsName("Material_"+encounter.Settings.entrance.arrivalMotion),"arrival uses existing non-RM two-hand motion without attacks");
        if(intro.ImpactStarted && !intro.RoarStarted && intro.Elapsed<intro.ImpactElapsed+.18f)
        {
            float normalized=encounter.Brain.Actor.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            Check(normalized>=encounter.Settings.entrance.arrivalImpactNormalized && normalized<encounter.Settings.entrance.arrivalImpactNormalized+.12f,"ground impact follows actual arrival animation contact");
            Check(intro.GetComponentsInChildren<ParticleSystem>().Any(p=>p.particleCount>0),"ground rupture emits actual atmospheric particles");
        }
        Check(intro.GetComponentsInChildren<TMPro.TextMeshProUGUI>().All(t=>t.text.Contains("건너뛰기")),"cutscene leaves image free of introductory title cards");
    }
    private static void Teleport(Vector3 position)
    {bool active=player.CharacterController.enabled;player.CharacterController.enabled=false;player.transform.position=position;player.CharacterController.enabled=active;player.Movement.ResetMotionAfterTeleport();Physics.SyncTransforms();}
    private static void Write(string status,string error)
    {
        File.WriteAllText(Path.Combine(output,"play-result.json"),JsonConvert.SerializeObject(new{status,stage,utc=DateTime.UtcNow,checks=passed.Distinct().ToArray(),error,
            snapshot=encounter?.Brain==null?null:new{phase=encounter.Brain.Phase,state=encounter.Brain.State,patterns=encounter.Brain.PatternCount,
                poise=encounter.Brain.Poise,groggies=encounter.Brain.GroggyCount,adds=encounter.AliveAdds,totalAdds=encounter.TotalAddsSpawned,eliteThrows=encounter.EliteThrows,
                baseFailure=encounter.Brain.Actor.GetComponent<EnemyBossMaterialExecutor>().LastFailure,
                compositeFailure=encounter.Brain.Composite.LastFailure}},Formatting.Indented));
    }
    private static void Finish(string status,string error)
    {
        started=false;
        try
        {
        if(movie!=null){movie.Cancel();UnityEngine.Object.Destroy(movie.gameObject);movie=null;}
        Write(status,error);if(encounter!=null)encounter.Exit(true);
        if(legacyBoss!=null && legacyBoss.IsLeased && legacySpawns!=null)legacySpawns.Release(legacyBoss);
        legacyBoss=null;
        if(legacyServiceRoot!=null)UnityEngine.Object.Destroy(legacyServiceRoot);
        legacyServiceRoot=null;legacySpawns=null;
        if(externalInputOwner!=null){GameplayInputBlocker.Unblock(externalInputOwner);UnityEngine.Object.Destroy(externalInputOwner);externalInputOwner=null;}
        if(Keyboard.current!=null)InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());
        }
        finally { EditorApplication.ExitPlaymode(); }
    }
}

// Editor 전용 고정 간격 촬영. Game View와 Unity의 실제 오디오 출력을 같은 프레임 단위로 저장한다.
public sealed class CrustaspikanEncounterMovieRecorder : MonoBehaviour
{
    private const int FramesPerSecond=30;
    public bool Busy {get;private set;}
    public string Error {get;private set;}
    public int FrameCount {get;private set;}
    public int Width {get;private set;}
    public int Height {get;private set;}
    public long AudioSamples {get;private set;}
    public float AudioPeak {get;private set;}
    public bool ResourcesRestored {get;private set;}
    private float previousCaptureDelta,seconds,startTime;
    private int channels,sampleRate;
    private bool ownsAudio,timingCaptured;
    private string directory;
    private BinaryWriter audio;
    private readonly List<object> frames=new List<object>();
    public void Begin(string path,float duration)
    {
        if(Busy)throw new InvalidOperationException("Movie recorder already active");
        directory=path;seconds=duration;Directory.CreateDirectory(directory);
        channels=AudioSettings.speakerMode==AudioSpeakerMode.Mono?1:AudioSettings.speakerMode==AudioSpeakerMode.Stereo?2:0;
        if(channels==0)throw new InvalidOperationException("Movie capture requires existing mono or stereo output");
        sampleRate=AudioSettings.outputSampleRate;
        previousCaptureDelta=Time.captureDeltaTime;timingCaptured=true;Time.captureDeltaTime=1f/FramesPerSecond;
        try
        {
            ownsAudio=AudioRenderer.Start();
            if(!ownsAudio)throw new InvalidOperationException("Native AudioRenderer is unavailable or already owned");
            audio=new BinaryWriter(File.Open(Path.Combine(directory,"game-audio.f32"),FileMode.Create,FileAccess.Write,FileShare.Read));
            Busy=true;startTime=Time.unscaledTime;StartCoroutine(Record());
        }
        catch{Release("FAILED");throw;}
    }
    private System.Collections.IEnumerator Record()
    {
        var end=new WaitForEndOfFrame();
        // unscaledTime은 PNG 저장 중에도 흐르므로 실제 촬영 프레임 수로 종료한다.
        int targetFrames=Mathf.CeilToInt(seconds*FramesPerSecond);
        while(Busy && FrameCount<targetFrames)
        {
            yield return end;
            Texture2D frame=null;
            try
            {
                frame=ScreenCapture.CaptureScreenshotAsTexture();
                if(FrameCount==0){Width=frame.width;Height=frame.height;}
                if(frame.width!=1920 || frame.height!=1080)throw new InvalidOperationException("Game View must stay at 1920x1080 during recording");
                string file="frame-"+FrameCount.ToString("D5")+".png";
                File.WriteAllBytes(Path.Combine(directory,file),frame.EncodeToPNG());
                int sampleCount=AudioRenderer.GetSampleCountForCaptureFrame();
                if(sampleCount<=0)throw new InvalidOperationException("Native audio frame contains no samples");
                using(var buffer=new Unity.Collections.NativeArray<float>(sampleCount*channels,Unity.Collections.Allocator.Temp))
                {
                    if(!AudioRenderer.Render(buffer))throw new InvalidOperationException("Native audio frame failed to render");
                    for(int i=0;i<buffer.Length;i++){AudioPeak=Mathf.Max(AudioPeak,Mathf.Abs(buffer[i]));audio.Write(buffer[i]);}
                    AudioSamples+=sampleCount;
                }
                frames.Add(new{index=FrameCount,file,gameFrame=Time.frameCount,t=(FrameCount+1)/(float)FramesPerSecond,
                    unscaledElapsed=Time.unscaledTime-startTime,samples=sampleCount});FrameCount++;
            }
            catch(Exception error){Error=error.ToString();Release("FAILED");}
            finally{if(frame!=null)Destroy(frame);}
        }
        if(Busy)Release("COMPLETE");
    }
    private void Release(string status)
    {
        Busy=false;
        bool stopped=!ownsAudio;
        try{audio?.Dispose();audio=null;if(ownsAudio)stopped=AudioRenderer.Stop();}
        finally{ownsAudio=false;if(timingCaptured)Time.captureDeltaTime=previousCaptureDelta;timingCaptured=false;ResourcesRestored=stopped;}
        if(!string.IsNullOrEmpty(directory))File.WriteAllText(Path.Combine(directory,"capture.json"),JsonConvert.SerializeObject(new{
            status,error=Error,width=Width,height=Height,fps=FramesPerSecond,frameCount=FrameCount,channels,sampleRate,audioSamples=AudioSamples,
            audioPeak=AudioPeak,videoSeconds=FrameCount/(float)FramesPerSecond,audioSeconds=AudioSamples/(double)sampleRate,
            resourcesRestored=ResourcesRestored,captureDeltaRestored=Time.captureDeltaTime,frames},Formatting.Indented));
    }
    public void Cancel(){if(Busy || ownsAudio || timingCaptured)Release("CANCELLED");}
    private void OnDisable()=>Cancel();
    private void OnDestroy()=>Cancel();
}
