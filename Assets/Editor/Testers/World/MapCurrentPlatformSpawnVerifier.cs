using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public static class MapCurrentPlatformSpawnVerifier
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static IEnumerator WaitSeconds(double seconds)
    { double until = EditorApplication.timeSinceStartup + seconds; while (EditorApplication.timeSinceStartup < until) yield return null; }
    public static IEnumerator Verify(RunWorldGate gate, MapMonsterField field, string output, Action<bool, string> check)
    {
        var budget = gate.SpawnBudget; var roster = gate.Roster; var player = PlayerContext.Instance.CurrentActor;
        int total = roster.Total, target = roster.Target;
        Vector3 Stand(MapRunRoster.Area a) => a.Ground.Points.OrderBy(p => (p - a.Ground.Center).sqrMagnitude).First();
        void Move(Vector3 point) => ActorTeleportUtility.TeleportSafely(player.transform, point + Vector3.up * .12f, Quaternion.identity);
        var current = field.FiniteArea; var stand = Stand(current);
        Move(stand); yield return null;
        check(budget.CurrentPlayerArea == current.Ground.Index, "Current-platform: actual floor selects the owning platform");
        var cap = typeof(MapSpawnBudget).GetField("<AliveLimit>k__BackingField", Hidden);
        int limit = budget.AliveLimit;
        MapRunRoster.Member[] pending;
        try
        {
            cap.SetValue(budget, 0);
            foreach (var member in roster.Members.Where(m => m.AreaIndex == current.Ground.Index && m.State == MapRunRoster.Life.Active).Take(5).ToArray())
                if (field.TryGetActor(member.Index, out var actor))
                    actor.Health.TakeDamage(new DamageInfo(1000000, actor.transform.position, player.gameObject, suppressDefaultHitVfx: true));
            yield return WaitSeconds(1.2);
            var slots = ((IEnumerable)typeof(MapMonsterField).GetField("spawnSlots", Hidden).GetValue(field)).Cast<object>();
            pending = slots.Where(s => !(bool)s.GetType().GetField("EverSpawned").GetValue(s))
                .Select(s => (MapRunRoster.Member)s.GetType().GetField("Member").GetValue(s)).Where(m => !m.Initial).ToArray();
            check(pending.Length > 0, "Current-platform: real reinforcement is queued while global capacity is full");
            var other = roster.Areas.Where(a => a.Ground.Index != current.Ground.Index)
                .OrderBy(a => (a.Ground.Center - stand).sqrMagnitude).First();
            Move(Stand(other));
            yield return null;
            check(budget.CurrentPlayerArea == other.Ground.Index, "Current-platform: changing platform replaces the active floor");
        }
        finally { cap.SetValue(budget, limit); }
        int released = field.ReinforcementReleased, killed = roster.Kills;
        yield return WaitSeconds(1.2);
        check(pending.All(m => m.State == MapRunRoster.Life.Pending && m.Lease == 0)
            && field.ReinforcementReleased == released, "Current-platform: leaving pauses queued fresh IDs and further release");
        check(roster.Total == total && roster.Target == target && roster.Kills == killed,
            "Current-platform: paused reserves preserve quota, completion target and actual death count");

        bool foundRoad = false; Vector3 road = default;
        foreach (var candidate in budget.Fields.SelectMany(f => f.FiniteApproaches))
            if (!roster.Areas.Any(a => MapCombatGeometry.Contains(a.Ground, candidate))
                && NavMesh.SamplePosition(candidate, out var nav, .3f, NavMesh.AllAreas))
            { road = nav.position; foundRoad = true; break; }
        check(foundRoad, "Current-platform: generated connecting road has a valid floor outside every platform");
        Move(road); yield return null;
        check(budget.CurrentPlayerArea == -1, "Current-platform: connecting road has no current reinforcement platform");
        var neverSpawned = roster.Members.Where(m => !m.Initial && m.Lease == 0).ToArray();
        var releases = budget.Fields.Select(f => f.ReinforcementReleased).ToArray();
        yield return WaitSeconds(1.2);
        check(neverSpawned.All(m => m.Lease == 0) && budget.Fields.Select(f => f.ReinforcementReleased).SequenceEqual(releases),
            "Current-platform: no platform releases or executes fresh reserves while player is on a road");
        Move(stand);
        double until = EditorApplication.timeSinceStartup + 30;
        while (!pending.Any(m => m.State == MapRunRoster.Life.Active) && EditorApplication.timeSinceStartup < until) yield return null;
        check(pending.Any(m => m.State == MapRunRoster.Life.Active), "Current-platform: returning resumes the same queued IDs");
        check(roster.Total == total && roster.Target == target && roster.Kills == killed,
            "Current-platform: return does not regenerate quota or fabricate deaths");
        check(MapSpawnBudget.MaxPerFrame == 2 && MapSpawnBudget.FrameBudgetMilliseconds == 2
            && budget.Fields.All(f => f.FiniteActiveCount <= f.LocalFightLimit), "Current-platform: shared two-attempt/two-ms policy and platform caps remain");
        File.WriteAllText(Path.Combine(output, "current-platform.json"), JsonConvert.SerializeObject(new { status = "PASS", area = current.Ground.Index,
            queuedIds = pending.Select(m => m.Id), total, target, killed, road = new[] { road.x, road.y, road.z } }, Formatting.Indented));
    }
}
