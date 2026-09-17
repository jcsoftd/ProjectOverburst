using UnityEngine;

[CreateAssetMenu(
    menuName = "OVERBURST/Enemies/Boss Definition",
    fileName = "EBD_Boss")]
public sealed class EnemyBossDefinition : ScriptableObject
{
    [SerializeField] private string bossId;
    [SerializeField] private string displayName;
    [SerializeField] private EnemyBossPhaseDefinition[] phases;

    public string BossId => bossId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName)
        ? bossId
        : displayName;
    public int PhaseCount => phases != null ? phases.Length : 0;

    public bool IsValid
    {
        get
        {
            if (string.IsNullOrWhiteSpace(bossId) || PhaseCount == 0)
                return false;

            float previousThreshold = 1.001f;
            for (int i = 0; i < phases.Length; i++)
            {
                EnemyBossPhaseDefinition phase = phases[i];
                if (phase == null || !phase.IsValid)
                    return false;

                float threshold = phase.EnterAtOrBelowNormalizedHealth;
                if (i == 0 && threshold < 0.999f)
                    return false;
                if (threshold >= previousThreshold)
                    return false;
                previousThreshold = threshold;
            }

            return true;
        }
    }

    public EnemyBossPhaseDefinition GetPhase(int index)
    {
        return index >= 0 && index < PhaseCount ? phases[index] : null;
    }

    public int ResolvePhaseIndex(float normalizedHealth)
    {
        if (!IsValid)
            return -1;

        float health = Mathf.Clamp01(normalizedHealth);
        int resolved = 0;
        for (int i = 1; i < phases.Length; i++)
        {
            if (health <= phases[i].EnterAtOrBelowNormalizedHealth)
                resolved = i;
            else
                break;
        }

        return resolved;
    }

    public void Configure(
        string id,
        string label,
        EnemyBossPhaseDefinition[] configuredPhases)
    {
        bossId = id != null ? id.Trim() : string.Empty;
        displayName = label != null ? label.Trim() : string.Empty;
        phases = configuredPhases != null
            ? (EnemyBossPhaseDefinition[])configuredPhases.Clone()
            : new EnemyBossPhaseDefinition[0];
    }
}
