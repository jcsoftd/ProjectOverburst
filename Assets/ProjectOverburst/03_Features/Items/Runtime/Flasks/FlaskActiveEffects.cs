using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class FlaskActiveEffects
{
    private sealed class Entry
    {
        public string itemId;
        public FlaskItemData data;
        public FlaskStats stats;
        public float start, end;
    }
    private readonly List<Entry> entries = new List<Entry>(3);
    public int Count => entries.Count;
    public bool PassesEnemyBodies
    {
        get { foreach (Entry e in entries) if (e.data.passesEnemyBodies) return true; return false; }
    }

    public bool Contains(string itemId)
    { foreach (Entry e in entries) if (e.itemId == itemId) return true; return false; }

    public bool Add(string itemId, FlaskItemData data, FlaskStats stats, float now)
    {
        if (data == null || string.IsNullOrEmpty(itemId) || entries.Count >= 3) return false;
        foreach (Entry e in entries) if (e.data.kind == data.kind || e.itemId == itemId) return false;
        entries.Add(new Entry { itemId = itemId, data = data, stats = stats, start = now, end = now + stats.duration });
        return true;
    }

    public float Get(FlaskEffect effect)
    {
        float total = 0f;
        foreach (Entry e in entries)
        {
            if (e.data.primaryEffect == effect) total += e.stats.primary;
            if (e.data.secondaryEffect == effect) total += e.stats.secondary;
        }
        return total;
    }

    public float Remaining(string itemId, float now)
    { foreach (Entry e in entries) if (e.itemId == itemId) return Mathf.Max(0f, e.end - now); return 0f; }

    // Integrate only the actual active portion of this frame, including a partial last frame.
    public float Advance(float previous, float now, out bool changed)
    {
        changed = false;
        float heal = 0f;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            Entry e = entries[i];
            float elapsed = Mathf.Max(0f, Mathf.Min(now, e.end) - Mathf.Max(previous, e.start));
            if (e.data.primaryEffect == FlaskEffect.HealPerSecond) heal += e.stats.primary * elapsed;
            if (e.data.secondaryEffect == FlaskEffect.HealPerSecond) heal += e.stats.secondary * elapsed;
            if (now >= e.end) { entries.RemoveAt(i); changed = true; }
        }
        return heal;
    }

    public bool Remove(string itemId) => entries.RemoveAll(e => e.itemId == itemId) > 0;
    public void Clear() { entries.Clear(); }
}
