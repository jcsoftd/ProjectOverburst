using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Uses actual runtime updates, serialized debug buttons, spawn service and pool. No asset writes.
[InitializeOnLoad]
public static class MonsterThemePlayVerifier
{
    private const string Key="MonsterThemePlayVerifier";
    private static readonly Stack<IEnumerator> work=new Stack<IEnumerator>();
    private static readonly List<string> passed=new List<string>(),errors=new List<string>();
    private static EnemyThemeDebugUI ui;
    private static PlayerInputFacade player;
    private static CombatHealth health;
    private static Vector3 center;
    private static int lastFrame;
    private static double deadline;
    private static bool background;
    private static int frameRate;
    public static string LastResult=>SessionState.GetString(Key+".result","NOT_RUN");
    static MonsterThemePlayVerifier(){EditorApplication.playModeStateChanged+=Changed;}
    [MenuItem("OVERBURST/Enemies/Themes/Validate Play Mode")]
    public static void Run()
    { Start(false); }
    [MenuItem("OVERBURST/Enemies/Themes/Validate Safety Play Mode")]
    public static void RunSafety() { Start(true); }
    [MenuItem("OVERBURST/Enemies/Themes/Validate Review Play Mode")]
    public static void RunReview() { Start(false,true); }
    [MenuItem("OVERBURST/Enemies/Themes/Validate Scene Transition Play Mode")]
    public static void RunTransition() { Start(false,false,true); }
    [MenuItem("OVERBURST/Enemies/Themes/Validate Arena Survival Play Mode")]
    public static void RunSurvival() { Start(false,survival:true); }
    [MenuItem("OVERBURST/Enemies/Themes/Validate Locomotion Play Mode")]
    public static void RunLocomotion() { Start(false,locomotion:true); }
    [MenuItem("OVERBURST/Enemies/Themes/Validate Crowd Motion Play Mode")]
    public static void RunCrowdMotion() { Start(false,crowdMotion:true); }
    [MenuItem("OVERBURST/Enemies/Themes/Validate Facing Play Mode")]
    public static void RunFacing() { Start(false,facing:true); }
    [MenuItem("OVERBURST/Enemies/Themes/Validate Fine Turns Play Mode")]
    public static void RunFineTurns() { Start(false,fineTurns:true); }
    [MenuItem("OVERBURST/Enemies/Themes/Capture Authored Turns")]
    public static void RunTurnReview() { Start(false,turnReview:true); }
    private static void Start(bool safety,bool review=false,bool transition=false,bool survival=false,bool locomotion=false,bool crowdMotion=false,bool facing=false,bool fineTurns=false,bool turnReview=false)
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode,"Already playing");
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        Check(scene.name==PersistentSceneFlow.PersistentSceneName && !scene.isDirty,"Requires saved PersistentScene");
        SessionState.SetBool(Key+".survival",survival);
        SessionState.SetBool(Key+".facing",facing);
        SessionState.SetBool(Key+".fineTurns",fineTurns);
        SessionState.SetBool(Key+".turnReview",turnReview);
        SessionState.SetBool(Key+".locomotion",locomotion);
        SessionState.SetBool(Key+".crowdMotion",crowdMotion);
        SessionState.SetBool(Key+".transition",transition);SessionState.SetBool(Key+".review",review);SessionState.SetBool(Key+".safety",safety);SessionState.SetBool(Key,true);SessionState.SetString(Key+".result","RUNNING");EditorApplication.EnterPlaymode();
    }
    private static void Changed(PlayModeStateChange state)
    {
        if(!SessionState.GetBool(Key,false))return;
        if(state==PlayModeStateChange.EnteredPlayMode)
        {
            background=Application.runInBackground;frameRate=Application.targetFrameRate;Application.runInBackground=true;Application.targetFrameRate=60;
            passed.Clear();errors.Clear();work.Clear();work.Push(Verify());lastFrame=-1;deadline=EditorApplication.timeSinceStartup+900;
            Application.logMessageReceived+=Log;EditorApplication.update+=Tick;
        }
        if(state==PlayModeStateChange.ExitingPlayMode)
        {
            Application.logMessageReceived-=Log;EditorApplication.update-=Tick;Application.runInBackground=background;Application.targetFrameRate=frameRate;
            if(LastResult=="RUNNING")SessionState.SetString(Key+".result","FAIL interrupted");
        }
        if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);Debug.Log("[MonsterThemePlay] "+LastResult);}
    }
    private static void Log(string message,string trace,LogType type){if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)errors.Add(message);}
    private static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();if(!EditorApplication.isPlaying || lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
        try
        {
            Check(EditorApplication.timeSinceStartup<deadline,"Timeout");
            while(work.Count>0){var next=work.Peek();if(!next.MoveNext()){work.Pop();continue;}if(next.Current is IEnumerator nested){work.Push(nested);continue;}return;}
            Check(errors.Count==0,string.Join(" | ",errors));Finish("PASS "+string.Join("; ",passed)+"; errors=0");
        }
        catch(Exception exception){Finish("FAIL "+exception+"; passed="+string.Join("; ",passed));}
    }
    private static void Finish(string result){SessionState.SetString(Key+".result",result);EditorApplication.update-=Tick;EditorApplication.ExitPlaymode();}
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static void Pass(string message){passed.Add(message);Debug.Log("[MonsterThemePlay] PASS "+message);}
    private static IEnumerator Seconds(float seconds){float end=Time.time+seconds;while(Time.time<end)yield return null;}
    private static IEnumerator Until(Func<bool> condition,float seconds,string message)
    {float end=Time.time+seconds;while(!condition()){Check(Time.time<end,message);yield return null;}}
    private static void Teleport(Vector3 position)
    {
        var cc=player.GetComponent<CharacterController>();bool enabled=cc.enabled;cc.enabled=false;player.transform.SetPositionAndRotation(position,Quaternion.identity);cc.enabled=enabled;
        player.GetComponent<PlayerMovement>().ResetMotionAfterTeleport();Physics.SyncTransforms();
    }
    private static EnemyThemeEncounter DebugEncounter()=>UnityEngine.Object.FindObjectsByType<EnemyThemeEncounter>(FindObjectsSortMode.None).FirstOrDefault(e=>e.name=="Theme debug encounter");
    private static void Kill(EnemyActor actor){actor.Health.TakeDamage(new DamageInfo(actor.Health.MaxHp+10000,actor.transform.position,player.gameObject,Vector3.forward));}
    private static EnemyActor Spawn(EnemyDefinition definition,Vector3 position)
    {
        var request=new EnemySpawnRequest(definition,position,Quaternion.identity,player.transform,null,player.transform,null,1,1,77);
        Check(EnemySpawnService.Current.TrySpawn(request,out var actor),"Spawn failed "+definition.EnemyId);
        actor.AI.enabled=false;actor.Movement.StopMovement();return actor;
    }
    private static IEnumerator Verify()
    {
        yield return Until(()=>PersistentSceneFlow.Instance!=null && !PersistentSceneFlow.Instance.IsSwitching && PersistentSceneFlow.Instance.CurrentSubSceneName==PersistentSceneFlow.HideoutSceneName,45,"Hideout load");
        player=PlayerInputFacade.Current;Check(player!=null,"Player missing");health=player.GetComponent<CombatHealth>();health.SetMaxHp(100000,true);
        ui=UnityEngine.Object.FindFirstObjectByType<EnemyThemeDebugUI>(FindObjectsInactive.Include);Check(ui!=null,"Debug UI missing");ui.gameObject.SetActive(true);
        ui.ToggleArena();Check(ui.InArena,"Arena entry");center=player.transform.position;yield return Seconds(.5f);
        Check(player.GetComponent<PlayerMovement>().IsGrounded,"Arena floor grounding");
        CombatDebugSettings.SetPlayerDamageReductionDebug(false);
        if(SessionState.GetBool(Key+".turnReview",false))
        {
            yield return MonsterThemeTurnReviewCapture.Capture(ui,player);
            ui.Clear();ui.ToggleArena();Pass("selected species / authored turns without foot planting / evaluated Unity frames");yield break;
        }
        if(SessionState.GetBool(Key+".fineTurns",false))
        {
            yield return MonsterThemeFineTurnVerifier.Verify(ui,player);
            ui.Clear();ui.ToggleArena();Pass("selected species / 15-45-minus45-135 degree turns / world sole traces; see per-species results");yield break;
        }
        if(SessionState.GetBool(Key+".facing",false))
        {
            yield return MonsterThemeFacingVerifier.Verify(ui,player);
            ui.Clear();ui.ToggleArena();Pass("14 species / complete turns / all configured attacks committed / evade / recovery");yield break;
        }
        if(SessionState.GetBool(Key+".crowdMotion",false))
        {
            yield return MonsterThemeCrowdMotionVerifier.Verify(ui,player);
            ui.Clear();ui.ToggleArena();Pass("3 tables / 50 each / visible moving poses / Gameplay pursuit / six squads");yield break;
        }
        if(SessionState.GetBool(Key+".locomotion",false))
        {
            yield return MonsterThemeLocomotionVerifier.Verify(ui,player);
            ui.Clear();ui.ToggleArena();Pass("14 species / looping bone poses / walk-run-backpedal / stop / hit-attack return / reuse");yield break;
        }
        if(SessionState.GetBool(Key+".survival",false)){yield return ArenaSurvival();yield break;}
        if(SessionState.GetBool(Key+".transition",false)){yield return SceneTransition();yield break;}
        if(SessionState.GetBool(Key+".review",false))
        {yield return Review();ui.Clear();ui.ToggleArena();yield return null;yield break;}
        if(SessionState.GetBool(Key+".safety",false))
        {
            yield return Safety();ui.Clear();if(ui.InArena)ui.ToggleArena();yield return null;yield break;
        }
        for(int index=0;index<3;index++)
        {
            Teleport(center);ui.spawnButtons[index].onClick.Invoke();var encounter=DebugEncounter();Check(encounter!=null,"Serialized spawn button");
            Check(!ui.Begin((index+1)%3,false),"Duplicate admission accepted");
            yield return Until(()=>encounter.State==EnemyThemeEncounterState.Combat || encounter.State==EnemyThemeEncounterState.Failed,15,"Spawn timeout");
            Check(encounter.SpawnedCount==50 && encounter.AliveCount==50 && encounter.FailedPlacements==0,"Exact roster failed "+index+" "+encounter.LastMessage);
            var roster=encounter.SnapshotActors();
            Check(roster.Count(a=>a.Definition.SquadParticipationMode==EnemySquadParticipationMode.Independent)==1,"Elite participation");
            yield return Seconds(3);
            var stats=EnemySquadPursuitRuntimeService.GetRuntimeStats();Check(stats.SquadCount==6,"Expected six squads: "+stats.SquadCount);
            Check(roster.All(a=>a.Animator!=null && a.Animator.enabled && !a.Animator.applyRootMotion),"Animator ownership");
            ui.clearButton.onClick.Invoke();yield return null;
            Check(roster.All(a=>!a.IsLeased),"Owned clear leaked actors");Pass("table "+index+" / 50 / 6 squads / duplicate blocked / clear");
        }
        var definitions=ui.tables.SelectMany(t=>t.Entries).Select(e=>e.definition).Distinct().ToArray();
        foreach(var definition in definitions)
        {
            EnemySpawnService.Current.RegisterAdditionalCatalog(ui.tables.First(t=>t.Entries.Any(e=>e.definition==definition)).Catalog,out _);
            EnemyActor prior=null;uint version=0;
            for(int cycle=0;cycle<20;cycle++)
            {
                var actor=Spawn(definition,center+Vector3.forward*8);Check(actor.Health.CurrentHp==actor.Health.MaxHp,"HP reset");
                if(prior==actor)Check(actor.LeaseVersion>version,"Lease version not advanced");
                actor.Animator.Play("Locomotion",0,0);yield return null;
                Check(!actor.Animator.applyRootMotion && actor.VisualRoot.localScale==Vector3.one,"Pooled model scale/root motion");
                version=actor.LeaseVersion;prior=actor;actor.RequestPoolRelease();
                Check(!actor.IsLeased && !actor.AbilityController.IsExecuting,"Pool execution leak");
            }
        }
        Pass("14 actors x 20 pool leases / HP / version / scale / cancellation");
        foreach(var definition in definitions)
        {
            var set=definition.ActorPrefab.AbilityController.AbilitySet;
            // Definition-owned sets are assigned on lease; prefab shell may retain a different set.
            var probe=Spawn(definition,center+Vector3.forward*8);set=probe.AbilityController.AbilitySet;probe.RequestPoolRelease();
            for(int i=0;i<set.Count;i++)
            {
                var ability=set.GetAbility(i);Teleport(center);health.ResetHealth();
                float distance=ability.ExecutionMode==EnemyAbilityExecutionMode.Projectile?4:ability.ExecutionMode==EnemyAbilityExecutionMode.Charge?3:Mathf.Min(1.1f,ability.Range*.75f);
                var actor=Spawn(definition,center-Vector3.forward*distance);actor.transform.rotation=Quaternion.identity;Physics.SyncTransforms();yield return Seconds(.2f);
                var executor=actor.GetComponents<EnemyAbilityExecutor>().First(e=>e.Supports(ability));
                float hp=health.CurrentHp;Vector3 start=actor.transform.position;
                int impacts=0;
                void CountImpact(CombatHealth source,DamageInfo info){if(info.source==actor.gameObject)impacts++;}
                health.OnDamaged+=CountImpact;
                Check(executor.TryStart(ability,i,player.transform),"Attack refused "+definition.EnemyId+" "+ability.AnimatorTrigger);
                yield return Seconds(ability.AttackAnimationDuration+1.1f);
                health.OnDamaged-=CountImpact;
                Check(health.CurrentHp<hp,"No attack impact "+definition.EnemyId+" "+ability.AnimatorTrigger+" mode="+ability.ExecutionMode);
                if(ability.HitCount>1)Check(impacts==ability.HitCount,"Combo impact count "+definition.EnemyId+" expected="+ability.HitCount+" actual="+impacts);
                if(ability.ExecutionMode==EnemyAbilityExecutionMode.Charge)Check(Vector3.Distance(start,actor.transform.position)>.3f,"Charge never moved");
                actor.RequestPoolRelease();float after=health.CurrentHp;yield return Seconds(.15f);Check(health.CurrentHp==after,"Late damage after pool");
            }
            Pass("attacks "+definition.EnemyId+" ("+set.Count+")");
        }
        Teleport(center);ui.waveButtons[1].onClick.Invoke();var waves=DebugEncounter();
        for(int wave=1;wave<=3;wave++)
        {
            int expected=wave*50;yield return Until(()=>waves.SpawnedCount==expected || waves.State==EnemyThemeEncounterState.Failed,25,"Wave timeout "+wave);
            Check(waves.State!=EnemyThemeEncounterState.Failed,"Wave placement "+waves.LastMessage);
            foreach(var actor in waves.SnapshotActors())if(!actor.Health.IsDead)Kill(actor);
        }
        yield return Until(()=>waves.State==EnemyThemeEncounterState.Completed,10,"Wave completion");
        Check(waves.DefeatedCount==150,"Kill accounting");ui.Clear();yield return null;Pass("3 direction waves / 150 / 150 deaths / complete");
        var arena=UnityEngine.Object.FindFirstObjectByType<EnemyThemeDebugArena>();var zones=arena.GetComponentsInChildren<EnemyThemeTriggerZone>();
        for(int index=0;index<zones.Length;index++)
        {
            var zone=zones[index];var encounter=zone.GetComponent<EnemyThemeEncounter>();
            Check(encounter.Table==ui.tables[index],"Map default/zone override");
            Teleport(zone.transform.position+Vector3.up*.2f);yield return Until(()=>zone.HasTriggered,3,"Physics trigger entry");
            Check(!zone.TryActivate(player.transform),"Trigger double entry");encounter.StopEncounter(true);Check(zone.Rearm(),"Rearm");
            Teleport(center);yield return Seconds(.2f);
        }
        Pass("map default / 2 overrides / physical trigger x3 / once / rearm");
        ui.Clear();ui.ToggleArena();Check(!ui.InArena,"Arena return");yield return Seconds(.5f);Check(player.transform.position.x<900,"Player return position");
        Pass("arena exit / owned cleanup / return");
    }
    private static IEnumerator Safety()
    {
        ui.spawnButtons[0].onClick.Invoke();var encounter=DebugEncounter();
        yield return Until(()=>encounter.SpawnedCount==50,15,"Safety setup spawn");
        var leased=encounter.SnapshotActors()[0];var definition=leased.Definition;uint oldVersion=leased.LeaseVersion;
        leased.RequestPoolRelease();var replacement=Spawn(definition,center+Vector3.forward*8);
        Check(replacement==leased && replacement.LeaseVersion!=oldVersion,"Reacquire fixture");
        ui.Clear();Check(replacement.IsLeased,"Old owner reclaimed a new lease");replacement.RequestPoolRelease();yield return null;
        Pass("stale owner cannot release a new lease");
        foreach(var table in ui.tables)EnemySpawnService.Current.RegisterAdditionalCatalog(table.Catalog,out _);
        var definitions=ui.tables.SelectMany(t=>t.Entries).Select(e=>e.definition).Distinct().ToArray();
        var sourceObject=new GameObject("Directional hit fixture");
        foreach(var def in definitions)
        {
            var actor=Spawn(def,center+Vector3.forward*8);yield return null;
            foreach(Vector3 local in new[]{Vector3.forward,Vector3.back,Vector3.left,Vector3.right})
            {
                sourceObject.transform.position=actor.transform.position+actor.transform.TransformDirection(local)*2;
                actor.Health.TakeDamage(new DamageInfo(1,actor.transform.position,sourceObject,-local));
                Check(Mathf.Abs(actor.Animator.GetFloat("HitX")-local.x)<.01f && Mathf.Abs(actor.Animator.GetFloat("HitZ")-local.z)<.01f,"Directional hit parameter "+def.EnemyId);
                yield return Seconds(.1f);
                string expected="GetHit"+(local.z>.5f?"Front":local.z<-.5f?"Back":local.x<0?"Left":"Right");
                var clip=actor.Animator.GetCurrentAnimatorClipInfo(0).OrderByDescending(c=>c.weight).FirstOrDefault().clip;
                Check(clip!=null && clip.name==expected,"Directional clip "+def.EnemyId+" expected="+expected+" actual="+(clip!=null?clip.name:"none"));
            }
            actor.RequestPoolRelease();
        }
        UnityEngine.Object.Destroy(sourceObject);Pass("14 actors x four directional hit clips");
        foreach(var def in definitions)
        {
            var actor=Spawn(def,center-Vector3.forward*3);var set=actor.AbilityController.AbilitySet;actor.RequestPoolRelease();
            for(int i=0;i<set.Count;i++)
            {
                var ability=set.GetAbility(i);float distance=ability.ExecutionMode==EnemyAbilityExecutionMode.Projectile?4:ability.ExecutionMode==EnemyAbilityExecutionMode.Charge?3:1;
                Teleport(center);actor=Spawn(def,center-Vector3.forward*distance);yield return Seconds(.2f);
                var executor=actor.GetComponents<EnemyAbilityExecutor>().First(e=>e.Supports(ability));
                Check(executor.TryStart(ability,i,player.transform),"Cancel fixture attack start "+def.EnemyId+" "+ability.AnimatorTrigger+" distance="+Vector3.Distance(actor.transform.position,player.transform.position));actor.RequestPoolRelease();
            }
        }
        float hp=health.CurrentHp;yield return Seconds(3);Check(health.CurrentHp==hp,"Late damage from canceled attacks");
        Pass("all configured attacks canceled on return / no late damage");
        var shooter=definitions.First(d=>d.EnemyId.Contains("Arathrox"));
        var enemy=Spawn(shooter,center-Vector3.forward*4);var abilitySet=enemy.AbilityController.AbilitySet;
        var projectile=Enumerable.Range(0,abilitySet.Count).Select(abilitySet.GetAbility).First(a=>a.ExecutionMode==EnemyAbilityExecutionMode.Projectile);
        enemy.RequestPoolRelease();
        for(int scenario=0;scenario<3;scenario++)
        {
            Teleport(center);enemy=Spawn(shooter,center-Vector3.forward*4);yield return Seconds(.2f);var executor=enemy.GetComponent<EnemyThemeSpecialExecutor>();hp=health.CurrentHp;
            Check(executor.TryStart(projectile,0,player.transform),"Projectile safety start");GameObject wall=null;
            if(scenario==0){wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=center-Vector3.forward*2+Vector3.up;wall.transform.localScale=new Vector3(8,3,.35f);Physics.SyncTransforms();}
            if(scenario==1)Teleport(center+Vector3.right*6);
            if(scenario==2){Kill(enemy);}
            yield return Seconds(projectile.AttackAnimationDuration+1.2f);
            Check(health.CurrentHp==hp,"Projectile wall/miss/death safety "+scenario);
            Check(!executor.HasProjectile,"Projectile lifetime leak");enemy.RequestPoolRelease();if(wall!=null)UnityEngine.Object.Destroy(wall);yield return null;
        }
        Teleport(center);Pass("projectile wall obstruction / committed aim miss / death cancellation");
        var root=new GameObject("Constrained encounter fixture");var bounds=root.AddComponent<BoxCollider>();bounds.isTrigger=true;bounds.size=Vector3.one;root.transform.position=center;
        var constrained=root.AddComponent<EnemyThemeEncounter>();constrained.Configure(ui.tables[0],bounds);Check(constrained.Begin(player.transform,false),"Constrained start");
        yield return Until(()=>constrained.State==EnemyThemeEncounterState.Failed,10,"Constrained should fail");Check(constrained.AliveCount==0,"Failed batch retained actors");UnityEngine.Object.Destroy(root);
        Pass("insufficient placement fails and returns entire batch");
        // Arena survival is intentional; death cleanup belongs to ordinary combat outside it.
        ui.ToggleArena();Check(!health.IsDeathFromDamagePrevented,"Protection leaked outside arena");
        ui.spawnButtons[2].onClick.Invoke();encounter=DebugEncounter();yield return Until(()=>encounter.SpawnedCount==50,15,"Death setup");
        var roster=encounter.SnapshotActors();health.TakeDamage(new DamageInfo(health.MaxHp+1000,player.transform.position,null,Vector3.zero));
        yield return Until(()=>!encounter.Running,5,"Dead target did not stop encounter");Check(roster.All(a=>!a.IsLeased),"Dead player cleanup");health.ResetHealth();Pass("player death stops and clears encounter");
    }
    private static IEnumerator ArenaSurvival()
    {
        int deaths=0,hits=0;
        void Died(CombatHealth target,DamageInfo info){deaths++;}
        void Hit(CombatHealth target,DamageInfo info){hits++;}
        health.OnDead+=Died;health.OnDamaged+=Hit;
        try
        {
            health.SetMaxHp(100,true);
            for(int i=0;i<5;i++)health.TakeDamage(new DamageInfo(10000,player.transform.position));
            Check(health.CurrentHp==1 && !health.IsDead && deaths==0 && hits==5,"Repeated lethal damage must leave 1 HP and emit hits without death");
            health.ApplyDamageOverTime(10000,.3f,.05f,null,Vector3.zero);yield return Seconds(.5f);
            Check(health.CurrentHp==1 && !health.IsDead && hits>5,"DoT bypassed survival");
            ui.Clear();Check(health.IsDeathFromDamagePrevented && health.MaxHp==100,"Clear changed protection or max HP");
            Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform,out var service),"Survival spawn service");
            Check(service.RegisterAdditionalCatalog(ui.tables[0].Catalog,out _),"Survival catalog");
            var enemy=Spawn(ui.tables[0].Entries[0].definition,center+Vector3.forward*8);
            Kill(enemy);Check(enemy.Health.IsDead && !enemy.Health.IsDeathFromDamagePrevented,"Monster incorrectly protected");enemy.RequestPoolRelease();
            Pass("arena repeated lethal damage / DoT / hit events / 1 HP / unchanged max HP / monster death");
            ui.ToggleArena();Check(!health.IsDeathFromDamagePrevented,"Return did not release protection immediately");
            health.TakeDamage(new DamageInfo(10000,player.transform.position));
            Check(health.IsDead && deaths==1,"Normal death outside arena");health.ResetHealth();yield return null;
            for(int cycle=0;cycle<2;cycle++)
            {
                ui.ToggleArena();Check(ui.InArena && health.IsDeathFromDamagePrevented,"Reentry protection");
                var arena=UnityEngine.Object.FindFirstObjectByType<EnemyThemeDebugArena>();
                if(cycle==0)
                {
                    arena.enabled=false;Check(!health.IsDeathFromDamagePrevented,"Disabled arena retained protection");
                    ui.ToggleArena();
                }
                else
                {
                    UnityEngine.Object.Destroy(arena.gameObject);yield return null;
                    Check(!health.IsDeathFromDamagePrevented,"Destroyed arena retained protection");
                }
                health.TakeDamage(new DamageInfo(10000,player.transform.position));Check(health.IsDead,"Lifecycle cleanup still prevents death");health.ResetHealth();yield return null;
            }
            Pass("normal return / outside death / repeated entry / disable / destruction restore damage");
        }
        finally {health.OnDead-=Died;health.OnDamaged-=Hit;}
    }
    private static IEnumerator Review()
    {
        string output=SessionState.GetString(Key+".output","");Check(System.IO.Directory.Exists(output),"Set existing review output directory before running");
        CombatDebugSettings.SetEnemyAiStateDebug(false);CombatDebugSettings.SetAttackPatternDebug(false);
        var playerActor=PlayerContext.GetOrCreate().CurrentActor;
        var sword=AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/OHS01_FleurDeLys/OHS01_FleurDeLys.asset");
        Check(playerActor.Equipment.EquipWeaponItem(new ItemData(sword,1,ItemGrade.Common)),"Review equip");
        PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);yield return Seconds(.5f);
        for(int index=0;index<3;index++)
        {
            if(SessionState.GetBool(Key+".skipCapture",false))break;
            Teleport(center);ui.spawnButtons[index].onClick.Invoke();var encounter=DebugEncounter();yield return Until(()=>encounter.SpawnedCount==50,15,"Review spawn");
            yield return Seconds(5);var samples=new List<float>();
            for(int frame=0;frame<180;frame++){samples.Add(Time.unscaledDeltaTime*1000);yield return null;}
            samples.Sort();Pass("Editor 50 "+ui.tables[index].ThemeId+" frame-ms median="+samples[90].ToString("F2")+" p95="+samples[171].ToString("F2"));
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(output,ui.tables[index].ThemeId+"-play.png"));yield return Seconds(.3f);ui.Clear();yield return null;
        }
        var melee=player.GetComponent<MeleeRuntime>();
        Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform,out var service),"Review spawn service");
        foreach(var table in ui.tables)Check(service.RegisterAdditionalCatalog(table.Catalog,out _),"Review catalog");
        foreach(var table in ui.tables)
        foreach(EnemyThemeTier tier in Enum.GetValues(typeof(EnemyThemeTier)))
        {
            var definition=table.Entries.First(e=>e.tier==tier).definition;
            Teleport(center);var enemy=Spawn(definition,center+Vector3.forward*1.25f);int swings=0;float hp=enemy.Health.MaxHp;
            yield return Seconds(.2f);
            while(!enemy.Health.IsDead && swings<64)
            {
                Teleport(center);enemy.transform.position=center+Vector3.forward*1.25f;Physics.SyncTransforms();
                yield return Until(()=>!melee.IsAttackInProgress && melee.IsAttackReady && player.GetComponent<PlayerMovement>().IsGrounded
                    && player.GetComponent<PlayerStateCoordinator>().CurrentCondition==PlayerConditionState.Normal,10,"Weapon not ready");
                var request=new WeaponActionRequest(WeaponActionSource.PlayerInput,enemy.GetComponent<CombatTarget>(),Vector3.forward);
                var result=melee.TryStartAction(request,out _);Check(result==WeaponActionResult.Accepted,"Player attack rejected: "+result);swings++;
                yield return Seconds(1.25f);
            }
            Check(enemy.Health.IsDead,"Player weapon never killed "+definition.EnemyId);
            Pass("weapon duel "+definition.EnemyId+" HP="+hp+" swings="+swings);enemy.RequestPoolRelease();melee.CancelCurrentAttackState();yield return Seconds(.2f);
        }
    }
    private static IEnumerator SceneTransition()
    {
        ui.spawnButtons[0].onClick.Invoke();var encounter=DebugEncounter();yield return Until(()=>encounter.SpawnedCount==50,15,"Transition spawn");
        var roster=encounter.SnapshotActors();var flow=PersistentSceneFlow.Instance;
        flow.EnterDungeon(DungeonRunEntryRequest.Create(731,PersistentSceneFlow.HideoutSceneName,"DungeonPortal"));
        yield return Until(()=>!flow.IsSwitching && flow.CurrentSubSceneName==PersistentSceneFlow.DungeonRunSceneName,90,"Dungeon transition");
        Check(!ui.InArena && roster.All(a=>a==null || !a.IsLeased),"Arena/monster leaked into dungeon");
        Check(!health.IsDeathFromDamagePrevented,"Arena survival leaked into dungeon");
        Check(RunSceneReadinessRegistry.GetState(PersistentSceneFlow.DungeonRunSceneName)==RunSceneReadinessState.Ready,"Dungeon readiness");
        Check(!UnityEngine.Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None).Any(a=>a.IsLeased && ui.tables.Any(t=>t.Entries.Any(e=>e.definition==a.Definition))),"Theme replaced default dungeon spawns");
        flow.ReturnToHub(RunSceneReturnContext.CreateHubTransfer(PersistentSceneFlow.HideoutSceneName,"DungeonPortal"));
        yield return Until(()=>!flow.IsSwitching && flow.CurrentSubSceneName==PersistentSceneFlow.HideoutSceneName,60,"Hideout return");
        Check(player.transform.position.x<900,"Arena coordinate leaked into return");Pass("50 active -> DungeonRun -> Hideout / owned cleanup / default spawn unchanged");
    }
}
