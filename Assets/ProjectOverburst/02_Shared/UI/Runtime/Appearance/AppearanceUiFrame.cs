using UnityEngine;
using UnityEngine.UI;

namespace Overburst.Appearance
{
    // Balanced thin frames remain sharp at every Canvas scale.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class AppearanceUiFrame : MaskableGraphic
    {
        [Min(.5f)] public float thickness=1.5f;
        public bool beveled;
        public Color fillColor=Color.clear;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var r=GetPixelAdjustedRect();
            float cut=beveled?Mathf.Min(18,r.height*.28f):0;
            var outer=new[]{new Vector2(r.xMin+cut,r.yMin),new Vector2(r.xMax-cut,r.yMin),new Vector2(r.xMax,r.center.y),new Vector2(r.xMax-cut,r.yMax),new Vector2(r.xMin+cut,r.yMax),new Vector2(r.xMin,r.center.y)};
            if(!beveled)outer=new[]{new Vector2(r.xMin,r.yMin),new Vector2(r.xMax,r.yMin),new Vector2(r.xMax,r.yMax),new Vector2(r.xMin,r.yMax)};
            if(fillColor.a>0)
            {
                int center=vh.currentVertCount;vh.AddVert(r.center,fillColor,Vector2.zero);
                foreach(var p in outer)vh.AddVert(p,fillColor,Vector2.zero);
                for(int i=0;i<outer.Length;i++)vh.AddTriangle(center,center+1+i,center+1+(i+1)%outer.Length);
            }
            var innerRect=new Rect(r.x+thickness,r.y+thickness,Mathf.Max(0,r.width-thickness*2),Mathf.Max(0,r.height-thickness*2));
            var inner=new Vector2[outer.Length];
            if(beveled)
            {
                inner=new[]{new Vector2(innerRect.xMin+cut,innerRect.yMin),new Vector2(innerRect.xMax-cut,innerRect.yMin),new Vector2(innerRect.xMax,innerRect.center.y),new Vector2(innerRect.xMax-cut,innerRect.yMax),new Vector2(innerRect.xMin+cut,innerRect.yMax),new Vector2(innerRect.xMin,innerRect.center.y)};
            }
            else inner=new[]{new Vector2(innerRect.xMin,innerRect.yMin),new Vector2(innerRect.xMax,innerRect.yMin),new Vector2(innerRect.xMax,innerRect.yMax),new Vector2(innerRect.xMin,innerRect.yMax)};
            for(int i=0;i<outer.Length;i++)
            {
                int next=(i+1)%outer.Length,start=vh.currentVertCount;
                vh.AddVert(outer[i],color,Vector2.zero);vh.AddVert(outer[next],color,Vector2.zero);
                vh.AddVert(inner[next],color,Vector2.zero);vh.AddVert(inner[i],color,Vector2.zero);
                vh.AddTriangle(start,start+1,start+2);vh.AddTriangle(start,start+2,start+3);
            }
        }
    }
}
