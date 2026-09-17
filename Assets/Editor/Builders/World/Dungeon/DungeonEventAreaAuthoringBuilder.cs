using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

internal static class DungeonEventAreaAuthoringBuilder
{
    internal static readonly Vector2Int MinimumDimensions = new(4, 4);
    internal static readonly IReadOnlyList<Vector2Int> SupportedDimensions =
        BuildSupportedDimensions();

    private const float PreferredDoorwayInset = 1.25f;
    private const float SearchRadius = 8f;
    private const float SearchStep = 0.5f;
    private const float FootprintInset = 0.18f;
    private const int SamplesPerAxis = 5;
    private const float MaximumFloorVariation = 0.20f;
    private const float MaximumExpectedFloorDelta = 0.75f;
    private const float MinimumFloorNormalDot = 0.80f;
    private const float AreaSurfaceLift = 0.025f;
    private const float MaximumSampleSpacing = 1f;
    private static readonly IReadOnlyList<Vector2> SearchOffsets =
        BuildSearchOffsets();

    internal readonly struct BuildResult
    {
        public BuildResult(
            int candidateCount,
            Vector3 localPosition,
            float floorVariation,
            string message)
        {
            CandidateCount = candidateCount;
            LocalPosition = localPosition;
            FloorVariation = floorVariation;
            Message = message;
        }

        public int CandidateCount { get; }
        public Vector3 LocalPosition { get; }
        public float FloorVariation { get; }
        public string Message { get; }
    }

    internal static BuildResult RebuildSizeCandidates(
        GameObject tileRoot,
        Transform eventAreaRoot,
        Transform primaryAnchor,
        int groundLayer)
    {
        if (tileRoot == null
            || eventAreaRoot == null
            || primaryAnchor == null)
        {
            throw new InvalidOperationException(
                "EventArea Authoring 필수 참조 누락");
        }

        Collider[] groundColliders = GetGroundColliders(
            tileRoot,
            eventAreaRoot,
            groundLayer);
        if (groundColliders.Length == 0)
        {
            return new BuildResult(
                0,
                Vector3.zero,
                0f,
                "Ground Collider 누락");
        }

        Vector3 preferred =
            primaryAnchor.position
            + primaryAnchor.forward * PreferredDoorwayInset;
        float expectedFloorY = primaryAnchor.position.y - 0.08f;
        int candidateCount = 0;
        Vector3 firstLocalPosition = Vector3.zero;
        float maximumFloorVariation = 0f;
        for (int dimensionIndex = 0;
             dimensionIndex < SupportedDimensions.Count;
             dimensionIndex++)
        {
            Vector2Int dimensions = SupportedDimensions[dimensionIndex];
            if (!TryFindPlacement(
                    tileRoot.transform,
                    groundColliders,
                    preferred,
                    expectedFloorY,
                    dimensions,
                    out Vector3 areaPosition,
                    out Quaternion areaRotation,
                    out float floorVariation))
            {
                continue;
            }

            string sizeId =
                $"Size_{dimensions.x:00}x{dimensions.y:00}";
            GameObject areaObject = new(
                $"Area_{dimensions.x}x{dimensions.y}");
            areaObject.transform.SetParent(eventAreaRoot, true);
            areaObject.transform.SetPositionAndRotation(
                areaPosition,
                areaRotation);
            DungeonEventAreaAuthoring authoring =
                areaObject.AddComponent<DungeonEventAreaAuthoring>();
            authoring.Configure(sizeId, dimensions);
            if (candidateCount == 0)
            {
                firstLocalPosition =
                    tileRoot.transform.InverseTransformPoint(areaPosition);
            }
            candidateCount++;
            maximumFloorVariation = Mathf.Max(
                maximumFloorVariation,
                floorVariation);
        }

        if (candidateCount > 0)
        {
            return new BuildResult(
                candidateCount,
                firstLocalPosition,
                maximumFloorVariation,
                $"4x4~8x8 후보 {candidateCount}개 생성");
        }

        return new BuildResult(
            0,
            Vector3.zero,
            0f,
            $"반경 {SearchRadius:F1}m 안에 유효한 4x4 바닥 없음");
    }

    internal static bool ValidateAuthoredArea(
        GameObject tileRoot,
        DungeonEventAreaAuthoring area,
        int groundLayer,
        out float floorVariation,
        out string error)
    {
        floorVariation = 0f;
        error = string.Empty;
        if (tileRoot == null || area == null)
        {
            error = "타일 또는 EventArea 누락";
            return false;
        }

        Collider[] groundColliders = GetGroundColliders(
            tileRoot,
            area.transform.parent,
            groundLayer);
        return TryValidateFootprint(
            groundColliders,
            area.transform.position,
            area.transform.rotation,
            area.DimensionsMeters,
            out floorVariation,
            out error);
    }

    private static bool TryFindPlacement(
        Transform tileTransform,
        IReadOnlyList<Collider> groundColliders,
        Vector3 preferred,
        float expectedFloorY,
        Vector2Int dimensions,
        out Vector3 areaPosition,
        out Quaternion areaRotation,
        out float floorVariation)
    {
        areaPosition = Vector3.zero;
        areaRotation = tileTransform.rotation;
        floorVariation = 0f;
        int orientationCount = dimensions.x == dimensions.y ? 1 : 2;
        for (int offsetIndex = 0;
             offsetIndex < SearchOffsets.Count;
             offsetIndex++)
        {
            Vector2 offset = SearchOffsets[offsetIndex];
            Vector3 sampleCenter = preferred
                + tileTransform.right * offset.x
                + tileTransform.forward * offset.y;
            if (!TryResolveCenterFloor(
                    groundColliders,
                    sampleCenter,
                    expectedFloorY,
                    out float floorY))
            {
                continue;
            }

            Vector3 candidatePosition = new(
                sampleCenter.x,
                floorY + AreaSurfaceLift,
                sampleCenter.z);
            for (int orientationIndex = 0;
                 orientationIndex < orientationCount;
                 orientationIndex++)
            {
                Quaternion candidateRotation =
                    tileTransform.rotation
                    * Quaternion.Euler(
                        0f,
                        orientationIndex == 0 ? 0f : 90f,
                        0f);
                if (!TryValidateFootprint(
                        groundColliders,
                        candidatePosition,
                        candidateRotation,
                        dimensions,
                        out floorVariation,
                        out _))
                {
                    continue;
                }

                areaPosition = candidatePosition;
                areaRotation = candidateRotation;
                return true;
            }
        }

        return false;
    }

    private static Collider[] GetGroundColliders(
        GameObject tileRoot,
        Transform eventAreaRoot,
        int groundLayer)
    {
        return tileRoot
            .GetComponentsInChildren<Collider>(true)
            .Where(collider =>
                collider != null
                && collider.enabled
                && !collider.isTrigger
                && collider.gameObject.layer == groundLayer
                && (eventAreaRoot == null
                    || !collider.transform.IsChildOf(eventAreaRoot))
                && collider.GetComponentInParent<DungeonStairRampProxy>()
                    == null)
            .ToArray();
    }

    private static bool TryResolveCenterFloor(
        IReadOnlyList<Collider> groundColliders,
        Vector3 center,
        float expectedFloorY,
        out float floorY)
    {
        return TrySampleGround(
            groundColliders,
            center,
            expectedFloorY,
            MaximumExpectedFloorDelta,
            out floorY,
            out _);
    }

    private static bool TryValidateFootprint(
        IReadOnlyList<Collider> groundColliders,
        Vector3 areaPosition,
        Quaternion areaRotation,
        Vector2Int dimensions,
        out float floorVariation,
        out string error)
    {
        floorVariation = 0f;
        error = string.Empty;
        if (groundColliders == null || groundColliders.Count == 0)
        {
            error = "Ground Collider 누락";
            return false;
        }

        float expectedFloorY = areaPosition.y - AreaSurfaceLift;
        float minimumY = float.PositiveInfinity;
        float maximumY = float.NegativeInfinity;
        float halfWidth = dimensions.x * 0.5f - FootprintInset;
        float halfDepth = dimensions.y * 0.5f - FootprintInset;
        int xSamples = Mathf.Max(
            SamplesPerAxis,
            Mathf.CeilToInt(dimensions.x / MaximumSampleSpacing) + 1);
        int zSamples = Mathf.Max(
            SamplesPerAxis,
            Mathf.CeilToInt(dimensions.y / MaximumSampleSpacing) + 1);
        Vector3 areaRight = areaRotation * Vector3.right;
        Vector3 areaForward = areaRotation * Vector3.forward;
        for (int xIndex = 0; xIndex < xSamples; xIndex++)
        {
            float x01 = xIndex / (xSamples - 1f);
            float localX = Mathf.Lerp(-halfWidth, halfWidth, x01);
            for (int zIndex = 0; zIndex < zSamples; zIndex++)
            {
                float z01 = zIndex / (zSamples - 1f);
                float localZ = Mathf.Lerp(-halfDepth, halfDepth, z01);
                Vector3 samplePosition = areaPosition
                    + areaRight * localX
                    + areaForward * localZ;
                if (!TrySampleGround(
                        groundColliders,
                        samplePosition,
                        expectedFloorY,
                        MaximumFloorVariation,
                        out float hitY,
                        out float normalDot))
                {
                    error = $"바닥 표본 실패: x={localX:F2}, z={localZ:F2}";
                    return false;
                }

                if (normalDot < MinimumFloorNormalDot)
                {
                    error = $"바닥 경사 초과: normalDot={normalDot:F3}";
                    return false;
                }

                minimumY = Mathf.Min(minimumY, hitY);
                maximumY = Mathf.Max(maximumY, hitY);
            }
        }

        floorVariation = maximumY - minimumY;
        if (floorVariation > MaximumFloorVariation)
        {
            error = $"영역 바닥 높이 편차 초과: {floorVariation:F3}m";
            return false;
        }

        return true;
    }

    private static bool TrySampleGround(
        IReadOnlyList<Collider> groundColliders,
        Vector3 position,
        float expectedFloorY,
        float maximumDelta,
        out float floorY,
        out float normalDot)
    {
        floorY = 0f;
        normalDot = 0f;
        bool found = false;
        float bestDelta = float.PositiveInfinity;
        Ray ray = new(
            new Vector3(position.x, expectedFloorY + 1.5f, position.z),
            Vector3.down);
        for (int i = 0; i < groundColliders.Count; i++)
        {
            Collider collider = groundColliders[i];
            if (collider == null
                || !collider.Raycast(ray, out RaycastHit hit, 3f))
            {
                continue;
            }

            float delta = Mathf.Abs(hit.point.y - expectedFloorY);
            if (delta > maximumDelta || delta >= bestDelta)
                continue;

            bestDelta = delta;
            floorY = hit.point.y;
            normalDot = Vector3.Dot(hit.normal.normalized, Vector3.up);
            found = true;
        }

        return found;
    }

    private static IReadOnlyList<Vector2Int> BuildSupportedDimensions()
    {
        List<Vector2Int> dimensions = new();
        for (int width = DungeonEventAreaAuthoring.MinimumDimension;
             width <= DungeonEventAreaAuthoring.MaximumDimension;
             width++)
        {
            for (int depth = width;
                 depth <= DungeonEventAreaAuthoring.MaximumDimension;
                 depth++)
            {
                dimensions.Add(new Vector2Int(width, depth));
            }
        }

        return dimensions;
    }

    private static IReadOnlyList<Vector2> BuildSearchOffsets()
    {
        List<Vector2> offsets = new();
        int stepCount = Mathf.CeilToInt(SearchRadius / SearchStep);
        for (int x = -stepCount; x <= stepCount; x++)
        {
            for (int z = -stepCount; z <= stepCount; z++)
            {
                Vector2 offset = new(x * SearchStep, z * SearchStep);
                if (offset.sqrMagnitude
                    <= SearchRadius * SearchRadius + 0.001f)
                {
                    offsets.Add(offset);
                }
            }
        }

        offsets.Sort((left, right) =>
        {
            int distanceCompare =
                left.sqrMagnitude.CompareTo(right.sqrMagnitude);
            if (distanceCompare != 0)
                return distanceCompare;
            int xCompare = left.x.CompareTo(right.x);
            return xCompare != 0
                ? xCompare
                : left.y.CompareTo(right.y);
        });
        return offsets;
    }
}
