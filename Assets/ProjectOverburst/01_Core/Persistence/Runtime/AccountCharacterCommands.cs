using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Overburst.Persistence
{
    public static class AccountCharacterCommands
    {
        public const string LegacyId = "legacy";
        private static readonly Regex NamePattern = new Regex(@"\A[가-힣A-Za-z0-9]{2,12}\z", RegexOptions.CultureInvariant);

        public static string ValidateName(string name)
        {
            name = (name ?? "").Normalize(System.Text.NormalizationForm.FormC).Trim();
            if (!NamePattern.IsMatch(name)) throw new ArgumentException("이름은 한글·영문·숫자 2~12자로 입력해 주세요.");
            return name;
        }

        public static void EnsureLegacy(AccountSnapshot state)
        {
            if (state.characters != null) return;
            state.activeCharacterId = LegacyId;
            state.characters = new List<AccountCharacterSnapshot> { new AccountCharacterSnapshot { id = LegacyId, name = "기존 캐릭터" } };
        }

        public static void Create(AccountSnapshot state, string id, string name, string classId,
            CharacterAppearanceSnapshot appearance, AccountContentRegistry registry)
        {
            if (classId != "knight") throw new InvalidOperationException("현재는 기사만 생성할 수 있습니다.");
            if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("잘못된 생성 식별자입니다.");
            name = ValidateName(name); AccountAppearanceCommands.Validate(appearance);
            EnsureLegacy(state);
            var existing = state.characters.Find(c => c.id == id);
            if (existing != null)
            {
                var saved = existing.id == state.activeCharacterId ? state.appearance : existing.personal?.appearance;
                if (existing.name == name && existing.classId == classId && appearance.Equals(saved)) return;
                throw new InvalidOperationException("이미 다른 내용으로 생성된 캐릭터입니다.");
            }
            if (state.characters.Exists(c => string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("이미 사용 중인 이름입니다.");
            if (state.run != null && state.run.phase != RunPhase.Extracted && state.run.phase != RunPhase.Failed)
                throw new InvalidOperationException("중단된 모험을 복구한 뒤 생성해 주세요.");
            var fresh = NewAccountFactory.Create(registry);
            foreach (var item in fresh.items) item.acquisitionOrder = state.nextAcquisitionOrder++;
            fresh.nextAcquisitionOrder = state.nextAcquisitionOrder;
            fresh.appearance = appearance.Copy();
            fresh.skillTree = AccountSkillTree.GrantEligible(null, fresh.level);
            state.nextAcquisitionOrder = Math.Max(state.nextAcquisitionOrder, fresh.nextAcquisitionOrder);
            state.characters.Add(new AccountCharacterSnapshot { id = id, name = name, classId = classId, personal = PersonalCopy(fresh) });
        }

        public static AccountSnapshot First(string id, string name, string classId,
            CharacterAppearanceSnapshot appearance, AccountContentRegistry registry)
        {
            if (classId != "knight" || !Guid.TryParseExact(id, "N", out _)) throw new InvalidOperationException("현재는 기사만 생성할 수 있습니다.");
            name = ValidateName(name); AccountAppearanceCommands.Validate(appearance);
            var state = NewAccountFactory.Create(registry);
            state.appearance = appearance.Copy();
            state.skillTree = AccountSkillTree.GrantEligible(null, state.level);
            state.activeCharacterId = id;
            state.characters = new List<AccountCharacterSnapshot> { new AccountCharacterSnapshot { id = id, name = name, classId = classId } };
            AccountInvariants.Validate(state, registry);
            return state;
        }

        public static void Select(AccountSnapshot state, string id)
        {
            EnsureLegacy(state);
            if (id == state.activeCharacterId) return;
            if (state.run != null && state.run.phase != RunPhase.Extracted && state.run.phase != RunPhase.Failed)
                throw new InvalidOperationException("중단된 모험을 복구한 뒤 선택해 주세요.");
            var next = state.characters.Find(c => c.id == id) ?? throw new InvalidOperationException("캐릭터를 찾지 못했습니다.");
            var previous = state.characters.Find(c => c.id == state.activeCharacterId);
            if (previous == null || next.personal == null) throw new InvalidDataException("캐릭터 저장이 올바르지 않습니다.");
            var sharedIds = SharedItemIds(state);
            var sharedItems = state.items.Where(i => sharedIds.Contains(i.instanceId)).ToList();
            previous.personal = PersonalCopy(state);
            ApplyPersonal(state, next.personal);
            state.items = sharedItems.Concat(next.personal.items).ToList();
            state.nextAcquisitionOrder = Math.Max(state.nextAcquisitionOrder, next.personal.nextAcquisitionOrder);
            next.personal = null; state.activeCharacterId = id;
        }

        private static HashSet<string> SharedItemIds(AccountSnapshot state)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tab in state.stashTabs) foreach (var id in tab.slots) if (!string.IsNullOrEmpty(id)) ids.Add(id);
            foreach (var merchant in state.merchants)
                foreach (var id in merchant.stock.Concat(merchant.currency)) if (!string.IsNullOrEmpty(id)) ids.Add(id);
            return ids;
        }

        private static AccountSnapshot PersonalCopy(AccountSnapshot state)
        {
            var copy = ItemSnapshotCodec.CopyValues(state);
            var shared = SharedItemIds(copy);
            copy.items.RemoveAll(i => shared.Contains(i.instanceId));
            copy.merchants.Clear(); foreach (var tab in copy.stashTabs) for (int i = 0; i < tab.slots.Count; i++) tab.slots[i] = null;
            copy.characters = null; copy.activeCharacterId = null;
            return copy;
        }

        private static void ApplyPersonal(AccountSnapshot target, AccountSnapshot source)
        {
            target.level = source.level; target.experience = source.experience;
            target.unlockedWorldLevel = source.unlockedWorldLevel; target.appliedWorldLevel = source.appliedWorldLevel;
            target.inventoryCapacity = source.inventoryCapacity; target.unlockedSlots = source.unlockedSlots;
            target.baseUnlockedSlots = source.baseUnlockedSlots; target.bagExperienceCarry = source.bagExperienceCarry;
            target.bagGoldCarry = source.bagGoldCarry; target.activeWeaponSlot = source.activeWeaponSlot;
            target.inventory = source.inventory; target.weapons = source.weapons; target.gear = source.gear;
            target.bags = source.bags; target.flasks = source.flasks; target.quickSlots = source.quickSlots;
            target.appearance = source.appearance; target.skillTree = source.skillTree; target.run = source.run;
            target.elementalGemInstanceId = source.elementalGemInstanceId; target.bossClearCount = source.bossClearCount;
            target.quests = source.quests; target.trackedQuestId = source.trackedQuestId;
        }

        public static void Validate(AccountSnapshot state, AccountContentRegistry registry)
        {
            if (state.characters == null)
            {
                if (!string.IsNullOrEmpty(state.activeCharacterId)) throw new InvalidDataException("Missing character roster.");
                return;
            }
            if (state.characters.Count == 0 || string.IsNullOrEmpty(state.activeCharacterId)) throw new InvalidDataException("Missing active character.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var items = new HashSet<string>(state.items.Select(i => i.instanceId), StringComparer.Ordinal);
            if (state.items.Any(i => i.acquisitionOrder >= state.nextAcquisitionOrder)) throw new InvalidDataException("Invalid account acquisition sequence.");
            int active = 0;
            foreach (var character in state.characters)
            {
                if (character == null || character.classId != "knight" || character.id == null
                    || (character.id != LegacyId && !Guid.TryParseExact(character.id, "N", out _))
                    || !ids.Add(character.id) || !names.Add(character.name ?? "")) throw new InvalidDataException("Invalid or duplicate character.");
                if (character.id != LegacyId && !string.Equals(character.name, ValidateName(character.name), StringComparison.Ordinal))
                    throw new InvalidDataException("Character name is not normalized.");
                if (character.id == state.activeCharacterId)
                {
                    if (character.personal != null) throw new InvalidDataException("Active character data is duplicated.");
                    active++; continue;
                }
                var personal = character.personal;
                if (personal == null || personal.characters != null || !string.IsNullOrEmpty(personal.activeCharacterId)
                    || personal.merchants == null || personal.merchants.Count != 0 || personal.stashTabs == null
                    || personal.stashTabs.Any(t => t == null || t.slots == null || t.slots.Any(id => !string.IsNullOrEmpty(id)))
                    || (personal.run != null && personal.run.phase != RunPhase.Extracted && personal.run.phase != RunPhase.Failed))
                    throw new InvalidDataException("Dormant character contains shared or unfinished data.");
                AccountInvariants.Validate(personal, registry);
                foreach (var item in personal.items) if (!items.Add(item.instanceId)) throw new InvalidDataException("Item is owned by multiple characters.");
                if (personal.items.Any(i => i.acquisitionOrder >= state.nextAcquisitionOrder)) throw new InvalidDataException("Invalid character acquisition sequence.");
            }
            if (active != 1) throw new InvalidDataException("Active character is missing.");
        }
    }
}
