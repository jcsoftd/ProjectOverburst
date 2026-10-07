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
using Object = UnityEngine.Object;

// Reviewed Blender samples use the same per-bone native Idle calibration as the standing daze.
public static class CrustaspikanParryRecoilBuilder
{
    public const string Folder = CrustaspikanMotionPlaybackBuilder.Root + "/LicensedClips/ParryRecoil";
    public const string ProfilePath = CrustaspikanMotionPlaybackBuilder.Root + "/Authored/PRP_Crustaspikan.asset";
    public const string EncounterPath = "Assets/ProjectOverburst/Resources/Enemies/Bosses/CrustaspikanEncounter/CE_Crustaspikan.asset";
    public static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출/Boss/20261007_CrustaspikanParryRecoilApply"));
    static string ClipPath(string id) => Folder + "/AN_Crustaspikan_ParryRecoil_" + id + "_v03.anim";
    static string StateName(string id) => "Material_ParryRecoil_" + id;
    static void Write(string name, object value)
    { Directory.CreateDirectory(Path.Combine(Output, "Evidence")); File.WriteAllText(Path.Combine(Output, "Evidence", name), JsonConvert.SerializeObject(value, Formatting.Indented)); }
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


    public static string ImportReviewedSamples(string sourceFile)
    {
        CrustaspikanMotionPlaybackBuilder.RequireIdle();
        var document = JObject.Parse(File.ReadAllText(sourceFile));
        if ((int)document["fps"] != 30) throw new InvalidDataException("Expected the reviewed 30fps authoring samples.");
        var baseline = (JObject)document["baseline"];
        if ((bool)document["rootMotion"] || (float)document["playbackSpeed"] != 1.5f) throw new InvalidDataException("Reviewed recoil contract differs.");
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
            foreach (var property in ((JObject)document["clips"]).Properties())
            {
                string name = property.Name;
                var spec = (JObject)document["clips"][name]; var frames = (JArray)spec["frames"];
                float duration = (float)spec["duration"]; bool looping = false;
                if (frames.Count < 2 || Mathf.Abs(duration - (frames.Count - 1) / 30f) > .0001f)
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
                var clip = new AnimationClip { name = "AN_Crustaspikan_ParryRecoil_" + name + "_v03", frameRate = 30, legacy = false,
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

            if (pending.Count != 10) throw new InvalidDataException("Exactly ten reviewed recoil motions required.");
            foreach (string id in pending.Keys)
                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath(id)) != null)
                    throw new InvalidOperationException("Existing recoil clip requires an explicit reviewed replacement: " + id);
            if (AssetDatabase.LoadAssetAtPath<CrustaspikanParryRecoilProfile>(ProfilePath) != null)
                throw new InvalidOperationException("Recoil profile already exists; inspect before applying twice.");
            Directory.CreateDirectory(Path.GetFullPath(Folder)); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var pair in pending) { AssetDatabase.CreateAsset(pair.Value, ClipPath(pair.Key)); AssetDatabase.SaveAssetIfDirty(pair.Value); }
            var profile = ScriptableObject.CreateInstance<CrustaspikanParryRecoilProfile>();
            profile.playbackSpeed = 1.5f; profile.poseBlendSeconds = .08f;
            profile.motions = pending.Select(pair => new CrustaspikanParryRecoilProfile.Motion {
                attack = (string)document["clips"][pair.Key]["attack"], strikeIndex = (int)document["clips"][pair.Key]["strikeIndex"],
                state = StateName(pair.Key), clip = pair.Value, sourceFrame = (int)document["clips"][pair.Key]["chosenFrame"],
                contactNormalized = (float)document["clips"][pair.Key]["contactNormalized"] }).ToArray();
            AssetDatabase.CreateAsset(profile, ProfilePath); AssetDatabase.SaveAssetIfDirty(profile);
            EnsureNativeAssets(); ValidateNative();
            Write("native-import.json", new { status = "PASS_NATIVE_IMPORT", input = sourceFile, sourceSha256 = SourceHash(sourceFile),
                bones = bones.Length, clips = profile.motions.Length, rootMotion = false, speed = profile.playbackSpeed,
                calibration = "Existing native Idle at .23; same handedness and per-bone rest rotation as current standing daze" });
            return "Ten reviewed recoil clips baked; first-hit and final-hit parries configured.";
        }
        finally
        {
            foreach (var clip in pending.Values) if (clip != null && !EditorUtility.IsPersistent(clip)) Object.DestroyImmediate(clip);
            if (clone != null) Object.DestroyImmediate(clone); EditorSceneManager.ClosePreviewScene(preview);
        }
    }
    public static void EnsureNativeAssets()
    {
        CrustaspikanMotionPlaybackBuilder.RequireIdle();
        var profile = AssetDatabase.LoadAssetAtPath<CrustaspikanParryRecoilProfile>(ProfilePath);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CrustaspikanMotionPlaybackBuilder.ControllerPath);
        if (profile == null || controller == null || profile.motions.Length != 10) throw new InvalidOperationException("Reviewed dependencies missing.");
        foreach (var motion in profile.motions)
        {
            if (!motion.IsValid) throw new InvalidOperationException("Recoil entry invalid.");
            var machine = controller.layers[0].stateMachine;
            var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == motion.state) ?? machine.AddState(motion.state);
            if (state.motion != null && state.motion != motion.clip) throw new InvalidOperationException("State collision: " + motion.state);
            state.motion = motion.clip; state.speed = 1f; state.speedParameterActive = false; state.writeDefaultValues = true;
            EditorUtility.SetDirty(state);
        }
        EditorUtility.SetDirty(controller); AssetDatabase.SaveAssetIfDirty(controller);
        var contents = PrefabUtility.LoadPrefabContents(CrustaspikanMotionPlaybackBuilder.PrefabPath);
        try
        {
            var reaction = contents.GetComponent<CrustaspikanTemporaryReaction>();
            if (reaction == null) throw new InvalidOperationException("Runtime recoil consumer missing.");
            var so = new SerializedObject(reaction); var field = so.FindProperty("parryRecoilProfile");
            if (field.objectReferenceValue != null && field.objectReferenceValue != profile) throw new InvalidOperationException("Consumer changed externally.");
            field.objectReferenceValue = profile; so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(contents, CrustaspikanMotionPlaybackBuilder.PrefabPath, out bool saved);
            if (!saved) throw new IOException("Recoil prefab save failed.");
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        var encounter = AssetDatabase.LoadAssetAtPath<CrustaspikanEncounterSettings>(EncounterPath);
        foreach (string id in new[] { "2HitComboAttack", "2HitComboAttackForward" })
        { var rule = encounter.Rule(id); if (rule == null) throw new InvalidOperationException("Combo rule missing."); rule.firstHitParry = true; }
        EditorUtility.SetDirty(encounter); AssetDatabase.SaveAssetIfDirty(encounter);
    }
    public static void ValidateNative()
    {
        var profile = AssetDatabase.LoadAssetAtPath<CrustaspikanParryRecoilProfile>(ProfilePath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CrustaspikanMotionPlaybackBuilder.PrefabPath);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CrustaspikanMotionPlaybackBuilder.ControllerPath);
        var collection = AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(CrustaspikanMaterialBuilder.CollectionPath);
        if (profile == null || profile.motions.Length != 10 || profile.playbackSpeed != 1.5f
            || prefab.GetComponent<CrustaspikanTemporaryReaction>().ParryRecoilProfile != profile) throw new InvalidOperationException("Recoil consumer contract differs.");
        var preview = EditorSceneManager.NewPreviewScene(); GameObject clone = null;
        var results = new List<object>();
        try
        {
            clone = Object.Instantiate(prefab); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone, preview);
            foreach (var component in clone.GetComponentsInChildren<MonoBehaviour>(true)) component.enabled = false;
            var animator = clone.GetComponent<EnemyActor>().Animator; animator.enabled = false; animator.fireEvents = false;
            var bones = animator.GetComponentsInChildren<Transform>(true).Where(t => t.name == "root" || t.name.StartsWith("Crustaspikan_", StringComparison.Ordinal)).ToArray();
            var loop = prefab.GetComponent<CrustaspikanTemporaryReaction>().ParryDazedClip;
            foreach (var motion in profile.motions)
            {
                var state = controller.layers[0].stateMachine.states.Select(s => s.state).Single(s => s.name == motion.state);
                var bindings = AnimationUtility.GetCurveBindings(motion.clip);
                if (!motion.IsValid || state.motion != motion.clip || state.speed != 1f || state.speedParameterActive || bindings.Length != 540
                    || bindings.Any(b => string.IsNullOrEmpty(b.path)) || AnimationUtility.GetAnimationEvents(motion.clip).Length != 0)
                    throw new InvalidOperationException("Native clip/state contract differs: " + motion.attack);
                motion.clip.SampleAnimation(animator.gameObject, motion.clip.length);
                var positions = bones.Select(b => b.localPosition).ToArray(); var rotations = bones.Select(b => b.localRotation).ToArray();
                loop.SampleAnimation(animator.gameObject, 0f);
                float joinPosition = bones.Select((b, i) => Vector3.Distance(b.localPosition, positions[i])).Max();
                float joinRotation = bones.Select((b, i) => Quaternion.Angle(b.localRotation, rotations[i])).Max();
                if (joinPosition > .005f || joinRotation > .1f) throw new InvalidOperationException("Loop handoff differs: " + motion.attack + "/" + joinPosition + "/" + joinRotation);
                var attack = collection.attacks.Single(a => a.runtimeClip.name == motion.attack);
                var tuning = Object.Instantiate(attack);
                try
                {
                    tuning.tuning = new EnemyBossAttackTuning { parries = attack.strikes.Select(s => new EnemyBossStrikeParryTuning()).ToArray() };
                    profile.ApplyWindows(tuning);
                    if (!tuning.IsValid) throw new InvalidOperationException("Authored parry timing invalid: " + motion.attack);
                }
                finally { Object.DestroyImmediate(tuning); }
                results.Add(new { attack = motion.attack, strikeIndex = motion.strikeIndex, sourceFrame = motion.sourceFrame,
                    contactNormalized = motion.contactNormalized, clip = AssetDatabase.GetAssetPath(motion.clip), guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(motion.clip)),
                    clipSeconds = motion.clip.length, recoilSeconds = motion.clip.length / profile.playbackSpeed, loopJoinPosition = joinPosition, loopJoinDegrees = joinRotation });
            }
            Write("native-validation.json", new { status = "PASS_NATIVE", motions = results, speed = profile.playbackSpeed, directStandingLoop = true,
                existingDazeCycleSeconds = loop.length, rootMotion = animator.applyRootMotion,
                missingScripts = clone.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)) });
        }
        finally { if (clone != null) Object.DestroyImmediate(clone); EditorSceneManager.ClosePreviewScene(preview); }
    }
    static string SourceHash(string path)
    { using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
}
