using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class ElementalReactionVfxCatalogEntry
{
    [SerializeField, InspectorName("슬롯 식별자")]
    private string id;
    [SerializeField, InspectorName("반응 타입")]
    private ElementalReactionType reactionType;
    [SerializeField, InspectorName("슬롯 타입")]
    private ElementalReactionVfxSlotType slotType;
    [SerializeField, InspectorName("Wrapper 프리팹")]
    private GameObject prefab;

    public string Id => id ?? string.Empty;
    public ElementalReactionType ReactionType => reactionType;
    public ElementalReactionVfxSlotType SlotType => slotType;
    public GameObject Prefab => prefab;

    public ElementalReactionVfxCatalogEntry(
        string configuredId,
        ElementalReactionType configuredReactionType,
        ElementalReactionVfxSlotType configuredSlotType,
        GameObject configuredPrefab)
    {
        id = configuredId ?? string.Empty;
        reactionType = configuredReactionType;
        slotType = configuredSlotType;
        prefab = configuredPrefab;
    }

    public bool AssignPrefabIfMissing(GameObject configuredPrefab)
    {
        if (prefab != null || configuredPrefab == null)
            return false;

        prefab = configuredPrefab;
        return true;
    }
}

[CreateAssetMenu(
    fileName = "ElementalReactionVfxCatalog",
    menuName = "OVERBURST/Combat/Elemental Reaction VFX Catalog")]
public sealed class ElementalReactionVfxCatalog : ScriptableObject
{
    public const string ResourcePath = "Combat/VFX/ElementalReactionVfxCatalog";

    [SerializeField, InspectorName("런타임 소비자 연결됨")]
    private bool runtimeConsumerConnected;
    [SerializeField, InspectorName("반응 VFX Wrapper 목록")]
    private List<ElementalReactionVfxCatalogEntry> entries = new List<ElementalReactionVfxCatalogEntry>();

    public bool RuntimeConsumerConnected => runtimeConsumerConnected;
    public IReadOnlyList<ElementalReactionVfxCatalogEntry> Entries => entries;

    public bool TryFind(string id, out ElementalReactionVfxCatalogEntry entry)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            ElementalReactionVfxCatalogEntry candidate = entries[i];
            if (candidate != null && string.Equals(candidate.Id, id, StringComparison.Ordinal))
            {
                entry = candidate;
                return true;
            }
        }

        entry = null;
        return false;
    }

    public bool EnsureAuthoringEntry(
        string id,
        ElementalReactionType reactionType,
        ElementalReactionVfxSlotType slotType,
        GameObject prefab)
    {
        if (TryFind(id, out ElementalReactionVfxCatalogEntry existing))
            return existing.AssignPrefabIfMissing(prefab); // 수동 참조는 덮어쓰지 않음

        entries.Add(new ElementalReactionVfxCatalogEntry(id, reactionType, slotType, prefab));
        return true;
    }

    public bool RemoveAuthoringEntriesNotIn(ISet<string> retainedIds)
    {
        if (retainedIds == null)
            throw new ArgumentNullException(nameof(retainedIds));

        bool changed = false;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            ElementalReactionVfxCatalogEntry entry = entries[i];
            if (entry != null && retainedIds.Contains(entry.Id))
                continue;

            entries.RemoveAt(i); // 폐기 슬롯 정리
            changed = true;
        }

        return changed;
    }

    public bool SetRuntimeConsumerConnected(bool connected)
    {
        if (runtimeConsumerConnected == connected)
            return false;

        runtimeConsumerConnected = connected;
        return true;
    }
}
