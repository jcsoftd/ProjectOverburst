using UnityEngine;

// Keeps the existing one-to-three-shot allocation and splits longer volleys once.
public readonly struct EnemyWeakProjectileDamageBudget
{
    public readonly float Total;
    public readonly int Count;
    private readonly EnemyWeakAttackDamageBudget shortBudget;
    private readonly float baseDamage, fractionalTail;
    private readonly int remainder;
    private EnemyWeakProjectileDamageBudget(float total, int count, EnemyWeakAttackDamageBudget small)
    {
        Total = total; Count = count; shortBudget = small;
        baseDamage = Mathf.Floor(total / count);
        remainder = Mathf.FloorToInt(total - baseDamage * count);
        fractionalTail = total - baseDamage * count - remainder;
    }
    public static bool TryCreate(float total, int count, out EnemyWeakProjectileDamageBudget budget)
    {
        budget = default;
        if (float.IsNaN(total) || float.IsInfinity(total) || count < 1 || count > 64 || total < count) return false;
        EnemyWeakAttackDamageBudget small = default;
        if (count <= 3 && !EnemyWeakAttackDamageBudget.TryCreate(total, count, out small)) return false;
        budget = new EnemyWeakProjectileDamageBudget(total, count, small); return true;
    }
    public float ForPhase(int phase) => phase < 0 || phase >= Count ? 0f
        : Count <= 3 ? shortBudget.ForPhase(phase)
        : baseDamage + (phase < remainder ? 1f : 0f) + (phase == Count - 1 ? fractionalTail : 0f);
}
