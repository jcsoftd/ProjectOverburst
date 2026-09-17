using UnityEngine;

public sealed class BuffInstance
{
    public BuffDefinition Definition { get; private set; }
    public float RemainingTime { get; private set; }
    private float tickTimer;

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

    public BuffInstance(BuffDefinition definition)
    {
        Definition = definition;
        Refresh();
    }

    public void Refresh()
    {
        if (Definition == null)
            return;

        Definition.Normalize();
        RemainingTime = Definition.duration;
        tickTimer = Definition.tickInterval; // 첫 tick 지연
    }

    public bool Tick(float deltaTime)
    {
        if (Definition == null)
            return false;

        RemainingTime -= Mathf.Max(0f, deltaTime);
        tickTimer -= Mathf.Max(0f, deltaTime);

        if (tickTimer > 0f)
            return false;

        tickTimer += Definition.tickInterval;
        return RemainingTime > 0f;
    }

    public bool IsExpired()
    {
        return RemainingTime <= 0f;
    }
}
