using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

// Original six strips, point counts and material properties, one procedural submission per prefab.
// CPU advances only random shape parameters; the GPU constructs the camera-facing ribbons.
public sealed class ChainElectricityBatchRenderer : MonoBehaviour
{
    private static readonly Unity.Profiling.ProfilerMarker UpdateMarker = new Unity.Profiling.ProfilerMarker("Overburst.Chain.Batch.Update");
    // Unsupported profiles/devices retain the original pooled renderer.
    public static bool Enabled { get; set; } = true;
    private static ChainElectricityBatchRenderer instance;
    private readonly Dictionary<GameObject, Batch> batches = new Dictionary<GameObject, Batch>();
    private uint seedCounter = 0x9e3779b9u;
    public static int ActiveLinks { get; private set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; Enabled = true; ActiveLinks = 0; }

    public static bool TrySpawn(GameObject prefab, Vector3 from, Vector3 to)
    {
        if (!Enabled || !Application.isPlaying || prefab == null || !SystemInfo.supportsComputeShaders) return false;
        if (instance == null)
        {
            var g = new GameObject("ChainElectricityBatchRenderer"); DontDestroyOnLoad(g);
            instance = g.AddComponent<ChainElectricityBatchRenderer>();
        }
        if (!instance.batches.TryGetValue(prefab, out Batch batch))
        {
            var original = prefab.GetComponentInChildren<ChainElectricityLiteVfxController>(true);
            batch = Batch.Create(original);
            instance.batches.Add(prefab, batch);
        }
        if (batch == null) return false;
        return batch.Add(from, to, Time.time, ++instance.seedCounter);
    }
    void LateUpdate()
    {
        using var measurement = UpdateMarker.Auto();
        ActiveLinks = 0;
        foreach (var pair in batches)
        {
            if (pair.Value == null) continue;
            pair.Value.AdvanceAndDraw(Time.time);
            ActiveLinks += pair.Value.Count;
        }
    }
    void OnDestroy()
    {
        foreach (var pair in batches) pair.Value?.Dispose();
        batches.Clear();
        if (instance == this) { instance = null; ActiveLinks = 0; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Link
    {
        public Vector4 CenterTime, Right, Up, Forward, Phases, Branch0, Branch1, BranchPhases;
    }
    private sealed class Batch : IDisposable
    {
        public int Count { get; private set; }
        private Link[] links = new Link[256];
        private uint[] random = new uint[256];
        private float[] nextShape = new float[256];
        private ComputeBuffer linkBuffer, profileBuffer, segmentBuffer;
        private bool dirty;
        private Material material;
        private readonly float lifetime, refresh, mainAmplitude, branchAmplitude;
        private readonly Vector3 prefabScale;
        private readonly int segmentCount;
        private readonly int renderLayer;
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private Batch(ChainElectricityLiteVfxController source, Shader shader)
        {
            lifetime = source.Lifetime; refresh = Mathf.Max(.01f,source.ShapeRefreshInterval);
            mainAmplitude = source.MainAmplitude; branchAmplitude = source.BranchAmplitude;
            prefabScale = source.transform.lossyScale;
            renderLayer = source.transform.root.gameObject.layer;
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            Material original = source.GetBatchLine(0).sharedMaterial;
            material.SetColor("_TintColor", original.GetColor("_TintColor"));
            material.SetFloat("_Intensity", original.GetFloat("_Intensity"));
            material.SetFloat("_EdgePower", original.GetFloat("_EdgePower"));
            // Two float4s per authored point: color, then width/t/unused/unused.
            var profile = new List<Vector4>(220);
            var segments = new List<Vector4>(104);
            for(int line=0;line<6;line++)
            {
                var renderer=source.GetBatchLine(line);
                int points=line<2?33:11, offset=profile.Count/2;
                for(int p=0;p<points;p++)
                {
                    float t=p/(float)(points-1);
                    Color color=renderer.colorGradient.Evaluate(t);
                    profile.Add(QualitySettings.activeColorSpace==ColorSpace.Linear?color.linear:color);
                    profile.Add(new Vector4(renderer.widthCurve.Evaluate(t)*renderer.widthMultiplier,t,0,0));
                    if(p<points-1) segments.Add(new Vector4(line,p,offset+p,points));
                }
            }
            segmentCount=segments.Count;
            profileBuffer=new ComputeBuffer(profile.Count,16);profileBuffer.SetData(profile);
            segmentBuffer=new ComputeBuffer(segments.Count,16);segmentBuffer.SetData(segments);
            linkBuffer=new ComputeBuffer(links.Length,Marshal.SizeOf<Link>());
            properties.SetBuffer("_Links",linkBuffer);properties.SetBuffer("_Profiles",profileBuffer);
            properties.SetBuffer("_Segments",segmentBuffer);properties.SetFloat("_Lifetime",lifetime);
            properties.SetFloat("_MainAmplitude",mainAmplitude);properties.SetFloat("_BranchAmplitude",branchAmplitude);
        }
        public static Batch Create(ChainElectricityLiteVfxController source)
        {
            if(source==null||source.MainPointCount!=33||source.BranchPointCount!=11||source.BranchCount!=2) return null;
            for(Transform t=source.transform;t!=null;t=t.parent)
                if(t.localPosition!=Vector3.zero||t.localRotation!=Quaternion.identity)return null;
            Material shared=null;
            for(int i=0;i<6;i++)
            {
                var line=source.GetBatchLine(i);
                if(line==null||line.useWorldSpace||line.alignment!=LineAlignment.View||line.textureMode!=LineTextureMode.Stretch) return null;
                if(line.transform.localPosition!=Vector3.zero||line.transform.localRotation!=Quaternion.identity||line.transform.localScale!=Vector3.one) return null;
                if(i==0) shared=line.sharedMaterial;
                if(line.sharedMaterial!=shared) return null;
            }
            if(shared==null||shared.shader.name!="OVERBURST/VFX/Chain Electricity Additive")return null;
            var shader=Resources.Load<Shader>("Combat/VFX/ChainElectricityBatch");
            return shader!=null&&shader.isSupported?new Batch(source,shader):null;
        }
        public bool Add(Vector3 from,Vector3 to,float now,uint seed)
        {
            Vector3 delta=to-from;float length=delta.magnitude;if(length<=.05f)return true;
            if(Count==links.Length)
            {
                int capacity=links.Length*2;Array.Resize(ref links,capacity);Array.Resize(ref random,capacity);Array.Resize(ref nextShape,capacity);
                linkBuffer.Dispose();linkBuffer=new ComputeBuffer(capacity,Marshal.SizeOf<Link>());properties.SetBuffer("_Links",linkBuffer);
            }
            int i=Count++;Quaternion rotation=Quaternion.LookRotation(delta/length,Vector3.up);
            Vector3 center=(from+to)*.5f;
            links[i]=new Link {CenterTime=new Vector4(center.x,center.y,center.z,now),
                Right=rotation*Vector3.right*prefabScale.x,Up=rotation*Vector3.up*prefabScale.y,
                Forward=rotation*Vector3.forward*(length*prefabScale.z)};
            random[i]=seed==0?0x9e3779b9u:seed;
            links[i].Phases.z=Next(ref random[i])*Mathf.PI*2;
            Rebuild(i);nextShape[i]=now+refresh;dirty=true;return true;
        }
        static float Next(ref uint state)
        {state^=state<<13;state^=state>>17;state^=state<<5;return(state&0x00ffffffu)/16777216f;}
        void Rebuild(int i)
        {
            ref Link link=ref links[i];ref uint state=ref random[i];
            link.Phases.x=Next(ref state)*Mathf.PI*2;link.Phases.y=Next(ref state)*Mathf.PI*2;
            for(int branch=0;branch<2;branch++)
            {
                float start=Mathf.Clamp01(.25f+branch*.38f+Mathf.Lerp(-.08f,.08f,Next(ref state)));
                int startIndex=Mathf.Clamp(Mathf.RoundToInt(start*32),1,31);
                float startZ=startIndex/32f-.5f;
                float direction=Next(ref state)<.2f?-1:1;
                float endZ=Mathf.Clamp(startZ+Mathf.Lerp(.11f,.22f,Next(ref state))*direction,-.48f,.48f);
                Vector2 radial=new Vector2(Mathf.Lerp(-1,1,Next(ref state)),Mathf.Lerp(-1,1,Next(ref state)));
                if(radial.sqrMagnitude<.1f)radial=Vector2.right;
                radial.Normalize();radial*=branchAmplitude*Mathf.Lerp(.72f,1.15f,Next(ref state));
                var values=new Vector4(startIndex/32f,endZ,radial.x,radial.y);
                float phase=Next(ref state)*Mathf.PI*2;
                if(branch==0){link.Branch0=values;link.BranchPhases.x=phase;}
                else{link.Branch1=values;link.BranchPhases.y=phase;}
            }
        }
        public void AdvanceAndDraw(float now) { Advance(now); Draw(now); }
        public void Advance(float now)
        {

            for(int i=Count-1;i>=0;i--)
            {
                if(now-links[i].CenterTime.w>=lifetime)
                {
                    int last=--Count;links[i]=links[last];random[i]=random[last];nextShape[i]=nextShape[last];dirty=true;continue;
                }
                if(now>=nextShape[i]){Rebuild(i);nextShape[i]+=refresh;dirty=true;}
            }
        }
        public void Draw(float now)
        {
            if(Count==0)return;
            // Small contiguous upload; unlike six LineRenderers per link, no per-strip native calls.
            if(dirty) { linkBuffer.SetData(links,0,0,Count); dirty=false; }
            Vector3 min=links[0].CenterTime,max=min;
            for(int i=0;i<Count;i++)
            {
                Vector3 center=links[i].CenterTime, forward=links[i].Forward;
                min=Vector3.Min(min,Vector3.Min(center-forward*.5f,center+forward*.5f));
                max=Vector3.Max(max,Vector3.Max(center-forward*.5f,center+forward*.5f));
            }
            Bounds bounds=new Bounds((min+max)*.5f,max-min+Vector3.one*4f);
            properties.SetFloat("_Now",now);
            Graphics.DrawProcedural(material,bounds,MeshTopology.Triangles,segmentCount*6,Count,null,properties,ShadowCastingMode.Off,false,renderLayer);
        }
        public void Dispose(){linkBuffer?.Dispose();profileBuffer?.Dispose();segmentBuffer?.Dispose();if(material!=null)UnityEngine.Object.Destroy(material);}
    }
}
