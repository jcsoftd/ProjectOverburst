using UnityEngine;
using UnityEngine.VFX;

[CreateAssetMenu(menuName = "OVERBURST/VFX/Blood Catalog")]
public sealed class BloodHitCatalog : ScriptableObject
{
    public const string ResourcePath = "Combat/BloodHitCatalog";
    public VisualEffectAsset slash, stab, burst;
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
