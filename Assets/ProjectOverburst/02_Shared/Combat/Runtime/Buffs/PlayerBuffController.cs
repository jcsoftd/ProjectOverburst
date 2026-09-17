using System;
using System.Collections.Generic;
using UnityEngine;

public class PlayerBuffController : MonoBehaviour
{
    public const string SlowBuffId = "damage_floor_slow";
    private const float MoveSpeedBuffThreshold = 0.001f;

    [SerializeField] private CombatHealth health;
    [SerializeField] private Color buffPopupColor = new Color(0.2f, 0.7f, 1f, 1f);
    [SerializeField] private Color debuffPopupColor = new Color(0.72f, 0.25f, 1f, 1f);

    private readonly List<BuffInstance> activeBuffs = new List<BuffInstance>();
    public event Action BuffsChanged;

    public float ActiveMoveSpeedMultiplier
    {
        get
        {
            float multiplier = 1f;
            for (int i = 0; i < activeBuffs.Count; i++)
            {
                BuffInstance instance = activeBuffs[i];
                if (instance != null && instance.Definition != null)
                    multiplier *= instance.Definition.moveSpeedMultiplier;
            }

            return Mathf.Clamp(multiplier, 0.05f, 10f);
        }
    }

    private void Awake()
    {
        ResolveHealth();
    }

    private void Update()
    {
        ResolveHealth();
        float deltaTime = Time.deltaTime;

        for (int i = activeBuffs.Count - 1; i >= 0; i--)
        {
            BuffInstance instance = activeBuffs[i];
            if (instance == null || instance.Definition == null)
            {
                activeBuffs.RemoveAt(i);
                NotifyBuffsChanged();
                continue;
            }

            if (instance.Tick(deltaTime))
                ApplyTickHeal(instance.Definition);

            if (instance.IsExpired())
            {
                activeBuffs.RemoveAt(i);
                NotifyBuffsChanged();
            }
        }
    }

    public void ApplyBuff(BuffDefinition definition)
    {
        if (definition == null)
            return;

        BuffDefinition runtimeDefinition = definition.Clone();
        runtimeDefinition.Normalize();
        BuffInstance existing = FindBuff(runtimeDefinition.buffId);
        if (existing != null)
        {
            existing.Refresh();
            NotifyBuffsChanged();
            ShowBuffAppliedPopup(runtimeDefinition);
            return;
        }

        activeBuffs.Add(new BuffInstance(runtimeDefinition));
        NotifyBuffsChanged();
        ShowBuffAppliedPopup(runtimeDefinition);
    }

    public void ApplySlowDebuff(float duration, float moveSpeedMultiplier)
    {
        BuffDefinition definition = new BuffDefinition
        {
            buffId = SlowBuffId,
            displayName = "Slow",
            duration = Mathf.Max(0.01f, duration),
            tickInterval = Mathf.Max(0.05f, duration),
            initialHealPercent = 0f,
            healPercentPerTick = 0f,
            moveSpeedMultiplier = Mathf.Clamp(moveSpeedMultiplier, 0.05f, 1f),
            isDebuff = true,
            iconPointsDown = true,
            baseIndicatorColor = new Color(0.38f, 0.12f, 0.14f, 0.85f),
            indicatorColor = new Color(1f, 0.18f, 0.12f, 1f)
        };

        ApplyBuff(definition);
    }

    public int GetActiveBuffs(List<BuffInstance> results)
    {
        if (results == null)
            return 0;

        results.Clear();
        for (int i = 0; i < activeBuffs.Count; i++)
        {
            BuffInstance instance = activeBuffs[i];
            if (instance != null && instance.Definition != null && !instance.IsExpired())
                results.Add(instance);
        }

        return results.Count;
    }

    public bool TryGetBuffRemainingRatio(string buffId, out float ratio)
    {
        ratio = 0f;
        BuffInstance instance = FindBuff(buffId);
        if (instance == null)
            return false;

        ratio = instance.RemainingRatio;
        return true;
    }

    public bool HasBuff(string buffId)
    {
        return FindBuff(buffId) != null;
    }

    private void ApplyTickHeal(BuffDefinition definition)
    {
        if (health == null || definition == null || definition.healPercentPerTick <= 0f)
            return;

        PlayerHealFeedback.ApplyHealPercent(health, definition.healPercentPerTick);
    }

    private void NotifyBuffsChanged()
    {
        BuffsChanged?.Invoke();
    }

    private void ShowBuffAppliedPopup(BuffDefinition definition)
    {
        if (definition == null)
            return;

        string text = GetBuffPopupText(definition);
        if (string.IsNullOrWhiteSpace(text))
            return;

        DamageNumberSpawner.SpawnStatusText(GetPopupPosition(), text, definition.isDebuff ? debuffPopupColor : buffPopupColor);
    }

    private string GetBuffPopupText(BuffDefinition definition)
    {
        if (definition.moveSpeedMultiplier > 1f + MoveSpeedBuffThreshold)
            return "이동속도+";

        if (definition.moveSpeedMultiplier < 1f - MoveSpeedBuffThreshold)
            return "이동속도-";

        if (!string.IsNullOrWhiteSpace(definition.displayName))
            return definition.displayName + (definition.isDebuff ? "-" : "+");

        return definition.isDebuff ? "디버프-" : "버프+";
    }

    private Vector3 GetPopupPosition()
    {
        if (health != null)
            return health.transform.position;

        return transform.position;
    }

    private BuffInstance FindBuff(string buffId)
    {
        if (string.IsNullOrWhiteSpace(buffId))
            return null;

        for (int i = 0; i < activeBuffs.Count; i++)
        {
            BuffInstance instance = activeBuffs[i];
            if (instance != null && instance.BuffId == buffId)
                return instance;
        }

        return null;
    }

    private void ResolveHealth()
    {
        if (health == null)
            health = GetComponentInParent<CombatHealth>();

        if (health == null)
            health = GetComponentInChildren<CombatHealth>();
    }
}
