using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

// A temporary hand-supported recovery, authored onto the existing generic rig.
// Source fall poses guide the torso; planted hands/feet and staggered release are baked into this separate clip.
public static class CrustaspikanGetUpBuilder
{
    public const string ClipPath = CrustaspikanMaterialBuilder.Root + "/LicensedClips/Temporary_GetUp.anim";
    public const string State = "Material_TemporaryGetUp";
    public const float Duration = 2.6f;
    sealed class Pose
    {
        public Vector3[] positions, scales;
        public Quaternion[] rotations;
        public Pose(Transform[] bones)
        {
            positions = bones.Select(t => t.localPosition).ToArray(); rotations = bones.Select(t => t.localRotation).ToArray();
            scales = bones.Select(t => t.localScale).ToArray();
        }
        public void Restore(Transform[] bones)
        { for (int i = 0; i < bones.Length; i++) { bones[i].localPosition = positions[i]; bones[i].localRotation = rotations[i]; bones[i].localScale = scales[i]; } }
        public void Blend(Transform[] bones, float amount)
        { for (int i = 0; i < bones.Length; i++) { bones[i].localPosition = Vector3.Lerp(bones[i].localPosition, positions[i], amount); bones[i].localRotation = Quaternion.Slerp(bones[i].localRotation, rotations[i], amount); bones[i].localScale = Vector3.Lerp(bones[i].localScale, scales[i], amount); } }
    }
    static float Ease(float from, float to, float time) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(from, to, time));
    static Transform Bone(Transform[] bones, string name) => bones.Single(t => t.name == "Crustaspikan_ " + name);
    static void Solve(Transform upper, Transform middle, Transform end, Vector3 target, Vector3 hint, Quaternion rotation, float weight)
    {
        if (weight <= 0) return;
        Vector3 origin = upper.position, oldMiddle = middle.position, oldEnd = end.position;
        float a = Vector3.Distance(origin, oldMiddle), b = Vector3.Distance(oldMiddle, oldEnd);
        Vector3 delta = target - origin; float length = Mathf.Clamp(delta.magnitude, Mathf.Abs(a - b) + .001f, a + b - .001f);
        Vector3 forward = delta.sqrMagnitude > .000001f ? delta.normalized : (oldEnd - origin).normalized;
        Vector3 side = Vector3.ProjectOnPlane(hint - origin, forward).normalized;
        if (side.sqrMagnitude < .001f) side = Vector3.ProjectOnPlane(oldMiddle - origin, forward).normalized;
        float along = (a * a - b * b + length * length) / (2 * length);
        Vector3 elbow = origin + forward * along + side * Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
        Quaternion u = upper.rotation;
        upper.rotation = Quaternion.Slerp(u, Quaternion.FromToRotation(oldMiddle - origin, elbow - origin) * u, weight);
        Quaternion m = middle.rotation;
        middle.rotation = Quaternion.Slerp(m, Quaternion.FromToRotation(end.position - middle.position, origin + forward * length - middle.position) * m, weight);
        end.rotation = Quaternion.Slerp(end.rotation, rotation, weight);
    }
    public static string Probe(string output)
    {
        var collection = AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(CrustaspikanMaterialBuilder.CollectionPath);
        var root = PrefabUtility.LoadPrefabContents(CrustaspikanTemporaryReactionBuilder.PrefabPath);
        try
        {
            var model = root.GetComponentInChildren<Animator>(true); model.enabled = false;
            var bones = model.GetComponentsInChildren<Transform>(true); var rows = new JArray();
            foreach (float p in new[] { 1f, .78f, .62f, .56f, .42f, 0f })
            {
                (p == 0 ? collection.FindMotion("IdleBreathe").runtime : collection.FindMotion("Death").runtime).SampleAnimation(model.gameObject, p * collection.FindMotion("Death").runtime.length);
                var points = new JObject();
                foreach (string name in new[] { "Pelvis", "Spine2", "L UpperArm", "L Forearm", "L Hand", "R UpperArm", "R Hand", "L Foot", "R Foot" })
                { Vector3 v = Bone(bones, name).position - root.transform.position; points[name] = new JArray(v.x, v.y, v.z); }
                rows.Add(new JObject { ["normalized"] = p, ["points"] = points });
            }
            File.WriteAllText(output, rows.ToString()); return rows.ToString();
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    public static AnimationClip Build(string evidence)
    {
        var collection = AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(CrustaspikanMaterialBuilder.CollectionPath);
        var death = collection.FindMotion("Death").runtime; var idle = collection.FindMotion("IdleBreathe").runtime;
        var root = PrefabUtility.LoadPrefabContents(CrustaspikanTemporaryReactionBuilder.PrefabPath);
        var clip = new AnimationClip { name = "Temporary_GetUp", frameRate = 30 };
        var rows = new JArray();
        try
        {
            var model = root.GetComponentInChildren<Animator>(true); model.enabled = false;
            var paths = new HashSet<string>(AnimationUtility.GetCurveBindings(death).Where(b => b.type == typeof(Transform)).Select(b => b.path));
            var bones = model.GetComponentsInChildren<Transform>(true).Where(t => paths.Contains(AnimationUtility.CalculateTransformPath(t, model.transform))).ToArray();
            idle.SampleAnimation(model.gameObject, 0); var idlePose = new Pose(bones);
            Transform lFoot = Bone(bones, "L Foot"), rFoot = Bone(bones, "R Foot"), lHand = Bone(bones, "L Hand"), rHand = Bone(bones, "R Hand");
            Vector3 idleLFoot = lFoot.position, idleRFoot = rFoot.position; Quaternion idleLF = lFoot.rotation, idleRF = rFoot.rotation;
            death.SampleAnimation(model.gameObject, death.length); var prone = new Pose(bones);
            Vector3 originalLHand = lHand.position, originalRHand = rHand.position, originalLFoot = lFoot.position, originalRFoot = rFoot.position;
            Quaternion proneLH = lHand.rotation, proneRH = rHand.rotation;
            Transform chest = Bone(bones, "Spine2"); float scale = model.transform.lossyScale.x;
            float span = Mathf.Abs(Bone(bones, "L UpperArm").position.x - Bone(bones, "R UpperArm").position.x) * .375f;
            Vector3 plantL = new Vector3(chest.position.x - span, Mathf.Min(originalLHand.y, idleLFoot.y + .4f * scale), chest.position.z + .1f * scale);
            Vector3 plantR = new Vector3(chest.position.x + span, Mathf.Min(originalRHand.y, idleRFoot.y + .4f * scale), chest.position.z + .1f * scale);
            var curves = new AnimationCurve[bones.Length, 10];
            for (int i = 0; i < bones.Length; i++) for (int axis = 0; axis < 10; axis++) curves[i, axis] = new AnimationCurve();
            for (int frame = 0; frame <= 78; frame++)
            {
                float time = frame / 30f;
                float source = time < .18f ? Mathf.Lerp(1, .78f, Ease(0, .18f, time)) : time < .4f ? .78f
                    : time < 1.35f ? Mathf.Lerp(.78f, .56f, Ease(.4f, 1.35f, time)) : Mathf.Lerp(.56f, .42f, Ease(1.35f, 1.95f, time));
                prone.Restore(bones); death.SampleAnimation(model.gameObject, death.length * source); idlePose.Blend(bones, Ease(1.8f, Duration, time));
                float plant = Ease(0, .4f, time);
                float leftSupport = 1 - Ease(1.1f, 1.75f, time), rightSupport = 1 - Ease(1.35f, 2f, time);
                Solve(Bone(bones, "L UpperArm"), Bone(bones, "L Forearm"), lHand, Vector3.Lerp(originalLHand, plantL, plant),
                    chest.position + Vector3.left * 5 * scale, proneLH, plant * leftSupport);
                Solve(Bone(bones, "R UpperArm"), Bone(bones, "R Forearm"), rHand, Vector3.Lerp(originalRHand, plantR, plant),
                    chest.position + Vector3.right * 5 * scale, proneRH, plant * rightSupport);
                Solve(Bone(bones, "L Thigh"), Bone(bones, "L Calf"), lFoot, Vector3.Lerp(originalLFoot, idleLFoot, Ease(.8f, 1.45f, time)) + Vector3.up * Mathf.Sin(Mathf.PI * Ease(.8f, 1.45f, time)) * .15f * scale,
                    chest.position + Vector3.forward * 3 * scale, Quaternion.Slerp(lFoot.rotation, idleLF, Ease(.8f, 1.5f, time)), Ease(0, .15f, time));
                Solve(Bone(bones, "R Thigh"), Bone(bones, "R Calf"), rFoot, Vector3.Lerp(originalRFoot, idleRFoot, Ease(1.05f, 1.7f, time)) + Vector3.up * Mathf.Sin(Mathf.PI * Ease(1.05f, 1.7f, time)) * .15f * scale,
                    chest.position + Vector3.forward * 3 * scale, Quaternion.Slerp(rFoot.rotation, idleRF, Ease(1.05f, 1.75f, time)), Ease(0, .15f, time));
                if (frame == 0) prone.Restore(bones); if (frame == 78) idlePose.Restore(bones);
                for (int i = 0; i < bones.Length; i++)
                {
                    Vector3 p = bones[i].localPosition, s = bones[i].localScale; Quaternion q = bones[i].localRotation;
                    var values = new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w, s.x, s.y, s.z };
                    for (int axis = 0; axis < values.Length; axis++) curves[i, axis].AddKey(time, values[axis]);
                }
                if (frame % 6 == 0) rows.Add(new JObject { ["time"] = time, ["chestY"] = chest.position.y,
                    ["leftHand"] = new JArray(lHand.position.x, lHand.position.y, lHand.position.z),
                    ["rightHand"] = new JArray(rHand.position.x, rHand.position.y, rHand.position.z) });
            }
            var properties = new[] { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z", "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w", "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z" };
            for (int i = 0; i < bones.Length; i++) for (int axis = 0; axis < properties.Length; axis++)
            {
                var curve = curves[i, axis];
                for (int key = 0; key < curve.length; key++) { AnimationUtility.SetKeyLeftTangentMode(curve, key, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(curve, key, AnimationUtility.TangentMode.Linear); }
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(bones[i], model.transform), typeof(Transform), properties[axis]), curve);
            }
            clip.EnsureQuaternionContinuity(); var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = false; AnimationUtility.SetAnimationClipSettings(clip, settings);
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            if (existing == null) { AssetDatabase.CreateAsset(clip, ClipPath); existing = clip; clip = null; }
            else { EditorUtility.CopySerialized(clip, existing); EditorUtility.SetDirty(existing); AssetDatabase.SaveAssetIfDirty(existing); }
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CrustaspikanMaterialBuilder.Root + "/AC_Crustaspikan.controller");
            var sm = controller.layers[0].stateMachine;
            var state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == State) ?? sm.AddState(State);
            state.motion = existing; state.speed = 1; EditorUtility.SetDirty(controller); AssetDatabase.SaveAssetIfDirty(controller);
            Directory.CreateDirectory(Path.GetDirectoryName(evidence)); File.WriteAllText(evidence, new JObject { ["status"] = "AUTHORED", ["clip"] = ClipPath, ["duration"] = existing.length, ["bones"] = bones.Length, ["samples"] = 79, ["poses"] = rows }.ToString());
            return existing;
        }
        finally { if (clip != null) Object.DestroyImmediate(clip); PrefabUtility.UnloadPrefabContents(root); }
    }
}
