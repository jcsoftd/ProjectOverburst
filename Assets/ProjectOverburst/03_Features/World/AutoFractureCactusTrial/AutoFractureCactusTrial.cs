using UnityEngine;
using UnityEngine.InputSystem;

// Experiment adapter: scenery uses normal attack contact without actor HP/on-hit rewards.
[DisallowMultipleComponent]
public sealed class AutoFractureCactusTrial : CombatHealth, IDamageable
{
    [SerializeField] GameObject intactVisual;
    [SerializeField] Rigidbody[] fragments;
    [SerializeField] Collider intactCollider;
    [SerializeField] CombatTarget target;
    [SerializeField, Min(1f)] float debrisLifetime = 3f;
    Vector3[] positions, scales;
    Quaternion[] rotations;
    float brokenAt;

    public bool Broken { get; private set; }
    public bool DebrisCleared { get; private set; }
    public int BreakCount { get; private set; }
    public int FragmentCount => fragments?.Length ?? 0;
    public bool BlocksMovement => intactCollider != null && intactCollider.enabled;

    void CachePose()
    {
        if (positions != null) return;
        positions = new Vector3[FragmentCount];
        scales = new Vector3[FragmentCount];
        rotations = new Quaternion[FragmentCount];
        for (int i = 0; i < FragmentCount; i++)
        {
            positions[i] = fragments[i].transform.localPosition;
            rotations[i] = fragments[i].transform.localRotation;
            scales[i] = fragments[i].transform.localScale;
        }
    }

    void IDamageable.TakeDamage(DamageInfo info)
    {
        if (!Application.isPlaying || Broken || info.damage <= 0f || info.isDamageOverTime) return;
        CachePose();
        Broken = true; DebrisCleared = false; brokenAt = Time.time; BreakCount++;
        target.enabled = false; intactCollider.enabled = false; intactVisual.SetActive(false);
        Vector3 direction = Vector3.ProjectOnPlane(info.direction, Vector3.up);
        direction = direction.sqrMagnitude > .001f ? direction.normalized : transform.forward;
        var actor = PlayerContext.Instance?.CurrentActor;
        var playerColliders = actor != null ? actor.GetComponentsInChildren<Collider>() : null;
        for (int i = 0; i < FragmentCount; i++)
        {
            var body = fragments[i]; body.gameObject.SetActive(true);
            var collider = body.GetComponent<Collider>(); collider.enabled = true;
            if (playerColliders != null)
                foreach (var playerCollider in playerColliders)
                    if (playerCollider != null) Physics.IgnoreCollision(collider, playerCollider);
            body.isKinematic = false; body.useGravity = true;
            Vector3 away = Vector3.ProjectOnPlane(body.worldCenterOfMass - info.hitPoint, Vector3.up);
            body.linearVelocity = direction * Random.Range(1.8f, 2.8f)
                + away.normalized * Random.Range(.2f, .7f) + Vector3.up * Random.Range(1.0f, 2.3f);
            body.angularVelocity = Random.insideUnitSphere * 5f;
        }
    }

    public void ResetPlant()
    {
        CachePose();
        for (int i = 0; i < FragmentCount; i++)
        {
            var body = fragments[i];
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            body.isKinematic = true; body.useGravity = false;
            body.transform.localPosition = positions[i]; body.transform.localRotation = rotations[i];
            body.transform.localScale = scales[i]; body.GetComponent<Collider>().enabled = false;
            body.gameObject.SetActive(false);
        }
        intactVisual.SetActive(true); intactCollider.enabled = true; target.enabled = true;
        Broken = false; DebrisCleared = false; ResetHealth(); Physics.SyncTransforms();
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame
            && !GameplayInputBlocker.IsGameplayInputBlocked) ResetPlant();
        if (!Broken || DebrisCleared) return;
        float age = Time.time - brokenAt;
        if (age < debrisLifetime - .5f) return;
        float scale = Mathf.Clamp01((debrisLifetime - age) / .5f);
        for (int i = 0; i < FragmentCount; i++)
        {
            fragments[i].transform.localScale = scales[i] * Mathf.Max(.001f, scale);
            if (age >= debrisLifetime) fragments[i].gameObject.SetActive(false);
        }
        DebrisCleared = age >= debrisLifetime;
    }

    void OnDisable()
    {
        if (fragments == null) return;
        foreach (var body in fragments)
            if (body != null) { body.isKinematic = true; body.useGravity = false; body.gameObject.SetActive(false); }
    }
}
