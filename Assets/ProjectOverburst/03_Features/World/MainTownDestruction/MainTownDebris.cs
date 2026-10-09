using UnityEngine;

public sealed class MainTownDebris : MonoBehaviour, ITransientVfxPlayback
{
    public Rigidbody[] bodies;
    Vector3[] positions, scales;
    Quaternion[] rotations;
    Collider[] colliders;
    Vector3 direction;
    float started;
    const float Lifetime = 3f;

    void Cache()
    {
        if (positions != null) return;
        positions = new Vector3[bodies.Length]; rotations = new Quaternion[bodies.Length];
        scales = new Vector3[bodies.Length]; colliders = new Collider[bodies.Length];
        for (int i = 0; i < bodies.Length; i++)
        {
            positions[i] = bodies[i].transform.localPosition; rotations[i] = bodies[i].transform.localRotation;
            scales[i] = bodies[i].transform.localScale; colliders[i] = bodies[i].GetComponent<Collider>();
        }
    }
    public void Prepare(Vector3 hitDirection)
    {
        direction = Vector3.ProjectOnPlane(hitDirection, Vector3.up);
        direction = direction.sqrMagnitude > .001f ? direction.normalized : Vector3.forward;
    }
    public void RestartVfx()
    {
        Cache(); started = Time.time;
        for (int i = 0; i < bodies.Length; i++)
        {
            var body = bodies[i]; body.transform.localPosition = positions[i];
            body.transform.localRotation = rotations[i]; body.transform.localScale = scales[i];
            body.gameObject.SetActive(true); colliders[i].enabled = true;
            body.isKinematic = false; body.useGravity = true;
            body.linearVelocity = direction * Random.Range(1.2f, 2f) + Vector3.up * Random.Range(.6f, 1.2f);
            body.angularVelocity = Vector3.Cross(Vector3.up, direction) * Random.Range(1.4f, 2.2f) + Random.insideUnitSphere * .5f;
        }
    }
    public void StopAndClearVfx()
    {
        Cache();
        for (int i = 0; i < bodies.Length; i++)
        {
            var body = bodies[i];
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            body.isKinematic = true; body.useGravity = false; colliders[i].enabled = false;
            body.transform.localPosition = positions[i]; body.transform.localRotation = rotations[i];
            body.transform.localScale = scales[i]; body.gameObject.SetActive(false);
        }
    }
    void Update()
    {
        float age = Time.time - started;
        if (age < Lifetime - .4f) return;
        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i].transform.localScale = scales[i] * Mathf.Max(.001f, (Lifetime - age) / .4f);
            if (age >= Lifetime) { colliders[i].enabled = false; bodies[i].gameObject.SetActive(false); }
        }
    }
}
