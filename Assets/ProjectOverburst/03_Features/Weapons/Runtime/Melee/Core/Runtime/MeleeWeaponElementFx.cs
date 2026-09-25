using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MeleeWeaponElementFx : MonoBehaviour, IWeaponTrailController
{
    [Header("Blade bindings")]
    [SerializeField] private Renderer bladeRenderer;
    [SerializeField] private Transform auraAnchor;
    [SerializeField] private Transform trailAnchor;

    [Header("MeshFX aura")]
    [SerializeField] private GameObject fireAura;
    [SerializeField] private GameObject waterAura;
    [SerializeField] private GameObject iceAura;
    [SerializeField] private GameObject electricAura;
    [SerializeField, Range(0.01f, 1f)] private float auraStartNormalized = 0.1f;
    [SerializeField, Range(0.01f, 1f)] private float auraParticleSizeScale = 0.42f;
    [SerializeField, Range(0.01f, 1f)] private float auraEmissionScale = 0.45f;
    [SerializeField, Range(0.01f, 1f)] private float auraSurfaceScale = 0.28f;

    [Header("Swing trail")]
    [SerializeField] private GameObject fireTrail;
    [SerializeField] private GameObject waterTrail;
    [SerializeField] private GameObject iceTrail;
    [SerializeField] private GameObject electricTrail;
    [SerializeField, Range(0.01f, 1f)] private float trailWidthScale = 0.16f;
    [SerializeField, Min(0.01f)] private float trailLifetime = 0.23f;
    [SerializeField] private Vector3[] trailOffsets =
    {
        new Vector3(0f, 0.55f, 0f),
        new Vector3(0f, 0.07f, 0f),
        new Vector3(0f, 0.42f, 0f)
    };

    private PlayerEquipment equipment;
    private OverburstElementEnergy energy;
    private WeaponElement shownElement;
    private GameObject auraContainer;
    private GameObject trailInstance;
    private ParticleSystem[] auraParticles;
    private float[] auraBaseSizes;
    private float[] auraBaseRates;
    private TrailRenderer[] trailRenderers;
    private Material auraSurfaceMaterial;
    private Color auraBaseDetailColor;
    private Color auraBaseFresnelColor;
    private Material[] bladeBaseMaterials;
    private bool awaitingOwner;

    private void OnEnable()
    {
        bladeBaseMaterials = bladeRenderer != null ? bladeRenderer.sharedMaterials : null;
        equipment = GetComponentInParent<PlayerEquipment>();
        energy = equipment != null ? equipment.GetComponent<OverburstElementEnergy>() : null;
        if (equipment != null) equipment.WeaponSlotsChanged += Refresh;
        if (energy != null) energy.Changed += Refresh;
        awaitingOwner = true;
        Refresh();
    }

    private void LateUpdate()
    {
        if (awaitingOwner || (shownElement != WeaponElement.None
            && (equipment == null || equipment.CurrentWeaponRoot != transform)))
        {
            Refresh();
        }
    }

    private void OnDisable()
    {
        if (equipment != null) equipment.WeaponSlotsChanged -= Refresh;
        if (energy != null) energy.Changed -= Refresh;
        ClearTrail();
        DestroyTrail();
        DestroyAura();
        shownElement = WeaponElement.None;
        awaitingOwner = false;
        equipment = null;
        energy = null;
    }

    public void BeginTrail()
    {
        Refresh();
        if (shownElement == WeaponElement.None || trailRenderers == null) return;
        for (int i = 0; i < trailRenderers.Length; i++)
        {
            if (trailRenderers[i] == null) continue;
            trailRenderers[i].Clear();
            trailRenderers[i].emitting = true;
        }
        MeleeElementSfxService.TryPlaySlash(
            shownElement,
            trailAnchor != null ? trailAnchor.position : transform.position);
    }

    public void EndTrail()
    {
        if (trailRenderers == null) return;
        for (int i = 0; i < trailRenderers.Length; i++)
            if (trailRenderers[i] != null) trailRenderers[i].emitting = false;
    }

    public void ClearTrail()
    {
        EndTrail();
        if (trailRenderers == null) return;
        for (int i = 0; i < trailRenderers.Length; i++)
            if (trailRenderers[i] != null) trailRenderers[i].Clear();
    }

    private void Refresh()
    {
        if (!isActiveAndEnabled) return;

        WeaponElement nextElement = ResolveEquippedElement();
        awaitingOwner = equipment != null && equipment.CurrentWeaponRoot == null;
        if (nextElement != shownElement)
        {
            ClearTrail();
            DestroyTrail();
            DestroyAura();
            shownElement = nextElement;
            if (shownElement != WeaponElement.None) CreateTrail();
        }

        if (shownElement == WeaponElement.None || energy == null
            || energy.Normalized < auraStartNormalized)
        {
            DestroyAura();
            return;
        }

        if (auraContainer == null) CreateAura();
        if (auraContainer != null) ApplyAuraStrength(energy.Normalized);
    }

    private WeaponElement ResolveEquippedElement()
    {
        if (equipment == null || energy == null || !energy.isActiveAndEnabled
            || equipment.CurrentWeaponRoot != transform
            || equipment.CurrentWeaponItem == null
            || energy.WeaponInstanceId != equipment.CurrentWeaponItem.runtimeInstanceId
            || energy.Element != equipment.CurrentWeaponItem.ResolvedElement)
        {
            return WeaponElement.None;
        }

        return OverburstElementRules.IsActive(energy.Element)
            ? energy.Element : WeaponElement.None;
    }

    private void CreateAura()
    {
        GameObject source = SelectAura(shownElement);
        if (source == null || bladeRenderer == null) return;

        auraContainer = new GameObject("ElementAura");
        auraContainer.transform.SetParent(auraAnchor != null ? auraAnchor : transform, false);
        auraContainer.SetActive(false);
        GameObject instance = Instantiate(source, auraContainer.transform, false);
        OverlayFX overlay = instance.GetComponent<OverlayFX>();
        if (overlay == null)
        {
            Debug.LogError("[MeleeWeaponElementFx] MeshFX OverlayFX is missing.", this);
            DestroyAura();
            return;
        }

        overlay.targetRenderer = bladeRenderer;
        auraContainer.SetActive(true);
        auraParticles = instance.GetComponentsInChildren<ParticleSystem>(true);
        auraBaseSizes = new float[auraParticles.Length];
        auraBaseRates = new float[auraParticles.Length];
        for (int i = 0; i < auraParticles.Length; i++)
        {
            auraBaseSizes[i] = auraParticles[i].main.startSizeMultiplier;
            auraBaseRates[i] = auraParticles[i].emission.rateOverTimeMultiplier;
        }

        Material[] current = bladeRenderer.sharedMaterials;
        for (int i = 0; i < current.Length; i++)
        {
            Material material = current[i];
            if (material == null || !material.name.StartsWith(overlay.overlayMaterial.name, StringComparison.Ordinal)
                || !material.name.EndsWith("(Runtime)", StringComparison.Ordinal)) continue;
            auraSurfaceMaterial = material;
            if (material.HasProperty("_Detail_Noise_Color"))
                auraBaseDetailColor = material.GetColor("_Detail_Noise_Color");
            if (material.HasProperty("_FresnelColor"))
                auraBaseFresnelColor = material.GetColor("_FresnelColor");
            break;
        }
    }

    private void ApplyAuraStrength(float normalizedEnergy)
    {
        float strength = Mathf.InverseLerp(auraStartNormalized, 1f, normalizedEnergy);
        for (int i = 0; i < auraParticles.Length; i++)
        {
            ParticleSystem system = auraParticles[i];
            if (system == null) continue;
            var main = system.main;
            main.startSizeMultiplier = auraBaseSizes[i] * auraParticleSizeScale
                * Mathf.Lerp(0.6f, 1f, strength);
            var emission = system.emission;
            emission.rateOverTimeMultiplier = auraBaseRates[i] * auraEmissionScale
                * Mathf.Lerp(0.55f, 1f, strength);
        }

        if (auraSurfaceMaterial == null) return;
        float surface = auraSurfaceScale * Mathf.Lerp(0.6f, 1f, strength);
        if (auraSurfaceMaterial.HasProperty("_Detail_Noise_Color"))
            auraSurfaceMaterial.SetColor("_Detail_Noise_Color", auraBaseDetailColor * surface);
        if (auraSurfaceMaterial.HasProperty("_FresnelColor"))
            auraSurfaceMaterial.SetColor("_FresnelColor", auraBaseFresnelColor * surface);
    }

    private void DestroyAura()
    {
        if (auraContainer == null && auraSurfaceMaterial == null) return;
        if (auraContainer != null) auraContainer.SetActive(false);
        if (bladeRenderer != null && bladeBaseMaterials != null)
            bladeRenderer.sharedMaterials = bladeBaseMaterials;
        if (auraSurfaceMaterial != null) DestroyRuntimeObject(auraSurfaceMaterial);
        if (auraContainer != null) DestroyRuntimeObject(auraContainer);
        auraSurfaceMaterial = null;
        auraContainer = null;
        auraParticles = null;
        auraBaseSizes = null;
        auraBaseRates = null;
    }

    private void CreateTrail()
    {
        GameObject source = SelectTrail(shownElement);
        if (source == null) return;
        trailInstance = new GameObject("ElementSwingTrail");
        trailInstance.transform.SetParent(trailAnchor != null ? trailAnchor : transform, false);
        trailInstance.SetActive(false);
        Instantiate(source, trailInstance.transform, false);
        foreach (AudioSource audio in trailInstance.GetComponentsInChildren<AudioSource>(true))
        {
            audio.Stop();
            audio.playOnAwake = false;
            audio.enabled = false;
        }

        trailRenderers = trailInstance.GetComponentsInChildren<TrailRenderer>(true);
        for (int i = 0; i < trailRenderers.Length; i++)
        {
            TrailRenderer trail = trailRenderers[i];
            trail.emitting = false;
            trail.Clear();
            trail.time = trailLifetime;
            trail.widthMultiplier *= trailWidthScale;
            trail.minVertexDistance = 0.025f;
            if (i < trailOffsets.Length) trail.transform.localPosition = trailOffsets[i];
        }
        trailInstance.SetActive(true);
    }

    private void DestroyTrail()
    {
        if (trailInstance != null) DestroyRuntimeObject(trailInstance);
        trailInstance = null;
        trailRenderers = null;
    }

    private GameObject SelectAura(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire: return fireAura;
            case WeaponElement.Water: return waterAura;
            case WeaponElement.Ice: return iceAura;
            case WeaponElement.Electric: return electricAura;
            default: return null;
        }
    }

    private GameObject SelectTrail(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire: return fireTrail;
            case WeaponElement.Water: return waterTrail;
            case WeaponElement.Ice: return iceTrail;
            case WeaponElement.Electric: return electricTrail;
            default: return null;
        }
    }

    private static void DestroyRuntimeObject(UnityEngine.Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }
}
