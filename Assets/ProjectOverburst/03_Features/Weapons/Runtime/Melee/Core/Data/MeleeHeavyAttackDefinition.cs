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
    public GameObject electricChainStart;
    public GameObject electricChainProc;

    [Header("원본 배율에서 주 폭발의 XZ 반경 (m)")]
    [Min(0.01f)] public float fireImpactRadius;
    [Min(0.01f)] public float electricImpactRadius;
    [Min(0.01f)] public float iceImpactRadius;
    [Min(0.01f)] public float fireChainRadius;

    public float ImpactScale(WeaponElement element, float damageRadius)
    {
        float reference = element == WeaponElement.Fire ? fireImpactRadius
            : element == WeaponElement.Electric ? electricImpactRadius : iceImpactRadius;
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
            default: return null;
        }
    }
}

[CreateAssetMenu(
    fileName = "MeleeHeavyAttackDefinition",
    menuName = "OVERBURST/Weapons/Melee Heavy Attack Definition")]
public sealed class MeleeHeavyAttackDefinition : ScriptableObject
{
    [InspectorName("강공 동작")]
    public MeleeComboStepData attack;

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
