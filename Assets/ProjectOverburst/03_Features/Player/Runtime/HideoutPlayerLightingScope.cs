using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Only the Hideout scene owns this scope. Leaving it restores the actor's rendering state.
[DisallowMultipleComponent]
public sealed class HideoutPlayerLightingScope : MonoBehaviour
{
    public const uint PlayerLayer = 2;
    [SerializeField] private Color originalMainColor = Color.white * 2;
    [SerializeField] private Vector3 originalMainDirection;
    [SerializeField] private float[] originalAmbientProbe = new float[27];

    public static HideoutPlayerLightingScope Active { get; private set; }
    public Color OriginalMainColor => originalMainColor;
    public Vector3 OriginalMainDirection => originalMainDirection;
    public IReadOnlyList<Renderer> Renderers => renderers;
    private readonly List<Renderer> renderers = new();
    private readonly Dictionary<Renderer, RendererState> states = new();
    private readonly Dictionary<UniversalAdditionalLightData, uint> lights = new();
    private readonly List<Renderer> scratch = new();
    private MaterialPropertyBlock block;
    private readonly SphericalHarmonicsL2[] probe = new SphericalHarmonicsL2[1];
    private float nextRefresh;

    private sealed class RendererState
    {
        public uint mask;
        public LightProbeUsage probeUsage;
        public MaterialPropertyBlock properties;
    }

    private void OnEnable()
    {
        block ??= new MaterialPropertyBlock();
        Active = this;
        if (originalAmbientProbe != null && originalAmbientProbe.Length == 27)
            for (int i = 0; i < 27; i++) probe[0][i / 9, i % 9] = originalAmbientProbe[i];
        RefreshActors();
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + .25f;
        RefreshActors();
    }

    public void RefreshActors()
    {
        // Equipment/party actors can be instantiated after the additive scene starts.
        foreach (var actor in FindObjectsByType<PlayerActorRuntime>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            scratch.Clear(); actor.GetComponentsInChildren(true, scratch);
            foreach (var renderer in scratch)
            {
                if (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer)) continue;
                if (states.ContainsKey(renderer)) continue;
                var saved = new MaterialPropertyBlock(); renderer.GetPropertyBlock(saved);
                states.Add(renderer, new RendererState {mask = renderer.renderingLayerMask, probeUsage = renderer.lightProbeUsage, properties = saved});
                renderer.renderingLayerMask = PlayerLayer;
                renderer.lightProbeUsage = LightProbeUsage.CustomProvided;
                renderer.GetPropertyBlock(block); block.CopySHCoefficientArraysFrom(probe);
                renderer.SetPropertyBlock(block); renderers.Add(renderer);
            }
            foreach (var light in actor.GetComponentsInChildren<Light>(true))
            {
                var data = light.GetUniversalAdditionalLightData();
                if (lights.ContainsKey(data)) continue;
                uint mask = (uint)data.renderingLayers;
                lights.Add(data, mask);
                data.renderingLayers = mask | PlayerLayer;
            }
        }
    }

    private void OnDisable()
    {
        foreach (var pair in states)
        {
            if (pair.Key == null) continue;
            pair.Key.renderingLayerMask = pair.Value.mask;
            pair.Key.lightProbeUsage = pair.Value.probeUsage;
            pair.Key.SetPropertyBlock(pair.Value.properties);
        }
        foreach (var pair in lights) if (pair.Key != null) pair.Key.renderingLayers = pair.Value;
        states.Clear(); lights.Clear(); renderers.Clear();
        if (Active == this) Active = null;
    }
}
