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

// Adds opt-in playback states to the existing controller. Supplier clips and attack data remain native references.
public static class CrustaspikanMotionPlaybackBuilder
{
    public const string Root = CrustaspikanMaterialBuilder.Root;
    public const string ProfilePath = Root + "/MPP_Crustaspikan.asset";
    public const string PrefabPath = Root + "/PF_CrustaspikanMaterials.prefab";
    public const string ControllerPath = Root + "/AC_Crustaspikan.controller";
    public const float WalkCycleSeconds = 2.6666667f;
    static readonly string[] Turns = { "Turn90Left", "Turn90Right", "Turn180Left", "Turn180Right" };
    static readonly string[] Gait = { "WalkForward", "WalkBackwards", "WalkLeft", "WalkRight", "WalkForwardWithRock", "WalkBackwardsWithRock" };
    static readonly string[] Prerequisites = { "CFG-01", "CFG-02", "CFG-03", "CFG-04", "OWN-06", "OWN-07", "TURN-01", "TURN-02", "TURN-03", "TURN-04", "TURN-05", "MOVE-04", "SUP-04", "REACT-04", "TIME-02" };
    public static string Project => Path.GetDirectoryName(Application.dataPath);
    public static string Output => Path.GetFullPath(Path.Combine(Project, "../개인파일/코덱스산출/Boss/20261006_CrustaspikanAnimationImplementation"));

    public static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Editor is busy.");
        if (IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
            throw new InvalidOperationException("Another account owns the Editor.");
    }
    static string Hash(string path)
    { using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
    static Dictionary<string, string> ProtectedFiles(EnemyBossMaterialCollection collection)
    {
        var paths = new HashSet<string> { CrustaspikanMaterialBuilder.CollectionPath, Root + "/Definition.asset" };
        foreach (var a in collection.attacks) { paths.Add(AssetDatabase.GetAssetPath(a)); paths.Add(AssetDatabase.GetAssetPath(a.ability)); }
        foreach (var m in collection.motions) { if (m.runtime != null) paths.Add(AssetDatabase.GetAssetPath(m.runtime)); if (m.source != null) paths.Add(AssetDatabase.GetAssetPath(m.source)); }
        return paths.SelectMany(p => new[] { p, p + ".meta" }).Where(p => File.Exists(Path.Combine(Project, p))).ToDictionary(p => p, p => Hash(Path.Combine(Project, p)));
    }
    static void AddParameter(AnimatorController c, string name, AnimatorControllerParameterType type, float initial = 0f)
    {
        var p = c.parameters.FirstOrDefault(x => x.name == name);
        if (p != null && p.type != type) throw new InvalidOperationException("Wrong parameter type: " + name);
        if (p == null) c.AddParameter(new AnimatorControllerParameter { name = name, type = type, defaultFloat = initial });
    }
    static AnimatorState State(AnimatorController c, string name)
    { var sm = c.layers[0].stateMachine; return sm.states.Select(x => x.state).FirstOrDefault(x => x.name == name) ?? sm.AddState(name); }
    static BlendTree Tree(AnimatorController c, string name, BlendTreeType type, string x, string y = null)
    {
        var tree = AssetDatabase.LoadAllAssetsAtPath(ControllerPath).OfType<BlendTree>().FirstOrDefault(t => t.name == name);
        if (tree == null) { tree = new BlendTree { name = name }; AssetDatabase.AddObjectToAsset(tree, c); }
        tree.blendType = type; tree.blendParameter = x; if (y != null) tree.blendParameterY = y; tree.useAutomaticThresholds = false; return tree;
    }
    static ChildMotion Child(AnimationClip clip, Vector2 position, float threshold, float duration)
        => new ChildMotion { motion = clip, position = position, threshold = threshold, timeScale = clip.length / duration };
    static void Rate(AnimatorState state, string parameter)
    { state.speed = 1f; state.speedParameter = parameter; state.speedParameterActive = true; EditorUtility.SetDirty(state); }
    sealed class Calibration
    {
        public string id;
        public AnimationCurve turn;
        public EnemyMotionPlaybackProfile.Contact[] contacts;
        public float stride;
        public object[] samples;
    }
    static Calibration Sample(EnemyBossMaterialCollection.Motion motion, Animator model, Transform l, Transform r)
    {
        var clip = motion.runtime;
        int count = Mathf.CeilToInt(clip.length * clip.frameRate);
        var left = new Vector3[count + 1]; var right = new Vector3[count + 1];
        for (int i = 0; i <= count; i++)
        {
            clip.SampleAnimation(model.gameObject, clip.length * i / count);
            left[i] = model.transform.InverseTransformPoint(l.position) * model.transform.lossyScale.x;
            right[i] = model.transform.InverseTransformPoint(r.position) * model.transform.lossyScale.x;
        }
        var markers = new List<EnemyMotionPlaybackProfile.Contact>();
        float strength = motion.id.StartsWith("Turn", StringComparison.Ordinal) ? .8f : 1f;
        foreach (var pair in new[] { (left, l.name), (right, r.name) })
        {
            float low = pair.Item1.Min(v => v.y), high = pair.Item1.Max(v => v.y), range = high - low;
            if (range < .025f) throw new InvalidOperationException("No sampled foot lift: " + motion.id + " " + pair.Item2);
            float threshold = low + range * .14f;
            float last = -1f;
            for (int i = 1; i < count; i++)
                if (pair.Item1[i - 1].y > threshold && pair.Item1[i].y <= threshold && (float)i / count - last > .12f)
                {
                    last = (float)i / count; clip.SampleAnimation(model.gameObject, clip.length * last);
                    var foot = pair.Item2 == l.name ? l : r;
                    var sole = new Vector3(foot.position.x, model.transform.root.position.y, foot.position.z);
                    markers.Add(new EnemyMotionPlaybackProfile.Contact { bone = pair.Item2, normalized = last, strength = strength, localSoleOffset = foot.InverseTransformPoint(sole) });
                }
        }
        var progress = new float[count + 1];
        float minL = left.Min(v => v.y), minR = right.Min(v => v.y);
        float heightL = Mathf.Max(.025f, left.Max(v => v.y) - minL), heightR = Mathf.Max(.025f, right.Max(v => v.y) - minR);
        float groundTravel = 0f, groundSeconds = 0f;
        for (int i = 1; i <= count; i++)
        {
            float airL = Mathf.Clamp01((left[i].y - minL) / heightL), airR = Mathf.Clamp01((right[i].y - minR) / heightR);
            Vector3 dl = left[i] - left[i - 1], dr = right[i] - right[i - 1]; dl.y = dr.y = 0f;
            // In-place clips contain no authored root yaw. Turn the motor during the sampled stepping intervals.
            progress[i] = progress[i - 1] + dl.magnitude * airL + dr.magnitude * airR + .00001f;
            if (airL < .14f) { groundTravel += dl.magnitude; groundSeconds += clip.length / count; }
            if (airR < .14f) { groundTravel += dr.magnitude; groundSeconds += clip.length / count; }
        }
        float total = progress[count]; var curve = new AnimationCurve();
        for (int i = 0; i <= count; i++) curve.AddKey((float)i / count, progress[i] / total);
        for (int i = 0; i < curve.length; i++) { AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear); }
        return new Calibration { id = motion.id, turn = curve, contacts = markers.OrderBy(x => x.normalized).ToArray(),
            stride = Mathf.Max(.05f, groundTravel / Mathf.Max(.001f, groundSeconds)) * clip.length / WalkCycleSeconds,
            samples = Enumerable.Range(0, count + 1).Select(i => (object)new { frame = i, normalized = (float)i / count, progress = progress[i] / total,
                left = new[] { left[i].x, left[i].y, left[i].z }, right = new[] { right[i].x, right[i].y, right[i].z } }).ToArray() };
    }

    public static string BuildDraft()
    {
        RequireIdle(); Directory.CreateDirectory(Output + "/Evidence");
        File.WriteAllText(Output + "/Evidence/native-draft-receipt.json", JsonConvert.SerializeObject(new { status = "STARTED", utc = DateTime.UtcNow }, Formatting.Indented));
        try { return BuildDraftInternal(); }
        catch (Exception error)
        {
            File.WriteAllText(Output + "/Evidence/native-draft-receipt.json", JsonConvert.SerializeObject(new { status = "FAILED", error = error.ToString(), utc = DateTime.UtcNow }, Formatting.Indented));
            throw;
        }
    }
    static string BuildDraftInternal()
    {
        RequireIdle(); Directory.CreateDirectory(Output + "/Evidence");
        CrustaspikanParryDazedBuilder.EnsureNativeAssets();
        var c = AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(CrustaspikanMaterialBuilder.CollectionPath);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (c == null || controller == null || prefab == null) throw new InvalidOperationException("Existing native boss material data missing.");
        var protectedBefore = ProtectedFiles(c); var controllerGuid = AssetDatabase.AssetPathToGUID(ControllerPath); var prefabGuid = AssetDatabase.AssetPathToGUID(PrefabPath);
        var beforeScenes = Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount).Select(i => { var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); return new { s.path, s.isDirty }; }).ToArray();
        var calibrations = new Dictionary<string, Calibration>();
        var preview = EditorSceneManager.NewPreviewScene(); GameObject clone = null;
        try
        {
            clone = Object.Instantiate(prefab); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone, preview);
            var model = clone.GetComponentInChildren<Animator>(true); model.enabled = false; model.fireEvents = false;
            var bones = model.GetComponentsInChildren<Transform>(true);
            var l = bones.Single(t => t.name == "Crustaspikan_ L Foot"); var r = bones.Single(t => t.name == "Crustaspikan_ R Foot");
            foreach (string id in Turns.Concat(Gait)) calibrations.Add(id, Sample(c.FindMotion(id), model, l, r));
        }
        finally { if (clone != null) Object.DestroyImmediate(clone); EditorSceneManager.ClosePreviewScene(preview); }
        File.WriteAllText(Output + "/Evidence/native-calibration.json", JsonConvert.SerializeObject(calibrations.Values.Select(x => new { x.id, contacts = x.contacts.Select(m => new { m.bone, m.normalized, m.strength, soleOffset = new[] { m.localSoleOffset.x, m.localSoleOffset.y, m.localSoleOffset.z } }), x.stride, x.samples }), Formatting.Indented));
        foreach (string name in new[] { "BossMoveX", "BossMoveZ", "TurnMagnitude" }) AddParameter(controller, name, AnimatorControllerParameterType.Float);
        AddParameter(controller, "BossMotionRate", AnimatorControllerParameterType.Float, 1f); AddParameter(controller, "OwnedMotionPlayback", AnimatorControllerParameterType.Bool);
        var walk = Tree(controller, "BossLocomotion2D", BlendTreeType.FreeformDirectional2D, "BossMoveX", "BossMoveZ");
        walk.children = new[] { Child(c.FindMotion("IdleBreathe").runtime, Vector2.zero, 0, WalkCycleSeconds), Child(c.FindMotion("WalkForward").runtime, Vector2.up, 0, WalkCycleSeconds),
            Child(c.FindMotion("WalkBackwards").runtime, Vector2.down, 0, WalkCycleSeconds), Child(c.FindMotion("WalkLeft").runtime, Vector2.left, 0, WalkCycleSeconds), Child(c.FindMotion("WalkRight").runtime, Vector2.right, 0, WalkCycleSeconds) };
        State(controller, "BossLocomotion").motion = walk; Rate(State(controller, "BossLocomotion"), "BossMotionRate");
        var carry = Tree(controller, "BossCarry2D", BlendTreeType.Simple1D, "BossMoveZ");
        carry.children = new[] { Child(c.FindMotion("WalkBackwardsWithRock").runtime, Vector2.zero, -1, WalkCycleSeconds), Child(c.FindMotion("WalkForwardWithRock").runtime, Vector2.zero, 1, WalkCycleSeconds) };
        State(controller, "BossCarryLocomotion").motion = carry; Rate(State(controller, "BossCarryLocomotion"), "BossMotionRate");
        foreach (string id in Turns)
        {
            var clip = c.FindMotion(id).runtime; var tree = Tree(controller, "Boss" + id + "Magnitude", BlendTreeType.Simple1D, "TurnMagnitude");
            tree.children = new[] { Child(c.FindMotion("IdleBreathe").runtime, Vector2.zero, 0, clip.length), Child(clip, Vector2.zero, 1, clip.length) };
            State(controller, "Boss" + id).motion = tree; Rate(State(controller, "Boss" + id), "BossMotionRate");
        }
        foreach (var a in c.attacks)
        {
            string stateName = "Attack_" + a.ability.AnimatorTrigger.Substring("Attack".Length); var state = State(controller, stateName);
            if (state.motion != a.runtimeClip) throw new InvalidOperationException("Attack clip changed: " + stateName);
            foreach (var transition in state.transitions)
                if (!transition.conditions.Any(x => x.parameter == "OwnedMotionPlayback")) transition.AddCondition(AnimatorConditionMode.IfNot, 0, "OwnedMotionPlayback");
        }
        var bindings = new List<EnemyMotionPlaybackProfile.Binding>();
        foreach (var m in c.motions.Where(x => x.IsPlayable))
        {
            var b = new EnemyMotionPlaybackProfile.Binding { motionId = m.id, sourceMotionId = m.id, state = m.state, rateParameter = "BossMotionRate",
                lifetime = m.runtime.isLooping ? EnemyMotionLifetime.Continuous : EnemyMotionLifetime.OneShot,
                ratePolicy = m.runtime.isLooping ? EnemyMotionRatePolicy.LiveLocomotion : EnemyMotionRatePolicy.SnapshotAtStart, updateMode = AnimatorUpdateMode.Normal };
            if (Turns.Contains(m.id)) { b.state = "Boss" + m.id; b.rate = 1.25f; b.authoredYaw = m.id.StartsWith("Turn180") ? 180f : 90f; b.turnProgress = calibrations[m.id].turn; }
            else Rate(State(controller, m.state), "BossMotionRate");
            if (Gait.Contains(m.id)) { b.state = m.id.EndsWith("WithRock", StringComparison.Ordinal) ? "BossCarryLocomotion" : "BossLocomotion"; b.strideSpeed = calibrations[m.id].stride; }
            if (calibrations.TryGetValue(m.id, out var sample)) b.contacts = sample.contacts;
            if (m.id == "Death") { b.state = "Death"; b.lifetime = EnemyMotionLifetime.Terminal; Rate(State(controller, "Death"), "BossMotionRate"); }
            bindings.Add(b);
        }
        bindings.Add(new EnemyMotionPlaybackProfile.Binding { motionId = "Locomotion", sourceMotionId = "IdleBreathe", state = "BossLocomotion", rateParameter = "BossMotionRate", lifetime = EnemyMotionLifetime.Continuous, ratePolicy = EnemyMotionRatePolicy.LiveLocomotion });
        bindings.Add(new EnemyMotionPlaybackProfile.Binding { motionId = "CarryLocomotion", sourceMotionId = "WalkForwardWithRock", state = "BossCarryLocomotion", rateParameter = "BossMotionRate", lifetime = EnemyMotionLifetime.Continuous, ratePolicy = EnemyMotionRatePolicy.LiveLocomotion });
        bindings.Add(new EnemyMotionPlaybackProfile.Binding { motionId = "ReactionPose", sourceMotionId = "Death", state = "Material_Death", rateParameter = "BossMotionRate", lifetime = EnemyMotionLifetime.HeldPose, ratePolicy = EnemyMotionRatePolicy.OwnedPose });
        bindings.Add(new EnemyMotionPlaybackProfile.Binding { motionId = "FrozenPose", sourceMotionId = "IdleBreathe", state = "Material_IdleBreathe", rateParameter = "BossMotionRate", lifetime = EnemyMotionLifetime.HeldPose, ratePolicy = EnemyMotionRatePolicy.OwnedPose });
        bindings.Add(new EnemyMotionPlaybackProfile.Binding { motionId = "GetHitReaction", sourceMotionId = "GetHitFront", state = "Material_GetHitFront", rateParameter = "BossMotionRate", ratePolicy = EnemyMotionRatePolicy.SnapshotAtStart });
        foreach (var a in c.attacks)
            bindings.Add(new EnemyMotionPlaybackProfile.Binding { motionId = "Attack_" + a.ability.AnimatorTrigger, sourceMotionId = c.motions.Single(m => m.runtime == a.runtimeClip).id, state = "Attack_" + a.ability.AnimatorTrigger.Substring("Attack".Length), rateParameter = "AttackAnimSpeed", ratePolicy = EnemyMotionRatePolicy.LivePhased });
        var profile = AssetDatabase.LoadAssetAtPath<EnemyMotionPlaybackProfile>(ProfilePath);
        if (profile == null) { profile = ScriptableObject.CreateInstance<EnemyMotionPlaybackProfile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }
        profile.Configure(c, bindings.ToArray(), EnemyMotionPlaybackProfile.ContractVersion);
        if (!profile.Validate(out string error)) throw new InvalidOperationException(error);
        ValidateNative(profile, controller, prefab);
        EditorUtility.SetDirty(controller); EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(controller); AssetDatabase.SaveAssetIfDirty(profile);
        if (controllerGuid != AssetDatabase.AssetPathToGUID(ControllerPath) || prefabGuid != AssetDatabase.AssetPathToGUID(PrefabPath)) throw new InvalidOperationException("Existing GUID changed.");
        foreach (var p in protectedBefore) if (Hash(Path.Combine(Project, p.Key)) != p.Value) throw new InvalidOperationException("Protected source changed: " + p.Key);
        File.WriteAllText(Output + "/Evidence/native-draft.json", JsonConvert.SerializeObject(new { status = "PASS_NATIVE_DRAFT_OPT_IN_OFF", controllerGuid, prefabGuid, bindings = bindings.Count,
            attacks = c.attacks.Length, protectedFiles = protectedBefore, calibration = calibrations.Values.Select(x => new { x.id, contacts = x.contacts.Select(m => new { m.bone, m.normalized, m.strength, soleOffset = new[] { m.localSoleOffset.x, m.localSoleOffset.y, m.localSoleOffset.z } }), x.stride, x.samples }), beforeScenes }, Formatting.Indented));
        File.WriteAllText(Output + "/Evidence/native-draft-receipt.json", JsonConvert.SerializeObject(new { status = "COMPLETE", utc = DateTime.UtcNow }, Formatting.Indented));
        return "Native draft validated; prefab opt-in remains off.";
    }

    public static void ValidateNative(EnemyMotionPlaybackProfile p, AnimatorController controller, GameObject prefab)
    {
        if (!p.Validate(out var error)) throw new InvalidOperationException(error);
        var parameters = controller.parameters.ToDictionary(x => x.name, x => x.type);
        foreach (string name in new[] { "BossMoveX", "BossMoveZ", "BossMotionRate", "TurnMagnitude", "AttackAnimSpeed" })
            if (!parameters.TryGetValue(name, out var type) || type != AnimatorControllerParameterType.Float) throw new InvalidOperationException("Missing Float: " + name);
        if (!parameters.TryGetValue("OwnedMotionPlayback", out var ownedType) || ownedType != AnimatorControllerParameterType.Bool) throw new InvalidOperationException("Missing opt-in Bool.");
        var states = controller.layers[0].stateMachine.states.Select(x => x.state).ToDictionary(x => x.name);
        foreach (var b in p.Bindings)
        {
            if (!states.TryGetValue(b.state, out var state) || !state.speedParameterActive || state.speedParameter != b.rateParameter) throw new InvalidOperationException("State/rate mismatch: " + b.motionId);
            var clip = p.ResolveClip(b); var tree = state.motion as BlendTree;
            if (tree == null ? state.motion != clip : !tree.children.Any(x => x.motion == clip)) throw new InvalidOperationException("State/clip mismatch: " + b.motionId);
            if (b.contacts.Any(x => x.normalized < 0 || x.normalized >= 1 || !EnemyMotionPlaybackProfile.FinitePositive(x.strength))) throw new InvalidOperationException("Invalid contact.");
            if (b.authoredYaw > 0)
            {
                if (tree == null || b.turnProgress == null || b.turnProgress.length < 2) throw new InvalidOperationException("Turn binding missing.");
                float prior = -1; for (int i = 0; i <= 100; i++) { float v = b.turnProgress.Evaluate(i / 100f); if (v < prior || !EnemyMotionPlaybackProfile.FiniteNonNegative(v) || v > 1.001f) throw new InvalidOperationException("Invalid turn curve."); prior = v; }
                if (Mathf.Abs(b.turnProgress.Evaluate(0)) > .0001f || Mathf.Abs(b.turnProgress.Evaluate(1) - 1) > .0001f) throw new InvalidOperationException("Incomplete turn curve.");
                foreach (var child in tree.children) if (Mathf.Abs(((AnimationClip)child.motion).length / child.timeScale - clip.length) > .001f) throw new InvalidOperationException("Magnitude changes turn clock.");
            }
        }
        foreach (var m in p.Collection.motions.Where(x => x.rootMotionVariant)) if (m.runtime != null || !string.IsNullOrEmpty(m.state)) throw new InvalidOperationException("RM entry activated.");
        var actor = prefab.GetComponent<EnemyActor>();
        if (actor == null || actor.Movement == null || actor.AbilityController == null || prefab.GetComponent<EnemyLocomotionAnimator>() == null
            || prefab.GetComponent<EnemyBossMaterialExecutor>() == null || prefab.GetComponent<EnemyBossCompositePatternExecutor>() == null
            || prefab.GetComponent<CrustaspikanTemporaryReaction>() == null || prefab.GetComponent<EnemyEliteFootstepEmitter>() == null)
            throw new InvalidOperationException("Mandatory consumer missing.");
        var so = new SerializedObject(actor.AbilityController); var array = so.FindProperty("executors");
        if (array == null || array.arraySize != 2 || !(array.GetArrayElementAtIndex(0).objectReferenceValue is EnemyBossCompositePatternExecutor)
            || !(array.GetArrayElementAtIndex(1).objectReferenceValue is EnemyBossMaterialExecutor)) throw new InvalidOperationException("Current attacks can enter an unowned executor.");
        if (!states.TryGetValue(CrustaspikanGetUpBuilder.State, out var recovery) || recovery.motion == null) throw new InvalidOperationException("Actual recovery pose missing.");
        var dazed = prefab.GetComponent<CrustaspikanTemporaryReaction>().ParryDazedClip;
        if (dazed == null || !dazed.isLooping || !states.TryGetValue(CrustaspikanTemporaryReaction.ParryDazedStateName, out var dazeState) || dazeState.motion != dazed)
            throw new InvalidOperationException("Standing parry daze loop missing.");
    }

    public static string EnableVerified(string resultFile)
    {
        RequireIdle(); var result = JObject.Parse(File.ReadAllText(resultFile)); var checks = (JArray)result["checks"];
        foreach (string id in Prerequisites) if (!checks.Any(x => (string)x["id"] == id && (string)x["status"] == "PASS")) throw new InvalidOperationException("Prerequisite not passed: " + id);
        if (!(result["hashes"] is JObject proof) || proof.Count == 0) throw new InvalidOperationException("No source/asset verification basis.");
        foreach (var hash in proof.Properties()) if (!File.Exists(Path.Combine(Project, hash.Name)) || Hash(Path.Combine(Project, hash.Name)) != (string)hash.Value) throw new InvalidOperationException("Verification basis changed: " + hash.Name);
        var profile = AssetDatabase.LoadAssetAtPath<EnemyMotionPlaybackProfile>(ProfilePath); var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        ValidateNative(profile, controller, AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var bridge = contents.GetComponent<EnemyAnimationBridge>(); var so = new SerializedObject(bridge);
            so.FindProperty("motionPlaybackProfile").objectReferenceValue = profile; so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath, out bool saved); if (!saved) throw new IOException("Opt-in prefab save failed.");
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        File.WriteAllText(Output + "/Evidence/native-enable.json", JsonConvert.SerializeObject(new { status = "ENABLED_AFTER_PREREQUISITES", proof = resultFile, profile = ProfilePath, prefab = PrefabPath }, Formatting.Indented));
        return "Native prefab opted in after isolated prerequisite validation.";
    }

    public static JObject VerificationHashes()
    {
        var c = AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(CrustaspikanMaterialBuilder.CollectionPath);
        var all = ProtectedFiles(c);
        foreach (string asset in new[] { ControllerPath, ProfilePath, PrefabPath, CrustaspikanParryDazedBuilder.ClipPath }) foreach (string p in new[] { asset, asset + ".meta" }) all[p] = Hash(Path.Combine(Project, p));
        foreach (string manifest in Directory.GetFiles(Output + "/Evidence", "stage-??.json"))
            foreach (var file in (JArray)JObject.Parse(File.ReadAllText(manifest))["files"])
            { string p = (string)file["path"]; if (File.Exists(Path.Combine(Project, p))) all[p] = Hash(Path.Combine(Project, p)); }
        return JObject.FromObject(all);
    }
}
