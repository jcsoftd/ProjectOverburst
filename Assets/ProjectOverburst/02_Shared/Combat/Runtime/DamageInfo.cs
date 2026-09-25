using UnityEngine;

[System.Flags]
public enum PlayerAttackKind { Unspecified = 0, Weak = 1, Heavy = 2, Elemental = 4 }

[System.Serializable]
public struct DamageInfo
{
    public float damage;
    public bool isCritical;
    public Vector3 hitPoint;
    public GameObject source;
    public Vector3 direction;
    public float knockback;
    public bool triggersOnHitEffects;
    public bool isDamageOverTime;
    public bool suppressDefaultHitVfx;
    public HitReactionData hitReaction;
    public WeaponElement element;
    public string sourceWeaponRuntimeInstanceId;
    public ElementalReactionType elementalReactionType;
    public int sourceAttackSequenceId;
    public PlayerAttackKind playerAttackKind;

    public DamageInfo(
        float damage,
        Vector3 hitPoint,
        GameObject source = null,
        Vector3 direction = default,
        float knockback = 0f,
        bool isCritical = false,
        bool triggersOnHitEffects = true,
        bool isDamageOverTime = false,
        HitReactionData hitReaction = default,
        bool suppressDefaultHitVfx = false,
        WeaponElement element = WeaponElement.None,
        string sourceWeaponRuntimeInstanceId = "",
        ElementalReactionType elementalReactionType = ElementalReactionType.None,
        int sourceAttackSequenceId = 0,
        PlayerAttackKind playerAttackKind = PlayerAttackKind.Unspecified)
    {
        this.damage = damage;
        this.hitPoint = hitPoint;
        this.source = source;
        this.direction = direction;
        this.knockback = knockback;
        this.isCritical = isCritical;
        this.triggersOnHitEffects = triggersOnHitEffects;
        this.isDamageOverTime = isDamageOverTime;
        this.suppressDefaultHitVfx = suppressDefaultHitVfx;
        this.hitReaction = hitReaction;
        this.element = element;
        this.sourceWeaponRuntimeInstanceId = sourceWeaponRuntimeInstanceId ?? string.Empty;
        this.elementalReactionType = elementalReactionType;
        this.sourceAttackSequenceId = sourceAttackSequenceId;
        this.playerAttackKind = playerAttackKind;
    }
}
