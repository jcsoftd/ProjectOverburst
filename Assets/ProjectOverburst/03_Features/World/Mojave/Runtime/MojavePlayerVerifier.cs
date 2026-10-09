using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Overburst.Mojave
{
    // Opt-in Player verification; normal launches use only the explorer controls.
    public sealed class MojavePlayerVerifier : MonoBehaviour
    {
        [Serializable] public sealed class Check { public string name; public bool pass; public string detail; }
        [Serializable] public sealed class Traversal {public int destination,frames;public float seconds,simulatedSeconds,maxDeltaTime;public Vector3 position;}
        [Serializable] public sealed class Result { public string status,phase; public bool inputSuppressed; public int ignoredNavigationFrames; public List<Check> checks=new List<Check>();public List<Traversal> traversals=new List<Traversal>(); public List<string> errors=new List<string>(); }
        readonly Result result=new Result();
        string output;
        MojaveExplorer actor;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        static void PrepareBackgroundReview()
        {
            var args=Environment.GetCommandLineArgs();int marker=Array.IndexOf(args,"--mojave-verify");
            if(!Application.isEditor&&marker>=0)Application.runInBackground=true;
        }
        void Start()
        {
            var args=Environment.GetCommandLineArgs();
            int marker=Array.IndexOf(args,"--mojave-verify");
            if(Application.isEditor||marker<0||marker+1>=args.Length){enabled=false;return;}
            output=args[marker+1];Directory.CreateDirectory(output);Application.runInBackground=true;
            actor=GetComponent<MojaveExplorer>();if(actor!=null){actor.reviewInputSuppressed=true;result.inputSuppressed=true;}
            File.WriteAllText(Path.Combine(output,"player-verification.json"),"{\"status\":\"RUNNING\",\"phase\":\"Player scene started\"}");
            Application.logMessageReceived+=Log;
            StartCoroutine(Verify());
        }
        void Log(string message,string stack,LogType kind)
        {
            if(kind==LogType.Exception||kind==LogType.Error||kind==LogType.Assert)result.errors.Add(message);
        }
        IEnumerator Verify()
        {
            yield return new WaitForSeconds(1);
            actor=GetComponent<MojaveExplorer>();
            if(actor==null||actor.world==null){Add("world boot",false,"Explorer or world missing");Finish();yield break;}
            var world=actor.world;world.EnsureLayout();
            bool grounded=false;
            foreach(var hit in Physics.RaycastAll(actor.transform.position+Vector3.up*2,Vector3.down,5))if(hit.collider is TerrainCollider)grounded=true;
            grounded=grounded&&Mathf.Abs(actor.transform.position.y-world.Ground(new Vector2(actor.transform.position.x,actor.transform.position.z)).y)<.2f;
            Add("terrain grounding",grounded,actor.transform.position.ToString());
            Add("portal in interaction range",Vector3.Distance(actor.transform.position,actor.portal.transform.position)<4.2f,actor.portal.transform.position.ToString());
            yield return Capture("Player_Entry.png");
            actor.EnterPortal();Add("portal entry",actor.entered&&Vector3.Distance(actor.transform.position,world.Ground(world.layout.places[1].center))<.3f,"The Dry Wash");
            foreach(int destination in new[]{2,4})
            {
                var point=world.layout.places[destination].center;
                if(!actor.MoveTo(point)){Add("walk to "+destination,false,"No route");Finish();yield break;}
                var traversal=new Traversal{destination=destination};result.traversals.Add(traversal);
                float started=Time.realtimeSinceStartup,nextReport=started;
                float deadline=Time.realtimeSinceStartup+120;
                while(Vector2.Distance(new Vector2(actor.transform.position.x,actor.transform.position.z),point)>.8f&&Time.realtimeSinceStartup<deadline) {
                    traversal.frames++;traversal.seconds=Time.realtimeSinceStartup-started;traversal.simulatedSeconds+=Time.deltaTime;
                    traversal.maxDeltaTime=Mathf.Max(traversal.maxDeltaTime,Time.deltaTime);traversal.position=actor.transform.position;
                    if(Time.realtimeSinceStartup>=nextReport){WriteProgress("Walking to "+world.layout.places[destination].name);nextReport=Time.realtimeSinceStartup+5;}
                    var key=Keyboard.current;var mouse=Mouse.current;
                    if((key!=null&&(key.wKey.isPressed||key.aKey.isPressed||key.sKey.isPressed||key.dKey.isPressed))
                        ||(mouse!=null&&(mouse.leftButton.wasPressedThisFrame||mouse.scroll.ReadValue().sqrMagnitude>0)))result.ignoredNavigationFrames++;
                    yield return null;
                }
                traversal.seconds=Time.realtimeSinceStartup-started;traversal.position=actor.transform.position;
                bool arrived=Vector2.Distance(new Vector2(actor.transform.position.x,actor.transform.position.z),point)<.8f;
                Add("walk to "+world.layout.places[destination].name,arrived,actor.transform.position.ToString());
                yield return Capture("Player_Place_"+destination+".png");
                if(!arrived){Finish();yield break;}
            }
            world.Generate(902117);actor.portal.Place(world);actor.Teleport(world.Staging);yield return null;
            Add("runtime seed regeneration",world.seed==902117,world.generationMilliseconds+" ms");
            actor.EnterPortal();yield return Capture("Player_NewSeed.png");
            Finish();
        }
        IEnumerator Capture(string name)
        {
            // Capture the real Player camera even when the automation window is hidden.
            yield return null;
            var camera=actor.view.GetComponent<Camera>();var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;float oldAspect=camera.aspect;
            RenderTexture rt=null;Texture2D texture=null;
            try
            {
                rt=new RenderTexture(1600,900,24,RenderTextureFormat.ARGBHalf);rt.Create();camera.targetTexture=rt;camera.aspect=1600f/900;camera.Render();
                RenderTexture.active=rt;texture=new Texture2D(1600,900,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1600,900),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(output,name),texture.EncodeToPNG());
            }
            finally{camera.targetTexture=oldTarget;camera.aspect=oldAspect;RenderTexture.active=oldActive;if(rt!=null){rt.Release();Destroy(rt);}if(texture!=null)Destroy(texture);}
            yield return null;
        }
        void Add(string name,bool pass,string detail){result.checks.Add(new Check{name=name,pass=pass,detail=detail});}
        void WriteProgress(string phase){result.status="RUNNING";result.phase=phase;File.WriteAllText(Path.Combine(output,"player-verification.json"),JsonUtility.ToJson(result,true));}
        void Finish()
        {
            bool pass=result.errors.Count==0&&result.checks.Count>0&&result.checks.TrueForAll(c=>c.pass);
            result.status=pass?"PASS":"FAIL";
            result.phase="Finished";
            File.WriteAllText(Path.Combine(output,"player-verification.json"),JsonUtility.ToJson(result,true));
            Application.logMessageReceived-=Log;Application.Quit(pass?0:1);
        }
        void OnDestroy(){Application.logMessageReceived-=Log;if(actor!=null)actor.reviewInputSuppressed=false;}
    }
}
