using UnityEngine;

// Baked item silhouette: two triangles, placed once at landing, no shadow-map pass or Update.
[DisallowMultipleComponent]
public sealed class WorldItemGroundShadow : MonoBehaviour
{
    [SerializeField] Transform surface;
    [SerializeField] Vector3 center;
    [SerializeField] Vector2 size;
    [SerializeField] Vector3 settledEuler;
    WorldPickupPresentation presentation;
    static readonly RaycastHit[] groundHits = new RaycastHit[16];

    public Transform Surface => surface;

    public void Configure(Transform value, Vector3 localCenter, Vector2 dimensions, Vector3 euler)
    {
        surface = value; center = localCenter; size = dimensions; settledEuler = euler;
        Hide();
    }

    public void Hide() { if (surface) surface.gameObject.SetActive(false); }

    public void Place()
    {
        if (!surface) return;
        if (!presentation) presentation = GetComponent<WorldPickupPresentation>();
        var visual = presentation ? presentation.VisualRoot : transform;
        Quaternion yaw = visual.localRotation * Quaternion.Inverse(Quaternion.Euler(settledEuler));
        var basis = visual == transform ? Quaternion.identity : transform.rotation;
        Vector3 point = transform.position + basis * (yaw * center);
        Vector3 normal = Vector3.up;
        // One ground probe at landing; never run a per-frame projector or physics query.
        int count = Physics.RaycastNonAlloc(point + Vector3.up * .3f, Vector3.down, groundHits, 1f, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        point.y = transform.position.y - (presentation ? presentation.GroundClearance : .025f);
        for (int i=0;i<count;i++)
        {
            var hit=groundHits[i];
            if(hit.distance>=nearest || hit.normal.y<.35f || hit.collider.transform.IsChildOf(transform)
                || hit.collider.GetComponentInParent<CombatHealth>())continue;
            nearest=hit.distance;point.y=hit.point.y;normal=hit.normal;
        }
        surface.SetPositionAndRotation(point + normal * .012f,
            Quaternion.FromToRotation(Vector3.up, normal) * basis * yaw);
        var scale = transform.lossyScale;
        surface.localScale = new Vector3(size.x/Mathf.Max(.0001f,Mathf.Abs(scale.x)), 1f,
            size.y/Mathf.Max(.0001f,Mathf.Abs(scale.z)));
        surface.gameObject.SetActive(true);
    }
}
