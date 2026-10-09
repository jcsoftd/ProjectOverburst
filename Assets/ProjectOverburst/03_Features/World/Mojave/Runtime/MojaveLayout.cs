using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overburst.Mojave
{
    public enum MojaveCombatKind { OpenBasin, LongWash, BroadCourt, AsymmetricHollow, TwinClearing, CrescentBasin, JunctionClearing, CoverCourt }

    [Serializable]
    public sealed class MojavePlace
    {
        public string name;
        public Vector2 center;
        public Vector2 radius;
        public float height;
        public float rotation;
        public int patch;
        public int theme;
        public MojaveCombatKind kind;
        public string KindLabel => CombatLabel(kind);
        public static string CombatLabel(MojaveCombatKind kind)
        {
            switch(kind) {
                case MojaveCombatKind.OpenBasin:return "넓은 분지";
                case MojaveCombatKind.LongWash:return "길쭉한 전장";
                case MojaveCombatKind.BroadCourt:return "넓은 전투 마당";
                case MojaveCombatKind.AsymmetricHollow:return "비대칭 골짜기";
                case MojaveCombatKind.TwinClearing:return "이어진 두 공터";
                case MojaveCombatKind.CrescentBasin:return "굽은 분지";
                case MojaveCombatKind.JunctionClearing:return "갈라지는 공터";
                default:return "암석 엄폐 전장";
            }
        }
        public float Distance(Vector2 p)
        {
            var q=Local(p);var a=radius;
            float Ellipse(Vector2 point,Vector2 size)=>(new Vector2(point.x/size.x,point.y/size.y).magnitude-1)*Mathf.Min(size.x,size.y);
            float Court(Vector2 point,Vector2 size)=>(Mathf.Pow(Mathf.Pow(Mathf.Abs(point.x/size.x),4)+Mathf.Pow(Mathf.Abs(point.y/size.y),4),.25f)-1)*Mathf.Min(size.x,size.y);
            switch(kind) {
                case MojaveCombatKind.LongWash:return Ellipse(q,new Vector2(a.x*1.28f,a.y*.68f));
                case MojaveCombatKind.BroadCourt:return Court(q,new Vector2(a.x*1.08f,a.y*.95f));
                case MojaveCombatKind.AsymmetricHollow:
                    float angle=Mathf.Atan2(q.y/a.y,q.x/a.x);float edge=1+.18f*Mathf.Cos(angle+.8f)+.12f*Mathf.Sin(angle*2+1.2f);
                    return Ellipse(q,a*edge);
                case MojaveCombatKind.TwinClearing:
                    return Mathf.Min(Ellipse(q-new Vector2(a.x*.58f,0),new Vector2(a.x*.70f,a.y*.83f)),Ellipse(q+new Vector2(a.x*.58f,0),new Vector2(a.x*.70f,a.y*.83f)));
                case MojaveCombatKind.CrescentBasin:
                    return Mathf.Max(Ellipse(q,a),-(q-new Vector2(a.x*.91f,a.y*.1f)).magnitude+a.y*.42f);
                case MojaveCombatKind.JunctionClearing:
                    return Mathf.Min(Ellipse(q,a*.78f),Mathf.Min(Ellipse(q-new Vector2(a.x*.51f,a.y*.28f),a*.58f),Ellipse(q+new Vector2(a.x*.43f,a.y*.25f),a*.62f)));
                case MojaveCombatKind.CoverCourt:return Court(q,new Vector2(a.x*.99f,a.y*.95f));
                default:return Ellipse(q,a);
            }
        }
        public Vector2 Local(Vector2 p)
        {
            p -= center;
            float a = -rotation * Mathf.Deg2Rad;
            return new Vector2(p.x * Mathf.Cos(a) - p.y * Mathf.Sin(a), p.x * Mathf.Sin(a) + p.y * Mathf.Cos(a));
        }
        public Vector2 World(Vector2 p)
        {
            float a = rotation * Mathf.Deg2Rad;
            return center + new Vector2(p.x * Mathf.Cos(a) - p.y * Mathf.Sin(a), p.x * Mathf.Sin(a) + p.y * Mathf.Cos(a));
        }
    }

    [Serializable]
    public sealed class MojaveTrail
    {
        public int from, to;
        public Vector2[] points;
        public float[] heights;
        public float[] widths;
        public float width;
        public bool usesEdgePorts;
        public Vector2 fromPortDirection,toPortDirection;
    }

    public sealed class MojaveLayout
    {
        public readonly List<MojavePlace> places = new List<MojavePlace>();
        public readonly List<MojaveTrail> trails = new List<MojaveTrail>();
        public readonly int seed;
        public const float Size = 256;
        public readonly float extent;
        public const float Elevation = 48;
        public const int Version = 3;
        public float junctionRounding;
        public const int MinimumMapSize = 256, MaximumMapSize = 768;
        public MojaveLayout(int seed, int patchCount, bool expanded = false, bool compact = false,int combatAreaCount=0,int mapSizeOverride=0)
        {
            this.seed = seed;
            float presetExtent = expanded ? (compact ? 320 : 384) : Size;
            if(mapSizeOverride!=0&&(mapSizeOverride<MinimumMapSize||mapSizeOverride>MaximumMapSize))throw new ArgumentOutOfRangeException(nameof(mapSizeOverride));
            extent = mapSizeOverride==0 ? presetExtent : mapSizeOverride;
            var r = new System.Random(seed);
            float Rand(float a, float b) => Mathf.Lerp(a, b, (float)r.NextDouble());
            var centers = new[] { new Vector2(-65,-72),new Vector2(-25,-32),new Vector2(37,-49),new Vector2(-51,34),new Vector2(25,29),new Vector2(62,81) };
            var names = new[] { "Saguaro Basin", "The Dry Wash", "Sunstone Court", "Joshua Hollow", "Redrock Crossing", "The High Basin" };
            var themes = new[] { 0,1,2,3,2,0 };
            if(expanded) {
                centers=new[] {new Vector2(-128,-123),new Vector2(-57,-112),new Vector2(26,-129),new Vector2(116,-113),
                    new Vector2(-118,-29),new Vector2(-34,-15),new Vector2(54,-43),new Vector2(135,-14),
                    new Vector2(-130,91),new Vector2(-51,120),new Vector2(42,77),new Vector2(119,121)};
                names=new[] {"Saguaro Basin","The Dry Wash","Sunstone Court","Joshua Hollow","Redrock Crossing","The High Basin",
                    "Cactus Bend","Broken Stone Reach","Pale Sand Hollow","Western Shelters","Sunlit Crossing","Far Desert Court"};
                themes=new[] {0,1,2,3,2,0,1,2,3,0,2,1};
            }
            if(expanded&&combatAreaCount==15) {
                centers=new[] {new Vector2(-140,-113),new Vector2(-68,-126),new Vector2(3,-110),new Vector2(75,-123),new Vector2(140,-105),
                    new Vector2(-134,-12),new Vector2(-64,5),new Vector2(8,-17),new Vector2(79,2),new Vector2(143,-8),
                    new Vector2(-139,106),new Vector2(-71,123),new Vector2(4,109),new Vector2(72,126),new Vector2(139,113)};
                names=new[] {"Saguaro Basin","The Dry Wash","Sunstone Court","Joshua Hollow","Redrock Crossing","The High Basin",
                    "Cactus Bend","Broken Stone Reach","Pale Sand Hollow","Western Shelters","Sunlit Crossing","Far Desert Court",
                    "Ridge Shelter","Cactus Wash","Eastern Basin"};
                themes=new[] {0,1,2,3,2,0,1,2,3,0,2,1,0,1,2};
            }
            var shuffled = new List<int>();for(int i=0;i<patchCount;i++)shuffled.Add(i);
            for(int i=shuffled.Count-1;i>0;i--){int j=r.Next(i+1);int t=shuffled[i];shuffled[i]=shuffled[j];shuffled[j]=t;}
            for(int i=0;i<centers.Length;i++)
                places.Add(new MojavePlace { name=names[i], center=centers[i]+new Vector2(Rand(-7,7),Rand(-7,7)),
                    radius=new Vector2(Rand(15,21),Rand(15,22)), height=5+i*.42f+Rand(-.35f,.35f),
                    rotation=Rand(0,360),patch=shuffled[i%shuffled.Count],theme=themes[i] });
            // Separate shape random stream preserves the established source choice and macro layout.
            var shapeRandom=new System.Random(unchecked(seed^0x5EED41));var kinds=new List<MojaveCombatKind>((MojaveCombatKind[])Enum.GetValues(typeof(MojaveCombatKind)));
            for(int i=kinds.Count-1;i>0;i--){int j=shapeRandom.Next(i+1);var kind=kinds[i];kinds[i]=kinds[j];kinds[j]=kind;}
            for(int i=0;i<places.Count;i++)places[i].kind=kinds[i%kinds.Count];
            bool fewer=expanded&&(combatAreaCount==6||combatAreaCount==9);
            if(fewer) {
                var indices=combatAreaCount==6?new[]{0,3,5,6,8,11}:new[]{0,1,3,4,5,6,8,10,11};
                var selected=new List<MojavePlace>();foreach(int index in indices)selected.Add(places[index]);places.Clear();places.AddRange(selected);
            }
            if(expanded&&compact)foreach(var place in places)place.center*=.78f;
            // Resize the layout, preserving authored tile, rock and passage widths.
            if(mapSizeOverride!=0)foreach(var place in places)place.center*=extent/presetExtent;
            if(fewer||(expanded&&combatAreaCount==15)) {
                ConnectByDistance(r,combatAreaCount==15?3:2);
            } else if(expanded) {
                AddTrail(0,1,5.2f);AddTrail(1,2,4.6f);AddTrail(2,3,4.4f);AddTrail(1,5,5.1f);
                AddTrail(5,4,4.7f);AddTrail(4,8,4.3f);AddTrail(5,9,4.5f);AddTrail(5,6,4.8f);
                AddTrail(6,7,4.4f);AddTrail(6,10,5.1f);AddTrail(10,11,4.7f);AddTrail(7,11,4.3f);
                AddTrail(8,9,4.5f);AddTrail(9,10,4.6f);
                if(r.NextDouble()>.5)AddTrail(3,7,3.8f);
            } else {
                AddTrail(0,1,5.2f);AddTrail(1,2,4.6f);AddTrail(1,3,4.4f);AddTrail(3,4,5.1f);AddTrail(2,4,4.7f);AddTrail(4,5,4.3f);
                if(r.NextDouble()>.5) AddTrail(3,5,3.8f);
            }
            void AddTrail(int a,int b,float width)=>CreateTrail(a,b,width,r);
        }

        public void ScatterCombatPlaces(MojaveCatalog catalog)
        {
            var random=new System.Random(unchecked(seed^0x71A95));
            var radii=new float[places.Count];var order=new List<int>();
            for(int i=0;i<places.Count;i++) {
                radii[i]=Mathf.Max(catalog.patches[places[i].patch].size*.707107f,Mathf.Max(places[i].radius.x,places[i].radius.y)*1.28f);
                order.Add(i);
            }
            // Plan large footprints first; commit positions only when every room fits.
            order.Sort((a,b)=>radii[a]==radii[b]?a.CompareTo(b):radii[b].CompareTo(radii[a]));
            var positions=new Vector2[places.Count];bool fitted=false;
            for(int restart=0;restart<32&&!fitted;restart++) {
                fitted=true;
                for(int n=0;n<order.Count;n++) {
                    int index=order[n];float limit=extent*.5f-radii[index]-8;bool found=false;
                    if(limit<=0)throw new InvalidOperationException("Map is too small for the combat tiles.");
                    for(int attempt=0;attempt<2048&&!found;attempt++) {
                        var candidate=new Vector2(Mathf.Lerp(-limit,limit,(float)random.NextDouble()),Mathf.Lerp(-limit,limit,(float)random.NextDouble()));
                        bool clear=true;
                        for(int previous=0;previous<n;previous++) {
                            int other=order[previous];float separation=radii[index]+radii[other]+6;
                            if((candidate-positions[other]).sqrMagnitude<separation*separation){clear=false;break;}
                        }
                        if(clear){positions[index]=candidate;found=true;}
                    }
                    if(!found){fitted=false;break;}
                }
            }
            if(!fitted)throw new InvalidOperationException("Combat tiles cannot fit without overlap. Increase the map size or reduce the area count.");
            for(int i=0;i<places.Count;i++)places[i].center=positions[i];
            trails.Clear();ConnectByDistance(random,places.Count>=15?3:2);
        }

        void ConnectByDistance(System.Random random,int loops)
        {
            var connected=new List<int>{0};var linked=new HashSet<int>();
            while(connected.Count<places.Count) {
                int from=0,to=-1;float shortest=float.PositiveInfinity;
                foreach(int a in connected)for(int b=0;b<places.Count;b++)if(!connected.Contains(b)) {
                    float distance=(places[a].center-places[b].center).sqrMagnitude;
                    if(distance<shortest){shortest=distance;from=a;to=b;}
                }
                CreateTrail(from,to,4.8f,random);linked.Add(Mathf.Min(from,to)*places.Count+Mathf.Max(from,to));connected.Add(to);
            }
            for(int loop=0;loop<loops;loop++) {
                int from=0,to=-1;float shortest=float.PositiveInfinity;
                for(int a=0;a<places.Count;a++)for(int b=a+1;b<places.Count;b++)if(!linked.Contains(a*places.Count+b)) {
                    float distance=(places[a].center-places[b].center).sqrMagnitude;
                    if(distance<shortest){shortest=distance;from=a;to=b;}
                }
                if(to>=0){CreateTrail(from,to,4.3f,random);linked.Add(from*places.Count+to);}
            }
        }

        void CreateTrail(int a,int b,float width,System.Random random)
        {
                float Rand(float low,float high)=>Mathf.Lerp(low,high,(float)random.NextDouble());
                var start=places[a].center;var end=places[b].center;
                var delta=end-start;var normal=new Vector2(-delta.y,delta.x).normalized;
                var bend=normal*Rand(-12,12);
                var c1=start+delta*.33f+bend;var c2=start+delta*.68f+bend;
                var p=new Vector2[25];var h=new float[25];var w=new float[25];
                for(int k=0;k<p.Length;k++) {float t=k/(float)(p.Length-1);float u=1-t;
                    p[k]=u*u*u*start+3*u*u*t*c1+3*u*t*t*c2+t*t*t*end;
                    h[k]=Mathf.Lerp(places[a].height,places[b].height,Mathf.SmoothStep(0,1,t));
                    w[k]=width*.5f+(Noise(p[k],.092f,35)-.5f)*.9f;}
                trails.Add(new MojaveTrail {from=a,to=b,points=p,heights=h,width=width, widths=w});
        }

        public void FitRockTrails(MojaveCatalog catalog,bool roundCorners=false,bool organicConnections=false,bool yieldBlockedShoulders=false)
        {
            var obstacles=new List<Vector3>();
            foreach(var place in places)foreach(var item in catalog.patches[place.patch].placements) {
                if(item.kind!=MojavePropKind.Boulder||item.height<1.2f)continue;
                var p=place.World(new Vector2(item.position.x,item.position.z));float radius=Mathf.Min(item.radius,12);
                if(Mathf.Abs(p.x)>extent*.5f-9||Mathf.Abs(p.y)>extent*.5f-9||RoomDistance(p,out _)>20)continue;
                if(Vector2.Distance(p,place.center)<item.radius+5.5f)continue;
                if(place.kind==MojaveCombatKind.CoverCourt&&PlaceDistance(p,place)<radius+3)continue;
                obstacles.Add(new Vector3(p.x,p.y,radius+2.4f));
            }
            bool Solid(Vector2 p) {foreach(var o in obstacles)if((new Vector2(o.x,o.y)-p).sqrMagnitude<o.z*o.z)return true;return false;}
            int side=Mathf.RoundToInt(extent*.5f);float half=extent*.5f;
            Vector2 Point(int key)=>new Vector2((key%side)*2-half,(key/side)*2-half);
            int Key(Vector2 p)=>Mathf.Clamp(Mathf.RoundToInt((p.y+half)/2),0,side-1)*side+Mathf.Clamp(Mathf.RoundToInt((p.x+half)/2),0,side-1);
            var costs=new float[side*side];var passable=new bool[side*side];
            for(int k=0;k<costs.Length;k++) {
                var p=Point(k);float distance=PlayDistance(p);
                passable[k]=Mathf.Abs(p.x)<half-10&&Mathf.Abs(p.y)<half-10&&distance<17&&!Solid(p);
                costs[k]=1+Mathf.Max(0,distance)*.25f;
            }
            bool Clear(Vector2 a,Vector2 b){int steps=Mathf.CeilToInt(Vector2.Distance(a,b)/.6f);for(int i=0;i<=steps;i++)if(Solid(Vector2.Lerp(a,b,i/(float)Mathf.Max(1,steps))))return false;return true;}
            var portDirections=new Dictionary<int,List<Vector2>>();
            Vector2 Rotate(Vector2 d,float degrees) {float a=degrees*Mathf.Deg2Rad;return new Vector2(d.x*Mathf.Cos(a)-d.y*Mathf.Sin(a),d.x*Mathf.Sin(a)+d.y*Mathf.Cos(a));}
            Vector2 Port(MojavePlace place,Vector2 direction) {
                float distance=0,limit=Mathf.Max(place.radius.x,place.radius.y)*2.5f;
                for(float t=1;t<limit;t+=.75f){if(PlaceDistance(place.center+direction*t,place)>0)break;distance=t;}
                return place.center+direction*Mathf.Max(0,distance-2);
            }
            float PortCrowding(int place,Vector2 direction) {
                float cost=0;if(portDirections.TryGetValue(place,out var used))foreach(var d in used)cost+=Mathf.Max(0,38-Vector2.Angle(d,direction));return cost;
            }
            void RememberPorts(MojaveTrail trail,Vector2 from,Vector2 to) {
                trail.usesEdgePorts=true;trail.fromPortDirection=from;trail.toPortDirection=to;
                if(!portDirections.ContainsKey(trail.from))portDirections[trail.from]=new List<Vector2>();
                if(!portDirections.ContainsKey(trail.to))portDirections[trail.to]=new List<Vector2>();
                portDirections[trail.from].Add(from);portDirections[trail.to].Add(to);
            }
            bool TryPortCurve(MojaveTrail trail,bool allowDisplacement=false) {
                var a=places[trail.from];var b=places[trail.to];var axis=(b.center-a.center).normalized;
                float Preferred(MojavePlace place,int salt) {float v=Noise(place.center,.073f,salt);return (v<.5f?-1:1)*Mathf.Lerp(20,38,Mathf.Abs(v-.5f)*2);}
                float preferredA=Preferred(a,trail.to+71),preferredB=Preferred(b,trail.from+103);
                var angles=new[]{-60f,-45,-30,-15,0,15,30,45,60};
                Vector2[] best=null;Vector2 bestA=default,bestB=default;float bestScore=float.PositiveInfinity;
                foreach(float angleA in angles)foreach(float angleB in angles)foreach(float leadFactor in new[]{.28f,.42f,.56f}) {
                    var da=Rotate(axis,angleA);var db=Rotate(-axis,angleB);var start=Port(a,da);var end=Port(b,db);
                    float lead=Mathf.Clamp(Vector2.Distance(start,end)*leadFactor,6,32);var c1=start+da*lead;var c2=end+db*lead;
                    var points=new Vector2[25];bool clear=true;float length=0,displacementCost=0;
                    for(int k=0;k<points.Length;k++) {
                        float t=k/(float)(points.Length-1),u=1-t;var q=u*u*u*start+3*u*u*t*c1+3*u*t*t*c2+t*t*t*end;points[k]=q;
                        if(Mathf.Abs(q.x)>extent*.5f-8||Mathf.Abs(q.y)>extent*.5f-8||(!allowDisplacement&&k>0&&!Clear(points[k-1],q))){clear=false;break;}
                        if(k>0)length+=Vector2.Distance(points[k-1],q); if(allowDisplacement&&Solid(q))displacementCost+=4;
                    }
                    if(!clear)continue;
                    float score=displacementCost+(Mathf.Abs(angleA-preferredA)+Mathf.Abs(angleB-preferredB))*.08f+length*.025f+Mathf.Abs(leadFactor-.42f)+PortCrowding(trail.from,da)+PortCrowding(trail.to,db);
                    // Keep separate approaches readable instead of merging them into a long parallel lane.
                    foreach(var other in trails)if(other.usesEdgePorts)for(int k=3;k<points.Length-3;k+=3) {
                        if(RoomDistance(points[k],out _)<3)continue;
                        float nearest=float.PositiveInfinity;
                        for(int j=0;j<other.points.Length-1;j++) {
                            var d=other.points[j+1]-other.points[j];float t=Mathf.Clamp01(Vector2.Dot(points[k]-other.points[j],d)/Mathf.Max(.001f,d.sqrMagnitude));
                            nearest=Mathf.Min(nearest,Vector2.Distance(points[k],other.points[j]+d*t));
                        }
                        score+=Mathf.Max(0,(trail.width+other.width)*.5f+3-nearest)*2;
                    }
                    if(score>=bestScore)continue;bestScore=score;best=points;bestA=da;bestB=db;
                }
                if(best==null)return false;
                trail.points=best;trail.heights=new float[best.Length];trail.widths=new float[best.Length];
                float total=0;for(int k=1;k<best.Length;k++)total+=Vector2.Distance(best[k-1],best[k]);float walked=0;
                for(int k=0;k<best.Length;k++) {
                    if(k>0)walked+=Vector2.Distance(best[k-1],best[k]);float t=walked/Mathf.Max(.001f,total);
                    trail.heights[k]=Mathf.Lerp(a.height,b.height,Mathf.SmoothStep(0,1,t));trail.widths[k]=trail.width*.5f+(Noise(best[k],.092f,35)-.5f)*.9f;
                }
                RememberPorts(trail,bestA,bestB);return true;
            }
            foreach(var trail in trails) {
                if(organicConnections&&TryPortCurve(trail))continue;
                // If a large tile seals every approach, the established road-clearing pass yields its shoulder rocks.
                if(organicConnections&&yieldBlockedShoulders&&TryPortCurve(trail,true))continue;
                var startPoint=places[trail.from].center;var endPoint=places[trail.to].center;
                bool detourPorts=false;Vector2 fromPort=default,toPort=default,fromDirection=default,toDirection=default;
                if(organicConnections) {
                    var axis=(endPoint-startPoint).normalized;float score=float.PositiveInfinity;
                    foreach(float angleA in new[]{-60f,-45,-30,-15,15,30,45,60,0})foreach(float angleB in new[]{-60f,-45,-30,-15,15,30,45,60,0}) {
                        var da=Rotate(axis,angleA);var db=Rotate(-axis,angleB);var pa=Port(places[trail.from],da);var pb=Port(places[trail.to],db);
                        var leadA=pa+da*6;var leadB=pb+db*6;
                        if(!passable[Key(leadA)]||!passable[Key(leadB)]||!Clear(pa,leadA)||!Clear(pb,leadB)||!Clear(leadA,Point(Key(leadA)))||!Clear(leadB,Point(Key(leadB))))continue;
                        float value=Vector2.Distance(leadA,leadB)*.08f+Mathf.Abs(Mathf.Abs(angleA)-25)*.06f+Mathf.Abs(Mathf.Abs(angleB)-25)*.06f+PortCrowding(trail.from,da)+PortCrowding(trail.to,db);
                        if(value>=score)continue;score=value;detourPorts=true;fromPort=pa;toPort=pb;fromDirection=da;toDirection=db;startPoint=leadA;endPoint=leadB;
                    }
                }
                if(roundCorners&&!detourPorts) {
                    bool sourceClear=true;for(int i=0;i<trail.points.Length-1;i++)if(!Clear(trail.points[i],trail.points[i+1])){sourceClear=false;break;}
                    if(sourceClear)continue;
                }
                int start=Key(startPoint),end=Key(endPoint);var open=new List<int>{start};var scores=new Dictionary<int,float>{{start,0}};
                var parents=new Dictionary<int,int>();var closed=new HashSet<int>();List<Vector2> path=null;
                var steps=new[]{-1,1,-side,side,-side-1,-side+1,side-1,side+1};
                for(int budget=0;open.Count>0&&budget<(extent>Size?side*side:16000);budget++) {
                    int best=0;float score=float.PositiveInfinity;
                    for(int i=0;i<open.Count;i++){float f=scores[open[i]]+Vector2.Distance(Point(open[i]),endPoint)*.5f;if(f<score){score=f;best=i;}}
                    int current=open[best];open.RemoveAt(best);
                    if(current==end){path=new List<Vector2>{endPoint};while(parents.TryGetValue(current,out int previous)){path.Add(Point(current));current=previous;}path.Add(startPoint);path.Reverse();break;}
                    closed.Add(current);var cp=Point(current);
                    foreach(int step in steps) {int next=current+step;if(next<0||next>=side*side||closed.Contains(next)||!passable[next])continue;
                        var np=Point(next);if(Mathf.Abs(np.x-cp.x)>2.1f)continue;
                        if(!Clear(cp,np))continue;
                        float cost=scores[current]+costs[next]*Vector2.Distance(cp,np);if(scores.TryGetValue(next,out float old)&&old<=cost)continue;
                        scores[next]=cost;parents[next]=current;if(!open.Contains(next))open.Add(next);
                    }
                }
                if(path==null)continue;
                if(detourPorts){path.Insert(0,fromPort);path.Add(toPort);endPoint=toPort;}
                var smooth=new List<Vector2>{path[0]};
                for(int i=0;i<path.Count-1;i++) {
                    var a=Vector2.Lerp(path[i],path[i+1],.25f);var b=Vector2.Lerp(path[i],path[i+1],.75f);
                    if(Clear(smooth[smooth.Count-1],a)&&Clear(a,b)){smooth.Add(a);smooth.Add(b);}else smooth.Add(path[i]);
                }
                smooth.Add(endPoint);
                var reduced=new List<Vector2>{smooth[0]};
                for(int i=1;i<smooth.Count-1;i++)if(Vector2.Distance(reduced[reduced.Count-1],smooth[i])>2.2f || !Clear(reduced[reduced.Count-1],smooth[i+1]))reduced.Add(smooth[i]);
                reduced.Add(endPoint);smooth=reduced;
                if(roundCorners) {
                    // Simplify the grid detour, then fillet its corners only where the whole arc clears the rocks.
                    var guide=new List<Vector2>{smooth[0]};
                    for(int i=0;i<smooth.Count-1;) {
                        int last=i+1;float guideLength=0;
                        for(int j=i+1;j<smooth.Count;j++) {
                            guideLength+=Vector2.Distance(smooth[j-1],smooth[j]);if(guideLength>24)break;
                            if(Clear(smooth[i],smooth[j]))last=j;
                        }
                        guide.Add(smooth[last]);i=last;
                    }
                    var curved=new List<Vector2>{guide[0]};
                    for(int i=1;i<guide.Count-1;i++) {
                        var corner=guide[i];float trim=Mathf.Min(8,Vector2.Distance(guide[i-1],corner)*.42f,Vector2.Distance(corner,guide[i+1])*.42f);
                        bool fitted=false;
                        for(int attempt=0;attempt<4;attempt++,trim*=.6f) {
                            var arcStart=Vector2.MoveTowards(corner,guide[i-1],trim);var arcEnd=Vector2.MoveTowards(corner,guide[i+1],trim);
                            int count=Mathf.Max(4,Mathf.CeilToInt(trim*2/.7f));var arc=new List<Vector2>{arcStart};bool clear=Clear(curved[curved.Count-1],arcStart);
                            for(int k=1;k<=count;k++) {
                                float t=k/(float)count,u=1-t;var p=u*u*arcStart+2*u*t*corner+t*t*arcEnd;
                                clear&=Clear(arc[arc.Count-1],p);arc.Add(p);
                            }
                            if(!clear)continue;curved.AddRange(arc);fitted=true;break;
                        }
                        if(!fitted)curved.Add(corner);
                    }
                    curved.Add(guide[guide.Count-1]);smooth=curved;
                }
                var h=new float[smooth.Count];var widths=new float[smooth.Count];
                float length=0;for(int i=1;i<smooth.Count;i++)length+=Vector2.Distance(smooth[i-1],smooth[i]);float walked=0;
                for(int i=0;i<smooth.Count;i++) {if(i>0)walked+=Vector2.Distance(smooth[i-1],smooth[i]);float t=walked/Mathf.Max(.001f,length);
                    h[i]=Mathf.Lerp(places[trail.from].height,places[trail.to].height,Mathf.SmoothStep(0,1,t));
                    widths[i]=trail.width*.5f+(Noise(smooth[i],.092f,35)-.5f)*.9f;}
                trail.points=smooth.ToArray();trail.heights=h;trail.widths=widths;
                if(detourPorts)RememberPorts(trail,fromDirection,toDirection);
            }
        }

        public float Noise(Vector2 p,float frequency,float offset=0)
        {
            float s=(seed & 65535)*.1731f;
            return Mathf.PerlinNoise(p.x*frequency+s+offset,p.y*frequency+s*.673f+offset);
        }

        public float RoomDistance(Vector2 p,out MojavePlace nearest)
        {
            float best=float.PositiveInfinity;nearest=places[0];
            foreach(var place in places) {
                float d=PlaceDistance(p,place);
                if(d<best){best=d;nearest=place;}
            }
            return best;
        }

        public float PlaceDistance(Vector2 p,MojavePlace place) => place.Distance(p)
            +(Noise(p,.061f,17)-.5f)*3+(Noise(p,.13f,46)-.5f)*1.3f;

        public float TrailDistance(Vector2 p,out float elevation,out float halfWidth)
        {
            float best=float.PositiveInfinity;elevation=5;halfWidth=2.5f;
            float combined=float.PositiveInfinity;
            float weightedHeight=0,totalWeight=0;
            foreach(var trail in trails) {
                float nearest=float.PositiveInfinity,height=5,trailWidth=2.5f;
                for(int i=0;i<trail.points.Length-1;i++) {
                    var a=trail.points[i];var d=trail.points[i+1]-a;
                    float t=Mathf.Clamp01(Vector2.Dot(p-a,d)/Mathf.Max(.0001f,d.sqrMagnitude));
                    float distance=Vector2.Distance(p,a+d*t);
                    float width=Mathf.Lerp(trail.widths[i],trail.widths[i+1],t);
                    if(distance-width<nearest){nearest=distance-width;height=Mathf.Lerp(trail.heights[i],trail.heights[i+1],t);trailWidth=width;}
                }
                if(nearest<best){best=nearest;elevation=height;halfWidth=trailWidth;}
                combined=CombineDistance(combined,nearest);
                // Blend nearby road stamps at junctions instead of switching abruptly to the closest road.
                float weight=1-Mathf.SmoothStep(0,1,Mathf.Clamp01(nearest/18));
                weightedHeight+=height*weight;totalWeight+=weight;
            }
            if(totalWeight>.0001f)elevation=weightedHeight/totalWeight;
            return combined;
        }

        public float CombineDistance(float a,float b)
        {
            if(junctionRounding<=0||float.IsPositiveInfinity(a)||float.IsPositiveInfinity(b))return Mathf.Min(a,b);
            float h=Mathf.Max(junctionRounding-Mathf.Abs(a-b),0)/junctionRounding;
            return Mathf.Min(a,b)-h*h*junctionRounding*.25f;
        }

        public float PlayDistance(Vector2 p)
        {
            float room=RoomDistance(p,out _);float trail=TrailDistance(p,out _,out _);
            return CombineDistance(room,trail);
        }
        public bool CanWalk(Vector2 p,float clearance=.5f) => Mathf.Abs(p.x)<extent*.5f-4 && Mathf.Abs(p.y)<extent*.5f-4 && PlayDistance(p)<-clearance;
    }
}
