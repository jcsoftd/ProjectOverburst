using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Project-owned gait tuning. Never changes a supplier's importer or animation clip.
public static class MonsterThemeLocomotionBuilder
{
    private const string LoopRoot = MonsterThemeCombatBuilder.Root + "/Animations/Locomotion";

    // 2026-10-01: 일반 재적용은 이동 속도·달리기 배율·회전 속도·회피 배율(밸런스 소유)과 기존 군집 배속 상한을 보존하고,
    // 보폭 측정 기준 속도·회전 애니메이션 속도·회전 곡선·루프 사본·블렌드 트리 연결 같은 파생값만 다시 계산한다.
    // 종별 기본 보행 속도표는 전체 빌더가 새로 만든 액터(ApplyCreated)에만 쓴다. 표에 없는 종은 이 도구 범위가 아니라 건너뛴다.
    [MenuItem("OVERBURST/Enemies/Themes/Repair Locomotion (Keep Tuned Speeds)")]
    public static void Apply() => ApplyFiltered(null, false);

    public static void ApplyCreated(IReadOnlyCollection<string> enemyIds)
    {
        if (enemyIds == null || enemyIds.Count == 0) return;
        ApplyFiltered(new HashSet<string>(enemyIds), true);
    }

    private static bool TryGaitDefaults(string enemyId, out float walkSpeed, out float runSpeed, out bool runUsesWalk)
    {
        runUsesWalk = false;
        switch (enemyId)
        {
            case "PrimalHunt_Caniathrox": walkSpeed = 1.10f; runSpeed = 3.05f; return true;
            case "PrimalHunt_CrustaspikanLarvae": walkSpeed = 1.30f; runSpeed = 1.82f; runUsesWalk = true; return true;
            case "PrimalHunt_Dimaxillosaurus": walkSpeed = 1.55f; runSpeed = 2.20f; runUsesWalk = true; return true;
            case "PrimalHunt_Venosaur_Tint_Brown": walkSpeed = 1.35f; runSpeed = 1.95f; runUsesWalk = true; return true;
            case "PrimalHunt_Occisodonte": walkSpeed = 1.20f; runSpeed = 1.85f; return true;
            case "SpiderBrood_RostrokarckLarvae": walkSpeed = 1.65f; runSpeed = 2.80f; return true;
            case "SpiderBrood_Horridomorph": walkSpeed = 1.10f; runSpeed = 2.45f; return true;
            case "SpiderBrood_Scolokarck_Tint3": walkSpeed = 1.55f; runSpeed = 2.20f; runUsesWalk = true; return true;
            case "SpiderBrood_Carcinoptera": walkSpeed = 1.45f; runSpeed = 2.15f; return true;
            case "SpiderBrood_Rostrokarck": walkSpeed = 1.40f; runSpeed = 2.00f; return true;
            case "VenomBrood_Venodonte_Tint1": walkSpeed = 1.65f; runSpeed = 2.70f; return true;
            case "VenomBrood_Venodonte_Tint3": walkSpeed = 1.55f; runSpeed = 2.55f; return true;
            case "VenomBrood_Arathrox": walkSpeed = 1.65f; runSpeed = 2.35f; return true;
            case "VenomBrood_Kupolojuve_Tint_Orange": walkSpeed = 1.45f; runSpeed = 2.10f; return true;
            case "VenomBrood_Kupolobrach_Tint_Orange": walkSpeed = 1.20f; runSpeed = 1.80f; return true;
            case "CavernMutants_Ceratoferox": walkSpeed = 1.20f; runSpeed = 2.55f; return true;
            case "CavernMutants_Cephalonops": walkSpeed = 1.15f; runSpeed = 2.50f; return true;
            case "CavernMutants_Gasterobrach": walkSpeed = 1.30f; runSpeed = 2.10f; return true;
            case "CavernMutants_Limadon": walkSpeed = 1.20f; runSpeed = 1.75f; return true;
            case "CavernMutants_Gorhorrid": walkSpeed = 1.40f; runSpeed = 2.20f; return true;
            case "CavernMutants_Ursacetus": walkSpeed = 1.05f; runSpeed = 1.60f; return true;
            default: walkSpeed = runSpeed = 0f; return false;
        }
    }

    private static void ApplyFiltered(HashSet<string> createdIds, bool applyGaitDefaults)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before changing locomotion assets.");

        // 쓰기 전에 대상을 모두 정한다. 중간 예외로 일부 액터만 메모리에서 바뀐 채 남지 않게 한다.
        var targets = new List<EnemyDefinition>();
        var skipped = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyThemeTable", new[] { MonsterThemeCombatBuilder.Root }))
        {
            var table = AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var entry in table.Entries)
            {
                var candidate = entry.definition;
                if (candidate == null || targets.Contains(candidate)) continue;
                if (createdIds != null && !createdIds.Contains(candidate.EnemyId)) continue;
                if (!TryGaitDefaults(candidate.EnemyId, out _, out _, out _))
                {
                    if (applyGaitDefaults)
                        throw new InvalidOperationException("Review the new species before gait tuning: " + candidate.EnemyId);
                    skipped.Add(candidate.EnemyId);
                    continue;
                }
                targets.Add(candidate);
            }
        }
        if (!AssetDatabase.IsValidFolder(LoopRoot))
            AssetDatabase.CreateFolder(MonsterThemeCombatBuilder.Root + "/Animations", "Locomotion");

        int count = 0;
        var touched = new List<UnityEngine.Object>();
        var drift = new List<string>();
        foreach (var definition in targets)
        {
                TryGaitDefaults(definition.EnemyId, out float walkSpeed, out float runSpeed, out bool runUsesWalk);
                var animation = definition.AnimationProfile;
                var movement = definition.MovementProfile;
                var optional = Enumerable.Range(0, animation.OptionalClipCount).Select(animation.GetOptionalClip).ToList();
                var walk = animation.Walk;
                var originalRun = new[] { animation.Run }.Concat(optional)
                    .FirstOrDefault(c => (c.name.Equals("Run", StringComparison.OrdinalIgnoreCase)||c.name=="RunForward")) ?? walk;
                var run = runUsesWalk ? walk : originalRun;
                var back = optional.FirstOrDefault(c => c.name.Equals("WalkBackwards", StringComparison.OrdinalIgnoreCase)
                    || c.name.Equals("CrawlBackwards", StringComparison.OrdinalIgnoreCase)) ?? walk;
                var turnLeft = optional.First(c => c.name == "Turn90Left");
                var turnRight = optional.First(c => c.name == "Turn90Right");
                movement.ConfigureTurnAnimation(90f / turnLeft.length, 90f / turnRight.length);
                movement.ConfigureTurnProgress(TurnProgress(animation,"Turn90Left_RM"),TurnProgress(animation,"Turn90Right_RM"));

                // Measure each supporting sole at the final model scale, including clips without root motion.
                // Species pace is independent of body scale; measured stride controls playback speed.
                var stride = MonsterThemeStrideCalibration.Measure(definition, walk, run, back);
                float walkReference = stride.walk.naturalSpeed;
                float runReference = stride.run.naturalSpeed;
                if (applyGaitDefaults)
                    movement.Configure(definition.EnemyId, walkSpeed, movement.TurnSpeed, walkReference, runSpeed / walkSpeed, movement.DodgeSpeedMultiplier);
                // 보폭 기준 속도(걷기·달리기·뒷걸음 재생 기준)는 기존 액터에서 현재 값을 유지한다. 원본 클립과 루프 사본을 잴 때
                // 1% 안팎, 종에 따라 그 이상 달라져 재생 속도(외형)가 바뀌기 때문이다. 2%를 넘게 벗어나면 쓰지 않고 보고만 한다.
                // 새로 만든 액터(ApplyCreated)는 측정값을 그대로 쓴다.
                var backpedal = new SerializedObject(movement).FindProperty("backpedalAnimationReferenceSpeed");
                if (applyGaitDefaults)
                {
                    movement.ConfigureAnimationReferenceSpeeds(walkReference, runReference);
                    movement.ConfigureBackpedalAnimationReferenceSpeed(stride.back.naturalSpeed);
                }
                else if (Drifted(movement.AnimationReferenceSpeed, walkReference) || Drifted(movement.RunAnimationReferenceSpeed, runReference)
                    || (backpedal != null && Drifted(backpedal.floatValue, stride.back.naturalSpeed)))
                    drift.Add(definition.EnemyId + " stored walk/run/back " + movement.AnimationReferenceSpeed.ToString("0.###") + "/"
                        + movement.RunAnimationReferenceSpeed.ToString("0.###") + "/" + (backpedal != null ? backpedal.floatValue.ToString("0.###") : "-")
                        + " measured " + walkReference.ToString("0.###") + "/" + runReference.ToString("0.###") + "/" + stride.back.naturalSpeed.ToString("0.###"));
                if (applyGaitDefaults || !movement.MatchAnimationToActualMovement)
                    movement.ConfigureCrowdAnimationSpeedLimit(1.12f);

                if (run != originalRun && !optional.Contains(originalRun)) optional.Add(originalRun);
                bool reverseBack=back==walk;
                var idle = Loop(definition.EnemyId, animation.Idle);
                walk = Loop(definition.EnemyId, walk);
                run = Loop(definition.EnemyId, run);
                back = Loop(definition.EnemyId, back);
                var controller = (AnimatorController)animation.RuntimeController;
                var state = controller.layers[0].stateMachine.states.First(s => s.state.name == "Locomotion").state;
                var tree = (BlendTree)state.motion;
                if (!controller.parameters.Any(p=>p.name=="TurnMagnitude"))controller.AddParameter("TurnMagnitude",AnimatorControllerParameterType.Float);
                BuildTurnState(controller,state,"FacingTurnLeft",idle,turnLeft);
                BuildTurnState(controller,state,"FacingTurnRight",idle,turnRight);
                var children = tree.children;
                foreach (float threshold in new[] { -1f, 0f, 1f, 2f })
                {
                    int index = Array.FindIndex(children, c => Mathf.Approximately(c.threshold, threshold));
                    if (index < 0) throw new InvalidOperationException("Missing gait threshold: " + definition.EnemyId);
                    children[index].motion = threshold < 0 ? back : threshold == 0 ? idle : threshold == 1 ? walk : run;
                    children[index].timeScale=threshold<0 && reverseBack?-1f:1f;
                }
                tree.children = children;
                foreach(var obsolete in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller)).OfType<BlendTree>().Where(t=>t.name=="StationaryFacing").ToArray())
                    UnityEngine.Object.DestroyImmediate(obsolete,true);
                int obsoleteParameter=Array.FindIndex(controller.parameters,p=>p.name=="Turn");
                if(obsoleteParameter>=0)controller.RemoveParameter(obsoleteParameter);
                animation.Configure(animation.ProfileId, controller, idle, walk, run,
                    Enumerable.Range(0, animation.AttackClipCount).Select(animation.GetAttackClip).ToArray(),
                    animation.Hit, animation.Death, optional.ToArray(),
                    Enumerable.Range(0, animation.ExcludedRootMotionClipCount).Select(animation.GetExcludedRootMotionClipPath).ToArray());
                EditorUtility.SetDirty(tree); EditorUtility.SetDirty(controller);
                EditorUtility.SetDirty(animation); EditorUtility.SetDirty(movement); count++;
                touched.AddRange(new UnityEngine.Object[] { controller, animation, movement, idle, walk, run, back });
        }
        // 이 도구가 만진 프로젝트 소유 자산만 저장한다. 다른 작업이 편집 중인 자산은 함께 저장하지 않는다.
        foreach (var asset in touched.Where(a => a != null).Distinct())
            if (AssetDatabase.GetAssetPath(asset).StartsWith("Assets/ProjectOverburst/", StringComparison.Ordinal))
                AssetDatabase.SaveAssetIfDirty(asset);
        Debug.Log("[MonsterThemeLocomotion] " + (applyGaitDefaults ? "created actors with gait defaults: " : "repaired, tuned speeds kept: ")
            + count + " actors; skipped (not in this tool's species table): " + skipped.Count + "; supplier assets unchanged."
            + (drift.Count > 0 ? " Stride reference kept (measured differs > 2%): " + string.Join("; ", drift) : ""));
    }

    private static bool Drifted(float current, float measured) =>
        current <= 0.0001f || Mathf.Abs(measured - current) / current > .02f;

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

    private static void BuildTurnState(AnimatorController controller,AnimatorState locomotion,string name,AnimationClip idle,AnimationClip turn)
    {
        var machine=controller.layers[0].stateMachine;
        var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name==name)??machine.AddState(name);
        var tree=state.motion as BlendTree;
        if(tree==null){tree=new BlendTree{name=name+"Amplitude"};AssetDatabase.AddObjectToAsset(tree,controller);state.motion=tree;}
        tree.blendType=BlendTreeType.Simple1D;tree.blendParameter="TurnMagnitude";tree.useAutomaticThresholds=false;
        tree.children=new[]{new ChildMotion{motion=idle,threshold=0,timeScale=idle.length/turn.length},new ChildMotion{motion=turn,threshold=1,timeScale=1}};
        state.speed=1f;state.speedParameterActive=false;
        if(state.transitions.Length==0){var exit=state.AddTransition(locomotion);exit.hasExitTime=true;exit.exitTime=1f;exit.duration=.06f;exit.hasFixedDuration=true;}
        EditorUtility.SetDirty(tree);EditorUtility.SetDirty(state);
    }

    private static AnimationCurve TurnProgress(EnemyAnimationProfile profile,string name)
    {
        var clip=Enumerable.Range(0,profile.ExcludedRootMotionClipCount).Select(profile.GetExcludedRootMotionClipPath).Distinct()
            .SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<AnimationClip>().First(c=>c.name==name);
        var bindings=AnimationUtility.GetCurveBindings(clip);
        foreach(var group in bindings.Where(b=>b.propertyName=="RootQ.x" || b.propertyName=="m_LocalRotation.x"))
        {
            string prefix=group.propertyName.Substring(0,group.propertyName.Length-1);
            var curves=new[]{"x","y","z","w"}.Select(axis=>{
                var binding=bindings.First(b=>b.path==group.path && b.propertyName==prefix+axis);
                return AnimationUtility.GetEditorCurve(clip,binding);}).ToArray();
            Quaternion Q(float t)=>new Quaternion(curves[0].Evaluate(t),curves[1].Evaluate(t),curves[2].Evaluate(t),curves[3].Evaluate(t));
            var start=Q(0);float total=Quaternion.Angle(start,Q(clip.length));
            if(total<70f || total>110f)continue;
            var keys=Enumerable.Range(0,33).Select(i=>new Keyframe(i/32f,Mathf.Clamp01(Quaternion.Angle(start,Q(clip.length*i/32f))/total))).ToArray();
            var curve=new AnimationCurve(keys);
            for(int i=0;i<curve.length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}
            return curve;
        }
        throw new InvalidOperationException("No authored 90-degree rotation curve: "+profile.ProfileId+" / "+name);
    }
}
