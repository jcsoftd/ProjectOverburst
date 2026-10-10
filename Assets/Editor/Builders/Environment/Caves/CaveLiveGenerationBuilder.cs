using System;
using System.IO;
using System.Linq;
using Overburst.Caves;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class CaveLiveGenerationBuilder
{
    public const string Root = CavePlatformMapBuilder.Root + "/Runtime";
    public const string AssetPath = Root + "/GenerationAssets.asset";
    const string Props = "Assets/ThirdParty/04_환경맵/TopDownCaves/Prefabs/";
    const string Demo = "Assets/ThirdParty/04_환경맵/TopDownCaves/Demo/Demo1.unity";

    [MenuItem("Overburst/Caves/실시간 생성 입장 씬 준비")]
    public static void Build()
    {
        CavePlatformMapBuilder.Guard();
        var original=SceneManager.GetActiveScene();
        string stage=PrefabStageUtility.GetCurrentPrefabStage()?.assetPath;
        foreach(int count in new[]{9,12,15})
        {
            var open=SceneManager.GetSceneByPath(CaveMainPortalBuilder.RunPath(count));
            if(open.IsValid()&&open.isLoaded&&open.isDirty) throw new InvalidOperationException("Save the open cave scene before replacing its entry bootstrap.");
        }
        if(stage!=null) StageUtility.GoToMainStage();
        if(!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder(CavePlatformMapBuilder.Root,"Runtime");
        var settings=AssetDatabase.LoadAssetAtPath<CaveGenerationAssets>(AssetPath);
        if(!settings) { settings=ScriptableObject.CreateInstance<CaveGenerationAssets>(); AssetDatabase.CreateAsset(settings,AssetPath); }
        var demo=EditorSceneManager.OpenScene(Demo,OpenSceneMode.Additive);
        try
        {
            settings.library=AssetDatabase.LoadAssetAtPath<CavePlatformLibrary>(CavePlatformMapBuilder.LibraryPath);
            settings.catalog=AssetDatabase.LoadAssetAtPath<CaveCatalog>("Assets/ProjectOverburst/04_Contents/World/Caves/Data/CaveCatalog.asset");
            var terrain=demo.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Terrain>()).Single();
            settings.terrain=terrain.terrainData; settings.terrainMaterial=terrain.materialTemplate; settings.terrainY=terrain.transform.position.y;
            var lights=demo.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Light>()).ToArray();
            GameObject LightPrefab(string name,Light light)
            {
                var clone=Object.Instantiate(light.gameObject); SceneManager.MoveGameObjectToScene(clone,demo);
                try { clone.name=name; clone.GetComponent<Light>().lightmapBakeType=LightmapBakeType.Realtime; return PrefabUtility.SaveAsPrefabAsset(clone,Root+"/"+name+".prefab"); }
                finally { Object.DestroyImmediate(clone); }
            }
            settings.warmLight=LightPrefab("WarmLight",lights.First(l=>l.type==LightType.Point&&l.color.r>.98f));
            settings.blueLight=LightPrefab("BlueLight",lights.First(l=>l.type==LightType.Point&&l.color.b>.99f&&l.color.r<.5f));
            settings.yellowLight=LightPrefab("YellowLight",lights.First(l=>l.type==LightType.Point&&l.color.r>.9f&&l.color.b<.2f));
            var lighting=new GameObject(CaveRuntimeDressing.LightingName); SceneManager.MoveGameObjectToScene(lighting,demo);
            try
            {
                var volume=lighting.AddComponent<UnityEngine.Rendering.Volume>(); volume.isGlobal=true; volume.sharedProfile=CaveDressingBuilder.EnsureColorProfile();
                var sun=Object.Instantiate(lights.First(l=>l.type==LightType.Directional),lighting.transform); sun.lightmapBakeType=LightmapBakeType.Realtime;
                settings.lighting=PrefabUtility.SaveAsPrefabAsset(lighting,Root+"/Lighting.prefab");
            }
            finally { Object.DestroyImmediate(lighting); }
            settings.rocks=new[]{"Rock1_1","Rock1_2","Rock1_3"}.Select(n=>AssetDatabase.LoadAssetAtPath<GameObject>(Props+n+".prefab")).ToArray();
            settings.eggs=Enumerable.Range(1,6).Select(n=>AssetDatabase.LoadAssetAtPath<GameObject>(Props+"Egg_0"+n+".prefab")).ToArray();
            settings.web=AssetDatabase.LoadAssetAtPath<GameObject>(Props+"Spiderweb3.prefab");
            settings.mistMaterial=AssetDatabase.LoadAssetAtPath<Material>(CaveDressingBuilder.Root+"/Details/CaveLocalGreyMist.mat");
            settings.models=settings.library.platforms.Select(p=>p.prefab).Concat(settings.rocks).Concat(settings.eggs).Append(settings.web)
                .SelectMany(p=>p.GetComponentsInChildren<MeshFilter>(true)).Where(f=>f.sharedMesh).Select(f=>f.sharedMesh).Distinct()
                .Select(m=>new CaveGenerationAssets.Model{mesh=m,name=Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(m))}).ToArray();
            EditorUtility.SetDirty(settings); AssetDatabase.SaveAssetIfDirty(settings);
            foreach(int count in new[]{9,12,15})
            {
                var path=CaveMainPortalBuilder.RunPath(count); var open=SceneManager.GetSceneByPath(path);
                if(open.IsValid()&&open.isLoaded) EditorSceneManager.CloseScene(open,true);
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                try
                {
                    SceneManager.SetActiveScene(demo);
                    var mode=RenderSettings.ambientMode; var sky=RenderSettings.skybox; var ambient=RenderSettings.ambientLight;
                    var equator=RenderSettings.ambientEquatorColor; var ground=RenderSettings.ambientGroundColor;
                    float intensity=RenderSettings.ambientIntensity, reflection=RenderSettings.reflectionIntensity;
                    SceneManager.SetActiveScene(scene);
                    RenderSettings.ambientMode=mode; RenderSettings.skybox=sky; RenderSettings.ambientLight=ambient;
                    RenderSettings.ambientEquatorColor=equator; RenderSettings.ambientGroundColor=ground;
                    RenderSettings.ambientIntensity=intensity; RenderSettings.reflectionIntensity=reflection; RenderSettings.fog=false;
                    var world=new GameObject("Live Cave · "+count+" platforms").AddComponent<CaveWorld>();
                    world.authoredLayout=true; world.combatCount=count; world.catalog=settings.catalog;
                    world.gameObject.AddComponent<CaveRuntimeGenerator>().assets=settings;
                    world.gameObject.AddComponent<CaveRunWorld>();
                    if(!EditorSceneManager.SaveScene(scene,path)) throw new IOException(path);
                    CaveMainPortalBuilder.AddBuildScene(path);
                }
                finally { EditorSceneManager.CloseScene(scene,true); }
            }
        }
        finally
        {
            EditorSceneManager.CloseScene(demo,true);
            if(original.IsValid()&&original.isLoaded) SceneManager.SetActiveScene(original);
            if(stage!=null) PrefabStageUtility.OpenPrefab(stage);
        }
    }
}
