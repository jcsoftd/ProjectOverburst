using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Runs the production actors and AI without forcing their state. Audit findings are
// saved even when a baseline is broken, rather than stopping at the first monster.
public static class MonsterThemeCombatRecoveryVerifier
{
    public static IEnumerator Verify(EnemyThemeDebugUI ui, PlayerInputFacade player)
    {
        string output = SessionState.GetString("MonsterThemePlayVerifier.output", "");
        if (!Directory.Exists(output)) throw new InvalidOperationException("Select an existing artifact directory");
        string phase = SessionState.GetString("MonsterThemeCombatRecovery.phase", "baseline");
        string mode = SessionState.GetString("MonsterThemeCombatRecovery.mode", "individual");
        if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out var service)) throw new Exception("Spawn service");
        foreach (var table in ui.tables) service.RegisterAdditionalCatalog(table.Catalog, out _);
        var results = new List<object>();
        var failures = new List<string>();
        var definitions = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).Distinct().ToArray();
        Vector3 home = player.transform.position;
        if (mode == "individual")
        {
            foreach (var definition in definitions)
            {
                var request = new EnemySpawnRequest(definition, home + Vector3.forward * 6, Quaternion.Euler(0,180,0), player.transform, null, player.transform, null, 1, 1, 91);
                if (!service.TrySpawn(request, out var actor)) throw new Exception("Spawn " + definition.EnemyId);
                actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                actor.AI.RequestAggro(player.transform);
                var trace = new List<object>(); var states = new Dictionary<string,int>();
                float begin = Time.time, next = 0, distanceMoved = 0;
                Vector3 previous = actor.transform.position;
                var abilities = new HashSet<string>();
                while (Time.time - begin < 18f)
                {
                    distanceMoved += Vector3.Distance(previous, actor.transform.position); previous = actor.transform.position;
                    if (actor.AbilityController.LastCommittedAbility != null) abilities.Add(actor.AbilityController.LastCommittedAbility.name);
                    if (Time.time >= next)
                    {
                        next = Time.time + .2f;
                        string state = actor.AI.CurrentStateName;
                        states[state] = states.TryGetValue(state, out var count) ? count+1 : 1;
                        var delta = player.transform.position - actor.transform.position; delta.y = 0;
                        trace.Add(new { t=Time.time-begin,state,distance=delta.magnitude,yaw=Vector3.Angle(actor.transform.forward,delta),locked=actor.Movement.IsActionLocked,blocking=actor.AnimationBridge.IsBlockingActionActive,ability=actor.AbilityController.LastCommittedAbility?.name });
                    }
                    yield return null;
                }
                actor.AI.enabled = false; actor.Movement.StopMovement();
                float startY = actor.transform.position.y, minimumY = startY;
                actor.Health.TakeDamage(new DamageInfo(actor.Health.MaxHp+10000, actor.transform.position, player.gameObject, Vector3.forward));
                float deathStart = Time.time;
                var death = new List<object>();
                while (actor.IsLeased && Time.time - deathStart < definition.AnimationProfile.Death.length + 2f)
                {
                    minimumY = Mathf.Min(minimumY,actor.transform.position.y);
                    if (Time.time >= next)
                    {
                        next = Time.time + .1f;
                        var body=actor.GetComponent<Rigidbody>();
                        death.Add(new {t=Time.time-deathStart,y=actor.transform.position.y,kinematic=body.isKinematic,gravity=body.useGravity});
                    }
                    yield return null;
                }
                results.Add(new {id=definition.EnemyId,distanceMoved,states,abilities,trace,death,startY,minimumY,rootDrop=startY-minimumY,released=!actor.IsLeased});
                if (phase != "baseline")
                {
                    if (startY-minimumY > .025f) failures.Add(definition.EnemyId+": death root sank");
                    if (abilities.Count == 0) failures.Add(definition.EnemyId+": no actual attack in 18 seconds");
                    if (actor.IsLeased) failures.Add(definition.EnemyId+": death did not return to pool");
                }
                if (actor.IsLeased) actor.RequestPoolRelease();
                File.WriteAllText(Path.Combine(output,phase+"-individual.json"),Newtonsoft.Json.JsonConvert.SerializeObject(results,Newtonsoft.Json.Formatting.Indented));
                Debug.Log("[MonsterCombatRecovery] " + phase + " " + definition.EnemyId + " abilities=" + abilities.Count + " rootDrop=" + (startY-minimumY));
                yield return null;
            }
            File.WriteAllText(Path.Combine(output,phase+"-individual-status.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{count=results.Count,failures,status=phase=="baseline"?"BASELINE_OBSERVED":failures.Count==0?"PASS":"FAIL"},Newtonsoft.Json.Formatting.Indented));
            if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ",failures));
        }
    }
}
