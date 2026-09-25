using System;
using UnityEngine;

/// <summary>Display snapshots only. The run owns eligibility, identity, rewards and persistence.</summary>
[Serializable]
public sealed class RunCardPresentation
{
    public string Title;
    public string Description;
    public string Value;
    public Sprite Icon;
    public ItemGrade Grade;
    public bool IsReward;
}

[Serializable]
public sealed class RunTransferPresentation
{
    public string ItemInstanceId;
    public string Name;
    public string Description;
    public Sprite Icon;
    public ItemGrade Grade;
    public int Quantity;
    public bool IsEquipped;
}

public enum RunTransferResult { Success, NoSpace, SaveFailed, StaleItem, Unavailable }
