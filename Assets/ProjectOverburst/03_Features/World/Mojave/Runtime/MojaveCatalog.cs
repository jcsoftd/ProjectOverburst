using UnityEngine;

namespace Overburst.Mojave
{
    public sealed class MojaveCatalog : ScriptableObject
    {
        // Empty theme fields retain the original Mojave v3 behaviour.
        public bool oasis;
        public bool volcano;
        public bool AuthoredSurface => oasis || volcano;
        public GameObject ashPrefab;
        public GameObject lavaSparkPrefab;
        public string biomeName;
        public string ownedRoot;
        public string[] placeNames;
        public Material skybox;
        public string DisplayName => string.IsNullOrEmpty(biomeName) ? "Mojave" : biomeName;
        public MojavePatch[] patches;
        public TerrainLayer[] layers;
        public Material terrainMaterial;
        public GameObject[] boulders;
        public GameObject[] redBoulders;
        public GameObject[] stones;
        public GameObject[] rubble;
        public GameObject[] shrubs;
        public GameObject[] grasses;
        public GameObject[] joshua;
        public GameObject[] cacti;
        public GameObject portalPrefab;
        public Material avatarMaterial;
        public Material avatarAccent;
        public Material portalMaterial;
    }
}
