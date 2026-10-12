using UnityEngine;

namespace Overburst.Mojave
{
    public sealed class MojaveCombatSizeLibrary : ScriptableObject
    {
        public MojaveCatalog catalog;
        public MojavePatch bossPatch;
        public MojaveCombatTileSet.Rule[] rules;
    }
}
