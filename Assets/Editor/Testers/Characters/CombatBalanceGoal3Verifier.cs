using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static partial class CombatBalanceGoal3Verifier
{
    const string Key = "CombatBalanceGoal3";
    static IEnumerator work;
    static int frame;
    static double deadline;
    static readonly List<object> results = new List<object>();
    static readonly List<string> errors = new List<string>();
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static CombatBalanceGoal3Verifier() { EditorApplication.playModeStateChanged += State; }
    public static void Run(string output, int goal = 3)
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Already playing");
        Check(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "PersistentScene", "Persistent scene required");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output);
        SessionState.SetInt(Key + ".goal", goal);
        SessionState.EraseString(Key + ".env");
        IsolatedSavePlayGuard.PrepareIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
        SessionState.SetBool(Key, true);
        SessionState.SetString(Key + ".status", "RUNNING");
        EditorApplication.EnterPlaymode(); // Unity restores the existing unsaved scene on exit.
    }
    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            SessionState.SetBool(Key + ".background", Application.runInBackground);
            SessionState.SetInt(Key + ".fps", Application.targetFrameRate);
            Application.runInBackground = true; Application.targetFrameRate = 60;
            results.Clear(); errors.Clear(); frame = -1; deadline = EditorApplication.timeSinceStartup + 180;
            int goal = SessionState.GetInt(Key + ".goal", 3);
            work = goal == 12 ? VerifyParryCadence() : goal == 11 ? VerifyDungeonDensity() : goal == 10 ? VerifySpawnBaseline() : goal == 9 ? VerifyReentry() : goal == 7 ? VerifyGoal7() : goal == 6 ? VerifyGoal6() : goal == 5 ? VerifyGoal5() : goal == 4 ? VerifyGoal4() : Verify(); Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            (work as IDisposable)?.Dispose(); work = null;
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
            Application.targetFrameRate = SessionState.GetInt(Key + ".fps", -1);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", null); SessionState.EraseString(Key + ".env");
            SessionState.SetBool(Key, false);
        }
    }
    static void Log(string m, string s, LogType t) { if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert) errors.Add(m); }
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try { Check(EditorApplication.timeSinceStartup < deadline, "Timeout"); if (work.MoveNext()) return; Check(errors.Count == 0, string.Join(" | ", errors)); Finish("PASS"); }
        catch (Exception e) { Finish("FAIL " + e); }
    }
    static void Finish(string status)
    {
        SessionState.SetString(Key + ".status", status);
        File.WriteAllText(Path.Combine(Output, "play-results.json"), JsonConvert.SerializeObject(new { status, results, errors }, Formatting.Indented));
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }
    static IEnumerator Verify()
    {
        EnemyActor current = null; EnemySpawnService spawn = null; EnemyThemeTrialHarness ui = null;
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            Check(Overburst.Persistence.AccountBootstrap.SaveDirectory.StartsWith(Output, StringComparison.OrdinalIgnoreCase), "Account is not isolated");
            var player = PlayerInputFacade.Current;
            ui = EnemyThemeTrialHarness.Current;
            Check(ui != null && player != null, "Missing arena/player");
            if (!ui.InArena) ui.ToggleArena(); Check(ui.InArena, "Arena entry");
            Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn), "Spawn service");
            foreach (var t in ui.tables) Check(spawn.RegisterAdditionalCatalog(t.Catalog, out string error), error);
            var definitions = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).Where(d => d != null).Distinct().ToArray();
            Check(definitions.Length == 29, "Expected 29 definitions: " + definitions.Length);
            foreach (var d in definitions)
            {
                var p = player.transform.position + Vector3.forward * 4;
                Check(Physics.Raycast(p + Vector3.up * 4, Vector3.down, out var floor, 9, LayerMask.GetMask("Default", "Environment", "Ground")), "Floor");
                var request = new EnemySpawnRequest(d, floor.point + Vector3.up * .035f, Quaternion.LookRotation(Vector3.back), player.transform, context: EncounterContext.Test);
                Check(spawn.TrySpawn(request, out current), d.name + " spawn");
                current.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                current.AI.enabled = false; current.Movement.StopMovement();
                var policy = current.GetComponent<EnemyHitResponseCoordinator>();
                var reaction = current.GetComponent<EnemyMovementReaction>();
                Check(policy.LastOutcome == EnemyHitResponseOutcome.None && !reaction.IsStunned, d.name + " pool reset");
                Damage(current, player, 101, 0);
                Check(policy.FlinchCount == 1 && reaction.IsStunned, d.name + " first ordinary hit");
                float at = Time.time;
                Damage(current, player, 102, 0); Check(policy.FlinchCount == 1, d.name + " cooldown");
                while (Time.time < at + .21f) yield return null;
                Damage(current, player, 101, 0); Check(policy.FlinchCount == 1, d.name + " duplicate phase");
                Damage(current, player, 101, 1); Check(policy.FlinchCount == 2, d.name + " second phase");
                Check(Mathf.Abs(current.Animator.speed - 1f) < .001f, d.name + " global animator speed leaked");
                at = Time.time; while (Time.time < at + .21f) yield return null;
                current.Health.TakeDamage(new DamageInfo(1, current.transform.position, isDamageOverTime: true));
                Check(policy.FlinchCount == 2, d.name + " DOT flinch");
                // Observe real death state entry rather than assuming the trigger changed pose immediately.
                int deaths = 0; current.Health.OnDead += (h, hit) => deaths++;
                current.Health.TakeDamage(new DamageInfo(current.Health.MaxHp * 2, current.transform.position));
                current.Health.TakeDamage(new DamageInfo(current.Health.MaxHp * 2, current.transform.position));
                Check(current.Health.IsDead && deaths == 1, d.name + " death once");
                at = Time.time; float entry = -1;
                while (Time.time < at + .20f)
                {
                    var a = current.Animator;
                    if (a.GetCurrentAnimatorStateInfo(0).IsName("Death") || a.IsInTransition(0) && a.GetNextAnimatorStateInfo(0).IsName("Death")) { entry = Time.time - at; break; }
                    yield return null;
                }
                Check(entry >= 0 && entry <= .12f, d.name + " death entry: " + entry);
                results.Add(new { d.EnemyId, firstHitFlinch = true, phaseDedup = true, dotIgnored = true, deathCount = deaths, deathEntry = entry, animatorSpeed = current.Animator.speed });
                spawn.Release(current); current = null; yield return null;
                Check(spawn.TrySpawn(request, out current), d.name + " respawn");
                Check(!current.Health.IsDead && current.GetComponent<EnemyHitResponseCoordinator>().FlinchCount == 0 && !current.GetComponent<EnemyMovementReaction>().IsStunned, d.name + " reuse after death");
                spawn.Release(current); current = null; yield return null;
            }
        }
        finally { if (current != null && current.IsLeased && spawn != null) spawn.Release(current); if (ui != null && ui.InArena) ui.ToggleArena(); }
    }
    static void Damage(EnemyActor a, PlayerInputFacade p, int sequence, int phase)
    {
        a.Health.TakeDamage(new DamageInfo(1, a.transform.position, p.gameObject, Vector3.forward, 4,
            sourceAttackSequenceId: sequence, playerAttackKind: PlayerAttackKind.Weak, sourceAttackPhaseIndex: phase, weakKnockbackDistance: .1f));
    }
    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
