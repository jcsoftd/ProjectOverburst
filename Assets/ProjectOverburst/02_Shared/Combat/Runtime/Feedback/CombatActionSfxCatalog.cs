using System;
using UnityEngine;

// CombatActionSfxService의 칸 이름 → 공급사 원본(ThirdParty) 클립 직접 참조. 원본을 복사하지 않는다.
// 여기에 없는 이름은 기존처럼 Resources/Combat/SFX/CombatAction/<이름>에서 읽는다.
[CreateAssetMenu(fileName = "CombatActionSfxCatalog", menuName = "OVERBURST/Combat/Combat Action SFX Catalog")]
public sealed class CombatActionSfxCatalog : ScriptableObject
{
    public const string ResourcePath = "Combat/SFX/CombatActionSfxCatalog";

    [Serializable]
    public sealed class Entry
    {
        public string name;
        public AudioClip clip;
    }

    public Entry[] entries = Array.Empty<Entry>();

    public AudioClip Find(string clipName)
    {
        if (entries == null) return null;
        for (int i = 0; i < entries.Length; i++)
        {
            Entry entry = entries[i];
            if (entry != null && entry.clip != null && entry.name == clipName) return entry.clip;
        }
        return null;
    }
}
