using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ShatterWaveProbe
{
    public static string Progress {get;private set;}="NOT_RUN";
    static IEnumerator run;static int frame;static double deadline;static bool oldBackground;
    static GameObject root;static EnemySpawnService spawner;static EnemyDefinition definition;static PlayerActorRuntime player;
    static readonly List<EnemyActor> actors=new List<EnemyActor>();
    static readonly List<string> rows=new List<string>();
    static readonly List<int> order=new List<int>();static readonly List<float> times=new List<float>();
    static float started;
    static string Output=>Path.GetFullPath("../개인파일/코덱스산출/Combat/ElementStatusGoal20260927/ShatterWaveResult.txt");
    public static void Begin(){
        if(!Application.isPlaying||run!=null)throw new Exception("idle Play required");
        rows.Clear();order.Clear();times.Clear();actors.Clear();frame=-1;deadline=EditorApplication.timeSinceStartup+40;
        oldBackground=Application.runInBackground;Application.runInBackground=true;Progress="RUNNING";run=Run();EditorApplication.update+=Tick;
    }
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);rows.Add("PASS "+message);}
    static void Tick(){
        EditorApplication.QueuePlayerLoopUpdate();if(frame==Time.frameCount)return;frame=Time.frameCount;
        try{if(!Application.isPlaying||EditorApplication.timeSinceStartup>deadline)throw new Exception("timeout");if(!run.MoveNext())Finish(null);}
        catch(Exception e){Finish(e.ToString());}
    }
    static EnemyActor Spawn(int index){
        var a=spawner.Spawn(new EnemySpawnRequest(definition,new Vector3(5000+index,0,5000),Quaternion.identity,player.transform,root,player.transform,root.transform,seed:index));
        a.Health.SetMaxHp(100000,true);a.AI.enabled=false;a.GetComponent<EnemyMovement>().enabled=false;
        a.Health.OnDamageResolved+=Hit;actors.Add(a);return a;
    }
    static void Hit(CombatHealth h,DamageInfo d,float actual,bool fatal){
        if(actual<=0||d.playerAttackKind!=PlayerAttackKind.Elemental)return;
        order.Add(actors.FindIndex(a=>a.Health==h));times.Add(Time.time-started);
    }
    static IEnumerator Run(){
        while(PlayerContext.Instance==null||PlayerContext.Instance.CurrentActor==null)yield return null;
        player=PlayerContext.Instance.CurrentActor;root=new GameObject("ShatterWaveProbe");
        var poolRoot=new GameObject("pool");poolRoot.transform.SetParent(root.transform);poolRoot.SetActive(false);
        var pool=root.AddComponent<EnemyPoolService>();pool.Configure(poolRoot.transform,0);
        var theme=MapThemeCatalog.Resolve("SpiderBrood");definition=theme.BuildRoster(1,0,0,27100)[0];
        spawner=root.AddComponent<EnemySpawnService>();spawner.Configure(theme.Catalog,pool);
        var heavy=AssetDatabase.LoadAssetAtPath<MeleeHeavyAttackDefinition>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Heavy/GreatswordHeavyAttack.asset");
        var prefab=heavy.elementVfx.iceShatter;var center=new Vector3(5000,0,5000);
        for(int i=0;i<5;i++)Spawn(i);
        var before=TransientVfxPool.GetStatistics(prefab);started=Time.time;
        for(int i=0;i<5;i++)ShatterWaveScheduler.Submit(actors[i].Health,100,player.gameObject,prefab,center,center+Vector3.right*(i+.1f),Vector3.forward,5);
        Check(order.Count==1&&order[0]==0,"center immediately, outer targets pending");
        while(ShatterWaveScheduler.PendingCount>0)yield return null;
        Check(order.Count==5,"one bonus per target");
        for(int i=0;i<5;i++){Check(order[i]==i,"outward order ring "+i);Check(times[i]>=i*.06f-.025f,"minimum delay ring "+i+" time="+times[i]);Check(Mathf.Abs(actors[i].Health.CurrentHp-99900)<.01f,"damage preserved ring "+i);}
        Check(TransientVfxPool.GetStatistics(prefab).Requests-before.Requests==5,"five VFX requests with five damage events");

        var target=actors[4];var status=target.GetComponent<ElementalStatusController>();
        float hp=target.Health.CurrentHp;
        ShatterWaveScheduler.Submit(target.Health,100,player.gameObject,prefab,center,center+Vector3.right*4.1f,Vector3.forward,5);
        status.TryApplyDirectHit(new ElementalStatusApplication(WeaponElement.Ice,100,player.gameObject,"new-weak",true,false,target.transform.position,Vector3.forward));
        while(ShatterWaveScheduler.PendingCount>0)yield return null;
        Check(status.GetStackCount(WeaponElement.Ice)==1&&Mathf.Abs(target.Health.CurrentHp-(hp-100))<.01f,"later weak stack preserved");

        int life=status.LifecycleVersion;
        ShatterWaveScheduler.Submit(target.Health,100,player.gameObject,prefab,center,center+Vector3.right*4.1f,Vector3.forward,5);
        target.Health.OnDamageResolved-=Hit;spawner.Release(target);actors.Remove(target);var recycled=Spawn(4);
        Check(recycled==target&&status.LifecycleVersion!=life,"official pool reused with new generation");
        before=TransientVfxPool.GetStatistics(prefab);
        while(ShatterWaveScheduler.PendingCount>0)yield return null;
        Check(recycled.Health.CurrentHp==100000&&TransientVfxPool.GetStatistics(prefab).Requests==before.Requests,"old reservation cancelled after reuse");
        ShatterWaveScheduler.Submit(recycled.Health,100,player.gameObject,prefab,center,center+Vector3.right*4.1f,Vector3.forward,5);
        recycled.Health.TakeDamage(new DamageInfo(1000000,recycled.transform.position,player.gameObject));
        before=TransientVfxPool.GetStatistics(prefab);
        while(ShatterWaveScheduler.PendingCount>0)yield return null;
        Check(TransientVfxPool.GetStatistics(prefab).Requests==before.Requests,"death before dispatch cancels reservation");
        rows.Add("Scope: real actor/pool and normal frame clock, direct scheduler boundary checks. Public heavy covered by cost probe.");
    }
    static void Finish(string error){
        EditorApplication.update-=Tick;run=null;Progress=error==null?"COMPLETE":"FAIL "+error;rows.Add(Progress);
        foreach(var a in actors)if(a!=null){a.Health.OnDamageResolved-=Hit;spawner.Release(a);}actors.Clear();
        if(root!=null)UnityEngine.Object.DestroyImmediate(root);Application.runInBackground=oldBackground;File.WriteAllLines(Output,rows);
    }
}
