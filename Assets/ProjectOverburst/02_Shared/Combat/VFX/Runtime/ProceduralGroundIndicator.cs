using System.Collections.Generic;
using UnityEngine;

public enum GroundIndicatorShape { Sector, Circle, Donut, Rectangle }

/// <summary>원본 Telegraph 재질과 파티클 시계를 재사용한다. 미터 단위 범위 표시는 피해 판정과 독립이다.</summary>
[ExecuteAlways, DisallowMultipleComponent]
public sealed class ProceduralGroundIndicator : MonoBehaviour
{
    [SerializeField] private ParticleSystem effect, fill, border;
    [SerializeField] private GroundIndicatorShape shape = GroundIndicatorShape.Sector;
    [SerializeField, Min(.05f)] private float outerRadius = 4f;
    [SerializeField, Min(0f)] private float innerRadius = 1f;
    [SerializeField, Range(1f,360f)] private float angle = 80f;
    [SerializeField, Min(.05f)] private float width = 1f, length = 4f;
    [SerializeField, Min(.01f)] private float flameWidth = .12f;
    [SerializeField, Range(0f,1f)] private float progress = .72f;
    [SerializeField] private bool visible = true;
    private Mesh fillMesh, borderMesh, ambientMesh;
    private Vector4 lastGeometry;
    private Vector4 lastExtra;
    private GroundIndicatorShape lastShape;
    private float simulatedTime = -1f;
    private MaterialPropertyBlock ambientProperties;
    // 공급사 UV_Distorted_PolarCords의 반경 프로파일.
    private static readonly float[] RadialProfile = {0,.2350353f,.317614f,.431733f,.543918f,.669812f,.807134f,.890378f,.941745f,.975608f,1};
    public GroundIndicatorShape Shape => shape;
    public float OuterRadius => outerRadius;
    public float InnerRadius => shape == GroundIndicatorShape.Circle ? 0f : innerRadius;
    public float Angle => angle;
    public float FlameWidth => flameWidth;
    public float Progress => progress;
    public ParticleSystemRenderer Surface => fill == null ? null : fill.GetComponent<ParticleSystemRenderer>();
    public ParticleSystemRenderer Border => border == null ? null : border.GetComponent<ParticleSystemRenderer>();
    public bool IsVisible => isActiveAndEnabled && visible;

    public void Initialize(ParticleSystem root, ParticleSystem fillLayer, ParticleSystem borderLayer)
    {
        effect=root; fill=fillLayer; border=borderLayer;
        foreach(var p in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            p.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=p.main; main.playOnAwake=false; main.simulationSpeed=1f;
        }
        Refresh();
    }
    public void Configure(GroundIndicatorShape kind,float outer,float inner=0f,float degrees=360f,float rectangleWidth=1f,float rectangleLength=4f)
    {
        shape=kind;outerRadius=outer;innerRadius=inner;angle=degrees;width=rectangleWidth;length=rectangleLength;Refresh();
    }
    public void SetFlameWidth(float meters) { flameWidth=meters;Refresh(); }
    public void SetProgress(float value)
    {
        progress=Mathf.Clamp01(Finite(value,0));
        if(effect==null||!IsVisible)return;
        float target=progress*4.2f;
        if(Mathf.Abs(target-simulatedTime)<.0001f)return;
        bool restart=simulatedTime<0 || target<simulatedTime;
        effect.Simulate(restart?target:target-simulatedTime,true,restart,false);
        effect.Pause(true); simulatedTime=target;
    }
    public void SetVisible(bool value)
    {
        if(visible==value)return;
        visible=value;
        if(!value&&effect!=null){effect.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);simulatedTime=-1;}
        else {Refresh();SetProgress(progress);}
    }
    public void Refresh()
    {
        if(!gameObject.scene.IsValid())return;
        outerRadius=Mathf.Clamp(Finite(outerRadius,4),.05f,1000);
        innerRadius=Mathf.Clamp(Finite(innerRadius,0),0,outerRadius-.01f);
        angle=Mathf.Clamp(Finite(angle,80),1,360);
        width=Mathf.Clamp(Finite(width,1),.05f,1000);length=Mathf.Clamp(Finite(length,4),.05f,1000);
        flameWidth=Mathf.Clamp(Finite(flameWidth,.12f),.01f,Mathf.Min(outerRadius,10));
        if(fill==null||border==null)return;
        var geometry=new Vector4(outerRadius,InnerRadius,angle,width);
        var scale=transform.lossyScale;
        var extra=new Vector4(length,flameWidth,scale.x,scale.z);
        if(fillMesh!=null&&lastGeometry==geometry&&lastExtra==extra&&lastShape==shape)
        {
            // 프리팹 생성 중 OnEnable 이후 원본 Renderer/Transform 값이 복사될 수 있다.
            Surface.mesh=fillMesh;Border.mesh=borderMesh;ConfigureAmbient();return;
        }
        if(fillMesh==null)fillMesh=NewMesh("Indicator fill");
        if(borderMesh==null)borderMesh=NewMesh("Indicator original fire border");
        BuildMeshes(); Surface.mesh=fillMesh;Border.mesh=borderMesh;
        BuildAmbientMesh();
        lastGeometry=geometry;lastExtra=extra;lastShape=shape;
        ConfigureAmbient();simulatedTime=-1;SetProgress(progress);
    }
    private static Mesh NewMesh(string label)=>new Mesh{name=label,hideFlags=HideFlags.HideAndDontSave,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
    private void BuildMeshes()
    {
        var points=new List<Vector2>();
        var vertices=new List<Vector3>();var uvs=new List<Vector2>();var triangles=new List<int>();
        float inner=InnerRadius;
        bool closed=shape!=GroundIndicatorShape.Sector||angle>=359.9f;
        if(shape==GroundIndicatorShape.Rectangle)
        {
            points.Add(new Vector2(-width/2,0));points.Add(new Vector2(-width/2,length));
            points.Add(new Vector2(width/2,length));points.Add(new Vector2(width/2,0));
            vertices.AddRange(new[]{ToMesh(points[0]),ToMesh(points[1]),ToMesh(points[3]),ToMesh(points[2])});
            uvs.AddRange(new[]{Vector2.zero,Vector2.up,Vector2.right,Vector2.one});triangles.AddRange(new[]{0,1,2,2,1,3});
        }
        else
        {
            float degrees=closed?360:angle;
            // 곡선 오차 약 1mm, 최대 2048분할. 텍스처 해상도와 무관하게 반경을 계산한다.
            float step=Mathf.Min(1f,Mathf.Acos(Mathf.Clamp(1f-.001f/outerRadius,-1,1))*Mathf.Rad2Deg*2f);
            int segments=Mathf.Clamp(Mathf.CeilToInt(degrees/Mathf.Max(step,.05f)),2,2048);
            for(int s=0;s<=segments;s++)
            {
                float t=(float)s/segments, theta=(t-.5f)*degrees*Mathf.Deg2Rad;
                Vector2 direction=new Vector2(Mathf.Sin(theta),Mathf.Cos(theta));
                for(int ring=0;ring<11;ring++)
                {
                    vertices.Add(ToMesh(direction*Mathf.Lerp(inner,outerRadius,RadialProfile[ring])));uvs.Add(new Vector2(t,ring/10f));
                    if(s>0&&ring>0){int end=s*11+ring;triangles.AddRange(new[]{end-12,end-11,end,end-12,end,end-1});}
                }
                points.Add(direction*outerRadius);
            }
            if(inner>0)
            {
                if(closed)
                {
                    Write(fillMesh,vertices,uvs,triangles);
                    var bv=new List<Vector3>();var bu=new List<Vector2>();var bt=new List<int>();
                    AddBorderStrip(points,true,bv,bu,bt);
                    var hole=new List<Vector2>();
                    for(int s=segments;s>=0;s--){float theta=((float)s/segments-.5f)*degrees*Mathf.Deg2Rad;hole.Add(new Vector2(Mathf.Sin(theta),Mathf.Cos(theta))*inner);}
                    AddBorderStrip(hole,true,bv,bu,bt);Write(borderMesh,bv,bu,bt);return;
                }
                // 끝점 중복을 제거하고 양쪽 직선과 안쪽 호를 닫는다.
                for(int s=segments;s>=0;s--){float theta=((float)s/segments-.5f)*degrees*Mathf.Deg2Rad;points.Add(new Vector2(Mathf.Sin(theta),Mathf.Cos(theta))*inner);}
            }
            else if(!closed)points.Add(Vector2.zero);
        }
        Write(fillMesh,vertices,uvs,triangles);
        var borderVertices=new List<Vector3>();var borderUvs=new List<Vector2>();var borderTriangles=new List<int>();
        AddBorderStrip(points,closed&&shape!=GroundIndicatorShape.Rectangle&&inner<=0,borderVertices,borderUvs,borderTriangles);
        Write(borderMesh,borderVertices,borderUvs,borderTriangles);
    }
    private void AddBorderStrip(List<Vector2> points,bool repeatedEnd,List<Vector3> vertices,List<Vector2> uvs,List<int> triangles)
    {
        int count=repeatedEnd?points.Count-1:points.Count,offset=vertices.Count;
        float perimeter=0;for(int i=0;i<count;i++)perimeter+=Vector2.Distance(points[i],points[(i+1)%count]);
        float travel=0;
        for(int i=0;i<=count;i++)
        {
            int at=i%count;Vector2 p=points[at];
            Vector2 previous=(p-points[(at+count-1)%count]).normalized,next=(points[(at+1)%count]-p).normalized;
            Vector2 a=new Vector2(-previous.y,previous.x),b=new Vector2(-next.y,next.x);
            Vector2 outward=(a+b).normalized;
            float correction=1f/Mathf.Max(.35f,Vector2.Dot(outward,b));
            if(i>0)travel+=Vector2.Distance(points[(i-1)%count],p);
            for(int ring=0;ring<11;ring++)
            {
                float distance=(RadialProfile[ring]-.941745f)*flameWidth/.1f;
                vertices.Add(ToMesh(p+outward*distance*correction));
                // 세계 길이가 커져도 원본 불꽃의 입자 밀도를 유지한다.
                uvs.Add(new Vector2(travel/Mathf.Max(.001f,perimeter)*Mathf.Max(1f,perimeter/(2f*Mathf.PI*3f)),ring/10f));
                if(i>0&&ring>0){int end=offset+i*11+ring;triangles.AddRange(new[]{end-12,end-11,end,end-12,end,end-1});}
            }
        }
    }
    private Vector3 ToMesh(Vector2 point)
    {
        var scale=transform.lossyScale;
        return new Vector3(point.x/Mathf.Max(.001f,Mathf.Abs(scale.x)),-point.y/Mathf.Max(.001f,Mathf.Abs(scale.z)),0);
    }
    private static void Write(Mesh mesh,List<Vector3> v,List<Vector2> uv,List<int> tri)
    {
        mesh.Clear();mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(tri,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
    }
    private void ConfigureAmbient()
    {
        foreach(var p in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            if(p==effect||p==fill||p==border)continue;
            float scale=outerRadius/3f;
            if(p.name.Contains("Fuzz"))
            {
                var main=p.main;main.startSize=1;
                p.transform.localScale=Vector3.one;p.transform.localRotation=Quaternion.Euler(270,0,0);
                var renderer=p.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Mesh;
                renderer.alignment=ParticleSystemRenderSpace.Local;renderer.mesh=ambientMesh;
                // 원본의 공중 번짐용 정점 이동은 바닥 범위 메시의 경계를 벗어나게 한다.
                if(ambientProperties==null)ambientProperties=new MaterialPropertyBlock();
                renderer.GetPropertyBlock(ambientProperties);
                ambientProperties.SetVector("_DetailVertexOffsetChannel",Vector4.zero);
                renderer.SetPropertyBlock(ambientProperties);
                continue;
            }
            p.transform.localScale=Vector3.one*scale;
            if(!p.name.Contains("Flecks"))continue;
            var emissionShape=p.shape;emissionShape.shapeType=ParticleSystemShapeType.Donut;
            emissionShape.radius=(outerRadius+InnerRadius)*.5f/scale;
            emissionShape.donutRadius=Mathf.Max(.01f,(outerRadius-InnerRadius)*.5f/scale);
            emissionShape.arc=shape==GroundIndicatorShape.Sector?angle:360;
            emissionShape.rotation=new Vector3(0,shape==GroundIndicatorShape.Sector?90-angle*.5f:0,0);
        }
    }
    private void BuildAmbientMesh()
    {
        if(ambientMesh==null)ambientMesh=NewMesh("Indicator original ambient clipped to range");
        var vertices=fillMesh.vertices;var uv=new Vector2[vertices.Length];
        // 원래 크기 24m/반경3m인 FuzzAdd의 중심 UV를 보존하고 메시로 빈 공간을 비운다.
        for(int i=0;i<vertices.Length;i++)uv[i]=new Vector2(.5f+vertices[i].x/(outerRadius*8f),.5f+vertices[i].y/(outerRadius*8f));
        ambientMesh.Clear();ambientMesh.vertices=vertices;ambientMesh.uv=uv;ambientMesh.triangles=fillMesh.triangles;
        ambientMesh.RecalculateNormals();ambientMesh.RecalculateBounds();
    }
    private static float Finite(float n,float fallback)=>float.IsNaN(n)||float.IsInfinity(n)?fallback:n;
    private void OnEnable(){simulatedTime=-1;Refresh();SetProgress(progress);}
    private void OnValidate()=>Refresh();
    private void OnDisable(){if(effect!=null)effect.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);simulatedTime=-1;}
    private void OnDestroy()
    {
        if(Surface!=null&&Surface.mesh==fillMesh)Surface.mesh=null;
        if(Border!=null&&Border.mesh==borderMesh)Border.mesh=null;
        if(effect!=null)foreach(var p in effect.GetComponentsInChildren<ParticleSystemRenderer>(true))if(p.mesh==ambientMesh)p.mesh=null;
        Release(fillMesh);Release(borderMesh);Release(ambientMesh);fillMesh=null;borderMesh=null;ambientMesh=null;
    }
    private static void Release(Object value){if(value==null)return;if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
}
