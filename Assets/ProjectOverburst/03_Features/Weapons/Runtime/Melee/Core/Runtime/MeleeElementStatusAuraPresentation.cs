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
        public int Stacks;
        public readonly float[] Rates, DistanceRates, Sizes, SizesY, SizesZ;

        public AuraModule(GameObject root, ParticleSystem[] particles)
        {
            Root = root;
            Particles = particles;
            Rates=new float[particles.Length];DistanceRates=new float[particles.Length];
            Sizes=new float[particles.Length];SizesY=new float[particles.Length];SizesZ=new float[particles.Length];
            for(int i=0;i<particles.Length;i++)
            {
                var main=particles[i].main;var emission=particles[i].emission;
                Rates[i]=emission.rateOverTimeMultiplier;DistanceRates[i]=emission.rateOverDistanceMultiplier;
                Sizes[i]=main.startSizeMultiplier;SizesY[i]=main.startSizeYMultiplier;SizesZ[i]=main.startSizeZMultiplier;
            }
        }
    }

    [SerializeField] private GameObject burningAura;
    [Tooltip("화상 불 크기 배율. 몸 반경에 맞춘 뒤 곱한다.")]
    [SerializeField, Range(.2f, 2f)] private float burningAuraScale = 1f;
    [Tooltip("화상 불 높이. 몸 중심에서 몸 절반 높이의 몇 배 위에서 나오는지(0 = 몸 중심).")]
    [SerializeField, Range(-1f, 1f)] private float burningAuraHeight = 0f;
    [SerializeField] private GameObject shockedAura;
    [SerializeField] private GameObject chilledAura;
    [Tooltip("어둠 잠식 상태. 60D: Piloto DarkAura")]
    [SerializeField] private GameObject corrodedAura;
    [Tooltip("잠식 오라 크기 배율. 몸 크기에 맞춘 뒤 곱한다.")]
    [SerializeField, Range(.2f, 1.5f)] private float corrodedAuraScale = .7f;

    // Burning grows with stacks: embers at 1, full-body fire at 5 (size, emission).
    private static readonly float[] BurnStackSize = { .85f, .92f, 1f, 1.07f, 1.15f };
    private static readonly float[] BurnStackDensity = { .55f, .65f, .75f, .85f, 1f };

    private readonly AuraModule[] modules = new AuraModule[4];
    private bool modulesCached;

    public void ConfigureTarget(CombatTarget target)
    {
        if(target==null)return;
        var volume=CombatTargetVfxPlacement.ResolveVolume(target);
        Vector3 parentScale=transform.lossyScale;
        if(corrodedAura!=null)
        {
            // The source aura is a sphere centered at its origin and must stay larger than the body to be seen,
            // so it follows body width; only very flat bodies are limited by height.
            corrodedAura.transform.position=volume.Center;
            float fit=Mathf.Min(volume.Radius/.6f,volume.HalfHeight*2f/.7f);
            float darkSize=Mathf.Clamp(fit,.45f,3f)*Mathf.Clamp(corrodedAuraScale,.2f,1.5f);
            SetWorldSize(corrodedAura.transform,darkSize,parentScale);
        }
        if(shockedAura!=null)
        {
            // The source aura is centered at its origin. Fit to the visual body, not a fixed +1m offset.
            shockedAura.transform.position=volume.Center;
            SetWorldSize(shockedAura.transform,Mathf.Clamp(volume.Radius/.6f,.45f,3f),parentScale);
        }
        if(burningAura!=null)
        {
            // Flames rise, so only body width drives the size (linear, so large monsters burn large).
            CombatTargetVfxPlacement.ResolveBurnTuning(target,out Vector3 burnOffset,out float burnTune);
            float burnSize=Mathf.Clamp(volume.Radius/.55f,.6f,3f)*Mathf.Clamp(burningAuraScale,.2f,2f)*burnTune;
            SetWorldSize(burningAura.transform,burnSize,parentScale);
            Vector3 sourceOffset=burningAura.transform.childCount>0
                ?burningAura.transform.TransformVector(burningAura.transform.GetChild(0).localPosition):Vector3.zero;
            burningAura.transform.position=volume.Center+Vector3.up*(volume.HalfHeight*burningAuraHeight)
                +burnOffset-sourceOffset;
        }
    }

    private static void SetWorldSize(Transform aura,float size,Vector3 parentScale)
    {
        aura.localScale=new Vector3(size/Mathf.Max(.001f,Mathf.Abs(parentScale.x)),
            size/Mathf.Max(.001f,Mathf.Abs(parentScale.y)),size/Mathf.Max(.001f,Mathf.Abs(parentScale.z)));
    }

    public void SetStackCount(MeleeElementStatusAuraType type,int count)
    {
        AuraModule module=GetModule(type);
        if(module==null)return;
        count=Mathf.Clamp(count,0,5);
        if(module.Stacks==count)return;
        module.Stacks=count;
        if(count==0)return;
        float density=count>=5?1f:count>=3?.75f+(count-3)*.1f:.45f+(count-1)*.1f;
        float size=count>=5?1.1f:count>=3?.9f+(count-3)*.1f:.72f+(count-1)*.08f;
        if(type==MeleeElementStatusAuraType.Burning){density=BurnStackDensity[count-1];size=BurnStackSize[count-1];}
        // Corrosion reaches five stacks almost at once, so it barely grows (.85 -> 1).
        else if(type==MeleeElementStatusAuraType.Corroded)size=.85f+(count-1)*.0375f;
        for(int i=0;i<module.Particles.Length;i++)
        {
            var particle=module.Particles[i];if(particle==null)continue;
            var emission=particle.emission;emission.rateOverTimeMultiplier=module.Rates[i]*density;
            emission.rateOverDistanceMultiplier=module.DistanceRates[i]*density;
            var main=particle.main;main.startSizeMultiplier=module.Sizes[i]*size;
            if(main.startSize3D){main.startSizeYMultiplier=module.SizesY[i]*size;main.startSizeZMultiplier=module.SizesZ[i]*size;}
        }
    }

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
            module.Stacks = 0;
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
            module.Stacks = 0;
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
            case MeleeElementStatusAuraType.Shocked: return shockedAura;
            case MeleeElementStatusAuraType.Chilled: return chilledAura;
            case MeleeElementStatusAuraType.Corroded: return corrodedAura;
            default: return null;
        }
    }

    private void Restart(AuraModule module)
    {
        using var costScope = ElementCombatCostMarkers.Aura_Restart.Auto();
        StopAndClear(module);
        SetActive(module.Root, true);
        for (int i = 0; i < module.Particles.Length; i++)
        {
            ParticleSystem particle = module.Particles[i];
            if (particle == null)
                continue;
            // The opt-in shared prototype does not yet distinguish per-stack particle settings.
            if (module.Stacks > 0 || !SharedLocalAuraRenderer.TryRegister(particle)) particle.Play(false);
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
        using var costScope = ElementCombatCostMarkers.Aura_StopClear.Auto();
        for (int i = 0; i < module.Particles.Length; i++)
        {
            ParticleSystem particle = module.Particles[i];
            if (particle == null)
                continue;
            SharedLocalAuraRenderer.Release(particle);
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
