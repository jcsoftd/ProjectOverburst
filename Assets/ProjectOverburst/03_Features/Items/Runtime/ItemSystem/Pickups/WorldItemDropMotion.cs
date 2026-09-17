using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WorldItemDropMotion : MonoBehaviour
{
    private const float GroundProbeHeight = 4f;
    private const float GroundProbeDistance = 16f;
    private const float MinimumGroundNormalY = 0.35f;
    private const int GroundHitCapacity = 24;

    private readonly RaycastHit[] groundHits = new RaycastHit[GroundHitCapacity];

    private WorldPickupPresentation presentation;
    private Transform visualRoot;
    private Vector3 startPosition;
    private Vector3 landingPosition;
    private Quaternion startVisualRotation;
    private Quaternion settledVisualRotation;
    private float elapsed;
    private float duration;
    private float arcHeight;
    private bool started;
    private bool landed;

    public event Action Landed; // 착지 상태 변경 알림

    public bool IsLanded => !started || landed;

    public void Begin()
    {
        presentation = GetComponent<WorldPickupPresentation>();
        if (presentation == null)
            presentation = gameObject.AddComponent<WorldPickupPresentation>();

        visualRoot = presentation.VisualRoot;
        startPosition = transform.position;
        landingPosition = ResolveLandingPosition(startPosition, presentation.ScatterRadius, presentation.GroundClearance);
        startVisualRotation = visualRoot.localRotation;
        settledVisualRotation = presentation.RollSettledLocalRotation();
        duration = presentation.DropDuration;
        arcHeight = presentation.ArcHeight;
        elapsed = 0f;
        started = true;
        landed = false;
        enabled = true;
    }

    public void SettleImmediately()
    {
        if (!started)
            return;

        ApplySettledState();
    }

    private void Update()
    {
        if (!started || landed)
            return;

        elapsed += Time.deltaTime;
        float normalizedTime = Mathf.Clamp01(elapsed / duration);
        float smoothTime = normalizedTime * normalizedTime * (3f - 2f * normalizedTime);

        Vector3 position = Vector3.Lerp(startPosition, landingPosition, smoothTime);
        position.y += 4f * arcHeight * normalizedTime * (1f - normalizedTime);
        transform.position = position;

        if (visualRoot != null)
            visualRoot.localRotation = Quaternion.Slerp(startVisualRotation, settledVisualRotation, smoothTime);

        if (normalizedTime >= 1f)
            ApplySettledState();
    }

    private void ApplySettledState()
    {
        bool wasLanded = landed;
        transform.position = landingPosition;
        if (visualRoot != null)
            visualRoot.localRotation = settledVisualRotation;

        landed = true;
        enabled = false;
        if (!wasLanded)
            Landed?.Invoke(); // 획득 후보 전환 알림
    }

    private Vector3 ResolveLandingPosition(Vector3 origin, float scatterRadius, float groundClearance)
    {
        Vector2 scatter = UnityEngine.Random.insideUnitCircle * scatterRadius;
        Vector3 candidate = origin + new Vector3(scatter.x, 0f, scatter.y);
        Vector3 rayOrigin = candidate + Vector3.up * GroundProbeHeight;
        int hitCount = Physics.RaycastNonAlloc(
            rayOrigin,
            Vector3.down,
            groundHits,
            GroundProbeDistance,
            ~0,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.MaxValue;
        bool foundGround = false;
        Vector3 groundPoint = candidate;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = groundHits[i];
            Collider hitCollider = hit.collider;
            if (!IsValidGround(hitCollider, hit.normal) || hit.distance >= nearestDistance)
                continue;

            nearestDistance = hit.distance;
            groundPoint = hit.point;
            foundGround = true;
        }

        if (foundGround)
            candidate.y = groundPoint.y + groundClearance;

        return candidate;
    }

    private bool IsValidGround(Collider hitCollider, Vector3 normal)
    {
        if (hitCollider == null || normal.y < MinimumGroundNormalY)
            return false;

        Transform hitTransform = hitCollider.transform;
        if (hitTransform == transform || hitTransform.IsChildOf(transform))
            return false;

        if (hitCollider.GetComponentInParent<WorldItemPickup>() != null)
            return false;

        if (hitCollider.GetComponentInParent<CurrencyWorldPickup>() != null)
            return false;

        return hitCollider.GetComponentInParent<CombatHealth>() == null;
    }
}
