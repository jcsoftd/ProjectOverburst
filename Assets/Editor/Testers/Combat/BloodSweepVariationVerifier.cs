using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

// Scoped to blood: uses the existing Editor, an isolated save and temporary Play-only targets.
[InitializeOnLoad]
public static class BloodSweepVariationVerifier
{
    const string ActiveKey = "Overburst.BloodSweepVerification";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static BloodHitVfxService service;
    static GameObject fixture;
    static CombatHealth health;
    static BloodHitProfile profile;
    static readonly List<string> checks = new List<string>();
    static readonly HashSet<int> seen = new HashSet<int>();
    static readonly HashSet<int> emitted = new HashSet<int>();
    static double next;
    static int step, sequence, beforePlayed, previous = -1, duplicateBefore;
    static bool issued;
    static BloodSweepVariationVerifier()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += state => {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) { next = EditorApplication.timeSinceStartup + 3; step = 0; }
            if (state == PlayModeStateChange.EnteredEditMode)
            { Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", null); SessionState.SetBool(ActiveKey, false); }
        };
    }
    static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); checks.Add("PASS " + message); }
    static void Clear() => typeof(BloodHitVfxService).GetMethod("Clear", Flags).Invoke(service, null);
    static BloodHitCatalog Catalog => Resources.Load<BloodHitCatalog>(BloodHitCatalog.ResourcePath);
    public static string VerifyAssetsAndRollback()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play first");
        checks.Clear(); var catalog = Catalog;
        Assert(catalog != null && catalog.useSweepVariations && catalog.sweepVariations.Length == 3, "3 sweep variants enabled");
        Assert(catalog.sweepVariations.Select(v => v.graph).Distinct().Count() == 3, "3 distinct graph assets");
        foreach (var variation in catalog.sweepVariations)
        {
            var go = new GameObject("Blood asset check"); go.SetActive(false);
            try {
                var vfx = go.AddComponent<VisualEffect>(); vfx.visualEffectAsset = variation.graph;
                Assert(vfx.HasFloat("HitSize") && vfx.HasInt("LoopCount") && vfx.HasFloat("SpecularValue")
                    && vfx.HasVector4("BloodColorMain") && vfx.HasVector4("BloodColorSecondary")
                    && vfx.HasVector4("BloodSpecularColor"), variation.label + " parameter contract");
            } finally { UnityEngine.Object.DestroyImmediate(go); }
        }
        Assert(catalog.Resolve(CombatImpactShape.Downward) == catalog.burst && catalog.Resolve(CombatImpactShape.Thrust) == catalog.stab,
            "downward and thrust routes retained");
        string expected = JsonUtility.ToJson(catalog);
        var baseline = JsonUtility.FromJson<BloodSweepVariationBuilder.Baseline>(
            File.ReadAllText(Path.Combine(BloodSweepVariationBuilder.Output, "appearance-before.json")));
        BloodSweepVariationBuilder.Restore();
        Assert(catalog.useSweepVariations == baseline.enabled, "rollback restores previous variant switch");
        foreach (var state in baseline.profiles)
        {
            var p = AssetDatabase.LoadAssetAtPath<BloodHitProfile>(state.path);
            Assert(p.mainColor.Equals(state.main) && p.secondaryColor.Equals(state.secondary)
                && p.specularColor.Equals(state.highlight) && p.specular == state.specular && p.size == state.size,
                p.name + " exact palette rollback and preserved size");
        }
        BloodSweepVariationBuilder.Apply();
        Assert(JsonUtility.ToJson(catalog) == expected, "reapply restores variant references");
        foreach (var state in baseline.profiles)
            Assert(AssetDatabase.LoadAssetAtPath<BloodHitProfile>(state.path).size == state.size, state.path + " size unchanged");
        File.WriteAllLines(Path.Combine(BloodSweepVariationBuilder.Output, "asset-rollback-checks.txt"), checks);
        return string.Join("\n", checks);
    }
    public static string StartPlayVerification()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(ActiveKey, false))
            throw new InvalidOperationException("Editor must be idle");
        checks.Clear(); seen.Clear(); emitted.Clear(); fixture = null; sequence = 0; previous = -1; issued = false;
        SessionState.SetBool(ActiveKey, true);
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(BloodSweepVariationBuilder.Output, "isolated-save"));
        return "Blood isolated Play verification started";
    }
    static Vector3 Point => Camera.main != null ? Camera.main.ViewportToWorldPoint(new Vector3(.5f, .5f, 8f)) : new Vector3(0, 1, 0);
    static void Hit(CombatImpactShape shape = CombatImpactShape.Sweep, bool critical = false, int phase = 0)
    {
        var request = new CombatHitFeedbackRequest(fixture, sequence, null, critical, default, Point, false,
            phaseIndex: phase, target: health, impactShape: shape, impactDirection: Vector3.right);
        BloodHitVfxService.Request(request, Point, 1f);
    }
    static void Tick()
    {
        if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying
            || EditorApplication.isPlayingOrWillChangePlaymode && EditorApplication.isCompiling
            || EditorApplication.timeSinceStartup < next) return;
        try
        {
            if (step == 0)
            {
                fixture = new GameObject("Blood sweep verification target");
                health = fixture.AddComponent<CombatHealth>(); var target = fixture.AddComponent<BloodHitTarget>();
                profile = Resources.Load<BloodHitProfile>("Combat/Blood/SpiderBrood");
                var serialized = new SerializedObject(target); serialized.FindProperty("profile").objectReferenceValue = profile;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                service = UnityEngine.Object.FindFirstObjectByType<BloodHitVfxService>();
                Assert(service != null && Camera.main != null, "product scene initialized with camera and blood service");
                Assert(service.GetComponentsInChildren<VisualEffect>(true).Length == BloodHitVfxService.Capacity, "fixed 96 blood slots");
                Clear(); step = 1;
            }
            if (step == 1)
            {
                if (!issued)
                {
                    beforePlayed = service.PlayedCount; duplicateBefore = service.DuplicateCount; sequence++;
                    var rng = UnityEngine.Random.state; Hit(); Hit();
                    Assert(rng.Equals(UnityEngine.Random.state), "melee variant request preserves gameplay RNG");
                    issued = true; next = EditorApplication.timeSinceStartup + .18; return;
                }
                Assert(service.PlayedCount == beforePlayed + 1 && service.DuplicateCount == duplicateBefore + 1, "one emission per target/attack/phase");
                int current = service.LastSweepVariation;
                Assert(current >= 0 && current < 3 && current != previous, "consecutive target hits change shape " + sequence + ": " + current);
                previous = current; seen.Add(current); issued = false;
                if (service.GetComponentsInChildren<VisualEffect>(true).Any(v => v.gameObject.activeSelf
                    && v.visualEffectAsset == Catalog.sweepVariations[current].graph && v.aliveParticleCount > 0)) emitted.Add(current);
                if (sequence < 18) { next = EditorApplication.timeSinceStartup + .04; return; }
                Assert(seen.Count == 3, "all 3 shapes selected through real request queue");
                var graphs = service.GetComponentsInChildren<VisualEffect>(true).Where(v => v.gameObject.activeSelf).ToArray();
                Assert(emitted.Count == 3, "all 3 variant graphs emitted live GPU particles");
                foreach (var v in graphs)
                    Assert(((Color)v.GetVector4("BloodColorMain")).Equals(profile.mainColor.linear), "runtime linear monster palette");
                Clear(); beforePlayed = service.SweepVariationPlayedCount;
                BloodHitVfxService.RequestAt(profile, Point, Vector3.right, CombatImpactShape.Sweep, 1, 0);
                sequence++; Hit(CombatImpactShape.Downward); sequence++; Hit(CombatImpactShape.Thrust);
                step = 2; next = EditorApplication.timeSinceStartup + .2; return;
            }
            if (step == 2)
            {
                Assert(service.SweepVariationPlayedCount == beforePlayed, "RequestAt/downward/thrust bypass sweep variations");
                var active = service.GetComponentsInChildren<VisualEffect>(true).Where(v => v.gameObject.activeSelf).Select(v => v.visualEffectAsset).ToArray();
                Assert(active.Contains(Catalog.slash) && active.Contains(Catalog.burst) && active.Contains(Catalog.stab), "legacy graphs emitted for other routes");
                Clear(); beforePlayed = service.PlayedCount;
                var target = new SerializedObject(fixture.GetComponent<BloodHitTarget>());
                var bloodless = Resources.Load<BloodHitProfile>("Enemies/Themes/Blood/Bone");
                Assert(bloodless != null && bloodless.suppressBlood, "bloodless profile loaded");
                target.FindProperty("profile").objectReferenceValue = bloodless;
                target.ApplyModifiedPropertiesWithoutUndo(); sequence++; Hit();
                target.FindProperty("profile").objectReferenceValue = profile; target.ApplyModifiedPropertiesWithoutUndo();
                step = 3; next = EditorApplication.timeSinceStartup + .2; return;
            }
            if (step == 3)
            {
                Assert(service.PlayedCount == beforePlayed, "bloodless target suppressed");
                for (int i = 0; i < 80; i++) { sequence++; Hit(); }
                beforePlayed = service.PlayedCount; step = 4; next = EditorApplication.timeSinceStartup + .2; return;
            }
            if (step == 4)
            {
                Assert(service.PlayedCount == beforePlayed + BloodHitVfxService.PerFrameLimit, "16 per-frame budget retained");
                Assert(service.PeakQueuedCount == 64 && service.DroppedQueueCount >= 16 && service.DroppedFrameCount >= 48, "64 queue overflow and frame dropping retained");
                Assert(service.GetComponentsInChildren<VisualEffect>(true).Length == 96, "pool does not grow under burst load");
                Clear(); Assert(service.ActiveCount == 0 && service.LastSweepVariation == -1, "scene cleanup clears active slots and shape history");
                next = EditorApplication.timeSinceStartup + .2; step = 5; return;
            }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        if (error != null) checks.Add("FAIL " + error);
        File.WriteAllLines(Path.Combine(BloodSweepVariationBuilder.Output, "play-checks.txt"), checks);
        File.WriteAllText(Path.Combine(BloodSweepVariationBuilder.Output, "play-result.json"),
            "{\"success\":" + (error == null ? "true" : "false") + ",\"checks\":" + checks.Count + "}");
        if (fixture != null) UnityEngine.Object.Destroy(fixture);
        Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", null);
        EditorApplication.isPlaying = false;
        if (error != null) Debug.LogException(error); else Debug.Log("Blood sweep Play verification PASS");
    }
}
