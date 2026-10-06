using UnityEngine;

// Opt-in placement for unusually large rigs. The warning still owns cue timing and eligibility.
[DisallowMultipleComponent]
public sealed class EnemyParryCueAnchor : MonoBehaviour
{
    [SerializeField] private Transform head;
    [SerializeField, Min(0f)] private float headClearance = .85f;
    [SerializeField, Min(0f)] private float silhouetteClearance = .3f;
    [SerializeField, Min(.1f)] private float cueSize = 3.2f;
    private SkinnedMeshRenderer[] bodies;

    public float CueSize => cueSize;
    public Transform Head => head;

    public void Configure(Transform animatedHead, float clearance, float size)
    { head = animatedHead; headClearance = Mathf.Max(0f, clearance); cueSize = Mathf.Max(.1f, size); bodies = null; }

    public bool TryResolve(Camera camera, out Vector3 position)
    {
        position = default;
        if (head == null || !(head == transform || head.IsChildOf(transform))) return false;
        position = head.position + Vector3.up * headClearance;
        if (camera == null) return true;
        if (bodies == null) bodies = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        bool found = false; Bounds silhouette = default;
        for (int i = 0; i < bodies.Length; i++)
        {
            var body = bodies[i];
            if (body == null || !body.enabled || !body.gameObject.activeInHierarchy) continue;
            if (!found) { silhouette = body.bounds; found = true; }
            else silhouette.Encapsulate(body.bounds);
        }
        if (!found) return true;
        Vector3 toCue = position - camera.transform.position;
        float distance = toCue.magnitude;
        if (distance <= .01f) return true;
        // Preserve the head's screen position while bringing the glint in front of raised arms/shoulders.
        var ray = new Ray(camera.transform.position, toCue / distance);
        if (silhouette.IntersectRay(ray, out float front) && front >= .1f && front < distance)
            position = ray.GetPoint(Mathf.Max(.1f, front - silhouetteClearance));
        return true;
    }
}
