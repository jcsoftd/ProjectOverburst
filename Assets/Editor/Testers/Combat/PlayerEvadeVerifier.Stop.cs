using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

public static partial class PlayerEvadeVerifier
{
    const string SwordStopVerificationKey = "Overburst.PlayerEvadeVerifier.SwordStop";
    public static void StartSwordStopIsolated(string directory, bool expectInPlace = true, bool capture = false)
    {
        SessionState.SetBool(SwordStopVerificationKey, true);
        SessionState.SetBool(SwordStopVerificationKey + ".ExpectInPlace", expectInPlace);
        SessionState.SetBool(SwordStopVerificationKey + ".Capture", capture);
        try { StartSwordFacingIsolated(directory); }
        catch { SessionState.EraseBool(SwordStopVerificationKey); SessionState.EraseBool(SwordStopVerificationKey + ".ExpectInPlace"); SessionState.EraseBool(SwordStopVerificationKey + ".Capture"); throw; }
    }
    static float PlanarDistance(float[] a, float[] b) => new Vector2(a[0]-b[0],a[2]-b[2]).magnitude;
    static IEnumerator VerifySwordStopGameplay()
    {
        if (SessionState.GetBool(StopCurveComparisonKey, false))
        {
            SessionState.EraseBool(StopCurveComparisonKey);
            yield return VerifySwordStopCurveComparison();
            yield break;
        }
        bool expectInPlace = SessionState.GetBool(SwordStopVerificationKey + ".ExpectInPlace",true);
        SessionState.EraseBool(SwordStopVerificationKey + ".ExpectInPlace");
        var facing=actor.GetComponent<PlayerCombatFacingController>();
        var driver=actor.GetComponent<MeleeWeaponCombatAnimatorDriver>();
        var set=actor.Equipment.CurrentWeaponData.GetMeleeDefinition().animationProfile.combatLocomotionSet;
        Check(set != null && set.directions.Length == 8,"Stop 실제 8방향 프로필");
        facingProbe=blocker.AddComponent<SwordFacingPoseProbe>(); facingProbe.Bind(animator,facing);
        try {
            if (SessionState.GetBool(SwordStopVerificationKey + ".Capture", false))
                facingProbe.StartCapture(Path.Combine(output,"Capture"));
            SessionState.EraseBool(SwordStopVerificationKey + ".Capture");
            for(int i=0;i<8;i++) {
                yield return FacingReset(); FacingAim(-i*45); yield return FacingWait(1.7f);
                Send(true); yield return FacingSample(1.05f,"move_"+i);
                var run=facingProbe.frames.Last();
                Send(); yield return FacingSample(set.directions[i].stop.length + .35f,"stop_"+i);
                var rows=facingProbe.frames.Where(f=>f.phase=="stop_"+i).ToArray();
                var stop=rows.Where(f=>f.clips.Contains(set.directions[i].stop.name)).ToArray();
                Check(stop.Length>0,"방향 "+i+" Stop 전용 클립");
                Check(SwordIdleClipVisible(actor.Equipment.CurrentWeaponData.GetMeleeDefinition().animationProfile.combatIdleClip),"방향 "+i+" Stop -> Idle");
                var end=rows.Last();
                float coast=PlanarDistance(end.actor,run.actor);
                float lateOffset=stop.Take(Math.Max(1,stop.Length-2)).Reverse().Take(3).Max(f=>PlanarDistance(f.hipsLocal,end.hipsLocal));
                float maxStep=rows.Prepend(run).Zip(rows,(a,b)=>PlanarDistance(a.actor,b.actor)).Max();
                samples.Add(new {direction=i,coast,lateOffset,maxStep}); Progress("stop_"+i);
                Check(rows.All(f=>PlanarDistance(f.model,f.actor)<.002f),"방향 "+i+" 모델 원점 이탈 없음");
                bool curve = set.directions[i].stopDistance != null && set.directions[i].stopDistance.length > 1;
                if (curve) {
                    Check(set.directions[i].stopMatchEntrySpeed == (i == 0 || i == 2 || i == 6), "방향 " + i + " 승인 C/B 정책");
                    Check(rows.Any(f => f.stopActive && f.stopSector == i), "방향 " + i + " 제품 곡선 실제 소비");
                    Check(Mathf.Abs(coast-end.stopDistance)<.003f, "방향 " + i + " 제품 곡선과 실제 이동 3mm 이내 " + coast);
                    if (!set.directions[i].stopMatchEntrySpeed)
                        Check(Mathf.Abs(coast-(set.directions[i].stopDistance.Evaluate(set.directions[i].stop.length)
                            - set.directions[i].stopDistance.Evaluate(end.stopStartTime))
                            * animator.humanScale / set.directions[i].stopSourceHumanScale)<.003f,
                            "방향 " + i + " B 동일 모션 구간 이동량 소비");
                    else Check(rows.Zip(rows.Skip(1),(a,b)=>b.stopDistance>=a.stopDistance-.00001f).All(x=>x),
                        "방향 " + i + " C 끝 디딤 역밀림 방지");
                    var timed=rows.Where(f=>f.stopActive && f.stopSector==i && f.clips.Contains(set.directions[i].stop.name)).ToArray();
                    Check(timed.All(f=>Mathf.Abs(f.stopNormalized-(f.stopStartTime+f.stopElapsed*f.stopRate)/set.directions[i].stop.length)<.06f),
                        "방향 " + i + " 모션/곡선 시계 오차 0.06 이내");
                    Check(Mathf.Abs(coast-end.stopTargetDistance)<.004f, "방향 " + i + " 축소 목표 거리 4mm 이내");
                    Check(Mathf.Abs(end.stopMomentum-1f)<.001f,"방향 "+i+" 긴 이동 최대 관성");
                } else {
                    Check(coast<.27f,"방향 "+i+" 작은 감속 이동 상한 "+coast);
                    Check(rows.Zip(rows.Skip(1),(a,b)=>b.speed<=a.speed+.02f).All(x=>x),"방향 "+i+" 정지 속도 단조 감소");
                }
                Check(end.speed<.001f,"방향 "+i+" 최종 모터 속도 0");
                if(expectInPlace) {
                    Check(coast>.08f,"방향 "+i+" 짧은 감속 이동 관측 "+coast);
                    Check(lateOffset<.025f,"방향 "+i+" Stop -> Idle 골반 이탈 25mm 미만 "+lateOffset);
                }
            }
            if (set.scaleStopWithMoveDuration) {
                for (int i=0;i<8;i++) {
                    yield return FacingReset(); FacingAim(-i*45); yield return FacingWait(1.7f);
                    Send(true); yield return FacingSample(.12f,"short_move_"+i);
                    var release=facingProbe.frames.Last();
                    Send(); yield return FacingSample(set.directions[i].stop.length+.35f,"short_stop_"+i);
                    var rows=facingProbe.frames.Where(f=>f.phase=="short_stop_"+i).ToArray(); var end=rows.Last();
                    float coast=PlanarDistance(release.actor,end.actor);
                    var longEnd=facingProbe.frames.Last(f=>f.phase=="stop_"+i);
                    Check(rows.Any(f=>f.stopActive),"짧은 이동 "+i+" 실제 Stop 진입");
                    Check(coast<longEnd.stopDistance*.55f,"짧은 이동 "+i+" 긴 이동보다 짧은 관성 "+coast);
                    Check(Mathf.Abs(coast-end.stopTargetDistance)<.004f,"짧은 이동 "+i+" 목표와 실제 이동 일치");
                    Check(Mathf.Abs(end.stopMomentum-set.shortStopTravelMultiplier)<.01f,"짧은 이동 "+i+" 최소 관성 배율");
                    Check(end.speed<.001f && !end.stopActive,"짧은 이동 "+i+" 잔류 속도 없음");
                    var timed=rows.Where(f=>f.stopActive && f.clips.Contains(set.directions[i].stop.name)).ToArray();
                    Check(timed.All(f=>Mathf.Abs(f.stopNormalized-(f.stopStartTime+f.stopElapsed*f.stopRate)/set.directions[i].stop.length)<.06f),
                        "짧은 이동 "+i+" 모션과 곡선 구간 일치");
                    samples.Add(new{kind="short",direction=i,coast,end.stopStartTime,end.stopMomentum,end.stopTargetDistance});
                }
                foreach(int i in new[]{2,4,6}) {
                    yield return FacingReset(); FacingAim(-i*45); yield return FacingWait(1.7f);
                    Send(true); yield return FacingSample(.45f,"medium_move_"+i); var release=facingProbe.frames.Last();
                    Send(); yield return FacingSample(set.directions[i].stop.length+.35f,"medium_stop_"+i);
                    var end=facingProbe.frames.Last(); float coast=PlanarDistance(release.actor,end.actor);
                    Check(end.stopMomentum>set.shortStopTravelMultiplier && end.stopMomentum<1f,"중간 이동 "+i+" 부드러운 관성 보간");
                    Check(Mathf.Abs(coast-end.stopTargetDistance)<.004f && end.speed<.001f,"중간 이동 "+i+" 거리와 잔류 속도");
                    samples.Add(new{kind="medium",direction=i,coast,end.stopStartTime,end.stopMomentum,end.stopTargetDistance});
                }
            }
            if(expectInPlace) {
                yield return FacingReset(); facingProbe.phase="move_restart_entry"; Send(true); yield return FacingWait(.9f);
                facingProbe.phase="stop_restart_entry"; Send();yield return FacingWait(.04f);
                Send(true);yield return FacingSample(.35f,"move_restart");
                Check(movement.MoveInput.sqrMagnitude>.5f && movement.Locomotion.HorizontalVelocity.magnitude>1 && !movement.IsCombatStopCurveActive,"Stop 중 재이동 즉시 수락·곡선 취소");
                yield return FacingReset(); Send(true);yield return FacingWait(.9f);
                Vector3 priorDirection=movement.Locomotion.HorizontalVelocity.normalized;
                Send(); yield return FacingWait(.04f);
                Send(true); heldMoveKey=UnityEngine.InputSystem.Key.S;
                facingProbe.phase="reverse_restart"; yield return Frames(2);
                Check(!movement.IsCombatStopCurveActive && Vector3.Dot(movement.Locomotion.HorizontalVelocity,priorDirection)<0f,
                    "Stop 중 반대 입력 첫 두 프레임에서 이전 관성 속도 폐기");
                yield return FacingSample(.35f,"reverse_move");
                Send();yield return FacingWait(1.3f);
                facingProbe.phase="move_attack_entry"; Send(true);yield return FacingWait(.9f);
                facingProbe.phase="stop_attack_entry"; Send();yield return FacingWait(.04f);
                Send(false,false,true);yield return FacingSample(.18f,"stop_attack");Send();
                Check(melee.IsAttackInProgress && !movement.IsCombatStopCurveActive,"Stop 중 공격 수락·곡선 취소"); facingProbe.phase="attack_stop_recovery"; yield return FacingWait(3.3f);
                yield return FacingReset(); facingProbe.phase="move_evade_entry"; Send(true);yield return FacingWait(.9f);
                facingProbe.phase="stop_evade_entry"; Send();yield return FacingWait(.04f);
                facingProbe.phase="stop_evade";
                yield return StartDodge(false);Check(evade.IsEvading && !movement.IsCombatStopCurveActive,"Stop 중 회피 수락·곡선 취소");yield return FacingWait(1.2f);
                yield return FacingReset();FacingAim(75);yield return FacingSample(1.2f,"turn_stop_regression");
                Check(facingProbe.frames.Any(f=>f.phase=="turn_stop_regression" && f.clips.Contains(set.right90.clip.name)),"Stop 수정 후 기존 90도 턴 유지");
                yield return FacingReset(); Send(true); yield return FacingWait(.9f);
                Vector3 releasePosition=actor.transform.position;
                Vector3 releaseDirection=movement.Locomotion.HorizontalVelocity.normalized;
                Send(); yield return FacingWait(.08f); FacingAim(100);
                yield return FacingSample(.15f,"stop_aim_change");
                Vector3 aimCoast=actor.transform.position-releasePosition; aimCoast.y=0;
                Check(Vector3.Cross(aimCoast,releaseDirection).magnitude<.003f,
                    "Stop 중 마우스 에임 변경에도 마지막 이동 방향 유지");
                yield return FacingWait(1.5f);

                yield return FacingReset(); Send(true); yield return FacingWait(.9f);
                releasePosition=actor.transform.position;
                releaseDirection=movement.Locomotion.HorizontalVelocity.normalized;
                var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name="Owned Stop curve collision fixture"; wall.transform.SetParent(blocker.transform);
                wall.transform.position=releasePosition+releaseDirection*.68f+Vector3.up;
                wall.transform.rotation=Quaternion.LookRotation(releaseDirection);
                wall.transform.localScale=new Vector3(4,3,.1f); Physics.SyncTransforms();
                try {
                    Send(); yield return FacingSample(1.5f,"stop_collision");
                    float blockedCoast=Vector3.Dot(actor.transform.position-releasePosition,releaseDirection);
                    Check(blockedCoast<.43f && blockedCoast>=0,"곡선 Stop 실제 벽 충돌 통과 방지 "+blockedCoast);
                    Check(!movement.IsCombatStopCurveActive && movement.Locomotion.HorizontalVelocity.magnitude<.001f,
                        "벽 충돌 Stop 종료 후 잔류 곡선/속도 없음");
                } finally { UnityEngine.Object.DestroyImmediate(wall); }
                Check(!animator.applyRootMotion,"공유 Animator 자동 루트모션 비활성 유지");
            }
            File.WriteAllText(Path.Combine(output,"StopSuiteCompleted.json"),"{\"status\":\"PASS\",\"directions\":8}");
        } finally {
            if(facingProbe!=null) {
                File.WriteAllText(Path.Combine(output,"FacingPoses.json"),JsonConvert.SerializeObject(facingProbe.frames,Formatting.None));
                UnityEngine.Object.DestroyImmediate(facingProbe);facingProbe=null;
            }
        }
    }
}
