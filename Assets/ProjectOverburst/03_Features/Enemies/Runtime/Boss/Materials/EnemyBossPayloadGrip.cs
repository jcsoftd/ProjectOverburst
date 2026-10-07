using UnityEngine;

// Fits a held body with arm rotations only; the authored motion remains the pose source.
public sealed class EnemyBossPayloadGrip
{
    readonly Arm left, right;
    public bool IsApplied => left.Applied || right.Applied;

    public EnemyBossPayloadGrip(Transform leftHand, Transform rightHand)
    { left = new Arm(leftHand); right = new Arm(rightHand); }

    public void Restore() { left.Restore(); right.Restore(); }

    public void Apply(Transform payload, Vector3 leftPoint, Vector3 rightPoint, float weight, float palmTilt)
    {
        Restore();
        if (payload == null || weight <= .0001f) return;
        ApplyPoints(payload.TransformPoint(leftPoint), payload.TransformPoint(rightPoint), weight, payload.forward, palmTilt);
    }

    public void ApplyPoints(Vector3 leftPoint, Vector3 rightPoint, float weight, Vector3 tiltAxis, float palmTilt)
    {
        Restore();
        if (weight <= .0001f) return;
        left.Fit(leftPoint, weight, tiltAxis, -palmTilt); right.Fit(rightPoint, weight, tiltAxis, palmTilt);
    }

    sealed class Arm
    {
        readonly Transform upper, elbow, hand;
        Quaternion sourceUpper, sourceElbow, sourceHand, appliedUpper, appliedElbow, appliedHand;
        public bool Applied { get; private set; }
        public Arm(Transform hand)
        { this.hand = hand; elbow = hand != null ? hand.parent : null; upper = elbow != null ? elbow.parent : null; }

        // A frozen hold has no Animator evaluation. Restore only rotations still written by us;
        // fresh animation/reaction samples have already replaced them and must remain intact.
        public void Restore()
        {
            if (!Applied) return;
            Restore(upper, appliedUpper, sourceUpper); Restore(elbow, appliedElbow, sourceElbow); Restore(hand, appliedHand, sourceHand);
            Applied = false;
        }
        static void Restore(Transform bone, Quaternion applied, Quaternion source)
        { if (bone != null && Mathf.Abs(Quaternion.Dot(bone.localRotation, applied)) > .999999f) bone.localRotation = source; }

        public void Fit(Vector3 point, float weight, Vector3 tiltAxis, float tilt)
        {
            if (upper == null || elbow == null || hand == null) return;
            sourceUpper = upper.localRotation; sourceElbow = elbow.localRotation; sourceHand = hand.localRotation;
            Quaternion wristRotation = hand.rotation;
            Vector3 origin = upper.position, first = elbow.position - origin, second = hand.position - elbow.position;
            float a = first.magnitude, b = second.magnitude;
            if (a < .001f || b < .001f) return;
            Vector3 target = Vector3.Lerp(hand.position, point, Mathf.Clamp01(weight));
            Vector3 reach = target - origin; float length = reach.magnitude;
            if (length < .001f) return;
            Vector3 direction = reach / length;
            length = Mathf.Clamp(length, Mathf.Abs(a - b) + .001f, a + b - .001f);
            target = origin + direction * length;
            // Keep the original elbow side instead of mirroring the arm around a new pole.
            Vector3 bend = Vector3.ProjectOnPlane(first, direction);
            if (bend.sqrMagnitude < .000001f) bend = Vector3.ProjectOnPlane(upper.forward, direction);
            if (bend.sqrMagnitude < .000001f) bend = Vector3.Cross(direction, Vector3.up);
            bend.Normalize();
            float along = (a * a - b * b + length * length) / (2f * length);
            float height = Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            Vector3 elbowTarget = origin + direction * along + bend * height;
            upper.rotation = Quaternion.FromToRotation(elbow.position - origin, elbowTarget - origin) * upper.rotation;
            elbow.rotation = Quaternion.FromToRotation(hand.position - elbow.position, target - elbow.position) * elbow.rotation;
            hand.rotation = Quaternion.AngleAxis(tilt * Mathf.Clamp01(weight), tiltAxis) * wristRotation;
            appliedUpper = upper.localRotation; appliedElbow = elbow.localRotation; appliedHand = hand.localRotation;
            Applied = true;
        }
    }
}
