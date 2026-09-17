using UnityEngine;

public static class TwoPointFootGroundSolver
{
    public readonly struct Settings
    {
        public readonly LayerMask GroundLayer;
        public readonly Vector3 UpDirection;
        public readonly float ProbeUpDistance;
        public readonly float ProbeDownDistance;
        public readonly float ProbeRadius;
        public readonly float FlatContactHeightTolerance;
        public readonly float MaxGroundAngle;

        public Settings(
            LayerMask groundLayer,
            Vector3 upDirection,
            float probeUpDistance,
            float probeDownDistance,
            float probeRadius,
            float flatContactHeightTolerance,
            float maxGroundAngle)
        {
            GroundLayer = groundLayer;
            UpDirection = upDirection.sqrMagnitude > 0.0001f ? upDirection.normalized : Vector3.up;
            ProbeUpDistance = Mathf.Max(0f, probeUpDistance);
            ProbeDownDistance = Mathf.Max(0.01f, probeDownDistance);
            ProbeRadius = Mathf.Max(0.001f, probeRadius);
            FlatContactHeightTolerance = Mathf.Max(0f, flatContactHeightTolerance);
            MaxGroundAngle = Mathf.Clamp(maxGroundAngle, 0f, 89f);
        }
    }

    public readonly struct Solution
    {
        public readonly Quaternion TargetRotation;
        public readonly bool HasHeelContact;
        public readonly bool HasToeContact;
        public readonly bool IsFlatSurface;
        public readonly float ParallelAngleError;

        public bool HasTwoContacts => HasHeelContact && HasToeContact;

        public Solution(
            Quaternion targetRotation,
            bool hasHeelContact,
            bool hasToeContact,
            bool isFlatSurface,
            float parallelAngleError)
        {
            TargetRotation = targetRotation;
            HasHeelContact = hasHeelContact;
            HasToeContact = hasToeContact;
            IsFlatSurface = isFlatSurface;
            ParallelAngleError = parallelAngleError;
        }
    }

    public static bool TrySolve(
        Vector3 animatedIkPosition,
        Quaternion animatedIkRotation,
        Transform heel,
        Transform toe,
        in Settings settings,
        out Solution solution)
    {
        solution = default;
        if (heel == null || toe == null)
            return false;

        bool hasHeelContact = TryProbe(heel.position, settings, out RaycastHit heelHit);
        bool hasToeContact = TryProbe(toe.position, settings, out RaycastHit toeHit);
        if (!hasHeelContact && !hasToeContact)
            return false;

        if (!hasHeelContact || !hasToeContact)
        {
            solution = new Solution(
                animatedIkRotation,
                hasHeelContact,
                hasToeContact,
                false,
                0f);
            return true;
        }

        Vector3 animatedSegment = toe.position - heel.position;
        if (animatedSegment.sqrMagnitude <= 0.000001f)
        {
            solution = new Solution(
                animatedIkRotation,
                true,
                true,
                false,
                0f);
            return true;
        }

        Vector3 up = settings.UpDirection;
        float contactHeightDifference = Mathf.Abs(Vector3.Dot(toeHit.point - heelHit.point, up));
        bool isFlatSurface = contactHeightDifference <= settings.FlatContactHeightTolerance;

        Vector3 targetSegment = isFlatSurface
            ? Vector3.ProjectOnPlane(animatedSegment, up)
            : toeHit.point - heelHit.point;

        if (targetSegment.sqrMagnitude <= 0.000001f)
            targetSegment = Vector3.ProjectOnPlane(animatedSegment, up);

        if (targetSegment.sqrMagnitude <= 0.000001f)
        {
            solution = new Solution(
                animatedIkRotation,
                true,
                true,
                isFlatSurface,
                0f);
            return true;
        }

        if (Vector3.Dot(animatedSegment, targetSegment) < 0f)
            targetSegment = -targetSegment;

        Quaternion rotationCorrection = Quaternion.FromToRotation(
            animatedSegment.normalized,
            targetSegment.normalized);
        Quaternion targetRotation = rotationCorrection * animatedIkRotation;

        Vector3 localHeel = Quaternion.Inverse(animatedIkRotation) * (heel.position - animatedIkPosition);
        Vector3 localToe = Quaternion.Inverse(animatedIkRotation) * (toe.position - animatedIkPosition);
        Vector3 predictedSegment = targetRotation * (localToe - localHeel);
        Vector3 referenceNormal = isFlatSurface
            ? up
            : GetAverageNormal(heelHit.normal, toeHit.normal, up);
        float parallelAngleError = Mathf.Abs(90f - Vector3.Angle(predictedSegment, referenceNormal));

        solution = new Solution(
            targetRotation,
            true,
            true,
            isFlatSurface,
            parallelAngleError);
        return true;
    }

    private static bool TryProbe(Vector3 contactPosition, in Settings settings, out RaycastHit hit)
    {
        Vector3 origin = contactPosition + settings.UpDirection * settings.ProbeUpDistance;
        float distance = settings.ProbeUpDistance + settings.ProbeDownDistance;
        bool hasHit = Physics.SphereCast(
            origin,
            settings.ProbeRadius,
            -settings.UpDirection,
            out hit,
            distance,
            settings.GroundLayer,
            QueryTriggerInteraction.Ignore);

        return hasHit
            && Vector3.Angle(hit.normal, settings.UpDirection) <= settings.MaxGroundAngle;
    }

    private static Vector3 GetAverageNormal(Vector3 first, Vector3 second, Vector3 fallback)
    {
        Vector3 average = first + second;
        return average.sqrMagnitude > 0.0001f ? average.normalized : fallback;
    }
}
