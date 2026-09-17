using UnityEngine;

[CreateAssetMenu(menuName = "OVERBURST/Enemies/Species Definition", fileName = "ESD_Enemy")]
public sealed class EnemySpeciesDefinition : ScriptableObject
{
    [SerializeField] private string speciesId;
    [SerializeField] private string displayName;
    [SerializeField] private GameObject vendorPrefab;
    [SerializeField] private EnemyAnimationProfile animationProfile;
    [SerializeField] private EnemyAbilitySet defaultAbilitySet;
    [SerializeField] private EnemyCombatRole combatRole = EnemyCombatRole.Vanguard;
    [SerializeField, Min(1f)] private float baseMaxHealth = 100f;
    [SerializeField] private EnemyMovementProfile movementProfile;
    [SerializeField] private EnemyBehaviorProfile behaviorProfile;
    [SerializeField, Min(0f)] private float threatCost = 1f;

    public string SpeciesId => speciesId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? speciesId : displayName;
    public GameObject VendorPrefab => vendorPrefab;
    public EnemyAnimationProfile AnimationProfile => animationProfile;
    public EnemyAbilitySet DefaultAbilitySet => defaultAbilitySet;
    public EnemyCombatRole CombatRole => combatRole;
    public float BaseMaxHealth => Mathf.Max(1f, baseMaxHealth);
    public EnemyMovementProfile MovementProfile => movementProfile;
    public EnemyBehaviorProfile BehaviorProfile => behaviorProfile;
    public float ThreatCost => Mathf.Max(0f, threatCost);
    public bool IsValid => !string.IsNullOrWhiteSpace(speciesId)
        && vendorPrefab != null
        && animationProfile != null
        && animationProfile.IsValid
        && defaultAbilitySet != null
        && defaultAbilitySet.IsValid
        && movementProfile != null
        && behaviorProfile != null;

    public void Configure(
        string id,
        string label,
        GameObject sourcePrefab,
        EnemyAnimationProfile animations,
        EnemyAbilitySet abilities)
    {
        speciesId = id != null ? id.Trim() : string.Empty;
        displayName = label != null ? label.Trim() : string.Empty;
        vendorPrefab = sourcePrefab;
        animationProfile = animations;
        defaultAbilitySet = abilities;
    }

    public void ConfigureRuntime(
        EnemyCombatRole role,
        float maxHealth,
        EnemyMovementProfile movement,
        EnemyBehaviorProfile behavior,
        float spawnThreatCost)
    {
        combatRole = role;
        baseMaxHealth = Mathf.Max(1f, maxHealth);
        movementProfile = movement;
        behaviorProfile = behavior;
        threatCost = Mathf.Max(0f, spawnThreatCost);
    }
}
