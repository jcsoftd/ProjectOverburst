using System;
using UnityEngine;

namespace Overburst.Mojave
{
    public enum MojaveCombatSize { Large, Medium, Small }
    public enum MojavePropKind { Plant, Tree, Boulder, Rubble }

    [Serializable]
    public struct MojavePlacement
    {
        public GameObject prefab;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public Vector2[] rockOutline;
        public float radius;
        public float height;
        public MojavePropKind kind;
    }

    // A genuine cropped source place: terrain relief, splat paint and authored arrangements.
    public sealed class MojavePatch : ScriptableObject
    {
        public string sourceScene;
        public GameObject slicedPrefab;
        public Vector2 sourceFocus;
        public Vector2 sourceCenter;
        public float size = 64;
        public Vector2 backgroundSize;
        public float[] backgroundMask;
        public float[] backgroundCoreMask;
        public Vector2 backgroundBlendSize;
        public float[] backgroundBlendMask;
        public Vector2 SurfaceSize => backgroundSize.x > 0 ? backgroundSize : Vector2.one * size;
        public MojaveCombatSize combatSize;
        public int resolution = 65;
        public int layerCount = 8;
        public float[] heights;
        public float[] paint;
        public MojavePlacement[] placements;
        public float[] waterDepth;
        public float[] waterLevel;
        public Vector2[] waterUv;
        public Material waterMaterial;

        public bool Water(Vector2 local, out float depth, out float level, out Vector2 uv)
        {
            depth=0;level=0;uv=Vector2.zero;
            if(waterDepth==null || waterDepth.Length!=resolution*resolution
                || Mathf.Abs(local.x)>size*.5f || Mathf.Abs(local.y)>size*.5f)return false;
            Coordinates(local,out int x,out int z,out float tx,out float tz);
            int a=z*resolution+x;
            depth=Sample(waterDepth,a,tx,tz);
            float w0=waterDepth[a]>.03f?(1-tx)*(1-tz):0,w1=waterDepth[a+1]>.03f?tx*(1-tz):0;
            float w2=waterDepth[a+resolution]>.03f?(1-tx)*tz:0,w3=waterDepth[a+resolution+1]>.03f?tx*tz:0;
            float sum=w0+w1+w2+w3;if(sum<.00001f)return false;
            // Dry samples have no water elevation. Excluding them keeps the pond horizontal at its edge.
            level=(waterLevel[a]*w0+waterLevel[a+1]*w1+waterLevel[a+resolution]*w2+waterLevel[a+resolution+1]*w3)/sum;
            uv=(waterUv[a]*w0+waterUv[a+1]*w1+waterUv[a+resolution]*w2+waterUv[a+resolution+1]*w3)/sum;
            return depth>.03f && waterMaterial!=null;
        }
        float Sample(float[] data,int a,float tx,float tz) => Mathf.Lerp(
            Mathf.Lerp(data[a],data[a+1],tx),Mathf.Lerp(data[a+resolution],data[a+resolution+1],tx),tz);

        public float Height(Vector2 local)
        {
            Coordinates(local, out int x, out int z, out float tx, out float tz);
            int a = z * resolution + x;
            return Mathf.Lerp(Mathf.Lerp(heights[a], heights[a + 1], tx),
                Mathf.Lerp(heights[a + resolution], heights[a + resolution + 1], tx), tz);
        }

        // Signed distance to the convex LOD0 outline, in the tile's authored coordinates.
        public static float OutlineDistance(Vector2[] outline,Vector2 p)
        {
            bool inside=true;float distance=float.PositiveInfinity;
            for(int i=0;i<outline.Length;i++) {
                var a=outline[i];var edge=outline[(i+1)%outline.Length]-a;var d=p-a;
                if(edge.x*d.y-edge.y*d.x<0)inside=false;
                float t=Mathf.Clamp01(Vector2.Dot(d,edge)/Mathf.Max(.00001f,edge.sqrMagnitude));
                distance=Mathf.Min(distance,(d-edge*t).sqrMagnitude);
            }
            return (inside?-1:1)*Mathf.Sqrt(distance);
        }
        public static System.Collections.Generic.IEnumerable<Vector2> OutlineSamples(Vector2[] outline,float step=1)
        {
            var min=outline[0];var max=min;
            for(int i=0;i<outline.Length;i++) {
                var a=outline[i];var b=outline[(i+1)%outline.Length];
                min=Vector2.Min(min,a);max=Vector2.Max(max,a);
                int steps=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(a,b)/step));
                for(int k=0;k<steps;k++)yield return Vector2.Lerp(a,b,k/(float)steps);
            }
            for(float z=min.y+step*.5f;z<max.y;z+=step)for(float x=min.x+step*.5f;x<max.x;x+=step) {
                var q=new Vector2(x,z);if(OutlineDistance(outline,q)<=0)yield return q;
            }
        }

        public float Footprint(Vector2 local)
        {
            var extent=SurfaceSize;
            if(backgroundMask==null||backgroundMask.Length!=resolution*resolution
                ||Mathf.Abs(local.x)>=extent.x*.5f||Mathf.Abs(local.y)>=extent.y*.5f)return 0;
            Coordinates(local,out int x,out int z,out float tx,out float tz);
            return Sample(backgroundMask,z*resolution+x,tx,tz);
        }

        public float BlendFootprint(Vector2 local)
        {
            if(backgroundBlendMask==null||backgroundBlendMask.Length!=resolution*resolution)return Footprint(local);
            var extent=backgroundBlendSize;
            if(Mathf.Abs(local.x)>=extent.x*.5f||Mathf.Abs(local.y)>=extent.y*.5f)return 0;
            float fx=Mathf.Clamp((local.x/extent.x+.5f)*(resolution-1),0,resolution-1.001f);
            float fz=Mathf.Clamp((local.y/extent.y+.5f)*(resolution-1),0,resolution-1.001f);
            int x=(int)fx,z=(int)fz;
            return Sample(backgroundBlendMask,z*resolution+x,fx-x,fz-z);
        }

        public float Core(Vector2 local)
        {
            var extent=SurfaceSize;
            if(backgroundCoreMask==null||backgroundCoreMask.Length!=resolution*resolution
                ||Mathf.Abs(local.x)>=extent.x*.5f||Mathf.Abs(local.y)>=extent.y*.5f)return 0;
            Coordinates(local,out int x,out int z,out float tx,out float tz);
            return Sample(backgroundCoreMask,z*resolution+x,tx,tz);
        }

        public void Paint(Vector2 local, float[] result)
        {
            Coordinates(local, out int x, out int z, out float tx, out float tz);
            int a = (z * resolution + x) * layerCount;
            for (int l = 0; l < layerCount; l++)
                result[l] = Mathf.Lerp(Mathf.Lerp(paint[a + l], paint[a + layerCount + l], tx),
                    Mathf.Lerp(paint[a + resolution * layerCount + l], paint[a + (resolution + 1) * layerCount + l], tx), tz);
        }

        void Coordinates(Vector2 local, out int x, out int z, out float tx, out float tz)
        {
            var extent=SurfaceSize;
            float fx = Mathf.Clamp((local.x / extent.x + .5f) * (resolution - 1), 0, resolution - 1.001f);
            float fz = Mathf.Clamp((local.y / extent.y + .5f) * (resolution - 1), 0, resolution - 1.001f);
            x = Mathf.FloorToInt(fx); z = Mathf.FloorToInt(fz); tx = fx - x; tz = fz - z;
        }
    }

}
