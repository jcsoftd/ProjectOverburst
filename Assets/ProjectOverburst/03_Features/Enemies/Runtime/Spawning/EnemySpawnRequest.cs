using System;
using UnityEngine;

[Serializable]
public struct EnemySpawnRequest
{
    [SerializeField] private EnemyDefinition definition;
    [SerializeField] private string definitionId;
    [SerializeField] private Vector3 position;
    [SerializeField] private Quaternion rotation;
    [SerializeField] private Transform parent;
    [SerializeField] private Transform target;
    [SerializeField] private GameObject encounterOwner;
    [SerializeField] private Transform encounterAnchor;
    [SerializeField, Min(0.01f)] private float difficultyMultiplier;
    [SerializeField, Min(0.01f)] private float encounterMultiplier;
    [SerializeField] private int runSeed;

    public EnemySpawnRequest(
        EnemyDefinition enemyDefinition,
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        Transform targetTransform = null,
        GameObject squadEncounterOwner = null,
        Transform squadEncounterAnchor = null,
        Transform spawnParent = null,
        float difficultyStatMultiplier = 1f,
        float encounterStatMultiplier = 1f,
        int seed = 0)
    {
        definition = enemyDefinition;
        definitionId = enemyDefinition != null ? enemyDefinition.EnemyId : string.Empty;
        position = spawnPosition;
        rotation = spawnRotation;
        parent = spawnParent;
        target = targetTransform;
        encounterOwner = squadEncounterOwner;
        encounterAnchor = squadEncounterAnchor;
        difficultyMultiplier = Mathf.Max(0.01f, difficultyStatMultiplier);
        encounterMultiplier = Mathf.Max(0.01f, encounterStatMultiplier);
        runSeed = seed;
    }

    public EnemySpawnRequest(
        string enemyDefinitionId,
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        Transform targetTransform = null,
        GameObject squadEncounterOwner = null,
        Transform squadEncounterAnchor = null,
        Transform spawnParent = null,
        float difficultyStatMultiplier = 1f,
        float encounterStatMultiplier = 1f,
        int seed = 0)
    {
        definition = null;
        definitionId = enemyDefinitionId != null ? enemyDefinitionId.Trim() : string.Empty;
        position = spawnPosition;
        rotation = spawnRotation;
        parent = spawnParent;
        target = targetTransform;
        encounterOwner = squadEncounterOwner;
        encounterAnchor = squadEncounterAnchor;
        difficultyMultiplier = Mathf.Max(0.01f, difficultyStatMultiplier);
        encounterMultiplier = Mathf.Max(0.01f, encounterStatMultiplier);
        runSeed = seed;
    }

    public EnemyDefinition Definition => definition;
    public string DefinitionId => definition != null ? definition.EnemyId : definitionId;
    public Vector3 Position => position;
    public Quaternion Rotation => IsZeroQuaternion(rotation) ? Quaternion.identity : rotation;
    public Transform Parent => parent;
    public Transform Target => target;
    public GameObject EncounterOwner => encounterOwner;
    public Transform EncounterAnchor => encounterAnchor != null ? encounterAnchor : target;
    public float DifficultyMultiplier => ResolveMultiplier(difficultyMultiplier);
    public float EncounterMultiplier => ResolveMultiplier(encounterMultiplier);
    public int RunSeed => runSeed;
    public bool HasDefinitionReference => definition != null;
    public bool HasDefinitionId => !string.IsNullOrWhiteSpace(DefinitionId);

    private static bool IsZeroQuaternion(Quaternion value)
    {
        return Mathf.Approximately(value.x, 0f)
            && Mathf.Approximately(value.y, 0f)
            && Mathf.Approximately(value.z, 0f)
            && Mathf.Approximately(value.w, 0f);
    }

    private static float ResolveMultiplier(float value)
    {
        return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value)
            ? value
            : 1f;
    }
}
