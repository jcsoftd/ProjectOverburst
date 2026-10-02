using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public static class PlayerKnockdownBuilder
{
    public const string Folder = "Assets/ProjectOverburst/03_Features/Player/Animations/Knockdown";
    public const string SetPath = Folder + "/PlayerKnockdownAnimationSet.asset";
    public const string PrefabPath = "Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab";
    public const string ControllerPath = "Assets/ProjectOverburst/03_Features/Player/Animations/AC_Player_Rigged.controller";
    const string Source = "Assets/ThirdParty/03_애니메이션/Girl_GreatSword_AnimSet/Animation/Humanoid/";
    const string Pose = "GirlGreatsword_Supine";

    [MenuItem("OVERBURST/Builders/Player/Build Knockdown")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Editor required");
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null || EditorUtility.IsDirty(controller)) throw new InvalidOperationException("Saved player controller required");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/ProjectOverburst/03_Features/Player/Animations", "Knockdown");
        var set = AssetDatabase.LoadAssetAtPath<PlayerKnockdownAnimationSet>(SetPath);
        if (set == null) { set = ScriptableObject.CreateInstance<PlayerKnockdownAnimationSet>(); AssetDatabase.CreateAsset(set, SetPath); }
        set.falls = new[] { "knockdown_up01", "knockdown_up02", "knockdown_up03" }
            .Select(n => Motion(n, Vector2.down)).ToArray();
        set.fallDistance = 2.2f;
        set.defaultRise = Motion("rise_02", Vector2.zero);
        set.directionalRises = new[] { Motion("rise_02", Vector2.up), Motion("rise_01", Vector2.down),
            Motion("rise_left_up", Vector2.left), Motion("rise_right_up", Vector2.right) };
        set.fallTemplate = Template("PlayerFallSlot", set.falls[0].clip);
        set.riseTemplate = Template("PlayerRiseSlot", set.defaultRise.clip);
        EditorUtility.SetDirty(set); AssetDatabase.SaveAssetIfDirty(set);

        if (!controller.parameters.Any(p => p.name == PlayerKnockdownAnimationSet.HoldTimeParameter))
            controller.AddParameter(PlayerKnockdownAnimationSet.HoldTimeParameter, AnimatorControllerParameterType.Float);
        var existing = controller.layers.FirstOrDefault(l => l.name == PlayerKnockdownAnimationSet.LayerName);
        if (existing == null)
        {
            var sm = new AnimatorStateMachine { name = PlayerKnockdownAnimationSet.LayerName };
            AssetDatabase.AddObjectToAsset(sm, controller);
            var layer = new AnimatorControllerLayer { name = PlayerKnockdownAnimationSet.LayerName, defaultWeight = 0,
                blendingMode = AnimatorLayerBlendingMode.Override, stateMachine = sm, iKPass = false };
            controller.AddLayer(layer); existing = layer;
        }
        var machine = existing.stateMachine;
        State(machine, "Player_ReactionEmpty", null);
        State(machine, PlayerKnockdownAnimationSet.FallState, set.fallTemplate);
        var hold = State(machine, PlayerKnockdownAnimationSet.HoldState, set.fallTemplate);
        hold.timeParameterActive = true; hold.timeParameter = PlayerKnockdownAnimationSet.HoldTimeParameter;
        hold.speed = 0;
        State(machine, PlayerKnockdownAnimationSet.RiseState, set.riseTemplate);
        machine.defaultState = machine.states.First(s => s.state.name == "Player_ReactionEmpty").state;
        EditorUtility.SetDirty(controller); AssetDatabase.SaveAssetIfDirty(controller);
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var reaction = root.GetComponent<PlayerKnockdownController>();
            if (reaction == null) reaction = root.AddComponent<PlayerKnockdownController>();
            var serialized = new SerializedObject(reaction);
            serialized.FindProperty("animationSet").objectReferenceValue = set;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static AnimatorState State(AnimatorStateMachine machine, string name, Motion clip)
    {
        var state = machine.states.FirstOrDefault(s => s.state.name == name).state ?? machine.AddState(name);
        state.motion = clip; state.speed = 1; state.iKOnFeet = false; state.writeDefaultValues = false;
        EditorUtility.SetDirty(state); return state;
    }
    static AnimationClip Clip(string name, bool inplace)
        => AssetDatabase.LoadAllAssetsAtPath(Source + name + (inplace ? "_inplace" : "") + ".fbx")
            .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
    static AnimationClip Template(string name, AnimationClip source)
    {
        string path = Folder + "/" + name + ".anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = UnityEngine.Object.Instantiate(source); clip.name = name; AssetDatabase.CreateAsset(clip, path); }
        return clip;
    }
    static AnimationCurve Curve(AnimationClip clip, string property)
        => AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), property));

    // Refresh only the owned set; other combat builders can own the controller and prefab.
    public static void RefreshFallTravelCurves()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Editor required");
        var set = AssetDatabase.LoadAssetAtPath<PlayerKnockdownAnimationSet>(SetPath);
        if (set == null || EditorUtility.IsDirty(set)) throw new InvalidOperationException("Saved knockdown set required");
        foreach (var fall in set.falls)
            fall.travel = FallTravel(fall.clip, Clip(fall.id, false));
        set.fallDistance = 2.2f;
        EditorUtility.SetDirty(set); AssetDatabase.SaveAssetIfDirty(set);
    }

    static AnimationCurve MonotonicCurve(float[] times, float[] distances, float length)
    {
        float total = distances[distances.Length - 1];
        if (total <= .0001f) throw new InvalidOperationException("Fall has no usable travel profile");
        var curve = new AnimationCurve(times.Select((t, i) => new Keyframe(t / length, distances[i] / total)).ToArray());
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
        }
        return curve;
    }

    static AnimationCurve FallTravel(AnimationClip clip, AnimationClip source)
    {
        int count = Mathf.RoundToInt(clip.length * clip.frameRate) + 1;
        var times = Enumerable.Range(0, count).Select(i => Mathf.Min(i / clip.frameRate, clip.length)).ToArray();
        times[count - 1] = clip.length;
        var raw = Curve(source, "RootT.z"); var inplace = Curve(clip, "RootT.z");
        var distances = new float[count]; float peak = 0;
        for (int i = 0; i < count; i++)
        {
            float removed = raw == null ? 0 : raw.Evaluate(times[i]) - raw.Evaluate(0);
            if (inplace != null) removed -= inplace.Evaluate(times[i]) - inplace.Evaluate(0);
            distances[i] = peak = Mathf.Max(peak, -removed);
        }
        // The first two falls retain a strong authored backward trajectory. Ignore later pose recoil.
        if (peak > .05f) return MonotonicCurve(times, distances, clip.length);

        // The third fall is a vertical flight: its removed horizontal root is only millimetres.
        // Derive acceleration and ground friction from the actual player's airborne pose instead.
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        var graph = default(PlayableGraph);
        try
        {
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true)) component.enabled = false;
            var animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.isHuman) throw new InvalidOperationException("Humanoid player required for fall sampling");
            animator.enabled = true; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false; animator.Rebind();
            graph = PlayableGraph.Create("Owned fall travel sampling"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable = AnimationClipPlayable.Create(graph, clip); playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false);
            var output = AnimationPlayableOutput.Create(graph, "Fall", animator); output.SetSourcePlayable(playable); graph.Play();
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips); var chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            var heights = new float[count]; var tilt = new float[count];
            for (int i = 0; i < count; i++)
            {
                playable.SetTime(Mathf.Min(times[i], clip.length * .99999f)); graph.Evaluate(0);
                heights[i] = root.transform.InverseTransformPoint(hips.position).y;
                tilt[i] = Vector3.Angle(chest.position - hips.position, Vector3.up);
            }
            int apex = Array.IndexOf(heights, heights.Max());
            int onset = Enumerable.Range(1, count - 1).FirstOrDefault(i => tilt[i] > 15 || Mathf.Abs(heights[i] - heights[0]) > .05f);
            float finalHeight = heights[count - 1]; float standingSpan = heights[0] - finalHeight;
            if (onset <= 0 || apex <= onset || standingSpan <= .1f || heights[apex] < heights[0] + .1f)
                throw new InvalidOperationException("Unsupported fall without authored horizontal travel or airborne pose");
            int contact = apex + 1;
            while (contact < count - 1 && heights[contact] > finalHeight + standingSpan * .15f) contact++;
            // Include the landing bounce; friction finishes once every remaining frame stays low.
            int settle = count - 1;
            while (settle > contact && heights[settle - 1] <= finalHeight + standingSpan * .04f) settle--;
            if (settle <= contact || contact <= apex) throw new InvalidOperationException("Fall landing interval required");
            float accelerationEnd = Mathf.Lerp(times[onset], times[apex], .6f);
            float Velocity(float t)
            {
                if (t <= times[onset] || t >= times[settle]) return 0;
                if (t < accelerationEnd) return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(times[onset], accelerationEnd, t));
                if (t < times[contact]) return 1;
                return 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(times[contact], times[settle], t));
            }
            distances[0] = 0;
            for (int i = 1; i < count; i++)
                distances[i] = distances[i - 1] + (Velocity(times[i - 1]) + Velocity(times[i])) * .5f * (times[i] - times[i - 1]);
            return MonotonicCurve(times, distances, clip.length);
        }
        finally { if (graph.IsValid()) graph.Destroy(); PrefabUtility.UnloadPrefabContents(root); }
    }

    static PlayerKnockdownAnimationSet.Motion Motion(string name, Vector2 direction)
    {
        var clip = Clip(name, true); var source = Clip(name, false);
        if (name.StartsWith("knockdown"))
            return new PlayerKnockdownAnimationSet.Motion { id = name, poseId = Pose, clip = clip, direction = direction, travel = FallTravel(clip, source) };
        string axis = Mathf.Abs(direction.x) > .5f ? "RootT.x" : "RootT.z";
        var raw = Curve(source, axis);
        float sign = Mathf.Abs(direction.x) > .5f ? direction.x : direction.y;
        var samples = new float[33]; float peak = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = source.length * i / (samples.Length - 1f);
            float value = raw != null ? raw.Evaluate(t) - raw.Evaluate(0) : 0;
            samples[i] = Mathf.Max(peak, value * sign); peak = samples[i];
        }
        var travel = peak > .05f
            ? new AnimationCurve(samples.Select((v, i) => new Keyframe(i / 32f, v / peak)).ToArray())
            : AnimationCurve.EaseInOut(0, 0, 1, 1);
        return new PlayerKnockdownAnimationSet.Motion { id = name, poseId = Pose, clip = clip, direction = direction, travel = travel };
    }
}
