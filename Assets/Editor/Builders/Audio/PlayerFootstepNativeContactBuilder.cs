using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class PlayerFootstepNativeContactBuilder
{
    public const string PlayerPath = "Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab";
    public const string AnimationRoot = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Animation";
    public const string DefaultOutput = "../개인파일/코덱스산출/Audio/20261004_PlayerFootstepSync/SwordContact";
    public static readonly string[] Directions = { "F", "FR", "R", "BR", "B", "BL", "L", "FL" };
    [Serializable] public sealed class ContactPoint { public int foot; public float seconds; public string function; }
    [Serializable] public sealed class ContactPlan { public string role, sourcePath, method; public ContactPoint[] contacts; public bool noNewContact; }
    public static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle EditMode required; preserve other owners' Play.");
    }
    public static string ValidateOutput(string output)
    {
        output = Path.GetFullPath(output);
        if (!output.StartsWith(Path.GetFullPath("../개인파일/코덱스산출") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use the private Codex output directory.");
        Directory.CreateDirectory(output); return output;
    }
    static string Hash(string path)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
    }
    static bool OwnedEvent(AnimationEvent e) => e.functionName == "OnWalkFootstep" || e.functionName == "OnRunFootstep";
    public static string CurveData(AnimationClip clip)
    {
        var curves = AnimationUtility.GetCurveBindings(clip).OrderBy(b => b.path).ThenBy(b => b.propertyName).ThenBy(b => b.type.FullName)
            .Select(b => { var c = AnimationUtility.GetEditorCurve(clip, b); return new { b.path, b.propertyName, type = b.type.FullName, c.keys, c.preWrapMode, c.postWrapMode }; }).ToArray();
        var references = AnimationUtility.GetObjectReferenceCurveBindings(clip).OrderBy(b => b.path).ThenBy(b => b.propertyName)
            .Select(b => new { b.path, b.propertyName, keys = AnimationUtility.GetObjectReferenceCurve(clip, b).Select(k => new { k.time, path = AssetDatabase.GetAssetPath(k.value) }).ToArray() }).ToArray();
        return JsonConvert.SerializeObject(new { clip.length, clip.frameRate, curves, references, settings = AnimationUtility.GetAnimationClipSettings(clip) });
    }
    public static Dictionary<string, AnimationClip> CurrentClips(WeaponCombatAnimationProfile profile)
    {
        if (profile == null || profile.combatLocomotionSet == null) throw new InvalidOperationException("Current Sword movement is not installed.");
        var result = new Dictionary<string, AnimationClip> {
            { "ExplorationWalk", AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/ProjectOverburst/03_Features/Player/Animations/JawFixed/Walk_Lfoot_JawFixed.anim") },
            { "ExplorationRun", AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/ProjectOverburst/03_Features/Player/Animations/JawFixed/Jog_Lfoot_JawFixed.anim") } };
        for (int i = 0; i < 8; i++)
        {
            result.Add("Combat" + Directions[i], profile.combatLocomotionSet.directions[i].loop);
            result.Add("CombatStart" + Directions[i], profile.combatLocomotionSet.directions[i].start);
        }
        return result;
    }
    public static string Apply(string output = DefaultOutput)
    {
        RequireIdle(); output = ValidateOutput(output);
        var plan = JsonConvert.DeserializeObject<ContactPlan[]>(File.ReadAllText(Path.Combine(output, "ContactPlan.json")));
        var profile = AssetDatabase.LoadAssetAtPath<WeaponCombatAnimationProfile>(AnimationRoot + "/GreatswordCombatAnimationProfile.asset");
        var clips = CurrentClips(profile);
        if (plan == null || plan.Length != clips.Count || clips.Keys.Any(r => plan.Count(p => p.role == r) != 1))
            throw new InvalidOperationException("A measured current 18-clip plan is required.");
        foreach (var p in plan)
        {
            var clip = clips[p.role];
            if (clip == null || AssetDatabase.GetAssetPath(clip) != p.sourcePath || !p.sourcePath.StartsWith("Assets/ProjectOverburst/")
                || !p.sourcePath.EndsWith(".anim") || EditorUtility.IsDirty(clip) || !clip.isHumanMotion
                || p.contacts == null || (p.contacts.Length == 0 && (!p.noNewContact || !p.role.StartsWith("CombatStart"))) || p.contacts.Any(c => (c.foot != 0 && c.foot != 1)
                    || float.IsNaN(c.seconds) || float.IsInfinity(c.seconds) || c.seconds < 0 || c.seconds >= clip.length
                    || (c.function != "OnWalkFootstep" && c.function != "OnRunFootstep")))
                throw new InvalidOperationException("Invalid or changed contact source: " + p.role);
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        if (prefab == null || EditorUtility.IsDirty(prefab)) throw new InvalidOperationException("Preserve unsaved player edits.");
        var protectedPaths = new[] { "Assets/ProjectOverburst/03_Features/Player/Animations/AC_Player_Rigged.controller",
            AssetDatabase.GetAssetPath(profile), AssetDatabase.GetAssetPath(profile.animatorOverrideController), AssetDatabase.GetAssetPath(profile.combatLocomotionSet) }
            .Concat(SwordIdleAttackCopyBuilder.Targets().Select(t => AssetDatabase.GetAssetPath(t.step.animationClip)))
            .Distinct().SelectMany(p => new[] { p, p + ".meta" }).Where(File.Exists).ToArray();
        var hashes = protectedPaths.Select(Hash).ToArray();
        var snapshots = clips.Values.Distinct().ToDictionary(c => c, c => { var copy = UnityEngine.Object.Instantiate(c); copy.name = c.name; return copy; });
        var curves = clips.ToDictionary(p => p.Key, p => CurveData(p.Value));
        GameObject contents = null; string emitterSnapshot = null; bool addedRelay = false, saved = false;
        try
        {
            contents = PrefabUtility.LoadPrefabContents(PlayerPath);
            var emitter = contents.GetComponent<FootstepEmitter>(); var animator = contents.GetComponentInChildren<Animator>(true);
            if (emitter == null || animator == null) throw new InvalidOperationException("Player audio/Animator missing.");
            emitterSnapshot = EditorJsonUtility.ToJson(emitter);
            foreach (var p in plan)
            {
                var target = clips[p.role]; var events = AnimationUtility.GetAnimationEvents(target).Where(e => !OwnedEvent(e)).ToList();
                events.AddRange(p.contacts.Select(c => new AnimationEvent { time = c.seconds, functionName = c.function,
                    intParameter = c.foot, messageOptions = SendMessageOptions.RequireReceiver }));
                AnimationUtility.SetAnimationEvents(target, events.OrderBy(e => e.time).ToArray());
                EditorUtility.SetDirty(target); AssetDatabase.SaveAssetIfDirty(target);
                if (CurveData(target) != curves[p.role]) throw new InvalidOperationException("Movement curves/settings changed: " + p.role);
            }
            var mapping = new List<KeyValuePair<AnimationClip, AnimationClip>>(); profile.animatorOverrideController.GetOverrides(mapping);
            var legacy = mapping.Where(p => p.Key.name.StartsWith("Frank_RPG_Warrior_8Way_Walk_")).Select(p => p.Key).ToArray();
            RuntimeAnimatorController controller = animator.runtimeAnimatorController;
            while (controller is AnimatorOverrideController overrides) controller = overrides.runtimeAnimatorController;
            var layers = ((AnimatorController)controller).layers;
            Func<string, string, string> statePath = (layerName, stateName) => {
                var machine = layers.Single(l => l.name == layerName).stateMachine;
                if (machine.states.Count(s => s.state.name == stateName) != 1)
                    throw new InvalidOperationException("Contact state not found: " + layerName + "/" + stateName);
                // Unity's fullPathHash uses the root state-machine name, which can differ from the layer label.
                return machine.name + "." + stateName;
            };
            var states = new[] { statePath("Base Layer", "NormalMove"), statePath("Combat_MeleeWeapon", "Melee_Locomotion"),
                statePath("Combat_MeleeWeapon_TransitionLower", "Melee_TransitionLower_Locomotion") }
                .Concat(profile.combatLocomotionSet.directions.Select(m => statePath("Combat_MeleeWeapon", m.startState))).ToArray();
            var offsets = clips.Select(p => p.Key.StartsWith("Combat") && !p.Key.StartsWith("CombatStart")
                ? profile.combatLocomotionSet.directions[Array.IndexOf(Directions, p.Key.Substring("Combat".Length))].loopCycleOffset : 0f).ToArray();
            emitter.ConfigureAnimationContacts(clips.Values.ToArray(), states, legacy, new[] { "Base Layer.AimMove" }, offsets);
            if (animator.GetComponent<PlayerFootstepAnimationRelay>() == null) { animator.gameObject.AddComponent<PlayerFootstepAnimationRelay>(); addedRelay = true; }
            if (protectedPaths.Where((p, i) => Hash(p) != hashes[i]).Any()) throw new InvalidOperationException("Animation integration ownership changed.");
            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPath); saved = true;
            AssetDatabase.ExportPackage(plan.Select(p => p.sourcePath).ToArray(), Path.Combine(output, "FootstepContacts_Restore.unitypackage"), ExportPackageOptions.Default);
            File.WriteAllText(Path.Combine(output, "ApplyResult.json"), JsonConvert.SerializeObject(new { status = "PASS", contacts = plan.Sum(p => p.contacts.Length),
                states, protectedPaths, hashes, guids = plan.ToDictionary(p => p.role, p => AssetDatabase.AssetPathToGUID(p.sourcePath)),
                animationCurvesAndSettingsPreserved = true, animationIntegrationPreserved = true, unrelatedEventsPreserved = true }, Formatting.Indented));
            return "PASS: current movement events and player audio wiring.";
        }
        catch (Exception error)
        {
            foreach (var pair in snapshots) { EditorUtility.CopySerialized(pair.Value, pair.Key); EditorUtility.SetDirty(pair.Key); AssetDatabase.SaveAssetIfDirty(pair.Key); }
            if (saved && contents != null && emitterSnapshot != null)
            {
                EditorJsonUtility.FromJsonOverwrite(emitterSnapshot, contents.GetComponent<FootstepEmitter>());
                if (addedRelay) UnityEngine.Object.DestroyImmediate(contents.GetComponentInChildren<PlayerFootstepAnimationRelay>(true));
                PrefabUtility.SaveAsPrefabAsset(contents, PlayerPath);
            }
            File.WriteAllText(Path.Combine(output, "ApplyFailure.json"), JsonConvert.SerializeObject(new { status = "FAIL_ROLLED_BACK", error = error.ToString() }, Formatting.Indented)); throw;
        }
        finally { foreach (var snapshot in snapshots.Values) UnityEngine.Object.DestroyImmediate(snapshot); if (contents != null) PrefabUtility.UnloadPrefabContents(contents); }
    }
}
