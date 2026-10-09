using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BreakableTownCandidatesBuilder
{
    public const string Root=BreakablePlantExperimentBuilder.Root+"/TownCandidates";
    public const string PrefabPath=Root+"/PF_BreakableTownPlants_Experiment.prefab";
    public const string InstanceName="__EXPERIMENT_BREAKABLE_TOWN_PLANTS__";
    public static readonly string[] Sources={"DF_Dead_Trees_04","DF_Bushes_03","DF_Broken_Tree_Trunk_01","DF_Branch_Group_01"};
    static readonly string[] Labels={"01 가는 고사목","02 낮은 덤불","03 부러진 줄기","04 마른 가지 군집"};
    static readonly Vector3[] Slots={new Vector3(-1.8f,0,-1.8f),new Vector3(1.8f,0,-1.8f),new Vector3(-1.8f,0,1.8f),new Vector3(1.8f,0,1.8f)};
    static void RequireIdle()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorUtility.scriptCompilationFailed)
            throw new InvalidOperationException("Successfully compiled idle Editor required.");
    }
    public static void Build()
    {
        RequireIdle();
        if(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)!=null)throw new InvalidOperationException("Candidate group exists; preserve it.");
        if(!AssetDatabase.IsValidFolder(Root))AssetDatabase.CreateFolder(BreakablePlantExperimentBuilder.Root,"TownCandidates");
        var preview=EditorSceneManager.NewPreviewScene();
        var meshes=new List<(Mesh mesh,string path)>();var candidates=new List<GameObject>();GameObject group=null;
        try
        {
            var cut=AssetDatabase.LoadAssetAtPath<Material>(BreakablePlantExperimentBuilder.Root+"/CutWood.mat");
            if(cut==null)throw new InvalidOperationException("Original experiment cut material missing.");
            group=new GameObject(InstanceName);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(group,preview);
            for(int kind=0;kind<Sources.Length;kind++)
            {
                string sourcePath="Assets/ThirdParty/04_환경맵/Dark Fantasy Bandit Camp & Wilderness/Prefabs/Vegetation/"+Sources[kind]+".prefab";
                var source=AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                var filters=source.GetComponentsInChildren<MeshFilter>(true);
                var filter=filters.FirstOrDefault(f=>f.name.EndsWith("LOD0"))??filters.First();
                if(filter.sharedMesh.subMeshCount!=1)throw new InvalidOperationException("Only inspected single-material models are supported: "+sourcePath);
                BreakablePlantExperimentBuilder.ReadSource(filter.sharedMesh,out Vector3[] vertices,out Vector2[] uv,out int[] triangles);
                Matrix4x4 matrix=source.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix;
                for(int i=0;i<vertices.Length;i++)vertices[i]=matrix.MultiplyPoint3x4(vertices[i]);
                var bounds=new Bounds(vertices[0],Vector3.zero);foreach(var v in vertices)bounds.Encapsulate(v);
                float size=kind==0?bounds.size.y:Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);
                float desired=kind==0?2.1f:kind==1?1.25f:1.8f;
                float scale=Mathf.Min(1,desired/size);
                Vector3 shift=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
                for(int i=0;i<vertices.Length;i++)vertices[i]=(vertices[i]-shift)*scale;
                bounds=new Bounds(vertices[0],Vector3.zero);foreach(var v in vertices)bounds.Encapsulate(v);
                Vector3 axis=kind==0?Vector3.up:bounds.size.x>=bounds.size.z?Vector3.right:Vector3.forward;
                Quaternion rotation=Quaternion.FromToRotation(axis,Vector3.up),inverse=Quaternion.Inverse(rotation);
                var sliced=vertices.Select(v=>rotation*v).ToArray();float low=sliced.Min(v=>v.y),high=sliced.Max(v=>v.y);
                float stump=kind==0?.18f:0;
                var candidate=new GameObject(Labels[kind]);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(candidate,preview);
                var plant=candidate.AddComponent<BreakablePlantExperiment>();Collider intact;
                if(kind==0){var c=candidate.AddComponent<CapsuleCollider>();c.center=new Vector3(0,bounds.size.y*.5f,0);c.height=bounds.size.y;c.radius=.18f;intact=c;}
                else{var c=candidate.AddComponent<BoxCollider>();c.center=bounds.center;c.size=new Vector3(Mathf.Max(.1f,bounds.size.x),Mathf.Max(.08f,bounds.size.y),Mathf.Max(.1f,bounds.size.z));intact=c;}
                var target=candidate.AddComponent<CombatTarget>();target.Configure(CombatTeam.Neutral,false);
                target.ConfigureVolume(new Vector3(0,Mathf.Max(.65f,bounds.center.y),0),Mathf.Clamp(Mathf.Max(bounds.size.x,bounds.size.z)*.32f,.35f,.6f),Mathf.Max(1.3f,bounds.size.y));
                var bodies=new List<Rigidbody>();int partCount=kind==0?5:4;
                for(int part=0;part<partCount;part++)
                {
                    bool remains=kind==0&&part==0;
                    float from=remains?low-.001f:low+stump+(high-low-stump)*(kind==0?part-1:part)/4;
                    float to=remains?low+stump:low+stump+(high-low-stump)*(kind==0?part:part+1)/4;
                    if(part==partCount-1)to=high+.001f;
                    var mesh=BreakablePlantExperimentBuilder.Slice(sliced,uv,triangles,from,to,kind!=1);
                    var points=mesh.vertices;for(int i=0;i<points.Length;i++)points[i]=inverse*points[i];mesh.vertices=points;mesh.RecalculateNormals();mesh.RecalculateBounds();
                    if(mesh.vertexCount==0)throw new InvalidOperationException("Empty fragment: "+Sources[kind]+" "+part);
                    mesh.name=Sources[kind]+" fragment "+part;meshes.Add((mesh,Root+"/"+Sources[kind]+"_Part"+part+".asset"));
                    var fragment=new GameObject(remains?"Stump (remains)":"Fragment "+part);fragment.transform.SetParent(candidate.transform,false);
                    fragment.AddComponent<MeshFilter>().sharedMesh=mesh;fragment.AddComponent<MeshRenderer>().sharedMaterials=new[]{filter.GetComponent<MeshRenderer>().sharedMaterial,cut};
                    if(remains)continue;
                    var c=fragment.AddComponent<BoxCollider>();c.center=mesh.bounds.center;c.size=new Vector3(Mathf.Max(.06f,mesh.bounds.size.x),Mathf.Max(.06f,mesh.bounds.size.y),Mathf.Max(.06f,mesh.bounds.size.z));c.enabled=false;
                    var body=fragment.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;body.mass=kind==1?.12f:.7f;body.linearDamping=.12f;body.angularDamping=.3f;bodies.Add(body);
                }
                var settings=new SerializedObject(plant);settings.FindProperty("stones").arraySize=bodies.Count;
                for(int i=0;i<bodies.Count;i++)settings.FindProperty("stones").GetArrayElementAtIndex(i).objectReferenceValue=bodies[i];
                settings.FindProperty("intactCollider").objectReferenceValue=intact;settings.FindProperty("target").objectReferenceValue=target;
                settings.FindProperty("showDamageNumbers").boolValue=false;settings.FindProperty("destroyOnDeath").boolValue=false;settings.FindProperty("showOverlay").boolValue=false;settings.ApplyModifiedPropertiesWithoutUndo();
                candidates.Add(candidate);
            }
            foreach(var item in meshes)
            {
                if(AssetDatabase.LoadMainAssetAtPath(item.path)!=null)throw new InvalidOperationException("Preserve existing asset: "+item.path);
                AssetDatabase.CreateAsset(item.mesh,item.path);
            }
            for(int i=0;i<candidates.Count;i++)
            {
                PrefabUtility.SaveAsPrefabAsset(candidates[i],Root+"/PF_Breakable_"+Sources[i]+".prefab");
                candidates[i].transform.SetParent(group.transform,false);candidates[i].transform.localPosition=Slots[i];
            }
            PrefabUtility.SaveAsPrefabAsset(group,PrefabPath);foreach(var item in meshes)AssetDatabase.SaveAssetIfDirty(item.mesh);
            if(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponentsInChildren<BreakablePlantExperiment>(true).Length!=4)throw new InvalidOperationException("Four candidate contracts required.");
        }
        finally
        {
            foreach(var candidate in candidates)if(candidate!=null&&candidate.transform.parent==null)UnityEngine.Object.DestroyImmediate(candidate);
            if(group!=null)UnityEngine.Object.DestroyImmediate(group);
            foreach(var item in meshes)if(item.mesh!=null&&!AssetDatabase.Contains(item.mesh))UnityEngine.Object.DestroyImmediate(item.mesh);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }
    [MenuItem("OVERBURST/Experiments/마을 식생 후보 4종/마을에 임시 배치")]
    public static void PlaceInTown()
    {
        RequireIdle();var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(BreakablePlantExperimentBuilder.TownPath);
        if(!scene.IsValid()||!scene.isLoaded)scene=EditorSceneManager.OpenScene(BreakablePlantExperimentBuilder.TownPath,OpenSceneMode.Additive);
        if(scene.isDirty)throw new InvalidOperationException("Preserve unsaved town changes.");
        var existing=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==InstanceName);if(existing!=null){Selection.activeGameObject=existing;return;}
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);if(prefab==null)throw new InvalidOperationException("Build candidates first.");
        var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);instance.name=InstanceName;
        instance.transform.SetPositionAndRotation(new Vector3(162.6f,0,112.2f),Quaternion.Euler(0,346,0));
        foreach(Transform child in instance.transform)
        {
            Vector3 position=child.position;
            foreach(var terrain in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Terrain>(true)))
            {var local=position-terrain.transform.position;if(local.x<0||local.z<0||local.x>terrain.terrainData.size.x||local.z>terrain.terrainData.size.z)continue;position.y=terrain.SampleHeight(position)+terrain.transform.position.y+.02f;break;}
            child.position=position;PrefabUtility.RecordPrefabInstancePropertyModifications(child);
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);Undo.RegisterCreatedObjectUndo(instance,"Place temporary town plant candidates");
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);Selection.activeGameObject=instance;
    }
    [MenuItem("OVERBURST/Experiments/마을 식생 후보 4종/마을 배치만 제거")]
    public static void RemoveTownPlacement()
    {
        RequireIdle();var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(BreakablePlantExperimentBuilder.TownPath);
        if(!scene.IsValid()||!scene.isLoaded)scene=EditorSceneManager.OpenScene(BreakablePlantExperimentBuilder.TownPath,OpenSceneMode.Additive);
        if(scene.isDirty)throw new InvalidOperationException("Preserve unsaved town changes.");
        foreach(var root in scene.GetRootGameObjects().Where(g=>g.name==InstanceName).ToArray())
        {if(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root)!=PrefabPath)throw new InvalidOperationException("Unexpected ownership; preserve root.");Undo.DestroyObjectImmediate(root);}
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
    }
}
