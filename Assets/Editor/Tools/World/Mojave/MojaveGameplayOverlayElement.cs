using System.Collections.Generic;
using Overburst.Mojave;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Projects the generator's actual combat and road fields onto the same camera frame.</summary>
public sealed class MojaveGameplayOverlayElement : VisualElement
{
    struct Sample { public Vector3 position; public float room, trail; }
    struct Triangle { public Vector3 a,b,c; public Color color; }
    readonly List<Triangle> triangles = new List<Triangle>();
    readonly Sample[] clipped = new Sample[4];
    readonly List<Vector3> projected = new List<Vector3>();
    readonly List<Color32> colors = new List<Color32>();
    MojaveWorld world;
    Camera camera;
    MojaveLayout layout;
    MojaveCatalog catalog;
    bool plan;
    int mode;
    bool tiles;
    public int TriangleCount => triangles.Count;
    public int ViewMode => mode;
    public int RoadTriangleCount { get; private set; }
    public int CombatTriangleCount => triangles.Count-RoadTriangleCount;
    public bool TilesVisible => tiles;

    public MojaveGameplayOverlayElement()
    {
        name="gameplay-overlay";pickingMode=PickingMode.Ignore;
        style.position=Position.Absolute;style.left=0;style.right=0;style.top=0;style.bottom=0;
        style.overflow=Overflow.Hidden;
        generateVisualContent+=Draw;
    }

    public void Bind(MojaveWorld target,Camera view)
    {
        world=target;camera=view;plan=false;layout=null;catalog=null;triangles.Clear();RoadTriangleCount=0;
        if(world==null){MarkDirtyRepaint();return;}
        world.EnsureLayout();layout=world.layout;catalog=world.catalog;Build();
    }
    public void BindPlan(MojaveLayout target,MojaveCatalog source)
    {
        world=null;camera=null;plan=true;layout=target;catalog=source;mode=2;tiles=false;triangles.Clear();RoadTriangleCount=0;Build();
    }
    void Build()
    {
        int side=plan?161:Mathf.RoundToInt(layout.extent*.5f)+1;float step=layout.extent/(side-1);
        var samples=new Sample[side*side];
        for(int z=0;z<side;z++)for(int x=0;x<side;x++) {
            var p=new Vector2(x*step-layout.extent*.5f,z*step-layout.extent*.5f);
            samples[z*side+x]=new Sample { position=plan?new Vector3(p.x,0,p.y):world.Ground(p,.12f),room=layout.RoomDistance(p,out _),trail=plan?layout.PlayDistance(p):world.roundedJunctions?world.PlayDistance(p):layout.TrailDistance(p,out _,out _) };
        }
        // Roads are drawn first so the combat shape is legible at its junctions.
        for(int pass=0;pass<2;pass++) {
            for(int z=0;z<side-1;z++)for(int x=0;x<side-1;x++) {
                int k=z*side+x;
                Clip(samples[k],samples[k+1],samples[k+side+1],pass==1);
                Clip(samples[k],samples[k+side+1],samples[k+side],pass==1);
            }
            if(pass==0)RoadTriangleCount=triangles.Count;
        }
        MarkDirtyRepaint();
    }

    void Clip(Sample a,Sample b,Sample c,bool combat)
    {
        float da=combat?a.room:a.trail,db=combat?b.room:b.trail,dc=combat?c.room:c.trail;
        if(da>0&&db>0&&dc>0)return;
        int count=0;
        Edge(a,b,da,db);Edge(b,c,db,dc);Edge(c,a,dc,da);
        if(count<3)return;
        Color tint=MojaveCombatPalette.Trail;
        if(combat) {
            var centre=(a.position+b.position+c.position)/3;
            layout.RoomDistance(new Vector2(centre.x,centre.z),out var place);
            tint=MojaveCombatPalette.For(place.kind);
        }
        for(int i=1;i<count-1;i++)triangles.Add(new Triangle {a=clipped[0].position,b=clipped[i].position,c=clipped[i+1].position,color=tint});
        void Edge(Sample from,Sample to,float d0,float d1)
        {
            if(d0<=0)clipped[count++]=from;
            if((d0<=0)==(d1<=0))return;
            clipped[count++]=new Sample {position=Vector3.Lerp(from.position,to.position,d0/(d0-d1))};
        }
    }

    public void SetMode(int value) {mode=Mathf.Clamp(value,0,2);MarkDirtyRepaint();}
    public void SetTiles(bool value) {tiles=value;MarkDirtyRepaint();}
    public Mesh CreateWorldMesh(float alpha=.3f)
    {
        var mesh=new Mesh {name="Mojave Scene regions",hideFlags=HideFlags.HideAndDontSave,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
        var vertices=new Vector3[triangles.Count*3];var tints=new Color32[vertices.Length];var indices=new int[vertices.Length];
        for(int i=0;i<triangles.Count;i++) {
            var triangle=triangles[i];vertices[i*3]=triangle.a;vertices[i*3+1]=triangle.b;vertices[i*3+2]=triangle.c;
            var tint=triangle.color;tint.a=alpha;for(int j=0;j<3;j++){tints[i*3+j]=tint;indices[i*3+j]=i*3+j;}
        }
        mesh.vertices=vertices;mesh.colors32=tints;mesh.triangles=indices;mesh.RecalculateBounds();return mesh;
    }
    public void RefreshCamera() => MarkDirtyRepaint();
    public Rect CameraRect
    {
        get {
            var r=contentRect;float aspect=plan?1:1600f/900f;float width=Mathf.Min(r.width,r.height*aspect),height=width/aspect;
            return new Rect(r.x+(r.width-width)*.5f,r.y+(r.height-height)*.5f,width,height);
        }
    }
    public Vector2 Project(Vector3 point)
    {
        var p=Viewport(point);var r=CameraRect;
        return new Vector2(r.x+p.x*r.width,r.y+(1-p.y)*r.height);
    }
    Vector3 Viewport(Vector3 p)=>plan?new Vector3(p.x/layout.extent+.5f,p.z/layout.extent+.5f,1):camera.WorldToViewportPoint(p);
    void Draw(MeshGenerationContext context)
    {
        if(layout==null||(!plan&&camera==null)||contentRect.width<1||contentRect.height<1)return;
        if(mode==0){if(tiles)DrawTiles(context);return;}
        projected.Clear();colors.Clear();var rect=CameraRect;
        foreach(var triangle in triangles) {
            var a=Viewport(triangle.a);var b=Viewport(triangle.b);var c=Viewport(triangle.c);
            if(!plan&&(a.z<=camera.nearClipPlane||b.z<=camera.nearClipPlane||c.z<=camera.nearClipPlane))continue;
            if(Mathf.Max(a.x,Mathf.Max(b.x,c.x))<0||Mathf.Min(a.x,Mathf.Min(b.x,c.x))>1
                ||Mathf.Max(a.y,Mathf.Max(b.y,c.y))<0||Mathf.Min(a.y,Mathf.Min(b.y,c.y))>1)continue;
            var color=triangle.color;color.a=mode==1?.48f:1;
            Add(a);Add(b);Add(c);
            void Add(Vector3 p) {projected.Add(new Vector3(rect.x+p.x*rect.width,rect.y+(1-p.y)*rect.height,Vertex.nearZ));colors.Add(color);}
        }
        // Keep each allocation below UI Toolkit's 16-bit vertex limit.
        for(int start=0;start<projected.Count;start+=60000) {
            int count=Mathf.Min(60000,projected.Count-start);var mesh=context.Allocate(count,count);
            for(int i=0;i<count;i++)mesh.SetNextVertex(new Vertex {position=projected[start+i],tint=colors[start+i]});
            for(int i=0;i<count;i+=3) {
                var a=projected[start+i];var b=projected[start+i+1];var c=projected[start+i+2];
                bool clockwise=(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x)>0;
                mesh.SetNextIndex((ushort)i);mesh.SetNextIndex((ushort)(i+(clockwise?1:2)));mesh.SetNextIndex((ushort)(i+(clockwise?2:1)));
            }
        }
        if(tiles&&!plan)DrawTiles(context);
    }

    void DrawTiles(MeshGenerationContext context)
    {
        var painter=context.painter2D;painter.lineWidth=1.7f;painter.strokeColor=new Color(1,.89f,.58f,.95f);
        foreach(var place in world.layout.places) {
            float half=world.catalog.patches[place.patch].size*.5f;
            var corners=new[]{new Vector2(-half,-half),new Vector2(half,-half),new Vector2(half,half),new Vector2(-half,half)};
            for(int edge=0;edge<4;edge++) {
                int steps=Mathf.CeilToInt(half*2/2);bool valid=false;painter.BeginPath();
                for(int i=0;i<=steps;i++) {
                    var point=world.Ground(place.World(Vector2.Lerp(corners[edge],corners[(edge+1)%4],i/(float)steps)),.15f);
                    var viewport=camera.WorldToViewportPoint(point);
                    if(viewport.z>camera.nearClipPlane) {
                        if(valid)painter.LineTo(Project(point));else painter.MoveTo(Project(point));valid=true;
                    } else if(valid){painter.Stroke();painter.BeginPath();valid=false;}
                }
                if(valid)painter.Stroke();
            }
        }
    }
}
