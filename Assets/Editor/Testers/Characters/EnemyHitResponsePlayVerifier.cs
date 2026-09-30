using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// Local-only, bounded Play test of the authored theme prefabs and real pool lifecycle.
[InitializeOnLoad]
public static class EnemyHitResponsePlayVerifier
{
    private const string Key = "EnemyHitResponsePlayVerifier";
    private static IEnumerator work;
    private static int lastFrame;
    private static double deadline;
    private static bool stopping;
    private static bool oldBackground;
    private static int oldFrameRate;
    private static readonly List<string> errors = new List<string>();
    private static readonly List<object> results = new List<object>();
    public static string LastResult => SessionState.GetString(Key + ".result", "NOT_RUN");

    static EnemyHitResponsePlayVerifier() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

    [MenuItem("OVERBURST/Enemies/Poise/Validate Play Mode")]
    public static void Run()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Already playing");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        Require(scene.name == PersistentSceneFlow.PersistentSceneName && !scene.isDirty,
            "Open the saved PersistentScene first");
        SessionState.SetBool(Key, true);
        SessionState.SetString(Key + ".result", "RUNNING");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            oldBackground = Application.runInBackground;
            oldFrameRate = Application.targetFrameRate;
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            stopping = false;
            lastFrame = -1;
            deadline = EditorApplication.timeSinceStartup + 300;
            errors.Clear();
            results.Clear();
            work = Verify();
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            stopping = true;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            (work as IDisposable)?.Dispose();
            work = null;
            Application.runInBackground = oldBackground;
            Application.targetFrameRate = oldFrameRate;
            if (LastResult == "RUNNING") SessionState.SetString(Key + ".result", "FAIL interrupted");
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key, false);
            Debug.Log("[EnemyHitResponse] " + LastResult);
        }
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(message);
    }

    private static void Tick()
    {
        if (stopping) return;
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try
        {
            Require(EditorApplication.timeSinceStartup < deadline, "Timeout");
            if (work.MoveNext()) return;
            Require(errors.Count == 0, string.Join(" | ", errors));
            Finish("PASS 21 themed prefabs, poise decisions, pool reset and attack retention; errors=0");
        }
        catch (Exception exception)
        {
            Finish("FAIL " + exception);
        }
    }

    private static void Finish(string result)
    {
        stopping = true;
        SessionState.SetString(Key + ".result", result);
        WriteResult(result);
        EditorApplication.update -= Tick;
        EditorApplication.ExitPlaymode();
    }

    private static void WriteResult(string status)
    {
        string workspace = Directory.GetParent(Application.dataPath).Parent.FullName;
        string folder = Path.Combine(workspace, "개인파일", "코덱스산출", "MonsterDesign", "20260923_PoiseGoal");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "play-results.json"),
            JsonConvert.SerializeObject(new { status, results, errors }, Formatting.Indented));
    }

    private static IEnumerator Verify()
    {
        EnemyActor current = null;
        EnemyThemeTrialHarness ui = null;
        EnemySpawnService spawn = null;
        MeleeRuntime playerMelee = null;
        try
        {
            float sceneDeadline = Time.realtimeSinceStartup + 45f;
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
                || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
            {
                Require(Time.realtimeSinceStartup < sceneDeadline, "Hideout did not load");
                yield return null;
            }
            PlayerInputFacade player = PlayerInputFacade.Current;
            Require(player != null, "Player missing");
            ui = EnemyThemeTrialHarness.Current;
            Require(ui != null, "Theme debug UI missing");
            ui.ToggleArena();
            Require(ui.InArena, "Arena entry failed");
            Require(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn), "Spawn service missing");
            foreach (var table in ui.tables)
                Require(spawn.RegisterAdditionalCatalog(table.Catalog, out string error), "Catalog: " + error);

            EnemyDefinition[] definitions = ui.tables.SelectMany(t => t.Entries)
                .Select(e => e.definition).Where(d => d != null).Distinct().ToArray();
            Require(definitions.Length == 21, "Expected 21 definitions, got " + definitions.Length);

            foreach (EnemyDefinition definition in definitions)
            {
                current = Spawn(spawn, definition, player, 4f);
                var coordinator = current.GetComponent<EnemyHitResponseCoordinator>();
                var reaction = current.GetComponent<EnemyMovementReaction>();
                Require(coordinator != null && reaction != null && reaction.HitWeightProfile != null,
                    "Missing poise contract: " + definition.EnemyId);
                current.AI.enabled = false;
                current.Movement.StopMovement();
                float hp = current.Health.CurrentHp;
                Damage(current, player, 1, false, 101);
                Require(current.Health.CurrentHp < hp, "Damage did not reach HP: " + definition.EnemyId);
                bool elite = definition.Grade.GradeType == EnemyGradeType.Elite;
                EnemyHitWeight weight = reaction.HitWeightProfile.Weight;
                if (elite)
                {
                    Require(coordinator.LastOutcome == EnemyHitResponseOutcome.FeedbackOnly,
                        "Elite ordinary hit flinched: " + definition.EnemyId);
                    Damage(current, player, 1, true, 102);
                    Require(coordinator.LastOutcome == EnemyHitResponseOutcome.Flinch,
                        "Elite critical did not flinch: " + definition.EnemyId);
                    Damage(current, player, 1, true, 103);
                    Require(coordinator.LastOutcome == EnemyHitResponseOutcome.FeedbackOnly,
                        "Elite ignored cooldown: " + definition.EnemyId);
                }
                else if (weight == EnemyHitWeight.Light)
                {
                    Require(coordinator.LastOutcome == EnemyHitResponseOutcome.Flinch,
                        "Small did not flinch: " + definition.EnemyId);
                    Damage(current, player, 1, false, 102);
                    Require(coordinator.LastOutcome == EnemyHitResponseOutcome.FeedbackOnly,
                        "Small ignored cooldown: " + definition.EnemyId);
                }
                else
                {
                    Require(coordinator.LastOutcome == EnemyHitResponseOutcome.FeedbackOnly
                        && coordinator.MediumAccumulatedHits == 1,
                        "Medium first hit: " + definition.EnemyId);
                    Damage(current, player, 1, false, 101);
                    Require(coordinator.LastOutcome == EnemyHitResponseOutcome.FeedbackOnly
                        && coordinator.MediumAccumulatedHits == 1,
                        "Medium counted same sequence twice: " + definition.EnemyId);
                    Damage(current, player, 1, false, 102);
                    Require(coordinator.LastOutcome == EnemyHitResponseOutcome.Flinch,
                        "Medium second distinct hit: " + definition.EnemyId);
                    Damage(current, player, 1, false, 103);
                    Require(coordinator.LastOutcome == EnemyHitResponseOutcome.FeedbackOnly,
                        "Medium ignored cooldown: " + definition.EnemyId);
                }
                results.Add(new { id = definition.EnemyId, grade = definition.Grade.GradeType.ToString(),
                    weight = weight.ToString(), hpLoss = hp - current.Health.CurrentHp,
                    flinches = coordinator.FlinchCount, feedback = coordinator.FeedbackOnlyCount, status = "PASS" });
                spawn.Release(current);
                current = null;
                yield return null;
            }

            var medium = definitions.First(d => d.Grade.GradeType == EnemyGradeType.Normal
                && d.MovementProfile.HitWeightProfile.Weight == EnemyHitWeight.Standard);
            current = Spawn(spawn, medium, player, 4f);
            current.AI.enabled = false;
            var policy = current.GetComponent<EnemyHitResponseCoordinator>();
            Require(policy.LastOutcome == EnemyHitResponseOutcome.None && policy.MediumAccumulatedHits == 0,
                "Pool reuse retained poise state");
            var dot = new DamageInfo(1f, current.transform.position, player.gameObject,
                isDamageOverTime: true, sourceAttackSequenceId: 700);
            current.Health.TakeDamage(dot);
            Require(policy.LastOutcome == EnemyHitResponseOutcome.FeedbackOnly && policy.MediumAccumulatedHits == 0,
                "DOT contributed to poise");
            current.AnimationBridge.SetFrozen(true);
            Damage(current, player, 1, false, 701);
            Require(policy.LastOutcome == EnemyHitResponseOutcome.FeedbackOnly && policy.MediumAccumulatedHits == 0,
                "Freeze lost priority");
            current.AnimationBridge.SetFrozen(false);
            current.Health.TakeDamage(new DamageInfo(current.Health.MaxHp + 1f, current.transform.position,
                player.gameObject, sourceAttackSequenceId: 702));
            Require(policy.LastOutcome == EnemyHitResponseOutcome.Death && current.Health.IsDead,
                "Death lost priority");
            results.Add(new { id = medium.EnemyId, dot = "PASS", freeze = "PASS", death = "PASS", poolReset = "PASS" });
            spawn.Release(current);
            current = null;
            yield return null;

            current = Spawn(spawn, medium, player, 4f);
            current.AI.enabled = false;
            policy = current.GetComponent<EnemyHitResponseCoordinator>();
            var mediumReaction = current.GetComponent<EnemyMovementReaction>();
            Damage(current, player, 1, false, 711, 4f);
            Require(policy.LastOutcome == EnemyHitResponseOutcome.FeedbackOnly
                && policy.MediumAccumulatedHits == 1 && !mediumReaction.IsStunned,
                "Greatsword knockback 4 forced an early break");
            Damage(current, player, 1, false, 712, 4f);
            Require(policy.LastOutcome == EnemyHitResponseOutcome.Flinch,
                "Two separate Greatsword hits did not break poise");
            results.Add(new { id = medium.EnemyId, greatswordKnockback4 = "PASS" });
            spawn.Release(current);
            current = null;
            yield return null;

            current = Spawn(spawn, medium, player, 4f);
            current.AI.enabled = false;
            policy = current.GetComponent<EnemyHitResponseCoordinator>();
            Damage(current, player, 1, false, 0);
            Damage(current, player, 1, false, 0);
            Require(policy.LastOutcome == EnemyHitResponseOutcome.FeedbackOnly
                && policy.MediumAccumulatedHits == 1,
                "Missing-sequence duplicate was counted twice");
            float noSequenceUntil = Time.time + 0.14f;
            while (Time.time < noSequenceUntil) yield return null;
            Damage(current, player, 1, false, 0);
            Require(policy.LastOutcome == EnemyHitResponseOutcome.Flinch,
                "Missing-sequence source did not progress after guard window");
            results.Add(new { id = medium.EnemyId, missingSequenceGuard = "PASS" });
            spawn.Release(current);
            current = null;
            yield return null;

            current = Spawn(spawn, medium, player, 4f);
            current.AI.enabled = false;
            policy = current.GetComponent<EnemyHitResponseCoordinator>();
            Damage(current, player, 1, false, 721);
            float expiredAt = Time.time + 1.15f;
            while (Time.time < expiredAt) yield return null;
            Damage(current, player, 1, false, 722);
            Require(policy.LastOutcome == EnemyHitResponseOutcome.FeedbackOnly
                && policy.MediumAccumulatedHits == 1,
                "Medium accumulated beyond the 1.1-second window");
            Damage(current, player, 1, true, 723);
            Require(policy.LastOutcome == EnemyHitResponseOutcome.Flinch,
                "Medium critical did not break poise");
            results.Add(new { id = medium.EnemyId, mediumWindow = "PASS", critical = "PASS" });
            spawn.Release(current);
            current = null;
            yield return null;

            current = Spawn(spawn, medium, player, 4f);
            current.AI.enabled = false;
            policy = current.GetComponent<EnemyHitResponseCoordinator>();
            Require(current.AbilityController.UsesCommittedAim,
                "Medium fixture lacks committed aim");
            current.AbilityController.PrepareAttackAim(player.transform);
            Require(current.AbilityController.HasPreparedAim(player.transform),
                "Medium did not prepare aim");
            Damage(current, player, 1, false, 731);
            Require(policy.LastOutcome == EnemyHitResponseOutcome.FeedbackOnly
                && current.AbilityController.HasPreparedAim(player.transform),
                "Feedback-only hit cleared committed aim");
            Damage(current, player, 1, false, 732);
            Require(policy.LastOutcome == EnemyHitResponseOutcome.Flinch
                && !current.AbilityController.HasPreparedAim(player.transform),
                "Approved flinch retained committed aim");
            results.Add(new { id = medium.EnemyId, committedAimRetention = "PASS",
                flinchAimClear = "PASS" });
            spawn.Release(current);
            current = null;
            yield return null;

            var small = definitions.First(d => d.Grade.GradeType == EnemyGradeType.Normal
                && d.MovementProfile.HitWeightProfile.Weight == EnemyHitWeight.Light
                && Enumerable.Range(0, d.AbilitySet.Count).Any(i => IsMelee(d.AbilitySet.GetAbility(i))));
            int smallIndex = Enumerable.Range(0, small.AbilitySet.Count)
                .First(i => IsMelee(small.AbilitySet.GetAbility(i)));
            var smallAbility = small.AbilitySet.GetAbility(smallIndex);
            current = Spawn(spawn, small, player, Mathf.Min(1.1f, smallAbility.Range * .65f));
            current.AI.enabled = false;
            current.Movement.StopMovement();
            float smallAttackDeadline = Time.time + 4f;
            while (!current.Melee.TryStartAbility(player.transform, smallAbility, smallIndex))
            {
                Require(Time.time < smallAttackDeadline, "Small attack could not start: " + small.EnemyId);
                current.Movement.FacePosition(player.transform.position);
                yield return null;
            }
            Require(current.Melee.IsAttacking, "Small attack was not active");
            Damage(current, player, 1, false, 741);
            Require(!current.Melee.IsAttacking
                && current.GetComponent<EnemyHitResponseCoordinator>().LastOutcome == EnemyHitResponseOutcome.Flinch,
                "Small attack did not cancel on allowed flinch");
            results.Add(new { id = small.EnemyId, smallAttackCancelledOnFlinch = true,
                status = "PASS" });
            spawn.Release(current);
            current = null;
            yield return null;

            foreach (bool elite in new[] { false, true })
            {
                var meleeDefinition = definitions.FirstOrDefault(d =>
                    (d.Grade.GradeType == EnemyGradeType.Elite) == elite
                    && (elite || d.MovementProfile.HitWeightProfile.Weight == EnemyHitWeight.Standard)
                    && Enumerable.Range(0, d.AbilitySet.Count).Any(i => IsMelee(d.AbilitySet.GetAbility(i))));
                Require(meleeDefinition != null, "No melee poise fixture: elite=" + elite);
                int index = Enumerable.Range(0, meleeDefinition.AbilitySet.Count)
                    .First(i => IsMelee(meleeDefinition.AbilitySet.GetAbility(i)));
                var ability = meleeDefinition.AbilitySet.GetAbility(index);
                current = Spawn(spawn, meleeDefinition, player, Mathf.Min(1.1f, ability.Range * .65f));
                current.AI.enabled = false;
                current.Movement.StopMovement();
                float startedAt = Time.time;
                while (!current.Melee.TryStartAbility(player.transform, ability, index))
                {
                    Require(Time.time - startedAt < 4f, "Melee could not start: " + meleeDefinition.EnemyId);
                    current.Movement.FacePosition(player.transform.position);
                    yield return null;
                }
                Require(current.Melee.IsAttacking, "Melee not active: " + meleeDefinition.EnemyId);
                var hitPolicy = current.GetComponent<EnemyHitResponseCoordinator>();
                Damage(current, player, 1, elite, 801);
                if (!elite) Damage(current, player, 1, false, 802);
                Require(current.Melee.IsAttacking && hitPolicy.LastOutcome == EnemyHitResponseOutcome.FeedbackOnly,
                    "In-progress attack cancelled: " + meleeDefinition.EnemyId);
                float until = Time.time + 5f;
                while (current.Melee.IsAttacking)
                {
                    Require(Time.time < until, "Melee did not finish: " + meleeDefinition.EnemyId);
                    yield return null;
                }
                results.Add(new { id = meleeDefinition.EnemyId, attack = ability.name,
                    retainedAfterHits = true, finished = true, status = "PASS" });
                spawn.Release(current);
                current = null;
                yield return null;
            }

            foreach (bool elite in new[] { false, true })
            {
                var projectileDefinition = definitions.FirstOrDefault(d =>
                    (d.Grade.GradeType == EnemyGradeType.Elite) == elite
                    && (elite || d.MovementProfile.HitWeightProfile.Weight == EnemyHitWeight.Standard)
                    && Enumerable.Range(0, d.AbilitySet.Count).Any(i =>
                        d.AbilitySet.GetAbility(i).ExecutionMode == EnemyAbilityExecutionMode.Projectile));
                Require(projectileDefinition != null, "No projectile poise fixture: elite=" + elite);
                int index = Enumerable.Range(0, projectileDefinition.AbilitySet.Count)
                    .First(i => projectileDefinition.AbilitySet.GetAbility(i).ExecutionMode
                        == EnemyAbilityExecutionMode.Projectile);
                var ability = projectileDefinition.AbilitySet.GetAbility(index);
                current = Spawn(spawn, projectileDefinition, player, 6f);
                current.AI.enabled = false;
                current.Movement.StopMovement();
                var special = current.GetComponent<EnemyThemeSpecialExecutor>();
                Require(special != null, "Projectile executor missing: " + projectileDefinition.EnemyId);
                float startDeadline = Time.time + 5f;
                while (!special.TryStart(ability, index, player.transform))
                {
                    Require(Time.time < startDeadline, "Projectile could not start: " + projectileDefinition.EnemyId);
                    current.Movement.FacePosition(player.transform.position);
                    yield return null;
                }
                float launchDeadline = Time.time + ability.AttackAnimationDuration + 3f;
                while (!special.HasProjectile)
                {
                    Require(Time.time < launchDeadline, "Projectile did not launch: " + projectileDefinition.EnemyId);
                    yield return null;
                }
                int impactsBefore = special.ImpactCount;
                var hitPolicy = current.GetComponent<EnemyHitResponseCoordinator>();
                Damage(current, player, 1, elite, 901);
                if (!elite) Damage(current, player, 1, false, 902);
                Require(special.HasProjectile && hitPolicy.LastOutcome == EnemyHitResponseOutcome.FeedbackOnly,
                    "Projectile cancelled by poise feedback: " + projectileDefinition.EnemyId);
                float impactDeadline = Time.time + 3f;
                while (special.HasProjectile)
                {
                    Require(Time.time < impactDeadline, "Projectile did not complete: " + projectileDefinition.EnemyId);
                    yield return null;
                }
                Require(special.ImpactCount > impactsBefore,
                    "Projectile disappeared before impact: " + projectileDefinition.EnemyId);
                results.Add(new { id = projectileDefinition.EnemyId, projectile = ability.name,
                    retainedAfterHits = true, impacted = true, status = "PASS" });
                spawn.Release(current);
                current = null;
                yield return null;
            }

            foreach (string id in new[] { "SpiderBrood_Scolokarck_Tint3", "SpiderBrood_Rostrokarck" })
            {
                var definition = definitions.First(d => d.EnemyId == id);
                current = Spawn(spawn, definition, player, 5f);
                current.AI.RequestAggro(player.transform);
                float attackDeadline = Time.time + 16f;
                while (!current.AbilityController.IsExecuting)
                {
                    Require(Time.time < attackDeadline,
                        "Live AI did not begin attack: " + id + " state=" + current.AI.CurrentStateName);
                    yield return null;
                }
                var livePolicy = current.GetComponent<EnemyHitResponseCoordinator>();
                bool elite = definition.Grade.GradeType == EnemyGradeType.Elite;
                Damage(current, player, 1, elite, 951);
                if (!elite) Damage(current, player, 1, false, 952);
                Require(livePolicy.LastOutcome == EnemyHitResponseOutcome.FeedbackOnly
                    && current.AbilityController.IsExecuting,
                    "Live AI attack cancelled by poise feedback: " + id);
                float finishDeadline = Time.time + 8f;
                while (current.AbilityController.IsExecuting)
                {
                    Require(Time.time < finishDeadline, "Live AI attack did not finish: " + id);
                    yield return null;
                }
                results.Add(new { id, liveAiAttackRetained = true,
                    ability = current.AbilityController.LastCommittedAbility != null
                        ? current.AbilityController.LastCommittedAbility.name : "legacy",
                    status = "PASS" });
                spawn.Release(current);
                current = null;
                yield return null;
            }

            var playerActor = PlayerContext.GetOrCreate().CurrentActor;
            playerMelee = player.GetComponent<MeleeRuntime>();
            Require(playerActor != null && playerMelee != null, "Player melee runtime missing");
            var oneHand = AssetDatabase.LoadAssetAtPath<WeaponItemData>(
                "Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/OHS01_FleurDeLys/OHS01_FleurDeLys.asset");
            var greatsword = AssetDatabase.LoadAssetAtPath<WeaponItemData>(
                "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            Require(oneHand != null && greatsword != null, "Weapon asset missing");
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);

            foreach (bool heavyWeapon in new[] { false, true })
            {
                var weapon = heavyWeapon ? greatsword : oneHand;
                playerMelee.CancelCurrentAttackState();
                Require(playerActor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common)),
                    "Could not equip " + weapon.name);
                current = Spawn(spawn, medium, player, 1.25f);
                current.AI.enabled = false;
                current.Movement.StopMovement();
                current.Health.SetMaxHp(10000f, true);
                var target = current.GetComponent<CombatTarget>();
                var hits = new List<DamageInfo>();
                Action<CombatHealth, DamageInfo> record = (h, d) => hits.Add(d);
                current.Health.OnDamaged += record;
                try
                {
                    float readyDeadline = Time.time + 5f;
                    while (!playerMelee.IsAttackReady || !player.GetComponent<PlayerMovement>().IsGrounded)
                    {
                        Require(Time.time < readyDeadline, "Player weapon not ready: " + weapon.name);
                        yield return null;
                    }
                    var request = new WeaponActionRequest(WeaponActionSource.PlayerInput, target, Vector3.forward);
                    var started = playerMelee.TryStartAction(request, out WeaponActionHandle handle);
                    Require(started == WeaponActionResult.Accepted, "Player attack rejected: " + started);
                    if (!heavyWeapon)
                    {
                        for (int step = 1; step < 3; step++)
                        {
                            float windowDeadline = Time.time + 5f;
                            while (true)
                            {
                                var continuation = playerMelee.TryContinue(handle, request);
                                if (continuation == WeaponActionResult.Accepted) break;
                                Require(continuation == WeaponActionResult.RejectedNotReady
                                    && Time.time < windowDeadline,
                                    "OneHandSword combo step " + (step + 1) + " rejected: " + continuation);
                                yield return null;
                            }
                        }
                    }
                    float finishDeadline = Time.time + 10f;
                    while (playerMelee.IsAttackInProgress)
                    {
                        Require(Time.time < finishDeadline, "Player attack did not finish: " + weapon.name);
                        yield return null;
                    }
                    int uniqueSequences = hits.Where(h => h.sourceAttackSequenceId > 0)
                        .Select(h => h.sourceAttackSequenceId).Distinct().Count();
                    Require(uniqueSequences >= (heavyWeapon ? 1 : 3),
                        "Real weapon did not deliver distinct attack sequences: " + weapon.name
                        + " sequences=" + uniqueSequences + " hits=" + hits.Count);
                    if (heavyWeapon)
                        Require(hits.Any(h => h.knockback >= 3.9f),
                            "Greatsword did not deliver knockback 4");
                    results.Add(new { weapon = weapon.name, hitCount = hits.Count,
                        distinctAttackSequences = uniqueSequences,
                        maximumKnockback = hits.Max(h => h.knockback), status = "PASS" });
                }
                finally
                {
                    current.Health.OnDamaged -= record;
                    playerMelee.CancelCurrentAttackState();
                }
                spawn.Release(current);
                current = null;
                yield return null;
            }

            const string guardPath = "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Guard.prefab";
            GameObject guardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(guardPath);
            Require(guardPrefab != null, "Legacy shield prefab missing");
            Vector3 guardPosition = player.transform.position + Vector3.forward * 4f;
            Vector3 guardForward = player.transform.position - guardPosition;
            guardForward.y = 0f;
            GameObject guard = UnityEngine.Object.Instantiate(guardPrefab, guardPosition,
                Quaternion.LookRotation(guardForward));
            try
            {
                Require(guard.GetComponent<EnemyHitResponseCoordinator>() == null,
                    "Legacy shield actor unexpectedly gained the theme policy");
                var defense = guard.GetComponent<EnemyDefenseController>();
                var reaction = guard.GetComponent<EnemyMovementReaction>();
                var health = guard.GetComponent<CombatHealth>();
                Require(defense != null && reaction != null && health != null,
                    "Legacy shield components missing");
                guard.GetComponent<EnemyAIController>().enabled = false;
                defense.SetDefending(true);
                Require(defense.IsDefending, "Legacy guard did not raise shield");
                float before = health.CurrentHp;
                health.TakeDamage(new DamageInfo(100f, guard.transform.position, player.gameObject,
                    guard.transform.forward, 5f));
                Require(Mathf.Abs(before - health.CurrentHp - 35f) < 0.05f && !reaction.IsStunned,
                    "Legacy shield damage or movement reaction changed");
                results.Add(new { id = "Murloc_Guard", shieldDamage = 35f,
                    noFlinch = true, legacyRoute = true, status = "PASS" });
            }
            finally
            {
                UnityEngine.Object.Destroy(guard);
            }
        }
        finally
        {
            playerMelee?.CancelCurrentAttackState();
            if (current != null && spawn != null && current.IsLeased) spawn.Release(current);
            if (ui != null && ui.InArena) ui.ToggleArena();
        }
    }

    private static bool IsMelee(EnemyAbilityDefinition ability) => ability != null
        && (ability.ExecutionMode == EnemyAbilityExecutionMode.MeleeArc
            || ability.ExecutionMode == EnemyAbilityExecutionMode.DirectTarget
            || ability.ExecutionMode == EnemyAbilityExecutionMode.AreaSlam);

    private static EnemyActor Spawn(EnemySpawnService spawn, EnemyDefinition definition,
        PlayerInputFacade player, float distance)
    {
        Vector3 position = player.transform.position + Vector3.forward * distance;
        Require(Physics.Raycast(position + Vector3.up * 4f, Vector3.down, out RaycastHit floor, 9f,
            LayerMask.GetMask("Default", "Environment", "Ground"), QueryTriggerInteraction.Ignore),
            "Arena floor: " + definition.EnemyId);
        position = floor.point + Vector3.up * 0.035f;
        Vector3 facing = player.transform.position - position;
        facing.y = 0f;
        var request = new EnemySpawnRequest(definition, position, Quaternion.LookRotation(facing),
            player.transform, null, player.transform, null, 1f, 1f, 91);
        Require(spawn.TrySpawn(request, out EnemyActor actor), "Spawn: " + definition.EnemyId);
        actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        return actor;
    }

    private static void Damage(EnemyActor actor, PlayerInputFacade player, float amount,
        bool critical, int sequence, float knockback = 3f)
    {
        Vector3 direction = actor.transform.position - player.transform.position;
        direction.y = 0f;
        actor.Health.TakeDamage(new DamageInfo(amount, actor.transform.position, player.gameObject,
            direction, knockback, critical, sourceAttackSequenceId: sequence));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[EnemyHitResponse] " + message);
    }
}
