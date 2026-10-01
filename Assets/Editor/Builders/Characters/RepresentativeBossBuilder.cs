using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// 2026-10-01 대표 보스 「암굴 거수왕」[시험] 자산 빌더.
// 일반 실행: 없는 자산만 기본값으로 만들고, 있는 자산의 수치(수동 조정값)는 건드리지 않는다. 참조·구조만 보강한다.
// 시험값 초기화: 명시 메뉴에서만 기본 시험값을 다시 쓴다. 원본 암굴 거수 자산·테마 카탈로그는 수정하지 않는다.
public static class RepresentativeBossBuilder
{
    public const string Root = "Assets/ProjectOverburst/Resources/Enemies/Bosses/CavernUrsacetusKing";
    public const string RosterPath = "Assets/ProjectOverburst/Resources/Enemies/Bosses/EnemyBossRoster.asset";
    public const string BossId = "CavernMutants_UrsacetusKing_Boss";
    public const string ThemeId = "CavernMutants";
    const string SourceDefinition = "Assets/ProjectOverburst/Resources/Enemies/Themes/Definitions/CavernMutants_Ursacetus.asset";
    const string HudPrefab = "Assets/ProjectOverburst/Resources/UI/HUD/PF_EnemyBossHud.prefab";
    const string Clips = "Assets/ThirdParty/01_비인간캐릭터/Protofactor/Sci Fi/Sci Fi Characters Mega Pack Vol 2/Sci Fi Creatures Vol 2/Ursacetus/FBX Files/Ursacetus@";
    const string Sfx = "Assets/ThirdParty/11_사운드/";

    sealed class Spec
    {
        public string id, trigger, clip; public EnemyAbilityExecutionMode mode; public float damagePercent;
        public bool strong, telegraphed, parry; public float prep, release, recovery, minWarning, minRecovery;
        public float minRange, range, radius, angle, cooldown, hit; public float[] extraHits = Array.Empty<float>();
    }

    // 시험 패턴 값(G2 설계 패턴표와 같다).
    static readonly Spec[] Specs =
    {
        new Spec { id = "A_LeftSwipe", trigger = "Attack1", clip = "LeftHandAttack", mode = EnemyAbilityExecutionMode.MeleeArc, damagePercent = 8,
            telegraphed = true, prep = .62f, release = .14f, recovery = .45f, minRecovery = .45f, range = 3.0f, radius = 2.6f, angle = 130, cooldown = 1.6f, hit = .42f },
        new Spec { id = "B_RightSwipe", trigger = "Attack2", clip = "RightHandAttack", mode = EnemyAbilityExecutionMode.MeleeArc, damagePercent = 8,
            telegraphed = true, prep = .62f, release = .14f, recovery = .45f, minRecovery = .45f, range = 3.0f, radius = 2.6f, angle = 130, cooldown = 1.6f, hit = .53f },
        new Spec { id = "C_DoubleSmash", trigger = "Attack3", clip = "2HandsSmashAttack", mode = EnemyAbilityExecutionMode.AreaSlam, damagePercent = 20,
            strong = true, telegraphed = true, parry = true, prep = 1.2f, release = .16f, recovery = .8f, minWarning = 1.2f, minRecovery = .8f,
            range = 3.0f, radius = 3.4f, angle = 360, cooldown = 6f, hit = .44f },
        new Spec { id = "D_LeapSmash", trigger = "Attack6", clip = "RightHandSmashAttack", mode = EnemyAbilityExecutionMode.Charge, damagePercent = 18,
            strong = true, telegraphed = true, parry = true, prep = 1.0f, release = .2f, recovery = .9f, minWarning = 1.0f, minRecovery = .9f,
            minRange = 4f, range = 9f, radius = 2.8f, angle = 120, cooldown = 7f, hit = .50f },
        new Spec { id = "E_QuakeStomp", trigger = "Attack5", clip = "LeftFootStompAttack", mode = EnemyAbilityExecutionMode.AreaSlam, damagePercent = 22,
            telegraphed = true, prep = 1.3f, release = .18f, recovery = .9f, minRecovery = .9f, range = 4.5f, radius = 5.5f, angle = 360, cooldown = 8f, hit = .43f },
        new Spec { id = "F_TwinClaw", trigger = "Attack4", clip = "2HitComboAttack", mode = EnemyAbilityExecutionMode.MeleeArc, damagePercent = 12,
            telegraphed = true, prep = .7f, release = .3f, recovery = .5f, minRecovery = .5f, range = 3.2f, radius = 2.6f, angle = 140, cooldown = 3f, hit = .50f,
            extraHits = new[] { .76f } },
        new Spec { id = "E2_GreatQuake", trigger = "Attack7", clip = "RightFootStompAttack", mode = EnemyAbilityExecutionMode.AreaSlam, damagePercent = 22,
            telegraphed = true, prep = 1.3f, release = .18f, recovery = .9f, minRecovery = .9f, range = 5.0f, radius = 6.8f, angle = 360, cooldown = 8f, hit = .46f },
    };

    [MenuItem("OVERBURST/Bosses/대표 보스 생성·연결 (조정값 보존)")]
    public static void BuildMenu() => Debug.Log(Build(false));

    [MenuItem("OVERBURST/Bosses/대표 보스 시험값 초기화 (기본값 다시 쓰기)")]
    public static void ResetMenu()
    {
        if (EditorUtility.DisplayDialog("대표 보스 시험값 초기화", "보스 전용 공격·그로기·페이즈 시험값을 빌더 기본값으로 되돌립니다. 원본 몬스터 값은 바뀌지 않습니다.", "초기화", "취소"))
            Debug.Log(Build(true));
    }

    public static string Build(bool resetTrialValues)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Play 중에는 실행하지 않는다.");
        var log = new List<string>();
        Directory.CreateDirectory(Root);
        var source = Load<EnemyDefinition>(SourceDefinition);
        var sourceAnimation = source.AnimationProfile;

        // 1. 공격 SO
        var abilities = new Dictionary<string, EnemyAbilityDefinition>();
        foreach (var spec in Specs)
        {
            string path = $"{Root}/EAD_Boss_UrsKing_{spec.id}.asset";
            var ability = LoadOrCreate<EnemyAbilityDefinition>(path, out bool created);
            if (created || resetTrialValues) { ApplySpec(ability, spec); log.Add((created ? "생성 " : "초기화 ") + spec.id); }
            abilities[spec.id] = ability;
        }

        // 2. 페이즈별 공격 목록(구성은 설계 소유라 매번 맞춘다. 개별 수치는 위 SO에 있다)
        var set1 = EnsureSet($"{Root}/EAS_Boss_UrsKing_Phase1.asset", "Boss_UrsKing_Phase1",
            new[] { "A_LeftSwipe", "B_RightSwipe", "C_DoubleSmash", "D_LeapSmash", "E_QuakeStomp" }.Select(k => abilities[k]).ToArray());
        var set2 = EnsureSet($"{Root}/EAS_Boss_UrsKing_Phase2.asset", "Boss_UrsKing_Phase2",
            new[] { "A_LeftSwipe", "F_TwinClaw", "C_DoubleSmash", "D_LeapSmash", "E2_GreatQuake" }.Select(k => abilities[k]).ToArray());

        // 3. 페이즈·보스 정의
        var phase1 = LoadOrCreate<EnemyBossPhaseDefinition>($"{Root}/EBPD_Boss_UrsKing_Phase1.asset", out bool p1New);
        if (p1New || resetTrialValues) phase1.Configure("UrsKing_P1", "암굴의 주인", 1f, set1); else SetRef(phase1, "abilitySet", set1);
        var phase2 = LoadOrCreate<EnemyBossPhaseDefinition>($"{Root}/EBPD_Boss_UrsKing_Phase2.asset", out bool p2New);
        if (p2New || resetTrialValues) phase2.Configure("UrsKing_P2", "분노", .55f, set2); else SetRef(phase2, "abilitySet", set2);
        EditorUtility.SetDirty(phase1); EditorUtility.SetDirty(phase2);
        var bossDefinition = LoadOrCreate<EnemyBossDefinition>($"{Root}/EBD_Boss_UrsKing.asset", out bool bdNew);
        if (bdNew || resetTrialValues || bossDefinition.PhaseCount != 2) bossDefinition.Configure("UrsacetusKing", "암굴 거수왕", new[] { phase1, phase2 });
        EditorUtility.SetDirty(bossDefinition);

        // 4. 보스 등급(보스 등급 현행 정책을 그대로 받는다: 약공 경직·넉백·일반 패링 기절 면제)
        var grade = LoadOrCreate<EnemyGradeProfile>($"{Root}/EGP_Boss_UrsKing.asset", out bool gNew);
        if (gNew || resetTrialValues) { grade.Configure("Boss_UrsKing", "보스", EnemyGradeType.Boss, 1f, 1f, 1f, 1f, 1.3f); EditorUtility.SetDirty(grade); }

        // 5. 보스 전용 Animator(원본 복제 + 추가 공격·포효·그로기 상태)
        string controllerPath = $"{Root}/AC_Boss_UrsKing.controller";
        if (!File.Exists(controllerPath))
        {
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(sourceAnimation.RuntimeController), controllerPath))
                throw new InvalidOperationException("Animator 복제 실패");
            AssetDatabase.ImportAsset(controllerPath);
            log.Add("Animator 복제");
        }
        var controller = Load<AnimatorController>(controllerPath);
        EnsureAttackState(controller, "Attack4", "Attack_4", Clip("2HitComboAttack"));
        EnsureAttackState(controller, "Attack5", "Attack_5", Clip("LeftFootStompAttack"));
        EnsureAttackState(controller, "Attack6", "Attack_6", Clip("RightHandSmashAttack"));
        EnsureAttackState(controller, "Attack7", "Attack_7", Clip("RightFootStompAttack"));
        EnsureTimedState(controller, "Boss_Roar", Clip("Roar1"), 1.6f, true);
        EnsureTimedState(controller, "Boss_Groggy", Clip("GetHitFront"), .45f, false);
        EnsureFacingTurnSpeed(controller, resetTrialValues, log);
        EditorUtility.SetDirty(controller);

        var animation = LoadOrCreate<EnemyAnimationProfile>($"{Root}/EAP_Boss_UrsKing.asset", out _);
        var exclusions = Enumerable.Range(0, sourceAnimation.ExcludedRootMotionClipCount).Select(sourceAnimation.GetExcludedRootMotionClipPath).ToArray();
        var optional = Enumerable.Range(0, sourceAnimation.OptionalClipCount).Select(sourceAnimation.GetOptionalClip).ToArray();
        animation.Configure("Boss_UrsKing", controller, sourceAnimation.Idle, sourceAnimation.Walk, sourceAnimation.Run,
            Specs.Select(s => Clip(s.clip)).Distinct().ToArray(), sourceAnimation.Hit, sourceAnimation.Death, optional, exclusions);
        EditorUtility.SetDirty(animation);

        // 6. 페이즈별 행동 프로필(원본 복제). 인지 거리·공격 사이 대기만 보스 시험값
        var behavior1 = EnsureBehavior(source.BehaviorProfile, $"{Root}/EBP_Boss_UrsKing_Phase1.asset", resetTrialValues, .9f, .55f, log);
        var behavior2 = EnsureBehavior(source.BehaviorProfile, $"{Root}/EBP_Boss_UrsKing_Phase2.asset", resetTrialValues, .55f, .3f, log);

        // 7. 전투 프로필(시험값)
        var profile = LoadOrCreate<EnemyBossCombatProfile>($"{Root}/EBCP_Boss_UrsKing.asset", out bool profileNew);
        if (profileNew || resetTrialValues)
        {
            profile.patterns = new[]
            {
                Pattern("A_LeftSwipe", "왼손 할퀴기", abilities["A_LeftSwipe"], 3, 3f, false, null),
                Pattern("B_RightSwipe", "오른손 할퀴기", abilities["B_RightSwipe"], 1, 3f, false, null),
                Pattern("C_DoubleSmash", "양손 내려찍기", abilities["C_DoubleSmash"], 3, 2f, false, "F_TwinClaw"),
                Pattern("D_LeapSmash", "도약 강타", abilities["D_LeapSmash"], 3, 2f, false, "A_LeftSwipe"),
                Pattern("E_QuakeStomp", "지진 발구르기", abilities["E_QuakeStomp"], 1, 2f, true, null),
                Pattern("F_TwinClaw", "2연타 할퀴기", abilities["F_TwinClaw"], 2, 3f, false, null),
                Pattern("E2_GreatQuake", "대지진 발구르기", abilities["E2_GreatQuake"], 2, 2f, true, null),
            };
            profile.dangerClip = Load<AudioClip>(Sfx + "Pro Sound Collection/Voice/TrollMonster/troll_monster_growl_long_01.wav");
            profile.roarClip = Load<AudioClip>(Sfx + "MonsterPack2/Large Monster Roars/Monster Roars 01/MonsterPack2_LargeMonster01_Roar01.wav");
            profile.groggyClip = Load<AudioClip>(Sfx + "Pro Sound Collection/Animal_Impersonations/gorilla_growl_deep_02.wav");
            log.Add(profileNew ? "전투 프로필 생성" : "전투 프로필 초기화");
        }
        else
        {
            // 조정값은 보존하고 끊긴 공격 참조만 다시 잇는다(ID 기준, 순서로 대응하지 않는다).
            foreach (var pattern in profile.patterns)
                if (pattern != null && pattern.ability == null && abilities.TryGetValue(pattern.patternId, out var restored)) pattern.ability = restored;
        }
        profile.phaseBehaviors = new[] { behavior1, behavior2 };
        EditorUtility.SetDirty(profile);

        // 8. 보스 정의(프리팹보다 먼저 만들어 참조를 잇는다)
        string definitionPath = $"{Root}/ED_Boss_UrsKing.asset";
        var definition = LoadOrCreate<EnemyDefinition>(definitionPath, out bool defNew);
        string prefabPath = $"{Root}/PF_Boss_UrsKing.prefab";
        var actorPrefab = EnsurePrefab(prefabPath, source.ActorPrefab, bossDefinition, profile, log);
        definition.ConfigureIdentity(BossId, "암굴 거수왕");
        definition.ConfigureComposition(source.Species, grade, source.Variant, actorPrefab);
        definition.ConfigureRuntime(animation, set1, behavior1, source.MovementProfile, null, EnemySquadParticipationMode.Independent);
        definition.SetTacticalProfile(source.TacticalProfile);
        if (defNew || resetTrialValues) SetFloat(definition, "referenceHealthCoefficient", 64f); // 정예 21.2의 약 3배 [시험]
        EditorUtility.SetDirty(definition);

        // 9. 보스 전용 카탈로그와 테마 명부
        var catalog = LoadOrCreate<EnemyCatalog>($"{Root}/EC_Boss_UrsKing.asset", out _);
        catalog.Configure(new[] { definition }); EditorUtility.SetDirty(catalog);
        var roster = LoadOrCreate<EnemyBossRoster>(RosterPath, out _);
        var entries = (roster.entries ?? Array.Empty<EnemyBossRoster.Entry>()).Where(e => e != null && e.themeId != ThemeId).ToList();
        entries.Add(new EnemyBossRoster.Entry { themeId = ThemeId, boss = definition, catalog = catalog });
        roster.entries = entries.ToArray(); EditorUtility.SetDirty(roster);

        // 10. HUD 그로기 바·상태 문구(없을 때만 추가)
        EnsureHud(log);

        AssetDatabase.SaveAssets();
        if (!definition.IsValid) throw new InvalidOperationException("보스 정의가 유효하지 않다.");
        if (!bossDefinition.IsValid) throw new InvalidOperationException("보스 페이즈 정의가 유효하지 않다.");
        if (!catalog.Validate(out string message)) throw new InvalidOperationException(message);
        return "대표 보스 빌드 완료: " + (log.Count == 0 ? "변경 없음(조정값 보존)" : string.Join(", ", log));
    }

    static EnemyBossCombatProfile.Pattern Pattern(string id, string name, EnemyAbilityDefinition ability, int mask, float weight, bool danger, string followUp)
        => new EnemyBossCombatProfile.Pattern { patternId = id, displayName = name, ability = ability, phaseMask = mask, weight = weight,
            unparryableDangerCue = danger, phaseTwoFollowUp = followUp };

    static void ApplySpec(EnemyAbilityDefinition ability, Spec spec)
    {
        var clip = Clip(spec.clip);
        float duration = clip.length;
        ability.Configure("Boss_UrsKing_" + spec.id, spec.trigger, 1f, spec.range, spec.radius, spec.angle, spec.cooldown,
            duration * spec.hit, spec.hit, duration * .95f, 1f, false, spec.mode, 1.5f, spec.mode != EnemyAbilityExecutionMode.Charge, duration);
        ability.ConfigureUsePolicy(spec.minRange, 0);
        if (spec.extraHits.Length > 0) ability.ConfigureAdditionalHits(spec.extraHits);
        using var so = new SerializedObject(ability);
        so.FindProperty("referencePatternDamagePercent").floatValue = spec.damagePercent;
        so.FindProperty("telegraphedStrongAttack").boolValue = spec.strong;
        so.FindProperty("telegraphedAttack").boolValue = spec.telegraphed;
        so.FindProperty("preparationDuration").floatValue = spec.prep;
        so.FindProperty("releaseDuration").floatValue = spec.release;
        so.FindProperty("recoveryDuration").floatValue = spec.recovery;
        so.FindProperty("minimumWarningTime").floatValue = spec.minWarning;
        so.FindProperty("minimumRecoveryTime").floatValue = spec.minRecovery;
        so.FindProperty("parryable").boolValue = spec.parry;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ability);
    }

    static EnemyAbilitySet EnsureSet(string path, string id, EnemyAbilityDefinition[] items)
    {
        var set = LoadOrCreate<EnemyAbilitySet>(path, out _);
        set.Configure(id, items); EditorUtility.SetDirty(set);
        return set;
    }

    static EnemyBehaviorProfile EnsureBehavior(EnemyBehaviorProfile source, string path, bool reset, float recovery, float wait, List<string> log)
    {
        bool created = !File.Exists(path);
        if (created && !AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path)) throw new InvalidOperationException("행동 프로필 복제 실패");
        var profile = Load<EnemyBehaviorProfile>(path);
        if (created || reset)
        {
            using var so = new SerializedObject(profile);
            so.FindProperty("recoveryDuration").floatValue = recovery;
            so.FindProperty("attackWaitDuration").floatValue = wait;
            so.FindProperty("noticeRange").floatValue = 22f;
            so.FindProperty("investigateRange").floatValue = 18f;
            so.FindProperty("discoverRange").floatValue = 14f;
            so.FindProperty("dodgeLungeChance").floatValue = 0f;
            so.ApplyModifiedPropertiesWithoutUndo();
            log.Add((created ? "생성 " : "초기화 ") + Path.GetFileNameWithoutExtension(path));
        }
        return profile;
    }

    static EnemyActor EnsurePrefab(string path, EnemyActor source, EnemyBossDefinition bossDefinition, EnemyBossCombatProfile profile, List<string> log)
    {
        if (!File.Exists(path))
        {
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source.gameObject, preview);
                instance.name = "PF_Boss_UrsKing";
                PrefabUtility.SaveAsPrefabAsset(instance, path, out bool ok);
                if (!ok) throw new InvalidOperationException("보스 프리팹 변형 저장 실패");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            log.Add("프리팹 변형 생성");
        }
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var actor = root.GetComponent<EnemyActor>();
            var phase = Ensure<EnemyBossPhaseController>(root);
            var outcome = Ensure<EnemyBossOutcomeController>(root);
            var director = Ensure<EnemyBossCombatDirector>(root);
            var patternExecutor = Ensure<EnemyBossPatternExecutor>(root);
            phase.Configure(bossDefinition, actor, root.GetComponent<CombatHealth>(), root.GetComponent<EnemyAbilityController>());
            outcome.Configure(phase, "MapBoss");
            director.Configure(profile);
            patternExecutor.Configure(director);
            using (var so = new SerializedObject(actor))
            {
                so.FindProperty("bossPhaseController").objectReferenceValue = phase;
                so.FindProperty("bossOutcomeController").objectReferenceValue = outcome;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            // 보스 선택 실행기를 맨 앞에 두고 기존 실행기는 순서를 유지한다.
            var controller = root.GetComponent<EnemyAbilityController>();
            using (var so = new SerializedObject(controller))
            {
                var executors = so.FindProperty("executors");
                var existing = new List<EnemyAbilityExecutor>();
                for (int i = 0; i < executors.arraySize; i++)
                    if (executors.GetArrayElementAtIndex(i).objectReferenceValue is EnemyAbilityExecutor e && e != patternExecutor) existing.Add(e);
                executors.arraySize = existing.Count + 1;
                executors.GetArrayElementAtIndex(0).objectReferenceValue = patternExecutor;
                for (int i = 0; i < existing.Count; i++) executors.GetArrayElementAtIndex(i + 1).objectReferenceValue = existing[i];
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            // 보스는 상단 HUD가 체력을 보여 준다. 머리 위 정예 체력바는 끈다.
            var overhead = root.GetComponent<EnemyOverheadHpBar>();
            if (overhead != null) overhead.enabled = false;
            // 일반 대상 체력 칸(EnemyTargetHpHud, "엘리트")도 상단 보스 HUD와 겹쳐 뜬다. 보스 HUD 하나만 쓴다.
            var targetReporter = root.GetComponent<EnemyTargetHpReporter>();
            if (targetReporter != null) targetReporter.enabled = false;
            PrefabUtility.SaveAsPrefabAsset(root, path);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<EnemyActor>();
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static void EnsureAttackState(AnimatorController controller, string trigger, string stateName, AnimationClip clip)
    {
        if (!controller.parameters.Any(p => p.name == trigger)) controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
        var machine = controller.layers[0].stateMachine;
        var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == stateName);
        if (state == null)
        {
            var template = machine.states.Select(s => s.state).First(s => s.name == "Attack_3");
            state = machine.AddState(stateName, template != null ? new Vector3(500f, 60f * controller.parameters.Length, 0f) : Vector3.zero);
            state.speedParameterActive = true; state.speedParameter = "AttackAnimSpeed";
            var any = machine.AddAnyStateTransition(state);
            any.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            any.duration = .06f; any.hasFixedDuration = true; any.canTransitionToSelf = false; any.hasExitTime = false;
            var exit = state.AddTransition(machine.states.Select(s => s.state).First(s => s.name == "Locomotion"));
            exit.hasExitTime = true; exit.exitTime = .98f; exit.duration = .08f;
        }
        state.motion = clip;
    }

    static void EnsureTimedState(AnimatorController controller, string stateName, AnimationClip clip, float speed, bool returnToLocomotion)
    {
        var machine = controller.layers[0].stateMachine;
        var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == stateName);
        if (state == null)
        {
            state = machine.AddState(stateName);
            state.speed = speed;
            if (returnToLocomotion)
            {
                var exit = state.AddTransition(machine.states.Select(s => s.state).First(s => s.name == "Locomotion"));
                exit.hasExitTime = true; exit.exitTime = .95f; exit.duration = .2f;
            }
        }
        state.motion = clip;
    }

    // 2026-10-01 Play07·진단 Probe09: 정예 원본의 제자리 회전(FacingTurnLeft/Right)은 각도와 무관하게 속도 1로 끝까지 재생되고,
    // 그동안 공격 정면 판정(5°)이 풀리지 않는다. 7°만 어긋나도 약 2.6초 공격을 못 해, 1:1 보스전에서 멍하니 도는 시간이 된다.
    // 보스 Animator 사본에서만 회전 상태를 빠르게 한다[시험값]. 원본 정예 Animator와 이동 프로필은 건드리지 않는다.
    // 원본 값(1)일 때나 초기화 때만 쓰고, 손으로 바꾼 값은 보존한다.
    const float BossFacingTurnSpeed = 2f;

    static void EnsureFacingTurnSpeed(AnimatorController controller, bool reset, List<string> log)
    {
        foreach (var child in controller.layers[0].stateMachine.states)
        {
            var state = child.state;
            if (state.name != "FacingTurnLeft" && state.name != "FacingTurnRight") continue;
            if (!reset && !Mathf.Approximately(state.speed, 1f)) continue;
            if (Mathf.Approximately(state.speed, BossFacingTurnSpeed)) continue;
            state.speed = BossFacingTurnSpeed;
            log.Add($"회전 상태 {state.name} 속도 {BossFacingTurnSpeed}");
        }
    }

    static void EnsureHud(List<string> log)
    {
        var root = PrefabUtility.LoadPrefabContents(HudPrefab);
        try
        {
            var view = root.GetComponent<EnemyBossHudView>();
            var health = root.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == "HealthBar");
            var phaseText = root.GetComponentsInChildren<TMPro.TMP_Text>(true).FirstOrDefault(t => t.name == "Phase");
            if (view == null || health == null || phaseText == null) throw new InvalidOperationException("보스 HUD 구조를 찾지 못했다.");
            var groggyBar = health.parent.Find("GroggyBar") as RectTransform;
            var stateText = phaseText.transform.parent.Find("BossState")?.GetComponent<TMPro.TMP_Text>();
            bool changed = false;
            if (groggyBar == null)
            {
                groggyBar = (RectTransform)UnityEngine.Object.Instantiate(health.gameObject, health.parent, false).transform;
                groggyBar.name = "GroggyBar";
                float height = health.rect.height;
                groggyBar.anchoredPosition = health.anchoredPosition + Vector2.down * (height * .5f + height * .25f + 6f);
                groggyBar.sizeDelta = new Vector2(health.sizeDelta.x, health.sizeDelta.y * .5f);
                foreach (var image in groggyBar.GetComponentsInChildren<Image>(true))
                    if (image.name == "Fill") { image.name = "GroggyFill"; image.color = new Color(1f, .74f, .22f, 1f); image.fillAmount = 0f; }
                changed = true;
            }
            if (stateText == null)
            {
                stateText = UnityEngine.Object.Instantiate(phaseText.gameObject, phaseText.transform.parent, false).GetComponent<TMPro.TMP_Text>();
                stateText.name = "BossState";
                stateText.text = string.Empty;
                stateText.color = new Color(1f, .78f, .3f, 1f);
                stateText.alignment = TMPro.TextAlignmentOptions.Right;
                var rect = stateText.rectTransform;
                rect.anchoredPosition = phaseText.rectTransform.anchoredPosition;
                changed = true;
            }
            var fill = groggyBar.GetComponentsInChildren<Image>(true).First(i => i.name == "GroggyFill");
            // 2026-10-01 캡처 보정: 이름 글꼴에 한글이 없어 □로 보였다. 페이즈 문구와 같은 한글 글꼴을 쓴다.
            var nameText = root.GetComponentsInChildren<TMPro.TMP_Text>(true).FirstOrDefault(t => t.name == "BossName");
            if (nameText != null && phaseText.font != null && nameText.font != phaseText.font)
            { nameText.font = phaseText.font; nameText.fontSharedMaterial = phaseText.fontSharedMaterial; changed = true; }
            // 체력 막대를 복제할 때 따라온 "100 / 100" 문구는 그로기 막대에 필요 없다.
            var copiedText = groggyBar.Find("HealthText");
            if (copiedText != null) { UnityEngine.Object.DestroyImmediate(copiedText.gameObject); changed = true; }
            // 상태 문구(그로기·포효)는 오른쪽 페이즈 문구 칸(가로 70~100%)과 겹치지 않게 윗줄 가운데 칸(35~65%)에 둔다.
            if (stateText.alignment != TMPro.TextAlignmentOptions.Bottom) { stateText.alignment = TMPro.TextAlignmentOptions.Bottom; changed = true; }
            var stateRect = stateText.rectTransform;
            if (stateRect.anchorMin != new Vector2(.35f, 1f) || stateRect.anchorMax != new Vector2(.65f, 1f))
            {
                stateRect.anchorMin = new Vector2(.35f, 1f);
                stateRect.anchorMax = new Vector2(.65f, 1f);
                stateRect.anchoredPosition = new Vector2(0f, phaseText.rectTransform.anchoredPosition.y);
                stateRect.sizeDelta = new Vector2(0f, phaseText.rectTransform.sizeDelta.y);
                changed = true;
            }
            using (var so = new SerializedObject(view))
            {
                if (so.FindProperty("groggyFill").objectReferenceValue != fill || so.FindProperty("stateText").objectReferenceValue != stateText)
                {
                    so.FindProperty("groggyFill").objectReferenceValue = fill;
                    so.FindProperty("stateText").objectReferenceValue = stateText;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }
            }
            if (changed) { PrefabUtility.SaveAsPrefabAsset(root, HudPrefab); log.Add("보스 HUD 그로기 바·상태 문구"); }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static T Ensure<T>(GameObject root) where T : Component
    {
        var existing = root.GetComponent<T>();
        return existing != null ? existing : root.AddComponent<T>();
    }

    static T LoadOrCreate<T>(string path, out bool created) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        created = asset == null;
        if (!created) return asset;
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    static T Load<T>(string path) where T : UnityEngine.Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null) throw new FileNotFoundException("자산 없음: " + path);
        return asset;
    }

    static AnimationClip Clip(string name)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Clips + name + ".fbx"))
            if (o is AnimationClip clip && !clip.name.StartsWith("__")) return clip;
        throw new FileNotFoundException("클립 없음: " + name);
    }

    static void SetRef(UnityEngine.Object target, string field, UnityEngine.Object value)
    {
        using var so = new SerializedObject(target);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetFloat(UnityEngine.Object target, string field, float value)
    {
        using var so = new SerializedObject(target);
        so.FindProperty(field).floatValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
