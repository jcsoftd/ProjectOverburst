using UnityEngine;

namespace Overburst.EditorTools.ComboMaker
{
    internal enum ComboMakerAttackMode { Light, Heavy, ParriedHeavy, Dodge, DashHeavy }

    internal static class ComboMakerAttackBinding
    {
        public static string Label(ComboMakerAttackMode mode)
        {
            switch (mode)
            {
                case ComboMakerAttackMode.Heavy: return "강공";
                case ComboMakerAttackMode.ParriedHeavy: return "패링 강화 강공";
                case ComboMakerAttackMode.Dodge: return "닷지 약공";
                case ComboMakerAttackMode.DashHeavy: return "대시 강공";
                default: return "약공 콤보";
            }
        }

        public static MeleeHeavyAttackDefinition Heavy(MeleeWeaponDefinition weapon, ComboMakerAttackMode mode)
        {
            if (weapon == null) return null;
            switch (mode)
            {
                case ComboMakerAttackMode.Heavy: return weapon.heavyAttackDefinition;
                case ComboMakerAttackMode.ParriedHeavy: return weapon.parriedHeavyAttackDefinition;
                case ComboMakerAttackMode.DashHeavy: return weapon.dashHeavyAttackDefinition;
                default: return null;
            }
        }

        public static Object Asset(MeleeWeaponDefinition weapon, ComboMakerAttackMode mode)
        {
            if (weapon == null) return null;
            if (mode == ComboMakerAttackMode.Light) return weapon.comboDefinition;
            if (mode == ComboMakerAttackMode.Dodge) return weapon.dodgeAttackDefinition;
            return Heavy(weapon, mode);
        }
    }
}
