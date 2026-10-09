using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overburst.Mojave
{
    // A connected membrane floor, with brood colonies occupying the non-combat field.
    public sealed class BiomassBiome : MonoBehaviour
    {
        [Serializable] public sealed class Colony { public int tile; public Vector2 center; public float rotation,radius; }
        public MojavePatch[] backgroundTiles;
        public GameObject[] shoulderPrefabs;
        public Vector2[] combatRadii={new Vector2(10,12),new Vector2(12,10),new Vector2(15,17),new Vector2(17,15),new Vector2(21,23),new Vector2(23,21)};
        public List<Colony> colonies=new List<Colony>();
        public int combatProps,backgroundProps,shoulderProps;
        readonly List<Vector3> obstacles=new List<Vector3>();
        readonly List<Vector3> occupied=new List<Vector3>();
        static float Smooth(float a,float b,float v)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,v));
        static Vector2 Rotate(Vector2 p,float a){a*=Mathf.Deg2Rad;return new Vector2(p.x*Mathf.Cos(a)-p.y*Mathf.Sin(a),p.x*Mathf.Sin(a)+p.y*Mathf.Cos(a));}
        public void ConfigureLayout(MojaveLayout layout)
        {
            var order=new List<int>();for(int i=0;i<combatRadii.Length;i++)order.Add(i);var random=new System.Random(layout.seed^0xBBC11);
            for(int i=order.Count-1;i>0;i--){int j=random.Next(i+1);int a=order[i];order[i]=order[j];order[j]=a;}
            foreach(var p in layout.places){p.patch=order[layout.places.IndexOf(p)%order.Count];p.radius=combatRadii[p.patch%combatRadii.Length];p.kind=MojaveCombatKind.AsymmetricHollow;p.height=7+layout.Noise(p.center,.021f,410)*3;p.name="Brood chamber "+(layout.places.IndexOf(p)+1);}
        }
        public void ShapeTrails(MojaveLayout layout)
        {
            foreach(var t in layout.trails)for(int i=0;i<t.points.Length;i++){
                float u=i/(float)(t.points.Length-1);float h=Mathf.Lerp(layout.places[t.from].height,layout.places[t.to].height,Smooth(0,1,u));
                h+=(layout.Noise(t.points[i],.037f,412)-.5f)*1.2f*Mathf.Sin(u*Mathf.PI);
                float d=layout.RoomDistance(t.points[i],out var room);t.heights[i]=Mathf.Lerp(h,room.height,1-Smooth(-2,10,d));
                t.widths[i]=Mathf.Lerp(3.2f,5.1f,layout.Noise(t.points[i],.038f,414));
            }
        }
        public void Build(MojaveWorld world,Action<MojaveGenerationStage> checkpoint)
        {
            occupied.Clear();obstacles.Clear();combatProps=backgroundProps=shoulderProps=0;
            Plan(world);checkpoint?.Invoke(MojaveGenerationStage.BackgroundPlan);
            Surface(world);checkpoint?.Invoke(MojaveGenerationStage.Surface);
            foreach(var place in world.layout.places){
                var parent=Group(world,place.name+" · combat");foreach(var item in world.catalog.patches[place.patch].placements){
                    var p=place.World(new Vector2(item.position.x,item.position.z));if(!Clear(world,p,item.radius,.4f))continue;
                    Spawn(world,item,p,Quaternion.Euler(0,-place.rotation,0)*item.rotation,parent);combatProps++;
                }
            }
            checkpoint?.Invoke(MojaveGenerationStage.CombatTiles);
            foreach(var site in colonies){
                var patch=backgroundTiles[site.tile];var parent=Group(world,"Background colony · "+patch.name);
                foreach(var item in patch.placements){var p=site.center+Rotate(new Vector2(item.position.x,item.position.z),site.rotation);Spawn(world,item,p,Quaternion.Euler(0,-site.rotation,0)*item.rotation,parent);backgroundProps++;}
            }
            checkpoint?.Invoke(MojaveGenerationStage.Backdrops);
            Shoulders(world);checkpoint?.Invoke(MojaveGenerationStage.Dressing);
            IslandGrounding.Apply(world,p=>world.Ground(new Vector2(p.x,p.z)).y);
            world.blockers=obstacles.ToArray();world.authoredPlacementCount=combatProps+backgroundProps;world.dressingCount=shoulderProps;
            checkpoint?.Invoke(MojaveGenerationStage.Grounding);
        }
        Transform Group(MojaveWorld w,string name){var t=new GameObject(name).transform;t.SetParent(w.generatedRoot,false);return t;}
        bool Clear(MojaveWorld w,Vector2 p,float r,float gap)=>Mathf.Abs(p.x)+r<w.MapSize*.5f-2&&Mathf.Abs(p.y)+r<w.MapSize*.5f-2&&w.PlayDistance(p)>r+gap;
        bool Occupied(Vector2 p,float r){foreach(var q in occupied)if(Vector2.Distance(p,new Vector2(q.x,q.y))<r+q.z+.2f)return true;return false;}
        void Plan(MojaveWorld w)
        {
            colonies.Clear();var random=new System.Random(w.seed^0xBAC19);float Range(float a,float b)=>Mathf.Lerp(a,b,(float)random.NextDouble());
            // Reserve actual prop footprints, not the empty rectangle surrounding the tile.
            for(int tier=0;tier<backgroundTiles.Length;tier++)for(int attempt=0;attempt<180;attempt++){
                var patch=backgroundTiles[tier];var p=new Vector2(Range(-w.MapSize*.48f,w.MapSize*.48f),Range(-w.MapSize*.48f,w.MapSize*.48f));float yaw=Range(0,360);bool valid=true;
                foreach(var item in patch.placements){var q=p+Rotate(new Vector2(item.position.x,item.position.z),yaw);if(!Clear(w,q,item.radius,.7f)||Occupied(q,item.radius)){valid=false;break;}}
                if(!valid)continue;float radius=0;foreach(var item in patch.placements){var q=p+Rotate(new Vector2(item.position.x,item.position.z),yaw);occupied.Add(new Vector3(q.x,q.y,item.radius));radius=Mathf.Max(radius,new Vector2(item.position.x,item.position.z).magnitude+item.radius);}
                colonies.Add(new Colony{tile=tier,center=p,rotation=yaw,radius=radius});
            }
        }
        void Surface(MojaveWorld w)
        {
            int n=w.MapSize>320?1025:513,a=n-1;var heights=new float[n,n];var paint=new float[a,a,w.catalog.layers.Length];
            for(int z=0;z<n;z++)for(int x=0;x<n;x++){
                var p=new Vector2(x/(float)(n-1)-.5f,z/(float)(n-1)-.5f)*w.MapSize;float h=6.5f+w.layout.Noise(p,.016f,420)*4.5f+w.layout.Noise(p,.068f,422)*.55f;
                float room=w.layout.RoomDistance(p,out var place),trail=w.TrailDistance(p,out float road,out _),play=w.layout.CombineDistance(room,trail);
                foreach(var site in colonies){float d=Vector2.Distance(p,site.center)/Mathf.Max(4,site.radius);h+=3.8f*(1-Smooth(.1f,1.65f,d));}
                float court=1-Smooth(-1,8,room),path=1-Smooth(-.2f,5+2*w.layout.Noise(p,.05f,428),trail);
                float target=place.height+(w.layout.Noise(p,.09f,424)-.5f)*.16f;h=Mathf.Lerp(h,target,court);
                h=Mathf.Lerp(h,road+(w.layout.Noise(p,.13f,426)-.5f)*.10f,path*(1-court));
                heights[z,x]=Mathf.Clamp(h,0,47)/48;if(x==a||z==a)continue;
                float veins=(1-Smooth(-.5f,2.5f,trail))*(.57f+.22f*w.layout.Noise(p,.14f,430));
                float membrane=(1-Smooth(-1,3,room))*(.60f+.12f*w.layout.Noise(p,.18f,431));
                float low=Mathf.Max(veins,membrane),growth=Smooth(0,7,play)*(.35f+.4f*w.layout.Noise(p,.07f,432));
                paint[z,x,0]=1-low;paint[z,x,1]=low;paint[z,x,2]=growth*(1-low);float sum=paint[z,x,0]+paint[z,x,1]+paint[z,x,2];for(int l=0;l<3;l++)paint[z,x,l]/=sum;
            }
            var td=new TerrainData{name="Biomass continuous membrane",heightmapResolution=n,alphamapResolution=a,baseMapResolution=512,size=new Vector3(w.MapSize,48,w.MapSize),terrainLayers=w.catalog.layers};
            td.SetHeights(0,0,heights);td.SetAlphamaps(0,0,paint);var go=Terrain.CreateTerrainGameObject(td);go.name="Living membrane floor";go.transform.SetParent(w.generatedRoot,false);go.transform.position=new Vector3(-w.MapSize*.5f,0,-w.MapSize*.5f);
            var terrain=go.GetComponent<Terrain>();terrain.materialTemplate=w.catalog.terrainMaterial;terrain.basemapDistance=1200;terrain.heightmapPixelError=3;w.AcceptGeneratedSurface(terrain,heights,paint);
        }
        void Spawn(MojaveWorld w,MojavePlacement item,Vector2 p,Quaternion rotation,Transform parent)
        {
            var go=Instantiate(item.prefab,parent);go.name=item.prefab.name;go.transform.SetPositionAndRotation(w.Ground(p),rotation);go.transform.localScale=item.scale;
            foreach(var c in go.GetComponentsInChildren<Collider>(true))c.enabled=false;
            if(item.height>.4f)obstacles.Add(new Vector3(p.x,p.y,item.radius));
        }
        void Shoulders(MojaveWorld w)
        {
            var parent=Group(w,"Vascular shoulders · brood nodes");var random=new System.Random(w.seed^0xB184);float Range(float a,float b)=>Mathf.Lerp(a,b,(float)random.NextDouble());
            var cache=new Dictionary<GameObject,Bounds>();
            foreach(var trail in w.layout.trails)for(int k=1;k<trail.points.Length-1;k+=2)for(int side=-1;side<=1;side+=2){
                if(random.NextDouble()<.15)continue;var direction=(trail.points[k+1]-trail.points[k-1]).normalized;var normal=new Vector2(-direction.y,direction.x)*side;
                var prefab=shoulderPrefabs[random.Next(shoulderPrefabs.Length)];if(!cache.TryGetValue(prefab,out var b)){var rs=prefab.GetComponentsInChildren<Renderer>(true);b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);cache[prefab]=b;}
                float scale=Range(1.15f,1.95f);float r2=Mathf.Max(b.extents.x,b.extents.z)*scale;var p=trail.points[k]+normal*(trail.widths[k]+r2+Range(.6f,1.8f));
                if(!Clear(w,p,r2,.35f)||Occupied(p,r2))continue;
                var item=new MojavePlacement{prefab=prefab,scale=prefab.transform.localScale*scale,rotation=Quaternion.Euler(0,Range(0,360),0),radius=r2,height=b.size.y*scale,kind=MojavePropKind.Boulder};
                Spawn(w,item,p,item.rotation,parent);occupied.Add(new Vector3(p.x,p.y,r2));shoulderProps++;
            }
        }
    }
}
