using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class MonsterWeakAttackPlayerLoopVerifier
{
    public static string StartActualThemeActivation(string outputDirectory, string activationPlan)
    {
        activationPlan = Path.GetFullPath(activationPlan);
        string allowed = Path.GetFullPath(Path.Combine(Workspace, "개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!activationPlan.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !File.Exists(activationPlan))
            throw new ArgumentException("Private approved theme plan required.");
        var input = JObject.Parse(File.ReadAllText(activationPlan));
        var themeIds=input["themes"]?.Select(t=>(string)t["runtimeThemeId"]).ToArray();
        if ((string)input["schema"] != "overburst.v3.theme-activation.v1" || themeIds==null || themeIds.Length==0
            || themeIds.Distinct().Count()!=themeIds.Length || themeIds.Any(id=>string.IsNullOrEmpty(id)
                || Resources.Load<EnemyThemeTable>("Enemies/Themes/Tables/"+id)?.Validate(out _)!=true))
            throw new ArgumentException("Distinct saved and valid theme tables required.");
        return StartInternal(outputDirectory, false, 1, false, true, false, null, true, null, null, activationPlan);
    }
    static void ThemeRequire(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
    static EnemyActor[] FieldActors(MapMonsterField field) => field.GetComponentsInChildren<EnemyActor>()
        .Where(a => a.IsLeased).ToArray();
    static bool RuntimeThemeMatches(EnemyActor actor)
    {
        var definition = actor.Definition;
        bool squadMember = definition.SquadParticipationMode == EnemySquadParticipationMode.SquadMember;
        return actor.AI.SquadParticipationMode == definition.SquadParticipationMode
            && actor.AI.SquadPursuitPreset == (squadMember ? definition.AiPreset : null)
            && actor.AI.UsesSquadPursuit == squadMember
            && typeof(EnemyAIController).GetField("squadPursuitPreset", Private).GetValue(actor.AI) == definition.AiPreset;
    }
    static IEnumerator RunActualThemeCases()
    {
        bool entered = false;
        var input = JObject.Parse(File.ReadAllText(plan.themeActivationPath));
        float deadline = Time.unscaledTime + 180;
        while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || !WorldSessionState.IsHideout
            || PlayerInputFacade.Current == null || AccountGameplaySession.Current == null)
        { ThemeRequire(Time.unscaledTime < deadline, "Actual player boot timeout."); yield return null; }
        ThemeRequire(Same(AccountBootstrap.SaveDirectory, Account), "Actual isolated account mismatch.");
        Time.captureDeltaTime = 1f / 30;
        try
        {
            foreach (var theme in input["themes"])
            {
                string id = (string)theme["runtimeThemeId"];
                var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
                var mapDefinition = Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
                var map = new MapInstanceState { mapContentId = registry.IdFor(mapDefinition),
                    monsterThemeId = id, level = 21, grade = ItemGrade.Common };
                ThemeRequire(PersistentSceneFlow.Instance.EnterDebugRun(DiamondDungeonWorld.SceneName, map),
                    "Actual run entry rejected: " + PersistentSceneFlow.Instance.RunEntryError); entered = true;
                deadline = Time.unscaledTime + 180;
                while (PersistentSceneFlow.Instance.IsSwitching || WorldSessionState.Phase != WorldPhase.Run)
                { ThemeRequire(Time.unscaledTime < deadline, "Actual run boot timeout: " + id); yield return null; }
                var world = Object.FindFirstObjectByType<DiamondDungeonWorld>();
                ThemeRequire(world != null && world.Theme.ThemeId == id, "Actual map theme fallback: " + id);
                var service = world.SpawnBudget.GetComponent<EnemySpawnService>();
                var field = world.Fields.First(f => f.Band == 2 && !f.HasTriggered);
                foreach (var other in world.Fields) if (other != field) other.enabled = false;
                foreach (var evt in world.EventDirector.Events) evt.enabled = false;
                var playerActor = PlayerContext.GetOrCreate().CurrentActor;
                playerActor.Health.SetMaxHp(100000, true);
                var player = playerActor.transform;
                player.position = field.transform.position + Vector3.up * .3f;
                var body = player.GetComponent<Rigidbody>(); if (body != null) body.position = player.position;
                Physics.SyncTransforms();
                ThemeRequire(field.SpawnOnce(player), "Real field spawn rejected: " + id);
                int expectedCount = 2 * MapSpawnPolicy.WaveSize(map);
                deadline = Time.unscaledTime + 80;
                while (field.IsSpawning)
                { ThemeRequire(Time.unscaledTime < deadline, "Real field spawn budget timeout: " + id); yield return null; }
                var actors = FieldActors(field);
                var table = world.Theme;
                var counts = new int[3];
                foreach (var actor in actors)
                {
                    ThemeRequire(table.Entries.Any(e => e.definition == actor.Definition), "Unapproved actual spawn.");
                    counts[(int)MapSpawnPolicy.Tier(table, actor.Definition)]++;
                    actor.AI.RequestAggro(player);
                    ThemeRequire(RuntimeThemeMatches(actor), "Runtime theme binding differs: " + actor.Definition.EnemyId);
                }
                ThemeRequire(field.SpawnedCount == expectedCount && actors.Length == expectedCount
                    && counts[0] == 50 && counts[1] == 16 && counts[2] == 4, "Field tier/count mismatch: " + id);
                yield return new WaitForSeconds(.8f);
                var squad = EnemySquadPursuitRuntimeService.GetRuntimeStats();
                ThemeRequire(squad.RegisteredAgentCount >= 66 && squad.IsActive, "Actual crowd squad did not activate: " + id);
                int livingBefore = field.RemainingCount;
                var victim = actors.First(a => MapSpawnPolicy.Tier(table, a.Definition) == EnemyThemeTier.Elite);
                victim.Health.TakeDamage(new DamageInfo(victim.Health.CurrentHp * 100 + 100000, victim.transform.position,
                    playerActor.gameObject, Vector3.forward, suppressDefaultHitVfx: true));
                ThemeRequire(victim.Health.IsDead && field.RemainingCount == livingBefore - 1, "Real death accounting failed: " + id);
                foreach (var actor in actors) if (actor != null && actor.IsLeased) service.Release(actor);
                yield return null; yield return new WaitForFixedUpdate();
                // Every authored entry also takes two real leases through the shared game catalog.
                var leaseRows = new JArray();
                foreach (var entry in table.Entries)
                {
                    var definition = entry.definition;
                    EnemyActor first = null;
                    uint firstVersion = 0;
                    int createdBefore = service.Pool.CreatedCount;
                    for (int lease = 0; lease < 2; lease++)
                    {
                        ThemeRequire(service.TrySpawn(new EnemySpawnRequest(definition, field.transform.position + Vector3.up * .08f,
                            Quaternion.identity, player, field.gameObject, field.transform, field.transform,
                            context: EncounterContext.Test), out var actor), "Actual shared catalog lease failed: " + definition.EnemyId);
                        if (lease == 0) { first = actor; firstVersion = actor.LeaseVersion; }
                        ThemeRequire(actor.Definition == definition && RuntimeThemeMatches(actor) && actor.transform.localScale == Vector3.one
                            && actor.Health.CurrentHp == actor.Health.MaxHp && (lease == 0 || actor != first || actor.LeaseVersion > firstVersion),
                            "Shared pool lease/binding failed: " + definition.EnemyId);
                        service.Release(actor);
                        yield return null; yield return new WaitForFixedUpdate();
                        ThemeRequire(!actor.IsLeased && !actor.gameObject.activeInHierarchy && !actor.AbilityController.IsExecuting,
                            "Actual shared pool reset failed: " + definition.EnemyId);
                    }
                    ThemeRequire(service.Pool.CreatedCount == createdBefore, "Shared pool allocated instead of reusing: " + definition.EnemyId);
                    leaseRows.Add(new JObject { ["id"] = definition.EnemyId, ["leases"] = 2, ["pass"] = true });
                }
                cases.Add(new JObject { ["id"] = id, ["pass"] = true, ["actualRunTheme"] = world.Theme.ThemeId,
                    ["spawned"] = expectedCount, ["small"] = counts[0], ["medium"] = counts[1], ["elite"] = counts[2],
                    ["actualSquadActive"] = squad.IsActive, ["squadAgents"] = squad.RegisteredAgentCount,
                    ["realPlayerSourceDeath"] = true, ["leases"] = leaseRows });
                WriteResult("RUNNING");
                PersistentSceneFlow.Instance.GetComponent<RunLifetimeDriver>().RequestAbandon(); entered = false;
                deadline = Time.unscaledTime + 120;
                while (PersistentSceneFlow.Instance.IsSwitching || !WorldSessionState.IsHideout)
                { ThemeRequire(Time.unscaledTime < deadline, "Actual hideout return timeout: " + id); yield return null; }
            }
        }
        finally
        {
            if (entered && PersistentSceneFlow.Instance != null && !PersistentSceneFlow.Instance.IsSwitching)
                PersistentSceneFlow.Instance.GetComponent<RunLifetimeDriver>()?.RequestAbandon();
        }
    }
}
