using System.Collections.Generic;
using UnityEngine;

public sealed class WorldItemPickupHoverResolver
{
    public const string OutlineObjectName = "__WorldItemPickupHoverOutline";

    private readonly List<CacheEntry> entries = new List<CacheEntry>(96);
    private readonly List<WorldItemPickupHoverCandidate> candidates = new List<WorldItemPickupHoverCandidate>(96);
    private int synchronizedRevision = int.MinValue;

    public int SynchronizedRevision => synchronizedRevision;
    public int CacheRebuildCount { get; private set; }
    public int EntryCount => entries.Count;

    public bool Synchronize(int activeSetRevision, IReadOnlyList<WorldItemPickup> pickups)
    {
        if (activeSetRevision == synchronizedRevision)
            return false;

        entries.Clear();
        for (int i = 0; pickups != null && i < pickups.Count; i++)
        {
            WorldItemPickup pickup = pickups[i];
            if (pickup == null)
                continue;

            WorldPickupPresentation presentation = pickup.GetComponent<WorldPickupPresentation>();
            Transform visualRoot = presentation != null ? presentation.VisualRoot : pickup.transform;
            Renderer[] discovered = visualRoot.GetComponentsInChildren<Renderer>(true);
            var renderers = new List<Renderer>(discovered.Length);
            for (int rendererIndex = 0; rendererIndex < discovered.Length; rendererIndex++)
            {
                Renderer renderer = discovered[rendererIndex];
                if (!IsModelRenderer(renderer))
                    continue;

                renderers.Add(renderer);
            }

            entries.Add(new CacheEntry(pickup, pickup.GetInstanceID(), renderers.ToArray()));
        }

        entries.Sort((left, right) => left.InstanceId.CompareTo(right.InstanceId));
        synchronizedRevision = activeSetRevision;
        CacheRebuildCount++;
        return true;
    }

    public void Clear()
    {
        entries.Clear();
        candidates.Clear();
        synchronizedRevision = int.MinValue;
    }

    public bool Contains(WorldItemPickup pickup)
    {
        int instanceId = pickup != null ? pickup.GetInstanceID() : 0;
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].InstanceId == instanceId && entries[i].Pickup != null)
                return true;
        }

        return false;
    }

    public bool TryGetRenderers(WorldItemPickup pickup, out Renderer[] renderers)
    {
        int instanceId = pickup != null ? pickup.GetInstanceID() : 0;
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].InstanceId != instanceId)
                continue;

            renderers = entries[i].Renderers;
            return true;
        }

        renderers = null;
        return false;
    }

    public WorldItemPickup Resolve(
        Camera camera,
        Vector2 mouseScreenPosition,
        float paddingPixels,
        float minimumSizePixels,
        IReadOnlyDictionary<int, float> pickupDistances)
    {
        candidates.Clear();
        if (camera == null)
            return null;

        Rect screenRect = camera.pixelRect;
        for (int i = 0; i < entries.Count; i++)
        {
            CacheEntry entry = entries[i];
            if (entry.Pickup == null
                || !TryProjectPickupBounds(camera, screenRect, entry.Renderers, out Rect rawBounds, out float depth))
            {
                continue;
            }

            Rect hitBounds = ExpandAndEnsureMinimum(rawBounds, screenRect, paddingPixels, minimumSizePixels);
            if (!hitBounds.Contains(mouseScreenPosition))
                continue;

            float pickupDistance = pickupDistances != null
                && pickupDistances.TryGetValue(entry.InstanceId, out float resolvedDistance)
                    ? resolvedDistance
                    : float.MaxValue;
            candidates.Add(new WorldItemPickupHoverCandidate(
                entry.Pickup,
                entry.InstanceId,
                rawBounds,
                hitBounds,
                depth,
                pickupDistance));
        }

        int selectedIndex = ResolveCandidateIndex(candidates, mouseScreenPosition);
        return selectedIndex >= 0 ? candidates[selectedIndex].Pickup : null;
    }

    /// <summary>
    /// 현재 카메라에 실제 모델 bounds가 한 픽셀이라도 교차하는 pickup만 수집한다.
    /// 명찰 앵커나 클러스터 대표점이 아니라 hover와 같은 Renderer 투영 경로를 사용한다.
    /// </summary>
    public int CollectScreenVisibleInstanceIds(Camera camera, HashSet<int> output)
    {
        if (output == null)
            return 0;

        output.Clear();
        if (camera == null || camera.pixelRect.width <= 0f || camera.pixelRect.height <= 0f)
            return 0;

        Rect screenRect = camera.pixelRect;
        for (int i = 0; i < entries.Count; i++)
        {
            CacheEntry entry = entries[i];
            if (entry.Pickup == null
                || !TryProjectPickupBounds(camera, screenRect, entry.Renderers, out _, out _))
            {
                continue;
            }

            output.Add(entry.InstanceId);
        }

        return output.Count;
    }

    public static Rect ExpandAndEnsureMinimum(
        Rect rawBounds,
        Rect screenRect,
        float paddingPixels,
        float minimumSizePixels)
    {
        float padding = Mathf.Max(0f, paddingPixels);
        float minimum = Mathf.Max(1f, minimumSizePixels);
        Vector2 center = rawBounds.center;
        Vector2 size = new Vector2(
            Mathf.Max(minimum, rawBounds.width + padding * 2f),
            Mathf.Max(minimum, rawBounds.height + padding * 2f));
        Rect expanded = new Rect(center - size * 0.5f, size);
        return Intersect(expanded, screenRect);
    }

    public static int ResolveCandidateIndex(
        IReadOnlyList<WorldItemPickupHoverCandidate> source,
        Vector2 mouseScreenPosition)
    {
        int selected = -1;
        for (int i = 0; source != null && i < source.Count; i++)
        {
            if (!source[i].HitBounds.Contains(mouseScreenPosition))
                continue;
            if (selected < 0 || Compare(source[i], source[selected], mouseScreenPosition) < 0)
                selected = i;
        }

        return selected;
    }

    private static int Compare(
        WorldItemPickupHoverCandidate left,
        WorldItemPickupHoverCandidate right,
        Vector2 mouseScreenPosition)
    {
        bool leftRaw = left.RawBounds.Contains(mouseScreenPosition);
        bool rightRaw = right.RawBounds.Contains(mouseScreenPosition);
        int compare = rightRaw.CompareTo(leftRaw); // raw 직접 포함이 padding-only보다 우선
        if (compare != 0)
            return compare;

        compare = NormalizedDistance(left.RawBounds, mouseScreenPosition)
            .CompareTo(NormalizedDistance(right.RawBounds, mouseScreenPosition));
        if (compare != 0)
            return compare;

        compare = left.CameraDepth.CompareTo(right.CameraDepth);
        if (compare != 0)
            return compare;

        compare = left.PickupDistance.CompareTo(right.PickupDistance);
        return compare != 0 ? compare : left.InstanceId.CompareTo(right.InstanceId);
    }

    private static float NormalizedDistance(Rect bounds, Vector2 point)
    {
        float halfWidth = Mathf.Max(0.5f, bounds.width * 0.5f);
        float halfHeight = Mathf.Max(0.5f, bounds.height * 0.5f);
        Vector2 delta = point - bounds.center;
        float x = delta.x / halfWidth;
        float y = delta.y / halfHeight;
        return x * x + y * y;
    }

    private static bool TryProjectPickupBounds(
        Camera camera,
        Rect screenRect,
        IReadOnlyList<Renderer> renderers,
        out Rect rawBounds,
        out float nearestDepth)
    {
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        nearestDepth = float.MaxValue;
        bool found = false;
        for (int rendererIndex = 0; renderers != null && rendererIndex < renderers.Count; rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            if (renderer == null
                || !renderer.enabled
                || renderer.forceRenderingOff
                || !renderer.gameObject.activeInHierarchy
                || (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0)
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 worldPoint = new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z);
                        Vector3 screenPoint = camera.WorldToScreenPoint(worldPoint);
                        if (screenPoint.z <= 0f)
                            continue;

                        minX = Mathf.Min(minX, screenPoint.x);
                        minY = Mathf.Min(minY, screenPoint.y);
                        maxX = Mathf.Max(maxX, screenPoint.x);
                        maxY = Mathf.Max(maxY, screenPoint.y);
                        nearestDepth = Mathf.Min(nearestDepth, screenPoint.z);
                        found = true;
                    }
                }
            }
        }

        if (!found)
        {
            rawBounds = default;
            nearestDepth = float.MaxValue;
            return false;
        }

        Rect projected = Rect.MinMaxRect(minX, minY, maxX, maxY);
        rawBounds = Intersect(projected, screenRect);
        return rawBounds.width > 0f && rawBounds.height > 0f;
    }

    private static Rect Intersect(Rect left, Rect right)
    {
        float xMin = Mathf.Max(left.xMin, right.xMin);
        float yMin = Mathf.Max(left.yMin, right.yMin);
        float xMax = Mathf.Min(left.xMax, right.xMax);
        float yMax = Mathf.Min(left.yMax, right.yMax);
        return xMax > xMin && yMax > yMin
            ? Rect.MinMaxRect(xMin, yMin, xMax, yMax)
            : Rect.zero;
    }

    private static bool IsModelRenderer(Renderer renderer)
    {
        if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)
            return false;
        if (renderer == null || renderer.gameObject.name == OutlineObjectName)
            return false;
        if (renderer.GetComponentInParent<ParticleSystem>() != null)
            return false;

        return renderer.GetComponent<TrailRenderer>() == null
            && renderer.GetComponent<LineRenderer>() == null;
    }

    private readonly struct CacheEntry
    {
        public WorldItemPickup Pickup { get; }
        public int InstanceId { get; }
        public Renderer[] Renderers { get; }

        public CacheEntry(WorldItemPickup pickup, int instanceId, Renderer[] renderers)
        {
            Pickup = pickup;
            InstanceId = instanceId;
            Renderers = renderers;
        }
    }
}

public readonly struct WorldItemPickupHoverCandidate
{
    public WorldItemPickup Pickup { get; }
    public int InstanceId { get; }
    public Rect RawBounds { get; }
    public Rect HitBounds { get; }
    public float CameraDepth { get; }
    public float PickupDistance { get; }

    public WorldItemPickupHoverCandidate(
        WorldItemPickup pickup,
        int instanceId,
        Rect rawBounds,
        Rect hitBounds,
        float cameraDepth,
        float pickupDistance)
    {
        Pickup = pickup;
        InstanceId = instanceId;
        RawBounds = rawBounds;
        HitBounds = hitBounds;
        CameraDepth = cameraDepth;
        PickupDistance = pickupDistance;
    }
}
