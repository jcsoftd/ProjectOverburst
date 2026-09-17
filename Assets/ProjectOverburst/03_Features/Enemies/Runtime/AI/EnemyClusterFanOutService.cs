using System.Collections.Generic;
using UnityEngine;

public readonly struct EnemyClusterFanOutMemberData // 공유 군집 안의 개체 위치 정보
{
    public EnemyClusterFanOutMemberData(
        int clusterSize,
        Vector3 center,
        Vector3 forward,
        Vector3 right,
        float depth,
        float lateral,
        float rearRatio,
        float currentHalfWidth,
        float desiredHalfWidth,
        float expansionStrength,
        int suggestedSideSign)
    {
        ClusterSize = clusterSize;
        Center = center;
        Forward = forward;
        Right = right;
        Depth = depth;
        Lateral = lateral;
        RearRatio = rearRatio;
        CurrentHalfWidth = currentHalfWidth;
        DesiredHalfWidth = desiredHalfWidth;
        ExpansionStrength = expansionStrength;
        SuggestedSideSign = suggestedSideSign;
    }

    public int ClusterSize { get; }
    public Vector3 Center { get; }
    public Vector3 Forward { get; }
    public Vector3 Right { get; }
    public float Depth { get; }
    public float Lateral { get; }
    public float RearRatio { get; }
    public float CurrentHalfWidth { get; }
    public float DesiredHalfWidth { get; }
    public float ExpansionStrength { get; }
    public int SuggestedSideSign { get; }
}

public static class EnemyClusterFanOutService // 타겟별 연결 군집 중심과 앞뒤 열 공유 계산
{
    public const float SnapshotInterval = 0.25f;
    public const float ClusterLinkDistance = 2.75f;

    private static readonly Dictionary<int, TargetCache> TargetCaches =
        new Dictionary<int, TargetCache>(8);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        TargetCaches.Clear();
    }

    public static bool TryGetMemberData(
        EnemyCrowdAgent agent,
        Transform target,
        out EnemyClusterFanOutMemberData data)
    {
        data = default;
        if (agent == null || target == null || !agent.IsCrowdActive)
            return false;

        int targetId = target.GetInstanceID();
        if (!TargetCaches.TryGetValue(targetId, out TargetCache cache))
        {
            cache = new TargetCache(target);
            TargetCaches.Add(targetId, cache);
        }

        if (cache.Target != target || Time.time >= cache.NextRefreshTime)
            cache.Rebuild(target);

        return cache.Members.TryGetValue(agent, out data);
    }

    public static void ClearCache()
    {
        TargetCaches.Clear();
    }

    public static void Invalidate(Transform target)
    {
        if (target == null)
            return;

        if (TargetCaches.TryGetValue(target.GetInstanceID(), out TargetCache cache))
            cache.RequestRefresh(); // 여러 개체가 같은 프레임에 막혀도 공유 계산은 한 번만 갱신
    }

    private sealed class TargetCache
    {
        public readonly Dictionary<EnemyCrowdAgent, EnemyClusterFanOutMemberData> Members =
            new Dictionary<EnemyCrowdAgent, EnemyClusterFanOutMemberData>(64);
        private readonly List<EnemyCrowdAgent> targetAgents = new List<EnemyCrowdAgent>(64);
        private readonly HashSet<EnemyCrowdAgent> unvisited = new HashSet<EnemyCrowdAgent>();
        private readonly Queue<EnemyCrowdAgent> queue = new Queue<EnemyCrowdAgent>(64);
        private readonly List<EnemyCrowdAgent> component = new List<EnemyCrowdAgent>(64);
        private readonly List<EnemyCrowdAgent> neighborBuffer = new List<EnemyCrowdAgent>(32);

        public TargetCache(Transform target)
        {
            Target = target;
        }

        public Transform Target { get; private set; }
        public float NextRefreshTime { get; private set; }
        private int LastRefreshFrame { get; set; } = -1;

        public void RequestRefresh()
        {
            if (LastRefreshFrame == Time.frameCount)
                return;

            NextRefreshTime = 0f;
        }

        public void Rebuild(Transform target)
        {
            Target = target;
            LastRefreshFrame = Time.frameCount;
            NextRefreshTime = Time.time + SnapshotInterval;
            Members.Clear();
            targetAgents.Clear();
            unvisited.Clear();
            queue.Clear();
            component.Clear();

            EnemyCrowdService.CollectAgentsForTarget(target, targetAgents);
            for (int i = 0; i < targetAgents.Count; i++)
                unvisited.Add(targetAgents[i]);

            for (int i = 0; i < targetAgents.Count; i++)
            {
                EnemyCrowdAgent start = targetAgents[i];
                if (start == null || !unvisited.Remove(start))
                    continue;

                queue.Clear();
                component.Clear();
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    EnemyCrowdAgent current = queue.Dequeue();
                    component.Add(current);
                    EnemyCrowdService.CollectNeighbors(
                        current.SnapshotPosition,
                        ClusterLinkDistance,
                        neighborBuffer);
                    for (int neighborIndex = 0; neighborIndex < neighborBuffer.Count; neighborIndex++)
                    {
                        EnemyCrowdAgent neighbor = neighborBuffer[neighborIndex];
                        if (neighbor == null || !unvisited.Contains(neighbor))
                            continue;

                        EnemyAIController controller = neighbor.Controller;
                        if (controller == null || controller.Target != target)
                            continue;

                        unvisited.Remove(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }

                BuildComponentData(target.position);
            }
        }

        private void BuildComponentData(Vector3 targetPosition)
        {
            int count = component.Count;
            if (count <= 0)
                return;

            Vector3 center = Vector3.zero;
            for (int i = 0; i < count; i++)
                center += component[i].SnapshotPosition;
            center /= count;

            Vector3 forward = targetPosition - center;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.0001f)
                forward = Vector3.forward;
            else
                forward.Normalize();
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);

            float minimumDepth = float.PositiveInfinity;
            float maximumDepth = float.NegativeInfinity;
            float currentHalfWidth = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector3 relative = component[i].SnapshotPosition - center;
                relative.y = 0f;
                float depth = Vector3.Dot(relative, forward);
                float lateral = Vector3.Dot(relative, right);
                minimumDepth = Mathf.Min(minimumDepth, depth);
                maximumDepth = Mathf.Max(maximumDepth, depth);
                currentHalfWidth = Mathf.Max(currentHalfWidth, Mathf.Abs(lateral));
            }

            float desiredHalfWidth = Mathf.Clamp(Mathf.Sqrt(count) * 0.7f, 2f, 6f);
            float expansionNeed = Mathf.Clamp01(
                (desiredHalfWidth - currentHalfWidth)
                / Mathf.Max(0.5f, desiredHalfWidth * 0.75f));
            float sizeStrength = Mathf.Lerp(0.55f, 1f, Mathf.InverseLerp(6f, 30f, count));
            float expansionStrength = expansionNeed * sizeStrength;
            float depthRange = maximumDepth - minimumDepth;

            for (int i = 0; i < count; i++)
            {
                EnemyCrowdAgent agent = component[i];
                Vector3 relative = agent.SnapshotPosition - center;
                relative.y = 0f;
                float depth = Vector3.Dot(relative, forward);
                float lateral = Vector3.Dot(relative, right);
                float frontRatio = depthRange > 0.1f
                    ? Mathf.InverseLerp(minimumDepth, maximumDepth, depth)
                    : 0.5f;
                float rearRatio = 1f - frontRatio;
                float centerBand = Mathf.Max(0.2f, agent.BodyRadius * 0.4f);
                int suggestedSideSign = lateral > centerBand
                    ? 1
                    : lateral < -centerBand
                        ? -1
                        : EnemyApproachSteering.ResolveStableTurnSign(agent.GetInstanceID());

                Members[agent] = new EnemyClusterFanOutMemberData(
                    count,
                    center,
                    forward,
                    right,
                    depth,
                    lateral,
                    rearRatio,
                    currentHalfWidth,
                    desiredHalfWidth,
                    expansionStrength,
                    suggestedSideSign);
            }
        }
    }
}
