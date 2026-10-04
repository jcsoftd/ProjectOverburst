using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.EditorTools.ComboMaker;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class ComboMakerLibraryVerifier
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string Output = Path.GetFullPath("../개인파일/코덱스산출/Tools/ComboMaker/20261005_CommonLibrary/data/verification.json");

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("유휴 EditMode에서 실행하세요.");
        var log = new List<string>();
        var scenes = Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
            .Select(UnityEngine.SceneManagement.SceneManager.GetSceneAt).Select(s => (s.handle, s.isDirty, s.rootCount)).ToArray();
        int previews = EditorSceneManager.previewSceneCount;
        var groups = ComboMakerWeaponLibrary.Collect();
        var assets = groups.SelectMany(g => g.Weapons).Cast<Object>()
            .Concat(groups.SelectMany(g => g.Weapons).Select(w => (Object)w.GetMeleeDefinition()))
            .Concat(groups.SelectMany(g => Enum.GetValues(typeof(ComboMakerAttackMode)).Cast<ComboMakerAttackMode>().Select(m => g.SharedAsset(m, out _))))
            .Append(Resources.Load<WeaponLevelCatalog>(WeaponLevelCatalog.ResourcePath)).Where(a => a != null).Distinct().ToArray();
        var originals = assets.ToDictionary(a => a, a => EditorJsonUtility.ToJson(a));
        var bytes = assets.ToDictionary(a => AssetDatabase.GetAssetPath(a), a => File.ReadAllBytes(AssetDatabase.GetAssetPath(a)));
        string failure = null;
        try
        {
            Require(groups.Length == 1 && groups[0].WeaponClass == WeaponClass.Greatsword, "현재 정식 목록은 대검 한 종류", log);
            Require(groups[0].Label == "대검 공통" && groups[0].Weapons.Length > 1, "여러 대검을 공통 항목 하나로 묶음", log);
            Require(!groups[0].BindingIssues().Any(), "현재 대검의 모든 공격 공통 연결 일치", log);
            foreach (ComboMakerAttackMode mode in Enum.GetValues(typeof(ComboMakerAttackMode)))
            {
                var shared = groups[0].SharedAsset(mode, out string issue);
                Require(shared != null && issue == null && groups[0].Weapons.All(w => ComboMakerAttackBinding.Asset(w.GetMeleeDefinition(), mode) == shared), mode + " 전체 대검의 저장 대상 동일", log);
            }
            CheckNativeUI(groups[0], log);
            CheckRegistrationAndSharedApply(groups[0].Representative, log);
        }
        catch (Exception e) { failure = e.ToString(); log.Add("FAIL: " + failure); }
        finally
        {
            foreach (var a in originals) Check(EditorJsonUtility.ToJson(a.Key) == a.Value, "정식 원본 JSON 보존: " + a.Key.name, log, ref failure);
            foreach (var a in bytes) Check(File.ReadAllBytes(a.Key).SequenceEqual(a.Value), "정식 원본 디스크 보존: " + a.Key, log, ref failure);
            Check(previews == EditorSceneManager.previewSceneCount, "검증 프리뷰 씬 반환", log, ref failure);
            foreach (var before in scenes)
            {
                var scene = Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount).Select(UnityEngine.SceneManagement.SceneManager.GetSceneAt).First(s => s.handle == before.handle);
                Check(scene.isDirty == before.isDirty && scene.rootCount == before.rootCount, "기존 씬 dirty/root 보존", log, ref failure);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            File.WriteAllText(Output, JsonConvert.SerializeObject(new { status = failure == null ? "PASS" : "FAIL", checks = log, failure, groups = groups.Select(g => new { g.Label, members = g.Weapons.Length }), play = "NOT_RUN: Editor 공통 콤보 목록 변경", player = "NOT_RUN: 런타임 변경 없음" }, Formatting.Indented));
        }
        if (failure != null) throw new InvalidOperationException(failure);
        return "PASS: " + log.Count + " 항목 / 대검 공통 " + groups[0].Weapons.Length + "종";
    }

    private static void CheckNativeUI(ComboMakerWeaponLibrary.Entry group, List<string> log)
    {
        var window = ScriptableObject.CreateInstance<ComboMakerWindow>();
        try
        {
            window.Show(); window.CreateGUI();
            var cards = window.rootVisualElement.Query<Button>().ToList().Where(b => b.ClassListContains("weapon-card")).ToArray();
            Require(cards.Length == 1 && cards[0].text == "대검 공통", "네이티브 목록에 대검 공통 버튼 하나", log);
            Require(cards[0].name == "weapon-class-9", "개별 자산 대신 무기 클래스 선택", log);
            foreach (ComboMakerAttackMode mode in Enum.GetValues(typeof(ComboMakerAttackMode)))
                Require(window.rootVisualElement.Q<Button>("attack-mode-" + mode)?.enabledSelf == true, mode + " 공통 공격 선택 가능", log);
            typeof(ComboMakerWindow).GetMethod("SelectAttackMode", Private).Invoke(window, new object[] { ComboMakerAttackMode.Heavy });
            typeof(ComboMakerWindow).GetField("tab", Private).SetValue(window, 1);
            typeof(ComboMakerWindow).GetMethod("BuildEditor", Private).Invoke(window, null);
            var radius = window.rootVisualElement.Q<FloatField>("heavy-field-elementVfx.fireChainRadius");
            Require(radius != null, "공통 강공의 실제 편집 필드", log);
            radius.value += .1f;
            var session = (ComboMakerSession)typeof(ComboMakerWindow).GetField("session", Private).GetValue(window);
            Require(session.Dirty, "네이티브 입력으로 미적용 작업 사본 생성", log);
            string draft = session.Capture(); var target = session.Asset;
            typeof(ComboMakerWindow).GetField("tab", Private).SetValue(window, 3);
            typeof(ComboMakerWindow).GetMethod("BuildEditor", Private).Invoke(window, null);
            var visual = window.rootVisualElement.Q<DropdownField>("preview-weapon-visual");
            Require(visual != null && visual.choices.Count == group.Weapons.Length, "개별 외형 선택은 프리뷰 탭에만 배치", log);
            int next = (visual.index + 1) % visual.choices.Count;
            var expected = group.Weapons[next]; visual.value = visual.choices[next];
            Require((WeaponItemData)typeof(ComboMakerWindow).GetField("selectedWeapon", Private).GetValue(window) == expected, "외형 선택의 실제 값 변경", log);
            Require(ReferenceEquals(session, typeof(ComboMakerWindow).GetField("session", Private).GetValue(window)) && session.Capture() == draft && session.Dirty, "외형 교체 후 미적용 사본 그대로 유지", log);
            Require(session.Asset == target && target == group.SharedAsset(ComboMakerAttackMode.Heavy, out _), "외형 교체 후 전체 대검 공통 적용 대상 유지", log);
        }
        finally { window.DiscardChanges(); window.Close(); }
    }

    private static void CheckRegistrationAndSharedApply(WeaponItemData source, List<string> log)
    {
        string path = "Assets/Editor/Testers/Weapons/ComboMakerLibraryFixture_" + Guid.NewGuid().ToString("N") + ".asset";
        var allocated = new List<Object>();
        T Copy<T>(T value) where T : Object { var clone = Object.Instantiate(value); clone.hideFlags = HideFlags.HideAndDontSave; allocated.Add(clone); return clone; }
        var catalog = ScriptableObject.CreateInstance<WeaponLevelCatalog>(); allocated.Add(catalog);
        var combo = Object.Instantiate(source.GetMeleeComboDefinition());
        var profiles = (Array)typeof(MeleeAttackVfxSlopeBakeUtility).GetField("Profiles", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        object originalProfile = profiles.GetValue(0);
        try
        {
            AssetDatabase.CreateAsset(combo, path);
            // Register only the temporary source for the synchronous Apply check.
            // The production bake/save path still runs, and finally restores the registry.
            profiles.SetValue(Activator.CreateInstance(originalProfile.GetType(), new object[]
                { AssetDatabase.GetAssetPath(source), path, "공통 콤보 저장 검사", combo.StepCount, 0 }), 0);
            var definition = Copy(source.GetMeleeDefinition()); definition.comboDefinition = combo;
            var a = Copy(source); a.combatDefinition = definition;
            var b = Copy(source); b.combatDefinition = definition;
            catalog.entries = new[] { new WeaponLevelCatalog.Entry { weapon = a }, new WeaponLevelCatalog.Entry { weapon = b }, new WeaponLevelCatalog.Entry { weapon = a } };
            var groups = ComboMakerWeaponLibrary.Collect(catalog);
            Require(groups.Length == 1 && groups[0].Weapons.Length == 2, "중복 등록과 여러 외형은 공통 종류 하나", log);
            var future = Copy(source); future.weaponClass = WeaponClass.Sword; future.combatDefinition = Copy(definition);
            Require(ComboMakerWeaponLibrary.Collect(catalog).Length == 1, "미등록 무기 종류는 목록에 노출하지 않음", log);
            catalog.entries = catalog.entries.Append(new WeaponLevelCatalog.Entry { weapon = future }).ToArray();
            Require(ComboMakerWeaponLibrary.Collect(catalog).Length == 2 && ComboMakerWeaponLibrary.Collect(catalog).Any(g => g.Label == "한손검 공통"), "새 종류를 카탈로그에 등록하면 자동으로 공통 항목 추가", log);
            using (var session = new ComboMakerSession())
            {
                session.LoadWeapon(a, ComboMakerAttackMode.Light);
                float edited = session.Working.steps[0].animationSpeedMultiplier + .01f;
                session.Working.steps[0].animationSpeedMultiplier = edited; session.Apply();
                Require(Mathf.Approximately(b.GetMeleeComboDefinition().steps[0].animationSpeedMultiplier, edited), "공통 사본 실제 적용을 다른 대검도 같은 자산으로 읽음", log);
                Require(Mathf.Approximately(AssetDatabase.LoadAssetAtPath<MeleeComboDefinition>(path).steps[0].animationSpeedMultiplier, edited), "공통 콤보 저장 값 확인", log);
            }
            var split = Copy(definition); split.comboDefinition = source.GetMeleeComboDefinition(); b.combatDefinition = split;
            var conflict = ComboMakerWeaponLibrary.Collect(catalog).First(g => g.WeaponClass == WeaponClass.Greatsword);
            Require(conflict.SharedAsset(ComboMakerAttackMode.Light, out string issue) == null && !string.IsNullOrEmpty(issue), "같은 종류의 다른 콤보 연결을 공통 편집으로 숨기지 않음", log);
            split.comboDefinition = combo; split.heavyAttackDefinition = null;
            Require(conflict.SharedAsset(ComboMakerAttackMode.Heavy, out issue) == null && !string.IsNullOrEmpty(issue), "일부 대검만 강공 연결이 없으면 불일치 감지", log);
            definition.heavyAttackDefinition = null;
            Require(conflict.SharedAsset(ComboMakerAttackMode.Heavy, out issue) == null && issue == null, "종류 전체에 없는 공격은 미연결 슬롯으로 처리", log);
        }
        finally
        {
            profiles.SetValue(originalProfile, 0);
            Undo.ClearUndo(combo); AssetDatabase.DeleteAsset(path);
            foreach (var value in allocated) if (value != null) Object.DestroyImmediate(value);
            if (combo != null) Object.DestroyImmediate(combo);
        }
        Require(profiles.GetValue(0).Equals(originalProfile), "임시 베이크 등록 반환", log);
    }

    private static void Require(bool condition, string text, List<string> log)
    { if (!condition) throw new InvalidOperationException(text); log.Add("PASS: " + text); }
    private static void Check(bool condition, string text, List<string> log, ref string failure)
    { log.Add((condition ? "PASS: " : "FAIL: ") + text); if (!condition) failure = (failure ?? "") + "\n" + text; }
}
