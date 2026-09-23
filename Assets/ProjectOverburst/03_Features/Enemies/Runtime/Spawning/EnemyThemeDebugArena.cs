using UnityEngine;
using TMPro;

// Development-only arena instance. Authored floor, map default and trigger zones live in its prefab.
public sealed class EnemyThemeDebugArena : MonoBehaviour
{
    public Transform entry;
    private CombatHealth protectedPlayer;
    public bool HasActiveEncounters
    {
        get
        {
            foreach (var encounter in GetComponentsInChildren<EnemyThemeEncounter>(true))
                if (encounter.HasOutstandingLeases) return true;
            return false;
        }
    }

    public bool ConfigureTrialMode(EnemyThemeTrialMode mode)
    {
        if (HasActiveEncounters) return false;
        foreach (var encounter in GetComponentsInChildren<EnemyThemeEncounter>(true))
        {
            var table = encounter.Table;
            var roster = EnemyThemeTrialPresets.Resolve(table, mode);
            encounter.ConfigureTrialRoster(roster);
            var label = encounter.transform.Find("Zone label")?.GetComponent<TextMeshPro>();
            if (label != null)
                label.text = $"{table.DisplayName}\n{EnemyThemeTrialPresets.Label(mode)} {roster.Total}마리 × 3회";
        }
        return true;
    }

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
