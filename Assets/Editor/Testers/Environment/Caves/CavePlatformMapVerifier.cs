using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using Overburst.Caves;
using Newtonsoft.Json;
using Object = UnityEngine.Object;

public static class CavePlatformMapVerifier
{
    static string Output => CavePlatformMapBuilder.Output + "/Data";
    static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static string Hash(string path)
    { using (var hash = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    public static object Verify(string scenePath)
    {
        CavePlatformMapBuilder.Guard(); Directory.CreateDirectory(Output);
        var library = AssetDatabase.LoadAssetAtPath<CavePlatformLibrary>(CavePlatformMapBuilder.LibraryPath); var checks = new List<object>();
        Assert(library && library.platforms.Length == 9, "Expected the nine user-edited platform types.");
        int angleSamples = 0;
        foreach (var platform in library.platforms)
        {
            Assert(platform.source && platform.prefab, "Missing source or generated prefab.");
            foreach (var t in platform.prefab.GetComponentsInChildren<Transform>(true)) Assert(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0, "Missing script in " + platform.prefab.name);
            foreach (var region in platform.regions)
            {
                Assert(region.angles.Length == region.anchors.Length && region.angles.Length > 0 && region.MaxAngle - region.MinAngle < 170, "Invalid angular interval.");
                int usable = 0;
                for (int n = 0; n <= 20; n++)
                {
                    float angle = Mathf.Lerp(region.MinAngle, region.MaxAngle, n / 20f);
                    if (!platform.TryAnchor(region, angle, out var anchor)) continue;
                    var radial = anchor - region.pivot; radial.y = 0;
                    float actual = Mathf.Atan2(radial.z, radial.x) * Mathf.Rad2Deg;
                    Assert(Mathf.Abs(Mathf.DeltaAngle(angle, actual)) < .01f, "Anchor left the common pivot ray."); usable++; angleSamples++;
                }
                Assert(usable > 0, platform.source.name + " has no usable angle in " + region.name);
            }

        }
        for (int seed = 19080; seed < 19083; seed++)
        {
            var a = CavePlatformMapBuilder.Plan(library, 9, seed); var b = CavePlatformMapBuilder.Plan(library, 9, seed);
            Assert(a.rooms.Count == 9 && a.links.Count == 8, "Layout is incomplete.");
            Assert(a.rooms.Zip(b.rooms, (x, y) => x.platform == y.platform && x.position == y.position && x.yaw == y.yaw).All(v => v), "Seed is not deterministic.");
            foreach (var link in a.links)
            {
                var ra = library.platforms[a.rooms[link.a].platform].regions[link.portA]; var rb = library.platforms[a.rooms[link.b].platform].regions[link.portB];
                Assert(link.angleA >= ra.MinAngle && link.angleA <= ra.MaxAngle && link.angleB >= rb.MinAngle && link.angleB <= rb.MaxAngle, "Yaw exceeded a fan.");
                var delta = link.end - link.start;
                Assert(Mathf.Abs(Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg - link.pitch) < .001f, "Height was not derived from bridge pitch.");
                if (link.stone)
                {
                    Assert(link.stairCount >= 1 && link.stairCount <= Mathf.Min(ra.maxStairCount, rb.maxStairCount), "Stair count exceeded the authored allowance.");
                    var pieces = CavePlatformMapBuilder.ConnectorPieces(library, link.stairCount);
                    Assert(Mathf.Abs(new Vector2(delta.x, delta.z).magnitude - pieces.Sum(p => new Vector2(p.Span.x, p.Span.z).magnitude)) < .002f, "Stair run was stretched.");
                    Assert(Mathf.Abs(Mathf.Abs(delta.y) - pieces.Sum(p => Mathf.Abs(p.Span.y))) < .002f, "Stair height changed.");
                }
                else
                {
                    Assert(Mathf.Abs(link.pitch - ra.pitch) <= ra.pitchTolerance + .001f && Mathf.Abs(-link.pitch - rb.pitch) <= rb.pitchTolerance + .001f, "Bridge pitch exceeded either end's tolerance.");
                    Assert(Mathf.Abs(delta.magnitude - library.timber.Span.magnitude) < .002f, "Bridge length was stretched.");
                }
            }
        }
        checks.Add(new { name = "Source preservation, continuous angles, deterministic layout", pass = true, angleSamples, seeds = 3 });
        var active = SceneManager.GetActiveScene(); var stage = PrefabStageUtility.GetCurrentPrefabStage()?.assetPath; bool dirty = active.isDirty; Scene scene = default;
        try
        {
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive); var world = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<CaveWorld>()).Single();
            Assert(world.authoredLayout, "The map uses the legacy generator.");
            Assert(world.courts.Select(c => Mathf.RoundToInt(c.center.y * 10)).Distinct().Count() > 1, "No elevation variation.");
            int connectedRooms = 0;
            for (int i = 0; i < world.courts.Count; i++)
            {
                var path = new NavMeshPath(); if (NavMesh.CalculatePath(world.courts[0].center, world.courts[i].center, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) connectedRooms++;
            }
            var connections = world.generatedRoot.GetComponentsInChildren<CaveRigidConnection>();
            Assert(connections.Length == world.courts.Count - 1, "Missing rigid connector models.");
            int sourceMeshChecks = 0;
            foreach (var connection in connections)
            {
                Vector3 cursor = connection.start;
                foreach (var piece in connection.pieces)
                {
                    Assert(Vector3.Distance(cursor, piece.instance.TransformPoint(piece.localStart)) < .01f, "Connector joints do not meet.");
                    Assert(Vector3.Distance(piece.instance.localScale, piece.source.transform.localScale) < .0001f, "Source connector scale changed.");
                    var sourceMeshes = piece.source.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).ToArray();
                    var actualMeshes = piece.instance.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).ToArray();
                    Assert(sourceMeshes.SequenceEqual(actualMeshes), "Connector meshes were rebuilt or deformed."); sourceMeshChecks += actualMeshes.Length;
                    if (connection.stairCount > 0) Assert(Vector3.Dot(piece.instance.up, Vector3.up) > .9999f, "Stair tilted and changed its height.");
                    cursor = piece.instance.TransformPoint(piece.localEnd);
                }
                Assert(Vector3.Distance(cursor, connection.end) < .01f, "Fixed connector endpoint mismatch.");
            }
            checks.Add(new { name = "Saved rigid models, source mesh identity, fixed scale and endpoint contact", pass = true, rooms = world.courts.Count, connections = connections.Length, sourceMeshChecks });
            var result = new { status = "PASS_RIGID_CONNECTION_NATIVE", scenePath, checks, connectedRooms, reviewContacts = connections.Count(c => c.needsReview), traversal = "DEFERRED_BY_USER; ProjectOverburst character handles small steps" }; File.WriteAllText(Output + "/" + Path.GetFileNameWithoutExtension(scenePath) + "_verification.json", JsonConvert.SerializeObject(result, Formatting.Indented)); return result;
        }
        finally
        {
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(active); if (stage != null && PrefabStageUtility.GetCurrentPrefabStage() == null) PrefabStageUtility.OpenPrefab(stage);
            Assert(active.isDirty == dirty, "User scene dirty state changed.");
        }
    }
}
