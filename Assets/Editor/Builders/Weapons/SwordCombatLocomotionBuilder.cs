using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SwordCombatLocomotionBuilder
{
    const string QueueKey = "Overburst.SwordCombatLocomotionBuilder.Pending";
    static double idleSince;
    static SwordCombatLocomotionBuilder()
    {
        if (!string.IsNullOrEmpty(SessionState.GetString(QueueKey, ""))) EditorApplication.update += ApplyWhenIdle;
    }
    public static string Queue(string output = DefaultOutput)
    {
        output = Path.GetFullPath(output);
        if (!output.StartsWith(Path.GetFullPath("../개인파일/코덱스산출") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Private output required.");
        Directory.CreateDirectory(output);
        SessionState.SetString(QueueKey, output);
        SessionState.SetString(QueueKey + ".Deadline", (EditorApplication.timeSinceStartup + 900).ToString(System.Globalization.CultureInfo.InvariantCulture));
        File.WriteAllText(Path.Combine(output, "ApplyQueue.json"), "{\"status\":\"WAITING_FOR_IDLE\"}");
        idleSince = 0;
        EditorApplication.update -= ApplyWhenIdle;
        EditorApplication.update += ApplyWhenIdle;
        return "Queued with reload-safe idle ownership check.";
    }
    static void ApplyWhenIdle()
    {
        string output = SessionState.GetString(QueueKey, "");
        if (string.IsNullOrEmpty(output)) { EditorApplication.update -= ApplyWhenIdle; return; }
        bool busy = EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating;
        double deadline = double.Parse(SessionState.GetString(QueueKey + ".Deadline", "0"), System.Globalization.CultureInfo.InvariantCulture);
        if (busy && EditorApplication.timeSinceStartup < deadline) { idleSince = 0; return; }
        if (!busy && idleSince == 0) { idleSince = EditorApplication.timeSinceStartup; return; }
        if (!busy && EditorApplication.timeSinceStartup - idleSince < .5) return;
        EditorApplication.update -= ApplyWhenIdle;
        SessionState.EraseString(QueueKey); SessionState.EraseString(QueueKey + ".Deadline");
        string path = Path.Combine(output, "ApplyQueue.json");
        try {
            if (busy) throw new Exception("Idle queue expired; no asset mutation performed.");
            File.WriteAllText(path, "{\"status\":\"RUNNING\"}");
            EditorApplication.LockReloadAssemblies();
            string result = Apply(output);
            File.WriteAllText(path, JsonConvert.SerializeObject(new { status = "PASS", result }));
        } catch (Exception e) {
            File.WriteAllText(path, JsonConvert.SerializeObject(new { status = "FAIL", error = e.ToString() }));
        } finally { if (!busy) EditorApplication.UnlockReloadAssemblies(); }
    }
    public const string Root = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Animation/SwordCombatLocomotion";
    const string Source = "Assets/ThirdParty/03_애니메이션/Sword_Animations_Pack/Animation/Humanoid/";
    const string ProfilePath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Animation/GreatswordCombatAnimationProfile.asset";
    public const string DefaultOutput = "../개인파일/코덱스산출/Animation/20261004_SwordCombatFacing";
    static readonly string[] Suffix = { "F_0", "F_R_45", "F_R_90", "B_R_45", "B_180", "B_L_45", "F_L_90", "F_L_45" };
    static readonly int[] Folders = { 1, 3, 5, 8, 6, 7, 4, 2 };
    static readonly float[] Speeds = { 3.876f, 3.579f, 4.020f, 4.811f, 4.031f, 4.812f, 4.020f, 3.579f };

    public static string Apply(string output = DefaultOutput)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Editor required.");
        output = Path.GetFullPath(output);
        if (!output.StartsWith(Path.GetFullPath("../개인파일/코덱스산출") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Private output required.");
        Directory.CreateDirectory(output);
        var profile = AssetDatabase.LoadAssetAtPath<WeaponCombatAnimationProfile>(ProfilePath);
        var aoc = profile.animatorOverrideController;
        var ac = aoc.runtimeAnimatorController as AnimatorController;
        if (ac == null) throw new Exception("Existing melee controller required.");
        var owned = new UnityEngine.Object[] { profile, aoc, ac };
        if (owned.Any(EditorUtility.IsDirty)) throw new Exception("Preserve unsaved owned asset edits.");
        var sceneBefore = SceneState();
        var sourceRecords = new List<object>();
        var originalBytes = owned.ToDictionary(AssetDatabase.GetAssetPath, p => File.ReadAllBytes(AssetDatabase.GetAssetPath(p)));
        Directory.CreateDirectory(Path.Combine(output, "Backup"));
        foreach (var p in originalBytes.Keys)
        {
            string dest = Path.Combine(output, "Backup", Path.GetFileName(p));
            if (!File.Exists(dest)) { File.WriteAllBytes(dest, originalBytes[p]); File.Copy(p + ".meta", dest + ".meta"); }
        }
        var oldClips = profile.locomotion.ToArray();
        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        aoc.GetOverrides(overrides);
        var keys = new Dictionary<AnimationClip, int>();
        for (int i = 0; i < oldClips.Length; i++)
        {
            int sector = new[] { 0, 4, 6, 2, 7, 1, 5, 3 }[i];
            foreach (var entry in overrides.Where(e => e.Value == oldClips[i])) keys[entry.Key] = sector;
        }
        var created = new List<string>();
        try
        {
            EnsureFolder(Root);
            string setPath = Root + "/SwordCombatLocomotion.asset";
            var set = AssetDatabase.LoadAssetAtPath<CombatLocomotionSet>(setPath);
            if (set == null) { set = ScriptableObject.CreateInstance<CombatLocomotionSet>(); AssetDatabase.CreateAsset(set, setPath); created.Add(setPath); }
            for (int i = 0; i < 8; i++)
            {
                string dir = "04_Run/02_Run_Combat_RM/" + Folders[i].ToString("00") + "_Run_Combat_" + Suffix[i] + "_RM/";
                var start = Copy(Source + dir + "Run_Combat_Start_" + Suffix[i] + "_RM.anim", Root + "/Sword_Start_" + Suffix[i] + ".anim", false, false, true, 0, false, sourceRecords, created);
                var loop = Copy(Source + dir + "Run_Combat_Loop_" + Suffix[i] + "_RM.anim", Root + "/Sword_Loop_" + Suffix[i] + ".anim", true, false, true, 0, false, sourceRecords, created);
                var stop = Copy(Source + dir + "Run_Combat_Stop_" + Suffix[i] + "_RM.anim", Root + "/Sword_Stop_" + Suffix[i] + ".anim", false, false, true, 0, false, sourceRecords, created);
                set.directions[i] = new CombatMoveMotion { start = start, loop = loop, stop = stop,
                    startState = "Melee_SwordStart_" + i, stopState = "Melee_SwordStop_" + i,
                    loopCycleOffset = i >= 3 && i <= 5 ? .325f : 0f, authoredSpeed = Speeds[i] };
            }
            set.left90 = Turn("90_L", -.90f * 100, .70f, false, sourceRecords, created);
            set.right90 = Turn("90_R", 90, .80f, false, sourceRecords, created);
            set.left180 = Turn("180_L", -180, .95f, false, sourceRecords, created);
            set.right180 = Turn("180_R", 180, 1f, true, sourceRecords, created);
            var layer = ac.layers.First(l => l.name == "Combat_MeleeWeapon");
            layer.iKPass = true;
            var layers = ac.layers;
            layers[Array.FindIndex(layers, l => l.name == layer.name)] = layer;
            ac.layers = layers;
            State(layer.stateMachine, set.idleState, profile.combatIdleClip);
            foreach (var m in set.directions) { State(layer.stateMachine, m.startState, m.start); State(layer.stateMachine, m.stopState, m.stop); }
            foreach (var m in new[] { set.left90, set.right90, set.left180, set.right180 }) State(layer.stateMachine, m.stateName, m.clip);
            // Current gameplay speed references remain separate from the new stride reference.
            foreach (var state in States(layer.stateMachine))
                if (state.name == profile.locomotionStateName && state.motion is BlendTree tree)
                    TuneTree(tree, keys, set, profile);
            var lower = ac.layers.First(l => l.name == "Combat_MeleeWeapon_TransitionLower");
            foreach (var state in States(lower.stateMachine))
                if (state.motion is BlendTree tree) TuneTree(tree, keys, set, profile);
            var new8 = new DirectionalAnimationSet8 { forward = set.directions[0].loop, forwardRight = set.directions[1].loop,
                right = set.directions[2].loop, backwardRight = set.directions[3].loop, backward = set.directions[4].loop,
                backwardLeft = set.directions[5].loop, left = set.directions[6].loop, forwardLeft = set.directions[7].loop };
            for (int i = 0; i < overrides.Count; i++)
                if (keys.TryGetValue(overrides[i].Key, out int sector))
                    overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, set.directions[sector].loop);
            aoc.ApplyOverrides(overrides);
            profile.locomotion = new8;
            profile.combatLocomotionSet = set;
            using (var sampler = new Sampler())
            {
                sampler.Sample(profile.combatIdleClip, 0);
                var chest = sampler.Animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? sampler.Animator.GetBoneTransform(HumanBodyBones.Chest);
                set.idleChestYaw = Yaw(chest.rotation);
                foreach (var m in set.directions)
                {
                    var end = sampler.Pose(m.start, m.start.length - .0001f);
                    float best = float.MaxValue;
                    for (int k = 0; k < 80; k++)
                    {
                        float phase = k / 80f;
                        float error = PoseDistance(end, sampler.Pose(m.loop, phase * m.loop.length));
                        if (error < best) { best = error; m.startLoopPhase = phase; }
                    }
                }
            }
            foreach (var o in owned.Append(set)) { EditorUtility.SetDirty(o); AssetDatabase.SaveAssetIfDirty(o); }
            if (SceneState() != sceneBefore) throw new Exception("Shared scene changed.");
            AssetDatabase.ExportPackage(AssetDatabase.FindAssets("", new[] { Root }).Select(AssetDatabase.GUIDToAssetPath).ToArray(),
                Path.Combine(output, "SwordCombatLocomotion.unitypackage"), ExportPackageOptions.Default);
            File.WriteAllText(Path.Combine(output, "ApplyResult.json"), JsonConvert.SerializeObject(new {
                status = "PASS", sceneBefore, gameplayApplied = true, clips = 28, sourceRecords,
                movementSpeedReferencesPreserved = true, combatFootIkEnabled = false,
                turns = new[] { set.left90, set.right90, set.left180, set.right180 }.Select(t => new { t.stateName, t.angle, duration = t.Duration, yawEnd = t.yaw.Evaluate(t.Duration) }),
                idleChestYaw = set.idleChestYaw }, Formatting.Indented));
            return "PASS: Sword turns and eight-direction Start/Loop/Stop connected.";
        }
        catch
        {
            foreach (var p in originalBytes.Keys) { File.WriteAllBytes(p, originalBytes[p]); AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceSynchronousImport); }
            foreach (var p in created) AssetDatabase.DeleteAsset(p);
            throw;
        }
    }
    static CombatTurnMotion Turn(string suffix, float angle, float duration, bool compressPreparation, List<object> records, List<string> created)
    {
        var clip = Copy(Source + "09_Turn/02_Turn_Combat/Turn_Combat_" + suffix + ".anim",
            Root + "/Sword_Turn_" + suffix + ".anim", false, true, false, duration, compressPreparation, records, created);
        var q = new[] { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" }.Select(n => AnimationUtility.GetEditorCurve(clip,
            AnimationUtility.GetCurveBindings(clip).First(b => b.propertyName == n))).ToArray();
        var keys = new List<Keyframe>(); float last = 0, accumulated = 0;
        int count = Mathf.CeilToInt(duration * 240);
        for (int i = 0; i <= count; i++)
        {
            float t = i * duration / count;
            float now = Yaw(new Quaternion(q[0].Evaluate(t), q[1].Evaluate(t), q[2].Evaluate(t), q[3].Evaluate(t)));
            if (i > 0) accumulated += Mathf.DeltaAngle(last, now);
            keys.Add(new Keyframe(t, accumulated)); last = now;
        }
        if (Mathf.Abs(accumulated - angle) > .5f) throw new Exception("Root yaw mismatch: " + suffix + " " + accumulated);
        var curve = new AnimationCurve(keys.ToArray());
        for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0);
        return new CombatTurnMotion { clip = clip, stateName = "Melee_SwordTurn_" + suffix, angle = angle, yaw = curve };
    }
    static AnimationClip Copy(string sourcePath, string path, bool loop, bool bakeXZ, bool bakeYaw,
        float duration, bool compressPreparation, List<object> records, List<string> created)
    {
        var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(sourcePath);
        if (source == null || !source.isHumanMotion) throw new Exception("Humanoid source missing: " + sourcePath);
        string hash = SwordIdleAttackCopyBuilder.Hash(sourcePath);
        var copy = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (copy == null) { copy = new AnimationClip(); AssetDatabase.CreateAsset(copy, path); created.Add(path); }
        EditorUtility.CopySerialized(source, copy);
        copy.name = Path.GetFileNameWithoutExtension(path);
        if (duration > 0)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                var original = AnimationUtility.GetEditorCurve(source, binding);
                int count = Mathf.CeilToInt(duration * 240);
                var ks = new Keyframe[count + 1];
                for (int i = 0; i <= count; i++)
                {
                    float t = duration * i / count;
                    float normalized = t / duration;
                    float sample = compressPreparation ? Warp(normalized) * source.length : normalized * source.length;
                    ks[i] = new Keyframe(t, original.Evaluate(sample));
                }
                var curve = new AnimationCurve(ks);
                for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0);
                AnimationUtility.SetEditorCurve(copy, binding, curve);
            }
            copy.frameRate = 120;
        }
        var settings = AnimationUtility.GetAnimationClipSettings(copy);
        settings.loopTime = loop; settings.loopBlendOrientation = bakeYaw;
        settings.loopBlendPositionXZ = bakeXZ; settings.keepOriginalOrientation = true;
        settings.keepOriginalPositionXZ = true;
        if (duration > 0) { settings.startTime = 0; settings.stopTime = duration; }
        AnimationUtility.SetAnimationClipSettings(copy, settings);
        EditorUtility.SetDirty(copy); AssetDatabase.SaveAssetIfDirty(copy);
        if (SwordIdleAttackCopyBuilder.Hash(sourcePath) != hash) throw new Exception("Supplier clip changed.");
        records.Add(new { sourcePath, hash, copy = path, guid = AssetDatabase.AssetPathToGUID(path), loop, bakeXZ, bakeYaw });
        return copy;
    }
    static float Warp(float p)
    {
        // Smooth monotonic preparation compression; footsteps and root yaw use the same baked time.
        return Mathf.Clamp01(TimeWarp.Evaluate(p));
    }
    static readonly AnimationCurve TimeWarp = new AnimationCurve(new Keyframe(0, 0, 0, 5.55556f),
        new Keyframe(.09f, 1f/3f, 1.5f, 1.5f), new Keyframe(.8f, .9f, .8f, .8f), new Keyframe(1, 1, .5f, 0));
    static IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
    {
        foreach (var s in machine.states) yield return s.state;
        foreach (var m in machine.stateMachines) foreach (var s in States(m.stateMachine)) yield return s;
    }
    static string SceneState() => JsonConvert.SerializeObject(Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
    { var s = SceneManager.GetSceneAt(i); return new { s.path, s.isDirty, s.rootCount }; }));
    static void TuneTree(BlendTree tree, Dictionary<AnimationClip,int> keys, CombatLocomotionSet set, WeaponCombatAnimationProfile profile)
    {
        var children = tree.children;
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].motion is BlendTree nested) TuneTree(nested, keys, set, profile);
            else if (children[i].motion is AnimationClip clip && keys.TryGetValue(clip, out int sector))
            {
                var m = set.directions[sector];
                var direction = Quaternion.Euler(0, sector * 45f, 0) * Vector3.forward;
                children[i].timeScale = profile.locomotionReferenceSpeeds.GetSpeed(direction) / m.authoredSpeed;
                children[i].cycleOffset = m.loopCycleOffset;
            }
        }
        tree.children = children;
        EditorUtility.SetDirty(tree);
    }
    static void State(AnimatorStateMachine machine, string name, AnimationClip clip)
    {
        var state = machine.states.FirstOrDefault(s => s.state.name == name).state ?? machine.AddState(name);
        state.motion = clip; state.iKOnFeet = false; state.writeDefaultValues = true; state.speed = 1;
        EditorUtility.SetDirty(state);
    }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
        AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
    }
    static float Yaw(Quaternion q) { Vector3 v = q * Vector3.forward; return Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg; }
    static float PoseDistance(Quaternion[] a, Quaternion[] b)
    {
        float sum = 0;
        for (int i = 0; i < a.Length; i++) { float d = Quaternion.Angle(a[i], b[i]); sum += d*d; }
        return sum;
    }
    sealed class Sampler : IDisposable
    {
        UnityEngine.SceneManagement.Scene scene;
        GameObject actor;
        PlayableGraph graph;
        AnimationClipPlayable playable;
        AnimationClip current;
        public Animator Animator { get; }
        readonly HumanBodyBones[] bones = { HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot };
        public Sampler()
        {
            scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab");
                actor = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                foreach (var mb in actor.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
                Animator = actor.GetComponentInChildren<P09CharacterVisualAdapter>(true).Animator;
                foreach (var a in actor.GetComponentsInChildren<Animator>(true)) a.enabled = a == Animator;
                Animator.applyRootMotion = false; Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            catch { Dispose(); throw; }
        }
        public void Sample(AnimationClip clip, float time)
        {
            if (current != clip)
            {
                if (graph.IsValid()) graph.Destroy();
                current = clip; graph = PlayableGraph.Create("Owned Sword facing authoring"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false); playable.SetSpeed(0);
                AnimationPlayableOutput.Create(graph, "Pose", Animator).SetSourcePlayable(playable); graph.Play();
            }
            playable.SetTime(time); graph.Evaluate(0);
        }
        public Quaternion[] Pose(AnimationClip clip, float time)
        {
            Sample(clip, time);
            return bones.Select(b => Animator.GetBoneTransform(b).localRotation).ToArray();
        }
        public void Dispose()
        {
            if (graph.IsValid()) graph.Destroy();
            if (actor != null) UnityEngine.Object.DestroyImmediate(actor);
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
