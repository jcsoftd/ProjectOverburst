using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>One mesh for all unique orthogonal routes, clipped by the native map viewport.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class OverburstSkillTreeRoutes : MaskableGraphic
{
    OverburstSkillTreeCatalog catalog;
    OverburstSkillTreePlan plan;
    Vector2 pan;
    float scale;
    float pixelScale = 1;
    static readonly Color Unplanned = OverburstSkillTreePalette.InactiveRoute;
    static readonly Color Planned = OverburstSkillTreePalette.Brass;
    static readonly Color Pending = OverburstSkillTreePalette.Ivory, Refund = OverburstSkillTreePalette.Refund;
    readonly Dictionary<string, OverburstSkillTreeCatalog.Node> index = new Dictionary<string, OverburstSkillTreeCatalog.Node>();
    readonly HashSet<string> plannedAreas = new HashSet<string>(), committedAreas = new HashSet<string>();
    public enum RouteState { Inactive, Learned, PurchaseDraft, RefundDraft }
    public void Configure(OverburstSkillTreeCatalog data, OverburstSkillTreePlan allocation, float mapScale, Vector2 mapPan, float canvasPixelScale = 1)
    {
        if(catalog!=data){index.Clear();foreach(var node in data.nodes)index.Add(node.id,node);}
        catalog=data;plan=allocation;scale=mapScale;pan=mapPan;pixelScale=Mathf.Clamp(canvasPixelScale,.1f,1);raycastTarget=false;
        plannedAreas.Clear();committedAreas.Clear();
        foreach(var node in data.nodes)if(node.cost>0){if(plan.Has(node.id))plannedAreas.Add(node.area);if(plan.IsCommitted(node.id))committedAreas.Add(node.area);}
        SetVerticesDirty();
    }
    bool Includes(string id, bool committed)
    {
        var node=index[id];var areas=committed?committedAreas:plannedAreas;
        if(node.kind=="root")return areas.Count>0;
        if(node.kind=="guide")return areas.Contains(node.area);
        return committed?plan.IsCommitted(id):plan.Has(id);
    }
    public RouteState StateFor(OverburstSkillTreeCatalog.Segment segment)
    {
        bool included=false,committed=false;
        for(int i=0;i<segment.sources.Length;i++){included|=Includes(segment.sources[i],false)&&Includes(segment.targets[i],false);committed|=Includes(segment.sources[i],true)&&Includes(segment.targets[i],true);}
        return included?committed?RouteState.Learned:RouteState.PurchaseDraft:committed?RouteState.RefundDraft:RouteState.Inactive;
    }
    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear(); if (catalog == null) return;
        foreach (var segment in catalog.segments)
        {
            var state=StateFor(segment);
            Vector2 a = segment.A * scale + pan, b = segment.B * scale + pan;
            var direction = b - a; if (direction.sqrMagnitude < .0001f) continue;
            // Keep a visible stroke below the reference resolution; subpixel lines can disappear at 720p.
            var tint=state==RouteState.Learned?Planned:state==RouteState.PurchaseDraft?Pending:state==RouteState.RefundDraft?Refund:Unplanned;
            float half=(state==RouteState.Learned?1.3f:state==RouteState.Inactive?.8f:1.1f)/pixelScale;
            if(state==RouteState.PurchaseDraft){float length=direction.magnitude,dash=7/pixelScale,gap=4/pixelScale;for(float d=0;d<length;d+=dash+gap)Strip(helper,a+direction*(d/length),a+direction*(Mathf.Min(d+dash,length)/length),half,tint);}
            else Strip(helper,a,b,half,tint);
        }
    }
    static void Strip(VertexHelper helper,Vector2 a,Vector2 b,float half,Color tint)
    {
        var direction=b-a;var normal=new Vector2(-direction.y,direction.x).normalized*half;int start=helper.currentVertCount;
        helper.AddVert(a-normal,tint,Vector2.zero);helper.AddVert(a+normal,tint,Vector2.zero);helper.AddVert(b+normal,tint,Vector2.zero);helper.AddVert(b-normal,tint,Vector2.zero);
        helper.AddTriangle(start,start+1,start+2);helper.AddTriangle(start,start+2,start+3);
    }
}
