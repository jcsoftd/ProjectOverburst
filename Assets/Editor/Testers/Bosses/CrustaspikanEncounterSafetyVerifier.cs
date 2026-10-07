using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Overburst.EditorTools.BossMaker;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

// Working copies and isolated runtime instances only; never saves the approved boss assets.
public static partial class CrustaspikanEncounterSafetyVerifier
{
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static JArray Scenes() => new JArray(Enumerable.Range(0, SceneManager.sceneCount).Select(i => {
        var s = SceneManager.GetSceneAt(i);
        return new JObject { ["path"] = s.path, ["dirty"] = s.isDirty, ["roots"] = s.rootCount };
    }));
    static void Require(bool pass, string error) { if (!pass) throw new InvalidOperationException(error); }
    static void Idle()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating
            && !EditorUtility.scriptCompilationFailed, "A ready idle Editor is required.");
        Require(!IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))
            && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", "")), "Account return must complete first.");
    }
    public static string VerifyNative(string directory)
    {
        Idle(); directory = IsolatedSavePlayGuard.ValidateDirectory(directory);
        Require(!Directory.Exists(directory), "Fresh private evidence directory required."); Directory.CreateDirectory(directory);
        var rows = new JArray(); var owned = new List<Object>(); BossMakerWindow window = null;
        var beforeScenes = Scenes(); int previews = BossMakerWindow.OpenPreviews;
        var collection = AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(CrustaspikanMaterialBuilder.CollectionPath);
        var settings = Resources.Load<CrustaspikanEncounterSettings>(CrustaspikanEncounterHost.SettingsPath);
        var originals = collection.attacks.SelectMany(m => new Object[] { m, m.ability })
            .Concat(new Object[] { collection, settings, settings.composites, settings.ParryRecoilProfile }).Where(o => o != null).Distinct().ToArray();
        var snapshots = originals.Select(o => EditorJsonUtility.ToJson(o)).ToArray();
        string failure = null;
        void Check(bool value, string label, string detail = "")
        {
            rows.Add(new JObject { ["case"] = label, ["pass"] = value, ["detail"] = detail }); Require(value, label + ": " + detail);
        }
        T Copy<T>(T source) where T : Object { var copy = Object.Instantiate(source); copy.hideFlags = HideFlags.HideAndDontSave; owned.Add(copy); return copy; }
        try
        {
            Check(settings.Validate(out var reason), "approved-settings-valid", reason);
            foreach (var source in collection.attacks)
            {
                var rule = settings.Rule(source.runtimeClip.name); var legacy = Copy(source);
                legacy.tuning = new EnemyBossAttackTuning { animationSpeedMultiplier = rule?.speed ?? 1f, damageMultiplier = rule?.damage ?? 1f,
                    parries = source.strikes.Select((s, i) => new EnemyBossStrikeParryTuning {
                        canParry = source.delivery == EnemyBossMaterialDelivery.Melee && ((rule?.finalHitParry ?? true) && i == source.strikes.Length - 1 || (rule?.firstHitParry ?? false) && i == 0)
                    }).ToArray() };
                settings.ParryRecoilProfile?.ApplyWindows(legacy);
                bool valid = CrustaspikanEncounterMaterialResolver.TryResolveTuning(settings, source, settings.ParryRecoilProfile, out var resolved, out reason);
                Check(valid && JsonUtility.ToJson(legacy.tuning) == JsonUtility.ToJson(resolved), "approved-tuning-preserved/" + source.runtimeClip.name, reason);
            }
            var badCollection = Copy(collection); var badSettings = Copy(settings);
            badCollection.attacks = collection.attacks.Concat(new[] { collection.attacks[0] }).ToArray(); badSettings.materials = badCollection;
            Check(!badSettings.Validate(out reason) && reason.Contains("중복"), "duplicate-attack-preflight-rejected", reason);
            badCollection.attacks = collection.attacks.Where(m => m.runtimeClip.name != "ThrowRock").ToArray();
            Check(!badSettings.Validate(out reason), "missing-implicit-throw-link-rejected", reason);
            var invalidRules = Copy(settings); invalidRules.materialRules = null;
            Check(!invalidRules.Validate(out reason), "null-rule-array-rejected", reason);
            invalidRules.materialRules = settings.materialRules.Concat(new[] { settings.materialRules[0] }).ToArray();
            Check(!invalidRules.Validate(out reason), "duplicate-rule-rejected", reason);
            invalidRules = Copy(settings); invalidRules.materialRules[0].speed = float.NaN;
            Check(!invalidRules.Validate(out reason), "non-finite-final-speed-rejected", reason);
            using (var session = new BossMakerSession(collection))
            {
                var draft = session.Drafts.Single(d => d.Source.runtimeClip.name == "LeftHandSmashAttack");
                draft.Edit("Owned unsafe timing probe", () => draft.Material.strikes[0].contactStart = draft.Material.strikes[0].impact = .30f);
                var errors = session.Validate(true);
                Check(draft.Material.IsValid && errors.Any(e => e.Contains("반동 프로필")), "native-valid-draft-composed-window-rejected", string.Join("\n", errors));
                bool blocked = false;
                try { session.Apply(); } catch (InvalidOperationException error) { blocked = error.Message.Contains("반동 프로필"); }
                Check(blocked, "unsafe-apply-blocked-before-copy-or-save");
            }
            using (var session = new BossMakerSession(collection))
            {
                var draft = session.Drafts.Single(d => d.Source.runtimeClip.name == "LeftHandSmashAttack");
                draft.Edit("Owned safe timing probe", () => { draft.Material.strikes[0].impact += .01f; draft.Material.strikes[0].contactStart = draft.Material.strikes[0].impact; });
                var errors = session.Validate(true);
                Check(errors.Count == 0, "safe-timing-edit-accepted", string.Join("\n", errors));
            }
            window = ScriptableObject.CreateInstance<BossMakerWindow>(); window.CreateGUI(); window.LoadCollection(collection);
            foreach (var spit in collection.attacks.Where(m => m.delivery == EnemyBossMaterialDelivery.Spit))
            {
                window.SelectAttack(Array.IndexOf(collection.attacks, spit)); SetTab(window, 0);
                var root = window.rootVisualElement;
                Check(!root.Q<Vector3Field>("boss-origin").enabledSelf && !root.Q<FloatField>("boss-yaw").enabledSelf,
                    "composite-unused-origin-and-yaw-disabled/" + spit.runtimeClip.name);
                Check(root.Query<Label>().ToList().Any(l => l.text?.Contains("복합 공격 설정") == true), "composite-direction-guidance/" + spit.runtimeClip.name);
            }
            var melee = collection.attacks.First(m => m.delivery == EnemyBossMaterialDelivery.Melee
                && (m.strikes[0].shape == GroundIndicatorShape.Sector || m.strikes[0].shape == GroundIndicatorShape.Rectangle));
            window.SelectAttack(Array.IndexOf(collection.attacks, melee)); SetTab(window, 0);
            Check(window.rootVisualElement.Q<Vector3Field>("boss-origin").enabledSelf && window.rootVisualElement.Q<FloatField>("boss-yaw").enabledSelf,
                "used-melee-origin-and-yaw-remain-editable");
            SetTab(window, 2);
            Check(window.rootVisualElement.Q<FloatField>("boss-weight").label == "일반 AI 선택 가중치"
                && window.rootVisualElement.Query<Label>().ToList().Any(l => l.text?.Contains("전투 설정의 패턴 가중치") == true), "generic-weight-scope-explained");
            Check(!window.Session.Dirty && window.rootVisualElement.Query<IMGUIContainer>().ToList().Count == 0, "toolkit-inspection-does-not-edit-assets");
        }
        catch (Exception error) { failure = error.ToString(); }
        finally
        {
            if (window != null) Object.DestroyImmediate(window);
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        }
        bool unchanged = originals.Select((o, i) => EditorJsonUtility.ToJson(o) == snapshots[i]).All(v => v);
        bool returned = JToken.DeepEquals(beforeScenes, Scenes()) && BossMakerWindow.OpenPreviews == previews;
        rows.Add(new JObject { ["case"] = "approved-source-objects-unchanged", ["pass"] = unchanged });
        rows.Add(new JObject { ["case"] = "scenes-and-previews-returned", ["pass"] = returned });
        var report = new JObject { ["status"] = failure == null && unchanged && returned ? "PASS" : "FAIL", ["failure"] = failure,
            ["scope"] = "Native assets, working copies, Apply rejection and UI Toolkit field semantics; no approved asset save and no Play.",
            ["cases"] = rows, ["scenesBefore"] = beforeScenes, ["scenesAfter"] = Scenes(), ["editorPid"] = System.Diagnostics.Process.GetCurrentProcess().Id };
        File.WriteAllText(Path.Combine(directory, "result.json"), report.ToString()); return report.ToString();
    }
    static void SetTab(BossMakerWindow window, int tab)
    {
        typeof(BossMakerWindow).GetField("tab", Private).SetValue(window, tab);
        typeof(BossMakerWindow).GetMethod("BuildFields", Private).Invoke(window, null);
    }
}
