using System;
using Overburst.Mojave;
using UnityEngine;

// Per-map geometry candidates. Population measurement and final live placement retain their runtime checks.
public sealed class DesertSpawnData : ScriptableObject
{
    public const string ResourceRoot = "World/MapRuns/DesertSpawns/";
    [Serializable] public sealed class BodyProfile { public float radius, height; public EnemyDefinition[] definitions; }
    [Serializable] public struct SpawnPoint { public Vector3 position; public long bodyMask; }
    [Serializable] public sealed class Area { public int index; public string place; public Vector3[] cells; public SpawnPoint[] points; }
    public string scenePath, sceneDependencyHash, bakeVersion;
    public int seed, layoutVersion;
    public TerrainData terrain;
    public BodyProfile[] bodies;
    public Area[] areas;

    public static DesertSpawnData Load(MojaveWorld world)
    {
        var data = Resources.Load<DesertSpawnData>(ResourceRoot + System.IO.Path.GetFileNameWithoutExtension(world.gameObject.scene.path));
        if (!data || data.scenePath != world.gameObject.scene.path || data.seed != world.seed || data.seed != world.bakedSeed
            || data.layoutVersion != world.bakedLayoutVersion || data.layoutVersion != MojaveLayout.Version
            || !world.surface || data.terrain != world.surface.terrainData || world.transform.lossyScale != Vector3.one
            || data.bodies == null || data.bodies.Length == 0 || data.bodies.Length > 63
            || data.areas == null || data.areas.Length != world.layout.places.Count || string.IsNullOrEmpty(data.bakeVersion)) return null;
        for (int i = 0; i < data.areas.Length; i++)
            if (data.areas[i] == null || data.areas[i].index != i || data.areas[i].points == null || data.areas[i].points.Length == 0
                || data.areas[i].place != JsonUtility.ToJson(world.layout.places[i])) return null;
        return data;
    }
    public int BodyIndex(EnemyDefinition definition, MapCombatGeometry.Body body)
    {
        for (int i = 0; i < bodies.Length; i++)
        {
            var profile = bodies[i];
            if (profile == null || profile.definitions == null || Mathf.Abs(profile.radius - body.Radius) > .00001f
                || Mathf.Abs(profile.height - body.Height) > .00001f) continue;
            foreach (var candidate in profile.definitions) if (candidate == definition) return i;
        }
        return -1;
    }
}
