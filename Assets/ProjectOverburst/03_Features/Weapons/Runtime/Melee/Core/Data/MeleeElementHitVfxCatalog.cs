using UnityEngine;

[CreateAssetMenu(
    fileName = "MeleeElementHitVfxCatalog",
    menuName = "OVERBURST/Weapons/Melee Element Hit VFX Catalog")]
public sealed class MeleeElementHitVfxCatalog : ScriptableObject
{
    [System.Serializable]
    public sealed class RuntimePool
    {
        public WeaponElement element;
        public GameObject prefab;
        [Min(0)] public int prewarmCount;
    }

    public const string ResourcePath = "Combat/VFX/MeleeElementHitVfxCatalog";

    [InspectorName("공용 원소 적중 프리팹")]
    public GameObject sharedHitPrefab;
    [Tooltip("공통 저작 프리팹에서 생성한 단일 원소 런타임 풀")]
    public RuntimePool[] runtimePools = System.Array.Empty<RuntimePool>();
    [Header("원본 최대 재생시간(초)")]
    [Min(0f)] public float fireLifetime = 5.15f;
    [Min(0f)] public float iceLifetime = 10f;
    [Min(0f)] public float electricLifetime = 7.5f;
    [Min(0f)] public float darkLifetime = 5f;
    [Min(0f)] public float lightLifetime = 2f;
    [Header("원소별 타격 크기 보정(배)")]
    [Tooltip("원소끼리 크기를 맞추는 배율. 불은 좁은 불꽃이라 조금 키우고, 가장 큰 빛은 줄인다.")]
    [Min(0.01f)] public float fireHitScale = 1.1f;
    [Min(0.01f)] public float iceHitScale = 1f;
    [Min(0.01f)] public float electricHitScale = 1f;
    [Min(0.01f)] public float darkHitScale = 1f;
    [Min(0.01f)] public float lightHitScale = .85f;
    [Header("원소별 재생속도(배)")]
    [Tooltip("파티클 재생속도 배율. 불은 약 0.45초 보이던 것을 약 0.3초로 줄인다.")]
    [Range(0.1f, 4f)] public float firePlaybackSpeed = 1.5f;
    [Range(0.1f, 4f)] public float icePlaybackSpeed = 1f;
    [Range(0.1f, 4f)] public float electricPlaybackSpeed = 1f;
    [Range(0.1f, 4f)] public float darkPlaybackSpeed = 1f;
    [Range(0.1f, 4f)] public float lightPlaybackSpeed = 1f;
    [InspectorName("풀 최대 보관 수")]
    [Min(1)] public int poolCapacity = 32;

    [Tooltip("시작 시 준비할 공용 적중 효과 수. 이 수 이상을 풀에 보관합니다. 8대상/0.8초, 최장 10초 수명을 포함합니다.")]
    [Min(0)] public int prewarmCount = 128;

    public int EffectivePoolCapacity => Mathf.Max(1, Mathf.Max(poolCapacity, prewarmCount));

    public bool TryResolve(WeaponElement element, out GameObject prefab)
    {
        RuntimePool entry = FindRuntimePool(element);
        prefab = Supports(element) ? (entry != null ? entry.prefab : sharedHitPrefab) : null;
        return prefab != null;
    }

    public int ResolvePrewarmCount(WeaponElement element)
    {
        RuntimePool entry = FindRuntimePool(element);
        return Mathf.Max(0, entry != null ? entry.prewarmCount : prewarmCount);
    }

    public int ResolvePoolCapacity(WeaponElement element) =>
        Mathf.Max(1, Mathf.Max(poolCapacity, ResolvePrewarmCount(element)));

    private RuntimePool FindRuntimePool(WeaponElement element)
    {
        if (runtimePools != null)
            for (int i = 0; i < runtimePools.Length; i++)
                if (runtimePools[i] != null && runtimePools[i].element == element && runtimePools[i].prefab != null)
                    return runtimePools[i];
        return null;
    }

    public float ResolveLifetime(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire:
                return Mathf.Max(0f, fireLifetime);
            case WeaponElement.Ice:
                return Mathf.Max(0f, iceLifetime);
            case WeaponElement.Electric:
                return Mathf.Max(0f, electricLifetime);
            case WeaponElement.Dark:
                return Mathf.Max(0f, darkLifetime);
            case WeaponElement.Light:
                return Mathf.Max(0f, lightLifetime);
            default:
                return 0f;
        }
    }

    public float ResolveHitScale(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire: return Mathf.Max(.01f, fireHitScale);
            case WeaponElement.Ice: return Mathf.Max(.01f, iceHitScale);
            case WeaponElement.Electric: return Mathf.Max(.01f, electricHitScale);
            case WeaponElement.Dark: return Mathf.Max(.01f, darkHitScale);
            case WeaponElement.Light: return Mathf.Max(.01f, lightHitScale);
            default: return 1f;
        }
    }

    public float ResolvePlaybackSpeed(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire: return Mathf.Clamp(firePlaybackSpeed, .1f, 4f);
            case WeaponElement.Ice: return Mathf.Clamp(icePlaybackSpeed, .1f, 4f);
            case WeaponElement.Electric: return Mathf.Clamp(electricPlaybackSpeed, .1f, 4f);
            case WeaponElement.Dark: return Mathf.Clamp(darkPlaybackSpeed, .1f, 4f);
            case WeaponElement.Light: return Mathf.Clamp(lightPlaybackSpeed, .1f, 4f);
            default: return 1f;
        }
    }

    public static bool Supports(WeaponElement element)
    {
        return element == WeaponElement.Fire
            || element == WeaponElement.Ice
            || element == WeaponElement.Electric
            || element == WeaponElement.Dark
            || element == WeaponElement.Light;
    }
}
