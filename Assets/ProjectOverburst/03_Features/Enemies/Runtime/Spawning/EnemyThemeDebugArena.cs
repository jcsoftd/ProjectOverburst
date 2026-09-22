using UnityEngine;

// Development-only arena instance. Authored floor, map default and trigger zones live in its prefab.
public sealed class EnemyThemeDebugArena : MonoBehaviour
{
    public Transform entry;
    private CombatHealth protectedPlayer;

    public void ProtectPlayer(CombatHealth health)
    {
        ReleasePlayerProtection();
        protectedPlayer = health;
        if (protectedPlayer != null) protectedPlayer.SetDamageDeathPrevention(this, true);
    }

    public void ReleasePlayerProtection()
    {
        if (protectedPlayer != null) protectedPlayer.SetDamageDeathPrevention(this, false);
        protectedPlayer = null;
    }
    public void ClearEncounters()
    {
        foreach (var encounter in GetComponentsInChildren<EnemyThemeEncounter>(true)) encounter.StopEncounter(true);
        foreach (var zone in GetComponentsInChildren<EnemyThemeTriggerZone>(true)) zone.Rearm();
    }
    private void OnDisable() { ReleasePlayerProtection(); ClearEncounters(); }
    private void OnDestroy() { ReleasePlayerProtection(); }
}
