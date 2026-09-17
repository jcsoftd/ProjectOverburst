using System;
using UnityEngine;

public enum BuffStackingPolicy
{
    RefreshOnly
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

        duration = Mathf.Max(0.01f, duration);
        tickInterval = Mathf.Max(0.05f, tickInterval);
        initialHealPercent = Mathf.Max(0f, initialHealPercent);
        healPercentPerTick = Mathf.Max(0f, healPercentPerTick);
        moveSpeedMultiplier = Mathf.Clamp(moveSpeedMultiplier, 0.05f, 10f);
        stackingPolicy = BuffStackingPolicy.RefreshOnly;
    }

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
            isDebuff = isDebuff,
            iconPointsDown = iconPointsDown,
            icon = icon,
            baseIndicatorColor = baseIndicatorColor,
            indicatorColor = indicatorColor,
            tickVfxPrefab = tickVfxPrefab
        };
    }
}
