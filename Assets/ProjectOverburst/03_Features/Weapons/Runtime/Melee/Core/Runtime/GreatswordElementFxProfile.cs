using UnityEngine;

// Shared appearance for every greatsword. Mesh bounds and anchors stay on each equipped prefab.
[CreateAssetMenu(menuName = "OVERBURST/Weapons/Greatsword Element FX Profile")]
public sealed class GreatswordElementFxProfile : ScriptableObject
{
    public const string ResourcePath = "Weapons/GreatswordElementFxProfile";

    [Header("Weapon Effects 2 blade effects")]
    [SerializeField] private GameObject fireBladeAccent;
    [SerializeField] private GameObject iceBladeAccent;
    [SerializeField] private GameObject electricBladeAccent;
    [SerializeField] private GameObject darkBladeAccent;
    [SerializeField] private GameObject lightBladeAccent;

    [Header("Swing trail")]
    [SerializeField] private GameObject fireTrail;
    [SerializeField] private GameObject iceTrail;
    [SerializeField] private GameObject electricTrail;
    [SerializeField] private GameObject darkTrail;
    [SerializeField] private GameObject lightTrail;

    [Header("Sword-tip trail")]
    [SerializeField] private GameObject fireTipTrail;
    [SerializeField] private GameObject iceTipTrail;
    [SerializeField] private GameObject electricTipTrail;
    [SerializeField] private GameObject darkTipTrail;
    [SerializeField] private GameObject lightTipTrail;
    [SerializeField, Range(.05f, 1f)] private float tipTrailLifetime = .2f;
    [SerializeField, Range(.01f, 1f)] private float tipTrailWidthScale = .15f;

    [Header("Element tuning")]
    [SerializeField] private MeleeWeaponElementFx.ElementTuning fireSettings = new MeleeWeaponElementFx.ElementTuning();
    [SerializeField] private MeleeWeaponElementFx.ElementTuning iceSettings = new MeleeWeaponElementFx.ElementTuning();
    [SerializeField] private MeleeWeaponElementFx.ElementTuning electricSettings = new MeleeWeaponElementFx.ElementTuning();
    [SerializeField] private MeleeWeaponElementFx.ElementTuning darkSettings = new MeleeWeaponElementFx.ElementTuning();
    [SerializeField] private MeleeWeaponElementFx.ElementTuning lightSettings = new MeleeWeaponElementFx.ElementTuning();

    public float TipTrailLifetime => tipTrailLifetime;
    public float TipTrailWidthScale => tipTrailWidthScale;

    public MeleeWeaponElementFx.ElementTuning TuningFor(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire: return fireSettings;
            case WeaponElement.Ice: return iceSettings;
            case WeaponElement.Electric: return electricSettings;
            case WeaponElement.Dark: return darkSettings;
            case WeaponElement.Light: return lightSettings;
            default: return fireSettings;
        }
    }

    public GameObject BladeFor(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire: return fireBladeAccent;
            case WeaponElement.Ice: return iceBladeAccent;
            case WeaponElement.Electric: return electricBladeAccent;
            case WeaponElement.Dark: return darkBladeAccent;
            case WeaponElement.Light: return lightBladeAccent;
            default: return null;
        }
    }

    public GameObject TrailFor(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire: return fireTrail;
            case WeaponElement.Ice: return iceTrail;
            case WeaponElement.Electric: return electricTrail;
            case WeaponElement.Dark: return darkTrail;
            case WeaponElement.Light: return lightTrail;
            default: return null;
        }
    }

    public GameObject TipFor(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire: return fireTipTrail;
            case WeaponElement.Ice: return iceTipTrail;
            case WeaponElement.Electric: return electricTipTrail;
            case WeaponElement.Dark: return darkTipTrail;
            case WeaponElement.Light: return lightTipTrail;
            default: return null;
        }
    }
}
