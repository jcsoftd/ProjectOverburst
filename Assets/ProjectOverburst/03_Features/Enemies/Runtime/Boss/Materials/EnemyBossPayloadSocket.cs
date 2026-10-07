using UnityEngine;

// Offsets are world metres in the actor-facing frame, independent of model/root scale.
public static class EnemyBossPayloadSocket
{
    public const float RockRevealFrame = 100f;
    public static Vector3 Position(Vector3 leftHand, Vector3 rightHand, Quaternion facing, Vector3 offsetMetres)
        => (leftHand + rightHand) * .5f + facing * offsetMetres;
}
