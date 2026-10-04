using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class GroundIndicatorVerifier
{
    public static object Verify()
    {
        GroundIndicatorBuilder.RequireIdle();
        var nova = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(GroundIndicatorBuilder.SourceGuid));
        Require(nova != null, "Original Nova source is missing");
        int radialLayers = 0;
        foreach (var renderer in nova.GetComponentsInChildren<ParticleSystemRenderer>(true))
        {
            if (!renderer.name.Contains("fill_add_soft") && !renderer.name.Contains("border_add_soft")) continue;
            Require(renderer.mesh != null && renderer.mesh.isReadable, "Nova radial mesh must be readable during Play");
            radialLayers++;
        }
        Require(radialLayers == 2, "Nova fill and border source layers changed");
        var checks = new List<object>(); var scene = EditorSceneManager.NewPreviewScene();
        var native = new HashSet<Material>();
        foreach (string guid in new[] { GroundIndicatorBuilder.ConeGuid, GroundIndicatorBuilder.SourceGuid, GroundIndicatorBuilder.RectangleGuid })
            foreach (var r in AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)).GetComponentsInChildren<ParticleSystemRenderer>(true))
                if (r.sharedMaterial != null) native.Add(r.sharedMaterial);
        try
        {
            foreach (GroundIndicatorShape kind in Enum.GetValues(typeof(GroundIndicatorShape)))
            {
                string path = GroundIndicatorBuilder.Root + "/PF_Indicator_" + kind + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Require(prefab != null, "Missing prefab " + path);
                var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                try
                {
                    var p = root.GetComponent<ProceduralGroundIndicator>();
                    Require(p != null && p.UsesApprovedDesign, "Approved source references missing");
                    p.Configure(kind, 4f, 1f, 150f, 2f, 4f); p.SetVisible(true);
                    Require(p.Surface != null && p.Border != null, "Missing native layers");
                    Require(root.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)) == 0, "Missing script");
                    var originalMaterial = p.Surface.sharedMaterial;
                    foreach (float radius in new[] { .25f, 1f, 4f, 12f, 50f })
                    {
                        p.Configure(kind, radius, radius * .18f, 150f, radius * .5f, radius);
                        CheckFootprint(p); Require(p.Surface.sharedMaterial == originalMaterial, "Material changed during resize");
                    }
                    p.Configure(kind, 4f, 1f, 30f, 2f, 12f); CheckFootprint(p);
                    p.Configure(kind, 4f, 3.5f, 140f, 8f, 2f); CheckFootprint(p);
                    foreach (float progress in new[] { .25f, .5f, .95f })
                    {
                        p.SetProgress(progress); CheckFootprint(p);
                        foreach (var system in root.GetComponentsInChildren<ParticleSystem>())
                            if (system.GetComponent<ParticleSystemRenderer>().renderMode == ParticleSystemRenderMode.Mesh)
                                Require(!system.sizeOverLifetime.enabled && Math.Abs(system.main.startSize.constant - 1f) < .00001f, "Particle size changed the numeric boundary");
                    }
                    foreach (var r in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
                        if (r.sharedMaterial != null)
                        {
                            Require(native.Contains(r.sharedMaterial), "Non-native material");
                            Require(!ShaderUtil.ShaderHasError(r.sharedMaterial.shader), "Shader error");
                        }
                    for (int i = 0; i < 3; i++)
                    {
                        p.SetVisible(false); Require(!p.IsVisible, "Hide failed");
                        p.SetVisible(true); p.SetProgress(.95f); Require(p.IsVisible, "Reentry failed");
                        root.SetActive(false); root.SetActive(true); p.SetProgress(.5f);
                    }
                    if (kind == GroundIndicatorShape.Rectangle)
                    {
                        p.Configure(kind, 4f, 0f, 360f, .8f, 3.42f, .4f); CheckFootprint(p);
                        var v = p.Surface.mesh.vertices;
                        Require(Math.Abs(v.Min(x => -x.y) + .4f) < .0001f && Math.Abs(v.Max(x => -x.y) - 3.82f) < .0001f, "Sphere-cast ends missing");
                    }
                    p.Configure(kind, float.NaN, float.PositiveInfinity, float.NaN);
                    Require(!float.IsNaN(p.OuterRadius) && p.InnerRadius < p.OuterRadius, "Invalid numbers");
                    checks.Add(new { shape = kind.ToString(), prefab = path, guid = AssetDatabase.AssetPathToGUID(path), resizing = 7, progressChecks = 3, reentry = 3, nativeMaterials = true });
                }
                finally { Object.DestroyImmediate(root); }
            }
            return new { status = "PASS", checks };
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
    private static void CheckFootprint(ProceduralGroundIndicator p)
    {
        var vertices = p.Surface.mesh.vertices;
        Require(vertices.All(v => !float.IsNaN(v.x) && !float.IsInfinity(v.y)), "Non-finite vertex");
        foreach (var v in vertices)
        {
            float distance = new Vector2(v.x, v.y).magnitude;
            if (p.Shape == GroundIndicatorShape.Rectangle)
                Require(Math.Abs(v.x) <= p.Width * .5f + .0001f && -v.y >= -p.CorridorCapRadius - .0001f && -v.y <= p.Length + p.CorridorCapRadius + .0001f, "Rectangle footprint drift");
            else Require(distance >= p.InnerRadius - .0001f && distance <= p.OuterRadius + .0001f, "Radial footprint drift");
        }
        if (p.Shape == GroundIndicatorShape.Sector && p.InnerRadius > 0f)
            Require(vertices.All(v => new Vector2(v.x, v.y).magnitude > 0f), "Acute apex returned");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
