using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
namespace Overburst.Caves
{
public sealed class CaveRuntimeDetails
{
    public const string RootName = "Procedural webs, egg nests and low mist";
    public const float WebSurfaceClearance = 3.5f;
    sealed class Result { public int webs, eggClusters, eggs, mistPockets, supportMeshes; }
    readonly CaveGenerationAssets assets;
    readonly CaveRuntimeGenerator owner;
    public CaveRuntimeDetails(CaveGenerationAssets assets, CaveRuntimeGenerator owner) { this.assets=assets; this.owner=owner; }
    static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
    static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    { var d = b - a; return Vector2.Distance(p, a + d * Mathf.Clamp01(Vector2.Dot(p - a, d) / Mathf.Max(.001f, d.sqrMagnitude))); }
    public static bool ClearPassages(CaveWorld world, Vector3 p, float margin)
    {
        foreach (var route in world.passages)
            for (int i = 1; i < route.points.Length; i++)
            {
                float y = Mathf.Min(route.points[i - 1].y, route.points[i].y);
                if (p.y < y - 2.8f) continue;
                if (DistanceToSegment(XZ(p), XZ(route.points[i - 1]), XZ(route.points[i])) < route.widths[Mathf.Min(i, route.widths.Length - 1)] * .5f + margin) return false;
            }
        return true;
    }
    string Model(MeshFilter f) => assets.ModelName(f);
    bool Support(MeshFilter f)
    {
        if (!f.sharedMesh || !f.gameObject.activeInHierarchy || !f.TryGetComponent<Renderer>(out var r) || !r.enabled) return false;
        string n = Model(f);
        return n.StartsWith("Rock", StringComparison.Ordinal) || n.StartsWith("Stal", StringComparison.Ordinal) || n.StartsWith("Platform", StringComparison.Ordinal) || n.StartsWith("Ruins", StringComparison.Ordinal);
    }
    static Bounds BoundsOf(GameObject g)
    { var renderers = g.GetComponentsInChildren<Renderer>(); var b = renderers[0].bounds; foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds); return b; }
    public float WebCeiling(CaveWorld world, Bounds sheet)
    {
        // Check the whole sheet footprint, including lower platforms in a compound court.
        float floor = world.courts.OrderBy(c => (XZ(c.center) - XZ(sheet.center)).sqrMagnitude).First().center.y;
        foreach (var c in world.courts)
            foreach (var f in c.tile.GetComponentsInChildren<MeshFilter>())
            {
                if (!Model(f).StartsWith("Platform", StringComparison.Ordinal) || !f.TryGetComponent<Renderer>(out var r)) continue;
                var b = r.bounds;
                if (sheet.min.x > b.max.x + 2 || sheet.max.x < b.min.x - 2 || sheet.min.z > b.max.z + 2 || sheet.max.z < b.min.z - 2) continue;
                floor = Mathf.Min(floor, c.center.y, b.max.y);
            }
        foreach (var p in world.passages) for (int i = 1; i < p.points.Length; i++)
            if (DistanceToSegment(XZ(sheet.center), XZ(p.points[i - 1]), XZ(p.points[i])) < XZ(sheet.extents).magnitude + p.widths[Mathf.Min(i, p.widths.Length - 1)] * .5f)
                floor = Mathf.Min(floor, p.points[i - 1].y, p.points[i].y);
        return floor - WebSurfaceClearance;
    }
    public System.Collections.IEnumerator Apply(CaveWorld world)
    {
        if (!world || !world.authoredLayout || world.courts.Count == 0) throw new ArgumentException("An authored cave map is required.");
        if (world.transform.Find(RootName)) throw new InvalidOperationException("This map already has final details. Use the undressed source for a fresh pass.");
        var terrain = world.GetComponentInChildren<Terrain>();
        if (!terrain) throw new InvalidOperationException("Generate the cave ground before final details.");
        Scene probes = default; GameObject root = null;
        var result = new Result(); bool success = false;
        try
        {
            var sources = world.GetComponentsInChildren<MeshFilter>().Where(Support).ToArray();
            var rocks = sources.Where(f => Model(f).StartsWith("Rock") && f.GetComponent<Renderer>().bounds.size.y > 2).ToArray();
            var existingWebs = world.GetComponentsInChildren<MeshFilter>().Where(f => Model(f).StartsWith("Spiderweb")).Select(f => f.GetComponent<Renderer>().bounds).ToArray();
            var existingEggs = world.GetComponentsInChildren<MeshFilter>().Where(f => Model(f).StartsWith("Egg")).Select(f => f.GetComponent<Renderer>().bounds.center).ToArray();
            probes = SceneManager.CreateScene("Cave dressing probes " + world.GetInstanceID(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var physics = probes.GetPhysicsScene();
            foreach (var f in sources)
            {
                var g = new GameObject("Surface probe") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(g, probes);
                g.transform.SetPositionAndRotation(f.transform.position, f.transform.rotation); g.transform.localScale = f.transform.lossyScale;
                g.AddComponent<MeshCollider>().sharedMesh = f.sharedMesh;
            }
            result.supportMeshes = sources.Length; Physics.SyncTransforms();
            root = new GameObject(RootName); root.transform.SetParent(world.transform, false);
            var webRoot = new GameObject("Webs anchored in crevices").transform; webRoot.SetParent(root.transform, false);
            var eggRoot = new GameObject("Egg nests at cliff feet").transform; eggRoot.SetParent(root.transform, false);
            var mistRoot = new GameObject("Low grey mist between platforms").transform; mistRoot.SetParent(root.transform, false);
            var random = new System.Random(unchecked(world.seed ^ 0x51D37));
            float Next(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
            var occupied = new List<Vector3>(); var nests = new List<Vector3>();
            var webSource = assets.web.GetComponentInChildren<MeshFilter>();
            var webMaterial = webSource.GetComponent<Renderer>().sharedMaterial;
            var sourceVertices = webSource.sharedMesh.vertices;
            int centerIndex = Enumerable.Range(0, sourceVertices.Length).OrderBy(i => XZ(sourceVertices[i]).sqrMagnitude).First();
            var ring = Enumerable.Range(0, sourceVertices.Length).Where(i => i != centerIndex).OrderBy(i => Mathf.Atan2(sourceVertices[i].z, sourceVertices[i].x)).ToArray();
            var orderedRocks = rocks.OrderBy(f => random.Next()).ToArray();
            foreach (var rock in orderedRocks)
            {
                if (result.webs >= world.courts.Count * 3) break;
                yield return null;
                var bounds = rock.GetComponent<Renderer>().bounds;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    float angle = Next(0, Mathf.PI * 2);
                    var radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    var p = bounds.center + radial * (Mathf.Max(bounds.extents.x, bounds.extents.z) + Next(.3f, 2));
                    p.y = WebCeiling(world, new Bounds(p, new Vector3(12, 0, 12))) - Next(.5f, 7.5f);
                    float ground = terrain.SampleHeight(p) + terrain.transform.position.y;
                    if (p.y < ground + .6f || !ClearPassages(world, p, 2) || occupied.Any(v => Vector3.Distance(v, p) < 5) || existingWebs.Any(b => b.SqrDistance(p) < 2.25f)) continue;
                    // Three or more ray hits across at least two solid objects form a supported, sagging sheet.
                    var rotation = Quaternion.Euler(Next(-32, 32), Next(0, 360), Next(-20, 20));
                    var hits = new Dictionary<int, Vector3>(); var supports = new HashSet<Collider>();
                    for (int i = 0; i < ring.Length; i++)
                    {
                        var local = sourceVertices[ring[i]]; local.y = 0; var dir = rotation * local.normalized;
                        if (!physics.Raycast(p, dir, out var hit, 6, ~0, QueryTriggerInteraction.Ignore) || hit.distance < .7f) continue;
                        hits.Add(i, hit.point + hit.normal * .02f); supports.Add(hit.collider);
                    }
                    if (hits.Count < 3 || supports.Count < 2) continue;
                    var keys = hits.Keys.OrderBy(i => i).ToArray();
                    if (keys.Select((k, i) => (keys[(i + 1) % keys.Length] - k + ring.Length) % ring.Length).Max() > ring.Length / 2) continue;
                    var vertices = new Vector3[sourceVertices.Length];
                    for (int i = 0; i < ring.Length; i++)
                    {
                        if (hits.TryGetValue(i, out var anchor)) { vertices[ring[i]] = anchor; continue; }
                        int before = keys.LastOrDefault(k => k < i); if (!keys.Any(k => k < i)) before = keys.Last();
                        int after = keys.FirstOrDefault(k => k > i); if (!keys.Any(k => k > i)) after = keys.First();
                        float t = (float)((i - before + ring.Length) % ring.Length) / ((after - before + ring.Length) % ring.Length);
                        vertices[ring[i]] = Vector3.Lerp(hits[before], hits[after], t) + (p - Vector3.Lerp(hits[before], hits[after], t)) * (.12f * Mathf.Sin(t * Mathf.PI));
                    }
                    var center = ring.Aggregate(Vector3.zero, (sum, i) => sum + vertices[i]) / ring.Length;
                    center.y -= Next(.25f, .65f); vertices[centerIndex] = center;
                    var sheet = new Bounds(vertices[0], Vector3.zero); foreach (var v in vertices) sheet.Encapsulate(v);
                    if (sheet.max.y > WebCeiling(world, sheet)) continue;
                    if (vertices.Any(v => v.y < terrain.SampleHeight(v) + terrain.transform.position.y + .08f || !ClearPassages(world, v, 1))) continue;
                    if (physics.Raycast(center + Vector3.up * .1f, Vector3.down, out var under, .3f)) continue;
                    var mesh = Object.Instantiate(webSource.sharedMesh); owner.Own(mesh); mesh.name = "Supported web " + (result.webs + 1);
                    mesh.vertices = vertices.Select(v => v - center).ToArray(); mesh.RecalculateBounds(); mesh.RecalculateNormals(); mesh.RecalculateTangents();
                    var go = new GameObject($"Web {++result.webs:00} · {hits.Count} anchors"); go.transform.SetParent(webRoot, false); go.transform.position = center;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = webMaterial;
                    renderer.shadowCastingMode = ShadowCastingMode.Off; occupied.Add(center); break;
                }
            }
            foreach (var rock in orderedRocks)
            {
                if (result.eggClusters >= world.courts.Count) break;
                yield return null;
                var bounds = rock.GetComponent<Renderer>().bounds;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    float angle = Next(0, Mathf.PI * 2); var outward = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    var p = bounds.center + outward * (Mathf.Max(bounds.extents.x, bounds.extents.z) + Next(.6f, 1.7f));
                    p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                    if (nests.Any(v => Vector3.Distance(v, p) < 12) || existingEggs.Any(v => Vector3.Distance(v, p) < 4) || !ClearPassages(world, p, 3)) continue;
                    var nearest = world.courts.OrderBy(c => (XZ(c.center) - XZ(p)).sqrMagnitude).First();
                    if (p.y > nearest.center.y - 2) continue;
                    bool covered = false;
                    foreach (var offset in new[] {Vector3.zero, Vector3.right * 2, Vector3.left * 2, Vector3.forward * 2, Vector3.back * 2})
                        covered |= physics.Raycast(p + offset + Vector3.up * 80, Vector3.down, out _, 79.8f);
                    if (covered) continue;
                    // Project cliff-side nests onto the lower scenery floor, even below a suspended cliff base.
                    var wallProbe = new Vector3(p.x, Mathf.Max(p.y, bounds.min.y) + .8f, p.z);
                    if (!physics.Raycast(wallProbe, -outward, out var wall, 4) || wall.distance > 3) continue;
                    var uv = (p - terrain.transform.position); var normal = terrain.terrainData.GetInterpolatedNormal(uv.x / terrain.terrainData.size.x, uv.z / terrain.terrainData.size.z);
                    if (normal.y < .9f) continue;
                    var group = new GameObject("Nest " + (result.eggClusters + 1)).transform; group.SetParent(eggRoot, false); group.position = p;
                    int count = random.Next(8, 14), placed = 0;
                    for (int i = 0; i < count; i++)
                    {
                        float a = Next(0, Mathf.PI * 2), radius = i == 0 ? 0 : Next(.4f, 1.9f);
                        var at = p + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius; at.y = terrain.SampleHeight(at) + terrain.transform.position.y;
                        if (Mathf.Abs(at.y - p.y) > .5f || physics.Raycast(at + Vector3.up * 80, Vector3.down, out _, 79.9f) || !ClearPassages(world, at, 2)) continue;
                        int variant = i == 0 ? 1 : random.Next(2, 7);
                        var prefab = assets.eggs[variant - 1];
                        var go = Object.Instantiate(prefab, group); go.transform.SetParent(group, false);
                        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0, Next(0, 360), 0);
                        float scale = i == 0 ? Next(1.4f, 1.9f) : Next(.45f, 1.15f); go.transform.localScale = Vector3.one * scale;
                        var b = BoundsOf(go); go.transform.position += at - new Vector3(b.center.x, b.min.y + .035f, b.center.z);
                        foreach (var collider in go.GetComponentsInChildren<Collider>()) collider.enabled = false;
                        placed++; result.eggs++;
                    }
                    if (placed < 3) { result.eggs -= placed; group.gameObject.SetActive(false); Object.Destroy(group.gameObject); continue; }
                    nests.Add(p); result.eggClusters++; break;
                }
            }
            var mistMaterial = assets.mistMaterial; var mistPositions = new List<Vector3>();
            foreach (var passage in world.passages)
            {
                var middle = (passage.points.First() + passage.points.Last()) * .5f;
                var direction = passage.points.Last() - passage.points.First();
                var side = new Vector3(direction.z, 0, -direction.x).normalized;
                float ceiling = Mathf.Min(passage.points.First().y, passage.points.Last().y) - 3.2f;
                Vector3 p = default; bool found = false;
                foreach (float offset in new[] {0f, 6f, -6f, 10f, -10f})
                {
                    p = middle + side * offset; p.y = terrain.SampleHeight(p) + terrain.transform.position.y + 1.1f;
                    if (p.y > ceiling || mistPositions.Any(v => Vector3.Distance(v, p) < 12) || physics.Raycast(p + Vector3.up * 80, Vector3.down, out _, 79.5f)) continue;
                    found = true; break;
                }
                if (!found) continue;
                var go = new GameObject("Mist pocket " + (++result.mistPockets)); go.transform.SetParent(mistRoot, false); go.transform.position = p;
                var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.useAutoRandomSeed = false; ps.randomSeed = (uint)random.Next(1, int.MaxValue);
                var main = ps.main; main.duration = 24; main.loop = true; main.prewarm = true; main.startLifetime = new ParticleSystem.MinMaxCurve(16, 24);
                main.startSpeed = 0; main.startSize = new ParticleSystem.MinMaxCurve(9, 15); main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                main.startColor = new Color(.43f, .46f, .48f, .3f); main.maxParticles = 12; main.simulationSpace = ParticleSystemSimulationSpace.Local;
                var emission = ps.emission; emission.rateOverTime = .42f;
                var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(7, .4f, 7);
                var velocity = ps.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.Local; velocity.x = .075f; velocity.y = 0; velocity.z = .035f;
                var color = ps.colorOverLifetime; color.enabled = true; var gradient = new Gradient();
                gradient.SetKeys(new[] {new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1)}, new[] {new GradientAlphaKey(0, 0), new GradientAlphaKey(.75f, .2f), new GradientAlphaKey(.75f, .7f), new GradientAlphaKey(0, 1)}); color.color = gradient;
                var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard; renderer.sharedMaterial = mistMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; renderer.maxParticleSize = 1;
                ps.Simulate(24, true, true, true); ps.Play(); mistPositions.Add(p);
            }
            success = true;
        }
        finally
        {
            if (probes.IsValid() && probes.isLoaded) { foreach(var probe in probes.GetRootGameObjects()) { probe.SetActive(false); Object.Destroy(probe); } SceneManager.UnloadSceneAsync(probes); }
            if (!success && root) { root.SetActive(false); Object.Destroy(root); }
        }
    }
}
}
