using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Connects the reviewed attack copies and their Idle to the existing greatsword contracts.</summary>
public static class SwordIdleGameplayBuilder
{
    const string DefaultOutput = "../개인파일/코덱스산출/Animation/20261004_SwordIdleGameplay";
    public static string Apply(string output = DefaultOutput)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Apply only in idle EditMode.");
        output = Path.GetFullPath(output);
        string allowed = Path.GetFullPath("../개인파일/코덱스산출") + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use the private Codex output directory.");
        Directory.CreateDirectory(output);
        var d = AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>(SwordIdleAttackCopyBuilder.Definition);
        if (d == null || d.comboDefinition == null || d.comboDefinition.StepCount != 4
            || d.dodgeAttackDefinition == null || d.dodgeAttackDefinition.StepCount != 1
            || d.heavyAttackDefinition == null || d.parriedHeavyAttackDefinition == null
            || d.dashHeavyAttackDefinition == null || d.animationProfile == null
            || d.animationProfile.animatorOverrideController == null)
            throw new InvalidOperationException("The existing eight-attack greatsword contract is required.");
        var profile = d.animationProfile;
        var controller = profile.animatorOverrideController;
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(SwordIdleAttackCopyBuilder.IdlePath);
        string[] roles = { "Combo1", "Combo2", "Combo3", "Combo4", "Heavy", "ParriedHeavy", "DodgeLight1", "DashHeavy" };
        var clips = roles.Select(role => AssetDatabase.LoadAssetAtPath<AnimationClip>(CopyPath(role))).ToArray();
        var previous = SwordIdleAttackCopyBuilder.Targets().Select(t => t.step.animationClip).ToArray();
        if (idle == null || !idle.isHumanMotion || clips.Any(c => c == null || !c.isHumanMotion)
            || previous.Length != 8 || previous.Any(c => c == null))
            throw new InvalidOperationException("Reviewed Humanoid assets are missing.");
        for (int i = 0; i < clips.Length; i++)
            if (Mathf.Abs(clips[i].length - previous[i].length) > .000001f
                || clips[i].frameRate != previous[i].frameRate)
                throw new InvalidOperationException("Attack timing mismatch: " + roles[i]);
        var baseController = controller.runtimeAnimatorController as AnimatorController;
        if (baseController == null) throw new InvalidOperationException("The shared melee Animator is missing.");
        var footIkStates = baseController.layers
            .Where(l => l.name == "Combat_MeleeWeapon" || l.name == "Combat_MeleeWeapon_TransitionLower")
            .SelectMany(l => States(l.stateMachine))
            .Where(s => s.name == "Melee_Attack" || s.name == "Melee_DodgeAttack" || s.name == "Melee_Locomotion" || s.name == "Melee_TransitionLower_Locomotion").ToArray();
        if (footIkStates.Length != 4) throw new InvalidOperationException("Expected the four formal melee attack/movement states.");
        var owned = new UnityEngine.Object[] { d.comboDefinition, d.heavyAttackDefinition,
            d.parriedHeavyAttackDefinition, d.dodgeAttackDefinition, d.dashHeavyAttackDefinition, profile, controller, baseController }
            .Concat(footIkStates.Cast<UnityEngine.Object>()).ToArray();
        if (owned.Any(EditorUtility.IsDirty))
            throw new InvalidOperationException("An owned definition/profile has unsaved edits. Preserve them before applying.");
        var beforeJson = owned.Select(o => EditorJsonUtility.ToJson(o)).ToArray();
        var sourcePaths = previous.Select(AssetDatabase.GetAssetPath).Distinct().ToArray();
        var sourceHashes = sourcePaths.Select(SwordIdleAttackCopyBuilder.Hash).ToArray();
        var copyHashes = clips.Select(c => SwordIdleAttackCopyBuilder.Hash(AssetDatabase.GetAssetPath(c))).ToArray();
        var guids = owned.Select(o => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(o))).ToArray();
        var scenes = SceneState();
        string backup = Path.Combine(output, "Backup");
        Directory.CreateDirectory(backup);
        for (int i = 0; i < owned.Length; i++)
        {
            string path = AssetDatabase.GetAssetPath(owned[i]);
            if (!File.Exists(Path.Combine(backup, Path.GetFileName(path))))
            {
                File.Copy(path, Path.Combine(backup, Path.GetFileName(path)));
                File.WriteAllText(Path.Combine(backup, Path.GetFileName(path) + ".editor.json"), beforeJson[i]);
            }
        }
        try
        {
            Undo.RecordObjects(owned, "Connect Sword Idle attacks");
            float[] settled = { .90f, .825f, .90f, 0f };
            d.comboDefinition.entryTransitionDuration = .12f;
            for (int i = 0; i < 4; i++)
            {
                var step = d.comboDefinition.steps[i];
                step.animationClip = clips[i];
                step.transitionDuration = .12f;
                step.settledIdleStartNormalizedTime = settled[i];
                d.comboDefinition.steps[i] = step;
            }
            var heavy = d.heavyAttackDefinition.attack; heavy.animationClip = clips[4]; d.heavyAttackDefinition.attack = heavy;
            var parry = d.parriedHeavyAttackDefinition.attack; parry.animationClip = clips[5]; d.parriedHeavyAttackDefinition.attack = parry;
            var dodge = d.dodgeAttackDefinition.steps[0]; dodge.animationClip = clips[6]; d.dodgeAttackDefinition.steps[0] = dodge;
            var dash = d.dashHeavyAttackDefinition.attack; dash.animationClip = clips[7]; d.dashHeavyAttackDefinition.attack = dash;
            AnimationClip oldIdle = profile.combatIdleClip;
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            controller.GetOverrides(overrides);
            int idleMappings = 0;
            for (int i = 0; i < overrides.Count; i++)
                if (overrides[i].Value == oldIdle || overrides[i].Value == idle)
                {
                    overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, idle);
                    idleMappings++;
                }
            if (idleMappings != 1) throw new InvalidOperationException("Expected one greatsword Combat Idle override.");
            int dodgeMappings = 0;
            for (int i = 0; i < overrides.Count; i++)
                if (overrides[i].Key.name == "Greatsword_DodgeAttack")
                {
                    overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, clips[6]);
                    dodgeMappings++;
                }
            if (dodgeMappings != 1) throw new InvalidOperationException("Expected one dedicated dodge-light animation slot.");
            profile.combatIdleClip = idle;
            controller.ApplyOverrides(overrides);
            foreach (var state in footIkStates) state.iKOnFeet = false;
            foreach (var o in owned) EditorUtility.SetDirty(o);
            // Tip progress derives from the selected clip; stale source data cannot accompany new clips.
            string comboBake = MeleeAttackVfxSlopeBakeUtility.BakeSelectedCombo(d.comboDefinition);
            string dodgeBake = MeleeAttackVfxSlopeBakeUtility.BakeSelectedCombo(d.dodgeAttackDefinition);
            if (!MeleeAttackVfxSlopeBakeUtility.ValidateCombo(d.comboDefinition, out string comboError))
                throw new InvalidOperationException(comboError);
            if (!MeleeAttackVfxSlopeBakeUtility.ValidateCombo(d.dodgeAttackDefinition, out string dodgeError))
                throw new InvalidOperationException(dodgeError);
            Undo.FlushUndoRecordObjects();
            foreach (var o in owned) AssetDatabase.SaveAssetIfDirty(o);
            for (int i = 0; i < sourcePaths.Length; i++)
                if (SwordIdleAttackCopyBuilder.Hash(sourcePaths[i]) != sourceHashes[i]) throw new Exception("Source clip changed.");
            for (int i = 0; i < clips.Length; i++)
                if (SwordIdleAttackCopyBuilder.Hash(AssetDatabase.GetAssetPath(clips[i])) != copyHashes[i]) throw new Exception("Reviewed copy changed.");
            if (SceneState() != scenes) throw new Exception("User scene state changed.");
            for (int i = 0; i < owned.Length; i++)
                if (AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(owned[i])) != guids[i]) throw new Exception("Asset GUID changed.");
            File.WriteAllText(Path.Combine(output, "ApplyResult.json"), JsonConvert.SerializeObject(new
            {
                status = "PASS", gameplayApplied = true,
                records = roles.Select((role, i) => new { role, path = AssetDatabase.GetAssetPath(clips[i]), guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(clips[i])) }),
                idle = AssetDatabase.GetAssetPath(idle), blend = .12f, settled,
                preservedContinuationOffsets = d.comboDefinition.steps.Select(s => s.continuationStartNormalizedTime),
                comboBake, dodgeBake, sourcesPreserved = true, copiesPreserved = true, scenesPreserved = true,
                movementClipsChanged = false, aimTurnSystemImplemented = false,
                automaticFootIk = footIkStates.Select(s => new { s.name, enabled = s.iKOnFeet })
            }, Formatting.Indented));
            return "PASS: eight reviewed attacks, Sword Idle and continuation profile connected; trajectories rebaked.";
        }
        catch
        {
            for (int i = 0; i < owned.Length; i++)
            {
                EditorJsonUtility.FromJsonOverwrite(beforeJson[i], owned[i]);
                EditorUtility.SetDirty(owned[i]); AssetDatabase.SaveAssetIfDirty(owned[i]);
            }
            throw;
        }
    }
    public static string CopyPath(string role) => SwordIdleAttackCopyBuilder.Folder + "/GS_" + role + "_SwordIdle.anim";
    static IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
    {
        foreach (var child in machine.states) yield return child.state;
        foreach (var child in machine.stateMachines)
            foreach (var state in States(child.stateMachine)) yield return state;
    }
    static string SceneState() => JsonConvert.SerializeObject(Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
    {
        var s = SceneManager.GetSceneAt(i); return new { s.path, s.isDirty, s.rootCount };
    }));
}
