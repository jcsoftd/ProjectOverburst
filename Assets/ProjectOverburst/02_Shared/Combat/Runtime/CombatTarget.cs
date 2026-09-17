using UnityEngine;

public readonly struct CombatTargetVolume
{
    public readonly Vector3 Center;
    public readonly float Radius;
    public readonly float HalfHeight;

    public CombatTargetVolume(Vector3 center, float radius, float halfHeight)
    {
        Center = center;
        Radius = Mathf.Max(0f, radius);
        HalfHeight = Mathf.Max(0f, halfHeight);
    }
}

[DisallowMultipleComponent]
[RequireComponent(typeof(CombatHealth))]
[RequireComponent(typeof(CombatAffiliation))]
public sealed class CombatTarget : MonoBehaviour
{
    [SerializeField] private CombatHealth damageReceiver;
    [SerializeField] private CombatAffiliation affiliation;
    [SerializeField] private Vector3 localCenter = new Vector3(0f, 1f, 0f);
    [SerializeField, Min(0.05f)] private float radius = 0.45f;
    [SerializeField, Min(0.1f)] private float height = 2f;
    private IElementalStatusReceiver elementalStatusReceiver;

    public CombatHealth DamageReceiver => damageReceiver;
    public IDamageable Damageable => damageReceiver;
    public CombatTeam Team => affiliation != null ? affiliation.Team : CombatTeam.Neutral;
    public int TargetId => GetInstanceID();
    public bool IsAlive => damageReceiver != null && !damageReceiver.IsDead && isActiveAndEnabled;
    public Vector3 WorldCenter => transform.TransformPoint(localCenter);
    public IElementalStatusReceiver ElementalStatusReceiver
    {
        get
        {
            if (!IsCachedStatusReceiverValid())
            {
                elementalStatusReceiver = GetComponent<IElementalStatusReceiver>(); // 최초 소비 시 한 번만 해석
#if UNITY_EDITOR
                ElementalStatusReceiverResolveCountForValidation++;
#endif
            }

            return elementalStatusReceiver;
        }
    }

#if UNITY_EDITOR
    public int ElementalStatusReceiverResolveCountForValidation { get; private set; }
#endif

    public CombatTargetVolume CurrentVolume
    {
        get { return ResolveVolumeAtRootPosition(transform.position); }
    }

    public CombatTargetVolume ResolveVolumeAtRootPosition(Vector3 rootWorldPosition)
    {
        Vector3 scale = transform.lossyScale;
        float planarScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z), 0.0001f);
        float verticalScale = Mathf.Max(Mathf.Abs(scale.y), 0.0001f);
        return new CombatTargetVolume(
            rootWorldPosition + transform.TransformVector(localCenter),
            radius * planarScale,
            height * verticalScale * 0.5f);
    }

    public CombatTargetVolume ResolveSweptVolume(Vector3 intendedRootWorldPosition)
    {
        CombatTargetVolume current = CurrentVolume;
        CombatTargetVolume intended = ResolveVolumeAtRootPosition(intendedRootWorldPosition);
        Vector3 planarDelta = intended.Center - current.Center;
        planarDelta.y = 0f;
        return new CombatTargetVolume(
            (current.Center + intended.Center) * 0.5f,
            Mathf.Max(current.Radius, intended.Radius) + planarDelta.magnitude * 0.5f,
            Mathf.Max(current.HalfHeight, intended.HalfHeight));
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        elementalStatusReceiver = null; // 풀 재사용 뒤 현재 조립 상태를 다시 한 번 확인
#if UNITY_EDITOR
        ElementalStatusReceiverResolveCountForValidation = 0;
#endif
        ResolveReferences();
        CombatTargetRegistry.Register(this);
    }

    private void OnDisable()
    {
        CombatTargetRegistry.Unregister(this);
    }

    private void OnValidate()
    {
        elementalStatusReceiver = null;
#if UNITY_EDITOR
        ElementalStatusReceiverResolveCountForValidation = 0;
#endif
        ResolveReferences();
        radius = Mathf.Max(0.05f, radius);
        height = Mathf.Max(0.1f, height);
    }

    public void Configure(CombatTeam configuredTeam, bool refreshVolume)
    {
        ResolveReferences();
        affiliation?.Configure(configuredTeam);
        if (refreshVolume)
            RefreshVolumeFromPrimaryCollider();

        if (isActiveAndEnabled)
            CombatTargetRegistry.Register(this);
    }

    public void ConfigureVolume(Vector3 configuredLocalCenter, float configuredRadius, float configuredHeight)
    {
        localCenter = configuredLocalCenter;
        radius = Mathf.Max(0.05f, configuredRadius);
        height = Mathf.Max(0.1f, configuredHeight);
        if (isActiveAndEnabled)
            CombatTargetRegistry.NotifySpatialChanged(this);
    }

    public void RefreshVolumeFromPrimaryCollider()
    {
        Collider primaryCollider = ResolvePrimaryCollider();
        if (primaryCollider == null)
            return;

        Bounds bounds = primaryCollider.bounds;
        Vector3 scale = transform.lossyScale;
        float planarScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z), 0.0001f);
        float verticalScale = Mathf.Max(Mathf.Abs(scale.y), 0.0001f);

        localCenter = transform.InverseTransformPoint(bounds.center);
        radius = Mathf.Max(0.05f, Mathf.Max(bounds.extents.x, bounds.extents.z) / planarScale);
        height = Mathf.Max(0.1f, bounds.size.y / verticalScale);
        if (isActiveAndEnabled)
            CombatTargetRegistry.NotifySpatialChanged(this);
    }

    public static CombatTarget Resolve(Collider collider)
    {
        return collider != null ? collider.GetComponentInParent<CombatTarget>() : null;
    }

    public static CombatTarget EnsureConfigured(GameObject actorObject, CombatTeam team, bool refreshVolume = true)
    {
        if (actorObject == null || actorObject.GetComponent<CombatHealth>() == null)
            return null;

        CombatAffiliation affiliation = actorObject.GetComponent<CombatAffiliation>();
        if (affiliation == null)
            affiliation = actorObject.AddComponent<CombatAffiliation>();

        CombatTarget target = actorObject.GetComponent<CombatTarget>();
        if (target == null)
            target = actorObject.AddComponent<CombatTarget>();

        target.Configure(team, refreshVolume);
        return target;
    }

    private void ResolveReferences()
    {
        if (damageReceiver == null)
            damageReceiver = GetComponent<CombatHealth>();

        if (affiliation == null)
            affiliation = GetComponent<CombatAffiliation>();
    }

    private bool IsCachedStatusReceiverValid()
    {
        if (elementalStatusReceiver == null)
            return false;

        return elementalStatusReceiver is not Object unityObject || unityObject != null;
    }

    private Collider ResolvePrimaryCollider()
    {
        CharacterController characterController = GetComponent<CharacterController>();
        if (characterController != null)
            return characterController;

        CapsuleCollider capsule = GetComponentInChildren<CapsuleCollider>(true);
        if (capsule != null)
            return capsule;

        return GetComponentInChildren<Collider>(true);
    }
}
