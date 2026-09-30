using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class CombatSpatialIndexVerifier
{
    private const string LogPath = "Logs/CombatSpatialIndexVerifier.log";

    private sealed class TargetRecord
    {
        public readonly int Id;
        public float X;
        public float Z;
        public float Radius;

        public TargetRecord(int id, float x, float z, float radius)
        {
            Id = id;
            X = x;
            Z = z;
            Radius = radius;
        }
    }

    [MenuItem("JC 툴/검증/전투/Spatial Index 독립 검증")]
    public static void RunFromMenu()
    {
        RunOrThrow();
        Debug.Log("[CombatSpatialIndexVerifier] 독립 Spatial Index 검증 통과");
    }

    public static void RunFromCommandLine()
    {
        RunOrThrow();
    }

    private static void RunOrThrow()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath) ?? "Logs");
        List<string> report = new List<string>
        {
            "CombatSpatialIndex 독립 검증",
            "UTC=" + DateTime.UtcNow.ToString("O")
        };

        try
        {
            RunBoundaryAndLargeTargetTests(report);
            RunMoveAndRemovalTests(report);
            RunRandomParityTests(report);
            RunAllocationTest(report);
            RunRegistryFormalValidationTests(report);
            RunRegistryStressValidationTests(report);
            RunRigidbodyMovePositionValidationTest(report);
            RunAmortizedMaintenanceValidationTest(report);
            report.Add("RESULT=PASS");
            File.WriteAllLines(LogPath, report);
            Debug.Log("[CombatSpatialIndexVerifier] PASS\n" + string.Join("\n", report));
        }
        catch (Exception exception)
        {
            report.Add("RESULT=FAIL");
            report.Add(exception.ToString());
            File.WriteAllLines(LogPath, report);
            Debug.LogException(exception);
            throw;
        }
    }

    private static void RunBoundaryAndLargeTargetTests(List<string> report)
    {
        CombatSpatialIndexCore<TargetRecord> grid = new CombatSpatialIndexCore<TargetRecord>(4f);
        List<TargetRecord> results = new List<TargetRecord>(16);
        TargetRecord positiveBoundary = new TargetRecord(1, 4.2f, 0f, 0.5f);
        TargetRecord negativeBoundary = new TargetRecord(2, -4.2f, 0f, 0.5f);
        TargetRecord large = new TargetRecord(3, 12f, 0f, 6f);

        AssertTrue(grid.RegisterOrUpdate(positiveBoundary.Id, positiveBoundary, positiveBoundary.X, positiveBoundary.Z, positiveBoundary.Radius), "첫 등록 실패");
        AssertTrue(grid.RegisterOrUpdate(negativeBoundary.Id, negativeBoundary, negativeBoundary.X, negativeBoundary.Z, negativeBoundary.Radius), "음수 셀 등록 실패");
        AssertTrue(grid.RegisterOrUpdate(large.Id, large, large.X, large.Z, large.Radius), "대형 대상 등록 실패");
        AssertFalse(grid.RegisterOrUpdate(large.Id, large, large.X, large.Z, large.Radius), "중복 등록이 새 등록으로 처리됨");
        AssertEqual(3, grid.Count, "중복 등록 뒤 Count 불일치");

        grid.CollectPotential(3.8f, 0f, 0f, results);
        AssertContainsExactly(results, positiveBoundary);
        grid.CollectPotential(-3.8f, 0f, 0f, results);
        AssertContainsExactly(results, negativeBoundary);
        grid.CollectPotential(18f, 0f, 0.1f, results);
        AssertContainsExactly(results, large); // 반경 6 대상의 가장자리 교차
        grid.CollectPotential(12f, 0f, 10f, results);
        AssertEqual(2, new HashSet<TargetRecord>(results).Count, "다중 셀 대상 중복 반환");
        AssertEqual(2, results.Count, "다중 셀 Query 결과 수 불일치");
        AssertIntegrity(grid);
        report.Add("boundary_large_dedup=PASS");
    }

    private static void RunMoveAndRemovalTests(List<string> report)
    {
        CombatSpatialIndexCore<TargetRecord> grid = new CombatSpatialIndexCore<TargetRecord>(4f);
        List<TargetRecord> results = new List<TargetRecord>(8);
        TargetRecord target = new TargetRecord(10, 0f, 0f, 0.5f);
        TargetRecord latest = new TargetRecord(11, 0.25f, 0f, 0.5f);
        grid.RegisterOrUpdate(target.Id, target, target.X, target.Z, target.Radius);
        grid.RegisterOrUpdate(latest.Id, latest, latest.X, latest.Z, latest.Radius);
        grid.CollectPotential(0f, 0f, 2f, results);
        AssertEqual(latest, results[0], "기존 Registry 최신 등록 우선 순서 불일치");

        target.X = 40f;
        target.Z = -20f;
        grid.RegisterOrUpdate(target.Id, target, target.X, target.Z, target.Radius);
        grid.CollectPotential(0f, 0f, 2f, results);
        AssertFalse(results.Contains(target), "이동 뒤 이전 셀 유령 대상 잔존");
        grid.CollectPotential(40f, -20f, 1f, results);
        AssertContainsExactly(results, target);
        AssertTrue(grid.Remove(target.Id), "등록 대상 해제 실패");
        AssertFalse(grid.Remove(target.Id), "중복 해제가 성공으로 처리됨");
        grid.CollectPotential(40f, -20f, 1f, results);
        AssertEqual(0, results.Count, "해제 대상 Query 잔존");
        AssertIntegrity(grid);
        report.Add("move_remove_order=PASS");
    }

    private static void RunRandomParityTests(List<string> report)
    {
        const int targetCount = 512;
        const int queryCount = 800;
        System.Random random = new System.Random(20260720);
        CombatSpatialIndexCore<TargetRecord> grid = new CombatSpatialIndexCore<TargetRecord>(4f);
        List<TargetRecord> records = new List<TargetRecord>(targetCount);
        List<TargetRecord> spatialResults = new List<TargetRecord>(targetCount);
        HashSet<int> spatialIds = new HashSet<int>();
        HashSet<int> bruteForceIds = new HashSet<int>();

        for (int i = 0; i < targetCount; i++)
        {
            float x = NextRange(random, -120f, 120f);
            float z = NextRange(random, -120f, 120f);
            float radius = i % 37 == 0 ? NextRange(random, 4f, 9f) : NextRange(random, 0.05f, 1.5f);
            TargetRecord record = new TargetRecord(i + 1000, x, z, radius);
            records.Add(record);
            grid.RegisterOrUpdate(record.Id, record, x, z, radius);
        }

        for (int queryIndex = 0; queryIndex < queryCount; queryIndex++)
        {
            if (queryIndex > 0 && queryIndex % 40 == 0)
            {
                int moveIndex = random.Next(records.Count);
                TargetRecord moving = records[moveIndex];
                moving.X = NextRange(random, -120f, 120f);
                moving.Z = NextRange(random, -120f, 120f);
                grid.RegisterOrUpdate(moving.Id, moving, moving.X, moving.Z, moving.Radius);
            }

            float originX = NextRange(random, -130f, 130f);
            float originZ = NextRange(random, -130f, 130f);
            float queryRadius = NextRange(random, 0f, 12f);
            grid.CollectPotential(originX, originZ, queryRadius, spatialResults);
            spatialIds.Clear();
            bruteForceIds.Clear();

            for (int i = 0; i < spatialResults.Count; i++)
            {
                if (!spatialIds.Add(spatialResults[i].Id))
                    throw new InvalidOperationException("Spatial Query 중복 TargetId: " + spatialResults[i].Id);
            }

            for (int i = 0; i < records.Count; i++)
            {
                TargetRecord candidate = records[i];
                float deltaX = candidate.X - originX;
                float deltaZ = candidate.Z - originZ;
                float combinedRadius = queryRadius + candidate.Radius;
                if (deltaX * deltaX + deltaZ * deltaZ <= combinedRadius * combinedRadius)
                    bruteForceIds.Add(candidate.Id);
            }

            if (!spatialIds.SetEquals(bruteForceIds))
                throw new InvalidOperationException("무작위 Query 결과 불일치: query=" + queryIndex);
        }

        AssertIntegrity(grid);
        report.Add("random_parity_targets=" + targetCount);
        report.Add("random_parity_queries=" + queryCount);
        report.Add("random_parity=PASS");
    }

    private static void RunAllocationTest(List<string> report)
    {
        CombatSpatialIndexCore<TargetRecord> grid = new CombatSpatialIndexCore<TargetRecord>(4f);
        List<TargetRecord> results = new List<TargetRecord>(512);
        for (int i = 0; i < 512; i++)
        {
            TargetRecord record = new TargetRecord(i, i % 32, i / 32, 0.5f);
            grid.RegisterOrUpdate(record.Id, record, record.X, record.Z, record.Radius);
        }

        for (int i = 0; i < 20; i++)
            grid.CollectPotential(16f, 8f, 12f, results); // 내부 List와 Sort 준비

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            grid.CollectPotential(16f, 8f, 12f, results);
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        if (allocatedBytes != 0L)
            throw new InvalidOperationException("예열 뒤 Query 할당 발생: " + allocatedBytes + " bytes / 1000 queries");

        report.Add("warmed_query_alloc_bytes_1000=" + allocatedBytes);
        report.Add("allocation=PASS");
    }

    private static void RunRegistryFormalValidationTests(List<string> report)
    {
        const float testOriginX = 10000f;
        List<CombatTarget> results = new List<CombatTarget>(8);
        List<GameObject> testObjects = new List<GameObject>(2);
        List<CombatTarget> activeOrder = new List<CombatTarget>(2);

        try
        {
            CombatTarget first = CreateRegistryTestTarget(
                "SpatialValidation_First",
                new Vector3(testOriginX, 0f, 0f),
                0.5f,
                testObjects);
            CombatTarget large = CreateRegistryTestTarget(
                "SpatialValidation_Large",
                new Vector3(testOriginX + 8f, 0f, 0f),
                6f,
                testObjects);

            CombatTargetRegistry.Register(first);
            CombatTargetRegistry.Register(large);
            activeOrder.Add(first);
            activeOrder.Add(large);

            AssertRegistryMatchesBruteForce(
                activeOrder,
                new Vector3(testOriginX + 1.5f, 0f, 0f),
                1f,
                results,
                "boundary and large target");
            AssertTrue(results.Contains(first), "Registry boundary target missing.");
            AssertTrue(results.Contains(large), "Registry large target overlap missing.");

            first.transform.position = new Vector3(testOriginX + 40f, 0f, -20f);
            CombatTargetRegistry.NotifySpatialChanged(first.transform);
            AssertRegistryMatchesBruteForce(
                activeOrder,
                new Vector3(testOriginX, 0f, 0f),
                2f,
                results,
                "old position after movement");
            AssertFalse(results.Contains(first), "Moved target remained at its old Registry position.");
            AssertRegistryMatchesBruteForce(activeOrder, first.transform.position, 1f, results, "new position after movement");
            AssertTrue(results.Contains(first), "Moved target missing from its new Registry position.");

            large.gameObject.SetActive(false);
            CombatTargetRegistry.Unregister(large);
            activeOrder.Remove(large);
            AssertRegistryMatchesBruteForce(
                activeOrder,
                new Vector3(testOriginX + 8f, 0f, 0f),
                1f,
                results,
                "inactive pooled target");
            AssertFalse(results.Contains(large), "Inactive target remained queryable.");

            large.gameObject.SetActive(true);
            CombatTargetRegistry.Register(large);
            activeOrder.Add(large);
            AssertRegistryMatchesBruteForce(
                activeOrder,
                new Vector3(testOriginX + 8f, 0f, 0f),
                1f,
                results,
                "re-enabled pooled target");
            AssertTrue(results.Contains(large), "Re-enabled pooled target was not queryable.");

            CombatHealth firstHealth = first.DamageReceiver;
            firstHealth.SetMaxHp(1f, true);
            firstHealth.TakeDamage(new DamageInfo(1f, first.WorldCenter, triggersOnHitEffects: false));
            AssertRegistryMatchesBruteForce(activeOrder, first.transform.position, 1f, results, "dead target");
            AssertFalse(results.Contains(first), "Dead target remained queryable.");

            CombatTargetRegistry.Unregister(large);
            activeOrder.Remove(large);
            AssertRegistryMatchesBruteForce(
                activeOrder,
                new Vector3(testOriginX + 8f, 0f, 0f),
                1f,
                results,
                "unregistered target");
            AssertFalse(results.Contains(large), "Unregistered target remained queryable.");

            AssertTrue(CombatTargetRegistry.ValidateSpatialIndexIntegrity(out string integrityError),
                "Formal Registry Spatial index integrity failed: " + integrityError);

            report.Add("registry_formal_queries=7");
            report.Add("registry_formal_boundary_move_disable_pool_death=PASS");
        }
        finally
        {
            for (int i = 0; i < testObjects.Count; i++)
            {
                GameObject testObject = testObjects[i];
                if (testObject == null)
                    continue;

                CombatTarget target = testObject.GetComponent<CombatTarget>();
                if (target != null)
                    CombatTargetRegistry.Unregister(target);
                UnityEngine.Object.DestroyImmediate(testObject);
            }
        }
    }

    private static void RunRegistryStressValidationTests(List<string> report)
    {
        const int targetCount = 200;
        const int queryCount = 600;
        const float originX = 20000f;
        System.Random random = new System.Random(20260721);
        List<GameObject> testObjects = new List<GameObject>(targetCount);
        List<CombatTarget> activeOrder = new List<CombatTarget>(targetCount);
        List<CombatTarget> results = new List<CombatTarget>(targetCount);

        try
        {
            for (int i = 0; i < targetCount; i++)
            {
                CombatTarget target = CreateRegistryTestTarget(
                    "SpatialStress_" + i,
                    new Vector3(
                        originX + NextRange(random, -80f, 80f),
                        0f,
                        NextRange(random, -80f, 80f)),
                    i % 31 == 0 ? NextRange(random, 4f, 8f) : NextRange(random, 0.2f, 1.2f),
                    testObjects);
                CombatTargetRegistry.Register(target);
                activeOrder.Add(target);
            }

            for (int queryIndex = 0; queryIndex < queryCount; queryIndex++)
            {
                for (int moveIndex = 0; moveIndex < 8; moveIndex++)
                {
                    CombatTarget moving = activeOrder[random.Next(activeOrder.Count)];
                    moving.transform.position = new Vector3(
                        originX + NextRange(random, -90f, 90f),
                        0f,
                        NextRange(random, -90f, 90f));
                    CombatTargetRegistry.NotifySpatialChanged(moving.transform);
                }

                if (queryIndex > 0 && queryIndex % 30 == 0)
                {
                    int poolIndex = random.Next(activeOrder.Count);
                    CombatTarget pooled = activeOrder[poolIndex];
                    activeOrder.RemoveAt(poolIndex);
                    pooled.gameObject.SetActive(false);
                    CombatTargetRegistry.Unregister(pooled);
                    pooled.DamageReceiver.ResetHealth();
                    pooled.gameObject.SetActive(true);
                    CombatTargetRegistry.Register(pooled);
                    activeOrder.Add(pooled);
                }

                if (queryIndex > 0 && queryIndex % 45 == 0)
                {
                    CombatTarget dying = activeOrder[random.Next(activeOrder.Count)];
                    if (dying.IsAlive)
                    {
                        dying.DamageReceiver.SetMaxHp(1f, true);
                        dying.DamageReceiver.TakeDamage(
                            new DamageInfo(1f, dying.WorldCenter, triggersOnHitEffects: false));
                    }
                }

                Vector3 queryCenter = new Vector3(
                    originX + NextRange(random, -95f, 95f),
                    0f,
                    NextRange(random, -95f, 95f));
                float queryRadius = NextRange(random, 0f, 12f);
                AssertRegistryMatchesBruteForce(
                    activeOrder,
                    queryCenter,
                    queryRadius,
                    results,
                    "stress query " + queryIndex);
            }

            AssertTrue(CombatTargetRegistry.ValidateSpatialIndexIntegrity(out string integrityError),
                "Stress Registry Spatial index integrity failed: " + integrityError);
            report.Add("registry_stress_targets=" + targetCount);
            report.Add("registry_stress_queries=" + queryCount);
            report.Add("registry_stress_move_pool_death=PASS");
        }
        finally
        {
            for (int i = 0; i < testObjects.Count; i++)
            {
                GameObject testObject = testObjects[i];
                if (testObject == null)
                    continue;

                CombatTarget target = testObject.GetComponent<CombatTarget>();
                if (target != null)
                    CombatTargetRegistry.Unregister(target);
                UnityEngine.Object.DestroyImmediate(testObject);
            }
        }
    }

    private static void RunRigidbodyMovePositionValidationTest(List<string> report)
    {
        const float testOriginX = 30000f;
        List<GameObject> testObjects = new List<GameObject>(1);
        List<CombatTarget> results = new List<CombatTarget>(4);
        SimulationMode previousSimulationMode = Physics.simulationMode;

        try
        {
            Physics.simulationMode = SimulationMode.Script;
            Vector3 start = new Vector3(testOriginX, 0f, 0f);
            Vector3 destination = new Vector3(testOriginX + 12f, 0f, 0f);
            GameObject testObject = new GameObject("SpatialValidation_RigidbodyMovePosition");
            testObject.hideFlags = HideFlags.HideAndDontSave;
            testObject.SetActive(false);
            testObject.transform.position = start;
            CombatHealth health = testObject.AddComponent<CombatHealth>();
            SerializedObject serializedHealth = new SerializedObject(health);
            serializedHealth.FindProperty("showDamageNumbers").boolValue = false;
            serializedHealth.ApplyModifiedPropertiesWithoutUndo();
            testObject.AddComponent<CombatAffiliation>().Configure(CombatTeam.Enemy);
            Rigidbody body = testObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.None;
            CombatTarget target = testObject.AddComponent<CombatTarget>();
            target.ConfigureVolume(Vector3.zero, 0.5f, 2f);
            testObjects.Add(testObject);
            testObject.SetActive(true);
            CombatTargetRegistry.Register(target); // EditMode는 OnEnable 자동 등록에 의존하지 않음
            body.position = start;
            Physics.SyncTransforms();
            CombatTargetRegistry.NotifySpatialChanged(target.transform);

            CombatTargetRegistry.CollectPotentialTargets(start, 1f, results); // 현재 프레임 전체 동기화 소모
            if (!results.Contains(target))
            {
                throw new InvalidOperationException(
                    "Rigidbody 이동 검증 시작 위치 등록 실패"
                    + " transform=" + target.transform.position
                    + " body=" + body.position
                    + " center=" + target.WorldCenter
                    + " alive=" + target.IsAlive
                    + " registered=" + CombatTargetRegistry.RegisteredCount
                    + " results=" + results.Count);
            }

            body.MovePosition(destination);
            CombatTargetRegistry.NotifySpatialMovement(target.transform, destination); // EnemyMotor와 동일한 swept 통지
            Physics.Simulate(Time.fixedDeltaTime);

            CombatTargetRegistry.CollectPotentialTargets(destination, 1f, results);
            AssertTrue(results.Contains(target), "MovePosition 물리 반영 뒤 같은 프레임 최종 위치가 Spatial에 반영되지 않았습니다.");
            CombatTargetRegistry.CollectPotentialTargets(start, 1f, results);
            AssertFalse(results.Contains(target), "MovePosition 물리 반영 뒤 이전 Spatial 셀에 대상이 남았습니다.");
            report.Add("rigidbody_move_position_same_frame=PASS");
        }
        finally
        {
            Physics.simulationMode = previousSimulationMode;
            for (int i = 0; i < testObjects.Count; i++)
            {
                GameObject testObject = testObjects[i];
                if (testObject == null)
                    continue;

                CombatTarget target = testObject.GetComponent<CombatTarget>();
                if (target != null)
                    CombatTargetRegistry.Unregister(target);
                UnityEngine.Object.DestroyImmediate(testObject);
            }
        }
    }

    private static void RunAmortizedMaintenanceValidationTest(List<string> report)
    {
        const float testOriginX = 32000f;
        List<GameObject> testObjects = new List<GameObject>(1);
        List<CombatTarget> results = new List<CombatTarget>(4);

        try
        {
            Vector3 start = new Vector3(testOriginX, 0f, 0f);
            Vector3 destination = new Vector3(testOriginX + 12f, 0f, 0f);
            GameObject testObject = new GameObject("SpatialValidation_AmortizedMaintenance");
            testObject.hideFlags = HideFlags.HideAndDontSave;
            testObject.SetActive(false);
            testObject.transform.position = start;
            CombatHealth health = testObject.AddComponent<CombatHealth>();
            SerializedObject serializedHealth = new SerializedObject(health);
            serializedHealth.FindProperty("showDamageNumbers").boolValue = false;
            serializedHealth.ApplyModifiedPropertiesWithoutUndo();
            testObject.AddComponent<CombatAffiliation>().Configure(CombatTeam.Enemy);
            CombatTarget target = testObject.AddComponent<CombatTarget>();
            target.ConfigureVolume(Vector3.zero, 0.5f, 2f);
            testObjects.Add(testObject);
            testObject.SetActive(true);
            CombatTargetRegistry.Register(target);

            testObject.transform.position = destination; // 명시 알림을 빠뜨린 외부 이동 경로를 재현
            Physics.SyncTransforms();

            CombatTargetRegistry.MaintainSpatialIndexForValidation(0);
            CombatTargetRegistry.CollectPotentialTargets(destination, 1f, results);
            AssertTrue(
                CombatTargetRegistry.LastSpatialMaintenanceProcessedCountForValidation <= 64,
                "쿼리 프레임의 Spatial 보정량이 고정 예산 64를 초과했습니다.");

            CombatTargetRegistry.MaintainSpatialIndexForValidation(CombatTargetRegistry.RegisteredCount + 64);
            CombatTargetRegistry.CollectPotentialTargets(destination, 1f, results);
            AssertTrue(results.Contains(target), "분할 Spatial 보정이 명시 알림 없는 최종 위치를 복구하지 못했습니다.");
            CombatTargetRegistry.CollectPotentialTargets(start, 1f, results);
            AssertFalse(results.Contains(target), "분할 Spatial 보정 뒤 이전 위치가 남았습니다.");
            report.Add("amortized_spatial_maintenance_budget=64");
            report.Add("amortized_unnotified_reconciliation=PASS");
        }
        finally
        {
            for (int i = 0; i < testObjects.Count; i++)
            {
                GameObject testObject = testObjects[i];
                if (testObject == null)
                    continue;

                CombatTarget target = testObject.GetComponent<CombatTarget>();
                if (target != null)
                    CombatTargetRegistry.Unregister(target);
                UnityEngine.Object.DestroyImmediate(testObject);
            }
        }
    }

    private static void AssertRegistryMatchesBruteForce(
        List<CombatTarget> activeOrder,
        Vector3 center,
        float radius,
        List<CombatTarget> actual,
        string context)
    {
        CombatTargetRegistry.CollectPotentialTargets(center, radius, actual);
        int expectedIndex = 0;
        for (int i = activeOrder.Count - 1; i >= 0; i--)
        {
            CombatTarget target = activeOrder[i];
            if (target == null || !target.IsAlive)
                continue;

            CombatTargetVolume volume = target.CurrentVolume;
            Vector3 offset = volume.Center - center;
            float combinedRadius = Mathf.Max(0f, radius) + volume.Radius;
            if (offset.x * offset.x + offset.z * offset.z > combinedRadius * combinedRadius)
                continue;

            if (expectedIndex >= actual.Count || !ReferenceEquals(actual[expectedIndex], target))
            {
                throw new InvalidOperationException(
                    "Formal Registry result mismatch at " + context
                    + " expectedIndex=" + expectedIndex
                    + " expected=" + target.TargetId
                    + " actual=" + (expectedIndex < actual.Count ? actual[expectedIndex].TargetId : 0));
            }

            expectedIndex++;
        }

        if (expectedIndex != actual.Count)
        {
            throw new InvalidOperationException(
                "Formal Registry result count mismatch at " + context
                + " expected=" + expectedIndex
                + " actual=" + actual.Count);
        }
    }

    private static CombatTarget CreateRegistryTestTarget(
        string objectName,
        Vector3 position,
        float radius,
        List<GameObject> testObjects)
    {
        GameObject testObject = new GameObject(objectName);
        testObject.hideFlags = HideFlags.HideAndDontSave;
        testObject.SetActive(false);
        testObject.transform.position = position;
        CombatHealth health = testObject.AddComponent<CombatHealth>();
        SerializedObject serializedHealth = new SerializedObject(health);
        serializedHealth.FindProperty("showDamageNumbers").boolValue = false;
        serializedHealth.ApplyModifiedPropertiesWithoutUndo();
        testObject.AddComponent<CombatAffiliation>().Configure(CombatTeam.Enemy);
        CombatTarget target = testObject.AddComponent<CombatTarget>();
        target.ConfigureVolume(Vector3.zero, radius, 2f);
        testObjects.Add(testObject);
        testObject.SetActive(true);
        return target;
    }

    private static float NextRange(System.Random random, float minimum, float maximum)
    {
        return minimum + (float)random.NextDouble() * (maximum - minimum);
    }

    private static void AssertIntegrity(CombatSpatialIndexCore<TargetRecord> grid)
    {
        if (!grid.ValidateIntegrity(out string error))
            throw new InvalidOperationException("Index 무결성 실패: " + error);
    }

    private static void AssertContainsExactly(List<TargetRecord> results, TargetRecord expected)
    {
        if (results.Count != 1 || !ReferenceEquals(results[0], expected))
            throw new InvalidOperationException("단일 대상 Query 결과 불일치");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message)
    {
        if (condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message + " expected=" + expected + " actual=" + actual);
    }
}
