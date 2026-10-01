using System;
using System.IO;
using System.Linq;
using Overburst.EditorTools.MonsterTuner;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static partial class MonsterTunerVerifier
{
    private static string HitOutput => Path.Combine(MonsterTunerSession.OutputRoot, "20261001_ReactionPlayback", "QA");
    public static string ReactionPlayback()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return "Editor busy";
        Checks.Clear(); Directory.CreateDirectory(HitOutput);
        var catalog = new MonsterTunerCatalog(); catalog.Refresh();
        var hashes = AssetDatabase.GetDependencies(catalog.Entries.Select(e => AssetDatabase.GetAssetPath(e.Definition)).ToArray(), true)
            .Where(File.Exists).Distinct().ToDictionary(p => p, MonsterTunerStamp.FileHash);
        string scenes = SceneState(); int live = MonsterTunerPreviewStage.LiveStages, textures = MonsterTunerWindow.LiveThumbnailTextures;
        float listWidth = EditorPrefs.GetFloat("Overburst.MonsterTuner.ListWidth", 240), detailWidth = EditorPrefs.GetFloat("Overburst.MonsterTuner.DetailWidth", 360);
        var window = ScriptableObject.CreateInstance<MonsterTunerWindow>(); window.VerificationOnly = true;
        window.titleContent = new GUIContent("몬스터 튜너 피격 검증"); window.position = new Rect(60, 60, 1440, 900); window.ShowUtility(); window.Focus();
        int index = 0, step = 0; double ready = EditorApplication.timeSinceStartup + 1d, deadline = ready + 100d;
        string draft = null; AnimationClip expected = null; bool replaced = false;
        EditorApplication.update += Run; return "Hit playback verification queued";
        void Run()
        {
            if (window == null) { Finish(false, "Verification window disappeared"); return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) { Finish(false, "Shared Editor became busy during UI verification"); return; }
            if (EditorApplication.timeSinceStartup < ready || window.rootVisualElement.panel == null) return;
            ready = EditorApplication.timeSinceStartup + .2d;
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Hit playback at " + index + ":" + step);
                var root = window.rootVisualElement;
                var stage = Get<MonsterTunerPreviewStage>(window, "stage");
                if (index < catalog.Entries.Count)
                {
                    var entry = catalog.Entries[index];
                    if (step == 3)
                    {
                        CapturePanel(window, Path.Combine(HitOutput, "hit-preview.png"));
                        var button = root.Q<Button>("hit-preview-play");
                        var scroll = Get<ScrollView>(window, "fields");
                        Check("최소 1100×720 피격·패링 버튼 스크롤 없이 표시", scroll.worldBound.Contains(button.worldBound.center)
                            && scroll.worldBound.Contains(root.Q<Button>("parry-preview-play").worldBound.center) && button.layout.width > 200f);
                        window.position = new Rect(60, 60, 1440, 900); index++; step = 0; return;
                    }
                    if (step == 0)
                    {
                        typeof(MonsterTunerWindow).GetMethod("SelectEntry", Private).Invoke(window, new object[] { entry });
                        Set(window, "tab", 0); Call(window, "BuildFields");
                        Check(entry.Label + " 기본 크기 세부 설정 접힘", !root.Q<Foldout>("scale-details").value);
                        Set(window, "tab", 4); Call(window, "BuildFields");
                        Check(entry.Label + " 모션 연결 수정 기본 접힘", !root.Q<Foldout>("motion-editing").value);
                        var bindings = MonsterTunerAnimationBindings.Read(entry.Definition.AnimationProfile);
                        var hits = bindings.Where(MonsterTunerAnimationBindings.IsHit).ToList();
                        Check(entry.Label + " 실제 피격 역할 발견", hits.Count > 0);
                        var fold = root.Q<Foldout>("auxiliary-motions");
                        Check(entry.Label + " 모든 피격 주요 모션 분류", hits.All(b => b.Label.StartsWith("피격", StringComparison.Ordinal)
                            && !(fold != null && fold.Contains(root.Q("motion-slot:" + b.Key)))));
                        Check(entry.Label + " hit 프로필 연결은 실제 일치 슬롯만", hits.Where(b => b.ProfileProperties.Contains("hit")).All(b => b.Actual == entry.Definition.AnimationProfile.Hit));
                        if (hits.Count == 4) Check(entry.Label + " 네 방향 이름 구분", hits.Select(b => b.Label).Distinct().Count() == 4
                            && new[] { "정면", "후면", "좌측", "우측" }.All(d => hits.Any(b => b.Label.EndsWith(d, StringComparison.Ordinal))));
                        draft = JsonUtility.ToJson(Get<MonsterTunerSession>(window, "session")); step = 1; return;
                    }
                    if (step == 1)
                    {
                        CapturePanel(window, Path.Combine(HitOutput, "layout.png"));
                        VerifyQuickMotions(entry.Definition, entry.Label);
                        Set(window, "tab", 4); Call(window, "BuildFields"); CapturePanel(window, Path.Combine(HitOutput, "layout.png"));
                        VerifyParry(entry.Definition, entry.Label);
                        var hits = MonsterTunerAnimationBindings.Read(entry.Definition.AnimationProfile).Where(MonsterTunerAnimationBindings.IsHit).ToList();
                        var direction = root.Q<DropdownField>("hit-preview-choice");
                        if (direction != null)
                            foreach (string choice in direction.choices)
                            {
                                ChangeControl(direction, choice); Click(root.Q<Button>("hit-preview-play"));
                                Check(entry.Label + " " + choice + " 독립 재생", stage.Clip == hits[direction.index].Actual && stage.Playing);
                            }
                        var binding = direction != null ? hits[direction.index] : hits[0];
                        expected = MonsterTunerAnimationBindings.WorkingClip(Get<MonsterTunerSession>(window, "session"), binding);
                        Click(root.Q<Button>("hit-preview-play"));
                        Check(entry.Label + " 피격 버튼 실제 클립 재생", stage.Playing && stage.Clip == expected);
                        step = 2; return;
                    }
                    Check(entry.Label + " 재생 시간이 전진", stage.Time > 0f);
                    Check(entry.Label + " 방향 선택·재생 사본 불변", draft == JsonUtility.ToJson(Get<MonsterTunerSession>(window, "session")));
                    if (!replaced && root.Q<DropdownField>("hit-preview-choice") != null)
                    {
                        var session = Get<MonsterTunerSession>(window, "session");
                        var hits = MonsterTunerAnimationBindings.Read(entry.Definition.AnimationProfile).Where(MonsterTunerAnimationBindings.IsHit).ToList();
                        var direction = root.Q<DropdownField>("hit-preview-choice"); var hit = hits[direction.index];
                        var replacement = entry.Definition.AnimationProfile.Idle;
                        Check("교체 검사 클립 존재·별개", replacement != null && replacement != expected);
                        Change(root.Q("motion-slot:" + hit.Key).Q<ObjectField>(), (Object)replacement);
                        Check("피격 교체 즉시 안내 갱신", root.Q<Label>("hit-preview-clip").text.Contains(replacement.name));
                        Check("교체 뒤 선택 방향 유지", Get<string>(window, "hitPreviewKey") == hit.Key);
                        CapturePanel(window, Path.Combine(HitOutput, "replaced-hit.png")); Click(root.Q<Button>("hit-preview-play"));
                        Check("미저장 교체 피격 재생", stage.Clip == replacement && stage.Playing);
                        Check("다른 피격 방향 독립", hits.Where(b => b.Key != hit.Key).All(b => MonsterTunerAnimationBindings.WorkingClip(session, b) == b.Actual));
                        stage.Sample(replacement.length * .5f); Click(root.Q<Button>("hit-preview-play"));
                        Check("피격 다시 재생은 처음부터", stage.Time < .01f && stage.Playing);
                        var value = session.Value("animation", "motion:" + hit.Key); value.text = string.Empty;
                        session.Set("animation", "motion:" + hit.Key, value, "누락 검사"); Call(window, "BuildFields");
                        Check("피격 클립 누락 재생 차단·안내", !root.Q<Button>("hit-preview-play").enabledSelf
                            && root.Q<Label>("hit-preview-clip").text.Contains("연결하세요"));
                        Check("누락 피격 방향의 상단 바로 재생도 비활성", !root.Q<Button>("quick-motion:피격").enabledSelf);
                        session.Discard(); Call(window, "UndoRedo");
                        window.position = new Rect(60, 60, 1100, 720); step = 3; replaced = true; return;
                    }
                    index++; step = 0; return;
                }
                foreach (string role in new[] { "대기", "이동", "공격", "피격", "패링", "사망" }) root.Q<Button>("quick-motion:" + role).SetEnabled(false);
                typeof(MonsterTunerWindow).GetMethod("PlayState", Private).Invoke(window, new object[] { PlayModeStateChange.EnteredEditMode });
                Check("Play 반환 처리 뒤 피격·패링 버튼 활성 복구", root.Q<Button>("quick-motion:피격").enabledSelf && root.Q<Button>("quick-motion:패링").enabledSelf);
                Check("30종 전체 피격·패링 재생 검사", catalog.Entries.Count >= 30 && replaced);
                Check("제품 몬스터·Controller·Clip 파일 불변", hashes.All(p => MonsterTunerStamp.FileHash(p.Key) == p.Value));
                Check("제품 씬·dirty 상태 보존", SceneState() == scenes);
                window.CloseVerification(); window = null;
                Check("테스트 프리뷰·썸네일 반환", MonsterTunerPreviewStage.LiveStages == live && MonsterTunerWindow.LiveThumbnailTextures == textures);
                Finish(true, "");
            }
            catch (Exception error) { Finish(false, error.ToString()); }
        }
        void VerifyQuickMotions(EnemyDefinition definition, string name)
        {
            var root = window.rootVisualElement; var stage = Get<MonsterTunerPreviewStage>(window, "stage");
            var session = Get<MonsterTunerSession>(window, "session"); string before = JsonUtility.ToJson(session);
            foreach (string role in new[] { "대기", "이동", "공격", "피격", "패링", "사망" })
            {
                var button = root.Q<Button>("quick-motion:" + role);
                Check(name + " " + role + " 바로 재생 버튼", button != null);
                if (!button.enabledInHierarchy) continue;
                AnimationClip expectedClip;
                if (role == "공격") expectedClip = MonsterTunerAnimationBindings.AttackClip(session, definition.AbilitySet.GetAbility(Get<int>(window, "abilityIndex")), Get<int>(window, "abilityIndex"));
                else
                {
                    var binding = (MonsterTunerAnimationBindings.Binding)typeof(MonsterTunerWindow).GetMethod("QuickBinding", Private).Invoke(window, new object[] { role });
                    expectedClip = MonsterTunerAnimationBindings.WorkingClip(session, binding);
                }
                Click(button);
                Check(name + " " + role + " 바로 재생 동작", stage.Playing && stage.Clip == expectedClip && root.Q<Label>("quick-motion-status").text.Contains(role));
            }
            Check(name + " 바로 재생 사본 불변", before == JsonUtility.ToJson(session));
        }
        void VerifyParry(EnemyDefinition definition, string name)
        {
            var root = window.rootVisualElement;
            var stage = Get<MonsterTunerPreviewStage>(window, "stage");
            var session = Get<MonsterTunerSession>(window, "session");
            string before = JsonUtility.ToJson(session);
            var bindings = MonsterTunerAnimationBindings.Read(definition.AnimationProfile);
            var parries = bindings.Where(b => b.Label.StartsWith("패링", StringComparison.Ordinal)).ToList();
            bool fallback = parries.Count == 0;
            if (fallback) parries = bindings.Where(MonsterTunerAnimationBindings.IsHit).ToList();
            else Check(name + " 패링 세 단계 연결", parries.Count == 3);
            var choice = root.Q<DropdownField>("parry-preview-choice");
            int count = choice != null ? choice.choices.Count : 1;
            for (int i = 0; i < count; i++)
            {
                if (choice != null) ChangeControl(choice, choice.choices[i]);
                Click(root.Q<Button>("parry-preview-play"));
                var binding = choice != null ? parries[choice.index] : parries[0];
                Check(name + " " + binding.Label + (fallback ? " 패링 대체 반응 재생" : " 패링 단계 재생"), stage.Playing && stage.Clip == binding.Actual);
                Click(root.Q<Button>("quick-motion:패링"));
                Check(name + " " + binding.Label + " 선택 뒤 상단 패링 버튼도 같은 클립 재생", stage.Playing && stage.Clip == binding.Actual
                    && root.Q<Label>("quick-motion-status").text.Contains("패링"));
                choice = root.Q<DropdownField>("parry-preview-choice");
                // The quick button rebuilds the cards; lay out their new click targets.
                var view = typeof(EditorWindow).GetField("m_Parent", Private).GetValue(window);
                var repaint = view.GetType().GetMethod("RepaintImmediately", Private);
                repaint.Invoke(view, null); repaint.Invoke(view, null);
            }
            Check(name + " 패링 선택·재생 사본 불변", before == JsonUtility.ToJson(session));
            if (!fallback && !replaced)
            {
                var binding = parries[choice != null ? choice.index : 0];
                var replacement = definition.AnimationProfile.Idle;
                Change(root.Q("motion-slot:" + binding.Key).Q<ObjectField>(), (Object)replacement);
                Check("패링 교체 즉시 안내 갱신", root.Q<Label>("parry-preview-clip").text.Contains(replacement.name));
                CapturePanel(window, Path.Combine(HitOutput, "parry-preview.png")); Click(root.Q<Button>("parry-preview-play"));
                Check("미저장 교체 패링 재생", stage.Playing && stage.Clip == replacement);
                Check("다른 패링 단계 독립", parries.Where(b => b.Key != binding.Key).All(b => MonsterTunerAnimationBindings.WorkingClip(session, b) == b.Actual));
                session.Discard(); Call(window, "UndoRedo"); CapturePanel(window, Path.Combine(HitOutput, "layout.png"));
            }
        }
        void Finish(bool success, string error)
        {
            EditorApplication.update -= Run;
            if (window != null) window.CloseVerification();
            EditorPrefs.SetFloat("Overburst.MonsterTuner.ListWidth", listWidth); EditorPrefs.SetFloat("Overburst.MonsterTuner.DetailWidth", detailWidth);
            File.WriteAllText(Path.Combine(HitOutput, "hit-verification.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new { success, error, count = Checks.Count, checks = Checks }, Newtonsoft.Json.Formatting.Indented));
        }
    }
}
