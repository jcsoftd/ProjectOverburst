using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

public static class MapFiniteSpawnPlayVerifier
{
    static IEnumerator Frames(int count) { while (count-- > 0) yield return null; }
    public static IEnumerator Verify(RunWorldGate gate, string output, Action<bool, string> check)
    {
        var roster = gate.Roster; var budget = gate.SpawnBudget; var player = PlayerContext.Instance.CurrentActor;
        string label = "G03 " + roster.Terrain + " " + roster.Areas.Count;
        check(budget && budget.InitialReady && budget.InitialPreparedCount > 0, label + ": initial actors ready behind loading screen");
        check(budget.Fields.Where(f => f.PrepareAtEntry).All(f => f.InitialPlaced), label + ": all entry-area initial members placed");
        check(EnemySpawnService.Current && EnemySpawnService.Current.gameObject.scene == gate.gameObject.scene, label + ": scene-local pool survives hub unload");
        check(budget.AliveCount <= budget.AliveLimit && budget.SpawnedThisFrame <= MapSpawnBudget.MaxPerFrame, label + ": frame and live actor limits respected");
        check(roster.Active == budget.Fields.Sum(f => f.FiniteActiveCount), label + ": every active ID has one actor lease");
        var first = roster.Members.First(m => m.State == MapRunRoster.Life.Active);
        var field = budget.Fields[first.AreaIndex];
        check(field.TryGetActor(first.Index, out var actor), label + ": logical member resolves to actual actor");
        yield return Frames(3);
        check(actor.AI.enabled && actor.Movement.enabled && actor.Animator.enabled, label + ": prepared actor resumes after input opens");
        ScreenCapture.CaptureScreenshot(Path.Combine(output, "initial-" + roster.Terrain + "-" + roster.Areas.Count + ".png"));
        yield return Frames(3);
        int total = roster.Total, target = roster.Target, before = roster.Kills;
        var hit = new DamageInfo(1000000, actor.transform.position, player.gameObject, suppressDefaultHitVfx: true);
        actor.Health.TakeDamage(hit); actor.Health.TakeDamage(hit);
        check(roster.Kills == before + 1 && first.State == MapRunRoster.Life.Dead, label + ": repeated damage records one natural death");
        yield return Frames(8);
        check(roster.Total == total && roster.Target == target && !field.TryGetActor(first.Index, out _), label + ": death preserves denominator and cannot respawn");

        // Kill the active entrance packs quickly. Finite reserves must become real walk-in actors.
        var initialActive = roster.Members.Where(m => m.State == MapRunRoster.Life.Active).ToArray();
        foreach (var member in initialActive)
            if (budget.Fields[member.AreaIndex].TryGetActor(member.Index, out var enemy))
                enemy.Health.TakeDamage(new DamageInfo(1000000, enemy.transform.position, player.gameObject, suppressDefaultHitVfx: true));
        int killed = roster.Kills;
        double until = EditorApplication.timeSinceStartup + 30;
        while (!roster.Members.Any(m => !m.Initial && m.State == MapRunRoster.Life.Active) && EditorApplication.timeSinceStartup < until) yield return null;
        Write(output, label, gate);
        check(roster.Members.Any(m => !m.Initial && m.State == MapRunRoster.Life.Active), label + ": depleted packs receive finite reinforcement");
        check(roster.Kills == killed && roster.Total == total, label + ": reinforcement and placement retries do not fabricate kills");

        var remaining = roster.Members.First(m => m.State == MapRunRoster.Life.Active);
        var owner = budget.Fields[remaining.AreaIndex]; owner.TryGetActor(remaining.Index, out var living);
        living.Health.RestoreLivingHealth(living.Health.MaxHp * .45f); float hp = living.Health.CurrentHp;
        int oldLease = remaining.Lease;
        var service = EnemySpawnService.Current;
        service.Release(living);
        until = EditorApplication.timeSinceStartup + 30;
        while (remaining.Lease == oldLease && EditorApplication.timeSinceStartup < until) yield return null;
        check(remaining.State == MapRunRoster.Life.Active && remaining.Lease > oldLease && owner.TryGetActor(remaining.Index, out living), label + ": unexpected pool return recovers same living ID");
        check(Mathf.Abs(living.Health.CurrentHp - hp) < .01f && roster.Kills == killed, label + ": pooled return preserves damaged HP without XP or death");
        check(!roster.TryRecordDeath(remaining.Index, oldLease), label + ": stale logical lease cannot count a death");
        check(!roster.TryActivate(first.Index, out _), label + ": dead ID stays dead after pool reuse");
        Vector3 supported = living.transform.position;
        int recoveries = budget.BoundaryRecoveries;
        var rigidbody = living.GetComponent<Rigidbody>();
        var fallen = supported - Vector3.up * 12;
        if (rigidbody) rigidbody.position = fallen;
        living.transform.position = fallen; Physics.SyncTransforms();
        until = EditorApplication.timeSinceStartup + 5;
        while (living.transform.position.y < supported.y - 2 && EditorApplication.timeSinceStartup < until) yield return null;
        check(living.transform.position.y >= supported.y - 2 && !living.Health.IsDead && roster.Kills == killed
            && Mathf.Abs(living.Health.CurrentHp - hp) < .01f, label + ": boundary recovery preserves the living ID, HP and death count");
        check(roster.Terrain == Overburst.Persistence.MapTerrainKind.Cave || budget.BoundaryRecoveries > recoveries,
            label + ": desert fall uses the finite spawn recovery path");
        // A full queue may delay a living ID but may never consume or count it as killed.
        var cap = typeof(MapSpawnBudget).GetField("<AliveLimit>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        int priorLimit = budget.AliveLimit, priorActive = roster.Active;
        cap.SetValue(budget, 0);
        try
        {
            yield return Frames(8);
            check(budget.SpawnedThisFrame == 0 && roster.Active == priorActive && roster.Kills == killed, label + ": exhausted global capacity preserves actors and pending IDs");
        }
        finally { cap.SetValue(budget, priorLimit); }
        var catalogField = typeof(EnemySpawnService).GetField("catalog", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var catalog = service.Catalog; int retries = budget.SpawnRetries;
        catalogField.SetValue(service, null);
        try
        {
            until = EditorApplication.timeSinceStartup + 6;
            while (budget.SpawnRetries < retries + 4 && EditorApplication.timeSinceStartup < until) yield return null;
            check(budget.SpawnRetries >= retries + 4 && roster.Total == total && roster.Kills == killed, label + ": repeated spawn failures preserve the finite denominator and deaths");
        }
        finally { catalogField.SetValue(service, catalog); }
        priorActive = roster.Active;
        until = EditorApplication.timeSinceStartup + 20;
        while (roster.Active <= priorActive && EditorApplication.timeSinceStartup < until) yield return null;
        check(roster.Active > priorActive, label + ": failed requests resume after the original service recovers");
        if (roster.Areas.Count == 20)
        {
            var far = roster.Areas.OrderByDescending(a => (a.Ground.Center - living.transform.position).sqrMagnitude).First();
            Vector3 entry = gate.EntryPoint.position;
            oldLease = remaining.Lease; hp = living.Health.CurrentHp;
            ActorTeleportUtility.TeleportSafely(player.transform, far.Ground.Center + Vector3.up * .12f, Quaternion.identity);
            until = EditorApplication.timeSinceStartup + 12;
            while (remaining.State == MapRunRoster.Life.Active && EditorApplication.timeSinceStartup < until) yield return null;
            check(remaining.State == MapRunRoster.Life.Pending && budget.StreamReturns > 0 && roster.Kills == killed, label + ": distant live actor streams out without a death");
            yield return Frames(20);
            ActorTeleportUtility.TeleportSafely(player.transform, entry, Quaternion.identity);
            until = EditorApplication.timeSinceStartup + 25;
            while (remaining.Lease == oldLease && EditorApplication.timeSinceStartup < until) yield return null;
            check(remaining.State == MapRunRoster.Life.Active && owner.TryGetActor(remaining.Index, out living)
                && Mathf.Abs(living.Health.CurrentHp - hp) < .01f, label + ": revisit restores same ID and damaged health");
            check(first.State == MapRunRoster.Life.Dead && roster.Total == total && roster.Target == target, label + ": revisiting never resets dead IDs or target");
        }
        if (roster.Areas.Count == 4)
        {
            int previousKills = roster.Kills; double movedAt = 0; int destination = 0, samples = 0;
            bool frameLimits = true;
            until = EditorApplication.timeSinceStartup + 120;
            while (roster.Kills < total && EditorApplication.timeSinceStartup < until)
            {
                foreach (var member in roster.Members)
                    if (member.State == MapRunRoster.Life.Active && budget.Fields[member.AreaIndex].TryGetActor(member.Index, out var enemy))
                        enemy.Health.TakeDamage(new DamageInfo(1000000, enemy.transform.position, player.gameObject, suppressDefaultHitVfx: true));
                if (EditorApplication.timeSinceStartup >= movedAt)
                {
                    var area = roster.Areas[destination++ % roster.Areas.Count];
                    ActorTeleportUtility.TeleportSafely(player.transform, area.Ground.Center + Vector3.up * .12f, Quaternion.identity);
                    movedAt = EditorApplication.timeSinceStartup + 3;
                }
                frameLimits &= budget.SpawnedThisFrame <= MapSpawnBudget.MaxPerFrame && roster.Kills >= previousKills && roster.Active <= budget.AliveLimit;
                samples++;
                previousKills = roster.Kills;
                yield return Frames(4);
            }
            Write(output, label, gate);
            check(frameLimits && samples > 0, label + ": shared frame budget and monotonic deaths across " + samples + " sweep samples");
            check(roster.Kills == total && roster.Active == 0 && roster.Pending == 0, label + ": all natural IDs can be reached and exhausted once");
            yield return Frames(20);
            check(roster.Kills == total && roster.Active == 0, label + ": exhausted map never replenishes itself");
        }
        Write(output, label, gate);
        ScreenCapture.CaptureScreenshot(Path.Combine(output, "density-" + roster.Terrain + "-" + roster.Areas.Count + ".png"));
        yield return Frames(3);
    }
    static void Write(string output, string label, RunWorldGate gate)
    {
        var budget = gate.SpawnBudget; var roster = gate.Roster;
        File.WriteAllText(Path.Combine(output, "finite-" + roster.Terrain + "-" + roster.Areas.Count + ".json"), JsonConvert.SerializeObject(new
        {
            label, roster.Total, roster.Target, roster.Kills, roster.Active, roster.Pending,
            budget.AliveLimit, budget.InitialPreparedCount, budget.SpawnRetries, budget.StreamReturns, budget.BoundaryRecoveries,
            areas = budget.Fields.Select(f => new { index = f.FiniteArea.Ground.Index, f.PrepareAtEntry, f.InitialPlaced, f.HasTriggered, f.LocalFightLimit, f.FiniteActiveCount, f.ReinforcementReleased }),
            members = roster.Members.Select(m => new { m.Id, enemy = m.Definition.EnemyId, m.AreaIndex, m.Initial, life = m.State.ToString(), m.Lease })
        }, Formatting.Indented));
    }
}
