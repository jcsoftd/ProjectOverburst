using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Isolated experiment. Never selected by production aura controllers.
public sealed class SharedAuraSimulationProbe : ScriptableObject
{
    static SharedAuraSimulationProbe active;
    public static string Progress="NOT_RUN";
    GameObject root;Camera camera;RenderTexture rt;Material material;
    readonly List<ParticleSystem> systems=new List<ParticleSystem>();
    readonly List<Mesh> meshes=new List<Mesh>();
    readonly List<string> rows=new List<string>();
    readonly Matrix4x4[][] matrices=new Matrix4x4[8][];
    IEnumerator routine;int frame=-1;bool background;double deadline;
    ProfilerRecorder update,geometry,draws;
    static string Folder=>Path.GetFullPath("../개인파일/코덱스산출/Combat/ElementStatusGoal20260927/SharedAuraExperiment");
    public static void Begin()
    {
        if(!Application.isPlaying||active!=null)throw new Exception("idle Play required");
        active=CreateInstance<SharedAuraSimulationProbe>();active.background=Application.runInBackground;Application.runInBackground=true;
        active.deadline=EditorApplication.timeSinceStartup+120;active.routine=active.Run();EditorApplication.update+=Tick;Progress="RUNNING";
    }
    static void Tick()
    {
        if(active==null)return;EditorApplication.QueuePlayerLoopUpdate();if(active.frame==Time.frameCount)return;active.frame=Time.frameCount;
        try{if(!Application.isPlaying||EditorApplication.timeSinceStartup>active.deadline)throw new Exception("interrupted/timeout");if(!active.routine.MoveNext())DestroyImmediate(active);}
        catch(Exception e){Progress="FAIL "+e;active.rows.Add(Progress);File.WriteAllLines(Path.Combine(Folder,"Result.txt"),active.rows);DestroyImmediate(active);}
    }
    static Vector3 Point(int i)=>new Vector3(4991+(i%10)*2,1,4993+(i/10)*1.5f);
    void Capture(string name)
    {
        var prior=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
        try{image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Folder,name+".png"),image.EncodeToPNG());}
        finally{RenderTexture.active=prior;DestroyImmediate(image);}
    }
    void DrawShared()
    {
        for(int group=0;group<systems.Count;group++)
        {
            systems[group].GetComponent<ParticleSystemRenderer>().BakeMesh(meshes[group],camera,false);
            if(meshes[group].vertexCount>0)Graphics.DrawMeshInstanced(meshes[group],0,material,matrices[group],matrices[group].Length,null,ShadowCastingMode.Off,false,0,camera,LightProbeUsage.Off);
        }
    }
    IEnumerator Run()
    {
        Directory.CreateDirectory(Folder);root=new GameObject("SharedAuraSimulationProbe");
        var cg=new GameObject("quarter view");cg.transform.SetParent(root.transform);camera=cg.AddComponent<Camera>();camera.enabled=false;
        camera.orthographic=true;camera.orthographicSize=11;camera.transform.position=new Vector3(5000,25,4975);camera.transform.LookAt(new Vector3(5000,1,5000));camera.farClipPlane=100;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.015f,.02f,.035f);
        rt=new RenderTexture(1280,720,24);camera.targetTexture=rt;
        for(int group=0;group<8;group++){var m=new List<Matrix4x4>();for(int i=group;i<100;i+=8)m.Add(Matrix4x4.TRS(Point(i),Quaternion.identity,Vector3.one));matrices[group]=m.ToArray();}
        var prefab=Resources.Load<GameObject>("Combat/VFX/PF_VFX_MeleeElementStatusAura");
        update=ProfilerRecorder.StartNew(new ProfilerCategory("Particles"),"ParticleSystem.UpdateJob",1,ProfilerRecorderOptions.StartImmediately|ProfilerRecorderOptions.WrapAroundWhenCapacityReached|ProfilerRecorderOptions.SumAllSamplesInFrame);
        geometry=ProfilerRecorder.StartNew(new ProfilerCategory("Particles"),"ParticleSystem.GeometryJob",1,ProfilerRecorderOptions.StartImmediately|ProfilerRecorderOptions.WrapAroundWhenCapacityReached|ProfilerRecorderOptions.SumAllSamplesInFrame);
        draws=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Draw Calls Count",1);
        rows.Add("Isolated 100 instances of one original local Billboard layer. Reference and shared use same eight seeds. Shared: eight original ParticleSystems, BakeMesh and instanced original material. No production integration, no actual actors, worker CPU sums not frame critical path.");
        foreach(string layer in new[]{"Fire","Lightning aura","Smoke"})
        foreach(bool shared in new[]{false,true})
        {
            Progress=layer+" "+(shared?"shared":"original");
            var template=prefab.GetComponentsInChildren<ParticleSystem>(true).First(p=>p.name==layer);
            material=new Material(template.GetComponent<ParticleSystemRenderer>().sharedMaterial){enableInstancing=true};
            int count=shared?8:100;
            for(int i=0;i<count;i++)
            {
                var ps=Instantiate(template,shared?new Vector3(5000,1,5000):Point(i),Quaternion.identity,root.transform);
                ps.gameObject.SetActive(true);ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);ps.useAutoRandomSeed=false;ps.randomSeed=(uint)((i%8)*37+17);
                var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.enabled=!shared;
                ps.Play(false);systems.Add(ps);if(shared)meshes.Add(new Mesh());
            }
            var cpu=new List<double>();var jobs=new List<double>();var geo=new List<double>();var draw=new List<double>();
            for(int f=0;f<210;f++)
            {
                var sw=Stopwatch.StartNew();if(shared)DrawShared();sw.Stop();camera.Render();
                yield return null;
                if(f<30)continue;
                cpu.Add(sw.Elapsed.TotalMilliseconds);if(update.Valid&&update.LastValue>=0&&update.LastValue<1000000000)jobs.Add(update.LastValue/1000000.0);
                if(geometry.Valid&&geometry.LastValue>=0&&geometry.LastValue<1000000000)geo.Add(geometry.LastValue/1000000.0);if(draws.Valid)draw.Add(draws.LastValue);
            }
            foreach(var ps in systems){ps.Simulate(2.25f,false,true,true);ps.Pause(false);}
            if(shared)DrawShared();camera.Render();Capture(layer.Replace(" ","_")+"_"+(shared?"shared":"original"));
            rows.Add(Progress+" systems="+systems.Count+" submitCPU="+Summary(cpu)+" particleCPU="+Summary(jobs)+" geometryCPU="+Summary(geo)+" sceneDraws="+Summary(draw));
            File.WriteAllLines(Path.Combine(Folder,"Result.txt"),rows);
            foreach(var ps in systems)DestroyImmediate(ps.gameObject);systems.Clear();foreach(var mesh in meshes)DestroyImmediate(mesh);meshes.Clear();DestroyImmediate(material);material=null;
            yield return null;
        }
        Progress="COMPLETE";rows.Add(Progress);File.WriteAllLines(Path.Combine(Folder,"Result.txt"),rows);
    }
    static string Summary(List<double> a){if(a.Count==0)return "UNAVAILABLE";a.Sort();return "avg:"+a.Average().ToString("F4")+",p95:"+a[Math.Min(a.Count-1,(int)(a.Count*.95))].ToString("F4");}
    void OnDestroy()
    {
        EditorApplication.update-=Tick;update.Dispose();geometry.Dispose();draws.Dispose();foreach(var mesh in meshes)if(mesh!=null)DestroyImmediate(mesh);
        if(camera!=null)camera.targetTexture=null;if(rt!=null){rt.Release();DestroyImmediate(rt);}if(root!=null)DestroyImmediate(root);if(material!=null)DestroyImmediate(material);
        Application.runInBackground=background;if(active==this)active=null;
    }
}
