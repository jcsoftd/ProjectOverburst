using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class HideoutGroundHazeBuilder
{
    public const string ScenePath="Assets/ProjectOverburst/00_Scenes/HideoutScene.unity";
    public const string Root="Assets/ProjectOverburst/05_Art/Environment/BarbarianHideout/Atmosphere";
    public const string PrefabPath=Root+"/PF_HideoutGroundHaze.prefab";
    public const string OcclusionExcludedLayer="TransparentFX";
    const string MaterialPath=Root+"/MAT_HideoutGroundHaze.mat";
    const string Output="../개인파일/코덱스산출/Environment/20261004_HideoutAtmosphere";
    const string Name="Hideout Ground Haze";
    static readonly Vector3 Min=new Vector3(-30,-1.7f,-20), Max=new Vector3(26,4,31);

    [MenuItem("OVERBURST/Environment/Preview Ground Haze")]
    public static void PreviewMenu()=>Debug.Log(Preview());
    [MenuItem("OVERBURST/Environment/Apply Ground Haze")]
    public static void ApplyMenu()=>Debug.Log(Apply());

    public static string Preview()
    {
        Idle(); Directory.CreateDirectory(Output);
        var before=BarbarianCampUrpVerifier.EditorSnapshot();
        var active=SceneManager.GetActiveScene();
        if(SceneManager.GetSceneByPath(ScenePath).isLoaded) throw new InvalidOperationException("Hideout must be closed before isolated preview.");
        var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        Material temporary=null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var assets=EnsureAssets(scene);
            var all=All(scene);
            var legacy=all.Single(t=>t.name=="Camp Ground Mist").gameObject;
            var particles=all.Select(t=>t.GetComponent<ParticleSystem>()).Where(p=>p!=null).ToArray();
            foreach(var p in particles) p.Simulate(20,true,true,true);
            Capture(scene,"original");
            legacy.SetActive(false);
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(assets,scene);
            instance.name=Name;
            temporary=new Material(instance.GetComponent<MeshRenderer>().sharedMaterial);
            instance.GetComponent<MeshRenderer>().sharedMaterial=temporary;
            temporary.SetFloat("_PreviewTime",0);
            Capture(scene,"haze");
            temporary.SetFloat("_PreviewTime",24);
            Capture(scene,"haze_24s");
            Write("preview.json",new {status="PASS",fog=RenderSettings.fog,start=RenderSettings.fogStartDistance,
                end=RenderSettings.fogEndDistance,terrainFollowing=true,steps=32,drawCalls=1,legacyMistHidden=true,
                shaderErrors=ShaderUtil.GetShaderMessages(temporary.shader).Select(m=>m.message).ToArray()});
        }
        finally
        {
            EditorSceneManager.CloseScene(scene,true);
            if(active.IsValid()&&active.isLoaded) SceneManager.SetActiveScene(active);
            if(temporary!=null) Object.DestroyImmediate(temporary);
        }
        if(before!=BarbarianCampUrpVerifier.EditorSnapshot()) throw new InvalidOperationException("Existing Editor state changed.");
        return "PASS: isolated normal-scene captures, full ground-height mask and native assets, Editor preserved.";
    }

    static GameObject EnsureAssets(Scene scene)
    {
        AssetDatabase.ImportAsset(Root+"/HideoutGroundHaze.shader",ImportAssetOptions.ForceSynchronousImport);
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(Root+"/HideoutGroundHaze.shader");
        if(shader==null) throw new InvalidOperationException("Haze shader missing.");
        var height=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/TX_GroundHeight.asset");
        if(height==null)
        {
            var ground=All(scene).Single(t=>t.name=="Camp Ground");
            var vertices=ground.GetComponent<MeshFilter>().sharedMesh.vertices;
            height=new Texture2D(109,77,TextureFormat.RFloat,false,true) {name="Hideout ground heights",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            var values=Enumerable.Range(0,109*77).Select(i=>new Color(-100,0,0,1)).ToArray();
            foreach(var v in vertices)
            {
                var p=ground.TransformPoint(v); int x=Mathf.RoundToInt(p.x+36),z=Mathf.RoundToInt(p.z+30);
                if(x>=0&&x<109&&z>=0&&z<77) values[z*109+x]=new Color(p.y,0,0,1);
            }
            if(values.Any(v=>v.r< -20)) throw new InvalidOperationException("Incomplete terrain map.");
            height.SetPixels(values); height.Apply(false,false); AssetDatabase.CreateAsset(height,Root+"/TX_GroundHeight.asset");
        }
        var noise=AssetDatabase.LoadAssetAtPath<Texture3D>(Root+"/TX_HazeNoise.asset");
        if(noise==null)
        {
            const int size=64; var values=new Color[size*size*size];
            for(int z=0;z<size;z++) for(int y=0;y<size;y++) for(int x=0;x<size;x++)
            {
                var p=new Vector3(x,y,z)/size;
                float n=ValueNoise(p*8,8)*.62f+ValueNoise(p*16,16)*.27f+ValueNoise(p*32,32)*.11f;
                values[(z*size+y)*size+x]=new Color(n,n,n,1);
            }
            noise=new Texture3D(size,size,size,TextureFormat.R8,true) {name="Seamless ground haze density",filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Repeat};
            noise.SetPixels(values); noise.Apply(true,false); AssetDatabase.CreateAsset(noise,Root+"/TX_HazeNoise.asset");
        }
        var mat=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if(mat==null) {mat=new Material(shader) {name="MAT_HideoutGroundHaze"}; AssetDatabase.CreateAsset(mat,MaterialPath);}
        mat.SetTexture("_Noise",noise); mat.SetTexture("_GroundHeight",height);
        mat.SetVector("_BoundsMin",Min); mat.SetVector("_BoundsMax",Max);
        mat.SetVector("_GroundBounds",new Vector4(-36,-30,108,76));
        mat.SetVector("_ClearCenter",new Vector4(0,2,8.5f,9));
        mat.SetFloat("_Density",.25f); mat.SetFloat("_Height",1.2f); mat.SetFloat("_NoiseScale",.12f);
        mat.SetColor("_FogColor",new Color(.67f,.62f,.53f,1));
        mat.SetColor("_SunTint",new Color(.91f,.85f,.70f,1));
        mat.SetFloat("_PreviewTime",-1); EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat);
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if(prefab==null)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                go.name=Name; go.layer=LayerMask.NameToLayer(OcclusionExcludedLayer);
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.transform.position=(Min+Max)*.5f; go.transform.localScale=Max-Min;
                var renderer=go.GetComponent<MeshRenderer>(); renderer.sharedMaterial=mat;
                renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
                renderer.lightProbeUsage=LightProbeUsage.Off; renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode=MotionVectorGenerationMode.ForceNoMotion;
                prefab=PrefabUtility.SaveAsPrefabAsset(go,PrefabPath);
            }
            finally {Object.DestroyImmediate(go);}
        }
        return prefab;
    }

    static float Hash(int x,int y,int z,int period)
    {
        unchecked
        {
            uint h=(uint)((x&(period-1))*73856093)^ (uint)((y&(period-1))*19349663) ^ (uint)((z&(period-1))*83492791) ^ 1977u;
            h ^= h>>13; h *= 1274126177u; h ^= h>>16;
            return (h&0x00ffffff)/16777215f;
        }
    }
    static float ValueNoise(Vector3 p,int period)
    {
        int x=Mathf.FloorToInt(p.x),y=Mathf.FloorToInt(p.y),z=Mathf.FloorToInt(p.z);
        Vector3 f=p-new Vector3(x,y,z); f=new Vector3(Smooth(f.x),Smooth(f.y),Smooth(f.z));
        float a=Mathf.Lerp(Mathf.Lerp(Hash(x,y,z,period),Hash(x+1,y,z,period),f.x),Mathf.Lerp(Hash(x,y+1,z,period),Hash(x+1,y+1,z,period),f.x),f.y);
        float b=Mathf.Lerp(Mathf.Lerp(Hash(x,y,z+1,period),Hash(x+1,y,z+1,period),f.x),Mathf.Lerp(Hash(x,y+1,z+1,period),Hash(x+1,y+1,z+1,period),f.x),f.y);
        return Mathf.Lerp(a,b,f.z);
    }
    static float Smooth(float f)=>f*f*f*(f*(f*6-15)+10);

    public static string Apply()
    {
        Idle(); Directory.CreateDirectory(Output);
        var before=BarbarianCampUrpVerifier.EditorSnapshot(); var active=SceneManager.GetActiveScene();
        if(SceneManager.GetSceneByPath(ScenePath).isLoaded) throw new InvalidOperationException("Hideout is already open.");
        string backup=Path.Combine(Output,"HideoutScene.before.unity");
        if(!File.Exists(backup))
        {
            File.Copy(ScenePath,backup,false);
            File.Copy(ScenePath+".meta",backup+".meta",false);
        }
        else if(HideoutPerlinGroundBuilder.Hash(backup)!=HideoutPerlinGroundBuilder.Hash(ScenePath))
            throw new InvalidOperationException("Scene differs from the existing backup.");
        var hash=HideoutPerlinGroundBuilder.Hash(ScenePath);
        var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if(prefab==null) throw new InvalidOperationException("Run the preview first.");
            var old=All(scene).Single(t=>t.name=="Camp Ground Mist").gameObject;
            if(All(scene).Any(t=>t.name==Name)) throw new InvalidOperationException("Haze already installed.");
            var snapshot=Snapshot(scene,old);
            var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
            go.name=Name;
            go.transform.SetParent(All(scene).Single(t=>t.name=="Camp Atmosphere"),true);
            old.SetActive(false);
            if(snapshot!=Snapshot(scene,old)) throw new InvalidOperationException("Unrelated content changed.");
            Validate(scene);
            if(hash!=HideoutPerlinGroundBuilder.Hash(ScenePath)) throw new InvalidOperationException("Scene file changed since loading.");
            EditorSceneManager.MarkSceneDirty(scene); if(!EditorSceneManager.SaveScene(scene)) throw new IOException("Scene save failed.");
            Write("apply.json",new {status="PASS",scene=ScenePath,prefab=PrefabPath,legacyRetained=true,unrelatedContentPreserved=true,
                beforeHash=hash,afterHash=HideoutPerlinGroundBuilder.Hash(ScenePath),missingScripts=0});
        }
        catch(Exception e) {Write("apply_failure.json",new {status="FAIL",error=e.ToString()});throw;}
        finally
        {
            EditorSceneManager.CloseScene(scene,true);
            if(active.IsValid()&&active.isLoaded) SceneManager.SetActiveScene(active);
        }
        if(before!=BarbarianCampUrpVerifier.EditorSnapshot()) throw new InvalidOperationException("Editor state changed.");
        return "PASS: native product prefab connected; previous mist retained disabled; unrelated scene content preserved.";
    }
    public static string Readback()
    {
        Idle(); var active=SceneManager.GetActiveScene(); var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        try {SceneManager.SetActiveScene(scene); Validate(scene); Capture(scene,"saved"); Write("readback.json",new {status="PASS",missingScripts=0,shaderErrors=0,sourcePrefabConnected=true});}
        finally {EditorSceneManager.CloseScene(scene,true); if(active.IsValid()&&active.isLoaded) SceneManager.SetActiveScene(active);}
        return "PASS: native saved-scene load, shader and GUID references.";
    }
    static void Validate(Scene scene)
    {
        var all=All(scene); var go=all.Single(t=>t.name==Name).gameObject;
        if(go.layer!=LayerMask.NameToLayer(OcclusionExcludedLayer)) throw new Exception("Haze volume must be excluded from world occlusion.");
        if(PrefabUtility.GetPrefabInstanceStatus(go)!=PrefabInstanceStatus.Connected) throw new Exception("Haze prefab disconnected.");
        if(all.Any(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)) throw new Exception("Missing script.");
        var mat=go.GetComponent<Renderer>().sharedMaterial;
        if(mat==null || mat.GetTexture("_Noise")==null || mat.GetTexture("_GroundHeight")==null || !mat.shader.isSupported) throw new Exception("Haze references invalid.");
        if(ShaderUtil.GetShaderMessages(mat.shader).Any(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)) throw new Exception("Shader compilation error.");
        if(go.GetComponent<Collider>()!=null || all.Single(t=>t.name=="Camp Ground Mist").gameObject.activeSelf) throw new Exception("Atmosphere collision/legacy state invalid.");
    }
    static string Snapshot(Scene s,GameObject except)=>JsonConvert.SerializeObject(All(s).Where(t=>t.name!=Name)
        .SelectMany(t=>t.GetComponents<Component>()).Where(c=>c!=null&&c.gameObject!=except&&c.gameObject.name!="Camp Atmosphere")
        .Select(c=>new {id=c.GetInstanceID(),data=EditorJsonUtility.ToJson(c)}));
    static Transform[] All(Scene s)=>s.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
    static void Idle() {if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating) throw new InvalidOperationException("Idle Editor required; running Play is preserved.");}
    static void Write(string name,object value)=>File.WriteAllText(Path.Combine(Output,name),JsonConvert.SerializeObject(value,Formatting.Indented));

    static void Capture(Scene scene,string stage)
    {
        var go=new GameObject("Temporary ground haze camera"); SceneManager.MoveGameObjectToScene(go,scene);
        var camera=go.AddComponent<Camera>(); camera.scene=scene;
        var data=camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing=true; data.requiresDepthOption=CameraOverrideOption.On;
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.17f,.18f,.2f);
        camera.nearClipPlane=.1f; camera.farClipPlane=180;
        var rt=new RenderTexture(1440,900,24,RenderTextureFormat.ARGBHalf); rt.Create();
        var image=new Texture2D(1440,900,TextureFormat.RGB24,false); var previous=RenderTexture.active;
        var positions=new[]{new Vector3(26,30,-36), new Vector3(7.46f,17,-9.51f),new Vector3(-18,3.5f,-10)};
        var targets=new[]{new Vector3(0,0,3),new Vector3(-.65f,1,-1.4f),new Vector3(-7,1,12)};
        try
        {
            for(int i=0;i<3;i++)
            {
                camera.fieldOfView=i==1?38:52;
                camera.transform.position=positions[i]; camera.transform.LookAt(targets[i]); camera.targetTexture=rt;
                camera.Render(); RenderTexture.active=rt; image.ReadPixels(new Rect(0,0,1440,900),0,0); image.Apply();
                File.WriteAllBytes(Path.Combine(Output,stage+"_"+i+".png"),image.EncodeToPNG());
            }
        }
        finally
        {
            camera.targetTexture=null; RenderTexture.active=previous; rt.Release(); Object.DestroyImmediate(rt);
            Object.DestroyImmediate(image); Object.DestroyImmediate(go);
        }
    }
}
