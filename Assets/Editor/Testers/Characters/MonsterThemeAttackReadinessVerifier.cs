using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Reproduces an attack reservation being charged despite an unfinished facing turn.
public static class MonsterThemeAttackReadinessVerifier
{
    public static IEnumerator Verify(EnemyThemeDebugUI ui, PlayerInputFacade player)
    {
        string output=SessionState.GetString("MonsterThemePlayVerifier.output", "");
        string phase=SessionState.GetString("MonsterThemeCombatRecovery.phase", "baseline");
        var results=new List<object>(); var failures=new List<string>();
        string[] ids={"PrimalHunt_Venosaur_Tint_Brown","VenomBrood_Kupolobrach_Tint_Orange","SpiderBrood_Rostrokarck"};
        foreach(string id in ids)
        {
            var definition=ui.tables.SelectMany(t=>t.Entries).First(e=>e.definition.EnemyId==id).definition;
            var request=new EnemySpawnRequest(definition,player.transform.position+Vector3.forward*6,Quaternion.identity,
                player.transform,null,player.transform,null,1,1,71);
            if(!EnemySpawnService.Current.TrySpawn(request,out var actor))throw new Exception("Spawn readiness probe");
            var machine=(EnemyStateMachine)typeof(EnemyAIController).GetField("stateMachine",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(actor.AI);
            var entries=new List<object>();int failed=0,started=0;float begin=Time.time;
            void Changed(IEnemyState previous,IEnemyState next)
            {
                if(next.Name!="Attack")return;
                bool running=actor.AbilityController.IsExecuting;
                if(running)started++;else failed++;
                entries.Add(new{time=Time.time-begin,running,facing=actor.Movement.IsFacingForAttack(player.transform.position),
                    blocking=actor.AnimationBridge.IsBlockingActionActive,distance=actor.AI.TargetDistance});
            }
            machine.StateChanged+=Changed;
            try
            {
                actor.AI.RequestAggro(player.transform);
                while(Time.time-begin<9)yield return null;
                results.Add(new{id,failed,started,entries});
                if(phase!="baseline" && (failed!=0 || started==0))failures.Add(id+": rejected="+failed+" started="+started);
            }
            finally {machine.StateChanged-=Changed;actor.RequestPoolRelease();}
        }
        File.WriteAllText(Path.Combine(output,"readiness-"+phase+".json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{results,failures},Newtonsoft.Json.Formatting.Indented));
        if(failures.Count>0)throw new Exception(string.Join("; ",failures));
    }
}
