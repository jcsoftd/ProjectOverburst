using UnityEngine;

[System.Serializable]
public sealed class SimulationFormationDefinition
{
    [SerializeField] private Vector3[] followerOffsets =
    {
        new Vector3(-1.35f, 0f, -1.25f),
        new Vector3(1.35f, 0f, -1.25f)
    };

    [SerializeField, Min(0.1f)] private float stoppedSlotRadius = 1.85f; // 정지 후 리더 중심 슬롯 반경
    [SerializeField, Range(15f, 75f)] private float stoppedSlotRearHalfAngle = 47f; // 후방 중심선 기준 좌우 각도
    [SerializeField] private float anchorMoveThreshold = 0.08f;
    [SerializeField] private float anchorRotationLerp = 12f;

    public float StoppedSlotRadius => Mathf.Max(0.1f, stoppedSlotRadius);
    public float StoppedSlotRearHalfAngle => Mathf.Clamp(stoppedSlotRearHalfAngle, 15f, 75f);
    public float AnchorMoveThreshold => Mathf.Max(0.001f, anchorMoveThreshold);
    public float AnchorRotationLerp => Mathf.Max(0f, anchorRotationLerp);

    public Vector3 GetFollowerOffset(int followerSlot)
    {
        if (followerOffsets == null || followerOffsets.Length == 0)
            return Vector3.zero;

        return followerOffsets[Mathf.Clamp(followerSlot, 0, followerOffsets.Length - 1)];
    }

    public Vector3 GetStoppedFollowerOffset(int followerSlot)
    {
        Vector3 authoredOffset = GetFollowerOffset(followerSlot);
        float signedAngle = followerSlot <= 0
            ? StoppedSlotRearHalfAngle
            : -StoppedSlotRearHalfAngle;
        Vector3 rearDirection = Quaternion.AngleAxis(signedAngle, Vector3.up) * Vector3.back;
        Vector3 offset = rearDirection * StoppedSlotRadius; // 같은 원주 위 후방 슬롯
        offset.y = authoredOffset.y;
        return offset;
    }
}
