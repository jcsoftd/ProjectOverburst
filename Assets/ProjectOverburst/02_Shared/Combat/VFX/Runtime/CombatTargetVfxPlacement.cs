using UnityEngine;

/// <summary>Visual body volume for effects. CombatTarget's hurt volume remains the hit rule.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CombatTarget))]
public sealed class CombatTargetVfxPlacement : MonoBehaviour
{
    private const float HitReferenceRadius = 1.2f;

    [SerializeField] private bool useAuthoredVolume;
    [SerializeField] private Vector3 localBodyCenter = new Vector3(0f, 1f, 0f);
    [SerializeField, Min(0.05f)] private float bodyRadius = 0.6f;
    [SerializeField, Min(0.1f)] private float bodyHeight = 2f;
    [SerializeField] private bool useAuthoredHitVolume;
    [SerializeField] private Vector3 localHitCenter = new Vector3(0f, 1f, 0f);
    [SerializeField, Min(0.05f)] private float hitRadius = 0.6f;
    [SerializeField, Min(0.1f)] private float hitHeight = 2f;
    [SerializeField, Range(0.4f, 1f)] private float contactRadiusFraction = 0.5f;
    [SerializeField, Range(0f, 0.8f)] private float contactHeightFraction = 0.55f;
    [Tooltip("화상 불 위치 보정(몬스터 방향 기준, m). 몸 모양이 튀는 몬스터만 쓴다.")]
    [SerializeField] private Vector3 burnOffset;
    [Tooltip("화상 불 크기 보정. 기본 1")]
    [SerializeField, Range(0.3f, 2f)] private float burnScale = 1f;
    [SerializeField] private Vector3 shockOffset;
    [SerializeField, Min(.01f)] private float shockScale = 1f;
    [SerializeField] private Vector3 corrosionOffset;
    [SerializeField, Min(.01f)] private float corrosionScale = 1f;

    private CombatTarget target;
    // 엘리트·보스 변형 크기(VisualRoot 배율). 소환할 때마다 다시 넣는 실행 값이라 저장하지 않는다.
    // 화상 불 위치 보정(burnOffset)은 m 단위로 맞춘 값이라 곱하지 않는다.
    [System.NonSerialized] private Vector3 variantScale = Vector3.one;

    private void Awake() => target = GetComponent<CombatTarget>();

    public void SetVariantScale(Vector3 scale)
    {
        variantScale = new Vector3(
            Mathf.Max(0.01f, Mathf.Abs(scale.x)),
            Mathf.Max(0.01f, Mathf.Abs(scale.y)),
            Mathf.Max(0.01f, Mathf.Abs(scale.z)));
    }

    public CombatTargetVolume VisualVolume
    {
        get
        {
            if (target == null) target = GetComponent<CombatTarget>();
            if (!useAuthoredVolume)
                return target != null ? target.CurrentHurtVolume : default;

            return AuthoredVolume(localBodyCenter, bodyRadius, bodyHeight);
        }
    }

    public CombatTargetVolume HitVolume => useAuthoredHitVolume
        ? AuthoredVolume(localHitCenter, hitRadius, hitHeight) : VisualVolume;

    private CombatTargetVolume AuthoredVolume(Vector3 center, float radius, float height)
    {
        Vector3 scale = transform.lossyScale;
        float planarScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z), 0.0001f);
        float verticalScale = Mathf.Max(Mathf.Abs(scale.y), 0.0001f);
        float variantPlanar = Mathf.Max(variantScale.x, variantScale.z);
        return new CombatTargetVolume(transform.TransformPoint(Vector3.Scale(center, variantScale)),
            radius * variantPlanar * planarScale, height * variantScale.y * verticalScale * 0.5f);
    }

    public static CombatTargetVolume ResolveVolume(CombatTarget target)
    {
        if (target == null) return default;
        return target.TryGetComponent(out CombatTargetVfxPlacement placement)
            ? placement.VisualVolume : target.CurrentHurtVolume;
    }

    /// <summary>Per-monster burning aura correction: world-space offset and size factor (zero / one when unset).</summary>
    public static void ResolveBurnTuning(CombatTarget target, out Vector3 worldOffset, out float scale)
    {
        worldOffset = Vector3.zero;
        scale = 1f;
        if (target == null || !target.TryGetComponent(out CombatTargetVfxPlacement placement)) return;
        worldOffset = placement.transform.rotation * placement.burnOffset;
        scale = placement.burnScale;
    }

    public static void ResolveAuraTuning(CombatTarget target, MeleeElementStatusAuraType type,
        out Vector3 worldOffset, out float scale)
    {
        worldOffset = Vector3.zero; scale = 1f;
        if (target == null || !target.TryGetComponent(out CombatTargetVfxPlacement placement)) return;
        Vector3 offset = type == MeleeElementStatusAuraType.Burning ? placement.burnOffset
            : type == MeleeElementStatusAuraType.Shocked ? placement.shockOffset : placement.corrosionOffset;
        scale = type == MeleeElementStatusAuraType.Burning ? placement.burnScale
            : type == MeleeElementStatusAuraType.Shocked ? placement.shockScale : placement.corrosionScale;
        worldOffset = placement.transform.rotation * offset;
        scale = float.IsNaN(scale) || float.IsInfinity(scale) ? 1f : Mathf.Max(.01f, scale);
    }

    public static Vector3 ResolveContact(CombatTarget target, Vector3 rawHitPoint,
        Vector3 incomingDirection, out float hitSizeMultiplier)
    {
        hitSizeMultiplier = 1f;
        if (target == null) return rawHitPoint;

        target.TryGetComponent(out CombatTargetVfxPlacement placement);
        CombatTargetVolume volume = placement != null
            ? placement.HitVolume : target.CurrentHurtVolume;
        if (volume.Radius <= 0f || volume.HalfHeight <= 0f) return rawHitPoint;
        float radiusFraction = placement != null ? placement.contactRadiusFraction : 0.82f;

        Vector3 direction = incomingDirection;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            direction = volume.Center - rawHitPoint;
            direction.y = 0f;
        }
        if (direction.sqrMagnitude <= 0.0001f)
            direction = target.transform.forward;
        direction.Normalize();

        Vector3 point = volume.Center - direction * (volume.Radius * radiusFraction);
        float hurtCenterY = target.CurrentHurtVolume.Center.y;
        float heightFraction = placement != null ? placement.contactHeightFraction : 0f;
        float suggestedY = volume.Center.y + (rawHitPoint.y - hurtCenterY)
            + volume.HalfHeight * heightFraction;
        point.y = Mathf.Clamp(suggestedY,
            volume.Center.y - volume.HalfHeight * 0.4f,
            volume.Center.y + volume.HalfHeight * 0.75f);
        hitSizeMultiplier = Mathf.Clamp(Mathf.Sqrt(volume.Radius / HitReferenceRadius), 0.55f, 1.5f);
        return point;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        bodyRadius = Mathf.Max(0.05f, bodyRadius);
        bodyHeight = Mathf.Max(0.1f, bodyHeight);
        hitRadius = Mathf.Max(0.05f, hitRadius);
        hitHeight = Mathf.Max(0.1f, hitHeight);
        contactRadiusFraction = Mathf.Clamp(contactRadiusFraction, 0.4f, 1f);
        contactHeightFraction = Mathf.Clamp(contactHeightFraction, 0f, 0.8f);
        burnScale = Mathf.Clamp(burnScale, 0.3f, 2f);
        shockScale = Mathf.Max(.01f, shockScale);
        corrosionScale = Mathf.Max(.01f, corrosionScale);
    }
#endif
}
