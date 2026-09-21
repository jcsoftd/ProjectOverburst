using UnityEngine;

// Development-only arena instance. Authored floor, map default and trigger zones live in its prefab.
public sealed class EnemyThemeDebugArena : MonoBehaviour
{
    public Transform entry;
    public void ClearEncounters()
    {
        foreach (var encounter in GetComponentsInChildren<EnemyThemeEncounter>(true)) encounter.StopEncounter(true);
        foreach (var zone in GetComponentsInChildren<EnemyThemeTriggerZone>(true)) zone.Rearm();
    }
    private void OnDisable() { ClearEncounters(); }
}
