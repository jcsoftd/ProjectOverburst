using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Profiling;
using Object=UnityEngine.Object;

public static partial class MainTownDestructionVerifier
{
    static IEnumerator PerformanceTrial()
    {
        var controller=Object.FindFirstObjectByType<MainTownDestruction>();var actor=PlayerContext.Instance.CurrentActor;
        Check(controller!=null&&controller.EntryCount==5850,"Second isolated boot restores all 5850 candidates");
        var weapon=AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
        Check(actor.Equipment.EquipWeaponItem(new ItemData(weapon,1,ItemGrade.Common)),"Greatsword equips for repeated attack measurement");
        typeof(PlayerEquipment).GetMethod("SetElementGem",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(actor.Equipment,new object[]{null});
        PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);var melee=actor.PlayerKit.MeleeRuntime;melee.SetManualInputEnabled(true);
        int index=Enumerable.Range(0,controller.EntryCount).Where(i=>controller.catalog.definitions[controller.DefinitionFor(i)].source.name=="DF_Barrel_Wood_01").OrderBy(i=>Vector3.Distance(actor.transform.position,controller.PositionFor(i))).First();
        var position=controller.PositionFor(index);var direction=Approach(controller,index,actor.transform,5);var prefab=controller.catalog.definitions[controller.DefinitionFor(index)].debris;
        ActorTeleportUtility.TeleportSafely(actor.transform,position-direction*1.5f,Quaternion.LookRotation(direction));float until=Time.time+2;while(Time.time<until)yield return null;
        TransientVfxPool.PrepareOne(prefab,2);TransientVfxPool.PrepareOne(prefab,2);var pooledBefore=TransientVfxPool.GetStatistics(prefab);float actorHp=actor.Health.CurrentHp;
        var barrelPrefab=prefab;var rows=new List<object>();var main=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread",1);var physics=ProfilerRecorder.StartNew(ProfilerCategory.Physics,"Physics.Simulate",1);var gc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1);var nearby=ProfilerRecorder.StartNew(ProfilerCategory.Scripts,"Overburst.EnvironmentDestruction.Nearby",1);var breaking=ProfilerRecorder.StartNew(ProfilerCategory.Scripts,"Overburst.EnvironmentDestruction.Break",1);var terrainCollision=ProfilerRecorder.StartNew(ProfilerCategory.Scripts,"Overburst.EnvironmentDestruction.TerrainColliderRefresh",1);
        try
        {
            for(int phase=0;phase<4;phase++)
            {
                if(phase==2){controller.RestoreAll();index=Enumerable.Range(0,controller.EntryCount).Where(i=>controller.IsTerrainTree(i)&&controller.catalog.definitions[controller.DefinitionFor(i)].source.name=="DF_Pine_01").OrderBy(i=>Vector3.Distance(actor.transform.position,controller.PositionFor(i))).First();position=controller.PositionFor(index);direction=Approach(controller,index,actor.transform,5);prefab=controller.catalog.definitions[controller.DefinitionFor(index)].debris;ActorTeleportUtility.TeleportSafely(actor.transform,position-direction*1.5f,Quaternion.LookRotation(direction));TransientVfxPool.PrepareOne(prefab,2);TransientVfxPool.PrepareOne(prefab,2);}
                PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);melee.SetManualInputEnabled(true);
                float settle=Time.time+.7f;while(Time.time<settle)yield return null;
                var samples=new List<double>();var physicsSamples=new List<double>();var allocations=new List<long>();var nearbySamples=new List<double>();var breakSamples=new List<double>();var collisionSamples=new List<double>();var frames=new List<double>();double last=Time.realtimeSinceStartupAsDouble;int attacks=0;float next=Time.time;var counts=new List<int>();until=Time.time+16;
                while(Time.time<until)
                {
                    if(phase%2==1&&Time.time>=next)
                    {
                        controller.RestoreAll();ActorTeleportUtility.TeleportSafely(actor.transform,position-direction*1.5f,Quaternion.LookRotation(direction));controller.RefreshNearby(actor.transform.position);
                        settle=Time.time+.25f;while(Time.time<settle)yield return null;
                        last=Time.realtimeSinceStartupAsDouble;
                        var result=melee.TryStartAction(new WeaponActionRequest(WeaponActionSource.PlayerInput,null,direction),out _);Check(result==WeaponActionResult.Accepted,"Successive real swing "+(++attacks)+" starts: "+result);next=Time.time+2;
                    }
                    yield return null;double now=Time.realtimeSinceStartupAsDouble;frames.Add((now-last)*1000);last=now;
                    if(main.Valid)samples.Add(main.LastValue/1000000d);if(physics.Valid)physicsSamples.Add(physics.LastValue/1000000d);if(gc.Valid)allocations.Add(gc.LastValue);if(nearby.Valid)nearbySamples.Add(nearby.LastValue/1000000d);if(breaking.Valid)breakSamples.Add(breaking.LastValue/1000000d);if(terrainCollision.Valid)collisionSamples.Add(terrainCollision.LastValue/1000000d);if(phase%2==1)counts.Add(controller.BrokenCount);
                }
                rows.Add(new{kind=phase<2?"placed props":"Terrain pine",phase=phase%2==0?"idle":"successive actual swings",attacks,frames=Stats(frames),mainThreadMs=Stats(samples),physicsMs=Stats(physicsSamples),gcBytes=Stats(allocations.Select(x=>(double)x).ToList()),nearbyMs=Stats(nearbySamples),breakMs=Stats(breakSamples),terrainCollisionMs=Stats(collisionSamples),maxBrokenPerSwing=counts.Count==0?0:counts.Max()});
                if(phase%2==1)Check(counts.Max()>0&&counts.Max()<20,"Repeated attacks break only the ordinary swing's local candidates");
            }
        }
        finally { main.Dispose();physics.Dispose();gc.Dispose();nearby.Dispose();breaking.Dispose();terrainCollision.Dispose(); }
        until=Time.time+4;while(Time.time<until)yield return null;var pooledAfter=TransientVfxPool.GetStatistics(barrelPrefab);
        Check(pooledAfter.Created==pooledBefore.Created&&pooledAfter.Misses==pooledBefore.Misses,"Eight successive swings reuse warmed barrel debris without new instances");Check(actor.Health.CurrentHp==actorHp,"Successive neutral attacks preserve player HP");
        Write("performance.json",new{scope="Shared Editor only; capture excluded; main-thread/physics/GC recorders and realtime frame intervals, no Player FPS claim",rows,pooledBefore,pooledAfter,controller.ActiveTargetCount,controller.CreatedTargetCount});controller.RestoreAll();
        int tree=Enumerable.Range(0,controller.EntryCount).First(i=>controller.IsTerrainTree(i)&&controller.catalog.definitions[controller.DefinitionFor(i)].source.name=="DF_Pine_01");var treePosition=controller.PositionFor(tree);
        int Hits()=>new[]{Vector3.forward,Vector3.back,Vector3.right,Vector3.left}.Sum(d=>Physics.RaycastAll(treePosition+Vector3.up*1.2f+d*2,-d,4,~(1<<2|1<<8|1<<7),QueryTriggerInteraction.Ignore).Count(h=>Vector3.Distance(new Vector3(h.point.x,0,h.point.z),new Vector3(treePosition.x,0,treePosition.z))<.6f));
        int before=Hits();controller.TryBreak(tree,new DamageInfo(10,treePosition,actor.gameObject,Vector3.forward,0));yield return null;Physics.SyncTransforms();int after=Hits();
        Write("terrain-collision.json",new{before,after,treePosition=treePosition.ToString("R")});Check(before==0||after<before,"Terrain tree collision decreases on destruction, or source has no blocking collider");controller.RestoreAll();
        yield return null;Check(Hits()==before,"Restoring Terrain tree restores original collision");
        until=Time.time+3.4f;while(Time.time<until)yield return null;
        var ownedScene=SceneManager.CreateScene("__OWN_MAIN_TOWN_DEBRIS_UNLOAD__");
        try
        {
            TransientVfxPool.Spawn(barrelPrefab,position,Quaternion.identity,3,4,contentSceneHandle:ownedScene.handle);Check(TransientVfxPool.GetStatistics(barrelPrefab).Active==1,"Owned content scene has a live debris lease");
            var unload=SceneManager.UnloadSceneAsync(ownedScene);while(!unload.isDone)yield return null;Check(TransientVfxPool.GetStatistics(barrelPrefab).Active==0,"Scene unload immediately returns active debris");
        }
        finally { if(ownedScene.IsValid()&&ownedScene.isLoaded)SceneManager.UnloadSceneAsync(ownedScene); }
        Check(errors.Count==0,"Second Play has no new runtime errors");
    }
    static object Stats(List<double> values)
    {
        if(values.Count==0)return new{samples=0,mean=0d,median=0d,p95=0d,max=0d};values.Sort();return new{samples=values.Count,mean=values.Average(),median=values[values.Count/2],p95=values[Mathf.Min(values.Count-1,Mathf.FloorToInt(values.Count*.95f))],max=values.Last()};
    }
}
