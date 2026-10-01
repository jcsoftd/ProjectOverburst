using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Overburst.EditorTools.MonsterTuner;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static partial class MonsterTunerVerifier
{
    private static string UXOutput => Path.Combine(MonsterTunerSession.OutputRoot, "20261001_UXUpdate", "QA");
    public static string UXUpdate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return "Editor busy";
        Checks.Clear(); Directory.CreateDirectory(UXOutput);
        int live = MonsterTunerPreviewStage.LiveStages, textures = MonsterTunerWindow.LiveThumbnailTextures;
        string scenes = SceneState();
        float listWidth = EditorPrefs.GetFloat("Overburst.MonsterTuner.ListWidth", 240), detailWidth = EditorPrefs.GetFloat("Overburst.MonsterTuner.DetailWidth", 360);
        string fixtures = "Assets/Editor/Testers/Characters/MonsterTuner/Fixtures";
        if (!AssetDatabase.IsValidFolder(fixtures)) AssetDatabase.CreateFolder("Assets/Editor/Testers/Characters/MonsterTuner", "Fixtures");
        string fixture = fixtures + "/UX_" + Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder(fixtures, fixture.Split('/').Last());
        var catalog = new MonsterTunerCatalog(); catalog.Refresh();
        var hashes = AssetDatabase.GetDependencies(catalog.Entries.Select(e => AssetDatabase.GetAssetPath(e.Definition)).ToArray(), true)
            .Where(p => File.Exists(p) && (p.StartsWith("Assets/ProjectOverburst/", StringComparison.Ordinal) || p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))).Distinct().ToDictionary(p => p, MonsterTunerStamp.FileHash);
        var window = ScriptableObject.CreateInstance<MonsterTunerWindow>(); window.VerificationOnly = true;
        window.titleContent = new GUIContent("몬스터 튜너 UX 검증"); window.position = new Rect(60, 60, 1440, 900); window.ShowUtility();
        int step = 0; double ready = EditorApplication.timeSinceStartup + 2d, deadline = ready + 90d;
        EnemyDefinition definition = null, peer = null; MonsterTunerSession session = null;
        MonsterTunerSaveReview review = null; string peerHash = null, pendingHash = null;
        List<MonsterTunerWriter.SaveTarget> planned = null;
        EditorApplication.update += Run; Progress("queued"); return "UX update verification queued";
        void Run()
        {
            if (window == null) { Finish(false, "Verification window disappeared"); return; }
            if (EditorApplication.timeSinceStartup > deadline) { Finish(false, "UX verification timed out at " + step); return; }
            if (EditorApplication.timeSinceStartup < ready || window.rootVisualElement.panel == null) return;
            ready = EditorApplication.timeSinceStartup + .3d;
            try
            {
                Progress("step " + step);
                var root = window.rootVisualElement;
                var stage = Get<MonsterTunerPreviewStage>(window, "stage");
                var viewport = Get<MonsterTunerViewport>(window, "viewport");
                if (step == 0)
                {
                    var icons = root.Query<Image>("icon").ToList().Select(i => i.image).OfType<Texture2D>().ToList();
                    Check("보이는 행 실제 썸네일 생성", icons.Count > 0);
                    Check("썸네일에 모델 픽셀 포함", icons.Any(t => t.GetPixels32().Select(p => p.r + ":" + p.g + ":" + p.b).Distinct().Count() > 60));
                    Check("화면 밖 몬스터 지연 로드", MonsterTunerWindow.LiveThumbnailTextures - textures < catalog.Entries.Count);
                    Check("썸네일 임시 프리뷰 즉시 반환", MonsterTunerPreviewStage.LiveStages == live + 1);
                    var before = Get<MonsterTunerSession>(window, "session"); string json = JsonUtility.ToJson(before);
                    var badgeScale = before.Value("variant", "visualScale"); badgeScale.vector *= 1.01f; before.Set("variant", "visualScale", badgeScale, "상태 표시 검사"); Call(window, "UpdateHeader");
                    var row = root.Query<VisualElement>().ToList().First(e => e.ClassListContains("mt-item") && Get<Dictionary<VisualElement, string>>(window, "thumbnailRows").TryGetValue(e, out string id) && id == before.definitionGuid);
                    Check("선택 행 ID·미저장 분리 표시", row.Q<Label>("meta").text == before.Definition.EnemyId && row.Q<Label>("state").text.Contains("미저장"));
                    before.Discard(); Call(window, "UpdateHeader");
                    var search = Get<ToolbarSearchField>(window, "search"); ChangeControl(search, "없는_몬스터_UX");
                    Check("재사용 행 이전 썸네일 연결 해제", Get<Dictionary<VisualElement, string>>(window, "thumbnailRows").Count == 0);
                    ChangeControl(search, string.Empty); Check("목록 검색 사본 불변", JsonUtility.ToJson(before) == json);
                    var source = catalog.Entries.First(e => e.Definition.ActorPrefab.GetComponent<EnemyThemeSpecialExecutor>() != null &&
                        Enumerable.Range(0, e.Definition.AbilitySet?.Count ?? 0).Any(i => e.Definition.AbilitySet.GetAbility(i)?.ExecutionMode == EnemyAbilityExecutionMode.Projectile)).Definition;
                    Check("UX fixture 정의 복사", AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), fixture + "/Selected.asset") && AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), fixture + "/Peer.asset"));
                    definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(fixture + "/Selected.asset"); peer = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(fixture + "/Peer.asset");
                    int ranged = Enumerable.Range(0, source.AbilitySet.Count).First(i => source.AbilitySet.GetAbility(i)?.ExecutionMode == EnemyAbilityExecutionMode.Projectile);
                    AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source.AbilitySet), fixture + "/Set.asset");
                    var set = AssetDatabase.LoadAssetAtPath<EnemyAbilitySet>(fixture + "/Set.asset");
                    var so = new SerializedObject(set); so.FindProperty("abilities").arraySize = 2; so.ApplyModifiedPropertiesWithoutUndo();
                    for (int i = 0; i < 2; i++)
                    {
                        AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source.AbilitySet.GetAbility(ranged)), fixture + "/Ability" + i + ".asset");
                        SetFixtureReference(set, "abilities.Array.data[" + i + "]", AssetDatabase.LoadAssetAtPath<EnemyAbilityDefinition>(fixture + "/Ability" + i + ".asset"));
                    }
                    SetFixtureReference(definition, "abilitySet", set); SetFixtureReference(peer, "abilitySet", set);
                    peerHash = MonsterTunerStamp.FileHash(AssetDatabase.GetAssetPath(peer));
                    var ownedCatalog = Get<MonsterTunerCatalog>(window, "catalog"); ownedCatalog.Entries.Add(Entry(definition)); ownedCatalog.Entries.Add(Entry(peer));
                    Select(definition); session = Get<MonsterTunerSession>(window, "session");
                    var scale = session.Value("variant", "visualScale"); scale.vector = Vector3.Scale(scale.vector, new Vector3(1.15f, 1.2f, .95f)); session.Set("variant", "visualScale", scale, "외형 크기");
                    Call(window, "UndoRedo");
                    var anchor = viewport.Points.First(p => p.Key.EndsWith("|m_LocalPosition") && p.Move != null); viewport.Select(anchor);
                    var card = root.Q("selected-point-card"); var local = root.Q<Vector3Field>("point-local"); Vector3 start = local.value;
                    Check("선택점 카드 ScrollView 밖 고정", card.parent != Get<ScrollView>(window, "fields") && card.parent == Get<ScrollView>(window, "fields").parent);
                    Change(local, start + new Vector3(.2f, .1f, -.1f));
                    string[] key = anchor.Key.Split('|'); Check("카드 XYZ 실제 사본 편집", session.Value(key[0], key[1]).vector == start + new Vector3(.2f, .1f, -.1f));
                    var node = MonsterTunerAddress.ResolveComponent(stage.Actor, key[0]) as Transform;
                    Check("부모 local과 현재 world 일치", Vector3.Distance(anchor.World(), node.parent.TransformPoint(root.Q<Vector3Field>("point-local").value)) < .001f);
                    Check("카드 부모 경로 표시", root.Q<Label>("point-parent").text.Contains(node.parent.name));
                    Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Check("카드 숫자 Undo", session.Value(key[0], key[1]).vector == start);
                    Undo.PerformRedo(); Check("카드 숫자 Redo", session.Value(key[0], key[1]).vector != start);
                    step++; return;
                }
                if (step == 1)
                {
                    CapturePanel(window, Path.Combine(UXOutput, "point-card.png"));
                    Click(root.Q<Button>("point-reset"));
                    Check("선택점만 되돌리고 크기 유지", session.edits.Count == 1 && session.edits[0].target == "variant");
                    Set(window, "tab", 3); Call(window, "BuildFields");
                    Change(Get<ScrollView>(window, "fields").Query<Toggle>().ToList().First(t => t.label == "몬스터별 머즐 조절"), true);
                    viewport.Select(viewport.Points.First(p => p.Label == "원거리 머즐"));
                    Change(root.Q<Vector3Field>("point-local"), new Vector3(.2f, .8f, .1f));
                    string address = MonsterTunerAddress.Component(stage.Actor, stage.Actor.GetComponent<EnemyThemeSpecialExecutor>());
                    Set(window, "abilityIndex", 1); Call(window, "BuildFields"); Call(window, "RefreshPoints");
                    Change(Get<ScrollView>(window, "fields").Query<Toggle>().ToList().First(t => t.label == "몬스터별 머즐 조절"), true);
                    viewport.Select(viewport.Points.First(p => p.Label == "원거리 머즐"));
                    Change(root.Q<Vector3Field>("point-local"), new Vector3(-.3f, .7f, .2f));
                    Set(window, "abilityIndex", 0); Call(window, "BuildFields"); Call(window, "RefreshPoints");
                    Click(root.Q<Button>("point-reset"));
                    var entries = session.Value(address, "muzzleOverrides").muzzles;
                    Check("머즐 되돌리기 다른 공격 보존", entries.Length == 1 && entries[0].ability.Resolve() == definition.AbilitySet.GetAbility(1) && entries[0].offset == new Vector3(-.3f, .7f, .2f));
                    Check("자동 머즐 카드 편집 비활성", root.Q<Vector3Field>("point-local").resolvedStyle.display == DisplayStyle.None || !root.Q<Vector3Field>("point-local").enabledSelf);
                    Set(window, "tab", 4); Call(window, "BuildFields");
                    var bindings = MonsterTunerAnimationBindings.Read(definition.AnimationProfile);
                    var aux = root.Q<Foldout>("auxiliary-motions");
                    Check("보조 모션 기본 접힘", aux != null && !aux.value);
                    Check("주요 대기·이동·공격·패링 기본 표시", bindings.Where(b => new[] { "대기", "걷기", "달리기", "피격", "사망" }.Contains(b.Label) || b.Label.StartsWith("패링") || b.StatePath.Contains(".Attack_")).All(b => !aux.Contains(root.Q("motion-slot:" + b.Key))));
                    string draft = JsonUtility.ToJson(session); ChangeControl(aux, true); Check("보조 모션 펼치기 사본 불변", aux.value && JsonUtility.ToJson(session) == draft);
                    Check("모든 실제 모션 슬롯 유지", Get<ScrollView>(window, "fields").Query<ObjectField>().ToList().Count == bindings.Count);
                    step++; return;
                }
                if (step == 2)
                {
                    CapturePanel(window, Path.Combine(UXOutput, "motion-foldout.png"));
                    Set(window, "auxiliaryMotionsExpanded", false); Call(window, "BuildFields");
                    Call(window, "SaveSelected"); review = Get<MonsterTunerSaveReview>(window, "saveReview");
                    planned = MonsterTunerWriter.ReviewTargets(session, Get<MonsterTunerCatalog>(window, "catalog"));
                    pendingHash = MonsterTunerStamp.FileHash(AssetDatabase.GetAssetPath(definition));
                    step++; return;
                }
                if (step == 3)
                {
                    CapturePanel(review, Path.Combine(UXOutput, "save-review.png"));
                    File.WriteAllLines(Path.Combine(UXOutput, "review-elements.txt"), Descendants(review.rootVisualElement).Select(e => e.GetType().Name + " " + e.name));
                    Check("저장 검토 Toolkit 전용", review.rootVisualElement.Query<IMGUIContainer>().ToList().Count == 0);
                    int rows = Descendants(review.rootVisualElement).Count(e => e.name?.StartsWith("review-change:") == true);
                    Check("저장 검토 전후 값 표", rows == session.edits.Count, rows + " / " + session.edits.Count);
                    Check("공유 수·신규 경로 계획", planned.Any(t => t.Label == "크기 프로필" && t.Created && t.SharedCount > 1) && planned.Any(t => t.Label == "Actor 프리팹" && t.Created));
                    Check("미생성 폴더 신규 경로 표시", planned.Where(t => t.Created).All(t => !string.IsNullOrEmpty(t.DestinationPath) && t.DestinationPath.StartsWith("Assets/ProjectOverburst/Resources/Enemies/Tuning/")));
                    Check("기존 정의 경로 계획", planned.First(t => t.Label == "몬스터 정의").DestinationPath == AssetDatabase.GetAssetPath(definition) && !planned.First().Created);
                    Click(review.rootVisualElement.Q<Button>("review-cancel")); review = null;
                    Check("취소 원본·사본 보존", session.Dirty && pendingHash == MonsterTunerStamp.FileHash(AssetDatabase.GetAssetPath(definition)));
                    var invalid = session.Value("variant", "visualScale"); invalid.vector.x = -1; session.Set("variant", "visualScale", invalid, "잘못된 크기");
                    Call(window, "SaveSelected"); review = Get<MonsterTunerSaveReview>(window, "saveReview"); step++; return;
                }
                if (step == 4)
                {
                    Check("첫 검증 오류 상단 표시", review.rootVisualElement.Q<Label>("review-first-error").text.Contains("크기 XYZ"));
                    Check("오류 상태 저장 차단", !review.rootVisualElement.Q<Button>("review-confirm").enabledSelf);
                    CapturePanel(review, Path.Combine(UXOutput, "save-validation-error.png"));
                    Click(review.rootVisualElement.Q<Button>("review-cancel")); review = null;
                    var valid = session.Value("variant", "visualScale"); valid.vector.x = 1.15f; session.Set("variant", "visualScale", valid, "외형 크기");
                    MonsterTunerWriter.FixtureRoot = fixture;
                    Call(window, "SaveSelected"); review = Get<MonsterTunerSaveReview>(window, "saveReview"); step++; return;
                }
                if (step == 5)
                {
                    string fingerprint = review.ReviewedEdits;
                    var altered = session.Value("variant", "visualScale"); altered.vector.x = 1.16f; session.Set("variant", "visualScale", altered, "검토 후 변경");
                    Check("검토 뒤 변경 저장 거절", window.CommitReview(session, fingerprint).Contains("편집이 바뀌었습니다") && session.Dirty);
                    Click(review.rootVisualElement.Q<Button>("review-refresh"));
                    Check("새로 검증한 사본 저장 가능", review.rootVisualElement.Q<Button>("review-confirm").enabledSelf);
                    CapturePanel(review, Path.Combine(UXOutput, "save-ready.png"));
                    var finalPlan = MonsterTunerWriter.ReviewTargets(session, Get<MonsterTunerCatalog>(window, "catalog"));
                    Click(review.rootVisualElement.Q<Button>("review-confirm")); review = null;
                    Check("검토 화면 실제 선택 저장", !session.Dirty && definition.Variant != peer.Variant && definition.ActorPrefab != peer.ActorPrefab);
                    Check("다른 몬스터 저장 없음", peerHash == MonsterTunerStamp.FileHash(AssetDatabase.GetAssetPath(peer)));
                    Check("저장 후 원본 재로드 값", Mathf.Approximately(definition.Variant.VisualScale.x, 1.16f));
                    Check("검토 경로와 실제 신규 자산 일치", finalPlan.Where(t => t.Created).All(t => AssetDatabase.LoadMainAssetAtPath(t.DestinationPath) != null));
                    // Existing overrides must reset in place without creating an array-order-only draft.
                    string actorPath = AssetDatabase.GetAssetPath(definition.ActorPrefab);
                    var savedActor = PrefabUtility.LoadPrefabContents(actorPath);
                    try
                    {
                        var executor = new SerializedObject(savedActor.GetComponent<EnemyThemeSpecialExecutor>());
                        var array = MonsterTunerValue.Read(executor.FindProperty("muzzleOverrides"), savedActor);
                        array.muzzles = array.muzzles.Concat(new[] { new MonsterTunerMuzzleValue
                        {
                            ability = new MonsterTunerValue { type = SerializedPropertyType.ObjectReference, text = GlobalObjectId.GetGlobalObjectIdSlow(definition.AbilitySet.GetAbility(0)).ToString() },
                            socket = new MonsterTunerValue { type = SerializedPropertyType.ObjectReference, text = "actor:" }, offset = new Vector3(.1f, .9f, .2f)
                        } }).ToArray();
                        array.Write(executor.FindProperty("muzzleOverrides"), savedActor); executor.ApplyModifiedPropertiesWithoutUndo();
                        PrefabUtility.SaveAsPrefabAsset(savedActor, actorPath);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(savedActor); }
                    session.Saved(); Call(window, "UndoRedo"); Set(window, "abilityIndex", 1); Set(window, "tab", 3); Call(window, "BuildFields"); Call(window, "RefreshPoints");
                    viewport.Select(viewport.Points.First(p => p.Label == "원거리 머즐"));
                    string muzzleAddress = MonsterTunerAddress.Component(stage.Actor, stage.Actor.GetComponent<EnemyThemeSpecialExecutor>());
                    string savedMuzzles = JsonUtility.ToJson(session.Value(muzzleAddress, "muzzleOverrides"));
                    Change(root.Q<Vector3Field>("point-local"), new Vector3(.7f, 1.1f, .2f));
                    Click(root.Q<Button>("point-reset"));
                    Check("기존 머즐 되돌리기 배열 순서·다른 공격 보존", savedMuzzles == JsonUtility.ToJson(session.Value(muzzleAddress, "muzzleOverrides")) && !session.Dirty);
                    Call(window, "UndoRedo"); viewport.Select(viewport.Points.First(p => p.Label.StartsWith("몸 충돌")));
                    window.position = new Rect(60, 60, 1100, 720); root.style.width = 1100; root.style.height = 720; step++; return;
                }
                CapturePanel(window, Path.Combine(UXOutput, "minimum-window.png"));
                Check("최소 폭 카드 XYZ 입력 폭", root.Q<Vector3Field>("point-local").Query<FloatField>().ToList().All(f => f.Q(className: "unity-base-field__input").layout.width >= 36));
                Check("최소 창 카드·상세 스크롤 공존", root.Q("selected-point-card").layout.height < 220 && Get<ScrollView>(window, "fields").layout.height > 250);
                Check("원본 몬스터·클립 파일 불변", hashes.All(p => MonsterTunerStamp.FileHash(p.Key) == p.Value));
                Check("열린 씬·dirty 상태 보존", SceneState() == scenes);
                window.CloseVerification(); window = null;
                Check("썸네일·프리뷰 전부 해제", MonsterTunerWindow.LiveThumbnailTextures == textures && MonsterTunerPreviewStage.LiveStages == live);
                Finish(true, "");
            }
            catch (Exception error) { Finish(false, error.ToString()); }
        }
        void Select(EnemyDefinition value) => typeof(MonsterTunerWindow).GetMethod("SelectEntry", Private).Invoke(window, new object[] { Entry(value) });
        void Finish(bool success, string error)
        {
            EditorApplication.update -= Run;
            if (review != null) review.Close(); if (window != null) window.CloseVerification();
            MonsterTunerWriter.FixtureRoot = null;
            if (AssetDatabase.IsValidFolder(fixture)) AssetDatabase.DeleteAsset(fixture);
            EditorPrefs.SetFloat("Overburst.MonsterTuner.ListWidth", listWidth); EditorPrefs.SetFloat("Overburst.MonsterTuner.DetailWidth", detailWidth);
            File.WriteAllText(Path.Combine(UXOutput, "ux-verification.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new { success, error, count = Checks.Count, checks = Checks }, Newtonsoft.Json.Formatting.Indented));
            Progress(success ? "complete" : "failed");
        }
        void Progress(string phase) => File.WriteAllText(Path.Combine(UXOutput, "progress.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new { phase, step }));
    }
    private static MonsterTunerCatalog.Entry Entry(EnemyDefinition value) => new MonsterTunerCatalog.Entry { Definition = value, Guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(value)), Theme = "검증", Grade = "검증" };
    private static string SceneState() => string.Join("|", Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i)).Select(s => s.path + ":" + s.isDirty + ":" + s.handle));
    private static void Click(Button button)
    {
        if (button == null || !button.enabledInHierarchy) throw new InvalidOperationException("Button is unavailable");
        Vector2 center = button.contentRect.center; Pointer(button, EventType.MouseDown, center, 0); Pointer(button, EventType.MouseUp, center, 0);
    }
    private static void ChangeControl<T>(VisualElement element, T next)
    {
        var field = (INotifyValueChanged<T>)element; T previous = field.value; field.SetValueWithoutNotify(next);
        using (var change = ChangeEvent<T>.GetPooled(previous, next)) { change.target = element; element.SendEvent(change); }
    }
    private static IEnumerable<VisualElement> Descendants(VisualElement root)
    {
        foreach (var child in root.hierarchy.Children())
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
