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
        MapFiniteSpawnIndexVerifier.VerifyRuntime(budget, check);
        var first = roster.Members.First(m => m.State == MapRunRoster.Life.Active);
        var field = budget.Fields[first.AreaIndex];
        check(field.TryGetActor(first.Index, out var actor), label + ": logical member resolves to actual actor");
        yield return Frames(3);
        check(actor.AI.enabled && actor.Movement.enabled && actor.Animator.enabled, label + ": prepared actor resumes after input opens");
        VerifyClearedPositions(gate, check);
        check(budget.Fields.All(f => f.LocalFightLimit >= (f.FiniteArea.BasePopulation <= 60 ? 12 : f.FiniteArea.BasePopulation <= 110 ? 18 : 28)
            && f.LocalFightLimit <= (f.FiniteArea.BasePopulation <= 60 ? 16 : f.FiniteArea.BasePopulation <= 110 ? 26 : 40)), label + ": reduced local fight limits in every platform");
        ScreenCapture.CaptureScreenshot(Path.Combine(output, "initial-" + roster.Terrain + "-" + roster.Areas.Count + ".png"));
        yield return Frames(3);
        int total = roster.Total, target = roster.Target, before = roster.Kills;
        var hit = new DamageInfo(1000000, actor.transform.position, player.gameObject, suppressDefaultHitVfx: true);
        actor.Health.TakeDamage(hit); actor.Health.TakeDamage(hit);
        check(roster.Kills == before + 1 && first.State == MapRunRoster.Life.Dead, label + ": repeated damage records one natural death");
        yield return Frames(8);
        check(roster.Total == total && roster.Target == target && !field.TryGetActor(first.Index, out _), label + ": death preserves denominator and cannot respawn");
        MapFiniteSpawnIndexVerifier.VerifyRuntime(budget, check);

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
            while (!roster.Areas.Where(a => a.Count > 0).All(a => a.Cleared) && EditorApplication.timeSinceStartup < until)
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
            check(roster.Areas.Where(a => a.Count > 0).All(a => a.Cleared && a.Kills >= a.ClearTarget)
                && roster.Kills + roster.Active + roster.Pending == total, label + ": every natural platform reaches 95 percent with its remaining IDs preserved");
            check(roster.Terrain != Overburst.Persistence.MapTerrainKind.Cave || roster.CaveTargetReached, label + ": platform clearing still reaches the fixed cave completion target");
            var leases = roster.Members.Select(m => m.Lease).ToArray();
            yield return Frames(20);
            check(roster.Members.Select(m => m.Lease).SequenceEqual(leases), label + ": cleared map never spawns or restores another natural ID");
        }
        Write(output, label, gate);
        MapFiniteSpawnIndexVerifier.VerifyRuntime(budget, check);
        ScreenCapture.CaptureScreenshot(Path.Combine(output, "density-" + roster.Terrain + "-" + roster.Areas.Count + ".png"));
        yield return Frames(3);
    }

    static void VerifyClearedPositions(RunWorldGate gate, Action<bool, string> check)
    {
        var roster = gate.Roster; var fields = gate.SpawnBudget.Fields;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var slotType = typeof(MapMonsterField).GetNestedType("Slot", System.Reflection.BindingFlags.NonPublic);
        var groupType = typeof(MapMonsterField).GetNestedType("SpawnGroup", System.Reflection.BindingFlags.NonPublic);
        var place = typeof(MapMonsterField).GetMethod("TryFinitePosition", flags);
        var kills = typeof(MapRunRoster.Area).GetProperty(nameof(MapRunRoster.Area.Kills));
        var player = PlayerContext.Instance.CurrentActor.transform.position;
        foreach (var court in roster.Areas.Where(a => a.Count > 0))
        {
            var source = roster.Areas.FirstOrDefault(a => a.Count > 0 && a != court);
            if (source == null) continue;
            var member = roster.Members.Skip(source.FirstMember).Take(source.Count).First(m => !m.Initial);
            var body = MapCombatGeometry.BodyFor(member.Definition);
            var slot = Activator.CreateInstance(slotType, true);
            void Set(string name, object value) => slotType.GetField(name).SetValue(slot, value);
            Set("Member", member); Set("Radius", body.Radius); Set("Height", body.Height);
            var group = Activator.CreateInstance(groupType, true);
            groupType.GetField("Ground").SetValue(group, court.Ground); Set("Group", group);
            foreach (var point in court.Ground.Points)
            {
                groupType.GetField("Point").SetValue(group, point); Set("GroupCursor", 0); Set("Cursor", 0);
                var args = new object[] { slot, player, false, Vector3.zero };
                if (!(bool)place.Invoke(fields[source.Ground.Index], args) || !MapCombatGeometry.Contains(court.Ground, (Vector3)args[3])) continue;
                int before = court.Kills;
                try
                {
                    // Synchronous fault injection: the same cached neighbour group must be rejected after clearing.
                    kills.SetValue(court, court.ClearTarget); Set("GroupCursor", 0); Set("Cursor", 0);
                    args[3] = Vector3.zero;
                    bool accepted = (bool)place.Invoke(fields[source.Ground.Index], args);
                    check(!accepted || !MapCombatGeometry.Contains(court.Ground, (Vector3)args[3]),
                        "Cleared platform " + court.Ground.Index + ": previously valid cached neighbour group cannot respawn here");
                    args[3] = Vector3.zero;
                    check(!(bool)place.Invoke(fields[court.Ground.Index], args),
                        "Cleared platform " + court.Ground.Index + ": own floor and approaches reject spawning");
                }
                finally { kills.SetValue(court, before); }
                check(!court.Cleared && roster.Areas.Sum(a => a.Kills) == roster.Kills,
                    "Cleared placement injection restores the original natural death counts");
                return;
            }
        }
        check(false, "A real offscreen neighbouring spawn point is required for the cleared-platform regression");
    }

    // Scale-only pass: actual population in every area, without repeating the failure-injection suite.
    public static IEnumerator VerifyScale(RunWorldGate gate, string output, Action<bool, string> check)
    {
        var roster = gate.Roster; var budget = gate.SpawnBudget; var player = PlayerContext.Instance.CurrentActor;
        int total = roster.Total, target = roster.Target, lastKills = roster.Kills;
        string label = "G08 cave " + roster.Areas.Count;
        var samples = new System.Collections.Generic.List<int>();
        var deaths = new System.Collections.Generic.List<int>();
        var areas = new System.Collections.Generic.List<object>();
        bool limits = true;
        void Sample()
        {
            limits &= budget.AliveCount <= budget.AliveLimit && budget.SpawnedThisFrame <= MapSpawnBudget.MaxPerFrame
                && roster.Kills >= lastKills && roster.Total == total && roster.Target == target;
            lastKills = roster.Kills; samples.Add(budget.AliveCount);
        }
        double until = EditorApplication.timeSinceStartup + 30;
        while (WorldMinimapController.Instance?.Exploration == null && EditorApplication.timeSinceStartup < until) yield return null;
        var map = WorldMinimapController.Instance?.Exploration;
        check(map != null, label + ": exploration ready");
        var reached = (bool[])typeof(MapExplorationVerifier).GetMethod("Reachable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Invoke(null, new object[] { map, map.CellAt(player.transform.position) });
        var entry = gate.EntryPoint.position;
        MapRunRoster.Member saved = null; Vector3 savedPoint = default; int savedLease = 0;
        void ClearNearbyOthers(int currentArea)
        {
            foreach (var member in roster.Members)
                if (member.AreaIndex != currentArea && member.State == MapRunRoster.Life.Active && member != saved
                    && budget.Fields[member.AreaIndex].TryGetActor(member.Index, out var enemy))
                {
                    var delta = enemy.transform.position - player.transform.position; delta.y = 0;
                    if (delta.sqrMagnitude > 24 * 24) continue;
                    enemy.Health.TakeDamage(new DamageInfo(1000000, enemy.transform.position, player.gameObject, suppressDefaultHitVfx: true));
                    deaths.Add(member.Index);
                }
        }
        foreach (var area in roster.Areas)
        {
            var point = area.Ground.Points.OrderBy(p => (p - area.Ground.Center).sqrMagnitude).First();
            var path = new UnityEngine.AI.NavMeshPath(); int cell = map.CellAt(point);
            check(UnityEngine.AI.NavMesh.CalculatePath(entry, point, UnityEngine.AI.NavMesh.AllAreas, path)
                && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete, label + ": NavMesh reaches area " + area.Ground.Index);
            check(cell >= 0 && map.IsGround(cell) && reached[cell], label + ": minimap links reach area " + area.Ground.Index);
            var field = budget.Fields[area.Ground.Index];
            if (!field.InitialPlaced)
            {
                bool found = false; Vector3 approach = point;
                float range = area.Ground.Points.Max(p => Vector3.Distance(p, area.Ground.Center)) + 30;
                var initialMembers = roster.Members.Skip(area.FirstMember).Take(area.Count).Where(m => m.Initial).ToArray();
                for (int n = path.corners.Length - 1; n > 0 && !found; n--)
                {
                    float length = Vector3.Distance(path.corners[n], path.corners[n - 1]);
                    for (float step = 0; step <= length && !found; step += .5f)
                    {
                        var candidate = Vector3.Lerp(path.corners[n], path.corners[n - 1], length > 0 ? step / length : 0);
                        var delta = candidate - area.Ground.Center; delta.y = 0;
                        if (delta.sqrMagnitude > range * range) continue;
                        if (initialMembers.Any(m => { var d = m.PlannedPosition - candidate; d.y = 0; return d.sqrMagnitude < 16; })) continue;
                        approach = candidate; found = true;
                    }
                }
                check(found, label + ": reachable pre-entry approach in area " + area.Ground.Index);
                ActorTeleportUtility.TeleportSafely(player.transform, approach + Vector3.up * .12f, Quaternion.identity);
                until = EditorApplication.timeSinceStartup + 20;
                while (!field.InitialPlaced && EditorApplication.timeSinceStartup < until) { ClearNearbyOthers(area.Ground.Index); Sample(); yield return null; }
            }
            ActorTeleportUtility.TeleportSafely(player.transform, point + Vector3.up * .12f, Quaternion.identity);
            until = EditorApplication.timeSinceStartup + 20;
            while ((!field.InitialPlaced || !map.IsRevealed(point)) && EditorApplication.timeSinceStartup < until) { ClearNearbyOthers(area.Ground.Index); Sample(); yield return null; }
            check(field.InitialPlaced && (field.FiniteActiveCount > 0 || deaths.Any(i => roster.Members[i].AreaIndex == area.Ground.Index)), label + ": actual initial actors in area " + area.Ground.Index);
            check(map.IsRevealed(point), label + ": visiting reveals area " + area.Ground.Index);
            var initial = roster.Members.Skip(area.FirstMember).Take(area.Count).Where(m => m.Initial && m.State == MapRunRoster.Life.Active).ToArray();
            int before = roster.Kills;
            foreach (var member in initial)
                if (field.TryGetActor(member.Index, out var enemy))
                { enemy.Health.TakeDamage(new DamageInfo(1000000, enemy.transform.position, player.gameObject, suppressDefaultHitVfx: true)); deaths.Add(member.Index); }
            check(roster.Kills == before + initial.Length && (initial.Length > 0 || deaths.Any(i => roster.Members[i].AreaIndex == area.Ground.Index && roster.Members[i].Initial)), label + ": initial kills recorded once in area " + area.Ground.Index);
            Write(output, label, gate);
            until = EditorApplication.timeSinceStartup + 60;
            while (!roster.Members.Skip(area.FirstMember).Take(area.Count).Any(m => !m.Initial && m.State == MapRunRoster.Life.Active)
                && EditorApplication.timeSinceStartup < until) { ClearNearbyOthers(area.Ground.Index); Sample(); yield return null; }
            var reserve = roster.Members.Skip(area.FirstMember).Take(area.Count).FirstOrDefault(m => !m.Initial && m.State == MapRunRoster.Life.Active);
            check(reserve != null && field.ReinforcementReleased > 0, label + ": actual reinforcement in area " + area.Ground.Index);
            if (saved == null && reserve != null && field.TryGetActor(reserve.Index, out var living))
            { saved = reserve; savedLease = reserve.Lease; savedPoint = point; living.Health.RestoreLivingHealth(living.Health.MaxHp * .6f); }
            areas.Add(new { area.Ground.Index, area.Count, area.InitialCount, initialKilled = initial.Length, field.ReinforcementReleased, field.FiniteActiveCount, field.NeighbourSpawnedCount });
            Write(output, label, gate);
        }
        ActorTeleportUtility.TeleportSafely(player.transform, savedPoint + Vector3.up * .12f, Quaternion.identity);
        until = EditorApplication.timeSinceStartup + 25;
        var owner = budget.Fields[saved.AreaIndex];
        while (!owner.TryGetActor(saved.Index, out _) && EditorApplication.timeSinceStartup < until) { Sample(); yield return null; }
        check(owner.TryGetActor(saved.Index, out var returned) && saved.Lease >= savedLease
            && Mathf.Abs(returned.Health.CurrentHp / returned.Health.MaxHp - .6f) < .002f,
            label + ": revisit preserves living ID and damaged health ratio");
        check(deaths.All(i => roster.Members[i].State == MapRunRoster.Life.Dead), label + ": revisiting does not restore killed IDs");
        Sample();
        check(limits && samples.Count > 0 && roster.Active == budget.Fields.Sum(f => f.FiniteActiveCount)
            && roster.Active + roster.Pending + roster.Kills == total, label + ": frame/live limits and finite totals across traversal");
        if (roster.Areas.Count == 10) check(budget.Fields.Sum(f => f.NeighbourSpawnedCount) > 0, label + ": actual neighbouring-platform reinforcement while remaining in the court");
        var sorted = samples.OrderBy(n => n).ToArray();
        File.WriteAllText(Path.Combine(output, "scale.json"), JsonConvert.SerializeObject(new { status = "PASS", roster.Total, roster.Target, roster.Kills,
            roster.Active, roster.Pending, budget.StreamReturns, activeP95 = sorted[Mathf.Min(sorted.Length - 1, Mathf.FloorToInt(sorted.Length * .95f))],
            activeMax = sorted.Last(), areas, scope = "All-area initial/reinforcement sample and teleport revisit with NavMesh/minimap topology; not a full clear, manual movement or Player performance." }, Formatting.Indented));
    }
    static void Write(string output, string label, RunWorldGate gate)
    {
        var budget = gate.SpawnBudget; var roster = gate.Roster;
        File.WriteAllText(Path.Combine(output, "finite-" + roster.Terrain + "-" + roster.Areas.Count + ".json"), JsonConvert.SerializeObject(new
        {
            label, roster.Total, roster.Target, roster.Kills, roster.Active, roster.Pending,
            budget.AliveLimit, budget.InitialPreparedCount, budget.SpawnRetries, budget.StreamReturns, budget.BoundaryRecoveries,
            areas = budget.Fields.Select(f => new { index = f.FiniteArea.Ground.Index, f.PrepareAtEntry, f.InitialPlaced, f.HasTriggered, f.LocalFightLimit, f.FiniteActiveCount, f.ReinforcementReleased, f.NeighbourSpawnedCount }),
            members = roster.Members.Select(m => new { m.Id, enemy = m.Definition.EnemyId, m.AreaIndex, m.Initial, life = m.State.ToString(), m.Lease })
        }, Formatting.Indented));
    }
}
