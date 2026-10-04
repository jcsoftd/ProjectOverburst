using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

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
    private static readonly List<KeyValuePair<Behaviour,bool>> cameraDrivers=new List<KeyValuePair<Behaviour,bool>>();
    static CrustaspikanEncounterPlayVerifier(){EditorApplication.update+=Update;EditorApplication.playModeStateChanged+=Changed;}
    public static string Start(string directory)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)throw new InvalidOperationException("Editor가 유휴 상태여야 합니다.");
        directory=IsolatedSavePlayGuard.ValidateDirectory(directory);Directory.CreateDirectory(directory);
        var settings=AssetDatabase.LoadAssetAtPath<CrustaspikanEncounterSettings>(CrustaspikanEncounterBuilder.AssetPath);
        if(settings==null || !settings.Validate(out string reason))throw new InvalidOperationException("설정 자산이 유효하지 않습니다.");
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
            EditorApplication.delayCall+=()=>{
                if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)return;
                IsolatedSavePlayGuard.UseRealAccount();
                var path=SessionState.GetString(Key+"output","");
                File.WriteAllText(Path.Combine(path,"editor-return.json"),JsonConvert.SerializeObject(new{
                    playing=EditorApplication.isPlaying,save=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),
                    active=IsolatedSavePlayGuard.ActiveDirectory,blocked=IsolatedSavePlayGuard.RequiresAccountChoice,
                    prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""),expires=SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires",""),
                    scenes=Enumerable.Range(0,UnityEngine.SceneManagement.SceneManager.sceneCount).Select(i=>{var s=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);return new{s.name,s.path,s.isDirty,roots=s.rootCount};}).ToArray(),
                    startScene=AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene)},Formatting.Indented));
            };
        }
    }
    private static void Update()
    {
        if(!EditorApplication.isPlaying || !SessionState.GetBool(Key+"pending",false))return;
        if(!started){started=true;output=SessionState.GetString(Key+"output","");stage=0;stageAt=Time.realtimeSinceStartup;passed.Clear();}
        try{Tick();}catch(Exception error){Finish("FAIL",error.ToString());}
    }
    private static float Age=>Time.realtimeSinceStartup-stageAt;
    private static void Next(){stage++;stageAt=Time.realtimeSinceStartup;Write("RUNNING","");}
    private static void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);passed.Add(label);}
    private static void Tick()
    {
        if(Age>65f)throw new TimeoutException("Stage "+stage+" timed out");
        var host=CrustaspikanEncounterHost.Current;
        switch(stage)
        {
            case 0:
                if(host?.Entrance==null || !host.CanEnter)return;
                player=PlayerContext.Instance.CurrentActor;oldCamera=Camera.main;originalHp=player.Health.CurrentHp;
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
                Check(Camera.main!=oldCamera,"battle camera selected");Next();break;
            case 1:
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
                if(encounter.Brain.IsGroggy || Age<5.5f)return;
                Check(encounter.Brain.Poise==0,"groggy resets after 4.5 seconds");
                encounter.Brain.Actor.Health.TakeDamage(new DamageInfo(1800f,encounter.Brain.Actor.transform.position,player.gameObject,triggersOnHitEffects:false));Next();break;
            case 5:
                if(encounter.Brain.Phase!=2 || encounter.Brain.IsTransitioning)return;
                Check(encounter.Brain.Observer.Frozen && encounter.Brain.Observer.Tactic==CrustaspikanTactic.Heavy,"phase two freezes observed heavy-hit tactic");
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
                Check(UnityEngine.Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Length+UnityEngine.Object.FindObjectsByType<CurrencyWorldPickup>(FindObjectsSortMode.None).Length==dropCount,"practice boss death grants no item or currency drops");
                encounter.Exit(true);Next();break;
            case 12:
                if(Age<1.5f)return;
                Check(host.ActiveEncounter==null && host.Entrance.gameObject.activeSelf,"return reactivates entrance");
                Check(Vector3.Distance(player.transform.position,entryPosition)<.2f,"return restores entry position");
                Check(Camera.main==oldCamera && oldCamera.enabled,"return restores main camera");
                Check(cameraDrivers.All(pair=>pair.Key!=null && pair.Key.enabled==pair.Value),"return restores original camera drivers");
                Check(!player.Health.IsDeathFromDamagePrevented,"encounter death protection released");
                Check((EnemySpawnService.Current!=null?EnemySpawnService.Current.Pool.LeasedCount:0)==originalLeases,"owned actors returned to shared pool");
                Check(player.Health.CurrentHp>=originalHp,"practice damage healed on return");Finish("PASS","");break;
        }
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
        Write(status,error);if(encounter!=null)encounter.Exit(true);
        started=false;EditorApplication.ExitPlaymode();
    }
}
