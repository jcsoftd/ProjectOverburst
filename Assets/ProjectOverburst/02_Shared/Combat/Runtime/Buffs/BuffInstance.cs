using UnityEngine;

public sealed class BuffInstance
{
    private static long nextInstanceId;
    internal long InstanceId { get; } = System.Threading.Interlocked.Increment(ref nextInstanceId);
    public BuffDefinition Definition { get; private set; }
    public float RemainingTime => (float)System.Math.Max(0d, expiresAt - elapsed);
    public int StackCount { get; private set; } = 1;
    public GameObject Source { get; private set; }
    internal int Revision { get; private set; }
    private double elapsed, expiresAt, nextTickAt;
    internal double NextTickDelay => nextTickAt - elapsed;
    internal double ExpiryDelay => expiresAt - elapsed;

    public BuffSnapshot Snapshot => new BuffSnapshot(this);

    public string BuffId { get { return Definition != null ? Definition.buffId : string.Empty; } }
    public float RemainingRatio
    {
        get
        {
            if (Definition == null || Definition.duration <= 0f)
                return 0f;

            return Mathf.Clamp01(RemainingTime / Definition.duration);
        }
    }

    public BuffInstance(BuffDefinition definition, GameObject source = null)
    {
        Definition = definition?.Clone();
        Definition?.Normalize();
        Source = source;
        Refresh();
    }

    public void Refresh()
    {
        if (Definition == null)
            return;

        Revision++;
        expiresAt = elapsed + Definition.duration;
        if (Definition.resetTickOnRefresh || nextTickAt == 0d)
            nextTickAt = elapsed + Definition.tickInterval;
    }

    internal BuffApplyResult Reapply(BuffDefinition definition, GameObject source)
    {
        if (definition.stackingPolicy == BuffStackingPolicy.ReplaceStronger && definition.strength < Definition.strength)
            return BuffApplyResult.Rejected;
        bool stacked = definition.stackingPolicy == BuffStackingPolicy.StackAndRefresh;
        StackCount = stacked ? (int)System.Math.Min((long)StackCount + 1, definition.maxStacks) : 1;
        Definition = definition;
        Source = source;
        Refresh();
        return stacked ? BuffApplyResult.Stacked : BuffApplyResult.Refreshed;
    }

    public bool Tick(float deltaTime)
    {
        return Advance(deltaTime) > 0;
    }

    internal int Advance(float deltaTime)
    {
        if (Definition == null || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            return 0;
        elapsed += deltaTime;
        double end = System.Math.Min(elapsed, expiresAt);
        if (nextTickAt > end || nextTickAt >= expiresAt) return 0;
        double due = System.Math.Floor((end - nextTickAt) / Definition.tickInterval) + 1d;
        // Expiry wins when a tick falls exactly on the deadline.
        if (nextTickAt + (due - 1d) * Definition.tickInterval >= expiresAt) due--;
        int ticks = (int)System.Math.Min(int.MaxValue, System.Math.Max(0d, due));
        nextTickAt += ticks * (double)Definition.tickInterval;
        return ticks;
    }

    public bool IsExpired()
    {
        return RemainingTime <= 0f;
    }
}

// Values only: consumers cannot advance the clock or edit a shared definition.
public readonly struct BuffSnapshot
{
    public readonly long InstanceId;
    public readonly int Revision;
    public readonly string BuffId;
    public readonly float RemainingTime, Duration, TickInterval, HealPercentPerTick, DamagePerTick, MoveSpeedMultiplier;
    public readonly int StackCount;
    public readonly bool IsDebuff;

    public BuffSnapshot(BuffInstance instance)
    {
        var definition = instance.Definition;
        InstanceId = instance.InstanceId;
        Revision = instance.Revision;
        BuffId = instance.BuffId;
        RemainingTime = instance.RemainingTime;
        Duration = definition.duration;
        TickInterval = definition.tickInterval;
        StackCount = instance.StackCount;
        IsDebuff = definition.isDebuff;
        HealPercentPerTick = (float)System.Math.Min(float.MaxValue, (double)definition.healPercentPerTick * StackCount);
        DamagePerTick = (float)System.Math.Min(float.MaxValue, (double)definition.damagePerTick * StackCount);
        MoveSpeedMultiplier = Mathf.Clamp(1f + (definition.moveSpeedMultiplier - 1f) * StackCount, 0.05f, 10f);
    }
}
