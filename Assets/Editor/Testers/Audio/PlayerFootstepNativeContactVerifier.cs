using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

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
        File.WriteAllText(Path.Combine(directory, "AssetValidation.json"), JsonConvert.SerializeObject(new { status = "PASS", clips = plan.Length,
            contacts = plan.Sum(p => p.contacts.Length), actualStatePathsVerified = savedStates.arraySize, cycleOffsetsVerified = savedClips.arraySize, missingScripts = 0, relayOnAnimator = prefab.GetComponentInChildren<Animator>(true).GetComponent<PlayerFootstepAnimationRelay>() != null }, Formatting.Indented));
        return "PASS: saved clip events, player contact configuration, relay and Missing Scripts.";
    }
}
