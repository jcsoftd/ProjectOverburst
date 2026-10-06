using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Reviewed Blender poses are baked onto the existing native generic skeleton.
public static partial class CrustaspikanParryDazedBuilder
{
    public const string Folder = CrustaspikanMotionPlaybackBuilder.Root + "/Authored";
    public const string ClipPath = Folder + "/AN_Crustaspikan_ParryDazed.anim";
    public const float CycleSeconds = 2.8f;
    static readonly string[] ClipPaths = { ClipPath, EnterClipPath, RecoverClipPath };
    static readonly string[] StateNames = { "Material_ParryDazed", "Material_ParryDazedEnter", "Material_ParryDazedRecover" };
    static readonly string[] FieldNames = { "parryDazedClip", "parryDazedEnterClip", "parryDazedRecoverClip" };

    public static string EnsureNativeAssets()
    {
        CrustaspikanMotionPlaybackBuilder.RequireIdle();
        var clips = ClipPaths.Select(AssetDatabase.LoadAssetAtPath<AnimationClip>).ToArray();
        if (clips.Any(c => c == null)) throw new InvalidOperationException("Reviewed daze clips missing. Restore them or ImportAuthoredSamples from the reviewed Blender source; automatic procedural recreation is disabled.");
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CrustaspikanMotionPlaybackBuilder.ControllerPath);
        if (controller == null) throw new InvalidOperationException("Boss controller missing.");
        var machine = controller.layers[0].stateMachine;
        for (int i = 0; i < clips.Length; i++)
        {
            var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == StateNames[i]) ?? machine.AddState(StateNames[i]);
            if (state.motion != null && state.motion != clips[i]) throw new InvalidOperationException("Daze state belongs to another motion: " + StateNames[i]);
            state.motion = clips[i]; state.speed = 1; state.speedParameterActive = false; state.writeDefaultValues = true;
            EditorUtility.SetDirty(state);
        }
        EditorUtility.SetDirty(controller); AssetDatabase.SaveAssetIfDirty(controller);
        var contents = PrefabUtility.LoadPrefabContents(CrustaspikanMotionPlaybackBuilder.PrefabPath);
        try
        {
            var reaction = contents.GetComponent<CrustaspikanTemporaryReaction>();
            if (reaction == null) throw new InvalidOperationException("Reaction consumer missing.");
            var so = new SerializedObject(reaction); bool changed = false;
            for (int i = 0; i < clips.Length; i++)
            {
                var property = so.FindProperty(FieldNames[i]); if (property == null) throw new InvalidOperationException("Daze field missing: " + FieldNames[i]);
                if (property.objectReferenceValue != null && property.objectReferenceValue != clips[i]) throw new InvalidOperationException("Daze consumer edited externally: " + FieldNames[i]);
                if (property.objectReferenceValue != clips[i]) { property.objectReferenceValue = clips[i]; changed = true; }
            }
            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(contents, CrustaspikanMotionPlaybackBuilder.PrefabPath, out bool saved);
                if (!saved) throw new IOException("Daze prefab save failed.");
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        ValidateNative(); return "Blender-authored enter, loop and recovery linked to ReactionPose.";
    }
    public static string RebuildAuthoredClip()
    { throw new InvalidOperationException("Supply the reviewed Blender samples to ImportAuthoredSamples; the rejected procedural animation will not be regenerated."); }

    static void Write(string name, object value)
    {
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출/Boss/20261007_CrustaspikanBlenderDaze/Evidence"));
        Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, name), JsonConvert.SerializeObject(value, Formatting.Indented));
    }
    public static void ValidateNative()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CrustaspikanMotionPlaybackBuilder.PrefabPath);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CrustaspikanMotionPlaybackBuilder.ControllerPath);
        if (prefab == null || controller == null) throw new InvalidOperationException("Native daze dependencies missing.");
        var so = new SerializedObject(prefab.GetComponent<CrustaspikanTemporaryReaction>());
        for (int i = 0; i < ClipPaths.Length; i++)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPaths[i]);
            var state = controller.layers[0].stateMachine.states.Select(s => s.state).Single(s => s.name == StateNames[i]);
            if (clip == null || clip.length <= 0 || clip.isLooping != (i == 0) || state.motion != clip
                || so.FindProperty(FieldNames[i]).objectReferenceValue != clip || i == 0 && Mathf.Abs(clip.length - CycleSeconds) > .001f)
                throw new InvalidOperationException("Daze native binding mismatch: " + StateNames[i]);
            var curves = AnimationUtility.GetCurveBindings(clip);
            if (curves.Length != 540 || curves.Any(b => string.IsNullOrEmpty(b.path)) || AnimationUtility.GetAnimationEvents(clip).Length != 0)
                throw new InvalidOperationException("Daze skeleton, root or event contract differs.");
            foreach (var binding in curves)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve.keys.Any(k => float.IsNaN(k.value) || float.IsInfinity(k.value)) || i == 0 && Mathf.Abs(curve.Evaluate(0) - curve.Evaluate(clip.length)) > .00001f)
                    throw new InvalidOperationException("Daze curve invalid: " + binding.path + " / " + binding.propertyName);
            }
        }
        Write("dazed-native.json", new { status = "PASS_NATIVE_DAZE_BINDING", clips = ClipPaths.Select(p => new { path = p,
            guid = AssetDatabase.AssetPathToGUID(p), duration = AssetDatabase.LoadAssetAtPath<AnimationClip>(p).length }),
            bones = 54, actorRootCurves = 0, events = 0, consumer = "CrustaspikanTemporaryReaction / existing ReactionPose owner" });
    }
}
