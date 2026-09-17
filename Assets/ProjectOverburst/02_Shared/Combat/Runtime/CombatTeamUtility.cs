using UnityEngine;

public static class CombatTeamUtility
{
    public static bool IsFriendlyPlayerActorDamage(CombatHealth targetHealth, DamageInfo info)
    {
        if (targetHealth == null || info.source == null)
            return false;

        CombatTeam sourceTeam = ResolveTeam(info.source);
        CombatTeam targetTeam = ResolveTeam(targetHealth.gameObject);
        if (sourceTeam != CombatTeam.Neutral && targetTeam != CombatTeam.Neutral)
            return sourceTeam == targetTeam;

        GameObject sourceActor = ResolvePlayerActorObject(info.source);
        if (sourceActor == null)
            return false;

        GameObject targetActor = ResolvePlayerActorObject(targetHealth.gameObject);
        return targetActor != null;
    }

    public static bool IsPlayerActorDamage(DamageInfo info)
    {
        return info.source != null
            && (ResolveTeam(info.source) == CombatTeam.PlayerParty
                || ResolvePlayerActorObject(info.source) != null);
    }

    public static bool IsPlayerActorHealth(CombatHealth health)
    {
        return health != null
            && (ResolveTeam(health.gameObject) == CombatTeam.PlayerParty
                || ResolvePlayerActorObject(health.gameObject) != null);
    }

    public static CombatTeam ResolveTeam(GameObject candidate)
    {
        if (candidate == null)
            return CombatTeam.Neutral;

        CombatAffiliation affiliation = candidate.GetComponentInParent<CombatAffiliation>();
        if (affiliation != null)
            return affiliation.Team;

        if (ResolvePlayerActorObject(candidate) != null)
            return CombatTeam.PlayerParty;

        if (candidate.layer == LayerMask.NameToLayer("Enemy")
            || candidate.GetComponentInParent<EnemyMovement>() != null
            || candidate.GetComponentInParent<EnemyController>() != null)
        {
            return CombatTeam.Enemy;
        }

        return CombatTeam.Neutral;
    }

    public static GameObject ResolvePlayerActorObject(GameObject candidate)
    {
        if (candidate == null)
            return null;

        PlayerActorRuntime actor = candidate.GetComponentInParent<PlayerActorRuntime>();
        if (actor != null)
            return actor.gameObject;

        PlayerMovement movement = candidate.GetComponentInParent<PlayerMovement>();
        if (movement != null)
            return movement.gameObject;

        Transform current = candidate.transform;
        while (current != null)
        {
            if (current.CompareTag("Player"))
                return current.gameObject;

            current = current.parent;
        }

        return null;
    }
}
