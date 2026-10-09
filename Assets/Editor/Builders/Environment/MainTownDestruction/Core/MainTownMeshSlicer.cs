using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Preserve the vendor's material slots, wind colors and UV channels without changing its importer.
internal static class MainTownMeshSlicer
{
    internal struct Vertex
    {
        public Vector3 p, n;
        public Vector4 tangent;
        public Color color;
        public Vector2 uv, uv1;
        public static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex {
            p=Vector3.Lerp(a.p,b.p,t), n=Vector3.Lerp(a.n,b.n,t).normalized,
            tangent=Vector4.Lerp(a.tangent,b.tangent,t), color=Color.Lerp(a.color,b.color,t),
            uv=Vector2.Lerp(a.uv,b.uv,t), uv1=Vector2.Lerp(a.uv1,b.uv1,t)
        };
    }
    internal sealed class Source
    {
        public Vertex[] vertices;
        public int[][] triangles;
        public Bounds bounds;
        public bool hasUV1;
    }

    internal static Source Read(Mesh mesh, Matrix4x4 transform)
    {
        using(var data=MeshUtility.AcquireReadOnlyMeshData(mesh))
        using(var p=new NativeArray<Vector3>(mesh.vertexCount,Allocator.Temp))
        using(var n=new NativeArray<Vector3>(mesh.vertexCount,Allocator.Temp))
        using(var t=new NativeArray<Vector4>(mesh.vertexCount,Allocator.Temp))
        using(var c=new NativeArray<Color>(mesh.vertexCount,Allocator.Temp))
        using(var uv=new NativeArray<Vector2>(mesh.vertexCount,Allocator.Temp))
        using(var uv1=new NativeArray<Vector2>(mesh.vertexCount,Allocator.Temp))
        {
            var d=data[0];d.GetVertices(p);d.GetNormals(n);d.GetUVs(0,uv);
            bool colors=mesh.HasVertexAttribute(VertexAttribute.Color), tangents=mesh.HasVertexAttribute(VertexAttribute.Tangent), second=mesh.HasVertexAttribute(VertexAttribute.TexCoord1);
            if(colors)d.GetColors(c);if(tangents)d.GetTangents(t);if(second)d.GetUVs(1,uv1);
            for(int channel=2;channel<8;channel++)
                if(mesh.HasVertexAttribute((VertexAttribute)((int)VertexAttribute.TexCoord0+channel)))throw new InvalidOperationException("Inspect additional UV channels before slicing: "+mesh.name);
            var result=new Source {vertices=new Vertex[mesh.vertexCount],triangles=new int[mesh.subMeshCount][],hasUV1=second};
            var normals=transform.inverse.transpose;
            for(int i=0;i<p.Length;i++) result.vertices[i]=new Vertex { p=transform.MultiplyPoint3x4(p[i]),n=normals.MultiplyVector(n[i]).normalized,
                tangent=tangents?new Vector4(transform.MultiplyVector((Vector3)t[i]).normalized.x,transform.MultiplyVector((Vector3)t[i]).normalized.y,transform.MultiplyVector((Vector3)t[i]).normalized.z,t[i].w):Vector4.zero,
                color=colors?c[i]:Color.white,uv=uv[i],uv1=uv1[i] };
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                var descriptor=d.GetSubMesh(sub);var indices=new int[descriptor.indexCount];
                if(descriptor.topology!=MeshTopology.Triangles)throw new InvalidOperationException("Triangle meshes are required.");
                if(mesh.indexFormat==IndexFormat.UInt16){var raw=d.GetIndexData<ushort>();for(int i=0;i<indices.Length;i++)indices[i]=raw[descriptor.indexStart+i]+descriptor.baseVertex;}
                else{var raw=d.GetIndexData<uint>();for(int i=0;i<indices.Length;i++)indices[i]=(int)raw[descriptor.indexStart+i]+descriptor.baseVertex;}
                if(transform.determinant<0)for(int i=0;i<indices.Length;i+=3){int swap=indices[i+1];indices[i+1]=indices[i+2];indices[i+2]=swap;}
                result.triangles[sub]=indices;
            }
            result.bounds=new Bounds(result.vertices[0].p,Vector3.zero);foreach(var vertex in result.vertices)result.bounds.Encapsulate(vertex.p);
            return result;
        }
    }

    internal static Mesh Slice(Source source, float low, float high, bool cap)
    {
        var vertices=new List<Vertex>();var triangles=Enumerable.Range(0,source.triangles.Length+(cap?1:0)).Select(_=>new List<int>()).ToArray();
        var boundaries=new[]{new List<(Vector3 a,Vector3 b)>(),new List<(Vector3 a,Vector3 b)>()};
        void Triangle(Vertex a,Vertex b,Vertex c,int sub){if(Vector3.Cross(b.p-a.p,c.p-a.p).sqrMagnitude<1e-14f)return;int start=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);triangles[sub].Add(start);triangles[sub].Add(start+1);triangles[sub].Add(start+2);}
        for(int sub=0;sub<source.triangles.Length;sub++)
        {
            var indices=source.triangles[sub];
            for(int i=0;i<indices.Length;i+=3)
            {
                var polygon=Clip(Clip(new List<Vertex>{source.vertices[indices[i]],source.vertices[indices[i+1]],source.vertices[indices[i+2]]},low,true),high,false);
                for(int j=1;j+1<polygon.Count;j++)Triangle(polygon[0],polygon[j],polygon[j+1],sub);
                if(!cap)continue;
                for(int j=0;j<polygon.Count;j++)
                {
                    var a=polygon[j].p;var b=polygon[(j+1)%polygon.Count].p;if((a-b).sqrMagnitude<1e-12f)continue;
                    if(Mathf.Abs(a.y-low)<1e-5f&&Mathf.Abs(b.y-low)<1e-5f)boundaries[0].Add((a,b));
                    if(Mathf.Abs(a.y-high)<1e-5f&&Mathf.Abs(b.y-high)<1e-5f)boundaries[1].Add((a,b));
                }
            }
        }
        if(cap)for(int plane=0;plane<2;plane++)
        {
            var edges=boundaries[plane];
            while(edges.Count>0)
            {
                var edge=edges[0];edges.RemoveAt(0);var loop=new List<Vector3>{edge.a,edge.b};
                while((loop[loop.Count-1]-loop[0]).sqrMagnitude>1e-8f)
                {
                    var end=loop[loop.Count-1];int next=edges.FindIndex(e=>(e.a-end).sqrMagnitude<1e-8f||(e.b-end).sqrMagnitude<1e-8f);
                    if(next<0)break;edge=edges[next];edges.RemoveAt(next);loop.Add((edge.a-end).sqrMagnitude<1e-8f?edge.b:edge.a);
                }
                if(loop.Count<4||(loop[loop.Count-1]-loop[0]).sqrMagnitude>1e-8f)continue;
                loop.RemoveAt(loop.Count-1);var center=loop.Aggregate(Vector3.zero,(sum,x)=>sum+x)/loop.Count;
                Vertex Cap(Vector3 p)=>new Vertex{p=p,n=plane==1?Vector3.up:Vector3.down,tangent=new Vector4(1,0,0,1),color=Color.white,uv=new Vector2(p.x,p.z)};
                for(int j=0;j<loop.Count;j++)
                {
                    var a=loop[j];var b=loop[(j+1)%loop.Count];if((Vector3.Cross(a-center,b-center).y>0)!=(plane==1)){var swap=a;a=b;b=swap;}
                    Triangle(Cap(center),Cap(a),Cap(b),triangles.Length-1);
                }
            }
        }
        var mesh=new Mesh{indexFormat=vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
        mesh.SetVertices(vertices.Select(x=>x.p).ToList());mesh.SetNormals(vertices.Select(x=>x.n).ToList());mesh.SetTangents(vertices.Select(x=>x.tangent).ToList());
        mesh.SetColors(vertices.Select(x=>x.color).ToList());mesh.SetUVs(0,vertices.Select(x=>x.uv).ToList());if(source.hasUV1)mesh.SetUVs(1,vertices.Select(x=>x.uv1).ToList());
        mesh.subMeshCount=triangles.Length;for(int sub=0;sub<triangles.Length;sub++)mesh.SetTriangles(triangles[sub],sub,false);mesh.RecalculateBounds();
        return mesh;
    }

    static List<Vertex> Clip(List<Vertex> input,float height,bool above)
    {
        var output=new List<Vertex>();if(input.Count==0)return output;
        for(int i=0;i<input.Count;i++)
        {
            var a=input[i];var b=input[(i+1)%input.Count];bool insideA=above?a.p.y>=height:a.p.y<=height,insideB=above?b.p.y>=height:b.p.y<=height;
            if(insideA)output.Add(a);if(insideA!=insideB)output.Add(Vertex.Lerp(a,b,(height-a.p.y)/(b.p.y-a.p.y)));
        }
        return output;
    }

    internal static double Area(Source source) => source.triangles.Sum(t=>Area(source.vertices.Select(x=>x.p).ToArray(),t));
    internal static double ExteriorArea(Mesh mesh,int subMeshes)=>Enumerable.Range(0,subMeshes).Sum(sub=>Area(mesh.vertices,mesh.GetTriangles(sub)));
    static double Area(Vector3[] vertices,int[] triangles){double area=0;for(int i=0;i<triangles.Length;i+=3)area+=Vector3.Cross(vertices[triangles[i+1]]-vertices[triangles[i]],vertices[triangles[i+2]]-vertices[triangles[i]]).magnitude*.5;return area;}
}

