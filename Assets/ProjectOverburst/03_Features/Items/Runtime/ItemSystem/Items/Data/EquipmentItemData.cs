using UnityEngine;

public enum EquipmentType // 장비 종류 (패시브 효과)
{
    Lifesteal,      // 흡혈
    GoldBoost,      // 골드획득 증가
    HpRegen,        // HP재생
    DamageAura,     // 데미지 증가 오라
    ShieldOnHit,    // 피격시 보호막
    ExplosionOnKill // 킬시 폭발
}

[CreateAssetMenu(fileName = "NewEquipment", menuName = "Items/Equipment")]
public class EquipmentItemData : BaseItemData
{
    [Header("장비 정보")]
    public EquipmentType equipmentType; // 패시브 효과 종류
    public float effectValue; // 효과 수치
}