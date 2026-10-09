using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Overburst.Experimental.VegetationFracture;
using Object = UnityEngine.Object;

public static class AutoFractureTownPropTrialBuilder
{
    public const string GroupName = "__EXPERIMENT_AUTO_FRACTURE_TOWN_PROPS__";
    const string SourceRoot = "Assets/ThirdParty/04_환경맵/Dark Fantasy Bandit Camp & Wilderness/Prefabs/Environment/";
    public static readonly string[] Names = { "DF_Barrel_Wood_01", "DF_Barrel_Wood_03", "DF_Wood_Box_05" };
    public static string PrefabPath(string name) => AutoFractureCactusTrialBuilder.Root + "/PF_AutoFracture_" + name + ".prefab";

    [MenuItem("OVERBURST/Experiments/자동 분할 마을 소품/생성 및 마을 배치")]
    public static void Install()
    {
        AutoFractureCactusTrialBuilder.RequireIdle();
        var scene = Town();
        foreach (string name in Names) if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name)) == null) Build(name);
        var existing = scene.GetRootGameObjects().Where(x => x.name == GroupName).ToArray();
        if (existing.Length > 1) throw new InvalidOperationException("Duplicate trial groups; preserve them for inspection.");
        if (existing.Length == 1) { ValidateGroup(existing[0]); Selection.activeGameObject = existing[0]; return; }
        var spawn = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<HubReturnPoint>(true))
            .Single(x => x.ReturnPointId == "Default").transform;
        var group = new GameObject(GroupName); SceneManager.MoveGameObjectToScene(group, scene);
        Undo.RegisterCreatedObjectUndo(group, "Place automatic fracture town props");
        for (int i = 0; i < Names.Length; i++)
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(Names[i])), scene);
            root.transform.SetParent(group.transform, false);
            Vector3 position = spawn.position + spawn.rotation * new Vector3(1.8f + i * 1.7f, 0, -2.6f);
            foreach (var terrain in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Terrain>(true)))
            {
                var local = position - terrain.transform.position; var size = terrain.terrainData.size;
                if (local.x < 0 || local.z < 0 || local.x > size.x || local.z > size.z) continue;
                position.y = terrain.SampleHeight(position) + terrain.transform.position.y + .01f; break;
            }
            root.transform.SetPositionAndRotation(position, spawn.rotation);
            PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);
        }
        ValidateGroup(group); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = group;
        Debug.Log("[자동 분할 마을 소품] 스폰 오른쪽 앞: 일반 통 / 넓은 통 / 작은 상자. 근접 공격, F8 전체 복원.");
    }

    static Scene Town()
    {
        var scene = SceneManager.GetSceneByPath(AutoFractureCactusTrialBuilder.TownPath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(AutoFractureCactusTrialBuilder.TownPath, OpenSceneMode.Additive);
        if (scene.isDirty) throw new InvalidOperationException("Preserve unsaved MainScene changes before editing trials.");
        return scene;
    }

    public static void ValidateGroup(GameObject group)
    {
        if (group.name != GroupName || group.transform.parent != null || group.transform.childCount != Names.Length)
            throw new InvalidOperationException("Unexpected group structure; refuse automatic removal.");
        for (int i = 0; i < Names.Length; i++)
        {
            var child = group.transform.GetChild(i).gameObject;
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child) != PrefabPath(Names[i])
                || child.GetComponent<AutoFractureCactusTrial>() == null)
                throw new InvalidOperationException("Unexpected trial child; preserve it for inspection.");
        }
    }

    [MenuItem("OVERBURST/Experiments/자동 분할 마을 소품/소품 배치만 제거")]
    public static void RemovePlacement()
    {
        AutoFractureCactusTrialBuilder.RequireIdle(); var scene = Town();
        var groups = scene.GetRootGameObjects().Where(x => x.name == GroupName).ToArray();
        foreach (var group in groups) ValidateGroup(group);
        foreach (var group in groups) Undo.DestroyObjectImmediate(group);
        if (groups.Length > 0) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
    }

    [MenuItem("OVERBURST/Experiments/자동 분할 마을 소품/선인장과 소품 배치 모두 제거")]
    public static void RemoveAllPlacements()
    {
        RemovePlacement(); AutoFractureCactusTrialBuilder.RemovePlacement();
    }

    static void Build(string name)
    {
        string rootPath = AutoFractureCactusTrialBuilder.Root;
        string meshPath = rootPath + "/" + name + "_Meshes.asset", materialPath = rootPath + "/" + name + "_Cut.mat";
        if (AssetDatabase.LoadMainAssetAtPath(meshPath) != null || AssetDatabase.LoadMainAssetAtPath(materialPath) != null)
            throw new InvalidOperationException("Partial trial assets exist; inspect before retrying: " + name);
        var original = AssetDatabase.LoadAssetAtPath<GameObject>(SourceRoot + name + ".prefab");
        if (original == null) throw new InvalidOperationException("Actual town source prefab missing: " + name);
        var filter = original.GetComponentsInChildren<MeshFilter>(true).Single();
        if (filter.transform != original.transform || original.transform.localScale != Vector3.one || original.transform.localRotation != Quaternion.identity)
            throw new InvalidOperationException("Inspect source transform before baking: " + name);
        var scene = EditorSceneManager.NewPreviewScene(); var transient = new List<Object>(); GameObject root = null;
        try
        {
            var source = ReadSource(filter.sharedMesh); transient.Add(source);
            var pieces = MeshFragmenter.Fragment(source, 12, new Vector3(2.33f, 7.5f, 17.6f));
            foreach (var piece in pieces) if (piece.Mesh != source) transient.Add(piece.Mesh);
            float expected = AutoFractureCactusTrialBuilder.Volume(source), actual = pieces.Sum(x => AutoFractureCactusTrialBuilder.Volume(x.Mesh));
            if (expected <= .001f || pieces.Length != 12 || pieces.Any(x => x.Mesh == source)
                || Mathf.Abs(actual - expected) / expected > .015f)
                throw new InvalidOperationException("Automatic fracture preflight failed: " + name);
            source.name = name + " readable source copy"; AssetDatabase.CreateAsset(source, meshPath);
            var cut = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name + " exposed wood" };
            cut.SetColor("_BaseColor", new Color(.36f,.23f,.12f)); cut.SetFloat("_Smoothness", .08f);
            AssetDatabase.CreateAsset(cut, materialPath);
            root = new GameObject("Auto fracture " + name); SceneManager.MoveGameObjectToScene(root, scene);
            var trial = root.AddComponent<AutoFractureCactusTrial>(); var bounds = source.bounds;
            var collider = root.AddComponent<BoxCollider>(); collider.center = bounds.center; collider.size = bounds.size;
            var target = root.AddComponent<CombatTarget>(); target.Configure(CombatTeam.Neutral, false);
            target.ConfigureVolume(bounds.center, Mathf.Max(bounds.extents.x,bounds.extents.z) + .06f, Mathf.Max(.6f,bounds.size.y + .1f));
            var intact = (GameObject)PrefabUtility.InstantiatePrefab(original, scene); intact.transform.SetParent(root.transform,false);
            intact.transform.localPosition=Vector3.zero; intact.transform.localRotation=Quaternion.identity;
            foreach (var c in intact.GetComponentsInChildren<Collider>(true)) { c.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(c); }
            var exterior = filter.GetComponent<MeshRenderer>().sharedMaterial; var bodies = new List<Rigidbody>();
            foreach (var piece in pieces)
            {
                piece.Mesh.name = name + " auto fragment " + bodies.Count; AssetDatabase.AddObjectToAsset(piece.Mesh, source);
                var chunk = new GameObject("Auto fragment " + bodies.Count.ToString("00")); chunk.transform.SetParent(root.transform,false);
                chunk.transform.localPosition = piece.Centroid; chunk.AddComponent<MeshFilter>().sharedMesh = piece.Mesh;
                chunk.AddComponent<MeshRenderer>().sharedMaterials = new[] { exterior, cut };
                var cc=chunk.AddComponent<MeshCollider>();cc.sharedMesh=piece.Mesh;cc.convex=true;cc.enabled=false;
                var body=chunk.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
                body.mass=Mathf.Clamp(AutoFractureCactusTrialBuilder.Volume(piece.Mesh)*110f,.05f,3f);body.linearDamping=.15f;body.angularDamping=.2f;
                bodies.Add(body);chunk.SetActive(false);
            }
            var settings=new SerializedObject(trial); settings.FindProperty("intactVisual").objectReferenceValue=intact;
            settings.FindProperty("fragments").arraySize=bodies.Count;
            for(int i=0;i<bodies.Count;i++)settings.FindProperty("fragments").GetArrayElementAtIndex(i).objectReferenceValue=bodies[i];
            settings.FindProperty("intactCollider").objectReferenceValue=collider;settings.FindProperty("target").objectReferenceValue=target;
            settings.FindProperty("showDamageNumbers").boolValue=false;settings.FindProperty("destroyOnDeath").boolValue=false;
            settings.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.SaveAsPrefabAsset(root,PrefabPath(name));AssetDatabase.SaveAssetIfDirty(source);
        }
        finally
        {
            if(root!=null)Object.DestroyImmediate(root);
            foreach(var item in transient)if(item!=null&&!AssetDatabase.Contains(item))Object.DestroyImmediate(item);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static Mesh ReadSource(Mesh original)
    {
        if(original.subMeshCount!=1)throw new InvalidOperationException("Trial requires a single source material.");
        using(var data=UnityEditor.MeshUtility.AcquireReadOnlyMeshData(original))
        using(var positions=new NativeArray<Vector3>(original.vertexCount,Allocator.Temp))
        using(var normals=new NativeArray<Vector3>(original.vertexCount,Allocator.Temp))
        using(var uvs=new NativeArray<Vector2>(original.vertexCount,Allocator.Temp))
        {
            var d=data[0];d.GetVertices(positions);d.GetNormals(normals);d.GetUVs(0,uvs);
            var sub=d.GetSubMesh(0);var triangles=new int[sub.indexCount];
            if(original.indexFormat==UnityEngine.Rendering.IndexFormat.UInt16){var indices=d.GetIndexData<ushort>();for(int i=0;i<triangles.Length;i++)triangles[i]=indices[sub.indexStart+i]+sub.baseVertex;}
            else{var indices=d.GetIndexData<uint>();for(int i=0;i<triangles.Length;i++)triangles[i]=(int)indices[sub.indexStart+i]+sub.baseVertex;}
            var mesh=new Mesh();mesh.vertices=positions.ToArray();mesh.normals=normals.ToArray();mesh.uv=uvs.ToArray();mesh.triangles=triangles;
            mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;
        }
    }
}
