using System;
using UnityEngine;
using UnityEngine.AI;

public sealed partial class CrustaspikanEncounter
{
    RunWorldGate mapGate;
    MapBossArena mapArena;
    bool mapEngaged;
    float nextMapEntryAttempt;
    readonly Collider[] mapOverlaps = new Collider[64];
    NavMeshPath mapPath;
    public bool IsMapEncounter => mapGate != null;
    public bool MapEngaged => mapEngaged;

    public bool BeginMap(RunWorldGate gate, PlayerActorRuntime actor, EnemySpawnService service)
    {
        if (!gate || gate.BossArena == null || !actor || !service || service.gameObject.scene != gate.gameObject.scene)
            throw new ArgumentException("Prepared desert and its spawn service required.");
        mapGate = gate; mapArena = gate.BossArena; Settings = mapArena.Settings;
        player = actor; spawns = service; mapPath = new NavMeshPath();
        transform.position = mapArena.BossPoint;
        if (!spawns.RegisterAdditionalCatalog(Settings.materials.catalog, out string reason)
            || !spawns.RegisterAdditionalCatalog(Settings.composites.summonCatalog, out reason))
        { LastFailure = reason; return false; }
        // Reserve assets here; no boss actor exists until the player enters its room.
        return true;
    }
    void MapBossDamage(CombatHealth health, DamageInfo info, float amount, bool fatal)
    {
        if (fatal) BossDefeated();
        else if (amount > 0) mapEngaged = true;
    }
    void UpdateMap()
    {
        if (!player || !spawns || !mapGate) return;
        if (Defeated) { EndPresentation(); return; }
        if (WorldSessionState.Phase != WorldPhase.Run || WorldSessionState.ContentScene != gameObject.scene
            || PersistentSceneFlow.Instance?.IsSwitching == true) return;
        if (!mapEngaged)
        {
            if (player.Health.IsDead || Time.unscaledTime < nextMapEntryAttempt
                || !MapCombatGeometry.Contains(mapArena.Ground, player.transform.position)) return;
            nextMapEntryAttempt = Time.unscaledTime + 2f;
            if (!PreparePresentation() || !SpawnBoss() || !PlayEntrance())
            {
                EntranceCinematic?.Cancel(); ClearCombat(); EndPresentation();
                return;
            }
            mapEngaged = true;
        }
        if (IsIntroducing) { ResetMotionSamples(); return; }
        ObserveMotion(); Brain.Tick();
    }
    Vector3 ClampMapArena(Vector3 desired)
    {
        var definition = Settings.materials.actorDefinition;
        if (MapCombatGeometry.TryRuntimePoint(mapArena.Ground, definition, desired, false, mapOverlaps, mapPath, out var point)) return point;
        Vector3 best = Brain != null && Brain.IsActive ? Brain.Actor.transform.position : mapArena.BossPoint;
        float distance = (best - desired).sqrMagnitude;
        foreach (var candidate in mapArena.Ground.Points)
        {
            float next = (candidate - desired).sqrMagnitude;
            if (next >= distance || !MapCombatGeometry.TryRuntimePoint(mapArena.Ground, definition, candidate, false, mapOverlaps, mapPath, out point)) continue;
            best = point; distance = next;
        }
        return best;
    }
}
