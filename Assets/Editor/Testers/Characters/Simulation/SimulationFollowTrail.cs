using System.Collections.Generic;
using UnityEngine;

public sealed class SimulationFollowTrail
{
    private const float DirectionEpsilon = 0.0001f;
    private const float CornerCaptureAngle = 12f;
    private const float CornerCaptureDistanceRatio = 0.25f;

    private readonly List<Vector3> points = new();
    private Vector3 lastObservedPosition;
    private Vector3 lastObservedDirection;
    private bool hasObservation;

    public int Count => points.Count;

    public void Reset(Vector3 position)
    {
        points.Clear();
        points.Add(position);
        lastObservedPosition = position;
        lastObservedDirection = Vector3.zero;
        hasObservation = true;
    }

    public void Record(Vector3 position, float sampleDistance, float retentionDistance)
    {
        if (points.Count == 0)
        {
            Reset(position);
            return;
        }

        sampleDistance = Mathf.Max(0.01f, sampleDistance);
        if (!hasObservation)
        {
            lastObservedPosition = points[points.Count - 1];
            lastObservedDirection = Vector3.zero;
            hasObservation = true;
        }

        Vector3 observedDelta = position - lastObservedPosition;
        observedDelta.y = 0f;
        float observedDistance = observedDelta.magnitude;
        retentionDistance = Mathf.Max(sampleDistance, retentionDistance);
        if (observedDistance > retentionDistance)
        {
            Reset(position); // 순간이동 구간은 추종 경로로 남기지 않는다.
            return;
        }

        if (observedDistance <= DirectionEpsilon)
        {
            lastObservedPosition = position;
            return;
        }

        Vector3 observedDirection = observedDelta / observedDistance;
        if (lastObservedDirection.sqrMagnitude > DirectionEpsilon
            && Vector3.Angle(lastObservedDirection, observedDirection) >= CornerCaptureAngle)
        {
            float cornerDistance = PlanarDistance(points[points.Count - 1], lastObservedPosition);
            if (cornerDistance >= sampleDistance * CornerCaptureDistanceRatio)
                points.Add(lastObservedPosition); // 회전 직전의 실제 모서리를 보존
        }

        AppendDistanceSamples(position, sampleDistance);
        lastObservedPosition = position;
        lastObservedDirection = observedDirection;
        Trim(retentionDistance, position);
    }

    public bool TryGetPoseBehind(
        Vector3 headPosition,
        float distance,
        out Vector3 point,
        out Vector3 forward)
    {
        point = points.Count > 0 ? points[0] : headPosition;
        forward = Vector3.zero;
        if (points.Count == 0)
            return false;

        float remaining = Mathf.Max(0f, distance);
        Vector3 newer = headPosition;
        Vector3 oldestForward = Vector3.zero;
        for (int i = points.Count - 1; i >= 0; i--)
        {
            Vector3 older = points[i];
            Vector3 segment = newer - older;
            segment.y = 0f;
            float segmentLength = segment.magnitude;
            if (segmentLength <= 0.0001f)
            {
                newer = older;
                continue;
            }

            Vector3 segmentForward = segment / segmentLength;
            oldestForward = segmentForward;
            if (remaining <= segmentLength)
            {
                point = Vector3.Lerp(newer, older, remaining / segmentLength);
                forward = segmentForward;
                return true;
            }

            remaining -= segmentLength;
            newer = older;
        }

        if (oldestForward.sqrMagnitude <= 0.0001f)
            return false;

        point = newer - oldestForward * remaining; // 오래된 경로 방향으로 연장
        forward = oldestForward;
        return true;
    }

    private void AppendDistanceSamples(Vector3 position, float sampleDistance)
    {
        Vector3 sampleOrigin = points[points.Count - 1];
        Vector3 segment = position - sampleOrigin;
        segment.y = 0f;
        float planarDistance = segment.magnitude;
        while (planarDistance + DirectionEpsilon >= sampleDistance)
        {
            float t = Mathf.Clamp01(sampleDistance / planarDistance);
            sampleOrigin = Vector3.Lerp(sampleOrigin, position, t);
            points.Add(sampleOrigin);

            segment = position - sampleOrigin;
            segment.y = 0f;
            planarDistance = segment.magnitude;
        }
    }

    private void Trim(float retentionDistance, Vector3 headPosition)
    {
        retentionDistance = Mathf.Max(0.01f, retentionDistance);
        float retained = PlanarDistance(headPosition, points[points.Count - 1]);
        for (int i = points.Count - 1; i > 0; i--)
        {
            retained += PlanarDistance(points[i], points[i - 1]);
            if (retained <= retentionDistance)
                continue;

            points.RemoveRange(0, i);
            return;
        }
    }

    private static float PlanarDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

}
