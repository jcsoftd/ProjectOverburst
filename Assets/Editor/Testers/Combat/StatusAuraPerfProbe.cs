using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

public sealed class StatusAuraPerfProbe : ScriptableObject
{
    static StatusAuraPerfProbe active;
    public static string Progress = "NOT_RUN";
    GameObject root;
    Camera camera;
    RenderTexture rt;
    Camera[] cameras;
    bool[] enabledStates;
    bool background;
    bool layers;
    readonly List<(ParticleSystem ps, bool noise)> particleDefaults = new List<(ParticleSystem, bool)>();
    IEnumerator routine;
    int frame = -1;
    readonly List<MeleeElementStatusAuraPresentation> instances = new List<MeleeElementStatusAuraPresentation>();
    readonly List<ProfilerRecorder> recorders = new List<ProfilerRecorder>();
    readonly List<string> labels = new List<string>();
    readonly List<string> rows = new List<string>();
    readonly FrameTiming[] timing = new FrameTiming[1];
    static string Folder => Path.GetFullPath("../개인파일/코덱스산출/Combat/ElementStatusGoal20260927");
    public static void Begin(bool compareLayers = false)
    {
        if (!Application.isPlaying || active != null) throw new Exception("idle Play required");
        active = CreateInstance<StatusAuraPerfProbe>();
        active.layers = compareLayers;
        active.routine = active.Run(); EditorApplication.update += Tick;
    }
    static void Tick()
    {
        if (active == null) return;
        if (!Application.isPlaying) { DestroyImmediate(active); return; }
        if (active.frame == Time.frameCount) return;
        active.frame = Time.frameCount;
        try { if (!active.routine.MoveNext()) DestroyImmediate(active); }
        catch (Exception e) { Progress = "FAIL " + e; DestroyImmediate(active); }
    }
    void Record(ProfilerCategory category, string name)
    {
        labels.Add(name);
        recorders.Add(ProfilerRecorder.StartNew(category, name, 1,
            ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.WrapAroundWhenCapacityReached | ProfilerRecorderOptions.SumAllSamplesInFrame));
    }
    IEnumerator Run()
    {
        background = Application.runInBackground; Application.runInBackground = true;
        root = new GameObject("StatusAuraPerfProbe");
        cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None); enabledStates = new bool[cameras.Length];
        for (int i = 0; i < cameras.Length; i++) { enabledStates[i] = cameras[i].enabled; cameras[i].enabled = false; }
        var cg = new GameObject("quarter view"); cg.transform.SetParent(root.transform); camera = cg.AddComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = 12;
        camera.transform.position = new Vector3(5000, 24, 4976); camera.transform.LookAt(new Vector3(5000, 0, 5000));
        camera.farClipPlane = 100; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.015f,.02f,.035f);
        rt = new RenderTexture(1920,1080,24); camera.targetTexture = rt;
        var prefab = Resources.Load<GameObject>("Combat/VFX/PF_VFX_MeleeElementStatusAura");
        for (int i = 0; i < 200; i++)
        {
            var g = Instantiate(prefab, new Vector3(4991 + i%20, 0, 4993 + i/20 * 1.4f), Quaternion.identity, root.transform);
            instances.Add(g.GetComponent<MeleeElementStatusAuraPresentation>()); g.SetActive(false);
            foreach(var ps in g.GetComponentsInChildren<ParticleSystem>(true)) particleDefaults.Add((ps, ps.noise.enabled));
        }
        Record(new ProfilerCategory("Particles"), "ParticleSystem.UpdateJob");
        Record(new ProfilerCategory("Particles"), "ParticleSystem.GeometryJob");
        Record(new ProfilerCategory("PlayerLoop"), "PreLateUpdate.ParticleSystemBeginUpdateAll");
        Record(new ProfilerCategory("PlayerLoop"), "PostLateUpdate.ParticleSystemEndUpdateAll");
        Record(ProfilerCategory.Render, "Draw Calls Count"); Record(ProfilerCategory.Memory, "GC Allocated In Frame");
        rows.Add("Original aura presentation fixture; full original particles, 1920x1080 quarter-view RT; no AI, no status damage, no aura visibility scheduler. Worker job samples are summed CPU time, not frame critical-path time. Editor global counters include unrelated work.");
        rows.Add(SystemInfo.graphicsDeviceName + " / " + SystemInfo.processorType);
        foreach (int mode in layers ? new[] { 0, 1 } : new[] { -1, 0, 1, 2 })
        foreach (int n in layers ? new[] {100} : mode < 0 ? new[] { 0 } : new[] { 50, 100, 200 })
        foreach (string omit in layers ? (mode==0 ? new[] {"full", "burnGlow", "burnAura", "noiseOnly", "full"} : new[] {"full", "shockSmoke", "shockSpark", "full"}) : new[]{"full"})
        {
            foreach (var p in instances) { p.ClearAllAuras(); p.gameObject.SetActive(false); }
            foreach(var entry in particleDefaults) { var noise=entry.ps.noise;noise.enabled=entry.noise; }
            for (int i = 0; i < n; i++)
            {
                var p = instances[i]; p.gameObject.SetActive(true);
                if (mode != 1) p.SetAuraActive(MeleeElementStatusAuraType.Burning,true);
                if (mode != 0) p.SetAuraActive(MeleeElementStatusAuraType.Shocked,true);
                foreach(var ps in p.GetComponentsInChildren<ParticleSystem>())
                {
                    ps.useAutoRandomSeed=false;ps.randomSeed=(uint)(i*17+ps.name.Length+1);
                    bool stop=(omit=="burnAura" && ps.name=="Fire ayra") || (omit=="burnGlow" && ps.name=="Glow")
                        || (omit=="burnFlame" && ps.name=="Fire") || (omit=="shockArc" && ps.name=="Lightning aura")
                        || (omit=="shockSpark" && ps.name=="Sparks") || (omit=="shockSmoke" && ps.name=="Smoke");
                    if(stop)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                    if(omit=="noiseOnly"){var noise=ps.noise;noise.enabled=false;}
                }
            }
            string stage = (mode < 0 ? "baseline" : mode == 0 ? "burn" : mode == 1 ? "shock" : "both") + " " + n;
            stage+=" omit="+omit;
            Progress = "warming " + stage;
            float until = Time.realtimeSinceStartup + 2;
            while (Time.realtimeSinceStartup < until) yield return null;
            var metrics = new List<double>[recorders.Count]; for (int j=0;j<metrics.Length;j++) metrics[j]=new List<double>(180);
            var frames=new List<double>(180);var gpu=new List<double>(180);
            Progress="measuring "+stage; int invalidCpu=0,invalidGpu=0;
            for(int i=0;i<180;i++)
            {
                yield return null;
                for(int j=0;j<metrics.Length;j++) if(recorders[j].Valid)
                {
                    long value=recorders[j].LastValue;
                    if(value<0 || (j<4 && value>1000000000L))invalidCpu++;
                    else metrics[j].Add(value / (j<4 ? 1000000.0 : 1.0));
                }
                frames.Add(Time.unscaledDeltaTime*1000);
                FrameTimingManager.CaptureFrameTimings(); if(FrameTimingManager.GetLatestTimings(1,timing)>0)
                {if(timing[0].gpuFrameTime>0 && timing[0].gpuFrameTime<1000)gpu.Add(timing[0].gpuFrameTime);else invalidGpu++;}
            }
            int particles=0,systems=0;
            foreach(var p in instances) if(p.gameObject.activeSelf) foreach(var ps in p.GetComponentsInChildren<ParticleSystem>()) { particles+=ps.particleCount; systems++; }
            string line=stage+" activeSystems="+systems+" sampledParticles="+particles+" frameMs="+Summary(frames)+" GPUms="+Summary(gpu);
            line+=" invalidCpuSamples="+invalidCpu+" invalidGpuSamples="+invalidGpu;
            for(int j=0;j<metrics.Length;j++) line+=" | "+labels[j]+"="+Summary(metrics[j]);
            rows.Add(line);
            File.WriteAllLines(Path.Combine(Folder,layers ? "StatusAuraSingleLayers.txt" : "StatusAuraBaseline.txt"),rows);
            if(layers)
            {
                // Freeze both reference and variant at the same local simulation age/seed for visual comparison.
                foreach(var p in instances)if(p.gameObject.activeSelf)foreach(var ps in p.GetComponentsInChildren<ParticleSystem>())
                    if(ps.isPlaying) { ps.Simulate(2.25f,false,true,true); ps.Pause(false); }
                camera.Render();var prior=RenderTexture.active;RenderTexture.active=rt;
                var texture=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);texture.Apply();
                File.WriteAllBytes(Path.Combine(Folder,"AuraSingle_"+mode+"_"+omit+".png"),texture.EncodeToPNG());DestroyImmediate(texture);RenderTexture.active=prior;
            }
        }
        Progress="complete: "+Path.Combine(Folder,layers ? "StatusAuraSingleLayers.txt" : "StatusAuraBaseline.txt");
    }
    static string Summary(List<double> a)
    {
        if(a.Count==0)return "UNAVAILABLE";a.Sort();double sum=0;foreach(double n in a)sum+=n;
        return "avg "+(sum/a.Count).ToString("F4")+" p95 "+a[Math.Min(a.Count-1,(int)(a.Count*.95))].ToString("F4");
    }
    void OnDestroy()
    {
        EditorApplication.update-=Tick;if(active==this)active=null;
        foreach(var r in recorders)r.Dispose();
        if(cameras!=null)for(int i=0;i<cameras.Length;i++)if(cameras[i]!=null)cameras[i].enabled=enabledStates[i];
        if(camera!=null)camera.targetTexture=null;
        if(rt!=null){rt.Release();DestroyImmediate(rt);}if(root!=null)DestroyImmediate(root);
        Application.runInBackground=background;
    }
}
