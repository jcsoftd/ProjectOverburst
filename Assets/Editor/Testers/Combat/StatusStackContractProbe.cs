using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static class StatusStackContractProbe
{
    public static string Run()
    {
        if(!Application.isPlaying || PlayerContext.Instance?.CurrentActor==null)throw new Exception("Ready Play required");
        var rows=new List<string>();var actors=new List<EnemyActor>();
        var player=PlayerContext.Instance.CurrentActor;var root=new GameObject("StatusStackContractProbe");
        var poolRoot=new GameObject("pool");poolRoot.transform.SetParent(root.transform);poolRoot.SetActive(false);
        var pool=root.AddComponent<EnemyPoolService>();pool.Configure(poolRoot.transform,0);
        var theme=MapThemeCatalog.Resolve("SpiderBrood");var roster=theme.BuildRoster(1,1,0,27100);
        var spawn=root.AddComponent<EnemySpawnService>();spawn.Configure(theme.Catalog,pool);
        void Check(bool ok,string message){if(!ok)throw new Exception(message);rows.Add("PASS "+message);}
        try
        {
            for(int i=0;i<9;i++)
            {
                var a=spawn.Spawn(new EnemySpawnRequest(roster[i==8?1:0],new Vector3(5000+i,0,5000),Quaternion.identity,player.transform,root,player.transform,root.transform,seed:i));
                a.Health.SetMaxHp(100000,true);a.AI.enabled=false;a.GetComponent<EnemyMovement>().enabled=false;actors.Add(a);
            }
            var origin=actors[0];var status=origin.GetComponent<ElementalStatusController>();
            var hits=new List<float>();
            void Hit(CombatHealth h,DamageInfo d,float actual,bool fatal){if(d.playerAttackKind==PlayerAttackKind.Elemental)hits.Add(actual);}
            foreach(var a in actors)a.Health.OnDamageResolved+=Hit;
            foreach(bool delayed in new[]{false,true})for(int s=1;s<=5;s++)foreach(float energy in new[]{0f,.5f,1f})
            {
                foreach(var a in actors){a.Health.SetMaxHp(100000,true);a.GetComponent<ElementalStatusController>().ClearAllStatuses();}
                for(int i=0;i<s;i++)status.TryApplyDirectHit(new ElementalStatusApplication(WeaponElement.Electric,100,player.gameObject,"probe-"+i,true,false,origin.transform.position,Vector3.forward));
                var batch=new ElementDischargeBatch();batch.Capture(player.GetComponent<CombatTarget>(),WeaponElement.Electric,3);
                batch.ConfirmInitial(origin.Health);status.ConsumeForDischarge(WeaponElement.Electric,out _);hits.Clear();int links=0;
                if(delayed){batch.BeginDelayed(origin.transform.position,Time.time);bool pending=true;for(int f=1;f<=100&&pending;f++)pending=batch.AdvanceDelayed(Time.time+f*.06f,100,energy,player.gameObject,null,(a,b)=>links++);Check(!pending,"delayed queue drained");}
                else batch.ExecuteLightning(100,energy,player.gameObject,(a,b)=>links++);
                int expected=s+(energy==1?2:energy==.5f?1:0);
                Check(links==expected&&hits.Count==expected,$"{(delayed?"delayed":"sync")} stack={s} energy={energy} links={links}");
                for(int i=0;i<hits.Count;i++)Check(Mathf.Abs(hits[i]-(25+5*(s-1))*Mathf.Pow(.8f,i))<.02f,$"hop={i+1} damage={hits[i]:F3}");
            }
            foreach(var a in actors)a.Health.OnDamageResolved-=Hit;
            var visibility=typeof(MeleeElementStatusAuraController).GetMethod("ApplyScheduledVisibility",BindingFlags.Instance|BindingFlags.NonPublic);
            foreach(int actorIndex in new[]{0,8})foreach(var element in new[]{WeaponElement.Fire,WeaponElement.Electric})
            {
                var actor=actors[actorIndex];var controller=actor.GetComponent<MeleeElementStatusAuraController>();var st=actor.GetComponent<ElementalStatusController>();
                var type=element==WeaponElement.Fire?MeleeElementStatusAuraType.Burning:MeleeElementStatusAuraType.Shocked;
                st.ClearAllStatuses();MeleeElementStatusAuraPresentation presentation=null;int plays=0;float previousRate=0,initialRate=0;
                for(int s=1;s<=5;s++)
                {
                    st.TryApplyDirectHit(new ElementalStatusApplication(element,100,player.gameObject,"aura-"+s,true,false,actor.transform.position,Vector3.forward));
                    controller=actor.GetComponent<MeleeElementStatusAuraController>();
                    visibility.Invoke(controller,new object[]{true});presentation=controller.PresentationForValidation;
                    Check(presentation!=null&&presentation.IsAuraActive(type),$"actor={actorIndex} {type} stack={s} visible");
                    if(s==1)plays=presentation.PlayCommandCountForValidation;
                    else Check(plays==presentation.PlayCommandCountForValidation,"stack increase does not restart particles");
                    float rate=presentation.GetAuraObject(type).GetComponentsInChildren<ParticleSystem>(true).Sum(ps=>ps.emission.rateOverTimeMultiplier);
                    Check(rate>previousRate,"density increases each stack");previousRate=rate;if(s==1)initialRate=rate;
                }
                if(element==WeaponElement.Electric)
                {
                    var module=presentation.GetAuraObject(type);var volume=CombatTargetVfxPlacement.ResolveVolume(actor.GetComponent<CombatTarget>());
                    Check(Vector3.Distance(module.transform.position,volume.Center)<.001f,"shock centered on visual body");
                    Check(module.transform.GetChild(0).localPosition==Vector3.zero,"source +1m removed");
                }
                st.ConsumeForDischarge(element,out _);Check(!controller.IsAuraActive(type)&&!controller.PresentationVisibleForValidation,"consume releases presentation");
                st.TryApplyDirectHit(new ElementalStatusApplication(element,100,player.gameObject,"fresh",true,false,actor.transform.position,Vector3.forward));
                visibility.Invoke(controller,new object[]{true});presentation=controller.PresentationForValidation;
                Check(Mathf.Abs(presentation.GetAuraObject(type).GetComponentsInChildren<ParticleSystem>(true).Sum(ps=>ps.emission.rateOverTimeMultiplier)-initialRate)<.001f,"pooled reuse restores stack1 authored rate");
                st.AdvanceReactionStatesForValidation(Time.time+20);Check(!controller.IsAuraActive(type)&&!controller.PresentationVisibleForValidation,"expiry releases presentation");
                st.ClearAllStatuses();
            }
            rows.Add("COMPLETE. Official small/medium actor prefabs and presentation pool, isolated positions. Chain clock advanced directly for deterministic contract checks; this is not a performance or real-input test.");
        }
        catch(Exception e){rows.Add("FAIL "+e);throw;}
        finally
        {
            foreach(var a in actors)if(a!=null)spawn.Release(a);
            UnityEngine.Object.DestroyImmediate(root);
            File.WriteAllLines(Path.GetFullPath("../개인파일/코덱스산출/Combat/ElementStatusGoal20260927/StackVfx/ContractResult.txt"),rows);
        }
        return rows[rows.Count-1]+" checks="+rows.Count;
    }
}
