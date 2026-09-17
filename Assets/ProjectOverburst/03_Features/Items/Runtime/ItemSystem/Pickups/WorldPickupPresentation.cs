using UnityEngine;

[DisallowMultipleComponent]
public sealed class WorldPickupPresentation : MonoBehaviour
{
    [Header("Visual")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Vector3 settledLocalEulerAngles;
    [SerializeField] private float groundClearance = 0.06f;

    [Header("Drop Motion")]
    [SerializeField] private float dropDuration = 0.38f;
    [SerializeField] private float arcHeight = 0.42f;
    [SerializeField] private float scatterRadius = 0.16f;
    [SerializeField] private bool randomizeYaw = true;
    [SerializeField] private Vector2 randomYawRange = new Vector2(0f, 360f);

    public Transform VisualRoot
    {
        get
        {
            if (visualRoot != null)
                return visualRoot;

            Transform modelRoot = transform.Find("ModelRoot");
            return modelRoot != null ? modelRoot : transform;
        }
    }

    public float GroundClearance => Mathf.Max(0f, groundClearance);
    public float DropDuration => Mathf.Max(0.01f, dropDuration);
    public float ArcHeight => Mathf.Max(0f, arcHeight);
    public float ScatterRadius => Mathf.Max(0f, scatterRadius);

    public Quaternion RollSettledLocalRotation()
    {
        float yaw = randomizeYaw
            ? Random.Range(Mathf.Min(randomYawRange.x, randomYawRange.y), Mathf.Max(randomYawRange.x, randomYawRange.y))
            : 0f;

        return Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.Euler(settledLocalEulerAngles);
    }
}
