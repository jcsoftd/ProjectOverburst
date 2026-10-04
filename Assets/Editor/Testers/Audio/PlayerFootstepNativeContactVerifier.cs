using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PlayerFootstepNativeContactVerifier
{
    public static string Verify(string directory = PlayerFootstepNativeContactBuilder.DefaultOutput)
    {
        PlayerFootstepNativeContactBuilder.RequireIdle(); directory = PlayerFootstepNativeContactBuilder.ValidateOutput(directory);
        var plan = JsonConvert.DeserializeObject<PlayerFootstepNativeContactBuilder.ContactPlan[]>(File.ReadAllText(Path.Combine(directory, "ContactPlan.json")));
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerFootstepNativeContactBuilder.PlayerPath);
        var serialized = new SerializedObject(prefab.GetComponent<FootstepEmitter>());
        if (serialized.FindProperty("animationContactClips").arraySize != 18 || serialized.FindProperty("animationContactCycleOffsets").arraySize != 18 || serialized.FindProperty("animationContactStates").arraySize != 11
            || prefab.GetComponentsInChildren<PlayerFootstepAnimationRelay>(true).Length != 1)
            throw new InvalidOperationException("Saved player contact configuration mismatch.");
        foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0) throw new InvalidOperationException("Missing player script: " + t.name);
        RuntimeAnimatorController controller = prefab.GetComponentInChildren<Animator>(true).runtimeAnimatorController;
        while (controller is AnimatorOverrideController overrides) controller = overrides.runtimeAnimatorController;
        var realPaths = ((AnimatorController)controller).layers.SelectMany(l => l.stateMachine.states.Select(s => l.stateMachine.name + "." + s.state.name)).ToArray();
        var savedStates = serialized.FindProperty("animationContactStates");
        for (int i = 0; i < savedStates.arraySize; i++)
            if (!realPaths.Contains(savedStates.GetArrayElementAtIndex(i).stringValue))
                throw new InvalidOperationException("Contact fullPathHash does not match actual state machine: " + savedStates.GetArrayElementAtIndex(i).stringValue);
        var profile = AssetDatabase.LoadAssetAtPath<WeaponCombatAnimationProfile>(PlayerFootstepNativeContactBuilder.AnimationRoot + "/GreatswordCombatAnimationProfile.asset");
        var savedClips = serialized.FindProperty("animationContactClips"); var savedOffsets = serialized.FindProperty("animationContactCycleOffsets");
        for (int i = 0; i < savedClips.arraySize; i++)
        {
            var clip = savedClips.GetArrayElementAtIndex(i).objectReferenceValue;
            var motion = profile.combatLocomotionSet.directions.FirstOrDefault(m => m.loop == clip);
            float expected = motion != null ? motion.loopCycleOffset : 0f;
            if (Mathf.Abs(savedOffsets.GetArrayElementAtIndex(i).floatValue - expected) > .00001f)
                throw new InvalidOperationException("Contact clip phase offset mismatch: " + clip.name);
        }
        foreach (var p in plan)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(p.sourcePath);
            var events = AnimationUtility.GetAnimationEvents(clip).Where(e => e.functionName == "OnWalkFootstep" || e.functionName == "OnRunFootstep").ToArray();
            if (events.Length != p.contacts.Length || p.contacts.Any(c => events.Count(e => e.intParameter == c.foot
                && Mathf.Abs(e.time - c.seconds) < .00001f && e.functionName == c.function) != 1))
                throw new InvalidOperationException("Saved clip contacts mismatch: " + p.role);
        }
        VerifyLoopBoundaries(directory);
        File.WriteAllText(Path.Combine(directory, "AssetValidation.json"), JsonConvert.SerializeObject(new { status = "PASS", clips = plan.Length,
            contacts = plan.Sum(p => p.contacts.Length), actualStatePathsVerified = savedStates.arraySize, cycleOffsetsVerified = savedClips.arraySize, missingScripts = 0, relayOnAnimator = prefab.GetComponentInChildren<Animator>(true).GetComponent<PlayerFootstepAnimationRelay>() != null }, Formatting.Indented));
        return "PASS: saved clip events, player contact configuration, relay and Missing Scripts.";
    }

    public static string VerifyLoopBoundaries(string directory)
    {
        PlayerFootstepNativeContactBuilder.RequireIdle(); directory = PlayerFootstepNativeContactBuilder.ValidateOutput(directory);
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/ProjectOverburst/03_Features/Player/Animations/JawFixed/Jog_Lfoot_JawFixed.anim");
        float seconds = AnimationUtility.GetAnimationEvents(clip).Single(e => e.functionName == "OnRunFootstep" && e.intParameter == 0).time;
        float eventPhase = seconds / clip.length;
        var cases = new List<object>(); bool passed = true;
        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            passed &= ProbeContactSequence(preview, clip, seconds, 0f, new[] { 5.9990535f, 6.9990535f }, new[] { 5, 6 }, 2, "consecutive contacts before wrap", cases);
            passed &= ProbeContactSequence(preview, clip, seconds, 0f, new[] { 6.009544f, 6.9990535f }, new[] { 5, 6 }, 2, "delayed end contact followed by next real contact", cases);
            passed &= ProbeContactSequence(preview, clip, seconds, 0f, new[] { 6.9990535f, 6.9990535f }, new[] { 6, 6 }, 1, "duplicate event occurrence", cases);
            passed &= ProbeContactSequence(preview, clip, seconds, .325f, new[] { 6.009544f - .325f, 6.9990535f - .325f }, new[] { 5, 6 }, 2, "cycle offset across wrap", cases);
            passed &= ProbeContactSequence(preview, clip, seconds, .325f, new[] { 6.009544f - .325f, 6.009544f - .325f }, new[] { 5, 5 }, 1, "duplicate delayed occurrence with offset", cases);
            passed &= ProbeContactSequence(preview, clip, seconds, 0f, new[] { 6f + eventPhase, 7f + eventPhase }, new[] { 6, 7 }, 2, "exact event phase", cases);
            passed &= ProbeContactSequence(preview, clip, 0f, 0f, new[] { 6.01f, 7.01f }, new[] { 6, 7 }, 2, "event at cycle start", cases);
            passed &= ProbeContactSequence(preview, clip, seconds, 0f, new[] { 6.009544f, 9.9990535f }, new[] { 5, 9 }, 2, "latest occurrences after skipped cycles", cases);
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
        File.WriteAllText(Path.Combine(directory, "LoopBoundaryValidation.json"), JsonConvert.SerializeObject(new {
            status = passed ? "PASS" : "FAIL", cases,
            scope = "Current runtime contact processing in owned preview objects; no audio or Play. Rate limits disabled only in these objects to isolate occurrence deduplication." }, Formatting.Indented));
        if (!passed) throw new InvalidOperationException("Footstep event-occurrence loop regression failed.");
        return "PASS: eight contact-occurrence boundary cases.";
    }

    static bool ProbeContactSequence(Scene scene, AnimationClip clip, float seconds, float offset, float[] phases,
        int[] expectedLoops, int expectedAccepted, string label, List<object> cases)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(FootstepEmitter);
        GameObject go = null; SurfaceProfile profile = null;
        try
        {
            go = new GameObject("OwnedFootstepBoundaryProbe"); SceneManager.MoveGameObjectToScene(go, scene);
            go.hideFlags = HideFlags.HideAndDontSave;
            var emitter = go.AddComponent<FootstepEmitter>(); var resolver = go.AddComponent<SurfaceResolver>();
            profile = ScriptableObject.CreateInstance<SurfaceProfile>(); profile.hideFlags = HideFlags.HideAndDontSave;
            resolver.Configure(profile, 0, .5f, 2f, Array.Empty<SurfaceResolutionRule>());
            emitter.Configure(null, null, resolver, null, 1.35f, 1.8f, 2f);
            emitter.ConfigureAnimationContacts(new[] { clip }, Array.Empty<string>(), Array.Empty<AnimationClip>(), Array.Empty<string>(), new[] { offset });
            type.GetField("minimumEmissionInterval", flags).SetValue(emitter, 0f);
            type.GetField("minimumSameFootInterval", flags).SetValue(emitter, 0f);
            type.GetField("selectedClip", flags).SetValue(emitter, clip);
            type.GetField("selectedStateHash", flags).SetValue(emitter, 573577218);
            var buffer = (PlayerFootstepContact[])type.GetField("candidates", flags).GetValue(emitter);
            var loops = (int[])type.GetField("lastFootLoops", flags).GetValue(emitter);
            var process = type.GetMethod("ProcessAnimationContacts", flags);
            var contacts = new List<object>(); bool loopKeysMatch = true;
            for (int i = 0; i < phases.Length; i++)
            {
                buffer[0] = new PlayerFootstepContact { Clip = clip, StateHash = 573577218, Foot = 0,
                    Frame = Time.frameCount, NormalizedTime = phases[i], EventSeconds = seconds, Kind = FootstepMotionKind.Run };
                type.GetField("candidateFrame", flags).SetValue(emitter, Time.frameCount);
                type.GetField("candidateCount", flags).SetValue(emitter, 1);
                int before = emitter.AnimationStepCount; process.Invoke(emitter, null);
                loopKeysMatch &= loops[0] == expectedLoops[i];
                contacts.Add(new { phase = phases[i], expectedLoop = expectedLoops[i], recordedLoop = loops[0], accepted = emitter.AnimationStepCount > before });
            }
            bool passed = loopKeysMatch && emitter.AnimationStepCount == expectedAccepted;
            cases.Add(new { label, offset, seconds, expectedAccepted, actualAccepted = emitter.AnimationStepCount, passed, contacts });
            return passed;
        }
        finally
        {
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
            if (profile != null) UnityEngine.Object.DestroyImmediate(profile);
        }
    }
}
