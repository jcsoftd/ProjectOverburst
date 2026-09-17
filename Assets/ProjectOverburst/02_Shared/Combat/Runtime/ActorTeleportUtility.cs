using UnityEngine;

public static class ActorTeleportUtility
{
    public static void TeleportSafely(
        Transform target,
        Vector3 position,
        Quaternion rotation)
    {
        if (target == null)
            return;

        CharacterController characterController = target.GetComponent<CharacterController>();
        bool restoreCharacterController = characterController != null && characterController.enabled;
        if (restoreCharacterController)
            characterController.enabled = false;

        Rigidbody rigidbody = target.GetComponent<Rigidbody>();
        if (rigidbody != null)
        {
            if (!rigidbody.isKinematic)
            {
                rigidbody.linearVelocity = Vector3.zero;
                rigidbody.angularVelocity = Vector3.zero;
            }

            rigidbody.position = position;
            rigidbody.rotation = rotation;
        }

        target.SetPositionAndRotation(position, rotation);
        target.GetComponent<PlayerMovement>()?.ResetMotionAfterTeleport();
        Physics.SyncTransforms();
        CombatTargetRegistry.NotifySpatialChanged(target);

        if (restoreCharacterController)
            characterController.enabled = true;

        Physics.SyncTransforms();
    }

}
