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
        if (SessionState.GetBool(ElementShowcaseKey, false))
        {
            SessionState.EraseBool(ElementShowcaseKey);
            yield return CaptureElementShowcase();
            yield break;
        }
        if (SessionState.GetBool(ElementSupplementKey, false))
        {
            SessionState.EraseBool(ElementSupplementKey);
            yield return CaptureElementSupplement();
            yield break;
        }
        if (SessionState.GetBool(SwordPresentationKey, false))
        {
            SessionState.EraseBool(SwordPresentationKey);
            yield return CaptureSwordPresentation();
            yield break;
        }
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

            yield return FacingReset(); facingProbe.phase = "turn_attack_pre"; FacingAim(80); yield return FacingWait(.25f);
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

            yield return FacingReset(); facingProbe.phase = "turn_move_pre"; FacingAim(80); yield return FacingWait(.25f);
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

            yield return FacingReset(); facingProbe.phase = "turn_180_attack_pre"; FacingAim(178); yield return FacingWait(.3f);
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
            yield return ContinuousAimDemonstration();
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
        File.WriteAllText(Path.Combine(output,"FacingSuiteCompleted.json"),"{\"status\":\"PASS\",\"continuousAim\":true}");
    }
    static IEnumerator ContinuousAimDemonstration()
    {
        yield return FacingReset();
        facingProbe.phase = "aim_demo";
        float[] times = {0,1.3f,2.6f,4.8f,7.2f,9.6f,12,14,16,18};
        float[] angles = {0,35,-35,105,-115,115,-135,175,0,0};
        float start = Time.time, limit = Time.unscaledTime + 60;
        while(Time.time-start < times[times.Length-1] && Time.unscaledTime < limit)
        {
            float t = Time.time-start;
            int segment = 0;
            while(segment < times.Length-2 && t > times[segment+1])segment++;
            float progress = Mathf.Clamp01((t-times[segment])/(times[segment+1]-times[segment]));
            progress = progress*progress*(3-2*progress);
            FacingAim(Mathf.Lerp(angles[segment],angles[segment+1],progress));
            yield return null;
        }
        Check(Time.time-start >= 18,"끊김 없는 좌우 조준18초 완주");
        var rows=facingProbe.frames.Where(f=>f.phase=="aim_demo").ToArray();
        Check(rows.Length>=500,"연속 조준 실제500프레임 이상 촬영");
        Check(rows.All(f=>f.leftWeight==0 && f.rightWeight==0),"연속 조준 발 IK0");
        Check(rows.Any(f=>f.clips.Any(c=>c.StartsWith("Sword_Turn_"))),"연속 좌우 조준에서 실제 하체 디딤");
        Check(rows.Zip(rows.Skip(1),(a,c)=>Mathf.Abs(Mathf.DeltaAngle(a.chestYaw,c.chestYaw))).All(v=>v<45),
            "연속 조준 단일 프레임45도 초과 상체 스냅 없음");
    }
}

[DefaultExecutionOrder(10000)]
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
        public float speed, unscaled, gauge, attackProgress;
        public int comboStep, parrySuccess;
        public bool attacking, heavy, parrying, evading;
        public bool stopActive;
        public int stopSector;
        public float stopElapsed, stopDistance, stopRate, stopNormalized;
        public float stopStartTime, stopTargetDistance, stopMomentum;
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
    SwordCleanCaptureStage cleanStage;
    public bool presentationCamera;
    Camera captureCamera;
    RenderTexture captureTarget, footTarget;
    Texture2D capturePixels, footPixels;
    bool captureFeet;
    string captureDirectory;
    float nextCapture;
    int priorCaptureRate;
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
    public void StartCapture(string directory, bool fixedClock = true)
    {
        captureDirectory = directory; Directory.CreateDirectory(directory);
        priorCaptureRate = Time.captureFramerate; Time.captureFramerate = fixedClock ? 30 : 0;
        var cameraObject = new GameObject("Owned Sword facing capture camera");
        cameraObject.transform.SetParent(transform, false);
        captureCamera = cameraObject.AddComponent<Camera>();
        var sourceCamera = Camera.main;
        if (sourceCamera == null) throw new InvalidOperationException("The gameplay camera is unavailable.");
        captureCamera.CopyFrom(sourceCamera); captureCamera.enabled = false;
        cleanStage = new SwordCleanCaptureStage(animator, facing.transform);
        var equipment = facing.GetComponent<PlayerEquipment>();
        if (equipment != null && equipment.CurrentWeaponRoot != null) cleanStage.AddVisibleActor(equipment.CurrentWeaponRoot.transform);
        int height = Mathf.Max(2,Mathf.RoundToInt(1280f/sourceCamera.aspect/2f)*2);
        captureTarget = new RenderTexture(1280,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB); captureTarget.Create();
        capturePixels = new Texture2D(1280,height,TextureFormat.RGB24,false);
        captureFeet = fixedClock;
        if (captureFeet)
        {
            Directory.CreateDirectory(Path.Combine(directory,"Foot"));
            footTarget = new RenderTexture(1024,1024,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB); footTarget.Create();
            footPixels = new Texture2D(1024,1024,TextureFormat.RGB24,false);
        }
    }
    void CopyGameplayCamera(Camera source)
    {
        captureCamera.CopyFrom(source); captureCamera.enabled = false;
        captureCamera.transform.SetPositionAndRotation(cleanStage.Position(source.transform.position),source.transform.rotation);
        captureCamera.scene = cleanStage.Scene; captureCamera.targetTexture = captureTarget;
        captureCamera.cullingMask = ~0;
        int ui = LayerMask.NameToLayer("UI"); if (ui >= 0) captureCamera.cullingMask &= ~(1 << ui);
        captureCamera.clearFlags = CameraClearFlags.SolidColor; captureCamera.backgroundColor = new Color(.13f,.16f,.19f);
        captureCamera.rect = new Rect(0,0,1,1); captureCamera.aspect = source.aspect;
        captureCamera.projectionMatrix = source.projectionMatrix;
    }
    string CaptureFootDetail(string name, Vector3 feetViewport)
    {
        if (!captureFeet) return null;
        Vector3 hip = captureCamera.WorldToViewportPoint(cleanStage.Position(hips.position));
        Vector3 leftFoot = captureCamera.WorldToViewportPoint(cleanStage.Position(feet[0].position));
        Vector3 rightFoot = captureCamera.WorldToViewportPoint(cleanStage.Position(feet[1].position));
        float size = Mathf.Clamp(Mathf.Max(Mathf.Abs(hip.y-feetViewport.y)*captureTarget.height*1.4f+70,
            Mathf.Abs(leftFoot.x-rightFoot.x)*captureTarget.width*1.2f+64),110,320);
        Vector2 center = new Vector2((hip.x+feetViewport.x)*.5f,(hip.y+feetViewport.y)*.5f);
        var projection = captureCamera.projectionMatrix;
        var crop = Matrix4x4.identity;
        crop.m00 = captureTarget.width/size; crop.m11 = captureTarget.height/size;
        crop.m03 = -(center.x*2-1)*crop.m00; crop.m13 = -(center.y*2-1)*crop.m11;
        try
        {
            // Render the same evaluated pose and game viewpoint at a higher detail resolution.
            captureCamera.targetTexture = footTarget; captureCamera.projectionMatrix = crop*projection;
            captureCamera.Render(); RenderTexture.active = footTarget;
            footPixels.ReadPixels(new Rect(0,0,1024,1024),0,0); footPixels.Apply();
            string relative = "Foot/"+name;
            File.WriteAllBytes(Path.Combine(captureDirectory,relative),footPixels.EncodeToPNG());
            return relative;
        }
        finally {captureCamera.targetTexture = captureTarget;captureCamera.projectionMatrix = projection;}
    }
    public void AddCaptureActor(Transform actor) => cleanStage.AddVisibleActor(actor);
    void Capture()
    {
        if (captureCamera == null || Time.unscaledTime < nextCapture
            || !(phase.StartsWith("small_") || phase.StartsWith("turn_") || phase.StartsWith("attack_") || phase.StartsWith("move_") || phase.StartsWith("stop_") || phase.StartsWith("combat_") || phase == "aim_demo")) return;
        nextCapture = Time.unscaledTime;
        var gameplayCamera = Camera.main;
        if (gameplayCamera == null) throw new InvalidOperationException("The gameplay camera was removed during capture.");
        CopyGameplayCamera(gameplayCamera);
        cleanStage.Sync(captureCamera,presentationCamera);
        Vector3 feetViewport = captureCamera.WorldToViewportPoint(cleanStage.Position((feet[0].position+feet[1].position)*.5f));
        var previous = RenderTexture.active;
        try
        {
            captureCamera.Render(); RenderTexture.active = captureTarget;
            capturePixels.ReadPixels(new Rect(0,0,captureTarget.width,captureTarget.height),0,0); capturePixels.Apply();
            string name = captureIndex++.ToString("D5") + ".png";
            File.WriteAllBytes(Path.Combine(captureDirectory,name),capturePixels.EncodeToPNG());
            captures.Add(new {name,phase,time=Time.time,unscaled=Time.unscaledTime,frame=Time.frameCount,
                aim=facing.AimYaw,lower=facing.LowerYaw,feetViewport=new[]{feetViewport.x,feetViewport.y},
                actorFraming=cleanStage.ActorFraming(captureCamera),
                width=captureTarget.width,height=captureTarget.height,
                view="live gameplay Camera.main",footName=CaptureFootDetail(name,feetViewport),
                gameCamera=new {name=gameplayCamera.name,orthographic=gameplayCamera.orthographic,
                    fieldOfView=gameplayCamera.fieldOfView,orthographicSize=gameplayCamera.orthographicSize,aspect=gameplayCamera.aspect,
                    position=new[]{gameplayCamera.transform.position.x,gameplayCamera.transform.position.y,gameplayCamera.transform.position.z},
                    euler=new[]{gameplayCamera.transform.eulerAngles.x,gameplayCamera.transform.eulerAngles.y,gameplayCamera.transform.eulerAngles.z},
                    quarterView=gameplayCamera.GetComponentInParent<QuarterViewCamera>() != null,
                    positionError=Vector3.Distance(captureCamera.transform.position,cleanStage.Position(gameplayCamera.transform.position)),
                    rotationError=Quaternion.Angle(captureCamera.transform.rotation,gameplayCamera.transform.rotation)}});
        }
        finally {RenderTexture.active = previous;}
    }
    void OnDestroy()
    {
        try
        {
            if (captureDirectory != null) File.WriteAllText(Path.Combine(captureDirectory,"Frames.json"), JsonConvert.SerializeObject(captures));
        }
        finally
        {
            if (captureDirectory != null) Time.captureFramerate = priorCaptureRate;
            if (captureCamera != null) DestroyImmediate(captureCamera.gameObject);
            if (captureTarget != null) {captureTarget.Release();DestroyImmediate(captureTarget);}
            if (capturePixels != null) DestroyImmediate(capturePixels);
            if (footTarget != null) {footTarget.Release();DestroyImmediate(footTarget);}
            if (footPixels != null) DestroyImmediate(footPixels);
            cleanStage?.Dispose(); cleanStage = null;
        }
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
        var melee = facing.GetComponent<MeleeRuntime>();
        var energy = facing.GetComponent<OverburstElementEnergy>();
        var parry = facing.GetComponent<PlayerParryController>();
        var evade = facing.GetComponent<PlayerEvadeController>();
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
            speed = movement.Locomotion.HorizontalVelocity.magnitude, unscaled=Time.unscaledTime,
            gauge=energy != null ? energy.Amount : 0,
            attacking=melee.IsAttackInProgress, heavy=melee.IsHeavyAttackInProgress,
            parrying=melee.IsHeavyParryMotionActive, evading=evade.IsEvading,
            parrySuccess=parry != null ? parry.SuccessCount : 0,
            comboStep=(int)typeof(MeleeRuntime).GetField("comboStepIndex",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(melee),
            attackProgress=melee.IsAttackInProgress ? (float)typeof(MeleeRuntime).GetMethod("GetAttackNormalizedTime",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(melee,null) : 0,
            stopActive = movement.IsCombatStopCurveActive,
            stopSector = facing.Set != null ? Array.IndexOf(facing.Set.directions, movement.CombatStopMotion) : -1,
            stopElapsed = movement.CombatStopCurveElapsed, stopDistance = movement.CombatStopCurveDistance,
            stopRate = movement.CombatStopCurveRate,
            stopStartTime = movement.CombatStopCurveStartTime, stopTargetDistance = movement.CombatStopCurveTargetDistance,
            stopMomentum = movement.CombatStopMomentum,
            stopNormalized = Mathf.Clamp01(animator.IsInTransition(layer) && movement.CombatStopMotion != null
                && animator.GetNextAnimatorStateInfo(layer).shortNameHash == Animator.StringToHash(movement.CombatStopMotion.stopState)
                ? animator.GetNextAnimatorStateInfo(layer).normalizedTime : animator.GetCurrentAnimatorStateInfo(layer).normalizedTime),
            grounded=movement.IsGrounded, controllerEnabled=facing.GetComponent<CharacterController>().enabled,
            groundGap=motor.GroundGap, verticalVelocity=movement.VerticalVelocity });
        frames[frames.Count-1].chestYaw = Yaw(chest.rotation);
        frames[frames.Count-1].joints = joints.Select(j => j != null ? new[]{j.localRotation.x,j.localRotation.y,j.localRotation.z,j.localRotation.w}:null).ToArray();
        Capture();
    }
}
