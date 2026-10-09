using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Overburst.Mojave
{
    public enum MojaveGenerationStage { Layout, BackgroundPlan, Surface, CombatTiles, Dressing, Backdrops, Grounding, Complete }

    public sealed class MojaveWorld : MonoBehaviour
    {
        public MojaveCatalog catalog;
        public int seed = 73129;
        public bool expandedMap;
        public bool compactMap;
        public int combatAreaCount;
        public bool randomCombatLayout;
        [Tooltip("0 = preset; otherwise map side length in metres (256–768). Tiles retain their authored size.")]
        public int mapSizeOverride;
        [HideInInspector] public bool roundedJunctions;
        [HideInInspector] public bool organicConnections;
        [HideInInspector] public bool terrainFinish;
        [HideInInspector] public bool refinedRoads;
        [HideInInspector] public bool yieldBlockedLargeShoulders;
        public int shoulderMassCount;
        [HideInInspector] public List<MojavePlacement> shoulderMasses=new List<MojavePlacement>();
        [NonSerialized] public List<Bounds> shoulderFootprints=new List<Bounds>();
        public int groundedPropCount;
        public int conformedMeshCount;
        public int overlappingPropCount;
        readonly List<Mesh> groundMeshes=new List<Mesh>();
        public void OwnGroundMesh(Mesh mesh)=>groundMeshes.Add(mesh);
        void ReleaseGroundMeshes(){foreach(var mesh in groundMeshes)if(mesh!=null)DestroyOwned(mesh);groundMeshes.Clear();}
        public float MapSize => layout!=null?layout.extent:mapSizeOverride>0?mapSizeOverride:expandedMap&&GetComponent<MojaveCombatTileSet>()!=null?(compactMap?320:384):MojaveLayout.Size;
        public Transform generatedRoot;
        public Terrain surface;
        public int bakedSeed;
        public int bakedLayoutVersion;
        [HideInInspector] public bool bakedRandomCombatLayout;
        public int authoredPlacementCount;
        public int dressingCount;
        public int coverCount;
        public Vector3[] blockers;
        [NonSerialized] public MojaveLayout layout;
        [NonSerialized] public long generationMilliseconds;
        readonly List<Vector3> occupied = new List<Vector3>();
        TerrainData runtimeData;
        System.Random random;
        VolcanoTrailField volcanoTrailField;
        float[,] generatedHeights;
        float[,,] generatedPaint;
        OasisSurfaceBuilder.Details generatedDetails;
        readonly List<Mesh> runtimeWaterMeshes=new List<Mesh>();
        public int waterSurfaceCount;
        public void OwnWaterMesh(Mesh mesh) => runtimeWaterMeshes.Add(mesh);
        public void ReleaseWaterMeshes()
        {
            foreach(var mesh in runtimeWaterMeshes)if(mesh!=null)DestroyOwned(mesh);runtimeWaterMeshes.Clear();
        }
        public bool IsWater(Vector2 p) => TryWater(p,out _);
        public bool TryWater(Vector2 p,out float height)
        {
            height=float.NegativeInfinity;
            var islands=GetComponent<IslandBiome>();
            if(islands!=null&&surface!=null){height=islands.seaLevel;return Ground(p).y<height-.03f;}
            if(catalog==null || !catalog.AuthoredSurface || surface==null || PlayDistance(p)<=1.35f)return false;
            float ground=Ground(p).y;
            foreach(var place in layout.places) {
                var patch=catalog.patches[place.patch];if(!patch.Water(place.Local(p),out _,out float level,out _))continue;
                float water=place.height+Mathf.Clamp(level*.55f,-2,2);
                if(water>ground+.045f)height=Mathf.Max(height,water);
            }
            return !float.IsNegativeInfinity(height);
        }

        void Awake()
        {
            if(!Application.isPlaying)return;
            SetWind();
            if (generatedRoot != null && surface != null && bakedSeed == seed && bakedLayoutVersion == MojaveLayout.Version && bakedRandomCombatLayout == randomCombatLayout && Mathf.Approximately(surface.terrainData.size.x,MapSize))
                {layout = CreateLayout();volcanoTrailField=catalog.volcano?new VolcanoTrailField(layout):null;}
            else Generate(seed);
        }

        public void Generate(int newSeed) => Generate(newSeed, null);

        // Optional presentation checkpoints observe the same production pipeline and RNG stream.
        public void Generate(int newSeed, Action<MojaveGenerationStage> checkpoint)
        {
            var clock=System.Diagnostics.Stopwatch.StartNew();
            if(catalog==null || catalog.patches==null || catalog.patches.Length<6) throw new InvalidOperationException("Mojave patch catalog is incomplete.");
            Clear();seed=newSeed;random=new System.Random(seed);layout=CreateLayout();
            volcanoTrailField=catalog.volcano?new VolcanoTrailField(layout):null;
            generatedRoot=new GameObject(catalog.DisplayName+" · assembled places").transform;generatedRoot.SetParent(transform,false);
            occupied.Clear();authoredPlacementCount=0;dressingCount=0;coverCount=0;
            checkpoint?.Invoke(MojaveGenerationStage.Layout);
            var islands=GetComponent<IslandBiome>();
            if(islands!=null) {
                islands.Build(this,checkpoint);
                bakedSeed=seed;bakedLayoutVersion=MojaveLayout.Version;SetWind();Physics.SyncTransforms();
                generationMilliseconds=clock.ElapsedMilliseconds;checkpoint?.Invoke(MojaveGenerationStage.Complete);return;
            }
            var biomass=GetComponent<BiomassBiome>();
            if(biomass!=null) {
                biomass.Build(this,checkpoint);
                bakedSeed=seed;bakedLayoutVersion=MojaveLayout.Version;SetWind();Physics.SyncTransforms();
                generationMilliseconds=clock.ElapsedMilliseconds;checkpoint?.Invoke(MojaveGenerationStage.Complete);return;
            }
            var mountain=GetComponent<MountainBiome>();
            if(mountain!=null){mountain.Build(this,checkpoint);bakedSeed=seed;bakedLayoutVersion=MojaveLayout.Version;SetWind();Physics.SyncTransforms();generationMilliseconds=clock.ElapsedMilliseconds;checkpoint?.Invoke(MojaveGenerationStage.Complete);return;}
            var backdrops=GetComponent<MojaveBackdropSet>();if(backdrops!=null)backdrops.Plan(this);
            checkpoint?.Invoke(MojaveGenerationStage.BackgroundPlan);
            BuildSurface();
            checkpoint?.Invoke(MojaveGenerationStage.Surface);
            waterSurfaceCount=catalog.AuthoredSurface?OasisSurfaceBuilder.BuildWater(this):0;
            foreach(var place in layout.places) AssemblePlace(place);
            checkpoint?.Invoke(MojaveGenerationStage.CombatTiles);
            DressCombatCover();DressRidges();DressTrails();DressPlants();
            if(catalog.volcano)VolcanoEffectsBuilder.Build(this);
            blockers=occupied.ToArray();
            checkpoint?.Invoke(MojaveGenerationStage.Dressing);
            var authoredTiles=GetComponent<MojaveCombatTileSet>();
            if(terrainFinish)MojaveTerrainFinish.DressShoulders(this);
            if(authoredTiles!=null)authoredTiles.DressTrailSides(this);
            if(backdrops!=null)backdrops.Dress(this);
            checkpoint?.Invoke(MojaveGenerationStage.Backdrops);
            var finalDetails=GetComponent<MojaveDetailDressing>();
            if(finalDetails!=null&&finalDetails.enabled)finalDetails.DressOpenRoadPockets();
            if(terrainFinish) {
                MojaveTerrainFinish.GroundProps(this);
            }
            checkpoint?.Invoke(MojaveGenerationStage.Grounding);
            if(finalDetails!=null&&finalDetails.enabled)finalDetails.Rebuild();
            bakedRandomCombatLayout=randomCombatLayout;
            bakedSeed=seed;bakedLayoutVersion=MojaveLayout.Version;SetWind();Physics.SyncTransforms();
            generationMilliseconds=clock.ElapsedMilliseconds;
            checkpoint?.Invoke(MojaveGenerationStage.Complete);
        }

        MojaveLayout CreateLayout() {
            var tileSet=GetComponent<MojaveCombatTileSet>();
            // Keep the accepted macro RNG stream: the original twelve large cuts define its patch count.
            int count=tileSet!=null&&tileSet.mixedSizes?Array.FindAll(catalog.patches,p=>p.combatSize==MojaveCombatSize.Large).Length:catalog.patches.Length;
            var result=new MojaveLayout(seed,count,expandedMap&&GetComponent<MojaveCombatTileSet>()!=null,compactMap,combatAreaCount,mapSizeOverride);
            if(catalog.placeNames!=null&&catalog.placeNames.Length==result.places.Count)
                for(int i=0;i<result.places.Count;i++)result.places[i].name=catalog.placeNames[i];
            if(catalog.AuthoredSurface) {
                bool HasSurface(MojavePatch patch) => patch.waterMaterial!=null&&(!catalog.volcano||Array.Exists(patch.waterDepth,d=>d>.03f));
                bool hasShore=false;foreach(var place in result.places)hasShore|=HasSurface(catalog.patches[place.patch]);
                if(!hasShore)for(int i=0;i<catalog.patches.Length;i++)if(HasSurface(catalog.patches[i])){result.places[3].patch=i;break;}
            }
            var authoredTiles=GetComponent<MojaveCombatTileSet>();
            if(authoredTiles!=null)authoredTiles.Apply(result,catalog);
            if(randomCombatLayout)result.ScatterCombatPlaces(catalog);
            if(terrainFinish)foreach(var trail in result.trails){trail.width*=1.45f;for(int i=0;i<trail.widths.Length;i++)trail.widths[i]*=1.45f;}
            var islands=GetComponent<IslandBiome>();if(islands!=null)islands.ConfigureLayout(result);
            var biomass=GetComponent<BiomassBiome>();if(biomass!=null)biomass.ConfigureLayout(result);
            var mountain=GetComponent<MountainBiome>();if(mountain!=null)mountain.ConfigureLayout(result);
            result.FitRockTrails(catalog,roundedJunctions,organicConnections,yieldBlockedLargeShoulders&&terrainFinish&&authoredTiles!=null&&!authoredTiles.mixedSizes);result.junctionRounding=roundedJunctions?8:0;
            if(terrainFinish)MojaveTerrainFinish.ShapeRoadHeights(result,refinedRoads);
            if(islands!=null)islands.ShapeCauseways(result);
            if(biomass!=null)biomass.ShapeTrails(result);
            if(mountain!=null)mountain.ShapeTrails(result);
            return result;
        }
        public void EnsureLayout() { if(layout==null){layout=CreateLayout();volcanoTrailField=catalog.volcano?new VolcanoTrailField(layout):null;} }
        public float TrailDistance(Vector2 p,out float elevation,out float halfWidth) => volcanoTrailField!=null?volcanoTrailField.Distance(p,out elevation,out halfWidth):layout.TrailDistance(p,out elevation,out halfWidth);
        public float PlayDistance(Vector2 p) => layout.CombineDistance(layout.RoomDistance(p,out _),TrailDistance(p,out _,out _));
        public bool IsWalkable(Vector2 p,float clearance=.45f)
        {
            EnsureLayout();if(!layout.CanWalk(p,clearance))return false;
            var islands=GetComponent<IslandBiome>();if(islands!=null&&Ground(p).y<islands.seaLevel+.30f)return false;
            if(blockers!=null)foreach(var b in blockers)if((new Vector2(b.x,b.y)-p).sqrMagnitude<(b.z+clearance)*(b.z+clearance))return false;
            return true;
        }
        public Vector3 Ground(Vector2 p,float offset=0) => new Vector3(p.x,surface.SampleHeight(new Vector3(p.x,0,p.y))+surface.transform.position.y+offset,p.y);
        public float PlannedBackgroundHeight(Vector2 p)
            =>PlannedBackgroundHeight(p,PlayDistance(p),NearbyPlayHeight(p));
        float PlannedBackgroundHeight(Vector2 p,float distance,float nearby)
        {
            float sum=0,weight=0;
            foreach(var place in layout.places) {
                float w=1/((p-place.center).sqrMagnitude+625);sum+=place.height*w;weight+=w;
            }
            if(terrainFinish) {
                float land=layout.Noise(p,.021f,621),fold=layout.Noise(p,.047f,625);
                float rise=Smooth(0,24,distance)*(.8f+land*4.5f)+Smooth(3,18,distance)*(fold-.4f)*.8f;
                return Mathf.Max(sum/weight-1.5f+land*3.5f,nearby+rise);
            }
            float bank=5+Smooth(3,28,distance)*(4+layout.Noise(p,.018f,24)*3);
            return Mathf.Max(sum/weight+bank,nearby+Smooth(1,18,distance)*6);
        }
        public float NearbyPlayHeight(Vector2 p)
        {
            float road=TrailDistance(p,out float roadHeight,out _),room=layout.RoomDistance(p,out var place);
            return road<room?roadHeight:place.height;
        }
        public Vector3 Entrance { get { EnsureLayout();return Ground(layout.places[0].center); } }
        public Vector3 Staging { get { EnsureLayout();return Ground(layout.places[0].center+new Vector2(-2,-2)); } }

        void Clear()
        {
            ReleaseGroundMeshes();shoulderMasses.Clear();shoulderFootprints.Clear();
            foreach(var mesh in runtimeWaterMeshes)if(mesh!=null)DestroyOwned(mesh);runtimeWaterMeshes.Clear();generatedDetails=null;
            if(generatedRoot!=null) {generatedRoot.gameObject.SetActive(false);DestroyOwned(generatedRoot.gameObject);}
            if(runtimeData!=null) {DestroyOwned(runtimeData);runtimeData=null;}
        }
        static void DestroyOwned(UnityEngine.Object item) {if(Application.isPlaying) Destroy(item);else DestroyImmediate(item);}
        public void AcceptGeneratedSurface(Terrain terrain,float[,] heights,float[,,] paint)
        {
            surface=terrain;runtimeData=terrain.terrainData;generatedHeights=heights;generatedPaint=paint;
        }
        public void AdoptBakedTerrain() { runtimeData=null; }
        public void WriteBakedSurface(TerrainData destination, bool attach = true)
        {
            if(generatedPaint==null || generatedHeights==null)throw new InvalidOperationException("Generate the surface before baking it.");
            destination.heightmapResolution=generatedHeights.GetLength(0);destination.alphamapResolution=generatedPaint.GetLength(0);
            destination.baseMapResolution=512;destination.size=new Vector3(MapSize,48,MapSize);destination.terrainLayers=catalog.layers;
            destination.SetHeights(0,0,generatedHeights);destination.SetAlphamaps(0,0,generatedPaint);destination.SetBaseMapDirty();
            generatedDetails?.Write(destination);
            var backdrops=GetComponent<MojaveBackdropSet>();if(backdrops!=null)backdrops.WriteDetails(this,destination);
            if(attach) { surface.terrainData=destination;surface.GetComponent<TerrainCollider>().terrainData=destination; }
        }
        void OnDestroy() {ReleaseGroundMeshes();foreach(var mesh in runtimeWaterMeshes)if(mesh!=null)DestroyOwned(mesh);runtimeWaterMeshes.Clear();if(runtimeData!=null)DestroyOwned(runtimeData);}
        float Rand(float a,float b) => Mathf.Lerp(a,b,(float)random.NextDouble());
        GameObject Pick(GameObject[] prefabs) => prefabs[random.Next(prefabs.Length)];
        static float Smooth(float a,float b,float v) => Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,v));

        void BuildSurface()
        {
            int resolution=MapSize>MojaveLayout.Size?1025:513;
            int alphaResolution=resolution-1;
            runtimeData=new TerrainData { name="Mojave continuous terrain · "+seed,heightmapResolution=resolution,alphamapResolution=alphaResolution,baseMapResolution=512,
                size=new Vector3(MapSize,MojaveLayout.Elevation,MapSize),terrainLayers=catalog.layers };
            var heights=new float[resolution,resolution];var paint=new float[alphaResolution,alphaResolution,catalog.layers.Length];
            var backdrops=GetComponent<MojaveBackdropSet>();var backgroundDistances=backdrops!=null?new float[resolution,resolution]:null;
            var sample=new float[catalog.layers.Length];
            bool mixedCombat=GetComponent<MojaveCombatTileSet>()?.mixedSizes==true;
            bool natural=backdrops!=null&&backdrops.library.preserveAuthoredEnvironment;
            for(int z=0;z<resolution;z++)for(int x=0;x<resolution;x++) {
                Vector2 p=new Vector2(x/(float)(resolution-1)*MapSize-MapSize*.5f,z/(float)(resolution-1)*MapSize-MapSize*.5f);
                float rd=layout.RoomDistance(p,out var place);float tr=TrailDistance(p,out float pathHeight,out _);float pd=layout.CombineDistance(rd,tr);
                if(backgroundDistances!=null)backgroundDistances[z,x]=pd;
                float n=layout.Noise(p,.024f,3);float small=layout.Noise(p,.12f,89);
                float h=5.2f+n*3.2f+(small-.5f)*.5f;
                h+=Smooth(1,18,pd)*(2.6f+layout.Noise(p,.045f,24)*6.6f);
                if(mixedCombat)h+=Smooth(2.5f,20,pd)*(1.2f+layout.Noise(p,.031f,271)*3.2f);
                if(natural)h=PlannedBackgroundHeight(p,pd,tr<rd?pathHeight:place.height)+(small-.5f)*.25f;
                float edge=Mathf.Max(Mathf.Abs(p.x),Mathf.Abs(p.y));h+=Smooth(MapSize*.5f-23,MapSize*.5f,edge)*5;
                var patch=catalog.patches[place.patch];var local=place.Local(p);
                // Retain the authored landform while softening its small ridges for combat footing.
                float relief=patch.Height(local)*.4f;
                relief+=(patch.Height(local+Vector2.right*3)+patch.Height(local-Vector2.right*3)
                    +patch.Height(local+Vector2.up*3)+patch.Height(local-Vector2.up*3))*.15f;
                relief=Mathf.Clamp(relief*.55f,-2,2);
                float roomWeight=1-Smooth(-3,10,rd);
                float roomHeight=place.height+relief+(small-.5f)*.08f;
                h=Mathf.Lerp(h,roomHeight,roomWeight);
                if(catalog.AuthoredSurface)foreach(var bankPlace in layout.places) {
                    var bankPatch=catalog.patches[bankPlace.patch];if(bankPatch.waterMaterial==null)continue;
                    var bankLocal=bankPlace.Local(p);
                    // Each bank has its own continuous support field. Switching the nearest room must not cut a cliff through a pond.
                    float bankWeight=1-Smooth(bankPatch.size*.5f,bankPatch.size*.5f+13,Mathf.Max(Mathf.Abs(bankLocal.x),Mathf.Abs(bankLocal.y)));
                    float bankRelief=bankPatch.Height(bankLocal)*.4f;
                    bankRelief+=(bankPatch.Height(bankLocal+Vector2.right*3)+bankPatch.Height(bankLocal-Vector2.right*3)
                        +bankPatch.Height(bankLocal+Vector2.up*3)+bankPatch.Height(bankLocal-Vector2.up*3))*.15f;
                    float bankHeight=bankPlace.height+Mathf.Clamp(bankRelief*.55f,-2,2);
                    if(bankPatch.Water(bankLocal,out _,out float bankLevel,out _)) {
                        float rim=Smooth(bankPatch.size*.5f-5,bankPatch.size*.5f-.2f,Mathf.Max(Mathf.Abs(bankLocal.x),Mathf.Abs(bankLocal.y)));
                        bankHeight=Mathf.Lerp(bankHeight,bankPlace.height+Mathf.Clamp(bankLevel*.55f,-2,2)+.16f,rim);
                    }
                    h=Mathf.Lerp(h,bankHeight,bankWeight);
                }
                // The same road stamp must cross room boundaries without fading out at the doorway.
                float transition=refinedRoads?4.5f+layout.Noise(p,.035f,882)*5f:terrainFinish?3.0f+layout.Noise(p,.06f,882)*3.5f:6.5f;
                float trailWeight=1-Smooth(-.3f,transition,tr);
                float roadDetail=refinedRoads?(layout.Noise(p,.075f,893)-.5f)*.42f:(small-.5f)*.22f;
                h=Mathf.Lerp(h,pathHeight+roadDetail-.15f,trailWeight);
                if(catalog.AuthoredSurface && pd>1.35f)foreach(var bankPlace in layout.places) {
                    var bankPatch=catalog.patches[bankPlace.patch];var bankLocal=bankPlace.Local(p);
                    if(!bankPatch.Water(bankLocal,out float depth,out float level,out _))continue;
                    float waterHeight=bankPlace.height+Mathf.Clamp(level*.55f,-2,2);
                    float bank=Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.35f,3.6f,pd));
                    float shore=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.03f,.8f,depth));
                    int neighbours=0;
                    if(bankPatch.Water(bankLocal+Vector2.right*2,out _,out _,out _))neighbours++;
                    if(bankPatch.Water(bankLocal-Vector2.right*2,out _,out _,out _))neighbours++;
                    if(bankPatch.Water(bankLocal+Vector2.up*2,out _,out _,out _))neighbours++;
                    if(bankPatch.Water(bankLocal-Vector2.up*2,out _,out _,out _))neighbours++;
                    shore*=neighbours*.25f;
                    shore*=1-Smooth(bankPatch.size*.5f-5,bankPatch.size*.5f-.2f,Mathf.Max(Mathf.Abs(bankLocal.x),Mathf.Abs(bankLocal.y)));
                    h=Mathf.Lerp(h,waterHeight-Mathf.Clamp(depth*.55f,.28f,2.2f),bank*shore);
                }
                heights[z,x]=h/MojaveLayout.Elevation;
                if(x>=alphaResolution || z>=alphaResolution)continue;
                // Paint follows the same continuous fields as the relief. Source tile paint remains visible.
                patch.Paint(local,sample);
                float tileWeight=(1-Smooth(1,12,rd))*.97f;
                float pebble=Smooth(.42f,.73f,layout.Noise(p,.10f,78));
                for(int l=0;l<catalog.layers.Length;l++)paint[z,x,l]=sample[l]*tileWeight;
                float baseWeight=1-tileWeight;
                if(catalog.volcano) {
                    // Native Volcano has four layers: dry ash, mud, rubble, cooled lava.
                    paint[z,x,0]+=baseWeight*(.47f+.18f*(1-pebble));paint[z,x,1]+=baseWeight*.08f;
                    paint[z,x,2]+=baseWeight*(.12f+.18f*pebble);paint[z,x,3]+=baseWeight*.15f;
                    float trail=1-Smooth(-.5f,3.5f,tr);float volcanicClearing=(1-Smooth(-7,-2,rd))*.16f;float volcanicFine=Mathf.Max(trail*.68f,volcanicClearing);
                    for(int l=0;l<catalog.layers.Length;l++)paint[z,x,l]*=1-volcanicFine;
                    paint[z,x,0]+=volcanicFine*.76f;paint[z,x,1]+=volcanicFine*.06f;paint[z,x,2]+=volcanicFine*.10f;paint[z,x,3]+=volcanicFine*.08f;
                    float total=0;for(int l=0;l<catalog.layers.Length;l++)total+=paint[z,x,l];
                    for(int l=0;l<catalog.layers.Length;l++)paint[z,x,l]/=total;
                    continue;
                }
                paint[z,x,0]+=baseWeight*(.26f+.26f*(1-pebble));paint[z,x,1]+=baseWeight*.13f;
                paint[z,x,2]+=baseWeight*(.10f+.30f*pebble);paint[z,x,3]+=baseWeight*.27f;
                float wear=1-Smooth(-.5f,3.5f,tr);
                float clearing=0;
                float fine=Mathf.Max(wear*(terrainFinish?.25f:.75f),clearing);
                for(int l=0;l<catalog.layers.Length;l++)paint[z,x,l]*=1-fine;
                paint[z,x,0]+=fine*.68f;paint[z,x,1]+=fine*.13f;paint[z,x,3]+=fine*.15f;paint[z,x,5]+=fine*.04f;
                float sum=0;for(int l=0;l<catalog.layers.Length;l++)sum+=paint[z,x,l];
                for(int l=0;l<catalog.layers.Length;l++)paint[z,x,l]/=sum;
            }
            var authoredTiles=GetComponent<MojaveCombatTileSet>();
            if(backdrops!=null&&backdrops.library.preserveAuthoredEnvironment) {
                if(authoredTiles!=null)authoredTiles.RoundSurface(this,heights);
                backdrops.BlendGround(this,heights,paint,backgroundDistances);
            } else {
                if(backdrops!=null)backdrops.BlendGround(this,heights,paint,backgroundDistances);
                if(authoredTiles!=null)authoredTiles.RoundSurface(this,heights);
            }
            if(terrainFinish)MojaveTerrainFinish.PaintRoads(this,paint);
            generatedHeights=heights;generatedPaint=paint;
            runtimeData.SetHeights(0,0,heights);runtimeData.SetAlphamaps(0,0,paint);
            var go=Terrain.CreateTerrainGameObject(runtimeData);go.name="Shared ground · no tile seams";go.transform.SetParent(generatedRoot,false);go.transform.position=new Vector3(-MapSize*.5f,0,-MapSize*.5f);
            surface=go.GetComponent<Terrain>();surface.materialTemplate=catalog.terrainMaterial;surface.drawInstanced=false;
            surface.heightmapPixelError=3;surface.basemapDistance=400;surface.shadowCastingMode=ShadowCastingMode.On;
            surface.drawTreesAndFoliage=catalog.AuthoredSurface;
            if(catalog.AuthoredSurface) {generatedDetails=OasisSurfaceBuilder.BuildDetails(this,runtimeData);surface.detailObjectDistance=95;surface.detailObjectDensity=1;}
            if(backdrops!=null)backdrops.WriteDetails(this,runtimeData);
        }

        void AssemblePlace(MojavePlace place)
        {
            var parent=new GameObject(place.name+" · source tile "+catalog.patches[place.patch].name).transform;parent.SetParent(generatedRoot,false);
            foreach(var authored in catalog.patches[place.patch].placements) {
                var p=place.World(new Vector2(authored.position.x,authored.position.z));
                if(Mathf.Abs(p.x)>MapSize*.5f-9||Mathf.Abs(p.y)>MapSize*.5f-9)continue;
                float rd=layout.RoomDistance(p,out _);float tr=TrailDistance(p,out _,out _);
                float radius=authored.radius;
                bool boulder=authored.kind==MojavePropKind.Boulder;
                bool tree=authored.kind==MojavePropKind.Tree;
                bool waterPlant=catalog.oasis&&authored.prefab.name.StartsWith("Lillypads",StringComparison.Ordinal);
                if(waterPlant&&!IsWater(p))continue;
                // Reserve this court's interior for smaller cover with deliberate gaps for circling.
                if(place.kind==MojaveCombatKind.CoverCourt&&(boulder||tree)&&layout.PlaceDistance(p,place)<radius+3)continue;
                if(boulder && (Vector2.Distance(p,place.center)<radius+5.5f || tr<radius+.7f))continue;
                if(boulder&&BlocksGameView(p,radius,authored.height))continue;
                if(tree && (tr<radius+.7f || Vector2.Distance(p,place.center)<3.4f))continue;
                if(authored.kind==MojavePropKind.Plant && tr<-.5f && rd>1 && Rand(0,1)<.55f)continue;
                if(terrainFinish&&MojaveTerrainFinish.Reserved(this,p,boulder?radius:.25f))continue;
                if(rd>20 || (catalog.AuthoredSurface && IsWater(p) && !boulder && !waterPlant))continue;
                float scale=1;
                radius*=scale;
                var rotation=Quaternion.Euler(0,-place.rotation,0)*authored.rotation;
                var item=Spawn(authored.prefab,p,authored.scale*scale,rotation,parent,authored.kind,authored.position.y);
                if(waterPlant) {
                    TryWater(p,out float waterHeight);
                    item.transform.position=new Vector3(p.x,waterHeight+.025f,p.y);
                }
                if(boulder||tree)occupied.Add(new Vector3(p.x,p.y,Mathf.Min(boulder?radius:radius*.65f,12)));
                authoredPlacementCount++;
            }
        }

        bool NearSolid(Vector2 p,float radius)
        {
            foreach(var o in occupied)if((new Vector2(o.x,o.y)-p).sqrMagnitude<Mathf.Pow(o.z+radius*.7f,2))return true;
            return false;
        }

        bool BlocksGameView(Vector2 p,float radius,float height)
        {
            if(!catalog.volcano||height<2.5f)return false;
            // At the fixed 225-degree game view, tall foreground rocks can hide a court even while its ground is open.
            var towardCamera=new Vector2(.7071068f,.7071068f);var acrossCamera=new Vector2(-.7071068f,.7071068f);
            foreach(var place in layout.places) {
                var relative=p-place.center;float forward=Vector2.Dot(relative,towardCamera);
                if(forward>-radius&&forward<18+radius&&Mathf.Abs(Vector2.Dot(relative,acrossCamera))<radius+4
                    &&height>Mathf.Max(2.5f,forward*.65f))return true;
            }
            return false;
        }

        void DressCombatCover()
        {
            foreach(var place in layout.places) {
                if(place.kind!=MojaveCombatKind.CoverCourt)continue;
                var parent=new GameObject(place.name+" · combat cover").transform;parent.SetParent(generatedRoot,false);
                int count=0;
                // Keep the arrival, centre and every fitted road open; cover creates flanking space inside the court.
                for(int i=0;i<72&&count<3;i++) {
                    float angle=Rand(0,Mathf.PI*2);
                    var local=new Vector2(Mathf.Cos(angle)*place.radius.x,Mathf.Sin(angle)*place.radius.y)*Rand(.48f,.72f);
                    var p=place.World(local);float radius=Rand(1.6f,2.2f);
                    if(layout.PlaceDistance(p,place)>-radius-1||TrailDistance(p,out _,out _)<radius+1.6f||NearSolid(p,radius+1.5f))continue;
                    var prefab=Pick(place.center.x>0?catalog.redBoulders:catalog.boulders);
                    var renderers=prefab.GetComponentsInChildren<Renderer>(true);
                    if(renderers.Length==0)continue;
                    var bounds=renderers[0].bounds;for(int j=1;j<renderers.Length;j++)bounds.Encapsulate(renderers[j].bounds);
                    float footprint=new Vector2(bounds.extents.x,bounds.extents.z).magnitude
                        +new Vector2(bounds.center.x-prefab.transform.position.x,bounds.center.z-prefab.transform.position.z).magnitude;
                    float scale=radius/Mathf.Max(.1f,footprint);
                    Spawn(prefab,p,Vector3.one*scale,Quaternion.Euler(0,Rand(0,360),0),parent,MojavePropKind.Boulder,-.18f);
                    occupied.Add(new Vector3(p.x,p.y,radius));count++;coverCount++;dressingCount++;
                }
            }
        }

        void DressRidges()
        {
            if(refinedRoads)return;
            var parent=new GameObject("Rock shoulders and sandstone ridges").transform;parent.SetParent(generatedRoot,false);
            var backdrops=GetComponent<MojaveBackdropSet>();
            // Irregular rock shoulders border connected play space; openings follow the graph rather than a grid.
            for(float z=-MapSize*.5f+16;z<MapSize*.5f-15;z+=4.7f)for(float x=-MapSize*.5f+16;x<MapSize*.5f-15;x+=4.7f) {
                var p=new Vector2(x+Rand(-1.8f,1.8f),z+Rand(-1.8f,1.8f));
                if(terrainFinish&&MojaveTerrainFinish.Reserved(this,p,4))continue;
                float d=PlayDistance(p);if(d<3.4f||d>15)continue;
                if(backdrops!=null&&backdrops.Reserves(p))continue;
                float cluster=layout.Noise(p,.055f,103);
                if(Rand(0,1)>(d<8?.55f:.15f)*(.5f+cluster*.7f))continue;
                var prefab=Pick(p.x>5?catalog.redBoulders:catalog.boulders);
                float scale=Rand(.68f,1.15f);float radius=3.8f*scale;
                if(catalog.AuthoredSurface)scale=radius/Footprint(prefab);
                if(d<radius+1.5f||NearSolid(p,radius)||(catalog.AuthoredSurface&&IsWater(p)))continue;
                if(catalog.volcano) {
                    var renderers=prefab.GetComponentsInChildren<Renderer>(true);var bounds=renderers[0].bounds;
                    for(int i=1;i<renderers.Length;i++)bounds.Encapsulate(renderers[i].bounds);
                    if(BlocksGameView(p,radius,bounds.size.y*scale))continue;
                }
                Spawn(prefab,p,Vector3.one*scale,Quaternion.Euler(Rand(-6,6),Rand(0,360),Rand(-4,4)),parent,MojavePropKind.Boulder,-.45f);
                occupied.Add(new Vector3(p.x,p.y,radius));dressingCount++;
            }
        }

        void DressTrails()
        {
            var parent=new GameObject("Arroyo banks · stones and weathered rubble").transform;parent.SetParent(generatedRoot,false);
            bool mixed=GetComponent<MojaveCombatTileSet>()?.mixedSizes==true;
            // Refined roads use authored background pieces instead of an independent paired stone border.
            if(!refinedRoads)foreach(var trail in layout.trails)for(int i=2;i<trail.points.Length-2;i+=mixed?4:2) {
                var d=(trail.points[i+1]-trail.points[i-1]).normalized;var normal=new Vector2(-d.y,d.x);
                for(int side=-1;side<=1;side+=2) {
                    var p=trail.points[i]+normal*side*(trail.width*.5f+Rand(1.1f,2.5f));
                    if(layout.RoomDistance(p,out _)<-5)continue;
                    if(Rand(0,1)<.62f)Spawn(Pick(catalog.rubble),p,Vector3.one*Rand(.30f,.56f),Quaternion.Euler(0,Rand(0,360),0),parent,MojavePropKind.Rubble,-.1f);
                    if(Rand(0,1)<.5f)Spawn(Pick(catalog.stones),p+normal*side*Rand(.1f,1),Vector3.one*Rand(.25f,.60f),Quaternion.Euler(Rand(-12,12),Rand(0,360),Rand(-8,8)),parent,MojavePropKind.Boulder,-.13f);
                    dressingCount++;
                }
            }
            foreach(var place in layout.places)for(int i=0;i<(mixed?8:22);i++) {
                float angle=Rand(0,Mathf.PI*2);float length=Rand(11,25);
                var p=place.center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*length;
                if(TrailDistance(p,out _,out _)<1.4f||(catalog.AuthoredSurface&&IsWater(p)))continue;
                Spawn(Pick(catalog.rubble),p,Vector3.one*Rand(.32f,.62f),Quaternion.Euler(0,Rand(0,360),0),parent,MojavePropKind.Rubble,-.10f);dressingCount++;
            }
        }

        void DressPlants()
        {
            var parent=new GameObject("Patchy desert scrub · shelter and exposed sand").transform;parent.SetParent(generatedRoot,false);
            for(float z=-MapSize*.5f+23;z<MapSize*.5f-18;z+=3)for(float x=-MapSize*.5f+20;x<MapSize*.5f-18;x+=3) {
                var p=new Vector2(x+Rand(-1.3f,1.3f),z+Rand(-1.3f,1.3f));float rd=layout.RoomDistance(p,out var place);float tr=TrailDistance(p,out _,out _);
                if(terrainFinish&&MojaveTerrainFinish.Reserved(this,p,.4f))continue;
                if(Mathf.Min(rd,tr)>19||tr<.6f||(catalog.AuthoredSurface&&IsWater(p)))continue;
                float cluster=layout.Noise(p,.11f,37);
                float density=rd<-5?.075f:rd<8?.50f:.24f;
                if(cluster<.39f||Rand(0,1)>density*.34f)continue;
                float chance=Rand(0,1);
                if(chance<.14f && rd>-.5f && tr>2.2f) {
                    var prefab=Pick(place.theme==3?catalog.joshua:catalog.cacti);
                    if(NearSolid(p,.8f))continue;
                    Spawn(prefab,p,Vector3.one*Rand(.78f,1.35f),Quaternion.Euler(0,Rand(0,360),0),parent,MojavePropKind.Tree,0);
                    occupied.Add(new Vector3(p.x,p.y,.8f));
                } else {
                    bool shrub=chance<.68f;
                    var prefab=Pick(shrub?catalog.shrubs:catalog.grasses);
                    Spawn(prefab,p,Vector3.one*Rand(.85f,1.65f),Quaternion.Euler(0,Rand(0,360),0),parent,MojavePropKind.Plant,-.02f);
                    // Tight companions create a plant colony instead of independent uniform scatter.
                    if(shrub)for(int k=0;k<random.Next(1,4);k++) {
                        var q=p+new Vector2(Rand(-1.2f,1.2f),Rand(-1.2f,1.2f));if(TrailDistance(q,out _,out _)<.3f)continue;
                        Spawn(Pick(catalog.grasses),q,Vector3.one*Rand(.9f,1.7f),Quaternion.Euler(0,Rand(0,360),0),parent,MojavePropKind.Plant,-.025f);
                    }
                }
                dressingCount++;
            }
        }

        static float Footprint(GameObject prefab)
        {
            var renderers=prefab.GetComponentsInChildren<Renderer>(true);if(renderers.Length==0)return 1;
            var bounds=renderers[0].bounds;for(int i=1;i<renderers.Length;i++)bounds.Encapsulate(renderers[i].bounds);
            return Mathf.Max(.1f,new Vector2(bounds.extents.x,bounds.extents.z).magnitude
                +new Vector2(bounds.center.x-prefab.transform.position.x,bounds.center.z-prefab.transform.position.z).magnitude);
        }
        GameObject Spawn(GameObject prefab,Vector2 p,Vector3 scale,Quaternion rotation,Transform parent,MojavePropKind kind,float embed)
        {
            var go=Instantiate(prefab,Ground(p,embed),rotation,parent);go.name=prefab.name;go.transform.localScale=scale;
            if(kind==MojavePropKind.Boulder)SeatRock(go);
            // Native collision follows the explicit playable field. Small dressing cannot snag the review actor.
            foreach(var collider in go.GetComponentsInChildren<Collider>())collider.enabled=kind==MojavePropKind.Tree||kind==MojavePropKind.Boulder;
            foreach(var lod in go.GetComponentsInChildren<LODGroup>())lod.fadeMode=LODFadeMode.None;
            return go;
        }

        public bool SeatRock(GameObject go)
        {
            if(GetComponent<MojaveCombatTileSet>()?.mixedSizes!=true)return false;
            if(LimitLooseStone(go))return true;
            var renderers=go.GetComponentsInChildren<Renderer>(true);if(renderers.Length==0)return false;
            var bounds=renderers[0].bounds;for(int i=1;i<renderers.Length;i++)bounds.Encapsulate(renderers[i].bounds);
            if(bounds.size.y<.65f)return false;
            float gap=bounds.min.y-Ground(new Vector2(bounds.center.x,bounds.center.z)).y+.12f;
            if(gap<=.08f)return false;
            go.transform.position-=Vector3.up*gap;return true;
        }

        public static bool IsLooseStoneName(string name)
        {
            for(int i=0;i<6;i++)if(name=="Rock_"+i||name.EndsWith("_Rock_"+i,StringComparison.Ordinal))return true;
            return false;
        }
        public bool LimitLooseStone(GameObject go)
        {
            if(!IsLooseStoneName(go.name))return false;
            var renderers=go.GetComponentsInChildren<Renderer>(true);if(renderers.Length==0)return false;
            Bounds BoundsOf(){var b=renderers[0].bounds;for(int i=1;i<renderers.Length;i++)b.Encapsulate(renderers[i].bounds);return b;}
            var bounds=BoundsOf();float factor=Mathf.Min(1,2.2f/Mathf.Max(.01f,Mathf.Max(bounds.size.x,bounds.size.z)),1.6f/Mathf.Max(.01f,bounds.size.y));
            if(factor>=.9999f)return false;
            go.transform.localScale*=factor;
            bounds=BoundsOf();go.transform.position+=Vector3.up*(Ground(new Vector2(bounds.center.x,bounds.center.z)).y-.12f-bounds.min.y);
            return true;
        }

        public static void SetWind()
        {
            Shader.SetGlobalFloat("WindPower",.10f);Shader.SetGlobalFloat("WindSpeed",.55f);
            Shader.SetGlobalFloat("WindBurstsPower",.18f);Shader.SetGlobalFloat("WindBurstsSpeed",1.2f);Shader.SetGlobalFloat("WindBurstsScale",16);
            Shader.SetGlobalFloat("MicroPower",.025f);Shader.SetGlobalFloat("MicroSpeed",.8f);Shader.SetGlobalFloat("MicroFrequency",2.5f);
            Shader.SetGlobalFloat("GrassRenderDist",120);
        }
    }
}
