using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Overburst.Mojave
{
    // Islands share the expedition graph, but its coast and causeways are one dry land field.
    public sealed class IslandBiome : MonoBehaviour
    {
        [Serializable] public sealed class Islet
        {
            public int tile;
            public Vector2 center;
            public float rotation, height, radius;
            public Vector2 Local(Vector2 p)=>Rotate(p-center,-rotation);
        }
        public MojavePatch[] backgroundTiles;
        public Material oceanMaterial;
        public Mesh oceanMesh;
        public GameObject[] shoreCliffs;
        public float seaLevel=6;
        public List<Islet> islets=new List<Islet>();
        public int combatProps, backgroundProps, shorelineProps;
        public Vector2[] combatRadii={new Vector2(9,11),new Vector2(11,9),new Vector2(13,15),new Vector2(15,13),new Vector2(18,20),new Vector2(20,18)};
        readonly List<Vector3> obstacles=new List<Vector3>();

        static float Smooth(float a,float b,float v)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,v));
        static Vector2 Rotate(Vector2 p,float degrees){float a=degrees*Mathf.Deg2Rad;return new Vector2(p.x*Mathf.Cos(a)-p.y*Mathf.Sin(a),p.x*Mathf.Sin(a)+p.y*Mathf.Cos(a));}
        public void ConfigureLayout(MojaveLayout layout)
        {
            foreach(var p in layout.places) {
                p.radius=combatRadii[p.patch%combatRadii.Length];
                p.kind=p.patch%3==1?MojaveCombatKind.AsymmetricHollow:MojaveCombatKind.OpenBasin;
                p.height=seaLevel+2.3f+layout.Noise(p.center,.032f,141)*2.8f;
                p.name="Island "+(layout.places.IndexOf(p)+1)+" · "+(p.radius.x<12?"small":p.radius.x<17?"medium":"large");
            }
        }
        public void ShapeCauseways(MojaveLayout layout)
        {
            foreach(var t in layout.trails)for(int k=0;k<t.points.Length;k++) {
                float u=k/(float)(t.points.Length-1);
                float shore=Mathf.Lerp(layout.places[t.from].height,layout.places[t.to].height,Smooth(0,1,u));
                float middle=Mathf.Pow(Mathf.Clamp01(Mathf.Sin(u*Mathf.PI)),.8f);
                float h=Mathf.Lerp(shore,seaLevel+.80f+layout.Noise(t.points[k],.045f,407)*.5f,middle);
                float d=layout.RoomDistance(t.points[k],out var room);
                t.heights[k]=Mathf.Lerp(h,room.height,1-Smooth(-3,15,d));
                t.widths[k]=Mathf.Lerp(2.6f,4.3f,layout.Noise(t.points[k],.038f,411));
            }
        }
        public void Build(MojaveWorld world,Action<MojaveGenerationStage> checkpoint)
        {
            obstacles.Clear();combatProps=backgroundProps=shorelineProps=0;
            Plan(world);checkpoint?.Invoke(MojaveGenerationStage.BackgroundPlan);
            Surface(world);checkpoint?.Invoke(MojaveGenerationStage.Surface);
            foreach(var place in world.layout.places) {
                var patch=world.catalog.patches[place.patch];
                var parent=new GameObject(place.name+" · tuned combat tile").transform;parent.SetParent(world.generatedRoot,false);
                foreach(var item in patch.placements) {
                    var p=place.World(new Vector2(item.position.x,item.position.z));
                    bool solid=item.kind==MojavePropKind.Boulder||item.kind==MojavePropKind.Tree;
                    float clearance=item.kind==MojavePropKind.Tree?.65f:item.radius;
                    if(solid&&(place.Distance(p)<clearance+1.8f||world.TrailDistance(p,out _,out _)<clearance+1.5f))continue;
                    if(!solid&&place.Distance(p)<-1&&item.height>.45f)continue;
                    if(world.Ground(p).y<seaLevel+.35f||Vector2.Distance(p,place.center)>patch.size*.65f)continue;
                    Spawn(world,item,p,Quaternion.Euler(0,-place.rotation,0)*item.rotation,parent,solid);combatProps++;
                }
            }
            checkpoint?.Invoke(MojaveGenerationStage.CombatTiles);
            foreach(var site in islets) {
                var patch=backgroundTiles[site.tile];var parent=new GameObject("Background islet · "+patch.name).transform;parent.SetParent(world.generatedRoot,false);
                foreach(var item in patch.placements) {
                    var local=new Vector2(item.position.x,item.position.z);var p=site.center+Rotate(local,site.rotation);
                    if(!Within(world,p,4)||world.PlayDistance(p)<item.radius+5)continue;
                    if(world.Ground(p).y<seaLevel+.2f&&item.kind!=MojavePropKind.Boulder)continue;
                    Spawn(world,item,p,Quaternion.Euler(0,-site.rotation,0)*item.rotation,parent,true);backgroundProps++;
                }
            }
            Shoreline(world);checkpoint?.Invoke(MojaveGenerationStage.Dressing);
            world.blockers=obstacles.ToArray();world.authoredPlacementCount=combatProps+backgroundProps;world.dressingCount=shorelineProps;
            checkpoint?.Invoke(MojaveGenerationStage.Backdrops);
            // Demo cliffs have deeply buried pivots. Their original terrain offset cannot be
            // applied to a new coast; seat the rock base below sea level instead.
            foreach(Transform group in world.generatedRoot)if(group.name.StartsWith("Background islet",StringComparison.Ordinal))
                foreach(Transform prop in group)if(prop.name.Contains("IslandClif")||prop.name.Contains("RockJungle")) {
                    var renderers=prop.GetComponentsInChildren<Renderer>(true);if(renderers.Length==0)continue;
                    var b=renderers[0].bounds;foreach(var part in renderers)b.Encapsulate(part.bounds);
                    prop.position+=Vector3.up*(seaLevel-1.5f-b.min.y);
                }
            GroundCliffVegetation(world);
            checkpoint?.Invoke(MojaveGenerationStage.Grounding);
            var water=new GameObject("Island ocean · sea level "+seaLevel);water.transform.SetParent(world.generatedRoot,false);water.transform.position=Vector3.up*seaLevel;
            water.transform.localScale=new Vector3(world.MapSize*2,1,world.MapSize*2);
            water.AddComponent<MeshFilter>().sharedMesh=oceanMesh;
            var renderer=water.AddComponent<MeshRenderer>();renderer.sharedMaterial=oceanMaterial;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            world.waterSurfaceCount=1;
        }
        void Plan(MojaveWorld world)
        {
            islets.Clear();if(backgroundTiles==null||backgroundTiles.Length==0)throw new InvalidOperationException("Missing island background tiles");
            var r=new System.Random(world.seed^0xAB174);float Range(float a,float b)=>Mathf.Lerp(a,b,(float)r.NextDouble());
            // Full formations get their space before small coastal rocks are considered.
            for(int tier=0;tier<backgroundTiles.Length;tier++)for(int attempt=0;attempt<140;attempt++) {
                int tile=tier%backgroundTiles.Length;var patch=backgroundTiles[tile];float radius=patch.size*.43f;
                var p=new Vector2(Range(-world.MapSize*.5f+radius,world.MapSize*.5f-radius),Range(-world.MapSize*.5f+radius,world.MapSize*.5f-radius));
                if(world.PlayDistance(p)<radius+9)continue;
                bool hit=false;foreach(var other in islets)if(Vector2.Distance(p,other.center)<radius+other.radius+8){hit=true;break;}if(hit)continue;
                islets.Add(new Islet{tile=tile,center=p,rotation=Range(0,360),height=seaLevel+Range(5,9),radius=radius});break;
            }
        }
        void Surface(MojaveWorld world)
        {
            int n=world.MapSize>320?1025:513;int a=n-1;var heights=new float[n,n];var paint=new float[a,a,world.catalog.layers.Length];var sample=new float[world.catalog.layers.Length];
            for(int z=0;z<n;z++)for(int x=0;x<n;x++) {
                var p=new Vector2(x/(float)(n-1)-.5f,z/(float)(n-1)-.5f)*world.MapSize;
                float seabed=seaLevel-5.5f+world.layout.Noise(p,.024f,151)*1.8f;
                float h=seabed;float rd=world.layout.RoomDistance(p,out var nearest);float tr=world.TrailDistance(p,out float road,out _);
                float landWeight=0;MojavePatch painted=null;Vector2 paintedLocal=Vector2.zero;
                foreach(var place in world.layout.places) {
                    var patch=world.catalog.patches[place.patch];var local=place.Local(p);float d=place.Distance(p);
                    float coastline=12+world.layout.Noise(p,.068f,139)*8;
                    float coastDistance=d+(world.layout.Noise(p,.041f,159)-.5f)*7*Smooth(-1,5,d);
                    float support=1-Smooth(6,coastline+13,coastDistance);
                    float relief=patch.Height(local);float court=1-Smooth(-1,7,d);
                    float interior=place.height+Mathf.Lerp(Mathf.Clamp(relief*.72f,-1.2f,6),Mathf.Clamp(relief*.12f,-.22f,.35f),court);
                    float shelf=Mathf.Lerp(seabed,interior,support);
                    h=Mathf.Max(h,shelf);
                    if(support>landWeight){landWeight=support;painted=patch;paintedLocal=local;}
                }
                foreach(var site in islets) {
                    var patch=backgroundTiles[site.tile];var local=site.Local(p);float d=local.magnitude+(world.layout.Noise(p,.048f,164)-.5f)*8;
                    float support=1-Smooth(site.radius*.55f,site.radius+10,d);
                    float island=site.height+Mathf.Clamp(patch.Height(local)*.65f,-2.5f,12);
                    h=Mathf.Max(h,Mathf.Lerp(seabed,island,support));
                    if(support>landWeight){landWeight=support;painted=patch;paintedLocal=local;}
                }
                float roadWeight=1-Smooth(-.3f,7+world.layout.Noise(p,.06f,178)*3,tr);
                float roadShelf=Mathf.Lerp(seabed,road+(world.layout.Noise(p,.09f,201)-.5f)*.12f,roadWeight);
                h=Mathf.Max(h,roadShelf);
                // Only the walkable strip is levelled to the longitudinal profile; shoreline is free to slope.
                float roadCore=1-Smooth(-.3f,1.5f,tr);
                float roomCore=1-Smooth(-3,0,rd);
                h=Mathf.Lerp(h,road,roadCore*(1-roomCore));
                if(float.IsNaN(h)||float.IsInfinity(h))throw new InvalidOperationException("Invalid island height at "+p);
                heights[z,x]=Mathf.Clamp(h,0,47)/MojaveLayout.Elevation;
                if(x==a||z==a)continue;
                Array.Clear(sample,0,sample.Length);
                if(painted!=null)painted.Paint(paintedLocal,sample);
                float dry=Smooth(seaLevel-2,seaLevel-.2f,h);float sourceWeight=landWeight*dry*.76f;
                for(int l=0;l<sample.Length;l++)paint[z,x,l]=sample[l]*sourceWeight;
                paint[z,x,3]+=(1-sourceWeight)*(1-dry);
                paint[z,x,1]+=(1-sourceWeight)*dry*.72f;paint[z,x,0]+=(1-sourceWeight)*dry*.28f;
                float wear=(1-Smooth(-1.2f,2.4f,tr))*(.55f+world.layout.Noise(p,.13f,172)*.2f)*Smooth(-7,2,rd);
                for(int l=0;l<sample.Length;l++)paint[z,x,l]*=1-wear;
                paint[z,x,0]+=wear*.45f;paint[z,x,1]+=wear*.42f;paint[z,x,2]+=wear*.13f;
                float sum=0;for(int l=0;l<sample.Length;l++)sum+=paint[z,x,l];
                for(int l=0;l<sample.Length;l++)paint[z,x,l]/=Mathf.Max(sum,.0001f);
            }
            var data=new TerrainData{name="Islands continuous coast · "+world.seed,heightmapResolution=n,alphamapResolution=a,baseMapResolution=512,size=new Vector3(world.MapSize,48,world.MapSize),terrainLayers=world.catalog.layers};
            data.SetHeights(0,0,heights);data.SetAlphamaps(0,0,paint);
            var go=Terrain.CreateTerrainGameObject(data);go.name="Continuous islands and sand causeways";go.transform.SetParent(world.generatedRoot,false);go.transform.position=new Vector3(-world.MapSize*.5f,0,-world.MapSize*.5f);
            var terrain=go.GetComponent<Terrain>();terrain.materialTemplate=world.catalog.terrainMaterial;terrain.drawInstanced=false;terrain.heightmapPixelError=2;terrain.basemapDistance=1200;
            world.AcceptGeneratedSurface(terrain,heights,paint);
        }
        void Spawn(MojaveWorld world,MojavePlacement item,Vector2 p,Quaternion rotation,Transform parent,bool solid)
        {
            if(item.prefab==null)return;
            var go=Instantiate(item.prefab,world.Ground(p,item.position.y),rotation,parent);go.name=item.prefab.name;go.transform.localScale=item.scale;
            foreach(var collider in go.GetComponentsInChildren<Collider>(true))collider.enabled=false;
            foreach(var lod in go.GetComponentsInChildren<LODGroup>(true))lod.fadeMode=LODFadeMode.None;
            if(solid)obstacles.Add(new Vector3(p.x,p.y,item.kind==MojavePropKind.Tree?.65f:item.radius));
        }
        static void GroundCliffVegetation(MojaveWorld world)
        {
            var supports=new List<MeshCollider>();
            try {
                foreach(Transform group in world.generatedRoot)if(group.name.StartsWith("Background islet",StringComparison.Ordinal))
                    foreach(Transform prop in group)if(prop.name.Contains("IslandClif")||prop.name.Contains("RockJungle")) {
                        var lod=prop.GetComponent<LODGroup>();
                        var renderers=lod!=null?lod.GetLODs()[0].renderers:prop.GetComponentsInChildren<Renderer>(true);
                        foreach(var part in renderers){var filter=part.GetComponent<MeshFilter>();if(filter==null||filter.sharedMesh==null)continue;var support=part.gameObject.AddComponent<MeshCollider>();support.sharedMesh=filter.sharedMesh;supports.Add(support);}
                    }
                Physics.SyncTransforms();
                float Floor(Vector3 p){float y=world.Ground(new Vector2(p.x,p.z)).y;var ray=new Ray(new Vector3(p.x,200,p.z),Vector3.down);foreach(var support in supports)if(support.Raycast(ray,out var hit,250))y=Mathf.Max(y,hit.point.y);return y;}
                IslandGrounding.Apply(world,Floor);
            }finally{foreach(var support in supports){support.enabled=false;if(Application.isPlaying)Destroy(support);else DestroyImmediate(support);}}
        }
        static bool Within(MojaveWorld world,Vector2 p,float inset)=>Mathf.Abs(p.x)<world.MapSize*.5f-inset&&Mathf.Abs(p.y)<world.MapSize*.5f-inset;
        void Shoreline(MojaveWorld world)
        {
            var parent=new GameObject("Coastal boulders and palm shoulders").transform;parent.SetParent(world.generatedRoot,false);
            var random=new System.Random(world.seed^0x731BC);float Range(float a,float b)=>Mathf.Lerp(a,b,(float)random.NextDouble());
            var prefabs=world.catalog.boulders;var bounds=new Dictionary<GameObject,Bounds>();
            for(float z=-world.MapSize*.5f+12;z<world.MapSize*.5f-12;z+=5.5f)for(float x=-world.MapSize*.5f+12;x<world.MapSize*.5f-12;x+=5.5f) {
                var p=new Vector2(x+Range(-2,2),z+Range(-2,2));float rd=world.layout.RoomDistance(p,out _);float tr=world.TrailDistance(p,out _,out _);float y=world.Ground(p).y;
                if(rd<1.6f||tr<1.8f||y<seaLevel+.22f||y>seaLevel+8)continue;
                if(world.layout.Noise(p,.095f,315)<.44f||random.NextDouble()>.60)continue;
                double pick=random.NextDouble();bool palm=pick<.30;bool cliff=!palm&&pick<.57&&shoreCliffs!=null&&shoreCliffs.Length>0;
                var pool=palm?world.catalog.joshua:cliff?shoreCliffs:prefabs;if(pool.Length==0)continue;
                var prefab=pool[random.Next(pool.Length)];
                if(!bounds.TryGetValue(prefab,out var b)){var rs=prefab.GetComponentsInChildren<Renderer>(true);b=rs[0].bounds;for(int i=1;i<rs.Length;i++)b.Encapsulate(rs[i].bounds);bounds[prefab]=b;}
                float scale=palm?Range(.80f,1.1f):cliff?Range(3.5f,6.5f)/Mathf.Max(b.extents.x,b.extents.z):Range(.80f,1.25f);float radius=palm?.65f:Mathf.Max(b.extents.x,b.extents.z)*scale;
                if(rd<radius+1.5f||tr<radius+1.0f)continue;
                bool hit=false;foreach(var o in obstacles)if(Vector2.Distance(p,new Vector2(o.x,o.y))<o.z+radius+.7f){hit=true;break;}if(hit)continue;
                Spawn(world,new MojavePlacement{prefab=prefab,scale=Vector3.one*scale,radius=radius,kind=palm?MojavePropKind.Tree:MojavePropKind.Boulder},p,Quaternion.Euler(0,Range(0,360),0),parent,true);shorelineProps++;
                if(palm||cliff)for(int j=0;j<5;j++) {
                    var q=p+new Vector2(Range(-radius-2,radius+2),Range(-radius-2,radius+2));
                    if(world.PlayDistance(q)<1||world.Ground(q).y<seaLevel+.4f)continue;
                    var plants=j%2==0?world.catalog.shrubs:world.catalog.grasses;if(plants.Length==0)continue;
                    Spawn(world,new MojavePlacement{prefab=plants[random.Next(plants.Length)],scale=Vector3.one*Range(.65f,1.1f),kind=MojavePropKind.Plant},q,Quaternion.Euler(0,Range(0,360),0),parent,false);shorelineProps++;
                }
            }
        }
    }
}
