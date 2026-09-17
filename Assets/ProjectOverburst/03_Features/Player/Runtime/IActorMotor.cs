public interface IActorMotor
{
    float BaseMoveSpeed { get; }
    void ApplyMovementIntent(ActorMovementIntent intent, float deltaTime);
    void Stop();
    void ResetMotionAfterTeleport();
}
