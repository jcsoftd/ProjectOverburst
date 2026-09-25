using UnityEngine;

[System.Serializable]
public sealed class MerchantReputationData
{
    [SerializeField] private string merchantId;
    [SerializeField] private int level;
    [SerializeField] private int experience;

    public string MerchantId { get { return merchantId; } }
    public int Level { get { return level; } }
    public int Experience { get { return experience; } }

    internal static MerchantReputationData Restore(string merchantId, int level, int experience)
    {
        if (string.IsNullOrEmpty(merchantId) || level < 0 || experience < 0) throw new System.ArgumentException("Invalid merchant reputation snapshot.");
        return new MerchantReputationData(merchantId, level) { experience = experience };
    }

    public MerchantReputationData(string merchantId, int level)
    {
        this.merchantId = merchantId;
        this.level = Mathf.Max(0, level);
        experience = 0;
    }

    public bool SetLevel(int value, int maxLevel, int requiredExperience)
    {
        int cappedMaxLevel = Mathf.Max(0, maxLevel);
        int clampedLevel = Mathf.Clamp(value, 0, cappedMaxLevel);
        int nextExperience = clampedLevel >= cappedMaxLevel ? Mathf.Max(1, requiredExperience) : 0;
        bool changed = level != clampedLevel || experience != nextExperience;
        level = clampedLevel;
        experience = nextExperience;
        return changed;
    }

    public bool AddExperience(int amount, int maxLevel, int requiredExperience)
    {
        if (amount <= 0)
            return false;

        int cappedMaxLevel = Mathf.Max(0, maxLevel);
        int required = Mathf.Max(1, requiredExperience);
        int previousLevel = level;
        int previousExperience = experience;

        if (level >= cappedMaxLevel)
        {
            experience = required;
            return previousExperience != experience;
        }

        experience += amount;
        while (level < cappedMaxLevel && experience >= required)
        {
            experience -= required;
            level++;
        }

        if (level >= cappedMaxLevel)
            experience = required;

        return previousLevel != level || previousExperience != experience;
    }
}
