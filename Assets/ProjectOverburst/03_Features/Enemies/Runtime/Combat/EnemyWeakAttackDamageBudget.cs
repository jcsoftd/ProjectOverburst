using UnityEngine;

// A value snapshot. Missed contacts never donate their allocation to a later hit.
public readonly struct EnemyWeakAttackDamageBudget
{
    public readonly float Total;
    public readonly int Count;
    private readonly float first, second, third;
    private EnemyWeakAttackDamageBudget(float total, int count, float a, float b, float c)
    { Total = total; Count = count; first = a; second = b; third = c; }

    public static bool TryCreate(float total, int count, out EnemyWeakAttackDamageBudget budget)
    {
        budget = default;
        if (float.IsNaN(total) || float.IsInfinity(total) || count < 1 || count > 3 || total < count) return false;
        if (count == 1) { budget = new EnemyWeakAttackDamageBudget(total, 1, total, 0, 0); return true; }
        float a = Mathf.Clamp(OverburstCombatBalance.RoundStat(total / count), 1f, total - (count - 1));
        float b = count == 2 ? total - a : Mathf.Clamp(OverburstCombatBalance.RoundStat(total / count), 1f, total - a - 1f);
        float c = count == 3 ? total - a - b : 0;
        budget = new EnemyWeakAttackDamageBudget(total, count, a, b, c);
        return true;
    }

    public float ForPhase(int phase) => phase < 0 || phase >= Count ? 0f : phase == 0 ? first : phase == 1 ? second : third;
}
