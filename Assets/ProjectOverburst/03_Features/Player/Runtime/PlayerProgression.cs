using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerProgression : MonoBehaviour
{
    private const string LevelKey = "Overburst.PlayerLevel.v1";
    private const string ExperienceKey = "Overburst.PlayerExperience.v1";
    private PlayerContext context;
    private PlayerEquipment equipment;
    private CombatHealth health;
    private float appliedHealthBonus;

    public static PlayerProgression Current => PlayerContext.Instance != null
        ? PlayerContext.Instance.GetComponent<PlayerProgression>() : null;
    public static int CurrentLevel => Current != null ? Current.Level : 1;
    public int Level { get; private set; } = 1;
    public int Experience { get; private set; }
    public int ExperienceToNext => OverburstGrowthRules.ExperienceToNext(Level);
    public float ExperienceProgress => Level >= OverburstGrowthRules.MaximumLevel ? 1f
        : ExperienceToNext > 0 ? Mathf.Clamp01((float)Experience / ExperienceToNext) : 0f;
    public float Armor => OverburstGrowthRules.PlayerArmorBonus(Level) + GearStatTotals.From(equipment).Armor;
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
        Level = OverburstGrowthRules.ClampLevel(PlayerPrefs.GetInt(LevelKey, 1));
        Experience = Level >= OverburstGrowthRules.MaximumLevel ? 0
            : Mathf.Clamp(PlayerPrefs.GetInt(ExperienceKey, 0), 0, Mathf.Max(0, ExperienceToNext - 1));
    }

    private void OnDisable()
    {
        Save();
        if (context != null) context.CurrentActorChanged -= BindActor;
        if (equipment != null) equipment.GearSlotsChanged -= RefreshStats;
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

    public void AddExperience(int amount)
    {
        if (amount <= 0 || Level >= OverburstGrowthRules.MaximumLevel) return;
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
        {
            Overburst.Persistence.AccountGameplaySession.Run(() => { AddExperience(amount); return true; });
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
        if (Overburst.Persistence.AccountGameplaySession.Current == null && !Overburst.Persistence.AccountBootstrap.Attempted)
        {
            PlayerPrefs.SetInt(LevelKey, Level);
            PlayerPrefs.SetInt(ExperienceKey, Experience);
        }
        int nextLevel = Level;
        Overburst.Persistence.AccountGameplaySession.Notify(() =>
        {
            RefreshStats();
            if (nextLevel > previousLevel) LeveledUp?.Invoke(previousLevel, nextLevel);
            Changed?.Invoke();
        });
    }

    public void RefreshStats()
    {
        if (equipment != null) equipment.RefreshCurrentWeaponStats();
        if (health == null) return;
        float bonus = OverburstGrowthRules.PlayerHealthBonus(Level) + GearStatTotals.From(equipment).MaxHealth;
        float previousMaximum = health.MaxHp;
        float nextMaximum = Mathf.Max(1f, previousMaximum - appliedHealthBonus + bonus);
        appliedHealthBonus = bonus;
        health.SetMaxHp(nextMaximum, false);
        if (!health.IsDead && nextMaximum > previousMaximum)
            health.Heal(nextMaximum - previousMaximum);
        Changed?.Invoke();
    }

    private void BindActor(PlayerActorRuntime actor)
    {
        if (equipment != null) equipment.GearSlotsChanged -= RefreshStats;
        CombatHealth nextHealth = actor != null ? actor.Health : null;
        if (nextHealth != health) appliedHealthBonus = 0f;
        equipment = actor != null ? actor.Equipment : null;
        health = nextHealth;
        if (equipment != null) equipment.GearSlotsChanged += RefreshStats;
        RefreshStats();
    }

    private void Save()
    {
        if (Overburst.Persistence.AccountGameplaySession.Current != null || Overburst.Persistence.AccountBootstrap.Attempted) return;
        PlayerPrefs.SetInt(LevelKey, Level);
        PlayerPrefs.SetInt(ExperienceKey, Experience);
        PlayerPrefs.Save();
    }
}
