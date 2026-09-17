using System.Collections.Generic;

public sealed class AttackHitRegistry
{
    private readonly HashSet<int> targetIds = new HashSet<int>();

    public void Clear()
    {
        targetIds.Clear();
    }

    public bool TryRegister(int targetId)
    {
        return targetIds.Add(targetId);
    }

    public bool Contains(int targetId)
    {
        return targetIds.Contains(targetId);
    }
}
