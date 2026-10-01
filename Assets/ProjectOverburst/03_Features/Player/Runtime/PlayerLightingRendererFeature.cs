using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// Keep the actor's original materials and depth occlusion, with its pre-camp lighting.
// Drawing after map grading keeps exposure, white balance and tonemapping off the actor.
public sealed class PlayerLightingRendererFeature : ScriptableRendererFeature
{
    private PlayerPass pass;
    public static int LastDrawFrame { get; private set; } = -1;
    public static int LastDrawCount { get; private set; }
    public override void Create() => pass = new PlayerPass();
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
    {
        if (PlayerLightingScope.Active != null && data.cameraData.cameraType == CameraType.Game)
            renderer.EnqueuePass(pass);
    }

    private sealed class PlayerPass : ScriptableRenderPass
    {
        private static readonly int MainColor = Shader.PropertyToID("_MainLightColor");
        private static readonly int MainDirection = Shader.PropertyToID("_MainLightPosition");
        private static readonly int MainMask = Shader.PropertyToID("_MainLightLayerMask");
        private static readonly int MainShadow = Shader.PropertyToID("_MainLightShadowParams");
        private static readonly int Fog = Shader.PropertyToID("unity_FogParams");
        private sealed class Data
        {
            public RendererListHandle body, outline;
            public int drawCount;
            public Vector4 color, direction, previousColor, previousDirection, previousFog, previousShadow;
            public int previousMask;
        }
        public PlayerPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            requiresIntermediateTexture = true;
        }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var scope = PlayerLightingScope.Active;
            if (scope == null) return;
            int drawCount = 0;
            foreach (var renderer in scope.Renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                drawCount += renderer.sharedMaterials.Length;
            }
            if (drawCount == 0) return;
            var resources = frameData.Get<UniversalResourceData>();
            var rendering = frameData.Get<UniversalRenderingData>();
            var camera = frameData.Get<UniversalCameraData>();
            var light = frameData.Get<UniversalLightData>();
            var filter = new FilteringSettings(RenderQueueRange.all, camera.camera.cullingMask, PlayerLightingScope.PlayerLayer);
            var bodySettings = RenderingUtils.CreateDrawingSettings(new List<ShaderTagId> {
                new("SRPDefaultUnlit"), new("UniversalForwardOnly"), new("UniversalForward")
            }, rendering, camera, light, camera.defaultOpaqueSortFlags);
            var outlineSettings = RenderingUtils.CreateDrawingSettings(new ShaderTagId("UniversalForward"), rendering, camera, light, camera.defaultOpaqueSortFlags);
            var body = graph.CreateRendererList(new RendererListParams(rendering.cullResults, bodySettings, filter));
            var outline = graph.CreateRendererList(new RendererListParams(rendering.cullResults, outlineSettings, filter));
            using (var builder = graph.AddRasterRenderPass<Data>("Player Original Lighting", out var data))
            {
                data.body = body; data.outline = outline; data.drawCount = drawCount; data.color = scope.OriginalMainColor;
                var direction = scope.OriginalMainDirection; data.direction = new Vector4(direction.x, direction.y, direction.z, 0);
                data.previousColor = Shader.GetGlobalVector(MainColor); data.previousDirection = Shader.GetGlobalVector(MainDirection);
                data.previousMask = Shader.GetGlobalInt(MainMask); data.previousFog = Shader.GetGlobalVector(Fog);
                data.previousShadow = Shader.GetGlobalVector(MainShadow);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                builder.UseRendererList(body); builder.UseRendererList(outline); builder.UseAllGlobalTextures(true);
                builder.AllowGlobalStateModification(true); builder.AllowPassCulling(false);
                builder.SetRenderFunc((Data d, RasterGraphContext context) =>
                {
                    var cmd = context.cmd;
                    cmd.SetGlobalVector(MainColor, d.color); cmd.SetGlobalVector(MainDirection, d.direction);
                    cmd.SetGlobalInt(MainMask, (int)PlayerLightingScope.PlayerLayer);
                    // Map shadow direction can differ from the original player light. Keep map shadows off this pass.
                    cmd.SetGlobalVector(MainShadow, Vector4.zero);
                    // The original player lighting had fog disabled.
                    // Linear fog uses w as visibility: zero would replace every surface with fog gray.
                    cmd.SetGlobalVector(Fog, new Vector4(0, 0, 0, 1));
                    cmd.DrawRendererList(d.body); cmd.DrawRendererList(d.outline);
                    cmd.SetGlobalVector(MainColor, d.previousColor); cmd.SetGlobalVector(MainDirection, d.previousDirection);
                    cmd.SetGlobalInt(MainMask, d.previousMask); cmd.SetGlobalVector(Fog, d.previousFog);
                    cmd.SetGlobalVector(MainShadow, d.previousShadow);
                    LastDrawFrame = Time.frameCount; LastDrawCount = d.drawCount;
                });
            }
        }
    }
}
