using System;
using UnityEngine;

[Serializable]
public sealed class SurfaceResolutionRule
{
    [SerializeField] private PhysicsMaterial physicMaterial;
    [SerializeField] private string nameToken;
    [SerializeField] private SurfaceProfile profile;

    public PhysicsMaterial PhysicMaterial => physicMaterial;
    public string NameToken => nameToken;
    public SurfaceProfile Profile => profile;

    public SurfaceResolutionRule(PhysicsMaterial material, string token, SurfaceProfile configuredProfile)
    {
        physicMaterial = material;
        nameToken = token;
        profile = configuredProfile;
    }

    public bool Matches(PhysicsMaterial material, string candidateName)
    {
        if (profile == null)
            return false;
        if (physicMaterial != null && material == physicMaterial)
            return true;
        return !string.IsNullOrWhiteSpace(nameToken)
            && !string.IsNullOrWhiteSpace(candidateName)
            && candidateName.IndexOf(nameToken, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

[DisallowMultipleComponent]
public sealed class SurfaceResolver : MonoBehaviour
{
    [SerializeField] private SurfaceProfile defaultProfile;
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField, Min(0.1f)] private float rayStartHeight = 0.5f;
    [SerializeField, Min(0.1f)] private float rayDistance = 2f;
    [SerializeField] private SurfaceResolutionRule[] rules = Array.Empty<SurfaceResolutionRule>();

    public SurfaceProfile DefaultProfile => defaultProfile;
    public SurfaceProfile LastResolvedProfile { get; private set; }
    public Collider LastResolvedCollider { get; private set; }

    public void Configure(
        SurfaceProfile configuredDefaultProfile,
        LayerMask configuredGroundMask,
        float configuredRayStartHeight,
        float configuredRayDistance,
        SurfaceResolutionRule[] configuredRules)
    {
        defaultProfile = configuredDefaultProfile;
        groundMask = configuredGroundMask;
        rayStartHeight = Mathf.Max(0.1f, configuredRayStartHeight);
        rayDistance = Mathf.Max(0.1f, configuredRayDistance);
        rules = configuredRules ?? Array.Empty<SurfaceResolutionRule>();
    }

    public SurfaceProfile Resolve()
    {
        Vector3 origin = transform.position + Vector3.up * rayStartHeight;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            LastResolvedCollider = null;
            LastResolvedProfile = defaultProfile;
            return LastResolvedProfile;
        }

        LastResolvedCollider = hit.collider;
        LastResolvedProfile = Resolve(hit.collider, hit.point);
        return LastResolvedProfile;
    }

    public SurfaceProfile Resolve(Collider surfaceCollider, Vector3 worldPoint)
    {
        if (surfaceCollider == null)
            return defaultProfile;

        SurfaceOverride explicitOverride = surfaceCollider.GetComponentInParent<SurfaceOverride>();
        if (explicitOverride != null && explicitOverride.Profile != null)
            return explicitOverride.Profile;

        PhysicsMaterial material = surfaceCollider.sharedMaterial;
        SurfaceProfile matched = ResolveRules(material, material != null ? material.name : null);
        if (matched != null)
            return matched;

        Terrain terrain = surfaceCollider.GetComponent<Terrain>();
        if (terrain != null)
        {
            string terrainLayerName = ResolveTerrainLayerName(terrain, worldPoint);
            matched = ResolveRules(material, terrainLayerName);
            if (matched != null)
                return matched;
        }

        matched = ResolveRules(material, surfaceCollider.name);
        return matched != null ? matched : defaultProfile;
    }

    private SurfaceProfile ResolveRules(PhysicsMaterial material, string candidateName)
    {
        if (rules == null)
            return null;
        for (int i = 0; i < rules.Length; i++)
        {
            SurfaceResolutionRule rule = rules[i];
            if (rule != null && rule.Matches(material, candidateName))
                return rule.Profile;
        }
        return null;
    }

    private static string ResolveTerrainLayerName(Terrain terrain, Vector3 worldPoint)
    {
        TerrainData data = terrain != null ? terrain.terrainData : null;
        if (data == null || data.terrainLayers == null || data.terrainLayers.Length == 0)
            return null;

        Vector3 local = worldPoint - terrain.transform.position;
        int x = Mathf.Clamp(Mathf.RoundToInt(local.x / Mathf.Max(0.001f, data.size.x) * (data.alphamapWidth - 1)), 0, data.alphamapWidth - 1);
        int z = Mathf.Clamp(Mathf.RoundToInt(local.z / Mathf.Max(0.001f, data.size.z) * (data.alphamapHeight - 1)), 0, data.alphamapHeight - 1);
        float[,,] weights = data.GetAlphamaps(x, z, 1, 1);
        int best = 0;
        float bestWeight = float.MinValue;
        int count = Mathf.Min(weights.GetLength(2), data.terrainLayers.Length);
        for (int i = 0; i < count; i++)
        {
            if (weights[0, 0, i] <= bestWeight)
                continue;
            bestWeight = weights[0, 0, i];
            best = i;
        }

        TerrainLayer layer = data.terrainLayers[best];
        if (layer == null)
            return null;
        if (layer.diffuseTexture != null)
            return layer.name + " " + layer.diffuseTexture.name;
        return layer.name;
    }
}
