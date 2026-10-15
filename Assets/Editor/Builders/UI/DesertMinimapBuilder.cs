using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Mojave;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class DesertMinimapBuilder
{
    public const string AssetRoot = "Assets/ProjectOverburst/Resources/UI/Minimap/Desert";
    const string Key = "Overburst.DesertMinimapBuilder.";
    const float CellSize = .25f;
    static string output;
    static List<MapTerrainCatalog.DesertCandidate> candidates;
    static readonly List<object> reports = new List<object>();
    static int index;
    static DesertMinimapBuilder()
    {
        string interrupted = SessionState.GetString(Key + "output", "");
        if (interrupted == "") return;
        File.WriteAllText(Path.Combine(interrupted, "result.json"), JsonConvert.SerializeObject(new { status = "FAIL", error = "Bake interrupted by domain reload; completed candidate files are preserved." }));
        SessionState.EraseString(Key + "output");
    }

    [MenuItem("OVERBURST/UI/Bake Selected Desert Minimaps")]
    static void Menu() => Start(Path.GetFullPath("../개인파일/코덱스산출/UI/DesertMinimapBake/" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff")));

    // Reads the current selected-candidate catalog. Regenerated scenes need no hard-coded scene list here.
    public static void Start(string directory, string candidateId = null)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || output != null || SessionState.GetString(Key + "output", "") != "") throw new InvalidOperationException("Idle Editor required.");
        directory = Path.GetFullPath(directory);
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!directory.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(directory)) throw new ArgumentException("Fresh owned evidence directory required.");
        MapTerrainCatalog.InvalidateCandidates();
        candidates = candidateId == null ? MapTerrainCatalog.DesertCandidates.ToList() : new List<MapTerrainCatalog.DesertCandidate> { MapTerrainCatalog.Resolve(candidateId) };
        Directory.CreateDirectory(directory); output = directory; index = 0; reports.Clear();
        SessionState.SetString(Key + "output", output);
        EditorApplication.update += Tick;
        Write("progress.json", new { state = "queued", count = candidates.Count, utc = DateTime.UtcNow });
    }
    static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Write("result.json", new { status = "CANCELLED", completed = reports, reason = "Play began; completed candidates are preserved." });
                Finish(); return;
            }
            var candidate = candidates[index];
            Write("progress.json", new { state = "baking", index, count = candidates.Count, candidate.id, utc = DateTime.UtcNow });
            reports.Add(Bake(candidate));
            Write("candidates.json", reports);
            index++;
            if (index == candidates.Count)
            {
                var selected = new HashSet<string>(MapTerrainCatalog.DesertCandidates.Select(c => c.id));
                var unused = AssetDatabase.FindAssets("t:DesertMinimapData", new[] { AssetRoot })
                    .Select(AssetDatabase.GUIDToAssetPath).Where(p => !selected.Contains(Path.GetFileNameWithoutExtension(p))).ToArray();
                Write("result.json", new { status = "PASS", candidates = reports, unusedPreserved = unused, scope = "Selected candidate bake, identity, floor connectivity; no Player build" });
                Finish();
            }
        }
        catch (Exception e) { Write("result.json", new { status = "FAIL", completed = reports, error = e.ToString() }); Finish(); Debug.LogException(e); }
        finally { if (!EditorApplication.isPlayingOrWillChangePlaymode) EditorUtility.UnloadUnusedAssetsImmediate(true); }
    }
    static void Finish() { EditorApplication.update -= Tick; SessionState.EraseString(Key + "output"); output = null; candidates = null; }
    static void Write(string file, object value) => File.WriteAllText(Path.Combine(output, file), JsonConvert.SerializeObject(value, Formatting.Indented));

    public static object BakeCandidate(MapTerrainCatalog.DesertCandidate candidate, string directory, string assetRoot)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating
            ||output!=null||SessionState.GetString(Key+"output", "")!="")throw new InvalidOperationException("Idle Editor required.");
        string allowed=Path.GetFullPath(Path.Combine(Application.dataPath,"../../개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        directory=Path.GetFullPath(directory);
        if(!directory.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||Directory.Exists(directory)
            ||!assetRoot.StartsWith("Assets/ProjectOverburst/",StringComparison.Ordinal)||assetRoot.Contains(".."))
            throw new ArgumentException("Fresh owned evidence and asset directories required.");
        Directory.CreateDirectory(directory);output=directory;
        try{return Bake(candidate,assetRoot);}
        finally{output=null;}
    }
    static object Bake(MapTerrainCatalog.DesertCandidate candidate,string assetRoot=AssetRoot)
    {
        string path = assetRoot + "/" + candidate.id + ".asset";
        string dependency = AssetDatabase.GetAssetDependencyHash(candidate.scene).ToString();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var scene = EditorSceneManager.OpenPreviewScene(candidate.scene);
        Texture2D texture = null;
        try
        {
            var world = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MojaveWorld>(true)).Single();
            if (!world.generatedRoot || !world.surface || world.bakedSeed != candidate.seed || world.seed != candidate.seed
                || world.bakedLayoutVersion != MojaveLayout.Version) throw new InvalidOperationException("Selected desert terrain must be regenerated first: " + candidate.id);
            world.EnsureLayout();
            if (world.layout.places.Count != candidate.areaCount || (world.layout.bounds - new Vector2(candidate.width, candidate.depth)).sqrMagnitude > .0001f)
                throw new InvalidOperationException("Candidate catalog does not match the generated terrain: " + candidate.id);
            var data = AssetDatabase.LoadAssetAtPath<DesertMinimapData>(path);
            if (data && data.version == DesertMinimapData.CurrentVersion && data.sourceDependencyHash == dependency
                && data.layoutSignature == DesertMinimapData.LayoutSignature(world) && data.cellSize <= CellSize)
            {
                data.Validate(candidate, world); ValidateTopology(data, world);
                return new { candidate.id, status = "CURRENT", data.width, data.height, seconds = watch.Elapsed.TotalSeconds, source = dependency };
            }
            var probe = new GameObject("Minimap obstacle probe");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(probe, scene);
            var capsule = probe.AddComponent<CapsuleCollider>(); capsule.height = 1.5f; capsule.radius = .2f; capsule.center = Vector3.up * .9f;
            var obstacles = new Dictionary<Vector2Int, List<Collider>>();
            foreach (var collider in world.generatedRoot.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger || collider is TerrainCollider || collider is CharacterController
                    || collider.attachedRigidbody && !collider.attachedRigidbody.isKinematic || collider.GetComponentInParent<CombatHealth>()) continue;
                Bounds bounds = collider.bounds; bounds.Expand(.4f);
                for (int z = Mathf.FloorToInt(bounds.min.z / 8); z <= Mathf.FloorToInt(bounds.max.z / 8); z++)
                for (int x = Mathf.FloorToInt(bounds.min.x / 8); x <= Mathf.FloorToInt(bounds.max.x / 8); x++)
                {
                    var key = new Vector2Int(x, z);
                    if (!obstacles.TryGetValue(key, out var bucket)) obstacles.Add(key, bucket = new List<Collider>());
                    bucket.Add(collider);
                }
            }
            bool Floor(Vector2 p)
            {
                if (!world.IsWalkable(p, .05f)) return false;
                Vector3 ground = world.Ground(p);
                if (!obstacles.TryGetValue(new Vector2Int(Mathf.FloorToInt(p.x / 8), Mathf.FloorToInt(p.y / 8)), out var bucket)) return true;
                foreach (var collider in bucket)
                {
                    var b = collider.bounds;
                    if (b.max.y <= ground.y + .35f || b.min.y > ground.y + 1.65f || p.x < b.min.x - .2f || p.x > b.max.x + .2f || p.y < b.min.z - .2f || p.y > b.max.z + .2f) continue;
                    if (Physics.ComputePenetration(capsule, ground, Quaternion.identity, collider, collider.transform.position, collider.transform.rotation, out _, out float depth) && depth > .015f) return false;
                }
                return true;
            }
            var prepared = ScriptableObject.CreateInstance<DesertMinimapData>();
            try
            {
                prepared.version = DesertMinimapData.CurrentVersion; prepared.candidateId = candidate.id;
                prepared.sourceScene = candidate.scene; prepared.sourceDependencyHash = dependency;
                prepared.seed = candidate.seed; prepared.layoutVersion = world.bakedLayoutVersion;
                prepared.layoutSignature = DesertMinimapData.LayoutSignature(world);
                prepared.combatTiles = world.layout.places.Select(p => world.catalog.patches[p.patch]).ToArray();
                prepared.terrainPosition = world.surface.transform.position; prepared.terrainSize = world.surface.terrainData.size;
                // Retry only a topology that lost a passage at the initial resolution.
                string coarseFailure = null;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    float cell = attempt == 0 ? CellSize : CellSize * .5f;
                    prepared.cellSize = cell; prepared.bounds = DesertMinimapData.SamplingBounds(world, cell);
                    prepared.width = Mathf.RoundToInt(prepared.bounds.width / cell); prepared.height = Mathf.RoundToInt(prepared.bounds.height / cell);
                    if (prepared.width > 4096 || prepared.height > 4096 || (long)prepared.width * prepared.height > 4194304)
                        throw new InvalidOperationException("사막 고해상도 베이크가 탐색 지도 크기 제한을 초과합니다: " + candidate.id);
                    prepared.cells = new byte[prepared.width * prepared.height]; prepared.connections = new byte[prepared.cells.Length];
                    Vector2 Point(int x, int z) => prepared.bounds.position + new Vector2((x + .5f) * cell, (z + .5f) * cell);
                    for (int z = 1; z < prepared.height - 1; z++) for (int x = 1; x < prepared.width - 1; x++)
                        if (Floor(Point(x, z))) prepared.cells[z * prepared.width + x] = MinimapTerrainPixels.Ground;
                    for (int z = 1; z < prepared.height - 1; z++) for (int x = 1; x < prepared.width - 1; x++)
                    {
                        int i = z * prepared.width + x;
                        if (prepared.cells[i] == 0) continue;
                        if (prepared.cells[i + 1] != 0 && Floor(Point(x, z) + new Vector2(cell * .5f, 0)))
                        { prepared.connections[i] |= MinimapExplorationMap.East; prepared.connections[i + 1] |= MinimapExplorationMap.West; }
                        if (prepared.cells[i + prepared.width] != 0 && Floor(Point(x, z) + new Vector2(0, cell * .5f)))
                        { prepared.connections[i] |= MinimapExplorationMap.North; prepared.connections[i + prepared.width] |= MinimapExplorationMap.South; }
                    }
                    try { ValidateTopology(prepared, world); break; }
                    catch (InvalidOperationException e) when (attempt == 0) { coarseFailure = e.Message; }
                }
                EnsureFolder(assetRoot + "/Images");
                string imagePath = assetRoot + "/Images/" + candidate.id + ".png";
                Backup(path); Backup(imagePath);
                texture = new Texture2D(prepared.width, prepared.height, TextureFormat.RGBA32, false);
                texture.SetPixels32(MinimapTerrainPixels.Build(prepared.cells, prepared.width, prepared.height)); texture.Apply(false, false);
                File.WriteAllBytes(imagePath, texture.EncodeToPNG()); AssetDatabase.ImportAsset(imagePath, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(imagePath);
                importer.textureType = TextureImporterType.Default; importer.npotScale = TextureImporterNPOTScale.None;
                importer.isReadable = true; importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.maxTextureSize = 4096; importer.SaveAndReimport();
                prepared.image = AssetDatabase.LoadAssetAtPath<Texture2D>(imagePath);
                prepared.Validate(candidate, world);
                if (AssetDatabase.GetAssetDependencyHash(candidate.scene).ToString() != dependency)
                    throw new InvalidOperationException("Source terrain changed while baking: " + candidate.id);
                if (!data) { data = ScriptableObject.CreateInstance<DesertMinimapData>(); AssetDatabase.CreateAsset(data, path); }
                EditorUtility.CopySerialized(prepared, data); EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data);
                return new { candidate.id, status = "BAKED", data.width, data.height, data.cellSize, ground = data.cells.Count(c => c != 0),
                    seconds = watch.Elapsed.TotalSeconds, source = dependency, coarseFailure };
            }
            finally { Object.DestroyImmediate(prepared); }
        }
        finally { if (texture) Object.DestroyImmediate(texture); EditorSceneManager.ClosePreviewScene(scene); }
    }

    public static void ValidateTopology(DesertMinimapData data, MojaveWorld world)
    {
        var visited = new bool[data.cells.Length]; var queue = new Queue<int>();
        int Find(Vector3 position)
        {
            int best = -1; float nearest = 64;
            for (int z = Mathf.Max(1, Mathf.FloorToInt((position.z - data.bounds.yMin - 8) / data.cellSize)); z < Mathf.Min(data.height - 1, Mathf.CeilToInt((position.z - data.bounds.yMin + 8) / data.cellSize)); z++)
            for (int x = Mathf.Max(1, Mathf.FloorToInt((position.x - data.bounds.xMin - 8) / data.cellSize)); x < Mathf.Min(data.width - 1, Mathf.CeilToInt((position.x - data.bounds.xMin + 8) / data.cellSize)); x++)
            {
                int i = z * data.width + x; if (data.cells[i] == 0) continue;
                float distance = (new Vector2(data.bounds.xMin + (x + .5f) * data.cellSize - position.x, data.bounds.yMin + (z + .5f) * data.cellSize - position.z)).sqrMagnitude;
                if (distance < nearest) { nearest = distance; best = i; }
            }
            if (best < 0) throw new InvalidOperationException("Minimap has no floor near a required location.");
            return best;
        }
        int start = Find(DesertRunWorld.EntryFor(world)); visited[start] = true; queue.Enqueue(start);
        while (queue.Count > 0)
        {
            int here = queue.Dequeue(); byte flags = data.connections[here];
            void Add(int next, byte flag)
            {
                if ((flags & flag) == 0) return;
                if (next < 0 || next >= data.cells.Length || data.cells[next] == 0) throw new InvalidOperationException("Invalid minimap topology edge.");
                if (visited[next]) return; visited[next] = true; queue.Enqueue(next);
            }
            Add(here + 1, MinimapExplorationMap.East); Add(here - 1, MinimapExplorationMap.West);
            Add(here + data.width, MinimapExplorationMap.North); Add(here - data.width, MinimapExplorationMap.South);
        }
        foreach (var place in world.layout.places)
            if (!visited[Find(world.Ground(place.center))]) throw new InvalidOperationException("Minimap passage disconnected: " + place.name);
    }
    static void Backup(string path)
    {
        foreach (string source in new[] { path, path + ".meta" })
            if (File.Exists(source)) { string target = Path.Combine(output, "Before", source); Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(source, target, false); }
    }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, slash)); AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
