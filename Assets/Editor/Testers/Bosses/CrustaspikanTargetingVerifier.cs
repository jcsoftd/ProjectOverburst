using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

// Real PersistentScene player, portal and boss executors. The baseline records failures before a fix.
[InitializeOnLoad]
public static class CrustaspikanTargetingVerifier
{
    private const string Key = "Overburst.CrustaspikanTargetingVerifier.";
    private static readonly List<object> checks = new List<object>();
    private static readonly List<object> samples = new List<object>();
    private static readonly List<object> cases = new List<object>();
    private static PlayerActorRuntime player;
    private static CrustaspikanEncounter encounter;
    private static EnemyBossMaterialExecutor material;
    private static string output, label;
    private static bool running, baseline, beganAttack, strikeSeen, lockProbeMoved;
    private static bool sawCue, sawCueAudio, sawFirstComboCue;
    private static int stage, caseIndex, failures, damageHits, commits, misalignedCommits, initialCompositeReleases, parriesBefore;
    private static int initialMaterialCompletions, initialCompositeCompletions, expectedMaterialCompletions, expectedCompositeCompletions;
    private static float stageAt, gameAt, caseAt, nextSample, startAngle, startHp, observedCommitAt;
    private static bool parryRequested, firstComboThreat;
    private static Quaternion committedRotation;
    private static Vector3 caseBossPosition;
    private static InputSettings.BackgroundBehavior inputBefore;
    private static bool backgroundBefore;
    private static readonly string[] patterns = {
        "left_light", "right_light", "combo", "left_smash", "right_smash",
        "left_stomp", "right_stomp", "turn_hand_left", "turn_hand_right", "donut",
        "rear_left", "rear_right", "advance_combo", "retreat_counter", "rock_throw", "weak_spit", "strong_spit", "elite_throw"
    };
    private static readonly float[] angles = { 90, 180, -120, -90, 120, -90, 90, 150, -150, 180, 180, 180, 120, -120, 150, -150, 120, -120 };
    private static readonly float[] distances = { 7, 7, 8, 10, 8, 4, 4, 8, 8, 8, 5, 5, 12, 6, 12, 12, 12, 12 };

    static CrustaspikanTargetingVerifier()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += Changed;
    }

    public static string Start(string directory, bool recordBaseline = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Editor must be idle.");
        if (IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
            throw new InvalidOperationException("Another isolated account owns the Editor.");
        output = IsolatedSavePlayGuard.ValidateDirectory(directory); Directory.CreateDirectory(output);
        SessionState.SetString(Key + "output", output);
        SessionState.SetBool(Key + "baseline", recordBaseline);
        SessionState.SetBool(Key + "pending", true);
        SessionState.SetString(Key + "input", InputSystem.settings.backgroundBehavior.ToString());
        SessionState.SetBool(Key + "background", Application.runInBackground);
        WriteFile("before.json", new { project = Application.dataPath, pid = System.Diagnostics.Process.GetCurrentProcess().Id,
            startScene = AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene),
            scenes = Scenes(), input = InputSystem.settings.backgroundBehavior.ToString(), Application.runInBackground });
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "IsolatedSave"));
        return "Targeting verification accepted";
    }

    private static object[] Scenes() => Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
        .Select(i => { var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); return (object)new { s.name, s.path, s.isDirty, roots = s.rootCount }; }).ToArray();

    private static void Changed(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key + "pending", false)
            && !SessionState.GetBool(Key + "reloadLocked", false))
        {
            EditorApplication.LockReloadAssemblies(); SessionState.SetBool(Key + "reloadLocked", true);
        }
        if (state == PlayModeStateChange.ExitingPlayMode) UnlockReload();
        if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key + "pending", false)) return;
        SessionState.SetBool(Key + "pending", false);
        SessionState.SetBool(Key + "returnPending", true);
        SessionState.SetFloat(Key + "deadline", (float)EditorApplication.timeSinceStartup + 120f);
    }

    private static void ReturnAccount()
    {
        if (!SessionState.GetBool(Key + "returnPending", false)) return;
        string path = SessionState.GetString(Key + "output", "");
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "deadline", 0f))
            { SessionState.SetBool(Key + "returnPending", false); File.WriteAllText(Path.Combine(path, "editor-return.json"), "{\"status\":\"DEFERRED_EDITOR_BUSY\"}"); }
            return;
        }
        string owned = Path.GetFullPath(Path.Combine(path, "IsolatedSave"));
        string[] dirs = { Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), IsolatedSavePlayGuard.ActiveDirectory,
            SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "") };
        if (dirs.Any(d => !string.IsNullOrEmpty(d) && !string.Equals(Path.GetFullPath(d), owned, StringComparison.OrdinalIgnoreCase)))
        {
            if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "deadline", 0f))
            { SessionState.SetBool(Key + "returnPending", false); File.WriteAllText(Path.Combine(path, "editor-return.json"), "{\"status\":\"DEFERRED_OTHER_OWNER\"}"); }
            return;
        }
        try
        {
            IsolatedSavePlayGuard.UseRealAccount();
            InputSystem.settings.backgroundBehavior = (InputSettings.BackgroundBehavior)Enum.Parse(typeof(InputSettings.BackgroundBehavior), SessionState.GetString(Key + "input", "ResetAndDisableNonBackgroundDevices"));
            Application.runInBackground = SessionState.GetBool(Key + "background", true);
            SessionState.SetBool(Key + "returnPending", false);
            File.WriteAllText(Path.Combine(path, "editor-return.json"), JsonConvert.SerializeObject(new { status = "RETURNED",
                env = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), active = IsolatedSavePlayGuard.ActiveDirectory,
                blocked = IsolatedSavePlayGuard.RequiresAccountChoice, prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""),
                expires = SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""), pending = false, returnPending = false,
                reloadLocked = SessionState.GetBool(Key + "reloadLocked", false),
                scenes = Scenes(), startScene = AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene),
                input = InputSystem.settings.backgroundBehavior.ToString(), Application.runInBackground }, Formatting.Indented));
        }
        catch (Exception error) { SessionState.SetBool(Key + "returnPending", false); Debug.LogException(error); }
    }

    private static void Update()
    {
        ReturnAccount();
        if (!EditorApplication.isPlaying || !SessionState.GetBool(Key + "pending", false)) return;
        try
        {
            if (!running)
            {
                running = true; output = SessionState.GetString(Key + "output", ""); baseline = SessionState.GetBool(Key + "baseline", false);
                checks.Clear(); samples.Clear(); cases.Clear(); failures = 0; stage = 0; stageAt = Time.realtimeSinceStartup;
                inputBefore = (InputSettings.BackgroundBehavior)Enum.Parse(typeof(InputSettings.BackgroundBehavior), SessionState.GetString(Key + "input", "ResetAndDisableNonBackgroundDevices"));
                backgroundBefore = SessionState.GetBool(Key + "background", true);
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; Application.runInBackground = true;
            }
            if (Time.realtimeSinceStartup - stageAt > 80f) throw new TimeoutException("Targeting stage " + stage);
            Tick();
        }
        catch (Exception error) { Finish("FAIL", error.ToString()); }
    }

    private static void Tick()
    {
        var host = CrustaspikanEncounterHost.Current;
        if (stage == 0)
        {
            if (host == null || host.Entrance == null || !host.CanEnter) return;
            player = PlayerContext.Instance.CurrentActor;
            Teleport(host.Entrance.transform.position + Vector3.back);
            Check(host.Entrance.TryInteract(player) == InteractionExecutionResult.StartedTransition, "actual portal entry");
            encounter = host.ActiveEncounter;
            Check(encounter != null && encounter.Brain != null, "actual player and boss exist");
            Next(); return;
        }
        if (stage == 1)
        {
            if (encounter.IsIntroducing) { encounter.EntranceCinematic.Skip(); return; }
            if (Time.realtimeSinceStartup - stageAt < 1f) return;
            var sword = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            Check(player.Equipment.EquipWeaponItem(new ItemData(sword, 1, ItemGrade.Common)), "isolated player equips actual parry weapon");
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
            ResetBoss(120, 7); label = "idle facing"; gameAt = Time.time;
            Next(); return;
        }
        if (stage == 2)
        {
            Sample(); if (Time.time - gameAt < 2.3f) return;
            Check(Angle() < 12f, "idle tracks side/rear player", new { angle = Angle() });
            caseIndex = 0; PrepareCase(); Next(); return;
        }
        if (stage == 3)
        {
            Sample(); ObserveCue();
            bool executing = encounter.Brain.Actor.AbilityController.IsExecuting;
            strikeSeen |= encounter.Brain.Composite.ReleaseCount > initialCompositeReleases;
            if (executing && !beganAttack)
            {
                beganAttack = true; startAngle = Angle(); committedRotation = encounter.Brain.Actor.transform.rotation;
                if (!patterns[caseIndex].StartsWith("rear_") && patterns[caseIndex] != "donut")
                    Check(startAngle <= 12f, label + " faces player before execution", new { angle = startAngle });
            }
            if (!baseline && label == "combo" && beganAttack && material.NormalizedTime > .08f && !lockProbeMoved)
            {
                Teleport(encounter.Brain.Actor.transform.position + committedRotation * Vector3.left * 8f);
                lockProbeMoved = true;
            }
            if (beganAttack && (!executing && encounter.Brain.CurrentPatternId == "" || Time.time - caseAt > 18f))
            {
                if (label == "combo" && lockProbeMoved)
                    Check(Quaternion.Angle(committedRotation, encounter.Brain.Actor.transform.rotation) < 2f, "committed melee direction permits dodge");
                else if (caseIndex < 14) Check(damageHits > 0, label + " deals actual player damage", new { damageHits, hpLost = startHp - player.Health.CurrentHp });
                Check(strikeSeen, label + " reaches actual strike event");
                bool timedOut = Time.time - caseAt > 18f;
                Check(!timedOut && ExecutionCompleted(initialMaterialCompletions, material.CompletedCount, expectedMaterialCompletions, material.LastFailure),
                    label + " executor completes", new { before = initialMaterialCompletions, after = material.CompletedCount, expected = expectedMaterialCompletions, timedOut, material.LastFailure });
                Check(!timedOut && ExecutionCompleted(initialCompositeCompletions, encounter.Brain.Composite.CompletedCount, expectedCompositeCompletions, encounter.Brain.Composite.LastFailure),
                    label + " composite executor completes", new { before = initialCompositeCompletions, after = encounter.Brain.Composite.CompletedCount, expected = expectedCompositeCompletions, timedOut, encounter.Brain.Composite.LastFailure });
                if (!baseline && (caseIndex < 5 && label != "combo" || label.StartsWith("turn_hand")))
                {
                    Check(sawCue, label + " actual parry flash particle");
                    Check(sawCueAudio, label + " actual parry warning sound voice");
                }
                if (!baseline && label == "combo") Check(!sawFirstComboCue, "combo first hit gives no parry cue");
                cases.Add(new { label, startAngle, damageHits, strikeSeen, lockProbeMoved, sawCue, sawCueAudio, sawFirstComboCue,
                    materialCompletions = material.CompletedCount - initialMaterialCompletions, expectedMaterialCompletions,
                    compositeCompletions = encounter.Brain.Composite.CompletedCount - initialCompositeCompletions, expectedCompositeCompletions, timedOut });
                caseIndex++;
                int length = baseline ? 5 : patterns.Length;
                if (caseIndex < length) { PrepareCase(); stageAt = Time.realtimeSinceStartup; Write("RUNNING", ""); return; }
                if (baseline) { Finish(failures > 0 ? "FAIL_REPRODUCED" : "PASS", ""); return; }
                ResetBoss(0, 8); encounter.Brain.ReviewMode = false; label = "automatic phase one moving player";
                commits = misalignedCommits = 0; observedCommitAt = 0f; gameAt = Time.time; Next(); return;
            }
            if (Time.time - caseAt > 22f) throw new TimeoutException("Pattern did not complete: " + label);
            return;
        }
        if (stage == 4 || stage == 6)
        {
            MoveAroundBoss(); Sample(); ObserveCommit();
            if (Time.time - gameAt < 24f) return;
            Check(commits >= 2, label + " automatically chooses attacks", new { commits });
            Check(misalignedCommits == 0, label + " aimed commits", new { commits, misalignedCommits });
            Check(encounter.Brain.Actor.AI.Target == player.transform, label + " retains actual player target");
            if (stage == 4)
            {
                encounter.Brain.Actor.Health.TakeDamage(new DamageInfo(1700f, encounter.Brain.Actor.transform.position, player.gameObject));
                gameAt = Time.time; Next(); return;
            }
            ResetBoss(0, 8); encounter.Brain.ReviewMode = false;
            SetField("cooldowns", new Dictionary<string, float>(encounter.Settings.patterns.ToDictionary(p => p.id, p => Time.time + 20f)));
            var boss = encounter.Brain.Actor; var body = boss.GetComponent<Rigidbody>();
            caseBossPosition = encounter.ArenaCenter + new Vector3(0, .05f, -12);
            body.position = caseBossPosition; boss.transform.position = caseBossPosition;
            Teleport(encounter.ArenaCenter + new Vector3(0, .08f, 16));
            startAngle = Vector3.Distance(boss.transform.position, player.transform.position); gameAt = Time.time;
            label = "cooldown chase"; Next(); return;
        }
        if (stage == 5)
        {
            Sample(); if (encounter.Brain.Phase != 2 || encounter.Brain.IsTransitioning) return;
            Check(encounter.Brain.Phase == 2, "safe phase transition retains target");
            label = "automatic phase two moving player"; gameAt = Time.time; commits = misalignedCommits = 0; observedCommitAt = 0f; Next(); return;
        }
        if (stage == 7)
        {
            Sample(); if (Time.time - gameAt < 5f) return;
            float distance = Vector3.Distance(encounter.Brain.Actor.transform.position, player.transform.position);
            Check(distance < startAngle - 3f, "no available attack pursues distant player", new { before = startAngle, after = distance });
            Check(Angle() < 12f, "chase faces actual player");
            encounter.Restart(); Check(encounter.Brain.Actor.AI.Target == player.transform, "restart reacquires actual target");
            encounter.Exit(true); encounter = null; gameAt = Time.time; Next(); return;
        }
        if (stage == 8)
        {
            if (Time.time - gameAt < .7f) return;
            Teleport(host.Entrance.transform.position + Vector3.back);
            Check(host.Entrance.TryInteract(player) == InteractionExecutionResult.StartedTransition, "repeat portal entry");
            encounter = host.ActiveEncounter; encounter.EntranceCinematic?.Skip(); gameAt = Time.time; Next(); return;
        }
        if (stage == 9)
        {
            if (encounter.IsIntroducing || Time.time - gameAt < 1f) return;
            Check(encounter.Brain.Actor.AI.Target == player.transform, "repeat encounter uses current player");
            Check(encounter.BossHud.BoundEncounterSource == encounter.Brain, "existing HUD binds restarted brain");
            encounter.Brain.ReviewMode = true; material = encounter.Brain.Actor.GetComponent<EnemyBossMaterialExecutor>();
            Teleport(encounter.Brain.Actor.transform.position + encounter.Brain.Actor.transform.forward * 9f);
            parriesBefore = player.GetComponent<PlayerParryController>().ParriedAttackCount;
            parryRequested = firstComboThreat = false;
            Check(encounter.Brain.StartPatternForReview("combo"), "actual final-hit parry regression starts");
            gameAt = Time.time; Next(); return;
        }
        if (stage == 10)
        {
            Sample(); var target = player.GetComponent<CombatTarget>();
            if (material.NormalizedTime > .2f && material.NormalizedTime < .34f)
                firstComboThreat |= material.IsParryThreatTo(target);
            if (!parryRequested && material.NormalizedTime > .35f && material.IsParryThreatTo(target))
            {
                var result = player.GetComponent<MeleeRuntime>().TryStartHeavyAttack((encounter.Brain.Actor.transform.position - player.transform.position).normalized);
                Check(result == WeaponActionResult.Accepted, "actual heavy input starts in final parry window", result.ToString());
                parryRequested = true;
            }
            if (player.GetComponent<PlayerParryController>().ParriedAttackCount <= parriesBefore)
            { if (Time.time - gameAt > 12f) throw new TimeoutException("Actual final-hit parry did not happen"); return; }
            Check(!firstComboThreat, "first combo strike is never parry threat");
            Check(parryRequested, "actual player parries final combo strike");
            Check(!encounter.Brain.Actor.AbilityController.IsExecuting, "parry cancels committed attack");
            Check(encounter.Brain.Target == player.transform, "parry retains encounter target");
            gameAt = Time.time; Next(); return;
        }
        if (stage == 11)
        {
            if (encounter.Brain.Actor.GetComponent<CrustaspikanTemporaryReaction>()?.BlocksActions == true || Time.time - gameAt < 2f) return;
            var decoy = new GameObject("Targeting verifier decoy");
            try
            {
                encounter.Brain.Actor.AI.SetTarget(decoy.transform);
                encounter.Brain.Tick();
                Check(encounter.Brain.Actor.AI.Target == player.transform, "stale AI target repaired to actual player");
                player.enabled = false; encounter.Brain.Tick();
                Check(encounter.Brain.Target == null && !encounter.Brain.Actor.AbilityController.IsExecuting, "invalid player suspends attacks");
            }
            finally { player.enabled = true; UnityEngine.Object.Destroy(decoy); }
            encounter.Brain.Tick(); Check(encounter.Brain.Target == player.transform, "valid player resumes target after temporary loss");
            Finish(failures == 0 ? "PASS" : "FAIL", "");
        }
    }

    private static void ResetBoss(float angle, float distance)
    {
        Unsubscribe(); encounter.Restart(); encounter.Brain.ReviewMode = true;
        var boss = encounter.Brain.Actor; var body = boss.GetComponent<Rigidbody>();
        body.position = encounter.ArenaCenter + Vector3.up * .05f; body.rotation = Quaternion.identity;
        boss.transform.SetPositionAndRotation(body.position, Quaternion.identity); Physics.SyncTransforms();
        Teleport(boss.transform.position + Quaternion.Euler(0, angle, 0) * Vector3.forward * distance);
        material = boss.GetComponent<EnemyBossMaterialExecutor>();
        material.StrikeReleased += Released; player.Health.OnDamageResolved += Damaged;
    }

    private static void PrepareCase()
    {
        ResetBoss(angles[caseIndex], distances[caseIndex]); label = patterns[caseIndex];
        beganAttack = strikeSeen = lockProbeMoved = false; damageHits = 0; startAngle = -1; startHp = player.Health.CurrentHp;
        sawCue = sawCueAudio = sawFirstComboCue = false;
        initialMaterialCompletions = material.CompletedCount;
        initialCompositeCompletions = encounter.Brain.Composite.CompletedCount;
        expectedMaterialCompletions = expectedCompositeCompletions = 0;
        var pattern = encounter.Settings.patterns.Single(p => p.id == label);
        foreach (var step in pattern.steps)
        {
            if (step.kind != CrustaspikanStepKind.Attack && step.kind != CrustaspikanStepKind.ThrowElite) continue;
            string clip = step.kind == CrustaspikanStepKind.ThrowElite ? "ThrowRock" : step.materialOrMotion;
            var attack = encounter.Brain.RuntimeMaterials.attacks.Single(a => a.runtimeClip.name == clip);
            if (encounter.Brain.Composite.Supports(attack.ability)) expectedCompositeCompletions++;
            else if (material.Supports(attack.ability)) expectedMaterialCompletions++;
            else throw new InvalidOperationException("No executor supports " + clip);
        }
        Check(expectedMaterialCompletions + expectedCompositeCompletions > 0, label + " has expected attack executions");
        caseAt = Time.time; Check(encounter.Brain.StartPatternForReview(label), label + " starts assembly");
        initialCompositeReleases = encounter.Brain.Composite.ReleaseCount;
    }
    private static void Released(EnemyBossAttackMaterial attack, int strike) { strikeSeen = true; }
    internal static bool ExecutionCompleted(int before, int after, int expected, string error)
        => expected >= 0 && after - before == expected && string.IsNullOrEmpty(error);
    private static void Damaged(CombatHealth health, DamageInfo info, float amount, bool fatal)
    { if (amount > 0f && encounter?.Brain != null && info.source == encounter.Brain.Actor.gameObject) damageHits++; }
    private static void Unsubscribe()
    { if (material != null) material.StrikeReleased -= Released; if (player != null) player.Health.OnDamageResolved -= Damaged; material = null; }
    private static void ObserveCommit()
    {
        var controller = encounter.Brain.Actor.AbilityController;
        float at = (float)typeof(EnemyAbilityController).GetField("lastCommittedAt", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller);
        var attack = encounter.Brain.Composite.CurrentMaterial ?? material.CurrentMaterial;
        if (at > observedCommitAt && attack != null)
        {
            commits++; float angle = Angle();
            if (encounter.Brain.CurrentPatternId != "rear_left" && encounter.Brain.CurrentPatternId != "rear_right"
                && attack.delivery == EnemyBossMaterialDelivery.Melee && angle > 15f) misalignedCommits++;
            observedCommitAt = at;
        }
    }
    private static void ObserveCue()
    {
        if (material == null || !material.IsExecuting) return;
        bool cue = encounter.Brain.Actor.GetComponents<EnemyStrongAttackWarning>()
            .Any(w => w.FinalSignal && w.GetComponentsInChildren<ParticleSystem>().Any(p => p.gameObject.name == "Parry cue glint" && p.particleCount > 0));
        if (!cue) return;
        sawCue = true;
        if (label == "combo" && material.NormalizedTime < .34f) sawFirstComboCue = true;
        var clip = CombatActionSfxService.ResolveNamedClip("ParryWindowPing_MetallicRingLong");
        sawCueAudio |= UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Any(s => s.clip == clip && s.isPlaying && s.volume > 0f);
    }
    private static void MoveAroundBoss()
    {
        var boss = encounter.Brain.Actor;
        Vector3 delta = player.transform.position - boss.transform.position; delta.y = 0f;
        Vector3 desired = boss.transform.position + Quaternion.Euler(0, 35f * Time.deltaTime, 0) * delta.normalized * 8f;
        player.CharacterController.Move(Vector3.ClampMagnitude(desired - player.transform.position, 4.5f * Time.deltaTime));
    }
    private static float Angle()
    { Vector3 d = player.transform.position - encounter.Brain.Actor.transform.position; d.y = 0; return Vector3.Angle(encounter.Brain.Actor.transform.forward, d); }
    private static void Teleport(Vector3 point)
    { bool enabled = player.CharacterController.enabled; player.CharacterController.enabled = false; player.transform.position = point; player.CharacterController.enabled = enabled; player.Movement.ResetMotionAfterTeleport(); Physics.SyncTransforms(); }
    private static void SetField(string name, object value) => typeof(CrustaspikanEncounterBrain).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(encounter.Brain, value);
    private static void Sample()
    {
        if (Time.time < nextSample) return; nextSample = Time.time + .15f;
        var brain = encounter.Brain; var p = player.transform.position; var b = brain.Actor.transform.position;
        samples.Add(new { t = Time.time, label, state = brain.State, pattern = brain.CurrentPatternId, brain.Phase,
            player = new[] { p.x, p.y, p.z }, boss = new[] { b.x, b.y, b.z }, angle = Angle(), executing = material?.IsExecuting, progress = material?.NormalizedTime });
    }
    private static void Check(bool pass, string name, object evidence = null)
    { checks.Add(new { pass, name, evidence }); if (!pass) failures++; }
    private static void Next() { stage++; stageAt = Time.realtimeSinceStartup; Write("RUNNING", ""); }
    private static void Write(string status, string error)
    { WriteFile("result.json", new { status, baseline, stage, failures, checks, cases, error, utc = DateTime.UtcNow }); }
    private static void WriteFile(string file, object value) => File.WriteAllText(Path.Combine(output, file), JsonConvert.SerializeObject(value, Formatting.Indented));
    private static void Finish(string status, string error)
    {
        running = false;
        try
        {
            Write(status, error); WriteFile("samples.json", samples); Unsubscribe();
            encounter?.Exit(true); encounter = null;
            InputSystem.settings.backgroundBehavior = inputBefore; Application.runInBackground = backgroundBefore;
        }
        finally { UnlockReload(); EditorApplication.ExitPlaymode(); }
    }
    private static void UnlockReload()
    {
        if (!SessionState.GetBool(Key + "reloadLocked", false)) return;
        SessionState.SetBool(Key + "reloadLocked", false); EditorApplication.UnlockReloadAssemblies();
    }
}
