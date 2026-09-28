using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Overburst.Persistence
{
    // Upgrade derived values only. No random draws, new identities or container moves.
    public static class ItemBalanceMigration
    {
        public static AccountSnapshot UpgradeAccount(AccountSnapshot source, AccountContentRegistry registry, out bool changed)
        {
            changed = false;
            if (source == null) return null;
            var copy = ItemSnapshotCodec.CopyValues(source);
            if (copy.items == null) throw new InvalidDataException("Missing items during balance migration.");
            foreach (var item in copy.items)
                changed |= Upgrade(item, registry.Resolve<BaseItemData>(item.contentId));
            return changed ? copy : source;
        }

        public static bool Upgrade(ItemSnapshot item, BaseItemData data)
        {
            if (item == null) throw new InvalidDataException("Missing item during balance migration.");
            ValidateVersion(item.balanceVersion);
            if (item.balanceVersion == OverburstCombatBalance.ItemBalanceVersion) return false;
            var gear = UpgradeGear(data as GearItemData, item.grade, item.gearRolls);
            var weapon = UpgradeWeapon(data as WeaponItemData, item.grade, item.qualityProfile, item.weaponRolls);
            item.gearRolls = gear;
            item.weaponRolls = weapon;
            item.balanceVersion = OverburstCombatBalance.ItemBalanceVersion;
            return true;
        }

        public static void UpgradeRuntime(ItemData item)
        {
            ValidateVersion(item.balanceVersion);
            if (item.balanceVersion == OverburstCombatBalance.ItemBalanceVersion) return;
            // Empty authored templates still use the existing initial-generation path.
            var gear = item.gearRolls != null && item.gearRolls.Count > 0
                ? UpgradeGear(item.baseData as GearItemData, item.grade, item.gearRolls) : item.gearRolls;
            var weapon = item.weaponGradeStatRolls != null && item.weaponGradeStatRolls.Count > 0
                ? UpgradeWeapon(item.baseData as WeaponItemData, item.grade, item.meleeStarDistributionProfile, item.weaponGradeStatRolls)
                : item.weaponGradeStatRolls;
            item.gearRolls = gear;
            item.weaponGradeStatRolls = weapon;
            item.balanceVersion = OverburstCombatBalance.ItemBalanceVersion;
        }

        private static void ValidateVersion(int version)
        {
            if (version < 0 || version > OverburstCombatBalance.ItemBalanceVersion)
                throw new InvalidDataException("Unsupported item balance version: " + version);
        }

        private static List<GearStatRoll> UpgradeGear(GearItemData data, ItemGrade grade, List<GearStatRoll> rows)
        {
            if (data == null) return rows;
            var copy = ItemSnapshotCodec.CopyValues(rows);
            if (data.kind == GearKind.Necklace && copy != null && copy.Count == GearQuality.RowCount
                && copy[0] != null && copy[0].stat == GearStat.MaxHealth)
            {
                // Legacy necklaces could not contain HP as a secondary.
                for (int i = 1; i < copy.Count; i++)
                    if (copy[i] == null || copy[i].stat == GearStat.MaxHealth)
                        throw new InvalidDataException("Invalid legacy necklace secondary.");
                copy[0].stat = GearStat.CriticalDamage;
                for (int i = 1; i < copy.Count; i++)
                    if (copy[i].stat == GearStat.CriticalDamage) copy[i].stat = GearStat.MaxHealth;
            }
            if (!GearQuality.IsValid(data, grade, copy))
                throw new InvalidDataException("Invalid gear balance migration; original rows preserved.");
            return copy;
        }

        private static List<WeaponGradeStatRoll> UpgradeWeapon(WeaponItemData data, ItemGrade grade,
            MeleeStarDistributionProfile profile, List<WeaponGradeStatRoll> rows)
        {
            if (data == null || !WeaponGradeStatRoller.IsMeleeWeapon(data)) return rows;
            var copy = ItemSnapshotCodec.CopyValues(rows);
            if (copy == null) throw new InvalidDataException("Missing legacy weapon rows.");
            foreach (var row in copy)
            {
                if (row == null || row.starRolls == null) throw new InvalidDataException("Missing legacy stars.");
                float positive = 0f, negative = 0f;
                float unit = row.statType == WeaponGradeStatType.Damage ? .25f
                    : WeaponGradeStatRoller.GetMeleeBaseStarValue(row.statType);
                foreach (var star in row.starRolls)
                {
                    if (star == null || !Enum.IsDefined(typeof(WeaponGradeStarType), star.starType))
                        throw new InvalidDataException("Invalid legacy star color.");
                    if (star.IsNegative) negative += unit;
                    else positive += unit * star.ValueMultiplier;
                }
                bool oldValues = Mathf.Abs(row.positiveTotalValue - positive) < .0001f
                    && Mathf.Abs(row.negativeTotalValue - negative) < .0001f;
                float newRatio = row.statType == WeaponGradeStatType.Damage ? .4f : 1f;
                bool currentValues = Mathf.Abs(row.positiveTotalValue - positive * newRatio) < .0001f
                    && Mathf.Abs(row.negativeTotalValue - negative * newRatio) < .0001f;
                if (!oldValues && !currentValues)
                    throw new InvalidDataException("Legacy quality totals disagree with saved stars.");
            }
            if (!WeaponGradeStatRoller.TryRefreshMeleeGradeRollValues(copy)
                || !WeaponGradeStatRoller.HasFormalMeleeGradeRolls(data, grade, copy, profile))
                throw new InvalidDataException("Invalid weapon balance migration; original stars preserved.");
            return copy;
        }
    }
}
