using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Overburst.Mojave
{
    // Optional Oasis surfaces use the same six-place v3 layout and the actual sliced source data.
    public static class OasisSurfaceBuilder
    {
        public sealed class Details
        {
            public DetailPrototype[] prototypes;
            public DetailScatterMode mode;
            public int[][,] layers;
            public void Write(TerrainData target)
            {
                target.SetDetailScatterMode(mode);target.SetDetailResolution(512,16);target.detailPrototypes=prototypes;
                for(int l=0;l<layers.Length;l++)target.SetDetailLayer(0,0,l,layers[l]);
            }
        }
        sealed class SourceDetails
        {
            public Terrain terrain;
            public int[][,] layers;
        }
        public static Details BuildDetails(MojaveWorld world,TerrainData destination)
        {
            var sources=new Dictionary<int,SourceDetails>();
            foreach(var place in world.layout.places) {
                if(sources.ContainsKey(place.patch))continue;
                var terrain=world.catalog.patches[place.patch].slicedPrefab.GetComponentInChildren<Terrain>();var td=terrain.terrainData;
                var source=new SourceDetails {terrain=terrain,layers=new int[td.detailPrototypes.Length][,]};
                for(int l=0;l<source.layers.Length;l++)source.layers[l]=td.GetDetailLayer(0,0,td.detailWidth,td.detailHeight,l);
                sources.Add(place.patch,source);
            }
            var first=sources[world.layout.places[0].patch].terrain.terrainData;
            var result=new Details {prototypes=first.detailPrototypes,mode=first.detailScatterMode,layers=new int[first.detailPrototypes.Length][,]};
            for(int l=0;l<result.layers.Length;l++)result.layers[l]=new int[512,512];
            for(int z=0;z<512;z++)for(int x=0;x<512;x++) {
                var p=new Vector2((x+.5f)/512*256-128,(z+.5f)/512*256-128);
                float rd=world.layout.RoomDistance(p,out var place),tr=world.TrailDistance(p,out _,out _);
                if(rd>13 || tr<.7f || world.IsWater(p))continue;
                var patch=world.catalog.patches[place.patch];var source=sources[place.patch];var td=source.terrain.terrainData;
                var local=place.Local(p)+patch.sourceFocus;
                float nx=(local.x-source.terrain.transform.position.x)/td.size.x,nz=(local.y-source.terrain.transform.position.z)/td.size.z;
                if(nx<0 || nx>=1 || nz<0 || nz>=1)continue;
                int sx=Mathf.Min(td.detailWidth-1,(int)(nx*td.detailWidth)),sz=Mathf.Min(td.detailHeight-1,(int)(nz*td.detailHeight));
                float bank=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(4,13,rd));
                float density=bank*(rd<-4?.30f:.85f)*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.7f,2.7f,tr));
                for(int l=0;l<result.layers.Length;l++)result.layers[l][z,x]=Mathf.RoundToInt(source.layers[l][sz,sx]*density);
            }
            result.Write(destination);return result;
        }
        struct Vertex
        {
            public Vector3 p;
            public Vector2 uv;
            public float sign;
            public static Vertex Lerp(Vertex a,Vertex b,float t)
            {
                var p=Vector3.Lerp(a.p,b.p,t);p.y=a.sign>0?a.p.y:b.p.y;
                return new Vertex {p=p,uv=Vector2.Lerp(a.uv,b.uv,t),sign=0};
            }
        }
        public static int BuildWater(MojaveWorld world)
        {
            int count=0;
            foreach(var place in world.layout.places) {
                var patch=world.catalog.patches[place.patch];if(patch.waterMaterial==null)continue;
                const int n=129;var grid=new Vertex[n*n];
                for(int z=0;z<n;z++)for(int x=0;x<n;x++) {
                    var local=new Vector2(x/(float)(n-1)-.5f,z/(float)(n-1)-.5f)*patch.size;var p=place.World(local);
                    var vertex=new Vertex {p=new Vector3(p.x,0,p.y),sign=-.03f};
                    if(patch.Water(local,out float depth,out float level,out var uv)) {
                        float waterHeight=place.height+Mathf.Clamp(level*.55f,-2,2);
                        vertex.p.y=waterHeight;vertex.uv=uv;
                        // Only an overlapping higher pond can replace this surface; dry room ownership must not crop its shoreline.
                        float overlap=float.PositiveInfinity;
                        foreach(var other in world.layout.places)if(other!=place) {
                            var otherPatch=world.catalog.patches[other.patch];
                            if(otherPatch.Water(other.Local(p),out float otherDepth,out float otherLevel,out _)
                                &&other.height+Mathf.Clamp(otherLevel*.55f,-2,2)>waterHeight+.01f)
                                overlap=Mathf.Min(overlap,.03f-otherDepth);
                        }
                        vertex.sign=Mathf.Min(depth-.03f,waterHeight-world.Ground(p).y-.045f);
                        vertex.sign=Mathf.Min(vertex.sign,overlap);
                        vertex.sign=Mathf.Min(vertex.sign,world.PlayDistance(p)-1.35f);
                        vertex.sign=Mathf.Min(vertex.sign,125-Mathf.Max(Mathf.Abs(p.x),Mathf.Abs(p.y)));
                    }
                    grid[z*n+x]=vertex;
                }
                var vertices=new List<Vector3>();var uvs=new List<Vector2>();var triangles=new List<int>();
                var input=new Vertex[3];var polygon=new List<Vertex>(4);
                void Triangle(int ia,int ib,int ic) {
                    input[0]=grid[ia];input[1]=grid[ib];input[2]=grid[ic];polygon.Clear();
                    for(int i=0;i<3;i++) {
                        var a=input[i];var b=input[(i+1)%3];
                        if(a.sign>0)polygon.Add(a);
                        if((a.sign>0)!=(b.sign>0))polygon.Add(Vertex.Lerp(a,b,a.sign/(a.sign-b.sign)));
                    }
                    if(polygon.Count<3)return;
                    int start=vertices.Count;foreach(var v in polygon){vertices.Add(v.p);uvs.Add(v.uv);}
                    for(int i=1;i<polygon.Count-1;i++){triangles.Add(start);triangles.Add(start+i);triangles.Add(start+i+1);}
                }
                for(int z=0;z<n-1;z++)for(int x=0;x<n-1;x++) {int i=z*n+x;Triangle(i,i+n,i+1);Triangle(i+1,i+n,i+n+1);}
                if(triangles.Count==0)continue;
                var mesh=new Mesh {name=(world.catalog.volcano?"Volcano authored lava · ":"Oasis authored shore · ")+place.name,indexFormat=IndexFormat.UInt32};
                mesh.SetVertices(vertices);mesh.SetUVs(0,uvs);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
                var go=new GameObject(mesh.name);go.transform.SetParent(world.generatedRoot,false);go.layer=4;
                go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=patch.waterMaterial;
                renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=true;
                go.AddComponent<MeshCollider>().sharedMesh=mesh;
                world.OwnWaterMesh(mesh);count++;
            }
            return count;
        }
    }
}
