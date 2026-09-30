using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

public static class ElementNaturalChargeProbe
{
    public static string Progress { get; private set; } = "NOT_RUN";
    static OverburstElementEnergy checkedEnergy;
    static WeaponElement activeElement;
    static float energyBase;
    static int weakHits,duplicateChecks;
    static readonly Dictionary<long,float> phaseGains=new Dictionary<long,float>();
    static readonly Dictionary<string,int> firstStack=new Dictionary<string,int>();
    static string eventFailure;
    static bool radiusValidation;
    static bool reloadLocked;
    static string reportLabel;
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
    static int enemyHits, heavyHits, derivedHits, casts;
    static float firstHeavyTime,firstDerivedTime;
    static ProfilerRecorder draws,gc,sharedBuild;
    static bool sharedAura,oldShared,smallOnly,enteredArena;
    static EnemyThemeTrialHarness arenaUi;
    static readonly FrameTiming[] timing=new FrameTiming[1];
    static EnemyThemeTable selectedTheme;
    static string PathName=>Path.GetFullPath("../개인파일/코덱스산출/Combat/ElementStatusGoal20260927/"+reportLabel+(radiusValidation?"RadiusNatural_":"NaturalCharge_PreparedVfx_")+selectedTheme.ThemeId+"_InputIsolated_Arena"+(smallOnly?"_SmallOnly":"")+(sharedAura?"_DelayedShared.txt":"_Delayed.txt"));
    public static string Begin(string themeId = "SpiderBrood", bool useSharedAura = false, bool onlySmall = false, bool validateRadius = false, string outputPrefix = "")
    {
        if(!Application.isPlaying || routine!=null)throw new Exception("idle Play required");
        radiusValidation=validateRadius;
        reportLabel=outputPrefix;
        selectedTheme=MapThemeCatalog.Resolve(themeId);
        if(selectedTheme==null || !selectedTheme.Validate(out _))throw new Exception("production map theme missing: "+themeId);
        oldShared=SharedLocalAuraRenderer.Enabled;sharedAura=useSharedAura;smallOnly=onlySmall;SharedLocalAuraRenderer.Enabled=sharedAura;
        eventFailure=null;enteredArena=false;arenaUi=null;rows.Clear();enemies.Clear();Progress="RUNNING";frame=-1;deadline=EditorApplication.timeSinceStartup+600;
        background=Application.runInBackground;Application.runInBackground=true;
        EditorApplication.LockReloadAssemblies();reloadLocked=true;
        routine=Run();EditorApplication.update+=Tick;return Progress;
    }
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();if(frame==Time.frameCount)return;frame=Time.frameCount;
        try { if(!Application.isPlaying || EditorApplication.timeSinceStartup>deadline)throw new Exception("timeout/interrupted");if(eventFailure!=null)throw new Exception(eventFailure);if(!routine.MoveNext())Finish(null); }
        catch(Exception e){Finish(e.ToString());}
    }
    static void PlayerHit(CombatHealth h,DamageInfo d,float actual,bool fatal){if(actual>0)enemyHits++;}
    static void EnemyHit(CombatHealth h,DamageInfo d,float actual,bool fatal)
    {
        if(actual<=0||d.isDamageOverTime)return;
        if((d.playerAttackKind&PlayerAttackKind.Heavy)!=0){heavyHits++;if(float.IsNaN(firstHeavyTime))firstHeavyTime=Time.time;}
        else if((d.playerAttackKind&PlayerAttackKind.Elemental)!=0&&!d.triggersOnHitEffects){derivedHits++;if(float.IsNaN(firstDerivedTime))firstDerivedTime=Time.time;}
        else if(d.triggersOnHitEffects&&d.sourceAttackSequenceId>0&&checkedEnergy!=null)
        {
            weakHits++;
            long key=((long)d.sourceAttackSequenceId<<32)|(uint)d.sourceAttackPhaseIndex;
            var tuning=OverburstElementTuning.Current;
            float gain=(d.isCritical?tuning.maximumEnergy*tuning.criticalEnergyFraction:tuning.energyPerAttack)*(1+FlaskCombatModifiers.Bonus(player.gameObject,FlaskEffect.EnergyGain));
            phaseGains.TryGetValue(key,out float previous);phaseGains[key]=Mathf.Max(previous,gain);
            float expected=Mathf.Min(tuning.maximumEnergy,energyBase+phaseGains.Values.Sum());
            if(Mathf.Abs(checkedEnergy.Amount-expected)>.001f)eventFailure="energy phase mismatch actual="+checkedEnergy.Amount+" expected="+expected;
            string targetKey=h.GetInstanceID()+":"+d.sourceAttackSequenceId;
            int stack=h.GetComponent<ElementalStatusController>().GetStackCount(activeElement);
            if(firstStack.TryGetValue(targetKey,out int initial)){duplicateChecks++;if(stack>initial)eventFailure="multiple stacks from one attack input";}
            else firstStack[targetKey]=stack;
        }
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
        input=player.GetComponent<PlayerInputFacade>();if(input==null)input=UnityEngine.Object.FindFirstObjectByType<PlayerInputFacade>();oldGameplay=input!=null&&input.IsGameplayEnabled;input?.DisableGameplay();initialPlayerY=player.transform.position.y;
        oldWeapon=player.Equipment.CurrentWeaponItem;spawner=EnemySpawnService.Current;
        root=new GameObject("ElementNaturalChargeProbe");
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
        foreach(var h in handles){var d=Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetDescription(h);if(d.Name.StartsWith("Overburst.Cost.")||d.Name=="Overburst.TransientVfx.Create"||d.Name=="ParticleSystem.UpdateJob"||d.Name=="ParticleSystem.GeometryJob"||d.Name=="ParticleSystem.WaitForPreviousRenderingToFinish"||d.Name=="ParticleSystem.Draw"||d.Name=="Gfx.WaitForPresentOnGfxThread"||d.Name=="Gfx.WaitForRenderThread"||d.Name=="RenderLoop.DrawSRPBatcher"){costNames.Add(d.Name);costRecorders.Add(ProfilerRecorder.StartNew(d.Category,d.Name,1));}}
        var vfxDefinition=AssetDatabase.LoadAssetAtPath<MeleeHeavyAttackDefinition>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Heavy/GreatswordHeavyAttack.asset");
        var dischargePrefabs=new[]{vfxDefinition.elementVfx.fireImpact,vfxDefinition.elementVfx.iceImpact,vfxDefinition.elementVfx.iceShatter,vfxDefinition.elementVfx.electricImpact,vfxDefinition.elementVfx.electricChainLink,vfxDefinition.elementVfx.electricChainStart,vfxDefinition.elementVfx.electricChainProc}.Where(p=>p!=null).Distinct().ToArray();
        rows.Add("Natural weak attack / combo continuation / charged heavy, two cycles per element/count. No injected damage, statuses, or energy. Actual monster AI and large HP. Public action API driven each valid window, not human input or a minimum recharge-time guarantee. No frame-performance claim.");
        rows.Add("Editor existing independent EnemyThemeDebugArena: production MapThemeCatalog "+selectedTheme.ThemeId+", official EnemySpawnService. "+(smallOnly?"User ratio: 50/100 all small, zero medium/elite.":"User ratio: 50=39small/10medium/1elite;100=78small/20medium/2elite.")+" Theme species weights retained. Actual AI/movement/animation/VFX; high HP; public heavy entry. Not live dungeon layout, mouse input, or Player-build FPS guarantee.");
        rows.Add("Arena player="+player.transform.position+" InArena="+arenaUi.InArena);
        rows.Add(SystemInfo.processorType+" / "+SystemInfo.graphicsDeviceName+" / GameView "+Screen.width+"x"+Screen.height);
        foreach(int count in radiusValidation?new[]{50}:new[]{50,100})
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
                melee.CancelCurrentAttackState();input?.CombatInputs?.Invalidate();float settle=Time.time+11;while(Time.time<settle)yield return null;var item=new ItemData(sword,1,ItemGrade.Common,1,element);
                if(!player.Equipment.EquipWeaponItem(item))throw new Exception("equip failed");
                PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
                yield return null;yield return null;
                foreach(var enemy in enemies)enemy.GetComponent<ElementalStatusController>().ClearAllStatuses();
                var energy=player.GetComponent<OverburstElementEnergy>()??player.gameObject.AddComponent<OverburstElementEnergy>();
                energy.Clear(); // Reset fixture once. No injected hits, stacks or energy.
                activeElement=element;checkedEnergy=energy;phaseGains.Clear();firstStack.Clear();weakHits=duplicateChecks=0;
                enemyHits=heavyHits=derivedHits=casts=0;firstHeavyTime=firstDerivedTime=float.NaN;
                var impactPrefab=vfxDefinition.elementVfx.GetImpact(element);
                var impactBefore=TransientVfxPool.GetStatistics(impactPrefab);
                var chainBefore=TransientVfxPool.GetStatistics(vfxDefinition.elementVfx.FireChainExplosion);
                float lastImpact=float.NaN;
                for(int cycle=1;cycle<=2;cycle++)
                {
                    Progress=count+" "+element+" natural-charge "+cycle;
                    phaseGains.Clear();firstStack.Clear();energyBase=energy.Amount;int inputs=0;int hitsBefore=weakHits;
                    float chargeBegin=Time.time;float chargeDeadline=Time.time+45;
                    var chargeFrames=new List<double>();
                    var handle=WeaponActionHandle.Invalid;
                    while(energy.Normalized<.999f)
                    {
                        if(Time.time>chargeDeadline)throw new Exception("natural charge timeout "+Progress+" energy="+energy.Amount);
                        var request=new WeaponActionRequest(WeaponActionSource.PlayerInput,null,AimAtNearest());
                        if(!melee.IsAttackInProgress)
                        {
                            if(melee.TryStartAction(request,out handle)==WeaponActionResult.Accepted)inputs++;
                        }
                        else if(melee.TryContinue(handle,request)==WeaponActionResult.Accepted)inputs++;
                        yield return null;chargeFrames.Add(Time.unscaledDeltaTime*1000.0);
                    }
                    while(melee.IsAttackInProgress)yield return null;
                    int[] stacks=new int[6];int frozen=0;
                    foreach(var enemy in enemies){var status=enemy.GetComponent<ElementalStatusController>();stacks[Mathf.Clamp(status.GetStackCount(element),0,5)]++;if(status.IsFrozen)frozen++;}
                    rows.Add(Progress+" chargedSeconds="+(Time.time-chargeBegin)+" inputs="+inputs+" weakHits="+(weakHits-hitsBefore)+" energy="+energy.Amount+" stackHistogram0to5="+string.Join(",",stacks)+" frozen="+frozen+" phaseKeys="+phaseGains.Count+" duplicateStackChecks="+duplicateChecks);
                    var burstPrefab=element==WeaponElement.Fire?vfxDefinition.elementVfx.FireChainExplosion:element==WeaponElement.Ice?vfxDefinition.elementVfx.iceShatter:impactPrefab;
                    var burstBefore=TransientVfxPool.GetStatistics(burstPrefab);
                    rows.Add("PREPREADY "+count+" "+element+" cycle="+cycle+" idle="+burstBefore.Idle+" active="+burstBefore.Active+" target="+MeleeElementPoolMaintenance.GetTarget(burstPrefab)+" chargeFrames="+Summary(chargeFrames));
                    float previousImpact=lastImpact;firstHeavyTime=firstDerivedTime=float.NaN;
                    int beforeHeavy=heavyHits,beforeDerived=derivedHits;
                    if(melee.TryStartHeavyAttack(AimAtNearest())!=WeaponActionResult.Accepted)throw new Exception("charged heavy rejected");
                    casts++;
                    while(float.IsNaN(firstHeavyTime)){if(Time.time>chargeDeadline+10)throw new Exception("heavy missed all targets");yield return null;}
                    lastImpact=firstHeavyTime;
                    if(radiusValidation)CheckRadius(vfxDefinition,element);
                    if(energy.Amount!=0)throw new Exception("heavy did not consume energy");
                    while(melee.IsAttackInProgress){if(energy.Amount!=0)throw new Exception("derived damage recharged energy");yield return null;}
                    if(radiusValidation&&element==WeaponElement.Fire)CheckFireChainRadius(vfxDefinition);
                    var burstAfter=TransientVfxPool.GetStatistics(burstPrefab);
                    rows.Add("HEAVYPOOL "+count+" "+element+" cycle="+cycle+" requests="+(burstAfter.Requests-burstBefore.Requests)+" created="+(burstAfter.Created-burstBefore.Created)+" misses="+(burstAfter.Misses-burstBefore.Misses)+" active="+burstAfter.Active+" idle="+burstAfter.Idle);
                    rows.Add(count+" "+element+" discharge="+cycle+" heavyHits="+(heavyHits-beforeHeavy)+" derivedSoFar="+(derivedHits-beforeDerived)+" energyAfter=0 impactInterval="+(lastImpact-previousImpact));
                    File.WriteAllLines(PathName,rows);
                }
                while(ElementChainScheduler.ActiveCastCount>0)yield return null;
                float finishTail=Time.time+12;while(Time.time<finishTail){if(energy.Amount!=0)throw new Exception("tail recharged energy");yield return null;}
                var impactAfter=TransientVfxPool.GetStatistics(impactPrefab);
                var chainAfter=TransientVfxPool.GetStatistics(vfxDefinition.elementVfx.FireChainExplosion);
                if((impactPrefab!=null&&impactAfter.Requests-impactBefore.Requests!=2)||impactAfter.Active!=0||chainAfter.Active!=0)throw new Exception("impact routing/tail mismatch");
                rows.Add("VFX "+count+" "+element+" landingRequests="+(impactAfter.Requests-impactBefore.Requests)+" chainRequests="+(chainAfter.Requests-chainBefore.Requests)+" landingActive="+impactAfter.Active+" chainActive="+chainAfter.Active);
                if(casts!=2||weakHits==0||duplicateChecks==0)throw new Exception("insufficient natural coverage");
                rows.Add("PASS "+count+" "+element+" natural two cycles; damage-derived energy checked per phase, repeat target input stacks deduplicated, heavy/chain no recharge; total derived="+derivedHits);
                File.WriteAllLines(PathName,rows);
                checkedEnergy=null;
            }
        }
        Progress="retiring preparation caches";
        if(radiusValidation)yield break;
        float retirement=Time.unscaledTime+36;while(Time.unscaledTime<retirement)yield return null;
        foreach(var prefab in new[]{vfxDefinition.elementVfx.fireImpact,vfxDefinition.elementVfx.FireChainExplosion,vfxDefinition.elementVfx.iceShatter,vfxDefinition.elementVfx.electricImpact})
        {
            if(prefab==null)continue;var stats=TransientVfxPool.GetStatistics(prefab);
            if(stats.Active!=0||stats.Idle!=0||MeleeElementPoolMaintenance.GetTarget(prefab)!=0)throw new Exception("preparation cache failed to retire: "+prefab.name);
            rows.Add("PASS retired "+prefab.name);
        }
    }
    static Transform[] ActiveInstances(GameObject prefab)=>Resources.FindObjectsOfTypeAll<Transform>()
        .Where(t=>t.gameObject.scene.IsValid()&&t.gameObject.activeInHierarchy&&t.name==prefab.name+"(Clone)").ToArray();
    static void CheckRadius(MeleeHeavyAttackDefinition definition,WeaponElement element)
    {
        var discharge=(OverburstElementDischarge)typeof(MeleeRuntime).GetField("activeDischarge",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(melee);
        float radius=discharge.Radius;
        var prefab=definition.elementVfx.GetImpact(element);
        if(prefab!=null)
        {
            var active=ActiveInstances(prefab);if(active.Length==0)throw new Exception("No live landing VFX "+element);
            float expected=definition.elementVfx.ImpactScale(element,radius);
            foreach(var t in active)if((t.localScale-prefab.transform.localScale*expected).sqrMagnitude>.00001f)throw new Exception("Landing radius scale mismatch");
        }
        var cue=definition.attack.attackPhases[0].vfxCues[0].definition;
        var circles=ActiveInstances(cue.neutralPrefab);if(circles.Length==0)throw new Exception("No live common heavy circle");
        foreach(var t in circles)if(Mathf.Abs(t.localScale.x*cue.authoredCircleRadius-radius)>.001f)throw new Exception("Common circle radius mismatch");
        rows.Add("PASS live radius "+element+" damage="+radius+" impactScale="+definition.elementVfx.ImpactScale(element,radius)+" commonCircle="+circles.Length);
    }
    static void CheckFireChainRadius(MeleeHeavyAttackDefinition definition)
    {
        var prefab=definition.elementVfx.FireChainExplosion;var active=ActiveInstances(prefab);
        if(active.Length==0)throw new Exception("No live chain explosion");
        foreach(var t in active)
        {
            float radius=t.localScale.x/prefab.transform.localScale.x*definition.elementVfx.FireChainReferenceRadius;
            float stack=(radius-1)/.2f;
            if(stack<-.001f||stack>4.001f||Mathf.Abs(stack-Mathf.Round(stack))>.001f)throw new Exception("Chain radius must be1..1.8 by consumed stack");
        }
        rows.Add("PASS live chain radius instances="+active.Length);
    }
    static Vector3 AimAtNearest()
    {
        EnemyActor nearest=null;float distance=float.PositiveInfinity;
        foreach(var enemy in enemies){if(enemy==null||enemy.Health.IsDead)continue;float d=(enemy.transform.position-player.transform.position).sqrMagnitude;if(d<distance){distance=d;nearest=enemy;}}
        Vector3 direction=nearest!=null?nearest.transform.position-player.transform.position:Vector3.forward;direction.y=0;return direction.sqrMagnitude>.0001f?direction.normalized:Vector3.forward;
    }
    public static List<EnemyDefinition> BuildProductionRoster(EnemyThemeTable theme,int count)
    {
        int medium=Mathf.RoundToInt(count*.2f),elite=count>=100?2:1;
        return theme.BuildRoster(count-medium-elite,medium,elite,27100);
    }
    static string Summary(List<double>a){if(a.Count==0)return "UNAVAILABLE";a=new List<double>(a);a.Sort();double sum=0;foreach(double x in a)sum+=x;return "avg:"+(sum/a.Count).ToString("F3")+",p95:"+a[Math.Min(a.Count-1,(int)(a.Count*.95))].ToString("F3")+",max:"+a[a.Count-1].ToString("F3");}
    static void Finish(string error)
    {
        try
        {
        if(input!=null&&oldGameplay)input.EnableGameplay();input=null;
        EditorApplication.update-=Tick;routine=null;foreach(var recorder in costRecorders)recorder.Dispose();costRecorders.Clear();costNames.Clear();
        melee?.CancelCurrentAttackState();foreach(var e in enemies)if(e!=null){e.Health.OnDamageResolved-=EnemyHit;spawner.Release(e);}enemies.Clear();
        if(root!=null)UnityEngine.Object.DestroyImmediate(root);
        if(playerHealth!=null){playerHealth.OnDamageResolved-=PlayerHit;playerHealth.SetMaxHp(oldMaxHp,true);}
        if(player!=null){if(oldWeapon!=null)player.Equipment.EquipWeaponItem(oldWeapon);else player.Equipment.ClearCurrentWeapon();player.GetComponent<OverburstElementEnergy>()?.Clear();}
        if(enteredArena&&arenaUi!=null&&arenaUi.InArena)arenaUi.ToggleArena();enteredArena=false;
        draws.Dispose();gc.Dispose();sharedBuild.Dispose();SharedLocalAuraRenderer.Enabled=oldShared;Application.runInBackground=background;
        Progress=error==null?"COMPLETE":"FAIL "+error;rows.Add(Progress);File.WriteAllLines(PathName,rows);
        ElementFrameCostCapture.Stop();
        }
        finally { if(reloadLocked){reloadLocked=false;EditorApplication.UnlockReloadAssemblies();} }
    }
}

