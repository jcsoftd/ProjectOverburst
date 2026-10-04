using UnityEngine;

[CreateAssetMenu(menuName = "OVERBURST/VFX/Blood Effects Pack Catalog")]
public sealed class BloodEffectsPackCatalog : ScriptableObject
{
    public const string ResourcePath = "Combat/BloodEffectsPackCatalog";
    [System.Serializable]
    public sealed class Spray
    {
        public string label;
        public GameObject prefab;
        public int shapeMask = 7;
        public int minimumPriority;
        public bool flowing;
        public Vector3 localEuler;
        [Min(.01f)] public float scale = .9f;
        [Min(.1f)] public float lifetime = 2f;
        public bool Accepts(CombatImpactShape shape, int priority, bool drip = false)
            => prefab != null && (drip ? flowing : priority >= minimumPriority && (shapeMask & (1 << (int)shape)) != 0);
    }
    public Spray[] sprays;
    public GameObject[] sweepDecals, thrustDecals, downwardDecals, lethalDecals, trailDecals;
    public Sprite screenSprite;
    public Material[] screenMaterials;
    public GameObject ResolveDecal(CombatImpactShape shape, bool lethal, int variant, bool trail = false)
    {
        var choices = trail ? trailDecals : lethal ? lethalDecals : shape == CombatImpactShape.Thrust ? thrustDecals
            : shape == CombatImpactShape.Downward ? downwardDecals : sweepDecals;
        return choices != null && choices.Length > 0 ? choices[(int)((uint)variant % (uint)choices.Length)] : null;
    }
    public bool Accepts(int index, CombatImpactShape shape, int priority, bool drip = false)
        => sprays != null && index >= 0 && index < sprays.Length && sprays[index] != null && sprays[index].Accepts(shape, priority, drip);
    public int ResolveSpray(CombatImpactShape shape, int priority, uint seed, int previous, bool drip = false)
    {
        int count = 0;
        if (sprays == null) return -1;
        for (int i = 0; i < sprays.Length; i++) if (Accepts(i, shape, priority, drip)) count++;
        if (count == 0) return -1;
        bool skip = count > 1 && Accepts(previous, shape, priority, drip);
        int choice = (int)(seed % (uint)(count - (skip ? 1 : 0)));
        for (int i = 0; i < sprays.Length; i++)
            if (Accepts(i, shape, priority, drip) && (!skip || i != previous) && choice-- == 0) return i;
        return -1;
    }
}
