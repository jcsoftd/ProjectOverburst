using System;
using System.Collections.Generic;

namespace Overburst.Persistence
{
    [Serializable]
    public sealed class AccountSnapshot
    {
        public int schemaVersion = EasySaveAccountStore.SchemaVersion;
        public string profileId = "default";
        public long revision;
        public long nextAcquisitionOrder = 1;
        public int level = 1;
        public int experience;
        public int unlockedWorldLevel = 1;
        public int appliedWorldLevel = 1;
        public int inventoryCapacity = BagQuality.InventoryCapacity;
        public int bagExperienceCarry;
        public int bagGoldCarry;
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
        public List<QuestSnapshot> quests = new List<QuestSnapshot>();
        public string trackedQuestId;
        public RunSnapshot run;
        public string lastTransactionId;
        public long bossClearCount;
        public string elementalGemInstanceId;
        public SkillTreeSnapshot skillTree;
        public CharacterAppearanceSnapshot appearance;
        public string activeCharacterId;
        public string tutorialFirstCharacterId;
        public List<AccountCharacterSnapshot> characters;

        internal AccountSnapshot WithCurrencyInventory(List<string> slots, List<ItemSnapshot> values, long nextOrder)
        {
            var candidate = (AccountSnapshot)MemberwiseClone();
            candidate.inventory = slots;
            candidate.items = values;
            candidate.nextAcquisitionOrder = nextOrder;
            return candidate;
        }

        // Only the transaction owner may share these already-validated collections.
        // This candidate changes scalar progression; public Read still deep-copies.
        internal AccountSnapshot WithProgression(int nextLevel, int nextExperience)
        {
            var candidate = (AccountSnapshot)MemberwiseClone();
            candidate.level = nextLevel;
            candidate.experience = nextExperience;
            return candidate;
        }
    }

    [Serializable] public sealed class AccountCharacterSnapshot
    {
        public string id;
        public string name;
        public string classId = "knight";
        // Active personal data lives in the account root. Dormant data has no shared stash/merchants/roster.
        public AccountSnapshot personal;
    }

    [Serializable] public sealed class ItemContainerSnapshot
    {
        public List<string> slots = new List<string>();
    }

    [Serializable] public sealed class ItemSnapshot
    {
        public int balanceVersion;
        public string contentId;
        public string instanceId;
        public long acquisitionOrder;
        public int level;
        public ItemGrade grade;
        public int count;
        public string originRunId;
        public MeleeStarDistributionProfile qualityProfile;
        public List<WeaponGradeStatRoll> weaponRolls = new List<WeaponGradeStatRoll>();
        public List<GearStatRoll> gearRolls = new List<GearStatRoll>();
        public ElementGemState gemState;
        public BagInstanceState bag;
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
        public int reputationExperience; // Legacy schema 2 only.
        public int reputationTotalXp;
        public int reputationPolicyVersion; // Zero identifies legacy data during schema migration.
        public bool stockInitialized;
        public List<string> stock = new List<string>();
        public List<string> currency = new List<string>();
    }

    // Keep the serialized values of the legacy run phases.
    public enum RunPhase { EntryPending = 0, Active = 1, BossCleared = 2, Extracted = 3, Failed = 4, Completed = 5 }

    // None also accepts older extracted saves that predate this field.
    public enum RunExitReason { None = 0, Completed = 1, EmergencyEscape = 2 }

    [Serializable] public sealed class RunSnapshot
    {
        public string runId;
        public RunPhase phase;
        public RunExitReason exitReason;
        public string mapInstanceId;
        public MapInstanceState map;
        public long bossClearedAtUtcTicks;
        public long completedAtUtcTicks;
        public int completionArea;
        public UnityEngine.Vector3 completionAnchor;
        public ItemSnapshot completionReward;
        public bool completionRewardClaimed;
        public List<string> transferredObjects = new List<string>();
        public List<string> rewardedEncounters = new List<string>();
    }

    public enum MapTerrainKind { Legacy = 0, Desert = 1, Cave = 2 }

    [Serializable] public sealed class MapMonsterTheme
    {
        public string themeId;
        public int weight;
    }

    [Serializable] public sealed class MapInstanceState
    {
        public string mapContentId;
        public string monsterThemeId;
        public int generationVersion;
        public MapTerrainKind terrain;
        public int seed;
        public int areaCount;
        public string desertCandidateId;
        public int width, depth;
        public List<MapMonsterTheme> monsterThemes = new List<MapMonsterTheme>();
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
