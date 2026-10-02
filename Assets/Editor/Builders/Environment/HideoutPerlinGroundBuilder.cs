using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Bakes gentle hideout relief while retaining authored prop foundations and service paths.</summary>
public static class HideoutPerlinGroundBuilder
{
    public const string ScenePath = "Assets/ProjectOverburst/00_Scenes/HideoutScene.unity";
    public const string MeshPath = "Assets/ProjectOverburst/05_Art/Environment/BarbarianHideout/Ground/M_HideoutCamp_Perlin.asset";
    public const float CampAmplitude = .28f;
    public const float OuterAmplitude = .85f;
    const int CellsX = 108, CellsZ = 76;
    static readonly Vector2 WorldMin = new Vector2(-36,-30);
    static readonly Vector2 WorldMax = new Vector2(72,46);
    const string DefaultOutput = "../개인파일/코덱스산출/Environment/20261002_HideoutPerlinGround";

    struct Pad
    {
        public Vector2 min, max;
        public float fade;
        public float Weight(Vector3 p)
        {
            float dx = Mathf.Max(min.x - p.x, 0, p.x - max.x);
            float dz = Mathf.Max(min.y - p.z, 0, p.z - max.y);
            return Mathf.SmoothStep(0, 1, Mathf.Sqrt(dx * dx + dz * dz) / fade);
        }
    }

    struct PathStrip
    {
        public Vector2 a, b;
        public float radius;
        public float Weight(Vector3 p)
        {
            var point = new Vector2(p.x, p.z);
            var segment = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(point - a, segment) / Mathf.Max(segment.sqrMagnitude, .001f));
            return Mathf.SmoothStep(0, 1, (Vector2.Distance(point, a + t * segment) - radius) / 3f);
        }
    }

    [MenuItem("OVERBURST/Environment/Preview Hideout Perlin Ground")]
    public static void PreviewMenu() => Debug.Log(Preview(DefaultOutput));

    [MenuItem("OVERBURST/Environment/Apply Hideout Perlin Ground")]
    public static void ApplyMenu() => Debug.Log(Apply(DefaultOutput, Hash(ScenePath)));

    public static string Preview(string output)
    {
        RequireIdle();
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        Directory.CreateDirectory(output);
        string setup = BarbarianCampUrpVerifier.EditorSnapshot();
        string sceneHash = Hash(ScenePath);
        var scene = EditorSceneManager.OpenPreviewScene(ScenePath);
        Mesh mesh = null;
        try
        {
            var ground = Ground(scene);
            mesh = Generate(scene, ground.transform, out int pads);
            Capture(scene, output, "before");
            SetMesh(ground, mesh);
            FitBoundaries(scene);
            Capture(scene, output, "after");
            var checks = Validate(scene, ground, mesh);
            SaveHeightMap(ground.transform, mesh, Path.Combine(output, "height_map.png"));
            Write(output, "preview_result.json", new {status="PASS", sceneHash, pads, checks,
                campAmplitude=CampAmplitude, outerAmplitude=OuterAmplitude, vertices=mesh.vertexCount,
                triangles=mesh.triangles.Length / 3, width=CellsX, depth=CellsZ});
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            if (mesh != null) Object.DestroyImmediate(mesh);
        }
        if (setup != BarbarianCampUrpVerifier.EditorSnapshot() || sceneHash != Hash(ScenePath))
            throw new InvalidOperationException("Preview changed the existing Editor or scene file.");
        return "PASS: baked terrain preview, collision samples and existing Editor preservation.";
    }

    public static string Apply(string output, string expectedSceneHash)
    {
        RequireIdle();
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        Directory.CreateDirectory(output);
        if (Hash(ScenePath) != expectedSceneHash) throw new InvalidOperationException("Hideout changed since review; refresh the preview first.");
        if (Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(s=>s.path==ScenePath))
            throw new InvalidOperationException("Close or finish the already open Hideout before applying.");
        string setup = BarbarianCampUrpVerifier.EditorSnapshot();
        var active = SceneManager.GetActiveScene();
        string backup = Path.Combine(output, "Before");
        Directory.CreateDirectory(backup);
        File.Copy(ScenePath, Path.Combine(backup, "HideoutScene.unity"), true);
        if (File.Exists(MeshPath)) File.Copy(MeshPath, Path.Combine(backup, "M_HideoutCamp_Perlin.asset"), true);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        Mesh generated = null;
        try
        {
            var ground = Ground(scene);
            string before = NonGroundSnapshot(scene, ground);
            generated = Generate(scene, ground.transform, out int pads);
            EnsureFolder(Path.GetDirectoryName(MeshPath).Replace('\\','/'));
            var asset = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (asset == null)
            {
                AssetDatabase.CreateAsset(generated, MeshPath);
                asset = generated;
                generated = null;
            }
            else
            {
                EditorUtility.CopySerialized(generated, asset);
                EditorUtility.SetDirty(asset);
            }
            AssetDatabase.SaveAssetIfDirty(asset);
            SetMesh(ground, asset);
            FitBoundaries(scene);
            var checks = Validate(scene, ground, asset);
            if (before != NonGroundSnapshot(scene, ground)) throw new InvalidOperationException("A non-ground component changed during baking.");
            if (Hash(ScenePath) != expectedSceneHash) throw new InvalidOperationException("Hideout changed during baking; no scene save attempted.");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Native Hideout save failed.");
            Write(output, "apply_result.json", new {status="PASS", checks, pads, mesh=MeshPath,
                beforeSceneHash=expectedSceneHash, afterSceneHash=Hash(ScenePath), nonGroundComponentsPreserved=true});
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            if (generated != null) Object.DestroyImmediate(generated);
        }
        if (setup != BarbarianCampUrpVerifier.EditorSnapshot()) throw new InvalidOperationException("Existing Editor scene setup changed.");
        return "PASS: saved ground mesh and matching MeshCollider; other scene components and open scenes preserved.";
    }

    public static string VerifySaved(string output)
    {
        RequireIdle();
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        var scene = EditorSceneManager.OpenPreviewScene(ScenePath);
        try
        {
            var ground = Ground(scene);
            var mesh = ground.GetComponent<MeshFilter>().sharedMesh;
            if (AssetDatabase.GetAssetPath(mesh) != MeshPath) throw new InvalidOperationException("Saved mesh reference is missing.");
            var checks = Validate(scene, ground, mesh);
            var rebuilt=Generate(scene,ground.transform,out int pads);
            bool deterministic;
            try { deterministic=mesh.vertices.SequenceEqual(rebuilt.vertices) && mesh.triangles.SequenceEqual(rebuilt.triangles) && mesh.uv.SequenceEqual(rebuilt.uv); }
            finally { Object.DestroyImmediate(rebuilt); }
            if(!deterministic) throw new InvalidOperationException("Rebuilding changes the saved terrain unexpectedly.");
            Write(output, "saved_result.json", new {status="PASS", checks, mesh=MeshPath, deterministic, pads});
            return "PASS: saved scene reload and native collision checks.";
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    static Mesh Generate(Scene scene, Transform ground, out int padCount)
    {
        var pads = Foundations(scene);
        var routes = Routes(scene, pads);
        padCount = pads.Count;
        var vertices = new Vector3[(CellsX+1)*(CellsZ+1)];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[CellsX*CellsZ*6];
        for (int z=0; z<=CellsZ; z++) for (int x=0; x<=CellsX; x++)
        {
            int i=z*(CellsX+1)+x;
            var world = new Vector3(WorldMin.x+x, ground.position.y, WorldMin.y+z);
            var local = ground.InverseTransformPoint(world);
            float outside = Mathf.SmoothStep(0,1,RectDistance(world, new Vector2(-16,-10),new Vector2(16,18))/12f);
            float amplitude = Mathf.Lerp(CampAmplitude,OuterAmplitude,outside);
            float noise = .65f*SignedNoise(world,38f,131.37f,79.91f)
                        + .25f*SignedNoise(world,16f,33.19f,217.63f)
                        + .10f*SignedNoise(world,7f,291.71f,18.47f);
            float mask=1f;
            foreach (var pad in pads) mask=Mathf.Min(mask,pad.Weight(world));
            foreach (var route in routes) mask=Mathf.Min(mask,route.Weight(world));
            // The perimeter stays at the authored elevation beside the existing world boundaries.
            float edge=Mathf.Min(x,CellsX-x,z,CellsZ-z);
            float height=noise*amplitude*mask*Mathf.SmoothStep(0,1,edge/6f);
            world.y += height;
            vertices[i]=ground.InverseTransformPoint(world);
            // Unity's built-in Plane reverses both UV axes; retain the existing Mud orientation.
            uv[i]=new Vector2(1-(local.x+5)/10,1-(local.z+5)/10);
        }
        int n=0;
        for (int z=0;z<CellsZ;z++) for(int x=0;x<CellsX;x++)
        {
            int a=z*(CellsX+1)+x, b=a+1, c=a+CellsX+1, d=c+1;
            triangles[n++]=a; triangles[n++]=c; triangles[n++]=b;
            triangles[n++]=b; triangles[n++]=c; triangles[n++]=d;
        }
        var mesh=new Mesh {name="M_HideoutCamp_Perlin", vertices=vertices, uv=uv, triangles=triangles};
        mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        return mesh;
    }

    static List<Pad> Foundations(Scene scene)
    {
        var pads=new List<Pad>();
        var layout=All(scene).Single(t=>t.name=="Camp Layout");
        var cache=new Dictionary<Mesh,Vector3[]>();
        foreach (Transform group in layout) foreach (Transform prop in group)
        {
            var points=new List<Vector3>();
            foreach(var filter in prop.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh=filter.sharedMesh;
                if(mesh==null) continue;
                if(!cache.TryGetValue(mesh,out var local))
                {
                    using(var data=Mesh.AcquireReadOnlyMeshData(mesh))
                    using(var array=new NativeArray<Vector3>(mesh.vertexCount,Allocator.Temp))
                    { data[0].GetVertices(array); local=array.ToArray(); }
                    cache.Add(mesh,local);
                }
                points.AddRange(local.Select(filter.transform.TransformPoint));
            }
            if(points.Count==0) continue;
            float bottom=points.Min(p=>p.y);
            // Elevated hanging props do not create extra flat islands.
            if(bottom>.45f || bottom< -1f) continue;
            bool nature=group.name=="Nature";
            var footprint=nature ? points.Where(p=>p.y<=bottom+.35f).ToArray() : points.ToArray();
            float padding=nature ? .75f : .9f;
            pads.Add(new Pad {min=new Vector2(footprint.Min(p=>p.x)-padding,footprint.Min(p=>p.z)-padding),
                max=new Vector2(footprint.Max(p=>p.x)+padding,footprint.Max(p=>p.z)+padding),fade=nature ? 3.5f : 3f});
        }
        return pads;
    }

    static List<PathStrip> Routes(Scene scene,List<Pad> pads)
    {
        var routes=new List<PathStrip>();
        var center=new Vector2(0,-3);
        foreach(var t in All(scene))
        {
            bool service=t.name=="StashObject" || t.name.EndsWith("MerchantObject",StringComparison.Ordinal);
            bool anchor=t.name=="SpawnPoint" || t.name=="Test Pickup Origin" || t.GetComponent<HubReturnPoint>()!=null;
            if(!service && !anchor) continue;
            var p=new Vector2(t.position.x,t.position.z);
            float radius=t.name=="SpawnPoint" ? 6f : (t.name=="Test Pickup Origin" ? 7f : 3f);
            pads.Add(new Pad {min=p-Vector2.one*radius,max=p+Vector2.one*radius,fade=4f});
            if(service || (anchor && Mathf.Abs(p.x)<20)) routes.Add(new PathStrip {a=center,b=p,radius=1.7f});
        }
        // Preserve the gate and the route to the pickup clearing south of camp.
        routes.Add(new PathStrip {a=center,b=new Vector2(0,-12),radius=2.5f});
        // Existing practice actors also stand on Camp Ground, with no independent floor.
        pads.Add(new Pad {min=new Vector2(47,-19),max=new Vector2(71,-5),fade=4f});
        return routes;
    }

    static void FitBoundaries(Scene scene)
    {
        var center=(WorldMin+WorldMax)*.5f;
        foreach(var t in All(scene).Where(t=>t.name.StartsWith("Camp Boundary ",StringComparison.Ordinal)))
        {
            var box=t.GetComponent<BoxCollider>();
            if(box==null || Vector3.Distance(t.lossyScale,Vector3.one)>.001f)
                throw new InvalidOperationException("Unsupported boundary shape.");
            bool vertical=t.name.EndsWith("0",StringComparison.Ordinal) || t.name.EndsWith("1",StringComparison.Ordinal);
            float x=t.name.EndsWith("0",StringComparison.Ordinal) ? WorldMax.x : (t.name.EndsWith("1",StringComparison.Ordinal) ? WorldMin.x : center.x);
            float z=t.name.EndsWith("2",StringComparison.Ordinal) ? WorldMax.y : (t.name.EndsWith("3",StringComparison.Ordinal) ? WorldMin.y : center.y);
            t.position=new Vector3(x,t.position.y,z);
            box.size=vertical ? new Vector3(1,box.size.y,CellsZ) : new Vector3(CellsX,box.size.y,1);
        }
        Physics.SyncTransforms();
    }

    static object Validate(Scene scene,GameObject ground,Mesh mesh)
    {
        var collider=ground.GetComponent<MeshCollider>();
        if(mesh==null || collider.sharedMesh!=mesh || ground.layer!=LayerMask.NameToLayer("Ground"))
            throw new InvalidOperationException("Ground mesh/collider/layer mismatch.");
        if(mesh.vertexCount!=(CellsX+1)*(CellsZ+1) || mesh.triangles.Length!=CellsX*CellsZ*6)
            throw new InvalidOperationException("Unexpected terrain topology.");
        var vertices=mesh.vertices.Select(ground.transform.TransformPoint).ToArray();
        var triangles=mesh.triangles;
        float maxSlope=0;
        for(int i=0;i<triangles.Length;i+=3)
        {
            var normal=Vector3.Cross(vertices[triangles[i+1]]-vertices[triangles[i]],vertices[triangles[i+2]]-vertices[triangles[i]]).normalized;
            maxSlope=Mathf.Max(maxSlope,Vector3.Angle(Vector3.up,normal));
        }
        if(maxSlope>15f) throw new InvalidOperationException("Terrain slope exceeds the gentle-ground limit: "+maxSlope);
        int rays=0;
        for(int z=1;z<CellsZ;z+=5) for(int x=1;x<CellsX;x+=5)
        {
            var p=vertices[z*(CellsX+1)+x];
            if(!collider.Raycast(new Ray(p+Vector3.up*5,Vector3.down),out var hit,10) || Mathf.Abs(hit.point.y-p.y)>.002f)
                throw new InvalidOperationException("Ground collider disagrees with the baked surface.");
            rays++;
        }
        var pads=Foundations(scene); var routes=Routes(scene,pads);
        int protectedSamples=0;
        foreach(var p in vertices)
        {
            bool flat=pads.Any(pad=>pad.Weight(p)==0) || routes.Any(route=>route.Weight(p)==0);
            if(!flat) continue;
            if(Mathf.Abs(p.y-ground.transform.position.y)>.002f) throw new InvalidOperationException("A foundation or service path is no longer flat.");
            protectedSamples++;
        }
        if(All(scene).Any(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0))
            throw new InvalidOperationException("Hideout has missing scripts.");
        int props=All(scene).Single(t=>t.name=="Camp Layout").Cast<Transform>().Sum(g=>g.childCount);
        var bounds=ground.GetComponent<Renderer>().bounds;
        if(Mathf.Abs(bounds.min.x-WorldMin.x)>.002f || Mathf.Abs(bounds.max.x-WorldMax.x)>.002f ||
           Mathf.Abs(bounds.min.z-WorldMin.y)>.002f || Mathf.Abs(bounds.max.z-WorldMax.y)>.002f)
            throw new InvalidOperationException("Saved ground footprint is wrong.");
        var walls=All(scene).Where(t=>t.name.StartsWith("Camp Boundary ",StringComparison.Ordinal)).ToArray();
        if(walls.Length!=4) throw new InvalidOperationException("Expected four perimeter walls.");
        foreach(var wall in walls)
        {
            var box=wall.GetComponent<BoxCollider>();
            bool vertical=wall.name.EndsWith("0",StringComparison.Ordinal) || wall.name.EndsWith("1",StringComparison.Ordinal);
            if(box==null || !box.enabled || box.isTrigger || Mathf.Abs((vertical ? box.size.z : box.size.x)-(vertical ? CellsZ : CellsX))>.002f)
                throw new InvalidOperationException("Perimeter wall does not follow the shortened ground.");
        }
        return new {vertices=mesh.vertexCount,triangles=triangles.Length/3,minimum=vertices.Min(p=>p.y),
            maximum=vertices.Max(p=>p.y),maxSlopeDegrees=maxSlope,colliderRays=rays,protectedSamples,props,missingScripts=0,
            width=CellsX,depth=CellsZ,perimeterWalls=walls.Length,
            campMaximumRelief=vertices.Where(p=>p.x>=-16 && p.x<=16 && p.z>=-10 && p.z<=18).Max(p=>Mathf.Abs(p.y))};
    }

    static void Capture(Scene scene,string output,string stage)
    {
        var root=new GameObject("Temporary terrain review camera");
        SceneManager.MoveGameObjectToScene(root,scene);
        var camera=root.AddComponent<Camera>(); camera.scene=scene;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.17f,.18f,.20f);
        camera.nearClipPlane=.1f; camera.farClipPlane=500;
        var target=RenderTexture.GetTemporary(1600,1000,24);
        var texture=new Texture2D(1600,1000,TextureFormat.RGB24,false);
        var previous=RenderTexture.active;
        try
        {
            for(int i=0;i<2;i++)
            {
                camera.transform.position=i==0 ? new Vector3(26,30,-36) : new Vector3(-37,8,-33);
                camera.transform.LookAt(i==0 ? new Vector3(0,0,3) : new Vector3(-10,0,0));
                camera.fieldOfView=i==0 ? 52 : 65;
                camera.targetTexture=target; camera.Render(); RenderTexture.active=target;
                texture.ReadPixels(new Rect(0,0,1600,1000),0,0); texture.Apply();
                File.WriteAllBytes(Path.Combine(output,stage+(i==0 ? "_camp.png" : "_outer.png")),texture.EncodeToPNG());
            }
        }
        finally
        {
            camera.targetTexture=null; RenderTexture.active=previous;
            RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(texture); Object.DestroyImmediate(root);
        }
    }

    static void SaveHeightMap(Transform ground,Mesh mesh,string path)
    {
        var texture=new Texture2D(CellsX+1,CellsZ+1,TextureFormat.RGB24,false);
        var pixels=mesh.vertices.Select(v=> {float h=ground.TransformPoint(v).y;
            return h>=0 ? Color.Lerp(new Color(.34f,.38f,.30f),new Color(.8f,.66f,.32f),h/OuterAmplitude)
                        : Color.Lerp(new Color(.34f,.38f,.30f),new Color(.16f,.3f,.48f),-h/OuterAmplitude);}).ToArray();
        texture.SetPixels(pixels); texture.Apply(); File.WriteAllBytes(path,texture.EncodeToPNG()); Object.DestroyImmediate(texture);
    }

    static float SignedNoise(Vector3 p,float period,float ox,float oz) => Mathf.Clamp01(Mathf.PerlinNoise(p.x/period+ox,p.z/period+oz))*2-1;
    static float RectDistance(Vector3 p,Vector2 min,Vector2 max) => new Vector2(Mathf.Max(min.x-p.x,0,p.x-max.x),Mathf.Max(min.y-p.z,0,p.z-max.y)).magnitude;
    static Transform[] All(Scene scene) => scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
    static GameObject Ground(Scene scene) => All(scene).Single(t=>t.name=="Camp Ground").gameObject;
    static void SetMesh(GameObject ground,Mesh mesh)
    { ground.GetComponent<MeshFilter>().sharedMesh=mesh; var collider=ground.GetComponent<MeshCollider>(); collider.sharedMesh=null; collider.sharedMesh=mesh; Physics.SyncTransforms(); }
    static string NonGroundSnapshot(Scene scene,GameObject ground) => JsonConvert.SerializeObject(All(scene).SelectMany(t=>t.GetComponents<Component>())
        .Where(c=>c!=null && !(c.gameObject==ground && (c is MeshFilter || c is MeshCollider)) &&
            !(c.name.StartsWith("Camp Boundary ",StringComparison.Ordinal) && (c is Transform || c is BoxCollider)))
        .Select(c=>new {id=c.GetInstanceID(),data=EditorJsonUtility.ToJson(c)}));
    public static string Hash(string path) { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant(); }
    static void Write(string output,string name,object data) => File.WriteAllText(Path.Combine(output,name),JsonConvert.SerializeObject(data,Formatting.Indented));
    static void EnsureFolder(string path)
    { if(AssetDatabase.IsValidFolder(path)) return; EnsureFolder(Path.GetDirectoryName(path).Replace('\\','/')); AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path)); }
    static void RequireIdle()
    { if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Use an idle Editor; existing Play is never interrupted."); }
}
