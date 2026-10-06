using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

public static partial class SevenThemeFollowupFlowVerifier
{
    static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly FieldInfo ObservationInterval = typeof(EnemyAIController).GetField("chaseObservationInterval", PrivateInstance);
    static readonly FieldInfo ObservationHeading = typeof(EnemyAIController).GetField("observedChaseHeading", PrivateInstance);
    static readonly FieldInfo ObservationDeadline = typeof(EnemyAIController).GetField("nextChaseObservationTime", PrivateInstance);
    static readonly HashSet<EnemyAIController> ActivePlanningActors=(HashSet<EnemyAIController>)typeof(EnemyAIController).GetField("ActiveEnemies",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
    static Vector3 GetTacticalObservedTarget(EnemyAIController ai) => ((EnemyTacticalPositioning)typeof(EnemyAIController).GetField("tacticalPositioning",PrivateInstance).GetValue(ai))?.ObservedAimPosition ?? Vector3.zero;
    static void UseObservationInterval(EnemyAIController ai, float seconds) => ObservationInterval.SetValue(ai, seconds);

    static IEnumerator ChaseComparison(EnemyCatalog catalog, CombatHealth health, string[] selectedIds=null, float[] selectedDelays=null)
    {
        string[] ids = selectedIds ?? new[]{ "SpiderBrood_Formickarce", "V3_Skorpmare", "V3_Kapeloproboskid", "DeathHarvest_Reaper" };
        foreach (string id in ids) foreach (float delay in (selectedDelays ?? new[] { 0f, .45f, .65f, .85f }))
        {
            if (!catalog.TryGet(id, out var d)) throw new InvalidOperationException(id);
            currentId = id + "_" + Mathf.RoundToInt(delay * 100); trace = new JArray(); damages = new JArray(); begin = Time.time;
            phase = "chase_zigzag"; health.SetMaxHp(1e9f, true); target.position = new Vector3(-5f, 0, 8f); Physics.SyncTransforms();
            if (!service.TrySpawn(new EnemySpawnRequest(d, Vector3.up * .035f, Quaternion.identity, target, context: EncounterContext.Test), out actor)) throw new InvalidOperationException(id);
            UseObservationInterval(actor.AI, delay); actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; actor.AI.RequestAggro(target);
            camera = new Capture(Path.Combine(Folder, currentId), target, actor, true); camera.Mark(phase);
            float start = Time.time;
            while (Time.time - start < 5f)
            {
                target.position = new Vector3(((int)((Time.time - start) / .8f) % 2 == 0 ? -1 : 1) * 5f, 0, 8f);
                Physics.SyncTransforms(); yield return null;
            }
            phase = "chase_orbit"; camera.Mark(phase); start = Time.time;
            Vector3 center = actor.transform.position;
            while (Time.time - start < 5f)
            {
                float angle = (Time.time - start) * 90f * Mathf.Deg2Rad;
                target.position = center + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * 7f;
                target.position = new Vector3(target.position.x, 0, target.position.z); Physics.SyncTransforms(); yield return null;
            }
            phase = "chase_target_gone"; camera.Mark(phase); target.gameObject.SetActive(false); float absent = Time.time;
            while (Time.time - absent < .3f) yield return null;
            bool targetLossImmediate = actor.AI.CurrentStateName == "Return" || actor.AI.CurrentStateName == "Roam";
            target.gameObject.SetActive(true);
            int eval = actor.AI.PlanningEvaluationCount, reuse = actor.AI.PlanningReuseCount;
            results.Add(new JObject { ["id"] = id, ["name"] = d.DisplayName, ["observationSeconds"] = delay, ["planningEvaluations"] = eval, ["planningReuse"] = reuse,
                ["targetLossWithinSeconds"] = .3f, ["targetLossImmediate"] = targetLossImmediate, ["actualSavedAI"] = true, ["usesAuthoredTurn"] = d.MovementProfile.HasTurnAnimation,
                ["usesSquadPursuit"] = actor.AI.UsesSquadPursuit, ["usesRangedTactics"] = actor.AI.UsesRangedTactics });
            SaveCase(currentId); yield return null;
        }
        yield return ChasePlanningCost(catalog);
    }

    static IEnumerator ChasePlanningCost(EnemyCatalog catalog)
    {
        // CPU/GC of the changed planning path only. Rendering, player input and total frame-rate are outside this benchmark.
        if (!catalog.TryGet("SpiderBrood_Formickarce", out var definition)) throw new InvalidOperationException("Benchmark definition missing");
        var method = typeof(EnemyAIController).GetMethod("ResolveChasePlan", PrivateInstance);
        foreach (int count in new[] { 50, 150 }) foreach (float delay in new[] { 0f, .65f })
        {
            currentId = "planning_" + count + "_" + Mathf.RoundToInt(delay * 100); trace = new JArray(); damages = new JArray(); begin = Time.time;
            var actors = new List<EnemyActor>(count); var calls = new List<Func<Vector3>>(count);
            try
            {
                target.position = Vector3.forward * 20;
                for (int i = 0; i < count; i++)
                {
                    Vector3 origin = new Vector3((i % 15 - 7) * 1.6f, .035f, -3f - (i / 15) * 1.6f);
                    if (!service.TrySpawn(new EnemySpawnRequest(definition, origin, Quaternion.identity, target, context: EncounterContext.Test), out var a)) throw new InvalidOperationException("Benchmark spawn");
                    actors.Add(a); UseObservationInterval(a.AI, delay); a.AI.RequestAggro(target);
                    // Drive planning once per simulation frame with all production crowd/flow inputs, without unrelated state/render work.
                    a.AI.enabled = false; a.Movement.enabled = false;
                    ActivePlanningActors.Add(a.AI); // Preserve production active-count LOD inputs during the direct planning benchmark.
                    foreach (var renderer in a.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
                    calls.Add((Func<Vector3>)Delegate.CreateDelegate(typeof(Func<Vector3>), a.AI, method));
                }
                phase = "planning_warmup";
                for (int frame = 0; frame < 30; frame++) { foreach (var call in calls) call(); yield return null; }
                var samples = new List<double>(120); long allocated = 0;
                int before = actors.Sum(a => a.AI.PlanningEvaluationCount), reuseBefore = actors.Sum(a => a.AI.PlanningReuseCount);
                phase = "planning_measured";
                for (int frame = 0; frame < 120; frame++)
                {
                    target.position = new Vector3(Mathf.Sin(frame * .09f) * 5f, 0, 20f); Physics.SyncTransforms();
                    long bytes = GC.GetAllocatedBytesForCurrentThread(), ticks = Stopwatch.GetTimestamp();
                    foreach (var call in calls) call();
                    long stop = Stopwatch.GetTimestamp(); allocated += GC.GetAllocatedBytesForCurrentThread() - bytes;
                    samples.Add((stop - ticks) * 1000.0 / Stopwatch.Frequency); yield return null;
                }
                samples.Sort();
                results.Add(new JObject { ["scenario"] = "planning_CPU_GC_only", ["actorCount"] = count, ["observationSeconds"] = delay, ["samples"] = samples.Count,
                    ["medianMs"] = samples[samples.Count / 2], ["p95Ms"] = samples[(int)(samples.Count * .95f)], ["allocatedBytes"] = allocated,
                    ["planningEvaluations"] = actors.Sum(a => a.AI.PlanningEvaluationCount) - before, ["planningReuse"] = actors.Sum(a => a.AI.PlanningReuseCount) - reuseBefore,
                    ["includesRendering"] = false, ["fullFrameBenchmark"] = false, ["registeredActorCount"] = EnemyAIController.ActiveEnemyCount,["productionActiveCountLOD"] = true });
            }
            finally { foreach (var value in actors) { ActivePlanningActors.Remove(value.AI); foreach (var renderer in value.GetComponentsInChildren<Renderer>()) renderer.enabled = true; value.AI.enabled = true; value.Movement.enabled = true; service.Release(value); } }
            Write("RUNNING"); yield return null;
        }
    }
}
