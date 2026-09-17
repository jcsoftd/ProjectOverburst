using UnityEngine;

public enum CombatTeam
{
    Neutral,
    PlayerParty,
    Enemy
}

[DisallowMultipleComponent]
public sealed class CombatAffiliation : MonoBehaviour
{
    [SerializeField] private CombatTeam team = CombatTeam.Neutral;

    public CombatTeam Team => team;

    public void Configure(CombatTeam configuredTeam)
    {
        team = configuredTeam;
    }
}

public static class CombatTargetFilter
{
    public static bool CanDamage(CombatTarget source, CombatTarget target)
    {
        if (target == null || !target.IsAlive || source == target)
            return false;

        CombatTeam sourceTeam = source != null
            ? source.Team
            : CombatTeam.Neutral;
        CombatTeam targetTeam = target.Team;

        return sourceTeam == CombatTeam.Neutral
            || targetTeam == CombatTeam.Neutral
            || sourceTeam != targetTeam;
    }
}
