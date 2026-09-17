using UnityEngine;

// GOAL B2: 공격 root motion과 회피 직접 이동의 적 관통 방지를 한 소유자로 모은다.
// 실제 CharacterController 이동은 OverburstCharacterMotor3D만 실행한다.
[DisallowMultipleComponent]
[RequireComponent(typeof(OverburstCharacterMotor3D))]
public sealed class CombatMotionDriver : MonoBehaviour
{
    private const string EnemyPhysicalLayerName = "Enemy";
    private const int QueryCapacity = 64;

    [SerializeField] private PlayerMovement movement;
    [SerializeField] private OverburstCharacterMotor3D motor;
    [SerializeField] private CombatTarget combatTarget;

    private float enemyClearance = 0.03f;
    private int enemyLayerMask;
    private readonly RaycastHit[] castHits = new RaycastHit[QueryCapacity];
    private readonly Collider[] overlapHits = new Collider[QueryCapacity];

    public PlayerMovement BoundMovement => movement;
    public OverburstCharacterMotor3D Motor => motor;
    public CombatTarget CombatTarget => combatTarget;
    public int EnemyLayerMask => enemyLayerMask;

    private void Awake()
    {
        ResolveReferences();
        ResolveEnemyLayer();
    }

    public void Bind(
        PlayerMovement owner,
        OverburstCharacterMotor3D characterMotor,
        CombatTarget ownerTarget,
        float clearance)
    {
        if (owner != null)
            movement = owner;
        if (characterMotor != null)
            motor = characterMotor;
        if (ownerTarget != null)
            combatTarget = ownerTarget;
        enemyClearance = Mathf.Max(0f, clearance);
        ResolveReferences();
        ResolveEnemyLayer();
    }

    public Vector3 ApplyEvadeDisplacement(Vector3 displacement)
    {
        displacement.y = 0f;
        Vector3 safeDisplacement = ResolveSafeDisplacement(displacement);
        if (motor != null && safeDisplacement.sqrMagnitude > 0.000001f)
            motor.MoveDirect(safeDisplacement);
        return safeDisplacement;
    }

    public Vector3 ApplyWeaponRootMotion(Vector3 displacement)
    {
        displacement.y = 0f;
        Vector3 safeDisplacement = ResolveSafeDisplacement(displacement);
        if (motor != null && safeDisplacement.sqrMagnitude > 0.000001f)
            motor.MoveDirect(safeDisplacement);
        return motor != null ? motor.ControllerPlanarVelocity : Vector3.zero;
    }

    public Vector3 ResolveSafeDisplacement(Vector3 displacement)
    {
        ResolveReferences();
        CharacterController controller = motor != null ? motor.Controller : null;
        float distance = displacement.magnitude;
        if (distance <= 0.0001f
            || controller == null
            || !controller.enabled
            || !TryResolveCharacterCapsule(controller, out Vector3 top, out Vector3 bottom, out float radius))
        {
            return displacement;
        }

        if (enemyLayerMask == 0)
            return Vector3.zero;

        Vector3 direction = displacement / distance;
        int overlapCount = Physics.OverlapCapsuleNonAlloc(
            top,
            bottom,
            radius,
            overlapHits,
            enemyLayerMask,
            QueryTriggerInteraction.Ignore);
        if (overlapCount >= overlapHits.Length)
            return Vector3.zero;

        Vector3 capsuleCenter = (top + bottom) * 0.5f;
        for (int i = 0; i < overlapCount; i++)
        {
            Collider overlap = overlapHits[i];
            if (IsEnemyPhysicalBlocker(overlap)
                && IsMovingTowardCollider(direction, overlap, capsuleCenter))
            {
                return Vector3.zero;
            }
        }

        int hitCount = Physics.CapsuleCastNonAlloc(
            top,
            bottom,
            radius,
            direction,
            castHits,
            distance,
            enemyLayerMask,
            QueryTriggerInteraction.Ignore);
        if (hitCount >= castHits.Length)
            return Vector3.zero;

        float allowedDistance = distance;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = castHits[i];
            if (!IsEnemyPhysicalBlocker(hit.collider))
                continue;

            if (hit.distance <= 0.001f
                && !IsMovingTowardCollider(direction, hit.collider, capsuleCenter))
            {
                continue;
            }

            allowedDistance = Mathf.Min(allowedDistance, Mathf.Max(0f, hit.distance));
        }

        return direction * allowedDistance;
    }

    private bool TryResolveCharacterCapsule(
        CharacterController controller,
        out Vector3 top,
        out Vector3 bottom,
        out float radius)
    {
        top = Vector3.zero;
        bottom = Vector3.zero;
        radius = 0f;
        if (controller == null)
            return false;

        Vector3 scale = transform.lossyScale;
        float radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z), 0.0001f);
        float heightScale = Mathf.Max(Mathf.Abs(scale.y), 0.0001f);
        float bodyRadius = Mathf.Max(0.01f, controller.radius * radiusScale);
        float height = Mathf.Max(bodyRadius * 2f, controller.height * heightScale);
        float halfSegment = Mathf.Max(0f, height * 0.5f - bodyRadius);
        Vector3 center = transform.TransformPoint(controller.center);
        Vector3 up = transform.up.sqrMagnitude > 0.0001f ? transform.up.normalized : Vector3.up;
        top = center + up * halfSegment;
        bottom = center - up * halfSegment;
        radius = bodyRadius + enemyClearance;
        return true;
    }

    private bool IsEnemyPhysicalBlocker(Collider candidate)
    {
        if (candidate == null
            || !candidate.enabled
            || candidate.isTrigger
            || candidate.transform == transform
            || candidate.transform.IsChildOf(transform))
        {
            return false;
        }

        CombatTarget candidateTarget = CombatTarget.Resolve(candidate);
        return candidateTarget != null
            && candidateTarget != combatTarget
            && candidateTarget.Team == CombatTeam.Enemy;
    }

    private static bool IsMovingTowardCollider(
        Vector3 direction,
        Collider candidate,
        Vector3 capsuleCenter)
    {
        if (candidate == null)
            return false;

        Vector3 toCandidate = candidate.bounds.center - capsuleCenter;
        toCandidate.y = 0f;
        if (toCandidate.sqrMagnitude <= 0.0001f)
            return true;
        return Vector3.Dot(direction, toCandidate.normalized) > 0.001f;
    }

    private void ResolveReferences()
    {
        if (movement == null)
            movement = GetComponent<PlayerMovement>();
        if (motor == null)
            motor = GetComponent<OverburstCharacterMotor3D>();
        if (combatTarget == null)
            combatTarget = GetComponent<CombatTarget>();
    }

    private void ResolveEnemyLayer()
    {
        int layer = LayerMask.NameToLayer(EnemyPhysicalLayerName);
        enemyLayerMask = layer >= 0 ? 1 << layer : 0;
        if (enemyLayerMask == 0)
            Debug.LogError("[CombatMotionDriver] Enemy physical layer is missing.", this);
    }
}
