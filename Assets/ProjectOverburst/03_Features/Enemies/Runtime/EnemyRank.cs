using System.Collections.Generic;
using UnityEngine;

public enum EnemyRankType
{
    Normal,
    Elite
}

public sealed class EnemyRank : MonoBehaviour
{
    private static readonly HashSet<EnemyRank> activeEnemies = new HashSet<EnemyRank>();
    public static uint ActiveRevision { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry()
    {
        activeEnemies.Clear();
        unchecked { ActiveRevision++; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RestoreActiveRegistry()
    {
        // Covers Enter Play Mode with both domain and scene reload disabled.
        foreach (EnemyRank enemy in FindObjectsByType<EnemyRank>(FindObjectsSortMode.None))
            if (enemy.isActiveAndEnabled) activeEnemies.Add(enemy);
        unchecked { ActiveRevision++; }
    }

    [SerializeField] private EnemyRankType rank = EnemyRankType.Normal;
    [SerializeField] private string displayName;

    public EnemyRankType Rank { get { return rank; } }
    public EnemyGradeType GradeType { get; private set; } = EnemyGradeType.Normal;
    public int Level { get; private set; } = 1;

    private EnemyRankType authoredRank;
    private string authoredDisplayName;
    private bool authoredStateCaptured;
    private CombatHealth health;
    private float authoredMaxHealth;
    private bool experienceGranted;
    private EncounterContext encounter;
    public EncounterContext Encounter => encounter ?? EncounterContext.Test;

    public void ConfigureEncounter(EncounterContext context)
    {
        encounter = context ?? EncounterContext.Test;
        AssignLevel();
    }

    private void Awake()
    {
        CaptureAuthoredState();
        health = GetComponent<CombatHealth>();
        if (health != null) authoredMaxHealth = health.MaxHp;
    }

    private void OnEnable()
    {
        if (activeEnemies.Add(this)) { unchecked { ActiveRevision++; } }
        if (health == null) health = GetComponent<CombatHealth>();
        if (health != null) health.OnDead += AwardExperience;
        experienceGranted = false;
        AssignLevel();
        if (health != null && GetComponent<EnemyActor>() == null)
            ApplyLevelToHealth(authoredMaxHealth > 0f ? authoredMaxHealth : health.MaxHp);
    }

    private void OnDisable()
    {
        if (activeEnemies.Remove(this)) { unchecked { ActiveRevision++; } }
        if (health != null) health.OnDead -= AwardExperience;
    }

    public static void CollectActive(List<EnemyRank> buffer)
    {
        if (buffer == null)
            return;

        buffer.Clear();
        foreach (EnemyRank enemy in activeEnemies)
        {
            if (enemy != null && enemy.isActiveAndEnabled)
                buffer.Add(enemy);
        }
    }

    public void ConfigureFromDefinition(EnemyDefinition definition)
    {
        CaptureAuthoredState();
        EnemyGradeProfile grade = definition != null ? definition.Grade : null;
        GradeType = grade != null ? grade.GradeType : EnemyGradeType.Normal;
        rank = GradeType == EnemyGradeType.Normal
            ? EnemyRankType.Normal
            : EnemyRankType.Elite;
        displayName = definition != null ? definition.DisplayName : authoredDisplayName;
        AssignLevel();
        unchecked { ActiveRevision++; }
    }

    public void ResetForPool()
    {
        unchecked { ActiveRevision++; }
        CaptureAuthoredState();
        rank = authoredRank;
        displayName = authoredDisplayName;
        GradeType = authoredRank == EnemyRankType.Elite
            ? EnemyGradeType.Elite
            : EnemyGradeType.Normal;
        Level = 1;
        encounter = null;
        experienceGranted = false;
    }

    public void ApplyLevelToHealth(float authoredBase)
    {
        if (health == null) health = GetComponent<CombatHealth>();
        if (health != null)
            health.SetMaxHp(Mathf.Max(1f, authoredBase) * OverburstGrowthRules.EnemyHealthFactor(Level), true);
    }

    private void AssignLevel()
    {
        Level = Encounter.MapLevel;
        unchecked { ActiveRevision++; }
    }

    private void AwardExperience(CombatHealth source, DamageInfo info)
    {
        if (!Encounter.CanGrantRewards || experienceGranted || info.source == null
            || info.source.GetComponentInParent<PlayerActorRuntime>() == null) return;
        experienceGranted = true;
        PlayerProgression progression = PlayerProgression.Current;
        if (progression != null)
            progression.AddExperience(Mathf.RoundToInt(OverburstGrowthRules.ExperienceForKill(Level, GradeType, progression.Level) * Encounter.ExperienceMultiplier));
    }

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(displayName))
                return displayName;

            return gameObject.name.Replace("(Clone)", string.Empty).Trim();
        }
    }

    private void CaptureAuthoredState()
    {
        if (authoredStateCaptured)
            return;

        authoredRank = rank;
        authoredDisplayName = displayName;
        GradeType = rank == EnemyRankType.Elite
            ? EnemyGradeType.Elite
            : EnemyGradeType.Normal;
        authoredStateCaptured = true;
    }
}
