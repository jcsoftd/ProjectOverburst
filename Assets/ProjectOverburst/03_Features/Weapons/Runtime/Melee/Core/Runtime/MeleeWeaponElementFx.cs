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

    [Header("Sword-tip trail trial")]
    [SerializeField] private GameObject fireTipTrail;
    [SerializeField] private GameObject iceTipTrail;
    [SerializeField] private GameObject electricTipTrail;
    [SerializeField] private GameObject darkTipTrail;
    [SerializeField] private GameObject lightTipTrail;
    [SerializeField, Range(.05f, 1f)] private float tipTrailLifetime = .2f;
    [SerializeField, Range(.01f, 1f)] private float tipTrailWidthScale = .15f;

    [Serializable]
    public sealed class ElementTuning
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
    private GameObject tipTrailRoot;
    private TrailRenderer[] tipTrailRenderers;
    private float[] tipTrailBaseWidths;
    private ParticleSystem[] tipTrailParticles;
    private ParticleSystem.MinMaxCurve[] tipParticleBaseSizes;
    private float[] tipParticleTimeRates, tipParticleDistanceRates;
    private float[] tipPreviewEmissionCarry;
    private WeaponElectricLineAfterimage bladeAfterimage;
    private bool tipTrailEmitting;
    private float currentStrength = 1f;
    private float currentNormalizedEnergy = 1f;
    private static GreatswordElementFxProfile sharedGreatswordProfile;
#if UNITY_EDITOR
    private bool editorUseLocalFxSettings;
#endif
    private GreatswordElementFxProfile SharedGreatswordProfile
    {
        get
        {
#if UNITY_EDITOR
            if (editorUseLocalFxSettings) return null;
#endif
            if (equipment != null && equipment.CurrentWeaponItem != null &&
                (!(equipment.CurrentWeaponItem.baseData is WeaponItemData weapon) ||
                 weapon.weaponClass != WeaponClass.Greatsword)) return null;
            if (sharedGreatswordProfile == null)
                sharedGreatswordProfile = Resources.Load<GreatswordElementFxProfile>(GreatswordElementFxProfile.ResourcePath);
            return sharedGreatswordProfile;
        }
    }
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
        if (!IsEditorPreview) SampleElectricAfterimages(Time.deltaTime);
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
        currentNormalizedEnergy = Mathf.Clamp01(normalizedEnergy);
        float strength = element == WeaponElement.Electric ? currentNormalizedEnergy
            : element == WeaponElement.Fire || element == WeaponElement.Dark || element == WeaponElement.Light
                ? currentNormalizedEnergy * (1f + tuning.idleStrength * (1f - currentNormalizedEnergy))
            : Mathf.Lerp(tuning.idleStrength, 1f, currentNormalizedEnergy);
        currentStrength = strength;
        if (bladePlayback == null)
        {
            bladePlayback = CreatePlayback(SelectBladeAccent(element), tuning.bladeCenter,
                tuning.bladeLength, tuning.bladeThickness, "WeaponEffects2Blade");
            bladePlayback?.SetBladeWidthMultiplier(Mathf.Clamp01(normalizedEnergy));
            bladePlayback?.SetTrailWidthMultiplier(currentNormalizedEnergy);
            bladePlayback?.SetEnergy(strength);
            bladePlayback?.PlayContinuously();
        }
        bladeAfterimage = bladePlayback?.Root.GetComponentInChildren<WeaponElectricLineAfterimage>(true);
        bladeAfterimage?.SetEnergy(currentNormalizedEnergy);
        if (currentNormalizedEnergy > 0f &&
            bladeAfterimage != null && !bladeAfterimage.IsEmitting) bladeAfterimage.Begin();
        if (!SharesPackage && additionalPlayback == null)
        {
            additionalPlayback = CreatePlayback(SelectTrail(element), tuning.trailCenter,
                tuning.trailLength, tuning.trailScale, "WeaponEffects2AdditionalSwing");
            additionalPlayback?.SetBladeWidthMultiplier(currentNormalizedEnergy);
            additionalPlayback?.SetTrailWidthMultiplier(currentNormalizedEnergy);
            additionalPlayback?.SetEnergy(strength);
            additionalPlayback?.PlayContinuously();
        }
        EnsureTipTrail(SelectTipTrail(element));
        SetTipTrailEnergy(element == WeaponElement.Electric ? currentNormalizedEnergy : 1f);
        bladePlayback?.SetBladeWidthMultiplier(currentNormalizedEnergy);
        bladePlayback?.SetTrailWidthMultiplier(currentNormalizedEnergy);
        bladePlayback?.SetEnergy(strength);
        additionalPlayback?.SetBladeWidthMultiplier(currentNormalizedEnergy);
        additionalPlayback?.SetTrailWidthMultiplier(currentNormalizedEnergy);
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
            new Vector3(tuning.trailScale, tuning.trailScale, trailSpan / WeaponEffects2Playback.AuthoredBladeLength),
            shownElement == WeaponElement.Fire || shownElement == WeaponElement.Dark || shownElement == WeaponElement.Light);
    }
    public void BeginTrail()
    {
        Refresh();
        BeginAdditional();
        BeginTipTrail();
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
            additionalPlayback?.SetBladeWidthMultiplier(currentNormalizedEnergy);
            additionalPlayback?.SetTrailWidthMultiplier(currentNormalizedEnergy);
            additionalPlayback?.SetEnergy(currentStrength);
            additionalPlayback?.PlayContinuously();
        }
        additionalPlayback?.SetEnergy(currentStrength);
    }
    // Weapon Effects 2 stays equipped; the separate sword-tip trial follows attack phases.
    public void EndTrail()
    { SetTipTrailEmitting(false, false); }
    public void ClearTrail()
    { SetTipTrailEmitting(false, true); }
    private void DisposeEffects()
    {
        DisposeTipTrail();
        bladeAfterimage?.Clear();
        additionalPlayback?.Dispose(); additionalPlayback = null;
        bladePlayback?.Dispose(); bladePlayback = null;
        bladeAfterimage = null;
    }

    private void EnsureTipTrail(GameObject source)
    {
        if (tipTrailRoot != null || source == null || trailAnchor == null) return;
        tipTrailRoot = new GameObject("VefectsSwordTipTrail");
        tipTrailRoot.SetActive(false);
        tipTrailRoot.transform.SetParent(trailAnchor, false);
        GameObject instance = Instantiate(source, tipTrailRoot.transform, false);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        foreach (var audioSource in instance.GetComponentsInChildren<AudioSource>(true))
            audioSource.enabled = false; // Existing weapon swing SFX remains the sole audio source.
        tipTrailRenderers = instance.GetComponentsInChildren<TrailRenderer>(true);
        tipTrailBaseWidths = new float[tipTrailRenderers.Length];
        int tipIndex = 0;
        foreach (var trail in tipTrailRenderers)
        {
            var profile = SharedGreatswordProfile;
            trail.time = Mathf.Min(trail.time, profile != null ? profile.TipTrailLifetime : tipTrailLifetime);
            trail.widthMultiplier *= profile != null ? profile.TipTrailWidthScale : tipTrailWidthScale;
            trail.minVertexDistance = Mathf.Min(trail.minVertexDistance, .01f);
            trail.numCornerVertices = Mathf.Max(trail.numCornerVertices, 2);
            tipTrailBaseWidths[tipIndex++] = trail.widthMultiplier;
            trail.emitting = false;
            trail.Clear();
        }
        tipTrailParticles = instance.GetComponentsInChildren<ParticleSystem>(true);
        tipParticleBaseSizes = new ParticleSystem.MinMaxCurve[tipTrailParticles.Length];
        tipParticleTimeRates = new float[tipTrailParticles.Length];
        tipParticleDistanceRates = new float[tipTrailParticles.Length];
        tipPreviewEmissionCarry = new float[tipTrailParticles.Length];
        for (int i = 0; i < tipTrailParticles.Length; i++)
        {
            var particle = tipTrailParticles[i];
            tipParticleBaseSizes[i] = particle.main.startSize;
            tipParticleTimeRates[i] = particle.emission.rateOverTimeMultiplier;
            tipParticleDistanceRates[i] = particle.emission.rateOverDistanceMultiplier;
            particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        tipTrailRoot.SetActive(true);
        SetTipTrailEmitting(false, true);
    }

    private void BeginTipTrail()
    {
        if (tipTrailRenderers == null) return;
        SetTipTrailEmitting(false, true);
        SetTipTrailEmitting(true, false);
    }

    private void SetTipTrailEmitting(bool emitting, bool clear)
    {
        tipTrailEmitting = emitting;
        if (tipTrailRenderers == null) return;
        foreach (var trail in tipTrailRenderers)
        {
            trail.emitting = emitting && (shownElement != WeaponElement.Electric || currentNormalizedEnergy > 0f);
            if (clear) trail.Clear();
        }
        if (tipTrailParticles == null) return;
        if (clear && tipPreviewEmissionCarry != null) Array.Clear(tipPreviewEmissionCarry, 0, tipPreviewEmissionCarry.Length);
        foreach (var particle in tipTrailParticles)
        {
            var emission = particle.emission;
            emission.enabled = !IsEditorPreview && emitting && currentNormalizedEnergy > 0f;
            if (emitting && currentNormalizedEnergy > 0f)
                particle.Play(false);
            else if (clear) particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void SetTipTrailEnergy(float normalizedEnergy)
    {
        float energy = Mathf.Clamp01(normalizedEnergy);
        if (tipTrailRenderers == null) return;
        for (int i = 0; i < tipTrailRenderers.Length; i++)
        {
            var trail = tipTrailRenderers[i];
            trail.widthMultiplier = tipTrailBaseWidths[i] * energy;
            trail.emitting = tipTrailEmitting && energy > 0f;
            if (energy <= 0f) trail.Clear();
        }
        if (tipTrailParticles == null) return;
        for (int i = 0; i < tipTrailParticles.Length; i++)
        {
            var particle = tipTrailParticles[i];
            var main = particle.main;
            main.startSize = ScaleTipCurve(tipParticleBaseSizes[i], energy);
            var emission = particle.emission;
            emission.rateOverTimeMultiplier = tipParticleTimeRates[i] * energy;
            emission.rateOverDistanceMultiplier = tipParticleDistanceRates[i] * energy;
            emission.enabled = !IsEditorPreview && tipTrailEmitting && energy > 0f;
            if (tipTrailEmitting && energy > 0f && !particle.isEmitting) particle.Play(false);
            if (energy <= 0f) particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private static ParticleSystem.MinMaxCurve ScaleTipCurve(ParticleSystem.MinMaxCurve curve, float amount)
    {
        if (curve.mode == ParticleSystemCurveMode.Constant || curve.mode == ParticleSystemCurveMode.TwoConstants)
        { curve.constantMin *= amount; curve.constantMax *= amount; }
        else curve.curveMultiplier *= amount;
        return curve;
    }

    private void DisposeTipTrail()
    {
        SetTipTrailEmitting(false, true);
        tipTrailRenderers = null;
        tipTrailBaseWidths = null;
        tipTrailParticles = null;
        tipParticleBaseSizes = null;
        tipParticleTimeRates = tipParticleDistanceRates = null;
        tipPreviewEmissionCarry = null;
        if (tipTrailRoot == null) return;
        tipTrailRoot.SetActive(false);
        if (Application.isPlaying) Destroy(tipTrailRoot);
        else DestroyImmediate(tipTrailRoot);
        tipTrailRoot = null;
    }
#if UNITY_EDITOR
    // The tuner previews unsaved values copied onto its isolated preview component.
    public void EditorUseLocalFxSettings() { editorUseLocalFxSettings = true; }
    public void EditorPreviewEnergy(WeaponElement element, float normalizedEnergy)
    { if (IsEditorPreview) ShowElement(element, normalizedEnergy); }
    public void EditorSampleEnergy(float seconds)
    { if (IsEditorPreview) bladePlayback?.Sample(seconds); }
    public void EditorAdvanceEnergyPreview(float seconds)
    { if (IsEditorPreview) bladePlayback?.Advance(seconds); }
    public void EditorClearEnergyPreview()
    { if (IsEditorPreview) { DisposeEffects(); shownElement = WeaponElement.None; } }
    public void EditorBeginTrailPreview()
    { if (IsEditorPreview) { BeginAdditional(); BeginTipTrail(); } }
    public void EditorEndTrailPreview() { if (IsEditorPreview) EndTrail(); }
    public void EditorClearTrailPreview()
    {
        if (!IsEditorPreview) return;
        ClearTrail();
        bladeAfterimage?.Clear();
        if (currentNormalizedEnergy > 0f) bladeAfterimage?.Begin();
    }
    public void EditorAdvanceTrailPreview(float seconds)
    {
        if (!IsEditorPreview) return;
        additionalPlayback?.Advance(seconds);
        if (tipTrailEmitting && tipTrailRenderers != null)
            foreach (var trail in tipTrailRenderers) trail.AddPosition(trail.transform.position);
        if (tipTrailParticles != null)
            for (int i = 0; i < tipTrailParticles.Length; i++)
            {
                var particle = tipTrailParticles[i];
                if (particle.gameObject.activeInHierarchy)
                {
                    particle.Simulate(seconds, false, false, false);
                    if (tipTrailEmitting && currentNormalizedEnergy > 0f)
                    {
                        // Editor scrub uses a manual clock. Place line particles at each sampled
                        // sword-tip position so a fast sweep leaves a reproducible world-space wake.
                        tipPreviewEmissionCarry[i] += tipParticleTimeRates[i] * currentNormalizedEnergy * seconds;
                        int count = Mathf.FloorToInt(tipPreviewEmissionCarry[i]);
                        if (count > 0)
                        {
                            tipPreviewEmissionCarry[i] -= count;
                            particle.Emit(count);
                        }
                    }
                    particle.Pause(false);
                }
            }
        SampleElectricAfterimages(seconds);
    }
    public int EditorTrailParticleCount => (bladePlayback?.Count ?? 0) + (additionalPlayback?.Count ?? 0);
#endif
    private void SampleElectricAfterimages(float seconds)
    {
        if (bladeAfterimage != null && bladeRenderer != null && trailAnchor != null)
        {
            Vector3 tip = trailAnchor.position;
            Vector3 direction = tip - bladeRenderer.bounds.center;
            if (direction.sqrMagnitude < .0001f) direction = bladeRenderer.transform.forward;
            Vector3 bladeBase = tip - direction.normalized * bladeEffectBounds.size.z;
            bladeAfterimage?.Sample(seconds, bladeBase, tip);
        }
    }

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
        var profile = SharedGreatswordProfile;
        if (profile != null) return profile.TuningFor(element);
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
        var profile = SharedGreatswordProfile;
        if (profile != null) return profile.TrailFor(element);
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
        var profile = SharedGreatswordProfile;
        if (profile != null) return profile.BladeFor(element);
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

    private GameObject SelectTipTrail(WeaponElement element)
    {
        var profile = SharedGreatswordProfile;
        if (profile != null) return profile.TipFor(element);
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
