using UnityEngine;

// Fixed prefab leases. Components and material blocks are cached before combat.
public sealed class BloodEffectsPackPool
{
    public const int Capacity = 48;
    sealed class Slot
    {
        public GameObject root;
        public ParticleSystem[] systems;
        public Renderer[] renderers;
        public int variant, priority;
        public float until;
    }
    readonly Slot[] slots = new Slot[Capacity];
    readonly int[] targets = new int[96], variants = new int[96];
    readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
    readonly BloodEffectsPackCatalog catalog;
    int cursor;
    public int ActiveCount { get; private set; }
    public int PlayedCount { get; private set; }
    public int PreemptedCount { get; private set; }
    public int LastVariant { get; private set; } = -1;
    public uint PlayedVariants { get; private set; }
    public bool Ready { get; }

    public BloodEffectsPackPool(Transform parent, BloodEffectsPackCatalog source)
    {
        catalog = source;
        if (source == null || source.sprays == null || source.sprays.Length == 0) return;
        for (int i = 0; i < Capacity; i++)
        {
            int variant = i % source.sprays.Length;
            if (source.sprays[variant]?.prefab == null) { Dispose(); return; }
            var root = Object.Instantiate(source.sprays[variant].prefab, parent);
            root.name = "Blood Pack " + i;
            root.SetActive(false);
            var slot = new Slot { root = root, variant = variant,
                systems = root.GetComponentsInChildren<ParticleSystem>(true), renderers = root.GetComponentsInChildren<Renderer>(true) };
            foreach (var ps in slot.systems) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            slots[i] = slot;
        }
        Ready = true;
    }
    public void Tick(float now)
    {
        foreach (var slot in slots) if (slot != null && slot.until > 0f && now >= slot.until) Release(slot);
    }
    public bool Play(BloodHitProfile profile, Vector3 point, Vector3 direction, CombatImpactShape shape,
        float size, int priority, uint seed, int target, bool drip = false)
    {
        if (!Ready) return false;
        int previous = -1;
        if (target != 0) for (int i = 0; i < targets.Length; i++) if (targets[i] == target) { previous = variants[i]; break; }
        int variant = catalog.ResolveSpray(shape, priority, seed, previous, drip);
        if (variant < 0) return false;
        Slot chosen = null;
        foreach (var slot in slots) if (slot.variant == variant && slot.until == 0f) { chosen = slot; break; }
        // A busy form can borrow another eligible lease instead of dropping while the pool is free.
        if (chosen == null)
            for (int pass = 0; pass < 2 && chosen == null; pass++)
                for (int offset = 1; offset < catalog.sprays.Length && chosen == null; offset++)
                {
                    int candidate = (variant + offset) % catalog.sprays.Length;
                    if (!catalog.Accepts(candidate, shape, priority, drip) || (pass == 0 && candidate == previous)) continue;
                    foreach (var slot in slots) if (slot.variant == candidate && slot.until == 0f) { chosen = slot; break; }
                }
        if (chosen == null && priority > 0)
            foreach (var slot in slots)
                if (catalog.Accepts(slot.variant, shape, priority, drip) && slot.priority < priority
                    && (chosen == null || slot.until < chosen.until)) chosen = slot;
        if (chosen == null) return false;
        variant = chosen.variant;
        if (chosen.until > 0f) { PreemptedCount++; Release(chosen); }
        var definition = catalog.sprays[variant];
        direction = direction.sqrMagnitude < .0001f ? Vector3.forward : direction.normalized;
        Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > .95f ? Vector3.forward : Vector3.up;
        chosen.root.transform.SetPositionAndRotation(point, Quaternion.LookRotation(direction, up) * Quaternion.Euler(definition.localEuler));
        // Profile sizes were authored for VFX Graph. Normalize around the game's 3.5 baseline.
        chosen.root.transform.localScale = Vector3.one * definition.scale * Mathf.Clamp(profile.size / 3.5f, .4f, 1.6f)
            * size * (priority >= 2 ? 1.2f : priority == 1 ? 1.1f : 1f);
        block.Clear();
        block.SetColor("_BaseColor", profile.mainColor);
        block.SetFloat("_Smoothness", Mathf.Clamp(profile.specular + .22f, .25f, .55f));
        block.SetFloat("_HueShift", 0f);
        block.SetFloat("_ColorIntensity", .8f);
        block.SetFloat("_AmbientColorIntensity", .45f);
        foreach (var renderer in chosen.renderers) renderer.SetPropertyBlock(block);
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
        foreach (var slot in slots) if (slot?.root != null) Object.Destroy(slot.root);
    }
}
