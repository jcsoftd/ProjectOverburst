using System;
using UnityEngine;

// Pure target state. No damage ticks, combo indices, elemental combinations or Unity object ownership.
public sealed class OverburstElementState
{
    private readonly int[] stacks = new int[4];
    private readonly float[] expires = new float[4];
    private float frozenUntil;
    public bool HasAny { get { for (int i = 0; i < 4; i++) if (stacks[i] > 0) return true; return false; } }
    public bool IsFrozen(float now) => stacks[1] > 0 && frozenUntil > now;
    public float FrozenRemaining(float now) => Mathf.Max(0f, frozenUntil - now);
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
    public bool Add(WeaponElement element, float now, OverburstElementTuning tuning, float freezeDurationMultiplier = 1f)
    {
        Expire(now);
        int index = OverburstElementRules.Index(element);
        if (index < 0 || tuning == null || float.IsNaN(now) || float.IsInfinity(now)) return false;
        if (index == 1 && IsFrozen(now)) return false; // light attacks neither shatter nor prolong freeze
        stacks[index] = Mathf.Min(Mathf.Max(1, tuning.maximumStacks), stacks[index] + 1);
        expires[index] = now + Mathf.Max(0.1f, tuning.statusDuration);
        if (index == 1 && stacks[index] >= Mathf.Max(1, tuning.maximumStacks))
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
        if (index == 1) frozenUntil = 0f;
        return count;
    }
    public bool Expire(float now)
    {
        bool changed = false;
        for (int i = 0; i < 4; i++)
        {
            if (stacks[i] <= 0 || now < expires[i]) continue;
            stacks[i] = 0;
            expires[i] = 0f;
            if (i == 1) frozenUntil = 0f;
            changed = true;
        }
        return changed;
    }
    public void Clear()
    {
        Array.Clear(stacks, 0, stacks.Length);
        Array.Clear(expires, 0, expires.Length);
        frozenUntil = 0f;
    }
}
