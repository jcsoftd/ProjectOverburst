using System;
using UnityEngine;

// 몬스터 테마 → 대표 보스. 등록되지 않은 테마는 지도 던전의 기존 임시 캡슐 보스 경로를 그대로 쓴다.
[CreateAssetMenu(menuName = "OVERBURST/Enemies/Boss Roster", fileName = "EnemyBossRoster")]
public sealed class EnemyBossRoster : ScriptableObject
{
    public const string ResourcePath = "Enemies/Bosses/EnemyBossRoster";

    [Serializable]
    public sealed class Entry
    {
        public string themeId;
        public EnemyDefinition boss;
        [Tooltip("스폰 서비스에 추가 등록할 보스 전용 카탈로그(테마 카탈로그는 바꾸지 않는다)")]
        public EnemyCatalog catalog;
        public bool IsValid => !string.IsNullOrWhiteSpace(themeId) && boss != null && boss.IsValid
            && catalog != null && catalog.TryGet(boss.EnemyId, out var registered) && registered == boss;
    }

    public Entry[] entries = Array.Empty<Entry>();

    private static EnemyBossRoster current;
    private static bool loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { current = null; loaded = false; }

    public static EnemyBossRoster Current
    {
        get
        {
            if (!loaded) { current = Resources.Load<EnemyBossRoster>(ResourcePath); loaded = true; }
            return current;
        }
    }

    public static Entry Resolve(string themeId)
    {
        var roster = Current;
        if (roster == null || roster.entries == null || string.IsNullOrWhiteSpace(themeId)) return null;
        foreach (var entry in roster.entries)
            if (entry != null && entry.themeId == themeId && entry.IsValid) return entry;
        return null;
    }
}
