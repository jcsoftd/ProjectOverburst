using UnityEngine;

[CreateAssetMenu(fileName = "AttackPattern", menuName = "Combat/Melee Attack Pattern")]
public sealed class AttackPatternDefinition : ScriptableObject
{
    [Header("패턴 구성")]
    [InspectorName("범위 형태")]
    public AttackAreaShape shape = AttackAreaShape.Sector;
    [InspectorName("채움 방식")]
    public AttackFillMode fillMode = AttackFillMode.AngularSweep;
    [InspectorName("진행 방향")]
    public AttackFillDirection direction = AttackFillDirection.LeftToRight;

    [Header("패턴 좌표계")]
    [InspectorName("각도 오프셋")]
    public float angleOffset;
    [InspectorName("수직 허용 범위")]
    [Min(0.1f)] public float verticalTolerance = 3f;
    [InspectorName("적중 재검사 여유 범위")]
    [Min(0f)] public float hitRevalidationTolerance = 0.4f;

    [Header("진행 방식")]
    [InspectorName("진행 곡선")]
    public AnimationCurve progressCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    public AttackPatternRuntimeData Resolve(
        float range,
        float angle,
        float width,
        float forwardOffset)
    {
        float resolvedAngle = Mathf.Max(1f, angle);
        if (shape == AttackAreaShape.Circle)
            resolvedAngle = 360f;

        return new AttackPatternRuntimeData(
            shape,
            fillMode,
            direction,
            Mathf.Max(0.1f, range),
            Mathf.Clamp(resolvedAngle, 1f, 360f),
            Mathf.Max(0.05f, width),
            forwardOffset,
            angleOffset,
            Mathf.Max(0.1f, verticalTolerance),
            Mathf.Max(0f, hitRevalidationTolerance),
            progressCurve);
    }
}
