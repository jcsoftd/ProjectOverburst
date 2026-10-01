using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// A runtime owner survives map changes. Map assets and player materials stay authored as they are.
[DisallowMultipleComponent]
public sealed class PlayerLightingScope : MonoBehaviour
{
    public const uint PlayerLayer = 2;
    [SerializeField] private Color originalMainColor = Color.white * 2;
    [SerializeField] private Vector3 originalMainDirection = new(.3213938f, .76604444f, -.5566705f);
    // Native capture of the pre-camp Hideout's original ambient probe.
    [SerializeField] private float[] originalAmbientProbe = {
        .180283785f,-.00568231149f,-.0125606805f,.007252015f,.005699968f,-.009872423f,.0103301583f,-.010436317f,.02503557f,
        .225714117f,.04134383f,-.0197532624f,.0114032608f,.009111973f,-.01578327f,.014379967f,-.0152235953f,.034436062f,
        .306922853f,.127484947f,-.0340533927f,.0196566433f,.016365191f,-.02834915f,.0177155528f,-.0220852569f,.0404654965f
    };

    public static PlayerLightingScope Active { get; private set; }
    public Color OriginalMainColor => originalMainColor;
    public Vector3 OriginalMainDirection => originalMainDirection;
    public IReadOnlyList<Renderer> Renderers => renderers;
    private readonly List<Renderer> renderers = new();
    private readonly Dictionary<Renderer, RendererState> states = new();
    private readonly Dictionary<UniversalAdditionalLightData, LightState> lights = new();
    private readonly List<Renderer> scratch = new();
    private MaterialPropertyBlock block;
    private readonly SphericalHarmonicsL2[] probe = new SphericalHarmonicsL2[1];
    private int refreshedFrame = -1;

    private sealed class RendererState
    {
        public uint mask;
        public LightProbeUsage probeUsage;
        public MaterialPropertyBlock properties;
    }
    private sealed class LightState
    {
        public uint mask, shadowMask;
        public bool customShadows;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState() => Active = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Active != null) return;
        var owner = new GameObject("Player Original Lighting");
        DontDestroyOnLoad(owner);
        owner.AddComponent<PlayerLightingScope>();
    }

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        if (Active != null && Active != this) { enabled = false; return; }
        block ??= new MaterialPropertyBlock();
        Active = this;
        if (originalAmbientProbe != null && originalAmbientProbe.Length == 27)
            for (int i = 0; i < 27; i++) probe[0][i / 9, i % 9] = originalAmbientProbe[i];
        SceneManager.sceneLoaded += SceneLoaded;
        SceneManager.sceneUnloaded += SceneUnloaded;
        RenderPipelineManager.beginCameraRendering += BeforeCamera;
        RefreshActors();
    }

    private void SceneLoaded(Scene scene, LoadSceneMode mode) { refreshedFrame = -1; RefreshActors(); }
    private void SceneUnloaded(Scene scene)
    {
        foreach (var renderer in new List<Renderer>(states.Keys))
            if (renderer == null) states.Remove(renderer);
        foreach (var light in new List<UniversalAdditionalLightData>(lights.Keys))
            if (light == null) lights.Remove(light);
        renderers.RemoveAll(renderer => renderer == null);
        refreshedFrame = -1;
    }
    private void BeforeCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera.cameraType != CameraType.Game || refreshedFrame == Time.frameCount) return;
        RefreshActors();
        refreshedFrame = Time.frameCount;
    }

    public void RefreshActors()
    {
        if (!Application.isPlaying || Active != this) return;
        // Includes inactive body variants and equipment created after initial boot.
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
        }
        foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var data = light.GetUniversalAdditionalLightData();
            if (!lights.TryGetValue(data, out var saved))
            {
                saved = new LightState {mask = data.renderingLayers, shadowMask = data.shadowRenderingLayers, customShadows = data.customShadowLayers};
                lights.Add(data, saved);
            }
            bool playerLight = light.GetComponentInParent<PlayerActorRuntime>(true) != null;
            uint mask = playerLight ? saved.mask | PlayerLayer : saved.mask & ~PlayerLayer;
            if ((uint)data.renderingLayers != mask) data.renderingLayers = mask;
            if (!playerLight)
            {
                // Keep the character casting a shadow onto the map.
                if (!data.customShadowLayers) data.customShadowLayers = true;
                uint shadowMask = saved.shadowMask | PlayerLayer;
                if ((uint)data.shadowRenderingLayers != shadowMask) data.shadowRenderingLayers = shadowMask;
            }
        }
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneUnloaded -= SceneUnloaded;
        RenderPipelineManager.beginCameraRendering -= BeforeCamera;
        foreach (var pair in states)
        {
            if (pair.Key == null) continue;
            pair.Key.renderingLayerMask = pair.Value.mask;
            pair.Key.lightProbeUsage = pair.Value.probeUsage;
            pair.Key.SetPropertyBlock(pair.Value.properties);
        }
        foreach (var pair in lights)
        {
            if (pair.Key == null) continue;
            pair.Key.renderingLayers = pair.Value.mask;
            pair.Key.shadowRenderingLayers = pair.Value.shadowMask;
            pair.Key.customShadowLayers = pair.Value.customShadows;
        }
        states.Clear(); lights.Clear(); renderers.Clear(); refreshedFrame = -1;
        if (Active == this) Active = null;
    }
}
