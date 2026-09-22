using System;
using UnityEngine;

// Visual-only support for authored stationary turns. Movement and attack ownership remain unchanged.
[DefaultExecutionOrder(1100)]
[DisallowMultipleComponent]
public sealed class EnemyTurnFootPlanting : MonoBehaviour
{
    [Serializable]
    public sealed class Binding
    {
        public string endPath;
        public Vector3 soleInFoot;
        public AnimationCurve leftContact;
        public AnimationCurve rightContact;
    }

    private sealed class Foot
    {
        public Binding binding;
        public Transform upper, knee, end;
        public Vector3 planted, bendNormal;
        public bool hasPlant;
        public float weight;
    }

    [SerializeField] private Binding[] bindings = Array.Empty<Binding>();
    private Foot[] feet = Array.Empty<Foot>();
    private EnemyLocomotionAnimator locomotion;
    private int turnSequence = -1;

    public int BindingCount => bindings.Length;
    public bool HasValidBindings
    {
        get
        {
            if (feet.Length < 2) return false;
            foreach (var foot in feet)
                if (foot == null || foot.binding.leftContact == null || foot.binding.rightContact == null)
                    return false;
            return true;
        }
    }

    public void Configure(Binding[] value)
    {
        bindings = value ?? Array.Empty<Binding>();
        Resolve();
    }

    private void Awake() { Resolve(); }
    private void OnDisable() { ClearPlants(); }

    private void Resolve()
    {
        locomotion = GetComponent<EnemyLocomotionAnimator>();
        feet = new Foot[bindings.Length];
        for (int i = 0; i < bindings.Length; i++)
        {
            Transform end = transform.Find(bindings[i].endPath);
            if (end == null || end.parent == null || end.parent.parent == null)
                continue;
            feet[i] = new Foot { binding = bindings[i], end = end, knee = end.parent, upper = end.parent.parent };
        }
        ClearPlants();
    }

    private void ClearPlants()
    {
        foreach (var foot in feet)
            if (foot != null) { foot.hasPlant = false; foot.weight = 0f; }
        turnSequence = -1;
    }

    private void LateUpdate()
    {
        if (locomotion == null || !locomotion.IsTurning)
        {
            if (turnSequence != -1) ClearPlants();
            return;
        }
        if (turnSequence != locomotion.TurnSequence)
        {
            ClearPlants();
            turnSequence = locomotion.TurnSequence;
        }
        float phase = locomotion.TurnNormalizedTime;
        foreach (var foot in feet)
        {
            if (foot == null) continue;
            AnimationCurve contact = locomotion.TurnDirection < 0f ? foot.binding.leftContact : foot.binding.rightContact;
            float desired = contact == null ? 0f : Mathf.Clamp01(contact.Evaluate(phase));
            Vector3 sole = foot.end.TransformPoint(foot.binding.soleInFoot);
            if (!foot.hasPlant && desired > .05f)
            {
                foot.planted = sole;
                foot.hasPlant = true;
            }
            foot.weight = Mathf.MoveTowards(foot.weight, desired, Time.deltaTime * 18f);
            if (desired < .01f && foot.weight < .01f) foot.hasPlant = false;
            if (!foot.hasPlant || foot.weight <= .001f) continue;

            // Preserve the authored foot rotation; only correct the support chain's endpoint.
            Quaternion footRotation = foot.end.rotation;
            Vector3 target = foot.end.position + (foot.planted - sole) * foot.weight;
            SolveSupport(foot, target);
            foot.end.rotation = footRotation;
        }
    }

    private static void SolveSupport(Foot foot, Vector3 target)
    {
        Vector3 root = foot.upper.position;
        Vector3 upper = foot.knee.position - root;
        Vector3 lower = foot.end.position - foot.knee.position;
        float a = upper.magnitude, b = lower.magnitude;
        Vector3 offset = target - root;
        float distance = offset.magnitude;
        if (a < .001f || b < .001f || distance < .001f || distance > (a + b) * 1.08f)
            return;

        Vector3 normal = Vector3.Cross(upper, lower);
        if (normal.sqrMagnitude > .000001f) foot.bendNormal = normal.normalized;
        if (foot.bendNormal.sqrMagnitude < .000001f) return;
        Vector3 direction = offset / distance;
        Vector3 bend = Vector3.Cross(foot.bendNormal, direction).normalized;
        if (Vector3.Dot(bend, upper) < 0f) bend = -bend;
        float reach = Mathf.Clamp(distance, Mathf.Abs(a - b) + .0001f, a + b - .0001f);
        float along = (a * a + reach * reach - b * b) / (2f * reach);
        float height = Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
        Vector3 kneeTarget = root + direction * along + bend * height;
        foot.upper.rotation = Quaternion.FromToRotation(upper, kneeTarget - root) * foot.upper.rotation;
        foot.knee.rotation = Quaternion.FromToRotation(foot.end.position - foot.knee.position, target - foot.knee.position) * foot.knee.rotation;
    }
}
