using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

public static class ElementCrowdCombatProbe
{
    public static string Progress { get; private set; } = "NOT_RUN";
    static IEnumerator routine;
    static int frame;
    static double deadline;
    static bool background;
    static PlayerActorRuntime player;
    static MeleeRuntime melee;
    static CombatHealth playerHealth;
    static float oldMaxHp;
    static ItemData oldWeapon;
    static EnemySpawnService spawner;
    static GameObject root;
    static readonly List<EnemyActor> enemies=new List<EnemyActor>();
    static readonly List<string> rows=new List<string>();
    static int enemyHits, heavyHits, derivedHits, casts;
    static float firstHeavyTime,firstDerivedTime;
    static ProfilerRecorder draws,gc,sharedBuild;
    static bool sharedAura,oldShared,smallOnly,enteredArena;
    static EnemyThemeTrialHarness arenaUi;
    static readonly FrameTiming[] timing=new FrameTiming[1];
    static EnemyThemeTable selectedTheme;
    static string PathName=>Path.GetFullPath("../개인파일/코덱스산출/Combat/ElementStatusGoal20260927/CrowdCombat_"+selectedTheme.ThemeId+"_Arena"+(smallOnly?"_SmallOnly":"")+(sharedAura?"_DelayedShared.txt":"_Delayed.txt"));
    public static string Begin(string themeId = "SpiderBrood", bool useSharedAura = false, bool onlySmall = false)
    {
        if(!Application.isPlaying || routine!=null)throw new Exception("idle Play required");
        selectedTheme=MapThemeCatalog.Resolve(themeId);
        if(selectedTheme==null || !selectedTheme.Validate(out _))throw new Exception("production map theme missing: "+themeId);
        oldShared=SharedLocalAuraRenderer.Enabled;sharedAura=useSharedAura;smallOnly=onlySmall;SharedLocalAuraRenderer.Enabled=sharedAura;
        enteredArena=false;arenaUi=null;rows.Clear();enemies.Clear();Progress="RUNNING";frame=-1;deadline=EditorApplication.timeSinceStartup+420;
        background=Application.runInBackground;Application.runInBackground=true;
        routine=Run();EditorApplication.update+=Tick;return Progress;
    }
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();if(frame==Time.frameCount)return;frame=Time.frameCount;
        try { if(!Application.isPlaying || EditorApplication.timeSinceStartup>deadline)throw new Exception("timeout/interrupted");if(!routine.MoveNext())Finish(null); }
        catch(Exception e){Finish(e.ToString());}
    }
    static void PlayerHit(CombatHealth h,DamageInfo d,float actual,bool fatal){if(actual>0)enemyHits++;}
    static void EnemyHit(CombatHealth h,DamageInfo d,float actual,bool fatal)
    {
        if(actual<=0||d.isDamageOverTime)return;
        if((d.playerAttackKind&PlayerAttackKind.Heavy)!=0){heavyHits++;if(float.IsNaN(firstHeavyTime))firstHeavyTime=Time.time;}
        else if((d.playerAttackKind&PlayerAttackKind.Elemental)!=0){derivedHits++;if(float.IsNaN(firstDerivedTime))firstDerivedTime=Time.time;}
    }
    static IEnumerator Run()
    {
        while(PersistentSceneFlow.Instance==null||PersistentSceneFlow.Instance.IsSwitching||PersistentSceneFlow.Instance.CurrentSubSceneName!=PersistentSceneFlow.HideoutSceneName)yield return null;
        arenaUi=EnemyThemeTrialHarness.Current;
        if(arenaUi==null)throw new Exception("existing independent arena UI missing");
        if(!arenaUi.InArena){arenaUi.ToggleArena();enteredArena=true;}
        if(!arenaUi.InArena)throw new Exception("independent arena entry failed");
        yield return null;yield return null;
        player=PlayerContext.GetOrCreate().CurrentActor;melee=player.GetComponent<MeleeRuntime>();
        if(player.transform.position.x<900||player.transform.position.z<900)throw new Exception("player did not enter arena");
        playerHealth=player.GetComponent<CombatHealth>();oldMaxHp=playerHealth.MaxHp;playerHealth.SetMaxHp(1000000,true);playerHealth.OnDamageResolved+=PlayerHit;
        oldWeapon=player.Equipment.CurrentWeaponItem;spawner=EnemySpawnService.Current;
        root=new GameObject("ElementCrowdCombatProbe");
        if(spawner==null)
        {
            var poolRoot=new GameObject("inactive pool");poolRoot.transform.SetParent(root.transform);poolRoot.SetActive(false);
            var pool=root.AddComponent<EnemyPoolService>();pool.Configure(poolRoot.transform,0);
            spawner=root.AddComponent<EnemySpawnService>();
            spawner.Configure(selectedTheme.Catalog,pool);
        }
        else if(!spawner.RegisterAdditionalCatalog(selectedTheme.Catalog,out var reason))throw new Exception(reason);
        var sword=AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
        draws=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Draw Calls Count",1);gc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1);
        sharedBuild=ProfilerRecorder.StartNew(ProfilerCategory.Scripts,"Overburst.SharedLocalAura.Build",1);
        rows.Add("Editor existing independent EnemyThemeDebugArena: production MapThemeCatalog "+selectedTheme.ThemeId+", official EnemySpawnService. "+(smallOnly?"User ratio: 50/100/200/300 all small, zero medium/elite.":"User ratio: 50=39small/10medium/1elite;100=78small/20medium/2elite.")+" Theme species weights retained. Actual AI/movement/animation/VFX; high HP; public heavy entry. Not live dungeon layout, mouse input, or Player-build FPS guarantee.");
        rows.Add("Arena player="+player.transform.position+" InArena="+arenaUi.InArena);
        rows.Add(SystemInfo.processorType+" / "+SystemInfo.graphicsDeviceName+" / GameView "+Screen.width+"x"+Screen.height);
        foreach(int count in smallOnly?new[]{50,100,200,300}:new[]{50,100})
        {
            foreach(var e in enemies)if(e!=null){e.Health.OnDamageResolved-=EnemyHit;spawner.Release(e);}enemies.Clear();
            Progress="spawning "+count;double spawnBegin=EditorApplication.timeSinceStartup;
            var roster=smallOnly?selectedTheme.BuildRoster(count,0,0,27100):BuildProductionRoster(selectedTheme,count);
            rows.Add("roster "+count+": "+string.Join(", ",roster.GroupBy(d=>d.EnemyId).Select(g=>g.Key+"="+g.Count())));
            while(enemies.Count<count)
            {
                int i=enemies.Count;float angle=i*2.399963f;float radius=4f+(i%5)*.6f;
                Vector3 point=player.transform.position+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*radius;
                int groundLayer=LayerMask.NameToLayer("Ground");int mask=groundLayer>=0?1<<groundLayer:Physics.DefaultRaycastLayers;
                if(Physics.Raycast(point+Vector3.up*4,Vector3.down,out var hit,12,mask,QueryTriggerInteraction.Ignore))point.y=hit.point.y+.1f;
                var enemy=spawner.Spawn(new EnemySpawnRequest(roster[i],point,Quaternion.identity,player.transform,root,player.transform,root.transform,seed:i));
                if(enemy==null)throw new Exception("spawn failed "+i);
                enemy.Health.SetMaxHp(100000,true);enemy.Health.OnDamageResolved+=EnemyHit;enemies.Add(enemy);
            }
            rows.Add("spawn to "+count+" synchronousMs="+((EditorApplication.timeSinceStartup-spawnBegin)*1000).ToString("F2"));
            float warm=Time.time+3;while(Time.time<warm)yield return null;
            foreach(var element in new[]{WeaponElement.Fire,WeaponElement.Ice,WeaponElement.Electric})
            {
                melee.CancelCurrentAttackState();var item=new ItemData(sword,1,ItemGrade.Common,1,element);
                if(!player.Equipment.EquipWeaponItem(item))throw new Exception("equip failed");
                PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
                yield return null;yield return null;
                foreach(int phase in new[]{0,1,2})
                {
                    while(ElementChainScheduler.ActiveCastCount>0)yield return null;
                    foreach(var e in enemies)e.GetComponent<ElementalStatusController>().ClearAllStatuses();
                    enemyHits=heavyHits=derivedHits=casts=0;
                    firstHeavyTime=firstDerivedTime=float.NaN;
                    string stage=count+" "+element+" "+(phase==0?"no-status":phase==1?"status":"heavy");Progress=stage;
                    int sharedOwners=0,sharedSamples=0;var sharedCpu=new List<double>();
                    var frames=new List<double>();var gpu=new List<double>();var drawValues=new List<double>();var gcValues=new List<double>();
                    float end=Time.time+4,nextStatus=Time.time,nextCast=Time.time+.2f;
                    while(Time.time<end)
                    {
                        if(phase>0&&Time.time>=nextStatus)
                        {
                            nextStatus=Time.time+2;
                            foreach(var e in enemies)for(int s=0;s<5;s++)e.GetComponent<ElementalStatusController>().TryApplyDirectHit(new ElementalStatusApplication(element,100,player.gameObject,item.runtimeInstanceId,true,false,e.transform.position,Vector3.forward));
                        }
                        if(phase==2 && Time.time>=nextCast && !melee.IsAttackInProgress)
                        {
                            var energy=player.GetComponent<OverburstElementEnergy>()??player.gameObject.AddComponent<OverburstElementEnergy>();
                            energy.Clear();energy.BindWeapon(item.runtimeInstanceId,element);for(int j=0;j<10;j++)energy.RecordConfirmedHit(item.runtimeInstanceId,element,j+1,10);
                            if(melee.TryStartHeavyAttack(Vector3.forward)==WeaponActionResult.Accepted)casts++;
                            nextCast=Time.time+1.5f;
                        }
                        yield return null;
                        frames.Add(Time.unscaledDeltaTime*1000.0);
                        sharedOwners=Math.Max(sharedOwners,SharedLocalAuraRenderer.OwnerCount);sharedSamples=Math.Max(sharedSamples,SharedLocalAuraRenderer.ActiveSampleCount);if(sharedBuild.Valid)sharedCpu.Add(sharedBuild.LastValue/1000000.0);
                        if(draws.Valid)drawValues.Add(draws.LastValue);if(gc.Valid)gcValues.Add(gc.LastValue);
                        FrameTimingManager.CaptureFrameTimings();if(FrameTimingManager.GetLatestTimings(1,timing)>0 && timing[0].gpuFrameTime>0&&timing[0].gpuFrameTime<1000)gpu.Add(timing[0].gpuFrameTime);
                    }
                    int alive=0,aggro=0;foreach(var e in enemies){if(e.gameObject.activeInHierarchy&&!e.Health.IsDead)alive++;if(e.AI.Target!=null)aggro++;}
                    float firstDelay=firstDerivedTime-firstHeavyTime;
                    rows.Add(stage+" alive="+alive+" targets="+aggro+" enemyHits="+enemyHits+" casts="+casts+" heavyHits="+heavyHits+" derivedHits="+derivedHits+" firstDerivedDelay="+firstDelay+" frameMs="+Summary(frames)+" GPUms="+Summary(gpu)+" draws="+Summary(drawValues)+" GCbytes="+Summary(gcValues)+" sharedOwnersMax="+sharedOwners+" sharedSamplesMax="+sharedSamples+" sharedBuildMs="+Summary(sharedCpu));
                    File.WriteAllLines(PathName,rows);
                    if(alive!=count)throw new Exception("fixture lost enemies");
                    if(phase==2 && element!=WeaponElement.Ice && (float.IsNaN(firstDelay)||firstDelay<(element==WeaponElement.Fire?.19f:.07f)))throw new Exception("missing delayed damage "+stage);
                    melee.CancelCurrentAttackState();
                }
            }
        }
        while(ElementChainScheduler.ActiveCastCount>0)yield return null;
    }
    public static List<EnemyDefinition> BuildProductionRoster(EnemyThemeTable theme,int count)
    {
        int medium=Mathf.RoundToInt(count*.2f),elite=count>=100?2:1;
        return theme.BuildRoster(count-medium-elite,medium,elite,27100);
    }
    static string Summary(List<double>a){if(a.Count==0)return "UNAVAILABLE";a.Sort();double sum=0;foreach(double x in a)sum+=x;return "avg:"+(sum/a.Count).ToString("F3")+",p95:"+a[Math.Min(a.Count-1,(int)(a.Count*.95))].ToString("F3")+",max:"+a[a.Count-1].ToString("F3");}
    static void Finish(string error)
    {
        EditorApplication.update-=Tick;routine=null;
        melee?.CancelCurrentAttackState();foreach(var e in enemies)if(e!=null){e.Health.OnDamageResolved-=EnemyHit;spawner.Release(e);}enemies.Clear();
        if(root!=null)UnityEngine.Object.DestroyImmediate(root);
        if(playerHealth!=null){playerHealth.OnDamageResolved-=PlayerHit;playerHealth.SetMaxHp(oldMaxHp,true);}
        if(player!=null){if(oldWeapon!=null)player.Equipment.EquipWeaponItem(oldWeapon);else player.Equipment.ClearCurrentWeapon();player.GetComponent<OverburstElementEnergy>()?.Clear();}
        if(enteredArena&&arenaUi!=null&&arenaUi.InArena)arenaUi.ToggleArena();enteredArena=false;
        draws.Dispose();gc.Dispose();sharedBuild.Dispose();SharedLocalAuraRenderer.Enabled=oldShared;Application.runInBackground=background;
        Progress=error==null?"COMPLETE":"FAIL "+error;rows.Add(Progress);File.WriteAllLines(PathName,rows);
    }
}
