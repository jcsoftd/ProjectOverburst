using UnityEngine;

public static class MeleeAttackLock // 근접 이동 잠금
{
    public static void Begin(PlayerMovement playerController, float duration, Vector3 lockedDirection)
    {
        if (playerController == null)
            return;

        playerController.BeginMeleeAttackMoveLock(duration, lockedDirection);
    }
}
