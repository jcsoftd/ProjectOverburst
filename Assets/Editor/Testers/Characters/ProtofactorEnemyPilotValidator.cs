using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

public static class ProtofactorEnemyPilotValidator
{
    private static readonly string[] RequiredParameters =
    {
        "Locomotion",
        "MoveAnimSpeed",
        "AttackAnimSpeed",
        "Attack1",
        "Attack2",
        "Attack3",
        "GotHit",
        "Death",
        "Taunt",
        "IdleBreak"
    };

    private static readonly string[] RequiredStates =
    {
        "Locomotion",
        "Attack_1",
        "Attack_2",
        "Attack_3",
        "Get_hit",
        "Death",
        "Taunt",
        "Idle_break"
    };

    [MenuItem("OVERBURST/Codex/Validate/Enemies/Validate Protofactor Enemy Pilot")]
    public static void ValidateFromMenu()
    {
        ValidateOrThrow();
    }

    public static void RunOnceFromCommandLine()
    {
        ValidateOrThrow();
    }

    public static void ValidateOrThrow()
    {
        List<string> failures = new List<string>();
        ValidateAnimatorController(failures);
        ValidateDefinitions(failures);
        ValidateRosterComposition(failures);
        ValidateMovementProfiles(failures);
        ValidateAnimationProfiles(failures);
        ValidatePrefabs(failures);
        ValidateCatalog(failures);
        ValidateAiPreset(failures);
        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "Protofactor enemy pilot validation failed:\n- " +
                string.Join("\n- ", failures));
        }

        Debug.Log(
            "[ProtofactorEnemyPilotValidator] PASS definitions=7 prefabs=4 " +
            "grounded=4 Normal/Elite sharing + Ursacetus Boss phase/executor contract valid.");
    }

    private static void ValidateAnimatorController(List<string> failures)
    {
        AnimatorController controller =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(
                ProtofactorEnemyPilotBuilder.BaseControllerPath);
        if (controller == null)
        {
            failures.Add("Base animator controller missing.");
            return;
        }

        Dictionary<string, AnimatorControllerParameter> parameters =
            controller.parameters.ToDictionary(parameter => parameter.name);
        for (int i = 0; i < RequiredParameters.Length; i++)
        {
            string required = RequiredParameters[i];
            if (!parameters.ContainsKey(required))
                failures.Add("Animator parameter missing: " + required);
            else
            {
                AnimatorControllerParameterType expectedType =
                    required == "Locomotion"
                    || required == "MoveAnimSpeed"
                    || required == "AttackAnimSpeed"
                        ? AnimatorControllerParameterType.Float
                        : AnimatorControllerParameterType.Trigger;
                if (parameters[required].type != expectedType)
                {
                    failures.Add(
                        "Animator parameter type mismatch: " + required +
                        " expected=" + expectedType +
                        " actual=" + parameters[required].type);
                }
            }
        }

        if (controller.layers == null || controller.layers.Length != 1)
        {
            failures.Add("Animator must have exactly one Base Layer.");
            return;
        }

        HashSet<string> states = new HashSet<string>(
            controller.layers[0].stateMachine.states.Select(state => state.state.name),
            StringComparer.Ordinal);
        for (int i = 0; i < RequiredStates.Length; i++)
        {
            if (!states.Contains(RequiredStates[i]))
                failures.Add("Animator state missing: " + RequiredStates[i]);
        }

        ValidateControllerClips(controller, ProtofactorEnemyPilotBuilder.BaseControllerPath, failures);
        ValidateOverrideController(ProtofactorEnemyPilotBuilder.CeratoferoxControllerPath, failures);
        ValidateOverrideController(ProtofactorEnemyPilotBuilder.RapaxControllerPath, failures);
        ValidateOverrideController(ProtofactorEnemyPilotBuilder.GobblerControllerPath, failures);
        ValidateOverrideController(ProtofactorEnemyPilotBuilder.UrsacetusControllerPath, failures);
    }

    private static void ValidateOverrideController(string path, List<string> failures)
    {
        AnimatorOverrideController controller =
            AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
        if (controller == null)
        {
            failures.Add("Override controller missing: " + path);
            return;
        }

        if (controller.runtimeAnimatorController == null)
            failures.Add("Override controller has no base controller: " + path);
        ValidateControllerClips(controller, path, failures);
    }

    private static void ValidateControllerClips(
        RuntimeAnimatorController controller,
        string ownerPath,
        List<string> failures)
    {
        AnimationClip[] clips = controller.animationClips;
        if (clips == null || clips.Length == 0)
        {
            failures.Add("Animator has no animation clips: " + ownerPath);
            return;
        }

        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            string clipPath = clip != null ? AssetDatabase.GetAssetPath(clip) : string.Empty;
            if (clip == null)
                failures.Add("Animator contains a null clip: " + ownerPath);
            else if (IsRootMotionPath(clipPath))
                failures.Add("Animator references root-motion clip: " + clipPath);
        }
    }

    private static void ValidateDefinitions(List<string> failures)
    {
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < ProtofactorEnemyPilotBuilder.DefinitionPaths.Length; i++)
        {
            string path = ProtofactorEnemyPilotBuilder.DefinitionPaths[i];
            ScriptableObject definition = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (definition == null || definition.GetType().Name != "EnemyDefinition")
            {
                failures.Add("EnemyDefinition missing or wrong type: " + path);
                continue;
            }

            SerializedObject serialized = new SerializedObject(definition);
            string id = ReadString(serialized, "enemyId", "definitionId");
            if (string.IsNullOrWhiteSpace(id))
                failures.Add("Definition ID is empty: " + path);
            else if (!ids.Add(id))
                failures.Add("Duplicate definition ID: " + id);
            RequireObject(serialized, path, failures, "species");
            RequireObject(serialized, path, failures, "grade");
            RequireObject(serialized, path, failures, "variant");
            RequireObject(serialized, path, failures, "actorPrefab", "prefab");
            RequireObject(serialized, path, failures, "animationProfile");
            RequireObject(serialized, path, failures, "abilitySet");
            RequireObject(serialized, path, failures, "behaviorProfile");
            RequireObject(serialized, path, failures, "movementProfile");
            if (definition is EnemyDefinition typedDefinition && !typedDefinition.IsValid)
                failures.Add("EnemyDefinition.IsValid is false: " + path);
            else if (definition is EnemyDefinition validDefinition)
            {
                if (validDefinition.SquadParticipationMode
                    != EnemySquadParticipationMode.Independent)
                {
                    RequireObject(
                        serialized,
                        path,
                        failures,
                        "aiPreset",
                        "squadPursuitPreset");
                }
                ValidateAbilityContracts(validDefinition.AbilitySet, path, failures);
            }
        }
    }

    private static void ValidateAbilityContracts(
        EnemyAbilitySet abilitySet,
        string ownerPath,
        List<string> failures)
    {
        if (abilitySet == null || !abilitySet.IsValid)
        {
            failures.Add("EnemyAbilitySet is invalid: " + ownerPath);
            return;
        }

        for (int i = 0; i < abilitySet.Count; i++)
        {
            EnemyAbilityDefinition ability = abilitySet.GetAbility(i);
            if (ability == null)
            {
                failures.Add("Ability is null: " + ownerPath + " index=" + i);
                continue;
            }

            if (ability.ExecutionMode != EnemyAbilityExecutionMode.DirectTarget
                && ability.ExecutionMode != EnemyAbilityExecutionMode.AreaSlam)
            {
                failures.Add(
                    "Authored ability must use DirectTarget or AreaSlam: "
                    + ownerPath
                    + " ability="
                    + ability.AbilityId);
            }
            if (!ability.RequireLineOfSight)
                failures.Add("Authored ability must require line of sight: " + ability.AbilityId);
        }
    }

    private static void ValidateRosterComposition(List<string> failures)
    {
        EnemyDefinition ceratoNormal = LoadDefinition(0, failures);
        EnemyDefinition ceratoElite = LoadDefinition(1, failures);
        EnemyDefinition rapaxNormal = LoadDefinition(2, failures);
        EnemyDefinition rapaxElite = LoadDefinition(3, failures);
        EnemyDefinition gobblerNormal = LoadDefinition(4, failures);
        EnemyDefinition gobblerElite = LoadDefinition(5, failures);
        EnemyDefinition ursacetusBoss = LoadDefinition(6, failures);

        ValidateNormalElitePair(ceratoNormal, ceratoElite, "Ceratoferox", failures);
        ValidateNormalElitePair(rapaxNormal, rapaxElite, "Rapax", failures);
        ValidateNormalElitePair(gobblerNormal, gobblerElite, "Gobbler", failures);
        if (gobblerNormal != null
            && (gobblerNormal.AbilitySet == null || gobblerNormal.AbilitySet.Count != 2))
        {
            failures.Add("Gobbler must expose exactly Bite/Ram two-ability pilot set.");
        }
        ValidateUrsacetusBoss(ursacetusBoss, failures);
    }

    private static void ValidateUrsacetusBoss(
        EnemyDefinition definition,
        List<string> failures)
    {
        if (definition == null)
            return;
        if (definition.Grade == null
            || definition.Grade.GradeType != EnemyGradeType.Boss)
        {
            failures.Add("Ursacetus grade must be Boss.");
        }
        if (definition.SquadParticipationMode
            != EnemySquadParticipationMode.Independent)
        {
            failures.Add("Ursacetus must use Independent squad participation.");
        }
        if (definition.AiPreset != null)
            failures.Add("Ursacetus must not reference a squad AI preset.");

        EnemyBossDefinition boss = AssetDatabase.LoadAssetAtPath<EnemyBossDefinition>(
            ProtofactorEnemyPilotBuilder.UrsacetusBossDefinitionPath);
        if (boss == null || !boss.IsValid || boss.PhaseCount != 3)
        {
            failures.Add("Ursacetus boss definition must expose three valid phases.");
            return;
        }

        int[] expectedCounts = { 1, 2, 3 };
        for (int i = 0; i < boss.PhaseCount; i++)
        {
            EnemyBossPhaseDefinition phase = boss.GetPhase(i);
            if (phase == null
                || phase.AbilitySet == null
                || phase.AbilitySet.Count != expectedCounts[i])
            {
                failures.Add(
                    "Ursacetus phase ability count mismatch: index=" + i);
                continue;
            }
            ValidateAbilityContracts(
                phase.AbilitySet,
                boss.name + " phase=" + i,
                failures);
        }
    }

    private static EnemyDefinition LoadDefinition(int index, List<string> failures)
    {
        if (index < 0 || index >= ProtofactorEnemyPilotBuilder.DefinitionPaths.Length)
            return null;

        string path = ProtofactorEnemyPilotBuilder.DefinitionPaths[index];
        EnemyDefinition definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);
        if (definition == null)
            failures.Add("Roster definition missing: " + path);
        return definition;
    }

    private static void ValidateNormalElitePair(
        EnemyDefinition normal,
        EnemyDefinition elite,
        string speciesId,
        List<string> failures)
    {
        if (normal == null || elite == null)
            return;
        if (normal.Species != elite.Species)
            failures.Add(speciesId + " Normal/Elite must share Species.");
        if (normal.ActorPrefab != elite.ActorPrefab)
            failures.Add(speciesId + " Normal/Elite must share ActorPrefab.");
        if (normal.AbilitySet != elite.AbilitySet)
            failures.Add(speciesId + " Normal/Elite must share phase-two AbilitySet.");
        if (normal.Grade == null || normal.Grade.GradeType != EnemyGradeType.Normal)
            failures.Add(speciesId + " Normal grade mismatch.");
        if (elite.Grade == null || elite.Grade.GradeType != EnemyGradeType.Elite)
            failures.Add(speciesId + " Elite grade mismatch.");
        if (elite.Variant == null)
        {
            failures.Add(speciesId + " Elite variant missing.");
            return;
        }

        Vector3 expectedScale = Vector3.one * 1.15f;
        if (elite.Variant.VisualScale != expectedScale
            || elite.Variant.CollisionScale != expectedScale
            || elite.Variant.AnchorScale != expectedScale)
        {
            failures.Add(speciesId + " Elite visual/collision/anchor scale must be 1.15.");
        }
        if (elite.Variant.Tint == Color.white)
            failures.Add(speciesId + " Elite tint must differ from Normal.");
        if (!Mathf.Approximately(elite.Grade.HealthMultiplier, 1f)
            || !Mathf.Approximately(elite.Grade.DamageMultiplier, 1f)
            || !Mathf.Approximately(elite.Grade.MoveSpeedMultiplier, 1f)
            || !Mathf.Approximately(elite.Grade.AttackSpeedMultiplier, 1f))
        {
            failures.Add(speciesId + " Elite balance multipliers must remain placeholder 1.0.");
        }
    }

    private static void ValidateAnimationProfiles(List<string> failures)
    {
        for (int i = 0; i < ProtofactorEnemyPilotBuilder.AnimationProfilePaths.Length; i++)
        {
            string path = ProtofactorEnemyPilotBuilder.AnimationProfilePaths[i];
            ScriptableObject profile = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (profile == null || profile.GetType().Name != "EnemyAnimationProfile")
            {
                failures.Add("EnemyAnimationProfile missing or wrong type: " + path);
                continue;
            }

            SerializedObject serialized = new SerializedObject(profile);
            RequireObject(serialized, path, failures, "runtimeController", "animatorController", "controller");
            RequireClip(serialized, path, failures, "idle", "idleClip");
            RequireClip(serialized, path, failures, "walk", "walkClip");
            RequireClip(serialized, path, failures, "run", "runClip");
            RequireClip(serialized, path, failures, "hit", "hitClip");
            RequireClip(serialized, path, failures, "death", "deathClip");
            SerializedProperty attacks = FindProperty(serialized, "attackClips");
            if (attacks == null || !attacks.isArray || attacks.arraySize < 3)
            {
                failures.Add("Animation profile needs at least three attacks: " + path);
            }
            else
            {
                for (int attackIndex = 0; attackIndex < attacks.arraySize; attackIndex++)
                {
                    Object value = attacks.GetArrayElementAtIndex(attackIndex).objectReferenceValue;
                    ValidateClip(value as AnimationClip, path + " attack[" + attackIndex + "]", failures);
                }
            }

            ValidateClipArray(serialized, path, "optional", failures);
            ValidateClipArray(serialized, path, "optionalClips", failures);
            SerializedProperty excluded = FindProperty(
                serialized,
                "excludedRootMotionClipPaths");
            if (excluded == null || !excluded.isArray || excluded.arraySize == 0)
            {
                failures.Add("Root-motion exclusion audit is empty: " + path);
            }
            else
            {
                for (int excludedIndex = 0; excludedIndex < excluded.arraySize; excludedIndex++)
                {
                    string excludedPath =
                        excluded.GetArrayElementAtIndex(excludedIndex).stringValue;
                    if (!IsRootMotionPath(excludedPath))
                    {
                        failures.Add(
                            "Root-motion exclusion has invalid path: " +
                            path + " -> " + excludedPath);
                    }
                }
            }
        }
    }

    private static void ValidateMovementProfiles(List<string> failures)
    {
        ValidateMovementProfile(
            ProtofactorEnemyPilotBuilder.CeratoferoxMovementProfilePath,
            0.989f,
            4.2f,
            failures);
        ValidateMovementProfile(
            ProtofactorEnemyPilotBuilder.RapaxMovementProfilePath,
            1.175f,
            7.496f,
            failures);
        ValidatePositiveMovementProfile(
            ProtofactorEnemyPilotBuilder.GobblerMovementProfilePath,
            failures);
        ValidatePositiveMovementProfile(
            ProtofactorEnemyPilotBuilder.UrsacetusMovementProfilePath,
            failures);
    }

    private static void ValidatePositiveMovementProfile(
        string path,
        List<string> failures)
    {
        EnemyMovementProfile profile =
            AssetDatabase.LoadAssetAtPath<EnemyMovementProfile>(path);
        if (profile == null)
        {
            failures.Add("EnemyMovementProfile missing: " + path);
            return;
        }
        if (profile.AnimationReferenceSpeed <= 0f
            || profile.RunAnimationReferenceSpeed <= 0f)
        {
            failures.Add("Movement animation reference speeds must be positive: " + path);
        }
    }

    private static void ValidateMovementProfile(
        string path,
        float expectedWalkReferenceSpeed,
        float expectedRunReferenceSpeed,
        List<string> failures)
    {
        EnemyMovementProfile profile =
            AssetDatabase.LoadAssetAtPath<EnemyMovementProfile>(path);
        if (profile == null)
        {
            failures.Add("EnemyMovementProfile missing: " + path);
            return;
        }

        if (!Mathf.Approximately(
                profile.AnimationReferenceSpeed,
                expectedWalkReferenceSpeed))
        {
            failures.Add(
                "Walk animation reference speed mismatch: "
                + path
                + " actual="
                + profile.AnimationReferenceSpeed);
        }

        if (!Mathf.Approximately(
                profile.RunAnimationReferenceSpeed,
                expectedRunReferenceSpeed))
        {
            failures.Add(
                "Run animation reference speed mismatch: "
                + path
                + " actual="
                + profile.RunAnimationReferenceSpeed);
        }
    }

    private static void ValidatePrefabs(List<string> failures)
    {
        for (int i = 0; i < ProtofactorEnemyPilotBuilder.PrefabPaths.Length; i++)
        {
            string path = ProtofactorEnemyPilotBuilder.PrefabPaths[i];
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                failures.Add("Actor prefab missing: " + path);
                continue;
            }

            if (prefab.transform.localScale != Vector3.one)
                failures.Add("Actor root scale must be one: " + path);
            if (!prefab.activeSelf)
                failures.Add("Actor prefab root must be active by default: " + path);

            Transform visualRoot = prefab.transform.Find("VisualRoot");
            Transform collisionRoot = prefab.transform.Find("CollisionRoot");
            Transform anchors = prefab.transform.Find("Anchors");
            if (visualRoot == null)
                failures.Add("VisualRoot missing: " + path);
            else if (visualRoot.localScale != Vector3.one)
                failures.Add("VisualRoot authored scale must be one: " + path);
            else if (visualRoot.localPosition.sqrMagnitude > 0.000001f)
                failures.Add("VisualRoot must remain at the actor ground pivot: " + path);
            if (collisionRoot == null)
                failures.Add("CollisionRoot missing: " + path);
            else if (collisionRoot.localScale != Vector3.one)
                failures.Add("CollisionRoot authored scale must be one: " + path);
            if (anchors == null)
                failures.Add("Anchors missing: " + path);
            else if (anchors.localScale != Vector3.one)
                failures.Add("Anchors authored scale must be one: " + path);
            if (anchors != null)
            {
                RequireChild(anchors, "AttackPoint", path, failures);
                RequireChild(anchors, "HitVfxPoint", path, failures);
                RequireChild(anchors, "HpBarAnchor", path, failures);
                RequireChild(anchors, "GroundProbe", path, failures);
            }

            if (collisionRoot != null && collisionRoot.GetComponent<CapsuleCollider>() == null)
                failures.Add("CollisionRoot CapsuleCollider missing: " + path);
            if (prefab.GetComponent<Rigidbody>() == null)
                failures.Add("Actor Rigidbody missing: " + path);
            RunFallGuard runFallGuard = prefab.GetComponent<RunFallGuard>();
            if (runFallGuard == null)
                failures.Add("RunFallGuard missing: " + path);
            else if (runFallGuard.enabled)
                failures.Add("RunFallGuard must be disabled by default: " + path);

            EnemyAIController enemyAi = prefab.GetComponent<EnemyAIController>();
            if (enemyAi == null)
            {
                failures.Add("EnemyAIController missing: " + path);
            }
            else
            {
                try
                {
                    if (enemyAi.CurrentDebugStateName != "None")
                    {
                        failures.Add(
                            "Uninitialized EnemyAIController debug state must be None: "
                            + path
                            + " actual="
                            + enemyAi.CurrentDebugStateName);
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(
                        "Uninitialized EnemyAIController debug state threw "
                        + exception.GetType().Name
                        + ": "
                        + path);
                }
            }

            EnemyOverheadHpBar overheadHpBar = prefab.GetComponent<EnemyOverheadHpBar>();
            if (overheadHpBar == null)
            {
                failures.Add("EnemyOverheadHpBar missing: " + path);
            }
            else
            {
                Transform expectedAnchor = anchors != null
                    ? anchors.Find("HpBarAnchor")
                    : null;
                SerializedObject serializedHpBar = new SerializedObject(overheadHpBar);
                SerializedProperty anchorProperty = FindProperty(serializedHpBar, "hpBarAnchor");
                if (anchorProperty == null)
                    failures.Add("EnemyOverheadHpBar.hpBarAnchor property missing: " + path);
                else if (anchorProperty.objectReferenceValue == null)
                    failures.Add("EnemyOverheadHpBar.hpBarAnchor is null: " + path);
                else if (anchorProperty.objectReferenceValue != expectedAnchor)
                    failures.Add("EnemyOverheadHpBar.hpBarAnchor mismatch: " + path);
            }
            bool isBossPrefab = path == ProtofactorEnemyPilotBuilder.PrefabPaths[3];
            if (isBossPrefab && overheadHpBar != null && overheadHpBar.enabled)
                failures.Add("Boss overhead HP bar must be disabled in favor of Boss HUD.");

            Component actor = prefab.GetComponents<Component>()
                .FirstOrDefault(component => component != null && component.GetType().Name == "EnemyActor");
            if (actor == null)
            {
                failures.Add("EnemyActor missing: " + path);
            }
            else
            {
                SerializedObject serializedActor = new SerializedObject(actor);
                RequireObject(serializedActor, path, failures, "definition");
                RequireObject(serializedActor, path, failures, "identity");
                RequireObject(serializedActor, path, failures, "visualRoot");
                RequireObject(serializedActor, path, failures, "collisionRoot");
                RequireObject(serializedActor, path, failures, "anchors");
                RequireObject(serializedActor, path, failures, "health");
                RequireObject(serializedActor, path, failures, "movement");
                RequireObject(serializedActor, path, failures, "ai");
                RequireObject(serializedActor, path, failures, "melee");
                RequireObject(serializedActor, path, failures, "abilityController", "abilities");
                RequireObject(serializedActor, path, failures, "animationBridge");
                RequireObject(serializedActor, path, failures, "animator");
                RequireObject(serializedActor, path, failures, "runFallGuard");
                if (actor is EnemyActor typedActor && !typedActor.IsAuthoringValid)
                    failures.Add("EnemyActor.IsAuthoringValid is false: " + path);
                if (isBossPrefab
                    && actor is EnemyActor bossActor
                    && (bossActor.BossPhaseController == null
                        || bossActor.BossOutcomeController == null))
                {
                    failures.Add(
                        "EnemyActor boss lifecycle references missing: "
                        + path);
                }
            }

            Component abilityController = prefab.GetComponents<Component>()
                .FirstOrDefault(component =>
                    component != null && component.GetType().Name == "EnemyAbilityController");
            if (abilityController == null)
            {
                failures.Add("EnemyAbilityController missing: " + path);
            }
            else
            {
                SerializedObject serializedAbility = new SerializedObject(abilityController);
                RequireObject(serializedAbility, path, failures, "meleeExecutor");
                RequireObject(serializedAbility, path, failures, "abilitySet");
                SerializedProperty executors = FindProperty(serializedAbility, "executors");
                int expectedExecutorCount = isBossPrefab ? 2 : 1;
                if (executors == null
                    || !executors.isArray
                    || executors.arraySize != expectedExecutorCount)
                {
                    failures.Add(
                        "EnemyAbilityController executor count mismatch: "
                        + path
                        + " expected="
                        + expectedExecutorCount);
                }
                else
                {
                    for (int executorIndex = 0;
                         executorIndex < executors.arraySize;
                         executorIndex++)
                    {
                        if (executors
                            .GetArrayElementAtIndex(executorIndex)
                            .objectReferenceValue == null)
                        {
                            failures.Add(
                                "EnemyAbilityController executor is null: "
                                + path
                                + " index="
                                + executorIndex);
                        }
                    }
                }
            }
            if (prefab.GetComponent<EnemyMeleeAbilityExecutor>() == null)
                failures.Add("EnemyMeleeAbilityExecutor missing: " + path);
            if (isBossPrefab)
            {
                if (prefab.GetComponent<EnemyAreaSlamAbilityExecutor>() == null)
                    failures.Add("EnemyAreaSlamAbilityExecutor missing: " + path);
                EnemyBossPhaseController bossController =
                    prefab.GetComponent<EnemyBossPhaseController>();
                if (bossController == null
                    || bossController.BossDefinition == null
                    || !bossController.BossDefinition.IsValid)
                {
                    failures.Add("EnemyBossPhaseController contract invalid: " + path);
                }
                EnemyBossOutcomeController bossOutcome =
                    prefab.GetComponent<EnemyBossOutcomeController>();
                if (bossOutcome == null
                    || bossOutcome.Boss != bossController
                    || string.IsNullOrWhiteSpace(
                        bossOutcome.RewardProfileId))
                {
                    failures.Add(
                        "EnemyBossOutcomeController contract invalid: "
                        + path);
                }
            }

            Animator animator = prefab.GetComponentInChildren<Animator>(true);
            if (animator == null)
                failures.Add("Animator missing: " + path);
            else
            {
                if (animator.applyRootMotion)
                    failures.Add("Animator root motion must be disabled: " + path);
                if (animator.runtimeAnimatorController == null)
                    failures.Add("Animator controller missing: " + path);
                else
                    ValidateControllerClips(animator.runtimeAnimatorController, path, failures);
            }

            if (visualRoot != null)
            {
                Transform vendorModel = visualRoot.Find("VendorModel");
                if (vendorModel == null)
                {
                    failures.Add("Nested VendorModel missing: " + path);
                }
                else
                {
                    Object source = PrefabUtility.GetCorrespondingObjectFromSource(vendorModel.gameObject);
                    string sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;
                    if (!sourcePath.StartsWith(
                            "Assets/ThirdParty/01_비인간캐릭터/Protofactor/",
                            StringComparison.Ordinal))
                    {
                        failures.Add("VendorModel is not a nested Protofactor prefab: " + path);
                    }
                }
            }

            if (HasMissingScripts(prefab))
                failures.Add("Missing Script exists: " + path);

            ValidateBakedGrounding(prefab, path, failures);
        }
    }

    private static void ValidateBakedGrounding(
        GameObject prefab,
        string path,
        List<string> failures)
    {
        GameObject preview =
            PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (preview == null)
        {
            failures.Add("Grounding preview instantiate failed: " + path);
            return;
        }

        try
        {
            Transform visualRoot = preview.transform.Find("VisualRoot");
            Animator animator = preview.GetComponentInChildren<Animator>(true);
            if (visualRoot == null || animator == null)
                return;

            ProtofactorEnemyPilotBuilder.EvaluateGroundingPose(
                preview,
                animator);
            Bounds bounds =
                ProtofactorEnemyPilotBuilder.CalculateBakedVisualBounds(
                    preview,
                    visualRoot);
            if (bounds.size.sqrMagnitude <= 0.0001f)
            {
                failures.Add("Baked visual bounds are empty: " + path);
                return;
            }

            float expectedBottom =
                ProtofactorEnemyPilotBuilder.GroundContactInset;
            if (Mathf.Abs(bounds.min.y - expectedBottom) > 0.01f)
            {
                failures.Add(
                    "Baked foot bottom is not grounded: "
                    + path
                    + " actual="
                    + bounds.min.y.ToString("F4")
                    + " expected="
                    + expectedBottom.ToString("F4"));
            }

            CapsuleCollider bodyCollider =
                preview.transform.Find("CollisionRoot")
                    ?.GetComponent<CapsuleCollider>();
            if (bodyCollider != null)
            {
                float colliderBottom =
                    bodyCollider.center.y - bodyCollider.height * 0.5f;
                if (Mathf.Abs(colliderBottom - expectedBottom) > 0.001f)
                {
                    failures.Add(
                        "Collider bottom does not match visual ground inset: "
                        + path);
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(preview);
        }
    }

    private static void ValidateCatalog(List<string> failures)
    {
        ScriptableObject catalog =
            AssetDatabase.LoadAssetAtPath<ScriptableObject>(ProtofactorEnemyPilotBuilder.CatalogPath);
        if (catalog == null || catalog.GetType().Name != "EnemyCatalog")
        {
            failures.Add("EnemyCatalog missing or wrong type.");
            return;
        }

        SerializedObject serialized = new SerializedObject(catalog);
        SerializedProperty definitions = FindProperty(serialized, "definitions");
        if (definitions == null || !definitions.isArray)
        {
            failures.Add("EnemyCatalog definitions array missing.");
            return;
        }
        if (definitions.arraySize != ProtofactorEnemyPilotBuilder.DefinitionPaths.Length)
        {
            failures.Add(
                "EnemyCatalog definition count mismatch: expected="
                + ProtofactorEnemyPilotBuilder.DefinitionPaths.Length
                + " actual="
                + definitions.arraySize);
        }

        HashSet<Object> objects = new HashSet<Object>();
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < definitions.arraySize; i++)
        {
            Object value = definitions.GetArrayElementAtIndex(i).objectReferenceValue;
            if (value == null)
            {
                failures.Add("EnemyCatalog null definition at index " + i);
                continue;
            }
            if (!objects.Add(value))
                failures.Add("EnemyCatalog duplicate definition reference: " + value.name);

            SerializedObject definition = new SerializedObject(value);
            string id = ReadString(definition, "enemyId", "definitionId");
            if (string.IsNullOrWhiteSpace(id))
                failures.Add("EnemyCatalog definition has empty ID: " + value.name);
            else if (!ids.Add(id))
                failures.Add("EnemyCatalog duplicate enemy ID: " + id);
        }

        for (int i = 0; i < ProtofactorEnemyPilotBuilder.DefinitionPaths.Length; i++)
        {
            Object expected = AssetDatabase.LoadMainAssetAtPath(
                ProtofactorEnemyPilotBuilder.DefinitionPaths[i]);
            if (expected != null && !objects.Contains(expected))
                failures.Add("EnemyCatalog missing pilot definition: " + expected.name);
        }
    }

    private static void ValidateAiPreset(List<string> failures)
    {
        EnemyAiPreset preset = AssetDatabase.LoadAssetAtPath<EnemyAiPreset>(
            ProtofactorEnemyPilotBuilder.AiPresetPath);
        if (preset == null)
        {
            failures.Add("Protofactor AI preset missing.");
            return;
        }

        if (preset.PresetId != "ProtofactorSquad")
            failures.Add("Protofactor AI preset ID mismatch.");
        if (preset.ActivationCount != 41)
            failures.Add("Protofactor squad activation count must be 41.");
        if (preset.MinimumSquadSize != 4 || preset.MaximumSquadSize != 8)
            failures.Add("Protofactor squad size contract must be 4~8.");
        if (preset.DefaultMonsterCount != 3)
            failures.Add("Protofactor AI preset must reference all three actor prefabs.");
        for (int i = 0; i < preset.DefaultMonsterCount; i++)
        {
            if (preset.GetDefaultMonsterPrefab(i) == null)
                failures.Add("Protofactor AI preset prefab is null: " + i);
        }
    }

    private static void RequireClip(
        SerializedObject serialized,
        string path,
        List<string> failures,
        params string[] propertyNames)
    {
        SerializedProperty property = FindProperty(serialized, propertyNames);
        ValidateClip(
            property != null ? property.objectReferenceValue as AnimationClip : null,
            path + " " + string.Join("/", propertyNames),
            failures);
    }

    private static void ValidateClipArray(
        SerializedObject serialized,
        string path,
        string propertyName,
        List<string> failures)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null || !property.isArray)
            return;
        for (int i = 0; i < property.arraySize; i++)
        {
            ValidateClip(
                property.GetArrayElementAtIndex(i).objectReferenceValue as AnimationClip,
                path + " " + propertyName + "[" + i + "]",
                failures);
        }
    }

    private static void ValidateClip(
        AnimationClip clip,
        string owner,
        List<string> failures)
    {
        if (clip == null)
        {
            failures.Add("Animation clip missing: " + owner);
            return;
        }

        string path = AssetDatabase.GetAssetPath(clip);
        if (IsRootMotionPath(path))
            failures.Add("Root-motion clip is not allowed: " + owner + " -> " + path);
    }

    private static bool IsRootMotionPath(string path)
    {
        return System.IO.Path.GetFileNameWithoutExtension(path)
            .EndsWith("_RM", StringComparison.OrdinalIgnoreCase);
    }

    private static void RequireChild(
        Transform parent,
        string childName,
        string path,
        List<string> failures)
    {
        if (parent.Find(childName) == null)
            failures.Add(childName + " missing: " + path);
    }

    private static void RequireObject(
        SerializedObject serialized,
        string path,
        List<string> failures,
        params string[] propertyNames)
    {
        SerializedProperty property = FindProperty(serialized, propertyNames);
        if (property == null)
            failures.Add("Serialized reference property missing: " + path + " / " + string.Join("|", propertyNames));
        else if (property.objectReferenceValue == null)
            failures.Add("Serialized reference is null: " + path + " / " + property.propertyPath);
    }

    private static string ReadString(SerializedObject serialized, params string[] propertyNames)
    {
        SerializedProperty property = FindProperty(serialized, propertyNames);
        return property != null ? property.stringValue : string.Empty;
    }

    private static SerializedProperty FindProperty(
        SerializedObject serialized,
        params string[] propertyNames)
    {
        for (int i = 0; i < propertyNames.Length; i++)
        {
            SerializedProperty property = serialized.FindProperty(propertyNames[i]);
            if (property != null)
                return property;
        }

        return null;
    }

    private static bool HasMissingScripts(GameObject root)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transforms[i].gameObject) > 0)
                return true;
        }

        return false;
    }
}
