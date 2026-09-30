using UnityEngine;

public enum MeleeElementStatusAuraType
{
    Burning = 0,
    Shocked = 1,
    Chilled = 2,
    Corroded = 3
}

[DisallowMultipleComponent]
[DefaultExecutionOrder(-10000)]
public sealed class MeleeElementStatusAuraController : MonoBehaviour, ITransientVfxPlayback
{
    private const int AuraTypeCount = 4;
    private readonly bool[] logicalStates = new bool[AuraTypeCount];
    private readonly int[] stackCounts = new int[AuraTypeCount];
    private int logicalActiveCount;
    private int schedulerIndex = -1;
    private CombatTarget cachedCombatTarget;
    private MeleeElementStatusAuraPresentation presentation;

    internal bool HasLogicalAura => logicalActiveCount > 0;
    internal bool IsPresentationVisible => presentation != null;
    internal int SchedulerIndex => schedulerIndex;
    internal Vector3 VisibilityPosition => cachedCombatTarget != null
        ? cachedCombatTarget.WorldCenter
        : transform.position;

#if UNITY_EDITOR
    public int VisibilityCheckCountForValidation { get; private set; }
    public bool PresentationVisibleForValidation => presentation != null;
    public int SchedulerIndexForValidation => schedulerIndex;
    public MeleeElementStatusAuraPresentation PresentationForValidation => presentation;
#endif

    private void Awake()
    {
        cachedCombatTarget = GetComponent<CombatTarget>();
        ClearAllAuras();
    }

    private void OnDisable()
    {
        ClearAllAuras();
    }

    public bool SetAuraActive(
        MeleeElementStatusAuraType auraType,
        bool active,
        bool restartIfAlreadyActive = true)
    {
        int index = (int)auraType;
        if (index < 0 || index >= logicalStates.Length)
            return false;

        bool wasActive = logicalStates[index];
        if (!active) stackCounts[index] = 0;
        else if (stackCounts[index] == 0) stackCounts[index] = 1;
        if (wasActive != active)
        {
            logicalStates[index] = active;
            logicalActiveCount += active ? 1 : -1;
        }

        if (!active)
        {
            if (presentation != null)
                presentation.SetAuraActive(auraType, false, false);
            UnregisterIfIdle();
            return true;
        }

        MeleeElementStatusAuraVisibilityScheduler.Register(this);
        if (presentation != null)
        {
            presentation.SetStackCount(auraType,stackCounts[index]);
            presentation.SetAuraActive(auraType, true, !wasActive || restartIfAlreadyActive);
        }
        return true;
    }

    public void SetStackCount(MeleeElementStatusAuraType type, int count)
    {
        int index=(int)type;
        if(index<0||index>=AuraTypeCount)return;
        count=Mathf.Clamp(count,0,5);
        if(stackCounts[index]==count&&logicalStates[index]==(count>0))return;
        stackCounts[index]=count;
        presentation?.SetStackCount(type,count);
        SetAuraActive(type,count>0,false);
    }

    public bool StartAura(MeleeElementStatusAuraType auraType)
    {
        return SetAuraActive(auraType, true, false);
    }

    public bool RefreshAuraLifetime(MeleeElementStatusAuraType auraType)
    {
        int index = (int)auraType;
        return index >= 0 && index < logicalStates.Length && logicalStates[index];
    }

    public bool RefreshAura(MeleeElementStatusAuraType auraType)
    {
        return RefreshAuraLifetime(auraType);
    }

    public bool IsAuraActive(MeleeElementStatusAuraType auraType)
    {
        int index = (int)auraType;
        return index >= 0 && index < logicalStates.Length && logicalStates[index];
    }

    public bool ClearAura(MeleeElementStatusAuraType auraType)
    {
        return SetAuraActive(auraType, false, false);
    }

    public void ClearAllAuras()
    {
        for (int i = 0; i < logicalStates.Length; i++)
        {
            logicalStates[i] = false;
            stackCounts[i] = 0;
        }

        logicalActiveCount = 0;
        MeleeElementStatusAuraVisibilityScheduler.Unregister(this);
        ReleasePresentation();
    }

    public void RestartVfx()
    {
        if (presentation != null)
            presentation.RestartActiveAuras();
    }

    public void StopAndClearVfx()
    {
        ClearAllAuras();
    }

    internal void SetSchedulerIndex(int index)
    {
        schedulerIndex = index;
    }

    internal void ApplyScheduledVisibility(bool visible)
    {
#if UNITY_EDITOR
        VisibilityCheckCountForValidation++;
#endif
        if (!visible)
        {
            ReleasePresentation();
            return;
        }

        if (presentation != null || !HasLogicalAura)
            return;

        MeleeElementStatusAuraPresentation leased =
            MeleeElementStatusAuraVisibilityScheduler.TryLeasePresentation(this);
        if (leased == null)
            return;

        presentation = leased;
        for (int i = 0; i < logicalStates.Length; i++)
        {
            if (logicalStates[i])
            {
                presentation.SetStackCount((MeleeElementStatusAuraType)i,stackCounts[i]);
                presentation.SetAuraActive((MeleeElementStatusAuraType)i, true, false);
            }
        }
    }

    internal void ReleasePresentation()
    {
        if (presentation == null)
            return;

        MeleeElementStatusAuraPresentation released = presentation;
        presentation = null;
        MeleeElementStatusAuraVisibilityScheduler.ReleasePresentation(released);
    }

    private void UnregisterIfIdle()
    {
        if (logicalActiveCount > 0)
            return;

        MeleeElementStatusAuraVisibilityScheduler.Unregister(this);
        ReleasePresentation();
    }

#if UNITY_EDITOR
    public void DisableForValidation()
    {
        OnDisable();
    }
#endif
}
