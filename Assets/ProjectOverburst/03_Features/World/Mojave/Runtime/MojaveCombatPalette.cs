using UnityEngine;

namespace Overburst.Mojave
{
    public static class MojaveCombatPalette
    {
        public static readonly Color Trail = new Color32(70, 196, 233, 255);
        public static Color For(MojaveCombatKind kind)
        {
            switch(kind) {
                case MojaveCombatKind.OpenBasin:return new Color32(230,185,96,255);
                case MojaveCombatKind.LongWash:return new Color32(223,130,78,255);
                case MojaveCombatKind.BroadCourt:return new Color32(132,198,121,255);
                case MojaveCombatKind.AsymmetricHollow:return new Color32(127,159,220,255);
                case MojaveCombatKind.TwinClearing:return new Color32(170,145,223,255);
                case MojaveCombatKind.CrescentBasin:return new Color32(232,151,165,255);
                case MojaveCombatKind.JunctionClearing:return new Color32(85,201,185,255);
                default:return new Color32(185,135,104,255);
            }
        }
    }
}
