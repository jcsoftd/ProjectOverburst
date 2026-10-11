using System;
using UnityEngine;

[Serializable]
public sealed class MerchantReputationData
{
    [SerializeField] private string merchantId;
    [SerializeField] private int totalExperience;
    public string MerchantId => merchantId;
    public int Level => MerchantReputationPolicy.Level(totalExperience);
    public int Experience => MerchantReputationPolicy.Experience(totalExperience);
    public int TotalExperience => totalExperience;
    internal static MerchantReputationData Restore(string id, int level, int experience) =>
        RestoreTotal(id, MerchantReputationPolicy.Migrate(level, experience));
    internal static MerchantReputationData RestoreTotal(string id, int total)
    {
        if (string.IsNullOrWhiteSpace(id) || total < 0 || total > MerchantReputationPolicy.MaximumExperience)
            throw new ArgumentException("Invalid merchant reputation snapshot.");
        return new MerchantReputationData(id, 0) { totalExperience = total };
    }
    public MerchantReputationData(string id, int level)
    { merchantId = id; totalExperience = MerchantReputationPolicy.Threshold(level); }
    public bool SetLevel(int value, int maxLevel, int requiredExperience)
    {
        int next = MerchantReputationPolicy.Threshold(value);
        if (next == totalExperience) return false;
        totalExperience = next; return true;
    }
    public bool AddExperience(int amount, int maxLevel, int requiredExperience)
    {
        if (amount <= 0) return false;
        int next = (int)Math.Min(MerchantReputationPolicy.MaximumExperience, (long)totalExperience + amount);
        if (next == totalExperience) return false;
        totalExperience = next; return true;
    }
}
