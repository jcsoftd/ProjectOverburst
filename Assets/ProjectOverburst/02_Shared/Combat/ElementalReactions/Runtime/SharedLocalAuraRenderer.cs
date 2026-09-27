using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

// Shared native simulation for compatible attached billboards. World-space particles stay native.
// Kept opt-in until moving-crowd visual/performance validation is complete.
[DefaultExecutionOrder(9500)]
public sealed class SharedLocalAuraRenderer : MonoBehaviour
{
    public static bool Enabled;
    const int Variations=8, MaximumSamples=128;
    static SharedLocalAuraRenderer instance;
    static readonly ProfilerMarker RenderMarker=new ProfilerMarker("Overburst.SharedLocalAura.Build");
    sealed class Sample
    {
        public ParticleSystem System;public ParticleSystemRenderer Renderer;public Material Material;public string Kind;
        public int Frame,Variant,Users;public Mesh Mesh=new Mesh();
        public readonly List<Vector3> Positions=new List<Vector3>(256),Normals=new List<Vector3>(256);
        public readonly List<Color32> Colors=new List<Color32>(256);
        public readonly List<Vector4> Uv=new List<Vector4>(256);
        public readonly List<int> Indices=new List<int>(384);
    }
    sealed class Owner { public ParticleSystem Source;public ParticleSystemRenderer Renderer;public Sample Sample;public bool RendererEnabled; }
    sealed class Batch
    {
        public Material Material;public Mesh Mesh=new Mesh{indexFormat=IndexFormat.UInt32};
        public readonly List<Vector3> Positions=new List<Vector3>(16384),Normals=new List<Vector3>(16384);
        public readonly List<Color32> Colors=new List<Color32>(16384);
        public readonly List<Vector4> Uv=new List<Vector4>(16384);
        public readonly List<int> Indices=new List<int>(24576);
        public void Clear(){Positions.Clear();Normals.Clear();Colors.Clear();Uv.Clear();Indices.Clear();}
    }
    sealed class DepthOrder:IComparer<Owner>
    {
        public Vector3 CameraPosition;
        public int Compare(Owner a,Owner b)
        {
            float da=(a.Source.transform.position-CameraPosition).sqrMagnitude,db=(b.Source.transform.position-CameraPosition).sqrMagnitude;
            int order=db.CompareTo(da);return order!=0?order:a.Source.GetInstanceID().CompareTo(b.Source.GetInstanceID());
        }
    }
    readonly List<Sample> samples=new List<Sample>(32);
    readonly List<Owner> owners=new List<Owner>(256);
    readonly Dictionary<ParticleSystem,Owner> lookup=new Dictionary<ParticleSystem,Owner>(256);
    readonly Dictionary<Material,Batch> batches=new Dictionary<Material,Batch>();
    readonly DepthOrder depthOrder=new DepthOrder();
    public static int OwnerCount=>instance!=null?instance.owners.Count:0;
    public static int ActiveSampleCount {get{if(instance==null)return 0;int n=0;foreach(var s in instance.samples)if(s.Users>0)n++;return n;}}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics(){instance=null;Enabled=false;}
    static bool UniformScale(Transform t)
    {
        var s=t.lossyScale;return s.x>0&&Mathf.Abs(s.x-s.y)<.0001f&&Mathf.Abs(s.x-s.z)<.0001f;
    }
    public static bool TryRegister(ParticleSystem source)
    {
        var camera=Camera.main;
        if(!Enabled||!Application.isPlaying||source==null||camera==null||!camera.orthographic||!UniformScale(source.transform))return false;
        if(source.name!="Fire"&&source.name!="Lightning aura"&&source.name!="Smoke")return false;
        var main=source.main;var renderer=source.GetComponent<ParticleSystemRenderer>();
        if(main.simulationSpace!=ParticleSystemSimulationSpace.Local||main.startSize3D||main.startRotation3D||main.scalingMode!=ParticleSystemScalingMode.Hierarchy
            ||renderer==null||!renderer.enabled||renderer.renderMode!=ParticleSystemRenderMode.Billboard||renderer.alignment!=ParticleSystemRenderSpace.View
            ||source.trails.enabled||source.collision.enabled||source.subEmitters.enabled||source.transform.childCount!=0)return false;
        if(instance==null){var root=new GameObject("SharedLocalAuraRenderer");DontDestroyOnLoad(root);instance=root.AddComponent<SharedLocalAuraRenderer>();}
        return instance.Add(source,renderer);
    }
    bool Add(ParticleSystem source,ParticleSystemRenderer renderer)
    {
        if(lookup.ContainsKey(source))return true;
        int variant=(int)(unchecked((uint)source.GetInstanceID()*2654435761u)>>29);Sample sample=null,unused=null;
        foreach(var candidate in samples)
        {
            if(candidate.Kind!=source.name||candidate.Material!=renderer.sharedMaterial)continue;
            if(candidate.Users==0){unused=candidate;continue;}
            if(candidate.Frame==Time.frameCount&&candidate.Variant==variant){sample=candidate;break;}
        }
        if(sample==null)
        {
            sample=unused;
            if(sample==null)
            {
                if(samples.Count>=MaximumSamples)return false;
                var clone=Instantiate(source,transform);clone.name="Shared "+source.name;clone.transform.localPosition=Vector3.zero;clone.transform.localRotation=Quaternion.identity;clone.transform.localScale=Vector3.one;
                var cloneRenderer=clone.GetComponent<ParticleSystemRenderer>();cloneRenderer.enabled=false;
                var main=clone.main;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
                sample=new Sample{System=clone,Renderer=cloneRenderer,Material=renderer.sharedMaterial,Kind=source.name};sample.Mesh.MarkDynamic();samples.Add(sample);
            }
            sample.System.gameObject.SetActive(true);sample.System.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            sample.System.useAutoRandomSeed=false;sample.System.randomSeed=(uint)(17+variant*37+(Time.frameCount&0xffff));
            sample.Frame=Time.frameCount;sample.Variant=variant;sample.System.Play(false);
        }
        sample.Users++;var owner=new Owner{Source=source,Renderer=renderer,Sample=sample,RendererEnabled=renderer.enabled};owners.Add(owner);lookup.Add(source,owner);
        source.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);renderer.enabled=false;return true;
    }
    public static void Release(ParticleSystem source)
    {
        if(instance==null||source==null||!instance.lookup.TryGetValue(source,out var owner))return;
        instance.Remove(owner,false);
    }
    void Remove(Owner owner,bool resume)
    {
        lookup.Remove(owner.Source);owners.Remove(owner);
        if(owner.Renderer!=null)owner.Renderer.enabled=owner.RendererEnabled;
        if(resume&&owner.Source!=null&&owner.Source.gameObject.activeInHierarchy)owner.Source.Play(false);
        if(--owner.Sample.Users==0){owner.Sample.System.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);owner.Sample.System.gameObject.SetActive(false);}
    }
    void LateUpdate()
    {
        var camera=Camera.main;
        for(int i=owners.Count-1;i>=0;i--)
        {
            var o=owners[i];bool unsupported=!Enabled||camera==null||!camera.orthographic;
            if(o.Source==null||!o.Source.gameObject.activeInHierarchy)Remove(o,false);
            else if(unsupported||!UniformScale(o.Source.transform))Remove(o,true);
        }
        if(owners.Count==0)return;
        using(RenderMarker.Auto())
        {
            foreach(var batch in batches.Values)batch.Clear();
            foreach(var sample in samples)
            {
                if(sample.Users==0)continue;
                sample.Renderer.BakeMesh(sample.Mesh,camera,ParticleSystemBakeMeshOptions.Default);
                sample.Mesh.GetVertices(sample.Positions);sample.Mesh.GetNormals(sample.Normals);sample.Mesh.GetColors(sample.Colors);sample.Mesh.GetUVs(0,sample.Uv);sample.Mesh.GetIndices(sample.Indices,0);
            }
            depthOrder.CameraPosition=camera.transform.position;owners.Sort(depthOrder);
            foreach(var owner in owners)
            {
                var s=owner.Sample;if(s.Positions.Count==0)continue;
                if(!batches.TryGetValue(s.Material,out var batch)){batch=new Batch{Material=s.Material};batch.Mesh.MarkDynamic();batches.Add(s.Material,batch);}
                int offset=batch.Positions.Count;var t=owner.Source.transform;float scale=t.lossyScale.x;var pose=t.localToWorldMatrix;
                for(int q=0;q<s.Positions.Count;q+=4)
                {
                    Vector3 center=(s.Positions[q]+s.Positions[q+1]+s.Positions[q+2]+s.Positions[q+3])*.25f;
                    Vector3 worldCenter=pose.MultiplyPoint3x4(center);
                    for(int j=0;j<4;j++)
                    {
                        int v=q+j;batch.Positions.Add(worldCenter+(s.Positions[v]-center)*scale);
                        batch.Normals.Add(s.Normals[v]);batch.Colors.Add(s.Colors[v]);batch.Uv.Add(s.Uv[v]);
                    }
                }
                foreach(int index in s.Indices)batch.Indices.Add(offset+index);
            }
            foreach(var batch in batches.Values)
            {
                batch.Mesh.Clear();if(batch.Positions.Count==0)continue;
                batch.Mesh.SetVertices(batch.Positions);batch.Mesh.SetNormals(batch.Normals);batch.Mesh.SetColors(batch.Colors);batch.Mesh.SetUVs(0,batch.Uv);batch.Mesh.SetTriangles(batch.Indices,0,true);
                Graphics.DrawMesh(batch.Mesh,Matrix4x4.identity,batch.Material,0,camera,0,null,ShadowCastingMode.Off,false,null,LightProbeUsage.Off);
            }
        }
    }
    void OnDisable(){while(owners.Count>0)Remove(owners[owners.Count-1],true);}
    void OnDestroy()
    {
        while(owners.Count>0)Remove(owners[owners.Count-1],true);
        foreach(var s in samples)if(s.Mesh!=null)Destroy(s.Mesh);
        foreach(var b in batches.Values)if(b.Mesh!=null)Destroy(b.Mesh);
        if(instance==this)instance=null;
    }
}
