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
            if(world.refinedRoads){PlanRoadsideColonies(world);return;}
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
        static void PlanRoadsideColonies(MojaveWorld world)
        {
            var prefabs=world.catalog.boulders.Where(p=>p.name.Contains("RockAssemble")).ToArray();
            var outlines=new Dictionary<GameObject,Vector2[]>();var bounds=new Dictionary<GameObject,Bounds>();
            Vector2[] Outline(GameObject prefab) {
                if(outlines.TryGetValue(prefab,out var result))return result;
                var points=new List<Vector2>();var lod=prefab.GetComponent<LODGroup>();
                var renderers=lod!=null?lod.GetLODs()[0].renderers:prefab.GetComponentsInChildren<Renderer>(true);
                foreach(var renderer in renderers) {
                    if(renderer==null)continue;var mf=renderer.GetComponent<MeshFilter>();if(mf==null||mf.sharedMesh==null)continue;
                    foreach(var v in Vertices(mf.sharedMesh)) {var q=mf.transform.TransformPoint(v)-prefab.transform.position;points.Add(new Vector2(q.x,q.z));}
                }
                bounds[prefab]=BoundsOf(prefab);outlines[prefab]=result=Hull(points);return result;
            }
            Vector2[] Placed(MojavePlacement a) {
                var matrix=Matrix4x4.TRS(a.position,a.rotation,a.scale);
                return Outline(a.prefab).Select(v=>{var p=matrix.MultiplyPoint3x4(new Vector3(v.x,0,v.y));return new Vector2(p.x,p.z);}).ToArray();
            }
            var occupied=new List<Vector2[]>();
            var backdrops=world.GetComponent<MojaveBackdropSet>();
            foreach(var stamp in backdrops.stamps)foreach(var a in backdrops.NaturalPlacements(world,stamp)) {
                if(a.kind!=MojavePropKind.Boulder||a.height<1.4f||MojaveWorld.IsLooseStoneName(a.prefab.name))continue;
                if(a.rockOutline!=null&&a.rockOutline.Length>=3)occupied.Add(a.rockOutline.Select(stamp.World).ToArray());
                else {var p=stamp.World(new Vector2(a.position.x,a.position.z));var placed=a;placed.position=new Vector3(p.x,0,p.y);placed.rotation=backdrops.PlacementRotation(stamp,a.rotation);occupied.Add(Placed(placed));}
            }
            foreach(var place in world.layout.places)foreach(var source in world.catalog.patches[place.patch].placements) {
                if(source.kind!=MojavePropKind.Boulder||source.height<1.4f||MojaveWorld.IsLooseStoneName(source.prefab.name))continue;
                var p=place.World(new Vector2(source.position.x,source.position.z));
                if(world.layout.RoomDistance(p,out _)>20||world.TrailDistance(p,out _,out _)<source.radius+.7f)continue;
                var a=source;a.position=new Vector3(p.x,0,p.y);a.rotation=Quaternion.Euler(0,-place.rotation,0)*a.rotation;occupied.Add(Placed(a));
            }
            var random=new System.Random(world.seed^0x6159);
            float Range(float a,float b)=>Mathf.Lerp(a,b,(float)random.NextDouble());
            if(prefabs.Length==0)throw new InvalidOperationException("Mojave rock assemblies are required for roadside colonies.");
            var stations=new List<(Vector2 point,Vector2 normal,float width,float clearance,float yaw)>();
            // Each bank has its own colony centres; companions gather around an anchor instead of a paired row.
            foreach(var trail in world.layout.trails)for(int side=-1;side<=1;side+=2) {
                float next=Range(5,18),walked=0;
                for(int i=0;i<trail.points.Length-1;i++) {
                    var delta=trail.points[i+1]-trail.points[i];float length=delta.magnitude;if(length<.01f)continue;
                    var tangent=delta/length;var normal=new Vector2(-tangent.y,tangent.x)*side;
                    while(next<walked+length) {
                        var p=trail.points[i]+tangent*(next-walked);
                        if(world.layout.RoomDistance(p,out _)>3) {
                            float width=Range(8.5f,13.5f),yaw=Range(0,360);
                            stations.Add((p,normal,width,Range(.2f,1.8f),yaw));
                            int companions=random.Next(1,4);
                            for(int k=0;k<companions;k++) {
                                float along=Range(3.5f,11f)*(random.Next(2)==0?-1:1);
                                stations.Add((p+tangent*along,normal,width*Range(.42f,.78f),Range(.15f,3.6f),yaw+Range(-40,40)));
                            }
                        }
                        next+=Range(17f,34f);
                    }
                    walked+=length;
                }
            }
            // Fit the largest individual footprints first, retaining irregular sizes and edge setbacks.
            foreach(var station in stations.OrderByDescending(s=>s.width)) {
                if(world.shoulderMasses.Count>=world.layout.places.Count*10)break;
                float edge=0;while(edge<20&&world.PlayDistance(station.point+station.normal*edge)<.55f)edge+=.5f;
                if(edge>=20)continue;
                for(int attempt=0;attempt<3;attempt++) {
                    var prefab=prefabs[random.Next(prefabs.Length)];var shape=Outline(prefab);var source=bounds[prefab];
                    float scale=Mathf.Clamp(station.width/Mathf.Max(source.size.x,source.size.z),.3f,1);
                    var rotation=Quaternion.Euler(0,station.yaw+Range(-15,15),0);
                    var transformed=shape.Select(v=>{var q=rotation*new Vector3(v.x*scale,0,v.y*scale);return new Vector2(q.x,q.z);}).ToArray();
                    float inward=transformed.Min(v=>Vector2.Dot(v,station.normal));
                    var p=station.point+station.normal*(edge-inward+station.clearance);
                    var hull=transformed.Select(v=>v+p).ToArray();
                    if(hull.Any(v=>Mathf.Abs(v.x)>world.MapSize*.5f-5||Mathf.Abs(v.y)>world.MapSize*.5f-5))continue;
                    if(MojavePatch.OutlineSamples(hull,.6f).Any(v=>world.PlayDistance(v)<.5f))continue;
                    if(occupied.Any(other=>Overlap(hull,other)))continue;
                    var a=new MojavePlacement{prefab=prefab,position=new Vector3(p.x,0,p.y),rotation=rotation,scale=Vector3.one*scale,kind=MojavePropKind.Boulder,height=source.size.y*scale,rockOutline=hull};
                    var b=PlacementBounds(a);a.radius=new Vector2(b.extents.x,b.extents.z).magnitude;
                    world.shoulderMasses.Add(a);world.shoulderFootprints.Add(b);occupied.Add(hull);break;
                }
            }
        }
        public static void DressShoulders(MojaveWorld world)
        {
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
        public static void GroundProps(MojaveWorld world,Func<GameObject,bool> include=null)
        {
            world.groundedPropCount=world.conformedMeshCount=0;
            var cache=new Dictionary<Mesh,Vector3[]>();
            // Source geometry is shared; terrain contact remains specific to each instance.
            var samples=new Dictionary<Mesh,Vector3[]>();var islandCache=new Dictionary<Mesh,int[]>();
            var backdrops=world.GetComponent<MojaveBackdropSet>();
            bool preservePlayBoundary=backdrops!=null&&backdrops.library!=null&&backdrops.library.useAllNaturalCandidates;
            foreach(var go in PropRoots(world)) {
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
                    go.transform.position-=Vector3.up*(gap+(rock?.10f:.035f));
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

