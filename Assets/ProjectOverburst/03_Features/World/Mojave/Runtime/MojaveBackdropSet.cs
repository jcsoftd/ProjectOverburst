using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Overburst.Mojave
{
    // Authored rock colonies are stamped only into background; shared ground keeps the joins continuous.
    public sealed class MojaveBackdropSet : MonoBehaviour
    {
        [Serializable] public sealed class Stamp
        {
            public MojavePatch tile;
            public Vector2 center;
            public float yaw;
            public float groundHeight;
            public float connectionPadding;
            public bool denseFill;
            public Vector2 Local(Vector2 p) {float a=-yaw*Mathf.Deg2Rad;var d=p-center;return new Vector2(d.x*Mathf.Cos(a)-d.y*Mathf.Sin(a),d.x*Mathf.Sin(a)+d.y*Mathf.Cos(a));}
            public Vector2 World(Vector2 p) {float a=yaw*Mathf.Deg2Rad;return center+new Vector2(p.x*Mathf.Cos(a)-p.y*Mathf.Sin(a),p.x*Mathf.Sin(a)+p.y*Mathf.Cos(a));}
        }
        public MojaveBackdropLibrary library;
        public MojaveBackdropLibrary fillLibrary;
        public bool majorForms;
        public bool fillGaps;
        public List<Stamp> stamps=new List<Stamp>();
        public int placementCount,boulderCount,detailCount,largeRockCount,mediumRockCount;
        public int combatOverlapSkipped;
        public int raisedJoinSamples,loweredBankSamples,sharpJoinSamplesBefore,sharpJoinSamplesAfter;
        public float joinMaxLift,bankMaxLowering,joinMaxSlopeBefore,joinMaxSlopeAfter,joinSlope99Before,joinSlope99After;
        float[,] colonyClearance;
        // Stamp.World rotates counter-clockwise in XZ; Unity Y rotation uses the opposite sign.
        public Quaternion PlacementRotation(Stamp stamp,Quaternion authored)=>Quaternion.Euler(0,library!=null&&library.useAllNaturalCandidates?-stamp.yaw:stamp.yaw,0)*authored;
        bool Natural => library!=null&&library.preserveAuthoredEnvironment;
        public IEnumerable<MojavePlacement> NaturalPlacements(MojaveWorld world,Stamp stamp)
        {
            return stamp.tile.placements.Where(a=> {
                if(a.rockOutline!=null&&a.rockOutline.Length>=3)
                    return MojavePatch.OutlineSamples(a.rockOutline).All(q=> {
                        var point=stamp.World(q);return Mathf.Abs(point.x)<world.MapSize*.5f-2&&Mathf.Abs(point.y)<world.MapSize*.5f-2&&world.PlayDistance(point)>.5f;
                    });
                var p=stamp.World(new Vector2(a.position.x,a.position.z));
                float radius=a.kind==MojavePropKind.Boulder?a.radius:.35f;
                return Mathf.Abs(p.x)+radius<world.MapSize*.5f-5&&Mathf.Abs(p.y)+radius<world.MapSize*.5f-5&&world.PlayDistance(p)>radius+.75f;
            });
        }
        public void Plan(MojaveWorld world)
        {
            stamps.Clear();placementCount=boulderCount=detailCount=largeRockCount=mediumRockCount=0;
            combatOverlapSkipped=0;
            colonyClearance=null;
            if(library==null||library.tiles==null||library.tiles.Length==0)throw new InvalidOperationException("Backdrop library is missing.");
            var rng=new System.Random(world.seed^0x7146B);float Range(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());
            if(Natural) {PlanNatural(world,rng);if(world.terrainFinish)MojaveTerrainFinish.PlanShoulders(world);if(fillLibrary!=null)PlanFilled(world,rng,fillLibrary);return;}
            if(fillGaps) {PlanFilled(world,rng);return;}
            foreach(float size in new[]{48f,24f,12f}) {
                var tiles=library.tiles.Where(t=>Mathf.Abs(t.size-size)<.1f).ToArray();if(tiles.Length==0)continue;
                float step=size*.80f,limit=world.MapSize*.5f-size*.65f-5;int count=0,budget=size>30?16:size>15?30:48;
                if(majorForms)budget=size>30?16:18;
                for(float z=-limit+Range(0,step);z<limit;z+=step)for(float x=-limit+Range(0,step);x<limit;x+=step) {
                    if(count>=budget)continue;
                    var p=new Vector2(x+Range(-step*.23f,step*.23f),z+Range(-step*.23f,step*.23f));
                    if(Mathf.Abs(p.x)>limit||Mathf.Abs(p.y)>limit||world.PlayDistance(p)<(size>30?13:7))continue;
                    if(stamps.Any(s=>Vector2.Distance(p,s.center)<(s.tile.size+size)*.36f))continue;
                    var stamp=new Stamp{tile=tiles[rng.Next(tiles.Length)],center=p,yaw=Range(0,360)};
                    if(!stamp.tile.placements.Any(a=>a.kind==MojavePropKind.Boulder&&a.height>1.4f&&Fits(world,stamp.World(new Vector2(a.position.x,a.position.z)),a.radius)))continue;
                    stamps.Add(stamp);count++;
                }
            }
        }
        bool Fits(MojaveWorld world,Vector2 p,float radius)=>Mathf.Abs(p.x)+radius<world.MapSize*.5f-6&&Mathf.Abs(p.y)+radius<world.MapSize*.5f-6&&world.PlayDistance(p)>radius+(world.terrainFinish?1.0f:3)&&(!world.terrainFinish||!MojaveTerrainFinish.Reserved(world,p,radius*.75f));
        IEnumerable<MojavePlacement> Forms(MojavePatch tile)
        {
            if(!majorForms)return tile.placements;
            var rocks=tile.placements.Where(a=>a.kind==MojavePropKind.Boulder&&a.height>=1.4f).OrderByDescending(a=>a.height);
            return (fillGaps?rocks.Where(MainRock):rocks.Take(2))
                .Concat(tile.placements.Where(a=>a.kind==MojavePropKind.Boulder&&a.height>=.65f&&a.height<1.4f).OrderByDescending(a=>a.height).Take(1))
                .Concat(tile.placements.Where(a=>a.kind==MojavePropKind.Plant||a.kind==MojavePropKind.Tree).OrderByDescending(a=>a.height).Take(2))
                .Concat(tile.placements.Where(a=>a.kind==MojavePropKind.Rubble).OrderByDescending(a=>a.height).Take(1));
        }
        static bool MainRock(MojavePlacement a)=>!MojaveWorld.IsLooseStoneName(a.prefab.name)&&(a.prefab.name.Contains("RockAssemble")||(a.height>=4&&a.radius>=3));
        void PlanNatural(MojaveWorld world,System.Random rng)
        {
            bool outlined=library.tiles.Any(t=>t.placements.Any(a=>a.rockOutline!=null&&a.rockOutline.Length>=3));
            const float cell=2;
            float half=world.MapSize*.5f;int width=Mathf.CeilToInt(world.MapSize/cell);
            var available=new bool[width*width];
            var playAvailable=new bool[width*width];
            var occupiedRocks=new List<Vector2[]>();
            for(int z=0;z<width;z++)for(int x=0;x<width;x++) {
                var p=new Vector2((x+.5f)*cell-half,(z+.5f)*cell-half);
                available[z*width+x]=Mathf.Abs(p.x)<half-5&&Mathf.Abs(p.y)<half-5&&world.PlayDistance(p)>(outlined?1.25f:6);
            }
            Array.Copy(available,playAvailable,available.Length);
            bool Available(Vector2 p,bool rockFit,out int index) {
                int x=Mathf.FloorToInt((p.x+half)/cell),z=Mathf.FloorToInt((p.y+half)/cell);index=z*width+x;
                return x>=0&&x<width&&z>=0&&z<width&&(rockFit?playAvailable[index]:available[index]);
            }
            foreach(var tile in library.tiles.OrderBy(t=>!library.useAllNaturalCandidates&&(t.name.StartsWith("02_")||t.name.StartsWith("07_")||t.name.StartsWith("13_"))?1:0)
                .ThenBy(t=>t.placements.Any(a=>a.kind==MojavePropKind.Boulder&&a.height>=4)?0:1).ThenByDescending(t=>t.SurfaceSize.x*t.SurfaceSize.y)) {
                if(fillLibrary!=null&&!library.useAllNaturalCandidates&&tile.name!="01_Long_Ridge"&&tile.name!="03_Northern_Chain"&&tile.name!="04_Rock_Valley"&&tile.name!="05_Vertical_Ridge"&&tile.name!="08_Rock_Hollow")continue;
                var points=new List<Vector2>();var extent=tile.SurfaceSize;
                bool rockFit=world.refinedRoads&&library.useAllNaturalCandidates&&outlined;
                bool compact=rockFit&&extent.x*extent.y<1500;
                var outlines=tile.placements.Where(a=>a.rockOutline!=null&&a.rockOutline.Length>=3).Select(a=>a.rockOutline).ToArray();
                float gap=1.25f+Mathf.Clamp((tile.heights.Max()-tile.heights.Min())*.10f,0,1.25f);
                var corePoints=new List<Vector2>();
                for(float z=-extent.y*.5f+2;z<extent.y*.5f;z+=4)for(float x=-extent.x*.5f+2;x<extent.x*.5f;x+=4)
                    if(tile.Core(new Vector2(x,z))>.99f)corePoints.Add(new Vector2(x,z));
                if(corePoints.Count==0)corePoints.Add(Vector2.zero);
                // Trimmed ground already includes the rock footing; do not expand it back into a bounding square.
                for(float z=-extent.y*.5f+cell*.5f;z<extent.y*.5f;z+=cell)
                    for(float x=-extent.x*.5f+cell*.5f;x<extent.x*.5f;x+=cell)
                        if(!rockFit&&(outlined?tile.Footprint(new Vector2(x,z))>.05f:tile.Core(new Vector2(x,z))>.2f))points.Add(new Vector2(x,z));
                if(outlined)foreach(var rock in tile.placements.Where(a=>a.rockOutline!=null&&a.rockOutline.Length>=3))
                    points.AddRange(MojavePatch.OutlineSamples(rock.rockOutline));
                if(points.Count==0)throw new InvalidOperationException("Empty natural footprint: "+tile.name);
                int accepted=0,limit=extent.x*extent.y>6000?2:extent.x*extent.y>1500?6:extent.x*extent.y>500?10:16;
                if(tile.name.StartsWith("02_"))limit=1;
                if(tile.name.StartsWith("13_"))limit=3;
                if(tile.name.StartsWith("07_"))limit=2;
                float step=Mathf.Clamp(Mathf.Min(extent.x,extent.y)*.22f,5,13);
                var centers=new List<Vector2>();
                for(int pass=0;pass<2;pass++)for(float z=-half+7+pass*step*.5f;z<half-7;z+=step)
                    for(float x=-half+7+pass*step*.5f;x<half-7;x+=step)
                        centers.Add(new Vector2(x,z)+new Vector2((float)rng.NextDouble()-.5f,(float)rng.NextDouble()-.5f)*step*.18f);
                for(int i=centers.Count-1;i>0;i--){int j=rng.Next(i+1);var p=centers[i];centers[i]=centers[j];centers[j]=p;}
                if(!compact&&tile.placements.Any(a=>a.kind==MojavePropKind.Boulder&&a.height>=4))centers=centers.OrderByDescending(p=>world.PlayDistance(p)).ToList();
                foreach(var center in centers)for(int turn=0,offset=rng.Next(12);turn<12;turn++) {
                    if(accepted>=limit)break;
                    var stamp=new Stamp{tile=tile,center=center,yaw=((turn+offset)%12)*30,connectionPadding=gap};
                    var cells=new HashSet<int>();bool fits=true;
                    foreach(var point in points) {
                        if(!Available(stamp.World(point),rockFit,out int index)){fits=false;break;}
                        cells.Add(index);
                    }
                    if(!fits)continue;
                    if(!outlined)foreach(var a in tile.placements) {
                        if(a.kind!=MojavePropKind.Boulder||a.height<1.4f)continue;
                        var p=stamp.World(new Vector2(a.position.x,a.position.z));
                        float radius=a.radius+gap;
                        for(float z=-radius;z<=radius;z+=cell)for(float x=-radius;x<=radius;x+=cell) {
                            if(!Available(p+new Vector2(x,z),false,out int index)){fits=false;break;}
                            cells.Add(index);
                        }
                        if(!fits)break;
                    }
                    if(!fits)continue;
                    // All authored formations share their sand skirts. Only substantial intersections of real rock outlines reject a placement.
                    Vector2[][] placedOutlines=null;
                    if(rockFit) {
                        placedOutlines=outlines.Select(o=>o.Select(stamp.World).ToArray()).ToArray();
                        if(placedOutlines.Any(a=>occupiedRocks.Any(b=>RockOverlap(a,b,.4f))))continue;
                    }
                    // The coarse occupancy grid is only a broad phase; confirm the actual rock outline against the curved play boundary.
                    if(library.useAllNaturalCandidates&&outlined&&tile.placements.Where(a=>a.rockOutline!=null&&a.rockOutline.Length>=3)
                        .Any(a=>MojavePatch.OutlineSamples(a.rockOutline,.5f).Any(q=>world.PlayDistance(stamp.World(q))<.4f)))continue;
                    stamp.groundHeight=Mathf.Max(corePoints.Average(q=>world.PlannedBackgroundHeight(stamp.World(q))-tile.Height(q)),
                        corePoints.Max(q=>world.NearbyPlayHeight(stamp.World(q))+(world.terrainFinish?3.5f:7)-tile.Height(q)));
                    stamps.Add(stamp);accepted++;foreach(int index in cells)available[index]=false;
                    occupiedRocks.AddRange(placedOutlines??outlines.Select(o=>o.Select(stamp.World).ToArray()));
                    break;
                }
            }
        }
        // Convex LOD0 outlines may touch or overlap shallowly; empty tile terrain is not a solid.
        public static bool RockOverlap(Vector2[] a,Vector2[] b,float allowed)
        {
            bool Separated(Vector2[] edges) {
                for(int i=0;i<edges.Length;i++) {
                    var d=edges[(i+1)%edges.Length]-edges[i];if(d.sqrMagnitude<.00001f)continue;
                    var axis=new Vector2(-d.y,d.x).normalized;
                    float a0=float.PositiveInfinity,a1=float.NegativeInfinity,b0=a0,b1=a1;
                    foreach(var p in a){float v=Vector2.Dot(p,axis);a0=Mathf.Min(a0,v);a1=Mathf.Max(a1,v);}
                    foreach(var p in b){float v=Vector2.Dot(p,axis);b0=Mathf.Min(b0,v);b1=Mathf.Max(b1,v);}
                    if(Mathf.Min(a1,b1)-Mathf.Max(a0,b0)<=allowed)return true;
                }
                return false;
            }
            return !Separated(a)&&!Separated(b);
        }
        bool FitsBesideColony(MojaveWorld world,Vector2 p,float radius)
        {
            if(!Fits(world,p,radius))return false;
            if(world.organicConnections)return ColonyClearance(world,p)>radius*.78f+1.25f;
            foreach(var s in stamps.Where(s=>!s.denseFill)) {
                if(s.tile.Core(s.Local(p))>.05f)return false;
                for(int i=0;i<8;i++)if(s.tile.Core(s.Local(p+new Vector2(Mathf.Cos(i*Mathf.PI*.25f),Mathf.Sin(i*Mathf.PI*.25f))*(radius+1)))>.05f)return false;
            }
            return true;
        }
        float ColonyClearance(MojaveWorld world,Vector2 p)
        {
            const float cell=2;float half=world.MapSize*.5f;int n=Mathf.CeilToInt(world.MapSize/cell)+1;
            if(colonyClearance==null) {
                colonyClearance=new float[n,n];for(int z=0;z<n;z++)for(int x=0;x<n;x++)colonyClearance[z,x]=1000;
                // Reserve the actual stone masses; the old landform mask also excluded their empty sand skirts.
                foreach(var stamp in stamps.Where(s=>!s.denseFill))foreach(var rock in NaturalPlacements(world,stamp)) {
                    if(rock.kind!=MojavePropKind.Boulder||rock.height<.65f)continue;
                    var center=stamp.World(new Vector2(rock.position.x,rock.position.z));
                    float radius=(MojaveWorld.IsLooseStoneName(rock.prefab.name)?Mathf.Min(1.1f,rock.radius):rock.radius)*.8f,reach=radius+32;
                    int x0=Mathf.Max(0,Mathf.FloorToInt((center.x-reach+half)/cell)),x1=Mathf.Min(n-1,Mathf.CeilToInt((center.x+reach+half)/cell));
                    int z0=Mathf.Max(0,Mathf.FloorToInt((center.y-reach+half)/cell)),z1=Mathf.Min(n-1,Mathf.CeilToInt((center.y+reach+half)/cell));
                    for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
                    {
                        var q=new Vector2(x*cell-half,z*cell-half);
                        float distance=rock.rockOutline!=null&&rock.rockOutline.Length>=3
                            ?MojavePatch.OutlineDistance(rock.rockOutline,stamp.Local(q))
                            :Vector2.Distance(q,center)-radius;
                        colonyClearance[z,x]=Mathf.Min(colonyClearance[z,x],distance);
                    }
                }
            }
            return colonyClearance[Mathf.Clamp(Mathf.RoundToInt((p.y+half)/cell),0,n-1),Mathf.Clamp(Mathf.RoundToInt((p.x+half)/cell),0,n-1)];
        }
        void PlanFilled(MojaveWorld world,System.Random rng,MojaveBackdropLibrary supportingLibrary=null)
        {
            const float cell=4;
            float half=world.MapSize*.5f;int width=Mathf.CeilToInt(world.MapSize/cell);
            var covered=new bool[width*width];
            var selectedLibrary=supportingLibrary??library;
            bool Fit(Vector2 p,float radius)=>supportingLibrary!=null?FitsBesideColony(world,p,radius):Fits(world,p,radius);
            var rocks=selectedLibrary.tiles.ToDictionary(t=>t,t=>Forms(t).Where(a=>a.kind==MojavePropKind.Boulder&&a.height>=1.4f).ToArray());
            HashSet<int> RockCells(Stamp stamp)
            {
                var cells=new HashSet<int>();
                foreach(var rock in rocks[stamp.tile]) {
                    var p=stamp.World(new Vector2(rock.position.x,rock.position.z));if(!Fit(p,rock.radius))continue;
                    float radius=Mathf.Max(2,rock.radius*.70f);
                    int minX=Mathf.Max(0,Mathf.FloorToInt((p.x-radius+half)/cell)),maxX=Mathf.Min(width-1,Mathf.FloorToInt((p.x+radius+half)/cell));
                    int minZ=Mathf.Max(0,Mathf.FloorToInt((p.y-radius+half)/cell)),maxZ=Mathf.Min(width-1,Mathf.FloorToInt((p.y+radius+half)/cell));
                    for(int z=minZ;z<=maxZ;z++)for(int x=minX;x<=maxX;x++) {
                        var q=new Vector2((x+.5f)*cell-half,(z+.5f)*cell-half);
                        if((q-p).sqrMagnitude<=radius*radius)cells.Add(z*width+x);
                    }
                }
                return cells;
            }
            // Fill actual rock footprints; tile rectangles may overlap and do not reserve sand gaps.
            foreach(float size in new[]{48f,24f,12f}) {
                var tiles=selectedLibrary.tiles.Where(t=>Mathf.Abs(t.size-size)<.1f).ToArray();if(tiles.Length==0)continue;
                float step=size*.55f,limit=half-6-size*.15f;var candidates=new List<Vector2>();
                for(int pass=0;pass<2;pass++)for(float z=-limit+step*.5f*pass;z<limit;z+=step)for(float x=-limit+step*.5f*pass;x<limit;x+=step)
                    candidates.Add(new Vector2(x+((float)rng.NextDouble()-.5f)*step*.30f,z+((float)rng.NextDouble()-.5f)*step*.30f));
                for(int i=candidates.Count-1;i>0;i--){int j=rng.Next(i+1);var p=candidates[i];candidates[i]=candidates[j];candidates[j]=p;}
                foreach(var center in candidates) {
                    if(world.PlayDistance(center)<2)continue;
                    Stamp best=null;HashSet<int> bestCells=null;int bestScore=0;
                    for(int attempt=0;attempt<6;attempt++) {
                        var stamp=new Stamp{tile=tiles[rng.Next(tiles.Length)],center=center,yaw=(float)rng.NextDouble()*360,denseFill=supportingLibrary!=null};
                        var cells=RockCells(stamp);int score=cells.Count(index=>!covered[index]);
                        if(score<(size>30?12:size>15?6:3)||score<cells.Count*.50f||score<=bestScore)continue;
                        best=stamp;bestCells=cells;bestScore=score;
                    }
                    if(best==null)continue;
                    stamps.Add(best);foreach(int index in bestCells)covered[index]=true;
                }
            }
        }
        static float Weight(Stamp stamp,Vector2 local,float distance)
        {
            if(stamp.tile.backgroundMask!=null&&stamp.tile.backgroundMask.Length>0)
                return stamp.tile.Footprint(local)*Mathf.SmoothStep(0,1,Mathf.InverseLerp(1,4,distance));
            float edge=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(stamp.tile.size*.28f,stamp.tile.size*.5f,Mathf.Max(Mathf.Abs(local.x),Mathf.Abs(local.y))));
            return edge*Mathf.SmoothStep(0,1,Mathf.InverseLerp(4,9,distance));
        }
        public bool Reserves(Vector2 p)=>stamps.Any(s=>Natural&&!s.denseFill?s.tile.Footprint(s.Local(p))>.001f:Mathf.Max(Mathf.Abs(s.Local(p).x),Mathf.Abs(s.Local(p).y))<s.tile.size*.36f);
        public void BlendGround(MojaveWorld world,float[,] heights,float[,,] paint,float[,] distances)
        {
            if(Natural){BlendRockGround(world,heights,paint,distances,stamps.Where(s=>s.denseFill));BlendNaturalGround(world,heights,paint,distances);return;}
            BlendRockGround(world,heights,paint,distances,stamps);
        }
        void BlendRockGround(MojaveWorld world,float[,] heights,float[,,] paint,float[,] distances,IEnumerable<Stamp> pieces)
        {
            int n=heights.GetLength(0),alpha=paint.GetLength(0);float cell=world.MapSize/(n-1),half=world.MapSize*.5f;
            var sample=new float[world.catalog.layers.Length];
            var reliefSum=fillGaps?new float[n,n]:null;var reliefWeight=fillGaps?new float[n,n]:null;
            foreach(var stamp in pieces) {
                float radius=stamp.tile.size*.71f;
                int minX=Mathf.Max(0,Mathf.FloorToInt((stamp.center.x-radius+half)/cell)),maxX=Mathf.Min(n-1,Mathf.CeilToInt((stamp.center.x+radius+half)/cell));
                int minZ=Mathf.Max(0,Mathf.FloorToInt((stamp.center.y-radius+half)/cell)),maxZ=Mathf.Min(n-1,Mathf.CeilToInt((stamp.center.y+radius+half)/cell));
                for(int z=minZ;z<=maxZ;z++)for(int x=minX;x<=maxX;x++) {
                    var local=stamp.Local(new Vector2(x*cell-half,z*cell-half));float weight=Weight(stamp,local,distances[z,x]);if(weight<=0)continue;
                    float relief=stamp.tile.Height(local)*.4f+(stamp.tile.Height(local+Vector2.right*2)+stamp.tile.Height(local-Vector2.right*2)+stamp.tile.Height(local+Vector2.up*2)+stamp.tile.Height(local-Vector2.up*2))*.15f;
                    float offset=Mathf.Clamp(relief*(majorForms?.55f:.32f),majorForms?-3.2f:-1.8f,majorForms?3.2f:1.8f)*weight/MojaveLayout.Elevation;
                    if(fillGaps){reliefSum[z,x]+=offset;reliefWeight[z,x]+=weight;}else heights[z,x]+=offset;
                    if(x>=alpha||z>=alpha)continue;
                    stamp.tile.Paint(local,sample);for(int layer=0;layer<sample.Length;layer++)paint[z,x,layer]=Mathf.Lerp(paint[z,x,layer],sample[layer],weight*.75f);
                }
            }
            if(fillGaps)for(int z=0;z<n;z++)for(int x=0;x<n;x++)heights[z,x]+=reliefSum[z,x]/Mathf.Max(1,reliefWeight[z,x]);
        }
        void BlendNaturalGround(MojaveWorld world,float[,] heights,float[,,] paint,float[,] distances)
        {
            int n=heights.GetLength(0),alpha=paint.GetLength(0);float step=world.MapSize/(n-1),half=world.MapSize*.5f;
            // Source relief guides the rock belt, while its surrounding land can adapt to the shared surface.
            const int grid=129;float spacing=world.MapSize/(grid-1);
            var ground=new float[grid,grid];var basis=new float[grid,grid];var target=new float[grid,grid];var strength=new float[grid,grid];
            var fixedPoint=new bool[grid,grid];var next=new float[grid,grid];
            for(int z=0;z<grid;z++)for(int x=0;x<grid;x++) {
                int ix=Mathf.RoundToInt(x/(float)(grid-1)*(n-1)),iz=Mathf.RoundToInt(z/(float)(grid-1)*(n-1));
                basis[z,x]=ground[z,x]=heights[iz,ix]*MojaveLayout.Elevation;
            }
            foreach(var stamp in stamps.Where(s=>!s.denseFill)) {
                float radius=stamp.tile.SurfaceSize.magnitude*.5f;
                int minX=Mathf.Max(0,Mathf.FloorToInt((stamp.center.x-radius+half)/spacing)),maxX=Mathf.Min(grid-1,Mathf.CeilToInt((stamp.center.x+radius+half)/spacing));
                int minZ=Mathf.Max(0,Mathf.FloorToInt((stamp.center.y-radius+half)/spacing)),maxZ=Mathf.Min(grid-1,Mathf.CeilToInt((stamp.center.y+radius+half)/spacing));
                for(int z=minZ;z<=maxZ;z++)for(int x=minX;x<=maxX;x++) {
                    var local=stamp.Local(new Vector2(x*spacing-half,z*spacing-half));float core=stamp.tile.Core(local);
                    if(core<=0||core*3<=strength[z,x])continue;
                    target[z,x]=stamp.groundHeight+stamp.tile.Height(local);strength[z,x]=core*3;
                    ground[z,x]=Mathf.Lerp(ground[z,x],target[z,x],core);
                }
            }
            for(int z=0;z<grid;z++)for(int x=0;x<grid;x++) {
                int ix=Mathf.RoundToInt(x/(float)(grid-1)*(n-1)),iz=Mathf.RoundToInt(z/(float)(grid-1)*(n-1));
                float d=distances[iz,ix],play=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(1,7,d));
                if(play>0){target[z,x]=basis[z,x];strength[z,x]=Mathf.Max(strength[z,x],play*45);}
                if(d<=1){fixedPoint[z,x]=true;ground[z,x]=basis[z,x];}
            }
            for(int iteration=0;iteration<100;iteration++) {
                for(int z=0;z<grid;z++)for(int x=0;x<grid;x++) {
                    if(fixedPoint[z,x]){next[z,x]=ground[z,x];continue;}
                    float neighbours=ground[z,Mathf.Max(0,x-1)]+ground[z,Mathf.Min(grid-1,x+1)]+ground[Mathf.Max(0,z-1),x]+ground[Mathf.Min(grid-1,z+1),x];
                    next[z,x]=(neighbours+basis[z,x]*.5f+target[z,x]*strength[z,x])/(4.5f+strength[z,x]);
                }
                var swap=ground;ground=next;next=swap;
            }
            for(int z=0;z<n;z++)for(int x=0;x<n;x++) {
                float fx=x/(float)(n-1)*(grid-1),fz=z/(float)(n-1)*(grid-1);
                int ix=Mathf.Min(grid-2,(int)fx),iz=Mathf.Min(grid-2,(int)fz);
                float h=Mathf.Lerp(Mathf.Lerp(ground[iz,ix],ground[iz,ix+1],fx-ix),Mathf.Lerp(ground[iz+1,ix],ground[iz+1,ix+1],fx-ix),fz-iz);
                float adapt=Mathf.SmoothStep(0,1,Mathf.InverseLerp(1,5,distances[z,x]));
                heights[z,x]=Mathf.Lerp(heights[z,x],h/MojaveLayout.Elevation,adapt);
            }
            var sample=new float[world.catalog.layers.Length];
            foreach(var stamp in stamps.Where(s=>!s.denseFill)) {
                float radius=stamp.tile.backgroundBlendSize.magnitude*.5f;
                int minX=Mathf.Max(0,Mathf.FloorToInt((stamp.center.x-radius+half)/step)),maxX=Mathf.Min(n-1,Mathf.CeilToInt((stamp.center.x+radius+half)/step));
                int minZ=Mathf.Max(0,Mathf.FloorToInt((stamp.center.y-radius+half)/step)),maxZ=Mathf.Min(n-1,Mathf.CeilToInt((stamp.center.y+radius+half)/step));
                for(int z=minZ;z<=maxZ;z++)for(int x=minX;x<=maxX;x++) {
                    var local=stamp.Local(new Vector2(x*step-half,z*step-half));
                    float core=stamp.tile.Core(local)*Mathf.SmoothStep(0,1,Mathf.InverseLerp(1,5,distances[z,x]));
                    float weight=Mathf.Max(core,stamp.tile.BlendFootprint(local)*.85f)*Mathf.SmoothStep(0,1,Mathf.InverseLerp(3,9,distances[z,x]));
                    if(weight<=0)continue;
                    if(x>=alpha||z>=alpha)continue;
                    stamp.tile.Paint(local,sample);
                    for(int layer=0;layer<sample.Length;layer++)paint[z,x,layer]=Mathf.Lerp(paint[z,x,layer],sample[layer],weight);
                }
            }
            ConnectBackgroundSlopes(world,heights,distances);
        }
        void ConnectBackgroundSlopes(MojaveWorld world,float[,] heights,float[,] distances)
        {
            int n=heights.GetLength(0);float step=world.MapSize/(n-1);
            void Slopes(out float maximum,out float percentile,out int sharp) {
                var values=new List<float>();maximum=0;sharp=0;
                for(int z=0;z<n-1;z+=4)for(int x=0;x<n-1;x+=4) {
                    if(distances[z,x]<=0)continue;
                    float slope=Mathf.Max(Mathf.Abs(heights[z,x+1]-heights[z,x]),Mathf.Abs(heights[z+1,x]-heights[z,x]))*MojaveLayout.Elevation/step;
                    maximum=Mathf.Max(maximum,slope);if(slope>1)sharp++;values.Add(slope);
                }
                values.Sort();percentile=values.Count>0?values[Mathf.Min(values.Count-1,(int)(values.Count*.99f))]:0;
            }
            Slopes(out joinMaxSlopeBefore,out joinSlope99Before,out sharpJoinSamplesBefore);
            const int grid=257;float spacing=world.MapSize/(grid-1);
            var grade=new float[grid,grid];
            for(int z=0;z<grid;z++)for(int x=0;x<grid;x++) {
                float land=world.layout.Noise(new Vector2(x*spacing-world.MapSize*.5f,z*spacing-world.MapSize*.5f),.027f,911);
                grade[z,x]=world.refinedRoads?Mathf.Lerp(.22f,.34f,land):world.terrainFinish?Mathf.Lerp(.27f,.45f,land):.5f;
            }
            var original=new float[grid,grid];var raised=new float[grid,grid];var ceiling=new float[grid,grid];var fixedPoint=new bool[grid,grid];
            for(int z=0;z<grid;z++)for(int x=0;x<grid;x++) {
                int ix=Mathf.RoundToInt(x/(float)(grid-1)*(n-1)),iz=Mathf.RoundToInt(z/(float)(grid-1)*(n-1));
                original[z,x]=raised[z,x]=heights[iz,ix]*MojaveLayout.Elevation;
                fixedPoint[z,x]=distances[iz,ix]<=0;
                ceiling[z,x]=fixedPoint[z,x]?original[z,x]:float.PositiveInfinity;
            }
            // Extend a gentle maximum bank height out from the finished playable field, including rounded junctions.
            for(int pass=0;pass<2;pass++)for(int order=0;order<4;order++)
                for(int zi=0;zi<grid;zi++)for(int xi=0;xi<grid;xi++) {
                    int z=(order&1)==0?zi:grid-1-zi,x=(order&2)==0?xi:grid-1-xi;if(fixedPoint[z,x])continue;
                    for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++) {
                        if((dx==0&&dz==0)||x+dx<0||x+dx>=grid||z+dz<0||z+dz>=grid)continue;
                        float rise=grade[z,x]*spacing*(dx!=0&&dz!=0?1.4142136f:1);
                        ceiling[z,x]=Mathf.Min(ceiling[z,x],ceiling[z+dz,x+dx]+rise);
                    }
                }
            for(int z=0;z<grid;z++)for(int x=0;x<grid;x++)raised[z,x]=Mathf.Min(raised[z,x],ceiling[z,x]);
            // Raise low land toward neighbouring ridges. Only playable ground is fixed; source skirts may bend freely.
            for(int pass=0;pass<4;pass++)for(int order=0;order<4;order++) {
                for(int zi=0;zi<grid;zi++)for(int xi=0;xi<grid;xi++) {
                    int z=(order&1)==0?zi:grid-1-zi,x=(order&2)==0?xi:grid-1-xi;if(fixedPoint[z,x])continue;
                    float h=raised[z,x];
                    for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++) {
                        if((dx==0&&dz==0)||x+dx<0||x+dx>=grid||z+dz<0||z+dz>=grid)continue;
                        float fall=(world.terrainFinish?grade[z,x]:.38f)*spacing*(dx!=0&&dz!=0?1.4142136f:1);
                        h=Mathf.Max(h,raised[z+dz,x+dx]-fall);
                    }
                    raised[z,x]=Mathf.Min(h,ceiling[z,x]);
                }
            }
            raisedJoinSamples=loweredBankSamples=0;joinMaxLift=bankMaxLowering=0;
            for(int z=0;z<n;z++)for(int x=0;x<n;x++) {
                float weight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,1,distances[z,x]));
                if(weight<=0)continue;
                float fx=x/(float)(n-1)*(grid-1),fz=z/(float)(n-1)*(grid-1);int ix=Mathf.Min(grid-2,(int)fx),iz=Mathf.Min(grid-2,(int)fz);
                float Delta(int zz,int xx)=>raised[zz,xx]-original[zz,xx];
                float lift=Mathf.Lerp(Mathf.Lerp(Delta(iz,ix),Delta(iz,ix+1),fx-ix),Mathf.Lerp(Delta(iz+1,ix),Delta(iz+1,ix+1),fx-ix),fz-iz)*weight;
                heights[z,x]+=lift/MojaveLayout.Elevation;
                if(lift>.05f)raisedJoinSamples++;if(lift<-.05f)loweredBankSamples++;
                joinMaxLift=Mathf.Max(joinMaxLift,lift);bankMaxLowering=Mathf.Max(bankMaxLowering,-lift);
            }
            // Coarse height blending preserves detail, but can leave sharp residuals between samples.
            // Project only excessive local grades at the final resolution, keeping playable samples fixed.
            if(world.terrainFinish) {
                if(world.refinedRoads) {
                    var smooth=new float[n,n];
                    // Round the shoulder's change of slope, keeping the combat and road footprint intact.
                    for(int pass=0;pass<5;pass++) {
                        for(int z=1;z<n-1;z++)for(int x=1;x<n-1;x++) {
                            float weight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,2,distances[z,x]))*.55f;
                            float average=(heights[z,x-1]+heights[z,x+1]+heights[z-1,x]+heights[z+1,x])*.25f;
                            smooth[z,x]=Mathf.Lerp(heights[z,x],average,weight);
                        }
                        for(int z=1;z<n-1;z++)for(int x=1;x<n-1;x++)heights[z,x]=smooth[z,x];
                    }
                }
                float rise=(world.refinedRoads?.32f:.50f)*step/MojaveLayout.Elevation;
                void Limit(int z,int x,int zz,int xx,float maximum) {
                    float difference=heights[z,x]-heights[zz,xx],excess=Mathf.Abs(difference)-maximum;
                    if(excess<=0)return;
                    float a=distances[z,x]>0?1:0,b=distances[zz,xx]>0?1:0;
                    if(a+b==0)return;
                    float correction=Mathf.Sign(difference)*excess/(a+b);
                    heights[z,x]-=correction*a;heights[zz,xx]+=correction*b;
                }
                for(int pass=0;pass<12;pass++)for(int zi=0;zi<n-1;zi++)for(int xi=0;xi<n-1;xi++) {
                    int z=(pass&1)==0?zi:n-2-zi,x=(pass&1)==0?xi:n-2-xi;
                    Limit(z,x,z,x+1,rise);Limit(z,x,z+1,x,rise);
                    Limit(z,x,z+1,x+1,rise*1.4142136f);Limit(z,x+1,z+1,x,rise*1.4142136f);
                }
            }
            Slopes(out joinMaxSlopeAfter,out joinSlope99After,out sharpJoinSamplesAfter);
        }
        public void WriteDetails(MojaveWorld world,TerrainData destination)
        {
            const int resolution=512;destination.SetDetailResolution(resolution,16);
            var first=library.tiles[0].slicedPrefab.GetComponentInChildren<Terrain>().terrainData;
            destination.detailPrototypes=first.detailPrototypes;detailCount=0;
            var sourceData=new Dictionary<MojavePatch,TerrainData>();var sourceLayers=new Dictionary<MojavePatch,int[][,]>();
            foreach(var tile in stamps.Select(s=>s.tile).Distinct()) {
                var td=tile.slicedPrefab.GetComponentInChildren<Terrain>().terrainData;sourceData[tile]=td;
                var layers=new int[td.detailPrototypes.Length][,];for(int l=0;l<layers.Length;l++)layers[l]=td.GetDetailLayer(0,0,td.detailWidth,td.detailHeight,l);sourceLayers[tile]=layers;
            }
            var cells=new List<(Stamp stamp,int x,int z,int sx,int sz,float weight)>();float step=world.MapSize/resolution,half=world.MapSize*.5f;
            foreach(var stamp in stamps) {
                var td=sourceData[stamp.tile];float radius=Natural?stamp.tile.SurfaceSize.magnitude*.5f:stamp.tile.size*.71f;
                int minX=Mathf.Max(0,Mathf.FloorToInt((stamp.center.x-radius+half)/step)),maxX=Mathf.Min(resolution-1,Mathf.CeilToInt((stamp.center.x+radius+half)/step));
                int minZ=Mathf.Max(0,Mathf.FloorToInt((stamp.center.y-radius+half)/step)),maxZ=Mathf.Min(resolution-1,Mathf.CeilToInt((stamp.center.y+radius+half)/step));
                for(int z=minZ;z<=maxZ;z++)for(int x=minX;x<=maxX;x++) {
                    var p=new Vector2((x+.5f)*step-half,(z+.5f)*step-half);var local=stamp.Local(p);float weight=Weight(stamp,local,world.PlayDistance(p));if(weight<=.1f)continue;
                    var extent=stamp.tile.SurfaceSize;
                    int sx=Mathf.Clamp((int)((local.x/extent.x+.5f)*td.detailWidth),0,td.detailWidth-1),sz=Mathf.Clamp((int)((local.y/extent.y+.5f)*td.detailHeight),0,td.detailHeight-1);
                    cells.Add((stamp,x,z,sx,sz,weight));
                }
            }
            for(int l=0;l<first.detailPrototypes.Length;l++) {
                var layer=new int[resolution,resolution];foreach(var c in cells) {
                    int value=Mathf.Clamp(Mathf.RoundToInt(sourceLayers[c.stamp.tile][l][c.sz,c.sx]*c.weight*(Natural?1:.65f)),0,16);
                    layer[c.z,c.x]=Mathf.Max(layer[c.z,c.x],value);
                }
                foreach(int value in layer)detailCount+=value;destination.SetDetailLayer(0,0,l,layer);
            }
            world.surface.drawTreesAndFoliage=true;world.surface.detailObjectDistance=85;world.surface.detailObjectDensity=1;
        }
        public void Dress(MojaveWorld world)
        {
            var root=new GameObject(Natural?"Background · complete natural environment pieces":"Background · sliced rock colonies 12m 24m 48m").transform;root.SetParent(world.generatedRoot,false);
            for(int i=0;i<stamps.Count;i++) {
                var stamp=stamps[i];var parent=new GameObject("Backdrop_"+i.ToString("00")+" · "+stamp.tile.name).transform;parent.SetParent(root,false);
                bool colony=Natural&&!stamp.denseFill;
                IEnumerable<MojavePlacement> placements=stamp.tile.placements;
                if(colony)placements=NaturalPlacements(world,stamp);
                if(majorForms&&!colony) {
                    var eligible=stamp.tile.placements.Where(a=>stamp.denseFill?FitsBesideColony(world,stamp.World(new Vector2(a.position.x,a.position.z)),a.kind==MojavePropKind.Boulder?a.radius:.45f):Fits(world,stamp.World(new Vector2(a.position.x,a.position.z)),a.kind==MojavePropKind.Boulder?a.radius:.45f)).ToArray();
                    var rocks=eligible.Where(a=>a.kind==MojavePropKind.Boulder&&a.height>=1.4f).OrderByDescending(a=>a.height);
                    var medium=eligible.Where(a=>a.kind==MojavePropKind.Boulder&&a.height>=.65f&&a.height<1.4f).OrderByDescending(a=>a.height).Take(1);
                    var plants=eligible.Where(a=>a.kind==MojavePropKind.Plant||a.kind==MojavePropKind.Tree).OrderByDescending(a=>a.height).Take(2);
                    var rubble=eligible.Where(a=>a.kind==MojavePropKind.Rubble).OrderByDescending(a=>a.height).Take(1);
                    placements=(fillGaps?rocks.Where(MainRock):rocks.Take(2)).Concat(medium).Concat(plants).Concat(rubble);
                }
                foreach(var authored in placements) {
                    var p=stamp.World(new Vector2(authored.position.x,authored.position.z));bool rock=authored.kind==MojavePropKind.Boulder;
                    float radius=rock?authored.radius:.45f;
                    if(!colony&&!(stamp.denseFill?FitsBesideColony(world,p,radius):Fits(world,p,radius)))continue;
                    if(!colony&&rock&&!fillGaps&&(world.blockers??Array.Empty<Vector3>()).Any(b=>Vector2.Distance(p,new Vector2(b.x,b.y))<b.z+radius*.70f))continue;
                    var go=Instantiate(authored.prefab,world.Ground(p,authored.position.y),PlacementRotation(stamp,authored.rotation),parent);
                    go.transform.localScale=authored.scale;go.name=authored.prefab.name;
                    if(fillGaps&&rock&&!(colony&&authored.rockOutline!=null&&authored.rockOutline.Length>=3)&&TouchesPlay(world,go)) {
                        combatOverlapSkipped++;go.SetActive(false);
                        if(Application.isPlaying)Destroy(go);else DestroyImmediate(go);
                        continue;
                    }
                    if(rock){if(colony)world.LimitLooseStone(go);else world.SeatRock(go);}
                    foreach(var collider in go.GetComponentsInChildren<Collider>(true))collider.enabled=false;
                    foreach(var lod in go.GetComponentsInChildren<LODGroup>(true))lod.fadeMode=LODFadeMode.None;
                    placementCount++;if(rock){boulderCount++;if(authored.height>=4)largeRockCount++;else if(authored.height>=1.4f)mediumRockCount++;}
                }
            }
        }
        static bool TouchesPlay(MojaveWorld world,GameObject go)
        {
            var renderers=go.GetComponentsInChildren<Renderer>(true);if(renderers.Length==0)return false;
            var b=renderers[0].bounds;for(int i=1;i<renderers.Length;i++)b.Encapsulate(renderers[i].bounds);
            for(float z=b.min.z;z<=b.max.z;z+=1)for(float x=b.min.x;x<=b.max.x;x+=1)
                if(world.PlayDistance(new Vector2(x,z))<=0)return true;
            return false;
        }
    }
}
