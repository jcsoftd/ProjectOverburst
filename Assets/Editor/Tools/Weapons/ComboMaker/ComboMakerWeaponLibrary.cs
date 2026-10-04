using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Overburst.EditorTools.ComboMaker
{
    internal static class ComboMakerWeaponLibrary
    {
        internal sealed class Entry
        {
            public WeaponClass WeaponClass { get; }
            public WeaponItemData[] Weapons { get; }
            public string Label => ClassLabel(WeaponClass) + " 공통";
            public WeaponItemData Representative => Weapons.FirstOrDefault(w => w.weaponRootPrefab != null) ?? Weapons[0];

            public Entry(WeaponClass weaponClass, WeaponItemData[] weapons)
            { WeaponClass = weaponClass; Weapons = weapons; }

            public Object SharedAsset(ComboMakerAttackMode mode, out string issue)
            {
                var assets = Weapons.Select(w => ComboMakerAttackBinding.Asset(w.GetMeleeDefinition(), mode)).Distinct().ToArray();
                issue = assets.Length > 1
                    ? Label + "의 " + ComboMakerAttackBinding.Label(mode) + " 연결이 서로 다릅니다. 공통 자산 연결을 맞춘 뒤 다시 읽으세요."
                    : null;
                return assets.Length == 1 ? assets[0] : null;
            }

            public IEnumerable<string> BindingIssues()
            {
                foreach (ComboMakerAttackMode mode in Enum.GetValues(typeof(ComboMakerAttackMode)))
                {
                    SharedAsset(mode, out string issue);
                    if (issue != null) yield return issue;
                }
            }
        }

        public static Entry[] Collect() => Collect(Resources.Load<WeaponLevelCatalog>(WeaponLevelCatalog.ResourcePath));

        public static Entry[] Collect(WeaponLevelCatalog catalog)
        {
            if (catalog == null) return Array.Empty<Entry>();
            return (catalog.entries ?? Array.Empty<WeaponLevelCatalog.Entry>())
                .Select(e => e.weapon)
                .Where(w => WeaponContentPolicy.IsActiveWeapon(w) && w.GetMeleeComboDefinition() != null)
                .Distinct().GroupBy(w => w.weaponClass)
                .Select(g => new Entry(g.Key, g.OrderBy(w => w.name, StringComparer.Ordinal).ToArray()))
                .OrderBy(g => (int)g.WeaponClass).ToArray();
        }

        public static string ClassLabel(WeaponClass weaponClass)
        {
            switch (weaponClass.ToString())
            {
                case "Greatsword": return "대검";
                case "Sword": return "한손검";
                case "Dagger": return "단검";
                case "Orb": return "오브";
                default: return weaponClass.ToString();
            }
        }

        public static string PreviewLabel(WeaponItemData weapon)
            => string.IsNullOrWhiteSpace(weapon.itemName) ? weapon.name : weapon.itemName + " · " + weapon.name;
    }
}
