using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Overburst.Caves;

public static class CaveDetailDressingVerifier
{
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static string Key(Transform t)
    {
        string key = t.name + "[" + t.GetSiblingIndex() + "]";
        return t.parent ? Key(t.parent) + "/" + key : key;
    }
    static string V(Vector3 v) => $"{v.x:R},{v.y:R},{v.z:R}";
    static Dictionary<string, string> OriginalObjects(Scene scene)
    {
        return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
            .Where(t => !Key(t).Contains(CaveDetailDressingBuilder.RootName)).ToDictionary(Key, t =>
            {
                string state = V(t.localPosition) + "|" + t.localRotation + "|" + V(t.localScale) + "|" + t.gameObject.activeSelf;
                var mesh = t.GetComponent<MeshFilter>(); if (mesh) state += "|" + GlobalObjectId.GetGlobalObjectIdSlow(mesh.sharedMesh);
                var renderer = t.GetComponent<Renderer>(); if (renderer) state += "|" + renderer.enabled + "|" + string.Join(",", renderer.sharedMaterials.Select(m => GlobalObjectId.GetGlobalObjectIdSlow(m).ToString()));
                foreach (var collider in t.GetComponents<Collider>()) state += "|" + collider.GetType().Name + collider.enabled;
                return state;
            });
    }
    public static object Verify(string sourcePath, string decoratedPath)
    {
        CavePlatformMapBuilder.Guard();
        Require(!IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory), "Another session owns the account.");
        var active = SceneManager.GetActiveScene(); bool dirty = active.isDirty; Scene source = default, decorated = default, probes = default;
        try
        {
            source = EditorSceneManager.OpenPreviewScene(sourcePath); decorated = EditorSceneManager.OpenPreviewScene(decoratedPath);
            var before = OriginalObjects(source); var after = OriginalObjects(decorated);
            Require(before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out var state) && state == pair.Value), "An original object changed.");
            var world = decorated.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<CaveWorld>()).Single();
            var original = source.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<CaveWorld>()).Single();
            Require(world.courts.Count == original.courts.Count && world.passages.Count == original.passages.Count, "Layout changed.");
            var terrain = world.GetComponentInChildren<Terrain>();
            Require(terrain.terrainData == original.GetComponentInChildren<Terrain>().terrainData, "Original terrain data changed.");
            var details = world.transform.Find(CaveDetailDressingBuilder.RootName);
            Require(details, "Details are missing after reopening.");
            Require(details.GetComponentsInChildren<Collider>().All(c => !c.enabled), "Decoration blocks movement.");
            foreach (var t in details.GetComponentsInChildren<Transform>()) Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0, "Missing script in details.");
            probes = EditorSceneManager.NewPreviewScene(); var physics = probes.GetPhysicsScene();
            foreach (var f in world.GetComponentsInChildren<MeshFilter>().Where(f => !f.transform.IsChildOf(details)))
            {
                string model = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(f.sharedMesh));
                if (!f.sharedMesh || !(model.StartsWith("Rock") || model.StartsWith("Stal") || model.StartsWith("Platform") || model.StartsWith("Ruins"))) continue;
                if (!f.TryGetComponent<Renderer>(out var renderer) || !renderer.enabled) continue;
                var g = new GameObject("Verification surface") {hideFlags = HideFlags.HideAndDontSave}; SceneManager.MoveGameObjectToScene(g, probes);
                g.transform.SetPositionAndRotation(f.transform.position, f.transform.rotation); g.transform.localScale = f.transform.lossyScale; g.AddComponent<MeshCollider>().sharedMesh = f.sharedMesh;
            }
            Physics.SyncTransforms(); int anchors = 0;
            var directions = new[] {Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back};
            foreach (var web in details.GetChild(0).GetComponentsInChildren<MeshFilter>())
            {
                Require(EditorUtility.IsPersistent(web.sharedMesh), "Web mesh was not saved.");
                int contacts = 0;
                foreach (var v in web.sharedMesh.vertices.Select(web.transform.TransformPoint))
                {
                    Require(CaveDetailDressingBuilder.ClearPassages(world, v, .8f), "Web enters a traversable passage.");
                    Require(v.y >= terrain.SampleHeight(v) + terrain.transform.position.y, "Web is below terrain.");
                    if (directions.Any(d => physics.Raycast(v + d * .15f, -d, out _, .3f))) contacts++;
                }
                Require(contacts >= 3, web.name + " has fewer than three physical contacts: " + contacts); anchors += contacts;
                Require(web.sharedMesh.vertices.All(v => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)), "Invalid geometry.");
            }
            int eggCount = 0;
            foreach (var renderer in details.GetChild(1).GetComponentsInChildren<Renderer>())
            {
                var b = renderer.bounds; var p = b.center; float floor = terrain.SampleHeight(p) + terrain.transform.position.y;
                Require(Mathf.Abs(b.min.y - floor) < .25f, "An egg is floating or buried: " + renderer.name);
                Require(CaveDetailDressingBuilder.ClearPassages(world, p, 1), "Egg blocks a passage."); eggCount++;
            }
            int particles = 0; bool motion = false;
            foreach (var ps in details.GetChild(2).GetComponentsInChildren<ParticleSystem>())
            {
                Require(ps.main.playOnAwake && ps.main.loop && ps.main.prewarm, "Mist does not start and loop.");
                Require(!ps.useAutoRandomSeed, "Mist is not seeded.");
                var buffer = new ParticleSystem.Particle[ps.main.maxParticles]; ps.Simulate(12, true, true, true); int count = ps.GetParticles(buffer);
                Require(count > 0, "Mist emits no particles."); var first = buffer[0].position; uint seed = buffer[0].randomSeed;
                ps.Simulate(1, true, false, true); count = ps.GetParticles(buffer);
                motion |= buffer.Take(count).Any(p => p.randomSeed == seed && Vector3.Distance(p.position, first) > .01f);
                Require(buffer.Take(count).All(p => Mathf.Abs(p.position.y) <= .25f), "Mist rises above its low horizontal layer."); particles += count;
            }
            Require(motion, "Mist does not move.");
            bool refused = false; try { CaveDetailDressingBuilder.Apply(world); } catch (InvalidOperationException e) { refused = e.Message.Contains("already has final details"); }
            Require(refused, "Repeated dressing stacked duplicates.");
            var result = new {status = "PASS", sourcePath, decoratedPath, originalObjectsPreserved = before.Count, rooms = world.courts.Count, connections = world.passages.Count,
                webs = details.GetChild(0).childCount, physicalWebContacts = anchors, eggClusters = details.GetChild(1).childCount, eggs = eggCount, mistPockets = details.GetChild(2).childCount, particles, motion, duplicatePassRefused = refused,
                scope = "Editor native reopen, original object preservation, web support/clearance, egg grounding, particle simulation. Player and character Play not run."};
            string output = Path.GetFullPath(Application.dataPath + "/../../개인파일/코덱스산출/World/20261010_CaveWebDressing"); Directory.CreateDirectory(output);
            File.WriteAllText(output + "/verification.json", JsonConvert.SerializeObject(result, Formatting.Indented)); return result;
        }
        finally
        {
            if (probes.IsValid()) EditorSceneManager.ClosePreviewScene(probes);
            if (decorated.IsValid()) EditorSceneManager.ClosePreviewScene(decorated);
            if (source.IsValid()) EditorSceneManager.ClosePreviewScene(source);
            Require(active.isDirty == dirty, "The open scene dirty state changed.");
        }
    }
}
