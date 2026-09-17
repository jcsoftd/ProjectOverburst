using UnityEngine;

[DisallowMultipleComponent]
public sealed class ShieldGripMount : MonoBehaviour
{
    private const string GripPointName = "GripPoint";
    private const string BackPointName = "BackPoint";

    [SerializeField] private Transform gripPoint;
    [SerializeField] private Transform backPoint;

    public Transform GripPoint => ResolvePoint(ref gripPoint, GripPointName); // 왼팔 장착 기준점
    public Transform BackPoint => ResolvePoint(ref backPoint, BackPointName); // 등 보관 기준점
    public bool IsConfigured => GripPoint != null && BackPoint != null; // 두 포인트 연결 상태

    public void Configure(Transform grip, Transform back)
    {
        gripPoint = grip; // 손 기준점
        backPoint = back; // 등 기준점
    }

    public bool TryAlignRootToSocket(
        Transform shieldRoot,
        Transform socket,
        bool useBackPoint,
        Vector3 socketLocalEulerOffset)
    {
        Transform point = useBackPoint ? BackPoint : GripPoint; // 현재 포즈 기준점
        if (shieldRoot == null || socket == null || point == null || !point.IsChildOf(shieldRoot))
            return false;

        if (shieldRoot.parent != socket)
            shieldRoot.SetParent(socket, false); // 선택 소켓으로 이동

        Vector3 pointPositionInRoot = shieldRoot.InverseTransformPoint(point.position); // 루트 기준 포인트
        Quaternion pointRotationInRoot = Quaternion.Inverse(shieldRoot.rotation) * point.rotation;
        Quaternion socketLocalOffset = Quaternion.Euler(socketLocalEulerOffset);
        Quaternion rootRotation = socketLocalOffset * Quaternion.Inverse(pointRotationInRoot); // 소켓 로컬 축 보정 후 포인트 일치
        Vector3 scaledPointPosition = Vector3.Scale(pointPositionInRoot, shieldRoot.localScale);

        shieldRoot.localRotation = rootRotation;
        shieldRoot.localPosition = -(rootRotation * scaledPointPosition); // 소켓 원점에 포인트 일치
        return true;
    }

    private Transform ResolvePoint(ref Transform current, string pointName)
    {
        if (current != null)
            return current;

        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null && children[i].name == pointName)
            {
                current = children[i]; // 이름 기반 복구
                break;
            }
        }

        return current;
    }
}
