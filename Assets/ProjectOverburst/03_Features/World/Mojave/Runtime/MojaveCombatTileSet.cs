using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overburst.Mojave
{
    // Optional authored spaces for the polished tile library. Original Mojave and Oasis remain independent.
    public sealed class MojaveCombatTileSet : MonoBehaviour
    {
        [Serializable] public struct Rule
        {
            public MojavePatch patch;
            public MojaveCombatKind kind;
            public Vector2 radius;
            public float[] exits;
        }
        public Rule[] rules;
        public bool mixedSizes;
        [HideInInspector] public int trailSidePlacementCount;

        public void Apply(MojaveLayout layout,MojaveCatalog catalog)
        {
            if(rules==null)return;
            if(mixedSizes) {
                var random=new System.Random(layout.seed^0x361A2);
                void Shuffle<T>(List<T> list) {for(int i=list.Count-1;i>0;i--){int j=random.Next(i+1);var value=list[i];list[i]=list[j];list[j]=value;}}
                var tiers=new List<MojaveCombatSize>();
                for(int i=0;i<layout.places.Count;i++)tiers.Add((MojaveCombatSize)(i%3));
                Shuffle(tiers);
                var candidates=new List<int>[3];var used=new int[3];
                for(int tier=0;tier<3;tier++) {
                    candidates[tier]=new List<int>();
                    for(int i=0;i<catalog.patches.Length;i++)if((int)catalog.patches[i].combatSize==tier)candidates[tier].Add(i);
                    if(candidates[tier].Count==0)throw new InvalidOperationException("Missing combat size "+tier);
                    Shuffle(candidates[tier]);
                }
                for(int i=0;i<layout.places.Count;i++){int tier=(int)tiers[i];layout.places[i].patch=candidates[tier][used[tier]++%candidates[tier].Count];}
            }
            foreach(var place in layout.places)
                foreach(var rule in rules)
                    if(rule.patch==catalog.patches[place.patch]) {place.kind=rule.kind;place.radius=rule.radius;break;}
        }

        public void DressTrailSides(MojaveWorld world)
        {
            trailSidePlacementCount=0;
            if(world.refinedRoads||rules==null||rules.Length==0)return;
            var parent=new GameObject("Trail shoulders · broken stone and scrub colonies").transform;
            parent.SetParent(world.generatedRoot,false);
            var random=new System.Random(world.seed^0x4172);
            var boundsCache=new Dictionary<GameObject,Bounds>();
            float Range(float a,float b)=>Mathf.Lerp(a,b,(float)random.NextDouble());
            GameObject Pick(GameObject[] prefabs)=>prefabs==null||prefabs.Length==0?null:prefabs[random.Next(prefabs.Length)];
            void Add(GameObject prefab,Vector2 p,float height,float embed)
            {
                if(prefab==null||Mathf.Abs(p.x)>world.MapSize*.5f-6||Mathf.Abs(p.y)>world.MapSize*.5f-6)return;
                if(!boundsCache.TryGetValue(prefab,out var bounds)) {
                    var renderers=prefab.GetComponentsInChildren<Renderer>(true);if(renderers.Length==0)return;
                    bounds=renderers[0].bounds;for(int k=1;k<renderers.Length;k++)bounds.Encapsulate(renderers[k].bounds);
                    boundsCache[prefab]=bounds;
                }
                float scale=height/Mathf.Max(.01f,bounds.size.y);
                float radius=new Vector2(bounds.extents.x,bounds.extents.z).magnitude*scale;
                if(mixedSizes)radius+=new Vector2(bounds.center.x-prefab.transform.position.x,bounds.center.z-prefab.transform.position.z).magnitude*scale;
                if(world.terrainFinish&&MojaveTerrainFinish.Reserved(world,p,radius))return;
                if(world.layout.TrailDistance(p,out _,out _) < radius+.65f||world.layout.RoomDistance(p,out _)<-4.5f)return;
                foreach(var blocker in world.blockers??Array.Empty<Vector3>())
                    if(Vector2.Distance(p,new Vector2(blocker.x,blocker.y))<blocker.z*.5f+radius)return;
                var go=Instantiate(prefab,world.Ground(p,embed),Quaternion.Euler(0,Range(0,360),0),parent);
                go.name="TrailSide_"+prefab.name;go.transform.localScale=Vector3.one*scale;
                if(prefab.name.Contains("Rock")||prefab.name.Contains("Stone")||prefab.name.Contains("Boulder"))world.SeatRock(go);
                foreach(var collider in go.GetComponentsInChildren<Collider>(true))collider.enabled=false;
                foreach(var lod in go.GetComponentsInChildren<LODGroup>(true))lod.fadeMode=LODFadeMode.None;
                trailSidePlacementCount++;
            }
            foreach(var trail in world.layout.trails) {
                float next=mixedSizes?Range(8,12):Range(4,7),travelled=0;
                for(int i=0;i<trail.points.Length-1;i++) {
                    var a=trail.points[i];var delta=trail.points[i+1]-a;float length=delta.magnitude;if(length<.001f)continue;
                    var tangent=delta/length;var normal=new Vector2(-tangent.y,tangent.x);
                    while(next<travelled+length) {
                        float t=(next-travelled)/length;var center=a+delta*t;
                        float half=Mathf.Lerp(trail.widths[i],trail.widths[i+1],t);
                        for(int side=-1;side<=1;side+=2) {
                            if(random.NextDouble()<.19)continue;
                            var p=center+tangent*Range(-.7f,.7f)+normal*side*(half+(mixedSizes?Range(3.6f,5.2f):Range(1.9f,2.8f)));
                            if(mixedSizes) {
                                Add(Pick(world.catalog.boulders),p,Range(2.3f,4.2f),-.20f);
                                if(random.NextDouble()<.45)Add(Pick(world.catalog.shrubs),p-normal*side*.5f-tangent*.8f,Range(.55f,.9f),-.02f);
                                if(random.NextDouble()<.25)Add(Pick(world.catalog.rubble),p-tangent*1.2f,Range(.09f,.16f),-.03f);
                                continue;
                            }
                            Add(Pick(world.catalog.stones),p,Range(.32f,.72f),-.09f);
                            Add(Pick(world.catalog.rubble),p-tangent*.8f-normal*side*.35f,Range(.08f,.13f),-.025f);
                            Add(Pick(world.catalog.grasses),p+tangent*1.15f,Range(.28f,.48f),-.015f);
                            Add(Pick(world.catalog.shrubs),p+normal*side*1.0f-tangent*.55f,Range(.40f,.70f),-.02f);
                            if(random.NextDouble()<.18)Add(Pick(world.catalog.cacti),p+normal*side*1.6f+tangent*.8f,Range(.65f,1.05f),-.02f);
                        }
                        next+=mixedSizes?Range(10f,15f):Range(5.0f,7.2f);
                    }
                    travelled+=length;
                }
            }
        }

        public void RoundSurface(MojaveWorld world,float[,] heights)
        {
            if(world.refinedRoads||rules==null||rules.Length==0)return;
            int n=heights.GetLength(0);float step=world.MapSize/(n-1);float sigma=2.3f/step;int radius=Mathf.CeilToInt(sigma*3);
            var kernel=new float[radius*2+1];float sum=0;
            for(int k=-radius;k<=radius;k++){kernel[k+radius]=Mathf.Exp(-k*k/(2*sigma*sigma));sum+=kernel[k+radius];}
            for(int k=0;k<kernel.Length;k++)kernel[k]/=sum;
            var horizontal=new float[n,n];var smooth=new float[n,n];
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)for(int k=-radius;k<=radius;k++)horizontal[z,x]+=heights[z,Mathf.Clamp(x+k,0,n-1)]*kernel[k+radius];
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)for(int k=-radius;k<=radius;k++)smooth[z,x]+=horizontal[Mathf.Clamp(z+k,0,n-1),x]*kernel[k+radius];
            for(int z=0;z<n;z++)for(int x=0;x<n;x++) {
                var p=new Vector2(x*step-world.MapSize*.5f,z*step-world.MapSize*.5f);
                float distance=world.layout.PlayDistance(p);
                // Keep the authored footing, round the adjoining sand banks and their thin joining creases.
                float weight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-2.5f,1.2f,distance));
                heights[z,x]=Mathf.Lerp(heights[z,x],smooth[z,x],weight);
            }
        }
    }
}
