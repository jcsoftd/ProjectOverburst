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

    private readonly StatusEffectRuntime effects = new StatusEffectRuntime();
    private Action<BuffSnapshot, GameObject, int> executeTick;
    private Func<bool> tickIsCurrent;
    private int tickLifecycle;
    private BuffSnapshot tickSnapshot;
    private CombatHealth subscribedHealth;
    private static readonly Action<CombatHealth, float> HealWithFeedback = PlayerHealFeedback.ApplyHealPercent;
    public event Action BuffsChanged { add => effects.Changed += value; remove => effects.Changed -= value; }

    public float ActiveMoveSpeedMultiplier
    {
        get
        {
            float multiplier = effects.MoveSpeedMultiplier(FlaskCombatModifiers.Bonus(gameObject, FlaskEffect.SlowResistance));
            multiplier *= 1f + FlaskCombatModifiers.Bonus(gameObject, FlaskEffect.MoveSpeed);
            return Mathf.Clamp(multiplier, 0.05f, 10f);
        }
    }

    private void Awake()
    {
        ResolveHealth();
        executeTick = ExecuteTick;
        tickIsCurrent = IsTickLifetimeCurrent;
    }

    private void OnEnable()
    {
        ResolveHealth();
        BindHealth();
    }

    private void OnDisable()
    {
        UnbindHealth();
        ClearBuffs();
    }

    private void HandleDead(CombatHealth _, DamageInfo info) => ClearBuffs();
    private void HandleReset(CombatHealth _) => ClearBuffs();

    private void ClearBuffs()
    {
        effects.Clear();
    }

    private void Update()
    {
        ResolveHealth();
        BindHealth();
        if (executeTick == null) executeTick = ExecuteTick;
        effects.Advance(Time.deltaTime, executeTick);
    }

    public void ApplyBuff(BuffDefinition definition)
    {
        TryApplyBuff(definition);
    }

    public BuffApplyResult TryApplyBuff(BuffDefinition definition, GameObject source = null)
    {
        ResolveHealth();
        BindHealth();
        if (!isActiveAndEnabled || health == null || health.IsDead) return BuffApplyResult.Rejected;
        var result = effects.TryApply(definition, out var instance, source);
        if (result != BuffApplyResult.Rejected && effects.Find(instance.BuffId) == instance)
            ShowBuffAppliedPopup(instance.Definition);
        return result;
    }

    public bool RemoveBuff(string buffId) => effects.Remove(buffId);
    public int GetSnapshots(List<BuffSnapshot> results) => effects.GetSnapshots(results);

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
            stackingPolicy = BuffStackingPolicy.ReplaceStronger,
            strength = 1f - Mathf.Clamp(moveSpeedMultiplier, 0.05f, 1f),
            isDebuff = true,
            iconPointsDown = true,
            baseIndicatorColor = new Color(0.38f, 0.12f, 0.14f, 0.85f),
            indicatorColor = new Color(1f, 0.18f, 0.12f, 1f)
        };

        ApplyBuff(definition);
    }

    public int GetActiveBuffs(List<BuffInstance> results)
    {
        return effects.GetActiveBuffs(results);
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

    private void ExecuteTick(BuffSnapshot snapshot, GameObject source, int ticks)
    {
        tickLifecycle = effects.LifecycleVersion;
        tickSnapshot = snapshot;
        if (tickIsCurrent == null) tickIsCurrent = IsTickLifetimeCurrent;
        try { BuffEffectExecutor.ApplyTick(health, snapshot, source, ticks, HealWithFeedback, tickIsCurrent); }
        finally { tickSnapshot = default; }
    }

    private bool IsTickLifetimeCurrent()
        => isActiveAndEnabled && tickLifecycle == effects.LifecycleVersion && health == subscribedHealth
            && effects.IsCurrent(tickSnapshot);

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
        return effects.Find(buffId);
    }

    private void BindHealth()
    {
        if (!isActiveAndEnabled || ReferenceEquals(subscribedHealth, health)) return;
        UnbindHealth();
        // A replacement health component starts a new target lifetime.
        effects.Clear();
        subscribedHealth = health;
        if (subscribedHealth == null) return;
        subscribedHealth.OnDead += HandleDead;
        subscribedHealth.OnReset += HandleReset;
    }

    private void UnbindHealth()
    {
        if (subscribedHealth != null)
        {
            subscribedHealth.OnDead -= HandleDead;
            subscribedHealth.OnReset -= HandleReset;
        }
        subscribedHealth = null;
    }

    private void ResolveHealth()
    {
        if (health == null)
            health = GetComponentInParent<CombatHealth>();

        if (health == null)
            health = GetComponentInChildren<CombatHealth>();
    }
}
