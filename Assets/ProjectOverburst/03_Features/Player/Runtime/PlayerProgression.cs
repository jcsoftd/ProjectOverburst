using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerProgression : MonoBehaviour
{
    private PlayerContext context;
    private PlayerEquipment equipment;
    private CombatHealth health;
    private float appliedHealthBonus;
    private int pendingExperience;
    private long pendingBagExperienceUnits;
    private int localBagExperienceCarry;
    private float nextExperienceRetry;

    public static PlayerProgression Current => PlayerContext.Instance != null
        ? PlayerContext.Instance.GetComponent<PlayerProgression>() : null;
    public static int CurrentLevel => Current != null ? Current.Level : 1;
    public int Level { get; private set; } = 1;
    public int Experience { get; private set; }
    public int ExperienceToNext => OverburstGrowthRules.ExperienceToNext(Level);
    public float ExperienceProgress => Level >= OverburstGrowthRules.MaximumLevel ? 1f
        : ExperienceToNext > 0 ? Mathf.Clamp01((float)Experience / ExperienceToNext) : 0f;
    public float Armor => CombatBalanceFormulas.PlayerArmor(Level, GearStatTotals.From(equipment),
        MapRunBuffs.Bonus(MapBuffKind.Armor)) + Overburst.Persistence.SkillTreeBonuses.ArmorFlat;
    public event Action Changed;
    public event Action<int, int> LeveledUp;

    internal void ApplyAccountProgression(int level, int experience, bool notify)
    {
        if (level < 1 || level > OverburstGrowthRules.MaximumLevel || experience < 0)
            throw new ArgumentOutOfRangeException(nameof(level));
        Level = level;
        Experience = experience;
        if (notify) RefreshStats();
    }

    private void Awake()
    {
        Level = 1;
        Experience = 0;
    }

    private void OnDisable()
    {
        Save();
        if (context != null) context.CurrentActorChanged -= BindActor;
        if (equipment != null) { equipment.GearSlotsChanged -= RefreshStats; equipment.GemSlotsChanged -= RefreshGemStats; }
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) Save();
    }

    private void OnApplicationQuit() => Save();

    public void Bind(PlayerContext owner)
    {
        if (context != null) context.CurrentActorChanged -= BindActor;
        context = owner;
        if (context != null) context.CurrentActorChanged += BindActor;
        BindActor(context != null ? context.CurrentActor : null);
    }

    private void LateUpdate()
    {
        if ((pendingExperience > 0 || pendingBagExperienceUnits > 0) && Time.unscaledTime >= nextExperienceRetry)
            FlushPendingExperience();
    }

    public bool FlushPendingExperience()
    {
        if (pendingExperience == 0 && pendingBagExperienceUnits == 0) return true;
        if (!Overburst.Persistence.AccountGameplaySession.ShouldRoute) return false;
        int amount = pendingExperience;
        long bagUnits = pendingBagExperienceUnits;
        bool committed = Overburst.Persistence.AccountGameplaySession.Current.GrantExperience(this, amount, bagUnits);
        if (committed) { pendingExperience -= amount; pendingBagExperienceUnits -= bagUnits; nextExperienceRetry = 0f; }
        else nextExperienceRetry = Time.unscaledTime + 1f;
        return committed;
    }

    public static long CalculateKillExperienceUnits(int amount, float baseMultiplier, float additionalPercent, float bagPercent)
    {
        if (amount < 0 || float.IsNaN(baseMultiplier) || float.IsInfinity(baseMultiplier) || baseMultiplier < 0
            || float.IsNaN(additionalPercent) || float.IsInfinity(additionalPercent) || additionalPercent < 0
            || float.IsNaN(bagPercent) || float.IsInfinity(bagPercent) || bagPercent < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        return checked((long)Math.Round(amount * (double)baseMultiplier
            * (1d + (additionalPercent + (double)bagPercent) * .01d) * BagQuality.RewardScale,
            MidpointRounding.AwayFromZero));
    }

    public void AddKillExperience(int amount, float additionalPercent = 0, float baseMultiplier = 1)
    {
        if (amount <= 0 || Level >= OverburstGrowthRules.MaximumLevel) return;
        long totalUnits = CalculateKillExperienceUnits(amount, baseMultiplier, additionalPercent,
            BagQuality.EquippedBonus(BagStat.KillExperience));
        totalUnits = checked(totalUnits * TesterSupportItemData.CurrentMultiplier);
        amount = checked((int)(totalUnits / BagQuality.RewardScale));
        long units = totalUnits % BagQuality.RewardScale;
        if (totalUnits == 0) return;
        if (Overburst.Persistence.AccountGameplaySession.Current != null)
        {
            pendingExperience = checked(pendingExperience + amount);
            pendingBagExperienceUnits = checked(pendingBagExperienceUnits + units);
            return;
        }
        int rewarded = BagQuality.ApplyReward(amount, units, localBagExperienceCarry, out int carry);
        localBagExperienceCarry = carry;
        AddExperienceCore(rewarded);
    }

    public void AddExperience(int amount, bool applyRewardMultiplier = true)
    {
        if (amount <= 0 || Level >= OverburstGrowthRules.MaximumLevel) return;
        AddExperienceCore(checked(amount * (applyRewardMultiplier ? TesterSupportItemData.CurrentMultiplier : 1)));
    }

    private void AddExperienceCore(int amount)
    {
        if (amount <= 0 || Level >= OverburstGrowthRules.MaximumLevel) return;
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
        {
            pendingExperience = checked(pendingExperience + amount);
            return;
        }
        int previousLevel = Level;
        Experience = checked(Experience + amount);
        while (Level < OverburstGrowthRules.MaximumLevel && Experience >= ExperienceToNext)
        {
            Experience -= ExperienceToNext;
            Level++;
        }
        if (Level >= OverburstGrowthRules.MaximumLevel) Experience = 0;
        NotifyExperienceCommitted(previousLevel);
    }

    internal void ApplyCommittedExperience(int level, int experience)
    {
        int previousLevel = Level;
        ApplyAccountProgression(level, experience, false);
        NotifyExperienceCommitted(previousLevel);
    }

    internal void NotifyExperienceCommitted(int previousLevel)
    {
        int nextLevel = Level;
        Overburst.Persistence.AccountGameplaySession.Notify(() =>
        {
            RefreshStats();
            if (nextLevel > previousLevel)
            {
                // A level reward refills the final maximum, including run HP modifiers.
                // RestoreLivingHealth cannot revive a dead actor or apply a healing penalty twice.
                if (health != null && health.isActiveAndEnabled && !health.IsDead)
                    health.RestoreLivingHealth(health.MaxHp);
                LeveledUp?.Invoke(previousLevel, nextLevel);
            }
            Changed?.Invoke();
        });
    }

    private void RefreshGemStats() => RefreshStatsCore(false);
    public void RefreshStats() => RefreshStatsCore(true);
    public void RefreshSkillTreeStats() => RefreshStatsCore(false);
    private void RefreshStatsCore(bool healIncrease)
    {
        if (equipment != null) equipment.RefreshCurrentWeaponStats();
        if (health == null) return;
        float previousMaximum = health.MaxHp;
        float baseMaximum = Mathf.Max(1f, health.UnmodifiedMaxHp - appliedHealthBonus);
        float permanentBonus = CombatBalanceFormulas.PlayerPermanentHealthBonus(Level, GearStatTotals.From(equipment));
        permanentBonus += (baseMaximum + permanentBonus) * Overburst.Persistence.SkillTreeBonuses.HealthPercent / 100f;
        float preRunMaximum = Mathf.Max(1f, baseMaximum + permanentBonus);
        float bonus = permanentBonus + preRunMaximum * MapRunBuffs.Bonus(MapBuffKind.MaxHealth);
        float nextMaximum = Mathf.Max(1f, baseMaximum + bonus);
        appliedHealthBonus = bonus;
        health.SetUnmodifiedMaxHp(nextMaximum, false);
        if (healIncrease && !health.IsDead && health.MaxHp > previousMaximum)
            health.Heal(health.MaxHp - previousMaximum, notifyHealing: false);
        Changed?.Invoke();
    }

    private void BindActor(PlayerActorRuntime actor)
    {
        if (equipment != null) { equipment.GearSlotsChanged -= RefreshStats; equipment.GemSlotsChanged -= RefreshGemStats; }
        CombatHealth nextHealth = actor != null ? actor.Health : null;
        if (nextHealth != health) appliedHealthBonus = 0f;
        equipment = actor != null ? actor.Equipment : null;
        health = nextHealth;
        if (equipment != null) { equipment.GemSlotsChanged += RefreshGemStats; equipment.GearSlotsChanged += RefreshStats; }
        RefreshStats();
    }

    private void Save()
    {
        FlushPendingExperience();
    }
}
