using UnityEngine;
using UnityEngine.Rendering;

public static class EnemyRuntimeFactory // 공용 몬스터 생성 경로
{
    private const int CrowdSpawnAttempts = 12;
    private const float MinimumCrowdSpawnSearchRadius = 1.5f;

    public static GameObject Create(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        Transform parent,
        string instanceName,
        Transform player,
        float detectionRange,
        float stopDistance,
        DropTable dropTable,
        PlayerInventory targetInventory,
        PickupGradeVfxSet pickupGradeVfxSet,
        GameObject hitVfxPrefab,
        GameObject deathVfxPrefab,
        bool attachRunFallGuard)
    {
        if (prefab == null)
            return null;

        float spawnBodyRadius = EnemyCrowdAgent.EstimateBodyRadius(prefab);
        float spawnSearchRadius = Mathf.Max(MinimumCrowdSpawnSearchRadius, spawnBodyRadius * 4f);
        if (EnemyCrowdService.TryFindSpawnPosition(
                position,
                spawnSearchRadius,
                spawnBodyRadius,
                CrowdSpawnAttempts,
                out Vector3 resolvedSpawnPosition))
        {
            position = resolvedSpawnPosition; // 보행 가능·최소 겹침 생성 위치
        }

        GameObject monster = Object.Instantiate(prefab, position, rotation, parent);
        monster.name = instanceName;
        SetLayerRecursively(monster, LayerMask.NameToLayer("Enemy"));
        ConfigureRuntimeShadows(monster);

        if (attachRunFallGuard)
        {
            RunWalkableContext.TryAttachFallGuard(
                monster,
                RunFallGuardMode.KillOnExit);
        }

        Rigidbody body = monster.GetComponent<Rigidbody>();
        if (body == null)
            body = monster.AddComponent<Rigidbody>();
        body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        CombatHealth health = monster.GetComponent<CombatHealth>();
        if (health == null)
            health = monster.AddComponent<CombatHealth>();
        CombatTarget.EnsureConfigured(monster, CombatTeam.Enemy);

        EnsureComponent<EnemyRank>(monster);
        EnsureComponent<EnemyTargetHpReporter>(monster);
        EnsureComponent<EnemyController>(monster);
        EnsureComponent<HitFlashFeedback>(monster);

        CombatVfx vfxController = EnsureComponent<CombatVfx>(monster);
        if (hitVfxPrefab != null || deathVfxPrefab != null)
            vfxController.Configure(hitVfxPrefab, deathVfxPrefab);

        EnsureComponent<EnemyMovement>(monster);
        EnsureComponent<EnemyMeleeAttackController>(monster);
        EnsureComponent<EnemyCrowdAgent>(monster);
        EnsureComponent<ElementalStatusController>(monster);

        EnemyAIController aiController = EnsureComponent<EnemyAIController>(monster);
        aiController.Configure(player, position, detectionRange, stopDistance);

        EnemyLootDropper dropper = EnsureComponent<EnemyLootDropper>(monster);
        dropper.Configure(dropTable, targetInventory, player, pickupGradeVfxSet);
        return monster;
    }

    private static T EnsureComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    private static void ConfigureRuntimeShadows(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;

            renderers[i].shadowCastingMode = ShadowCastingMode.On;
            renderers[i].receiveShadows = true;
        }
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        if (target == null || layer < 0)
            return;

        Transform[] children = target.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
            children[i].gameObject.layer = layer;
    }
}
