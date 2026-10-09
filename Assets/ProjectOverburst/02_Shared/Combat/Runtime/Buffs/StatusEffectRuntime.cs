using System;
using System.Collections.Generic;
using UnityEngine;

public enum BuffApplyResult { Rejected, Applied, Refreshed, Stacked }

// Owns time and membership only; an actor adapter supplies tick execution.
public sealed class StatusEffectRuntime
{
    private readonly List<BuffInstance> active = new List<BuffInstance>();
    private readonly List<PendingAdvance> advancing = new List<PendingAdvance>();
    private struct PendingAdvance
    {
        public readonly BuffInstance Instance;
        public readonly int Revision;
        public readonly BuffSnapshot Snapshot;
        public readonly GameObject Source;
        public readonly double Interval, ExpiryOffset;
        public int Ticks;
        public double NextTickOffset;
        public bool Expires;
        public PendingAdvance(BuffInstance instance, int ticks, double nextTickOffset, double expiryOffset)
        {
            Instance = instance; Ticks = ticks; Revision = instance.Revision;
            Snapshot = instance.Snapshot; Source = instance.Source;
            Interval = Snapshot.TickInterval; NextTickOffset = nextTickOffset;
            ExpiryOffset = expiryOffset; Expires = instance.IsExpired();
        }
    }
    private bool isAdvancing;
    public int LifecycleVersion { get; private set; }
    public event Action Changed;

    public BuffApplyResult TryApply(BuffDefinition definition, out BuffInstance instance, GameObject source = null)
    {
        instance = null;
        if (definition == null || string.IsNullOrWhiteSpace(definition.buffId)) return BuffApplyResult.Rejected;
        var copy = definition.Clone();
        copy.Normalize();
        instance = Find(copy.buffId);
        BuffApplyResult result;
        if (instance == null)
        {
            // A fresh application supersedes pending ticks from an expired entry of the same ID.
            for (int i = active.Count - 1; i >= 0; i--)
                if (active[i].BuffId == copy.buffId) active.RemoveAt(i);
            instance = new BuffInstance(copy, source);
            active.Add(instance);
            result = BuffApplyResult.Applied;
        }
        else result = instance.Reapply(copy, source);
        if (result != BuffApplyResult.Rejected) NotifyChanged();
        return result;
    }

    public BuffInstance Find(string id)
    {
        for (int i = 0; i < active.Count; i++)
            if (active[i].BuffId == id && !active[i].IsExpired()) return active[i];
        return null;
    }

    public bool Remove(BuffInstance handle)
    {
        if (handle == null || !active.Remove(handle)) return false;
        NotifyChanged();
        return true;
    }

    public bool Remove(string id)
    {
        foreach (var instance in active)
            if (instance.BuffId == id) return Remove(instance);
        return false;
    }

    // Expiring entries remain current until their earlier due ticks finish executing.
    public bool IsCurrent(BuffSnapshot snapshot)
    {
        foreach (var instance in active)
            if (instance.InstanceId == snapshot.InstanceId && instance.Revision == snapshot.Revision) return true;
        return false;
    }

    public void Clear()
    {
        LifecycleVersion++;
        if (active.Count == 0) return;
        active.Clear();
        NotifyChanged();
    }

    public int GetActiveBuffs(List<BuffInstance> results)
    {
        if (results == null) return 0;
        results.Clear();
        foreach (var instance in active)
            if (!instance.IsExpired()) results.Add(instance);
        return results.Count;
    }

    public int GetSnapshots(List<BuffSnapshot> results)
    {
        if (results == null) return 0;
        results.Clear();
        foreach (var instance in active)
            if (!instance.IsExpired()) results.Add(instance.Snapshot);
        return results.Count;
    }

    public float MoveSpeedMultiplier(float slowResistance = 0f)
    {
        double multiplier = 1d;
        slowResistance = float.IsNaN(slowResistance) ? 0f : Mathf.Clamp01(slowResistance);
        foreach (var instance in active)
        {
            if (instance.IsExpired()) continue;
            float speed = instance.Snapshot.MoveSpeedMultiplier;
            if (speed < 1f) speed = 1f - (1f - speed) * (1f - slowResistance);
            multiplier *= speed;
        }
        // Actor adapters apply their final cap after other sources (e.g. flasks).
        return (float)Math.Min(float.MaxValue, multiplier);
    }

    public void Advance(float deltaTime, Action<BuffSnapshot, GameObject, int> executeTick = null)
    {
        if (isAdvancing || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
        isAdvancing = true;
        int lifecycle = LifecycleVersion;
        try
        {
            // Advance every clock before callbacks so reapplication starts at the same frame time.
            foreach (var instance in active)
            {
                double tickOffset = instance.NextTickDelay, expiryOffset = instance.ExpiryDelay;
                int ticks = instance.Advance(deltaTime);
                advancing.Add(new PendingAdvance(instance, executeTick == null ? 0 : ticks, tickOffset, expiryOffset));
            }
            while (lifecycle == LifecycleVersion)
            {
                int next = -1; double earliest = double.PositiveInfinity; bool expireNext = false;
                for (int i = 0; i < advancing.Count; i++)
                {
                    var candidate = advancing[i];
                    if (!active.Contains(candidate.Instance) || candidate.Instance.Revision != candidate.Revision) continue;
                    bool expire = candidate.Expires && (candidate.Ticks == 0 || candidate.ExpiryOffset <= candidate.NextTickOffset);
                    double offset = expire ? candidate.ExpiryOffset : candidate.Ticks > 0 ? candidate.NextTickOffset : double.PositiveInfinity;
                    // At equal times, expiration precedes ticks; tick ties retain application order.
                    if (offset < earliest || (offset == earliest && expire && !expireNext))
                    { next = i; earliest = offset; expireNext = expire; }
                }
                if (next < 0) break;
                var pending = advancing[next];
                if (expireNext)
                {
                    pending.Expires = false; pending.Ticks = 0; advancing[next] = pending;
                    Remove(pending.Instance);
                }
                else
                {
                    pending.Ticks--; pending.NextTickOffset += pending.Interval; advancing[next] = pending;
                    try { executeTick(pending.Snapshot, pending.Source, 1); }
                    catch (Exception error)
                    { pending.Ticks = 0; advancing[next] = pending; Debug.LogException(error); }
                }
            }
        }
        finally { advancing.Clear(); isAdvancing = false; }
    }

    private void NotifyChanged()
    {
        if (Changed == null) return;
        foreach (Action observer in Changed.GetInvocationList())
        {
            try { observer(); }
            catch (Exception error) { Debug.LogException(error); }
        }
    }
}
