using UnityEngine;
using UnityEngine.VFX;

[CreateAssetMenu(menuName = "OVERBURST/VFX/Blood Catalog")]
public sealed class BloodHitCatalog : ScriptableObject
{
    public const string ResourcePath = "Combat/BloodHitCatalog";
    public VisualEffectAsset slash, stab, burst;
    public bool useSweepVariations;
    public SweepVariation[] sweepVariations;
    [System.Serializable]
    public sealed class SweepVariation
    {
        public string label;
        public VisualEffectAsset graph;
        public Vector3 localEuler = new Vector3(0, 90, 0);
        [Min(.1f)] public float sizeMultiplier = 1f;
    }
    // Cosmetic selection never consumes gameplay Random state. Skip the last successful shape.
    public bool TryResolveSweep(uint seed, int previous, out int index, out SweepVariation variation)
    {
        index = -1; variation = null;
        if (!useSweepVariations || sweepVariations == null) return false;
        int valid = 0;
        for (int i = 0; i < sweepVariations.Length; i++)
            if (sweepVariations[i]?.graph != null) valid++;
        if (valid == 0) return false;
        bool skipPrevious = valid > 1 && previous >= 0 && previous < sweepVariations.Length
            && sweepVariations[previous]?.graph != null;
        int selected = (int)(seed % (uint)(valid - (skipPrevious ? 1 : 0)));
        for (int i = 0; i < sweepVariations.Length; i++)
        {
            if (sweepVariations[i]?.graph == null || (skipPrevious && i == previous)) continue;
            if (selected-- != 0) continue;
            index = i; variation = sweepVariations[i]; return true;
        }
        return false;
    }
    public VisualEffectAsset ResolvePooledGraph(int slot)
    {
        int count = useSweepVariations && sweepVariations != null ? sweepVariations.Length : 0;
        int kind = slot % (3 + count);
        if (kind < 3) return Resolve((CombatImpactShape)kind);
        return sweepVariations[kind - 3]?.graph ?? slash;
    }
    public GameObject directionalDecal, spotDecal, splatterDecal, puddleDecal;
    public GameObject[] sweepDecals, thrustDecals, downwardDecals, lethalDecals;
    [Min(.1f)] public float lifetime = 2.5f;
    public VisualEffectAsset Resolve(CombatImpactShape shape)
        => shape == CombatImpactShape.Thrust ? stab : shape == CombatImpactShape.Downward ? burst : slash;
    public GameObject ResolveDecal(CombatImpactShape shape, bool lethal, int variant)
    {
        GameObject[] choices = lethal ? lethalDecals
            : shape == CombatImpactShape.Thrust ? thrustDecals
            : shape == CombatImpactShape.Downward ? downwardDecals : sweepDecals;
        if (choices != null && choices.Length > 0)
            return choices[(int)((uint)variant % (uint)choices.Length)];
        return lethal ? puddleDecal
            : shape == CombatImpactShape.Thrust ? spotDecal
            : shape == CombatImpactShape.Downward ? splatterDecal : directionalDecal;
    }
}
