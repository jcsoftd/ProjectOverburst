using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;

namespace Overburst.Mojave
{
    // Final bake shares the finished road field and terrain across paint, shoulder placement and grounding.
    public static class MojaveTerrainFinish
    {
        static float Smooth(float a,float b,float v)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,v));
        public static void ShapeRoadHeights(MojaveLayout layout,bool refined=false)
        {
            foreach(var trail in layout.trails) {
                float travelled=0;
                for(int i=0;i<trail.points.Length;i++) {
                    var p=trail.points[i];float d=layout.RoomDistance(p,out _);
                    float rise=(layout.Noise(p,.026f,701)-.32f)*7+Mathf.Max(0,layout.Noise(p,.055f,711)-.45f);
                    if(refined) {
                        if(i>0)travelled+=Vector2.Distance(p,trail.points[i-1]);
                        float phase=trail.from*1.91f+trail.to*.73f+layout.seed*.013f;
                        // Broad variation has room to read as a widening or a pinch, not a serrated edge.
                        float spread=.5f+.5f*Mathf.Sin(travelled*.075f+phase);
                        trail.widths[i]=Mathf.Clamp(trail.width*Mathf.Lerp(.76f,1.40f,spread),5.2f,10.6f)*.5f;
                        rise=(layout.Noise(p,.018f,701)-.27f)*8+(layout.Noise(p,.048f,711)-.5f)*1.2f;
                    }
                    trail.heights[i]+=Smooth(-1,11,d)*rise;
                }
                if(refined)for(int pass=0;pass<3;pass++) {
                    var original=(float[])trail.heights.Clone();
                    for(int i=1;i<original.Length-1;i++)trail.heights[i]=Mathf.Lerp(original[i],(original[i-1]+original[i+1])*.5f,.5f);
                }
                // Bound longitudinal grade while preserving the entrance heights.
                for(int pass=0;pass<4;pass++)for(int direction=0;direction<2;direction++)
                    for(int k=1;k<trail.heights.Length-1;k++) {
                        int i=direction==0?k:trail.heights.Length-1-k,j=direction==0?i-1:i+1;
                        float step=Vector2.Distance(trail.points[i],trail.points[j])*(refined?.18f:.22f);
                        trail.heights[i]=Mathf.Clamp(trail.heights[i],trail.heights[j]-step,trail.heights[j]+step);
                    }
            }
        }
        public static void PaintRoads(MojaveWorld world,float[,,] paint)
        {
            int n=paint.GetLength(0),layers=paint.GetLength(2);
            for(int z=0;z<n;z++)for(int x=0;x<n;x++) {
                var p=new Vector2(x/(float)n*world.MapSize-world.MapSize*.5f,z/(float)n*world.MapSize-world.MapSize*.5f);
                float d=world.TrailDistance(p,out _,out _),rd=world.layout.RoomDistance(p,out _);
                float patch=world.layout.Noise(p,.19f,803),broad=world.layout.Noise(p,.052f,805);
                float margin=(world.layout.Noise(p,.12f,807)-.5f)*2;
                float weight=(1-Smooth(-1.2f+margin,1.6f+margin,d))*Smooth(-7,1,rd);
                weight*=Mathf.Lerp(.55f,.95f,Smooth(.28f,.72f,broad));
                if(weight<=0)continue;
                float rubble=Mathf.Lerp(.55f,.90f,Smooth(.2f,.78f,patch));
                for(int l=0;l<layers;l++)paint[z,x,l]*=1-weight;
                paint[z,x,2]+=weight*rubble;paint[z,x,0]+=weight*(1-rubble)*.65f;paint[z,x,3]+=weight*(1-rubble)*.35f;
            }
        }
        static Bounds BoundsOf(GameObject go)
        {
            var rs=go.GetComponentsInChildren<Renderer>(true);var b=rs[0].bounds;
            for(int i=1;i<rs.Length;i++)b.Encapsulate(rs[i].bounds);return b;
        }
        public static bool ClearOfPlay(MojaveWorld world,Bounds b,float margin=.3f)
        {
            for(float z=b.min.z;z<=b.max.z+.3f;z+=.6f)for(float x=b.min.x;x<=b.max.x+.3f;x+=.6f)
                if(world.PlayDistance(new Vector2(Mathf.Min(x,b.max.x),Mathf.Min(z,b.max.z)))<margin)return false;
            return true;
        }
        public static GameObject[] PropRoots(MojaveWorld world)
        {
            var roots=new HashSet<GameObject>();
            foreach(var mf in world.generatedRoot.GetComponentsInChildren<MeshFilter>(true)) {
                if(mf.sharedMesh==null)continue;
                var t=mf.transform;
                var lod=t.GetComponentInParent<LODGroup>();
                if(lod!=null)t=lod.transform;
                else if(t.name.Contains("_LOD")&&t.parent!=null)t=t.parent;
                roots.Add(t.gameObject);
            }
            return roots.Where(g=>!g.transform.parent.GetComponentsInParent<Transform>().Any(t=>roots.Contains(t.gameObject))).ToArray();
        }

        public static bool Reserved(MojaveWorld world,Vector2 p,float radius)
        {
            foreach(var b in world.shoulderFootprints) {
                var q=b.ClosestPoint(new Vector3(p.x,b.center.y,p.y));
                if(Vector2.Distance(p,new Vector2(q.x,q.z))<radius+.2f)return true;
            }
            return false;
        }
        static Bounds PlacementBounds(MojavePlacement a)
        {
            var source=BoundsOf(a.prefab);var matrix=Matrix4x4.TRS(a.position,a.rotation,a.scale);
            var b=new Bounds(matrix.MultiplyPoint3x4(source.center-a.prefab.transform.position),Vector3.zero);
            for(int z=-1;z<=1;z+=2)for(int y=-1;y<=1;y+=2)for(int x=-1;x<=1;x+=2)
                b.Encapsulate(matrix.MultiplyPoint3x4(source.center-a.prefab.transform.position+Vector3.Scale(source.extents,new Vector3(x,y,z))));
            return b;
        }
        public static void PlanShoulders(MojaveWorld world)
        {
            world.shoulderMasses.Clear();world.shoulderFootprints.Clear();
            if(world.refinedRoads)return;
            var prefabs=world.catalog.boulders.Where(p=>p.name.Contains("RockAssemble")).ToArray();
            if(prefabs.Length==0)throw new InvalidOperationException("Mojave rock assemblies are required for natural shoulder masses.");
            var occupied=new List<Bounds>();
            // Protect the source's major formations before allocating the remaining boundary to smaller candidates.
            var backdropSet=world.GetComponent<MojaveBackdropSet>();
            foreach(var stamp in backdropSet.stamps)
                foreach(var source in stamp.tile.placements.Where(a=>a.kind==MojavePropKind.Boulder&&!MojaveWorld.IsLooseStoneName(a.prefab.name)&&a.height>=1.4f)) {
                    var a=source;var p=stamp.World(new Vector2(a.position.x,a.position.z));
                    a.position=new Vector3(p.x,0,p.y);a.rotation=backdropSet.PlacementRotation(stamp,a.rotation);occupied.Add(PlacementBounds(a));
                }
            foreach(var place in world.layout.places)
                foreach(var source in world.catalog.patches[place.patch].placements.Where(a=>a.kind==MojavePropKind.Boulder&&!MojaveWorld.IsLooseStoneName(a.prefab.name)&&a.height>=1.4f)) {
                    var a=source;var p=place.World(new Vector2(a.position.x,a.position.z));
                    if(world.layout.RoomDistance(p,out _)>20||world.TrailDistance(p,out _,out _)<a.radius+.7f)continue;
                    a.position=new Vector3(p.x,0,p.y);a.rotation=Quaternion.Euler(0,-place.rotation,0)*a.rotation;occupied.Add(PlacementBounds(a));
                }
            var rng=new System.Random(world.seed^0x6159);
            float Range(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());
            var points=new List<Vector2>();
            for(float z=-world.MapSize*.5f+10;z<world.MapSize*.5f-10;z+=4)
                for(float x=-world.MapSize*.5f+10;x<world.MapSize*.5f-10;x+=4)points.Add(new Vector2(x+Range(-1.7f,1.7f),z+Range(-1.7f,1.7f)));
            points=points.OrderByDescending(p=>world.layout.Noise(p,.049f,989)).ToList();
            int tier=0;
            foreach(float width in new[]{10f,7f,5f}) {
                int added=0,budget=world.layout.places.Count*(tier==0?5:tier==1?3:2);tier++;
                foreach(var p in points) {
                    if(added>=budget)break;
                    float distance=world.PlayDistance(p);
                    if(distance<2.2f||distance>20||world.layout.Noise(p,.065f,981)<.39f)continue;
                    var prefab=prefabs[rng.Next(prefabs.Length)];var source=BoundsOf(prefab);
                    float scale=Mathf.Clamp(width/Mathf.Max(source.size.x,source.size.z),.45f,1);
                    var a=new MojavePlacement{prefab=prefab,position=new Vector3(p.x,0,p.y),rotation=Quaternion.Euler(0,Range(0,360),0),scale=Vector3.one*scale,kind=MojavePropKind.Boulder,height=source.size.y*scale};
                    var b=PlacementBounds(a);a.radius=new Vector2(b.extents.x,b.extents.z).magnitude;
                    if(!ClearOfPlay(world,b,.6f))continue;
                    bool overlap=occupied.Any(other=>{
                        float dx=Mathf.Min(b.max.x,other.max.x)-Mathf.Max(b.min.x,other.min.x);
                        float dz=Mathf.Min(b.max.z,other.max.z)-Mathf.Max(b.min.z,other.min.z);
                        return dx>0&&dz>0&&dx*dz>Mathf.Min(b.size.x*b.size.z,other.size.x*other.size.z)*.15f;
                    });
                    if(overlap)continue;
                    world.shoulderMasses.Add(a);world.shoulderFootprints.Add(b);occupied.Add(b);added++;
                }
            }
        }
        internal static Vector2[] Hull(IEnumerable<Vector2> source)
        {
            var points=source.Distinct().OrderBy(v=>v.x).ThenBy(v=>v.y).ToArray();
            if(points.Length<3)return points;
            float Cross(Vector2 a,Vector2 b,Vector2 c)=>(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
            var hull=new List<Vector2>();
            foreach(var p in points){while(hull.Count>=2&&Cross(hull[hull.Count-2],hull[hull.Count-1],p)<=0)hull.RemoveAt(hull.Count-1);hull.Add(p);}
            int lower=hull.Count;
            for(int i=points.Length-2;i>=0;i--){while(hull.Count>lower&&Cross(hull[hull.Count-2],hull[hull.Count-1],points[i])<=0)hull.RemoveAt(hull.Count-1);hull.Add(points[i]);}
            hull.RemoveAt(hull.Count-1);return hull.ToArray();
        }
        static bool Overlap(Vector2[] a,Vector2[] b)
        {
            foreach(var polygon in new[]{a,b})for(int i=0;i<polygon.Length;i++) {
                var d=polygon[(i+1)%polygon.Length]-polygon[i];var axis=new Vector2(-d.y,d.x).normalized;
                float amin=float.PositiveInfinity,amax=float.NegativeInfinity,bmin=amin,bmax=amax;
                foreach(var p in a){float v=Vector2.Dot(p,axis);amin=Mathf.Min(amin,v);amax=Mathf.Max(amax,v);}
                foreach(var p in b){float v=Vector2.Dot(p,axis);bmin=Mathf.Min(bmin,v);bmax=Mathf.Max(bmax,v);}
                if(amax+.15f<bmin||bmax+.15f<amin)return false;
            }
            return true;
        }
        public static void DressShoulders(MojaveWorld world)
        {
            world.shoulderMassCount=0;if(world.refinedRoads)return;
            var parent=new GameObject("Road boundary · large first, then small").transform;parent.SetParent(world.generatedRoot,false);
            world.shoulderMassCount=0;
            foreach(var a in world.shoulderMasses) {
                var go=UnityEngine.Object.Instantiate(a.prefab,world.Ground(new Vector2(a.position.x,a.position.z)),a.rotation,parent);
                go.name="ShoulderMass_"+a.prefab.name;go.transform.localScale=a.scale;
                foreach(var collider in go.GetComponentsInChildren<Collider>(true))collider.enabled=false;
                foreach(var lod in go.GetComponentsInChildren<LODGroup>(true))lod.fadeMode=LODFadeMode.None;
                world.shoulderMassCount++;
            }
        }
        internal static Vector3[] Vertices(Mesh mesh)
        {
#if UNITY_EDITOR
            using(var data=UnityEditor.MeshUtility.AcquireReadOnlyMeshData(mesh))
#else
            using(var data=Mesh.AcquireReadOnlyMeshData(mesh))
#endif
            using(var vertices=new NativeArray<Vector3>(mesh.vertexCount,Allocator.Temp)) {
                data[0].GetVertices(vertices);return vertices.ToArray();
            }
        }

        public static int[] MeshIslands(Mesh mesh,Vector3[] vertices)
        {
            int[] parent=Enumerable.Range(0,vertices.Length).ToArray();
            int Root(int i){while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;}
            void Join(int a,int b){a=Root(a);b=Root(b);if(a!=b)parent[a]=b;}
            var welded=new Dictionary<(int,int,int),int>();
            for(int i=0;i<vertices.Length;i++) {
                var v=vertices[i];var key=(Mathf.RoundToInt(v.x*10000),Mathf.RoundToInt(v.y*10000),Mathf.RoundToInt(v.z*10000));
                if(welded.TryGetValue(key,out var j))Join(i,j);else welded[key]=i;
            }
#if UNITY_EDITOR
            using(var data=UnityEditor.MeshUtility.AcquireReadOnlyMeshData(mesh))
#else
            using(var data=Mesh.AcquireReadOnlyMeshData(mesh))
#endif
            {
                for(int sub=0;sub<data[0].subMeshCount;sub++) {
                    var desc=data[0].GetSubMesh(sub);if(desc.topology!=MeshTopology.Triangles)continue;
                    using(var indices=new NativeArray<int>(desc.indexCount,Allocator.Temp)) {
                        data[0].GetIndices(indices,sub);
                        for(int i=0;i<indices.Length;i+=3){Join(indices[i],indices[i+1]);Join(indices[i],indices[i+2]);}
                    }
                }
            }
            for(int i=0;i<parent.Length;i++)parent[i]=Root(i);return parent;
        }

        static List<(Transform transform,Vector3 point)> Feet(GameObject go,Dictionary<Mesh,Vector3[]> cache,Dictionary<Mesh,Vector3[]> samples)
        {
            var result=new List<(Transform,Vector3)>();
            var group=go.GetComponent<LODGroup>();
            var renderers=group!=null?group.GetLODs()[0].renderers:go.GetComponentsInChildren<Renderer>(true);
            foreach(var renderer in renderers) {
                if(renderer==null)continue;
                var filter=renderer.GetComponent<MeshFilter>();if(filter==null||filter.sharedMesh==null)continue;
                var mesh=filter.sharedMesh;
                if(!cache.TryGetValue(mesh,out var vertices)){vertices=Vertices(mesh);cache[mesh]=vertices;}
                var b=mesh.bounds;
                if(!samples.TryGetValue(mesh,out var points)) {
                    var grid=new Dictionary<int,Vector3>();
                    foreach(var v in vertices) {
                        int x=Mathf.Clamp((int)((v.x-b.min.x)/Mathf.Max(.001f,b.size.x)*8),0,7);
                        int z=Mathf.Clamp((int)((v.z-b.min.z)/Mathf.Max(.001f,b.size.z)*8),0,7);
                        int key=z*8+x;
                        if(!grid.TryGetValue(key,out var old)||v.y<old.y)grid[key]=v;
                    }
                    samples[mesh]=points=grid.Values.ToArray();
                }
                foreach(var v in points)if(v.y<=b.min.y+b.size.y*(go.name.Contains("Rock")?.55f:.24f)+.01f)result.Add((filter.transform,v));
            }
            return result;
        }
        // Use the stem's lowest ring, not the canopy's low branches, as the planting anchor.
        public static Vector3 PlantRoot(GameObject go,Dictionary<Mesh,Vector3[]> cache,Dictionary<Mesh,Vector3[]> samples)
        {
            var feet=Feet(go,cache,samples).Select(f=>f.transform.TransformPoint(f.point)).ToArray();
            if(feet.Length==0)throw new InvalidOperationException("No plant root geometry: "+go.name);
            float bottom=feet.Min(p=>p.y);var ring=feet.Where(p=>p.y<=bottom+.025f).ToArray();
            return ring.Aggregate(Vector3.zero,(sum,p)=>sum+p)/ring.Length;
        }

        public static bool IntrudesRoad(MojaveWorld world,GameObject go,Dictionary<Mesh,Vector3[]> cache,Dictionary<Mesh,Vector3[]> samples,Dictionary<Vector2Int,float> road)
        {
            bool Inside(Vector2 p,float margin) {
                var cell=new Vector2Int(Mathf.RoundToInt(p.x*2),Mathf.RoundToInt(p.y*2));
                if(!road.TryGetValue(cell,out float d))road[cell]=d=world.TrailDistance(new Vector2(cell.x*.5f,cell.y*.5f),out _,out _);
                // Query the actual point in the uncertain band; cache only definite inside/outside cells.
                if(d<margin-.5f)return true;if(d>margin+.5f)return false;
                return world.TrailDistance(p,out _,out _) < margin;
            }
            var b=BoundsOf(go);float radius=new Vector2(b.extents.x,b.extents.z).magnitude;
            if(world.TrailDistance(new Vector2(b.center.x,b.center.z),out _,out _)>radius+.35f)return false;
            bool solid=go.name.Contains("Rock")||go.name.Contains("Stone")||go.name.Contains("Boulder")||go.name.Contains("Rubble");
            if(!solid) {
                var root=PlantRoot(go,cache,samples);
                return Inside(new Vector2(root.x,root.z),.35f);
            }
            var lod=go.GetComponent<LODGroup>();
            foreach(var renderer in lod!=null?lod.GetLODs()[0].renderers:go.GetComponentsInChildren<Renderer>(true)) {
                if(renderer==null)continue;var filter=renderer.GetComponent<MeshFilter>();if(filter==null||filter.sharedMesh==null)continue;
                if(!cache.TryGetValue(filter.sharedMesh,out var vertices))cache[filter.sharedMesh]=vertices=Vertices(filter.sharedMesh);
                foreach(var v in vertices) {
                    var q=filter.transform.TransformPoint(v);
                    if(Inside(new Vector2(q.x,q.z),.25f))return true;
                }
            }
            return false;
        }
        public static int ClearRoadProps(MojaveWorld world)
        {
            int count=0;var cache=new Dictionary<Mesh,Vector3[]>();var samples=new Dictionary<Mesh,Vector3[]>();var road=new Dictionary<Vector2Int,float>();
            foreach(var go in PropRoots(world)) {
                if(!go.activeInHierarchy)continue;
                var b=BoundsOf(go);bool outside=world.mapDimensions!=Vector2.zero&&(!world.InMap(new Vector2(b.min.x,b.min.z),.5f)||!world.InMap(new Vector2(b.max.x,b.max.z),.5f));
                if(!outside&&!IntrudesRoad(world,go,cache,samples,road))continue;
                go.SetActive(false);if(Application.isPlaying)UnityEngine.Object.Destroy(go);else UnityEngine.Object.DestroyImmediate(go);count++;
            }
            return count;
        }

        public static void FinishSurface(MojaveWorld world,float[,] heights)
        {
            int n=heights.GetLength(0);float step=world.MapSize/(n-1),half=world.MapSize*.5f;
            // Average a few metres across the bank; the old fixed-edge limiter cannot round this crease.
            var sum=new double[n+1,n+1];
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)sum[z+1,x+1]=heights[z,x]+sum[z,x+1]+sum[z+1,x]-sum[z,x];
            for(int z=0;z<n;z++)for(int x=0;x<n;x++) {
                var p=new Vector2(x*step-half,z*step-half);
                float rd=world.layout.RoomDistance(p,out _),tr=world.TrailDistance(p,out _,out _);
                float land=world.layout.Noise(p,.032f,1171),width=Mathf.Lerp(3.5f,7,land);
                float weight=Smooth(-3,-.6f,tr)*(1-Smooth(2,width,tr))*Smooth(-1,4,rd);
                if(weight>0) {
                    int radius=Mathf.Max(1,Mathf.RoundToInt(Mathf.Lerp(1.3f,2.8f,land)/step));
                    int x0=Mathf.Max(0,x-radius),x1=Mathf.Min(n,x+radius+1),z0=Mathf.Max(0,z-radius),z1=Mathf.Min(n,z+radius+1);
                    float average=(float)((sum[z1,x1]-sum[z0,x1]-sum[z1,x0]+sum[z0,x0])/((x1-x0)*(z1-z0)));
                    heights[z,x]=Mathf.Lerp(heights[z,x],average,weight*.9f);
                }
                if(world.playableRelief) {
                    float relief=(world.layout.Noise(p,.038f,1193)-.5f)*1.25f+(world.layout.Noise(p,.083f,1199)-.5f)*.22f;
                    heights[z,x]+=relief*(1-Smooth(0,8,world.layout.CombineDistance(rd,tr)))/MojaveLayout.Elevation;
                }
            }
        }

        public static void GroundProps(MojaveWorld world,Func<GameObject,bool> include=null)
        {
            world.groundedPropCount=world.conformedMeshCount=world.overlappingPropCount=0;
            var cache=new Dictionary<Mesh,Vector3[]>();
            // Source geometry is shared; terrain contact remains specific to each instance.
            var plantNames=new HashSet<string>(world.catalog.cacti.Concat(world.catalog.joshua).Where(g=>g!=null).Select(g=>g.name));
            var samples=new Dictionary<Mesh,Vector3[]>();var islandCache=new Dictionary<Mesh,int[]>();
            var backdrops=world.GetComponent<MojaveBackdropSet>();
            bool preservePlayBoundary=backdrops!=null&&backdrops.library!=null&&backdrops.library.useAllNaturalCandidates;
            bool Rock(GameObject g)=>g.name.Contains("Rock")||g.name.Contains("Stone")||g.name.Contains("Boulder");
            // Ground larger solid forms first. Later small props cannot claim space inside their bodies.
            var solids=new List<(Bounds bounds,Vector2[] outline)>();
            var solidCells=new Dictionary<Vector2Int,List<int>>();
            var partCache=new Dictionary<Mesh,Vector3[][]>();
            Vector2Int Cell(Vector3 p)=>new Vector2Int(Mathf.FloorToInt(p.x/8),Mathf.FloorToInt(p.z/8));
            void AddSolid(Bounds bounds,Vector2[] outline) {
                int index=solids.Count;solids.Add((bounds,outline));var min=Cell(bounds.min);var max=Cell(bounds.max);
                for(int z=min.y;z<=max.y;z++)for(int x=min.x;x<=max.x;x++) {
                    var key=new Vector2Int(x,z);if(!solidCells.TryGetValue(key,out var list))solidCells[key]=list=new List<int>();list.Add(index);
                }
            }
            void AddRock(GameObject go) {
                var lod=go.GetComponent<LODGroup>();
                foreach(var renderer in lod!=null?lod.GetLODs()[0].renderers:go.GetComponentsInChildren<Renderer>(true)) {
                    if(renderer==null)continue;var filter=renderer.GetComponent<MeshFilter>();if(filter==null||filter.sharedMesh==null)continue;
                    var mesh=filter.sharedMesh;
                    if(!partCache.TryGetValue(mesh,out var parts)) {
                        if(!cache.TryGetValue(mesh,out var vertices))cache[mesh]=vertices=Vertices(mesh);
                        if(!islandCache.TryGetValue(mesh,out var islands))islandCache[mesh]=islands=MeshIslands(mesh,vertices);
                        parts=Enumerable.Range(0,vertices.Length).GroupBy(i=>islands[i]).Select(g=>g.Select(i=>vertices[i]).ToArray()).ToArray();partCache[mesh]=parts;
                    }
                    foreach(var part in parts) {
                        if(part.Length<4)continue;
                        var points=new List<Vector2>();var bounds=new Bounds(filter.transform.TransformPoint(part[0]),Vector3.zero);
                        foreach(var v in part){var q=filter.transform.TransformPoint(v);bounds.Encapsulate(q);points.Add(new Vector2(q.x,q.z));}
                        var hull=Hull(points);if(hull.Length>=3&&bounds.size.y>.15f)AddSolid(bounds,hull);
                    }
                }
            }
            foreach(var go in PropRoots(world).OrderByDescending(Rock).ThenByDescending(g=>{var b=BoundsOf(g);return b.size.x*b.size.y*b.size.z;})) {
                if(include!=null&&!include(go))continue;
                if(go.GetComponentsInChildren<Renderer>(true).Length==0)continue;
                var b=BoundsOf(go);var p=new Vector2(b.center.x,b.center.z);
                bool rubble=go.name.Contains("Rubble");
                bool rock=go.name.Contains("Rock")||go.name.Contains("Stone")||go.name.Contains("Boulder");
                if(rubble) {
                    // Ground wide rubble patches column by column, using one base for every LOD.
                    foreach(var filter in go.GetComponentsInChildren<MeshFilter>(true)) {
                        var source=filter.sharedMesh;if(source==null)continue;
                        if(!cache.TryGetValue(source,out var vertices)){vertices=Vertices(source);cache[source]=vertices;}
                        if(!islandCache.TryGetValue(source,out var islands))islandCache[source]=islands=MeshIslands(source,vertices);
                        var floors=new Dictionary<int,float>();
                        for(int i=0;i<vertices.Length;i++) {float y=filter.transform.TransformPoint(vertices[i]).y;int id=islands[i];if(!floors.ContainsKey(id)||y<floors[id])floors[id]=y;}
                        var shifted=new Vector3[vertices.Length];
                        for(int i=0;i<vertices.Length;i++) {
                            var v=filter.transform.TransformPoint(vertices[i]);
                            v.y+=world.Ground(new Vector2(v.x,v.z)).y-floors[islands[i]]-.045f;
                            shifted[i]=filter.transform.InverseTransformPoint(v);
                        }
                        var mesh=UnityEngine.Object.Instantiate(source);mesh.name="Grounded_"+source.name;
                        mesh.vertices=shifted;mesh.RecalculateBounds();mesh.RecalculateNormals();
                        filter.sharedMesh=mesh;world.OwnGroundMesh(mesh);world.conformedMeshCount++;
                        foreach(var collider in go.GetComponentsInChildren<MeshCollider>(true))if(collider.sharedMesh==source)collider.sharedMesh=mesh;
                    }
                } else {
                    var rotation=go.transform.rotation;
                    if(rock&&!MojaveWorld.IsLooseStoneName(go.name)) {
                        float radius=Mathf.Clamp(Mathf.Max(b.extents.x,b.extents.z)*.65f,.5f,6);
                        float dx=(world.Ground(p+Vector2.right*radius).y-world.Ground(p-Vector2.right*radius).y)/(radius*2);
                        float dz=(world.Ground(p+Vector2.up*radius).y-world.Ground(p-Vector2.up*radius).y)/(radius*2);
                        var tilt=Quaternion.FromToRotation(Vector3.up,new Vector3(-dx,1,-dz).normalized);
                        tilt=Quaternion.RotateTowards(Quaternion.identity,tilt,18);
                        go.transform.rotation=tilt*rotation;
                        if(!ClearOfPlay(world,BoundsOf(go),0)&&(preservePlayBoundary||ClearOfPlay(world,b,0)))go.transform.rotation=rotation;
                    }
                    world.LimitLooseStone(go);
                    var feet=Feet(go,cache,samples);
                    if(feet.Count==0)throw new InvalidOperationException("No grounding samples: "+go.name);
                    float gap=float.NegativeInfinity;
                    foreach(var foot in feet) {
                        var q=foot.transform.TransformPoint(foot.point);
                        gap=Mathf.Max(gap,q.y-world.Ground(new Vector2(q.x,q.z)).y);
                    }
                    if(plantNames.Contains(go.name)||go.name.StartsWith("Joshua")||go.name.StartsWith("Saguaro")||go.name.StartsWith("Cuctas")||go.name.StartsWith("Piko")) {
                        var root=PlantRoot(go,cache,samples);
                        go.transform.position+=Vector3.up*(world.Ground(new Vector2(root.x,root.z)).y-root.y-.025f);
                    } else go.transform.position-=Vector3.up*(gap+(rock?.10f:.035f));
                }
                if(world.refinedRoads&&!rubble) {
                    if(rock)AddRock(go);
                    else {
                        // Inspect the actual lowest stem points, allowing foliage and shallow edge contact.
                        var feet=Feet(go,cache,samples);var basePoints=feet.Select(f=>f.transform.TransformPoint(f.point)).ToArray();
                        if(basePoints.Length>0) {
                            float bottom=basePoints.Min(q=>q.y);var roots=basePoints.Where(q=>q.y<bottom+.06f).ToArray();
                            var root=roots.Aggregate(Vector3.zero,(sum,q)=>sum+q)/roots.Length;
                            bool buried=false;
                            if(solidCells.TryGetValue(Cell(root),out var list))foreach(int index in list) {
                                var solid=solids[index];
                                if(root.y+.25f<solid.bounds.min.y||root.y>solid.bounds.max.y-.05f)continue;
                                if(MojavePatch.OutlineDistance(solid.outline,new Vector2(root.x,root.z))<-.18f){buried=true;break;}
                            }
                            if(buried) {
                                go.SetActive(false);if(Application.isPlaying)UnityEngine.Object.Destroy(go);else UnityEngine.Object.DestroyImmediate(go);
                                world.overlappingPropCount++;continue;
                            }
                            // Trunks share the same large-first rule; overlapping crowns remain natural.
                            var contact=BoundsOf(go);float radius=Mathf.Clamp(Mathf.Min(contact.size.x,contact.size.z)*.10f,.08f,.3f);
                            var hull=Enumerable.Range(0,8).Select(i=>new Vector2(root.x,root.z)+new Vector2(Mathf.Cos(i*Mathf.PI*.25f),Mathf.Sin(i*Mathf.PI*.25f))*radius).ToArray();
                            AddSolid(new Bounds(root+Vector3.up*.4f,new Vector3(radius*2,.8f,radius*2)),hull);
                        }
                    }
                }
                foreach(var lod in go.GetComponentsInChildren<LODGroup>(true))lod.RecalculateBounds();
                world.groundedPropCount++;
            }
        }
#if UNITY_EDITOR
        public static void PersistMeshes(MojaveWorld world,string terrainPath)
        {
            var saved=new Dictionary<Mesh,Mesh>();
            foreach(var filter in world.generatedRoot.GetComponentsInChildren<MeshFilter>(true)) {
                var mesh=filter.sharedMesh;
                if(mesh==null||!mesh.name.StartsWith("Grounded_",StringComparison.Ordinal)||UnityEditor.AssetDatabase.Contains(mesh))continue;
                if(!saved.TryGetValue(mesh,out var copy)) {
                    copy=UnityEngine.Object.Instantiate(mesh);copy.hideFlags=HideFlags.None;
                    UnityEditor.AssetDatabase.AddObjectToAsset(copy,terrainPath);saved.Add(mesh,copy);
                }
                filter.sharedMesh=copy;
            }
            foreach(var collider in world.generatedRoot.GetComponentsInChildren<MeshCollider>(true))
                if(collider.sharedMesh!=null&&saved.TryGetValue(collider.sharedMesh,out var copy))collider.sharedMesh=copy;
            UnityEditor.AssetDatabase.SaveAssetIfDirty(world.surface.terrainData);
        }
#endif
    }
}

