using System.Collections.Generic;
using UnityEngine;

/// <summary>One actor's rolling combat bonus budget; all equipped flasks receive the same accepted amount.</summary>
public sealed class FlaskChargeRules
{
    public const float PassiveCombatRate = 2f;
    public const float ParticipationSeconds = 4f;
    public const float BonusWindowSeconds = 10f;
    public const float BonusWindowMaximum = 10f;
    public const float SharedUseInterval = .25f;
    private struct Grant { public float time; public float value; }
    private readonly Queue<Grant> grants = new Queue<Grant>();
    private float windowTotal;
    private float lastParticipation = float.NegativeInfinity;
    private float lastUse = float.NegativeInfinity;

    public void Participate(float now) { lastParticipation = now; }
    public bool IsParticipating(float now) => now >= lastParticipation && now - lastParticipation <= ParticipationSeconds;
    public float ParticipatingDuration(float previous, float now) => Mathf.Max(0f,
        Mathf.Min(now, lastParticipation + ParticipationSeconds) - Mathf.Max(previous, lastParticipation));
    public bool CanUse(float now) => now - lastUse >= SharedUseInterval;
    public void Used(float now) { lastUse = now; }

    public float TakeBonus(float requested, float now)
    {
        while (grants.Count > 0 && now - grants.Peek().time >= BonusWindowSeconds)
            windowTotal = Mathf.Max(0f, windowTotal - grants.Dequeue().value);
        float value = Mathf.Clamp(requested, 0f, Mathf.Max(0f, BonusWindowMaximum - windowTotal));
        if (value > 0f) { grants.Enqueue(new Grant { time = now, value = value }); windowTotal += value; }
        return value;
    }

    public void Clear()
    {
        grants.Clear(); windowTotal = 0f;
        lastParticipation = lastUse = float.NegativeInfinity;
    }

    public static void Initialize(FlaskInstanceState state, FlaskStats stats)
    {
        if (state == null || state.chargeInitialized) return;
        state.charge = stats.capacity;
        state.chargeInitialized = true;
    }

    public static void Gain(FlaskInstanceState state, FlaskStats stats, float amount)
    {
        if (state == null || amount <= 0f) return;
        state.charge = Mathf.Clamp(state.charge + amount, 0f, stats.capacity);
    }

    public static bool Spend(FlaskInstanceState state, FlaskStats stats)
    {
        if (state == null || !state.chargeInitialized || state.charge + .00001f < stats.cost) return false;
        state.charge = Mathf.Max(0f, state.charge - stats.cost);
        return true;
    }

    public static int Uses(FlaskInstanceState state, FlaskStats stats)
    {
        return state == null || stats.cost <= 0f ? 0 : Mathf.Max(0, Mathf.FloorToInt((state.charge + .00001f) / stats.cost));
    }
}
