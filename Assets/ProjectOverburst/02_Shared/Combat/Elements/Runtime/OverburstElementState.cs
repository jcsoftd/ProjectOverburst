using System;
using UnityEngine;

// Pure target state and deadlines. Damage delivery and presentation belong to the controller.
public sealed class OverburstElementState
{
    private readonly int[] stacks = new int[OverburstElementRules.Count];
    private readonly float[] expires = new float[OverburstElementRules.Count];
    private readonly float[] nextTicks = new float[OverburstElementRules.Count];
    private readonly float[] intervals = new float[OverburstElementRules.Count];
    private float frozenUntil;
    public bool HasAny { get { for (int i = 0; i < stacks.Length; i++) if (stacks[i] > 0) return true; return false; } }
    public bool IsFrozen(float now) => stacks[1] > 0 && frozenUntil > now;
    public float FrozenRemaining(float now) => Mathf.Max(0f, frozenUntil - now);
    public int RawCount(WeaponElement element) => stacks[OverburstElementRules.Index(element)];
    public bool TryTakeTick(float now, out WeaponElement element, out int count)
    {
        int selected = -1;
        float earliest = float.PositiveInfinity;
        for (int i = 0; i < stacks.Length; i++)
            if (stacks[i] > 0 && intervals[i] > 0f && nextTicks[i] <= now + .00001f
                && nextTicks[i] <= expires[i] + .00001f && nextTicks[i] < earliest)
            { selected = i; earliest = nextTicks[i]; }
        element = selected < 0 ? WeaponElement.None : OverburstElementRules.At(selected);
        count = selected < 0 ? 0 : stacks[selected];
        if (selected < 0) return false;
        nextTicks[selected] += intervals[selected];
        return true;
    }
    public int Count(WeaponElement element, float now)
    {
        Expire(now);
        int index = OverburstElementRules.Index(element);
        return index >= 0 ? stacks[index] : 0;
    }
    public float Remaining(WeaponElement element, float now)
    {
        int index = OverburstElementRules.Index(element);
        return index >= 0 ? Mathf.Max(0f, expires[index] - now) : 0f;
    }
    public bool Add(WeaponElement element, float now, OverburstElementTuning tuning, float freezeDurationMultiplier = 1f, int freezeReduction = 0, int corrosionExtra = 0, bool freezeImmune = false)
    {
        Expire(now);
        int index = OverburstElementRules.Index(element);
        if (index < 0 || tuning == null || float.IsNaN(now) || float.IsInfinity(now)) return false;
        bool alreadyFrozen = index == 1 && IsFrozen(now);
        if (alreadyFrozen && !freezeImmune) return false; // light attacks neither shatter nor prolong freeze
        if (stacks[index] == 0)
        {
            intervals[index] = tuning.TickInterval(element);
            nextTicks[index] = now + intervals[index];
        }
        int cap = Mathf.Max(1, tuning.maximumStacks) + (element == WeaponElement.Dark ? Mathf.Clamp(corrosionExtra, 0, 2) : 0);
        stacks[index] = Mathf.Min(cap, stacks[index] + 1);
        expires[index] = now + tuning.StatusDuration(element);
        if (alreadyFrozen) { expires[index] = frozenUntil; return true; }
        if (index == 1 && stacks[index] >= Mathf.Clamp(tuning.maximumStacks - freezeReduction, 1, Mathf.Max(1, tuning.maximumStacks)))
        {
            frozenUntil = now + Mathf.Max(0.1f, tuning.freezeDuration) * Mathf.Clamp(freezeDurationMultiplier, 1f, 2f);
            expires[index] = frozenUntil;
        }
        return true;
    }
    public int Consume(WeaponElement element, float now, out bool shattered)
    {
        Expire(now);
        int index = OverburstElementRules.Index(element);
        shattered = index == 1 && IsFrozen(now);
        if (index < 0 || (index == 1 && !shattered)) return 0;
        int count = stacks[index];
        stacks[index] = 0;
        expires[index] = 0f;
        intervals[index] = nextTicks[index] = 0f;
        if (index == 1) frozenUntil = 0f;
        return count;
    }
    public bool Expire(float now) => Expire(now, out _);
    // Removes up to amount stacks without touching the deadline. Freeze-bearing cold is never partially removed.
    public int Remove(WeaponElement element, int amount, float now)
    {
        Expire(now);
        int index = OverburstElementRules.Index(element);
        if (index < 0 || index == 1 || amount <= 0 || stacks[index] <= 0) return 0;
        int removed = Mathf.Min(amount, stacks[index]);
        stacks[index] -= removed;
        if (stacks[index] == 0)
        {
            expires[index] = 0f;
            intervals[index] = nextTicks[index] = 0f;
        }
        return removed;
    }
    public bool Expire(float now, out int expiredMask)
    {
        expiredMask = 0;
        for (int i = 0; i < stacks.Length; i++)
        {
            if (stacks[i] <= 0 || now < expires[i]) continue;
            // A tick at expiry belongs to this status. Do not lose it when a frame budget defers delivery.
            if (intervals[i] > 0f && nextTicks[i] <= expires[i] + .00001f) continue;
            stacks[i] = 0;
            expires[i] = 0f;
            intervals[i] = nextTicks[i] = 0f;
            if (i == 1) frozenUntil = 0f;
            expiredMask |= 1 << i;
        }
        return expiredMask != 0;
    }
    public void ClearElement(WeaponElement element)
    {
        int i = OverburstElementRules.Index(element); if (i < 0) return;
        stacks[i] = 0; expires[i] = intervals[i] = nextTicks[i] = 0; if (i == 1) frozenUntil = 0;
    }
    public void Clear()
    {
        Array.Clear(stacks, 0, stacks.Length);
        Array.Clear(expires, 0, expires.Length);
        Array.Clear(nextTicks, 0, nextTicks.Length);
        Array.Clear(intervals, 0, intervals.Length);
        frozenUntil = 0f;
    }
}
