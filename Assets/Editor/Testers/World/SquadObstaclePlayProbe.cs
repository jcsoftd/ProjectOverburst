using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.AI;

// Uses the generated map's own obstacle colliders, baked navigation and production AI/motor.
public static class SquadObstaclePlayProbe
{
    static void Place(Transform target, Vector3 position, Quaternion rotation)
    {
        // Production spawning enables a fresh motor lease. Test-only relocation must
        // likewise discard the prior safe-floor point instead of sweeping a teleport.
        var motor = target.GetComponent<EnemyMotor>();
        if (motor) typeof(EnemyMotor).GetField("caveBoundary", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(motor, default(Overburst.Caves.CaveFallProtection.Movement));
        ActorTeleportUtility.TeleportSafely(target, position, rotation);
    }

    public static IEnumerator VerifyRegions(RunWorldGate gate, string output, Action<bool, string> check)
    {
        var player = PlayerContext.Instance.CurrentActor;
        var original = player.transform.position;
        var records = new List<object>(); var failures = new List<string>();
        string directory = Path.Combine(output, "NavigationRegions"); Directory.CreateDirectory(directory);
        var fields = gate.SpawnBudget.Fields.Where(f => f.FiniteArea.Count > 0)
            .GroupBy(f => MapRunRoster.PlatformSize(gate.Map.terrain, f.FiniteArea.Ground))
            .Select(g => g.OrderBy(f => f.FiniteArea.Ground.Index).First()).ToArray();
        int cases = 0;
        try
        {
            foreach (var field in fields)
            {
                string size = MapRunRoster.PlatformSize(gate.Map.terrain, field.FiniteArea.Ground).ToString();
                var center = field.FiniteArea.Ground.Center;
                var stand = field.FiniteArea.Ground.Points.OrderBy(p => (p - center).sqrMagnitude).First();
                Place(player.transform, stand + Vector3.up * .12f, Quaternion.identity);
                float until = Time.time + 20;
                while (!field.InitialPlaced && Time.time < until) yield return null;
                for (int settle = 0; settle < 20; settle++) yield return null;
                foreach (bool largest in new[] { false, true })
                {
                    string name = size + "-area" + field.FiniteArea.Ground.Index + (largest ? "-large-body" : "-small-body");
                    string folder = Path.Combine(directory, name); Directory.CreateDirectory(folder);
                    void Record(bool ok, string message) { records.Add(new { name, ok, message }); if (!ok) failures.Add(name + ": " + message); }
                    yield return Verify(field, folder, Record, largest);
                    cases++;
                    File.WriteAllText(Path.Combine(directory, "progress.json"), JsonConvert.SerializeObject(records, Formatting.Indented));
                }
            }
            var world = gate.GetComponent<Overburst.Caves.CaveWorld>();
            if (world)
            {
                var links = world.generatedRoot.GetComponentsInChildren<Overburst.Caves.CaveRigidConnection>();
                foreach (var connection in links.GroupBy(c => c.stairCount > 0).Select(g => g.OrderBy(c => Vector3.Distance(c.start, c.end)).First()))
                foreach (bool reverse in new[] { false, true })
                {
                    var axis = connection.end - connection.start; axis.y = 0; axis.Normalize();
                    var a = reverse ? connection.end + axis * 2 : connection.start - axis * 2;
                    var b = reverse ? connection.start - axis * 6 : connection.end + axis * 6;
                    var source = reverse ? connection.boundaryB : connection.boundaryA;
                    var target = reverse ? connection.boundaryA : connection.boundaryB;
                    if (source.Sample(a, 10, 10, out var from)) a = from;
                    if (target.Sample(b, 10, 10, out var to)) b = to;
                    if (NavMesh.SamplePosition(a, out var navA, 2, NavMesh.AllAreas)) a = navA.position;
                    if (NavMesh.SamplePosition(b, out var navB, 2, NavMesh.AllAreas)) b = navB.position;
                    var field = gate.SpawnBudget.Fields.Where(f => f.FiniteArea.Count > 0).OrderBy(f => (f.FiniteArea.Ground.Center - a).sqrMagnitude).First();
                    Place(player.transform, a + Vector3.up * .12f, Quaternion.identity);
                    float until = Time.time + 20;
                    while (!field.InitialPlaced && Time.time < until) yield return null;
                    for (int settle = 0; settle < 20; settle++) yield return null;
                    string name = (connection.stairCount > 0 ? "stairs" : "bridge") + (reverse ? "-reverse" : "-forward");
                    string folder = Path.Combine(directory, name); Directory.CreateDirectory(folder);
                    void Record(bool ok, string message) { records.Add(new { name, ok, message }); if (!ok) failures.Add(name + ": " + message); }
                    yield return Verify(field, folder, Record, true, a, b, connection.stairCount > 0 ? source : null);
                    cases++;
                    File.WriteAllText(Path.Combine(directory, "progress.json"), JsonConvert.SerializeObject(records, Formatting.Indented));
                }
            }
        }
        finally
        {
            Place(player.transform, original, Quaternion.identity);
            File.WriteAllText(Path.Combine(directory, "result.json"), JsonConvert.SerializeObject(new { cases, failures, records }, Formatting.Indented));
        }
        check(cases >= fields.Length * 2, "Navigation sampled each available platform size with two body selections");
        check(failures.Count == 0, "Navigation regions: " + string.Join("; ", failures));
    }

    public static IEnumerator VerifyLooseGrouping(MapMonsterField field, Action<bool, string> check)
    {
        var actors = new List<EnemyActor>(); field.CollectLivingActors(actors);
        var pair = actors.Where(a => a.AI && a.AI.UsesMeleeSquadMovement && a.AI.UsesSquadPursuit)
            .GroupBy(a => a.AI.SquadPursuitPreset).First(g => g.Count() >= 2).Take(2).ToArray();
        var owners = pair.Select(a => a.AI.SquadEncounterOwner).ToArray();
        var anchors = pair.Select(a => a.AI.SquadEncounterAnchor).ToArray();
        var ids = pair.Select(a => a.AI.SpawnPackId).ToArray();
        var positions = pair.Select(a => a.transform.position).ToArray();
        var root = new GameObject("Owned loose squad membership check");
        try
        {
            Place(pair[1].transform, positions[0] + Vector3.right * 2, Quaternion.identity);
            foreach (var actor in pair)
            {
                actor.AI.SetSquadEncounter(root, PlayerContext.Instance.CurrentActor.transform, 0);
                actor.AI.RequestAggro(PlayerContext.Instance.CurrentActor.transform);
            }
            float until = Time.time + 1.2f;
            while (Time.time < until) { EnemySquadPursuitRuntimeService.GetRuntimeStats(); yield return null; }
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var agents = (IDictionary)typeof(EnemySquadPursuitRuntimeService).GetField("Agents", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var a = agents[pair[0].AI]; var b = agents[pair[1].AI];
            var squadA = a.GetType().GetField("Squad", flags).GetValue(a);
            var squadB = b.GetType().GetField("Squad", flags).GetValue(b);
            check(squadA != null && ReferenceEquals(squadA, squadB), "Two nearby unassigned actors form a squad without 41 actors");
            var encounter = a.GetType().GetField("Encounter", flags).GetValue(a);
            check((bool)encounter.GetType().GetField("Active", flags).GetValue(encounter), "A two-actor encounter activates immediately");
        }
        finally
        {
            for (int i = 0; i < pair.Length; i++) if (pair[i])
            {
                pair[i].AI.SetSquadEncounter(owners[i], anchors[i], ids[i]);
                Place(pair[i].transform, positions[i], Quaternion.identity);
            }
            UnityEngine.Object.Destroy(root);
        }
    }

    public static IEnumerator Verify(MapMonsterField field, string output, Action<bool, string> check, bool largest = false, Vector3? forcedStart = null, Vector3? forcedGoal = null, Overburst.Caves.CavePlatformBoundary restrictedPlatform = null)
    {
        var actors = new List<EnemyActor>(); field.CollectLivingActors(actors);
        var candidates = actors.Where(a => a.AI && a.AI.UsesMeleeSquadMovement && a.AI.SpawnPackId > 0).OrderBy(a => a.GetComponent<EnemyCrowdAgent>().PhysicalBodyRadius).ToArray();
        // A small platform can roll only ranged species. Use an existing melee actor to test its terrain.
        bool borrowedActor = candidates.Length == 0;
        if (borrowedActor)
            candidates = UnityEngine.Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None)
                .Where(a => a.Health && !a.Health.IsDead && a.AI && a.AI.UsesMeleeSquadMovement && a.AI.SpawnPackId > 0)
                .Where(a => field.FiniteArea.Elite > 0 || !a.Definition.Grade || a.Definition.Grade.GradeType == EnemyGradeType.Normal)
                .OrderBy(a => a.GetComponent<EnemyCrowdAgent>().PhysicalBodyRadius).ToArray();
        var actor = candidates.Length == 0 ? null : largest ? candidates[candidates.Length - 1] : candidates[0];
        check(actor, "Obstacle probe has a production melee actor");
        if (!actor) yield break;
        var player = PlayerContext.Instance.CurrentActor;
        var points = field.FiniteArea.Ground.Points;
        var path = new NavMeshPath();
        Vector3 start = default, goal = default; string obstacle = null;
        if (forcedStart.HasValue && forcedGoal.HasValue)
        {
            start = forcedStart.Value; goal = forcedGoal.Value; obstacle = "Installed connector";
            bool complete = NavMesh.CalculatePath(start, goal, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            check(complete, "Connector has a complete navigation route");
            if (!complete) yield break;
            if (restrictedPlatform)
            {
                int mask = NavMesh.AllAreas & ~(1 << NavMesh.GetAreaFromName("Jump"));
                var enemyPath = new NavMeshPath();
                check(!NavMesh.CalculatePath(start, goal, mask, enemyPath) || enemyPath.status != NavMeshPathStatus.PathComplete,
                    "Enemy route excludes the installed stair area");
                var playerBoundary = new Overburst.Caves.CaveFallProtection.Movement();
                var point = start;
                for (int i = 1; i <= 160; i++) point = playerBoundary.Resolve(point, Vector3.Lerp(start, goal, i / 160f), .35f);
                check(Vector3.Distance(point, goal) < .8f, "Default player boundary still traverses the stairs");
            }
        }
        // Ordinary obstacle cases must start on enemy-walkable floor. Stair traversal is checked separately above.
        int navigationMask = Overburst.Caves.CaveFallProtection.HasActiveWorld
            ? NavMesh.AllAreas & ~(1 << NavMesh.GetAreaFromName("Jump")) : NavMesh.AllAreas;
        int searches = 0;
        var hits = new RaycastHit[32];
        for (int i = 0; i < points.Count && obstacle == null; i += Mathf.Max(1, points.Count / 120))
        for (int j = i + 1; j < points.Count && obstacle == null; j += Mathf.Max(1, points.Count / 120))
        {
            Vector3 a = points[i], b = points[j], d = b - a;
            float distance = d.magnitude;
            if (distance < 8f || distance > 18f || Mathf.Abs(d.y) > .7f) continue;
            if (!NavMesh.SamplePosition(a, out var ah, .5f, navigationMask)
                || !NavMesh.SamplePosition(b, out var bh, .5f, navigationMask)
                || !NavMesh.Raycast(ah.position, bh.position, out _, navigationMask)) continue;
            int count = Physics.RaycastNonAlloc(a + Vector3.up * .6f, d.normalized, hits, distance, ~0, QueryTriggerInteraction.Ignore);
            string name = null;
            for (int k = 0; k < count; k++)
                if (hits[k].collider && !hits[k].collider.GetComponentInParent<EnemyActor>()
                    && !hits[k].collider.GetComponentInParent<PlayerActorRuntime>()
                    && Mathf.Abs(hits[k].normal.y) < .7f)
                    name = hits[k].collider.name;
            if (name == null) continue;
            bool valid = NavMesh.CalculatePath(ah.position, bh.position, navigationMask, path)
                && path.status == NavMeshPathStatus.PathComplete && path.corners.Length >= 3;
            if (++searches % 12 == 0) yield return null;
            if (!valid) continue;
            float length = 0; var corners = path.corners;
            for (int k = 1; k < corners.Length; k++) length += Vector3.Distance(corners[k - 1], corners[k]);
            if (length > distance * 2.5f || length > 35f) continue;
            start = ah.position; goal = bh.position; obstacle = name;
        }
        bool control = obstacle == null;
        if (control)
        {
            float farthest = 0;
            for (int i = 0; i < points.Count; i += Mathf.Max(1, points.Count / 80))
            for (int j = i + 1; j < points.Count; j += Mathf.Max(1, points.Count / 80))
            {
                if (!NavMesh.SamplePosition(points[i], out var a, .5f, navigationMask)
                    || !NavMesh.SamplePosition(points[j], out var b, .5f, navigationMask)
                    || NavMesh.Raycast(a.position, b.position, out _, navigationMask)) continue;
                float distance = Vector3.Distance(a.position, b.position);
                if (distance <= farthest) continue;
                farthest = distance; start = a.position; goal = b.position;
            }
            check(farthest > actor.AI.AttackEnterRange + 1, "Clear-floor control has enough approach distance");
            if (farthest <= actor.AI.AttackEnterRange + 1) yield break;
            obstacle = "Clear-floor control (no suitable obstacle pair)";
            NavMesh.CalculatePath(start, goal, navigationMask, path);
        }
        check(true, "Real obstacle, clear-floor control or installed connector route selected");
        var evidence = new List<object>();
        var originalOwner = actor.AI.SquadEncounterOwner; var originalAnchor = actor.AI.SquadEncounterAnchor;
        int originalPack = actor.AI.SpawnPackId; var originalHome = actor.AI.HomePosition;
        var originalPosition = actor.transform.position; var originalRotation = actor.transform.rotation;
        var testOwner = new GameObject("Owned obstacle approach encounter");
        var behaviours = new List<(Behaviour value, bool enabled)>();
        var collisions = new List<(Collider value, bool enabled)>();
        // Isolate static navigation from body blocking. Restore everyone before the input combat segment.
        foreach (var other in UnityEngine.Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None))
        {
            if (other == actor) continue;
            other.Movement?.StopMovement();
            foreach (var value in new Behaviour[] { other.AI, other.Movement, other.GetComponent<EnemyCrowdAgent>() })
                if (value) { behaviours.Add((value, value.enabled)); value.enabled = false; }
            foreach (var value in other.GetComponentsInChildren<Collider>())
                if (!value.isTrigger) { collisions.Add((value, value.enabled)); value.enabled = false; }
            other.GetComponent<EnemyMotor>()?.HoldPosition();
        }
        try
        {
        Place(player.transform, goal + Vector3.up * .12f, Quaternion.identity);
        actor.Movement.StopMovement();
        Place(actor.transform, start + Vector3.up * .06f, Quaternion.LookRotation(goal - start));
        actor.AI.SetHomePosition(start);
        actor.AI.SetSquadEncounter(testOwner, player.transform, 1);
        actor.AI.RequestAggro(player.transform);
        typeof(EnemyAIController).GetMethod("ChangeToChase", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(actor.AI, null);
        File.WriteAllText(Path.Combine(output, "obstacle-setup.json"), JsonConvert.SerializeObject(new {
            actor = actor.name, id = actor.GetInstanceID(), obstacle, borrowedActor, navigationMask, radius = actor.GetComponent<EnemyCrowdAgent>().PhysicalBodyRadius,
            start = new { start.x, start.y, start.z }, goal = new { goal.x, goal.y, goal.z } }, Formatting.Indented));
        double began = Time.realtimeSinceStartupAsDouble, next = 0;
        float best = Vector3.Distance(start, goal), travelled = 0;
        float arrivalDistance = Mathf.Max(3.5f, actor.AI.AttackEnterRange + .25f);
        float requiredTravel = Mathf.Max(.5f, Mathf.Min(5f, Vector3.Distance(start, goal) - arrivalDistance - .25f));
        bool clearArrival = false;
        bool stayedOnPlatform = true; int forcedMoves = 0;
        Vector3 previous = actor.transform.position;
        int maxSearches = 0;
        var navigation = typeof(EnemyAIController).Assembly.GetType("EnemyNavigationRoute");
        var counter = navigation.GetProperty("SearchesThisFrame", BindingFlags.NonPublic | BindingFlags.Static);
        while (Time.realtimeSinceStartupAsDouble - began < (restrictedPlatform ? 5 : 35) && !clearArrival)
        {
            if (restrictedPlatform)
            {
                // Exercise the production motor too: attack/knockback destinations cannot cross the connector.
                if (forcedMoves++ < 10) actor.GetComponent<EnemyMotor>().MoveToPosition(goal);
                stayedOnPlatform &= restrictedPlatform.ContainsLocal(restrictedPlatform.transform.InverseTransformPoint(actor.transform.position));
            }
            maxSearches = Mathf.Max(maxSearches, (int)counter.GetValue(null));
            var position = actor.transform.position;
            best = Mathf.Min(best, Vector3.Distance(position, goal));
            travelled += Vector3.Distance(previous, position); previous = position;
            if (Time.realtimeSinceStartupAsDouble >= next)
            {
                // This is a fixed-target navigation fixture; real free movement/combat follows separately.
                Place(player.transform, goal + Vector3.up * .12f, Quaternion.identity);
                clearArrival = !restrictedPlatform && Vector3.Distance(position, goal) <= arrivalDistance
                    && NavMesh.SamplePosition(position, out var current, 1.5f, navigationMask)
                    && !NavMesh.Raycast(current.position, goal, out _, navigationMask);
                evidence.Add(new { seconds = Time.realtimeSinceStartupAsDouble - began, x = position.x, y = position.y, z = position.z,
                    state = actor.AI.CurrentStateName, squad = actor.AI.SquadPursuitDebugModeName, distance = Vector3.Distance(position, goal), destination = typeof(EnemyAIController).GetField("navigationDestination", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(actor.AI).ToString() });
                next = Time.realtimeSinceStartupAsDouble + .25;
            }
            yield return null;
        }
        var debugFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        string Read(string name) => typeof(EnemyAIController).GetField(name, debugFlags)?.GetValue(actor.AI)?.ToString();
        var route = typeof(EnemyAIController).GetField("observedNavigation", debugFlags).GetValue(actor.AI);
        string Route(string name) => route?.GetType().GetField(name, debugFlags)?.GetValue(route)?.ToString();
        File.WriteAllText(Path.Combine(output, "obstacle-play.json"), JsonConvert.SerializeObject(new {
            obstacle, start = new { start.x, start.y, start.z }, goal = new { goal.x, goal.y, goal.z },
            corners = path.corners.Select(v => new { v.x, v.y, v.z }), travelled, closestDistance = best, maxSearches,
            arrivalDistance, clearArrival, requiredTravel, control, stayedOnPlatform, forcedMoves,
            passed = restrictedPlatform ? stayedOnPlatform && forcedMoves >= 10 : clearArrival && travelled > requiredTravel, samples = evidence,
            actor = actor.name, player = player.transform.position.ToString(), navActive = Read("navigationActive"),
            destination = Read("navigationDestination"), detour = Read("hasDetour"), detourCorner = Read("detourCorner"),
            blocker = Read("detourObstacle"), routeCount = Route("Count"), routeGoal = Route("Goal") }, Formatting.Indented));
        check(maxSearches <= 2, "Global navigation search cap remains two per frame");
        if (restrictedPlatform)
            check(stayedOnPlatform && forcedMoves >= 10, "Submission restriction: AI and forced motor movement cannot enter stairs");
        else check(clearArrival && travelled > requiredTravel, "Production AI clears a real obstacle and reaches its attack approach distance: " + best);
        }
        finally
        {
            if (actor)
            {
                actor.AI.SetSquadEncounter(originalOwner, originalAnchor, originalPack);
                actor.AI.SetHomePosition(originalHome);
                Place(actor.transform, originalPosition, originalRotation);
            }
            foreach (var row in collisions) if (row.value) row.value.enabled = row.enabled;
            foreach (var row in behaviours) if (row.value) row.value.enabled = row.enabled;
            UnityEngine.Object.Destroy(testOwner);
        }
    }
}
