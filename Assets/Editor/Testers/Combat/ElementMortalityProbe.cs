using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

public static class ElementMortalityProbe
{
    public static string Progress { get; private set; } = "NOT_RUN";
    static IEnumerator routine;
    static int frame;
    static PlayerInputFacade input;static bool oldGameplay;static float initialPlayerY;
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
    static readonly List<ProfilerRecorder> costRecorders=new List<ProfilerRecorder>();
    static readonly List<string> costNames=new List<string>();
    static int enemyHits, heavyHits, derivedHits, casts, fatalities; static bool reloadLocked;
    static float firstHeavyTime,firstDerivedTime;
    static ProfilerRecorder draws,gc,sharedBuild;
    static bool sharedAura,oldShared,smallOnly,enteredArena;
    static EnemyThemeTrialHarness arenaUi;
    static readonly FrameTiming[] timing=new FrameTiming[1];
    static EnemyThemeTable selectedTheme;
    static string PathName=>Path.GetFullPath("../개인파일/코덱스산출/Combat/ElementStatusGoal20260927/MortalDischarge_"+selectedTheme.ThemeId+"_InputIsolated_Arena"+(smallOnly?"_SmallOnly":"")+(sharedAura?"_DelayedShared.txt":"_Delayed.txt"));
    public static string Begin(string themeId = "SpiderBrood", bool useSharedAura = false, bool onlySmall = false)
    {
        if(!Application.isPlaying || routine!=null)throw new Exception("idle Play required");
        selectedTheme=MapThemeCatalog.Resolve(themeId);
        if(selectedTheme==null || !selectedTheme.Validate(out _))throw new Exception("production map theme missing: "+themeId);
        oldShared=SharedLocalAuraRenderer.Enabled;sharedAura=useSharedAura;smallOnly=onlySmall;SharedLocalAuraRenderer.Enabled=sharedAura;
        enteredArena=false;arenaUi=null;rows.Clear();enemies.Clear();Progress="RUNNING";frame=-1;deadline=EditorApplication.timeSinceStartup+600;
        background=Application.runInBackground;Application.runInBackground=true;
        EditorApplication.LockReloadAssemblies();reloadLocked=true;routine=Run();EditorApplication.update+=Tick;return Progress;
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
        if(fatal)fatalities++; if(actual<=0||d.isDamageOverTime)return;
        if((d.playerAttackKind&PlayerAttackKind.Heavy)!=0){heavyHits++;if(float.IsNaN(firstHeavyTime))firstHeavyTime=Time.time;}
        else if((d.playerAttackKind&PlayerAttackKind.Elemental)!=0){derivedHits++;if(float.IsNaN(firstDerivedTime))firstDerivedTime=Time.time;}
    }
    static IEnumerator Run()
    {
        while(!UnityEngine.SceneManagement.SceneManager.GetSceneByName(PersistentSceneFlow.HideoutSceneName).isLoaded
            || (PersistentSceneFlow.Instance!=null&&PersistentSceneFlow.Instance.IsSwitching)
            || PlayerContext.Instance?.CurrentActor==null)yield return null;
        arenaUi=EnemyThemeTrialHarness.Current;
        if(arenaUi==null)throw new Exception("existing independent arena UI missing");
        if(!arenaUi.InArena){arenaUi.ToggleArena();enteredArena=true;}
        if(!arenaUi.InArena)throw new Exception("independent arena entry failed");
        yield return null;yield return null;
        player=PlayerContext.GetOrCreate().CurrentActor;melee=player.GetComponent<MeleeRuntime>();
        if(player.transform.position.x<900||player.transform.position.z<900)throw new Exception("player did not enter arena");
        playerHealth=player.GetComponent<CombatHealth>();oldMaxHp=playerHealth.MaxHp;playerHealth.SetMaxHp(1000000,true);playerHealth.OnDamageResolved+=PlayerHit;
        input=player.GetComponent<PlayerInputFacade>();if(input==null)input=UnityEngine.Object.FindFirstObjectByType<PlayerInputFacade>();oldGameplay=input!=null&&input.IsGameplayEnabled;input?.DisableGameplay();initialPlayerY=player.transform.position.y;
        oldWeapon=player.Equipment.CurrentWeaponItem;spawner=EnemySpawnService.Current;
        root=new GameObject("ElementMortalityProbe");
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
        foreach(var f in typeof(ElementCombatCostMarkers).GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static))f.GetValue(null);
        var handles=new List<Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle>();Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable(handles);
        foreach(var h in handles){var d=Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetDescription(h);if(d.Name.StartsWith("Overburst.Cost.")||d.Name.StartsWith("Overburst.Loot.")||d.Name.StartsWith("Overburst.Currency.")||d.Name=="Overburst.TransientVfx.Create"||d.Name=="ParticleSystem.UpdateJob"||d.Name=="ParticleSystem.GeometryJob"||d.Name=="ParticleSystem.WaitForPreviousRenderingToFinish"||d.Name=="ParticleSystem.Draw"||d.Name=="Gfx.WaitForPresentOnGfxThread"||d.Name=="Gfx.WaitForRenderThread"||d.Name=="RenderLoop.DrawSRPBatcher"){costNames.Add(d.Name);costRecorders.Add(ProfilerRecorder.StartNew(d.Category,d.Name,1));}}
        var vfxDefinition=AssetDatabase.LoadAssetAtPath<MeleeHeavyAttackDefinition>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Heavy/GreatswordHeavyAttack.asset");
        var dischargePrefabs=new[]{vfxDefinition.elementVfx.fireImpact,vfxDefinition.elementVfx.FireChainExplosion,vfxDefinition.elementVfx.iceImpact,vfxDefinition.elementVfx.iceShatter,vfxDefinition.elementVfx.electricImpact,vfxDefinition.elementVfx.electricChainLink}.Where(p=>p!=null).Distinct().ToArray();
        rows.Add("Inclusive CPU markers. Worker sums are not wall time; nested scopes overlap. Exactly one charged heavy per element/count; 5 stacks injected once before cast as upper-bound fixture, never refilled. Separate initial-impact/propagation/residual bins; 20-second aggregate is not heavy cost. Real monster HP preserved. Current VFX preparation enabled; no forced rapid repeated casts. Reward policy remains EncounterContext.Test with isolated account save.");
        rows.Add("Editor existing independent EnemyThemeDebugArena: production MapThemeCatalog "+selectedTheme.ThemeId+", official EnemySpawnService. "+(smallOnly?"User ratio: 50/100 all small, zero medium/elite.":"User ratio: 50=39small/10medium/1elite;100=78small/20medium/2elite.")+" Theme species weights retained. Actual AI/movement/animation/VFX and authored enemy HP; only player HP enlarged; public heavy entry. Not live dungeon layout, mouse input, or Player-build FPS guarantee.");
        rows.Add("Arena player="+player.transform.position+" InArena="+arenaUi.InArena);
        rows.Add(SystemInfo.processorType+" / "+SystemInfo.graphicsDeviceName+" / GameView "+Screen.width+"x"+Screen.height);
        foreach(int count in smallOnly?new[]{50,100}:new[]{50,100})
        {
            foreach(var element in new[]{WeaponElement.Fire,WeaponElement.Ice,WeaponElement.Electric})
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
                enemy.Health.OnDamageResolved+=EnemyHit;enemies.Add(enemy);
            }
            rows.Add("spawn to "+count+" synchronousMs="+((EditorApplication.timeSinceStartup-spawnBegin)*1000).ToString("F2"));
            float warm=Time.time+3;while(Time.time<warm)yield return null;

                melee.CancelCurrentAttackState();input?.CombatInputs?.Invalidate();float settle=Time.time+11;while(Time.time<settle)yield return null;var item=new ItemData(sword,1,ItemGrade.Common,1,element);
                if(!player.Equipment.EquipWeaponItem(item))throw new Exception("equip failed");
                PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
                yield return null;yield return null;
                int heavyRound=0;
                foreach(int phase in new[]{2})
                {
                    while(ElementChainScheduler.ActiveCastCount>0)yield return null;
                    foreach(var e in enemies)e.GetComponent<ElementalStatusController>().ClearAllStatuses();
                    enemyHits=heavyHits=derivedHits=casts=fatalities=0; int goldBefore=CurrencyPickupPool.Returns; bool rewards=EncounterContext.Test.CanGrantRewards; if(!rewards)throw new Exception("Test reward context inactive");
                    firstHeavyTime=firstDerivedTime=float.NaN;
                    string stage=count+" "+element+" "+(phase==0?"no-status":phase==1?"status":"heavy");Progress=stage;
                    if(phase==2)stage+="-round"+(++heavyRound);
                    var poolBefore=TransientVfxPool.GetStatistics(vfxDefinition.elementVfx.FireChainExplosion);
                    var samples=new double[4096,costNames.Count];int sampleCount=0;
                    float maxPlayerRise=0;
                    int sharedOwners=0,sharedSamples=0;var sharedCpu=new List<double>();
                    var frames=new List<double>();var gpu=new List<double>();var drawValues=new List<double>();var gcValues=new List<double>();
                    var segments=new List<string>();
                    bool statusInjected=false;bool castAttempted=false;
                    float end=Time.time+(phase==2?20:4),nextCast=Time.time+.2f;
                    while(Time.time<end)
                    {
                        if(phase>0&&!statusInjected)
                        {
                            statusInjected=true;
                            foreach(var e in enemies)for(int s=0;s<5;s++)e.GetComponent<ElementalStatusController>().TryApplyDirectHit(new ElementalStatusApplication(element,100,player.gameObject,item.runtimeInstanceId,true,false,e.transform.position,Vector3.forward));
                        }
                        if(phase==2 && !castAttempted && Time.time>=nextCast && !melee.IsAttackInProgress)
                        {
                            castAttempted=true;
                            var energy=player.GetComponent<OverburstElementEnergy>()??player.gameObject.AddComponent<OverburstElementEnergy>();
                            energy.Clear();energy.BindWeapon(item.runtimeInstanceId,element);for(int j=0;j<10;j++)energy.RecordConfirmedHit(item.runtimeInstanceId,element,j+1,10);
                            if(melee.TryStartHeavyAttack(Vector3.forward)==WeaponActionResult.Accepted)casts++;
                        }
                        int hitsBefore=heavyHits;
                        bool chainBefore=ElementChainScheduler.ActiveCastCount>0;
                        bool tailBefore=dischargePrefabs.Any(p=>TransientVfxPool.GetStatistics(p).Active>0);
                        yield return null;
                        bool tailNow=dischargePrefabs.Any(p=>TransientVfxPool.GetStatistics(p).Active>0);
                        segments.Add(phase!=2?"baseline":heavyHits>hitsBefore?"initial-impact":float.IsNaN(firstHeavyTime)?"windup":chainBefore||ElementChainScheduler.ActiveCastCount>0?"propagation":tailBefore||tailNow?"residual":"post-vfx");
                        maxPlayerRise=Mathf.Max(maxPlayerRise,player.transform.position.y-initialPlayerY);
                        frames.Add(Time.unscaledDeltaTime*1000.0);
                        if(sampleCount<4096){for(int m=0;m<costRecorders.Count;m++)samples[sampleCount,m]=costRecorders[m].Valid?costRecorders[m].LastValue/1000000.0:double.NaN;sampleCount++;}
                        sharedOwners=Math.Max(sharedOwners,SharedLocalAuraRenderer.OwnerCount);sharedSamples=Math.Max(sharedSamples,SharedLocalAuraRenderer.ActiveSampleCount);if(sharedBuild.Valid)sharedCpu.Add(sharedBuild.LastValue/1000000.0);
                        if(draws.Valid)drawValues.Add(draws.LastValue);if(gc.Valid)gcValues.Add(gc.LastValue);
                        FrameTimingManager.CaptureFrameTimings();if(FrameTimingManager.GetLatestTimings(1,timing)>0 && timing[0].gpuFrameTime>0&&timing[0].gpuFrameTime<1000)gpu.Add(timing[0].gpuFrameTime);
                    }
                    int alive=0,aggro=0;foreach(var e in enemies){if(e==null)continue;if(e.gameObject.activeInHierarchy&&!e.Health.IsDead)alive++;if(e.AI.Target!=null)aggro++;}
                    float firstDelay=firstDerivedTime-firstHeavyTime;
                    rows.Add(stage+" alive="+alive+" targets="+aggro+" enemyHits="+enemyHits+" casts="+casts+" heavyHits="+heavyHits+" derivedHits="+derivedHits+" firstDerivedDelay="+firstDelay+" frameMs="+Summary(frames)+" GPUms="+Summary(gpu)+" draws="+Summary(drawValues)+" GCbytes="+Summary(gcValues)+" sharedOwnersMax="+sharedOwners+" sharedSamplesMax="+sharedSamples+" sharedBuildMs="+Summary(sharedCpu));
                    for(int m=0;m<costNames.Count;m++){var values=new List<double>(sampleCount);for(int f=0;f<sampleCount;f++)values.Add(samples[f,m]);rows.Add(stage+" COST "+costNames[m]+" ms="+Summary(values));}
                    var worst=Enumerable.Range(0,Math.Min(sampleCount,frames.Count)).OrderByDescending(i=>frames[i]).Take(3);
                    foreach(var segment in segments.Distinct())
                    {
                        var indices=Enumerable.Range(0,Math.Min(sampleCount,frames.Count)).Where(i=>segments[i]==segment).ToArray();
                        rows.Add(stage+" SEGMENT "+segment+" frames="+indices.Length+" frameMs="+Summary(indices.Select(i=>frames[i]).ToList()));
                        for(int m=0;m<costNames.Count;m++)rows.Add(stage+" SEGMENT "+segment+" COST "+costNames[m]+" ms="+Summary(indices.Select(i=>samples[i,m]).ToList()));
                    }
                    foreach(var f in worst){rows.Add(stage+" WORST frameIndex="+f+" wallMs="+frames[f]+" "+string.Join(" ",Enumerable.Range(0,costNames.Count).Select(m=>costNames[m]+"="+samples[f,m].ToString("F3"))));}
                    var poolAfter=TransientVfxPool.GetStatistics(vfxDefinition.elementVfx.FireChainExplosion);rows.Add(stage+" FIREPOOL requests="+(poolAfter.Requests-poolBefore.Requests)+" created="+(poolAfter.Created-poolBefore.Created)+" destroyed="+(poolAfter.Destroyed-poolBefore.Destroyed)+" misses="+(poolAfter.Misses-poolBefore.Misses)+" active="+poolAfter.Active+" idle="+poolAfter.Idle+" peak="+poolAfter.PeakActive);
                    rows.Add(stage+" PLAYER maxRise="+maxPlayerRise);
                    File.WriteAllLines(PathName,rows);
                    if(phase<2&&(heavyHits!=0||derivedHits!=0))throw new Exception("unexpected attack in baseline "+stage);
                    rows.Add(stage+" MORTAL fatalities="+fatalities+" rewardAllowed="+rewards+" goldReturned="+(CurrencyPickupPool.Returns-goldBefore)); if(fatalities==0)throw new Exception("No mortality coverage");
                    if(phase==2&&(casts!=1||heavyHits==0||ElementChainScheduler.ActiveCastCount!=0))throw new Exception("single discharge incomplete "+stage);
                    if(phase==2&&dischargePrefabs.Any(p=>TransientVfxPool.GetStatistics(p).Active!=0))throw new Exception("VFX tail not complete "+stage);
                    if(phase==2 && derivedHits>0 && element!=WeaponElement.Ice && (float.IsNaN(firstDelay)||firstDelay<(element==WeaponElement.Fire?.19f:.07f)))throw new Exception("missing delayed damage "+stage);
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
    static string Summary(List<double>a){if(a.Count==0)return "UNAVAILABLE";a=new List<double>(a);a.Sort();double sum=0;foreach(double x in a)sum+=x;return "avg:"+(sum/a.Count).ToString("F3")+",p95:"+a[Math.Min(a.Count-1,(int)(a.Count*.95))].ToString("F3")+",max:"+a[a.Count-1].ToString("F3");}
    static void Finish(string error)
    {
        try {
        if(input!=null&&oldGameplay)input.EnableGameplay();input=null;
        EditorApplication.update-=Tick;routine=null;foreach(var recorder in costRecorders)recorder.Dispose();costRecorders.Clear();costNames.Clear();
        melee?.CancelCurrentAttackState();foreach(var e in enemies)if(e!=null){e.Health.OnDamageResolved-=EnemyHit;spawner.Release(e);}enemies.Clear();
        if(root!=null)UnityEngine.Object.DestroyImmediate(root);
        if(playerHealth!=null){playerHealth.OnDamageResolved-=PlayerHit;playerHealth.SetMaxHp(oldMaxHp,true);}
        if(player!=null){if(oldWeapon!=null)player.Equipment.EquipWeaponItem(oldWeapon);else player.Equipment.ClearCurrentWeapon();player.GetComponent<OverburstElementEnergy>()?.Clear();}
        if(enteredArena&&arenaUi!=null&&arenaUi.InArena)arenaUi.ToggleArena();enteredArena=false;
        draws.Dispose();gc.Dispose();sharedBuild.Dispose();SharedLocalAuraRenderer.Enabled=oldShared;Application.runInBackground=background;
        Progress=error==null?"COMPLETE":"FAIL "+error;rows.Add(Progress);File.WriteAllLines(PathName,rows);
        } finally {if(reloadLocked){reloadLocked=false;EditorApplication.UnlockReloadAssemblies();}}
    }
}
