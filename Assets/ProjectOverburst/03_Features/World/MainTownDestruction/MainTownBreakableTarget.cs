using System.Collections.Generic;
using UnityEngine;

// The existing neutral contact route must not award enemy HP/on-hit rewards.
public sealed class MainTownBreakableTarget : CombatHealth, IDamageable
{
    MainTownDestruction owner;
    int entry;
    CombatTargetVolume[] contacts;
    public void Bind(MainTownDestruction controller, int index) { owner = controller; entry = index; ResetHealth(); }

    public void ConfigureContacts(CombatTargetVolume[] localVolumes, Quaternion rotation, Vector3 scale)
    {
        if (contacts == null || contacts.Length != localVolumes.Length)
            contacts = new CombatTargetVolume[localVolumes.Length];
        float planarScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        Bounds envelope = default;
        for (int i = 0; i < contacts.Length; i++)
        {
            var local = localVolumes[i];
            var volume = new CombatTargetVolume(transform.position + rotation * Vector3.Scale(local.Center, scale),
                local.Radius * planarScale, local.HalfHeight * Mathf.Abs(scale.y));
            contacts[i] = volume;
            var bounds = new Bounds(volume.Center, new Vector3(volume.Radius * 2, volume.HalfHeight * 2, volume.Radius * 2));
            if (i == 0) envelope = bounds; else envelope.Encapsulate(bounds);
        }
        // The registry uses an enclosing circle; actual weapon contact checks each body volume.
        float envelopeRadius = 0;
        for (int i = 0; i < contacts.Length; i++)
        {
            Vector3 delta = contacts[i].Center - envelope.center; delta.y = 0;
            envelopeRadius = Mathf.Max(envelopeRadius, delta.magnitude + contacts[i].Radius);
        }
        GetComponent<CombatTarget>().ConfigureVolume(envelope.center - transform.position, envelopeRadius, envelope.size.y);
    }

    public bool TryEvaluateAttack(AttackPatternRuntimeData pattern, AttackPatternBasis basis, out float progress)
    {
        progress = float.PositiveInfinity;
        if (contacts == null) return false;
        bool hit = false;
        for (int i = 0; i < contacts.Length; i++)
            if (AttackPatternEvaluator.TryEvaluate(pattern, basis, contacts[i], out float value))
            { hit = true; progress = Mathf.Min(progress, value); }
        return hit;
    }

    public static CombatTargetVolume[] CreateLocalContacts(MainTownDestructionCatalog.Definition definition)
    {
        var volumes = new List<CombatTargetVolume>();
        Transform root = definition.source.transform;
        foreach (var collider in definition.source.GetComponentsInChildren<Collider>(true))
        {
            if (collider.isTrigger) continue;
            Matrix4x4 matrix = root.worldToLocalMatrix * collider.transform.localToWorldMatrix;
            if (collider is CapsuleCollider capsule && capsule.direction == 1)
            {
                float radius = capsule.radius * Mathf.Max(matrix.MultiplyVector(Vector3.right).magnitude, matrix.MultiplyVector(Vector3.forward).magnitude);
                volumes.Add(new CombatTargetVolume(matrix.MultiplyPoint3x4(capsule.center), radius,
                    Mathf.Max(radius, capsule.height * matrix.MultiplyVector(Vector3.up).magnitude * .5f)));
            }
            else if (collider is BoxCollider box) AddBody(new Bounds(box.center, box.size), matrix, volumes);
            else if (collider is MeshCollider mesh && mesh.sharedMesh != null) AddBody(mesh.sharedMesh.bounds, matrix, volumes);
        }
        if (volumes.Count == 0) AddBody(definition.bounds, Matrix4x4.identity, volumes);
        return volumes.ToArray();
    }

    static void AddBody(Bounds bounds, Matrix4x4 matrix, List<CombatTargetVolume> volumes)
    {
        bool alongX = bounds.size.x >= bounds.size.z;
        float radius = Mathf.Max(.025f, Mathf.Min(bounds.extents.x, bounds.extents.z));
        float length = alongX ? bounds.size.x : bounds.size.z;
        int count = Mathf.Max(1, Mathf.CeilToInt(length / radius));
        float halfSpan = Mathf.Max(0, length * .5f - radius);
        for (int i = 0; i < count; i++)
        {
            var center = bounds.center;
            float offset = count == 1 ? 0 : Mathf.Lerp(-halfSpan, halfSpan, i / (float)(count - 1));
            if (alongX) center.x += offset; else center.z += offset;
            float planarScale = Mathf.Max(matrix.MultiplyVector(Vector3.right).magnitude, matrix.MultiplyVector(Vector3.forward).magnitude);
            volumes.Add(new CombatTargetVolume(matrix.MultiplyPoint3x4(center), radius * planarScale,
                Mathf.Max(.05f, bounds.extents.y * matrix.MultiplyVector(Vector3.up).magnitude)));
        }
    }
    void IDamageable.TakeDamage(DamageInfo info)
    {
        if (owner != null && info.damage > 0 && !info.isDamageOverTime)
            owner.TryBreak(entry, info);
    }
}
