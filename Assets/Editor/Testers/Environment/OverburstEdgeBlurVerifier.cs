using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class OverburstEdgeBlurVerifier
{
    public static string VerifyAssets()
    {
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(OverburstEdgeBlurBuilder.RendererPath);
        var features = renderer.rendererFeatures.OfType<OverburstEdgeBlurRendererFeature>().ToArray();
        Require(features.Length == 1 && features[0].isActive && features[0].BlurShader != null, "Single active PC blur feature with shader");
        Require(features[0].BlurShader.isSupported && !ShaderUtil.ShaderHasError(features[0].BlurShader), "Blur shader supported and error-free");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OverburstEdgeBlurBuilder.PrefabPath);
        Require(prefab != null && prefab.GetComponent<OverburstEdgeBlur>() != null, "Toggle prefab loads");
        Require(prefab.GetComponentsInChildren<Canvas>(true).Length==0,"Formal edge prefab has no temporary UI");
        Require(prefab.GetComponentsInChildren<Transform>(true).All(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0), "Prefab Missing Script 0");
        Require(AssetDatabase.AssetPathToGUID(OverburstEdgeBlurBuilder.PrefabPath).Length == 32, "Prefab GUID valid");
        VerifyShaderPixels(features[0].BlurShader);
        return "PASS: feature/prefab/shader references and actual GPU pixel checks";
    }

    private static void VerifyShaderPixels(Shader shader)
    {
        Texture2D pattern = null, readback = null;
        RenderTexture source = null, target = null;
        RTHandle handle = null;
        Material material = null;
        CommandBuffer commands = null;
        var previous = RenderTexture.active;
        const int width = 640, height = 360;
        try
        {
            pattern = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                pixels[y * width + x] = ((x + y) & 1) == 0 ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255);
            pattern.SetPixels32(pixels); pattern.Apply();
            source = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            source.Create(); target.Create(); Graphics.Blit(pattern, source);
            handle = RTHandles.Alloc(source);
            material = CoreUtils.CreateEngineMaterial(shader);
            material.SetFloat("_EdgeBlurStrength", .72f);
            commands = new CommandBuffer { name = "Edge blur pixel verification" };
            commands.SetRenderTarget(target);
            Blitter.BlitTexture(commands, handle, new Vector4(1, 1, 0, 0), material, 0);
            Graphics.ExecuteCommandBuffer(commands);
            RenderTexture.active = target;
            readback = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            readback.ReadPixels(new Rect(0, 0, width, height), 0, 0); readback.Apply();
            Require(Mathf.Abs(readback.GetPixel(width / 2, height / 2).r - 1) < .02f, "Shader central white pixel stays sharp");
            Require(readback.GetPixel(width / 2 + 1, height / 2).r < .02f, "Shader central black pixel stays sharp");
            float edgeValue = readback.GetPixel(2, height / 2).r;
            Require(edgeValue > .06f && edgeValue < .94f, "Shader outer edge actually blurs high-frequency detail");
            Require(readback.GetPixel(2, height / 2).a > .99f, "Shader preserves alpha");
        }
        finally
        {
            RenderTexture.active = previous;
            commands?.Release();
            handle?.Release();
            if (source != null) { source.Release(); Object.DestroyImmediate(source); }
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            CoreUtils.Destroy(material);
            if (pattern != null) Object.DestroyImmediate(pattern);
            if (readback != null) Object.DestroyImmediate(readback);
        }
    }

    private static void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);}
}
