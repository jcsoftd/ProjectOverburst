using UnityEngine;

[CreateAssetMenu(menuName = "OVERBURST/Enemies/Enemy Definition", fileName = "ED_Enemy_Normal")]
public sealed class EnemyDefinition : ScriptableObject
{
    [SerializeField] private string enemyId;
    [SerializeField] private string displayName;
    [SerializeField] private EnemySpeciesDefinition species;
    [SerializeField] private EnemyGradeProfile grade;
    [SerializeField] private EnemyVariantProfile variant;
    [SerializeField] private EnemyActor actorPrefab;
    [SerializeField] private EnemyAnimationProfile animationProfile;
    [SerializeField] private EnemyAbilitySet abilitySet;
    [SerializeField] private EnemyBehaviorProfile behaviorProfile;
    [SerializeField] private EnemyMovementProfile movementProfile;
    [SerializeField] private EnemyAiPreset aiPreset;
    [SerializeField] private EnemySquadParticipationMode squadParticipationMode = EnemySquadParticipationMode.SquadMember;

    [SerializeField] private EnemyTacticalProfile tacticalProfile;
    public EnemyTacticalProfile TacticalProfile => tacticalProfile;
    public void SetTacticalProfile(EnemyTacticalProfile profile) { tacticalProfile = profile; }

    public string EnemyId => enemyId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? enemyId : displayName;
    public EnemySpeciesDefinition Species => species;
    public EnemyGradeProfile Grade => grade;
    public EnemyVariantProfile Variant => variant;
    public EnemyActor ActorPrefab => actorPrefab;
    public EnemyAnimationProfile AnimationProfile => animationProfile != null
        ? animationProfile
        : species != null ? species.AnimationProfile : null;
    public EnemyAbilitySet AbilitySet => abilitySet != null
        ? abilitySet
        : species != null ? species.DefaultAbilitySet : null;
    public EnemyBehaviorProfile BehaviorProfile => behaviorProfile != null
        ? behaviorProfile
        : species != null ? species.BehaviorProfile : null;
    public EnemyMovementProfile MovementProfile => movementProfile != null
        ? movementProfile
        : species != null ? species.MovementProfile : null;
    public EnemyAiPreset SquadPursuitPreset => aiPreset;
    public EnemyAiPreset AiPreset => aiPreset;
    public EnemySquadParticipationMode SquadParticipationMode => squadParticipationMode;
    public bool IsValid => !string.IsNullOrWhiteSpace(enemyId)
        && species != null
        && species.IsValid
        && grade != null
        && grade.IsValid
        && variant != null
        && variant.IsValid
        && actorPrefab != null
        && AnimationProfile != null
        && AnimationProfile.IsValid
        && AbilitySet != null
        && AbilitySet.IsValid
        && BehaviorProfile != null
        && MovementProfile != null
        && (squadParticipationMode == EnemySquadParticipationMode.Independent || aiPreset != null);

    public EnemyRuntimeStats ResolveRuntimeStats(
        float difficultyMultiplier = 1f,
        float encounterMultiplier = 1f)
    {
        float difficulty = Mathf.Max(0.01f, difficultyMultiplier);
        float encounter = Mathf.Max(0.01f, encounterMultiplier);
        float combatScale = difficulty * encounter;
        float gradeScale = grade != null ? grade.ScaleMultiplier : 1f;
        Vector3 gradeVector = Vector3.one * gradeScale;

        return new EnemyRuntimeStats(
            (species != null ? species.BaseMaxHealth : 100f)
                * (grade != null ? grade.HealthMultiplier : 1f)
                * (variant != null ? variant.HealthMultiplier : 1f)
                * combatScale,
            (grade != null ? grade.DamageMultiplier : 1f)
                * (variant != null ? variant.DamageMultiplier : 1f)
                * combatScale,
            (grade != null ? grade.MoveSpeedMultiplier : 1f)
                * (variant != null ? variant.MoveSpeedMultiplier : 1f),
            (grade != null ? grade.AttackSpeedMultiplier : 1f)
                * (variant != null ? variant.AttackSpeedMultiplier : 1f),
            Vector3.Scale(gradeVector, variant != null ? variant.VisualScale : Vector3.one),
            Vector3.Scale(gradeVector, variant != null ? variant.CollisionScale : Vector3.one),
            Vector3.Scale(gradeVector, variant != null ? variant.AnchorScale : Vector3.one),
            variant != null ? variant.Tint : Color.white);
    }

    public void ConfigureIdentity(string id, string label)
    {
        enemyId = id != null ? id.Trim() : string.Empty;
        displayName = label != null ? label.Trim() : string.Empty;
    }

    public void ConfigureComposition(
        EnemySpeciesDefinition speciesDefinition,
        EnemyGradeProfile gradeProfile,
        EnemyVariantProfile variantProfile,
        EnemyActor prefab)
    {
        species = speciesDefinition;
        grade = gradeProfile;
        variant = variantProfile;
        actorPrefab = prefab;
    }

    public void ConfigureRuntime(
        EnemyAnimationProfile animations,
        EnemyAbilitySet abilities,
        EnemyBehaviorProfile behavior,
        EnemyMovementProfile movement,
        EnemyAiPreset squadPreset,
        EnemySquadParticipationMode participationMode)
    {
        animationProfile = animations;
        abilitySet = abilities;
        behaviorProfile = behavior;
        movementProfile = movement;
        aiPreset = squadPreset;
        squadParticipationMode = participationMode;
    }
}
