using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class BarbarianCampUrpVerifier
{
    public static string Inspect(string output, string stage, bool requirePass)
    {
        BarbarianCampUrpRepair.RequireIdle();
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        Directory.CreateDirectory(output);
        var setupBefore = EditorSnapshot();
        var models = AssetDatabase.FindAssets("t:GameObject", new[] {BarbarianCampUrpRepair.Root})
            .Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p => p)
            .Select(p => Audit(p, new[] {AssetDatabase.LoadAssetAtPath<GameObject>(p)})).ToArray();
        var scenes = new List<object>();
        int sceneBad = 0, pink = 0;
        foreach (var path in AssetDatabase.FindAssets("t:Scene", new[] {BarbarianCampUrpRepair.Root})
                     .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
        {
            var scene = EditorSceneManager.OpenPreviewScene(path);
            try
            {
                var roots = scene.GetRootGameObjects();
                var audit = Audit(path, roots);
                sceneBad += audit.badSlots;
                string image = Path.Combine(output, stage + "_" + Path.GetFileNameWithoutExtension(path) + ".png");
                int pinkPixels = Render(scene, image);
                pink += pinkPixels;
                scenes.Add(new {audit, image = Path.GetFileName(image), pinkPixels});
            }
            finally {EditorSceneManager.ClosePreviewScene(scene);}
        }
        var setupAfter = EditorSnapshot();
        bool preserved = setupBefore == setupAfter;
        bool valid = models.All(m => m.badSlots == 0 && m.missingScripts == 0) && sceneBad == 0 && pink == 0 && preserved;
        var result = new {status = requirePass ? (valid ? "PASS" : "FAIL") : "DIAGNOSTIC", assets = models.Length,
            renderers = models.Sum(m => m.renderers), badAssets = models.Count(m => m.badSlots > 0),
            badAssetSlots = models.Sum(m => m.badSlots), badSceneSlots = sceneBad, pinkPixels = pink,
            missingScripts = models.Sum(m => m.missingScripts), editorSetupPreserved = preserved, models, scenes};
        File.WriteAllText(Path.Combine(output, stage + "_audit.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
        File.WriteAllText(Path.Combine(output, stage + "_editor.json"), setupAfter);
        if (!preserved) throw new InvalidOperationException("Editor setup changed during material inspection.");
        if (requirePass && (result.badAssets != 0 || sceneBad != 0 || pink != 0 || result.missingScripts != 0))
            throw new InvalidOperationException("Package rendering/material audit failed; inspect the saved result.");
        return JsonConvert.SerializeObject(new {result.status, result.assets, result.renderers, result.badAssets,
            result.badSceneSlots, result.pinkPixels, result.missingScripts, result.editorSetupPreserved});
    }

    sealed class AssetAudit
    {
        public string path;
        public int transforms, renderers, meshes, distinctMeshes, badSlots, missingScripts;
        public string[] invalidMaterials;
    }

    static AssetAudit Audit(string path, GameObject[] roots)
    {
        var transforms = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var renderers = roots.SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).ToArray();
        var meshes = roots.SelectMany(r => r.GetComponentsInChildren<MeshFilter>(true)).Select(f => f.sharedMesh).ToArray();
        var bad = renderers.SelectMany(r => r.sharedMaterials).Where(m => m == null || m.shader == null ||
            !m.shader.isSupported || ShaderUtil.ShaderHasError(m.shader) || m.GetTag("RenderPipeline", false, "") != "UniversalPipeline").ToArray();
        return new AssetAudit {path = path, transforms = transforms.Length, renderers = renderers.Length,
            meshes = meshes.Length, distinctMeshes = meshes.Distinct().Count(), badSlots = bad.Length,
            missingScripts = transforms.Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)),
            invalidMaterials = bad.Select(m => m == null ? "MISSING" : AssetDatabase.GetAssetPath(m)).Distinct().ToArray()};
    }

    public static string EditorSnapshot()
    {
        return JsonConvert.SerializeObject(new {active = SceneManager.GetActiveScene().path,
            scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Select(s => new
            {s.path, dirty = s.isDirty, roots = s.GetRootGameObjects().Select(r => new {r.name, active = r.activeSelf,
                hierarchy = r.GetComponentsInChildren<Transform>(true).Select(t => new {t.name,
                    position = t.localPosition.ToString("R"), rotation = t.localRotation.ToString("R"), scale = t.localScale.ToString("R")}).ToArray()}).ToArray()}).ToArray()}, Formatting.Indented);
    }

    static int Render(Scene scene, string path)
    {
        var root = new GameObject("Temporary camp material inspection camera");
        SceneManager.MoveGameObjectToScene(root, scene);
        var camera = root.AddComponent<Camera>(); camera.scene = scene;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.18f, .18f, .18f);
        camera.nearClipPlane = .1f; camera.farClipPlane = 1000; camera.orthographic = true;
        var renderers = scene.GetRootGameObjects().Where(r => r != root).SelectMany(r => r.GetComponentsInChildren<Renderer>(true))
            .Where(r => r.enabled && r.gameObject.activeInHierarchy && r.bounds.size.x < 100 && r.bounds.size.z < 100).ToArray();
        if (renderers.Length == 0) throw new InvalidOperationException("No demo objects to capture.");
        var bounds = renderers[0].bounds; foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        camera.transform.position = bounds.center + new Vector3(0, 50, -38);
        camera.transform.LookAt(bounds.center); camera.orthographicSize = Mathf.Max(bounds.extents.z, bounds.extents.x / 1.6f) * 1.18f + 2;
        var target = RenderTexture.GetTemporary(1600, 1000, 24);
        var texture = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            return texture.GetPixels32().Count(p => p.r > 180 && p.b > 180 && p.g < 90 && p.r > p.g * 1.6f && p.b > p.g * 1.6f);
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(texture); Object.DestroyImmediate(root);
        }
    }
}
