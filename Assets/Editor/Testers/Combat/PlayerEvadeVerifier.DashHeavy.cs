using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

public static partial class PlayerEvadeVerifier
{
    public static void VerifyDashHeavyAssets(string directory)
    {
        Directory.CreateDirectory(directory);
        var results=new List<string>();
        void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);results.Add(label);}
        var definition=AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>(PlayerEvadeBuilder.DefinitionPath);
        var heavy=definition.dashHeavyAttackDefinition;
        Require(heavy!=null&&heavy.IsConfigured,"대시 강공 데이터 연결");
        Require(heavy!=definition.heavyAttackDefinition&&heavy!=definition.parriedHeavyAttackDefinition,"일반/패링 정의 분리");
        Require(heavy.attack.attackPhases.Length==1&&heavy.attack.movementPhases.Length==0
            &&(heavy.attack.visualHeightCurve==null||heavy.attack.visualHeightCurve.length==0),"한 판정/이동 중복 없음");
        var phase=heavy.attack.attackPhases[0];
        Require(phase.attackPattern.shape==AttackAreaShape.Rectangle&&phase.attackPattern.fillMode==AttackFillMode.LinearFill
            &&phase.attackPattern.direction==AttackFillDirection.NearToFar,"고정 원점 전방 순차 판정");
        Require(phase.basisFollowMode==AttackBasisFollowMode.Fixed,"범위 원점 고정");
        Require(Mathf.Abs(phase.SafeStart*heavy.attack.animationClip.length-.5f)<.0001f,"원본30프레임 베기 판정");
        Require(heavy.attack.playbackAcceleration.dashHeavyFocus,"공격/애니메이터 공통 집중 시계");
        Require(Resources.Load<Material>(DashHeavyFocusPresentation.MaterialResource)!=null,"집중 효과 Material 연결");
        Require(CombatActionSfxService.ResolveNamedClip("DashHeavyGather")!=null&&CombatActionSfxService.ResolveNamedClip("DashHeavyRelease")!=null,"집중/해방 소리 연결");
        foreach(var binding in AnimationUtility.GetCurveBindings(heavy.attack.animationClip))
            if(binding.propertyName=="RootT.x"||binding.propertyName=="RootT.z")
                foreach(var key in AnimationUtility.GetEditorCurve(heavy.attack.animationClip,binding).keys)
                    Require(Mathf.Abs(key.value)<.000001f,"수평 루트 절대0 "+binding.propertyName);
        foreach(float speed in new[]{.5f,1f,1.5f,2.5f})foreach(float start in new[]{0f,.12f,.24f,.42f,.47f,.47999f})
        {
            var plan=new DashHeavyTravelPlan(start,5f,.48f,.35f,speed);
            float previous=0f;
            for(int i=0;i<=600;i++)
            {
                float t=plan.Stop*i/600f, x=plan.Position(t);
                Require(!float.IsNaN(x)&&x>=previous-.00001f&&x<=5.00001f,"늦은 입력/공속별 단조 5m "+start+"/"+speed+"/"+i);
                Require(plan.Velocity(t)>=-.0001f,"후진 없음 "+start+"/"+speed+"/"+i);previous=x;
            }
            Require(Mathf.Abs(plan.Position(plan.Stop)-5f)<.00001f&&plan.Velocity(plan.Stop)==0f,"총거리·최종 정지 "+start+"/"+speed);
        }
        for(int i=0;i<40;i++)Require(DashHeavyFocusClock.Sample(.25f+(i+1)/240f)>DashHeavyFocusClock.Sample(.25f+i/240f),"집중 자세 연속 진행 "+i);
        File.WriteAllText(Path.Combine(directory,"DashHeavyAssetResult.json"),JsonConvert.SerializeObject(new{status="PASS",count=results.Count,checks=results},Formatting.Indented));
    }
    static void EquipDashHeavyGem(WeaponElement element)
    {
        var data=AssetDatabase.FindAssets("t:ElementGemItemData").Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<ElementGemItemData>).First(g=>g.element==element);
        var item=new ItemData(data,1,data.fixedGrade);
        typeof(PlayerEquipment).GetMethod("SetElementGem",Private).Invoke(actor.Equipment,new object[]{item});
        Check(actor.Equipment.ActiveElement==element,"격리 원소 보석 연결 "+element);
    }
    static IEnumerator VerifyDashHeavy()
    {
        var previousGem=actor.Equipment.EquippedElementGem;
        int previousCaptureRate=Time.captureFramerate;
        // Fixed capture delta does not model the unscaled evade clock together
        // with scaled focus playback. Exercise the production clocks instead.
        Time.captureFramerate=0;
        try
        {
            yield return VerifyHeavyFocusMotions();
            var energy=actor.GetComponent<OverburstElementEnergy>();
            if(energy==null)energy=actor.gameObject.AddComponent<OverburstElementEnergy>();
            foreach(var element in new[]{WeaponElement.Fire,WeaponElement.Ice,WeaponElement.Electric,WeaponElement.Dark,WeaponElement.Light})
            foreach(float clickAt in new[]{0f,.24f,.43f})
            {
                yield return Reset();EquipDashHeavyGem(element);FillEnergy(energy,12000+(int)element*1000+(int)(clickAt*100));
                Vector3 entry=actor.transform.position;
                yield return StartDodge(false,false,clickAt==0f);
                float dashStart=Field<float>(evade,"evadeStartTime");
                if(clickAt>0f){while(OverburstGameClock.UnscaledTime-dashStart<clickAt)yield return null;Send(false,false,false,true);yield return Frames(1);}
                Send();
                // Radiance can increase attack speed: preview intentionally waits
                // so contact does not precede the evade's damage handoff.
                float previewLimit=Time.unscaledTime+1f;
                while(!melee.IsDashHeavyWindupActive&&evade.IsEvading)
                {Check(Time.unscaledTime<previewLimit,"공속 보정 후 준비 시점 도달");yield return null;}
                Check(melee.IsDashHeavyWindupActive,"대시 중 E 준비 시작 "+element+"/"+clickAt);
                var definition=actor.Equipment.CurrentWeaponData.GetMeleeDefinition();
                var active=definition.dashHeavyAttackDefinition.attack;
                bool capture=element==WeaponElement.Fire&&Mathf.Abs(clickAt-.24f)<.001f;
                if(capture)
                {
                    poseProbe.actorRoot=actor.transform;
                    poseProbe.bones=new[]{HumanBodyBones.Hips,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot,HumanBodyBones.RightHand}
                        .Select(animator.GetBoneTransform).ToArray();
                    poseProbe.poseSamples.Clear();poseProbe.recordPoses=true;
                }
                var targets=new List<GameObject>();var hits=new List<int>();var hitTimes=new List<float>();var packets=new List<object>();
                try
                {
                    float speed=Field<float>(melee,"dashHeavyPlaybackSpeed");
                    var plan=Field<DashHeavyTravelPlan>(melee,"dashHeavyTravel");
                    var phase=active.attackPhases[0];var stats=actor.Equipment.CurrentWeaponStats;
                    var pattern=phase.ResolvePattern(stats.range,stats.meleeSlashAngle,actor.Equipment.CurrentWeaponData.GetMeleeDefinition().baseSettings.hitWidth);
                    Vector3 basis=entry+forward*plan.Position(plan.Start+DashHeavyFocusClock.RealAt(.5f)/speed);
                    for(int index=0;index<3;index++)
                    {
                        int captured=index;
                        var target=new GameObject("Owned dash heavy target "+index);targets.Add(target);fixtures.Add(target);
                        target.transform.position=basis+forward*pattern.Range*(.15f+.30f*index);
                        var combat=target.AddComponent<CombatTarget>();target.GetComponent<CombatAffiliation>().Configure(CombatTeam.Enemy);
                        var health=target.GetComponent<CombatHealth>();health.SetMaxHp(1000000,true);
                        health.OnDamaged+=(h,d)=>{packets.Add(new{target=captured,kind=d.playerAttackKind,d.triggersOnHitEffects,h.CurrentHp});if(d.triggersOnHitEffects&&(d.playerAttackKind&PlayerAttackKind.Heavy)!=0){hits.Add(captured);hitTimes.Add(Time.time);}};
                    }
                    // Capture targets before the handoff can start the phase.
                    while(evade.IsEvading)yield return null;
                    yield return Frames(1);
                    Check(melee.IsHeavyAttackInProgress&&melee.ActiveDodgeFollowUp==PlayerDodgeFollowUpKind.Heavy,
                        "준비 재시작 없는 E 인계 "+element+"/"+clickAt+" grounded="+movement.IsGrounded
                        +" windup="+melee.IsDashHeavyWindupActive+" condition="+actor.GetComponent<PlayerStateCoordinator>().CurrentCondition
                        +" completed="+evade.LastEndWasCompleted+" pending="+input.CombatInputs.PendingDodgeFollowUp);
                    active=Field<MeleeComboStepData>(melee,"activeAttackStep");
                    Check(active.animationClip==definition.dashHeavyAttackDefinition.attack.animationClip,"실제 E 클립");
                    Check(Vector3.Dot(Field<Vector3>(melee,"activeAttackDirection"),forward)>.999f,"대시 방향으로 판정 인계");
                    int action=Field<int>(melee,"activeActionId");bool commit=false;int commits=0;float limit=Time.unscaledTime+8f;
                    var trace=new List<object>();
                    float previous=Vector3.Dot(actor.transform.position-entry,forward);bool gatherSeen=false,releaseSeen=false;
                    int captureStage=0;
                    while(melee.IsAttackInProgress)
                    {
                        trace.Add(new{frame=Time.frameCount,scaled=Time.time,unscaled=Time.unscaledTime,delta=Time.deltaTime,
                            unscaledDelta=Time.unscaledDeltaTime,id=Field<int>(melee,"activeActionId"),kind=melee.ActiveDodgeFollowUp,
                            context=actor.Equipment.WeaponContextRevision,gem=actor.Equipment.GemRevision,
                            position=DashHeavyDiagnosticVector(actor.transform.position),direction=DashHeavyDiagnosticVector(Field<Vector3>(melee,"activeAttackDirection")),progress=active.playbackAcceleration.ToClipProgress((Time.time-Field<float>(melee,"attackStartTime"))/Field<float>(melee,"attackDuration"))});
                        if(Field<int>(melee,"activeActionId")!=action)
                            File.WriteAllText(Path.Combine(output,"InterruptedCase.json"),JsonConvert.SerializeObject(new{element,clickAt,action,
                                terminal=Field<WeaponActionState>(melee,"terminalActionState"),trace},Formatting.Indented));
                        Check(Time.unscaledTime<limit&&Field<int>(melee,"activeActionId")==action,
                            "단일 강공 행동 ID 유지 "+element+"/"+clickAt+" actual="+Field<int>(melee,"activeActionId")
                            +" reason="+Field<WeaponActionState>(melee,"terminalActionState").CompletionReason);
                        float now=Vector3.Dot(actor.transform.position-entry,forward);
                        Check(now>=previous-.025f&&now<=5.15f,"실제 인계·착지 이동 연속/5m "+now);previous=now;
                        bool value=Field<bool>(melee,"heavyDischargeCommitted");if(value&&!commit)commits++;commit|=value;
                        gatherSeen|=Field<bool>(melee,"dashHeavyGatherPlayed");releaseSeen|=Field<bool>(melee,"dashHeavyReleasePlayed");
                        if(capture)
                        {
                            float elapsed=Time.time-Field<float>(melee,"attackStartTime");
                            float source=DashHeavyFocusClock.Sample(elapsed*speed);
                            float[] points={.36f,26f/60f,.49f,.60f};
                            if(captureStage<points.Length&&source>=points[captureStage])
                            {
                                ScreenCapture.CaptureScreenshot(Path.Combine(output,"E_Fire_"+captureStage+".png"));captureStage++;
                            }
                        }
                        yield return null;
                    }
                    Check(commits==1&&energy.Amount<.001f,"원소 에너지 전량 1회 소비");
                    File.WriteAllText(Path.Combine(output,"Case_"+element+"_"+clickAt.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+".json"),JsonConvert.SerializeObject(new{
                        element,clickAt,entry=DashHeavyDiagnosticVector(entry),forward=DashHeavyDiagnosticVector(forward),basis=DashHeavyDiagnosticVector(basis),speed,pattern.Range,pattern.Width,hits,packets,targets=targets.Select(t=>new{position=DashHeavyDiagnosticVector(t.transform.position),hp=t.GetComponent<CombatHealth>().CurrentHp,team=t.GetComponent<CombatTarget>().Team}).ToArray(),trace},Formatting.Indented));
                    Check(hits.Count==3&&hits.SequenceEqual(new[]{0,1,2})&&hitTimes[2]>hitTimes[0],"가까운 적부터 대상당 1회 순차 피해 "+string.Join(",",hits));
                    Check(gatherSeen&&releaseSeen,"준비/해방 소리 이벤트 1회 진행");
                    Check(Mathf.Abs(Vector3.Dot(actor.transform.position-entry,forward)-5f)<.15f,"실제 총5m 착지·슬라이드 완료");
                    yield return Frames(2);
                    Check(actor.Equipment.CurrentWeaponRoot.GetComponentInChildren<DashHeavyFocusPresentation>(true)?.enabled==false,"공격 후 집중 자원 반환");
                    samples.Add(new{element,clickAt,hits,hitTimes,travel=Vector3.Dot(actor.transform.position-entry,forward)});
                    if(capture)
                    {
                        poseProbe.recordPoses=false;
                        File.WriteAllText(Path.Combine(output,"E_ActualPoses.json"),JsonConvert.SerializeObject(poseProbe.poseSamples,Formatting.Indented));
                    }
                }
                finally{foreach(var t in targets){fixtures.Remove(t);UnityEngine.Object.DestroyImmediate(t);}}
                Progress("E "+element+"/"+clickAt);
            }
            foreach(string cancel in new[]{"ui","weapon","gem","knockdown","disable"})
            {
                yield return Reset();EquipDashHeavyGem(WeaponElement.Fire);
                yield return StartDodge(false,false,true);Send();yield return Frames(2);
                float focusLimit=Time.unscaledTime+4f;
                while(Time.timeScale>.8f){Check(Time.unscaledTime<focusLimit&&melee.IsDashHeavyWindupActive,"활성 집중 취소 구간 도달");yield return null;}
                Check(melee.IsDashHeavyWindupActive,"취소 전 강공 준비 "+cancel);
                if(cancel=="ui")GameplayInputBlocker.Block(blocker);
                else if(cancel=="weapon")actor.Equipment.EquipWeaponItem(new ItemData(weaponItem.baseData,1,ItemGrade.Common));
                else if(cancel=="gem")EquipDashHeavyGem(WeaponElement.Ice);
                else if(cancel=="knockdown")evade.CancelForKnockdown();else evade.enabled=false;
                yield return Wait(.7f);
                Check(!melee.IsDashHeavyWindupActive&&!melee.IsAttackInProgress,"준비 취소·공격 누출 없음 "+cancel);
                Check(!OverburstTimeEffectArbiter.IsActive&&Mathf.Approximately(Time.timeScale,1f),"준비 취소 후 시간 반환 "+cancel);
                var focus=actor.Equipment.CurrentWeaponRoot.GetComponentInChildren<DashHeavyFocusPresentation>(true);
                Check(focus!=null&&!focus.enabled&&!focus.GetComponentsInChildren<Renderer>().Any(r=>r.enabled),"준비 취소 후 소유 효과 숨김 "+cancel);
                var camera=QuarterViewCamera.ActiveInstance;
                if(camera!=null)Check((float)typeof(QuarterViewCamera).GetMethod("CurrentHeavyFocusZoom",Private).Invoke(camera,null)<.0001f,"준비 취소 후 확대 반환 "+cancel);
                GameplayInputBlocker.Unblock(blocker);evade.enabled=true;
            }
            yield return Reset();EquipDashHeavyGem(WeaponElement.Fire);FillEnergy(energy,50000);
            float parryEnergy=energy.Amount;
            yield return StartDodge(false,false,true);Send();while(evade.IsEvading)yield return null;yield return Frames(1);
            Check(actor.GetComponent<PlayerParryController>().IsWindowOpen,"E 인계 패링 창");
            melee.NotifyHeavyParried(Field<int>(melee,"activeActionId"));
            Check(melee.IsHeavyParryMotionActive&&melee.ActiveDodgeFollowUp==PlayerDodgeFollowUpKind.None,"패링 성공 강화 강공으로 인계");
            yield return Frames(1);
            Check(actor.Equipment.CurrentWeaponRoot.GetComponentInChildren<DashHeavyFocusPresentation>(true)?.enabled==false,"패링 E 집중 자원 반환");
            float parryLimit=Time.unscaledTime+10;while(melee.IsAttackInProgress){Check(Time.unscaledTime<parryLimit,"패링 동작 완주");yield return null;}
            Check(Mathf.Abs(energy.Amount-Mathf.Min(parryEnergy,energy.BaseMaximum)*.5f)<.01f,"패링 기존 50% 환급");
        }
        finally
        {
            Time.captureFramerate=previousCaptureRate;
            melee.CancelCurrentAction();
            typeof(PlayerEquipment).GetMethod("SetElementGem",Private).Invoke(actor.Equipment,new object[]{previousGem});
        }
    }
    static float[] DashHeavyDiagnosticVector(Vector3 value) => new[]{value.x,value.y,value.z};
}
