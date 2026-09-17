using UnityEngine;

public enum ConsumableType
{
    HealHp,
    Invincible,
    TimeStop,
    SpeedBoost
}

[CreateAssetMenu(fileName = "NewConsumable", menuName = "Items/Consumable")]
public class ConsumableItemData : BaseItemData
{
    [Header("Consumable")]
    public ConsumableType consumableType;
    public ItemGrade defaultGrade = ItemGrade.Common;
    public float effectValue;
    public float duration;
    public float cooldown;
    public int maxStack = 5;
    public bool consumeOnUse = true;
    public bool IsPermanentSingleItem { get { return !consumeOnUse; } }

    [Header("Buff")]
    public string targetBuffId;
    public float moveSpeedMultiplier = 1f;
    public string useMessage;
}
