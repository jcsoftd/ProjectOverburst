using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overburst.Mojave
{
    // Exact nearest-segment queries for dense terrain sampling. The supplied layout and its points remain unchanged.
    public sealed class VolcanoTrailField
    {
        struct Segment
        {
            public Vector2 a,d,min,max;
            public float denominator,width0,width1,height0,height1,maxWidth;
            public int index;
        }
        sealed class Node
        {
            public Vector2 min,max;
            public float maxWidth;
            public Segment[] leaf;
            public Node left,right;
        }
        readonly Node[] trails;
        public VolcanoTrailField(MojaveLayout layout)
        {
            trails=new Node[layout.trails.Count];
            for(int k=0;k<trails.Length;k++) {
                var trail=layout.trails[k];var segments=new Segment[trail.points.Length-1];
                for(int i=0;i<segments.Length;i++) {
                    var a=trail.points[i];var b=trail.points[i+1];var d=b-a;
                    segments[i]=new Segment {a=a,d=d,min=Vector2.Min(a,b),max=Vector2.Max(a,b),denominator=Mathf.Max(.0001f,d.sqrMagnitude),
                        width0=trail.widths[i],width1=trail.widths[i+1],maxWidth=Mathf.Max(trail.widths[i],trail.widths[i+1]),
                        height0=trail.heights[i],height1=trail.heights[i+1],index=i};
                }
                trails[k]=Build(segments);
            }
        }
        static Node Build(Segment[] segments)
        {
            if(segments.Length==0)return null;
            var node=new Node {min=segments[0].min,max=segments[0].max,maxWidth=segments[0].maxWidth};
            foreach(var s in segments) {node.min=Vector2.Min(node.min,s.min);node.max=Vector2.Max(node.max,s.max);node.maxWidth=Mathf.Max(node.maxWidth,s.maxWidth);}
            if(segments.Length<=6){node.leaf=segments;return node;}
            bool x=node.max.x-node.min.x>node.max.y-node.min.y;
            Array.Sort(segments,(a,b)=>{
                float ca=x?a.min.x+a.max.x:a.min.y+a.max.y,cb=x?b.min.x+b.max.x:b.min.y+b.max.y;
                int order=ca.CompareTo(cb);return order!=0?order:a.index.CompareTo(b.index);
            });
            int middle=segments.Length/2;var left=new Segment[middle];var right=new Segment[segments.Length-middle];
            Array.Copy(segments,0,left,0,left.Length);Array.Copy(segments,middle,right,0,right.Length);
            node.left=Build(left);node.right=Build(right);return node;
        }
        static float Bound(Node n,Vector2 p)
        {
            float dx=Mathf.Max(Mathf.Max(n.min.x-p.x,0),p.x-n.max.x),dy=Mathf.Max(Mathf.Max(n.min.y-p.y,0),p.y-n.max.y);
            return Mathf.Sqrt(dx*dx+dy*dy)-n.maxWidth;
        }
        static void Query(Node node,Vector2 p,ref float nearest,ref float height,ref float width,ref int firstIndex)
        {
            if(node==null||Bound(node,p)>nearest+.0001f)return;
            if(node.leaf!=null) {
                foreach(var s in node.leaf) {
                    float t=Mathf.Clamp01(Vector2.Dot(p-s.a,s.d)/s.denominator);
                    float distance=Vector2.Distance(p,s.a+s.d*t),w=Mathf.Lerp(s.width0,s.width1,t);
                    float candidate=distance-w;
                    if(candidate<nearest||(candidate==nearest&&s.index<firstIndex)) {
                        nearest=candidate;height=Mathf.Lerp(s.height0,s.height1,t);width=w;firstIndex=s.index;
                    }
                }
                return;
            }
            if(Bound(node.left,p)<=Bound(node.right,p)) {Query(node.left,p,ref nearest,ref height,ref width,ref firstIndex);Query(node.right,p,ref nearest,ref height,ref width,ref firstIndex);}
            else {Query(node.right,p,ref nearest,ref height,ref width,ref firstIndex);Query(node.left,p,ref nearest,ref height,ref width,ref firstIndex);}
        }
        public float Distance(Vector2 p,out float elevation,out float halfWidth)
        {
            float best=float.PositiveInfinity;elevation=5;halfWidth=2.5f;float weightedHeight=0,totalWeight=0;
            foreach(var trail in trails) {
                float nearest=float.PositiveInfinity,height=5,width=2.5f;int firstIndex=int.MaxValue;
                Query(trail,p,ref nearest,ref height,ref width,ref firstIndex);
                if(nearest<best){best=nearest;elevation=height;halfWidth=width;}
                float weight=1-Mathf.SmoothStep(0,1,Mathf.Clamp01(nearest/18));
                weightedHeight+=height*weight;totalWeight+=weight;
            }
            if(totalWeight>.0001f)elevation=weightedHeight/totalWeight;
            return best;
        }
    }
}
