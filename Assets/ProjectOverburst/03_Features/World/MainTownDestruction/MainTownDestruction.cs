using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Profiling;

public sealed class MainTownDestruction : MonoBehaviour
{
    [Serializable] public struct Placement { public Transform root; public int definition; }
    public MainTownDestructionCatalog catalog;
    public Placement[] placements = Array.Empty<Placement>();
    public Terrain terrain;
    const float CellSize = 12f, ActivationRadius = 12f;
    static readonly ProfilerMarker RefreshMarker = new ProfilerMarker("Overburst.EnvironmentDestruction.Nearby");
    static readonly ProfilerMarker BreakMarker = new ProfilerMarker("Overburst.EnvironmentDestruction.Break");
    static readonly ProfilerMarker TerrainCollisionMarker = new ProfilerMarker("Overburst.EnvironmentDestruction.TerrainColliderRefresh");
    readonly List<Entry> entries = new List<Entry>();
    readonly Dictionary<Vector2Int, List<int>> cells = new Dictionary<Vector2Int, List<int>>();
    readonly Dictionary<int, MainTownBreakableTarget> active = new Dictionary<int, MainTownBreakableTarget>();
    readonly HashSet<int> wanted = new HashSet<int>();
    readonly List<int> removals = new List<int>();
    readonly HashSet<int> queuedDefinitions = new HashSet<int>();
    readonly Queue<int> warm = new Queue<int>();
    TerrainData originalTerrain, runtimeTerrain;
    TerrainCollider terrainCollider;
    TreeInstance[] originalTrees, trees;
    float nextRefresh;
    Vector3 lastPosition;
    bool initialized, treeCollisionDirty;
    int broken;

    sealed class Entry
    {
        public int definition, treeIndex = -1;
        public Transform original;
        public Vector3 position, scale;
        public Quaternion rotation;
        public bool broken, originalActive;
        // An in-flight attack holds this target's identity. Never bind it to another entry.
        public MainTownBreakableTarget target;
    }
    public int EntryCount => entries.Count;
    public int BrokenCount => broken;
    public int ActiveTargetCount => active.Count;
    public int CreatedTargetCount { get; private set; }
    public TerrainData OriginalTerrain => originalTerrain;
    public TerrainData RuntimeTerrain => runtimeTerrain;
#if UNITY_EDITOR
    public int TerrainCollisionRefreshCount { get; private set; }
#endif
    public MainTownBreakableTarget TargetFor(int index) => active.TryGetValue(index, out var value) ? value : null;
    public bool IsBroken(int index) => entries[index].broken;
    public Vector3 PositionFor(int index) => entries[index].position;
    public int DefinitionFor(int index) => entries[index].definition;
    public bool IsTerrainTree(int index) => entries[index].treeIndex >= 0;

    void Start()
    {
        if (catalog == null) { enabled = false; return; }
        for (int i = 0; i < placements.Length; i++)
        {
            var placement = placements[i]; if (placement.root == null || !placement.root.gameObject.activeInHierarchy) continue;
            Add(new Entry { definition = placement.definition, original = placement.root, position = placement.root.position,
                rotation = placement.root.rotation, scale = placement.root.lossyScale, originalActive = placement.root.gameObject.activeSelf });
        }
        if (terrain != null)
        {
            // Play must never modify or save the authored TerrainData, including its tree colliders.
            originalTerrain = terrain.terrainData; originalTrees = originalTerrain.treeInstances;
            runtimeTerrain = Instantiate(originalTerrain); runtimeTerrain.name = originalTerrain.name + " (runtime destruction)";
            runtimeTerrain.hideFlags = HideFlags.DontSave; trees = runtimeTerrain.treeInstances;
            terrain.terrainData = runtimeTerrain; terrainCollider = terrain.GetComponent<TerrainCollider>();
            if (terrainCollider != null) terrainCollider.terrainData = runtimeTerrain;
            var prototypes = originalTerrain.treePrototypes;
            for (int i = 0; i < trees.Length; i++)
            {
                var tree = trees[i]; int definition = -1;
                for (int d = 0; d < catalog.definitions.Length; d++)
                    if (catalog.definitions[d].source == prototypes[tree.prototypeIndex].prefab) { definition = d; break; }
                if (definition < 0 || tree.widthScale <= 0 || tree.heightScale <= 0) continue;
                Add(new Entry { definition = definition, treeIndex = i,
                    position = terrain.transform.position + Vector3.Scale(tree.position, originalTerrain.size),
                    rotation = Quaternion.Euler(0, tree.rotation * Mathf.Rad2Deg, 0),
                    scale = new Vector3(tree.widthScale, tree.heightScale, tree.widthScale) });
            }
        }
        initialized = true; lastPosition = new Vector3(float.PositiveInfinity, 0, 0);
    }
    void Add(Entry entry)
    {
        int index = entries.Count; entries.Add(entry); var cell = Cell(entry.position);
        if (!cells.TryGetValue(cell, out var list)) cells.Add(cell, list = new List<int>());
        list.Add(index);
    }
    static Vector2Int Cell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));
    void Update()
    {
        if (!initialized) return;
        var actor = PlayerContext.Instance?.CurrentActor;
        if (actor != null && (Time.time >= nextRefresh || (actor.transform.position - lastPosition).sqrMagnitude > 4))
            RefreshNearby(actor.transform.position);
        if (warm.Count > 0) TransientVfxPool.PrepareOne(catalog.definitions[warm.Dequeue()].debris, 2);
    }
    public void RefreshNearby(Vector3 position)
    {
        using var scope = RefreshMarker.Auto();
        lastPosition = position; nextRefresh = Time.time + .25f; wanted.Clear(); var cell = Cell(position);
        for (int x = cell.x - 1; x <= cell.x + 1; x++) for (int z = cell.y - 1; z <= cell.y + 1; z++)
        {
            if (!cells.TryGetValue(new Vector2Int(x, z), out var list)) continue;
            for (int i = 0; i < list.Count; i++)
            {
                int index = list[i]; var entry = entries[index];
                if (entry.broken || entry.treeIndex < 0 && (entry.original == null || !entry.original.gameObject.activeInHierarchy)) continue;
                var delta = entry.position - position; delta.y = 0;
                if (delta.sqrMagnitude <= ActivationRadius * ActivationRadius) wanted.Add(index);
            }
        }
        removals.Clear(); foreach (var pair in active) if (!wanted.Contains(pair.Key)) removals.Add(pair.Key);
        for (int i = 0; i < removals.Count; i++) ReleaseTarget(removals[i]);
        foreach (int index in wanted)
        {
            if (active.ContainsKey(index)) continue; var entry = entries[index];
            MainTownBreakableTarget health = entry.target;
            if (health == null)
            {
                var go = new GameObject("Nearby environment target"); go.SetActive(false); go.transform.SetParent(transform, false);
                health = go.AddComponent<MainTownBreakableTarget>(); go.AddComponent<CombatAffiliation>(); go.AddComponent<CombatTarget>();
                entry.target = health;
                CreatedTargetCount++;
            }
            health.transform.SetPositionAndRotation(entry.position, Quaternion.identity); health.transform.localScale = Vector3.one; health.Bind(this, index);
            var definition = catalog.definitions[entry.definition]; var center = entry.rotation * Vector3.Scale(definition.bounds.center, entry.scale);
            var size = Vector3.Scale(definition.bounds.size, entry.scale);
            bool trunk = definition.tree && definition.bounds.size.y >= 3 && !definition.source.name.StartsWith("DF_FallenDeadTree") && !definition.source.name.StartsWith("DF_Branch");
            float radius = trunk ? Mathf.Clamp(Mathf.Min(Mathf.Abs(size.x), Mathf.Abs(size.z)) * .15f, .15f, .55f) : Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.z)) * .45f;
            if (trunk) { center.x = 0; center.z = 0; }
            var target = health.GetComponent<CombatTarget>(); target.Configure(CombatTeam.Neutral, false);
            target.ConfigureVolume(center, Mathf.Max(.1f, radius), Mathf.Max(.2f, Mathf.Abs(size.y)));
            health.gameObject.SetActive(true); active.Add(index, health);
            if (queuedDefinitions.Add(entry.definition)) { warm.Enqueue(entry.definition); warm.Enqueue(entry.definition); }
        }
    }
    void ReleaseTarget(int index)
    {
        if (!active.TryGetValue(index, out var target)) return;
        target.gameObject.SetActive(false); active.Remove(index);
    }
    public bool TryBreak(int index, DamageInfo info)
    {
        using var scope = BreakMarker.Auto();
        if (!Application.isPlaying || index < 0 || index >= entries.Count || info.damage <= 0 || info.isDamageOverTime) return false;
        var entry = entries[index]; if (entry.broken) return false;
        if (entry.treeIndex < 0 && (entry.original == null || !entry.original.gameObject.activeInHierarchy)) return false;
        entry.broken = true; broken++; ReleaseTarget(index);
        if (entry.original != null) entry.original.gameObject.SetActive(false);
        else
        {
            var tree = trees[entry.treeIndex]; tree.widthScale = 0; tree.heightScale = 0; trees[entry.treeIndex] = tree;
            runtimeTerrain.SetTreeInstance(entry.treeIndex, tree);
            treeCollisionDirty = true;
        }
        var definition = catalog.definitions[entry.definition];
        TransientVfxPool.Spawn(definition.debris, entry.position, entry.rotation, 3f, 4,
            prepareBeforeActivation: go => { go.transform.localScale = entry.scale; go.GetComponent<MainTownDebris>().Prepare(info.direction); },
            contentSceneHandle: gameObject.scene.handle);
        return true;
    }
    [ContextMenu("Restore this scene's destructible objects")]
    public void RestoreAll()
    {
        if (!initialized) return;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i]; if (!entry.broken) continue;
            if (entry.original != null) entry.original.gameObject.SetActive(entry.originalActive);
            else if (entry.treeIndex >= 0) { trees[entry.treeIndex] = originalTrees[entry.treeIndex]; runtimeTerrain.SetTreeInstance(entry.treeIndex, trees[entry.treeIndex]); treeCollisionDirty = true; }
            entry.broken = false;
        }
        broken = 0; nextRefresh = 0;
        RefreshTreeCollisions();
    }
    void LateUpdate() => RefreshTreeCollisions();
    void RefreshTreeCollisions()
    {
        if (!treeCollisionDirty) return;
        treeCollisionDirty = false;
        if (terrainCollider == null || !terrainCollider.enabled) return;
        using var scope = TerrainCollisionMarker.Auto();
#if UNITY_EDITOR
        TerrainCollisionRefreshCount++;
#endif
        // SetTreeInstance updates visuals but leaves Unity's tree collision cache intact.
        // Rebuild once after all contacts in this frame, preserving terrain and prototype data.
        terrainCollider.enabled = false;
        terrainCollider.enabled = true;
    }
    void OnDisable()
    {
        RefreshTreeCollisions();
        removals.Clear(); foreach (var pair in active) removals.Add(pair.Key);
        for (int i = 0; i < removals.Count; i++) ReleaseTarget(removals[i]);
    }
    void OnDestroy()
    {
        RestoreAll();
        if (terrain != null && terrain.terrainData == runtimeTerrain) terrain.terrainData = originalTerrain;
        if (terrainCollider != null && terrainCollider.terrainData == runtimeTerrain) terrainCollider.terrainData = originalTerrain;
        if (runtimeTerrain != null) Destroy(runtimeTerrain);
    }
}
