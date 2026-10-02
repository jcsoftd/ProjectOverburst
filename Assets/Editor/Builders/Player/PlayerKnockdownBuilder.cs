using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

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
    static PlayerKnockdownAnimationSet.Motion Motion(string name, Vector2 direction)
    {
        var clip = Clip(name, true); var source = Clip(name, false);
        string axis = Mathf.Abs(direction.x) > .5f ? "RootT.x" : "RootT.z";
        var raw = Curve(source, axis); var inplace = Curve(clip, axis);
        float sign = Mathf.Abs(direction.x) > .5f ? direction.x : direction.y;
        var samples = new float[33]; float peak = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = source.length * i / (samples.Length - 1f);
            float value = raw != null ? raw.Evaluate(t) - raw.Evaluate(0) : 0;
            if (name.StartsWith("knockdown") && inplace != null) value -= inplace.Evaluate(t) - inplace.Evaluate(0);
            samples[i] = Mathf.Max(peak, value * sign); peak = samples[i];
        }
        var travel = peak > .05f
            ? new AnimationCurve(samples.Select((v, i) => new Keyframe(i / 32f, v / peak)).ToArray())
            : AnimationCurve.EaseInOut(0, 0, 1, 1);
        return new PlayerKnockdownAnimationSet.Motion { id = name, poseId = Pose, clip = clip, direction = direction, travel = travel };
    }
}
