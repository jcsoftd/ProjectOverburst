using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

internal static class SwordDistortionSurfaces
{
    private static readonly HashSet<Component> Active = new HashSet<Component>();
    public static bool Any => Active.Count > 0;
    public static void Set(Component owner, bool active)
    {
        if (active) Active.Add(owner); else Active.Remove(owner);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => Active.Clear();
}

// 투명 파티클까지 포함한 화면을 복사한 뒤 검의 굴절 표면만 그린다.
public sealed class SwordDistortionRendererFeature : ScriptableRendererFeature
{
    private RefractionPass pass;
    public override void Create() => pass = new RefractionPass();
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (SwordDistortionSurfaces.Any && renderingData.cameraData.cameraType != CameraType.Reflection)
            renderer.EnqueuePass(pass);
    }

    private sealed class RefractionPass : ScriptableRenderPass
    {
        private static readonly ShaderTagId Tag = new ShaderTagId("SwordRefraction");
        private static readonly int SceneColor = Shader.PropertyToID("_SwordSceneColor");
        private sealed class DrawData { public RendererListHandle renderers; }

        public RefractionPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var camera = frameData.Get<UniversalCameraData>();
            var rendering = frameData.Get<UniversalRenderingData>();
            var light = frameData.Get<UniversalLightData>();
            if (resources.isActiveTargetBackBuffer) return;
            var descriptor = graph.GetTextureDesc(resources.activeColorTexture);
            descriptor.name = "Sword Refraction Scene";
            descriptor.depthBufferBits = DepthBits.None;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.clearBuffer = false;
            var copy = graph.CreateTexture(descriptor);
            using (var builder = graph.AddBlitPass(resources.activeColorTexture, copy,
                Vector2.one, Vector2.zero, passName: "Sword Refraction Capture", returnBuilder: true))
                builder.SetGlobalTextureAfterPass(copy, SceneColor);

            var drawing = RenderingUtils.CreateDrawingSettings(Tag, rendering, camera, light, SortingCriteria.CommonTransparent);
            var filtering = new FilteringSettings(RenderQueueRange.transparent, camera.camera.cullingMask);
            var list = graph.CreateRendererList(new RendererListParams(rendering.cullResults, drawing, filtering));
            using (var builder = graph.AddRasterRenderPass<DrawData>("Sword Refraction Surfaces", out var data))
            {
                data.renderers = list;
                builder.UseRendererList(list);
                builder.UseTexture(copy, AccessFlags.Read);
                if (resources.cameraDepthTexture.IsValid()) builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                builder.SetRenderFunc((DrawData d, RasterGraphContext ctx) => ctx.cmd.DrawRendererList(d.renderers));
            }
        }
    }
}
