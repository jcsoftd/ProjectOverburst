using System;
using System.IO;
using System.Linq;
using Overburst.EditorTools.MonsterTuner;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static partial class MonsterTunerVerifier
{
    // Verify independent roles, timing choices and serialized draft recovery using owned fixtures.
    public static string Advanced()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return "Editor busy";
        Checks.Clear(); Directory.CreateDirectory(Output); Directory.CreateDirectory(UXOutput);
        string fixtures = "Assets/Editor/Testers/Characters/MonsterTuner/Fixtures";
        if (!AssetDatabase.IsValidFolder(fixtures)) AssetDatabase.CreateFolder("Assets/Editor/Testers/Characters/MonsterTuner", "Fixtures");
        string root = fixtures + "/Extended_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder(fixtures, root.Split('/').Last());
        var window = ScriptableObject.CreateInstance<MonsterTunerWindow>(); window.VerificationOnly = true;
        window.titleContent = new GUIContent("몬스터 튜너 사본·모션 검증"); window.ShowUtility();
        int frames = 0, live = MonsterTunerPreviewStage.LiveStages;
        // Window.Show may already have allocated its own stage; account for it in the final check.
        if (Get<MonsterTunerPreviewStage>(window, "stage").Actor != null) live--;
        string beforeScenes = Scenes();
        EditorApplication.update += Run;
        return "Advanced verification queued; report: advanced-verification.json";
        void Run()
        {
            if (window == null) { EditorApplication.update -= Run; return; }
            if (++frames < 3 || window.rootVisualElement.panel == null) return;
            EditorApplication.update -= Run;
            MonsterTunerSession persisted = null, recovered = null, motionSession = null;
            EnemyAnimationProfile workingProfile = null;
            string recoveryA = null, recoveryB = null;
            try
            {
                var catalog = new MonsterTunerCatalog(); catalog.Refresh();
                var source = catalog.Entries.First(e => e.Definition.AbilitySet != null &&
                    MonsterTunerAnimationBindings.Read(e.Definition.AnimationProfile).Any(b => b.StatePath.Contains(".Attack_"))).Definition;
                var production = AssetDatabase.GetDependencies(catalog.Entries.Select(e => AssetDatabase.GetAssetPath(e.Definition)).ToArray(), true)
                    .Where(p => File.Exists(p) && (p.StartsWith("Assets/ProjectOverburst/", StringComparison.Ordinal) || p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))).Distinct().ToDictionary(p => p, MonsterTunerStamp.FileHash);
                Check("고급 fixture 정의 복사", AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), root + "/A.asset") && AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), root + "/B.asset"));
                var a = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(root + "/A.asset");
                var b = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(root + "/B.asset");
                var binding = MonsterTunerAnimationBindings.Read(source.AnimationProfile).First(x => x.StatePath.Contains(".Attack_"));
                string state = binding.StatePath.Split('.').Last();
                int abilityIndex = Enumerable.Range(0, source.AbilitySet.Count).First(i => source.AbilitySet.GetAbility(i) != null &&
                    ("Attack_" + source.AbilitySet.GetAbility(i).AnimatorTrigger.Substring(6)) == state);
                AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source.AbilitySet), root + "/Set.asset");
                AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source.AbilitySet.GetAbility(abilityIndex)), root + "/Ability.asset");
                var set = AssetDatabase.LoadAssetAtPath<EnemyAbilitySet>(root + "/Set.asset");
                var ability = AssetDatabase.LoadAssetAtPath<EnemyAbilityDefinition>(root + "/Ability.asset");
                SetFixtureReference(set, "abilities.Array.data[" + abilityIndex + "]", ability); SetFixtureReference(a, "abilitySet", set);
                var serialized = new SerializedObject(ability);
                serialized.FindProperty("telegraphedAttack").boolValue = false;
                serialized.FindProperty("attackAnimationDuration").floatValue = 2f;
                serialized.FindProperty("hitNormalizedTime").floatValue = .4f;
                var extra = serialized.FindProperty("additionalHitNormalizedTimes"); extra.arraySize = 2;
                extra.GetArrayElementAtIndex(0).floatValue = .6f; extra.GetArrayElementAtIndex(1).floatValue = .8f;
                serialized.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssetIfDirty(ability);
                var replacement = new AnimationClip { name = "OwnedFourSecondMotion" };
                AnimationUtility.SetEditorCurve(replacement, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x"), AnimationCurve.Constant(0, 4f, 0));
                AssetDatabase.CreateAsset(replacement, root + "/Replacement.anim");
                Select(a); Set(window, "tab", 4); Call(window, "BuildFields");
                var session = Get<MonsterTunerSession>(window, "session");
                Change(MotionField(), replacement);
                Check("비율 유지 새 기준 길이", Mathf.Approximately(session.Value("ability:" + abilityIndex, "attackAnimationDuration").number, 4f));
                Check("비율 유지 첫·추가 타격", Mathf.Approximately(session.Value("ability:" + abilityIndex, "hitNormalizedTime").number, .4f) && session.Value("ability:" + abilityIndex, "additionalHitNormalizedTimes").numbers.SequenceEqual(new[] { .6f, .8f }));
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Check("클립·시간 변경 하나의 Undo", !session.Dirty);
                Call(window, "BuildFields");
                Change(Get<ScrollView>(window, "fields").Query<DropdownField>().ToList().First(f => f.label == "공격 교체 시"), "현재 타격 초 유지");
                Change(MotionField(), replacement);
                Check("초 유지 첫 타격", Mathf.Abs(session.Value("ability:" + abilityIndex, "hitNormalizedTime").number * 4f - .8f) < .0001f);
                Check("초 유지 추가 타격", session.Value("ability:" + abilityIndex, "additionalHitNormalizedTimes").numbers.Select(t => t * 4f).SequenceEqual(new[] { 1.2f, 1.6f }));
                var stage = Get<MonsterTunerPreviewStage>(window, "stage");
                var replay = Get<ScrollView>(window, "fields").Query<Button>().ToList().FirstOrDefault(button => button.text == "▶ " + replacement.name);
                Check("교체 클립 재생 버튼·길이 즉시 갱신", replay != null && Get<ScrollView>(window, "fields").Query<Label>().ToList().Any(label => label.text.StartsWith("4.00s")));
                CapturePanel(window, Path.Combine(UXOutput, "replaced-motion-layout.png"));
                Get<ScrollView>(window, "fields").ScrollTo(replay);
                CapturePanel(window, Path.Combine(UXOutput, "replaced-motion.png")); Click(replay);
                Check("교체 직후 버튼 실제 새 클립 재생", stage.Clip == replacement && stage.Playing);
                stage.SetClip(replacement, ability);
                Check("실전 계산 공격 속도", Mathf.Approximately(stage.AttackSpeed, stage.Enemy.Melee.AbilityAnimationSpeed));
                session.Discard(); Call(window, "UndoRedo");
                var scale = session.Value("variant", "visualScale"); scale.vector *= 1.1f; session.Set("variant", "visualScale", scale, "A 사본");
                Select(b); var second = Get<MonsterTunerSession>(window, "session");
                var secondScale = second.Value("variant", "visualScale"); secondScale.vector *= 1.2f; second.Set("variant", "visualScale", secondScale, "B 사본");
                Select(a);
                Check("두 몬스터 전환 사본 유지", Get<MonsterTunerSession>(window, "session") == session && session.Dirty && second.Dirty);
                Check("전환 프리뷰 한 마리", MonsterTunerPreviewStage.LiveStages == live + 1);
                persisted = MonsterTunerSession.Create(a); recovered = MonsterTunerSession.Create(b);
                recoveryA = persisted.RecoveryPath; recoveryB = recovered.RecoveryPath;
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(session), persisted); persisted.Persist();
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(second), recovered); recovered.Persist();
                Object.DestroyImmediate(persisted); persisted = MonsterTunerSession.Create(a);
                Object.DestroyImmediate(recovered); recovered = MonsterTunerSession.Create(b);
                Check("두 사본 JSON 복원", persisted.Restore() && recovered.Restore() && persisted.Value("variant", "visualScale").vector == scale.vector && recovered.Value("variant", "visualScale").vector == secondScale.vector);
                var hits = persisted.Value("ability:" + abilityIndex, "additionalHitNormalizedTimes"); hits.numbers = new[] { .55f, .75f }; persisted.Set("ability:" + abilityIndex, "additionalHitNormalizedTimes", hits, "추가 타격 복원");
                Object.DestroyImmediate(persisted); persisted = MonsterTunerSession.Create(a); persisted.Restore();
                Check("배열·벡터 혼합 복원", persisted.Value("ability:" + abilityIndex, "additionalHitNormalizedTimes").numbers.SequenceEqual(hits.numbers) && persisted.Value("variant", "visualScale").vector == scale.vector);
                Check("복구 지문 외부 변경 감지", persisted.stamps.All(s => s.Matches()));
                Call(window, "BeforeReload");
                Check("Reload 전 프리뷰 해제", MonsterTunerPreviewStage.LiveStages == live);
                Select(a);
                var sharedSource = catalog.Entries.First(e => MonsterTunerAnimationBindings.Read(e.Definition.AnimationProfile).GroupBy(x => x.Original).Any(g => g.Count() > 1)).Definition;
                var sharedBindings = MonsterTunerAnimationBindings.Read(sharedSource.AnimationProfile);
                var role = sharedBindings.GroupBy(x => x.Original).First(g => g.Count() > 1).First();
                motionSession = MonsterTunerSession.Create(sharedSource, false);
                var motion = motionSession.Value("animation", "motion:" + role.Key); motion.text = GlobalObjectId.GetGlobalObjectIdSlow(replacement).ToString(); motionSession.Set("animation", "motion:" + role.Key, motion, "단일 역할 교체");
                Check("공유 클립 역할별 Controller 필요", !MonsterTunerAnimationBindings.CanUseOverrides(sharedBindings, MonsterTunerAnimationBindings.Replacements(motionSession)));
                var controller = MonsterTunerAnimationBindings.SaveController(motionSession, root + "/Independent.controller");
                var final = MonsterTunerAnimationBindings.ReadController(controller);
                Check("단일 역할 실제 Motion 교체", final.First(x => x.Key == role.Key).Actual == replacement);
                Check("같은 클립 타 역할 보존", sharedBindings.Where(x => x.Key != role.Key).All(x => final.First(f => f.Key == x.Key).Actual == x.Actual));
                Check("Controller 상태·파라미터 유지", final.Count == sharedBindings.Count && ((AnimatorController)controller).parameters.Length == MonsterTunerAnimationBindings.BaseController(sharedSource.AnimationProfile.RuntimeController).parameters.Length);
                workingProfile = Object.Instantiate(sharedSource.AnimationProfile); MonsterTunerAnimationBindings.SynchronizeProfile(motionSession, workingProfile, controller);
                Check("프로필 실제 Controller 동기화", workingProfile.RuntimeController == controller);
                Check("게임 원본·FBX 불변", production.All(p => p.Value == MonsterTunerStamp.FileHash(p.Key)));
                Check("제품 씬 dirty 보존", beforeScenes == Scenes());
                session.Discard(); second.Discard(); window.CloseVerification();
                Check("고급 검증 자원 회수", MonsterTunerPreviewStage.LiveStages == live);
                Save("advanced", true, "");
                ObjectField MotionField() => Get<ScrollView>(window, "fields").Query<ObjectField>().ToList().First(f => f.label == binding.Label);
            }
            catch (Exception e) { Save("advanced", false, e.ToString()); }
            finally
            {
                if (window != null) window.CloseVerification();
                foreach (var owned in new Object[] { persisted, recovered, motionSession, workingProfile }) if (owned != null) { Undo.ClearUndo(owned); Object.DestroyImmediate(owned); }
                foreach (string path in new[] { recoveryA, recoveryB }) if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
                AssetDatabase.DeleteAsset(root);
                if (AssetDatabase.IsValidFolder(fixtures) && Directory.GetFileSystemEntries(fixtures).Length == 0) AssetDatabase.DeleteAsset(fixtures);
            }
        }
        void Select(EnemyDefinition definition)
        {
            var entry = new MonsterTunerCatalog.Entry { Definition = definition, Guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(definition)) };
            typeof(MonsterTunerWindow).GetMethod("SelectEntry", Private).Invoke(window, new object[] { entry });
        }
        string Scenes() => string.Join("|", Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i)).Select(s => s.path + ":" + s.isDirty));
    }
}
