using UnityEngine;

public static class SimulationChainFormationSolver
{
    public static void ResolveFollowPose(
        SimulationFollowTrail sourceTrail,
        Vector3 sourcePosition,
        Vector3 fallbackForward,
        float distanceBehind,
        float verticalOffset,
        out Vector3 targetPosition,
        out Vector3 pathForward)
    {
        fallbackForward.y = 0f;
        if (fallbackForward.sqrMagnitude <= 0.0001f)
            fallbackForward = Vector3.forward;
        fallbackForward.Normalize();

        distanceBehind = Mathf.Max(0f, distanceBehind);
        Vector3 pathCenter = sourcePosition;
        pathForward = fallbackForward;
        bool hasPathPose = sourceTrail != null
            && sourceTrail.TryGetPoseBehind(
                sourcePosition,
                distanceBehind,
                out pathCenter,
                out pathForward);
        if (!hasPathPose)
        {
            pathForward = fallbackForward;
            pathCenter = sourcePosition - pathForward * distanceBehind;
        }

        pathForward.y = 0f;
        if (pathForward.sqrMagnitude <= 0.0001f)
            pathForward = fallbackForward;
        pathForward.Normalize();

        targetPosition = pathCenter + Vector3.up * verticalOffset;
    }
}
