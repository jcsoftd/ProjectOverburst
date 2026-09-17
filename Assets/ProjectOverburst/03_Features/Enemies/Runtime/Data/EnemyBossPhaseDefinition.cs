using UnityEngine;

[CreateAssetMenu(
    menuName = "OVERBURST/Enemies/Boss Phase Definition",
    fileName = "EBPD_Boss_Phase01")]
public sealed class EnemyBossPhaseDefinition : ScriptableObject
{
    [SerializeField] private string phaseId;
    [SerializeField] private string displayName;
    [SerializeField, Range(0f, 1f)]
    private float enterAtOrBelowNormalizedHealth = 1f;
    [SerializeField] private EnemyAbilitySet abilitySet;

    public string PhaseId => phaseId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName)
        ? phaseId
        : displayName;
    public float EnterAtOrBelowNormalizedHealth =>
        Mathf.Clamp01(enterAtOrBelowNormalizedHealth);
    public EnemyAbilitySet AbilitySet => abilitySet;
    public bool IsValid => !string.IsNullOrWhiteSpace(phaseId)
        && abilitySet != null
        && abilitySet.IsValid;

    public void Configure(
        string id,
        string label,
        float healthThreshold,
        EnemyAbilitySet abilities)
    {
        phaseId = id != null ? id.Trim() : string.Empty;
        displayName = label != null ? label.Trim() : string.Empty;
        enterAtOrBelowNormalizedHealth = Mathf.Clamp01(healthThreshold);
        abilitySet = abilities;
    }
}
