using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

// 임시 비교 토글이 켜진 주 카메라에만 가장자리 흐림을 합성한다.
public sealed class OverburstEdgeBlurRendererFeature : ScriptableRendererFeature
{
    public const string ShaderName = "Hidden/OVERBURST/EdgeBlur";
    [SerializeField] private Shader blurShader;
    private Material material;
    private EdgeBlurPass pass;
    public Shader BlurShader => blurShader;
    public static int RecordedPassCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCounters() => RecordedPassCount = 0;

    public override void Create()
    {
        CoreUtils.Destroy(material);
        material = blurShader != null ? CoreUtils.CreateEngineMaterial(blurShader) : null;
        pass = new EdgeBlurPass(material);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var cameraData = renderingData.cameraData;
        if (!Application.isPlaying || material == null ||
            cameraData.cameraType != CameraType.Game || cameraData.renderType != CameraRenderType.Base ||
            !cameraData.camera.CompareTag("MainCamera")) return;
        bool momentActive = CombatMomentPresentation.TryGetScreen(cameraData.camera, out float gain, out Vector2 center);
        if (!OverburstEdgeBlurPreview.IsEnabled && !momentActive) return;
        material.SetFloat("_EdgeBlurStrength", OverburstEdgeBlurPreview.IsEnabled ? OverburstEdgeBlurPreview.CurrentStrength : 0f);
        material.SetVector("_MomentPulse", new Vector4(center.x, center.y, gain, 0f));
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(material);
        material = null;
        pass = null;
    }

    private sealed class EdgeBlurPass : ScriptableRenderPass
    {
        private readonly Material material;
        public EdgeBlurPass(Material material)
        {
            this.material = material;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer || material == null) return;
            var source = resources.activeColorTexture;
            var descriptor = graph.GetTextureDesc(source);
            descriptor.name = "OVERBURST Edge Blur Color";
            descriptor.depthBufferBits = DepthBits.None;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.clearBuffer = false;
            var destination = graph.CreateTexture(descriptor);
            var parameters = new RenderGraphUtils.BlitMaterialParameters(source, destination, material, 0);
            graph.AddBlitPass(parameters, passName: "OVERBURST Edge Blur");
            resources.cameraColor = destination;
            RecordedPassCount++;
        }
    }
}
