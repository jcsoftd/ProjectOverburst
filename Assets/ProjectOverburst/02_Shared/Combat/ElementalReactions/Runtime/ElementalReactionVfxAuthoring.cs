using UnityEngine;

public enum ElementalReactionVfxSlotType
{
    Start = 0,
    Loop = 1,
    Proc = 2,
    Link = 3,
    End = 4
}

public enum ElementalReactionVfxSpawnBasis
{
    HitPoint = 0,
    TargetCenter = 1,
    TargetGround = 2,
    WorldPosition = 3,
    SourceToTarget = 4,
    TargetVolume = 5
}

public enum ElementalReactionVfxScaleMode
{
    None = 0,
    ReactionRadius = 1,
    TargetVolume = 2
}

[DisallowMultipleComponent]
public sealed class ElementalReactionVfxAuthoring : MonoBehaviour
{
    [SerializeField, InspectorName("반응 타입")]
    private ElementalReactionType reactionType;
    [SerializeField, InspectorName("슬롯 타입")]
    private ElementalReactionVfxSlotType slotType;
    [SerializeField, InspectorName("생성 기준")]
    private ElementalReactionVfxSpawnBasis spawnBasis;
    [SerializeField, InspectorName("대상 추적")]
    private bool followTarget;
    [SerializeField, InspectorName("크기 기준")]
    private ElementalReactionVfxScaleMode scaleMode;
    [SerializeField, Min(0f), InspectorName("제작 기준 반경")]
    private float authoredRadius;
    [SerializeField, InspectorName("로컬 위치 보정")]
    private Vector3 localOffset;
    [SerializeField, Min(0f), InspectorName("안전 수명(초)")]
    private float lifetime;
    [SerializeField, Min(1), InspectorName("풀 용량 후보")]
    private int poolCapacity = 8;
    [SerializeField, InspectorName("자연 종료 우선")]
    private bool naturalCompletion = true;

    public ElementalReactionType ReactionType => reactionType;
    public ElementalReactionVfxSlotType SlotType => slotType;
    public ElementalReactionVfxSpawnBasis SpawnBasis => spawnBasis;
    public bool FollowTarget => followTarget;
    public ElementalReactionVfxScaleMode ScaleMode => scaleMode;
    public float AuthoredRadius => authoredRadius;
    public Vector3 LocalOffset => localOffset;
    public float Lifetime => lifetime;
    public int PoolCapacity => poolCapacity;
    public bool NaturalCompletion => naturalCompletion;

    public bool SetPoolCapacity(int configuredPoolCapacity)
    {
        int resolvedCapacity = Mathf.Max(1, configuredPoolCapacity);
        if (poolCapacity == resolvedCapacity)
            return false;

        poolCapacity = resolvedCapacity;
        return true;
    }

    public void ConfigureInitialDefaults(
        ElementalReactionType configuredReactionType,
        ElementalReactionVfxSlotType configuredSlotType,
        ElementalReactionVfxSpawnBasis configuredSpawnBasis,
        bool configuredFollowTarget,
        ElementalReactionVfxScaleMode configuredScaleMode,
        float configuredAuthoredRadius,
        Vector3 configuredLocalOffset,
        float configuredLifetime,
        int configuredPoolCapacity,
        bool configuredNaturalCompletion)
    {
        reactionType = configuredReactionType;
        slotType = configuredSlotType;
        spawnBasis = configuredSpawnBasis;
        followTarget = configuredFollowTarget;
        scaleMode = configuredScaleMode;
        authoredRadius = Mathf.Max(0f, configuredAuthoredRadius);
        localOffset = configuredLocalOffset;
        lifetime = Mathf.Max(0f, configuredLifetime);
        poolCapacity = Mathf.Max(1, configuredPoolCapacity);
        naturalCompletion = configuredNaturalCompletion;
    }

    private void OnValidate()
    {
        authoredRadius = Mathf.Max(0f, authoredRadius);
        lifetime = Mathf.Max(0f, lifetime);
        poolCapacity = Mathf.Max(1, poolCapacity);
    }
}
