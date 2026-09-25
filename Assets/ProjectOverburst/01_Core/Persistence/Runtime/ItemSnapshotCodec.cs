using System;
using System.IO;
using UnityEngine;

namespace Overburst.Persistence
{
    public static class ItemSnapshotCodec
    {
        public static T CopyValues<T>(T value) where T : class
        {
            // JsonUtility materializes null nested classes; that would invent a pending run.
            return value == null ? null : ES3.Deserialize<T>(ES3.Serialize(value));
        }

        public static ItemSnapshot Capture(ItemData item, AccountContentRegistry registry)
        {
            if (item == null) return null;
            var snapshot = new ItemSnapshot
            {
                contentId = registry.IdFor(item.baseData), instanceId = item.runtimeInstanceId,
                acquisitionOrder = item.acquisitionOrder, level = item.level, grade = item.grade,
                count = item.stackCount, originRunId = item.originRunId,
                element = item.ResolvedElement, hasElement = item.HasInstanceElement,
                qualityProfile = item.meleeStarDistributionProfile,
                weaponRolls = item.weaponGradeStatRolls, gearRolls = item.gearRolls,
                bagRolls = item.bagOptions, flask = item.flaskState, map = item.mapState
            };
            Validate(snapshot, registry);
            return CopyValues(snapshot);
        }

        public static ItemData Restore(ItemSnapshot snapshot, AccountContentRegistry registry)
        {
            Validate(snapshot, registry);
            var copy = CopyValues(snapshot);
            return ItemData.RestoreSaved(copy, registry.Resolve<BaseItemData>(copy.contentId));
        }

        public static void Validate(ItemSnapshot s, AccountContentRegistry registry)
        {
            if (s == null || string.IsNullOrWhiteSpace(s.instanceId) || s.count <= 0 || s.acquisitionOrder < 0 || !Enum.IsDefined(typeof(ItemGrade), s.grade))
                throw new InvalidDataException("Invalid saved item identity/count/grade.");
            var data = registry.Resolve<BaseItemData>(s.contentId);
            if (s.level < 1 || s.level > 100) throw new InvalidDataException("Saved item level outside 1..100.");
            if (!Enum.IsDefined(typeof(WeaponElement), s.element)) throw new InvalidDataException("Invalid saved element.");
            if (data is WeaponItemData && (s.weaponRolls == null || !s.hasElement)) throw new InvalidDataException("Missing saved weapon rolls/element.");
            if (data is WeaponItemData weapon && WeaponGradeStatRoller.IsMeleeWeapon(weapon)
                && !WeaponGradeStatRoller.HasFormalMeleeGradeRolls(weapon, s.grade, s.weaponRolls, s.qualityProfile))
                throw new InvalidDataException("Invalid saved melee quality rows.");
            if (data is GearItemData gear && !GearQuality.IsValid(gear, s.grade, s.gearRolls)) throw new InvalidDataException("Invalid saved gear rolls.");
            if (data is BagItemData && s.bagRolls == null) throw new InvalidDataException("Missing saved bag rolls.");
            if (data is FlaskItemData && !FlaskGradeRoller.IsValid(s.flask, s.grade)) throw new InvalidDataException("Invalid saved flask rolls.");
            if (s.map != null && (s.map.level < 1 || s.map.level > 100 || s.map.options == null || !Enum.IsDefined(typeof(ItemGrade), s.map.grade))) throw new InvalidDataException("Invalid saved map.");
            if (data is MapItemData && (s.map == null || s.map.mapContentId != s.contentId || s.map.level != s.level || s.map.grade != s.grade)) throw new InvalidDataException("Map item and instance values disagree.");
            if (!(data is MapItemData) && s.map != null) throw new InvalidDataException("Non-map item contains map state.");
        }
    }
}
