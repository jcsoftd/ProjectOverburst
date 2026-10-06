using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static partial class SevenThemeFollowupFlowVerifier
{
    static string Mode => SessionState.GetString(ModeKey, "");
    static void SaveCase(string key)
    {
        SaveCurrentTrace(); camera?.Dispose(); camera = null;
        service.Release(actor); actor = null; phase = "none"; Write("RUNNING");
    }

    static IEnumerator NearProbe(EnemyCatalog catalog, CombatHealth health, CapsuleCollider capsule)
    {
        // Deliberately close diagnostic target and bounded navigation isolate failed backpedals.
        // Disabled target collider prevents artificial penetration impulses; this is not dungeon footage.
        capsule.enabled = false;
        string[] ids = { "V3_Skorpmare", "V3_darkKnight2", "V3_Gasterodonte", "V3_Kapeloproboskid", "V3_Anglerox", "V3_Onyscidus" };
        var previousArea = RunWalkableContext.Current;
        try
        {
            foreach (string id in ids) foreach (bool blocked in new[] { false, true })
            {
                if (!catalog.TryGet(id, out var d)) throw new InvalidOperationException(id);
                currentId = id + (blocked ? "_blocked" : "_open"); trace = new JArray(); damages = new JArray(); begin = Time.time;
                phase = "close_" + (blocked ? "blocked_navigation" : "open_floor"); target.position = Vector3.forward * .55f; health.SetMaxHp(1e9f, true);
                RunWalkableContext.Clear();
                if (!service.TrySpawn(new EnemySpawnRequest(d, Vector3.up * .035f, Quaternion.identity, target, context: EncounterContext.Test), out actor)) throw new InvalidOperationException("Spawn " + id);
                actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                yield return null; yield return new WaitForFixedUpdate();
                if (blocked)
                {
                    var cells = new bool[1, 1]; cells[0, 0] = true;
                    RunWalkableContext.SetCurrent(new RunWalkableArea(cells, 1, 1, -.12f, -.12f, .24f));
                }
                actor.AI.RequestAggro(target); new EnemyCombatBehavior(actor.AI).Evaluate();
                camera = new Capture(Path.Combine(Folder, currentId), target, actor); camera.Mark(phase);
                Vector3 first = actor.transform.position; float until = Time.time + 7f;
                while (Time.time < until) yield return null;
                int entries = 0, attacks = 0; string previous = "";
                foreach (JObject sample in trace)
                {
                    string state = (string)sample["ai"];
                    if (state != previous && state == "Reposition") entries++;
                    if (state != previous && state == "Attack") attacks++;
                    previous = state;
                }
                results.Add(new JObject { ["id"] = id, ["scenario"] = phase, ["name"] = d.DisplayName, ["repositionEntries"] = entries, ["attackEntries"] = attacks,
                    ["displacement"] = Vector3.Distance(first, actor.transform.position), ["diagnosticTargetColliderDisabled"] = true, ["actualSavedAI"] = true });
                SaveCase(currentId); yield return null;
                RunWalkableContext.Clear();
            }
        }
        finally { capsule.enabled = true; RunWalkableContext.SetCurrent(previousArea); }
    }
}
