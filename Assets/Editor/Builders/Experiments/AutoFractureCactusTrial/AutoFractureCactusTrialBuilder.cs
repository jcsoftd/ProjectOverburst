using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Overburst.Experimental.VegetationFracture;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class AutoFractureCactusTrialBuilder
{
    public const string Root = "Assets/ProjectOverburst/03_Features/World/AutoFractureCactusTrial";
    public const string PrefabPath = Root + "/PF_AutoFractureCactusTrial.prefab";
    public const string TownPath = "Assets/ProjectOverburst/00_Scenes/MainScene.unity";
    public const string InstanceName = "__EXPERIMENT_AUTO_FRACTURE_CACTUS__";

    public static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling
            || EditorApplication.isUpdating || EditorUtility.scriptCompilationFailed)
            throw new InvalidOperationException("Stop Play and finish compilation before editing the cactus experiment.");
    }

    [MenuItem("OVERBURST/Experiments/자동 분할 선인장/생성 및 마을 배치")]
    public static void Install()
    {
        RequireIdle();
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) Build();
        var scene = SceneManager.GetSceneByPath(TownPath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(TownPath, OpenSceneMode.Additive);
        if (scene.isDirty) throw new InvalidOperationException("MainScene has unsaved changes; preserve them first.");
        var existing = Plants(scene).SingleOrDefault();
        if (existing != null) { Selection.activeGameObject = existing.gameObject; return; }
        var spawn = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<HubReturnPoint>(true))
            .Single(x => x.ReturnPointId == "Default").transform;
        Vector3 position = spawn.position + spawn.rotation * new Vector3(-2.2f, 0, -2.6f);
        foreach (var terrain in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Terrain>(true)))
        {
            var relative = position - terrain.transform.position; var size = terrain.terrainData.size;
            if (relative.x < 0 || relative.z < 0 || relative.x > size.x || relative.z > size.z) continue;
            position.y = terrain.SampleHeight(position) + terrain.transform.position.y + .01f; break;
        }
        var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
        root.name = InstanceName; root.transform.SetPositionAndRotation(position, spawn.rotation);
        Undo.RegisterCreatedObjectUndo(root, "Place automatic fracture cactus experiment");
        PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = root;
        Debug.Log("[자동 분할 선인장] 마을 스폰 왼쪽 앞에 배치했습니다. 평소 공격 / F8 복원.");
    }

    [MenuItem("OVERBURST/Experiments/자동 분할 선인장/마을 배치만 제거")]
    public static void RemovePlacement()
    {
        RequireIdle();
        var scene = SceneManager.GetSceneByPath(TownPath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(TownPath, OpenSceneMode.Additive);
        if (scene.isDirty) throw new InvalidOperationException("MainScene has unsaved changes; preserve them first.");
        var plants = Plants(scene).ToArray();
        foreach (var plant in plants)
            if (plant.name != InstanceName || plant.transform.parent != null
                || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(plant.gameObject) != PrefabPath)
                throw new InvalidOperationException("Unexpected cactus instance. Refuse automatic removal.");
        foreach (var plant in plants) Undo.DestroyObjectImmediate(plant.gameObject);
        if (plants.Length > 0) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
    }

    public static IEnumerable<AutoFractureCactusTrial> Plants(Scene scene) => scene.GetRootGameObjects()
        .SelectMany(x => x.GetComponentsInChildren<AutoFractureCactusTrial>(true))
        .Where(x => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(x.gameObject) == PrefabPath);

    public static void Build()
    {
        RequireIdle();
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) throw new InvalidOperationException("Preserve the existing cactus prefab.");
        if (AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/CactusMeshes.asset") != null) throw new InvalidOperationException("Partial assets already exist; inspect them before retrying.");
        var scene = EditorSceneManager.NewPreviewScene();
        var transient = new List<Object>(); GameObject root = null;
        try
        {
            var centers = new[] { new Vector3(0,1.125f,0), new Vector3(-.34f,1.12f,0), new Vector3(-.65f,1.40f,0), new Vector3(.33f,.82f,0), new Vector3(.62f,1.10f,0) };
            var heights = new[] { 2.25f, .68f, .80f, .66f, .86f };
            var radii = new[] { .27f, .15f, .16f, .14f, .15f };
            var counts = new[] { 12, 4, 6, 4, 6 };
            var sources = new Mesh[centers.Length]; var generated = new FragmentResult[centers.Length][];
            for (int part = 0; part < centers.Length; part++)
            {
                var source = sources[part] = Stem(radii[part], heights[part]); transient.Add(source);
                var pieces = generated[part] = MeshFragmenter.Fragment(source, counts[part], new Vector3(part * 2.33f, 7.5f, 17.6f));
                foreach (var piece in pieces) if (piece.Mesh != source) transient.Add(piece.Mesh);
                if (pieces.Length < 2 || pieces.Any(x => x.Mesh == source || x.Mesh == null)) throw new InvalidOperationException("No valid automatic fragments.");
                float expected = Volume(source), actual = pieces.Sum(x => Volume(x.Mesh));
                if (Mathf.Abs(actual - expected) / expected > .015f) throw new InvalidOperationException("Fracture volume mismatch: part " + part + ", " + expected + " / " + actual);
            }
            var skin = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Cactus exterior" };
            skin.SetColor("_BaseColor", new Color(.40f, .55f, .28f)); skin.SetFloat("_Smoothness", .12f);
            var inside = new Material(skin) { name = "Cactus cut flesh" };
            inside.SetColor("_BaseColor", new Color(.68f, .77f, .46f));
            var texture = SkinTexture(); skin.SetTexture("_BaseMap", texture);
            AssetDatabase.CreateAsset(texture, Root + "/CactusSkin.asset");
            AssetDatabase.CreateAsset(skin, Root + "/CactusExterior.mat");
            AssetDatabase.CreateAsset(inside, Root + "/CactusInterior.mat");
            root = new GameObject("Automatic mesh fracture cactus trial"); SceneManager.MoveGameObjectToScene(root, scene);
            var plant = root.AddComponent<AutoFractureCactusTrial>();
            var collider = root.AddComponent<CapsuleCollider>(); collider.center = Vector3.up * 1.13f;
            collider.radius = .64f; collider.height = 2.30f;
            var target = root.AddComponent<CombatTarget>(); target.Configure(CombatTeam.Neutral, false);
            target.ConfigureVolume(Vector3.up * 1.13f, .70f, 2.30f);
            var combines = new CombineInstance[centers.Length]; var bodies = new List<Rigidbody>();
            var intact = new GameObject("Intact cactus"); intact.transform.SetParent(root.transform, false);
            var all = new Mesh { name = "Intact cactus" }; AssetDatabase.CreateAsset(all, Root + "/CactusMeshes.asset");
            for (int part = 0; part < centers.Length; part++)
            {
                var source = sources[part];
                var rotation = part == 1 || part == 3 ? Quaternion.Euler(0,0,90) : Quaternion.identity;
                combines[part] = new CombineInstance { mesh = source, transform = Matrix4x4.TRS(centers[part], rotation, Vector3.one) };
                var pieces = generated[part];
                foreach (var piece in pieces)
                {
                    piece.Mesh.name = "Cactus fragment " + bodies.Count;
                    AssetDatabase.AddObjectToAsset(piece.Mesh, all);
                    var chunk = new GameObject("Auto fragment " + bodies.Count.ToString("00")); chunk.transform.SetParent(root.transform, false);
                    chunk.transform.localPosition = centers[part] + rotation * piece.Centroid; chunk.transform.localRotation = rotation;
                    chunk.AddComponent<MeshFilter>().sharedMesh = piece.Mesh;
                    chunk.AddComponent<MeshRenderer>().sharedMaterials = new[] { skin, inside };
                    var cc = chunk.AddComponent<MeshCollider>(); cc.sharedMesh = piece.Mesh; cc.convex = true; cc.enabled = false;
                    var body = chunk.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                    body.mass = Mathf.Clamp(Volume(piece.Mesh) * 120f, .05f, 3f); body.linearDamping = .15f; body.angularDamping = .2f;
                    bodies.Add(body); chunk.SetActive(false);
                }
            }
            all.CombineMeshes(combines); all.RecalculateBounds();
            EditorUtility.SetDirty(all);
            intact.AddComponent<MeshFilter>().sharedMesh = all; intact.AddComponent<MeshRenderer>().sharedMaterial = skin;
            var settings = new SerializedObject(plant);
            settings.FindProperty("intactVisual").objectReferenceValue = intact;
            settings.FindProperty("fragments").arraySize = bodies.Count;
            for (int i = 0; i < bodies.Count; i++) settings.FindProperty("fragments").GetArrayElementAtIndex(i).objectReferenceValue = bodies[i];
            settings.FindProperty("intactCollider").objectReferenceValue = collider; settings.FindProperty("target").objectReferenceValue = target;
            settings.FindProperty("showDamageNumbers").boolValue = false; settings.FindProperty("destroyOnDeath").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); AssetDatabase.SaveAssetIfDirty(all);
            if (bodies.Count < 20 || bodies.Count > 32) throw new InvalidOperationException("Expected 20 to 32 automatically generated pieces.");
        }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
            foreach (var item in transient) if (item != null && !AssetDatabase.Contains(item)) Object.DestroyImmediate(item);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static Texture2D SkinTexture()
    {
        var tex = new Texture2D(256,128,TextureFormat.RGBA32,false) { name = "Cactus ribbed skin", wrapMode = TextureWrapMode.Repeat };
        for (int y = 0; y < tex.height; y++) for (int x = 0; x < tex.width; x++)
        {
            float rib = .62f + .38f * Mathf.Pow(.5f + .5f * Mathf.Cos(x * Mathf.PI * 24 / 256f), 2);
            bool needle = (x % 21 == 3 || x % 21 == 4) && (y % 19 >= 8 && y % 19 <= 11);
            tex.SetPixel(x,y,needle ? new Color(.72f,.73f,.47f) : new Color(rib,rib,rib));
        }
        tex.Apply(); return tex;
    }

    static Mesh Stem(float radius, float height)
    {
        const int sides = 12;
        var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
        var levels = new[] { 0f, .06f, .88f, .97f, 1f }; var widths = new[] { .80f, 1f, 1f, .72f, .15f };
        for (int row = 0; row < levels.Length; row++) for (int col = 0; col <= sides; col++)
        {
            float angle = col * Mathf.PI * 2 / sides;
            vertices.Add(new Vector3(Mathf.Cos(angle) * radius * widths[row], (levels[row] - .5f) * height, Mathf.Sin(angle) * radius * widths[row]));
            uv.Add(new Vector2((float)col / sides, levels[row]));
        }
        for (int row = 0; row < levels.Length - 1; row++) for (int col = 0; col < sides; col++)
        {
            int a = row * (sides + 1) + col, b = a + sides + 1, c = b + 1, d = a + 1;
            triangles.AddRange(new[] { a,b,c, a,c,d });
        }
        int bottom = vertices.Count; vertices.Add(Vector3.down * height * .5f); uv.Add(Vector2.zero);
        int top = vertices.Count; vertices.Add(Vector3.up * height * .5f); uv.Add(Vector2.one);
        for (int col = 0; col < sides; col++)
        {
            triangles.AddRange(new[] { bottom,col,col+1 });
            int a = (levels.Length - 1) * (sides + 1) + col; triangles.AddRange(new[] { top,a+1,a });
        }
        var mesh = new Mesh { name = "Closed cactus stem" }; mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles,0);
        mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); return mesh;
    }

    public static float Volume(Mesh mesh)
    {
        var vertices = mesh.vertices; var triangles = mesh.triangles; double volume = 0;
        for (int i = 0; i < triangles.Length; i += 3) volume += Vector3.Dot(vertices[triangles[i]], Vector3.Cross(vertices[triangles[i+1]], vertices[triangles[i+2]])) / 6.0;
        return Mathf.Abs((float)volume);
    }
}

[CustomEditor(typeof(AutoFractureCactusTrial))]
public sealed class AutoFractureCactusTrialInspector : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox("자동 분할 실험: 선인장 32조각, 마을 소품 각 12조각. 평소 근접 공격 / F8 전체 복원. 배치 제거 후 전용 폴더와 옆 meta를 휴지통으로 이동하세요.", MessageType.Info);
        if (Application.isPlaying && GUILayout.Button("이 오브젝트 복원")) ((AutoFractureCactusTrial)target).ResetPlant();
        if (!Application.isPlaying && GUILayout.Button("자동 분할 실험의 마을 배치 모두 제거")) AutoFractureTownPropTrialBuilder.RemoveAllPlacements();
    }
}
