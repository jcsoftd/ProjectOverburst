using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal static class DungeonStairRampBuilder
{
    private const int SampleCount = 41;
    private const float RunInsetRatio = 0.04f;
    private const float SurfaceClearance = 0.025f;
    private const float RampThickness = 0.12f;
    private const float MaximumRampAngle = 44.5f;
    private const float MaximumEndpointExtension = 1.5f;

    internal readonly struct BuildResult
    {
        public BuildResult(int candidateCount, int rampCount, IReadOnlyList<string> skipped)
        {
            CandidateCount = candidateCount;
            RampCount = rampCount;
            Skipped = skipped;
        }

        public int CandidateCount { get; }
        public int RampCount { get; }
        public IReadOnlyList<string> Skipped { get; }
    }

    internal readonly struct CandidateAudit
    {
        public CandidateAudit(
            IReadOnlyList<string> acceptedPaths,
            IReadOnlyList<string> skippedDescriptions)
        {
            AcceptedPaths = acceptedPaths;
            SkippedDescriptions = skippedDescriptions;
        }

        public IReadOnlyList<string> AcceptedPaths { get; }
        public IReadOnlyList<string> SkippedDescriptions { get; }
        public int CandidateCount =>
            AcceptedPaths.Count + SkippedDescriptions.Count;
    }

    private readonly struct AxisAnalysis
    {
        public AxisAnalysis(
            bool usesX,
            int hitCount,
            float heightRange,
            float lowerOuterCoordinate,
            float upperOuterCoordinate,
            float startHeight,
            float endHeight)
        {
            UsesX = usesX;
            HitCount = hitCount;
            HeightRange = heightRange;
            LowerOuterCoordinate = lowerOuterCoordinate;
            UpperOuterCoordinate = upperOuterCoordinate;
            StartHeight = startHeight;
            EndHeight = endHeight;
        }

        public bool UsesX { get; }
        public int HitCount { get; }
        public float HeightRange { get; }
        public float LowerOuterCoordinate { get; }
        public float UpperOuterCoordinate { get; }
        public float StartHeight { get; }
        public float EndHeight { get; }
    }

    private readonly struct RampGeometry
    {
        public RampGeometry(
            Vector3 center,
            Quaternion rotation,
            float width,
            float length,
            float angle,
            float minimumSurfaceHeight)
        {
            Center = center;
            Rotation = rotation;
            Width = width;
            Length = length;
            Angle = angle;
            MinimumSurfaceHeight = minimumSurfaceHeight;
        }

        public Vector3 Center { get; }
        public Quaternion Rotation { get; }
        public float Width { get; }
        public float Length { get; }
        public float Angle { get; }
        public float MinimumSurfaceHeight { get; }
    }

    public static BuildResult Rebuild(
        GameObject prefabRoot,
        Transform rampRoot,
        int groundLayer,
        float minimumWalkableHeight)
    {
        List<MeshCollider> candidates = DiscoverCandidates(prefabRoot, rampRoot);
        List<string> skipped = new();
        int rampCount = 0;

        for (int i = 0; i < candidates.Count; i++)
        {
            MeshCollider collider = candidates[i];
            collider.enabled = true;
        }

        Physics.SyncTransforms();

        for (int i = 0; i < candidates.Count; i++)
        {
            MeshCollider collider = candidates[i];
            if (!TryAnalyzeRamp(collider, out RampGeometry geometry, out string reason))
            {
                skipped.Add(GetHierarchyPath(collider.transform) + " | " + reason);
                continue;
            }

            if (geometry.MinimumSurfaceHeight < minimumWalkableHeight)
            {
                skipped.Add(
                    GetHierarchyPath(collider.transform)
                    + " | 하층 배경 계단");
                continue;
            }

            CreateRamp(rampRoot, collider, geometry, groundLayer);
            collider.enabled = false;
            EditorUtility.SetDirty(collider);
            rampCount++;
        }

        return new BuildResult(candidates.Count, rampCount, skipped);
    }

    public static IReadOnlyList<string> GetAcceptedCandidatePaths(
        GameObject prefabRoot,
        float minimumWalkableHeight)
    {
        List<MeshCollider> candidates = DiscoverCandidates(prefabRoot, null);
        List<string> paths = new();
        List<bool> originalEnabledStates = new(candidates.Count);
        try
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                originalEnabledStates.Add(candidates[i].enabled);
                candidates[i].enabled = true;
            }

            Physics.SyncTransforms();
            for (int i = 0; i < candidates.Count; i++)
            {
                MeshCollider collider = candidates[i];
                if (TryAnalyzeRamp(
                        collider,
                        out RampGeometry geometry,
                        out _)
                    && geometry.MinimumSurfaceHeight
                        >= minimumWalkableHeight)
                {
                    paths.Add(GetHierarchyPath(collider.transform));
                }
            }
        }
        finally
        {
            for (int i = 0;
                 i < candidates.Count
                 && i < originalEnabledStates.Count;
                 i++)
            {
                candidates[i].enabled =
                    originalEnabledStates[i];
            }

            Physics.SyncTransforms();
        }

        return paths;
    }

    public static CandidateAudit AuditCandidates(
        GameObject prefabRoot,
        float minimumWalkableHeight)
    {
        List<MeshCollider> candidates = DiscoverCandidates(prefabRoot, null);
        List<bool> originalEnabledStates = new(candidates.Count);
        List<string> acceptedPaths = new();
        List<string> skippedDescriptions = new();

        try
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                originalEnabledStates.Add(candidates[i].enabled);
                candidates[i].enabled = true;
            }

            Physics.SyncTransforms();
            for (int i = 0; i < candidates.Count; i++)
            {
                MeshCollider collider = candidates[i];
                string path = GetHierarchyPath(collider.transform);
                if (!TryAnalyzeRamp(
                        collider,
                        out RampGeometry geometry,
                        out string reason))
                {
                    skippedDescriptions.Add(path + " | " + reason);
                }
                else if (geometry.MinimumSurfaceHeight
                    < minimumWalkableHeight)
                {
                    skippedDescriptions.Add(
                        path + " | 하층 배경 계단");
                }
                else
                {
                    acceptedPaths.Add(path);
                }
            }
        }
        finally
        {
            for (int i = 0;
                 i < candidates.Count && i < originalEnabledStates.Count;
                 i++)
            {
                candidates[i].enabled = originalEnabledStates[i];
            }

            Physics.SyncTransforms();
        }

        return new CandidateAudit(acceptedPaths, skippedDescriptions);
    }

    private static List<MeshCollider> DiscoverCandidates(GameObject prefabRoot, Transform rampRoot)
    {
        MeshCollider[] colliders = prefabRoot.GetComponentsInChildren<MeshCollider>(true);
        List<MeshCollider> candidates = new();

        for (int i = 0; i < colliders.Length; i++)
        {
            MeshCollider collider = colliders[i];
            if (collider == null
                || collider.sharedMesh == null
                || collider.isTrigger
                || rampRoot != null && collider.transform.IsChildOf(rampRoot)
                || !HasStairIdentity(collider)
                || HasNonWalkableStairIdentity(collider)
                || !IsPrimaryLod(collider))
            {
                continue;
            }

            candidates.Add(collider);
        }

        candidates.Sort(
            (left, right) => string.Compare(
                GetHierarchyPath(left.transform),
                GetHierarchyPath(right.transform),
                StringComparison.Ordinal));
        return candidates;
    }

    private static bool TryAnalyzeRamp(
        MeshCollider collider,
        out RampGeometry geometry,
        out string reason)
    {
        geometry = default;
        reason = string.Empty;
        Mesh mesh = collider.sharedMesh;
        Bounds bounds = mesh.bounds;
        AxisAnalysis xAxis = AnalyzeAxis(collider, bounds, true);
        AxisAnalysis zAxis = AnalyzeAxis(collider, bounds, false);
        AxisAnalysis run = xAxis.HeightRange >= zAxis.HeightRange ? xAxis : zAxis;

        if (run.HitCount < SampleCount * 0.7f)
        {
            reason = $"상판 표본 부족: {run.HitCount}/{SampleCount}";
            return false;
        }

        if (run.HeightRange < 0.10f)
        {
            reason = $"높이 변화 부족: x={xAxis.HeightRange:F3}, z={zAxis.HeightRange:F3}";
            return false;
        }

        float widthCenter = run.UsesX ? bounds.center.z : bounds.center.x;
        Vector3 localStart = run.UsesX
            ? new Vector3(run.LowerOuterCoordinate, run.StartHeight + SurfaceClearance, widthCenter)
            : new Vector3(widthCenter, run.StartHeight + SurfaceClearance, run.LowerOuterCoordinate);
        Vector3 localEnd = run.UsesX
            ? new Vector3(run.UpperOuterCoordinate, run.EndHeight + SurfaceClearance, widthCenter)
            : new Vector3(widthCenter, run.EndHeight + SurfaceClearance, run.UpperOuterCoordinate);
        Vector3 worldStart = collider.transform.TransformPoint(localStart);
        Vector3 worldEnd = collider.transform.TransformPoint(localEnd);

        if (worldEnd.y < worldStart.y)
            (worldStart, worldEnd) = (worldEnd, worldStart);

        Vector3 forward = worldEnd - worldStart;
        Vector3 horizontal = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (horizontal.sqrMagnitude <= 0.0001f)
        {
            reason = "수평 진행 방향 없음";
            return false;
        }

        float sourceAngle = Vector3.Angle(horizontal, forward);
        if (sourceAngle >= 60f)
        {
            reason = $"비보행 수직 구조: {sourceAngle:F2}도";
            return false;
        }

        if (sourceAngle > MaximumRampAngle)
        {
            float rise = Mathf.Abs(forward.y);
            float requiredHorizontalLength =
                rise / Mathf.Tan(MaximumRampAngle * Mathf.Deg2Rad);
            float extension = Mathf.Max(0f, requiredHorizontalLength - horizontal.magnitude);
            if (extension > MaximumEndpointExtension)
            {
                reason = $"급경사 보정 초과: {extension:F3}m";
                return false;
            }

            Vector3 horizontalDirection = horizontal.normalized;
            worldStart -= horizontalDirection * (extension * 0.5f);
            worldEnd += horizontalDirection * (extension * 0.5f);
            forward = worldEnd - worldStart;
        }

        float length = forward.magnitude;
        if (length <= 0.5f)
        {
            reason = $"경사 길이 부족: {length:F3}m";
            return false;
        }

        forward.Normalize();
        Vector3 localWidthAxis = run.UsesX ? Vector3.forward : Vector3.right;
        Vector3 worldWidthAxis = collider.transform.TransformVector(localWidthAxis).normalized;
        Vector3 normal = Vector3.Cross(forward, worldWidthAxis).normalized;
        if (normal.y < 0f)
        {
            worldWidthAxis = -worldWidthAxis;
            normal = -normal;
        }

        float angle = Vector3.Angle(Vector3.ProjectOnPlane(forward, Vector3.up), forward);
        if (angle > MaximumRampAngle + 0.01f)
        {
            reason = $"최종 경사 초과: {angle:F2}도";
            return false;
        }

        float localWidth = run.UsesX ? bounds.size.z : bounds.size.x;
        float worldWidthScale = collider.transform.TransformVector(localWidthAxis).magnitude;
        float width = localWidth * worldWidthScale * 0.86f;
        if (width < 0.65f)
        {
            reason = $"경사 폭 부족: {width:F3}m";
            return false;
        }

        Vector3 surfaceCenter = (worldStart + worldEnd) * 0.5f;
        Vector3 center = surfaceCenter - normal * (RampThickness * 0.5f);
        geometry = new RampGeometry(
            center,
            Quaternion.LookRotation(forward, normal),
            width,
            length,
            angle,
            Mathf.Min(worldStart.y, worldEnd.y));
        return true;
    }

    private static AxisAnalysis AnalyzeAxis(MeshCollider collider, Bounds bounds, bool usesX)
    {
        float minimum = usesX ? bounds.min.x : bounds.min.z;
        float maximum = usesX ? bounds.max.x : bounds.max.z;
        float fixedCoordinate = usesX ? bounds.center.z : bounds.center.x;
        float inset = (maximum - minimum) * RunInsetRatio;
        float sampleMinimum = minimum + inset;
        float sampleMaximum = maximum - inset;
        List<Vector2> samples = new(SampleCount);
        float minimumHeight = float.PositiveInfinity;
        float maximumHeight = float.NegativeInfinity;

        for (int i = 0; i < SampleCount; i++)
        {
            float t = SampleCount <= 1 ? 0.5f : i / (SampleCount - 1f);
            float coordinate = Mathf.Lerp(sampleMinimum, sampleMaximum, t);
            Vector3 localOrigin = usesX
                ? new Vector3(coordinate, bounds.max.y + 2f, fixedCoordinate)
                : new Vector3(fixedCoordinate, bounds.max.y + 2f, coordinate);
            Vector3 worldOrigin = collider.transform.TransformPoint(localOrigin);
            if (!collider.Raycast(new Ray(worldOrigin, Vector3.down), out RaycastHit hit, 30f))
                continue;

            float height = collider.transform.InverseTransformPoint(hit.point).y;
            samples.Add(new Vector2(coordinate, height));
            minimumHeight = Mathf.Min(minimumHeight, height);
            maximumHeight = Mathf.Max(maximumHeight, height);
        }

        if (samples.Count < SampleCount * 0.7f)
            return new AxisAnalysis(usesX, samples.Count, 0f, 0f, 0f, 0f, 0f);

        float heightRange = maximumHeight - minimumHeight;
        int minimumIndex = 0;
        int maximumIndex = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            if (samples[i].y < samples[minimumIndex].y)
                minimumIndex = i;
            if (samples[i].y > samples[maximumIndex].y)
                maximumIndex = i;
        }

        bool risesWithCoordinate = minimumIndex < maximumIndex;
        float plateauTolerance = Mathf.Max(0.005f, heightRange * 0.02f);
        float lowerOuterCoordinate = samples[minimumIndex].x;
        float upperOuterCoordinate = samples[maximumIndex].x;

        for (int i = 0; i < samples.Count; i++)
        {
            Vector2 sample = samples[i];
            if (sample.y <= minimumHeight + plateauTolerance)
            {
                lowerOuterCoordinate = risesWithCoordinate
                    ? Mathf.Min(lowerOuterCoordinate, sample.x)
                    : Mathf.Max(lowerOuterCoordinate, sample.x);
            }

            if (sample.y >= maximumHeight - plateauTolerance)
            {
                upperOuterCoordinate = risesWithCoordinate
                    ? Mathf.Max(upperOuterCoordinate, sample.x)
                    : Mathf.Min(upperOuterCoordinate, sample.x);
            }
        }

        return new AxisAnalysis(
            usesX,
            samples.Count,
            heightRange,
            lowerOuterCoordinate,
            upperOuterCoordinate,
            minimumHeight,
            maximumHeight);
    }

    private static void CreateRamp(
        Transform parent,
        MeshCollider sourceCollider,
        RampGeometry geometry,
        int groundLayer)
    {
        string path = GetHierarchyPath(sourceCollider.transform);
        string hash = Hash128.Compute(path).ToString().Substring(0, 8);
        string meshName = SanitizeName(sourceCollider.sharedMesh.name);
        GameObject ramp = new($"Ramp_{meshName}_{hash}");
        ramp.transform.SetParent(parent, true);
        ramp.transform.SetPositionAndRotation(geometry.Center, geometry.Rotation);
        ramp.transform.localScale = Vector3.one;
        ramp.layer = groundLayer;
        ramp.isStatic = true;

        BoxCollider boxCollider = ramp.AddComponent<BoxCollider>();
        boxCollider.center = Vector3.zero;
        boxCollider.size = new Vector3(geometry.Width, RampThickness, geometry.Length);
        boxCollider.isTrigger = false;

        DungeonStairRampProxy marker = ramp.AddComponent<DungeonStairRampProxy>();
        marker.Configure(path, geometry.Angle);
    }

    private static bool HasStairIdentity(MeshCollider collider)
    {
        return ContainsStairName(collider.name)
            || ContainsStairName(collider.sharedMesh != null ? collider.sharedMesh.name : string.Empty);
    }

    private static bool HasNonWalkableStairIdentity(MeshCollider collider)
    {
        string identity = collider.name;
        if (collider.sharedMesh != null)
            identity += " " + collider.sharedMesh.name;

        return identity.Contains("railing", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("decor", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrimaryLod(MeshCollider collider)
    {
        string objectName = collider.name;
        string meshName = collider.sharedMesh != null ? collider.sharedMesh.name : string.Empty;
        return !ContainsNonPrimaryLod(objectName) && !ContainsNonPrimaryLod(meshName);
    }

    private static bool ContainsStairName(string value)
    {
        return value.Contains("stair", StringComparison.OrdinalIgnoreCase)
            || value.Contains("step", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsNonPrimaryLod(string value)
    {
        return value.Contains("LOD1", StringComparison.OrdinalIgnoreCase)
            || value.Contains("LOD2", StringComparison.OrdinalIgnoreCase)
            || value.Contains("LOD3", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetHierarchyPath(Transform transform)
    {
        List<string> names = new();
        Transform current = transform;
        while (current != null)
        {
            names.Add(current.name);
            current = current.parent;
        }

        names.Reverse();
        return string.Join("/", names);
    }

    private static string SanitizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Stair";

        char[] chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_')
                chars[i] = '_';
        }

        return new string(chars);
    }
}
