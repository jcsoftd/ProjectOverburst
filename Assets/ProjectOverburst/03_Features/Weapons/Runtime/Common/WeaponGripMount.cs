using UnityEngine;
using UnityEngine.Serialization;

public enum WeaponGripAnchor
{
    RightHand,
    Back
}

public sealed class WeaponGripMount : MonoBehaviour
{
    private const string RightHandGripPointName = "RightHandGripPoint";
    private const string BackGripPointName = "BackGripPoint";

    [FormerlySerializedAs("gripRoot")]
    [SerializeField] private Transform rightHandGripPoint;
    [SerializeField] private Transform backGripPoint;

    public Transform RightHandGripPoint => ResolveRightHandGripPoint();
    public Transform BackGripPoint => ResolveBackGripPoint();
    public bool IsConfigured => ResolveRightHandGripPoint() != null;
    public bool HasBackGripPoint => ResolveBackGripPoint(false) != null;

    public void Configure(Transform rightHand, Transform back)
    {
        rightHandGripPoint = rightHand;
        backGripPoint = back;
    }

    public bool TryResolveRootLocalPose(
        Transform weaponRoot,
        WeaponGripAnchor anchor,
        Vector3 desiredGripLocalPosition,
        Quaternion desiredGripLocalRotation,
        out Vector3 rootLocalPosition,
        out Quaternion rootLocalRotation)
    {
        Transform grip = ResolveGripPoint(anchor);
        if (weaponRoot == null || grip == null || !grip.IsChildOf(weaponRoot))
        {
            rootLocalPosition = desiredGripLocalPosition;
            rootLocalRotation = desiredGripLocalRotation;
            return false;
        }

        Vector3 gripPositionInRoot = weaponRoot.InverseTransformPoint(grip.position);
        Quaternion gripRotationInRoot = Quaternion.Inverse(weaponRoot.rotation) * grip.rotation;
        rootLocalRotation = desiredGripLocalRotation * Quaternion.Inverse(gripRotationInRoot);
        Vector3 scaledGripPosition = Vector3.Scale(gripPositionInRoot, weaponRoot.localScale);
        rootLocalPosition = desiredGripLocalPosition - rootLocalRotation * scaledGripPosition;
        return true;
    }

    public bool TryGetCurrentGripPose(
        Transform weaponRoot,
        WeaponGripAnchor anchor,
        out Vector3 gripLocalPosition,
        out Quaternion gripLocalRotation)
    {
        Transform grip = ResolveGripPoint(anchor);
        Transform parent = weaponRoot != null ? weaponRoot.parent : null;
        if (weaponRoot == null || parent == null || grip == null || !grip.IsChildOf(weaponRoot))
        {
            gripLocalPosition = weaponRoot != null ? weaponRoot.localPosition : Vector3.zero;
            gripLocalRotation = weaponRoot != null ? weaponRoot.localRotation : Quaternion.identity;
            return false;
        }

        gripLocalPosition = parent.InverseTransformPoint(grip.position);
        gripLocalRotation = Quaternion.Inverse(parent.rotation) * grip.rotation;
        return true;
    }

    private Transform ResolveRightHandGripPoint()
    {
        if (rightHandGripPoint != null)
            return rightHandGripPoint;

        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null && children[i].name == RightHandGripPointName)
            {
                rightHandGripPoint = children[i];
                break;
            }
        }

        return rightHandGripPoint;
    }

    private Transform ResolveBackGripPoint(bool fallbackToRightHand = true)
    {
        if (backGripPoint != null)
            return backGripPoint;

        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null && children[i].name == BackGripPointName)
            {
                backGripPoint = children[i];
                break;
            }
        }

        return backGripPoint != null || !fallbackToRightHand
            ? backGripPoint
            : ResolveRightHandGripPoint();
    }

    private Transform ResolveGripPoint(WeaponGripAnchor anchor)
    {
        return anchor == WeaponGripAnchor.Back
            ? ResolveBackGripPoint()
            : ResolveRightHandGripPoint();
    }
}
