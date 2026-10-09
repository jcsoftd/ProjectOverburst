using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Overburst.Mojave
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class MojaveExplorer : MonoBehaviour
    {
        public MojaveWorld world;
        public MojaveCamera view;
        public MojavePortal portal;
        public float speed=5.6f;
        public bool entered;
        public bool showGuide=true;
        public bool showMap;
        public string currentPlace;
        [System.NonSerialized] public bool reviewInputSuppressed;
        CharacterController body;
        Queue<Vector2> route=new Queue<Vector2>();
        float verticalVelocity;
        bool regenerating;
        GUIStyle titleStyle,smallStyle,promptStyle;
        Texture2D mapTexture;
        int mapSeed=int.MinValue;
        string notice;
        float noticeUntil;

        void Start() { body=GetComponent<CharacterController>();world.EnsureLayout();Teleport(world.Staging);portal.Place(world); }
        void Update()
        {
            if(regenerating)return;
            var keyboard=reviewInputSuppressed?null:Keyboard.current;var mouse=reviewInputSuppressed?null:Mouse.current;
            if(keyboard!=null) {
                if(keyboard.eKey.wasPressedThisFrame && Vector3.Distance(transform.position,portal.transform.position)<4.2f)EnterPortal();
                if(keyboard.escapeKey.wasPressedThisFrame){route.Clear();entered=false;Teleport(world.Staging);Notice("Returned to the waygate");}
                if(keyboard.rKey.wasPressedThisFrame)StartCoroutine(Regenerate());
                if(keyboard.tabKey.wasPressedThisFrame)showMap=!showMap;
                if(keyboard.hKey.wasPressedThisFrame)showGuide=!showGuide;
            }
            var input=Vector2.zero;
            if(keyboard!=null)input=new Vector2((keyboard.dKey.isPressed?1:0)-(keyboard.aKey.isPressed?1:0),(keyboard.wKey.isPressed?1:0)-(keyboard.sKey.isPressed?1:0));
            if(input.sqrMagnitude>.01f) {
                route.Clear();var forward=view.transform.forward;forward.y=0;forward.Normalize();var right=view.transform.right;right.y=0;right.Normalize();
                StepDirection((right*input.x+forward*input.y).normalized,Time.deltaTime*(keyboard.leftShiftKey.isPressed?1.45f:1));
            } else {
                if(mouse!=null && mouse.leftButton.wasPressedThisFrame) {
                    var ray=view.GetComponent<Camera>().ScreenPointToRay(mouse.position.ReadValue());
                    if(Physics.Raycast(ray,out var hit,500) && hit.collider is TerrainCollider)MoveTo(new Vector2(hit.point.x,hit.point.z));
                }
                if(route.Count>0) {
                    var goal=route.Peek();var delta=goal-new Vector2(transform.position.x,transform.position.z);
                    if(delta.magnitude<.24f)route.Dequeue();else StepDirection(new Vector3(delta.x,0,delta.y).normalized,Mathf.Min(Time.deltaTime,delta.magnitude/Mathf.Max(.01f,speed)));
                } else StepDirection(Vector3.zero,Time.deltaTime);
            }
            world.layout.RoomDistance(new Vector2(transform.position.x,transform.position.z),out var place);
            currentPlace=place.name;
        }

        public bool StepDirection(Vector3 direction,float deltaTime)
        {
            if(body==null)body=GetComponent<CharacterController>();
            // Consume the elapsed frame in short collision steps instead of discarding slow-frame time.
            bool allowed=true;float remaining=Mathf.Clamp(deltaTime,0,.5f);
            while(remaining>.00001f) {float step=Mathf.Min(remaining,.05f);remaining-=step;allowed=StepSlice(direction,step)&&allowed;}
            return allowed;
        }
        bool StepSlice(Vector3 direction,float dt)
        {
            var delta=direction*speed*dt;
            Vector2 p=new Vector2(transform.position.x+delta.x,transform.position.z+delta.z);
            bool allowed=direction.sqrMagnitude<.001f || world.IsWalkable(p,.38f);
            if(!allowed) {
                var a=new Vector2(p.x,transform.position.z);var b=new Vector2(transform.position.x,p.y);
                if(world.IsWalkable(a,.38f))delta.z=0;else if(world.IsWalkable(b,.38f))delta.x=0;else delta=Vector3.zero;
            }
            verticalVelocity=body.isGrounded?-2:Mathf.Max(-20,verticalVelocity-22*dt);
            body.Move(delta+Vector3.up*verticalVelocity*dt);
            if(direction.sqrMagnitude>.01f&&allowed) {
                var visual=transform.Find("Temporary explorer");if(visual!=null)visual.rotation=Quaternion.Slerp(visual.rotation,Quaternion.LookRotation(direction),1-Mathf.Exp(-12*dt));
            }
            return allowed;
        }
        public void Teleport(Vector3 point)
        {
            if(body==null)body=GetComponent<CharacterController>();body.enabled=false;transform.position=point+Vector3.up*.06f;body.enabled=true;
            verticalVelocity=0;route.Clear();Physics.SyncTransforms();if(view!=null)view.Snap();
        }
        public void EnterPortal()
        {
            world.EnsureLayout();Teleport(world.Ground(world.layout.places[1].center));entered=true;Notice(world.layout.places[1].name);
        }
        IEnumerator Regenerate()
        {
            regenerating=true;Notice("Shaping a new desert…");yield return null;yield return null;
            try {int next=unchecked(world.seed*1664525+1013904223)&int.MaxValue;world.Generate(next);portal.Place(world);
                entered=false;Teleport(world.Staging);Notice("A new desert awaits at the waygate");}
            finally {regenerating=false;}
        }
        void Notice(string text) {notice=text;noticeUntil=Time.unscaledTime+4;}

        public bool MoveTo(Vector2 destination)
        {
            if(!world.IsWalkable(destination,.45f))return false;
            var path=FindPath(new Vector2(transform.position.x,transform.position.z),destination);
            route.Clear();foreach(var p in path)route.Enqueue(p);return route.Count>0;
        }
        public List<Vector2> FindPath(Vector2 from,Vector2 to)
        {
            // Sparse A* along a metre grid, then sight-line simplification against the same play field.
            world.EnsureLayout();int size=Mathf.RoundToInt(world.MapSize);float half=world.MapSize*.5f;
            int Key(Vector2 p)=>Mathf.Clamp(Mathf.RoundToInt(p.y+half),0,size-1)*size+Mathf.Clamp(Mathf.RoundToInt(p.x+half),0,size-1);
            Vector2 Point(int k)=>new Vector2(k%size-half,k/size-half);
            int start=Key(from),end=Key(to);var open=new List<int>{start};var g=new Dictionary<int,float>{{start,0}};var parent=new Dictionary<int,int>();var closed=new HashSet<int>();
            var steps=new[]{-1,1,-size,size,-size-1,-size+1,size-1,size+1};
            for(int budget=0;open.Count>0&&budget<(size>256?size*size:18000);budget++) {
                int best=0;float score=float.PositiveInfinity;
                for(int i=0;i<open.Count;i++){float f=g[open[i]]+Vector2.Distance(Point(open[i]),to);if(f<score){score=f;best=i;}}
                int current=open[best];open.RemoveAt(best);if(current==end){
                    var path=new List<Vector2>{to};while(parent.TryGetValue(current,out int previous)){path.Add(Point(current));current=previous;}path.Reverse();
                    var simplified=new List<Vector2>();Vector2 anchor=from;
                    for(int i=0;i<path.Count;i++){int j=i;while(j+1<path.Count&&ClearLine(anchor,path[j+1]))j++;simplified.Add(path[j]);anchor=path[j];i=j;}
                    return simplified;
                }
                closed.Add(current);var cp=Point(current);
                foreach(int step in steps){int next=current+step;if(next<0||next>=size*size||closed.Contains(next))continue;
                    var np=Point(next);if(Mathf.Abs(np.x-cp.x)>1.1f||!world.IsWalkable(np,.55f))continue;
                    if(Mathf.Abs(np.x-cp.x)>.5f&&Mathf.Abs(np.y-cp.y)>.5f&&(!world.IsWalkable(new Vector2(np.x,cp.y),.55f)||!world.IsWalkable(new Vector2(cp.x,np.y),.55f)))continue;
                    float cost=g[current]+Vector2.Distance(cp,np);if(g.TryGetValue(next,out float old)&&old<=cost)continue;
                    g[next]=cost;parent[next]=current;if(!open.Contains(next))open.Add(next);
                }
            }
            return new List<Vector2>();
        }
        bool ClearLine(Vector2 a,Vector2 b)
        {
            int steps=Mathf.CeilToInt(Vector2.Distance(a,b)/.5f);
            for(int i=1;i<=steps;i++)if(!world.IsWalkable(Vector2.Lerp(a,b,i/(float)steps),.55f))return false;return true;
        }

        void OnGUI()
        {
            if(titleStyle==null){titleStyle=new GUIStyle(GUI.skin.label){fontSize=21,fontStyle=FontStyle.Bold};titleStyle.normal.textColor=new Color(.93f,.86f,.72f);
                smallStyle=new GUIStyle(GUI.skin.label){fontSize=13};smallStyle.normal.textColor=new Color(.84f,.82f,.75f);
                promptStyle=new GUIStyle(titleStyle){alignment=TextAnchor.MiddleCenter,fontSize=18};}
            GUI.color=Color.white;
            if(showGuide) {
                GUI.Box(new Rect(24,22,280,78),GUIContent.none);GUI.Label(new Rect(39,30,250,29),currentPlace??world.catalog.DisplayName,titleStyle);
                GUI.Label(new Rect(39,61,250,25),world.catalog.DisplayName+"  /  "+world.seed.ToString("D"),smallStyle);
                GUI.Box(new Rect(24,Screen.height-66,715,42),GUIContent.none);
                GUI.Label(new Rect(38,Screen.height-57,700,27),"WASD / click  Move    Shift  Run    Wheel  Zoom    MMB  Orbit    R  New seed    Tab  Map    H  Hide",smallStyle);
            }
            if(portal!=null&&!regenerating&&Vector3.Distance(transform.position,portal.transform.position)<4.2f)
                GUI.Label(new Rect(Screen.width*.5f-200,Screen.height-130,400,44),"E  ·  Enter "+world.layout.places[1].name,promptStyle);
            if(Time.unscaledTime<noticeUntil)GUI.Label(new Rect(Screen.width*.5f-230,110,460,42),notice,promptStyle);
            if(showMap)DrawMap();
        }
        void DrawMap()
        {
            if(mapTexture==null||mapSeed!=world.seed){
                if(mapTexture!=null)Destroy(mapTexture);mapTexture=new Texture2D(256,256,TextureFormat.RGBA32,false);var pixels=new Color[256*256];
                for(int y=0;y<256;y++)for(int x=0;x<256;x++) {
                    var p=new Vector2(x/256f*world.MapSize-world.MapSize*.5f,y/256f*world.MapSize-world.MapSize*.5f);float room=world.layout.RoomDistance(p,out var place);
                    pixels[y*256+x]=room<0?MojaveCombatPalette.For(place.kind):world.layout.TrailDistance(p,out _,out _)<0?MojaveCombatPalette.Trail:new Color(.06f,.075f,.09f,.96f);
                }
                mapTexture.SetPixels(pixels);mapTexture.Apply();mapSeed=world.seed;
            }
            var rect=new Rect(Screen.width-326,25,300,300);GUI.DrawTexture(rect,mapTexture);
            for(int i=0;i<world.layout.places.Count;i++) {var place=world.layout.places[i];var p=new Vector2(rect.x+(place.center.x+world.MapSize*.5f)/world.MapSize*rect.width,rect.y+rect.height-(place.center.y+world.MapSize*.5f)/world.MapSize*rect.height);GUI.Label(new Rect(p.x-8,p.y-9,18,18),(i+1).ToString(),promptStyle);}
            var actor=transform.position;float px=rect.x+(actor.x+world.MapSize*.5f)/world.MapSize*rect.width;float py=rect.y+rect.height-(actor.z+world.MapSize*.5f)/world.MapSize*rect.height;
            GUI.color=new Color(.35f,.85f,1);GUI.DrawTexture(new Rect(px-3,py-3,6,6),Texture2D.whiteTexture);GUI.color=Color.white;
        }
        void OnDestroy() {if(mapTexture!=null)Destroy(mapTexture);}
    }
}
