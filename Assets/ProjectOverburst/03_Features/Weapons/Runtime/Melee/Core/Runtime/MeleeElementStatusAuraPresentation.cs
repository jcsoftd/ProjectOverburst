using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MeleeElementStatusAuraPresentation : MonoBehaviour
{
    private sealed class AuraModule
    {
        public readonly GameObject Root;
        public readonly ParticleSystem[] Particles;
        public bool Active;

        public AuraModule(GameObject root, ParticleSystem[] particles)
        {
            Root = root;
            Particles = particles;
        }
    }

    [SerializeField] private GameObject burningAura;
    [SerializeField] private GameObject wetAura;
    [SerializeField] private GameObject shockedAura;
    [SerializeField] private GameObject chilledAura;

    private readonly AuraModule[] modules = new AuraModule[4];
    private bool modulesCached;

#if UNITY_EDITOR
    public int CacheBuildCountForValidation { get; private set; }
    public int PlayCommandCountForValidation { get; private set; }
    public int StopCommandCountForValidation { get; private set; }
#endif

    private void Awake()
    {
        CacheModules();
        ClearAllAuras();
    }

    public bool SetAuraActive(
        MeleeElementStatusAuraType auraType,
        bool active,
        bool restartIfAlreadyActive = true)
    {
        AuraModule module = GetModule(auraType);
        if (module?.Root == null)
            return false;

        if (!active)
        {
            HideModule(module);
            module.Active = false;
            return true;
        }

        bool wasActive = module.Active;
        module.Active = true;
        if (!wasActive || restartIfAlreadyActive)
            Restart(module);
        return true;
    }

    public bool IsAuraActive(MeleeElementStatusAuraType auraType)
    {
        AuraModule module = GetModule(auraType);
        return module != null && module.Active;
    }

    public bool ClearAura(MeleeElementStatusAuraType auraType)
    {
        return SetAuraActive(auraType, false, false);
    }

    public void ClearAllAuras()
    {
        EnsureModulesCached();
        for (int i = 0; i < modules.Length; i++)
        {
            AuraModule module = modules[i];
            if (module == null)
                continue;
            HideModule(module);
            module.Active = false;
        }
    }

    public void RestartActiveAuras()
    {
        for (int i = 0; i < modules.Length; i++)
        {
            AuraModule module = modules[i];
            if (module?.Active == true)
                Restart(module);
        }
    }

    public GameObject GetAuraObject(MeleeElementStatusAuraType auraType)
    {
        switch (auraType)
        {
            case MeleeElementStatusAuraType.Burning: return burningAura;
            case MeleeElementStatusAuraType.Wet: return wetAura;
            case MeleeElementStatusAuraType.Shocked: return shockedAura;
            case MeleeElementStatusAuraType.Chilled: return chilledAura;
            default: return null;
        }
    }

    private void Restart(AuraModule module)
    {
        StopAndClear(module);
        SetActive(module.Root, true);
        for (int i = 0; i < module.Particles.Length; i++)
        {
            ParticleSystem particle = module.Particles[i];
            if (particle == null)
                continue;
            particle.Play(false);
#if UNITY_EDITOR
            PlayCommandCountForValidation++;
#endif
        }
    }

    private void HideModule(AuraModule module)
    {
        if (module?.Root == null)
            return;
        if (module.Root.activeSelf)
            StopAndClear(module);
        SetActive(module.Root, false);
    }

    private void StopAndClear(AuraModule module)
    {
        for (int i = 0; i < module.Particles.Length; i++)
        {
            ParticleSystem particle = module.Particles[i];
            if (particle == null)
                continue;
            particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
#if UNITY_EDITOR
            StopCommandCountForValidation++;
#endif
        }
    }

    private AuraModule GetModule(MeleeElementStatusAuraType auraType)
    {
        EnsureModulesCached();
        int index = (int)auraType;
        return index >= 0 && index < modules.Length ? modules[index] : null;
    }

    private void EnsureModulesCached()
    {
        if (!modulesCached)
            CacheModules();
    }

    private void CacheModules()
    {
        for (int i = 0; i < modules.Length; i++)
        {
            GameObject root = GetAuraObject((MeleeElementStatusAuraType)i);
            ParticleSystem[] particles = root != null
                ? root.GetComponentsInChildren<ParticleSystem>(true)
                : Array.Empty<ParticleSystem>();
            modules[i] = new AuraModule(root, particles);
        }
        modulesCached = true;
#if UNITY_EDITOR
        CacheBuildCountForValidation++;
#endif
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        modulesCached = false;
    }

    public void RebuildModulesForValidation()
    {
        modulesCached = false;
        CacheModules();
    }
#endif

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }
}
