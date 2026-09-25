using UnityEngine;

[System.Serializable]
public struct MeleeHeavyElementVfxSet
{
    public GameObject fireImpact;
    public GameObject iceImpact;
    public GameObject iceShatter;
    public GameObject electricImpact;
    public GameObject electricChainLink;
    public GameObject waterImpact;

    public GameObject GetImpact(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire: return fireImpact;
            case WeaponElement.Ice: return iceImpact;
            case WeaponElement.Electric: return electricImpact;
            case WeaponElement.Water: return waterImpact;
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
