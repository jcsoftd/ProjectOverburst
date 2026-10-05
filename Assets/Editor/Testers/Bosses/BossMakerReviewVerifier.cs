using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Overburst.EditorTools.BossMaker;
using Object = UnityEngine.Object;

public static class BossMakerReviewVerifier
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Type TypeOf(string name) => typeof(BossMakerWindow).Assembly.GetType("Overburst.EditorTools.BossMaker." + name, true);
    static object New(string name, params object[] args) => Activator.CreateInstance(TypeOf(name), Flags, null, args, null);
    static object Get(object value, string name) => value.GetType().GetProperty(name, Flags).GetValue(value);
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, Flags).Invoke(value, args);
    sealed class Draft : IDisposable
    {
        public readonly object value;
        public Draft(object value) { this.value = value; }
        public EnemyBossAttackMaterial Material => (EnemyBossAttackMaterial)Get(value, "Material");
        public EnemyBossAttackMaterial Source => (EnemyBossAttackMaterial)Get(value, "Source");
        public EnemyAbilityDefinition Ability => (EnemyAbilityDefinition)Get(value, "Ability");
        public bool Dirty => (bool)Get(value, "Dirty");
        public void Edit(string label, Action edit) => Call(value, "Edit", label, edit);
        public object Capture() => Call(value, "Capture");
        public System.Collections.Generic.List<string> Validate() => (System.Collections.Generic.List<string>)Call(value, "Validate");
        public void Dispose() => Call(value, "Dispose");
    }
    static JArray Scenes() => new JArray(Enumerable.Range(0, SceneManager.sceneCount).Select(i => {
        var s = SceneManager.GetSceneAt(i);
        return new JObject { ["path"] = s.path, ["dirty"] = s.isDirty, ["roots"] = s.rootCount };
    }));
    public static string Main(string output)
    {
        Directory.CreateDirectory(output);
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))) return "DEFERRED_BUSY";
        var scenes = Scenes();
        var collection = AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(CrustaspikanMaterialBuilder.CollectionPath);
        var prefab = collection.actorDefinition.ActorPrefab;
        var composites = prefab.GetComponent<EnemyBossCompositePatternExecutor>()?.Patterns;
        var report = new JObject { ["originalsValid"] = collection.attacks.All(m => m.IsValid), ["compositesValid"] = composites?.IsValid,
            ["scenesBefore"] = scenes, ["editorPid"] = System.Diagnostics.Process.GetCurrentProcess().Id };
        int hiddenBefore = Resources.FindObjectsOfTypeAll<EnemyBossCompositePatternSet>().Count(s => s.hideFlags == HideFlags.HideAndDontSave);
        var session = New("BossMakerSession", collection, null);
        var drafts = ((System.Collections.IEnumerable)Get(session, "Drafts")).Cast<object>().Select(o => new Draft(o)).ToArray();
        EnemyBossCompositePatternSet copy = null;
        object preview = null;
        try
        {
            if (composites != null)
            {
                copy = Object.Instantiate(composites); copy.hideFlags = HideFlags.HideAndDontSave;
                var p = copy.spitPatterns.First(p => p.emissions.Any(e => e.count > 0));
                var emission = p.emissions.First(e => e.count > 0);
                var d = drafts.First(d => d.Source == p.material);
                int phase = emission.phase;
                d.Edit("Review timing on working copy", () => { d.Material.strikes[phase].impact = emission.normalizedTime + .005f; d.Material.strikes[phase].contactStart = d.Material.strikes[phase].impact; });
                p.material = d.Material;


                var errors = (System.Collections.Generic.List<string>)Call(session, "Validate", true);
                report["compositeTiming"] = new JObject { ["attack"] = d.Source.displayName, ["emission"] = emission.normalizedTime,
                    ["newImpact"] = d.Material.strikes[phase].impact, ["materialValid"] = d.Material.IsValid,
                    ["compositeValid"] = copy.IsValid, ["saveErrors"] = new JArray(errors),
                    ["unsafeSaveAccepted"] = d.Material.IsValid && !copy.IsValid && errors.Count == 0 };
            }
            if (composites != null)
            {
                var p = composites.spitPatterns.First(p => p.emissions.Any(e => e.count > 0));
                var emission = p.emissions.First(e => e.count > 0);
                var d = drafts.First(d => d.Source == p.material); int phase = emission.phase;
                d.Edit("Restore safe timing", () => { d.Material.strikes[phase].impact = d.Source.strikes[phase].impact; d.Material.strikes[phase].contactStart = d.Source.strikes[phase].contactStart; d.Material.tuning.damageMultiplier = 1.25f; });
                report["validCompositeEditAccepted"] = ((System.Collections.Generic.List<string>)Call(session, "Validate", true)).Count == 0;
            }
            var rock = drafts.First(d => d.Material.delivery == EnemyBossMaterialDelivery.Boulder);
            rock.Edit("Review displacement on working copy", () => { rock.Material.advanceDistance = 3f; rock.Material.advanceWindow = new Vector2(0f, .2f); });
            preview = New("BossMakerPreview", collection); Call(preview, "Load", rock.value, null); Call(preview, "Seek", rock.Material.strikes[0].impact);
            var stage = (CrustaspikanMaterialPreview)TypeOf("BossMakerPreview").GetField("stage", Flags).GetValue(preview);
            var left = stage.AnimationRoot.GetComponentsInChildren<Transform>(true).First(t => t.name == collection.boulderLeftHandBone);
            var right = stage.AnimationRoot.GetComponentsInChildren<Transform>(true).First(t => t.name == collection.boulderRightHandBone);
            Vector3 expected = (left.position + right.position) * .5f + collection.boulderOffset;
            var trajectories = (System.Collections.Generic.IEnumerable<Vector3[]>)Call(preview, "Trajectories");
            Vector3 actual = trajectories.First()[0];
            Vector3 landing = trajectories.First().Last();
            report["projectilePreview"] = new JObject { ["launchErrorMeters"] = Vector3.Distance(expected, actual),
                ["landingCenterErrorMeters"] = Vector3.Distance(landing, (Vector3)Get(preview, "Target") + Vector3.up * collection.boulderVisualRadius),
                ["expectedLaunch"] = expected.ToString("F4"), ["actualLaunch"] = actual.ToString("F4") };
            var recovered = new Draft(New("BossMakerDraft", rock.Source, rock.Capture()));
            try { report["recovery"] = recovered.Dirty && recovered.Material.advanceDistance == 3f && recovered.Material.ability == recovered.Ability; }
            finally { recovered.Dispose(); }
            var transient = Object.Instantiate(collection.attacks.First(m => m.delivery == EnemyBossMaterialDelivery.Melee));
            var transientAbility = Object.Instantiate(transient.ability); transient.ability = transientAbility;
            var guard = new Draft(New("BossMakerDraft", transient, null));
            try {
                guard.Edit("Review dirty conflict", () => guard.Material.tuning.damageMultiplier = 1.4f);
                transient.displayName += " External";
                report["dirtyConflictDetected"] = guard.Validate().Any(e => e.Contains("원본이 다른 곳"));
                var clean = new Draft(New("BossMakerDraft", transient, null));
                try { transient.displayName += " Refresh"; report["cleanRefresh"] = (bool)Call(clean.value, "RefreshCleanSource") && !clean.Dirty && clean.Material.displayName == transient.displayName; }
                finally { clean.Dispose(); }
            }
            finally { guard.Dispose(); Object.DestroyImmediate(transient); Object.DestroyImmediate(transientAbility); }
            var windows = Resources.FindObjectsOfTypeAll<BossMakerWindow>();
            report["windows"] = new JArray(windows.Select(w => new JObject { ["dirty"] = w.hasUnsavedChanges, ["width"] = w.position.width, ["height"] = w.position.height }));
            report["encounters"] = new JArray(AssetDatabase.FindAssets("t:CrustaspikanEncounterSettings").Select(g => {
                var s = AssetDatabase.LoadAssetAtPath<CrustaspikanEncounterSettings>(AssetDatabase.GUIDToAssetPath(g));
                return new JObject { ["path"] = AssetDatabase.GetAssetPath(s), ["linkedToCollection"] = s.materials == collection,
                    ["rules"] = new JArray(s.materialRules.Select(r => new JObject { ["clip"] = r.clip, ["speed"] = r.speed, ["damage"] = r.damage, ["finalHitParry"] = r.finalHitParry })) };
            }));
        }
        finally { if (preview != null) Call(preview, "Dispose"); if (copy != null) Object.DestroyImmediate(copy); Call(session, "Dispose"); }
        report["scenesAfter"] = Scenes(); report["scenesPreserved"] = JToken.DeepEquals(scenes, report["scenesAfter"]);
        report["previewCopiesReturned"] = hiddenBefore == Resources.FindObjectsOfTypeAll<EnemyBossCompositePatternSet>().Count(s => s.hideFlags == HideFlags.HideAndDontSave);
        bool pass = (bool)report["previewCopiesReturned"] && (bool)report["originalsValid"] && (bool)report["compositesValid"]
            && (bool)report["validCompositeEditAccepted"] && !(bool)report["compositeTiming"]["unsafeSaveAccepted"] && ((JArray)report["compositeTiming"]["saveErrors"]).Count > 0
            && (float)report["projectilePreview"]["launchErrorMeters"] < .001f
            && (float)report["projectilePreview"]["landingCenterErrorMeters"] < .001f
            && (bool)report["recovery"] && (bool)report["dirtyConflictDetected"] && (bool)report["cleanRefresh"] && (bool)report["scenesPreserved"];
        report["status"] = pass ? "PASS" : "FAIL";
        File.WriteAllText(Path.Combine(output, "probe.json"), report.ToString());
        return report.ToString();
    }
}
