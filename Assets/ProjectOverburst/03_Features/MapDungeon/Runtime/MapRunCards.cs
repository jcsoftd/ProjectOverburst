using System;
using System.Collections.Generic;
using UnityEngine;

public enum MapCardKind { Buff, LootChest, Experience, TransferObject }
public enum MapBuffKind { MaxHealth, Armor, Attack, ElementalDamage, AttackSpeed, MoveSpeed, ItemDrop, ExperienceGain }

public sealed class MapCardChoice
{
    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string EffectText { get; }
    public MapCardKind Kind { get; }
    public MapBuffKind Buff { get; }
    public ItemGrade Grade { get; }
    public float Value { get; }

    public MapCardChoice(string id, string title, string description, string effectText, MapCardKind kind,
        MapBuffKind buff, ItemGrade grade, float value)
    {
        Id = id;
        Title = title;
        Description = description;
        EffectText = effectText;
        Kind = kind;
        Buff = buff;
        Grade = grade;
        Value = value;
    }
}

public sealed class MapCardOffer
{
    public MapCardChoice[] Choices { get; }
    public MapCardOffer(MapCardChoice[] choices)
    {
        if (choices == null || choices.Length != 3) throw new ArgumentException("Three cards are required.");
        Choices = choices;
    }
}

// Map grades and item-drop grades are deliberately separate from these card odds.
public static class MapRunCardPolicy
{
    private static readonly string[] BuffNames =
    {
        "생명의 각인", "강철의 각인", "파괴의 각인", "원소의 각인",
        "질풍의 각인", "순풍의 각인", "수확의 각인", "지혜의 각인"
    };
    private static readonly float[] BaseValues = { .12f, 0f, .10f, .14f, .06f, .06f, .10f, .10f };
    private static readonly float[] GradeFactors = { 1f, 1.3f, 1.65f, 2.05f, 2.55f, 3.1f, 3.8f };
    private static readonly double[] GradeCumulative = { .43, .70, .85, .93, .975, .994, 1.0 };

    public static MapCardOffer Roll(System.Random random, int mapLevel, int playerLevel)
    {
        if (random == null) throw new ArgumentNullException(nameof(random));
        var available = new List<MapBuffKind>();
        for (int i = 0; i < BuffNames.Length; i++)
            if (playerLevel < OverburstGrowthRules.MaximumLevel || (MapBuffKind)i != MapBuffKind.ExperienceGain)
                available.Add((MapBuffKind)i);
        var result = new MapCardChoice[3];
        result[0] = RollBuff(random, available, mapLevel);
        result[1] = RollBuff(random, available, mapLevel);
        double special = random.NextDouble();
        if (special < .05)
            result[2] = new MapCardChoice("loot_chest", "봉인된 전리품", "높은 등급의 아이템 상자를 소환합니다.", "특별 상자",
                MapCardKind.LootChest, default, ItemGrade.Common, 0f);
        else if (special < .08)
        {
            if (playerLevel >= OverburstGrowthRules.MaximumLevel)
                result[2] = RollBuff(random, available, mapLevel);
            else
            {
                int experience = OverburstGrowthRules.ExperienceForKill(mapLevel, EnemyGradeType.Normal, playerLevel) * 5;
                result[2] = new MapCardChoice("experience", "기억의 파편", "경험치를 즉시 얻습니다. 사망해도 유지됩니다.",
                    $"경험치 +{experience}", MapCardKind.Experience, default, ItemGrade.Common, experience);
            }
        }
        else if (special < .10)
            result[2] = new MapCardChoice("transfer_object", "안전한 전송", "창고 전송 오브젝트를 소환합니다.", "전송 오브젝트",
                MapCardKind.TransferObject, default, ItemGrade.Common, 0f);
        else result[2] = RollBuff(random, available, mapLevel);
        return new MapCardOffer(result);
    }

    private static MapCardChoice RollBuff(System.Random random, List<MapBuffKind> available, int mapLevel)
    {
        int selected = random.Next(available.Count);
        MapBuffKind kind = available[selected];
        available.RemoveAt(selected);
        ItemGrade grade = RollGrade(random.NextDouble());
        float factor = GradeFactors[(int)grade];
        float value = kind == MapBuffKind.Armor
            ? Mathf.Round((10f + mapLevel * 2f) * factor)
            : BaseValues[(int)kind] * factor;
        string detail = kind == MapBuffKind.Armor
            ? $"방어력 +{value:0}"
            : $"{EffectName(kind)} +{Mathf.RoundToInt(value * 100f)}%";
        return new MapCardChoice("buff_" + kind, BuffNames[(int)kind], BuffDescription(kind), detail,
            MapCardKind.Buff, kind, grade, value);
    }

    private static string BuffDescription(MapBuffKind kind)
    {
        switch (kind)
        {
            case MapBuffKind.MaxHealth: return "최대 체력과 현재 체력이 함께 증가합니다.";
            case MapBuffKind.Armor: return "받는 피해가 줄어듭니다.";
            case MapBuffKind.Attack: return "모든 공격이 더 강해집니다.";
            case MapBuffKind.ElementalDamage: return "원소 공격의 피해가 증가합니다.";
            case MapBuffKind.AttackSpeed: return "공격 동작이 빨라집니다.";
            case MapBuffKind.MoveSpeed: return "이동이 빨라집니다.";
            case MapBuffKind.ItemDrop: return "몬스터의 장비·물약·지도 드롭 확률이 증가합니다.";
            default: return "몬스터 처치 경험치가 증가합니다.";
        }
    }

    private static string EffectName(MapBuffKind kind)
    {
        switch (kind)
        {
            case MapBuffKind.MaxHealth: return "최대 체력";
            case MapBuffKind.Attack: return "공격력";
            case MapBuffKind.ElementalDamage: return "원소 피해";
            case MapBuffKind.AttackSpeed: return "공격 속도";
            case MapBuffKind.MoveSpeed: return "이동 속도";
            case MapBuffKind.ItemDrop: return "아이템 드롭률";
            default: return "획득 경험치";
        }
    }

    public static ItemGrade RollGrade(double roll)
    {
        for (int i = 0; i < GradeCumulative.Length; i++)
            if (roll < GradeCumulative[i]) return (ItemGrade)i;
        return ItemGrade.Mythic;
    }
}
