using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>승인된 F/R/L=C, 나머지=B 정지 이동 곡선을 native 원본에서 추출한다.</summary>
public static class SwordStopMovementBuilder
{
    const string ProfilePath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Animation/GreatswordCombatAnimationProfile.asset";
    const string Source = "Assets/ThirdParty/03_애니메이션/Sword_Animations_Pack/Animation/Humanoid/";
    static readonly string[] Suffix = { "F_0", "F_R_45", "F_R_90", "B_R_45", "B_180", "B_L_45", "F_L_90", "F_L_45" };
    static readonly int[] Folders = { 1, 3, 5, 8, 6, 7, 4, 2 };

    public static string Apply(string directory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)))
            throw new InvalidOperationException("Idle real-account Editor required.");
        directory = Path.GetFullPath(directory);
        if (!directory.StartsWith(Path.GetFullPath("../개인파일/코덱스산출") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Private output required.");
        Directory.CreateDirectory(directory);
        var profile = AssetDatabase.LoadAssetAtPath<WeaponCombatAnimationProfile>(ProfilePath);
        var set = profile != null ? profile.combatLocomotionSet : null;
        if (set == null || set.directions.Length != 8 || EditorUtility.IsDirty(set))
            throw new InvalidOperationException("A clean eight-direction set is required.");
        string setPath = AssetDatabase.GetAssetPath(set);
        string guid = AssetDatabase.AssetPathToGUID(setPath);
        File.Copy(setPath, Path.Combine(directory, "SetBefore.asset"), true);
        var snapshot = UnityEngine.Object.Instantiate(set);
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject actor = null;
        PlayableGraph graph = default;
        var records = new List<object>();
        var measurements = new List<object>();
        var sourcePaths = Enumerable.Range(0, 8).Select(i => Source + "04_Run/02_Run_Combat_RM/"
            + Folders[i].ToString("D2") + "_Run_Combat_" + Suffix[i] + "_RM/Run_Combat_Stop_" + Suffix[i] + "_RM.anim").ToArray();
        var sourceHashes = sourcePaths.Select(SwordIdleAttackCopyBuilder.Hash).ToArray();
        var stopHashes = set.directions.Select(m => SwordIdleAttackCopyBuilder.Hash(AssetDatabase.GetAssetPath(m.stop))).ToArray();
        try
        {
            actor = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab"), scene);
            foreach (var mb in actor.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
            var animator = actor.GetComponentInChildren<P09CharacterVisualAdapter>(true).Animator;
            foreach (var other in actor.GetComponentsInChildren<Animator>(true)) other.enabled = other == animator;
            animator.applyRootMotion = true; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            for (int i = 0; i < 8; i++)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(sourcePaths[i]);
                if (clip == null || Mathf.Abs(clip.length - set.directions[i].stop.length) > .0001f)
                    throw new InvalidOperationException("Source Stop length mismatch: " + i);
                if (graph.IsValid()) graph.Destroy();
                graph = PlayableGraph.Create("Owned approved Stop curve extraction");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false); playable.SetSpeed(1);
                AnimationPlayableOutput.Create(graph, "Stop", animator).SetSourcePlayable(playable); graph.Play();
                var keys = new List<Keyframe>(); var frames = new List<object>();
                Vector3 axis = Quaternion.Euler(0, i * 45, 0) * Vector3.forward;
                Vector3 zero = Vector3.zero;
                for (int k = 0; k <= 120; k++)
                {
                    float t = clip.length * k / 120;
                    if (k == 0) { playable.SetTime(0); graph.Evaluate(0); zero = animator.rootPosition; }
                    else graph.Evaluate(clip.length / 120);
                    float distance = Vector3.Dot(animator.rootPosition - zero, axis);
                    keys.Add(new Keyframe(t, distance));
                    frames.Add(new { t, distance });
                }
                var curve = new AnimationCurve(keys.ToArray());
                for (int k = 0; k < curve.length; k++) curve.SmoothTangents(k, 0);
                var motion = set.directions[i];
                motion.stopDistance = curve; motion.stopSourceHumanScale = animator.humanScale;
                motion.stopMatchEntrySpeed = i == 0 || i == 2 || i == 6;
                records.Add(new { direction = i, mode = motion.stopMatchEntrySpeed ? "C" : "B",
                    source = sourcePaths[i], sourceHash = sourceHashes[i], clip = AssetDatabase.GetAssetPath(motion.stop),
                    length = clip.length, humanScale = animator.humanScale, distance = curve.Evaluate(clip.length) });
                measurements.Add(new { i, length = clip.length, humanScale = animator.humanScale, frames });
            }
            set.stopEntrySeconds = .08f;
            EditorUtility.SetDirty(set); AssetDatabase.SaveAssetIfDirty(set);
            if (guid != AssetDatabase.AssetPathToGUID(setPath)) throw new Exception("Set GUID changed.");
            if (!sourceHashes.SequenceEqual(sourcePaths.Select(SwordIdleAttackCopyBuilder.Hash))) throw new Exception("Supplier source changed.");
            if (!stopHashes.SequenceEqual(set.directions.Select(m => SwordIdleAttackCopyBuilder.Hash(AssetDatabase.GetAssetPath(m.stop)))))
                throw new Exception("In-place Stop poses/events changed.");
            if (set.directions.Any(m => m.stopDistance == null || m.stopDistance.length != 121 || m.stopSourceHumanScale <= 0))
                throw new Exception("Saved curve data incomplete.");
            File.WriteAllText(Path.Combine(directory, "NativeMeasured.json"), JsonConvert.SerializeObject(measurements));
            File.WriteAllText(Path.Combine(directory, "ApplyResult.json"), JsonConvert.SerializeObject(new {
                status = "PASS", setPath, guid, records, supplierPreserved = true, stopClipsPreserved = true,
                scaleStartStopWithLocomotionSpeed = set.scaleStartStopWithLocomotionSpeed, set.stopEntrySeconds }, Formatting.Indented));
            AssetDatabase.ExportPackage(setPath, Path.Combine(directory, "ApprovedStopCurves.unitypackage"), ExportPackageOptions.Default);
            return "PASS: F/R/L=C, FR/BR/B/BL/FL=B; eight curves saved.";
        }
        catch
        {
            EditorUtility.CopySerialized(snapshot, set); EditorUtility.SetDirty(set); AssetDatabase.SaveAssetIfDirty(set);
            throw;
        }
        finally
        {
            if (graph.IsValid()) graph.Destroy();
            if (actor != null) UnityEngine.Object.DestroyImmediate(actor);
            EditorSceneManager.ClosePreviewScene(scene);
            UnityEngine.Object.DestroyImmediate(snapshot);
        }
    }
}
