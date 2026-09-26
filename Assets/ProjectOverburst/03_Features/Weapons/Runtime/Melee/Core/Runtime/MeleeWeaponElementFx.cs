using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MeleeWeaponElementFx : MonoBehaviour, IWeaponTrailController
{
    [Header("Blade bindings")]
    [SerializeField] private Renderer bladeRenderer;
    [SerializeField] private Mesh auraEmissionMesh;
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
    [SerializeField, Range(0.01f, 1f)] private float trailWidthScale = 1f;
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
    private ParticleSystem.MinMaxCurve[] auraBaseSizes;
    private float[] auraBaseRates;
    private TrailRenderer[] trailRenderers;
    private Material auraSurfaceMaterial;
    private Color auraBaseDetailColor;
    private Color auraBaseFresnelColor;
    private Material[] bladeBaseMaterials;
    private bool awaitingOwner;
    private float trailStopTime = -1f;

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
        if (trailStopTime >= 0f && Time.time >= trailStopTime) ClearTrail();
        BindOwner();
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
        trailStopTime = -1f;
        for (int i = 0; i < trailRenderers.Length; i++)
        {
            if (trailRenderers[i] == null) continue;
            trailRenderers[i].Clear();
            trailRenderers[i].enabled = true;
            trailRenderers[i].emitting = true;
        }
        MeleeElementSfxService.TryPlaySlash(
            shownElement,
            trailAnchor != null ? trailAnchor.position : transform.position);
    }

    public void EndTrail()
    {
        if (trailRenderers == null) return;
        trailStopTime = Time.time + trailLifetime;
        for (int i = 0; i < trailRenderers.Length; i++)
            if (trailRenderers[i] != null) trailRenderers[i].emitting = false;
    }

    public void ClearTrail()
    {
        EndTrail();
        trailStopTime = -1f;
        if (trailRenderers == null) return;
        for (int i = 0; i < trailRenderers.Length; i++)
            if (trailRenderers[i] != null)
            {
                trailRenderers[i].Clear();
                trailRenderers[i].enabled = false;
            }
    }

    private void Refresh()
    {
        if (!isActiveAndEnabled) return;

        BindOwner();
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

    private GameObject CreateBladeSpace(string name)
    {
        var root = new GameObject(name);
        root.SetActive(false);
        root.transform.SetParent(bladeRenderer.transform, false);
        Vector3 scale = bladeRenderer.transform.lossyScale;
        root.transform.localScale = new Vector3(1f / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
            1f / Mathf.Max(.0001f, Mathf.Abs(scale.y)), 1f / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
        return root;
    }

    private void CreateAura()
    {
        GameObject source = SelectAura(shownElement);
        if (source == null || bladeRenderer == null || auraEmissionMesh == null) return;

        auraContainer = CreateBladeSpace("ElementAura");
        GameObject instance = Instantiate(source, auraContainer.transform, false);
        // Keep vendor visuals, but own binding/lifetime. Vendor mesh-renderer scaling assumes a unit-scale model.
        OverlayFX overlay = instance.GetComponent<OverlayFX>();
        if (overlay == null || overlay.overlayMaterial == null)
        {
            DestroyAura();
            return;
        }
        overlay.enabled = false;
        overlay.targetRenderer = null;
        auraSurfaceMaterial = new Material(overlay.overlayMaterial) { name = overlay.overlayMaterial.name + " (Weapon Runtime)" };
        var bounds = bladeRenderer.localBounds;
        Vector3 minimum = bounds.min;
        minimum.z += bounds.size.z * .22f;
        auraSurfaceMaterial.SetVector("_LocalBoundsMinimum", minimum);
        auraSurfaceMaterial.SetVector("_LocalBoundsMaximum", bounds.max);
        if (auraSurfaceMaterial.HasProperty("_DetailVertexOffsetChannel"))
        {
            Vector3 modelScale = bladeRenderer.transform.lossyScale;
            float scale = Mathf.Max(Mathf.Abs(modelScale.x), Mathf.Abs(modelScale.y), Mathf.Abs(modelScale.z));
            auraSurfaceMaterial.SetVector("_DetailVertexOffsetChannel",
                auraSurfaceMaterial.GetVector("_DetailVertexOffsetChannel") * (.5f / Mathf.Max(.0001f, scale)));
        }
        OverlayFX.ShaderKeywordController.SetGradientAxis(auraSurfaceMaterial, OverlayFX.GradientAxis.Z);
        OverlayFX.ShaderKeywordController.SetUvDirection(auraSurfaceMaterial, OverlayFX.UvDirection.Z);
        if (auraSurfaceMaterial.HasProperty("_Detail_Noise_Color")) auraBaseDetailColor = auraSurfaceMaterial.GetColor("_Detail_Noise_Color");
        if (auraSurfaceMaterial.HasProperty("_FresnelColor")) auraBaseFresnelColor = auraSurfaceMaterial.GetColor("_FresnelColor");
        Material[] materials = new Material[bladeBaseMaterials.Length + 1];
        Array.Copy(bladeBaseMaterials, materials, bladeBaseMaterials.Length);
        materials[materials.Length - 1] = auraSurfaceMaterial;
        bladeRenderer.sharedMaterials = materials;

        auraParticles = instance.GetComponentsInChildren<ParticleSystem>(true);
        auraBaseSizes = new ParticleSystem.MinMaxCurve[auraParticles.Length];
        auraBaseRates = new float[auraParticles.Length];
        for (int i = 0; i < auraParticles.Length; i++)
        {
            ParticleSystem ps = auraParticles[i];
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.transform.localPosition = Vector3.zero;
            ps.transform.localRotation = Quaternion.identity;
            ps.transform.localScale = Vector3.one;
            var main = ps.main;
            main.playOnAwake = false;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startSpeed = ScaleCurve(main.startSpeed, .15f);
            main.startLifetime = ScaleCurve(main.startLifetime, .65f);
            // Retain each vendor particle's size ratio, lifetime shape, flipbook and colour.
            auraBaseSizes[i] = main.startSize;
            auraBaseRates[i] = ps.emission.rateOverTimeMultiplier;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Mesh;
            shape.mesh = auraEmissionMesh;
            shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
            shape.scale = Vector3.one;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
            var velocity = ps.velocityOverLifetime;
            velocity.x = ScaleCurve(velocity.x, .15f);
            velocity.y = ScaleCurve(velocity.y, .15f);
            velocity.z = ScaleCurve(velocity.z, .15f);
            velocity.orbitalOffsetX = ScaleCurve(velocity.orbitalOffsetX, .15f);
            velocity.orbitalOffsetY = ScaleCurve(velocity.orbitalOffsetY, .15f);
            velocity.orbitalOffsetZ = ScaleCurve(velocity.orbitalOffsetZ, .15f);
            var noise = ps.noise;
            noise.strength = ScaleCurve(noise.strength, .05f);
        }
        ApplyAuraStrength(energy != null ? energy.Normalized : 1f);
        auraContainer.SetActive(true);
        foreach (var ps in auraParticles) ps.Play(false);
    }
    private static ParticleSystem.MinMaxCurve ScaleCurve(ParticleSystem.MinMaxCurve curve, float scale)
    {
        if (curve.mode == ParticleSystemCurveMode.Constant) curve.constant *= scale;
        else if (curve.mode == ParticleSystemCurveMode.TwoConstants)
        {
            curve.constantMin *= scale;
            curve.constantMax *= scale;
        }
        else curve.curveMultiplier *= scale;
        return curve;
    }
    private void ApplyAuraStrength(float normalizedEnergy)
    {
        float strength = Mathf.InverseLerp(auraStartNormalized, 1f, normalizedEnergy);
        for (int i = 0; i < auraParticles.Length; i++)
        {
            ParticleSystem system = auraParticles[i];
            if (system == null) continue;
            var main = system.main;
            main.startSize = ScaleCurve(auraBaseSizes[i], auraParticleSizeScale * Mathf.Lerp(.75f, 1f, strength));
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
        if (source == null || bladeRenderer == null || auraEmissionMesh == null) return;
        trailInstance = CreateBladeSpace("ElementSwingTrail");
        Instantiate(source, trailInstance.transform, false);
        foreach (AudioSource audio in trailInstance.GetComponentsInChildren<AudioSource>(true))
        {
            audio.Stop(); audio.playOnAwake = false; audio.enabled = false;
        }
        Bounds bounds = auraEmissionMesh.bounds;
        trailRenderers = trailInstance.GetComponentsInChildren<TrailRenderer>(true);
        float sourceMaxWidth = 0f;
        foreach (TrailRenderer trail in trailRenderers)
            if (trail != null) sourceMaxWidth = Mathf.Max(sourceMaxWidth, trail.widthMultiplier);
        float bladeLength = bounds.size.z;
        foreach (TrailRenderer trail in trailRenderers)
        {
            trail.emitting = false;
            trail.enabled = false;
            trail.Clear();
            trail.time = trailLifetime;
            trail.widthMultiplier = sourceMaxWidth > 0f
                ? trail.widthMultiplier / sourceMaxWidth * bladeLength * trailWidthScale
                : 0f;
            trail.minVertexDistance = .015f;
            // The prefab layers share one blade anchor; never offset them beyond WeaponTip.
            trail.transform.position = trailInstance.transform.TransformPoint(new Vector3(bounds.center.x, bounds.center.y,
                Mathf.Lerp(bounds.min.z, bounds.max.z, .72f)));
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
