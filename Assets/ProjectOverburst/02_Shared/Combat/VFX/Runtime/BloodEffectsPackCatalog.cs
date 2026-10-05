using UnityEngine;

[CreateAssetMenu(menuName = "OVERBURST/VFX/Blood Effects Pack Catalog")]
public sealed class BloodEffectsPackCatalog : ScriptableObject
{
    public const string ResourcePath = "Combat/BloodEffectsPackCatalog";
    public const string VolumetricResourcePath = "Combat/VolumetricBloodCatalog";
    public bool volumetric;
    public static string ResourceFor(BloodEffectStyle style) => style == BloodEffectStyle.Volumetric ? VolumetricResourcePath : ResourcePath;
    [System.Serializable]
    public sealed class Spray
    {
        public string label;
        public GameObject prefab;
        public GameObject groundPrefab;
        public int shapeMask = 7;
        public int minimumPriority;
        public bool flowing;
        public bool impactAccent;
        public Vector3 localEuler;
        [Min(.01f)] public float scale = .9f;
        [Min(.1f)] public float lifetime = 2f;
        public bool Accepts(CombatImpactShape shape, int priority, bool drip = false, bool accented = false)
            => prefab != null && (drip ? flowing : priority >= minimumPriority && (!impactAccent || accented) && ((accented && impactAccent) || (shapeMask & (1 << (int)shape)) != 0));
    }
    public Spray[] sprays;
    public GameObject[] sweepDecals, thrustDecals, downwardDecals, lethalDecals, trailDecals;
    public Shader sprayProfileShader, groundProfileShader;
    public Sprite screenSprite;
    public Material[] screenMaterials;
    public GameObject ResolveDecal(CombatImpactShape shape, bool lethal, int variant, bool trail = false)
    {
        var choices = trail ? trailDecals : lethal ? lethalDecals : shape == CombatImpactShape.Thrust ? thrustDecals
            : shape == CombatImpactShape.Downward ? downwardDecals : sweepDecals;
        return choices != null && choices.Length > 0 ? choices[(int)((uint)variant % (uint)choices.Length)] : null;
    }
    public bool Accepts(int index, CombatImpactShape shape, int priority, bool drip = false, bool accented = false)
        => sprays != null && index >= 0 && index < sprays.Length && sprays[index] != null && sprays[index].Accepts(shape, priority, drip, accented);
    public int ResolveSpray(CombatImpactShape shape, int priority, uint seed, int previous, bool drip = false, bool accented = false)
    {
        int count = 0;
        bool preferAccent = false;
        if (accented && !drip && sprays != null)
            for (int i = 0; i < sprays.Length; i++)
                if (sprays[i] != null && sprays[i].impactAccent && sprays[i].Accepts(shape, priority, false, true)) { preferAccent = true; break; }
        bool Eligible(int index) => Accepts(index, shape, priority, drip, accented) && (!preferAccent || sprays[index].impactAccent);
        if (sprays == null) return -1;
        for (int i = 0; i < sprays.Length; i++) if (Eligible(i)) count++;
        if (count == 0) return -1;
        bool skip = count > 1 && Eligible(previous);
        int choice = (int)(seed % (uint)(count - (skip ? 1 : 0)));
        for (int i = 0; i < sprays.Length; i++)
            if (Eligible(i) && (!skip || i != previous) && choice-- == 0) return i;
        return -1;
    }
}
