#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

namespace Overburst.DebugTools.Performance
{
    public sealed partial class CombatPerformanceRunner
    {
        struct CspRequest
        {
            public int seed,x,z,epoch,group,level,phase,attempts,retries,candidates,town,distance,ground,visible,capsule,crowded;
            public string definition;public double enqueued,firstAttempt,lastAttempt,activated;public Vector3 point;
        }
        readonly Dictionary<(int,int,int,int),CspRequest> cspRequests=new Dictionary<(int,int,int,int),CspRequest>(1024);
        int cspEpoch,cspAttempts,cspRetries,cspCandidates;
        void CspRequestTrace(CombatSpawnQualityTrace.Entry e)
        {
            if(e.kind==14){cspEpoch++;return;}
            if(e.kind==11)cspAttempts++;
            if(e.kind==12){cspRetries++;cspCandidates+=e.candidates;}
            if(e.kind!=10&&e.kind!=11&&e.kind!=12&&e.kind!=13)return;
            if(Run.profile.cspObservation!="Detailed")return;
            var key=(e.seed,e.cell.x,e.cell.y,cspEpoch);
            cspRequests.TryGetValue(key,out var r);
            double now=recorder.ElapsedSeconds;
            if(e.kind==10)
            {
                if(cspRequests.ContainsKey(key))throw new InvalidOperationException("duplicate request identity");
                if(cspRequests.Count>=1024)throw new InvalidOperationException("request summary capacity");
                r=new CspRequest{seed=e.seed,x=e.cell.x,z=e.cell.y,epoch=cspEpoch,group=e.group,level=e.level,phase=cspPhase,definition=e.definition,enqueued=now,firstAttempt=-1,lastAttempt=-1,activated=-1};
            }
            else if(!cspRequests.ContainsKey(key))throw new InvalidOperationException("request before enqueue");
            if(e.kind==11){r.attempts++;if(r.firstAttempt<0)r.firstAttempt=now;r.lastAttempt=now;}
            if(e.kind==12)r.retries++;
            if(e.kind==12||e.kind==13){r.candidates+=e.candidates;r.town+=e.town;r.distance+=e.distance;r.ground+=e.ground;r.visible+=e.visible;r.capsule+=e.capsule;r.crowded+=e.crowded;}
            if(e.kind==13){r.activated=now;r.point=e.point;}
            cspRequests[key]=r;
        }
        void CspWriteRequests(string folder)
        {
            using(var w=new StreamWriter(Path.Combine(folder,"spawn-requests.csv"),false,new System.Text.UTF8Encoding(false)))
            {
                w.WriteLine("seed,cell_x,cell_z,epoch,group,level,phase,definition,enqueued,first_attempt,last_attempt,activated,attempts,retries,candidates,town,distance,ground,visible,capsule,crowded,x,y,z");
                foreach(var r in cspRequests.Values)w.WriteLine(string.Join(",",new[]{N(r.seed),N(r.x),N(r.z),N(r.epoch),N(r.group),N(r.level),N(r.phase),"\""+(r.definition??"").Replace("\"","\"\"")+"\"",N(r.enqueued),N(r.firstAttempt),N(r.lastAttempt),N(r.activated),N(r.attempts),N(r.retries),N(r.candidates),N(r.town),N(r.distance),N(r.ground),N(r.visible),N(r.capsule),N(r.crowded),N(r.point.x),N(r.point.y),N(r.point.z)}));
            }
        }

        struct CspFrame { public int frame, phase, active, pending, visited, hits, fatal, threatened, attacking, blocked, created, leased, returns, pickups, pickupsCreated, nameplates, vfxFixed, vfxNatural; public double seconds; public Vector3 point; public Vector2 stick; }
        struct CspEvent { public int phase, kind, frame, target, lease, sequence, attack, attackPhase, element, dot; public double seconds; public float damage, hp; public CombatSpawnQualityTrace.Entry spawn; }
        [Serializable] sealed class CspResult { public int schema=3, frames, events, dropped, hits, fatal, bound, activeBindings; public string program="CSP", goal="G00", observation, status, reason, boundary="Actual gamepad movement; terrain and enemy collision unchanged; no town immunity", threat="AI target/aggro observed; first accepted enemy attack unavailable; attack state is a separate observation"; public double setupMs, setupBytes, traceMs, traceBytes, setupMonoHeapDelta; public bool threadAllocationAvailable, monoHeapDeltaAvailable; public string[] missingMetrics; public string allocationScope="Known 1MiB sentinel validates API; heap delta is global and not retained-buffer or thread allocation"; public Vector3 start, exit; public List<string> checks=new List<string>(); }
        [Serializable] public sealed class CspRecordContract
        {
            public int schema, frameCapacity, eventCapacity, requestCapacity;
            public string program, goal, observation, clock, markerScope, workloadScope, traceScope;
            public string qualityBoundary, controlBoundary, exportBoundary, candidateAccounting, phase14, phase17, phase13;
            public string[] requestedMetrics, missingMetrics;
            public bool metricSampleCounts, threadAllocationAvailable, monoHeapDeltaAvailable, traceBinding;
        }
        CspFrame[] cspFrames; CspEvent[] cspEvents;
        int cspFrameCount, cspEventCount, cspPhase, cspHits, cspFatal, cspDropped, cspBound;
        CspResult cspResult;
        OutdoorWorldSpawner cspOutdoor; Gamepad cspPad; PlayerMovement cspMovement;
        ReadOnlyArray<InputDevice>? cspOldDevices;
        InputSettings cspOldSettings, cspSettings; bool cspWasInTown;
        Vector2 cspStick;
        readonly Dictionary<CombatHealth, EnemyActor> cspActors = new Dictionary<CombatHealth, EnemyActor>(512);
        readonly List<EnemyActor> cspTracked = new List<EnemyActor>(512);
        Action<CombatHealth,DamageInfo,float,bool> cspHitHandler;
        static string[] DetailedMetrics = { "Main Thread", "Render Thread", "PlayerLoop", "GC Allocated In Frame", "GC.Collect", "GC Used Memory", "Total Used Memory", "Physics.Simulate", "Animators.Update", "ParticleSystem.UpdateJob", "Gfx.WaitForPresentOnGfxThread", "WaitForTargetFPS", "Resources.Load", "Shader.CreateGPUProgram", "OVERBURST.MapSpawn.Detail.Acquire", "OVERBURST.MapSpawn.Detail.Prepare", "OVERBURST.MapSpawn.Detail.Activate", "OVERBURST.MapSpawn.Detail.Finalize", "OVERBURST.CSP.Outdoor.Total", "OVERBURST.CSP.Outdoor.Queue", "OVERBURST.CSP.Outdoor.Position", "Audio.Update", "Draw Calls Count", "SetPass Calls Count", "Triangles Count", "GC Reserved Memory", "Overburst.Cost.Pool.ResolveLifetime", "Overburst.Cost.Pool.Release", "Overburst.TransientVfx.Create", "Overburst.Loot.Resolve", "Overburst.Loot.Origin", "Overburst.Loot.Items" };
        static readonly string[] BaseMetrics={"GC Allocated In Frame","GC.Collect","GC Used Memory"};
        static readonly string[] TraceMetrics = { CombatSpawnQualityTrace.TraceObserverName };
        string[] CspRequiredMetrics() => Run.profile.cspObservation == "Detailed" ? CombatSpawnQualityTrace.DetailedMetricNames : TraceMetrics;
        string[] CspMetrics()
        {
            var core = Run.profile.cspObservation == "Detailed" ? DetailedMetrics : BaseMetrics;
            var extra = CspRequiredMetrics();
            var names = new string[core.Length + extra.Length];
            Array.Copy(core, names, core.Length); Array.Copy(extra, 0, names, core.Length, extra.Length);
            return names;
        }
        public static string CspInputContract(InputActionAsset asset)
        {
            if(!asset)return "UNAVAILABLE";
            var b=new System.Text.StringBuilder();
            void Token(string value){value=value??"";b.Append(value.Length).Append(':').Append(value);}
            foreach(var map in asset.actionMaps)
            {
                Token(map.name);
                foreach(var action in map.actions)
                {
                    Token(action.name);Token(action.type.ToString());Token(action.expectedControlType);Token(action.processors);Token(action.interactions);
                    foreach(var binding in action.bindings){Token(binding.name);Token(binding.path);Token(binding.effectivePath);Token(binding.groups);Token(binding.processors);Token(binding.overrideProcessors);Token(binding.interactions);Token(binding.overrideInteractions);Token(binding.isComposite.ToString());Token(binding.isPartOfComposite.ToString());}
                }
            }
            foreach(var scheme in asset.controlSchemes){Token(scheme.name);Token(scheme.bindingGroup);foreach(var requirement in scheme.deviceRequirements){Token(requirement.controlPath);Token(requirement.isOptional.ToString());Token(requirement.isOR.ToString());}}
            return b.ToString();
        }
        string CspInputBindings()
        {
            if(!input||!input.RuntimeAsset)return "UNAVAILABLE";
            string contract=CspInputContract(input.RuntimeAsset);
            string folder=Path.Combine(Run.output,"Input");Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"runtime-actions.json"),input.RuntimeAsset.ToJson(),new System.Text.UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder,"binding-contract.txt"),contract,new System.Text.UTF8Encoding(false));
            return CombatPerformancePaths.Hash(contract);
        }
        void CspCheck(bool value,string name) { cspResult.checks.Add((value?"PASS: ":"FAIL: ")+name); if(!value) throw new InvalidOperationException(name); }
        IEnumerator ExecuteCsp()
        {
            double end=Time.realtimeSinceStartupAsDouble+120;
            bool selected=false;
            while(!Overburst.Persistence.AccountBootstrap.Ready || PersistentSceneFlow.Instance==null || PersistentSceneFlow.Instance.IsSwitching)
            {
                var title=TitleCharacterPanel.Instance;
                if(title && !selected && title.start && title.start.interactable)
                { title.start.onClick.Invoke(); selected=true; }
                if(title && selected && title.enter && title.enter.gameObject.activeInHierarchy && title.enter.interactable) title.enter.onClick.Invoke();
                if(Time.realtimeSinceStartupAsDouble>end) throw new TimeoutException("CSP product boot/title selection");
                yield return null;
            }
            player=PlayerContext.Instance.CurrentActor; input=player.GetComponent<PlayerInputFacade>(); melee=player.GetComponent<MeleeRuntime>();
            cspOutdoor=OutdoorWorldSpawner.Current; cspMovement=player.GetComponent<PlayerMovement>();
            cspResult=new CspResult { observation=Run.profile.cspObservation, start=player.transform.position, status="RUNNING" };
            CspCheck(player && input && cspMovement && melee,"current player/input/motor present");
            CspCheck(!Run.profile.cspTown || cspOutdoor,"real MainScene outdoor present");
            cspOldSettings=InputSystem.settings; cspSettings=UnityEngine.Object.Instantiate(cspOldSettings); cspSettings.hideFlags=HideFlags.DontSave;
            cspSettings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            cspSettings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView; InputSystem.settings=cspSettings;
            cspOldDevices=input.GameplayMap.devices; cspPad=InputSystem.AddDevice<Gamepad>("CSP owned gamepad"); input.GameplayMap.devices=new InputDevice[]{cspPad}; input.EnableGameplay();
            oldVsync=QualitySettings.vSyncCount; oldTarget=Application.targetFrameRate; settingsOwned=true;
            if(Run.profile.uncapped){QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;}
            blood=UnityEngine.Object.FindFirstObjectByType<BloodHitVfxService>();
            List<Vector3> route=null;
            if(Run.profile.cspTown) { CspCheck(cspOutdoor.IsInTown(player.transform.position),"start inside town"); route=FindCspExit(); cspResult.exit=route[route.Count-1]; }
            player.Health.OnDamageResolved+=CspPlayerHit;
            CaptureEnvironment();
            long sentinelBefore=GC.GetAllocatedBytesForCurrentThread(), heapBefore=UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            var sentinel=new byte[1024*1024]; sentinel[0]=1;
            cspResult.threadAllocationAvailable=GC.GetAllocatedBytesForCurrentThread()-sentinelBefore>=sentinel.Length;
            cspResult.monoHeapDeltaAvailable=UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong()-heapBefore>=sentinel.Length;
            GC.KeepAlive(sentinel);
            long setup=Stopwatch.GetTimestamp(), bytes=GC.GetAllocatedBytesForCurrentThread(), setupHeap=UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            cspFrames=new CspFrame[Run.profile.frameCapacity]; cspEvents=new CspEvent[Run.profile.eventCapacity];
            cspHitHandler=CspHit;
            CombatSpawnQualityTrace.ResetObservation();
            Begin("csp-continuous", "CSP phases; fixture combat separated from real town",0,"",CombatPerformanceLayout.Dense,Run.profile.elements[0],0,0);
            cspResult.missingMetrics=recorder.MissingRequestedMetrics;
            foreach(var required in CspRequiredMetrics())
                CspCheck(Array.IndexOf(cspResult.missingMetrics,required)<0,"required metric available: "+required);
            if(Run.profile.cspObservation!="WallOnly") CombatSpawnQualityTrace.Sink=CspTrace;
            cspResult.setupMs=(Stopwatch.GetTimestamp()-setup)*1000d/Stopwatch.Frequency; cspResult.setupBytes=GC.GetAllocatedBytesForCurrentThread()-bytes; cspResult.setupMonoHeapDelta=UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong()-setupHeap;
            cspWasInTown=cspOutdoor && cspOutdoor.IsInTown(player.transform.position);
            CspSetPhase(1); yield return Wait(Run.profile.baselineSeconds);
            if(Run.profile.cspTown)
            {
                CspSetPhase(2); yield return CspWalk(route);
                CspCheck(!cspOutdoor.IsInTown(player.transform.position),"first actual exit reached");
                CspSetPhase(3); yield return Wait(Run.profile.measureSeconds);
                route.Reverse(); CspSetPhase(4); yield return CspWalk(route);
                CspCheck(cspOutdoor.IsInTown(player.transform.position),"actual return inside town");
                CspSetPhase(5); yield return Wait(1);
                route.Reverse(); CspSetPhase(6); yield return CspWalk(route);
                CspCheck(!cspOutdoor.IsInTown(player.transform.position),"second actual exit reached");
                CspSetPhase(7); yield return Wait(Run.profile.measureSeconds);
                CspSetPhase(20);
                var outdoorVictims=new List<EnemyActor>();
                foreach(var a in UnityEngine.Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None))if(a && a.IsLeased && !a.Health.IsDead && a.gameObject.scene.name==PersistentSceneFlow.MainSceneName)outdoorVictims.Add(a);
                CspSetPhase(17);
                foreach(var a in outdoorVictims)a.Health.TakeDamage(new DamageInfo(10000000,a.transform.position,player.gameObject,Vector3.forward));
                yield return null;
                CspSetPhase(18);yield return Wait(Run.profile.tailSeconds);
                route.Reverse(); CspSetPhase(8); yield return CspWalk(route);
                CspCheck(cspOutdoor.IsInTown(player.transform.position),"return before arena");
                CspCheck(cspOutdoor.PendingCount==0 && cspOutdoor.ActiveCount==0,"town reset clears owned population");
                CspCheck(!player.Health.IsDead,"no player death during real exit route");
            }
            if(Run.profile.cspCombat)
            {
                CspSetPhase(9); // Preparation is retained in the continuous raw stream, excluded from combat phase summaries.
                input.GameplayMap.devices=cspOldDevices; InputSystem.RemoveDevice(cspPad); cspPad=null;
                PersistentSceneFlow.Instance.SwitchHubScene(PersistentSceneFlow.HideoutSceneName);
                double hubDeadline=Time.realtimeSinceStartupAsDouble+90;
                while(PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName!=PersistentSceneFlow.HideoutSceneName){if(Time.realtimeSinceStartupAsDouble>hubDeadline)throw new TimeoutException("CSP arena hub transition");yield return null;}
                Prepare(); player.Equipment.SetElementGem(gems[Run.profile.elements[0]]);
                if(!player.Equipment.EquipWeaponItem(new ItemData(weapon,1,ItemGrade.Common))) throw new InvalidOperationException("CSP weapon equip");
                DebugTeleport.To(new Vector3(1000,.2f,985)); yield return Wait(Run.profile.warmupSeconds);
                int count=Run.profile.enemyCounts[0]; var theme=MapThemeCatalog.Resolve(Run.profile.themes[0]);
                CspCheck(spawner.RegisterAdditionalCatalog(theme.Catalog,out string reason),"catalog registration: "+reason);
                var roster=theme.BuildRoster(count-Mathf.RoundToInt(count*.2f)-Math.Max(1,count/50),Mathf.RoundToInt(count*.2f),Math.Max(1,count/50),Run.profile.seed);
                var ids=new string[count]; for(int i=0;i<count;i++)ids[i]=roster[i].EnemyId; Run.rosterIds=ids; segment.rosterSignature=CombatPerformancePaths.Hash(string.Join("|",ids));
                UnityEngine.Random.InitState(Run.profile.seed);
                CspSetPhase(10);
                for(int i=0;i<count;i++)
                {
                    float angle=i*2.399963f,radius=2.8f+i%5*.3f;
                    Vector3 p=player.transform.position+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*radius;
                    if(Physics.Raycast(p+Vector3.up*5,Vector3.down,out var hit,12,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))p.y=hit.point.y+.1f;
                    var actor=spawner.Spawn(new EnemySpawnRequest(roster[i],p,Quaternion.identity,player.transform,ownedRoot,player.transform,ownedRoot.transform,seed:Run.profile.seed+i));
                    if(!actor)throw new InvalidOperationException("production spawn failed"); actor.Health.SetUnmodifiedMaxHp(Run.profile.enemyHp,true); actor.Health.OnDamageResolved+=EnemyHit; enemies.Add(actor);
                    if((i+1)%Run.profile.spawnPerFrame==0)yield return null;
                }
                yield return Wait(Run.profile.approachSeconds);
                CspSetPhase(11); yield return CspAttack(3);
                CspSetPhase(12); yield return CspAttack(Run.profile.measureSeconds);
                CspCheck(segment.accepted>0 && segment.enemyHits>0,"public attacks accepted and actual hits recorded");
                melee.CancelCurrentAttackState();
                CspSetPhase(13); // Fixed damage is labelled separately from natural weapon attacks.
                var victim=enemies.Find(a=>a && !a.Health.IsDead);
                CspCheck(victim,"repeat-damage fixture target");
                for(int i=0;i<100;i++) victim.Health.TakeDamage(new DamageInfo(1,victim.transform.position,player.gameObject,Vector3.forward));
                yield return Wait(1);
                CspSetPhase(14);
                int beforeKills=segment.kills, expected=Alive();
                foreach(var actor in enemies) if(actor && !actor.Health.IsDead) actor.Health.TakeDamage(new DamageInfo(Run.profile.enemyHp*100,actor.transform.position,player.gameObject,Vector3.forward));
                CspCheck(segment.kills-beforeKills==expected,"fatal fixture dispatch count matches living actors");
                yield return Wait(Run.profile.tailSeconds);
                CspSetPhase(15); ReleaseEnemies(); yield return Wait(1);
                CspCheck(spawner.Pool.PendingReturnCount==0,"pool pending returns drained");
            }
            CspSetPhase(16); yield return null;
            cspResult.status="COMPLETE";
        }
        IEnumerator CspAttack(float seconds)
        {
            double until=Time.realtimeSinceStartupAsDouble+seconds; nextAttack=Time.realtimeSinceStartupAsDouble; actionOrdinal=0;
            while(Time.realtimeSinceStartupAsDouble<until)
            { PollCompletion(); if(Time.realtimeSinceStartupAsDouble>=nextAttack){RequestAction(false);nextAttack=Time.realtimeSinceStartupAsDouble+Run.profile.attackInterval;} yield return null; }
            PollCompletion();
        }
        void CspSetPhase(int phase) { cspPhase=phase; Progress="CSP phase "+phase; recorder.Event(100+phase); }
        void CspTick()
        {
            if(cspFrames==null || recorder==null)return;
            if(CombatSpawnQualityTrace.Failure!=null)throw new InvalidOperationException("CSP trace callback failed",CombatSpawnQualityTrace.Failure);
            long observerAt=Stopwatch.GetTimestamp();
            if(Screen.width!=Run.width||Screen.height!=Run.height)throw new InvalidOperationException("CSP render resolution changed during continuous recording");
            if(recorder.CapacityReached || cspFrameCount>=cspFrames.Length)throw new InvalidOperationException("CSP frame buffer capacity");
            if(!TransientVfxPool.DiagnosticLeaseCountsValid)throw new InvalidOperationException("CSP VFX lease counters disagree with active leases");
            cspFrames[cspFrameCount++]=new CspFrame{frame=Time.frameCount,phase=cspPhase,seconds=recorder.ElapsedSeconds,point=player?player.transform.position:default,stick=cspStick,
                active=cspPhase>=10?Alive():(cspOutdoor?cspOutdoor.ActiveCount:0),pending=cspOutdoor?cspOutdoor.PendingCount:0,visited=cspOutdoor?cspOutdoor.VisitedCount:0,hits=cspHits,fatal=cspFatal,
                blocked=GameplayInputBlocker.IsGameplayInputBlocked?1:0,created=spawner&&spawner.Pool?spawner.Pool.CreatedCount:0,leased=spawner&&spawner.Pool?spawner.Pool.LeasedCount:0,returns=spawner&&spawner.Pool?spawner.Pool.PendingReturnCount:0,
                pickups=WorldItemPickup.DiagnosticRegisteredCount,pickupsCreated=CombatSpawnQualityTrace.ItemPickupsCreated,nameplates=WorldItemNameplateVisibilityRegistry.VisibleCount,
                vfxFixed=TransientVfxPool.DiagnosticFixedLeaseCount,vfxNatural=TransientVfxPool.DiagnosticNaturalLeaseCount};
            recorder.AddObserverTicks(observerAt);
        }
        void CspLateTick()
        {
            if(cspFrames==null || cspFrameCount==0)return;
            ref var f=ref cspFrames[cspFrameCount-1]; f.phase=cspPhase;
            if(cspOutdoor && player && cspPhase<9){bool inside=cspOutdoor.IsInTown(player.transform.position);if(inside!=cspWasInTown){CspAdd(new CspEvent{phase=cspPhase,kind=inside?41:40,frame=Time.frameCount,seconds=recorder.ElapsedSeconds,spawn=new CombatSpawnQualityTrace.Entry{point=player.transform.position}});cspWasInTown=inside;}}
            for(int i=cspTracked.Count-1;i>=0;i--) { var a=cspTracked[i]; if(a && !a.IsLeased){a.Health.OnDamageResolved-=cspHitHandler;cspActors.Remove(a.Health);cspTracked.RemoveAt(i);continue;} if(!a || !a.IsLeased || a.Health.IsDead)continue; if(a.AI && a.AI.Target==player.transform && a.AI.IsAggroActive)f.threatened++; if(a.AI && a.AI.CurrentStateName=="Attack")f.attacking++; }
        }
        void CspTrace(CombatSpawnQualityTrace.Entry e)
        {
            CspRequestTrace(e);
            if(e.kind==20 && e.actor && !cspActors.ContainsKey(e.actor.Health)) {cspActors.Add(e.actor.Health,e.actor);cspTracked.Add(e.actor);e.actor.Health.OnDamageResolved+=cspHitHandler;cspBound++;}
            if(e.kind==21 && e.actor && cspActors.Remove(e.actor.Health)) {e.actor.Health.OnDamageResolved-=cspHitHandler;cspTracked.Remove(e.actor);}
            if(e.kind!=11 && e.kind!=12) CspAdd(new CspEvent{phase=cspPhase,kind=e.kind,frame=e.frame,seconds=recorder.ElapsedSeconds,spawn=e,target=e.actor?e.actor.GetInstanceID():0,lease=e.actor?(int)e.actor.LeaseVersion:0});
        }
        void CspHit(CombatHealth health,DamageInfo info,float damage,bool fatal)
        { long observed=Stopwatch.GetTimestamp();cspHits++;if(fatal)cspFatal++;cspActors.TryGetValue(health,out var actor);CspAdd(new CspEvent{phase=cspPhase,kind=30,frame=Time.frameCount,seconds=recorder.ElapsedSeconds,target=actor?actor.GetInstanceID():health.GetInstanceID(),damage=damage,hp=health.CurrentHp,lease=actor?(int)actor.LeaseVersion:0,attack=info.sourceAttackSequenceId,attackPhase=info.sourceAttackPhaseIndex,element=(int)info.element,dot=info.isDamageOverTime?1:0});recorder.AddObserverTicks(observed); }
        void CspPlayerHit(CombatHealth health,DamageInfo info,float damage,bool fatal)
        {if(recorder!=null)CspAdd(new CspEvent{phase=cspPhase,kind=31,frame=Time.frameCount,seconds=recorder.ElapsedSeconds,target=health.GetInstanceID(),damage=damage,hp=health.CurrentHp,attack=info.sourceAttackSequenceId,attackPhase=info.sourceAttackPhaseIndex,element=(int)info.element});}
        void CspAdd(CspEvent e) {if(cspEventCount==cspEvents.Length){cspDropped++;return;}e.sequence=cspEventCount;cspEvents[cspEventCount++]=e;}
        void CspQueue(Vector2 stick){long observed=Stopwatch.GetTimestamp();cspStick=stick;InputSystem.QueueStateEvent(cspPad,new GamepadState{leftStick=stick});recorder?.AddObserverTicks(observed);}
        List<Vector3> FindCspExit()
        {
            var box=(BoxCollider)typeof(OutdoorWorldSpawner).GetField("town",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cspOutdoor);
            var bounds=box.bounds; var start=player.transform.position;
            var candidates=new[]{new Vector3(bounds.min.x-8,start.y,start.z),new Vector3(bounds.max.x+8,start.y,start.z),new Vector3(start.x,start.y,bounds.min.z-8),new Vector3(start.x,start.y,bounds.max.z+8)};
            Array.Sort(candidates,(a,b)=>(a-start).sqrMagnitude.CompareTo((b-start).sqrMagnitude));
            int mask=~LayerMask.GetMask("Player","Enemy","Ignore Raycast");
            foreach(var desired in candidates)
            {
                if(!CspFloor(desired,start.y,mask,out var target))continue;
                if(NavMesh.SamplePosition(start,out var ns,2,NavMesh.AllAreas) && NavMesh.SamplePosition(target,out var nt,2,NavMesh.AllAreas))
                {var path=new NavMeshPath();if(NavMesh.CalculatePath(ns.position,nt.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete){var r=new List<Vector3>(path.corners);r[0]=start;return r;}}
                var direct=GridCspRoute(start,target,bounds,mask); if(direct!=null)return direct;
            }
            throw new InvalidOperationException("No physical town exit route");
        }
        List<Vector3> GridCspRoute(Vector3 start,Vector3 target,Bounds townBounds,int mask)
        {
            const float step=.75f; const int size=161; int half=size/2;
            var seen=new bool[size*size];var previous=new int[size*size];var points=new Vector3[size*size];var queue=new int[size*size];int head=0,tail=0;
            int center=half*size+half;seen[center]=true;points[center]=start;previous[center]=-1;queue[tail++]=center;
            while(head<tail)
            {
                int at=queue[head++],x=at%size,z=at/size;
                if(!cspOutdoor.IsInTown(points[at]) && (points[at]-target).sqrMagnitude<100)
                {var route=new List<Vector3>();for(int p=at;p>=0;p=previous[p])route.Add(points[p]);route.Reverse();return route;}
                for(int d=0;d<4;d++)
                {
                    int nx=x+(d==0?1:d==1?-1:0),nz=z+(d==2?1:d==3?-1:0);if(nx<0||nx>=size||nz<0||nz>=size)continue;int ni=nz*size+nx;if(seen[ni])continue;seen[ni]=true;
                    Vector3 p=start+new Vector3((nx-half)*step,0,(nz-half)*step);
                    if(!CspFloor(p,points[at].y,mask,out p))continue;
                    var cc=player.GetComponent<CharacterController>();float radius=cc?Mathf.Max(.1f,cc.radius-cc.skinWidth):.3f;float height=cc?cc.height:1.8f;float centerY=cc?cc.center.y:height*.5f;
                    Vector3 bottom=p+Vector3.up*(centerY-height*.5f+radius+.06f),top=p+Vector3.up*(centerY+height*.5f-radius);
                    int overlaps=Physics.OverlapCapsuleNonAlloc(bottom,top,radius,cspRouteOverlaps,mask,QueryTriggerInteraction.Ignore);bool blocked=overlaps==cspRouteOverlaps.Length;
                    for(int q=0;q<overlaps;q++)if(!cspRouteOverlaps[q].transform.IsChildOf(player.transform))blocked=true;if(blocked)continue;
                    Vector3 delta=p-points[at];int hits=Physics.CapsuleCastNonAlloc(bottom-delta+Vector3.up*(cc?cc.stepOffset:.3f),top-delta+Vector3.up*(cc?cc.stepOffset:.3f),radius,delta.normalized,cspRouteHits,delta.magnitude,mask,QueryTriggerInteraction.Ignore);blocked=hits==cspRouteHits.Length;
                    for(int q=0;q<hits;q++)if(!cspRouteHits[q].collider.transform.IsChildOf(player.transform))blocked=true;if(blocked)continue;
                    points[ni]=p;previous[ni]=at;queue[tail++]=ni;
                }
            }
            return null;
        }
        readonly RaycastHit[] cspRouteHits=new RaycastHit[32];
        readonly Collider[] cspRouteOverlaps=new Collider[32];
        bool CspFloor(Vector3 desired,float height,int mask,out Vector3 point)
        {
            point=default;int count=Physics.RaycastNonAlloc(new Vector3(desired.x,height+3,desired.z),Vector3.down,cspRouteHits,6,mask,QueryTriggerInteraction.Ignore);float best=float.MaxValue;bool found=false;
            for(int i=0;i<count;i++){var h=cspRouteHits[i];if(h.collider.transform.IsChildOf(player.transform)||h.normal.y<.72f)continue;float gap=Math.Abs(h.point.y-height);if(gap<.6f&&gap<best){best=gap;point=h.point+Vector3.up*.02f;found=true;}}return found&&count<cspRouteHits.Length;
        }
        IEnumerator CspWalk(List<Vector3> points)
        {
            double until=Time.realtimeSinceStartupAsDouble+90,progressAt=Time.realtimeSinceStartupAsDouble;float best=float.MaxValue;
            for(int i=0;i<points.Count;i++)
            {
                best=float.MaxValue;progressAt=Time.realtimeSinceStartupAsDouble;
                while(new Vector2(player.transform.position.x-points[i].x,player.transform.position.z-points[i].z).sqrMagnitude>.15f*.15f)
                {
                    if(player.Health.IsDead)throw new InvalidOperationException("player died on actual town route");
                    var delta=points[i]-player.transform.position;delta.y=0;float dist=delta.magnitude;
                    if(dist<best-.08f){best=dist;progressAt=Time.realtimeSinceStartupAsDouble;}
                    if(Time.realtimeSinceStartupAsDouble>until||Time.realtimeSinceStartupAsDouble-progressAt>8)throw new TimeoutException("actual motor route stalled at "+player.transform.position+" target "+points[i]);
                    delta.Normalize();CspQueue(Vector2.ClampMagnitude(new Vector2(Vector3.Dot(delta,cspMovement.ResolveMoveDirection(Vector2.right)),Vector3.Dot(delta,cspMovement.ResolveMoveDirection(Vector2.up))),1));yield return null;
                }
            }
            CspQueue(Vector2.zero);yield return Wait(.15f);CspCheck(cspMovement.IsGrounded,"actual route arrival grounded");
        }
        void CspExportAndCleanup()
        {
            if(CombatSpawnQualityTrace.Sink==CspTrace)CombatSpawnQualityTrace.Sink=null;
            if(player)player.Health.OnDamageResolved-=CspPlayerHit;
            foreach(var pair in cspActors)if(pair.Key)pair.Key.OnDamageResolved-=cspHitHandler;
            cspActors.Clear();cspTracked.Clear();
            if(cspPad!=null){if(input)input.GameplayMap.devices=cspOldDevices;InputSystem.RemoveDevice(cspPad);cspPad=null;}
            if(cspOldSettings){InputSystem.settings=cspOldSettings;if(cspSettings)Destroy(cspSettings);}
            if(cspResult==null)return;
            cspResult.frames=cspFrameCount;cspResult.events=cspEventCount;cspResult.hits=cspHits;cspResult.fatal=cspFatal;cspResult.dropped=cspDropped;cspResult.bound=cspBound;
            cspResult.traceMs=CombatSpawnQualityTrace.ObserverTicks*1000d/Stopwatch.Frequency;cspResult.traceBytes=CombatSpawnQualityTrace.ObserverAllocated;
            cspResult.status=Run.status;cspResult.reason=Run.reason;
            if(cspDropped>0){Run.status="FAILED";Run.reason+=" CSP event buffer overflow";}
            string folder=Path.Combine(Run.output,"CSP");Directory.CreateDirectory(folder);
            using(var w=new StreamWriter(Path.Combine(folder,"positions.csv"),false,new System.Text.UTF8Encoding(false)))
            {w.WriteLine("frame,seconds,phase,x,y,z,input_x,input_y,active,pending,visited,hits,fatal,threatened,attacking,input_blocked,pool_created,pool_leased,pool_returns,item_pickups_registered,item_pickups_created_total,nameplates_visible,vfx_fixed_leases,vfx_natural_leases");for(int i=0;i<cspFrameCount;i++){var f=cspFrames[i];w.WriteLine(string.Join(",",new[]{N(f.frame),N(f.seconds),N(f.phase),N(f.point.x),N(f.point.y),N(f.point.z),N(f.stick.x),N(f.stick.y),N(f.active),N(f.pending),N(f.visited),N(f.hits),N(f.fatal),N(f.threatened),N(f.attacking),N(f.blocked),N(f.created),N(f.leased),N(f.returns),N(f.pickups),N(f.pickupsCreated),N(f.nameplates),N(f.vfxFixed),N(f.vfxNatural)}));}}
            using(var w=new StreamWriter(Path.Combine(folder,"events.csv"),false,new System.Text.UTF8Encoding(false)))
            {w.WriteLine("sequence,frame,seconds,phase,kind,target,lease,damage,hp,attack,attack_phase,element,dot,seed,cell_x,cell_z,group,level,definition,x,y,z,candidates,town,distance,ground,visible,capsule,crowded");for(int i=0;i<cspEventCount;i++){var e=cspEvents[i];var s=e.spawn;w.WriteLine(string.Join(",",new[]{N(e.sequence),N(e.frame),N(e.seconds),N(e.phase),N(e.kind),N(e.target),N(e.lease),N(e.damage),N(e.hp),N(e.attack),N(e.attackPhase),N(e.element),N(e.dot),N(s.seed),N(s.cell.x),N(s.cell.y),N(s.group),N(s.level),"\""+(s.definition??"").Replace("\"","\"\"")+"\"",N(s.point.x),N(s.point.y),N(s.point.z),N(s.candidates),N(s.town),N(s.distance),N(s.ground),N(s.visible),N(s.capsule),N(s.crowded)}));}}
            CspWriteRequests(folder);
            CombatPerformancePaths.SaveJson(Path.Combine(folder,"quality.json"),cspResult);
            CombatPerformancePaths.SaveJson(Path.Combine(folder,"record-contract.json"),new CspRecordContract { schema=3,program="CSP",goal="G00",observation=Run.profile.cspObservation,requestedMetrics=CspMetrics(),missingMetrics=cspResult.missingMetrics,metricSampleCounts=Run.profile.cspObservation=="Detailed",
                markerScope="Inclusive CPU durations; nested markers and worker time must not be summed or subtracted from wall. Trace observer marker is separate from observer_ms and can overlap fixture observer scopes.",
                workloadScope="positions.csv counts are early-Update snapshots; phase/threat fields are updated in early LateUpdate. Pickup registry and successful factory creation total exclude currency; nameplates are the last published visible set; VFX counts are scheduled pool leases, not particles or all effects. Marker sample counts are calls, not successful drops.",
                traceScope="Observer.Trace is frame-local ProfilerRecorder time, including trace accounting. quality.traceMs is callback Stopwatch total for this run. WallOnly has no trace sink. Missing samples remain unavailable; neither duration is net game time.",
                frameCapacity=Run.profile.frameCapacity,eventCapacity=Run.profile.eventCapacity,requestCapacity=1024,clock="Stopwatch seconds; Time.frameCount; completed wall intervals only",threadAllocationAvailable=cspResult.threadAllocationAvailable,monoHeapDeltaAvailable=cspResult.monoHeapDeltaAvailable,qualityBoundary="Automated contracts only; visual, audio and direct feel NOT_RUN",controlBoundary="WallOnly retains shared wall/GC/positions/core arena health observers. Incremental trace overhead comparison; not zero-automation gameplay.",traceBinding=Run.profile.cspObservation!="WallOnly",exportBoundary="Raw export occurs after measurement closes; full analysis runs after actual EditMode return",candidateAccounting="Rejected and successful attempts included",phase14="Injected arena fatalities and 5s tail",phase17="Injected real outdoor fatalities, with production rewards",phase13="100 direct damage fixture; not natural combat"});
        }
        static string N(double value)=>value.ToString("0.######",CultureInfo.InvariantCulture);
    }
}
#endif
