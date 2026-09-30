using UnityEngine;

// Look of theme projectiles. Spit and acid shots reuse the blood liquid, tinted by the thrower's
// blood profile; an override can retint one ability or add the electric weapon hit on impact.
[CreateAssetMenu(menuName = "OVERBURST/VFX/Enemy Projectile Catalog")]
public sealed class EnemyProjectileVfxCatalog : ScriptableObject
{
    public const string ResourcePath = "Combat/VFX/EnemyProjectileVfxCatalog";

    [System.Serializable]
    public struct AbilityOverride
    {
        public string abilityId;
        [Tooltip("비우면 쏘는 몬스터의 혈흔 색을 쓴다.")] public BloodHitProfile tint;
        [Tooltip("명중 때 번개 대검 타격 VFX를 함께 재생한다.")] public bool electricImpact;
        [Min(0f), Tooltip("0이면 기본 크기.")] public float scale;
        [Tooltip("투사체가 나오는 뼈 이름(입·꼬리·손). 비우면 몸 중심 0.8m 높이.")] public string muzzleBone;
        [Tooltip("머즐 뼈에서 몬스터 정면으로 더 내민 거리(m).")] public float muzzleForward;
    }

    public GameObject projectile;
    [Min(.1f)] public float scale = 1f;
    [Min(.1f), Tooltip("정예·강공 투사체 배율")] public float strongScale = 1.35f;
    [Range(.55f, 1.5f)] public float launchSpraySize = .6f;
    [Range(.55f, 1.5f)] public float impactSplashSize = .9f;
    public AbilityOverride[] overrides = System.Array.Empty<AbilityOverride>();

    private static EnemyProjectileVfxCatalog current;
    private static bool loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { current = null; loaded = false; }

    public static EnemyProjectileVfxCatalog Current
    {
        get
        {
            if (!loaded)
            {
                current = Resources.Load<EnemyProjectileVfxCatalog>(ResourcePath);
                loaded = true;
            }
            return current;
        }
    }

    public bool TryGetOverride(EnemyAbilityDefinition ability, out AbilityOverride result)
    {
        if (ability != null && overrides != null)
            for (int i = 0; i < overrides.Length; i++)
                if (overrides[i].abilityId == ability.AbilityId)
                {
                    result = overrides[i];
                    return true;
                }
        result = default;
        return false;
    }
}
