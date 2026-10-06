using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// A native joint clip authored on the actual generic rig; no actor root motion or supplier curve edits.
public static class CrustaspikanParryDazedBuilder
{
    public const string Folder = CrustaspikanMotionPlaybackBuilder.Root + "/Authored";
    public const string ClipPath = Folder + "/AN_Crustaspikan_ParryDazed.anim";
    public const float CycleSeconds = 2.8f;
    const int Samples = 168;
    sealed class Pose
    {
        public Transform bone;
        public Vector3 position, scale;
        public Quaternion rotation;
        public Vector3[] positions = new Vector3[Samples + 1], scales = new Vector3[Samples + 1];
        public Quaternion[] rotations = new Quaternion[Samples + 1];
        public void Reset() { bone.localPosition = position; bone.localRotation = rotation; bone.localScale = scale; }
        public void Record(int i) { positions[i] = bone.localPosition; rotations[i] = bone.localRotation; scales[i] = bone.localScale; }
    }
    static Transform Bone(Animator a, string suffix) => a.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Crustaspikan_" + suffix);
    static void Tilt(Transform bone, Vector3 axis, float degrees) => bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
    static void SolveLimb(Transform first, Transform middle, Transform end, Vector3 target, Vector3 bend)
    {
        Vector3 start = first.position, delta = target - start;
        float a = Vector3.Distance(start, middle.position), b = Vector3.Distance(middle.position, end.position);
        float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(a - b) + .0001f, a + b - .0001f);
        Vector3 direction = delta.normalized;
        Vector3 side = Vector3.ProjectOnPlane(bend, direction).normalized;
        if (side.sqrMagnitude < .01f) side = Vector3.Cross(direction, Vector3.right).normalized;
        float along = (a * a - b * b + distance * distance) / (2f * distance);
        Vector3 joint = start + direction * along + side * Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
        first.rotation = Quaternion.FromToRotation(middle.position - start, joint - start) * first.rotation;
        middle.rotation = Quaternion.FromToRotation(end.position - middle.position, target - middle.position) * middle.rotation;
    }
    static void Curve(AnimationClip clip, string path, string property, float[] values)
    {
        bool constant = values.All(v => Mathf.Abs(v - values[0]) < .000001f);
        var curve = new AnimationCurve();
        for (int i = 0; i <= Samples; i++)
            if (!constant || i == 0 || i == Samples) curve.AddKey(CycleSeconds * i / Samples, values[i]);
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
        }
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), curve);
    }
    static void Bake(AnimationClip clip, Pose pose, Transform model)
    {
        string path = AnimationUtility.CalculateTransformPath(pose.bone, model);
        for (int n = 0; n < 3; n++)
        {
            int axis = n; string component = "xyz"[n].ToString();
            Curve(clip, path, "m_LocalPosition." + component, pose.positions.Select(v => v[axis]).ToArray());
            Curve(clip, path, "m_LocalScale." + component, pose.scales.Select(v => v[axis]).ToArray());
        }
        for (int n = 0; n < 4; n++)
        { int axis = n; Curve(clip, path, "m_LocalRotation." + "xyzw"[n], pose.rotations.Select(v => v[axis]).ToArray()); }
    }

    public static string EnsureNativeAssets()
    {
        CrustaspikanMotionPlaybackBuilder.RequireIdle();
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(CrustaspikanMotionPlaybackBuilder.Root, "Authored");
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if (clip == null) clip = AuthorClip();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CrustaspikanMotionPlaybackBuilder.ControllerPath);
        if (controller == null) throw new InvalidOperationException("Boss controller missing.");
        var machine = controller.layers[0].stateMachine;
        var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == CrustaspikanTemporaryReaction.ParryDazedStateName)
            ?? machine.AddState(CrustaspikanTemporaryReaction.ParryDazedStateName);
        if (state.motion != null && state.motion != clip) throw new InvalidOperationException("Parry daze state contains another owner's motion.");
        state.motion = clip; state.speed = 1f; state.speedParameterActive = false; state.writeDefaultValues = true;
        EditorUtility.SetDirty(state); EditorUtility.SetDirty(controller); AssetDatabase.SaveAssetIfDirty(controller);
        var contents = PrefabUtility.LoadPrefabContents(CrustaspikanMotionPlaybackBuilder.PrefabPath);
        try
        {
            var reaction = contents.GetComponent<CrustaspikanTemporaryReaction>();
            if (reaction == null) throw new InvalidOperationException("Temporary reaction consumer missing.");
            var so = new SerializedObject(reaction); var property = so.FindProperty("parryDazedClip");
            if (property == null) throw new InvalidOperationException("Daze clip field missing.");
            if (property.objectReferenceValue != null && property.objectReferenceValue != clip) throw new InvalidOperationException("Daze consumer was edited externally.");
            if (property.objectReferenceValue != clip)
            {
                property.objectReferenceValue = clip; so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(contents, CrustaspikanMotionPlaybackBuilder.PrefabPath, out bool saved);
                if (!saved) throw new IOException("Daze prefab save failed.");
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        ValidateNative(); return "Authored standing parry daze loop linked and native validated.";
    }

    static AnimationClip AuthorClip()
    {
        var collection = AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(CrustaspikanMaterialBuilder.CollectionPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CrustaspikanMotionPlaybackBuilder.PrefabPath);
        if (collection == null || prefab == null) throw new InvalidOperationException("Native rig missing.");
        Scene preview = EditorSceneManager.NewPreviewScene(); GameObject clone = null; AnimationClip clip = null;
        try
        {
            clone = Object.Instantiate(prefab); SceneManager.MoveGameObjectToScene(clone, preview);
            var animator = clone.GetComponent<EnemyActor>().Animator;
            foreach (var component in clone.GetComponentsInChildren<MonoBehaviour>(true)) component.enabled = false;
            animator.enabled = false; animator.fireEvents = false;
            var idle = collection.FindMotion("IdleBreathe").runtime;
            idle.SampleAnimation(animator.gameObject, idle.length * .23f);
            var rig = animator.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == "root" || t.name.StartsWith("Crustaspikan_", StringComparison.Ordinal)).ToArray();
            var poses = rig.Select(t => new Pose { bone = t, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
            var top = Bone(animator, ""); var spine = Bone(animator, " Spine"); var spine1 = Bone(animator, " Spine1"); var spine2 = Bone(animator, " Spine2");
            var neck = Bone(animator, " Neck"); var head = Bone(animator, " Head");
            var leftFoot = Bone(animator, " L Foot"); var rightFoot = Bone(animator, " R Foot");
            var leftPosition = leftFoot.position; var rightPosition = rightFoot.position;
            var leftRotation = leftFoot.rotation; var rightRotation = rightFoot.rotation;
            Vector3 hips = top.position, right = animator.transform.right, forward = animator.transform.forward, up = Vector3.up;
            float height = head.position.y - Mathf.Min(leftPosition.y, rightPosition.y);
            float maxFootDrift = 0f, minHandRatio = float.MaxValue, maxHandRatio = 0f;
            for (int i = 0; i <= Samples; i++)
            {
                foreach (var p in poses) p.Reset();
                float t = Mathf.PI * 2f * i / Samples;
                float sway = Mathf.Sin(t), lag = Mathf.Sin(t - .55f), buckle = Mathf.Sin(t * 2f - .35f);
                top.position = hips + right * (height * .035f * sway) - up * (height * (.047f + .025f * buckle))
                    + forward * (height * .012f * Mathf.Sin(t + .4f));
                Tilt(top, forward, -6f * sway); Tilt(top, right, 7f + 2f * Mathf.Sin(t * 2f));
                Tilt(spine, forward, -4f * lag); Tilt(spine, right, 5f);
                Tilt(spine1, forward, -3f * Mathf.Sin(t - .9f)); Tilt(spine1, right, 4f);
                Tilt(spine2, forward, 2f * Mathf.Sin(t - 1.1f));
                Tilt(neck, right, 12f + 5f * Mathf.Sin(t * 2f - .9f));
                Tilt(head, forward, 7f * Mathf.Sin(t - 1.35f)); Tilt(head, up, 8f * Mathf.Sin(t - .8f));
                foreach (string side in new[] { " L", " R" })
                {
                    float sign = side == " L" ? -1f : 1f;
                    var shoulder = Bone(animator, side + " UpperArm"); var elbow = Bone(animator, side + " Forearm"); var hand = Bone(animator, side + " Hand");
                    var clavicle = Bone(animator, side + " Clavicle"); Tilt(clavicle, forward, sign * 5f);
                    float length = Vector3.Distance(shoulder.position, elbow.position) + Vector3.Distance(elbow.position, hand.position);
                    Vector3 hanging = -up * .957f + right * (sign * .105f - .058f * Mathf.Sin(t - 1.05f))
                        + forward * (.07f * Mathf.Sin(t - .6f + sign * .35f));
                    Vector3 target = shoulder.position + hanging * length;
                    target.y = Mathf.Max(target.y, Mathf.Min(leftPosition.y, rightPosition.y) + height * .17f);
                    SolveLimb(shoulder, elbow, hand, target, forward + right * sign * .2f);
                    var finger = Bone(animator, side + " Finger0");
                    hand.rotation = Quaternion.FromToRotation(finger.position - hand.position, -up + right * (.1f * Mathf.Sin(t - 1.5f))) * hand.rotation;
                    Tilt(hand, -up, 7f * Mathf.Sin(t - 1.6f + sign * .3f));
                    foreach (var p in poses.Where(p => p.bone.name.StartsWith("Crustaspikan_" + side + " Finger", StringComparison.Ordinal)))
                        p.bone.localRotation *= Quaternion.AngleAxis(7f, Vector3.forward);
                    float ratio = (hand.position.y - Mathf.Min(leftPosition.y, rightPosition.y)) / height;
                    minHandRatio = Mathf.Min(minHandRatio, ratio); maxHandRatio = Mathf.Max(maxHandRatio, ratio);
                }
                foreach (var leg in new[] { (" L", leftFoot, leftPosition, leftRotation), (" R", rightFoot, rightPosition, rightRotation) })
                {
                    SolveLimb(Bone(animator, leg.Item1 + " Thigh"), Bone(animator, leg.Item1 + " Calf"), leg.Item2, leg.Item3, forward);
                    leg.Item2.rotation = leg.Item4;
                    maxFootDrift = Mathf.Max(maxFootDrift, Vector3.Distance(leg.Item2.position, leg.Item3));
                }
                foreach (var p in poses.Where(p => p.bone.name.StartsWith("Crustaspikan_ Tail", StringComparison.Ordinal)))
                    Tilt(p.bone, up, -2f * Mathf.Sin(t - 1.2f));
                foreach (var p in poses) p.Record(i);
            }
            // Exact endpoint closure eliminates a seam when the daze is sustained.
            foreach (var p in poses) { p.positions[Samples] = p.positions[0]; p.rotations[Samples] = p.rotations[0]; p.scales[Samples] = p.scales[0]; }
            clip = new AnimationClip { name = "AN_Crustaspikan_ParryDazed", frameRate = 60, legacy = false, wrapMode = WrapMode.Loop };
            foreach (var p in poses) Bake(clip, p, animator.transform);
            clip.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = true; settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings); AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
            if (maxFootDrift > height * .008f || minHandRatio < .15f || maxHandRatio > .42f) throw new InvalidOperationException("Authored daze does not meet planted-foot/limp-arm silhouette.");
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            if (existing == null) AssetDatabase.CreateAsset(clip, ClipPath);
            else { EditorUtility.CopySerialized(clip, existing); Object.DestroyImmediate(clip); clip = existing; EditorUtility.SetDirty(clip); }
            AssetDatabase.SaveAssetIfDirty(clip);
            Write("dazed-authoring.json", new { status = "PASS_NATIVE_AUTHORED_CLIP", clip = ClipPath, duration = clip.length, clip.frameRate,
                bones = poses.Length, maxFootDrift, minHandRatio, maxHandRatio, height, rootMotion = false, loopEndpointClosed = true, events = 0 });
            return clip;
        }
        finally
        {
            if (clone != null) Object.DestroyImmediate(clone);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            if (clip != null && !EditorUtility.IsPersistent(clip)) Object.DestroyImmediate(clip);
        }
    }
    static void Write(string name, object value)
    {
        string path = Path.Combine(CrustaspikanMotionPlaybackBuilder.Output, "Evidence", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonConvert.SerializeObject(value, Formatting.Indented));
    }
    public static string RebuildAuthoredClip()
    {
        CrustaspikanMotionPlaybackBuilder.RequireIdle(); AuthorClip(); ValidateNative();
        return "Authored daze joint curves rebuilt in place; native GUID preserved.";
    }
    public static void ValidateNative()
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CrustaspikanMotionPlaybackBuilder.PrefabPath);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CrustaspikanMotionPlaybackBuilder.ControllerPath);
        var state = controller.layers[0].stateMachine.states.Select(s => s.state).Single(s => s.name == CrustaspikanTemporaryReaction.ParryDazedStateName);
        if (clip == null || !clip.isLooping || Mathf.Abs(clip.length - CycleSeconds) > .001f || state.motion != clip
            || prefab.GetComponent<CrustaspikanTemporaryReaction>().ParryDazedClip != clip) throw new InvalidOperationException("Daze native binding mismatch.");
        var curves = AnimationUtility.GetCurveBindings(clip);
        if (curves.Any(b => string.IsNullOrEmpty(b.path)) || AnimationUtility.GetAnimationEvents(clip).Length != 0) throw new InvalidOperationException("Daze contains root motion/events.");
        foreach (var binding in curves)
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve.keys.Any(k => float.IsNaN(k.value) || float.IsInfinity(k.value)) || Mathf.Abs(curve.Evaluate(0) - curve.Evaluate(clip.length)) > .00001f)
                throw new InvalidOperationException("Daze loop curve does not close: " + binding.path + " / " + binding.propertyName);
        }
        Write("dazed-native.json", new { status = "PASS_NATIVE_DAZE_BINDING", clip = ClipPath, guid = AssetDatabase.AssetPathToGUID(ClipPath), duration = clip.length,
            clip.frameRate, curves = curves.Length, looping = clip.isLooping, actorRootCurves = 0, events = 0, consumer = "CrustaspikanTemporaryReaction / ReactionPose owner" });
    }
}
