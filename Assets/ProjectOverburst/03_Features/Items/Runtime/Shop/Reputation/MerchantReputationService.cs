using System;
using System.Collections.Generic;
using UnityEngine;

public class MerchantReputationService : MonoBehaviour
{
    private static MerchantReputationService instance;

    [SerializeField] private MerchantDefinition[] merchantDefinitions;
    [SerializeField] private int maxLevel = 3;
    [SerializeField] private int experienceRequiredPerLevel = 30;
    [SerializeField] private MerchantReputationLevelSetting[] levelSettings;

    private readonly Dictionary<string, MerchantReputationData> reputations = new Dictionary<string, MerchantReputationData>();

    public static event Action<string, int> ReputationChanged;

    public IReadOnlyList<MerchantDefinition> MerchantDefinitions { get { return merchantDefinitions; } }

    private void Awake()
    {
        instance = this;
        EnsureDefaultLevelSettings();
        RegisterMerchants(merchantDefinitions);
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    public static void RegisterMerchants(MerchantDefinition[] definitions)
    {
        MerchantReputationService service = ResolveInstance();
        if (service == null)
            return;

        service.RegisterMerchantDefinitions(definitions);
    }

    public static int GetLevel(MerchantDefinition merchant)
    {
        MerchantReputationService service = ResolveInstance();
        return service != null ? service.GetLevelInternal(merchant) : 0;
    }

    public static float GetDiscountRate(MerchantDefinition merchant)
    {
        MerchantReputationService service = ResolveInstance();
        return service != null ? service.GetSetting(merchant).BuyDiscountRate : 0f;
    }

    public static float GetLevelProgress01(MerchantDefinition merchant)
    {
        MerchantReputationService service = ResolveInstance();
        if (service == null)
            return 0f;

        MerchantReputationData data = service.EnsureData(service.GetMerchantId(merchant));
        if (data == null)
            return 0f;

        if (data.Level >= Mathf.Max(0, service.maxLevel))
            return 1f;

        return Mathf.Clamp01((float)data.Experience / service.GetRequiredExperienceInternal());
    }

    public static int GetExperience(MerchantDefinition merchant)
    {
        MerchantReputationService service = ResolveInstance();
        if (service == null)
            return 0;

        MerchantReputationData data = service.EnsureData(service.GetMerchantId(merchant));
        return data != null ? data.Experience : 0;
    }

    public static int GetRequiredExperience()
    {
        MerchantReputationService service = ResolveInstance();
        return service != null ? service.GetRequiredExperienceInternal() : 30;
    }

    public static void AddReputation(MerchantDefinition merchant, int amount)
    {
        MerchantReputationService service = ResolveInstance();
        if (service != null)
            service.AddReputationInternal(merchant, amount);
    }

    public static void AddReputationToAll(int amount)
    {
        MerchantReputationService service = ResolveInstance();
        if (service != null)
            service.AddReputationToAllInternal(amount);
    }

    public static void AddReputationExperience(MerchantDefinition merchant, int amount)
    {
        MerchantReputationService service = ResolveInstance();
        if (service != null)
            service.AddReputationExperienceInternal(merchant, amount);
    }

    public static void AddReputationExperienceToAll(int amount)
    {
        MerchantReputationService service = ResolveInstance();
        if (service != null)
            service.AddReputationExperienceToAllInternal(amount);
    }

    public static void GetMerchantGoldRange(MerchantDefinition merchant, out int min, out int max, int fallbackMin, int fallbackMax)
    {
        MerchantReputationService service = ResolveInstance();
        if (service == null)
        {
            min = Mathf.Max(0, fallbackMin);
            max = Mathf.Max(min, fallbackMax);
            return;
        }

        MerchantReputationLevelSetting setting = service.GetSetting(merchant);
        min = setting.MerchantGoldMin;
        max = setting.MerchantGoldMax;
    }

    public static void GetGeneralGoodsRange(MerchantDefinition merchant, out int min, out int max, int fallbackMin, int fallbackMax)
    {
        MerchantReputationService service = ResolveInstance();
        if (service == null)
        {
            min = Mathf.Max(1, fallbackMin);
            max = Mathf.Max(min, fallbackMax);
            return;
        }

        MerchantReputationLevelSetting setting = service.GetSetting(merchant);
        min = setting.GeneralGoodsMinTotal;
        max = setting.GeneralGoodsMaxTotal;
    }

    public static void GetComboGemStockRange(MerchantDefinition merchant, out int min, out int max, int fallbackMin, int fallbackMax)
    {
        MerchantReputationService service = ResolveInstance();
        if (service == null)
        {
            min = Mathf.Max(0, fallbackMin);
            max = Mathf.Max(min, fallbackMax);
            return;
        }

        MerchantReputationLevelSetting setting = service.GetSetting(merchant);
        min = setting.ComboGemMinStockCount;
        max = setting.ComboGemMaxStockCount;
    }

    public static float GetComboGemUncommonChance(MerchantDefinition merchant, float fallback)
    {
        MerchantReputationService service = ResolveInstance();
        return service != null ? service.GetSetting(merchant).ComboGemUncommonChance : Mathf.Clamp01(fallback);
    }

    private static MerchantReputationService ResolveInstance()
    {
        if (instance != null)
            return instance;

        instance = FindFirstObjectByType<MerchantReputationService>(FindObjectsInactive.Include);
        if (instance != null)
            instance.EnsureDefaultLevelSettings();

        return instance;
    }

    private void RegisterMerchantDefinitions(MerchantDefinition[] definitions)
    {
        if (definitions == null)
            return;

        for (int i = 0; i < definitions.Length; i++)
            EnsureData(GetMerchantId(definitions[i]));
    }

    private int GetLevelInternal(MerchantDefinition merchant)
    {
        MerchantReputationData data = EnsureData(GetMerchantId(merchant));
        return data != null ? data.Level : 0;
    }

    private void AddReputationInternal(MerchantDefinition merchant, int amount)
    {
        MerchantReputationData data = EnsureData(GetMerchantId(merchant));
        if (data == null)
            return;

        int previous = data.Level;
        if (data.SetLevel(previous + amount, maxLevel, GetRequiredExperienceInternal()))
            ReputationChanged?.Invoke(data.MerchantId, data.Level);
    }

    private void AddReputationToAllInternal(int amount)
    {
        RegisterMerchantDefinitions(merchantDefinitions);
        if (merchantDefinitions == null || merchantDefinitions.Length == 0)
            return;

        for (int i = 0; i < merchantDefinitions.Length; i++)
            AddReputationInternal(merchantDefinitions[i], amount);
    }

    private void AddReputationExperienceInternal(MerchantDefinition merchant, int amount)
    {
        MerchantReputationData data = EnsureData(GetMerchantId(merchant));
        if (data == null)
            return;

        if (data.AddExperience(amount, maxLevel, GetRequiredExperienceInternal()))
            ReputationChanged?.Invoke(data.MerchantId, data.Level);
    }

    private void AddReputationExperienceToAllInternal(int amount)
    {
        RegisterMerchantDefinitions(merchantDefinitions);
        if (merchantDefinitions == null || merchantDefinitions.Length == 0)
            return;

        for (int i = 0; i < merchantDefinitions.Length; i++)
            AddReputationExperienceInternal(merchantDefinitions[i], amount);
    }

    private MerchantReputationLevelSetting GetSetting(MerchantDefinition merchant)
    {
        EnsureDefaultLevelSettings();
        int level = GetLevelInternal(merchant);
        for (int i = 0; i < levelSettings.Length; i++)
        {
            if (levelSettings[i] != null && levelSettings[i].Level == level)
                return levelSettings[i];
        }

        return levelSettings[0];
    }

    private MerchantReputationData EnsureData(string merchantId)
    {
        if (string.IsNullOrWhiteSpace(merchantId))
            return null;

        MerchantReputationData data;
        if (!reputations.TryGetValue(merchantId, out data) || data == null)
        {
            data = new MerchantReputationData(merchantId, 0);
            reputations[merchantId] = data;
        }

        return data;
    }

    private string GetMerchantId(MerchantDefinition merchant)
    {
        return merchant != null ? merchant.name : string.Empty;
    }

    private void EnsureDefaultLevelSettings()
    {
        maxLevel = Mathf.Max(0, maxLevel);
        experienceRequiredPerLevel = Mathf.Max(1, experienceRequiredPerLevel);
        if (HasCompleteDefaultLevelSettings())
            return;

        levelSettings = new[]
        {
            new MerchantReputationLevelSetting(0, 0f, 1000, 2000, 15, 29, 10, 15, 0.5f),
            new MerchantReputationLevelSetting(1, 0.05f, 1200, 2400, 18, 32, 12, 17, 0.5f),
            new MerchantReputationLevelSetting(2, 0.10f, 1500, 3000, 22, 36, 15, 20, 0.7f),
            new MerchantReputationLevelSetting(3, 0.10f, 2000, 4000, 25, 40, 18, 22, 0.8f)
        };
    }

    private int GetRequiredExperienceInternal()
    {
        experienceRequiredPerLevel = Mathf.Max(1, experienceRequiredPerLevel);
        return experienceRequiredPerLevel;
    }

    private bool HasCompleteDefaultLevelSettings()
    {
        if (levelSettings == null || levelSettings.Length < 4)
            return false;

        bool hasLevel0 = false;
        bool hasLevel1 = false;
        bool hasLevel2 = false;
        bool hasLevel3 = false;
        for (int i = 0; i < levelSettings.Length; i++)
        {
            MerchantReputationLevelSetting setting = levelSettings[i];
            if (setting == null)
                return false;

            switch (setting.Level)
            {
                case 0:
                    hasLevel0 = true;
                    break;
                case 1:
                    hasLevel1 = true;
                    break;
                case 2:
                    hasLevel2 = true;
                    break;
                case 3:
                    hasLevel3 = true;
                    break;
            }
        }

        return hasLevel0 && hasLevel1 && hasLevel2 && hasLevel3;
    }
}
