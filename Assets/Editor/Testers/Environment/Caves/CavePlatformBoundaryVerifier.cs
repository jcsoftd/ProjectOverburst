using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Caves;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CavePlatformBoundaryVerifier
{
    public static void AddFixtureBoundaries(CaveWorld world)
    {
        var root = world.generatedRoot;
        foreach (string name in new[] { "Start platform", "Far platform", "High platform", "Disconnected platform" })
        {
            var go = root.Find(name).gameObject; var box = go.GetComponent<BoxCollider>(); var h = box.size * .5f;
            var boundary = go.AddComponent<CavePlatformBoundary>();
            var points = new[] { new Vector3(-h.x, h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z) };
            boundary.loops = new[] { new CavePlatformBoundary.Loop { points = points } };
            boundary.floorTriangles = new[] { points[0], points[1], points[2], points[0], points[2], points[3] };
        }
        var a = root.Find("Start platform").GetComponent<CavePlatformBoundary>();
        var b = root.Find("Far platform").GetComponent<CavePlatformBoundary>();
        var c = root.Find("High platform").GetComponent<CavePlatformBoundary>();
        var bridge = root.Find("Bridge").gameObject.AddComponent<CaveRigidConnection>();
        bridge.boundaryA = a; bridge.boundaryB = b; bridge.walkWidth = 1.6f;
        bridge.start = a.transform.position + new Vector3(3, .5f, 0); bridge.end = b.transform.position + new Vector3(-3, .5f, 0);
        bridge.pieces = new[] { new CaveRigidConnection.Piece { instance = bridge.transform } };
        var stairs = root.Find("Stair 0").gameObject.AddComponent<CaveRigidConnection>();
        stairs.boundaryA = b; stairs.boundaryB = c; stairs.walkWidth = 2;
        stairs.start = b.transform.position + new Vector3(3, .5f, 0); stairs.end = c.transform.position + new Vector3(-3, .5f, 0);
        stairs.pieces = Enumerable.Range(0, 3).Select(i => new CaveRigidConnection.Piece { instance = root.Find("Stair " + i) }).ToArray();
    }

    static float XZ(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    public static object VerifyWorld(CaveWorld world)
    {
        if (!world.generatedRoot) throw new InvalidOperationException("Generate a cave in Play first; entry scenes intentionally contain no saved layout.");
        CaveFallProtection.Register(world);
        int routes = 0, closures = 0, rims = 0, absentPorts = 0;
        var failures = new List<string>();
        var boundaries = world.generatedRoot.GetComponentsInChildren<CavePlatformBoundary>();
        var connectors = world.generatedRoot.GetComponentsInChildren<CaveRigidConnection>();
        Require(boundaries.Length == world.courts.Count, "Each court must use an authored boundary");
        foreach (var c in connectors)
        {
            Require(c.BoundaryOpen, "Unmapped or missing connector " + c.name);
            var d = c.end - c.start; d.y = 0; d.Normalize(); var right = new Vector3(-d.z, 0, d.x);
            foreach (bool reverse in new[] { false, true })
                foreach (float fraction in new[] { -.25f, 0, .25f })
                {
                    Vector3 a = (reverse ? c.end + d * 1.2f : c.start - d * 1.2f) + right * c.walkWidth * fraction;
                    Vector3 b = (reverse ? c.start - d * 1.2f : c.end + d * 1.2f) + right * c.walkWidth * fraction;
                    var guard = new CaveFallProtection.Movement(); var at = a;
                    int count = Mathf.CeilToInt(Vector3.Distance(a, b) / .15f);
                    for (int step = 0; step <= count; step++)
                    {
                        var target = Vector3.Lerp(a, b, step / (float)count);
                        at = guard.Resolve(at, target, .4f);
                        if (!guard.Active || XZ(at, target) > .3f) { failures.Add(c.name + $" route reverse={reverse}, side={fraction}, step={step}, error={XZ(at, target):F2}"); break; }
                    }
                    routes++;
                }
            foreach (bool reverse in new[] { false, true })
            {
                var start = reverse ? c.end + d * 1.2f : c.start - d * 1.2f;
                var target = (c.start + c.end) * .5f;
                var piece = c.pieces[0].instance.gameObject; bool active = piece.activeSelf;
                try
                {
                    piece.SetActive(false);
                    var guard = new CaveFallProtection.Movement(); var at = guard.Resolve(start, target, .4f);
                    if (!guard.Active || XZ(at, target) < 1) failures.Add(c.name + " removed piece did not close gate");
                    if (!CaveFallProtection.TryDropLanding(start, target, .05f, out var drop) || XZ(drop, target) < 1) failures.Add(c.name + " loot escaped closed gate");
                    closures++;
                }
                finally { piece.SetActive(active); }
            }
        }
        foreach (var boundary in boundaries)
        {
            foreach (var loop in boundary.Data.loops)
                for (int i = 0; i < loop.points.Length; i += 4)
                {
                    var a = loop.points[i]; var b = loop.points[(i + 1) % loop.points.Length]; var mid = (a + b) * .5f;
                    var along = b - a; along.y = 0; along.Normalize(); var inward = new Vector3(-along.z, 0, along.x);
                    if (!boundary.ContainsLocal(mid + inward * .8f)) inward = -inward;
                    var from = boundary.transform.TransformPoint(mid + inward * .8f);
                    var target = boundary.transform.TransformPoint(mid - inward * 1.8f);
                    if (!boundary.ContainsLocal(mid + inward * .8f) || boundary.ContainsLocal(mid - inward * 1.8f)) continue;
                    if (connectors.Any(c => c.Sample(target, 10, 10, out _))) continue;
                    if (boundaries.Any(other => other != boundary && other.Sample(target, 3, 3, out _))) continue;
                    var guard = new CaveFallProtection.Movement(); var result = guard.Resolve(from, target, .4f);
                    if (!guard.Active || XZ(result, target) < .05f) failures.Add(boundary.name + " rim " + i + " escaped active=" + guard.Active + " error=" + XZ(result,target) + " from=" + from + " target=" + target + " result=" + result);
                    rims++;
                }
            for (int port = 0; port < boundary.Data.ports.Length; port++)
            {
                if (connectors.Any(c => (c.boundaryA == boundary && c.portA == port) || (c.boundaryB == boundary && c.portB == port))) continue;
                var p = boundary.Data.ports[port]; var d = new Vector3(Mathf.Cos(p.heading * Mathf.Deg2Rad), 0, Mathf.Sin(p.heading * Mathf.Deg2Rad));
                var from = boundary.transform.TransformPoint(p.Anchor(p.heading) - d * 2);
                var target = boundary.transform.TransformPoint(p.Anchor(p.heading) + d * (p.deckLength * .65f));
                if (!boundary.ContainsLocal(boundary.transform.InverseTransformPoint(from))) continue;
                var guard = new CaveFallProtection.Movement(); var result = guard.Resolve(from, target, .4f);
                if (!guard.Active || XZ(result, target) < .5f) failures.Add(boundary.name + " unused port " + port + " escaped");
                absentPorts++;
            }
        }
        var moving = new CaveFallProtection.Movement(); var spawn = world.courts[0].center;
        moving.Resolve(spawn, spawn, .4f);
        var watch = System.Diagnostics.Stopwatch.StartNew(); long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) moving.Resolve(spawn, spawn + Vector3.right * ((i & 1) * .05f), .4f);
        long allocations = GC.GetAllocatedBytesForCurrentThread() - before; watch.Stop();
        Require(allocations == 0, "Steady movement must not allocate");
        return new { status = failures.Count == 0 ? "PASS" : "FAIL", scene = world.gameObject.scene.path, routes, closures, rims, absentPorts, move1000Milliseconds = watch.Elapsed.TotalMilliseconds, allocations, failures };
    }

    public static object VerifySaved(string directory)
    {
        CavePlatformMapBuilder.Guard(); Directory.CreateDirectory(directory);
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        var stage = PrefabStageUtility.GetCurrentPrefabStage()?.assetPath;
        if (stage != null) StageUtility.GoToMainStage();
        var results = new List<object>();
        try
        {
            foreach (int count in new[] { 9, 12, 15 })
            {
                var scene = EditorSceneManager.OpenScene(CavePlatformMapBuilder.SceneRoot + "/Caves_Run_" + count + ".unity", OpenSceneMode.Additive);
                CaveWorld world = null;
                try
                {
                    world = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CaveWorld>()).Single();
                    results.Add(VerifyWorld(world));
                }
                finally { if (world) CaveFallProtection.Unregister(world); EditorSceneManager.CloseScene(scene, true); }
            }
        }
        finally
        {
            SceneManager.SetActiveScene(original);
            if (stage != null) PrefabStageUtility.OpenPrefab(stage);
            Require(original.isDirty == dirty, "User scene dirty state changed");
        }
        File.WriteAllText(Path.Combine(directory, "boundary-native.json"), JsonConvert.SerializeObject(results, Formatting.Indented));
        return results;
    }
}
