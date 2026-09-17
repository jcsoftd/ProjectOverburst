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

public class BaseItemData : ScriptableObject
{
    [Header("Basic")]
    public string itemName;
    public Sprite icon;
    public Color color = Color.white;
    public string description;
    public float weight;
    public int sellPrice;

    [Header("World Pickup")]
    public GameObject worldPickupPrefab;

    [Header("Random Stat Pool")]
    public RandomStat[] possibleStats;
}
