using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class EnemyStatePatternPrefabFormalizer
{
    private const float DetectionRange = 10f;
    private const float LoseTargetRange = 14f;
    private const float DefaultMoveStopDistance = 1.5f;
    private const float AttackExitMargin = 0.4f;
    private const float ReturnArriveDistance = 0.3f;
    private const float AttackHitNormalizedTime = 0.45f;
    private const float HitAnimationSpeedMultiplier = 2.5f;
    private const float KnockbackDistancePerStrength = 0.1f;
    private const float HitAnimationSpeedBoostDuration = 0.45f;
    private const string HideoutScenePath = "Assets/ProjectOverburst/00_Scenes/HideoutScene.unity";
    private const string BehaviorProfileFolder = "Assets/ProjectOverburst/Resources/Enemies/BehaviorProfiles";

    private static readonly string[] PrefabPaths =
    {
        "Assets/ProjectOverburst/Resources/Enemies/PF_StageMonster.prefab",
        "Assets/ProjectOverburst/Resources/Enemies/PF_StageMonster_FishmanTest.prefab",
        "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Grunt.prefab",
        "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Scout.prefab",
        "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Spearling.prefab",
        "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Guard.prefab",
        "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Brute.prefab",
        "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Warlord.prefab"
    };

    public static IReadOnlyList<string> TargetPrefabPaths { get { return PrefabPaths; } }

    public static bool UsesDensityApproachByDefault(string rootName)
    {
        switch (rootName)
        {
            case "PF_StageMonster_Murloc_Grunt":
            case "PF_StageMonster_Murloc_Scout":
            case "PF_StageMonster_Murloc_Spearling":
            case "PF_StageMonster_Murloc_Guard":
            case "PF_StageMonster_Murloc_Brute":
            case "PF_StageMonster_Murloc_Warlord":
                return true;
            default:
                return false;
        }
    }

    [MenuItem("OVERBURST/Codex/Setup/Enemies/Formalize State Pattern AI Prefabs")]
    public static void FormalizeAllFromMenu()
    {
        FormalizeAll();
    }

    public static void RunOnceFromCommandLine()
    {
        FormalizeAll();
    }

    [MenuItem("OVERBURST/Codex/Setup/Enemies/Formalize Elemental Status Assembly")]
    public static void FormalizeElementalStatusAssemblyFromMenu()
    {
        FormalizeElementalStatusAssemblyFromCommandLine();
    }

    public static void FormalizeElementalStatusAssemblyFromCommandLine()
    {
        List<string> failures = new List<string>();
        int savedCount = 0;

        for (int i = 0; i < PrefabPaths.Length; i++)
        {
            string path = PrefabPaths[i];
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                failures.Add(path + " (prefab missing)");
                continue;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                ConfigureElementalStatusAssembly(root, path);
                ValidateElementalStatusAssembly(root, path);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                savedCount++;
            }
            catch (System.Exception exception)
            {
                failures.Add(path + " (" + exception.Message + ")");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (failures.Count > 0)
            throw new System.InvalidOperationException("Enemy elemental status assembly failed:\n" + string.Join("\n", failures));

        Debug.Log("[EnemyStatePatternPrefabFormalizer] Elemental status assembly prefabs=" + savedCount);
    }

    public static void ConfigureEnemyPrefab(GameObject root)
    {
        if (root == null)
            throw new System.ArgumentNullException(nameof(root));

        CombatHealth health = EnsureComponent<CombatHealth>(root);
        EnemyController enemyController = EnsureComponent<EnemyController>(root);
        EnemyMotor motor = EnsureComponent<EnemyMotor>(root);
        EnemyMovementReaction movementReaction = EnsureComponent<EnemyMovementReaction>(root);
        EnemyLocomotionAnimator locomotionAnimator = EnsureComponent<EnemyLocomotionAnimator>(root);
        EnemyMovement movement = EnsureComponent<EnemyMovement>(root);
        EnemyMeleeAttackController meleeAttack = EnsureComponent<EnemyMeleeAttackController>(root);
        EnemySensor sensor = EnsureComponent<EnemySensor>(root);
        EnsureComponent<EnemyCrowdAgent>(root);
        EnemyAIController aiController = EnsureComponent<EnemyAIController>(root);
        EnsureComponent<EnemyLootDropper>(root);
        ConfigureElementalStatusAssembly(root, root.name);

        EnemyAnimationBridge animationBridge = root.GetComponent<EnemyAnimationBridge>();
        EnemyBehaviorProfile behaviorProfile = LoadBehaviorProfile(root.name);
        EnemyDefenseController defenseController = root.GetComponent<EnemyDefenseController>();
        if (behaviorProfile != null && behaviorProfile.HasShield)
            defenseController = defenseController != null ? defenseController : root.AddComponent<EnemyDefenseController>();
        else if (defenseController != null)
        {
            Object.DestroyImmediate(defenseController);
            defenseController = null;
        }
        Transform attackPoint = EnsureAttackPoint(root.transform);

        SerializedObject serializedEnemyController = new SerializedObject(enemyController);
        SetObject(serializedEnemyController, "health", health);
        serializedEnemyController.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject serializedMovement = new SerializedObject(movement);
        SetObject(serializedMovement, "health", health);
        SetObject(serializedMovement, "motor", motor);
        SetObject(serializedMovement, "reaction", movementReaction);
        SetObject(serializedMovement, "locomotionAnimator", locomotionAnimator);
        serializedMovement.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject serializedMotor = new SerializedObject(motor);
        SetObject(serializedMotor, "body", root.GetComponent<Rigidbody>());
        serializedMotor.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject serializedReaction = new SerializedObject(movementReaction);
        SetObject(serializedReaction, "health", health);
        SetObject(serializedReaction, "motor", motor);
        SetFloat(serializedReaction, "knockbackDistancePerStrength", KnockbackDistancePerStrength);
        serializedReaction.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject serializedLocomotionAnimator = new SerializedObject(locomotionAnimator);
        SetObject(serializedLocomotionAnimator, "animationBridge", animationBridge);
        serializedLocomotionAnimator.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject serializedAttack = new SerializedObject(meleeAttack);
        SetObject(serializedAttack, "movement", movement);
        SetObject(serializedAttack, "movementReaction", movementReaction);
        SetObject(serializedAttack, "animationBridge", animationBridge);
        SetObject(serializedAttack, "attackPoint", attackPoint);
        SetFloat(serializedAttack, "hitNormalizedTime", AttackHitNormalizedTime);
        SerializedProperty targetLayer = serializedAttack.FindProperty("targetLayer");
        if (targetLayer != null && targetLayer.intValue == 0)
        {
            int playerLayer = LayerMask.NameToLayer("Player");
            targetLayer.intValue = playerLayer >= 0 ? 1 << playerLayer : ~0;
        }

        SerializedProperty attackRangeProperty = serializedAttack.FindProperty("attackRange");
        float attackRange = attackRangeProperty != null ? Mathf.Max(0f, attackRangeProperty.floatValue) : 1.7f;
        if (attackRange <= 0f)
        {
            attackRange = 1.7f;
            if (attackRangeProperty != null)
                attackRangeProperty.floatValue = attackRange;
        }
        serializedAttack.ApplyModifiedPropertiesWithoutUndo();

        if (animationBridge != null)
        {
            SerializedObject serializedBridge = new SerializedObject(animationBridge);
            SetObject(serializedBridge, "movementReaction", movementReaction);
            SetObject(serializedBridge, "defenseController", defenseController);
            SetString(serializedBridge, "hitStateName", "Get_hit");
            SetFloat(serializedBridge, "hitReactionAnimationSpeedMultiplier", HitAnimationSpeedMultiplier);
            SetFloat(serializedBridge, "hitAnimationSpeedBoostDuration", HitAnimationSpeedBoostDuration);
            serializedBridge.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(animationBridge);
        }

        SerializedObject serializedAI = new SerializedObject(aiController);
        SetObject(serializedAI, "health", health);
        SetObject(serializedAI, "movement", movement);
        SetObject(serializedAI, "movementReaction", movementReaction);
        SetObject(serializedAI, "sensor", sensor);
        SetObject(serializedAI, "meleeAttack", meleeAttack);
        SetObject(serializedAI, "animationBridge", animationBridge);
        SetObject(serializedAI, "defenseController", defenseController);
        SetObject(serializedAI, "behaviorProfile", behaviorProfile);
        SetObject(serializedAI, "target", null);
        SetFloat(serializedAI, "detectionRange", DetectionRange);
        SetFloat(serializedAI, "loseTargetRange", LoseTargetRange);
        SetFloat(serializedAI, "moveStopDistance", Mathf.Min(DefaultMoveStopDistance, Mathf.Max(0f, attackRange - 0.2f)));
        SetFloat(serializedAI, "attackEnterRange", attackRange);
        SetFloat(serializedAI, "attackExitRange", attackRange + AttackExitMargin);
        SetFloat(serializedAI, "returnArriveDistance", ReturnArriveDistance);
        SetBool(serializedAI, "findPlayerByTag", true);
        SetString(serializedAI, "playerTag", "Player");
        SetBool(serializedAI, "useDensityApproachSteering", UsesDensityApproachByDefault(root.name));
        SetBool(serializedAI, "logStateChanges", false);
        serializedAI.ApplyModifiedPropertiesWithoutUndo();

        if (defenseController != null)
        {
            SerializedObject serializedDefense = new SerializedObject(defenseController);
            SetObject(serializedDefense, "behaviorProfile", behaviorProfile);
            SetObject(serializedDefense, "animationBridge", animationBridge);
            serializedDefense.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(defenseController);
        }

        EditorUtility.SetDirty(enemyController);
        EditorUtility.SetDirty(motor);
        EditorUtility.SetDirty(movementReaction);
        EditorUtility.SetDirty(locomotionAnimator);
        EditorUtility.SetDirty(movement);
        EditorUtility.SetDirty(meleeAttack);
        EditorUtility.SetDirty(aiController);
    }

    private static void ConfigureElementalStatusAssembly(GameObject root, string path)
    {
        CombatHealth health = RequireComponent<CombatHealth>(root, path);
        EnemyRank enemyRank = RequireComponent<EnemyRank>(root, path);
        EnemyMovement movement = RequireComponent<EnemyMovement>(root, path);
        EnemyMeleeAttackController meleeAttack = RequireComponent<EnemyMeleeAttackController>(root, path);
        ElementalStatusController statusController = EnsureComponent<ElementalStatusController>(root);
        MeleeElementStatusAuraController auraController = EnsureElementStatusAuraOwner(root);

        SerializedObject serializedStatus = new SerializedObject(statusController);
        SetObject(serializedStatus, "combatHealth", health);
        SetObject(serializedStatus, "enemyRank", enemyRank);
        SetObject(serializedStatus, "enemyMovement", movement);
        SetObject(serializedStatus, "enemyAttackController", meleeAttack);
        SetObject(serializedStatus, "auraController", auraController);
        serializedStatus.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(statusController);
    }

    private static MeleeElementStatusAuraController EnsureElementStatusAuraOwner(GameObject root)
    {
        MeleeElementStatusAuraController existing =
            root.GetComponent<MeleeElementStatusAuraController>();
        return existing != null
            ? existing
            : root.AddComponent<MeleeElementStatusAuraController>();
    }

    private static void ValidateElementalStatusAssembly(GameObject root, string path)
    {
        CombatHealth health = RequireComponent<CombatHealth>(root, path);
        EnemyRank enemyRank = RequireComponent<EnemyRank>(root, path);
        EnemyMovement movement = RequireComponent<EnemyMovement>(root, path);
        EnemyMeleeAttackController meleeAttack = RequireComponent<EnemyMeleeAttackController>(root, path);
        ElementalStatusController statusController = RequireComponent<ElementalStatusController>(root, path);
        MeleeElementStatusAuraController auraController =
            root.GetComponent<MeleeElementStatusAuraController>();
        if (auraController == null)
            throw new System.InvalidOperationException(path + " missing elemental status aura controller");
        if (root.GetComponentInChildren<MeleeElementStatusAuraPresentation>(true) != null)
            throw new System.InvalidOperationException(path + " contains resident elemental status aura presentation");

        SerializedObject serializedStatus = new SerializedObject(statusController);
        RequireObjectReference(serializedStatus, "combatHealth", health, path);
        RequireObjectReference(serializedStatus, "enemyRank", enemyRank, path);
        RequireObjectReference(serializedStatus, "enemyMovement", movement, path);
        RequireObjectReference(serializedStatus, "enemyAttackController", meleeAttack, path);
        RequireObjectReference(serializedStatus, "auraController", auraController, path);

        if (HasMissingScripts(root))
            throw new System.InvalidOperationException(path + " Missing Script exists");
    }

    private static EnemyBehaviorProfile LoadBehaviorProfile(string rootName)
    {
        string id = rootName.StartsWith("PF_StageMonster_", System.StringComparison.Ordinal)
            ? rootName.Substring("PF_StageMonster_".Length)
            : "Default";
        if (id == "FishmanTest" || string.IsNullOrWhiteSpace(id))
            id = "Default";
        return AssetDatabase.LoadAssetAtPath<EnemyBehaviorProfile>(BehaviorProfileFolder + "/EBP_" + id + ".asset");
    }

    private static void FormalizeAll()
    {
        List<string> failures = new List<string>();
        int savedCount = 0;

        for (int i = 0; i < PrefabPaths.Length; i++)
        {
            string path = PrefabPaths[i];
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                failures.Add(path + " (prefab missing)");
                continue;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                ConfigureEnemyPrefab(root);
                ValidatePrefabRoot(root, path);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                savedCount++;
            }
            catch (System.Exception exception)
            {
                failures.Add(path + " (" + exception.Message + ")");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        FormalizeSceneSettings();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        for (int i = 0; i < PrefabPaths.Length; i++)
        {
            string path = PrefabPaths[i];
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                continue;

            try
            {
                ValidatePrefabRoot(prefab, path);
            }
            catch (System.Exception exception)
            {
                failures.Add(path + " (saved validation: " + exception.Message + ")");
            }
        }

        if (failures.Count > 0)
            throw new System.InvalidOperationException("Enemy state AI prefab formalization failed:\n" + string.Join("\n", failures));

        Debug.Log("[EnemyStatePatternPrefabFormalizer] Formalized and validated prefabs=" + savedCount);
    }

    private static void FormalizeSceneSettings()
    {
        Scene hideoutScene = OpenSceneForEdit(HideoutScenePath, out bool closeHideoutScene);
        try
        {
            HideoutMonsterSpawnDebugController[] spawnPoints = FindComponentsInScene<HideoutMonsterSpawnDebugController>(hideoutScene);
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                SerializedObject serializedSpawnPoint = new SerializedObject(spawnPoints[i]);
                SerializedProperty spawnConfig = serializedSpawnPoint.FindProperty("spawnConfig");
                if (spawnConfig != null)
                    MurlocSpawnPackConfigBuilder.ConfigureSharedSpawnConfig(spawnConfig);
                SerializedProperty detection = spawnConfig != null ? spawnConfig.FindPropertyRelative("detectionRange") : null;
                SerializedProperty stopDistance = spawnConfig != null ? spawnConfig.FindPropertyRelative("stopDistance") : null;
                if (detection == null || stopDistance == null)
                    continue;

                detection.floatValue = DetectionRange;
                stopDistance.floatValue = DefaultMoveStopDistance;
                serializedSpawnPoint.ApplyModifiedPropertiesWithoutUndo();
                ValidateSharedSpawnConfig(
                    new SerializedObject(spawnPoints[i]).FindProperty("spawnConfig"),
                    HideoutScenePath);
                EditorUtility.SetDirty(spawnPoints[i]);
            }

            if (spawnPoints.Length > 0)
            {
                EditorSceneManager.MarkSceneDirty(hideoutScene);
                EditorSceneManager.SaveScene(hideoutScene);
            }
        }
        finally
        {
            if (closeHideoutScene)
                EditorSceneManager.CloseScene(hideoutScene, true);
        }
    }

    private static void ValidateSharedSpawnConfig(SerializedProperty spawnConfig, string ownerPath)
    {
        if (spawnConfig == null)
            throw new System.InvalidOperationException(ownerPath + " spawnConfig is missing");

        SerializedProperty enemyPrefabs = spawnConfig.FindPropertyRelative("enemyPrefabs");
        if (enemyPrefabs == null || !enemyPrefabs.isArray || enemyPrefabs.arraySize != 6)
            throw new System.InvalidOperationException(ownerPath + " expected six core murloc prefabs");

        for (int i = 0; i < enemyPrefabs.arraySize; i++)
        {
            SerializedProperty prefab = enemyPrefabs.GetArrayElementAtIndex(i).FindPropertyRelative("prefab");
            if (prefab == null || prefab.objectReferenceValue == null)
                throw new System.InvalidOperationException(ownerPath + " murloc prefab entry is missing: " + i);
        }

        string[] expectedPackIds =
        {
            "Murloc_BasicMob",
            "Murloc_ScoutPack",
            "Murloc_ShieldLine",
            "Murloc_BruteRaid",
            "Murloc_WarlordEscort"
        };
        SerializedProperty packs = spawnConfig.FindPropertyRelative("spawnPacks");
        if (packs == null || !packs.isArray || packs.arraySize != expectedPackIds.Length)
            throw new System.InvalidOperationException(ownerPath + " expected five murloc spawn packs");

        for (int packIndex = 0; packIndex < packs.arraySize; packIndex++)
        {
            SerializedProperty pack = packs.GetArrayElementAtIndex(packIndex);
            string packId = pack.FindPropertyRelative("packId")?.stringValue;
            if (packId != expectedPackIds[packIndex])
                throw new System.InvalidOperationException(ownerPath + " spawn pack order mismatch: " + packIndex);

            SerializedProperty entries = pack.FindPropertyRelative("entries");
            if (entries == null || !entries.isArray || entries.arraySize <= 0)
                throw new System.InvalidOperationException(ownerPath + " spawn pack is empty: " + packId);

            for (int entryIndex = 0; entryIndex < entries.arraySize; entryIndex++)
            {
                SerializedProperty prefab = entries.GetArrayElementAtIndex(entryIndex).FindPropertyRelative("prefab");
                if (prefab == null || prefab.objectReferenceValue == null)
                    throw new System.InvalidOperationException(ownerPath + " spawn pack prefab is missing: " + packId);
            }
        }

        if (spawnConfig.FindPropertyRelative("respawnWaveCount")?.intValue != 6
            || !Mathf.Approximately(spawnConfig.FindPropertyRelative("respawnWaveInterval")?.floatValue ?? 0f, 5f))
        {
            throw new System.InvalidOperationException(ownerPath + " respawn sequence contract mismatch");
        }
    }

    private static Scene OpenSceneForEdit(string path, out bool closeWhenFinished)
    {
        Scene loadedScene = SceneManager.GetSceneByPath(path);
        if (loadedScene.IsValid() && loadedScene.isLoaded)
        {
            if (loadedScene.isDirty)
                throw new System.InvalidOperationException("Scene has unsaved changes: " + path);

            closeWhenFinished = false;
            return loadedScene;
        }

        closeWhenFinished = true;
        return EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
    }

    private static T FindComponentInScene<T>(Scene scene) where T : Component
    {
        T[] components = FindComponentsInScene<T>(scene);
        return components.Length > 0 ? components[0] : null;
    }

    private static T[] FindComponentsInScene<T>(Scene scene) where T : Component
    {
        List<T> results = new List<T>();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            results.AddRange(roots[i].GetComponentsInChildren<T>(true));
        return results.ToArray();
    }

    private static void ValidatePrefabRoot(GameObject root, string path)
    {
        RequireComponent<CombatHealth>(root, path);
        RequireComponent<EnemyController>(root, path);
        EnemyMotor motor = RequireComponent<EnemyMotor>(root, path);
        EnemyMovementReaction movementReaction = RequireComponent<EnemyMovementReaction>(root, path);
        RequireComponent<EnemyLocomotionAnimator>(root, path);
        EnemyMovement movement = RequireComponent<EnemyMovement>(root, path);
        EnemyMeleeAttackController meleeAttack = RequireComponent<EnemyMeleeAttackController>(root, path);
        EnemySensor sensor = RequireComponent<EnemySensor>(root, path);
        RequireComponent<EnemyCrowdAgent>(root, path);
        EnemyAIController aiController = RequireComponent<EnemyAIController>(root, path);
        RequireComponent<EnemyLootDropper>(root, path);
        ValidateElementalStatusAssembly(root, path);

        if (HasMissingScripts(root))
            throw new System.InvalidOperationException("Missing Script exists");

        SerializedObject serializedAttack = new SerializedObject(meleeAttack);
        RequireObjectReference(serializedAttack, "movement", movement, path);
        RequireObjectReference(serializedAttack, "movementReaction", movementReaction, path);
        SerializedProperty attackPoint = serializedAttack.FindProperty("attackPoint");
        if (attackPoint == null || attackPoint.objectReferenceValue == null)
            throw new System.InvalidOperationException(path + " attackPoint is missing");
        if (attackPoint.objectReferenceValue == root.transform)
            throw new System.InvalidOperationException(path + " attackPoint must be a child transform");

        SerializedObject serializedAI = new SerializedObject(aiController);
        RequireObjectReference(serializedAI, "health", root.GetComponent<CombatHealth>(), path);
        RequireObjectReference(serializedAI, "movement", movement, path);
        RequireObjectReference(serializedAI, "movementReaction", movementReaction, path);
        RequireObjectReference(serializedAI, "sensor", sensor, path);
        RequireObjectReference(serializedAI, "meleeAttack", meleeAttack, path);
        RequireFloat(serializedAI, "detectionRange", DetectionRange, path);
        RequireFloat(serializedAI, "loseTargetRange", LoseTargetRange, path);
        RequireFloat(serializedAI, "returnArriveDistance", ReturnArriveDistance, path);

        SerializedObject serializedReaction = new SerializedObject(movementReaction);
        RequireObjectReference(serializedReaction, "motor", motor, path);
    }

    private static T EnsureComponent<T>(GameObject root) where T : Component
    {
        T component = root.GetComponent<T>();
        return component != null ? component : root.AddComponent<T>();
    }

    private static Transform EnsureAttackPoint(Transform root)
    {
        Transform attackPoint = root.Find("AttackPoint");
        if (attackPoint == null)
        {
            GameObject attackPointObject = new GameObject("AttackPoint");
            attackPoint = attackPointObject.transform;
            attackPoint.SetParent(root, false);
            attackPoint.localPosition = new Vector3(0f, 1f, 1.45f);
            attackPoint.localRotation = Quaternion.identity;
            attackPoint.localScale = Vector3.one;
        }

        return attackPoint;
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

    private static T RequireComponent<T>(GameObject root, string path) where T : Component
    {
        T component = root.GetComponent<T>();
        if (component == null)
            throw new System.InvalidOperationException(path + " missing " + typeof(T).Name);
        return component;
    }

    private static void RequireObjectReference(SerializedObject serialized, string propertyName, Object expected, string path)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null || property.objectReferenceValue != expected)
            throw new System.InvalidOperationException(path + " invalid reference " + propertyName);
    }

    private static void RequireFloat(SerializedObject serialized, string propertyName, float expected, string path)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null || !Mathf.Approximately(property.floatValue, expected))
            throw new System.InvalidOperationException(path + " invalid value " + propertyName);
    }

    private static void SetObject(SerializedObject serialized, string propertyName, Object value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
            property.objectReferenceValue = value;
    }

    private static void SetFloat(SerializedObject serialized, string propertyName, float value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
            property.floatValue = value;
    }

    private static void SetBool(SerializedObject serialized, string propertyName, bool value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
            property.boolValue = value;
    }

    private static void SetString(SerializedObject serialized, string propertyName, string value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
            property.stringValue = value;
    }
}
