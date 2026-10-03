using System;
using UnityEditor;
using UnityEngine;

public static class PlayerDashVfxBuilder
{
    public const string Root="Assets/ProjectOverburst/03_Features/Player/VFX";
    [MenuItem("OVERBURST/Player/Build Dash Afterimage Comparison")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)
            throw new InvalidOperationException("대시 VFX 제작에는 유휴 Editor가 필요합니다.");
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PlayerEvadeBuilder.PlayerPath);
        if(prefab==null||EditorUtility.IsDirty(prefab))throw new InvalidOperationException("미저장 플레이어 프리팹은 소유 작업에서 처리해야 합니다.");
        EnsureFolder(Root+"/Materials");
        var pose=Material(Root+"/Materials/M_DashAfterimage.mat","OVERBURST/Player/Dash Afterimage");
        var dust=Material(Root+"/Materials/M_DashGroundDust.mat","OVERBURST/Player/Dash Ground Dust");
        ApplyEvadeGroundDust(dust);
        GameObject contents=null;
        try
        {
            contents=PrefabUtility.LoadPrefabContents(PlayerEvadeBuilder.PlayerPath);
            var effect=contents.GetComponent<PlayerDashVfx>()??contents.AddComponent<PlayerDashVfx>();
            var fields=new SerializedObject(effect);
            fields.FindProperty("afterimageMaterial").objectReferenceValue=pose;
            fields.FindProperty("dustMaterial").objectReferenceValue=dust;
            fields.FindProperty("colorStyle").intValue=0;
            fields.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(contents,PlayerEvadeBuilder.PlayerPath);
        }
        finally{if(contents!=null)PrefabUtility.UnloadPrefabContents(contents);}
    }
    static void ApplyEvadeGroundDust(Material dust)
    {
        const string path = "Assets/ProjectOverburst/Resources/Feel/PF_OverburstFeelHub.prefab";
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null || EditorUtility.IsDirty(asset)) throw new InvalidOperationException("회피 피드백 프리팹 누락 또는 미저장 변경");
        var contents = PrefabUtility.LoadPrefabContents(path);
        try
        {
            foreach (var emitter in contents.GetComponentsInChildren<OverburstFeelEmitter>(true))
            {
                if (emitter.Cue != OverburstFeelCue.Evade || emitter.ParticleSystem == null) continue;
                var particles = emitter.ParticleSystem;
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main;
                main.startColor = new Color(.53f, .48f, .39f, .26f);
                main.startLifetime = new ParticleSystem.MinMaxCurve(.25f, .42f);
                main.startSize = new ParticleSystem.MinMaxCurve(.19f, .34f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(.35f, .7f);
                main.gravityModifier = -.04f;
                var shape = particles.shape;
                shape.position = new Vector3(0f, .05f, 0f);
                shape.scale = new Vector3(1f, .2f, 1f);
                var renderer = particles.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = dust;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
            }
            PrefabUtility.SaveAsPrefabAsset(contents, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
    }
    static Material Material(string path,string shaderName)
    {
        var shader=Shader.Find(shaderName);if(shader==null||!shader.isSupported)throw new InvalidOperationException("잔상 셰이더 오류: "+shaderName);
        var asset=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(asset!=null&&EditorUtility.IsDirty(asset))throw new InvalidOperationException("미저장 잔상 재질");
        if(asset==null){asset=new Material(shader);AssetDatabase.CreateAsset(asset,path);}
        else{asset.shader=shader;EditorUtility.SetDirty(asset);AssetDatabase.SaveAssetIfDirty(asset);}
        return asset;
    }
    static void EnsureFolder(string path)
    {
        int index=path.LastIndexOf('/');if(AssetDatabase.IsValidFolder(path))return;
        EnsureFolder(path.Substring(0,index));AssetDatabase.CreateFolder(path.Substring(0,index),path.Substring(index+1));
    }
}
