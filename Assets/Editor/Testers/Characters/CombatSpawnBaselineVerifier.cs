using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static partial class CombatBalanceGoal3Verifier
{
    static IEnumerator VerifySpawnBaseline()
    {
        while(PersistentSceneFlow.Instance==null||PersistentSceneFlow.Instance.IsSwitching||PersistentSceneFlow.Instance.CurrentSubSceneName!="HideoutScene")yield return null;
        Check(Overburst.Persistence.AccountBootstrap.SaveDirectory.StartsWith(Output,StringComparison.OrdinalIgnoreCase),"isolated account");
        deadline=EditorApplication.timeSinceStartup+600;
        var player=PlayerInputFacade.Current;
        PlayerContext.Instance.CurrentActor.Health.SetMaxHp(1000000,true);
        var ui=UnityEngine.Object.FindFirstObjectByType<EnemyThemeDebugUI>(FindObjectsInactive.Include);
        ui.gameObject.SetActive(true);if(!ui.InArena)ui.ToggleArena();
        var origin=player.transform.position+Vector3.forward*20;
        try
        {
            foreach(var theme in ui.tables.Where(t=>t.ThemeId!="DeathHarvest"))
            {
                var root=new GameObject("SpawnCostFixture");var inactive=new GameObject("Inactive");inactive.transform.SetParent(root.transform);inactive.SetActive(false);
                var pool=root.AddComponent<EnemyPoolService>();pool.Configure(inactive.transform,0);
                var service=root.AddComponent<EnemySpawnService>();service.Configure(theme.Catalog,pool);
                var roster=theme.BuildRoster(26,4,0,12345);
                var actors=new List<EnemyActor>();
                try
                {
                    for(int pass=0;pass<2;pass++)
                    {
                        long bytes=GC.GetAllocatedBytesForCurrentThread();var watch=System.Diagnostics.Stopwatch.StartNew();
                        var perActor=new List<double>();
                        for(int i=0;i<roster.Count;i++)
                        {
                            double at=watch.Elapsed.TotalMilliseconds;
                            var p=origin+new Vector3(i%6*2,0,i/6*2);
                            Check(service.TrySpawn(new EnemySpawnRequest(roster[i],p,Quaternion.identity,player.transform),out var actor),"baseline spawn");
                            perActor.Add(watch.Elapsed.TotalMilliseconds-at);actor.AI.enabled=false;actors.Add(actor);
                        }
                        double elapsed=watch.Elapsed.TotalMilliseconds;long allocated=GC.GetAllocatedBytesForCurrentThread()-bytes;
                        yield return null;
                        results.Add(new{theme=theme.ThemeId,pass=pass==0?"cold30":"pooled30",elapsedMs=elapsed,allocatedBytes=allocated,maxActorMs=perActor.Max(),nextFrameMs=Time.unscaledDeltaTime*1000,pool.LeasedCount});
                        foreach(var actor in actors)service.Release(actor);actors.Clear();
                        for(int i=0;i<3;i++)yield return null;
                    }
                }
                finally {foreach(var actor in actors)if(actor!=null&&actor.IsLeased)service.Release(actor);UnityEngine.Object.Destroy(root);}
                yield return null;
            }
        }
        finally {if(ui!=null&&ui.InArena)ui.ToggleArena();}
    }
}
