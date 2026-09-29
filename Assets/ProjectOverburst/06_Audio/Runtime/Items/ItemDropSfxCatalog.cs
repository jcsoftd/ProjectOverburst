using System;
using UnityEngine;

public enum ItemDropSfxKind
{
    Weapon,
    Armor,
    Accessory,
    Potion,
    Gold,
    Map,
    Bag
}

// 월드 아이템 드랍 소리: 착지 드랍음(종류별) + 등장 등급음(전설 이상). 재생은 ItemDropSfxService.
[CreateAssetMenu(fileName = "ItemDropSfxCatalog", menuName = "OVERBURST/Items/Item Drop SFX Catalog")]
public sealed class ItemDropSfxCatalog : ScriptableObject
{
    public const string ResourcePath = "Items/SFX/ItemDropSfxCatalog";

    [Header("착지 드랍음 — 종류별, 여러 개면 랜덤")]
    public AudioClip[] weapon = Array.Empty<AudioClip>();
    public AudioClip[] armor = Array.Empty<AudioClip>();
    public AudioClip[] accessory = Array.Empty<AudioClip>();
    public AudioClip[] potion = Array.Empty<AudioClip>();
    [Tooltip("보류(2026-09-30). 비어 있으면 소리 없음.")]
    public AudioClip[] gold = Array.Empty<AudioClip>();
    public AudioClip[] map = Array.Empty<AudioClip>();
    [Tooltip("가방. 잡템·퀘스트 등 분류 밖 아이템도 이 소리를 쓴다.")]
    public AudioClip[] bag = Array.Empty<AudioClip>();
    [Range(0f, 1f)] public float dropVolume = 0.7f;

    [Header("등장 등급음 — 아이템이 나타날 때 드랍음과 별도로 1회")]
    public AudioClip legendary;
    public AudioClip artifact;
    public AudioClip mythic;
    [Range(0f, 1f)] public float revealVolume = 0.8f;

    [Header("공간·중복")]
    [Range(0f, 1f)] public float spatialBlend = 0.6f;
    [Min(0.1f)] public float minDistance = 3f;
    [Min(0.2f)] public float maxDistance = 30f;
    [Tooltip("같은 종류(또는 같은 등급)가 한꺼번에 떨어질 때 이 간격 안의 재생은 건너뛴다.")]
    [Min(0f)] public float sameCueCooldown = 0.05f;

    public AudioClip[] GetDropClips(ItemDropSfxKind kind)
    {
        switch (kind)
        {
            case ItemDropSfxKind.Weapon: return weapon;
            case ItemDropSfxKind.Armor: return armor;
            case ItemDropSfxKind.Accessory: return accessory;
            case ItemDropSfxKind.Potion: return potion;
            case ItemDropSfxKind.Gold: return gold;
            case ItemDropSfxKind.Map: return map;
            default: return bag;
        }
    }

    public AudioClip GetRevealClip(ItemGrade grade)
    {
        switch (grade)
        {
            case ItemGrade.Legendary: return legendary;
            case ItemGrade.Artifact: return artifact;
            case ItemGrade.Mythic: return mythic;
            default: return null;
        }
    }
}
