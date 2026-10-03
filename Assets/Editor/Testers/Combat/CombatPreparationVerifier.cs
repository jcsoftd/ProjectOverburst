#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MoreMountains.Feedbacks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Explicit calls only, no editor update callback, Play entry, real saves, or scene edits.
public static class CombatPreparationVerifier
{
    public static object VerifyAssets()
    {
        var errors = new List<string>();
        var feel = Resources.Load<CombatImpactFeel>("Feel/PF_CombatImpactFeel");
        if (feel == null) return new { status = "FAIL", reason = "Feel prefab is missing." };
        var serialized = new SerializedObject(feel);
        var slots = serialized.FindProperty("slots");
        var surfaces = new int[4];
        var clips = new HashSet<AudioClip>();
        for (int i = 0; i < slots.arraySize; i++)
        {
            var slot = slots.GetArrayElementAtIndex(i);
            int surface = slot.FindPropertyRelative("surface").enumValueIndex;
            surfaces[surface]++;
            var player = slot.FindPropertyRelative("player").objectReferenceValue as MMF_Player;
            var particles = slot.FindPropertyRelative("particles").objectReferenceValue as ParticleSystem;
            if (player == null || particles == null) { errors.Add("Slot binding " + i); continue; }
            if (Convert.ToInt32(player.InitializationMode) != 0 || player.AutoInitialization || player.AutoPlayOnStart || player.AutoPlayOnEnable)
                errors.Add("Unexpected automatic playback/initialization " + i);
            foreach (var feedback in player.FeedbacksList)
                if (feedback is MMF_AudioSource audio && audio.RandomSfx != null)
                    foreach (var clip in audio.RandomSfx) if (clip != null) clips.Add(clip);
        }
        if (slots.arraySize != 52 || !surfaces.SequenceEqual(new[] { 16, 16, 16, 4 })) errors.Add("Feel capacity/surface contract");
        if (clips.Count != 4) errors.Add("Landing clip contract");
        var blood = Resources.Load<BloodHitCatalog>(BloodHitCatalog.ResourcePath);
        var materials = new HashSet<Material>();
        if (blood == null) errors.Add("Blood catalog missing");
        else foreach (var group in new[] { blood.sweepDecals, blood.thrustDecals, blood.downwardDecals, blood.lethalDecals })
            foreach (var prefab in group ?? Array.Empty<GameObject>())
            {
                var projector = prefab != null ? prefab.GetComponent<DecalProjector>() : null;
                if (projector == null || projector.material == null) errors.Add("Blood template missing");
                else materials.Add(projector.material);
            }
        int sprayCapacity = BloodHitVfxService.Capacity, decalCapacity = BloodGroundDecalService.Capacity;
        float hold = BloodGroundDecalService.HoldSeconds, fade = BloodGroundDecalService.FadeSeconds;
        if (sprayCapacity != 96 || decalCapacity != 96 || hold != 15 || fade != 5) errors.Add("Blood capacity/lifetime");
        return new { status = errors.Count == 0 ? "PASS" : "FAIL", errors, slots = slots.arraySize, surfaces, landingClips = clips.Count,
            decalUniqueMaterials = materials.Count, scope = "Authored contracts; does not establish visual or Player performance parity." };
    }

    static void Check(bool valid, string name, List<string> errors) { if (!valid) errors.Add(name); }
    public static IEnumerator VerifyOwnedPlay(Action<object> completed)
    {
        if (!Application.isPlaying || string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || UnityEditor.SessionState.GetString(Overburst.DebugTools.CombatPreparationDiagnostics.AccountKey, "") != IsolatedSavePlayGuard.ActiveDirectory)
        { completed(new { status = "DEFERRED", reason = "Owned isolated Play required." }); yield break; }
        if (Overburst.DebugTools.CombatStutterCapture.Running)
        { completed(new { status = "DEFERRED", reason = "Never run quality exercises during a measured capture." }); yield break; }
        var errors = new List<string>();
        GameObject prefab = null;
        BloodHitProfile profile = null;
        bool completedNormally = false;
        try
        {
            // A private transient prefab exercises valid idle, Random, active lease and fixed return.
            prefab = new GameObject("CombatPreparationVerifier owned pool probe") { hideFlags = HideFlags.HideAndDontSave };
            prefab.AddComponent<CombatPreparationPoolProbe>();
            var random = UnityEngine.Random.state;
            Check(TransientVfxPool.PrepareOne(prefab, 1), "Prepare created", errors);
            Check(JsonUtility.ToJson(random) == JsonUtility.ToJson(UnityEngine.Random.state), "Preparation RNG preserved", errors);
            Check(TransientVfxPool.GetValidIdleCount(prefab) == 1, "Idle prepared", errors);
            var pools = (Dictionary<GameObject, Queue<GameObject>>)typeof(TransientVfxPool).GetField("Pools", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Object.DestroyImmediate(pools[prefab].Peek());
            Check(TransientVfxPool.GetValidIdleCount(prefab) == 0, "Destroyed idle excluded", errors);
            Check(TransientVfxPool.PrepareOne(prefab, 1), "Destroyed idle replenished", errors);
            var lease = TransientVfxPool.Spawn(prefab, Vector3.zero, Quaternion.identity, .2f, 2, useUnscaledTime: true);
            Check(lease != null && lease.activeSelf && TransientVfxPool.GetStatistics(prefab).Active == 1, "Actual lease plays", errors);
            Check(!TransientVfxPool.TrimOne(prefab, 0) && lease != null && lease.activeSelf, "Trim preserves active lease", errors);
            yield return new WaitForSecondsRealtime(.3f);
            Check(TransientVfxPool.GetStatistics(prefab).Active == 0 && TransientVfxPool.GetValidIdleCount(prefab) == 1, "Fixed lease returns intact", errors);
            TransientVfxPool.TrimOne(prefab, 0);
            yield return null;

            // Modify an owned clone, never the real profile asset; cache hits must apply its latest colors.
            var decals = Object.FindFirstObjectByType<BloodGroundDecalService>();
            var catalog = Resources.Load<BloodHitCatalog>(BloodHitCatalog.ResourcePath);
            var sourceProfile = Resources.Load<BloodHitProfile>(BloodHitVfxService.PlayerProfilePath);
            Check(decals != null && catalog != null && sourceProfile != null, "Blood quality prerequisites", errors);
            if (decals != null && catalog != null && sourceProfile != null)
            {
                profile = Object.Instantiate(sourceProfile);
                var material = catalog.sweepDecals[0].GetComponent<DecalProjector>().material;
                var tint = typeof(BloodGroundDecalService).GetMethod("TintedMaterial", BindingFlags.Instance | BindingFlags.NonPublic);
                int shown = decals.ShownCount, requested = decals.RequestedCount;
                var first = (Material)tint.Invoke(decals, new object[] { material, profile });
                Check(decals.ShownCount == shown && decals.RequestedCount == requested, "Material cache creates no synthetic marks", errors);
                profile.mainColor = new Color(.2f, .6f, .4f, 1f); profile.specular = 1f;
                var second = (Material)tint.Invoke(decals, new object[] { material, profile });
                Check(first == second, "Material cache identity", errors);
                if (second.HasProperty("_MainColor")) Check(Vector4.Distance(second.GetColor("_MainColor"), profile.mainColor.linear) < .0001f, "Latest profile color reapplied", errors);
                if (second.HasProperty("_SpecularValue")) Check(Mathf.Approximately(second.GetFloat("_SpecularValue"), .4f), "Specular clamp unchanged", errors);
            }

            // Let production contact leases expire before checking normal saturation and lethal priority.
            yield return new WaitForSecondsRealtime(.8f);
            var feel = Object.FindFirstObjectByType<CombatImpactFeel>();
            if (feel == null) { errors.Add("Feel instance missing"); yield break; }
            int dropped = feel.DroppedCount, preempted = feel.PreemptedCount;
            for (int i = 0; i < 16; i++) Check(CombatImpactFeel.Play(CombatImpactSurface.Flesh, CombatImpactShape.Sweep, Vector3.zero, Vector3.forward), "Normal slot " + i, errors);
            Check(!CombatImpactFeel.Play(CombatImpactSurface.Flesh, CombatImpactShape.Sweep, Vector3.zero, Vector3.forward), "Normal overflow drops", errors);
            Check(CombatImpactFeel.Play(CombatImpactSurface.Flesh, CombatImpactShape.Downward, Vector3.zero, Vector3.forward, lethal: true), "Lethal preempts", errors);
            Check(feel.DroppedCount == dropped + 1 && feel.PreemptedCount == preempted + 1, "Priority counters unchanged", errors);
            yield return new WaitForSecondsRealtime(.8f);
            Check(CombatImpactFeel.Play(CombatImpactSurface.Ground, CombatImpactShape.Sweep, Vector3.zero, Vector3.forward), "Landing plays", errors);
            Check(!CombatImpactFeel.Play(CombatImpactSurface.Ground, CombatImpactShape.Sweep, Vector3.zero, Vector3.forward), "Landing throttle unchanged", errors);
            yield return new WaitForSecondsRealtime(.8f);

            var slots = (CombatImpactFeel.Slot[])typeof(CombatImpactFeel).GetField("slots", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(feel);
            feel.Configure(slots); // Same owned runtime bindings; make preparation incomplete for the race exercise.
            var owner = CombatImpactFeel.PrepareForGameplay(2f);
            var join = CombatImpactFeel.PrepareForGameplay(2f);
            try
            {
                Check(owner.MoveNext(), "Preparation yields", errors);
                Check(join.MoveNext(), "Duplicate joins", errors);
                Check(CombatImpactFeel.Play(CombatImpactSurface.Shell, CombatImpactShape.Thrust, Vector3.zero, Vector3.forward), "Live hit interrupts preparation", errors);
                var live = slots.First(s => s.surface == CombatImpactSurface.Shell && s.availableAt > Time.unscaledTime);
                float liveUntil = live.availableAt;
                Check(!owner.MoveNext() && !join.MoveNext(), "Late preparation cannot clear live hit", errors);
                Check(live.availableAt == liveUntil, "Live hit lease preserved after late continuation", errors);
                Check(CombatImpactFeel.PreparationStatus == "InterruptedByPlay", "Race status", errors);
            }
            finally { (owner as IDisposable)?.Dispose(); (join as IDisposable)?.Dispose(); }
            var cancelled = CombatImpactFeel.PrepareForGameplay(2f, () => true);
            try { Check(!cancelled.MoveNext(), "Cancelled preparation exits", errors); }
            finally { (cancelled as IDisposable)?.Dispose(); }
            completedNormally = true;
        }
        finally
        {
            if (profile != null) Object.Destroy(profile);
            if (prefab != null)
            {
                var host = typeof(TransientVfxPool).GetField("host", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                if (host != null)
                {
                    var leases = (IList)host.GetType().GetField("activeLeases", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
                    var release = host.GetType().GetMethod("ReturnLeaseAt", BindingFlags.Instance | BindingFlags.NonPublic);
                    for (int i = leases.Count - 1; i >= 0; i--)
                        if (ReferenceEquals(leases[i].GetType().GetField("Prefab").GetValue(leases[i]), prefab)) release.Invoke(host, new object[] { i });
                }
                var pools = (Dictionary<GameObject, Queue<GameObject>>)typeof(TransientVfxPool).GetField("Pools", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                if (pools.TryGetValue(prefab, out var queue)) { while (queue.Count > 0) { var item = queue.Dequeue(); if (item != null) Object.Destroy(item); } pools.Remove(prefab); }
                var diagnostics = (IDictionary)typeof(TransientVfxPool).GetField("Diagnostics", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                diagnostics.Remove(prefab); Object.Destroy(prefab);
            }
            if (!completedNormally && errors.Count == 0) errors.Add("Quality exercise interrupted; no pass claim.");
            completed(new { status = errors.Count == 0 ? "PASS" : "FAIL", errors,
                scope = "Owned synthetic lifecycle/quality exercises after measurement; direct visual feel and Player remain separate." });
        }
    }
}

#endif
