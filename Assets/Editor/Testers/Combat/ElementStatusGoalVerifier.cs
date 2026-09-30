using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;

// Temporary actors only. Execute in isolated-save Play; no scene or asset saves.
public static class ElementStatusGoalVerifier
{
    static readonly List<string> results = new List<string>();
    static readonly Vector3 origin = new Vector3(5000,0,5000);
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); results.Add("PASS " + name); }
    static GameObject Actor(Transform parent, string name, CombatTeam team, Vector3 offset)
    {
        var g = new GameObject(name); g.transform.SetParent(parent); g.transform.position = origin + offset;
        var h = g.AddComponent<CombatHealth>(); h.SetMaxHp(100000f,true);
        g.AddComponent<CombatAffiliation>().Configure(team);
        var t = g.AddComponent<CombatTarget>(); t.Configure(team,true); t.ConfigureHurtVolume(Vector3.zero,.1f,.2f);
        return g;
    }
    static ElementalStatusController Status(GameObject g, GameObject source, WeaponElement e, int stacks)
    {
        var s = g.GetComponent<ElementalStatusController>() ?? g.AddComponent<ElementalStatusController>();
        for(int i=0;i<stacks;i++) s.TryApplyDirectHit(new ElementalStatusApplication(e,100f,source,"test",true,false,g.transform.position,Vector3.forward));
        return s;
    }
    public static string Run()
    {
        if(!Application.isPlaying) throw new Exception("Play required");
        results.Clear(); var root=new GameObject("ElementStatusGoalVerifier");
        try
        {
            var tuning=OverburstElementTuning.Current;
            var pure=new OverburstElementState(); pure.Add(WeaponElement.Fire,0,tuning);
            Check(!pure.TryTakeTick(.499f,out _,out _),"burn first tick is delayed");
            int ticks=0; while(pure.TryTakeTick(5,out _,out _)) ticks++;
            pure.Expire(5); Check(ticks==10&&!pure.HasAny,"burn includes expiry tick: 10 / 5s");
            pure.Add(WeaponElement.Fire,10,tuning); pure.Add(WeaponElement.Fire,10.25f,tuning);
            Check(pure.TryTakeTick(10.5f,out _,out int stack)&&stack==2,"stack refresh preserves tick phase");
            pure.Clear(); for(int i=0;i<7;i++) pure.Add(WeaponElement.Ice,0,tuning);
            Check(pure.Count(WeaponElement.Ice,0)==5&&pure.IsFrozen(4.999f)&&!pure.IsFrozen(5),"five stacks freeze exactly five seconds");
            var source=Actor(root.transform,"source",CombatTeam.PlayerParty,Vector3.back*10);
            var target=Actor(root.transform,"target",CombatTeam.Enemy,Vector3.zero);
            var status=Status(target,source,WeaponElement.Fire,5); var health=target.GetComponent<CombatHealth>();
            float hp=health.CurrentHp;
            status.AdvanceReactionStatesForValidation(Time.time+5f);
            Check(Mathf.Abs(hp-health.CurrentHp-50f)<.001f&&status.MoveSpeedMultiplier==1,"burn 5 stacks = 50 HP total, no speed lock");
            Check(!status.HasStatus(WeaponElement.Fire),"burn expires after final tick");
            var hit=new DamageInfo(100,target.transform.position,source,element:WeaponElement.Fire,sourceAttackSequenceId:999);
            Check(status.ApplyConfirmedHit(hit,100),"weak initial input applies");
            hit.sourceAttackPhaseIndex=2;
            Check(!status.ApplyConfirmedHit(hit,100)&&status.GetStackCount(WeaponElement.Fire)==1,"multi-rotation input adds only one stack");
            status.ClearAllStatuses(); Status(target,source,WeaponElement.Electric,5); hp=health.CurrentHp;
            status.AdvanceReactionStatesForValidation(Time.time+1.5f);
            Check(Mathf.Abs(hp-health.CurrentHp-10f)<.001f&&status.MoveSpeedMultiplier==0,"shock tick damages and interrupts movement");
            status.ConsumeForDischarge(WeaponElement.Electric,out _);
            status.AdvanceReactionStatesForValidation(Time.time+2.03f);
            Check(status.MoveSpeedMultiplier==1,"consumed shock still releases its control lock");
            status.ClearAllStatuses(); Status(target,source,WeaponElement.Ice,4);
            Check(Mathf.Abs(status.MoveSpeedMultiplier-.84f)<.0001f,"four cold stacks slow 16 percent");
            Check(status.ConsumeForDischarge(WeaponElement.Ice,out _)==0,"cold under five is not consumed");
            Status(target,source,WeaponElement.Ice,1);
            Check(status.IsFrozen&&status.MoveSpeedMultiplier==0,"normal enemy fully freezes");
            Check(status.ConsumeForDischarge(WeaponElement.Ice,out bool shatter)==5&&shatter&&status.MoveSpeedMultiplier==1,"shatter clears freeze control");
            foreach(EnemyGradeType grade in new[]{EnemyGradeType.Elite,EnemyGradeType.GreaterElite,EnemyGradeType.Boss})
            {
                var ranked=Actor(root.transform,grade.ToString(),CombatTeam.Enemy,Vector3.right*100);
                var rank=ranked.AddComponent<EnemyRank>();
                typeof(EnemyRank).GetField("<GradeType>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(rank,grade);
                var s=Status(ranked,source,WeaponElement.Ice,5);
                Check(s.IsFrozen&&s.MoveSpeedMultiplier>0&&s.ConsumeForDischarge(WeaponElement.Ice,out bool eligible)==5&&eligible,grade+" immune to freeze stop, can shatter");
                UnityEngine.Object.DestroyImmediate(ranked);
            }
            var energy=source.AddComponent<OverburstElementEnergy>();
            foreach(int e in new[]{0,50,100})
            {
                energy.Clear(); energy.BindWeapon("test",WeaponElement.Fire);
                for(int i=0;i<e/10;i++) energy.RecordConfirmedHit("test",WeaponElement.Fire,i+1,10);
                Check(energy.TryCommitDischarge(100,out var d),"commit energy "+e);
                float n=e/100f;
                Check(Mathf.Abs(d.Radius-(1.5f+2.5f*n))<.001f&&Mathf.Abs(d.FirstBlastDamage-100*(.6f+.75f*n)*(1+2*n))<.01f,"energy damage/radius "+e);
            }
            UnityEngine.Object.DestroyImmediate(target);
            foreach(int count in new[]{50,100,200}) RunBatch(root.transform,source,count);
            Check(energy.Amount==0,"derived damage does not recharge energy");
            return string.Join("\n",results);
        }
        catch(Exception e) { return string.Join("\n",results)+"\nFAIL "+e; }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    static void RunBatch(Transform root,GameObject source,int count)
    {
        var group=new GameObject("batch"+count);group.transform.SetParent(root);
        try
        {
            var targets=new GameObject[count];
            for(int i=0;i<count;i++) targets[i]=Actor(group.transform,"victim"+i,CombatTeam.Enemy,new Vector3((i%20)*.3f,0,(i/20)*.3f));
            var batch=new ElementDischargeBatch();
            foreach(WeaponElement e in new[]{WeaponElement.Fire,WeaponElement.Electric})
            {
                foreach(var g in targets){g.GetComponent<CombatHealth>().ResetHealth();g.GetComponent<ElementalStatusController>()?.ClearAllStatuses();Status(g,source,e,5);}
                batch.Capture(source.GetComponent<CombatTarget>(),e,2);
                foreach(var g in targets) batch.ConfirmInitial(g.GetComponent<CombatHealth>());
                var sw=Stopwatch.StartNew();
                if(e==WeaponElement.Fire) batch.ExecuteFire(100,source,null);else batch.ExecuteLightning(100,1,source,null);
                sw.Stop();
                Check(batch.OriginCount==count&&batch.SecondaryHits<=count*2&&batch.SecondaryHits>0,count+" "+e+" all origins and <=2 derived hits each");
                foreach(var g in targets) CheckDamageBound(g,e);
                results.Add("MEASURE "+count+" "+e+" candidates="+batch.CandidateChecks+" hits="+batch.SecondaryHits+" isolatedCPUms="+sw.Elapsed.TotalMilliseconds.ToString("F3")+" VFX=off (not frame performance)");
                batch.Clear();
            }
            // A lethal initial origin must still propagate, but a recycled origin must not.
            foreach(var g in targets){g.GetComponent<CombatHealth>().ResetHealth();g.GetComponent<ElementalStatusController>().ClearAllStatuses();Status(g,source,WeaponElement.Fire,5);}
            batch.Capture(source.GetComponent<CombatTarget>(),WeaponElement.Fire,2);
            batch.ConfirmInitial(targets[0].GetComponent<CombatHealth>());
            targets[0].GetComponent<CombatHealth>().TakeDamage(new DamageInfo(200000,targets[0].transform.position,source,triggersOnHitEffects:false));
            batch.ExecuteFire(100,source,null);
            Check(batch.SecondaryHits>0,"lethal origin propagates "+count);
            batch.Capture(source.GetComponent<CombatTarget>(),WeaponElement.Fire,2);
            batch.ConfirmInitial(targets[1].GetComponent<CombatHealth>());
            targets[1].SetActive(false); targets[1].SetActive(true);
            batch.ExecuteFire(100,source,null);Check(batch.SecondaryHits==0,"recycled origin invalidated "+count);
        }
        finally{UnityEngine.Object.DestroyImmediate(group);}
    }
    static void CheckDamageBound(GameObject target,WeaponElement e)
    {
        float damage=100000-target.GetComponent<CombatHealth>().CurrentHp;
        if(damage>(e==WeaponElement.Fire?120.01f:50.01f))throw new Exception("damage cap "+target.name+" "+damage);
    }
}
