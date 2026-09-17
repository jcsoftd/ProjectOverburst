using UnityEngine;

public sealed class EnemySensor : MonoBehaviour // 시야와 청각 기억 전담
{
    private const int LineOfSightHitCapacity = 32;
    private static readonly RaycastHit[] LineOfSightHits = new RaycastHit[LineOfSightHitCapacity];

    private Vector3 lastHeardPosition;
    private float nextNoticeTime;

    public Vector3 LastHeardPosition => lastHeardPosition;
    public bool CanNotice => Time.time >= nextNoticeTime;

    public bool IsWithinRange(Transform owner, Transform target, float range)
    {
        if (owner == null || target == null || !target.gameObject.activeInHierarchy)
            return false;

        Vector3 delta = target.position - owner.position;
        delta.y = 0f;
        return delta.sqrMagnitude <= range * range;
    }

    public bool CanSee(Transform owner, Transform target, float range, float fieldOfView)
    {
        if (!IsWithinRange(owner, target, range))
            return false;

        Vector3 targetDirection = target.position - owner.position;
        targetDirection.y = 0f;
        if (targetDirection.sqrMagnitude > 0.0001f && fieldOfView < 359.9f)
        {
            Vector3 forward = owner.forward;
            forward.y = 0f;
            float minimumDot = Mathf.Cos(Mathf.Clamp(fieldOfView, 1f, 360f) * 0.5f * Mathf.Deg2Rad);
            if (forward.sqrMagnitude > 0.0001f
                && Vector3.Dot(forward.normalized, targetDirection.normalized) < minimumDot)
            {
                return false;
            }
        }

        return HasLineOfSight(owner, target);
    }

    public void RememberSound(Transform target)
    {
        if (target != null)
            lastHeardPosition = target.position;
    }

    public void BeginNoticeCooldown(float duration)
    {
        nextNoticeTime = Time.time + Mathf.Max(0f, duration);
    }

    private static bool HasLineOfSight(Transform owner, Transform target)
    {
        Vector3 origin = owner.position + Vector3.up;
        Vector3 destination = target.position + Vector3.up;
        Vector3 direction = destination - origin;
        float distance = direction.magnitude;
        if (distance <= 0.001f)
            return true;

        int hitCount = Physics.RaycastNonAlloc(
            origin,
            direction / distance,
            LineOfSightHits,
            distance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        Transform nearestTransform = null;
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            Transform hitTransform = LineOfSightHits[i].transform;
            if (hitTransform == null
                || hitTransform == owner
                || hitTransform.IsChildOf(owner)
                || LineOfSightHits[i].distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = LineOfSightHits[i].distance;
            nearestTransform = hitTransform;
        }

        if (nearestTransform == null)
            return true;

        return nearestTransform == target
            || nearestTransform.IsChildOf(target)
            || target.IsChildOf(nearestTransform);
    }
}
