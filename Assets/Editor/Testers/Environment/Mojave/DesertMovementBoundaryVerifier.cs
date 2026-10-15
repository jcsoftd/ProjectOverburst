using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.Mojave;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class DesertMovementBoundaryVerifier
{
    static float MovementRadius(float radius) => (float)typeof(DesertRunWorld).GetMethod("BoundaryRadius", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { radius });

    public static void VerifyEntry(RunWorldGate gate, Action<bool, string> check)
    {
        var roster = gate.Roster; int entry = roster.EntryAreaIndex;
        check(entry >= 0 && entry < roster.Areas.Count, "The prepared run records its entry area.");
        var area = roster.Areas[entry];
        check(area.Count == 0 && area.InitialCount == 0 && area.BasePopulation == 0
            && roster.Members.All(m => m.AreaIndex != entry) && gate.SpawnBudget.Fields[entry].InitialPlaced,
            "The entry tile retains its field but owns no natural or reserve members.");
        check(roster.Members.All(m => !MapCombatGeometry.Contains(area.Ground, m.PlannedPosition)), "All planned member positions exclude the actual entry floor.");
        var field = gate.SpawnBudget.Fields.First(f => f.FiniteArea.Count > f.FiniteArea.InitialCount);
        var member = roster.Members.Skip(field.FiniteArea.FirstMember).Take(field.FiniteArea.Count).First(m => !m.Initial);
        var path = new NavMeshPath(); var overlaps = new Collider[32];
        var body = MapCombatGeometry.BodyFor(member.Definition);
        Vector3 candidate = default; bool found = false;
        foreach (var point in area.Ground.Points)
            if (Vector3.Distance(point, gate.EntryPoint.position) > 5
                && MapCombatGeometry.TryRuntimePoint(field.FiniteArea.Ground, member.Definition, point, true, overlaps, path, out candidate)) { found = true; break; }
        check(found, "A physically valid entry-floor approach is available for the spawn rejection probe.");
        var slotType = typeof(MapMonsterField).GetNestedType("Slot", BindingFlags.NonPublic);
        var slot = Activator.CreateInstance(slotType, true);
        slotType.GetField("Member").SetValue(slot, member); slotType.GetField("Radius").SetValue(slot, body.Radius); slotType.GetField("Height").SetValue(slot, body.Height);
        int total = field.FiniteArea.Ground.Points.Count + 1;
        int cursor = (field.FiniteArea.Ground.Points.Count - member.Index * 17 % total + total) % total;
        slotType.GetField("Cursor").SetValue(slot, cursor);
        var approachesField = typeof(MapMonsterField).GetField("approaches", BindingFlags.Instance | BindingFlags.NonPublic);
        var prior = approachesField.GetValue(field);
        try
        {
            approachesField.SetValue(field, new List<Vector3> { candidate });
            var arguments = new object[] { slot, candidate + Vector3.forward * 100, true, Vector3.zero };
            bool produced = (bool)typeof(MapMonsterField).GetMethod("TryFinitePosition", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(field, arguments);
            check((int)slotType.GetField("Cursor").GetValue(slot) > cursor
                && (!produced || !MapCombatGeometry.Contains(area.Ground, (Vector3)arguments[3])),
                "The real spawn retry path rejects entry-floor approaches owned by a different field.");
        }
        finally { approachesField.SetValue(field, prior); }
    }

    public static IEnumerator VerifyPlay(RunWorldGate gate, string output, Action<bool, string> check)
    {
        var world = gate.GetComponent<DesertRunWorld>(); var terrain = gate.GetComponent<MojaveWorld>();
        var type = typeof(DesertRunWorld).GetNestedType("WalkBoundary", BindingFlags.NonPublic);
        var grid = typeof(DesertRunWorld).GetField("boundary", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world);
        var stand = (Func<Vector2, float, bool>)type.GetMethod("CanStand").CreateDelegate(typeof(Func<Vector2, float, bool>), grid);
        var actor = PlayerContext.Instance.CurrentActor; var motor = actor.GetComponent<OverburstCharacterMotor3D>();
        var movement = actor.GetComponent<PlayerMovement>(); var cc = motor.Controller;
        var position = actor.transform.position; var rotation = actor.transform.rotation;
        int excluded = cc.excludeLayers.value; bool enabled = movement.enabled;
        float radius = MovementRadius(cc.radius), foot = cc.center.y - cc.height * .5f;
        var probes = new List<(Vector3 start, Vector3 direction)>(); var records = new List<object>(); bool passed = false;
        var data = world.MinimapData; int[] dx = { 1, -1, 0, 0 }, dz = { 0, 0, 1, -1 }; byte[] flags = { 1, 2, 4, 8 };
        for (int z = 1; z < data.height - 1; z++) for (int x = 1; x < data.width - 1; x++)
        {
            int index = z * data.width + x; if (data.cells[index] != MinimapTerrainPixels.Ground) continue;
            for (int direction = 0; direction < 4; direction++)
            {
                if ((data.connections[index] & flags[direction]) != 0) continue;
                var outward = new Vector2(dx[direction], dz[direction]);
                var edge = data.bounds.position + new Vector2(x + .5f, z + .5f) * data.cellSize + outward * data.cellSize * .5f;
                var point = edge - outward * (radius + .1f);
                if (!stand(point, radius)) continue;
                var ground = terrain.Ground(point);
                if (!NavMesh.SamplePosition(ground, out var nav, .18f, NavMesh.AllAreas) || Vector3.Distance(nav.position, ground) > .2f) continue;
                probes.Add((ground + Vector3.up * (.04f - foot), new Vector3(outward.x, 0, outward.y)));
            }
        }
        check(probes.Count >= 8, "The real map has reachable configured-clearance boundary probes.");
        actor.Health.SetDamageDeathPrevention(world, true);
        try
        {
            movement.enabled = false; int enemy = LayerMask.NameToLayer("Enemy");
            if (enemy >= 0) cc.excludeLayers = excluded | 1 << enemy;
            check(typeof(DesertRunWorld).GetProperty("BoundaryWorld", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) == world,
                "The prepared current desert is registered before actor movement.");
            var movementType = typeof(DesertRunWorld).GetNestedType("BoundaryMovement", BindingFlags.NonPublic);
            var boundaryMovement = Activator.CreateInstance(movementType);
            var resolve = movementType.GetMethod("Resolve");
            var contactProbe = probes[0];
            var contact = (Vector3)resolve.Invoke(boundaryMovement, new object[] { contactProbe.start, contactProbe.start + contactProbe.direction * 100, cc.radius });
            var drift = contact + contactProbe.direction * .001f;
            var recovered = (Vector3)resolve.Invoke(boundaryMovement, new object[] { drift, drift, cc.radius + .000001f });
            check(Vector3.Distance(recovered, contact) < .002f, "Small body-radius changes retain the last safe position through physics contact jitter.");
            for (int probe = 0; probe < 8; probe++)
            {
                var item = probes[probe * probes.Count / 8];
                foreach (int mode in new[] { 0, 1, 2 })
                {
                    ActorTeleportUtility.TeleportSafely(actor.transform, item.start, rotation);
                    if (mode == 1) motor.BeginEvadeMotion();
                    for (int frame = 0; frame < 12; frame++)
                    {
                        motor.ProbeGround(.02f);
                        if (mode == 1) motor.MoveEvade(item.direction * 2);
                        else if (mode == 2) motor.MoveDirect(item.direction * 3);
                        else { var velocity = item.direction * 12; motor.Step(new MotorStepCommand { currentHorizontalVelocity = velocity, targetHorizontalVelocity = velocity, moveRate = 1000, airControl = 1 }, .02f); }
                        check(stand(new Vector2(actor.transform.position.x, actor.transform.position.z), radius), "Actual player footprint stays inside probe " + probe + " mode " + mode + " frame " + frame);
                        yield return null;
                    }
                    if (mode == 1) motor.EndEvadeMotion();
                    var finish = actor.transform.position;
                    records.Add(new { probe, mode, start = new[] { item.start.x, item.start.y, item.start.z }, finish = new[] { finish.x, finish.y, finish.z } });
                }
            }
            if (data.candidateId == "SQ15_01")
            {
                var start = terrain.Ground(new Vector2(47.2f, 31.6f)) + Vector3.up * (.04f - foot);
                var goal = new Vector2(23.6425629f, 20.70612f);
                ActorTeleportUtility.TeleportSafely(actor.transform, start, rotation);
                for (int frame = 0; frame < 300 && Vector2.Distance(new Vector2(actor.transform.position.x, actor.transform.position.z), goal) > .4f; frame++)
                {
                    var delta = goal - new Vector2(actor.transform.position.x, actor.transform.position.z);
                    var velocity = new Vector3(delta.x, 0, delta.y).normalized * 7;
                    motor.ProbeGround(.02f);
                    motor.Step(new MotorStepCommand { currentHorizontalVelocity = velocity, targetHorizontalVelocity = velocity, moveRate = 1000, airControl = 1 }, .02f);
                    check(stand(new Vector2(actor.transform.position.x, actor.transform.position.z), radius), "SQ15 final boss approach footprint remains valid " + frame);
                    yield return null;
                }
                check(Vector2.Distance(new Vector2(actor.transform.position.x, actor.transform.position.z), goal) <= .4f,
                    "Actual player crosses the recorded final SQ15 boss approach and reaches its goal.");
            }
            ActorTeleportUtility.TeleportSafely(actor.transform, position, rotation);
            for (int frame = 0; frame < 20; frame++) yield return null;
            var actors = new List<EnemyActor>(); foreach (var field in gate.SpawnBudget.Fields) field.CollectLivingActors(actors);
            var enemyActor = actors.Where(a => a.GetComponent<EnemyMotor>() && a.CollisionRoot && a.CollisionRoot.GetComponentsInChildren<CapsuleCollider>().Any(c => c.enabled))
                .OrderBy(a => a.CollisionRoot.GetComponentsInChildren<CapsuleCollider>().Where(c => c.enabled).Min(c => Mathf.Max(c.bounds.extents.x, c.bounds.extents.z))).First();
            var enemyMotor = enemyActor.GetComponent<EnemyMotor>(); var body = enemyActor.GetComponent<Rigidbody>();
            var capsule = enemyActor.CollisionRoot.GetComponentsInChildren<CapsuleCollider>().First(c => c.enabled);
            var oldPosition = enemyMotor.Position; var oldRotation = enemyMotor.Rotation;
            bool aiEnabled = enemyActor.AI.enabled, moveEnabled = enemyActor.Movement.enabled, kinematic = body.isKinematic;
            var velocityBefore = body.linearVelocity; var angularBefore = body.angularVelocity;
            float enemyRadius = MovementRadius(Mathf.Max(capsule.bounds.extents.x, capsule.bounds.extents.z));
            float enemyFoot = enemyActor.transform.position.y - capsule.bounds.min.y;
            try
            {
                enemyActor.AI.enabled = false; enemyActor.Movement.enabled = false; body.isKinematic = true;
                int visited = 0;
                for (int probe = 0; probe < probes.Count && visited < 4; probe++)
                {
                    var item = probes[probe]; var p = new Vector2(item.start.x, item.start.z);
                    if (!stand(p, enemyRadius)) continue;
                    ActorTeleportUtility.TeleportSafely(actor.transform, item.start - item.direction * 2, rotation);
                    enemyMotor.enabled = false;
                    enemyActor.transform.position = terrain.Ground(p) + Vector3.up * (enemyFoot + .04f); body.position = enemyActor.transform.position;
                    Physics.SyncTransforms();
                    enemyMotor.enabled = true;
                    enemyMotor.MoveToPosition(enemyMotor.Position + item.direction * 100);
                    check(stand(new Vector2(enemyMotor.Position.x, enemyMotor.Position.z), enemyRadius), "Actual pooled enemy forced movement respects the configured clearance boundary " + visited);
                    body.isKinematic = false; body.linearVelocity = item.direction * 100;
                    for (int frame = 0; frame < 4; frame++) yield return null;
                    var finish = enemyMotor.Position;
                    float currentRadius = Mathf.Max(capsule.bounds.extents.x, capsule.bounds.extents.z);
                    // Rigidbody contacts have a 1 mm correction deadband at the configured movement clearance.
                    bool inside = stand(new Vector2(finish.x, finish.z), MovementRadius(currentRadius) - .002f);
                    File.WriteAllText(Path.Combine(output, "enemy-boundary-" + visited + ".json"), JsonConvert.SerializeObject(new { inside, active = enemyActor.gameObject.activeInHierarchy,
                        motorEnabled = enemyMotor.enabled, body.isKinematic, radius = enemyRadius, currentRadius, skinPreserved = stand(new Vector2(finish.x, finish.z), MovementRadius(currentRadius) - .002f),
                        position = new[] { finish.x, finish.y, finish.z }, streamReturns = gate.SpawnBudget.StreamReturns, recoveries = gate.SpawnBudget.BoundaryRecoveries }, Formatting.Indented));
                    check(inside, "Actual enemy physics and LateUpdate correction retain the boundary " + visited);
                    body.isKinematic = true; visited++;
                }
                check(visited > 0, "Actual enemy body geometry was exercised at the boundary.");
            }
            finally
            {
                enemyMotor.enabled = false; body.position = oldPosition; body.rotation = oldRotation;
                enemyActor.transform.SetPositionAndRotation(oldPosition, oldRotation); body.isKinematic = kinematic;
                if (!kinematic) { body.linearVelocity = velocityBefore; body.angularVelocity = angularBefore; }
                enemyMotor.enabled = true; enemyActor.AI.enabled = aiEnabled; enemyActor.Movement.enabled = moveEnabled;
            }
            passed = true;
        }
        finally
        {
            motor.EndEvadeMotion(); cc.excludeLayers = excluded;
            ActorTeleportUtility.TeleportSafely(actor.transform, position, rotation); movement.enabled = enabled;
            actor.Health.SetDamageDeathPrevention(world, false);
            File.WriteAllText(Path.Combine(output, "desert-boundary-play.json"), JsonConvert.SerializeObject(new { status = passed ? "PASS" : "FAIL", records, restored = movement.enabled == enabled && cc.excludeLayers.value == excluded }, Formatting.Indented));
        }
    }

    public static string VerifyCatalog(string directory, string candidateId = null)
    {
        MojaveWorldBuilder.Guard();
        if (IsolatedSavePlayGuard.ActiveDirectory != "" || SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "") != ""
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY")))
            throw new InvalidOperationException("Idle real-account Editor required.");
        string output = Path.GetFullPath(directory);
        string allowed = Path.GetFullPath(Application.dataPath + "/../../개인파일/코덱스산출") + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output)) throw new ArgumentException("Fresh evidence directory required.");
        if (candidateId != null) MapTerrainCatalog.Resolve(candidateId);
        Directory.CreateDirectory(output);
        string Scenes() => JsonConvert.SerializeObject(Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i)).Select(s => new { s.path, s.isDirty, s.rootCount }));
        string before = Scenes(); int previews = EditorSceneManager.previewSceneCount;
        var rows = new List<object>(); var type = typeof(DesertRunWorld).GetNestedType("WalkBoundary", BindingFlags.NonPublic);
        try
        {
            foreach (var candidate in MapTerrainCatalog.DesertCandidates.Where(c => candidateId == null || c.id == candidateId))
            {
                var scene = EditorSceneManager.OpenScene(candidate.scene, OpenSceneMode.Additive);
                NavMeshData navigation = null; NavMeshDataInstance instance = default;
                try
                {
                    var world = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MojaveWorld>(true)).Single();
                    world.EnsureLayout(); var data = DesertMinimapData.Load(candidate, world);
                    var grid = Activator.CreateInstance(type, new object[] { data });
                    var stand = (Func<Vector2, float, bool>)type.GetMethod("CanStand").CreateDelegate(typeof(Func<Vector2, float, bool>), grid);
                    var sources = new List<NavMeshBuildSource>();
                    Physics.SyncTransforms();
                    NavMeshBuilder.CollectSources(world.generatedRoot, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
                    for (int i = 0; i < sources.Count; i++)
                    {
                        var source = sources[i];
                        if (source.shape != NavMeshBuildSourceShape.Mesh || !(source.sourceObject is Mesh mesh) || mesh.isReadable) continue;
                        source.shape = NavMeshBuildSourceShape.Box; source.transform *= Matrix4x4.Translate(mesh.bounds.center);
                        source.size = mesh.bounds.size; source.sourceObject = null; source.area = 1; sources[i] = source;
                    }
                    var bounds = new Bounds(world.surface.transform.position + world.surface.terrainData.size * .5f, world.surface.terrainData.size); bounds.Expand(4);
                    type.GetMethod("AppendNavigationSources").Invoke(grid, new object[] { sources, bounds, .2f });
                    var settings = NavMesh.GetSettingsByID(0); settings.overrideVoxelSize = true; settings.voxelSize = .2f;
                    settings.overrideTileSize = true; settings.tileSize = 128; settings.agentRadius = .4f; settings.agentHeight = 1.8f; settings.agentClimb = .6f;
                    navigation = new NavMeshData(settings.agentTypeID);
                    if (!NavMeshBuilder.UpdateNavMeshData(navigation, settings, sources, bounds)) throw new Exception("NavMesh build failed: " + candidate.id);
                    instance = NavMesh.AddNavMeshData(navigation);
                    File.WriteAllText(Path.Combine(output, candidate.id + "-build.json"), JsonConvert.SerializeObject(new { sources = sources.GroupBy(s => s.shape).Select(g => new { shape = g.Key.ToString(), count = g.Count() }), vertices = NavMesh.CalculateTriangulation().vertices.Length }, Formatting.Indented));
                    if (candidate.id == "SQ15_01")
                    {
                        var from = new Vector2(27.6363468f, 22.5430183f); var to = new Vector2(23.6425629f, 20.70612f);
                        var move = (Func<Vector2, Vector2, float, Vector2>)type.GetMethod("Move").CreateDelegate(typeof(Func<Vector2, Vector2, float, Vector2>), grid);
                        var resolved = move(from, to, .25f);
                        File.WriteAllText(Path.Combine(output, "SQ15-stall.json"), JsonConvert.SerializeObject(new { startValid = stand(from, .25f), targetValid = stand(to, .25f), resolved = new[] { resolved.x, resolved.y }, navBlocked = NavMesh.Raycast(world.Ground(from), world.Ground(to), out _, NavMesh.AllAreas) }, Formatting.Indented));
                    }
                    Vector3 Floor(Vector3 desired)
                    {
                        for (int ring = 0; ring <= 8; ring++) for (int i = 0; i < (ring == 0 ? 1 : 16); i++)
                        {
                            float angle = i * Mathf.PI / 8; var p = new Vector2(desired.x + Mathf.Cos(angle) * ring, desired.z + Mathf.Sin(angle) * ring);
                            if (!world.IsWalkable(p, .65f)) continue;
                            var ground = world.Ground(p);
                            if (NavMesh.SamplePosition(ground, out var hit, .6f, NavMesh.AllAreas) && Mathf.Abs(hit.position.y - ground.y) <= .35f
                                && stand(new Vector2(hit.position.x, hit.position.z), .45f)) return hit.position;
                        }
                        throw new Exception("No full-footprint floor: " + candidate.id + " " + desired);
                    }
                    var entry = Floor(DesertRunWorld.EntryFor(world)); var path = new NavMeshPath();
                    foreach (var place in world.layout.places)
                        if (!NavMesh.CalculatePath(entry, Floor(world.Ground(place.center)), NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                            throw new Exception("Required path missing: " + candidate.id + " " + place.name);
                    int outside = 0, seams = 0;
                    int stride = Mathf.Max(1, data.cells.Length / 256);
                    for (int i = 0; i < data.cells.Length; i += stride)
                    {
                        if (data.cells[i] == MinimapTerrainPixels.Ground) continue;
                        var p = data.bounds.position + new Vector2(i % data.width + .5f, i / data.width + .5f) * data.cellSize;
                        if (NavMesh.SamplePosition(world.Ground(p), out _, .05f, NavMesh.AllAreas)) throw new Exception("Navigation leaks into blocked cell: " + candidate.id + " " + i);
                        outside++;
                    }
                    foreach (var p in new[] { new Vector2(data.bounds.xMin - 1, data.bounds.center.y), new Vector2(data.bounds.xMax + 1, data.bounds.center.y),
                        new Vector2(data.bounds.center.x, data.bounds.yMin - 1), new Vector2(data.bounds.center.x, data.bounds.yMax + 1) })
                    {
                        if (NavMesh.SamplePosition(world.Ground(p), out _, .05f, NavMesh.AllAreas)) throw new Exception("Navigation leaks beyond minimap bounds: " + candidate.id);
                        outside++;
                    }
                    for (int z = 0; z < data.height; z++) for (int x = 0; x < data.width; x++)
                    {
                        int i = z * data.width + x; if (data.cells[i] != MinimapTerrainPixels.Ground) continue;
                        foreach (int direction in new[] { 0, 1 })
                        {
                            int adjacent = i + (direction == 0 ? 1 : data.width);
                            if ((direction == 0 ? x + 1 >= data.width : z + 1 >= data.height) || data.cells[adjacent] != MinimapTerrainPixels.Ground
                                || (data.connections[i] & (direction == 0 ? 1 : 4)) != 0) continue;
                            var p = data.bounds.position + new Vector2(x + .5f, z + .5f) * data.cellSize;
                            var delta = (direction == 0 ? Vector2.right : Vector2.up) * data.cellSize;
                            if (NavMesh.SamplePosition(world.Ground(p), out var from, .05f, NavMesh.AllAreas)
                                && NavMesh.SamplePosition(world.Ground(p + delta), out var to, .05f, NavMesh.AllAreas)
                                && !NavMesh.Raycast(from.position, to.position, out _, NavMesh.AllAreas)) throw new Exception("Closed seam is traversable: " + candidate.id);
                            seams++;
                        }
                    }
                    var row = new { candidate.id, areas = world.layout.places.Count, outside, seams, sources = sources.Count };
                    rows.Add(row); File.WriteAllText(Path.Combine(output, candidate.id + ".json"), JsonConvert.SerializeObject(row, Formatting.Indented));
                }
                finally { if (instance.valid) instance.Remove(); if (navigation) UnityEngine.Object.DestroyImmediate(navigation); EditorSceneManager.CloseScene(scene, true); }
            }
            File.WriteAllText(Path.Combine(output, "result.json"), JsonConvert.SerializeObject(new { status = "PASS", rows, scope = "All saved candidate navigation builds, required paths, sampled blocked cells, exterior and closed seams; motor traversal tested separately in Play" }, Formatting.Indented));
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "result.json"), JsonConvert.SerializeObject(new { status = "FAIL", rows, error = error.ToString() }, Formatting.Indented)); throw; }
        finally
        {
            bool preserved = before == Scenes() && previews == EditorSceneManager.previewSceneCount;
            File.WriteAllText(Path.Combine(output, "return.json"), JsonConvert.SerializeObject(new { status = preserved ? "PASS" : "FAIL", preserved, previews, previewAfter = EditorSceneManager.previewSceneCount }, Formatting.Indented));
            if (!preserved) throw new Exception("Catalog verification did not preserve Editor scenes.");
        }
        return Path.Combine(output, "result.json");
    }

    public static string Run(string directory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle EditMode required.");
        string output = Path.GetFullPath(directory);
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output)) throw new ArgumentException("Fresh evidence directory required.");
        Directory.CreateDirectory(output);
        var checks = new List<string>(); var owned = new List<DesertMinimapData>();
        long allocated = -1;
        void Check(bool valid, string label) { if (!valid) throw new Exception(label); checks.Add(label); }
        var type = typeof(DesertRunWorld).GetNestedType("WalkBoundary", BindingFlags.NonPublic);
        DesertMinimapData Map(int width, int height, Func<int, int, bool> ground, Vector2 origin)
        {
            var data = ScriptableObject.CreateInstance<DesertMinimapData>(); owned.Add(data);
            data.width = width; data.height = height; data.cellSize = 1; data.bounds = new Rect(origin, new Vector2(width, height));
            data.cells = new byte[width * height]; data.connections = new byte[width * height];
            for (int z = 0; z < height; z++) for (int x = 0; x < width; x++) if (ground(x, z)) data.cells[z * width + x] = MinimapTerrainPixels.Ground;
            for (int z = 0; z < height; z++) for (int x = 0; x < width; x++)
            {
                int i = z * width + x; if (data.cells[i] == 0) continue;
                if (x + 1 < width && data.cells[i + 1] != 0) { data.connections[i] |= 1; data.connections[i + 1] |= 2; }
                if (z + 1 < height && data.cells[i + width] != 0) { data.connections[i] |= 4; data.connections[i + width] |= 8; }
            }
            return data;
        }
        object Grid(DesertMinimapData data) => Activator.CreateInstance(type, new object[] { data });
        Func<Vector2, float, bool> Stand(object grid) => (Func<Vector2, float, bool>)type.GetMethod("CanStand").CreateDelegate(typeof(Func<Vector2, float, bool>), grid);
        Func<Vector2, Vector2, float, Vector2> Move(object grid) => (Func<Vector2, Vector2, float, Vector2>)type.GetMethod("Move").CreateDelegate(typeof(Func<Vector2, Vector2, float, Vector2>), grid);
        try
        {
            var rectangle = Map(7, 7, (x, z) => x > 0 && x < 6 && z > 0 && z < 6, new Vector2(-3.5f, -3.5f));
            var grid = Grid(rectangle); var stand = Stand(grid); var move = Move(grid);
            Check(stand(Vector2.zero, .4f) && !stand(new Vector2(-3, 0), .4f) && !stand(new Vector2(2.2f, 0), .4f), "Whole footprint and negative world coordinates are checked.");
            Check(!stand(new Vector2(float.NaN, 0), .4f) && !stand(Vector2.zero, -1), "Invalid coordinates and body radii are rejected.");
            foreach (var direction in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
            {
                var result = move(Vector2.zero, direction * 100, .4f);
                Check(stand(result, .4f) && Vector2.Distance(result, direction * 2.1f) < .0002f, "Long displacement stops at the " + direction + " boundary.");
            }
            var slide = move(Vector2.zero, new Vector2(8, 1.3f), .4f);
            Check(stand(slide, .4f) && Vector2.Distance(slide, new Vector2(2.1f, 1.3f)) < .0002f, "Blocked movement slides along the edge.");
            var corner = move(Vector2.zero, new Vector2(8, 8), .4f);
            Check(stand(corner, .4f) && corner.x <= 2.1002f && corner.y <= 2.1002f, "A dash cannot pass a closed corner.");
            var hole = Map(7, 7, (x, z) => x > 0 && x < 6 && z > 0 && z < 6 && !(x == 3 && z == 3), new Vector2(-3.5f, -3.5f));
            var holeGrid = Grid(hole); var aroundHole = Move(holeGrid)(new Vector2(-1.5f, 0), new Vector2(1.5f, 0), .4f);
            Check(Stand(holeGrid)(aroundHole, .4f) && Mathf.Abs(aroundHole.x + .9f) < .0002f, "Movement cannot jump over a one-cell hole.");
            var holeStand = Stand(holeGrid); var holeMove = Move(holeGrid); var random = new System.Random(73130);
            for (int trial = 0; trial < 300; trial++)
            {
                var start = new Vector2((float)random.NextDouble() * 4 - 2, (float)random.NextDouble() * 4 - 2);
                if (!holeStand(start, .4f)) continue;
                var goal = new Vector2((float)random.NextDouble() * 12 - 6, (float)random.NextDouble() * 12 - 6);
                var result = holeMove(start, goal, .4f);
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(start, result) / .02f));
                for (int step = 0; step <= steps; step++)
                    if (!holeStand(Vector2.Lerp(start, result, step / (float)steps), .4f))
                        throw new Exception("Motor displacement crosses a hole: " + start + " -> " + goal + " resolved " + result);
            }
            Check(true, "Seeded actual motor displacement segments remain inside the full footprint boundary.");
            var touching = Map(5, 5, (x, z) => x == 2 && z == 2 || x == 3 && z == 3, new Vector2(-3.5f, -3.5f));
            var touchGrid = Grid(touching); var touch = Move(touchGrid)(new Vector2(-1, -1), Vector2.zero, .15f);
            Check(Stand(touchGrid)(touch, .15f) && touch.x <= -.6498f && touch.y <= -.6498f, "Diagonal touching cells do not create a passage.");
            var seam = Map(7, 7, (x, z) => x > 0 && x < 6 && z > 0 && z < 6, new Vector2(-3.5f, -3.5f));
            for (int z = 1; z < 6; z++) { seam.connections[z * 7 + 3] &= 14; seam.connections[z * 7 + 4] &= 13; }
            var seamGrid = Grid(seam); var stopped = Move(seamGrid)(new Vector2(-1, 0), new Vector2(1, 0), .2f);
            Check(Mathf.Abs(stopped.x - .3f) < .0002f && Stand(seamGrid)(stopped, .2f), "Disconnected adjacent floor cells remain a closed seam.");
            var corridor = Map(64, 5, (x, z) => x > 0 && x < 63 && z == 2, Vector2.zero);
            var corridorGrid = Grid(corridor); var corridorStand = Stand(corridorGrid); var corridorMove = Move(corridorGrid);
            var end = corridorMove(new Vector2(1.5f, 2.5f), new Vector2(100, 2.5f), .2f);
            Check(corridorStand(end, .2f) && Mathf.Abs(end.x - 62.8f) < .0003f, "Long connected corridors remain traversable and bounded.");
            Check(!corridorStand(new Vector2(20, 2.5f), .6f), "A body wider than the corridor cannot stand there.");
            var invalid = Map(5, 5, (x, z) => x > 0 && x < 4 && z > 0 && z < 4, Vector2.zero);
            invalid.connections[2 * 5 + 2] &= 13;
            bool rejected = false;
            try { Grid(invalid); } catch (TargetInvocationException error) { rejected = error.InnerException is InvalidOperationException; }
            Check(rejected, "Asymmetric topology is rejected before movement.");
            var sources = new List<NavMeshBuildSource>();
            type.GetMethod("AppendNavigationSources").Invoke(grid, new object[] { sources, new Bounds(Vector3.zero, new Vector3(10, 4, 10)), .2f });
            Check(sources.Count > 4 && sources.TrueForAll(s => s.shape == NavMeshBuildSourceShape.ModifierBox && s.area == NavMesh.GetAreaFromName("Not Walkable")), "Navigation input covers empty cells and terrain beyond the minimap bounds.");
            for (int i = 0; i < 20; i++) move(Vector2.zero, new Vector2(8, 1.3f), .4f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++) move(Vector2.zero, new Vector2(8, 1.3f), .4f);
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allocated == 0, "Repeated native movement queries allocate no managed memory.");
            File.WriteAllText(Path.Combine(output, "result.json"), JsonConvert.SerializeObject(new { status = "PASS", checks, allocated, scope = "Native synthetic topology and movement geometry; actual motor/scene/NavMesh tested separately in Play" }, Formatting.Indented));
        }
        catch (Exception error)
        { File.WriteAllText(Path.Combine(output, "result.json"), JsonConvert.SerializeObject(new { status = "FAIL", checks, allocated, error = error.ToString() }, Formatting.Indented)); throw; }
        finally { foreach (var data in owned) UnityEngine.Object.DestroyImmediate(data); }
        return Path.Combine(output, "result.json");
    }
}
