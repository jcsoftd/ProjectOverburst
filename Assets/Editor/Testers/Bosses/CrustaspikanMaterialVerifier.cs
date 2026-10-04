using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class CrustaspikanMaterialVerifier
{
    const string Key="Overburst.CrustaspikanMaterialVerifier.";
    sealed class Plan
    {public string output,phase,token,fixture,previousStart;public bool realPlayer,background,extras,geometry;public float captureDelta,timeScale;public double deadline;public JArray scenes;}
    static Plan plan;
    static readonly List<Object> owned=new List<Object>();
    static readonly JArray cases=new JArray();
    static EnemyMotor host;
    static Coroutine routine;
    static string failure;
    static string Account=>Path.Combine(plan.output,"Account");
    static bool Same(string a,string b)=>!string.IsNullOrEmpty(a)&&!string.IsNullOrEmpty(b)&&string.Equals(Path.GetFullPath(a).TrimEnd('/','\\'),Path.GetFullPath(b).TrimEnd('/','\\'),StringComparison.OrdinalIgnoreCase);
    static JArray Scenes()=>new JArray(Enumerable.Range(0,SceneManager.sceneCount).Select(i=>SceneManager.GetSceneAt(i)).Select(s=>new JObject{["path"]=s.path,["dirty"]=s.isDirty,["roots"]=s.rootCount}));
    static void Require(bool pass,string text){if(!pass)throw new InvalidOperationException(text);}
    static void Persist()=>SessionState.SetString(Key+"plan",JsonConvert.SerializeObject(plan));
    static void Result(string state)=>File.WriteAllText(Path.Combine(plan.output,"result.json"),new JObject{["status"]=state,["failure"]=failure,["realPlayer"]=plan.realPlayer,["cases"]=cases}.ToString());
    static CrustaspikanMaterialVerifier()
    {
        string saved=SessionState.GetString(Key+"plan","");if(!string.IsNullOrEmpty(saved))plan=JsonConvert.DeserializeObject<Plan>(saved);
        EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=Changed;AssemblyReloadEvents.beforeAssemblyReload+=Reload;
    }
    public static string Start(string output,bool realPlayer=false,bool extras=false,bool geometry=false)
    {
        Require(plan==null&&!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating,"Idle Editor required.");
        Require(string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)&&string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))&&string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","")),"Unoccupied account required.");
        output=Path.GetFullPath(output);string allowed=Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,"개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        Require(output.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)&&!File.Exists(Path.Combine(output,"plan.json")),"Fresh private output required.");Directory.CreateDirectory(output);
        plan=new Plan{output=output,phase="booting",token=Guid.NewGuid().ToString("N"),realPlayer=realPlayer,extras=extras,geometry=geometry,background=Application.runInBackground,captureDelta=Time.captureDeltaTime,timeScale=Time.timeScale,deadline=EditorApplication.timeSinceStartup+1200,scenes=Scenes(),previousStart=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)};
        plan.fixture=realPlayer?"Assets/ProjectOverburst/00_Scenes/PersistentScene.unity":"Assets/Editor/Testers/Bosses/CrustaspikanFixture_"+plan.token+".unity";
        cases.Clear();failure=null;Persist();File.WriteAllText(Path.Combine(output,"plan.json"),JsonConvert.SerializeObject(plan,Formatting.Indented));
        Scene active=SceneManager.GetActiveScene(),fixture=default;
        try
        {
            if(!realPlayer){fixture=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);Require(EditorSceneManager.SaveScene(fixture,plan.fixture),"Fixture save failed.");EditorSceneManager.CloseScene(fixture,true);fixture=default;}
            SceneManager.SetActiveScene(active);EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(plan.fixture);Application.runInBackground=true;IsolatedSavePlayGuard.EnterIsolatedPlay(Account);return Path.Combine(output,"plan.json");
        }
        catch(Exception error){if(fixture.IsValid())EditorSceneManager.CloseScene(fixture,true);if(active.IsValid())SceneManager.SetActiveScene(active);Return(error.ToString());throw;}
    }
    static bool OwnPlay=>plan!=null&&EditorApplication.isPlaying&&Same(IsolatedSavePlayGuard.ActiveDirectory,Account);
    static void Tick()
    {
        if(plan==null)return;
        try
        {
            if(plan.phase=="booting"&&EditorApplication.isPlaying)
            {Require(OwnPlay,"Account mismatch.");var root=new GameObject("Crustaspikan material verifier");owned.Add(root);host=root.AddComponent<EnemyMotor>();root.GetComponent<Rigidbody>().isKinematic=true;plan.phase="running";Persist();routine=host.StartCoroutine(Drive(plan.realPlayer?PlayerCases():plan.extras?Extras():plan.geometry?GeometryCases():FixtureCases()));}
            if(plan.phase=="running"){Require(OwnPlay,"Own Play interrupted.");EditorApplication.QueuePlayerLoopUpdate();}
            if(plan.phase!="returning"&&EditorApplication.timeSinceStartup>plan.deadline)Return("Verifier timeout.");
            if(plan.phase=="returning")FinishReturn();
        }
        catch(Exception error){Return(error.ToString());}
    }
    static IEnumerator Drive(IEnumerator body)
    {
        var stack=new Stack<IEnumerator>();stack.Push(body);
        while(stack.Count>0)
        {
            var current=stack.Peek();bool more;object value;
            try{more=current.MoveNext();value=more?current.Current:null;}
            catch(Exception error){while(stack.Count>0)try{(stack.Pop() as IDisposable)?.Dispose();}catch{}Return(error.ToString());yield break;}
            if(!more){stack.Pop();(current as IDisposable)?.Dispose();continue;}
            if(value is IEnumerator nested){stack.Push(nested);continue;}yield return value;
        }
        Return(null);
    }
    static void Position(Transform transform,Vector3 position)
    {transform.position=position;var body=transform.GetComponent<Rigidbody>();if(body!=null){body.position=position;body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;}Physics.SyncTransforms();}
    static EnemySpawnService Service(EnemyBossMaterialCollection collection)
    {
        var existing=EnemySpawnService.Current;if(existing!=null){Require(existing.RegisterAdditionalCatalog(collection.catalog,out var reason),reason);return existing;}
        var root=new GameObject("Owned boss material services");owned.Add(root);var inactive=new GameObject("Pool").transform;inactive.SetParent(root.transform,false);inactive.gameObject.SetActive(false);
        var pool=root.AddComponent<EnemyPoolService>();pool.Configure(inactive,0);var service=root.AddComponent<EnemySpawnService>();service.Configure(collection.catalog,pool);return service;
    }
    static EnemyActor Spawn(EnemySpawnService service,EnemyBossMaterialCollection collection,Vector3 position,Transform target)
    {
        Require(service.TrySpawn(new EnemySpawnRequest(collection.actorDefinition,position,Quaternion.identity,target,context:EncounterContext.Test),out var actor),"Saved actor spawn failed.");
        actor.AI.enabled=false;actor.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;return actor;
    }
    static void ProbeWarning(EnemyBossMaterialExecutor executor,EnemyBossAttackMaterial material,int phase,JArray probes)
    {
        var warnings=(EnemyStrongAttackWarning[])typeof(EnemyBossMaterialExecutor).GetField("warnings",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(executor);
        var warning=warnings[phase];if(warning==null||!warning.IsVisible)return;
        var indicator=warning.GetComponentsInChildren<ProceduralGroundIndicator>(true).Single(p=>p.IsVisible);
        var strike=material.strikes[phase];
        Require(indicator.Shape==strike.shape,"Native warning shape differs from damage.");
        if(strike.shape==GroundIndicatorShape.Rectangle)
            Require(Mathf.Abs(indicator.Width-strike.width)<.001f&&Mathf.Abs(indicator.Length-strike.length)<.001f&&indicator.CorridorCapRadius==0f,"Native corridor dimensions differ from damage.");
        else Require(Mathf.Abs(indicator.OuterRadius-strike.radius)<.001f&&Mathf.Abs(indicator.InnerRadius-strike.innerRadius)<.001f,"Native warning radii differ from damage.");
        if(material.delivery==EnemyBossMaterialDelivery.Melee)
        {
            var expected=strike.Origin(executor.transform);expected.y=executor.transform.position.y+.045f;
            Require(Vector3.Distance(indicator.transform.position,expected)<.002f,"Warning center differs from strike.");
            Require(Quaternion.Angle(indicator.transform.rotation,strike.Rotation(executor.transform))<.02f,"Warning facing differs from strike.");
        }
        if(strike.shape==GroundIndicatorShape.Sector)Require(Mathf.Abs(indicator.Angle-strike.angle)<.001f,"Warning sector angle differs from strike.");
        if(strike.shape!=GroundIndicatorShape.Rectangle)
        {
            var distances=indicator.Surface.mesh.vertices.Select(v=>new Vector2(v.x,v.y).magnitude).ToArray();
            Require(Mathf.Abs(distances.Min()-strike.innerRadius)<.002f&&Mathf.Abs(distances.Max()-strike.radius)<.002f,"Rendered boundary differs from damage.");
        }
        probes.Add(new JObject{["phase"]=phase,["shape"]=indicator.Shape.ToString(),["outer"]=indicator.OuterRadius,["inner"]=indicator.InnerRadius,["angle"]=indicator.Angle});
    }
    static Vector3 ContactPoint(EnemyBossMaterialStrike strike,Transform owner)
    {
        if(strike.shape==GroundIndicatorShape.Sector||strike.shape==GroundIndicatorShape.Donut)
            return strike.Origin(owner)+strike.Rotation(owner)*Vector3.forward*Mathf.Lerp(strike.innerRadius,strike.radius,.65f);
        if(strike.localOrigin.sqrMagnitude<.001f)return strike.Origin(owner)+strike.Rotation(owner)*Vector3.forward*(strike.radius*.65f);
        return strike.Origin(owner);
    }
    static IEnumerator GeometryCases()
    {
        // Boundaries use a clean fixture: a previous target must not occlude line of sight.
        var collection=AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(CrustaspikanMaterialBuilder.CollectionPath);var service=Service(collection);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);owned.Add(floor);floor.transform.position=Vector3.down*.5f;floor.transform.localScale=new Vector3(100,1,100);
        var victim=new GameObject("Geometry physical boundary target");owned.Add(victim);victim.layer=LayerMask.NameToLayer("Player");
        var capsule=victim.AddComponent<CapsuleCollider>();capsule.radius=.2f;capsule.height=1.55f;capsule.center=Vector3.up*.775f;
        var target=victim.AddComponent<CombatTarget>();target.Configure(CombatTeam.PlayerParty,false);target.ConfigureVolume(capsule.center,.5f,1.55f);victim.GetComponent<CombatHealth>().SetMaxHp(100000,true);
        foreach(var material in collection.attacks.Where(m=>m.delivery==EnemyBossMaterialDelivery.Melee))
        {
            var actor=Spawn(service,collection,Vector3.up*.035f,victim.transform);var executor=actor.GetComponent<EnemyBossMaterialExecutor>();
            foreach(var c in actor.GetComponentsInChildren<Collider>(true))Physics.IgnoreCollision(c,capsule);
            yield return null;yield return new WaitForFixedUpdate();
            for(int phase=0;phase<material.strikes.Length;phase++)
            {
                var s=material.strikes[phase];Vector3 origin=s.Origin(actor.transform);Quaternion rotation=s.Rotation(actor.transform);
                Action<string,Vector3,bool> probe=(name,point,expected)=>
                {
                    Position(victim.transform,point);
                    Require(s.Intersects(capsule,actor.transform)==expected,"Physical geometry boundary failed: "+material.name+"/"+phase+"/"+name);
                    Require(executor.WouldHit(material.ability,target,phase)==expected,"Threat geometry differs: "+material.name+"/"+phase+"/"+name);
                    cases.Add(new JObject{["pass"]=true,["id"]=material.materialId,["phase"]=phase,["scenario"]=name,["expectedHit"]=expected,["shape"]=s.shape.ToString(),["physicalRadius"]=capsule.radius,["combatRadius"]=.5f});
                };
                probe("outer-body-overlap",origin+rotation*Vector3.forward*(s.radius+.19f),true);
                probe("outer-body-outside",origin+rotation*Vector3.forward*(s.radius+.22f),false);
                probe("valid-contact",ContactPoint(s,actor.transform),true);
                if(s.innerRadius>0f)
                {
                    probe("safe-center",origin,false);
                    probe("inner-body-overlap",origin+rotation*Vector3.forward*(s.innerRadius-.19f),true);
                    probe("inner-body-inside-hole",origin+rotation*Vector3.forward*(s.innerRadius-.22f),false);
                }
                if(s.shape==GroundIndicatorShape.Sector)
                {
                    float d=Mathf.Lerp(s.innerRadius,s.radius,.65f);
                    foreach(float side in new[]{-1f,1f})
                    {
                        Vector3 ray=Quaternion.Euler(0,s.angle*.5f*side,0)*Vector3.forward;
                        Vector3 outward=new Vector3(ray.z,0,-ray.x)*side;
                        probe("angular-body-overlap-"+side,origin+rotation*(ray*d+outward*.19f),true);
                        probe("angular-body-outside-"+side,origin+rotation*(ray*d+outward*.22f),false);
                    }
                    probe("behind-sector",origin-rotation*Vector3.forward*d,false);
                }
                else if(s.shape==GroundIndicatorShape.Donut)
                    probe("rear-ring",origin-rotation*Vector3.forward*Mathf.Lerp(s.innerRadius,s.radius,.65f),true);
                Result("RUNNING");
            }
            service.Release(actor);yield return null;
        }
    }
    static IEnumerator FixtureCases()
    {
        Time.captureDeltaTime=1f/60;
        var collection=AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(CrustaspikanMaterialBuilder.CollectionPath);Require(collection!=null,"Saved materials missing.");
        var service=Service(collection);var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);owned.Add(floor);floor.transform.position=Vector3.down*.5f;floor.transform.localScale=new Vector3(100,1,100);
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab");
        var physical=prefab.GetComponentsInChildren<CapsuleCollider>(true).Single(c=>c.enabled&&!c.isTrigger);var victim=new GameObject("Saved physical player body");owned.Add(victim);victim.layer=prefab.layer;
        var capsule=victim.AddComponent<CapsuleCollider>();capsule.center=prefab.transform.InverseTransformPoint(physical.transform.TransformPoint(physical.center));capsule.radius=physical.radius*Mathf.Max(Mathf.Abs(physical.transform.lossyScale.x),Mathf.Abs(physical.transform.lossyScale.z));capsule.height=physical.height*Mathf.Abs(physical.transform.lossyScale.y);
        var target=victim.AddComponent<CombatTarget>();target.Configure(CombatTeam.PlayerParty,false);var volume=prefab.GetComponent<CombatTarget>().ResolveVolumeAtRootPosition(Vector3.zero);target.ConfigureVolume(volume.Center,volume.Radius,volume.HalfHeight*2);
        var health=victim.GetComponent<CombatHealth>();health.SetMaxHp(100000,true);EnemyStrongAttackWarning.PlayerTarget=target;var damage=new JArray();EnemyBossMaterialExecutor current=null;
        health.OnDamaged+=(_,info)=>damage.Add(new JObject{["phase"]=info.sourceAttackPhaseIndex,["sequence"]=info.sourceAttackSequenceId,["damage"]=info.damage,["normalized"]=current!=null?current.NormalizedTime:0f});
        yield return null;yield return new WaitForFixedUpdate();EnemyActor previous=null;uint priorLease=0;
        foreach(var material in collection.attacks)
        {
            foreach(string scenario in new[]{"contact","evade","cancel"})
            {
                damage.Clear();var warningProbes=new JArray();health.SetMaxHp(100000,true);var actor=Spawn(service,collection,Vector3.up*.035f,victim.transform);current=actor.GetComponent<EnemyBossMaterialExecutor>();
                Require(actor.LeaseVersion>priorLease,"Lease did not advance.");bool reused=previous==null||previous==actor;Require(reused,"Owned pool did not reuse actor.");priorLease=actor.LeaseVersion;previous=actor;
                foreach(var collider in actor.GetComponentsInChildren<Collider>(true))Physics.IgnoreCollision(collider,capsule);
                yield return null;yield return new WaitForFixedUpdate();
                Vector3 aim=material.delivery==EnemyBossMaterialDelivery.Melee?ContactPoint(material.strikes[0],actor.transform):actor.transform.position+Vector3.forward*12f;Position(victim.transform,aim);
                Require(actor.AbilityController.TryStartAbility(material.ability,victim.transform),"Native start rejected: "+material.materialId);float limit=Time.time+material.runtimeClip.length+4f;
                while(current.IsExecuting)
                {
                    Require(Time.time<limit,"Attack completion timeout: "+material.materialId+"; "+current.LastFailure);
                    if(material.delivery==EnemyBossMaterialDelivery.Melee&&scenario=="contact")
                    {int phase=0;while(phase<material.strikes.Length-1&&current.NormalizedTime>material.strikes[phase].contactEnd)phase++;Position(victim.transform,ContactPoint(material.strikes[phase],actor.transform));}
                    if(scenario=="evade"&&current.NormalizedTime>=material.strikes[0].impact-.15f/material.runtimeClip.length)Position(victim.transform,new Vector3(25f,.035f,-15f));
                    if(scenario=="cancel"&&current.NormalizedTime>=material.strikes[0].impact*.5f){actor.AbilityController.Cancel();break;}
                    if(scenario=="contact")for(int phase=0;phase<material.strikes.Length;phase++)
                        if(!warningProbes.OfType<JObject>().Any(p=>(int)p["phase"]==phase)&&current.NormalizedTime<material.strikes[phase].impact)ProbeWarning(current,material,phase,warningProbes);
                    yield return new WaitForFixedUpdate();
                }
                Require(current.LastFailure==null,"Motion execution failed: "+current.LastFailure);
                if(scenario=="contact")
                {
                    Require(warningProbes.Count==material.strikes.Length,"Native warnings were not inspected.");
                    Require(damage.Count==material.strikes.Length,"Wrong physical contact count: "+material.materialId+" / "+damage.Count);
                    for(int phase=0;phase<material.strikes.Length;phase++)
                    {
                        var hit=damage.OfType<JObject>().Single(d=>(int)d["phase"]==phase);
                        if(material.delivery==EnemyBossMaterialDelivery.Melee)Require((float)hit["normalized"]>=material.strikes[phase].impact-.0001f&&(float)hit["normalized"]<=material.strikes[phase].contactEnd+.025f,"Damage outside native contact window.");
                    }
                }
                else Require(damage.Count==0,"Damage after evade/cancel.");
                cases.Add(new JObject{["pass"]=true,["id"]=material.materialId,["scenario"]=scenario,["physicalColliderRadius"]=capsule.radius,["combatVolumeRadius"]=volume.Radius,["reused"]=reused,["impacts"]=current.ImpactCount,["launches"]=current.LaunchCount,["damageEvents"]=damage.DeepClone(),["warningProbes"]=warningProbes});Result("RUNNING");
                service.Release(actor);yield return null;Require(!current.IsExecuting&&!current.IsRockHeld&&current.ActiveProjectileCount==0,"Pool cleanup failed.");
            }
        }
        var prepared=Spawn(service,collection,Vector3.up*.035f,victim.transform);current=prepared.GetComponent<EnemyBossMaterialExecutor>();yield return null;
        int beforeDamage=damage.Count;Require(current.TryPlayMotion("UnearthRock",true),"Preparation start rejected.");while(current.IsExecuting)yield return null;Require(current.IsRockHeld&&prepared.Animator.speed==0f&&damage.Count==beforeDamage,"Unearth/held pose mismatch.");
        Require(current.TryPlayMotion("WalkForwardWithRock"),"Rock walk rejected after held pose.");while(current.IsExecuting)yield return null;Require(current.IsRockHeld&&prepared.Animator.speed==1f,"Rock walk continuity failed.");
        current.Cancel();Require(!current.IsRockHeld&&!current.IsExecuting,"Preparation cancel failed.");service.Release(prepared);
        cases.Add(new JObject{["pass"]=true,["id"]="UnearthRock→WalkForwardWithRock",["scenario"]="preparation",["damageEvents"]=0});Result("RUNNING");
    }
    static IEnumerator PlayerCases()
    {
        float boot=Time.unscaledTime+180f;
        while(PersistentSceneFlow.Instance==null||PersistentSceneFlow.Instance.IsSwitching||!WorldSessionState.IsHideout||PlayerInputFacade.Current==null||AccountGameplaySession.Current==null){Require(Time.unscaledTime<boot,"Actual player boot timeout.");yield return null;}
        Require(Same(AccountBootstrap.SaveDirectory,Account),"Actual player account mismatch.");
        var player=PlayerInputFacade.Current;var playerActor=PlayerContext.GetOrCreate().CurrentActor;
        var weapon=AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");Require(playerActor.Equipment.EquipWeaponItem(new ItemData(weapon,1,ItemGrade.Common)),"Actual weapon equip rejected.");yield return null;
        Require(!EnemyThemeTrialService.InArena&&!EnemyThemeTrialService.Busy,"Foreign trial active.");EnemyThemeTrialService.ToggleArena();Require(EnemyThemeTrialService.InArena,"Actual review arena entry rejected.");
        var collection=AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(CrustaspikanMaterialBuilder.CollectionPath);var service=Service(collection);var melee=player.GetComponent<MeleeRuntime>();melee.SetManualInputEnabled(true);playerActor.Health.SetMaxHp(100000,true);
        PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);Time.captureDeltaTime=1f/60;var target=player.GetComponent<CombatTarget>();
        // The product can grade low-energy parries separately. This fixture checks a fully charged parry.
        var fire=AssetDatabase.LoadAssetAtPath<ElementGemItemData>("Assets/ProjectOverburst/Resources/Items/ElementGems/EG_Fire_Common.asset");
        var setGem=typeof(PlayerEquipment).GetMethod("SetElementGem",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public);
        Require(fire!=null&&setGem!=null,"Actual full-energy parry fixture missing.");setGem.Invoke(playerActor.Equipment,new object[]{new ItemData(fire,1,ItemGrade.Common)});yield return null;
        var energy=melee.GetComponent<OverburstElementEnergy>();if(energy==null){energy=melee.gameObject.AddComponent<OverburstElementEnergy>();owned.Add(energy);}Require(energy!=null,"Actual element energy missing.");
        EnemyActor enemy=null;
        try
        {
            Vector3 stage=player.transform.position+Vector3.forward*14f;
            foreach(var material in collection.attacks.Where(m=>m.ability.IsParryable))
            {
                melee.CancelCurrentAttackState();yield return null;
                int charges=0;while(energy.Normalized<.999f&&charges++<100)
                    Require(energy.RecordConfirmedHit(playerActor.Equipment.CurrentWeaponItem.runtimeInstanceId,playerActor.Equipment.ActiveElement,EnemyAttackSequence.Next(),1f),"Actual parry energy setup rejected.");
                Require(energy.Normalized>=.999f,"Actual parry fixture energy not full.");float chargedEnergy=energy.Normalized;
                enemy=Spawn(service,collection,stage,player.transform);var executor=enemy.GetComponent<EnemyBossMaterialExecutor>();yield return null;yield return new WaitForFixedUpdate();
                Position(player.transform,ContactPoint(material.strikes[0],enemy.transform)+Vector3.up*.02f);player.transform.rotation=Quaternion.LookRotation(enemy.transform.position-player.transform.position);
                Require(enemy.AbilityController.TryStartAbility(material.ability,player.transform),"Actual attack start failed.");float deadline=Time.unscaledTime+15f;
                while(!enemy.AbilityController.IsParryThreatTo(target)){Require(Time.unscaledTime<deadline,"Actual parry threat never opened: "+material.materialId);yield return null;}
                var controller=player.GetComponent<PlayerParryController>();int before=controller!=null?controller.SuccessCount:0;float hp=playerActor.Health.CurrentHp,normalized=executor.NormalizedTime;
                Capture(Camera.main,Path.Combine(plan.output,material.materialId+"-game-camera.png"));
                Require(melee.TryStartHeavyAttack(enemy.transform.position-player.transform.position)==WeaponActionResult.Accepted,"Actual heavy input rejected.");controller=player.GetComponent<PlayerParryController>();Require(controller!=null,"Actual parry controller missing.");
                float success=Time.unscaledTime+1f;while(controller.SuccessCount==before){Require(Time.unscaledTime<success,"Actual player parry failed.");yield return null;}
                string grade=typeof(PlayerParryController).GetProperty("ActionGrade")?.GetValue(controller)?.ToString()??"Perfect";
                bool stunned=enemy.GetComponent<EnemyMovementReaction>().IsParryStunned;
                Require(grade=="Perfect"&&!executor.IsExecuting&&stunned&&playerActor.Health.CurrentHp==hp,
                    "Actual parry mismatch: grade="+grade+"; executing="+executor.IsExecuting+"; stunned="+stunned+"; hpBefore="+hp+"; hpAfter="+playerActor.Health.CurrentHp);
                cases.Add(new JObject{["pass"]=true,["id"]=material.materialId,["scenario"]="actualPlayerHeavyParry",["parryGrade"]=grade,["chargedEnergy"]=chargedEnergy,["nativeProgress"]=normalized,["successCount"]=controller.SuccessCount-before,["cancelled"]=true,["noPlayerDamage"]=true});Result("RUNNING");
                yield return new WaitForSecondsRealtime(.35f);service.Release(enemy);enemy=null;melee.CancelCurrentAttackState();
                float slowEnd=Time.unscaledTime+4f;while(Time.timeScale<.999f&&Time.unscaledTime<slowEnd)yield return null;
            }
        }
        finally{melee.CancelCurrentAttackState();if(enemy!=null&&enemy.IsLeased)service.Release(enemy);if(EnemyThemeTrialService.InArena&&!EnemyThemeTrialService.Busy)EnemyThemeTrialService.ToggleArena();}
    }
    static void Capture(Camera camera,string path)
    {
        Require(camera!=null,"Capture camera missing.");RenderTexture target=null;Texture2D pixels=null;var previous=RenderTexture.active;
        try
        {
            target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);target.Create();pixels=new Texture2D(1280,720,TextureFormat.RGBA32,false);
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0,false);pixels.Apply(false,false);File.WriteAllBytes(path,pixels.EncodeToPNG());
        }
        finally{RenderTexture.active=previous;if(target!=null){target.Release();Object.DestroyImmediate(target);}if(pixels!=null)Object.DestroyImmediate(pixels);}
    }
    static IEnumerator Extras()
    {
        Time.captureDeltaTime=1f/60;var collection=AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(CrustaspikanMaterialBuilder.CollectionPath);
        Require(collection.radialFillProfile?.Length==11&&collection.radialBorderProfile?.Length==11,"Native telegraph profiles missing.");var service=Service(collection);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);owned.Add(floor);floor.transform.position=Vector3.down*.5f;floor.transform.localScale=new Vector3(100,1,100);
        var victim=new GameObject("Extra physical target");owned.Add(victim);victim.layer=LayerMask.NameToLayer("Player");var capsule=victim.AddComponent<CapsuleCollider>();capsule.radius=.2f;capsule.height=1.55f;capsule.center=Vector3.up*.775f;
        var target=victim.AddComponent<CombatTarget>();target.Configure(CombatTeam.PlayerParty,false);target.ConfigureVolume(capsule.center,.5f,1.55f);var health=victim.GetComponent<CombatHealth>();health.SetMaxHp(100000,true);EnemyStrongAttackWarning.PlayerTarget=target;int damage=0;health.OnDamaged+=(_,__)=>damage++;
        var cameraRoot=new GameObject("Owned material overview camera");owned.Add(cameraRoot);var camera=cameraRoot.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=13;camera.farClipPlane=150;camera.transform.SetPositionAndRotation(new Vector3(22,27,-25),Quaternion.Euler(36,-35,0));camera.GetUniversalAdditionalCameraData().renderType=CameraRenderType.Base;
        foreach(var material in collection.attacks.Where(m=>m.delivery!=EnemyBossMaterialDelivery.Melee))
        {
            int before=damage;var actor=Spawn(service,collection,Vector3.up*.035f,victim.transform);var executor=actor.GetComponent<EnemyBossMaterialExecutor>();yield return null;
            Position(victim.transform,new Vector3(0,.035f,12));Require(actor.AbilityController.TryStartAbility(material.ability,victim.transform),"Extra ranged start rejected.");float deadline=Time.time+material.runtimeClip.length+4;
            while(executor.ImpactCount==0){Require(Time.time<deadline,"Native release missing.");yield return null;}
            var indicator=actor.GetComponentsInChildren<ProceduralGroundIndicator>(true).FirstOrDefault(p=>p.IsVisible&&p.Surface!=null);
            if(material.delivery==EnemyBossMaterialDelivery.Spit)Require(indicator!=null&&indicator.Shape==GroundIndicatorShape.Rectangle&&indicator.CorridorCapRadius==0,"Spit telegraph geometry mismatch.");
            Capture(camera,Path.Combine(plan.output,material.materialId+"-overview.png"));
            // Death after release must clear live flight and never damage its future target.
            Require(executor.ActiveProjectileCount>0,"No projectile in flight to cancel.");actor.Health.TakeDamage(new DamageInfo(1000000f,actor.transform.position,victim,Vector3.forward));
            Require(!executor.IsExecuting&&executor.ActiveProjectileCount==0&&!executor.IsRockHeld,"Death did not clear cast/flight.");
            yield return new WaitForSeconds(1.5f);Require(damage==before,"Damage survived caster death.");cases.Add(new JObject{["pass"]=true,["id"]=material.materialId,["scenario"]="flightDeathCleanupAndTelegraph",["damageAfterDeath"]=0});Result("RUNNING");if(actor.IsLeased)service.Release(actor);yield return null;
        }
        var edgeActor=Spawn(service,collection,Vector3.up*.035f,victim.transform);yield return null;
        var melee=collection.attacks.First(m=>m.delivery==EnemyBossMaterialDelivery.Melee);var strike=melee.strikes[0];Vector3 origin=strike.Origin(edgeActor.transform);
        Position(victim.transform,origin+Vector3.right*(strike.radius+.2f-.01f));Require(edgeActor.GetComponent<EnemyBossMaterialExecutor>().WouldHit(melee.ability,target),"Physical boundary overlap rejected.");
        Position(victim.transform,origin+Vector3.right*(strike.radius+.2f+.02f));Require(!edgeActor.GetComponent<EnemyBossMaterialExecutor>().WouldHit(melee.ability,target),"Enlarged combat volume expanded the damage boundary.");
        cases.Add(new JObject{["pass"]=true,["id"]=melee.materialId,["scenario"]="physicalBoundary",["bodyRadius"]=.2f,["combatRadius"]=.5f});service.Release(edgeActor);Result("RUNNING");
    }
    static void Return(string error)
    {
        if(plan==null)return;if(!string.IsNullOrEmpty(error))failure=error;Result(failure==null?"PASS":"FAIL");plan.phase="returning";Persist();if(OwnPlay)EditorApplication.isPlaying=false;
    }
    static void Changed(PlayModeStateChange state){if(plan!=null&&state==PlayModeStateChange.EnteredEditMode){plan.phase="returning";Persist();}}
    static void Reload(){if(plan!=null){if(plan.phase=="running"){failure="Own verifier interrupted by compilation.";plan.phase="returning";Result("FAIL");}Persist();}}
    static void FinishReturn()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        string env=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","");
        if(!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)||!string.IsNullOrEmpty(env)&&!Same(env,Account)||!string.IsNullOrEmpty(prepared)&&!Same(prepared,Account))return;
        if(host!=null&&routine!=null)host.StopCoroutine(routine);for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null)Object.DestroyImmediate(owned[i]);owned.Clear();host=null;routine=null;
        Time.captureDeltaTime=plan.captureDelta;if(Time.timeScale!=plan.timeScale)Time.timeScale=plan.timeScale;Application.runInBackground=plan.background;
        if(AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)==plan.fixture)EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(plan.previousStart)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(plan.previousStart);
        IsolatedSavePlayGuard.UseRealAccount();if(!plan.realPlayer&&plan.fixture=="Assets/Editor/Testers/Bosses/CrustaspikanFixture_"+plan.token+".unity"&&AssetDatabase.LoadAssetAtPath<SceneAsset>(plan.fixture)!=null)AssetDatabase.DeleteAsset(plan.fixture);
        var after=Scenes();File.WriteAllText(Path.Combine(plan.output,"return.json"),new JObject{["status"]=!IsolatedSavePlayGuard.RequiresAccountChoice&&JToken.DeepEquals(plan.scenes,after)?"PASS":"FAIL",["scenesBefore"]=plan.scenes,["scenesAfter"]=after,["guardChoice"]=IsolatedSavePlayGuard.RequiresAccountChoice,["active"]=IsolatedSavePlayGuard.ActiveDirectory,["prepared"]=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""),["expires"]=SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires",""),["environment"]=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),["startScene"]=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)}.ToString());
        SessionState.EraseString(Key+"plan");plan=null;
    }
}
