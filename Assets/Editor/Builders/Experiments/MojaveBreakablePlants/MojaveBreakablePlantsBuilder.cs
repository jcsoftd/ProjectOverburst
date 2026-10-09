using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class MojaveBreakablePlantsBuilder
{
    public const string Root="Assets/ProjectOverburst/03_Features/World/MojaveBreakablePlants";
    public const string GroupName="__EXPERIMENT_MOJAVE_BREAKABLE_PLANTS__";
    public const string TownPath="Assets/ProjectOverburst/00_Scenes/MainScene.unity";
    const string Sources="Assets/ThirdParty/04_환경맵/BK/PureNature_Mojave/Prefabs";
    public static readonly string[] Examples={"Saguaro1","Saguaro3","Saguaro5","Joshua1","Joshua3","Joshua5","Piko1","Piko3"};
    public static string PrefabPath(string name)=>Root+"/PF_Breakable_"+name+".prefab";
    public static IEnumerable<string> PrefabPaths()=>AssetDatabase.FindAssets("t:Prefab",new[]{Root}).Select(AssetDatabase.GUIDToAssetPath).Where(path=>path.StartsWith(Root+"/PF_Breakable_Saguaro")||path.StartsWith(Root+"/PF_Breakable_Joshua")||path.StartsWith(Root+"/PF_Breakable_Piko")).OrderBy(path=>path);
    public static string Artifact=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../개인파일/코덱스산출/World/20261009_MojaveBreakablePlants"));

    [MenuItem("OVERBURST/Experiments/Mojave 식생/파괴 프리팹 생성 및 마을 배치")]
    public static void Install()
    {
        RequireIdle();
        if(IsolatedSavePlayGuard.RequiresAccountChoice||IsolatedSavePlayGuard.ActiveDirectory!=""||SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","")!="")throw new InvalidOperationException("Another account owns this Editor.");
        var scene=Town();BuildAssets();
        var groups=scene.GetRootGameObjects().Where(x=>x.name==GroupName).ToArray();
        if(groups.Length>1)throw new InvalidOperationException("Duplicate experiment groups.");
        if(groups.Length==1){ValidateGroup(groups[0]);Selection.activeGameObject=groups[0];return;}
        var spawn=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<HubReturnPoint>(true)).Single(x=>x.ReturnPointId=="Default").transform;
        var positions=FreePositions(scene,spawn,Examples.Length);
        var group=new GameObject(GroupName);SceneManager.MoveGameObjectToScene(group,scene);Undo.RegisterCreatedObjectUndo(group,"사막 식생 파괴 예시 배치");
        for(int i=0;i<Examples.Length;i++)
        {
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(Examples[i])),scene);
            instance.transform.SetParent(group.transform,false);instance.transform.SetPositionAndRotation(positions[i],spawn.rotation);PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
        }
        ValidateGroup(group);EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new IOException("MainScene save failed.");Selection.activeGameObject=group;
        Write("town-placement.json",new{status="PASS",group=GroupName,count=Examples.Length,placements=group.transform.Cast<Transform>().Select(x=>new{x.name,position=new[]{x.position.x,x.position.y,x.position.z}}).ToArray()});
    }

    public static void BuildAssets()
    {
        RequireIdle();Folder(Root);var report=new List<object>();
        foreach(string path in AssetDatabase.FindAssets("t:Prefab",new[]{Sources+"/Trees"}).Select(AssetDatabase.GUIDToAssetPath).OrderBy(x=>x))
        {
            var original=AssetDatabase.LoadAssetAtPath<GameObject>(path);var lod=original.GetComponentInChildren<LODGroup>(true);
            var renderer=lod!=null?lod.GetLODs()[0].renderers.Single():original.GetComponentsInChildren<MeshRenderer>(true).Single();
            var filter=renderer.GetComponent<MeshFilter>();var source=MojavePlantMeshSlicer.Read(filter.sharedMesh,original.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix);
            if(source.bounds.size.y>3.3f){report.Add(new{name=original.name,status="EXCLUDED_LARGE",height=source.bounds.size.y});continue;}
            if(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(original.name))!=null){report.Add(new{name=original.name,status="EXISTING"});continue;}
            Build(original,path,renderer,source,path.Contains("/Trees/"),report);
        }
        Write("prefab-generation.json",report);
    }

    static void Build(GameObject original,string path,Renderer renderer,MojavePlantMeshSlicer.Source source,bool tree,List<object> report)
    {
        string meshPath=Root+"/"+original.name+"_Meshes.asset";
        if(AssetDatabase.LoadMainAssetAtPath(meshPath)!=null)throw new InvalidOperationException("Partial output preserved: "+meshPath);
        var preview=EditorSceneManager.NewPreviewScene();var temporary=new List<Object>();GameObject root=null;
        try
        {
            int count=tree?4:3;var meshes=new Mesh[count];
            for(int i=0;i<count;i++)
            {
                float low=source.bounds.min.y+source.bounds.size.y*i/count,high=source.bounds.min.y+source.bounds.size.y*(i+1)/count;
                if(i==0)low-=.001f;if(i==count-1)high+=.001f;
                meshes[i]=MojavePlantMeshSlicer.Slice(source,low,high,tree);temporary.Add(meshes[i]);meshes[i].name=original.name+"_Piece_"+i;
                if(meshes[i].vertexCount<3)throw new InvalidOperationException("Empty fragment: "+meshes[i].name);
            }
            double originalArea=MojavePlantMeshSlicer.Area(source),fragmentArea=meshes.Sum(m=>MojavePlantMeshSlicer.ExteriorArea(m,source.triangles.Length));
            if(originalArea<=0||Math.Abs(fragmentArea-originalArea)/originalArea>.001)throw new InvalidOperationException("Exterior area changed: "+original.name);
            var materialSlots=renderer.sharedMaterials;
            if(materialSlots.Length!=source.triangles.Length)throw new InvalidOperationException("Inspect material slots: "+original.name);
            if(tree)
            {
                string cutPath=Root+(original.name.StartsWith("Joshua")?"/WoodCut.mat":"/CactusCut.mat");var cut=AssetDatabase.LoadAssetAtPath<Material>(cutPath);
                if(cut==null){cut=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=Path.GetFileNameWithoutExtension(cutPath)};temporary.Add(cut);cut.SetColor("_BaseColor",original.name.StartsWith("Joshua")?new Color(.42f,.30f,.17f):new Color(.63f,.72f,.35f));cut.SetFloat("_Smoothness",.08f);AssetDatabase.CreateAsset(cut,cutPath);}
                materialSlots=materialSlots.Concat(new[]{cut}).ToArray();
            }
            root=new GameObject("PF_Breakable_"+original.name);SceneManager.MoveGameObjectToScene(root,preview);
            var runtime=root.AddComponent<MojaveBreakablePlant>();var bounds=source.bounds;
            var collider=root.AddComponent<BoxCollider>();collider.center=bounds.center;collider.size=new Vector3(Mathf.Max(.12f,bounds.size.x),Mathf.Max(.08f,bounds.size.y),Mathf.Max(.12f,bounds.size.z));collider.isTrigger=!tree;
            var target=root.AddComponent<CombatTarget>();target.Configure(CombatTeam.Neutral,false);
            var targetData=new SerializedObject(target);float height=Mathf.Max(.85f,bounds.size.y);
            targetData.FindProperty("localCenter").vector3Value=new Vector3(bounds.center.x,bounds.min.y+height*.5f,bounds.center.z);
            targetData.FindProperty("height").floatValue=height;targetData.FindProperty("radius").floatValue=Mathf.Clamp(Mathf.Max(bounds.size.x,bounds.size.z)*.5f,.22f,.8f);targetData.ApplyModifiedPropertiesWithoutUndo();
            var intact=(GameObject)PrefabUtility.InstantiatePrefab(original,preview);intact.name="Intact_"+original.name;intact.transform.SetParent(root.transform,false);
            foreach(var c in intact.GetComponentsInChildren<Collider>(true)){c.enabled=false;PrefabUtility.RecordPrefabInstancePropertyModifications(c);}
            foreach(var body in intact.GetComponentsInChildren<Rigidbody>(true)){body.isKinematic=true;body.useGravity=false;PrefabUtility.RecordPrefabInstancePropertyModifications(body);}
            var bodies=new List<Rigidbody>();AssetDatabase.CreateAsset(meshes[0],meshPath);
            for(int i=0;i<count;i++)
            {
                if(i>0)AssetDatabase.AddObjectToAsset(meshes[i],meshPath);
                var piece=new GameObject("Piece_"+i);piece.transform.SetParent(root.transform,false);
                piece.AddComponent<MeshFilter>().sharedMesh=meshes[i];piece.AddComponent<MeshRenderer>().sharedMaterials=materialSlots;
                var shape=piece.AddComponent<BoxCollider>();shape.center=meshes[i].bounds.center;shape.size=new Vector3(Mathf.Max(.04f,meshes[i].bounds.size.x),Mathf.Max(.04f,meshes[i].bounds.size.y),Mathf.Max(.04f,meshes[i].bounds.size.z));shape.enabled=false;
                var body=piece.AddComponent<Rigidbody>();body.mass=tree?.16f:.035f;body.isKinematic=true;body.useGravity=false;bodies.Add(body);piece.SetActive(false);
            }
            var data=new SerializedObject(runtime);data.FindProperty("intactVisual").objectReferenceValue=intact;data.FindProperty("intactCollider").objectReferenceValue=collider;data.FindProperty("target").objectReferenceValue=target;
            var fragments=data.FindProperty("fragments");fragments.arraySize=bodies.Count;for(int i=0;i<bodies.Count;i++)fragments.GetArrayElementAtIndex(i).objectReferenceValue=bodies[i];
            data.FindProperty("scatterScale").floatValue=tree?1f:.4f;
            data.FindProperty("showDamageNumbers").boolValue=false;data.FindProperty("destroyOnDeath").boolValue=false;data.ApplyModifiedPropertiesWithoutUndo();
            if(PrefabUtility.SaveAsPrefabAsset(root,PrefabPath(original.name))==null)throw new IOException("Prefab save failed.");AssetDatabase.SaveAssetIfDirty(meshes[0]);
            report.Add(new{name=original.name,source=path,status="PASS",height=bounds.size.y,pieces=count,materialSlots=materialSlots.Length,sourceArea=originalArea,fragmentArea,hasUV1=source.hasUV1,intactLOD=original.GetComponentInChildren<LODGroup>(true)?.lodCount??0,blocksMovement=tree});
        }
        finally{if(root!=null)Object.DestroyImmediate(root);foreach(var item in temporary)if(item!=null&&!AssetDatabase.Contains(item))Object.DestroyImmediate(item);EditorSceneManager.ClosePreviewScene(preview);}
    }

    static Vector3[] FreePositions(Scene scene,Transform spawn,int count)
    {
        Physics.SyncTransforms();var positions=new List<Vector3>();
        var terrains=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<Terrain>(true)).ToArray();
        for(float z=-8;z>=-32&&positions.Count<count;z-=4)
        for(float x=-12;x<=16&&positions.Count<count;x+=4)
        {
            var point=spawn.position+spawn.rotation*new Vector3(x,0,z);bool ground=false;
            foreach(var terrain in terrains){var local=point-terrain.transform.position;var size=terrain.terrainData.size;if(local.x<0||local.z<0||local.x>size.x||local.z>size.z)continue;point.y=terrain.SampleHeight(point)+terrain.transform.position.y+.015f;ground=true;break;}
            if(!ground)continue;
            if(Physics.OverlapCapsule(point+Vector3.up*.25f,point+Vector3.up*2.5f,1.05f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).Any(c=>!(c is TerrainCollider)&&c.gameObject.scene==scene))continue;
            positions.Add(point);
        }
        if(positions.Count<count)throw new InvalidOperationException("No clear ground for all examples. Preserve the scene and inspect placement.");return positions.ToArray();
    }

    static Scene Town(){var scene=SceneManager.GetSceneByPath(TownPath);if(!scene.IsValid()||!scene.isLoaded)scene=EditorSceneManager.OpenScene(TownPath,OpenSceneMode.Additive);if(scene.isDirty)throw new InvalidOperationException("Preserve unsaved MainScene changes.");return scene;}
    public static void RequireIdle(){if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorUtility.scriptCompilationFailed)throw new InvalidOperationException("Use the existing idle Editor after compilation.");}
    static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;string parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));}
    static void Write(string file,object data){string directory=Artifact+"/Evidence";Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,file),JsonConvert.SerializeObject(data,Formatting.Indented));}
    public static void ValidateGroup(GameObject group){if(group.transform.parent!=null||group.name!=GroupName||group.transform.childCount!=Examples.Length)throw new InvalidOperationException("Unexpected experiment group; preserve it.");for(int i=0;i<Examples.Length;i++)if(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(group.transform.GetChild(i).gameObject)!=PrefabPath(Examples[i]))throw new InvalidOperationException("Unexpected child; preserve it.");}

    [MenuItem("OVERBURST/Experiments/Mojave 식생/마을 배치만 제거")]
    public static void RemovePlacement(){RequireIdle();var scene=Town();var groups=scene.GetRootGameObjects().Where(x=>x.name==GroupName).ToArray();foreach(var group in groups)ValidateGroup(group);foreach(var group in groups)Undo.DestroyObjectImmediate(group);if(groups.Length>0){EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);}}
}
