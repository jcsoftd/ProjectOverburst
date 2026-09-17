using UnityEngine;

[CreateAssetMenu(fileName = "AIP_Enemy", menuName = "OVERBURST/Enemies/AI Preset")]
public sealed class EnemyAiPreset : ScriptableObject // 종족 AI와 시뮬레이터가 공유할 단순 설정 원본
{
    [Header("Identity")]
    [SerializeField] private string presetId = "Default"; // 코드와 저장값에서 사용할 고유 ID
    [SerializeField] private string displayName = "기본 AI"; // 시뮬레이터 표시 이름
    [SerializeField] private GameObject[] defaultMonsterPrefabs = new GameObject[0]; // 이 AI의 기본 로스터

    [Header("Squad Pursuit")]
    [SerializeField, Min(1)] private int activationCount = EnemySquadPursuitPlanner.DefaultActivationCount; // 부대 AI 활성 마릿수
    [SerializeField, Min(1)] private int minimumSquadSize = EnemySquadPursuitPlanner.DefaultMinimumSquadSize; // 최소 부대원
    [SerializeField, Min(1)] private int maximumSquadSize = EnemySquadPursuitPlanner.DefaultMaximumSquadSize; // 최대 부대원
    [SerializeField, Min(0.5f)] private float slotRadius = EnemySquadPursuitPlanner.DefaultSlotRadius; // 플레이어 주변 슬롯 반경
    [SerializeField, Min(0.5f)] private float directCommitRadius = EnemySquadPursuitPlanner.DefaultDirectCommitRadius; // 곡선 종료 반경
    [SerializeField, Min(0.5f)] private float nearReleaseDistance = EnemySquadPursuitPlanner.DefaultNearReleaseDistance; // 근거리 전환 거리
    [SerializeField, Min(0.5f)] private float farActivationDistance = EnemySquadPursuitPlanner.DefaultFarActivationDistance; // 원거리 재진입 거리
    [SerializeField, Range(0.1f, 2f)] private float reserveSpeedMultiplier = EnemySquadPursuitPlanner.DefaultReserveSpeedMultiplier; // 예비 부대 속도 배율
    [SerializeField, Range(0.01f, 0.99f)] private float remnantRatio = EnemySquadPursuitPlanner.DefaultRemnantRatio; // 슬롯권 박탈 생존 비율
    [SerializeField, Min(0.1f)] private float slotArrivalDistance = 1.6f; // 슬롯 도착 판정 거리
    [SerializeField, Min(0.5f)] private float reserveOrbitRadius = EnemySquadPursuitPlanner.DefaultReserveOrbitRadius; // 예비 부대 기준 궤도 반경
    [SerializeField, Min(0f)] private float reserveOrbitAngularSpeed = 6f; // 예비 부대 궤도 각속도
    [SerializeField, Range(0.1f, 2f)] private float curveRadiusMultiplier = 1f; // 전체 우회 곡률 배율

    public string PresetId => string.IsNullOrWhiteSpace(presetId) ? name : presetId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public int DefaultMonsterCount => defaultMonsterPrefabs != null ? defaultMonsterPrefabs.Length : 0;
    public int ActivationCount => Mathf.Max(1, activationCount);
    public int MinimumSquadSize => Mathf.Max(1, minimumSquadSize);
    public int MaximumSquadSize => Mathf.Max(MinimumSquadSize, maximumSquadSize);
    public float SlotRadius => Mathf.Max(0.5f, slotRadius);
    public float DirectCommitRadius => Mathf.Max(SlotRadius + 0.5f, directCommitRadius);
    public float NearReleaseDistance => Mathf.Clamp(nearReleaseDistance, 0.5f, SlotRadius);
    public float FarActivationDistance => Mathf.Max(DirectCommitRadius + 0.5f, farActivationDistance);
    public float ReserveSpeedMultiplier => Mathf.Clamp(reserveSpeedMultiplier, 0.1f, 2f);
    public float RemnantRatio => Mathf.Clamp(remnantRatio, 0.01f, 0.99f);
    public float SlotArrivalDistance => Mathf.Max(0.1f, slotArrivalDistance);
    public float ReserveOrbitRadius => Mathf.Max(FarActivationDistance + 0.5f, reserveOrbitRadius);
    public float ReserveOrbitAngularSpeed => Mathf.Max(0f, reserveOrbitAngularSpeed);
    public float CurveRadiusMultiplier => Mathf.Clamp(curveRadiusMultiplier, 0.1f, 2f);

    public GameObject GetDefaultMonsterPrefab(int index)
    {
        if (defaultMonsterPrefabs == null || index < 0 || index >= defaultMonsterPrefabs.Length)
            return null;

        return defaultMonsterPrefabs[index];
    }

    public void ConfigureIdentity(string id, string label, GameObject[] monsterPrefabs)
    {
        presetId = string.IsNullOrWhiteSpace(id) ? "Default" : id;
        displayName = string.IsNullOrWhiteSpace(label) ? presetId : label;
        defaultMonsterPrefabs = monsterPrefabs != null ? (GameObject[])monsterPrefabs.Clone() : new GameObject[0];
    }

    public void ConfigureSquadPursuit(
        int newActivationCount,
        int newMinimumSquadSize,
        int newMaximumSquadSize,
        float newSlotRadius,
        float newDirectCommitRadius,
        float newNearReleaseDistance,
        float newFarActivationDistance,
        float newReserveSpeedMultiplier,
        float newRemnantRatio,
        float newSlotArrivalDistance,
        float newReserveOrbitRadius,
        float newReserveOrbitAngularSpeed,
        float newCurveRadiusMultiplier)
    {
        activationCount = Mathf.Max(1, newActivationCount);
        minimumSquadSize = Mathf.Max(1, newMinimumSquadSize);
        maximumSquadSize = Mathf.Max(minimumSquadSize, newMaximumSquadSize);
        slotRadius = Mathf.Max(0.5f, newSlotRadius);
        directCommitRadius = Mathf.Max(slotRadius + 0.5f, newDirectCommitRadius);
        nearReleaseDistance = Mathf.Clamp(newNearReleaseDistance, 0.5f, slotRadius);
        farActivationDistance = Mathf.Max(directCommitRadius + 0.5f, newFarActivationDistance);
        reserveSpeedMultiplier = Mathf.Clamp(newReserveSpeedMultiplier, 0.1f, 2f);
        remnantRatio = Mathf.Clamp(newRemnantRatio, 0.01f, 0.99f);
        slotArrivalDistance = Mathf.Max(0.1f, newSlotArrivalDistance);
        reserveOrbitRadius = Mathf.Max(farActivationDistance + 0.5f, newReserveOrbitRadius);
        reserveOrbitAngularSpeed = Mathf.Max(0f, newReserveOrbitAngularSpeed);
        curveRadiusMultiplier = Mathf.Clamp(newCurveRadiusMultiplier, 0.1f, 2f);
    }

    private void OnValidate()
    {
        activationCount = Mathf.Max(1, activationCount);
        minimumSquadSize = Mathf.Max(1, minimumSquadSize);
        maximumSquadSize = Mathf.Max(minimumSquadSize, maximumSquadSize);
        slotRadius = Mathf.Max(0.5f, slotRadius);
        directCommitRadius = Mathf.Max(slotRadius + 0.5f, directCommitRadius);
        nearReleaseDistance = Mathf.Clamp(nearReleaseDistance, 0.5f, slotRadius);
        farActivationDistance = Mathf.Max(directCommitRadius + 0.5f, farActivationDistance);
        reserveSpeedMultiplier = Mathf.Clamp(reserveSpeedMultiplier, 0.1f, 2f);
        remnantRatio = Mathf.Clamp(remnantRatio, 0.01f, 0.99f);
        slotArrivalDistance = Mathf.Max(0.1f, slotArrivalDistance);
        reserveOrbitRadius = Mathf.Max(farActivationDistance + 0.5f, reserveOrbitRadius);
        reserveOrbitAngularSpeed = Mathf.Max(0f, reserveOrbitAngularSpeed);
        curveRadiusMultiplier = Mathf.Clamp(curveRadiusMultiplier, 0.1f, 2f);
    }
}
