using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>Render publisher animation clips in an isolated preview scene, without entering Play.</summary>
public static class UpcomingMonsterMotionCapture
{
    const int Cell=320,Cols=6,Layer=31;
    static JObject plan;
    static JArray results;
    static Queue<JObject> tasks;
    static string output;
    public static string Queue(string directory)
    {
        if(EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Motion capture requires idle Edit Mode.");
        if(tasks!=null)throw new InvalidOperationException("Motion capture is already running.");
        output=directory;Directory.CreateDirectory(Path.Combine(output,"motion-strips"));
        plan=JObject.Parse(File.ReadAllText(Path.Combine(output,"motion-capture-plan.json")));
        results=new JArray();
        var receipt=Path.Combine(output,"motion-capture-job.json");
        if(File.Exists(receipt))
        {
            var previous=JObject.Parse(File.ReadAllText(receipt));
            if((string)previous["status"]!="PASS")results=(JArray)previous["results"];
        }
        var done=new HashSet<string>(results.OfType<JObject>().Select(r=>(string)r["id"]+"/"+(string)r["kind"]));
        tasks=new Queue<JObject>(((JArray)plan["tasks"]).OfType<JObject>().Where(t=>!done.Contains((string)t["id"]+"/"+(string)t["kind"])));
        Write("QUEUED");EditorApplication.update+=Tick;
        return "QUEUED "+tasks.Count+" isolated animation captures";
    }
    static void Write(string status,string error=null)
    {
        var value=new JObject{{"status",status},{"remaining",tasks?.Count??0},{"results",results.DeepClone()}};
        if(error!=null)value["error"]=error;
        File.WriteAllText(Path.Combine(output,"motion-capture-job.json"),value.ToString());
    }
    static void Tick()
    {
        if(EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)return;
        try
        {
            if(tasks.Count==0){Write("PASS");EditorApplication.update-=Tick;tasks=null;return;}
            var task=tasks.Dequeue();Write("RUNNING");results.Add(Capture(task));Write("RUNNING");
        }
        catch(Exception e){Write("FAIL",e.ToString());EditorApplication.update-=Tick;tasks=null;Debug.LogException(e);}
    }
    static JObject Capture(JObject task)
    {
        string cid=(string)task["id"],kind=(string)task["kind"];
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>((string)task["model"]);
        var clip=AssetDatabase.LoadAllAssetsAtPath((string)task["animation"]).OfType<AnimationClip>()
            .FirstOrDefault(c=>string.Equals(c.name,(string)task["clip"],StringComparison.OrdinalIgnoreCase));
        if(prefab==null || clip==null)throw new InvalidOperationException("Source missing: "+cid+" "+kind);
        var scene=EditorSceneManager.NewPreviewScene();
        GameObject model=null;RenderTexture rt=null;Texture2D frame=null,sheet=null;
        var skins=new List<CpuSkin>();var materials=new List<Material>();var graph=default(PlayableGraph);var oldActive=RenderTexture.active;
        try
        {
            model=Object.Instantiate(prefab);SceneManager.MoveGameObjectToScene(model,scene);
            model.name="Motion preview "+cid;model.transform.position=Vector3.zero;
            foreach(var component in model.GetComponentsInChildren<MonoBehaviour>(true))component.enabled=false;
            foreach(var a in model.GetComponentsInChildren<Animation>(true))a.enabled=false;
            foreach(var r in model.GetComponentsInChildren<Renderer>(true))
            {
                r.gameObject.layer=Layer;
                if(r is SkinnedMeshRenderer skin)skin.updateWhenOffscreen=true;
                if(cid=="death-skull" && r.sharedMaterials.Any(m=>m==null) && r.name.StartsWith("GLOWING_EYE_")){r.enabled=false;continue;}
                r.sharedMaterials=r.sharedMaterials.Select(m=>Convert(m,materials)).ToArray();
            }
            var animator=model.GetComponentInChildren<Animator>(true)??model.AddComponent<Animator>();
            animator.enabled=true;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.applyRootMotion=false;
            graph=PlayableGraph.Create("Upcoming motion preview");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable=AnimationClipPlayable.Create(graph,clip);playable.SetApplyFootIK(false);playable.SetApplyPlayableIK(false);
            var animationOutput=AnimationPlayableOutput.Create(graph,"Clip",animator);animationOutput.SetSourcePlayable(playable);graph.Play();
            Action<float> pose=t=>{playable.SetTime(Mathf.Min(t,Mathf.Max(0,clip.length-.0001f)));graph.Evaluate(0);};
            int n=Mathf.Clamp(Mathf.CeilToInt(clip.length*18),24,48),rows=(n+Cols-1)/Cols;
            Bounds bounds=default;bool hasBounds=false;
            for(int f=0;f<n;f+=3){pose(clip.length*f/(n-1));var b=UpcomingMonsterThemeReviewSizing.GeometryBounds(model);if(!hasBounds){bounds=b;hasBounds=true;}else bounds.Encapsulate(b);}
            pose(clip.length);bounds.Encapsulate(UpcomingMonsterThemeReviewSizing.GeometryBounds(model));
            var cameraObject=new GameObject("Isolated motion camera");SceneManager.MoveGameObjectToScene(cameraObject,scene);
            var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;camera.cullingMask=1<<Layer;
            camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.25f,.28f,.33f);
            Vector3 direction=new Vector3(.75f,.48f,.65f).normalized;float distance=bounds.extents.magnitude*3+3;
            camera.transform.position=bounds.center+direction*distance;camera.transform.LookAt(bounds.center);
            float half=.1f;
            for(int c=0;c<8;c++){var corner=bounds.center+Vector3.Scale(bounds.extents,new Vector3((c&1)==0?-1:1,(c&2)==0?-1:1,(c&4)==0?-1:1));var v=camera.transform.InverseTransformPoint(corner);half=Mathf.Max(half,Mathf.Abs(v.x),Mathf.Abs(v.y));}
            camera.orthographicSize=half*1.12f;camera.nearClipPlane=.01f;camera.farClipPlane=distance*3+bounds.size.magnitude;
            Light(scene,"Key",new Vector3(35,-125,0),1.4f,Color.white);
            Light(scene,"Rim",new Vector3(35,45,0),.8f,new Color(.7f,.8f,1));
            Light(scene,"Fill",new Vector3(10,-40,0),.65f,Color.white);
            rt=RenderTexture.GetTemporary(Cell,Cell,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            frame=new Texture2D(Cell,Cell,TextureFormat.RGB24,false);sheet=new Texture2D(Cell*Cols,Cell*rows,TextureFormat.RGB24,false);
            // The GPU skinning buffer is updated once per Editor frame. CPU snapshots
            // capture every sampled pose even when all samples share one Editor tick.
            foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if(skin.enabled && skin.gameObject.activeInHierarchy && skin.sharedMesh!=null)
                    skins.Add(new CpuSkin(skin));
            double shapeRms=0;float jointDegrees=0;
            for(int f=0;f<n;f++)
            {
                pose(clip.length*f/(n-1));
                foreach(var skin in skins)
                {
                    skin.UpdatePose();
                    shapeRms=Math.Max(shapeRms,skin.ShapeRms);
                    jointDegrees=Mathf.Max(jointDegrees,skin.JointDegrees);
                }
                var request=new UniversalRenderPipeline.SingleCameraRequest{destination=rt};
                if(RenderPipeline.SupportsRenderRequest(camera,request))RenderPipeline.SubmitRenderRequest(camera,request);
                else {camera.targetTexture=rt;camera.Render();}
                RenderTexture.active=rt;frame.ReadPixels(new Rect(0,0,Cell,Cell),0,0);frame.Apply();
                sheet.SetPixels((f%Cols)*Cell,(rows-1-f/Cols)*Cell,Cell,Cell,frame.GetPixels());
            }
            sheet.Apply();string path=Path.Combine(output,"motion-strips",cid+"__"+kind+".png");File.WriteAllBytes(path,sheet.EncodeToPNG());
            return new JObject{{"id",cid},{"kind",kind},{"clip",clip.name},{"sec",clip.length},{"frames",n},{"cols",Cols},{"cell",Cell},{"strip",path},{"cpuSkinned",true},{"shapeRms",shapeRms},{"jointDegrees",jointDegrees},{"skinCount",skins.Count},{"blendShapeCount",skins.Sum(s=>s.Source.sharedMesh.blendShapeCount)},{"human",clip.isHumanMotion},{"animation",task["animation"]},{"source",task["model"]}};
        }
        finally
        {
            foreach(var skin in skins)skin.Dispose();
            if(graph.IsValid())graph.Destroy();RenderTexture.active=oldActive;
            if(rt!=null)RenderTexture.ReleaseTemporary(rt);if(frame!=null)Object.DestroyImmediate(frame);if(sheet!=null)Object.DestroyImmediate(sheet);
            EditorSceneManager.ClosePreviewScene(scene);foreach(var m in materials)if(m!=null)Object.DestroyImmediate(m);
        }
    }

    // Linear blend skinning in renderer-local coordinates, matching bone world
    // matrices and bind poses without multiplying legacy FBX scale twice.
    sealed class CpuSkin : IDisposable
    {
        public readonly SkinnedMeshRenderer Source;
        readonly GameObject holder;readonly Mesh mesh;readonly Transform[] bones;
        readonly Matrix4x4[] bindposes;readonly Vector3[] vertices,normals,posed,posedNormals;
        readonly Vector4[] tangents,posedTangents;readonly byte[] counts;readonly BoneWeight1[] weights;
        readonly Quaternion[] initialRotations;Vector3[] initialShape;
        public double ShapeRms {get;private set;}public float JointDegrees {get;private set;}
        public CpuSkin(SkinnedMeshRenderer source)
        {
            Source=source;var shared=source.sharedMesh;bones=source.bones;bindposes=shared.bindposes;
            if(bones.Length!=bindposes.Length)throw new InvalidOperationException("Bone/bind pose mismatch: "+source.name);
            vertices=shared.vertices;normals=shared.normals;tangents=shared.tangents;
            posed=new Vector3[vertices.Length];posedNormals=new Vector3[normals.Length];posedTangents=new Vector4[tangents.Length];
            var nativeCounts=shared.GetBonesPerVertex();var nativeWeights=shared.GetAllBoneWeights();
            try{counts=nativeCounts.ToArray();weights=nativeWeights.ToArray();}
            finally{nativeCounts.Dispose();nativeWeights.Dispose();}
            initialRotations=bones.Select(b=>b==null?Quaternion.identity:b.localRotation).ToArray();
            mesh=Object.Instantiate(shared);mesh.name="CPU sampled pose";mesh.MarkDynamic();
            holder=new GameObject("CPU pose "+source.name,typeof(MeshFilter),typeof(MeshRenderer));
            holder.layer=Layer;holder.transform.SetParent(source.transform,false);
            holder.GetComponent<MeshFilter>().sharedMesh=mesh;
            holder.GetComponent<MeshRenderer>().sharedMaterials=source.sharedMaterials;
            source.enabled=false;
        }
        public void UpdatePose()
        {
            var matrices=new Matrix4x4[bones.Length];var normalMatrices=new Matrix4x4[bones.Length];
            for(int b=0;b<bones.Length;b++)
            {
                matrices[b]=bones[b]==null?Matrix4x4.identity:Source.transform.worldToLocalMatrix*bones[b].localToWorldMatrix*bindposes[b];
                normalMatrices[b]=matrices[b].inverse.transpose;
                if(bones[b]!=null && bones[b]!=Source.rootBone)
                    JointDegrees=Mathf.Max(JointDegrees,Quaternion.Angle(initialRotations[b],bones[b].localRotation));
            }
            int cursor=0;var reference=(Source.rootBone??Source.transform).worldToLocalMatrix*Source.transform.localToWorldMatrix;
            var shape=new Vector3[vertices.Length];double square=0;
            for(int v=0;v<vertices.Length;v++)
            {
                Vector3 p=Vector3.zero,n=Vector3.zero,t=Vector3.zero;
                for(int j=0;j<counts[v];j++)
                {
                    var weight=weights[cursor++];var matrix=matrices[weight.boneIndex];
                    p+=matrix.MultiplyPoint3x4(vertices[v])*weight.weight;
                    if(normals.Length==vertices.Length)n+=normalMatrices[weight.boneIndex].MultiplyVector(normals[v])*weight.weight;
                    if(tangents.Length==vertices.Length)t+=matrix.MultiplyVector((Vector3)tangents[v])*weight.weight;
                }
                if(counts[v]==0){p=vertices[v];if(normals.Length==vertices.Length)n=normals[v];if(tangents.Length==vertices.Length)t=tangents[v];}
                posed[v]=p;if(normals.Length==vertices.Length)posedNormals[v]=n.normalized;
                if(tangents.Length==vertices.Length){t.Normalize();posedTangents[v]=new Vector4(t.x,t.y,t.z,tangents[v].w);}
                shape[v]=reference.MultiplyPoint3x4(p);
                if(initialShape!=null)square+=(shape[v]-initialShape[v]).sqrMagnitude;
            }
            if(initialShape==null)initialShape=shape;
            else ShapeRms=Math.Max(ShapeRms,Math.Sqrt(square/Math.Max(1,vertices.Length)));
            mesh.vertices=posed;if(normals.Length==vertices.Length)mesh.normals=posedNormals;else mesh.RecalculateNormals();
            if(tangents.Length==vertices.Length)mesh.tangents=posedTangents;mesh.RecalculateBounds();
            Source.enabled=false;holder.SetActive(Source.gameObject.activeInHierarchy);
        }
        public void Dispose(){Object.DestroyImmediate(holder);Object.DestroyImmediate(mesh);}
    }

    static Material Convert(Material source,List<Material> owned)
    {
        if(source==null)throw new InvalidOperationException("Missing source material.");
        if(source.shader!=null && source.shader.name.StartsWith("Universal Render Pipeline/"))return source;
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source,out string guid,out long file);
        var existing=AssetDatabase.LoadAssetAtPath<Material>(UpcomingMonsterThemeReviewBuilder.MaterialRoot+"/Source_"+guid+"_"+Math.Abs(file)+".mat");
        if(existing!=null)return existing;
        var result=new Material(Shader.Find("Universal Render Pipeline/Lit"));owned.Add(result);
        string colorMap=source.HasProperty("_BaseMap")?"_BaseMap":source.HasProperty("_BaseColorMap")?"_BaseColorMap":"_MainTex";
        if(source.HasProperty(colorMap))result.SetTexture("_BaseMap",source.GetTexture(colorMap));
        result.SetColor("_BaseColor",source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):source.HasProperty("_Color")?source.GetColor("_Color"):Color.white);
        foreach(string p in new[]{"_BumpMap","_MetallicGlossMap","_OcclusionMap","_EmissionMap"})if(source.HasProperty(p))result.SetTexture(p,source.GetTexture(p));
        if(result.GetTexture("_BumpMap")!=null)result.EnableKeyword("_NORMALMAP");
        result.SetFloat("_Smoothness",.28f);result.SetFloat("_Cull",0);
        if(source.HasProperty("_Mode") && source.GetFloat("_Mode")==1){result.SetFloat("_AlphaClip",1);result.SetFloat("_Cutoff",.5f);result.EnableKeyword("_ALPHATEST_ON");}
        return result;
    }
    static void Light(Scene scene,string name,Vector3 angles,float power,Color color)
    {
        var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);go.transform.rotation=Quaternion.Euler(angles);
        var light=go.AddComponent<Light>();light.type=LightType.Directional;light.intensity=power;light.color=color;light.cullingMask=1<<Layer;light.shadows=LightShadows.None;
    }
}
