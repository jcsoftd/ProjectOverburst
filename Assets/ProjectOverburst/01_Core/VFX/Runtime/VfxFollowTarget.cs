using UnityEngine;

public class VfxFollowTarget : MonoBehaviour // VFX 추적
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 localOffset;
    [SerializeField] private bool followRotation;
    [SerializeField] private bool destroyOnTargetLost = true;

    public void Initialize(Transform followTarget, Vector3 offset, bool shouldFollowRotation, bool shouldDestroyOnTargetLost)
    {
        target = followTarget;
        localOffset = offset;
        followRotation = shouldFollowRotation;
        destroyOnTargetLost = shouldDestroyOnTargetLost;
        UpdateTransform();
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            if (destroyOnTargetLost)
                Destroy(gameObject); // 대상 소실

            return;
        }

        UpdateTransform();
    }

    private void UpdateTransform()
    {
        if (target == null)
            return;

        transform.position = target.TransformPoint(localOffset);

        if (followRotation)
            transform.rotation = target.rotation; // 회전 추적
    }
}
