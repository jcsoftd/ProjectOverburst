using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class UpcomingMonsterV3ShowcaseVerifier
{
    static Queue<UpcomingMonsterReviewStation> pending;
    static JObject v3;
    static JArray results,failures;
    static string output;
    static int checks;
    static double deadline;
    static bool originalDirty;
    static void Check(bool pass,string message){checks++;if(!pass)failures.Add(message);}
    public static string Queue(string folder)
    {
        if(pending!=null)throw new InvalidOperationException("V3 validation is already running.");
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)throw new InvalidOperationException("Idle Editor required.");
        UpcomingMonsterReviewWindow.StopAll();output=folder;v3=JObject.Parse(File.ReadAllText(Path.Combine(folder,"v3-source.json")));
        var scene=SceneManager.GetSceneByPath(UpcomingMonsterThemeReviewBuilder.ScenePath);
        if(!scene.IsValid() || scene.isDirty)throw new InvalidOperationException("Saved showcase scene required.");
        originalDirty=scene.isDirty;checks=0;failures=new JArray();results=new JArray();
        var stations=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<UpcomingMonsterReviewStation>(true)).ToArray();
        Check(stations.Length==70,"70 v3 stations");
        foreach(JObject theme in v3["themes"])
        {
            var root=scene.GetRootGameObjects().Single(r=>r.name.StartsWith(((int)theme["number"]).ToString("00")+"_",StringComparison.Ordinal));
            var actual=root.GetComponentsInChildren<UpcomingMonsterReviewStation>(true);
            Check(actual.Length==theme["entries"].Count(),"theme slot count: "+root.name);
            foreach(JObject entry in theme["entries"])
            {
                var s=actual.SingleOrDefault(a=>a.cardKey==(string)entry["key"]);
                Check(s!=null,"theme membership: "+entry["key"]);
                if(s!=null)Check(s.grade==(string)entry["grade"] && s.selectionStatus==(string)entry["status"],"grade/status: "+s.cardKey);
            }
            // Physical silhouettes must not overlap in the saved lineup.
            for(int i=0;i<actual.Length;i++)for(int j=i+1;j<actual.Length;j++)
            {
                var a=new Bounds(actual[i].transform.position+Vector3.up*actual[i].displaySize.y*.5f,actual[i].displaySize);
                var b=new Bounds(actual[j].transform.position+Vector3.up*actual[j].displaySize.y*.5f,actual[j].displaySize);
                Check(!a.Intersects(b),"display overlap: "+actual[i].cardKey+" / "+actual[j].cardKey);
            }
        }
        foreach(var station in stations)
        {
            var card=(JObject)v3["cards"][station.cardKey];
            Check(card!=null,"v3 key: "+station.cardKey);
            foreach(string role in new[]{"idle","move","weak","strong"})
            {
                var motions=station.motions.Where(m=>m.role==role).ToArray();var expected=card[role].OfType<JObject>().ToArray();
                Check(motions.Length==expected.Length,"motion count: "+station.cardKey+" / "+role);
                for(int i=0;i<Math.Min(motions.Length,expected.Length);i++)
                {
                    Check(motions[i].clip==UpcomingMonsterThemeReviewBuilder.ResolveClip(expected[i]),"exact clip: "+station.cardKey+" / "+role+" / "+i);
                    Check(motions[i].parryable==((bool?)expected[i]["parryable"]??false),"parry flag: "+station.cardKey+" / "+role+" / "+i);
                }
            }
            Check(station.motions.Count(m=>m.role=="strong")<=1,"single strong: "+station.cardKey);
            foreach(var a in station.model.GetComponentsInChildren<Animator>(true))Check(!a.enabled,"saved Animator disabled: "+station.cardKey);
            foreach(var c in station.model.GetComponentsInChildren<Collider>(true))Check(!c.enabled,"saved collider disabled: "+station.cardKey);
            foreach(var b in station.model.GetComponentsInChildren<MonoBehaviour>(true))Check(b==null || !b.enabled || b is UniversalAdditionalLightData,"saved gameplay behaviour disabled: "+station.cardKey);
        }
        pending=new Queue<UpcomingMonsterReviewStation>(stations.GroupBy(s=>s.cardKey).Select(g=>g.First()));
        deadline=EditorApplication.timeSinceStartup+300;Write("RUNNING");EditorApplication.update+=Tick;
        AssemblyReloadEvents.beforeAssemblyReload+=ReloadCancel;EditorApplication.playModeStateChanged+=PlayCancel;
        return "QUEUED: saved bindings and all selected clips on isolated preview models";
    }
    static void ReloadCancel(){if(pending!=null)Cancel("Interrupted by compilation/reload; rerun in idle Editor.");}
    static void PlayCancel(PlayModeStateChange s){if(s==PlayModeStateChange.ExitingEditMode && pending!=null)Cancel("Interrupted by another Play; rerun in idle Editor.");}
    static void Unhook(){EditorApplication.update-=Tick;AssemblyReloadEvents.beforeAssemblyReload-=ReloadCancel;EditorApplication.playModeStateChanged-=PlayCancel;}
    static void Write(string status)=>File.WriteAllText(Path.Combine(output,"v3-motion-validation.json"),new JObject{{"status",status},{"checks",checks},{"remaining",pending?.Count??0},{"failures",failures.DeepClone()},{"results",results.DeepClone()},{"play","NOT_RUN: EditMode preview"}}.ToString());
    static void Tick()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating){if(EditorApplication.timeSinceStartup<deadline)return;Cancel("Editor busy timeout");return;}
        try
        {
            if(pending.Count==0)
            {
                var scene=SceneManager.GetSceneByPath(UpcomingMonsterThemeReviewBuilder.ScenePath);Check(scene.isDirty==originalDirty,"preview preserves saved scene dirty state");
                Write(failures.Count==0?"PASS":"FAIL");Unhook();pending=null;v3=null;return;
            }
            var source=pending.Dequeue();
            if(source.motions.Length==0){results.Add(new JObject{{"key",source.cardKey},{"clips",0},{"note","no dedicated clips"}});Write("RUNNING");return;}
            using(var preview=new UpcomingMonsterReviewPreview(source))
            {
                var motions=new JArray();
                foreach(var m in source.motions)
                {
                    preview.Select(m.clip);preview.Sample(m.clip.length*.13f);
                    var bones=preview.Model.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(s=>s.bones).Where(b=>b!=null).Distinct().ToArray();
                    var p=bones.Select(b=>b.localPosition).ToArray();var q=bones.Select(b=>b.localRotation).ToArray();
                    var anchor=preview.Model.transform.position;preview.Sample(m.clip.length*.63f);
                    float delta=bones.Select((b,i)=>Quaternion.Angle(q[i],b.localRotation)+Vector3.Distance(p[i],b.localPosition)*100).DefaultIfEmpty(0).Max();
                    Check(delta>.0001f,"animated weighted rig: "+source.cardKey+" / "+m.role+" / "+m.clip.name);
                    Check(Vector3.Distance(anchor,preview.Model.transform.position)<.00001f,"root stays anchored: "+source.cardKey+" / "+m.clip.name);
                    Check(!m.clip.isHumanMotion || preview.Model.GetComponentInChildren<Animator>(true).isHuman,"Humanoid avatar: "+source.cardKey+" / "+m.clip.name);
                    preview.Sample(0);Check(preview.Time==0,"scrub reset: "+source.cardKey+" / "+m.clip.name);
                    motions.Add(new JObject{{"role",m.role},{"clip",m.clip.name},{"boneDelta",delta},{"human",m.clip.isHumanMotion}});
                }
                if(source.cardKey=="runtime:DeathHarvest_RakeBrute" || source.cardKey=="planned:death-knight-2:elite" || source.cardKey=="planned:cavecrawler:medium")
                {
                    var chosen=source.motions.FirstOrDefault(m=>m.role=="weak")??source.motions[0];preview.Select(chosen.clip);preview.Sample(chosen.clip.length*.4f);
                    SaveTexture(preview.Render(960,640),Path.Combine(output,"preview_"+source.cardKey.Replace(':','_')+".png"));
                }
                results.Add(new JObject{{"key",source.cardKey},{"clips",motions}});
            }
            Write("RUNNING");
        }
        catch(Exception e){Cancel(e.ToString());}
    }
    static void Cancel(string reason){Check(false,reason);Write("FAIL");Unhook();pending=null;v3=null;}
    static void SaveTexture(RenderTexture rt,string path)
    {
        var active=RenderTexture.active;Texture2D t=null;
        try{RenderTexture.active=rt;t=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);t.Apply();var pixels=t.GetPixels32();int spread=pixels.Max(p=>Math.Max(p.r,Math.Max(p.g,p.b)))-pixels.Min(p=>Math.Min(p.r,Math.Min(p.g,p.b)));if(spread<10)throw new InvalidOperationException("Blank preview render: "+path);File.WriteAllBytes(path,t.EncodeToPNG());}
        finally{RenderTexture.active=active;if(t!=null)Object.DestroyImmediate(t);}
    }
    public static string Capture(string folder,int section)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)throw new InvalidOperationException("Idle Editor required.");
        var scene=SceneManager.GetSceneByPath(UpcomingMonsterThemeReviewBuilder.ScenePath);
        if(scene.isDirty)throw new InvalidOperationException("Capture refuses unsaved manual changes.");
        var roots=scene.GetRootGameObjects();var states=roots.Select(r=>r.activeSelf).ToArray();
        var camera=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Camera>(true)).Single(c=>c.name=="ReviewCamera_"+section);
        var rt=RenderTexture.GetTemporary(1920,1080,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        try
        {
            foreach(var root in roots)if(root.name.Length>=3 && root.name[2]=='_' && root.name.Substring(0,2)!="00")root.SetActive(root.name.StartsWith(section.ToString("00")+"_",StringComparison.Ordinal));
            var request=new UniversalRenderPipeline.SingleCameraRequest{destination=rt};
            if(RenderPipeline.SupportsRenderRequest(camera,request))RenderPipeline.SubmitRenderRequest(camera,request);
            else{camera.targetTexture=rt;try{camera.Render();}finally{camera.targetTexture=null;}}
            string path=Path.Combine(folder,section.ToString("00")+"_review.png");SaveTexture(rt,path);return path;
        }
        finally{RenderTexture.ReleaseTemporary(rt);for(int i=0;i<roots.Length;i++)roots[i].SetActive(states[i]);if(scene.isDirty)EditorSceneManager.SaveScene(scene);}
    }
}
