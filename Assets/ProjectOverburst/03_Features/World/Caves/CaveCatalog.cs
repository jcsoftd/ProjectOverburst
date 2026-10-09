using UnityEngine;

namespace Overburst.Caves
{
    public sealed class CaveCatalog : ScriptableObject
    {
        public GameObject[] combatTiles;
        public GameObject[] backgroundTiles;
        public GameObject bridge;
        public GameObject crystal;
        public GameObject bridgeSupport;
        public Material abyssMaterial;
        public Material explorerMaterial;
    }
}
