using System;
using System.Collections.Generic;
using UnityEngine;

public enum EnemyThemeTier { Small, Medium, Elite }

[CreateAssetMenu(menuName = "OVERBURST/Enemies/Theme Table", fileName = "ETT_Theme")]
public sealed class EnemyThemeTable : ScriptableObject
{
    [Serializable] public struct Entry
    {
        public EnemyDefinition definition;
        public EnemyThemeTier tier;
        [Min(.01f)] public float weight;
    }
    [SerializeField] private string themeId;
    [SerializeField] private string displayName;
    [SerializeField] private Entry[] entries;
    [SerializeField] private EnemyCatalog catalog;
    [SerializeField] private Color accent = Color.cyan;
    public string ThemeId => themeId;
    public string DisplayName => displayName;
    public Color Accent => accent;
    public EnemyCatalog Catalog => catalog;
    public IReadOnlyList<Entry> Entries => entries ?? Array.Empty<Entry>();

    public void Configure(string id, string label, EnemyCatalog sourceCatalog, Color color, Entry[] roster)
    {
        themeId = id; displayName = label; catalog = sourceCatalog; accent = color;
        entries = roster != null ? (Entry[])roster.Clone() : Array.Empty<Entry>();
    }

    public bool Validate(out string reason)
    {
        if (string.IsNullOrWhiteSpace(themeId) || catalog == null || !catalog.Validate(out reason))
        { reason = "테마 ID 또는 카탈로그가 유효하지 않습니다."; return false; }
        var ids = new HashSet<string>();
        int[] tiers = new int[3];
        foreach (var entry in Entries)
        {
            if (entry.definition == null || !entry.definition.IsValid || entry.weight <= 0 || float.IsNaN(entry.weight)
                || float.IsInfinity(entry.weight) || (int)entry.tier < 0 || (int)entry.tier > 2
                || !catalog.TryGet(entry.definition.EnemyId, out var registered) || registered != entry.definition
                || !ids.Add(entry.definition.EnemyId))
            { reason = "중복/누락된 몬스터 또는 잘못된 가중치가 있습니다."; return false; }
            tiers[(int)entry.tier]++;
        }
        if (Array.Exists(tiers, n => n == 0)) { reason = "소형·중형·정예를 모두 지정해야 합니다."; return false; }
        reason = string.Empty; return true;
    }

    // Largest-remainder allocation: exact tier totals, repeatable seed, no rare-elite roulette in a 50 test.
    public List<EnemyDefinition> BuildRoster(int small, int medium, int elite, int seed)
    {
        if (!Validate(out string reason)) throw new InvalidOperationException(reason);
        if (small < 0 || medium < 0 || elite < 0 || (long)small + medium + elite > 500)
            throw new ArgumentOutOfRangeException(nameof(small), "한 요청은 0~500마리 범위여야 합니다.");
        var result = new List<EnemyDefinition>(small + medium + elite);
        var random = new System.Random(seed);
        int[] totals = { small, medium, elite };
        for (int tier = 0; tier < 3; tier++)
        {
            var pool = new List<Entry>();
            double weight = 0;
            foreach (var entry in Entries) if ((int)entry.tier == tier) { pool.Add(entry); weight += entry.weight; }
            int[] counts = new int[pool.Count]; double[] remainders = new double[pool.Count]; int assigned = 0;
            for (int i = 0; i < pool.Count; i++)
            { double exact = totals[tier] * pool[i].weight / weight; counts[i] = (int)Math.Floor(exact); remainders[i] = exact - counts[i]; assigned += counts[i]; }
            while (assigned < totals[tier])
            { int best = 0; for (int i = 1; i < pool.Count; i++) if (remainders[i] > remainders[best]) best = i; counts[best]++; remainders[best] = -1; assigned++; }
            var slice = new List<EnemyDefinition>();
            for (int i = 0; i < pool.Count; i++) for (int j = 0; j < counts[i]; j++) slice.Add(pool[i].definition);
            for (int i = slice.Count - 1; i > 0; i--) { int j = random.Next(i + 1); var swap = slice[i]; slice[i] = slice[j]; slice[j] = swap; }
            result.AddRange(slice);
        }
        return result;
    }
}
