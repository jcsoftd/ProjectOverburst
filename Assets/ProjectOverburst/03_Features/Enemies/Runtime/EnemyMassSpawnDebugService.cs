using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum EnemyMassSpawnDebugPattern // 일회성 대량 스폰 배치
{
    Circle,
    Clustered
}

public static class EnemyMassSpawnDebugService // 디버그 버튼의 일회성 대량 생성 진입점
{
    private static EnemyMassSpawnDebugRuntime runtime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        runtime = null;
    }

    public static bool RequestSpawn(EnemyMassSpawnDebugPattern pattern, int count)
    {
        if (!Application.isPlaying || count <= 0)
            return false;

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        Transform player = playerObject != null ? playerObject.transform : null;
        if (player == null)
        {
            Debug.LogWarning("[EnemyMassSpawnDebug] Player 태그 대상을 찾지 못해 생성하지 않았습니다.");
            return false;
        }

        if (runtime == null)
        {
            GameObject runtimeObject = new GameObject("EnemyMassSpawnDebugRuntime");
            if (player.gameObject.scene.IsValid())
                SceneManager.MoveGameObjectToScene(runtimeObject, player.gameObject.scene);
            runtime = runtimeObject.AddComponent<EnemyMassSpawnDebugRuntime>();
        }

        runtime.Enqueue(pattern, count, player);
        return true;
    }
}

public sealed class EnemyMassSpawnDebugRuntime : MonoBehaviour // 프레임 분할로 대량 생성
{
    private const int SpawnCountPerFrame = 20;
    private const int StableSeed = 20260715;
    private const float GroundProbeHeight = 16f;
    private const float GroundSpawnOffset = 0.05f;

    private readonly Queue<SpawnRequest> requests = new Queue<SpawnRequest>();
    private bool processing;
    private int batchSerial;
    private int monsterSerial;

    public void Enqueue(EnemyMassSpawnDebugPattern pattern, int count, Transform player)
    {
        requests.Enqueue(new SpawnRequest(pattern, Mathf.Max(1, count), player));
        if (!processing)
            StartCoroutine(ProcessRequests());
    }

    private IEnumerator ProcessRequests()
    {
        processing = true;
        while (requests.Count > 0)
        {
            SpawnRequest request = requests.Dequeue();
            yield return SpawnBatch(request);
        }
        processing = false;
    }

    private IEnumerator SpawnBatch(SpawnRequest request)
    {
        EnemySpawnService spawnService = null;
        if (request.Player == null
            || !EnemyDebugSpawnRuntimeContext.TryGetSpawnService(
                transform,
                out spawnService))
        {
            Debug.LogWarning(
                "[EnemyMassSpawnDebug] 플레이어 또는 신규 몬스터 SpawnService가 없어 요청을 취소했습니다.",
                this);
            yield break;
        }

        int currentBatch = batchSerial++;
        var random = new System.Random(unchecked(StableSeed + currentBatch * 7919));
        int spawned = 0;
        for (int index = 0; index < request.Count; index++)
        {
            if (request.Player == null)
                break;

            Vector3 position = ResolveSpawnPosition(
                request.Pattern,
                index,
                request.Count,
                request.Player.position,
                random);
            position = ResolveGroundPosition(position, request.Player.position.y);
            int serial = monsterSerial++;
            string definitionId =
                EnemyDebugSpawnRuntimeContext.GetDefinitionId(index + currentBatch);
            EnemySpawnRequest spawnRequest = new EnemySpawnRequest(
                definitionId,
                position,
                Quaternion.identity,
                request.Player,
                gameObject,
                request.Player,
                transform,
                1f,
                1f,
                unchecked(StableSeed + serial));
            if (spawnService.TrySpawn(spawnRequest, out EnemyActor actor))
            {
                actor.name = string.Format(
                    "MassDebug_{0}_{1:00}_{2:0000}_{3}",
                    request.Pattern,
                    currentBatch,
                    serial,
                    definitionId);
                actor.AI?.RequestAggro(request.Player); // 생성 즉시 같은 디버그 전투 구역에서 추격
                spawned++;
            }

            if ((index + 1) % SpawnCountPerFrame == 0)
                yield return null;
        }

        Debug.Log(
            "[EnemyMassSpawnDebug] 배치=" + request.Pattern
            + " 요청=" + request.Count
            + " 생성=" + spawned,
            this);
    }

    private static Vector3 ResolveSpawnPosition(
        EnemyMassSpawnDebugPattern pattern,
        int index,
        int count,
        Vector3 center,
        System.Random random)
    {
        float signedA = NextSigned(random);
        if (pattern == EnemyMassSpawnDebugPattern.Circle)
        {
            float angle = index * (360f / Mathf.Max(1, count)) + signedA * 8f;
            float radius = 15f + Next01(random) * 6f;
            return center + Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * radius;
        }

        float clusterAngle = signedA * 24f;
        float clusterRadius = 13f + Next01(random) * 9f;
        Vector3 centerBias = Quaternion.AngleAxis(clusterAngle, Vector3.up) * Vector3.right * clusterRadius;
        return center
            + centerBias
            + new Vector3(NextSigned(random) * 1.5f, 0f, NextSigned(random) * 4.8f);
    }

    private static Vector3 ResolveGroundPosition(Vector3 position, float fallbackY)
    {
        int mask = ~0;
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        int playerLayer = LayerMask.NameToLayer("Player");
        if (enemyLayer >= 0)
            mask &= ~(1 << enemyLayer);
        if (playerLayer >= 0)
            mask &= ~(1 << playerLayer);

        Vector3 origin = position + Vector3.up * GroundProbeHeight;
        if (Physics.Raycast(
                origin,
                Vector3.down,
                out RaycastHit hit,
                GroundProbeHeight * 2f,
                mask,
                QueryTriggerInteraction.Ignore))
        {
            position.y = hit.point.y + GroundSpawnOffset;
        }
        else
        {
            position.y = fallbackY;
        }
        return position;
    }

    private static float Next01(System.Random random)
    {
        return (float)random.NextDouble();
    }

    private static float NextSigned(System.Random random)
    {
        return Next01(random) * 2f - 1f;
    }

    private readonly struct SpawnRequest
    {
        public SpawnRequest(EnemyMassSpawnDebugPattern pattern, int count, Transform player)
        {
            Pattern = pattern;
            Count = count;
            Player = player;
        }

        public EnemyMassSpawnDebugPattern Pattern { get; }
        public int Count { get; }
        public Transform Player { get; }
    }
}
