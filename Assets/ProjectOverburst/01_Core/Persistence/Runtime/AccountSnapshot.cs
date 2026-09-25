using System;
using System.Collections.Generic;

namespace Overburst.Persistence
{
    [Serializable]
    public sealed class AccountSnapshot
    {
        public int schemaVersion = 1;
        public string profileId = "default";
        public long revision;
        public long nextAcquisitionOrder = 1;
        public int level = 1;
        public int experience;
        public int inventoryCapacity = 35;
        public int unlockedSlots = 16;
        public int baseUnlockedSlots = 16;
        public int stashCapacity = 63;
        public int currentStashTab;
        public int activeWeaponSlot;
        public List<ItemSnapshot> items = new List<ItemSnapshot>();
        public List<string> inventory = new List<string>();
        public List<ItemContainerSnapshot> stashTabs = new List<ItemContainerSnapshot>();
        public List<string> weapons = new List<string>();
        public List<string> gear = new List<string>();
        public List<string> bags = new List<string>();
        public List<string> flasks = new List<string>();
        public List<QuickSlotSnapshot> quickSlots = new List<QuickSlotSnapshot>();
        public List<MerchantSnapshot> merchants = new List<MerchantSnapshot>();
        public RunSnapshot run;
        public string lastTransactionId;
        public long bossClearCount;
        public bool legacyProgressionImported;
    }

    [Serializable] public sealed class ItemContainerSnapshot
    {
        public List<string> slots = new List<string>();
    }

    [Serializable] public sealed class ItemSnapshot
    {
        public string contentId;
        public string instanceId;
        public long acquisitionOrder;
        public int level;
        public ItemGrade grade;
        public int count;
        public string originRunId;
        public WeaponElement element;
        public bool hasElement;
        public MeleeStarDistributionProfile qualityProfile;
        public List<WeaponGradeStatRoll> weaponRolls = new List<WeaponGradeStatRoll>();
        public List<GearStatRoll> gearRolls = new List<GearStatRoll>();
        public List<BagRandomOptionRoll> bagRolls = new List<BagRandomOptionRoll>();
        public FlaskInstanceState flask;
        public MapInstanceState map;
    }

    [Serializable] public sealed class QuickSlotSnapshot
    {
        public string consumableContentId;
        public string flaskInstanceId;
        public string skillId;
    }

    [Serializable] public sealed class MerchantSnapshot
    {
        public string contentId;
        public int capacity;
        public int reputationLevel;
        public int reputationExperience;
        public bool stockInitialized;
        public List<string> stock = new List<string>();
        public List<string> currency = new List<string>();
    }

    public enum RunPhase { EntryPending, Active, BossCleared, Extracted, Failed }

    [Serializable] public sealed class RunSnapshot
    {
        public string runId;
        public RunPhase phase;
        public string mapInstanceId;
        public MapInstanceState map;
        public long bossClearedAtUtcTicks;
        public List<string> transferredObjects = new List<string>();
        public List<string> rewardedEncounters = new List<string>();
    }

    [Serializable] public sealed class MapInstanceState
    {
        public string mapContentId;
        public string monsterThemeId;
        public int level = 1;
        public ItemGrade grade;
        public List<MapOptionRoll> options = new List<MapOptionRoll>();
    }

    [Serializable] public sealed class MapOptionRoll
    {
        public string optionId;
        public float value;
    }
}
