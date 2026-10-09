using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BreakablePlantExperimentBuilder
{
    public const string Root = "Assets/ProjectOverburst/03_Features/World/BreakablePlantExperiment";
    public const string PrefabPath = Root + "/PF_BreakableSmallTree_Experiment.prefab";
    public const string TownPath = "Assets/ProjectOverburst/00_Scenes/MainScene.unity";
    public const string InstanceName = "__EXPERIMENT_BREAKABLE_SMALL_TREE__";

    [MenuItem("OVERBURST/Experiments/작은 나무 파괴/마을에 임시 배치")]
    public static void PlaceInTown()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Stop Play before authoring the experiment.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(TownPath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(TownPath, OpenSceneMode.Additive);
        if (scene.isDirty) throw new InvalidOperationException("MainScene has unsaved changes; preserve them before placing the experiment.");
        var existing = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<BreakablePlantExperiment>(true)).FirstOrDefault(p => p.name == InstanceName && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(p.gameObject) == PrefabPath);
        if (existing != null) { Selection.activeGameObject = existing.gameObject; return; }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) throw new InvalidOperationException("Build the experiment prefab first.");
        var spawn = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MainTownPlacementAnchor>(true))
            .FirstOrDefault(a => a.LocationLabel.Contains("중앙") || a.LocationLabel.Contains("스폰"));
        Vector3 origin = spawn != null ? spawn.transform.position : new Vector3(154.8f,0,107.5f);
        Quaternion rotation = spawn != null ? spawn.transform.rotation : Quaternion.Euler(0,346,0);
        Vector3 position = origin + rotation * new Vector3(1.8f,0,2.7f);
        foreach (var terrain in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Terrain>(true)))
        {
            var local = position - terrain.transform.position; var size = terrain.terrainData.size;
            if (local.x < 0 || local.z < 0 || local.x > size.x || local.z > size.z) continue;
            position.y = terrain.SampleHeight(position) + terrain.transform.position.y + .02f;
            break;
        }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.name = InstanceName; instance.transform.SetPositionAndRotation(position, rotation);
        Undo.RegisterCreatedObjectUndo(instance, "Place temporary destruction wall");
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        Selection.activeGameObject = instance;
        Debug.Log("[작은 나무 파괴 실험] 마을 중앙 스폰 근처 " + position + "에 임시 배치했습니다.");
    }

    [MenuItem("OVERBURST/Experiments/작은 나무 파괴/마을 배치만 제거")]
    public static void RemoveTownPlacement()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Stop Play before removing the experiment.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(TownPath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(TownPath, OpenSceneMode.Additive);
        if (scene.isDirty) throw new InvalidOperationException("MainScene has unsaved changes; preserve them before removing the experiment.");
        foreach (var wall in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<BreakablePlantExperiment>(true)).Where(p => p.name == InstanceName).ToArray())
        {
            if (wall.name != InstanceName || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(wall.gameObject) != PrefabPath)
                throw new InvalidOperationException("Unexpected experiment instance; do not remove it automatically.");
            Undo.DestroyObjectImmediate(wall.gameObject);
        }
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Debug.Log("[작은 나무 파괴 실험] 임시 마을 배치만 제거했습니다. 다른 마을 객체는 유지합니다.");
    }

    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("An idle Editor is required.");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            throw new InvalidOperationException("Experiment prefab already exists; preserve it.");
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        try
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThirdParty/04_환경맵/Dark Fantasy Bandit Camp & Wilderness/Prefabs/Vegetation/DF_Dead_Trees_01.prefab");
            if (source == null) throw new InvalidOperationException("Town dead-tree source is missing.");
            var sourceFilter = source.GetComponentsInChildren<MeshFilter>(true).First(f => f.name.EndsWith("LOD0"));
            var sourceRenderer = sourceFilter.GetComponent<MeshRenderer>();
            if (sourceFilter.sharedMesh.subMeshCount != 1) throw new InvalidOperationException("This experiment expects one bark material.");
            var bark = sourceRenderer.sharedMaterial;
            var cutMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            cutMaterial.SetColor("_BaseColor",new Color(.29f,.20f,.12f));cutMaterial.SetFloat("_Smoothness",.03f);
            AssetDatabase.CreateAsset(cutMaterial,Root+"/CutWood.mat");
            root = new GameObject("Small broken tree — experiment");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var wall = root.AddComponent<BreakablePlantExperiment>();
            var box = root.AddComponent<CapsuleCollider>();
            box.center = new Vector3(0,1.15f,0);box.radius=.22f;box.height=2.3f;
            var target = root.AddComponent<CombatTarget>();
            target.Configure(CombatTeam.Neutral, false);
            target.ConfigureVolume(new Vector3(0,.85f,0), .35f,1.7f);
            ReadSource(sourceFilter.sharedMesh,out Vector3[] vertices,out Vector2[] uv,out int[] triangles);
            Matrix4x4 matrix=source.transform.worldToLocalMatrix*sourceFilter.transform.localToWorldMatrix;
            for(int i=0;i<vertices.Length;i++) vertices[i]=matrix.MultiplyPoint3x4(vertices[i])*.40f;
            float[] cuts={-.1f,.24f,.72f,1.22f,1.74f,2.6f};
            var bodies = new Rigidbody[4];
            for(int slice=0;slice<5;slice++)
            {
                var mesh=Slice(vertices,uv,triangles,cuts[slice],cuts[slice+1]);
                if(mesh.vertexCount==0) throw new InvalidOperationException("Empty tree slice "+slice);
                mesh.name="Small tree slice "+slice;
                AssetDatabase.CreateAsset(mesh,Root+"/TreeSlice_"+slice+".asset");
                var part=new GameObject(slice==0?"Stump (remains)":"Wood fragment "+slice);
                part.transform.SetParent(root.transform,false);part.AddComponent<MeshFilter>().sharedMesh=mesh;
                part.AddComponent<MeshRenderer>().sharedMaterials=new[]{bark,cutMaterial};
                if(slice==0)continue;
                var collider=part.AddComponent<BoxCollider>();collider.center=mesh.bounds.center;collider.size=mesh.bounds.size;collider.enabled=false;
                var body=part.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
                body.mass=.7f;body.linearDamping=.1f;body.angularDamping=.25f;bodies[slice-1]=body;
            }
            var settings = new SerializedObject(wall);
            settings.FindProperty("stones").arraySize = bodies.Length;
            for (int i = 0; i < bodies.Length; i++) settings.FindProperty("stones").GetArrayElementAtIndex(i).objectReferenceValue = bodies[i];
            settings.FindProperty("intactCollider").objectReferenceValue = box;
            settings.FindProperty("target").objectReferenceValue = target;
            settings.FindProperty("showDamageNumbers").boolValue = false;
            settings.FindProperty("destroyOnDeath").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (saved.GetComponents<CombatHealth>().Length != 1 || saved.GetComponent<BreakablePlantExperiment>().FragmentCount != 4
                || saved.GetComponent<CombatTarget>().Team != CombatTeam.Neutral)
                throw new InvalidOperationException("Saved experiment contract failed.");
            Debug.Log("[작은 나무 파괴 실험] 원본 보존·4조각/그루터기 프리팹 생성 완료: " + PrefabPath);
        }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    internal static void ReadSource(Mesh mesh,out Vector3[] vertices,out Vector2[] uv,out int[] triangles)
    {
        // Editor-only read access keeps the supplier's import settings unchanged.
        using(var data=MeshUtility.AcquireReadOnlyMeshData(mesh))
        using(var positions=new NativeArray<Vector3>(mesh.vertexCount,Allocator.Temp))
        using(var texcoords=new NativeArray<Vector2>(mesh.vertexCount,Allocator.Temp))
        {
            data[0].GetVertices(positions);data[0].GetUVs(0,texcoords);
            vertices=positions.ToArray();uv=texcoords.ToArray();
            var sub=data[0].GetSubMesh(0);triangles=new int[sub.indexCount];
            if(mesh.indexFormat==UnityEngine.Rendering.IndexFormat.UInt16)
            {var indices=data[0].GetIndexData<ushort>();for(int i=0;i<triangles.Length;i++)triangles[i]=indices[sub.indexStart+i]+sub.baseVertex;}
            else {var indices=data[0].GetIndexData<uint>();for(int i=0;i<triangles.Length;i++)triangles[i]=(int)indices[sub.indexStart+i]+sub.baseVertex;}
        }
    }
    struct Vertex {public Vector3 p;public Vector2 uv;public Vertex(Vector3 p,Vector2 uv){this.p=p;this.uv=uv;}}
    static List<Vertex> Clip(List<Vertex> polygon,float y,bool above)
    {
        var result=new List<Vertex>();
        for(int i=0;i<polygon.Count;i++)
        {
            Vertex a=polygon[i],b=polygon[(i+1)%polygon.Count];bool ai=above?a.p.y>=y:a.p.y<=y,bi=above?b.p.y>=y:b.p.y<=y;
            if(ai)result.Add(a);
            if(ai!=bi){float t=(y-a.p.y)/(b.p.y-a.p.y);result.Add(new Vertex(Vector3.Lerp(a.p,b.p,t),Vector2.Lerp(a.uv,b.uv,t)));}
        }
        return result;
    }
    internal static Mesh Slice(Vector3[] vertices,Vector2[] uv,int[] source,float low,float high,bool capsEnabled=true)
    {
        var output=new List<Vector3>();var texcoords=new List<Vector2>();var sides=new List<int>();var caps=new List<int>();
        var boundary=new[]{new List<(Vector3 a,Vector3 b)>(),new List<(Vector3 a,Vector3 b)>()};
        int Add(Vector3 p,Vector2 t){int index=output.Count;output.Add(p);texcoords.Add(t);return index;}
        for(int i=0;i<source.Length;i+=3)
        {
            var polygon=new List<Vertex>{new Vertex(vertices[source[i]],uv[source[i]]),new Vertex(vertices[source[i+1]],uv[source[i+1]]),new Vertex(vertices[source[i+2]],uv[source[i+2]])};
            polygon=Clip(Clip(polygon,low,true),high,false);
            for(int j=1;j+1<polygon.Count;j++){sides.Add(Add(polygon[0].p,polygon[0].uv));sides.Add(Add(polygon[j].p,polygon[j].uv));sides.Add(Add(polygon[j+1].p,polygon[j+1].uv));}
            for(int j=0;j<polygon.Count;j++)
            {
                var a=polygon[j].p;var b=polygon[(j+1)%polygon.Count].p;
                if((a-b).sqrMagnitude<.00000001f)continue;
                if(Mathf.Abs(a.y-low)<.00001f&&Mathf.Abs(b.y-low)<.00001f)boundary[0].Add((a,b));
                if(Mathf.Abs(a.y-high)<.00001f&&Mathf.Abs(b.y-high)<.00001f)boundary[1].Add((a,b));
            }
        }
        for(int plane=0;plane<(capsEnabled?2:0);plane++)
        {
            var edges=boundary[plane];
            while(edges.Count>0)
            {
                var first=edges[0];edges.RemoveAt(0);var loop=new List<Vector3>{first.a,first.b};
                while((loop[loop.Count-1]-loop[0]).sqrMagnitude>.0000001f)
                {
                    Vector3 end=loop[loop.Count-1];int next=edges.FindIndex(e=>(e.a-end).sqrMagnitude<.0000001f||(e.b-end).sqrMagnitude<.0000001f);
                    if(next<0)break;var e=edges[next];edges.RemoveAt(next);loop.Add((e.a-end).sqrMagnitude<.0000001f?e.b:e.a);
                }
                if(loop.Count<4||(loop[loop.Count-1]-loop[0]).sqrMagnitude>.0000001f)continue;
                loop.RemoveAt(loop.Count-1);Vector3 center=Vector3.zero;foreach(var point in loop)center+=point;center/=loop.Count;
                for(int i=0;i<loop.Count;i++)
                {
                    Vector3 a=loop[i],b=loop[(i+1)%loop.Count];
                    if((Vector3.Cross(a-center,b-center).y>0)!=(plane==1)){var swap=a;a=b;b=swap;}
                    caps.Add(Add(center,new Vector2(center.x,center.z)));caps.Add(Add(a,new Vector2(a.x,a.z)));caps.Add(Add(b,new Vector2(b.x,b.z)));
                }
            }
        }
        var mesh=new Mesh();mesh.SetVertices(output);mesh.SetUVs(0,texcoords);mesh.subMeshCount=2;
        mesh.SetTriangles(sides,0);mesh.SetTriangles(caps,1);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
}
