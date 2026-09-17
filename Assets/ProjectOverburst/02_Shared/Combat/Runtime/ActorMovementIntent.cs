using UnityEngine;

public enum ActorMovementGait
{
    Run,
    Walk
}

public readonly struct ActorMovementIntent
{
    public ActorMovementIntent(
        Vector3 destination,
        Vector3 desiredDirection,
        Vector3 facingDirection,
        ActorMovementGait gait,
        float speedMultiplier,
        ActorMovementPriority priority,
        bool shouldMove)
    {
        Destination = destination;
        DesiredDirection = desiredDirection;
        FacingDirection = facingDirection;
        Gait = gait;
        SpeedMultiplier = Mathf.Max(0f, speedMultiplier);
        Priority = priority;
        ShouldMove = shouldMove;
    }

    public Vector3 Destination { get; }
    public Vector3 DesiredDirection { get; }
    public Vector3 FacingDirection { get; }
    public ActorMovementGait Gait { get; }
    public float SpeedMultiplier { get; }
    public ActorMovementPriority Priority { get; }
    public bool ShouldMove { get; }

    public static ActorMovementIntent Hold(
        Vector3 position,
        Vector3 facingDirection,
        ActorMovementGait gait)
    {
        return new ActorMovementIntent(
            position,
            Vector3.zero,
            facingDirection,
            gait,
            0f,
            ActorMovementPriority.Hold,
            false);
    }
}
