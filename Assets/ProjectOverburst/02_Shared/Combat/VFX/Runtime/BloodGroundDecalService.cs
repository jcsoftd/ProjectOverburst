using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Ground marks have their own pool and lifetime; supplier blood graphs remain untouched.
[DefaultExecutionOrder(-790)]
public sealed class BloodGroundDecalService : MonoBehaviour
{
    public const int Capacity = 96;
    public const int PerFrameLimit = 4;
    public const float HoldSeconds = 15f;
    public const float FadeSeconds = 5f;
    private const int PendingCapacity = 128;
    private const float LandingDelay = .16f;
    private const float MaxLate = .45f;
    private const float MinimumNormalY = .7f;
    private const float MinimumSpacing = .32f;
    private const float ProjectionDepth = .07f;
    private static readonly int MainColor = Shader.PropertyToID("_MainColor");
    private static readonly int SecondaryColor = Shader.PropertyToID("_SecondaryColor");
    private static readonly int SpecularColor = Shader.PropertyToID("_SpecularColor");
    private static readonly int SpecularValue = Shader.PropertyToID("_SpecularValue");
    private static readonly RaycastHit[] GroundHits = new RaycastHit[16];

    private struct Pending
    {
        public BloodHitProfile Profile;
        public Vector3 Point, Normal, Tangent;
        public CombatImpactShape Shape;
        public float Size, At;
        public bool Lethal;
        public int Variant;
    }

    private struct Slot
    {
        public DecalProjector Projector;
        public Vector3 Point;
        public float Started;
        public bool Active;
    }

    private readonly Pending[] pending = new Pending[PendingCapacity];
    private readonly Slot[] slots = new Slot[Capacity];
    private readonly int[] nextVariant = new int[4];
    private readonly Dictionary<long, Material> materials = new Dictionary<long, Material>();
    private int pendingCount;
    private int groundMask;
    private BloodHitCatalog catalog;

    public int RequestedCount { get; private set; }
    public int ShownCount { get; private set; }
    public int ActiveCount { get; private set; }
    public int PeakActiveCount { get; private set; }
    public int SkippedNoGroundCount { get; private set; }
    public int SkippedSpatialCount { get; private set; }
    public int SkippedQueueCount { get; private set; }
    public int SkippedLateCount { get; private set; }
    public int SkippedPoolCount { get; private set; }
    public int MaterialVariantCount => materials.Count;

    public void Configure(BloodHitCatalog source)
    {
        if (catalog != null) return;
        catalog = source;
        groundMask = LayerMask.GetMask("Ground", "Default");
        if (!ValidTemplate(source.directionalDecal) || !ValidTemplate(source.spotDecal)
            || !ValidTemplate(source.splatterDecal) || !ValidTemplate(source.puddleDecal)
            || !ValidCollection(source.sweepDecals) || !ValidCollection(source.thrustDecals)
            || !ValidCollection(source.downwardDecals) || !ValidCollection(source.lethalDecals))
        {
            Debug.LogError("[BloodGroundDecalService] Blood decal catalog is incomplete.", this);
            catalog = null;
            return;
        }

        for (int i = 0; i < Capacity; i++)
        {
            var child = new GameObject("Blood Ground " + i);
            child.SetActive(false);
            child.transform.SetParent(transform, false);
            slots[i].Projector = child.AddComponent<DecalProjector>();
        }
    }

    private static bool ValidTemplate(GameObject prefab)
        => prefab && prefab.GetComponent<DecalProjector>()
            && prefab.GetComponent<DecalProjector>().material;

    private static bool ValidCollection(GameObject[] prefabs)
    {
        if (prefabs == null || prefabs.Length == 0) return false;
        foreach (GameObject prefab in prefabs)
            if (!ValidTemplate(prefab)) return false;
        return true;
    }

    public void Request(BloodHitProfile profile, Vector3 hitPoint, Vector3 direction,
        CombatImpactShape shape, float size, int priority)
    {
        if (catalog == null || !profile || profile.suppressBlood) return;
        RequestedCount++;
        if (!TryGround(hitPoint, direction, size, out Vector3 point, out Vector3 normal))
        {
            SkippedNoGroundCount++;
            return;
        }
        if (Overlaps(point))
        {
            SkippedSpatialCount++;
            return;
        }
        if (pendingCount >= PendingCapacity)
        {
            SkippedQueueCount++;
            return;
        }

        Vector3 tangent = Vector3.ProjectOnPlane(direction, normal);
        if (tangent.sqrMagnitude < .0001f)
            tangent = Vector3.ProjectOnPlane(Vector3.forward, normal);
        tangent.Normalize();
        pending[pendingCount++] = new Pending
        {
            Profile = profile,
            Point = point,
            Normal = normal,
            Tangent = tangent,
            Shape = shape,
            Size = Mathf.Clamp(size, .55f, 1.95f),
            Lethal = priority >= 2,
            Variant = nextVariant[priority >= 2 ? 3 : (int)shape]++,
            At = Time.time + LandingDelay
        };
    }

    private bool TryGround(Vector3 hitPoint, Vector3 direction, float size,
        out Vector3 point, out Vector3 normal)
    {
        Vector3 horizontal = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (horizontal.sqrMagnitude > .0001f)
            horizontal.Normalize();
        Vector3 origin = hitPoint + Vector3.up * .6f + horizontal * (.22f * Mathf.Clamp(size, .55f, 1.5f));
        int hits = Physics.RaycastNonAlloc(origin, Vector3.down, GroundHits,
            Mathf.Clamp(size * 2f + 5f, 5f, 10f), groundMask, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        point = default;
        normal = default;
        for (int i = 0; i < hits; i++)
        {
            RaycastHit hit = GroundHits[i];
            if (!hit.collider || hit.normal.y < MinimumNormalY || hit.distance >= nearest)
                continue;
            if (hit.collider.GetComponentInParent<CombatHealth>()
                || hit.collider.GetComponentInParent<PlayerActorRuntime>())
                continue;
            nearest = hit.distance;
            point = hit.point;
            normal = hit.normal;
        }
        return nearest < float.PositiveInfinity;
    }

    private bool Overlaps(Vector3 point)
    {
        float sqrSpacing = MinimumSpacing * MinimumSpacing;
        for (int i = 0; i < pendingCount; i++)
            if ((pending[i].Point - point).sqrMagnitude < sqrSpacing) return true;
        for (int i = 0; i < Capacity; i++)
            if (slots[i].Active && (slots[i].Point - point).sqrMagnitude < sqrSpacing) return true;
        return false;
    }

    private void LateUpdate()
    {
        float now = Time.time;
        for (int i = 0; i < Capacity; i++)
        {
            if (!slots[i].Active) continue;
            float age = now - slots[i].Started;
            if (age >= HoldSeconds + FadeSeconds)
            {
                Release(i);
                continue;
            }
            if (age > HoldSeconds)
                slots[i].Projector.fadeFactor = 1f - (age - HoldSeconds) / FadeSeconds;
        }

        int shown = 0;
        for (int i = 0; i < pendingCount; i++)
        {
            Pending request = pending[i];
            if (now < request.At) continue;
            if (shown >= PerFrameLimit && now - request.At <= MaxLate) continue;
            pending[i] = pending[--pendingCount];
            i--;
            if (now - request.At > MaxLate)
            {
                SkippedLateCount++;
                continue;
            }
            if (shown >= PerFrameLimit) continue;
            if (!Show(request, now))
            {
                SkippedPoolCount++;
                continue;
            }
            shown++;
        }
    }

    private bool Show(Pending request, float now)
    {
        int index = -1;
        for (int i = 0; i < Capacity; i++)
            if (!slots[i].Active) { index = i; break; }
        if (index < 0) return false; // Existing marks always retain their full 15+5 seconds.

        GameObject prefab = catalog.ResolveDecal(request.Shape, request.Lethal, request.Variant);
        DecalProjector source = prefab ? prefab.GetComponent<DecalProjector>() : null;
        if (!source || !source.material) return false;

        DecalProjector projector = slots[index].Projector;
        float scale = request.Lethal ? Mathf.Clamp(request.Size, 1f, 1.8f)
            : Mathf.Max(.7f, .85f * request.Size);
        projector.gameObject.SetActive(false);
        projector.transform.SetPositionAndRotation(
            request.Point + request.Normal * .015f,
            Quaternion.LookRotation(-request.Normal, request.Tangent));
        projector.material = TintedMaterial(source.material, request.Profile);
        projector.size = new Vector3(
            Mathf.Clamp(source.size.x * scale, .65f, 2.4f),
            Mathf.Clamp(source.size.y * scale, .65f, 2.4f), ProjectionDepth);
        projector.pivot = Vector3.zero;
        projector.drawDistance = Mathf.Min(source.drawDistance, 40f);
        projector.fadeScale = source.fadeScale;
        projector.fadeFactor = 1f;
        projector.gameObject.SetActive(true);

        slots[index].Point = request.Point;
        slots[index].Started = now;
        slots[index].Active = true;
        ActiveCount++;
        PeakActiveCount = Mathf.Max(PeakActiveCount, ActiveCount);
        ShownCount++;
        return true;
    }

    private Material TintedMaterial(Material source, BloodHitProfile profile)
    {
        long key = ((long)source.GetInstanceID() << 32) ^ (uint)profile.GetInstanceID();
        if (!materials.TryGetValue(key, out Material material) || !material)
        {
            material = new Material(source)
            {
                name = source.name + " • " + profile.name + " pooled",
                hideFlags = HideFlags.HideAndDontSave
            };
            materials[key] = material;
        }
        if (material.HasProperty(MainColor)) material.SetColor(MainColor, profile.mainColor.linear);
        if (material.HasProperty(SecondaryColor)) material.SetColor(SecondaryColor, profile.secondaryColor.linear);
        if (material.HasProperty(SpecularColor)) material.SetColor(SpecularColor, profile.specularColor.linear);
        if (material.HasProperty(SpecularValue)) material.SetFloat(SpecularValue, Mathf.Min(profile.specular, .4f));
        return material;
    }

    private void Release(int index)
    {
        slots[index].Projector.gameObject.SetActive(false);
        slots[index].Projector.fadeFactor = 1f;
        slots[index].Active = false;
        ActiveCount--;
    }

    private void OnEnable()
    {
        SceneManager.activeSceneChanged += SceneChanged;
        SceneManager.sceneUnloaded += SceneUnloaded;
    }

    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= SceneChanged;
        SceneManager.sceneUnloaded -= SceneUnloaded;
        Clear();
    }

    private void OnDestroy()
    {
        foreach (Material material in materials.Values)
        {
            if (!material) continue;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }
        materials.Clear();
    }

    private void SceneChanged(Scene previous, Scene current) => Clear();
    private void SceneUnloaded(Scene scene) => Clear();

    private void Clear()
    {
        pendingCount = 0;
        for (int i = 0; i < Capacity; i++)
            if (slots[i].Active && slots[i].Projector)
                Release(i);
        ActiveCount = 0;
    }
}
