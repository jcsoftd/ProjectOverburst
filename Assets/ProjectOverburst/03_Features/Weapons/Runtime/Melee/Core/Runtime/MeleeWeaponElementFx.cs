using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MeleeWeaponElementFx : MonoBehaviour, IWeaponTrailController
{
    [Header("Blade bindings")]
    [SerializeField] private Renderer bladeRenderer;
    [Tooltip("Blade-only bounds in renderer axes, with its world scale baked in; excludes grip and guard.")]
    [SerializeField] private Bounds bladeEffectBounds = new Bounds(new Vector3(0f, 0f, .52f), new Vector3(.12f, .06f, 1.04f));
    [SerializeField] private Transform auraAnchor;
    [SerializeField] private Transform trailAnchor;

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

    [Serializable]
    private sealed class ElementTuning
    {
        [Range(.01f, 1f)] public float idleStrength = .12f;
        [Range(.1f, 3f)] public float bladeThickness = 1f;
        [Range(0f, 1f)] public float bladeCenter = .5f;
        [Range(.5f, 3f)] public float bladeLength = 1f;
        [Range(.1f, 3f)] public float trailScale = 1f;
        [Range(0f, 1f)] public float trailCenter = .5f;
        [Range(.1f, 3f)] public float trailLength = 1f;
        [Range(.25f, 4f)] public float trailDensity = 1f;
        [Range(.1f, 5f)] public float trailParticleSize = 1f;
        [Range(.1f, 5f)] public float trailParticleLifetime = 1f;
        [Range(0f, 1f)] public float trailSpread = 1f;
    }
    [SerializeField] private ElementTuning fireSettings = new ElementTuning();
    [SerializeField] private ElementTuning iceSettings = new ElementTuning();
    [SerializeField] private ElementTuning electricSettings = new ElementTuning();
    [SerializeField] private ElementTuning darkSettings = new ElementTuning();
    [SerializeField] private ElementTuning lightSettings = new ElementTuning();
    private PlayerEquipment equipment;
    private OverburstElementEnergy energy;
    private WeaponElement shownElement;
    private bool awaitingOwner;
    private WeaponEffects2Playback bladePlayback, additionalPlayback;
    private float currentStrength = 1f;
    private bool SharesPackage => SelectTrail(shownElement) == SelectBladeAccent(shownElement);
    private bool IsEditorPreview
    {
        get
        {
#if UNITY_EDITOR
            return !Application.isPlaying && UnityEditor.SceneManagement.EditorSceneManager.IsPreviewSceneObject(gameObject);
#else
            return false;
#endif
        }
    }
    private void OnEnable() { BindOwner(); Refresh(); }
    private void LateUpdate()
    {
        BindOwner();
        if (awaitingOwner || (shownElement != WeaponElement.None &&
            (equipment == null || equipment.CurrentWeaponRoot != transform))) Refresh();
    }
    private void OnDisable()
    {
        MeleeElementHitVfxService.ReleaseElementDemand(this);
        if (equipment != null) equipment.WeaponSlotsChanged -= Refresh;
        if (energy != null) energy.Changed -= Refresh;
        DisposeEffects(); shownElement = WeaponElement.None;
        equipment = null; energy = null; awaitingOwner = false;
    }
    private void Refresh()
    {
        if (!isActiveAndEnabled) return;
        BindOwner();
        var element = ResolveEquippedElement();
        if (Application.isPlaying && !IsEditorPreview && shownElement != element)
            MeleeElementHitVfxService.SetElementDemand(this, element);
        awaitingOwner = equipment != null && equipment.CurrentWeaponRoot == null;
        ShowElement(element, ResolveNormalizedEnergyFor(element));
    }
    private float ResolveNormalizedEnergyFor(WeaponElement element)
    {
        if (energy == null || !energy.isActiveAndEnabled || equipment == null || equipment.CurrentWeaponItem == null
            || energy.WeaponInstanceId != equipment.CurrentWeaponItem.runtimeInstanceId || energy.Element != element) return 0;
        return energy.Normalized;
    }
    private void ShowElement(WeaponElement element, float normalizedEnergy)
    {
        if (shownElement != element) { DisposeEffects(); shownElement = element; }
        if (!OverburstElementRules.IsActive(element)) return;
        var tuning = SelectTuning(element);
        float strength = Mathf.Lerp(tuning.idleStrength, 1f, Mathf.Clamp01(normalizedEnergy));
        currentStrength = strength;
        if (bladePlayback == null)
        {
            bladePlayback = CreatePlayback(SelectBladeAccent(element), tuning.bladeCenter,
                tuning.bladeLength, tuning.bladeThickness, "WeaponEffects2Blade");
            bladePlayback?.SetEnergy(strength);
            bladePlayback?.PlayContinuously();
        }
        if (!SharesPackage && additionalPlayback == null)
        {
            additionalPlayback = CreatePlayback(SelectTrail(element), tuning.trailCenter,
                tuning.trailLength, tuning.trailScale, "WeaponEffects2AdditionalSwing");
            additionalPlayback?.SetEnergy(strength);
            additionalPlayback?.PlayContinuously();
        }
        bladePlayback?.SetEnergy(strength);
        additionalPlayback?.SetEnergy(strength);
    }
    private WeaponEffects2Playback CreatePlayback(GameObject source, float center, float length, float width, string name)
    {
        if (source == null || bladeRenderer == null) return null;
        var parent = new GameObject(name);
        parent.SetActive(false);
        parent.transform.SetParent(bladeRenderer.transform, false);
        Vector3 scale = bladeRenderer.transform.lossyScale;
        parent.transform.localScale = new Vector3(1f / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
            1f / Mathf.Max(.0001f, Mathf.Abs(scale.y)), 1f / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
        float span = bladeEffectBounds.size.z * length;
        float z = Mathf.Lerp(bladeEffectBounds.min.z, bladeEffectBounds.max.z, center) + span * .5f;
        var tuning = SelectTuning(shownElement);
        float trailSpan = bladeEffectBounds.size.z * tuning.trailLength;
        float trailZ = Mathf.Lerp(bladeEffectBounds.min.z, bladeEffectBounds.max.z, tuning.trailCenter) + trailSpan * .5f;
        return new WeaponEffects2Playback(source, parent.transform,
            new Vector3(bladeEffectBounds.center.x, bladeEffectBounds.center.y, z),
            new Vector3(width, width, span / WeaponEffects2Playback.AuthoredBladeLength), IsEditorPreview,
            tuning.trailDensity, tuning.trailSpread, tuning.trailParticleSize, tuning.trailParticleLifetime,
            new Vector3(bladeEffectBounds.center.x, bladeEffectBounds.center.y, trailZ),
            new Vector3(tuning.trailScale, tuning.trailScale, trailSpan / WeaponEffects2Playback.AuthoredBladeLength));
    }
    public void BeginTrail()
    {
        Refresh();
        BeginAdditional();
        if (OverburstElementRules.IsActive(shownElement))
            MeleeElementSfxService.TryPlaySlash(shownElement, trailAnchor != null ? trailAnchor.position : transform.position);
    }
    private void BeginAdditional()
    {
        // A complete package already contains its own wake. Distinct trails start at equip.
        if (SharesPackage || !OverburstElementRules.IsActive(shownElement)) return;
        var t = SelectTuning(shownElement);
        if (additionalPlayback == null)
        {
            additionalPlayback = CreatePlayback(SelectTrail(shownElement), t.trailCenter, t.trailLength,
                t.trailScale, "WeaponEffects2AdditionalSwing");
            additionalPlayback?.SetEnergy(currentStrength);
            additionalPlayback?.PlayContinuously();
        }
        additionalPlayback?.SetEnergy(currentStrength);
    }
    // Attack phases do not own a trail's lifetime; it stays on the equipped weapon.
    public void EndTrail() { }
    public void ClearTrail() { }
    private void DisposeEffects()
    {
        additionalPlayback?.Dispose(); additionalPlayback = null;
        bladePlayback?.Dispose(); bladePlayback = null;
    }
#if UNITY_EDITOR
    public void EditorPreviewEnergy(WeaponElement element, float normalizedEnergy)
    { if (IsEditorPreview) ShowElement(element, normalizedEnergy); }
    public void EditorSampleEnergy(float seconds)
    { if (IsEditorPreview) bladePlayback?.Sample(seconds); }
    public void EditorAdvanceEnergyPreview(float seconds)
    { if (IsEditorPreview) bladePlayback?.Advance(seconds); }
    public void EditorClearEnergyPreview()
    { if (IsEditorPreview) { DisposeEffects(); shownElement = WeaponElement.None; } }
    public void EditorBeginTrailPreview() { if (IsEditorPreview) BeginAdditional(); }
    public void EditorEndTrailPreview() { if (IsEditorPreview) EndTrail(); }
    public void EditorClearTrailPreview() { if (IsEditorPreview) ClearTrail(); }
    public void EditorAdvanceTrailPreview(float seconds)
    { if (IsEditorPreview) additionalPlayback?.Advance(seconds); }
    public int EditorTrailParticleCount => (bladePlayback?.Count ?? 0) + (additionalPlayback?.Count ?? 0);
#endif
    private void BindOwner()
    {
        PlayerEquipment nextEquipment = GetComponentInParent<PlayerEquipment>();
        OverburstElementEnergy nextEnergy = nextEquipment != null ? nextEquipment.GetComponent<OverburstElementEnergy>() : null;
        if (equipment == nextEquipment && energy == nextEnergy) return;
        if (equipment != null) equipment.WeaponSlotsChanged -= Refresh;
        if (energy != null) energy.Changed -= Refresh;
        equipment = nextEquipment;
        energy = nextEnergy;
        if (equipment != null) equipment.WeaponSlotsChanged += Refresh;
        if (energy != null) energy.Changed += Refresh;
        awaitingOwner = true;
    }
    private WeaponElement ResolveEquippedElement()
    {
        if (equipment == null || (energy != null && !energy.isActiveAndEnabled)
            || equipment.CurrentWeaponRoot != transform
            || equipment.CurrentWeaponItem == null
            || (equipment.GetComponent<CombatHealth>() is CombatHealth health && health.IsDead))
        {
            return WeaponElement.None;
        }

        WeaponElement element = equipment.CurrentWeaponItem.ResolvedElement;
        return OverburstElementRules.IsActive(element) ? element : WeaponElement.None;
    }

    private ElementTuning SelectTuning(WeaponElement element)
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

    private GameObject SelectTrail(WeaponElement element)
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

    private GameObject SelectBladeAccent(WeaponElement element)
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

}
