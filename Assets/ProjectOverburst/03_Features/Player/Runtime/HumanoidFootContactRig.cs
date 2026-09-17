using UnityEngine;

[DisallowMultipleComponent]
public sealed class HumanoidFootContactRig : MonoBehaviour
{
    [Header("Rig")]
    [SerializeField] private Animator targetAnimator;

    [Header("Left Foot")]
    [SerializeField] private Transform leftHeel;
    [SerializeField] private Transform leftToe;

    [Header("Right Foot")]
    [SerializeField] private Transform rightHeel;
    [SerializeField] private Transform rightToe;

    [Header("Gizmos")]
    [SerializeField] private bool showGizmos = true;
    [SerializeField, Min(0.002f)] private float gizmoRadius = 0.015f;

    public Animator TargetAnimator => targetAnimator;
    public Transform LeftHeel => leftHeel;
    public Transform LeftToe => leftToe;
    public Transform RightHeel => rightHeel;
    public Transform RightToe => rightToe;
    public bool IsConfigured => targetAnimator != null
        && leftHeel != null
        && leftToe != null
        && rightHeel != null
        && rightToe != null;

    private void OnDrawGizmos()
    {
        if (!showGizmos)
            return;

        DrawFoot(leftHeel, leftToe, new Color(0.15f, 0.75f, 1f, 0.9f));
        DrawFoot(rightHeel, rightToe, new Color(1f, 0.35f, 0.7f, 0.9f));
    }

    private void DrawFoot(Transform heel, Transform toe, Color lineColor)
    {
        float radius = Mathf.Max(0.002f, gizmoRadius);

        if (heel != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(heel.position, radius);
        }

        if (toe != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(toe.position, radius);
        }

        if (heel == null || toe == null)
            return;

        Gizmos.color = lineColor;
        Gizmos.DrawLine(heel.position, toe.position);
    }
}
