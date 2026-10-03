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
        RequireIdle();EnsureFolder(Root);
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(SourceGuid));
        if(source==null)throw new InvalidOperationException("Original red Telegraph_Nova is missing.");
        foreach(GroundIndicatorShape kind in Enum.GetValues(typeof(GroundIndicatorShape)))
        {
            string path=Root+"/PF_Indicator_"+kind+".prefab";
            var existing=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(existing!=null && existing.GetComponent<ProceduralGroundIndicator>()?.Surface!=null)continue;
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
                // 실제 게임이 원본 ambient에 적용하던 강도를 동일하게 반영한다.
                foreach(var p in systems)
                {
                    if(!p.name.Contains("Fuzz")&&!p.name.Contains("Flecks"))continue;
                    var main=p.main;var color=main.startColor;
                    float factor=p.name.Contains("Fuzz")?.55f:.75f;
                    if(color.mode==ParticleSystemGradientMode.Color){var c=color.color;c.a*=factor;main.startColor=c;}
                }
                var indicator=root.AddComponent<ProceduralGroundIndicator>();
                indicator.Initialize(root.GetComponent<ParticleSystem>(),fill,border);
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
