using UnityEngine;

[System.Serializable]
public struct MeleeHeavyElementVfxSet
{
    public GameObject fireImpact;
    [Tooltip("연소 대상에서 전파되는 연쇄폭발. 비어 있으면 기존 착지 폭발 참조를 사용합니다.")]
    public GameObject fireChainExplosion;
    public GameObject iceImpact;
    public GameObject iceShatter;
    public GameObject electricImpact;
    public GameObject electricChainLink;
    [Tooltip("번개 강공 착지 원에 직접 맞은 적마다 발밑에서 재생. Vertical Lightning Purple 프로젝트 변형")]
    public GameObject electricDirectHit;
    [Header("상위 원소 강공 (60D)")]
    [Tooltip("어둠 잠식 탄막 내려찍기(착지 1회). 60D 4.6, 사용자 지정 대기")]
    public GameObject darkBarrageSlam;
    [Tooltip("어둠 탄 1발. 투사체와 트레일이 모두 비어 있으면 임시 구체와 트레일을 사용한다")]
    public GameObject darkBarrageProjectile;
    [Tooltip("어둠 탄 뒤 꼬리. 투사체와 같은 위치를 따라간다")]
    public GameObject darkBarrageTrail;
    [Tooltip("어둠 탄 명중 폭발")]
    public GameObject darkBarrageHit;
    [Tooltip("빛 3연타(에너지 100 초과). Rune_Multi_Impact 프로젝트 변형")]
    public GameObject lightTripleImpact;
    [Tooltip("빛 2연타(에너지 100 이하). 1타를 끈 Rune_Multi_Impact 프로젝트 변형")]
    public GameObject lightDoubleImpact;

    [Header("원본 배율에서 주 폭발의 XZ 반경 (m)")]
    [Min(0.01f)] public float fireImpactRadius;
    [Min(0.01f)] public float electricImpactRadius;
    [Min(0.01f)] public float iceImpactRadius;
    [Min(0.01f)] public float fireChainRadius;
    [Tooltip("어둠 내려찍기 VFX가 1배율일 때 주 폭발의 XZ 반경")]
    [Min(0.01f)] public float darkImpactRadius;
    [Tooltip("빛 변형이 1배율일 때 가장 큰 3타 충격파의 XZ 반경")]
    [Min(0.01f)] public float lightImpactRadius;

    public float ImpactScale(WeaponElement element, float damageRadius)
    {
        float reference = element == WeaponElement.Fire ? fireImpactRadius
            : element == WeaponElement.Electric ? electricImpactRadius
            : element == WeaponElement.Dark ? darkImpactRadius
            : element == WeaponElement.Light ? lightImpactRadius : iceImpactRadius;
        return Mathf.Max(0f, damageRadius) / Mathf.Max(.01f, reference > 0f ? reference : 1f);
    }

    public float FireChainReferenceRadius => fireChainRadius > 0f ? fireChainRadius : 1f;

    public GameObject FireChainExplosion => fireChainExplosion != null ? fireChainExplosion : fireImpact;

    public GameObject GetImpact(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire: return fireImpact;
            case WeaponElement.Ice: return iceImpact;
            case WeaponElement.Electric: return electricImpact;
            case WeaponElement.Dark: return darkBarrageSlam;
            case WeaponElement.Light: return lightTripleImpact;
            default: return null;
        }
    }

    public GameObject GetLightImpact(bool triple) => triple ? lightTripleImpact : lightDoubleImpact;
}

[CreateAssetMenu(
    fileName = "MeleeHeavyAttackDefinition",
    menuName = "OVERBURST/Weapons/Melee Heavy Attack Definition")]
public sealed class MeleeHeavyAttackDefinition : ScriptableObject
{
    [InspectorName("강공 동작")]
    public MeleeComboStepData attack;

    [InspectorName("착지·원소 방출 판정 인덱스")]
    [Min(0)] public int dischargePhaseIndex;
    public int SafeDischargePhaseIndex => attack.attackPhases == null || attack.attackPhases.Length == 0
        ? 0 : Mathf.Clamp(dischargePhaseIndex, 0, attack.attackPhases.Length - 1);

    [InspectorName("에너지 보유 시 기본 피해 배율")]
    [Min(0.01f)] public float chargedDamageMultiplier = 1.3f;

    [InspectorName("에너지 0일 때 기본 피해 배율")]
    [Min(0.01f)] public float emptyDamageMultiplier = 0.65f;

    [Header("원소 방출 연출")]
    public MeleeHeavyElementVfxSet elementVfx;

    public bool IsConfigured => attack.animationClip != null
        && attack.attackPhases != null
        && attack.attackPhases.Length > 0;

    public float GetDamageMultiplier(bool hasEnergy)
    {
        return Mathf.Max(0.01f, hasEnergy ? chargedDamageMultiplier : emptyDamageMultiplier);
    }
}
