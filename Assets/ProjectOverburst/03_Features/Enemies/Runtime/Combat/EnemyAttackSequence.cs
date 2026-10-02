using UnityEngine;

// Shared across melee and special executors on the same actor, including pooled re-use.
public static class EnemyAttackSequence
{
    private static int next;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => next = 0;
    public static int Next()
    {
        next = next == int.MaxValue ? 1 : next + 1;
        return next;
    }
}
