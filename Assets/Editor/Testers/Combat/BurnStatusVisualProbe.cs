using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Local visual fixture: production actors, fixed positions and camera; no performance claim.
public sealed class BurnStatusVisualProbe : ScriptableObject
{
    static BurnStatusVisualProbe active;
    public static string Progress="NOT_RUN";
    GameObject root;Camera camera;RenderTexture rt;Material groundMaterial;
    EnemySpawnService spawner;readonly List<EnemyActor> actors=new List<EnemyActor>();
    ElementalReactionVfxRuntimeService service;FieldInfo validationCamera;object oldCamera;
    IEnumerator routine;int frame=-1;bool background;double deadline;
    readonly List<string> rows=new List<string>();
    static string Folder=>Path.GetFullPath("../개인파일/코덱스산출/Combat/ElementStatusGoal20260927/BurnCandidates/Applied");
    public static void Begin()
    {
        if(!Application.isPlaying||active!=null)throw new Exception("idle Play required");
        active=CreateInstance<BurnStatusVisualProbe>();active.background=Application.runInBackground;Application.runInBackground=true;
        active.deadline=EditorApplication.timeSinceStartup+120;active.routine=active.Run();EditorApplication.update+=Tick;Progress="RUNNING";
    }
    static void Tick()
    {
        if(active==null)return;EditorApplication.QueuePlayerLoopUpdate();
        if(active.frame==Time.frameCount)return;active.frame=Time.frameCount;
        try {if(!Application.isPlaying||EditorApplication.timeSinceStartup>active.deadline)throw new Exception("interrupted/timeout");if(!active.routine.MoveNext())DestroyImmediate(active);}
        catch(Exception e){Progress="FAIL "+e;active.rows.Add(Progress);File.WriteAllLines(Path.Combine(Folder,"Timeline.txt"),active.rows);DestroyImmediate(active);}
    }
    void Capture(string name,float age)
    {
        camera.Render();var prior=RenderTexture.active;RenderTexture.active=rt;
        var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
        try {image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Folder,name+".png"),image.EncodeToPNG());}
        finally {RenderTexture.active=prior;DestroyImmediate(image);}
        rows.Add(name+" time="+age.ToString("F4")+" activeCasts="+ElementChainScheduler.ActiveCastCount);
    }
    EnemyActor Spawn(EnemyDefinition definition,Vector3 point,int seed)
    {
        var player=PlayerContext.GetOrCreate().CurrentActor;
        var a=spawner.Spawn(new EnemySpawnRequest(definition,point,Quaternion.identity,player.transform,root,player.transform,root.transform,seed:seed));
        a.Health.SetMaxHp(100000,true);a.AI.enabled=false;
        var movement=a.GetComponent<EnemyMovement>();if(movement!=null)movement.enabled=false;
        var body=a.GetComponent<Rigidbody>();if(body!=null){body.linearVelocity=Vector3.zero;body.constraints=RigidbodyConstraints.FreezeAll;}
        actors.Add(a);return a;
    }
    void Apply(EnemyActor actor,WeaponElement element,int stacks)
    {
        for(int i=0;i<stacks;i++)actor.GetComponent<ElementalStatusController>().TryApplyDirectHit(new ElementalStatusApplication(element,100,root,"visual",true,false,actor.transform.position,Vector3.forward));
    }
    IEnumerator Run()
    {
        Directory.CreateDirectory(Folder);
        while(PersistentSceneFlow.Instance==null||PersistentSceneFlow.Instance.IsSwitching||PlayerContext.GetOrCreate().CurrentActor==null)yield return null;
        root=new GameObject("BurnStatusVisualProbe");var center=new Vector3(5000,0,5000);
        var poolRoot=new GameObject("pool");poolRoot.transform.SetParent(root.transform);poolRoot.SetActive(false);
        var pool=root.AddComponent<EnemyPoolService>();pool.Configure(poolRoot.transform,0);
        var theme=MapThemeCatalog.Resolve("SpiderBrood");spawner=root.AddComponent<EnemySpawnService>();spawner.Configure(theme.Catalog,pool);
        var cg=new GameObject("quarter view");cg.transform.SetParent(root.transform);camera=cg.AddComponent<Camera>();camera.enabled=true;
        camera.orthographic=true;camera.orthographicSize=3.5f;camera.transform.position=center+new Vector3(0,16,-16);camera.transform.LookAt(center);
        camera.nearClipPlane=.1f;camera.farClipPlane=100;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.045f,.05f,.06f);
        rt=new RenderTexture(1280,720,24);camera.targetTexture=rt;
        var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.transform.SetParent(root.transform);ground.transform.position=center+Vector3.down*.2f;ground.transform.localScale=new Vector3(40,.2f,40);
        groundMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));groundMaterial.color=new Color(.23f,.25f,.28f);ground.GetComponent<Renderer>().sharedMaterial=groundMaterial;
        service=FindFirstObjectByType<ElementalReactionVfxRuntimeService>();validationCamera=typeof(ElementalReactionVfxRuntimeService).GetField("validationCamera",BindingFlags.NonPublic|BindingFlags.Instance);oldCamera=validationCamera.GetValue(service);validationCamera.SetValue(service,camera);
        var roster=ElementCrowdCombatProbe.BuildProductionRoster(theme,50);
        var small=roster[0];var medium=roster.Find(d=>d.ActorPrefab!=null&&d.EnemyId.Contains("Carcinoptera"));
        Spawn(small,center+Vector3.left*1.7f,0);Spawn(medium,center+Vector3.right*1.7f,1);
        rows.Add("Fixed 45-degree quarter camera. Production SpiderBrood actors; AI/movement held for visual comparison. Not stop-immunity or performance proof.");
        foreach(var a in actors)Apply(a,WeaponElement.Fire,1);
        var visibility=FindFirstObjectByType<MeleeElementStatusAuraVisibilityScheduler>();
        typeof(MeleeElementStatusAuraVisibilityScheduler).GetField("cachedCamera",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(visibility,camera);
        foreach(var a in actors)Apply(a,WeaponElement.Fire,1);
        float start=Time.time;
        for(int step=1;step<=3;step++){
            while(Time.time-start<step*.8f)yield return null;
            Capture("Burn_"+step,Time.time-start);
            foreach(var a in actors){var p=a.GetComponentInChildren<MeleeElementStatusAuraPresentation>();if(p==null||!p.IsAuraActive(MeleeElementStatusAuraType.Burning))throw new Exception("burn presentation missing");var burn=p.GetAuraObject(MeleeElementStatusAuraType.Burning);if(burn.transform.Find("Fire Loop sim 1")==null)throw new Exception("wrong burn prefab");rows.Add(a.name+" burn particles="+burn.GetComponentsInChildren<ParticleSystem>().Length);}
        }
        while(Time.time-start<5.3f)yield return null;
        foreach(var a in actors){var p=a.GetComponentInChildren<MeleeElementStatusAuraPresentation>();if(p!=null&&p.IsAuraActive(MeleeElementStatusAuraType.Burning))throw new Exception("burn did not expire");}rows.Add("PASS natural 5s expiry");
        foreach(var a in actors)Apply(a,WeaponElement.Fire,1);yield return null;yield return null;
        foreach(var a in actors){a.GetComponent<ElementalStatusController>().ClearAllStatuses();var p=a.GetComponentInChildren<MeleeElementStatusAuraPresentation>();if(p!=null&&p.IsAuraActive(MeleeElementStatusAuraType.Burning))throw new Exception("clear failed");}rows.Add("PASS explicit clear");
        foreach(var a in actors)Apply(a,WeaponElement.Fire,1);yield return null;yield return null;
        foreach(var a in actors)spawner.Release(a);actors.Clear();yield return null;
        Spawn(small,center,3);yield return null;
        if(actors[0].GetComponentInChildren<MeleeElementStatusAuraPresentation>()!=null)throw new Exception("pooled stale aura");rows.Add("PASS pool reuse clean");
        Progress="COMPLETE";rows.Add(Progress);File.WriteAllLines(Path.Combine(Folder,"Timeline.txt"),rows);
    }
    void OnDestroy()
    {
        EditorApplication.update-=Tick;
        if(service!=null&&validationCamera!=null)validationCamera.SetValue(service,oldCamera);
        foreach(var a in actors)if(a!=null&&spawner!=null)spawner.Release(a);
        if(camera!=null)camera.targetTexture=null;if(rt!=null){rt.Release();DestroyImmediate(rt);}if(root!=null)DestroyImmediate(root);if(groundMaterial!=null)DestroyImmediate(groundMaterial);
        Application.runInBackground=background;if(active==this)active=null;
    }
}


