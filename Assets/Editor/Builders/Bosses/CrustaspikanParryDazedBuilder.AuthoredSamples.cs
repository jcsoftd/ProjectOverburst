using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class CrustaspikanParryDazedBuilder
{
    public const string EnterClipPath = Folder + "/AN_Crustaspikan_ParryDazedEnter.anim";
    public const string RecoverClipPath = Folder + "/AN_Crustaspikan_ParryDazedRecover.anim";

    sealed class SampledBone
    {
        public Transform bone;
        public Vector3 baselinePosition, localScale;
        public Quaternion baselineRotation;
        public Matrix4x4 blenderBaseline;
        public List<Vector3> positions = new List<Vector3>();
        public List<Quaternion> rotations = new List<Quaternion>();
    }
    static Matrix4x4 ReadMatrix(JToken rows)
    {
        if (!(rows is JArray array) || array.Count != 4) throw new InvalidDataException("Authored matrix must have four rows.");
        Matrix4x4 matrix = new Matrix4x4();
        for (int row = 0; row < 4; row++)
            for (int col = 0; col < 4; col++)
            {
                float value = (float)array[row][col];
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("Nonfinite authored matrix.");
                matrix[row, col] = value;
            }
        return matrix;
    }
    static Vector3 BlenderToUnity(Vector3 value) => new Vector3(-value.x, value.z, -value.y);
    static Quaternion WorldRotationDelta(Matrix4x4 pose, Matrix4x4 baseline)
    {
        var delta = pose * baseline.inverse;
        // The handedness conversion is applied on both sides of the rotation.
        // Forward in Unity corresponds to -Y in the Blender review scene.
        return Quaternion.LookRotation(BlenderToUnity(delta.MultiplyVector(Vector3.down)),
            BlenderToUnity(delta.MultiplyVector(Vector3.forward)));
    }
    static void SetSampleCurve(AnimationClip clip, string path, string property, float duration, IList<float> values)
    {
        bool constant = values.All(v => Mathf.Abs(v - values[0]) < .000001f);
        var curve = new AnimationCurve();
        for (int i = 0; i < values.Count; i++)
            if (!constant || i == 0 || i == values.Count - 1) curve.AddKey(duration * i / (values.Count - 1), values[i]);
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
        }
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), curve);
    }
    static int Depth(Transform bone, Transform root)
    { int depth = 0; while (bone != root) { bone = bone.parent; depth++; } return depth; }

    // Blender remains the authoring source. Bake against a sampled native Idle
    // so FBX rest-axis corrections cannot silently deform the existing actor.
    public static string ImportAuthoredSamples(string sourceFile)
    {
        CrustaspikanMotionPlaybackBuilder.RequireIdle();
        var document = JObject.Parse(File.ReadAllText(sourceFile));
        if ((int)document["fps"] != 30) throw new InvalidDataException("Expected the reviewed 30fps authoring samples.");
        var baseline = (JObject)document["baseline"];
        var collection = AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(CrustaspikanMaterialBuilder.CollectionPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CrustaspikanMotionPlaybackBuilder.PrefabPath);
        if (collection == null || prefab == null) throw new InvalidOperationException("Native boss rig missing.");
        var preview = EditorSceneManager.NewPreviewScene(); GameObject clone = null;
        var pending = new Dictionary<string, AnimationClip>();
        try
        {
            clone = Object.Instantiate(prefab); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone, preview);
            clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var component in clone.GetComponentsInChildren<MonoBehaviour>(true)) component.enabled = false;
            var animator = clone.GetComponent<EnemyActor>().Animator; animator.enabled = false; animator.fireEvents = false;
            var idle = collection.FindMotion("IdleBreathe").runtime;
            idle.SampleAnimation(animator.gameObject, idle.length * .23f);
            var bones = animator.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == "root" || t.name.StartsWith("Crustaspikan_", StringComparison.Ordinal))
                .OrderBy(t => Depth(t, animator.transform)).Select(t => new SampledBone {
                    bone = t, baselinePosition = t.position, baselineRotation = t.rotation, localScale = t.localScale,
                    blenderBaseline = ReadMatrix(baseline[t.name]?["world"]) }).ToArray();
            if (bones.Length != 54 || baseline.Count != bones.Length) throw new InvalidDataException("Reviewed skeleton differs from the actor.");
            var head = bones.Single(b => b.bone.name == "Crustaspikan_ Head");
            var foot = bones.Single(b => b.bone.name == "Crustaspikan_ L Foot");
            float scale = Vector3.Distance(head.baselinePosition, foot.baselinePosition)
                / Vector3.Distance(head.blenderBaseline.GetColumn(3), foot.blenderBaseline.GetColumn(3));
            foreach (string name in new[] { "Loop", "Enter", "Recover" })
            {
                var spec = (JObject)document["clips"][name]; var frames = (JArray)spec["frames"];
                float duration = (float)spec["duration"]; bool looping = (bool)spec["loop"];
                if (frames.Count < 2 || Mathf.Abs(duration - (frames.Count - 1) / 30f) > .0001f
                    || looping != (name == "Loop") || name == "Loop" && Mathf.Abs(duration - CycleSeconds) > .0001f)
                    throw new InvalidDataException("Reviewed clip timing differs: " + name);
                foreach (var b in bones) { b.positions.Clear(); b.rotations.Clear(); }
                foreach (JObject frame in frames)
                {
                    if (frame.Count != bones.Length) throw new InvalidDataException("Incomplete bone sample.");
                    foreach (var b in bones)
                    {
                        var pose = ReadMatrix(frame[b.bone.name]?["world"]);
                        var position = (Vector3)pose.GetColumn(3);
                        var original = (Vector3)b.blenderBaseline.GetColumn(3);
                        b.bone.SetPositionAndRotation(b.baselinePosition + BlenderToUnity(position - original) * scale,
                            WorldRotationDelta(pose, b.blenderBaseline) * b.baselineRotation);
                        b.bone.localScale = b.localScale;
                    }
                    foreach (var b in bones)
                    {
                        var q = b.bone.localRotation;
                        if (b.rotations.Count > 0 && Quaternion.Dot(q, b.rotations[b.rotations.Count - 1]) < 0f)
                            q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        b.positions.Add(b.bone.localPosition); b.rotations.Add(q);
                    }
                }
                var clip = new AnimationClip { name = "AN_Crustaspikan_ParryDazed" + (name == "Loop" ? "" : name), frameRate = 30, legacy = false,
                    wrapMode = looping ? WrapMode.Loop : WrapMode.ClampForever };
                pending.Add(name, clip);
                foreach (var b in bones)
                {
                    if (looping)
                    {
                        b.positions[b.positions.Count - 1] = b.positions[0]; b.rotations[b.rotations.Count - 1] = b.rotations[0];
                    }
                    string path = AnimationUtility.CalculateTransformPath(b.bone, animator.transform);
                    if (string.IsNullOrEmpty(path)) throw new InvalidDataException("ActorRoot authoring is forbidden.");
                    for (int axis = 0; axis < 3; axis++)
                    {
                        int n = axis; string suffix = "xyz"[n].ToString();
                        SetSampleCurve(clip, path, "m_LocalPosition." + suffix, duration, b.positions.Select(v => v[n]).ToArray());
                        SetSampleCurve(clip, path, "m_LocalScale." + suffix, duration, new[] { b.localScale[n], b.localScale[n] });
                    }
                    for (int axis = 0; axis < 4; axis++)
                    {
                        int n = axis; SetSampleCurve(clip, path, "m_LocalRotation." + "xyzw"[n], duration, b.rotations.Select(q => q[n]).ToArray());
                    }
                }
                clip.EnsureQuaternionContinuity();
                var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = looping; settings.loopBlend = false;
                AnimationUtility.SetAnimationClipSettings(clip, settings); AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
            }
            // All three clips are constructed before any persistent asset is replaced.
            foreach (var pair in pending)
            {
                string path = pair.Key == "Loop" ? ClipPath : pair.Key == "Enter" ? EnterClipPath : RecoverClipPath;
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (existing == null) AssetDatabase.CreateAsset(pair.Value, path);
                else { EditorUtility.CopySerialized(pair.Value, existing); EditorUtility.SetDirty(existing); }
                AssetDatabase.SaveAssetIfDirty(existing != null ? existing : pair.Value);
            }
            EnsureNativeAssets();
            Write("blender-daze-native-import.json", new { status = "PASS_NATIVE_AUTHORED_IMPORT", sourceFile,
                sourceSha256 = SourceHash(sourceFile), bones = bones.Length, fps = 30,
                loop = ClipPath, enter = EnterClipPath, recover = RecoverClipPath, calibration = "native Idle at .23; handedness and per-bone rest rotation" });
            return "Blender-authored enter/loop/recovery baked onto the existing native rig.";
        }
        finally
        {
            foreach (var clip in pending.Values) if (clip != null && !EditorUtility.IsPersistent(clip)) Object.DestroyImmediate(clip);
            if (clone != null) Object.DestroyImmediate(clone); EditorSceneManager.ClosePreviewScene(preview);
        }
    }
    static string SourceHash(string path)
    { using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
}
