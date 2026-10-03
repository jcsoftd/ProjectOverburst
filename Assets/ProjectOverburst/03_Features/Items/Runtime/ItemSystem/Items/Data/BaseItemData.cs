using UnityEngine;

public enum ItemGrade // 아이템 등급
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary,
    Artifact,
    Mythic,
    Cursed
}

public enum StatType // 능력치 종류
{
    Damage,
    AttackSpeed,
    MoveSpeed,
    MaxHp,
    GoldGain,
    DropRate,
    Cooldown,
    Weight
}

[System.Serializable]
public class RandomStat // 랜덤 능력치 하나
{
    public StatType statType;
    public float minValue;
    public float maxValue;
    [HideInInspector] public float rolledValue;
}

/// <summary>인벤토리 분류 배지의 표시 선택. 아이템 종류·기능·저장 형식과 분리한다.</summary>
public enum InventoryIconCategory
{
    Auto = 0,
    Misc = 9,
    Quest = 10,
    Consumable = 11,
    Material = 12,
    Key = 13,
    Currency = 14,
    Recipe = 15,
    Container = 16
}

public class BaseItemData : ScriptableObject
{
    [Header("Basic")]
    public string itemName;
    public Sprite icon;
    public Color color = Color.white;
    public string description;
    public float weight;
    public int sellPrice;

    [Header("Inventory Display")]
    [Tooltip("자동은 기존 데이터 종류를 사용한다. 재료·열쇠·제작법·상자 등은 표시용 분류만 지정한다.")]
    public InventoryIconCategory inventoryIconCategory = InventoryIconCategory.Auto;

    [Header("World Pickup")]
    public GameObject worldPickupPrefab;

    [Header("Random Stat Pool")]
    public RandomStat[] possibleStats;
}
