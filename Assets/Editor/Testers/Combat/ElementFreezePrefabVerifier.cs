using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class ElementFreezePrefabVerifier
{
    static void Check(bool ok,string message) { if(!ok)throw new Exception(message); }
    public static string Run()
    {
        Check(Application.isPlaying,"Play required");
        var rows=new List<string>();var root=new GameObject("FreezePrefabVerifier");
        var service=UnityEngine.Object.FindFirstObjectByType<ElementalReactionVfxRuntimeService>();
        var cameraField=typeof(ElementalReactionVfxRuntimeService).GetField("validationCamera",BindingFlags.Instance|BindingFlags.NonPublic);
        object oldCamera=cameraField.GetValue(service);
        var cg=new GameObject("validation camera");cg.transform.SetParent(root.transform);var cam=cg.AddComponent<Camera>();cam.enabled=false;
        cam.transform.position=new Vector3(5000,8,4992);cam.transform.LookAt(new Vector3(5000,1,5000));
        cameraField.SetValue(service,cam);
        try
        {
            foreach(string name in new[]{"Rapax","Gobbler","Ceratoferox","Ursacetus"})
            foreach(EnemyGradeType grade in new[]{EnemyGradeType.Normal,EnemyGradeType.Elite,EnemyGradeType.GreaterElite,EnemyGradeType.Boss})
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/Resources/Enemies/Protofactor/Prefabs/PF_EnemyActor_"+name+".prefab");
                var g=UnityEngine.Object.Instantiate(prefab,new Vector3(5000,0,5000),Quaternion.identity,root.transform);
                try
                {
                    var rank=g.GetComponent<EnemyRank>();typeof(EnemyRank).GetField("<GradeType>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(rank,grade);
                    var status=g.GetComponent<ElementalStatusController>();var health=g.GetComponent<CombatHealth>();health.SetMaxHp(100000,true);
                    var movement=g.GetComponent<EnemyMovement>();var motor=g.GetComponent<EnemyMotor>();var bridge=g.GetComponent<EnemyAnimationBridge>();
                    var melee=g.GetComponent<EnemyMeleeAttackController>();var animator=g.GetComponentInChildren<Animator>();
                    float initialSpeed=animator.speed;bool normal=grade==EnemyGradeType.Normal;
                    Action<int> ice=n=>{for(int i=0;i<n;i++)status.TryApplyDirectHit(new ElementalStatusApplication(WeaponElement.Ice,100,root,"fixture",true,false,g.transform.position,Vector3.forward));};
                    ice(4);
                    Check(!motor.IsFrozen && !bridge.IsFrozen && animator.speed>0,"four stacks froze actor");
                    int start=service.GetSpawnCountForValidation(ElementalReactionType.Freeze,ElementalReactionVfxSlotType.Start);
                    int end=service.GetSpawnCountForValidation(ElementalReactionType.Freeze,ElementalReactionVfxSlotType.End);
                    ice(1);
                    Check(status.IsFrozen,"freeze mark absent");
                    Check(motor.IsFrozen==normal && bridge.IsFrozen==normal && movement.IsStatusMovementLocked==normal,"grade movement/animation lock");
                    Check((animator.speed==0)==normal && (melee.StatusActionSpeedMultiplier==0)==normal,"grade Animator/melee lock");
                    // Source VTP3D Start/End wrappers are empty; Loop is the existing playable effect.
                    Check(service.GetLoopInstanceIdForValidation(status,ElementalReactionType.Freeze)!=0,"Loop not spawned");
                    status.AdvanceReactionStatesForValidation(Time.time+5.01f);
                    Check(!motor.IsFrozen && !bridge.IsFrozen && Mathf.Approximately(animator.speed,initialSpeed) && melee.StatusActionSpeedMultiplier==1,"expiry failed to restore");
                    Check(service.GetLoopInstanceIdForValidation(status,ElementalReactionType.Freeze)==0,"expiry VFX lifecycle");
                    ice(5);Check(status.ConsumeForDischarge(WeaponElement.Ice,out bool shattered)==5 && shattered,"shatter eligibility");
                    Check(!bridge.IsFrozen && animator.speed>0 && service.GetLoopInstanceIdForValidation(status,ElementalReactionType.Freeze)==0,"shatter release");
                    ice(5);g.SetActive(false);g.SetActive(true);
                    Check(!status.IsFrozen && !bridge.IsFrozen && !motor.IsFrozen && animator.speed>0,"pooled reuse freeze leak");
                    Check(service.GetLoopInstanceIdForValidation(status,ElementalReactionType.Freeze)==0,"pooled reuse VFX leak");
                    rows.Add("PASS "+name+" "+grade+": real prefab motor/Animator/melee, 4-stack mobility, 5-stack grade immunity, FrostAura Loop, expiry/shatter/disable-reenable release; Start/End empty in source, NOT_IMPLEMENTED");
                }
                finally {UnityEngine.Object.DestroyImmediate(g);}
            }
        }
        catch(Exception e){rows.Add("FAIL "+e);}
        finally {cameraField.SetValue(service,oldCamera);UnityEngine.Object.DestroyImmediate(root);}
        string result=string.Join("\n",rows);
        System.IO.File.WriteAllText(System.IO.Path.GetFullPath("../개인파일/코덱스산출/Combat/ElementStatusGoal20260927/FreezePrefabResult.txt"),result);
        return result;
    }
}
