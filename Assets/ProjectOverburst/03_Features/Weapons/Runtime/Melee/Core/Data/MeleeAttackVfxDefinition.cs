using UnityEngine;

public enum AttackVfxScaleMirrorAxis
{
    X,
    Y,
    Z
}

[CreateAssetMenu(
    fileName = "MeleeAttackVfx",
    menuName = "OVERBURST/Weapons/Melee Attack VFX Definition")]
public sealed class MeleeAttackVfxDefinition : ScriptableObject
{
    [InspectorName("기본 VFX 프리팹")]
    public GameObject neutralPrefab;

    [InspectorName("어둠 VFX 프리팹 (비우면 기본 사용)")]
    public GameObject darkPrefab;

    [InspectorName("기본 크기")]
    public Vector3 baseScale = Vector3.one;

    [Tooltip("0이면 기존 배율 사용. 원형 범위 효과는 루트 배율1에서의 반경을 지정하면 최종 판정 반경에 맞춥니다.")]
    [Min(0f)] public float authoredCircleRadius;

    [InspectorName("로컬 위치 보정")]
    public Vector3 localPositionOffset;

    [InspectorName("로컬 회전 보정")]
    public Vector3 localEulerOffset;

    [InspectorName("오른쪽에서 왼쪽 베기 미러링")]
    public bool mirrorRightToLeft;

    [InspectorName("수평 미러 스케일 축")]
    public AttackVfxScaleMirrorAxis horizontalMirrorScaleAxis = AttackVfxScaleMirrorAxis.X;

    [Header("바닥 배치")]
    [InspectorName("바닥 레이어")]
    public LayerMask groundLayerMask = 1 << 6;

    [InspectorName("바닥 검사 시작 높이")]
    [Min(0f)] public float groundProbeHeight = 2f;

    [InspectorName("바닥 검사 거리")]
    [Min(0.01f)] public float groundProbeDistance = 6f;

    [InspectorName("바닥 표면 오프셋")]
    public float groundSurfaceOffset = 0.02f;

    [InspectorName("재생 수명 (0 = ParticleSystem 자동 계산)")]
    [Min(0f)] public float lifetime;

    [InspectorName("풀 최대 보관 수")]
    [Min(1)] public int poolCapacity = 12;

    public int SafePoolCapacity => Mathf.Max(1, poolCapacity);

    public GameObject ResolvePrefab(WeaponElement element)
        => element == WeaponElement.Dark && darkPrefab != null ? darkPrefab : neutralPrefab;
}
