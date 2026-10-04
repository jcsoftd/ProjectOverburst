using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static partial class PlayerEvadeVerifier
{
    const string StopCurveComparisonKey = "Overburst.PlayerEvadeVerifier.StopCurveComparison";
    public static string QueueSwordStopCurveComparison(string directory)
    {
        if (!string.IsNullOrEmpty(SessionState.GetString(PendingKey, ""))
            || !string.IsNullOrEmpty(SessionState.GetString(ReturnKey, "")))
            throw new InvalidOperationException("Preserve the active verifier.");
        SessionState.SetBool(SwordStopVerificationKey, true);
        SessionState.SetBool(StopCurveComparisonKey, true);
        try { return QueueSwordFacingIsolated(directory); }
        catch { SessionState.EraseBool(SwordStopVerificationKey); SessionState.EraseBool(StopCurveComparisonKey); throw; }
    }

    static IEnumerator VerifySwordStopCurveComparison()
    {
        var facing = actor.GetComponent<PlayerCombatFacingController>();
        var profile = actor.Equipment.CurrentWeaponData.GetMeleeDefinition().animationProfile;
        var set = profile.combatLocomotionSet;
        var trial = actor.gameObject.AddComponent<SwordStopCurveComparisonOverride>();
        facingProbe = blocker.AddComponent<SwordFacingPoseProbe>();
        facingProbe.Bind(animator, facing);
        try
        {
            trial.Bind(movement, animator, profile,
                Path.GetFullPath("../개인파일/코덱스산출/Animation/20261004_SwordStopCurve/MeasuredRootMotion.json"));
            facingProbe.StartCapture(Path.Combine(output, "Capture"));
            foreach (int mode in new[] { 0, 1, 2 })
            foreach (int i in new[] { 0, 2, 3, 4 })
            {
                trial.Mode = mode;
                yield return FacingReset();
                FacingAim(-i * 45); yield return FacingWait(1.7f);
                Send(true); yield return FacingSample(1.05f, "move_curve_" + mode + "_" + i);
                var run = facingProbe.frames.Last();
                trial.Sector = i;
                Send(); yield return FacingSample(set.directions[i].stop.length + .4f, "stop_curve_" + mode + "_" + i);
                var rows = facingProbe.frames.Where(f => f.phase == "stop_curve_" + mode + "_" + i).ToArray();
                float coast = PlanarDistance(rows.Last().actor, run.actor);
                Check(rows.Any(f => f.clips.Contains(set.directions[i].stop.name)), "비교 " + mode + "/" + i + " 실제 Stop 클립");
                Check(SwordIdleClipVisible(profile.combatIdleClip), "비교 " + mode + "/" + i + " Idle 복귀");
                Check(rows.All(f => f.leftWeight == 0 && f.rightWeight == 0), "비교 " + mode + "/" + i + " IK0");
                Check(rows.Last().speed < .001f, "비교 " + mode + "/" + i + " 최종 속도 0");
                Check(mode == 0 || coast > .3f, "비교 " + mode + "/" + i + " 실제 곡선 이동 관측");
                Check(mode == 0 || Mathf.Abs(coast - trial.ConsumedDistance) < .003f,
                    "비교 " + mode + "/" + i + " 곡선 적분과 실제 이동 3mm 이내");
                if (mode == 1) Check(Mathf.Abs(coast - trial.AuthoredEndDistance) < .003f,
                    "방향 " + i + " 원본 Stop 마지막 프레임까지 이동 소비");
                samples.Add(new { mode, direction = i, coast, runSpeed = run.speed });
                Progress("stop_curve_" + mode + "_" + i);
            }
        }
        finally
        {
            File.WriteAllText(Path.Combine(output, "CurveMotionSamples.json"), JsonConvert.SerializeObject(trial.Samples));
            UnityEngine.Object.DestroyImmediate(trial);
            File.WriteAllText(Path.Combine(output, "FacingPoses.json"), JsonConvert.SerializeObject(facingProbe.frames));
            UnityEngine.Object.DestroyImmediate(facingProbe); facingProbe = null;
        }
    }
}

// This comparison exists only on the verifier's isolated Play actor. It never saves
// gameplay assets or changes the shipping motor's ownership of collision movement.
[DefaultExecutionOrder(270)]
public sealed class SwordStopCurveComparisonOverride : MonoBehaviour
{
    public int Mode, Sector;
    public readonly List<object> Samples = new List<object>();
    public float ConsumedDistance => consumed;
    public float AuthoredEndDistance => curves[Sector].Evaluate(lengths[Sector]) * animator.humanScale / sourceHumanScale;
    PlayerMovement movement;
    Animator animator;
    WeaponCombatAnimationProfile profile;
    PlayerInputFacade input;
    AnimationCurve[] curves;
    float[] lengths;
    float sourceHumanScale;
    bool wasMoving, active, ownsDeceleration;
    Vector3 direction;
    float releaseSpeed, elapsed, consumed, originalDeceleration, rate, velocityCorrection;
    const float EntrySeconds = .08f;
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly FieldInfo Deceleration = typeof(PlayerMovement).GetField("deceleration", Flags);
    static readonly FieldInfo WasMoving = typeof(PlayerMovement).GetField("combatStopWasMoving", Flags);
    static readonly FieldInfo StopDeceleration = typeof(PlayerMovement).GetField("combatStopDeceleration", Flags);

    public void Bind(PlayerMovement owner, Animator mainAnimator, WeaponCombatAnimationProfile p, string source)
    {
        movement = owner; animator = mainAnimator; profile = p;
        input = owner.GetComponent<PlayerInputFacade>();
        var data = JArray.Parse(File.ReadAllText(source));
        curves = new AnimationCurve[8]; lengths = new float[8];
        foreach (var row in data)
        {
            int i = (int)row["i"]; lengths[i] = (float)row["length"];
            sourceHumanScale = (float)row["humanScale"];
            var frames = row["frames"].ToArray();
            Vector2 axis = new Vector2(Mathf.Sin(i * 45 * Mathf.Deg2Rad), Mathf.Cos(i * 45 * Mathf.Deg2Rad));
            var zero = new Vector2((float)frames[0]["root"][0], (float)frames[0]["root"][1]);
            curves[i] = new AnimationCurve(frames.Select(f => new Keyframe((float)f["t"],
                Vector2.Dot(new Vector2((float)f["root"][0], (float)f["root"][1]) - zero, axis))).ToArray());
            for (int k = 0; k < curves[i].length; k++) curves[i].SmoothTangents(k, 0);
        }
    }
    void Update()
    {
        if (movement == null || input == null || curves == null) return;
        bool moving = input.MoveValue.sqrMagnitude > .001f;
        bool eligible = Mode > 0 && movement.IsGrounded && movement.IsMeleeCombatLocomotionMode
            && !movement.IsConditionMovementBlocked && !movement.IsEvading && !movement.IsMeleeAttackMoveLocked
            && !GameplayInputBlocker.IsGameplayInputBlocked;
        if (!eligible || moving)
        {
            Restore(); active = false; wasMoving = eligible && moving;
            return;
        }
        if (wasMoving)
        {
            wasMoving = false; active = true; elapsed = consumed = 0;
            Vector3 velocity = movement.Locomotion.HorizontalVelocity;
            direction = velocity.sqrMagnitude > .0001f ? velocity.normalized : Vector3.forward;
            releaseSpeed = velocity.magnitude;
            var motion = profile.combatLocomotionSet.directions[Sector];
            Vector3 sectorDirection = Quaternion.Euler(0, Sector * 45, 0) * Vector3.forward;
            rate = profile.combatLocomotionSet.scaleStartStopWithLocomotionSpeed
                ? profile.locomotionReferenceSpeeds.GetSpeed(sectorDirection) * profile.locomotionAnimationSpeedMultiplier / motion.authoredSpeed : 1;
            float firstStep = curves[Sector].keys[1].time;
            float authoredInitial = (curves[Sector].Evaluate(firstStep) - curves[Sector].Evaluate(0)) / firstStep * rate * animator.humanScale / sourceHumanScale;
            velocityCorrection = releaseSpeed - authoredInitial;
            originalDeceleration = (float)Deceleration.GetValue(movement);
            Deceleration.SetValue(movement, 0f); ownsDeceleration = true;
        }
        if (!active) return;
        // Prevent the existing short-brake policy adding a second displacement.
        WasMoving.SetValue(movement, false); StopDeceleration.SetValue(movement, 0f);
        if (elapsed * rate >= lengths[Sector])
        {
            movement.Locomotion.Stop(); active = false; Restore(); return;
        }
        float dt = Time.deltaTime; elapsed += dt;
        float nativeTime = Mathf.Min(elapsed * rate, lengths[Sector]);
        float distance = curves[Sector].Evaluate(nativeTime) * animator.humanScale / sourceHumanScale;
        if (Mode == 2)
        {
            float p = Mathf.Clamp01(elapsed / EntrySeconds);
            distance += velocityCorrection * EntrySeconds / 3f * (1 - Mathf.Pow(1 - p, 3));
            distance = Mathf.Max(consumed, distance);
        }
        Vector3 delta = direction * (distance - consumed); consumed = distance;
        movement.Locomotion.SetHorizontalVelocity(dt > 0 ? delta / dt : Vector3.zero);
        Samples.Add(new { time = Time.time, mode = Mode, sector = Sector, elapsed, nativeTime, rate,
            releaseSpeed, velocityCorrection, distance, delta = delta.magnitude });
    }
    void Restore()
    {
        if (ownsDeceleration && movement != null && (float)Deceleration.GetValue(movement) == 0f)
            Deceleration.SetValue(movement, originalDeceleration);
        ownsDeceleration = false;
    }
    void OnDisable() { Restore(); active = wasMoving = false; }
    void OnDestroy() { Restore(); }
}
