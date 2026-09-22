using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Project-owned gait tuning. Never changes a supplier's importer or animation clip.
public static class MonsterThemeLocomotionBuilder
{
    private const string LoopRoot = MonsterThemeCombatBuilder.Root + "/Animations/Locomotion";

    [MenuItem("OVERBURST/Enemies/Themes/Repair Locomotion")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before changing locomotion assets.");
        if (!AssetDatabase.IsValidFolder(LoopRoot))
            AssetDatabase.CreateFolder(MonsterThemeCombatBuilder.Root + "/Animations", "Locomotion");

        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyThemeTable", new[] { MonsterThemeCombatBuilder.Root }))
        {
            var table = AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var entry in table.Entries)
            {
                var definition = entry.definition;
                var animation = definition.AnimationProfile;
                var movement = definition.MovementProfile;
                var optional = Enumerable.Range(0, animation.OptionalClipCount).Select(animation.GetOptionalClip).ToList();
                var walk = animation.Walk;
                var originalRun = new[] { animation.Run }.Concat(optional)
                    .FirstOrDefault(c => c.name.Equals("Run", StringComparison.OrdinalIgnoreCase)) ?? walk;
                var run = originalRun;
                var back = optional.FirstOrDefault(c => c.name.Equals("WalkBackwards", StringComparison.OrdinalIgnoreCase)
                    || c.name.Equals("CrawlBackwards", StringComparison.OrdinalIgnoreCase)) ?? walk;
                var turnLeft = optional.First(c => c.name == "Turn90Left");
                var turnRight = optional.First(c => c.name == "Turn90Right");
                movement.ConfigureTurnAnimation(90f / turnLeft.length, 90f / turnRight.length);
                float walkSpeed, runSpeed;
                switch (definition.EnemyId)
                {
                    case "PrimalHunt_Caniathrox": walkSpeed = 1.05f; runSpeed = 2.65f; break;
                    case "PrimalHunt_Dimaxillosaurus": walkSpeed = 1.55f; runSpeed = 2.20f; run = walk; break;
                    case "PrimalHunt_Venosaur_Tint_Brown": walkSpeed = 1.35f; runSpeed = 1.95f; run = walk; break;
                    case "PrimalHunt_Occisodonte": walkSpeed = 1.20f; runSpeed = 1.85f; break;
                    case "SpiderBrood_RostrokarckLarvae": walkSpeed = 1.75f; runSpeed = 2.65f; break;
                    case "SpiderBrood_Horridomorph": walkSpeed = 1.10f; runSpeed = 1.70f; break;
                    case "SpiderBrood_Scolokarck_Tint3": walkSpeed = 1.55f; runSpeed = 2.20f; run = walk; break;
                    case "SpiderBrood_Carcinoptera": walkSpeed = 1.45f; runSpeed = 2.15f; break;
                    case "SpiderBrood_Rostrokarck": walkSpeed = 1.40f; runSpeed = 2.00f; break;
                    case "VenomBrood_Venodonte_Tint1":
                    case "VenomBrood_Venodonte_Tint3": walkSpeed = 1.70f; runSpeed = 2.55f; break;
                    case "VenomBrood_Arathrox": walkSpeed = 1.65f; runSpeed = 2.35f; break;
                    case "VenomBrood_Kupolojuve_Tint_Orange": walkSpeed = 1.45f; runSpeed = 2.10f; break;
                    case "VenomBrood_Kupolobrach_Tint_Orange": walkSpeed = 1.20f; runSpeed = 1.80f; break;
                    default: throw new InvalidOperationException("Review the new species before gait tuning: " + definition.EnemyId);
                }

                // Measure each supporting sole at the final model scale, including clips without root motion.
                var stride = MonsterThemeStrideCalibration.Measure(definition, walk, run, back);
                float walkReference = stride.walk.naturalSpeed;
                float runReference = stride.run.naturalSpeed;
                movement.Configure(definition.EnemyId, walkSpeed, movement.TurnSpeed, walkReference, runSpeed / walkSpeed, movement.DodgeSpeedMultiplier);
                movement.ConfigureAnimationReferenceSpeeds(walkReference, runReference);
                movement.ConfigureBackpedalAnimationReferenceSpeed(stride.back.naturalSpeed);
                movement.ConfigureCrowdAnimationSpeedLimit(1.12f);

                if (run != originalRun && !optional.Contains(originalRun)) optional.Add(originalRun);
                var idle = Loop(definition.EnemyId, animation.Idle);
                walk = Loop(definition.EnemyId, walk);
                run = Loop(definition.EnemyId, run);
                back = Loop(definition.EnemyId, back);
                var controller = (AnimatorController)animation.RuntimeController;
                var state = controller.layers[0].stateMachine.states.First(s => s.state.name == "Locomotion").state;
                var tree = (BlendTree)state.motion;
                if (!controller.parameters.Any(p => p.name == "Turn")) controller.AddParameter("Turn", AnimatorControllerParameterType.Float);
                var turnTree = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller)).OfType<BlendTree>().FirstOrDefault(t => t.name == "StationaryFacing");
                if (turnTree == null)
                {
                    turnTree = new BlendTree { name = "StationaryFacing", blendType = BlendTreeType.Simple1D, blendParameter = "Turn", useAutomaticThresholds = false };
                    AssetDatabase.AddObjectToAsset(turnTree, controller);
                }
                turnTree.children = new[] {
                    new ChildMotion { motion = Loop(definition.EnemyId, turnLeft), threshold = -1, timeScale = 1 },
                    new ChildMotion { motion = idle, threshold = 0, timeScale = 1 },
                    new ChildMotion { motion = Loop(definition.EnemyId, turnRight), threshold = 1, timeScale = 1 } };
                var children = tree.children;
                foreach (float threshold in new[] { -1f, 0f, 1f, 2f })
                {
                    int index = Array.FindIndex(children, c => Mathf.Approximately(c.threshold, threshold));
                    if (index < 0) throw new InvalidOperationException("Missing gait threshold: " + definition.EnemyId);
                    children[index].motion = threshold < 0 ? back : threshold == 0 ? (Motion)turnTree : threshold == 1 ? walk : run;
                }
                tree.children = children;
                animation.Configure(animation.ProfileId, controller, idle, walk, run,
                    Enumerable.Range(0, animation.AttackClipCount).Select(animation.GetAttackClip).ToArray(),
                    animation.Hit, animation.Death, optional.ToArray(),
                    Enumerable.Range(0, animation.ExcludedRootMotionClipCount).Select(animation.GetExcludedRootMotionClipPath).ToArray());
                EditorUtility.SetDirty(turnTree); EditorUtility.SetDirty(tree); EditorUtility.SetDirty(controller);
                EditorUtility.SetDirty(animation); EditorUtility.SetDirty(movement); count++;
            }
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[MonsterThemeLocomotion] Tuned " + count + " actors; supplier assets unchanged.");
    }

    private static AnimationClip Loop(string id, AnimationClip source)
    {
        if (source.isLooping) return source;
        string path = LoopRoot + "/" + id + "_" + source.name + ".anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        EditorUtility.CopySerialized(source, clip);
        clip.name = source.name;
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true; settings.loopBlend = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        clip.wrapMode = WrapMode.Loop;
        EditorUtility.SetDirty(clip);
        return clip;
    }
}
