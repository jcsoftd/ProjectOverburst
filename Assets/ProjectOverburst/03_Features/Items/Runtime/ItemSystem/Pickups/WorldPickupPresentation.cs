using UnityEngine;

[DisallowMultipleComponent]
public sealed class WorldPickupPresentation : MonoBehaviour
{
    public const float WeaponDropVisualScale = 0.6f;

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

    private Transform scaleTarget;
    private Vector3 authoredVisualScale;

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

    public void ApplyItemVisualScale(BaseItemData itemData)
    {
        Transform root = VisualRoot;
        if (scaleTarget != root)
        {
            scaleTarget = root;
            authoredVisualScale = root.localScale;
        }

        root.localScale = authoredVisualScale * (itemData is WeaponItemData ? WeaponDropVisualScale : 1f);
    }

    public bool TryGetVisualCenter(out Vector3 localCenter)
    {
        Transform root = VisualRoot;
        MeshFilter[] meshes = root.GetComponentsInChildren<MeshFilter>(true);
        Bounds bounds = default;
        bool hasBounds = false;
        for (int i = 0; i < meshes.Length; i++)
        {
            if (meshes[i].sharedMesh == null)
                continue;

            Bounds meshBounds = meshes[i].sharedMesh.bounds;
            Matrix4x4 toVisual = root.worldToLocalMatrix * meshes[i].transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 offset = new Vector3(
                    (corner & 1) == 0 ? -meshBounds.extents.x : meshBounds.extents.x,
                    (corner & 2) == 0 ? -meshBounds.extents.y : meshBounds.extents.y,
                    (corner & 4) == 0 ? -meshBounds.extents.z : meshBounds.extents.z);
                Vector3 point = toVisual.MultiplyPoint3x4(meshBounds.center + offset);
                if (!hasBounds)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    hasBounds = true;
                }
                else
                    bounds.Encapsulate(point);
            }
        }

        localCenter = hasBounds ? bounds.center : Vector3.zero;
        return hasBounds;
    }

    public Quaternion RollSettledLocalRotation()
    {
        float yaw = randomizeYaw
            ? Random.Range(Mathf.Min(randomYawRange.x, randomYawRange.y), Mathf.Max(randomYawRange.x, randomYawRange.y))
            : 0f;

        return Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.Euler(settledLocalEulerAngles);
    }
}
