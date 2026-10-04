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
    const string SwordFacingVerificationKey = "Overburst.PlayerEvadeVerifier.SwordFacing";
    const string FacingQueueKey = "Overburst.PlayerEvadeVerifier.FacingQueue";
    static double facingQueueIdle;
    static SwordFacingPoseProbe facingProbe;

    public static string QueueSwordFacingIsolated(string directory)
    {
        directory = IsolatedSavePlayGuard.ValidateDirectory(directory);
        string pending = SessionState.GetString(FacingQueueKey, "");
        if (!string.IsNullOrEmpty(pending) && pending != directory) throw new InvalidOperationException("Facing verification already queued.");
        Directory.CreateDirectory(directory);
        SessionState.SetString(FacingQueueKey, directory);
        SessionState.SetFloat(FacingQueueKey + ".Deadline", (float)EditorApplication.timeSinceStartup + 900);
        File.WriteAllText(Path.Combine(directory,"QueueReceipt.json"), "{\"status\":\"WAITING_FOR_IDLE\"}");
        ResumeFacingQueue(); return "QUEUED";
    }
    static void ResumeFacingQueue()
    {
        if (string.IsNullOrEmpty(SessionState.GetString(FacingQueueKey,""))) return;
        facingQueueIdle = 0; EditorApplication.update -= StartFacingWhenIdle; EditorApplication.update += StartFacingWhenIdle;
    }
    static void StartFacingWhenIdle()
    {
        string directory = SessionState.GetString(FacingQueueKey,"");
        if (string.IsNullOrEmpty(directory)) {EditorApplication.update -= StartFacingWhenIdle;return;}
        bool busy = EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(SessionState.GetString(ReturnKey,""));
        bool expired = EditorApplication.timeSinceStartup >= SessionState.GetFloat(FacingQueueKey+".Deadline",0);
        if (busy && !expired) {facingQueueIdle=0;return;}
        if (!expired && facingQueueIdle==0) {facingQueueIdle=EditorApplication.timeSinceStartup;return;}
        if (!expired && EditorApplication.timeSinceStartup-facingQueueIdle<.5) return;
        EditorApplication.update -= StartFacingWhenIdle; SessionState.EraseString(FacingQueueKey); SessionState.EraseFloat(FacingQueueKey+".Deadline");
        try
        {
            if (expired) throw new TimeoutException("Idle queue expired; no Play/account mutation.");
            StartSwordFacingIsolated(directory);
            File.WriteAllText(Path.Combine(directory,"QueueReceipt.json"),"{\"status\":\"STARTED\"}");
        }
        catch(Exception e) {File.WriteAllText(Path.Combine(directory,"QueueReceipt.json"),JsonConvert.SerializeObject(new {status="FAIL",error=e.ToString()}));}
    }

    public static void StartSwordFacingIsolated(string directory)
    {
        SessionState.SetBool(SwordFacingVerificationKey, true);
        try { StartIsolated(directory); }
        catch { SessionState.EraseBool(SwordFacingVerificationKey); throw; }
    }
    static IEnumerator FacingReset()
    {
        facingProbe.phase = "reset";
        yield return Reset();
        var facing = actor.GetComponent<PlayerCombatFacingController>();
        facing.enabled = false; facing.enabled = true; // fixture teleport: release only this component's prior visual state.
        yield return WaitForSwordIdle(actor.Equipment.CurrentWeaponData.GetMeleeDefinition().animationProfile.combatIdleClip);
        yield return FacingWait(.25f);
        facingProbe.phase = "ready";
    }
    static void FacingAim(float angle) { testFacing = Quaternion.Euler(0, angle, 0) * forward; }
    static IEnumerator FacingWait(float seconds)
    {
        float until = Time.time + seconds, timeout = Time.unscaledTime + seconds * 5 + 10;
        while (Time.time < until && Time.unscaledTime < timeout) yield return null;
        Check(Time.time >= until, "제품 애니메이션 시계 진행 " + seconds);
    }
    static IEnumerator FacingSample(float seconds, string phase)
    {
        facingProbe.phase = phase;
        yield return FacingWait(seconds);
        Check(facingProbe.frames.Any(f => f.phase == phase), phase + " 실제 포즈 프레임 관측");
        Check(facingProbe.frames.Where(f => f.phase == phase).All(f => f.leftWeight == 0 && f.rightWeight == 0),
            phase + " 발 IK 0");
    }
    static IEnumerator VerifySwordFacingGameplay()
    {
        if (SessionState.GetBool(SwordStopVerificationKey, false))
        {
            SessionState.EraseBool(SwordStopVerificationKey);
            yield return VerifySwordStopGameplay();
            yield break;
        }
        Check(EnemyThemeTrialService.InArena, "턴 검증 실제 시험장 진입");
        var driver = actor.GetComponent<MeleeWeaponCombatAnimatorDriver>();
        var facing = actor.GetComponent<PlayerCombatFacingController>();
        var set = actor.Equipment.CurrentWeaponData.GetMeleeDefinition().animationProfile.combatLocomotionSet;
        Check(set != null && set.directions.Length == 8, "실제 Sword 턴/8방향 프로필");
        Check(facing != null && animator.GetComponent<PlayerCombatAimPose>() != null, "제품 방향/상체 포즈 바인딩");
        facingProbe = blocker.AddComponent<SwordFacingPoseProbe>();
        facingProbe.Bind(animator, facing);
        facingProbe.StartCapture(Path.Combine(output, "Capture"));
        try
        {
            yield return FacingReset();
            float b = facing.LowerYaw;
            var feet = facingProbe.FootPositions();
            foreach (float angle in new[] { 10f, 30f, 45f, -30f, 0f })
            {
                FacingAim(angle);
                string label = "small_" + angle;
                yield return FacingSample(.35f, label);
                Check(Mathf.Abs(Mathf.DeltaAngle(b, facing.LowerYaw)) < .2f, label + " 하체 유지");
                var rows = facingProbe.frames.Where(f => f.phase == label).ToArray();
                Check(rows.Max(f => f.upperError) < 2.5f, label + " 실시간 상체 오차 <2.5도 최대=" + rows.Max(f => f.upperError));
                Check(facingProbe.FootPositions().Zip(feet, (a,c) => new Vector2(a.x-c.x,a.z-c.z).magnitude).Max() < .015f,
                    label + " 작은 조준 발 드리프트 <15mm");
            }
            foreach (float angle in new[] { 75f, -75f, 178f, -178f })
            {
                yield return FacingReset();
                b = facing.LowerYaw;
                FacingAim(angle);
                var expected = Math.Abs(angle) > 135 ? (angle < 0 ? set.left180 : set.right180) : (angle < 0 ? set.left90 : set.right90);
                string label = "turn_" + angle;
                yield return FacingSample(expected.Duration + .45f, label);
                Check(facingProbe.frames.Any(f => f.phase == label && f.clips.Contains(expected.clip.name)), label + " 실제 전용 클립");
                Check(Mathf.Abs(Mathf.DeltaAngle(b + expected.angle, facing.LowerYaw)) < .5f, label + " 회전 곡선 한 번 소비");
                var normalTwist = facingProbe.frames.Where(f => f.phase == label && f.poseActive && Math.Abs(f.delta) <= set.maximumUpperTwist - 1).ToArray();
                Check(normalTwist.Length > 0 && normalTwist.Max(f => f.upperError) < 2.5f,
                    label + " 허용 각도 상체 에임 유지 오차=" + (normalTwist.Length>0?normalTwist.Max(f=>f.upperError):-1));
            }
            yield return FacingReset();
            FacingAim(80); yield return FacingWait(.25f);
            var outgoing = driver.ActiveFacingTurn;
            Check(outgoing != null, "역방향 시험 턴 시작");
            FacingAim(-75);
            yield return FacingSample(.12f, "reverse_mid_turn");
            Check(driver.ActiveFacingTurn == outgoing, "발 공중 단계 역입력에서 기존 디딤 유지");
            yield return FacingSample(2.8f, "reverse_settle");
            Check(Math.Abs(Mathf.DeltaAngle(facing.LowerYaw, facing.AimYaw)) < set.turnThreshold, "역입력 후 안정 각도");

            yield return FacingReset(); FacingAim(80); yield return FacingWait(.25f);
            Check(driver.ActiveFacingTurn != null, "공격 연결 시험 턴 시작");
            Send(false, false, true);
            yield return FacingSample(.2f, "turn_attack");
            Send();
            Check(melee.IsAttackInProgress && driver.ActiveFacingTurn == null, "실제 클릭 공격이 턴에 즉시 우선");
            Check(!facing.IsPoseActive, "공격 중 추가 상체 yaw 해제");
            facingProbe.phase = "attack_recovery";
            yield return FacingWait(3.2f);
            yield return FacingSample(.3f, "attack_idle_return");
            Check(SwordIdleClipVisible(actor.Equipment.CurrentWeaponData.GetMeleeDefinition().animationProfile.combatIdleClip),
                "공격 뒤 새 Idle 복귀");

            yield return FacingReset(); FacingAim(80); yield return FacingWait(.25f);
            yield return StartDodge(false);
            Check(driver.ActiveFacingTurn == null && !facing.IsPoseActive, "턴 중 회피 입력 우선");
            yield return FacingWait(1.1f);

            yield return FacingReset(); FacingAim(80); yield return FacingWait(.25f);
            Check(driver.ActiveFacingTurn != null, "이동 연결 시험 턴 시작");
            Vector3 moveOrigin = actor.transform.position;
            Send(true); yield return FacingSample(.15f, "turn_move_entry");
            Check(driver.ActiveFacingTurn == null && Vector3.Distance(actor.transform.position,moveOrigin) > .02f,
                "턴에서 이동 즉시 수락·턴 취소");
            yield return FacingSample(1f, "turn_move_run");
            Check(facingProbe.frames.Any(f => f.phase == "turn_move_run" && f.clips.Any(n=>n.StartsWith("Sword_Loop_"))),
                "턴에서 실제 Sword Run 연결");
            Send(); yield return FacingSample(1.6f, "stop_turn_entry");
            FacingAim(-75); yield return FacingSample(2.1f, "stop_turn_return");
            Check(facingProbe.frames.Any(f => f.phase == "stop_turn_return" && f.clips.Any(n=>n.StartsWith("Sword_Turn_"))),
                "Stop 이후 새 조준에 실제 턴 연결");

            yield return FacingReset(); FacingAim(178); yield return FacingWait(.3f);
            Check(driver.ActiveFacingTurn != null && Math.Abs(driver.ActiveFacingTurn.angle)==180,
                "180도 턴 중 공격 시험 준비");
            Send(false,false,true); yield return FacingSample(.2f,"turn_180_attack"); Send();
            Check(melee.IsAttackInProgress && driver.ActiveFacingTurn == null,"180도 턴에서 공격 즉시 수락");
            facingProbe.phase = "attack_180_recovery"; yield return FacingWait(3.2f);
            Check(SwordIdleClipVisible(actor.Equipment.CurrentWeaponData.GetMeleeDefinition().animationProfile.combatIdleClip),
                "180도 턴 공격 뒤 Idle 복귀");

            for (int i = 0; i < 8; i++)
            {
                yield return FacingReset();
                FacingAim(-i * 45); yield return FacingWait(1.7f);
                // Settled aim can leave +/-30 degrees after a 90 turn. Move first then read its
                // real visual direction; no motor or attack eligibility is overridden.
                Send(true);
                yield return FacingSample(1.05f, "move_" + i);
                var rows = facingProbe.frames.Where(f => f.phase == "move_" + i).ToArray();
                Check(rows.Any(f => f.clips.Contains(set.directions[i].start.name)), "방향 " + i + " 실제 Start");
                Check(rows.Any(f => f.clips.Contains(set.directions[i].loop.name)), "방향 " + i + " 실제 Run");
                Check(rows.All(f => !f.clips.Any(n => n.StartsWith("Greatsword_Run"))), "방향 " + i + " 이전 이동 클립 없음");
                Send();
                yield return FacingSample(set.directions[i].stop.length + set.moveBlendSeconds + .3f, "stop_" + i);
                Check(facingProbe.frames.Any(f => f.phase == "stop_" + i && f.clips.Contains(set.directions[i].stop.name)),
                    "방향 " + i + " 실제 Stop");
                Check(SwordIdleClipVisible(actor.Equipment.CurrentWeaponData.GetMeleeDefinition().animationProfile.combatIdleClip),
                    "방향 " + i + " Stop 뒤 Idle");
            }
            yield return FacingReset();
            facingProbe.phase = "continuous_360";
            for (int i = 0; i <= 18; i++) { FacingAim(i * 20); yield return FacingWait(.12f); }
            yield return FacingSample(2.4f, "continuous_settle");
            Check(Math.Abs(Mathf.DeltaAngle(facing.LowerYaw, facing.AimYaw)) < set.turnThreshold, "연속 360도 최종 안정");
            var circular = facingProbe.frames.Where(f=>f.phase=="continuous_360" || f.phase=="continuous_settle").ToArray();
            Check(circular.Zip(circular.Skip(1),(a,b)=>new {dt=b.time-a.time,step=Math.Abs(Mathf.DeltaAngle(a.chestYaw,b.chestYaw))})
                .Where(x=>x.dt>0 && x.dt<.035f).All(x=>x.step<45f), "180도 경계 상체 단일 프레임 45도 초과 스냅 없음");
        }
        finally
        {
            if (facingProbe != null)
            {
                File.WriteAllText(Path.Combine(output, "FacingPoses.json"), JsonConvert.SerializeObject(facingProbe.frames, Formatting.None));
                UnityEngine.Object.DestroyImmediate(facingProbe);
                facingProbe = null;
            }
        }
        // Same real input/Animator regression for all eight already adapted attacks.
        yield return VerifySwordIdleGameplay();
    }
}

[DefaultExecutionOrder(950)]
public sealed class SwordFacingPoseProbe : MonoBehaviour
{
    [Serializable]
    public sealed class Frame
    {
        public int frame;
        public float time, delta, aim, lower, upperError, hipYaw, leftWeight, rightWeight;
        public bool poseActive;
        public string phase;
        public string[] clips;
        public float[][] feet;
        public float[] actor, model, hipsLocal;
        public float speed;
        public bool grounded, controllerEnabled;
        public float groundGap, verticalVelocity;
        public float chestYaw;
        public float[][] joints;
    }
    public string phase = "setup";
    public readonly List<Frame> frames = new List<Frame>();
    Animator animator;
    PlayerCombatFacingController facing;
    Transform[] feet;
    Transform chest, hips;
    Transform[] joints;
    Camera captureCamera;
    RenderTexture captureTarget;
    Texture2D capturePixels;
    string captureDirectory;
    float nextCapture;
    int captureIndex;
    readonly List<object> captures = new List<object>();
    PlayerFootLock footLock;
    object left, right;
    System.Reflection.FieldInfo weight;
    readonly List<AnimatorClipInfo> current = new List<AnimatorClipInfo>(), next = new List<AnimatorClipInfo>();
    public void Bind(Animator a, PlayerCombatFacingController f)
    {
        animator = a; facing = f;
        feet = new[] { a.GetBoneTransform(HumanBodyBones.LeftFoot), a.GetBoneTransform(HumanBodyBones.RightFoot) };
        chest = a.GetBoneTransform(HumanBodyBones.UpperChest) ?? a.GetBoneTransform(HumanBodyBones.Chest);
        hips = a.GetBoneTransform(HumanBodyBones.Hips);
        joints = new[] {HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot}
            .Select(a.GetBoneTransform).ToArray();
        footLock = a.GetComponent<PlayerFootLock>();
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        left = typeof(PlayerFootLock).GetField("leftFoot", flags).GetValue(footLock);
        right = typeof(PlayerFootLock).GetField("rightFoot", flags).GetValue(footLock);
        weight = left.GetType().GetField("Weight");
    }
    public Vector3[] FootPositions() => feet.Select(f => f.position).ToArray();
    public void StartCapture(string directory)
    {
        captureDirectory = directory; Directory.CreateDirectory(directory);
        var cameraObject = new GameObject("Owned Sword facing capture camera");
        cameraObject.transform.SetParent(transform, false);
        captureCamera = cameraObject.AddComponent<Camera>();
        captureCamera.CopyFrom(Camera.main); captureCamera.enabled = false;
        captureCamera.orthographic = true; captureCamera.orthographicSize = 1.65f;
        captureCamera.clearFlags = CameraClearFlags.SolidColor; captureCamera.backgroundColor = new Color(.13f,.16f,.19f);
        int ui = LayerMask.NameToLayer("UI"); if (ui >= 0) captureCamera.cullingMask &= ~(1 << ui);
        captureTarget = new RenderTexture(640,640,24,RenderTextureFormat.ARGB32); captureTarget.Create();
        capturePixels = new Texture2D(640,640,TextureFormat.RGB24,false);
        captureCamera.targetTexture = captureTarget;
    }
    void Capture()
    {
        if (captureCamera == null || Time.unscaledTime < nextCapture
            || !(phase.StartsWith("small_") || phase.StartsWith("turn_") || phase.StartsWith("attack_") || phase.StartsWith("move_") || phase.StartsWith("stop_"))) return;
        nextCapture = Time.unscaledTime + .05f;
        Vector3 target = facing.transform.position + Vector3.up * 1.05f;
        captureCamera.transform.position = target + new Vector3(3.4f,2.2f,-4.4f);
        captureCamera.transform.LookAt(target);
        var previous = RenderTexture.active;
        try
        {
            captureCamera.Render(); RenderTexture.active = captureTarget;
            capturePixels.ReadPixels(new Rect(0,0,640,640),0,0); capturePixels.Apply();
            string name = captureIndex++.ToString("D5") + ".png";
            File.WriteAllBytes(Path.Combine(captureDirectory,name),capturePixels.EncodeToPNG());
            captures.Add(new {name,phase,time=Time.time,frame=Time.frameCount,aim=facing.AimYaw,lower=facing.LowerYaw});
        }
        finally {RenderTexture.active = previous;}
    }
    void OnDestroy()
    {
        if (captureDirectory != null) File.WriteAllText(Path.Combine(captureDirectory,"Frames.json"), JsonConvert.SerializeObject(captures));
        if (captureCamera != null) DestroyImmediate(captureCamera.gameObject);
        if (captureTarget != null) {captureTarget.Release();DestroyImmediate(captureTarget);}
        if (capturePixels != null) DestroyImmediate(capturePixels);
    }
    static float Yaw(Quaternion q) { Vector3 v = q * Vector3.forward; return Mathf.Atan2(v.x,v.z)*Mathf.Rad2Deg; }
    void LateUpdate()
    {
        if (animator == null || facing == null) return;
        int layer = animator.GetLayerIndex("Combat_MeleeWeapon");
        animator.GetCurrentAnimatorClipInfo(layer,current); animator.GetNextAnimatorClipInfo(layer,next);
        float target = facing.LowerYaw + (facing.Set?.idleChestYaw ?? 0) + facing.UpperDelta;
        var movement = facing.GetComponent<PlayerMovement>();
        var motor = facing.GetComponent<OverburstCharacterMotor3D>();
        Vector3 actorPosition = facing.transform.position, modelPosition = animator.transform.position;
        frames.Add(new Frame { frame = Time.frameCount, time = Time.time, phase = phase,
            aim = facing.AimYaw, lower = facing.LowerYaw, delta = Mathf.DeltaAngle(facing.LowerYaw, facing.AimYaw),
            upperError = Math.Abs(Mathf.DeltaAngle(Yaw(chest.rotation),target)),
            hipYaw = Yaw(hips.rotation), poseActive = facing.IsPoseActive,
            leftWeight = (float)weight.GetValue(left), rightWeight = (float)weight.GetValue(right),
            clips = current.Concat(next).Where(c=>c.weight>.001).Select(c=>c.clip.name).Distinct().ToArray(),
            feet = feet.Select(b=>new[]{b.position.x,b.position.y,b.position.z}).ToArray(),
            actor = new[]{actorPosition.x,actorPosition.y,actorPosition.z}, model = new[]{modelPosition.x,modelPosition.y,modelPosition.z},
            hipsLocal = new[]{animator.transform.InverseTransformPoint(hips.position).x, animator.transform.InverseTransformPoint(hips.position).y, animator.transform.InverseTransformPoint(hips.position).z},
            speed = movement.Locomotion.HorizontalVelocity.magnitude,
            grounded=movement.IsGrounded, controllerEnabled=facing.GetComponent<CharacterController>().enabled,
            groundGap=motor.GroundGap, verticalVelocity=movement.VerticalVelocity });
        frames[frames.Count-1].chestYaw = Yaw(chest.rotation);
        frames[frames.Count-1].joints = joints.Select(j => j != null ? new[]{j.localRotation.x,j.localRotation.y,j.localRotation.z,j.localRotation.w}:null).ToArray();
        Capture();
    }
}
