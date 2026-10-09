using System;
using UnityEngine;

public enum BuffStackingPolicy
{
    RefreshOnly,
    StackAndRefresh,
    ReplaceStronger
}

[Serializable]
public sealed class BuffDefinition
{
    public string buffId = "health_pickup_regen";
    public string displayName = "Recovery";
    public float duration = 30f;
    public float tickInterval = 3f;
    public float initialHealPercent = 0.3f;
    public float healPercentPerTick = 0.05f;
    public float moveSpeedMultiplier = 1f;
    public BuffStackingPolicy stackingPolicy = BuffStackingPolicy.RefreshOnly;
    public int maxStacks = 1;
    public float strength = 1f;
    public bool resetTickOnRefresh = true;
    public float damagePerTick;
    public bool isDebuff;
    public bool iconPointsDown;
    public Sprite icon;
    public Color baseIndicatorColor = new Color(0.35f, 0.35f, 0.35f, 0.85f);
    public Color indicatorColor = new Color(0.12f, 1f, 0.28f, 1f);
    public GameObject tickVfxPrefab;

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(buffId))
            buffId = "health_pickup_regen";

        duration = Mathf.Max(0.01f, Finite(duration, 30f));
        tickInterval = Mathf.Max(0.05f, Finite(tickInterval, 3f));
        initialHealPercent = Mathf.Max(0f, Finite(initialHealPercent, 0f));
        healPercentPerTick = Mathf.Max(0f, Finite(healPercentPerTick, 0f));
        moveSpeedMultiplier = Mathf.Clamp(Finite(moveSpeedMultiplier, 1f), 0.05f, 10f);
        damagePerTick = Mathf.Max(0f, Finite(damagePerTick, 0f));
        strength = Mathf.Max(0f, Finite(strength, 0f));
        maxStacks = Mathf.Max(1, maxStacks);
        if (!Enum.IsDefined(typeof(BuffStackingPolicy), stackingPolicy))
            stackingPolicy = BuffStackingPolicy.RefreshOnly;
    }

    private static float Finite(float value, float fallback)
        => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;

    public BuffDefinition Clone()
    {
        return new BuffDefinition
        {
            buffId = buffId,
            displayName = displayName,
            duration = duration,
            tickInterval = tickInterval,
            initialHealPercent = initialHealPercent,
            healPercentPerTick = healPercentPerTick,
            moveSpeedMultiplier = moveSpeedMultiplier,
            stackingPolicy = stackingPolicy,
            maxStacks = maxStacks,
            strength = strength,
            resetTickOnRefresh = resetTickOnRefresh,
            damagePerTick = damagePerTick,
            isDebuff = isDebuff,
            iconPointsDown = iconPointsDown,
            icon = icon,
            baseIndicatorColor = baseIndicatorColor,
            indicatorColor = indicatorColor,
            tickVfxPrefab = tickVfxPrefab
        };
    }
}
