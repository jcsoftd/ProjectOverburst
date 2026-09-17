using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[System.Serializable]
public sealed class EnemySpawnEntry // 랜덤 적 항목
{
    [SerializeField] private GameObject prefab; // 적 프리팹
    [SerializeField] private float weight = 1f; // 가중치
    [SerializeField] private int minCount = 1; // 최소 수
    [SerializeField] private int maxCount = 1; // 최대 수
    [FormerlySerializedAs("scaleWithPlatformSize")]
    [SerializeField] private bool scaleWithAreaSize = true; // 영역 크기 배율

    public GameObject Prefab { get { return prefab; } }
    public float Weight { get { return Mathf.Max(0f, weight); } }
    public int MinCount { get { return Mathf.Max(0, minCount); } }
    public int MaxCount { get { return Mathf.Max(MinCount, maxCount); } }
    public bool ScaleWithAreaSize { get { return scaleWithAreaSize; } }
}

[System.Serializable]
public sealed class EnemySpawnPack // 조합 스폰
{
    [SerializeField] private string packId; // 내부 ID
    [SerializeField] private string displayName; // 표시명
    [SerializeField] private float weight = 1f; // 가중치
    [SerializeField] private bool isElitePack; // 엘리트 포함
    [SerializeField] private List<EnemySpawnEntry> entries =
        new List<EnemySpawnEntry>(); // 구성

    public string PackId { get { return packId; } }
    public string DisplayName { get { return displayName; } }
    public float Weight { get { return Mathf.Max(0f, weight); } }
    public bool IsElitePack { get { return isElitePack; } }
    public IReadOnlyList<EnemySpawnEntry> Entries { get { return entries; } }
}

public readonly struct EnemySpawnSelection // 최종 스폰 항목
{
    public readonly GameObject Prefab;
    public readonly int Count;

    public EnemySpawnSelection(GameObject prefab, int count)
    {
        Prefab = prefab;
        Count = count;
    }
}

[System.Serializable]
public sealed class EnemySpawnPackConfig // 몬스터 스폰 설정
{
    [Header("Spawn")]
    [SerializeField] private int initialSpawnCount = 5; // 초기 수
    [SerializeField] private int waveSpawnCount = 5; // 웨이브 수
    [SerializeField] private float waveInterval = 3f; // 웨이브 간격
    [SerializeField] private float waveDuration = 30f; // 웨이브 시간
    [SerializeField] private int respawnWaveCount = 6; // 사망 후 반복 수
    [SerializeField] private float respawnWaveInterval = 5f; // 사망 후 간격
    [SerializeField] private int respawnPacksPerWave = 1; // wave당 pack 수
    [SerializeField] private bool triggerRespawnOnFirstDeath = true; // 첫 사망 트리거
    [SerializeField] private bool allowMultipleRespawnSequences; // 중복 시퀀스
    [SerializeField] private float spawnMargin = 2f; // 가장자리 여백
    [SerializeField] private float spawnHeightOffset = 1.05f; // 높이 보정
    [SerializeField] private float minMonsterSpacing = 1.2f; // 최소 간격
    [SerializeField] private int maxPositionAttemptsPerMonster = 24; // 위치 재시도

    [Header("Aggro")]
    [SerializeField] private float aggroShareRadius = 14f; // 공유 어그로

    [Header("Enemy")]
    [SerializeField] private GameObject enemyPrefab; // 단일 fallback
    [SerializeField] private EnemySpawnEntry[] enemyPrefabs; // 랜덤 적 목록
    [SerializeField] private List<EnemySpawnPack> spawnPacks =
        new List<EnemySpawnPack>(); // 조합 목록
    [SerializeField] private float detectionRange = 999f; // 감지거리
    [SerializeField] private float stopDistance = 1.8f; // 정지거리

    [Header("Spawn Pack Scaling")]
    [FormerlySerializedAs("scaleSpawnCountByPlatformSize")]
    [SerializeField] private bool scaleSpawnCountByAreaSize = true; // 영역 크기 배율
    [FormerlySerializedAs("referencePlatformArea")]
    [SerializeField] private float referenceAreaSurface = 900f; // 기준 면적
    [FormerlySerializedAs("minPlatformSpawnMultiplier")]
    [SerializeField] private float minAreaSpawnMultiplier = 0.65f; // 최소 배율
    [FormerlySerializedAs("maxPlatformSpawnMultiplier")]
    [SerializeField] private float maxAreaSpawnMultiplier = 1.65f; // 최대 배율
    [FormerlySerializedAs("platformAreaScalePower")]
    [SerializeField] private float areaScalePower = 0.5f; // 완만한 증가
    [FormerlySerializedAs("minEnemiesPerPlatform")]
    [SerializeField] private int minEnemiesPerArea = 1; // 최소 수
    [FormerlySerializedAs("maxEnemiesPerPlatform")]
    [SerializeField] private int maxEnemiesPerArea = 10; // 최대 수
    [SerializeField] private int maxEnemiesPerRespawnWave = 8; // wave 최대 수
    [SerializeField] private int maxEnemiesPerRespawnSequence = 30; // 시퀀스 최대 수
    [SerializeField] private bool enableDebugLogs; // 로그

    [Header("Drop/VFX")]
    [SerializeField] private DropTable dropTable; // 몬스터 드랍
    [SerializeField] private PickupGradeVfxSet pickupGradeVfxSet; // 픽업 VFX
    [SerializeField] private GameObject hitVfxPrefab; // 피격 VFX
    [SerializeField] private GameObject deathVfxPrefab; // 사망 VFX

    public int InitialSpawnCount { get { return Mathf.Max(0, initialSpawnCount); } }
    public int WaveSpawnCount { get { return Mathf.Max(0, waveSpawnCount); } }
    public float WaveInterval { get { return Mathf.Max(0.1f, waveInterval); } }
    public float WaveDuration { get { return Mathf.Max(0f, waveDuration); } }
    public int RespawnWaveCount { get { return Mathf.Max(0, respawnWaveCount); } }
    public float RespawnWaveInterval { get { return Mathf.Max(0.1f, respawnWaveInterval); } }
    public int RespawnPacksPerWave { get { return Mathf.Max(1, respawnPacksPerWave); } }
    public bool TriggerRespawnOnFirstDeath { get { return triggerRespawnOnFirstDeath; } }
    public bool AllowMultipleRespawnSequences { get { return allowMultipleRespawnSequences; } }
    public float SpawnMargin { get { return Mathf.Max(0f, spawnMargin); } }
    public float SpawnHeightOffset { get { return Mathf.Max(0.1f, spawnHeightOffset); } }
    public float MinMonsterSpacing { get { return Mathf.Max(0f, minMonsterSpacing); } }
    public int MaxPositionAttemptsPerMonster { get { return Mathf.Max(1, maxPositionAttemptsPerMonster); } }
    public float AggroShareRadius { get { return Mathf.Max(0f, aggroShareRadius); } }
    public float DetectionRange { get { return Mathf.Max(0f, detectionRange); } }
    public float StopDistance { get { return Mathf.Max(0f, stopDistance); } }
    public bool HasSpawnPacks { get { return spawnPacks != null && spawnPacks.Count > 0; } }
    public float ReferenceAreaSurface { get { return Mathf.Max(1f, referenceAreaSurface); } }
    public float MinAreaSpawnMultiplier { get { return Mathf.Max(0f, minAreaSpawnMultiplier); } }
    public float MaxAreaSpawnMultiplier { get { return Mathf.Max(MinAreaSpawnMultiplier, maxAreaSpawnMultiplier); } }
    public float AreaScalePower { get { return Mathf.Max(0f, areaScalePower); } }
    public int MinEnemiesPerArea { get { return Mathf.Max(0, minEnemiesPerArea); } }
    public int MaxEnemiesPerArea { get { return Mathf.Max(MinEnemiesPerArea, maxEnemiesPerArea); } }
    public int MaxEnemiesPerRespawnWave { get { return Mathf.Max(1, maxEnemiesPerRespawnWave); } }
    public int MaxEnemiesPerRespawnSequence { get { return Mathf.Max(MaxEnemiesPerRespawnWave, maxEnemiesPerRespawnSequence); } }
    public bool EnableDebugLogs { get { return enableDebugLogs; } }
    public DropTable DropTable { get { return dropTable; } }
    public PickupGradeVfxSet PickupGradeVfxSet { get { return pickupGradeVfxSet; } }
    public GameObject HitVfxPrefab { get { return hitVfxPrefab; } }
    public GameObject DeathVfxPrefab { get { return deathVfxPrefab; } }
    public int GetWaveIterationCount()
    {
        return Mathf.FloorToInt(WaveDuration / WaveInterval);
    }

    public GameObject SelectEnemyPrefab()
    {
        return SelectEnemyPrefab(true);
    }

    public GameObject SelectEnemyPrefab(bool allowElite)
    {
        if (enemyPrefab != null && (enemyPrefabs == null || enemyPrefabs.Length == 0))
            return IsAllowedByRank(enemyPrefab, allowElite) ? enemyPrefab : null; // 단일 fallback

        if (enemyPrefabs == null || enemyPrefabs.Length == 0)
            return enemyPrefab; // 최종 fallback

        float totalWeight = 0f;
        for (int i = 0; i < enemyPrefabs.Length; i++)
        {
            EnemySpawnEntry entry = enemyPrefabs[i];
            if (entry != null && entry.Prefab != null && IsAllowedByRank(entry.Prefab, allowElite))
                totalWeight += entry.Weight;
        }

        if (totalWeight <= 0f)
            return null; // weight 없음

        float roll = Random.value * totalWeight; // 가중치 무작위
        for (int i = 0; i < enemyPrefabs.Length; i++)
        {
            EnemySpawnEntry entry = enemyPrefabs[i];
            if (entry == null || entry.Prefab == null || !IsAllowedByRank(entry.Prefab, allowElite))
                continue;

            roll -= entry.Weight;
            if (roll <= 0f)
                return entry.Prefab;
        }

        return enemyPrefab; // 안전 fallback
    }

    public List<EnemySpawnSelection> SelectSpawnPack(
        bool allowElite,
        float areaSurface = 0f)
    {
        return SelectSpawnPack(
            allowElite,
            MaxEnemiesPerArea,
            areaSurface);
    }

    public List<EnemySpawnSelection> SelectRespawnPack(
        int remainingSequenceBudget,
        float areaSurface = 0f)
    {
        int cap = Mathf.Min(MaxEnemiesPerRespawnWave, Mathf.Max(0, remainingSequenceBudget));
        if (cap <= 0)
            return null;

        return SelectSpawnPack(true, cap, areaSurface);
    }

    private List<EnemySpawnSelection> SelectSpawnPack(
        bool allowElite,
        int maxCount,
        float areaSurface)
    {
        EnemySpawnPack pack = SelectPack(allowElite);
        if (pack == null)
            return null; // 대체 경로

        float resolvedArea = areaSurface > 0f
            ? areaSurface
            : ReferenceAreaSurface;
        float multiplier = CalculateAreaMultiplier(resolvedArea);
        List<MutableSpawnSelection> mutable = BuildPackSelections(pack, multiplier);
        ClampSelections(mutable, maxCount);

        List<EnemySpawnSelection> selections =
            new List<EnemySpawnSelection>();
        int totalCount = 0;
        for (int i = 0; i < mutable.Count; i++)
        {
            if (mutable[i].Prefab == null || mutable[i].Count <= 0)
                continue;

            selections.Add(
                new EnemySpawnSelection(
                    mutable[i].Prefab,
                    mutable[i].Count));
            totalCount += mutable[i].Count;
        }

        if (enableDebugLogs)
        {
            string id = string.IsNullOrWhiteSpace(pack.PackId) ? pack.DisplayName : pack.PackId;
            Debug.Log("[EnemySpawnPack] Area=" + resolvedArea.ToString("0.0")
                + " Multiplier=" + multiplier.ToString("0.00")
                + " Selected=" + id
                + " Count=" + totalCount);
        }

        return selections.Count > 0 ? selections : null;
    }

    private EnemySpawnPack SelectPack(bool allowElite)
    {
        if (spawnPacks == null || spawnPacks.Count == 0)
            return null;

        float totalWeight = 0f;
        for (int i = 0; i < spawnPacks.Count; i++)
        {
            EnemySpawnPack pack = spawnPacks[i];
            if (pack != null && (allowElite || !pack.IsElitePack))
                totalWeight += pack.Weight;
        }

        if (totalWeight <= 0f)
            return null;

        float roll = Random.value * totalWeight;
        for (int i = 0; i < spawnPacks.Count; i++)
        {
            EnemySpawnPack pack = spawnPacks[i];
            if (pack == null || (!allowElite && pack.IsElitePack))
                continue;

            roll -= pack.Weight;
            if (roll <= 0f)
                return pack;
        }

        return null;
    }

    private List<MutableSpawnSelection> BuildPackSelections(
        EnemySpawnPack pack,
        float multiplier)
    {
        List<MutableSpawnSelection> selections = new List<MutableSpawnSelection>();
        IReadOnlyList<EnemySpawnEntry> entries = pack.Entries;
        if (entries == null)
            return selections;

        for (int i = 0; i < entries.Count; i++)
        {
            EnemySpawnEntry entry = entries[i];
            if (entry == null || entry.Prefab == null)
                continue;

            int baseCount = Random.Range(entry.MinCount, entry.MaxCount + 1);
            int finalCount = entry.ScaleWithAreaSize && scaleSpawnCountByAreaSize
                ? Mathf.RoundToInt(baseCount * multiplier)
                : baseCount;

            if (entry.MinCount > 0)
                finalCount = Mathf.Max(1, finalCount);

            selections.Add(new MutableSpawnSelection(entry.Prefab, Mathf.Max(0, finalCount), entry.ScaleWithAreaSize, entry.MinCount > 0));

            if (enableDebugLogs)
            {
                Debug.Log(
                    "[EnemySpawnPack] Entry="
                    + entry.Prefab.name
                    + " Base="
                    + baseCount
                    + " Final="
                    + finalCount);
            }
        }

        return selections;
    }

    private void ClampSelections(List<MutableSpawnSelection> selections, int maxCount)
    {
        if (selections == null || selections.Count == 0)
            return;

        int total = CountSelections(selections);
        while (total > maxCount && TryReduceScalableSelection(selections))
            total = CountSelections(selections);

        while (total < MinEnemiesPerArea && TryIncreaseScalableSelection(selections))
            total = CountSelections(selections);
    }

    private static int CountSelections(List<MutableSpawnSelection> selections)
    {
        int total = 0;
        for (int i = 0; i < selections.Count; i++)
            total += Mathf.Max(0, selections[i].Count);
        return total;
    }

    private static bool TryReduceScalableSelection(List<MutableSpawnSelection> selections)
    {
        for (int i = selections.Count - 1; i >= 0; i--)
        {
            MutableSpawnSelection selection = selections[i];
            int floor = selection.KeepAtLeastOne ? 1 : 0;
            if (!selection.ScaleWithAreaSize || selection.Count <= floor)
                continue;

            selection.Count--;
            selections[i] = selection;
            return true;
        }

        return false;
    }

    private static bool TryIncreaseScalableSelection(List<MutableSpawnSelection> selections)
    {
        for (int i = 0; i < selections.Count; i++)
        {
            MutableSpawnSelection selection = selections[i];
            if (!selection.ScaleWithAreaSize || selection.Prefab == null)
                continue;

            selection.Count++;
            selections[i] = selection;
            return true;
        }

        return false;
    }

    private float CalculateAreaMultiplier(float areaSurface)
    {
        if (!scaleSpawnCountByAreaSize)
            return 1f;

        float normalizedArea = Mathf.Max(0.01f, areaSurface / ReferenceAreaSurface);
        float multiplier = Mathf.Pow(normalizedArea, AreaScalePower);
        return Mathf.Clamp(multiplier, MinAreaSpawnMultiplier, MaxAreaSpawnMultiplier);
    }

    private static bool IsAllowedByRank(GameObject prefab, bool allowElite)
    {
        if (allowElite || prefab == null)
            return true;

        EnemyRank rank = prefab.GetComponent<EnemyRank>();
        return rank == null || rank.Rank != EnemyRankType.Elite; // 초기 일반만
    }

    private struct MutableSpawnSelection
    {
        public readonly GameObject Prefab;
        public readonly bool ScaleWithAreaSize;
        public readonly bool KeepAtLeastOne;
        public int Count;

        public MutableSpawnSelection(GameObject prefab, int count, bool scaleWithAreaSize, bool keepAtLeastOne)
        {
            Prefab = prefab;
            Count = count;
            ScaleWithAreaSize = scaleWithAreaSize;
            KeepAtLeastOne = keepAtLeastOne;
        }
    }
}
