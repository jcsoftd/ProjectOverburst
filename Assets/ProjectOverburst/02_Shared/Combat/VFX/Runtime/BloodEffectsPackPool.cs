using UnityEngine;

// Fixed prefab leases. Components and material blocks are cached before combat.
public sealed class BloodEffectsPackPool
{
    public const int Capacity = 48;
    public static float SizeMultiplier => BloodComparisonTuning.Scale;
    sealed class Slot
    {
        public GameObject root;
        public ParticleSystem[] systems;
        public Renderer[] renderers;
        public int variant, priority;
        public float until, baseScale;
        public BloodHitProfile profile;
        public VolumetricBloodAnimationData animation;
        public float started;
    }
    readonly Slot[] slots = new Slot[Capacity];
    readonly int[] targets = new int[96], variants = new int[96];
    readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
    readonly System.Collections.Generic.Dictionary<Material, Material> profileMaterials = new System.Collections.Generic.Dictionary<Material, Material>();
    readonly BloodEffectsPackCatalog catalog;
    int cursor;
    public int ActiveCount { get; private set; }
    public int PlayedCount { get; private set; }
    public int PreemptedCount { get; private set; }
    public int LastVariant { get; private set; } = -1;
    public float LastBaseScale { get; private set; }
    public Quaternion LastRotation { get; private set; }
    public GameObject LastGroundPrefab => LastVariant >= 0 ? catalog.sprays[LastVariant].groundPrefab : null;
    public uint PlayedVariants { get; private set; }
    public bool Ready { get; private set; }

    public BloodEffectsPackPool(Transform parent, BloodEffectsPackCatalog source)
    {
        catalog = source;
        if (source == null || source.sprays == null || source.sprays.Length == 0) return;
        for (int i = 0; i < Capacity; i++)
        {
            int variant = i % source.sprays.Length;
            if (source.sprays[variant]?.prefab == null) { Dispose(); return; }
            var root = Object.Instantiate(source.sprays[variant].prefab, parent);
            root.name = (source.volumetric ? "Blood Volumetric " : "Blood Pack ") + i;
            root.SetActive(false);
            var slot = new Slot { root = root, variant = variant,
                systems = root.GetComponentsInChildren<ParticleSystem>(true), renderers = root.GetComponentsInChildren<Renderer>(true) };
            foreach (var ps in slot.systems) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var renderer in slot.renderers)
            {
                var shared = renderer.sharedMaterials;
                for (int materialIndex = 0; materialIndex < shared.Length; materialIndex++) shared[materialIndex] = ProfileMaterial(shared[materialIndex]);
                renderer.sharedMaterials = shared;
            }
            slot.animation = root.GetComponent<VolumetricBloodAnimationData>();
            if (source.volumetric && (slot.animation == null || slot.animation.layers == null || slot.animation.layers.Length == 0)) { Object.Destroy(root); Dispose(); return; }
            slots[i] = slot;
        }
        Ready = true;
    }
    Material ProfileMaterial(Material source)
    {
        if (!source || !catalog.sprayProfileShader) return source;
        if (!profileMaterials.TryGetValue(source, out var material))
        {
            material = new Material(source) { name = source.name + " profile", hideFlags = HideFlags.HideAndDontSave };
            material.shader = catalog.sprayProfileShader;
            profileMaterials.Add(source, material);
        }
        return material;
    }
    public void Tick(float now)
    {
        foreach (var slot in slots) if (slot != null && slot.until > 0f)
        {
            if (now >= slot.until) Release(slot);
            else if (slot.animation != null) ApplyAnimation(slot, now - slot.started);
        }
    }
    public bool Play(BloodHitProfile profile, Vector3 point, Vector3 direction, CombatImpactShape shape,
        float size, int priority, uint seed, int target, bool drip = false, bool accented = false)
    {
        if (!Ready) return false;
        int previous = -1;
        if (target != 0) for (int i = 0; i < targets.Length; i++) if (targets[i] == target) { previous = variants[i]; break; }
        int variant = catalog.ResolveSpray(shape, priority, seed, previous, drip, accented);
        if (variant < 0) return false;
        bool preferAccent = accented && catalog.sprays[variant].impactAccent;
        Slot chosen = null;
        foreach (var slot in slots) if (slot.variant == variant && slot.until == 0f) { chosen = slot; break; }
        // A busy form can borrow another eligible lease instead of dropping while the pool is free.
        if (chosen == null)
            for (int pass = 0; pass < 2 && chosen == null; pass++)
                for (int offset = 1; offset < catalog.sprays.Length && chosen == null; offset++)
                {
                    int candidate = (variant + offset) % catalog.sprays.Length;
                    if (!catalog.Accepts(candidate, shape, priority, drip, accented) || (pass == 0 && (candidate == previous || (preferAccent && !catalog.sprays[candidate].impactAccent)))) continue;
                    foreach (var slot in slots) if (slot.variant == candidate && slot.until == 0f) { chosen = slot; break; }
                }
        if (chosen == null && priority > 0)
            foreach (var slot in slots)
                if (catalog.Accepts(slot.variant, shape, priority, drip, accented) && slot.priority < priority
                    && (chosen == null || slot.until < chosen.until)) chosen = slot;
        if (chosen == null) return false;
        variant = chosen.variant;
        if (chosen.until > 0f) { PreemptedCount++; Release(chosen); }
        var definition = catalog.sprays[variant];
        direction = direction.sqrMagnitude < .0001f ? Vector3.forward : direction.normalized;
        Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > .95f ? Vector3.forward : Vector3.up;
        // VAT includes world gravity; tilt would rotate the fall sideways. Keep only attack yaw.
        var rotation = catalog.volumetric ? Quaternion.Euler(0f, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + 270f, 0f)
            : Quaternion.LookRotation(direction, up);
        chosen.root.transform.SetPositionAndRotation(point, rotation * Quaternion.Euler(definition.localEuler));
        // Profile sizes were authored for VFX Graph. Normalize around the game's 3.5 baseline.
        chosen.baseScale = definition.scale * Mathf.Clamp(profile.size / 3.5f, .4f, 1.6f)
            * size * (priority >= 2 ? 1.2f : priority == 1 ? 1.1f : 1f);
        chosen.root.transform.localScale = Vector3.one * chosen.baseScale * SizeMultiplier;
        chosen.profile = profile;
        chosen.started = Time.time;
        foreach (var renderer in chosen.renderers) renderer.enabled = true;
        ApplyStyle(chosen);
        if (chosen.animation != null) ApplyAnimation(chosen, 0f);
        for (int i = 0; i < chosen.systems.Length; i++)
        {
            var ps = chosen.systems[i];
            ps.useAutoRandomSeed = false;
            ps.randomSeed = seed + (uint)i * 7919u;
        }
        chosen.root.SetActive(true);
        foreach (var ps in chosen.systems) ps.Play(false);
        chosen.until = Time.time + definition.lifetime;
        chosen.priority = priority;
        LastBaseScale = chosen.baseScale; LastRotation = chosen.root.transform.rotation;
        ActiveCount++; PlayedCount++; LastVariant = variant; PlayedVariants |= 1u << variant;
        if (target != 0)
        {
            int at = -1;
            for (int i = 0; i < targets.Length; i++) if (targets[i] == target) { at = i; break; }
            if (at < 0) { at = cursor; cursor = (cursor + 1) % targets.Length; }
            targets[at] = target; variants[at] = variant;
        }
        return true;
    }
    void ApplyStyle(Slot slot)
    {
        var profile = slot.profile;
        if (slot.animation != null) { ApplyAnimation(slot, Time.time - slot.started); return; }
        block.Clear(); block.SetColor("_BaseColor", profile.mainColor);
        block.SetFloat("_Smoothness", Mathf.Clamp(profile.specular, .1f, .4f));
        block.SetFloat("_HueShift", 0f); block.SetFloat("_AlbedoPower", .45f);
        block.SetFloat("_ColorIntensity", 1.1f * BloodComparisonTuning.SprayBrightness);
        block.SetFloat("_AmbientColorIntensity", .7f);
        foreach (var renderer in slot.renderers) renderer.SetPropertyBlock(block);
    }
    void ApplyAnimation(Slot slot, float age)
    {
        foreach (var layer in slot.animation.layers)
        {
            if (!layer.renderer) continue;
            layer.renderer.enabled = age < layer.seconds;
            if (!layer.renderer.enabled) continue;
            float frame = layer.speed.Evaluate(Mathf.Clamp01(age / layer.seconds)) * layer.frames + layer.offset + 1.1f;
            block.Clear();
            block.SetFloat("_UseCustomTime", 1f);
            block.SetFloat("_TimeInFrames", (Mathf.Ceil(-frame) + 1f) / (layer.frames + 1f));
            var sun = RenderSettings.sun;
            block.SetFloat("_LightIntencity", sun && sun.isActiveAndEnabled ? Mathf.Clamp(sun.intensity, .01f, 1f) : 1f);
            block.SetVector("_SunPos", sun && sun.isActiveAndEnabled ? -sun.transform.forward : new Vector3(1f, .5f, 1f));
            block.SetColor("_Color", BloodComparisonTuning.SprayColor(slot.profile.mainColor).gamma * 2f);
            block.SetColor("_SpecColor", BloodComparisonTuning.SprayColor(slot.profile.specularColor).gamma * .22f);
            layer.renderer.SetPropertyBlock(block);
        }
    }
    public void RefreshTuning()
    {
        foreach (var slot in slots) if (slot != null && slot.until > 0f)
        {
            slot.root.transform.localScale = Vector3.one * slot.baseScale * SizeMultiplier;
            ApplyStyle(slot);
        }
    }
    void Release(Slot slot)
    {
        foreach (var ps in slot.systems) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        slot.root.SetActive(false); slot.until = 0f; ActiveCount--;
    }
    public void Clear()
    {
        foreach (var slot in slots) if (slot != null && slot.until > 0f) Release(slot);
        System.Array.Clear(targets, 0, targets.Length); cursor = 0; LastVariant = -1;
    }
    public void Dispose()
    {
        Clear();
        Ready = false;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i]?.root != null) Object.Destroy(slots[i].root);
            slots[i] = null;
        }
        foreach (var material in profileMaterials.Values) if (material) Object.Destroy(material);
        profileMaterials.Clear();
    }
}
