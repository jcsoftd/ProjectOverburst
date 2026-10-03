using System;
using UnityEditor;
using UnityEngine;

public static class HeavyFocusPresentationBuilder
{
    public const string Root = "Assets/ProjectOverburst/Resources/Combat/VFX/";
    public const string ShaderPath = "Assets/ProjectOverburst/Resources/Shaders/DashHeavyFocus.shader";
    [MenuItem("OVERBURST/Weapons/Build Heavy Blade Gathering")]
    public static void BuildAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("유휴 Editor가 필요합니다.");
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("빛 모임 셰이더 오류");
        foreach (var path in new[] { Root + "DashHeavyFocus.mat", Root + "DashHeavyFocusHead.mat", Root + "DashHeavyFocusHeadMesh.asset" })
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset != null && EditorUtility.IsDirty(asset)) throw new InvalidOperationException("미저장 자산 보존: " + path);
        }
        MaterialAsset("DashHeavyFocus.mat", shader, 1.5f, 0f);
        MaterialAsset("DashHeavyFocusHead.mat", shader, 2.7f, 1f);
        string meshPath = Root + "DashHeavyFocusHeadMesh.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (mesh == null) { mesh = new Mesh { name = "HeavyGatheringLightQuad" }; AssetDatabase.CreateAsset(mesh, meshPath); }
        mesh.Clear();
        mesh.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0) };
        mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh);
    }
    private static void MaterialAsset(string name, Shader shader, float intensity, float mode)
    {
        string path = Root + name;
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
        material.shader = shader; material.SetFloat("_Intensity", intensity); material.SetFloat("_Mode", mode);
        material.SetFloat("_Glint", 0); material.SetColor("_Tint", Color.white);
        EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
    }
}
