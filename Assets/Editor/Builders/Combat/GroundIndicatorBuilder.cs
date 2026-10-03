using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GroundIndicatorBuilder
{
    public const string Root = "Assets/ProjectOverburst/Resources/Combat/Indicators";
    public const string PrefabPath = Root + "/PF_Indicator_Sector.prefab";
    public const string SourceGuid = "a078c1a32abbe584393e91050e2dd214";

    [MenuItem("JC Tool/VFX/인디케이터/기본 프리팹 생성")]
    public static void Build()
    {
        Build(false);
    }
    public static void Build(bool rebuildExisting)
    {
        RequireIdle();EnsureFolder(Root);
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(SourceGuid));
        if(source==null)throw new InvalidOperationException("Original red Telegraph_Nova is missing.");
        var fade=ReadOriginalConeFade();
        foreach(GroundIndicatorShape kind in Enum.GetValues(typeof(GroundIndicatorShape)))
        {
            string path=Root+"/PF_Indicator_"+kind+".prefab";
            var existing=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(!rebuildExisting && existing!=null && existing.GetComponent<ProceduralGroundIndicator>()?.UsesAuthoredEdgeFade==true)continue;
            if(existing!=null&&EditorUtility.IsDirty(existing))throw new InvalidOperationException("Indicator prefab has unsaved changes: "+path);
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var root=(GameObject)PrefabUtility.InstantiatePrefab(source,scene);
                PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                root.name="PF_Indicator_"+kind;
                var systems=root.GetComponentsInChildren<ParticleSystem>(true);
                var fill=systems.First(p=>p.name.Contains("fill_add_soft"));
                var border=systems.First(p=>p.name.Contains("border_add_soft"));
                Mesh original=fill.GetComponent<ParticleSystemRenderer>().mesh;
                foreach(var p in new[]{fill,border})
                {
                    p.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main=p.main;main.startSize=1f;
                    p.transform.localRotation=Quaternion.Euler(270,0,0);
                }
                var indicator=root.AddComponent<ProceduralGroundIndicator>();
                indicator.Initialize(root.GetComponent<ParticleSystem>(),fill,border);
                indicator.SetAuthoredEdgeFade(fade);
                indicator.Configure(kind,4,kind==GroundIndicatorShape.Circle||kind==GroundIndicatorShape.Rectangle?0:1,80,1,4);
                // 동적 메시를 자산에 직렬화하지 않는다. 인스턴스가 자기 메시를 만들고 반환한다.
                indicator.Surface.mesh=original;indicator.Border.mesh=original;
                foreach(var p in systems)if(p.name.Contains("Fuzz"))p.GetComponent<ParticleSystemRenderer>().mesh=original;
                var saved=PrefabUtility.SaveAsPrefabAsset(root,path,out bool ok);
                if(!ok||saved==null)throw new InvalidOperationException("Prefab save failed: "+path);
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
    private static AnimationCurve ReadOriginalConeFade()
    {
        var cone=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("55a51c403edec12489b9aaf003b60ce5"));
        var mask=cone.GetComponentsInChildren<ParticleSystemRenderer>(true).First(r=>r.name.Contains("fill_add_soft")).sharedMaterial.GetTexture("_AlphaOverride");
        if(mask==null)throw new InvalidOperationException("Original cone fill mask missing.");
        var previous=RenderTexture.active;var rt=RenderTexture.GetTemporary(512,512,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        var pixels=new Texture2D(512,512,TextureFormat.RGBAFloat,false,true);
        try
        {
            Graphics.Blit(mask,rt);RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,512,512),0,0);pixels.Apply();
            int peak=256;float maximum=0;
            for(int y=256;y<512;y++){float value=pixels.GetPixel(256,y).r;if(value>maximum){maximum=value;peak=y;}}
            if(maximum<.01f)throw new InvalidOperationException("Original cone mask has no edge signal.");
            var keys=new Keyframe[66];float radius=peak-24;
            for(int i=0;i<65;i++)
            {
                float distance=i/128f;float sample=peak-distance*radius;
                int lower=Mathf.FloorToInt(sample);
                float value=Mathf.Lerp(pixels.GetPixel(256,lower).r,pixels.GetPixel(256,lower+1).r,sample-lower);
                keys[i]=new Keyframe(distance,maximum>0?Mathf.Clamp01(value/maximum):0);
            }
            keys[65]=new Keyframe(1,0);
            var curve=new AnimationCurve(keys);
            for(int i=0;i<keys.Length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}
            return curve;
        }
        finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(pixels);}
    }
    public static void RequireIdle()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Edit mode required.");
    }
    public static void EnsureFolder(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;
        string parent=System.IO.Path.GetDirectoryName(path).Replace('\\','/');
        EnsureFolder(parent);AssetDatabase.CreateFolder(parent,System.IO.Path.GetFileName(path));
    }
}
