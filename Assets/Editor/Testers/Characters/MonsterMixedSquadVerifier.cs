using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// One short, targeted run: real ranged update, wall, retreat, 40/41/50 assignment and lease cleanup.
[InitializeOnLoad]
public static class MonsterMixedSquadVerifier
{
    private const string Key = "MonsterMixedSquadVerifier";
    private static readonly List<EnemyActor> actors = new List<EnemyActor>();
    private static readonly List<string> passed = new List<string>(), errors = new List<string>();
    private static IEnumerator routine;
    private static GameObject encounter, wall;
    private static PlayerInputFacade player;
    private static EnemySpawnService spawnService;
    private static bool background;
    private static int frame = -1, frameRate;
    private static double deadline;
    public static string LastResult => SessionState.GetString(Key + ".result", "NOT_RUN");
    static MonsterMixedSquadVerifier() { EditorApplication.playModeStateChanged += Changed; }
    [MenuItem("OVERBURST/Enemies/Themes/Validate Mixed Squad Essential")]
    public static void Run()
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Already playing");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        Check(scene.name == PersistentSceneFlow.PersistentSceneName && !scene.isDirty, "Requires saved PersistentScene");
        SessionState.SetBool(Key, true); SessionState.SetString(Key + ".result", "RUNNING");
        EditorApplication.EnterPlaymode();
    }
    private static void Changed(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            background = Application.runInBackground; frameRate = Application.targetFrameRate;
            Application.runInBackground = true; Application.targetFrameRate = 60;
            passed.Clear(); errors.Clear(); actors.Clear(); frame = -1;
            deadline = EditorApplication.timeSinceStartup + 100; routine = Verify();
            Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick;
            (routine as IDisposable)?.Dispose(); routine = null;
            Clear(); if (wall != null) UnityEngine.Object.DestroyImmediate(wall);
            if (encounter != null) UnityEngine.Object.DestroyImmediate(encounter);
            Application.logMessageReceived -= Log;
            Application.runInBackground = background; Application.targetFrameRate = frameRate;
            if (LastResult == "RUNNING") Save("FAIL interrupted");
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        { SessionState.SetBool(Key, false); Debug.Log("[MixedSquad] " + LastResult); }
    }
    private static void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    private static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return; frame = Time.frameCount;
        try
        {
            Check(EditorApplication.timeSinceStartup < deadline, "Timeout");
            if (routine.MoveNext()) return;
            Check(errors.Count == 0, string.Join(";", errors)); Finish("PASS");
        }
        catch (Exception exception) { Finish("FAIL " + exception); }
    }
    private static void Save(string result)
    {
        SessionState.SetString(Key + ".result", result + "; " + string.Join("; ", passed));
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출/MonsterAI/20260922_MixedSquadImplementation"));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "essential-play.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new { result, passed, errors }, Newtonsoft.Json.Formatting.Indented));
    }
    private static void Finish(string result) { Save(result); EditorApplication.update -= Tick; EditorApplication.ExitPlaymode(); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static EnemyActor Spawn(EnemyDefinition definition, Vector3 position)
    {
        var request = new EnemySpawnRequest(definition, position, Quaternion.LookRotation(player.transform.position - position), player.transform,
            encounter, player.transform, null, 1, 1, 731);
        Check(spawnService.TrySpawn(request, out var actor), "Spawn " + definition.EnemyId);
        actors.Add(actor); actor.AI.RequestAggro(player.transform); return actor;
    }
    private static void Clear() { foreach (var actor in actors) if (actor != null && actor.IsLeased) actor.RequestPoolRelease(); actors.Clear(); }
    private static void TeleportPlayer(Vector3 position)
    {
        var controller = player.GetComponent<CharacterController>(); bool enabled = controller.enabled;
        controller.enabled = false; player.transform.position = position; controller.enabled = enabled;
        player.GetComponent<PlayerMovement>().ResetMotionAfterTeleport(); Physics.SyncTransforms();
    }
    private static IEnumerator Verify()
    {
        float wait = Time.time + 40;
        while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
            || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
        { Check(Time.time < wait, "Hideout load"); yield return null; }
        player = PlayerInputFacade.Current; Check(player != null, "Player");
        player.GetComponent<CombatHealth>().SetMaxHp(100000, true);
        var ui = UnityEngine.Object.FindFirstObjectByType<EnemyThemeDebugUI>(FindObjectsInactive.Include);
        Check(ui != null, "Theme UI"); ui.gameObject.SetActive(true); ui.ToggleArena(); Check(ui.InArena, "Arena");
        Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(ui.transform, out spawnService), "Spawn service setup");
        foreach (var table in ui.tables) Check(spawnService.RegisterAdditionalCatalog(table.Catalog, out _), "Theme catalog registration");
        var definitions = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).Distinct().Where(d => MonsterMixedSquadBuilder.IsOriginalTheme(d.EnemyId)).ToArray();
        Check(definitions.Length == 14 && definitions.All(d => d.TacticalProfile != null), "14 profile mappings");
        passed.Add("14 profiles mapped");
        var ranged = definitions.First(d => d.EnemyId == "VenomBrood_Venodonte_Tint3");
        var melee = definitions.First(d => d.EnemyId == "VenomBrood_Venodonte_Tint1");
        var elite = definitions.First(d => d.EnemyId == "VenomBrood_Kupolobrach_Tint_Orange");
        encounter = new GameObject("Mixed squad essential probe");
        Vector3 center = player.transform.position;
        var shooter = Spawn(ranged, center + Vector3.forward * 5.25f);
        var special = shooter.GetComponent<EnemyThemeSpecialExecutor>();
        float end = Time.time + 10;
        while (special.LaunchCount == 0) { Check(Time.time < end, "Ranged did not fire; " + shooter.AI.TacticalReason); yield return null; }
        float cooldownEnd = Time.time + 1.4f;
        while (Time.time < cooldownEnd)
        { Check(shooter.AI.TargetDistance > 3f, "Ranged rushed melee while cooling down"); yield return null; }
        Check(special.ImpactCount > 0, "Projectile cancelled before reaching stationary target");
        bool aim = shooter.AbilityController.HasPreparedAim(player.transform);
        int committed = shooter.AbilityController.LastCommittedAbilityIndex;
        Check(shooter.AbilityController.TryGetRangedPositioningAbility(out var ability), "Ranged spatial ability");
        Check(aim == shooter.AbilityController.HasPreparedAim(player.transform) && committed == shooter.AbilityController.LastCommittedAbilityIndex, "Geometry query mutated aim/selection");
        passed.Add("real shot hits at 5.25m + cooldown holds range + read-only geometry");
        shooter.AbilityController.Cancel();
        wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Mixed squad temporary wall";
        wall.transform.position = (player.transform.position + shooter.transform.position) * .5f + Vector3.up;
        wall.transform.localScale = new Vector3(.8f, 2f, .35f); Physics.SyncTransforms();
        Check(!shooter.AbilityController.HasRangedPositioningLine(ability, player.transform, shooter.transform.position), "Wall not detected");
        Vector3 beforeWall = shooter.transform.position; end = Time.time + 5f; bool relocated = false;
        while (Time.time < end)
        {
            if (Vector3.Distance(beforeWall, shooter.transform.position) > .3f) relocated = true;
            if (relocated && shooter.AbilityController.HasRangedPositioningLine(ability, player.transform, shooter.transform.position)) break;
            yield return null;
        }
        Check(relocated && shooter.AbilityController.HasRangedPositioningLine(ability, player.transform, shooter.transform.position), "Wall reposition failed: " + shooter.AI.TacticalReason);
        UnityEngine.Object.DestroyImmediate(wall); wall = null;
        passed.Add("wall detected + actual lateral reposition clears shot");
        shooter.AbilityController.Cancel();
        Vector3 retreatStart = shooter.transform.position;
        TeleportPlayer(retreatStart + Vector3.back * 2.5f);
        end = Time.time + 3f;
        while (Time.time < end) yield return null;
        Check(shooter.AI.TacticalRetreatUsed, "No close retreat");
        Check(Vector3.Distance(retreatStart, shooter.transform.position) < 1.6f, "Unlimited retreat");
        passed.Add("one short retreat under continuous pressure");
        uint lease = shooter.LeaseVersion, generation = shooter.AI.TacticalContextGeneration;
        Clear(); yield return null;
        Check(EnemyEncounterTacticsContext.ContextCount == 0, "Context leaked after pool return");
        TeleportPlayer(center);
        var reused = Spawn(ranged, center + Vector3.forward * 4.5f);
        Check(!reused.AI.TacticalRetreatUsed && reused.AI.TacticalContextGeneration != generation, "Stale retreat/context on lease");
        if (reused == shooter) Check(reused.LeaseVersion != lease, "Lease not advanced");
        Clear(); passed.Add("pool reset + new context generation");
        for (int i = 0; i < 40; i++)
        {
            Vector3 position = center + Quaternion.Euler(0, i * 9f, 0) * Vector3.forward * (14f + i % 2 * 2f);
            Spawn(i < 32 ? melee : ranged, position);
        }
        yield return null; yield return null;
        Check(!EnemySquadPursuitRuntimeService.GetRuntimeStats().IsActive, "40 activated early");
        Spawn(ranged, center + Vector3.back * 18);
        yield return null; yield return null;
        var stats = EnemySquadPursuitRuntimeService.GetRuntimeStats();
        Check(stats.IsActive && stats.CombatEligibleAgentCount == 41 && stats.SquadCount == 4, "41 activation/32 melee assignment: " + stats.CombatEligibleAgentCount + "/" + stats.SquadCount);
        Check(actors.Where(a => a.AI.UsesRangedTactics).All(a => string.IsNullOrEmpty(a.AI.SquadPursuitDebugModeName)), "Ranged entered melee squad");
        for (int i = 0; i < 8; i++) Spawn(ranged, center + Quaternion.Euler(0, i * 45, 0) * Vector3.forward * 20);
        Spawn(elite, center + Vector3.right * 18);
        yield return null; yield return null;
        stats = EnemySquadPursuitRuntimeService.GetRuntimeStats();
        Check(stats.CombatEligibleAgentCount == 49 && stats.SquadCount == 4, "50 composition count");
        for (int i = 20; i < actors.Count; i++) actors[i].RequestPoolRelease();
        yield return null;
        Check(EnemySquadPursuitRuntimeService.GetRuntimeStats().IsActive, "Activated encounter lost latch below 41");
        Clear(); yield return null;
        Check(EnemyEncounterTacticsContext.ContextCount == 0, "Final context cleanup");
        passed.Add("40 inactive / 41 active / 50=32 melee+17 ranged+1 elite / latch / cleanup");
        ui.ToggleArena();
    }
}
