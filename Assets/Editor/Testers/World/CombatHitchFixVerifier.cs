using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.Caves;
using UnityEngine;

// Runs inside the existing isolated map-entry verifier; never starts or stops another Play session.
public static class CombatHitchFixVerifier
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static object Field(object value, string name) => value.GetType().GetField(name, Private).GetValue(value);
    public static IEnumerator CheckTimeStepTransition(string output, Action<bool, string> check)
    {
        check(!OverburstTimeEffectArbiter.IsActive && !OverburstTimeEffectArbiter.IsPaused,
            "Owned time-step regression starts before combat without another time effect");
        float scale = Time.timeScale, step = Time.fixedDeltaTime;
        var owner = new GameObject("OwnedTimeStepRegression");
        var samples = new List<object>();
        const float duration = 2f; // Cold entry may deliver fewer than seven updates during a 0.6s effect.
        try
        {
            check(OverburstTimeEffectArbiter.Request(owner, OverburstTimeEffectKind.ParryHitStop, .01f, duration)
                && Mathf.Approximately(Time.timeScale, .01f) && Time.fixedDeltaTime >= step * .5f - .000001f,
                "Hitstop applies its full time scale immediately without a 100x physics-step collapse");
            float firstStep = Time.fixedDeltaTime;
            for (int i = 0; i < 8; i++)
                OverburstTimeEffectArbiter.Request(owner, OverburstTimeEffectKind.ParryHitStop, .01f, duration);
            check(Mathf.Approximately(Time.fixedDeltaTime, firstStep), "Repeated same-frame requests cannot keep halving the physics step");
            bool bounded = true, converged = false;
            float previous = Time.fixedDeltaTime, end = Time.unscaledTime + 4f;
            while (OverburstTimeEffectArbiter.IsActive && Time.unscaledTime < end)
            {
                yield return null;
                bounded &= Time.fixedDeltaTime >= previous * .5f - .000001f;
                // Unity rounds stored time steps; allow 0.1 microseconds at the 0.2ms target.
                converged |= Mathf.Abs(Time.fixedDeltaTime - step * .01f / scale) < .0000001f;
                samples.Add(new { frame = Time.frameCount, unscaledSeconds = Time.realtimeSinceStartupAsDouble, scale = Time.timeScale, step = Time.fixedDeltaTime });
                previous = Time.fixedDeltaTime;
            }
            check(bounded && converged, "Physics step converges to the existing slow-motion rate with bounded reductions");
            check(!OverburstTimeEffectArbiter.IsActive && Mathf.Approximately(Time.timeScale, scale)
                && Mathf.Approximately(Time.fixedDeltaTime, step), "Effect expiry restores the original time scale and physics step");
            OverburstTimeEffectArbiter.Request(owner, OverburstTimeEffectKind.ParrySlow, .5f, 1f);
            OverburstTimeEffectArbiter.Request(owner, OverburstTimeEffectKind.ParryHitStop, .01f, .05f);
            check(OverburstTimeEffectArbiter.ActiveKind == OverburstTimeEffectKind.ParryHitStop
                && Mathf.Approximately(Time.timeScale, .01f), "Overlapping effects retain hitstop priority");
            OverburstTimeEffectArbiter.SetPaused(true);
            check(Time.timeScale == 0f && !OverburstTimeEffectArbiter.Request(owner, OverburstTimeEffectKind.HitStop, .2f, 1f),
                "Menu pause still overrides and rejects time effects");
            OverburstTimeEffectArbiter.ClearOwner(owner);
            check(Time.timeScale == 0f && Mathf.Approximately(Time.fixedDeltaTime, step),
                "Clearing a paused effect restores the physics step without unpausing");
            OverburstTimeEffectArbiter.SetPaused(false);
            check(Mathf.Approximately(Time.timeScale, scale) && Mathf.Approximately(Time.fixedDeltaTime, step),
                "Unpause after effect removal restores normal simulation");
        }
        finally
        {
            OverburstTimeEffectArbiter.ClearOwner(owner);
            if (OverburstTimeEffectArbiter.IsPaused) OverburstTimeEffectArbiter.SetPaused(false);
            UnityEngine.Object.Destroy(owner);
            File.WriteAllText(Path.Combine(output, "time-step-regression.json"), JsonConvert.SerializeObject(samples, Formatting.Indented));
        }
    }
    static int beforeEntryDischargeCount = -1;
    static int beforeEntryTingCount, beforeEntryPerfectLayerCount;
    public static void CaptureFirstHitBaseline(PlayerEquipment equipment, string output)
    {
        var energy = equipment.GetComponent<OverburstElementEnergy>();
        beforeEntryDischargeCount = energy ? energy.DischargeCount : 0;
        beforeEntryTingCount = ParryFeedbackService.LastAdditionalTingCount;
        beforeEntryPerfectLayerCount = ParryFeedbackService.LastPerfectLayerCount;
        File.WriteAllText(Path.Combine(output, "first-hit-before-entry.json"), JsonConvert.SerializeObject(new {
            amount = energy ? energy.Amount : 0f, discharges = beforeEntryDischargeCount,
            additionalTingCount = beforeEntryTingCount, perfectLayerCount = beforeEntryPerfectLayerCount,
            scope = "Before this map entry; discharge and last-parry diagnostics survive ordinary map transitions"
        }, Formatting.Indented));
    }
    public static void CheckFirstHitPreparation(PlayerEquipment equipment, string output, Action<bool, string> check)
    {
        var account = Overburst.Persistence.AccountGameplaySession.Current;
        var run = account.ReadRun();
        var session = new Overburst.Persistence.AccountRunSession(account);
        long revision = account.Revision, now = DateTime.UtcNow.Ticks;
        check(run != null && account.CurrentRunPhase == run.phase
            && account.CanAcquireFromRun(run.runId) && !account.CanAcquireFromRun(run.runId + "-stale"),
            "Live phase and run identity queries match the owned snapshot and reject a stale exit");
        session.ResolveOutcome(now, false); // JIT before measuring steady polling.
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        bool unchanged = true;
        for (int i = 0; i < 2048; i++)
            unchanged &= account.CurrentRunPhase == run.phase && account.CanAcquireFromRun(run.runId)
                && session.ResolveOutcome(now, false) == Overburst.Persistence.RunOutcome.None;
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        check(unchanged && allocated == 0 && account.Revision == revision,
            "2048 ordinary run/exit polls allocate zero bytes and never change account state");
        File.WriteAllText(Path.Combine(output, "run-status-reads.json"), JsonConvert.SerializeObject(new {
            polls = 2048, allocated, unchanged, revisionBefore = revision, revisionAfter = account.Revision
        }, Formatting.Indented));
        var energy = equipment.GetComponent<OverburstElementEnergy>();
        var focus = equipment.CurrentWeaponRoot.GetComponentInChildren<DashHeavyFocusPresentation>(true);
        File.WriteAllText(Path.Combine(output, "first-hit-lifecycle.json"), JsonConvert.SerializeObject(new {
            energyPresent = energy != null, amount = energy != null ? energy.Amount : -1f,
            discharges = energy != null ? energy.DischargeCount : -1, expectedDischarges = beforeEntryDischargeCount,
            additionalTingCount = ParryFeedbackService.LastAdditionalTingCount, expectedAdditionalTingCount = beforeEntryTingCount,
            perfectLayerCount = ParryFeedbackService.LastPerfectLayerCount, expectedPerfectLayerCount = beforeEntryPerfectLayerCount,
            focusPresent = focus != null, focusEnabled = focus != null && focus.enabled,
            weapon = equipment.CurrentWeaponData.name, root = equipment.CurrentWeaponRoot.name,
            allFocus = UnityEngine.Object.FindObjectsByType<DashHeavyFocusPresentation>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(f => new { f.name, f.enabled, parent = f.transform.parent.name }).ToArray(),
            heavy = equipment.CurrentWeaponData.GetMeleeDefinition()?.heavyAttackDefinition?.name
        }, Formatting.Indented));
        check(energy != null && energy.Element == equipment.ActiveElement && energy.WeaponInstanceId == equipment.CurrentWeaponItem.runtimeInstanceId,
            "Loading binds current weapon energy before the first confirmed hit");
        check(energy != null && energy.Amount == 0f && beforeEntryDischargeCount >= 0 && energy.DischargeCount == beforeEntryDischargeCount,
            "Preparation credits no energy and preserves the pre-entry discharge count");
        check(focus != null && !focus.enabled, "Charged-heavy focus is prepared and remains inactive before combat");
        var hud = UnityEngine.Object.FindObjectsByType<HudResourceBarFx>(FindObjectsSortMode.None)
            .Select(h => Field(h, "weaponFx")).FirstOrDefault(f => f != null);
        bool hudPrepared = hud != null && (WeaponElement)Field(hud, "element") == equipment.ActiveElement && Field(hud, "playback") != null;
        bool hudHidden = hud != null && !((Camera)Field(hud, "camera")).enabled && !((UnityEngine.UI.RawImage)Field(hud, "view")).enabled;
        check(hudPrepared && hudHidden, "Current-element HUD is prepared while zero-energy rendering stays disabled");
        var fonts = ((UnityEngine.Object[])Field(PersistentSceneFlow.Instance, "commonPresentation"))
            .OfType<TMPro.TMP_FontAsset>().Select(f => f.name).ToArray();
        check(new[] { "NotoSerifKR_Damage SDF", "NotoSerifKR_Stun SDF", "NotoSerifKR_Parry SDF" }.All(fonts.Contains),
            "Loading retains the three existing combat fonts before the first hit/parry");
        var parry = UnityEngine.Object.FindFirstObjectByType<ParryFeedbackService>();
        var voices = parry ? (AudioSource[])Field(parry, "voices") : Array.Empty<AudioSource>();
        check(parry && voices.Length == 8 && voices.All(v => v && !v.isPlaying),
            "Loading prepares the existing eight parry voices silently");
        var parryPrefabs = new[] {
            Resources.Load<EnemyTelegraphVisualLibrary>("Enemies/Balance/EnemyTelegraphVisualLibrary")?.ParrySuccess,
            Resources.Load<GameObject>("Combat/VFX/PF_ParryZoomBlur_Normal"),
            Resources.Load<GameObject>("Combat/VFX/PF_ParryZoomBlur_Perfect") };
        check(parryPrefabs.All(p => p && TransientVfxPool.GetStatistics(p).Idle >= 1),
            "First-use parry flash and both zoom effects are idle in the existing pool");
        var random = JsonUtility.ToJson(UnityEngine.Random.state);
        check(!ParryFeedbackService.PrepareOne() && random == JsonUtility.ToJson(UnityEngine.Random.state),
            "Repeated parry preparation does not create more objects or consume gameplay RNG");
        check(ParryFeedbackService.LastAdditionalTingCount == beforeEntryTingCount
            && ParryFeedbackService.LastPerfectLayerCount == beforeEntryPerfectLayerCount,
            "Preparation preserves the pre-entry successful-parry feedback diagnostics");
        File.WriteAllText(Path.Combine(output, "first-hit-preparation.json"), JsonConvert.SerializeObject(new {
            energyPresent = energy != null, amount = energy != null ? energy.Amount : -1f,
            discharges = energy != null ? energy.DischargeCount : -1, expectedDischarges = beforeEntryDischargeCount,
            element = energy != null ? energy.Element.ToString() : "missing", focusPresent = focus != null,
            additionalTingCount = ParryFeedbackService.LastAdditionalTingCount, expectedAdditionalTingCount = beforeEntryTingCount,
            perfectLayerCount = ParryFeedbackService.LastPerfectLayerCount, expectedPerfectLayerCount = beforeEntryPerfectLayerCount,
            hudPrepared, hudHidden, fonts, scope = "Before production keyboard/mouse combat; preparation must not credit energy"
        }, Formatting.Indented));
    }
    public static void CheckRuntime(RunWorldGate gate, string output, Action<bool, string> check)
    {
        EnemySquadPursuitRuntimeService.GetRuntimeStats();
        var agents = (IDictionary)typeof(EnemySquadPursuitRuntimeService).GetField("Agents", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var actors = new List<EnemyActor>();
        int checkedActors = 0, grouped = 0;
        foreach (var field in gate.SpawnBudget.Fields)
        {
            actors.Clear(); field.CollectLivingActors(actors);
            var packs = new Dictionary<int, object>();
            var squadIds = new Dictionary<object, int>();
            foreach (var actor in actors)
            {
                var ai = actor.AI;
                if (!ai) continue;
                check(ai.SpawnPackId > 0, "Map actor retains a logical spawn pack ID"); checkedActors++;
                if (!ai.isActiveAndEnabled || !ai.UsesSquadPursuit || !ai.UsesMeleeSquadMovement) continue;
                check(agents.Contains(ai), "Eligible map actor registered in squad runtime");
                var squad = Field(agents[ai], "Squad");
                check(squad != null, "Every live map melee has an active squad without a population threshold");
                if ((int)Field(squad, "SpawnPackId") == 0) { grouped++; continue; } // Local survivors may form a new group.
                check((int)Field(squad, "SpawnPackId") == ai.SpawnPackId, "Intact spawn pack keeps its binding");
                if (packs.TryGetValue(ai.SpawnPackId, out var prior)) check(ReferenceEquals(prior, squad), "One spawn pack stays one squad");
                if (squadIds.TryGetValue(squad, out var id)) check(id == ai.SpawnPackId, "Different spawn packs never merge");
                packs[ai.SpawnPackId] = squad; squadIds[squad] = ai.SpawnPackId; grouped++;
            }
        }
        check(checkedActors > 0 && grouped > 0, "Live map actors exercise pack membership");
        string path = Path.Combine(output, "boundary-differential.json");
        var world = UnityEngine.Object.FindFirstObjectByType<CaveWorld>();
        if (world && !File.Exists(path)) CompareCorrections(world, path);
    }

    public static void CalibrateAudioPools(string output, Action<bool, string> check)
    {
        var clip = AudioClip.Create("OwnedSilentPoolCalibration", 8000 * 30, 1, 8000, false);
        var stats = new List<object>();
        var combat = UnityEngine.Object.FindFirstObjectByType<CombatActionSfxService>();
        MeleeElementSfxService element = null;
        void Snapshot(string stage)
        {
            var voices = combat ? (AudioSource[])Field(combat, "voices") : Array.Empty<AudioSource>();
            stats.Add(new { stage, frame = Time.frameCount, dspTime = AudioSettings.dspTime,
                listenerPaused = AudioListener.pause, editorMuted = UnityEditor.EditorUtility.audioMasterMute,
                listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Select(l => new { l.enabled, active = l.gameObject.activeInHierarchy }).ToArray(),
                voices = voices.Where(v => v).Select(v => new { v.isPlaying, v.isVirtual, v.enabled, active = v.gameObject.activeInHierarchy,
                    owned = v.clip == clip, v.priority, v.spatialBlend, v.timeSamples }).ToArray() });
        }
        try
        {
            Snapshot("BeforeCombatFill");
            for (int i = 0; i < 48; i++) CombatActionSfxService.PlayEnvironmentBreak(clip, Vector3.zero);
            combat = UnityEngine.Object.FindFirstObjectByType<CombatActionSfxService>();
            Snapshot("AfterCombatFill");
            bool extra = CombatActionSfxService.PlayEnvironmentBreak(clip, Vector3.zero);
            Snapshot("AfterCombatExtra");
            check(!extra, "Saturated combat audio rejects equal-priority cue");
            var play = typeof(CombatActionSfxService).GetMethod("Play", Private, null,
                new[] { typeof(AudioClip), typeof(Vector3), typeof(float), typeof(float), typeof(float), typeof(float), typeof(int), typeof(float) }, null);
            check((bool)play.Invoke(combat, new object[] { clip, Vector3.zero, 0f, 0f, 1f, 10f, 0, 1f }), "Priority audio replaces an occupied voice");
            typeof(MeleeElementSfxService).GetMethod("EnsureInstance", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            element = UnityEngine.Object.FindFirstObjectByType<MeleeElementSfxService>();
            var method = typeof(MeleeElementSfxService).GetMethod("TryPlaySettings", Private);
            var settings = new MeleeElementSfxCueSettings { cooldown = 60, maxVoices = 0, volume = 0, spatial = false };
            bool Play(int key) => (bool)method.Invoke(element, new object[] { settings, clip, key, Vector3.zero, false, 60f });
            check(Play(-9001) && !Play(-9001), "Repeated element cue reaches cooldown diagnostic");
            settings.cooldown = 0; settings.maxVoices = 1;
            check(Play(-9002) && !Play(-9002), "Per-cue voice limit is distinct from pool exhaustion");
            settings.maxVoices = 0;
            for (int i = 0; i < 32; i++) Play(-9003);
            check(!Play(-9003), "Saturated element pool reaches capacity rejection diagnostic");
            stats.Add(new { frame = Time.frameCount, purpose = "Synthetic saturation after measured combat; not natural shortage evidence" });
        }
        finally
        {
            foreach (var service in new MonoBehaviour[] { combat, element })
                if (service) foreach (var source in service.GetComponentsInChildren<AudioSource>(true))
                    if (source.clip == clip) { source.Stop(); source.clip = null; }
            UnityEngine.Object.Destroy(clip);
            File.WriteAllText(Path.Combine(output, "pool-calibration.json"), JsonConvert.SerializeObject(stats, Formatting.Indented));
        }
    }
    public static void CheckPoolExport(string capture, Action<bool, string> check)
    {
        string totals = File.ReadAllText(Path.Combine(capture, "pool-totals.csv"));
        foreach (var token in new[] { "CombatSfx,capacity_reject,", "CombatSfx,replace_playing,", "MeleeElementSfx,cooldown,", "MeleeElementSfx,cue_limit,", "MeleeElementSfx,capacity_reject," })
            check(totals.Replace("\"", "").Contains(token), "Pool export: " + token);
    }

    public static object CheckBoundaryQueries(string path)
    {
        var library = UnityEditor.AssetDatabase.LoadAssetAtPath<CavePlatformLibrary>("Assets/ProjectOverburst/04_Contents/World/Caves/Procedural/PlatformLibrary.asset");
        var rows = new List<object>(); int total = 0, mismatches = 0;
        foreach (var platform in library.platforms)
        {
            var boundary = platform.prefab.GetComponentInChildren<CavePlatformBoundary>().Data;
            var exact = (Func<Vector3, bool>)Delegate.CreateDelegate(typeof(Func<Vector3, bool>), boundary,
                typeof(CavePlatformBoundary).GetMethod("ContainsExact", BindingFlags.Instance | BindingFlags.NonPublic));
            var points = new List<Vector3>();
            foreach (var loop in boundary.loops) foreach (var p in loop.points)
                for (int j = -1; j <= 1; j++) for (int k = -1; k <= 1; k++) points.Add(p + new Vector3(j * .001f, 0, k * .001f));
            foreach (var point in points) if (boundary.ContainsLocal(point) != exact(point)) mismatches++;
            var watch = System.Diagnostics.Stopwatch.StartNew(); int inside = 0;
            for (int repeat = 0; repeat < 3; repeat++) foreach (var point in points) if (boundary.ContainsLocal(point)) inside++;
            watch.Stop(); total += points.Count;
            rows.Add(new { name = boundary.name, points = points.Count, inside, milliseconds = watch.Elapsed.TotalMilliseconds });
        }
        var result = new { status = mismatches == 0 ? "PASS" : "FAIL", total, mismatches, rows };
        File.WriteAllText(path, JsonConvert.SerializeObject(result, Formatting.Indented));
        if (mismatches != 0) throw new InvalidOperationException("Indexed boundary changed exact polygon membership");
        return result;
    }

    static void CompareCorrections(CaveWorld world, string path)
    {
        int samples = 0, differences = 0; float maxError = 0;
        var watch = new System.Diagnostics.Stopwatch();
        double repeatedMs = 0, singleMs = 0;
        foreach (var court in world.courts)
        for (int angle = 0; angle < 16; angle++)
        {
            var start = court.center;
            var movement = new CaveFallProtection.Movement();
            movement.Resolve(start, start, .4f);
            var direction = new Vector3(Mathf.Cos(angle * Mathf.PI / 8), 0, Mathf.Sin(angle * Mathf.PI / 8));
            for (int step = 0; step < 12; step++)
            {
                var from = start + direction * (step * 3f) + Vector3.up * (step % 3);
                var single = movement; var repeated = movement;
                watch.Restart(); var one = single.Resolve(from, from, .4f); watch.Stop(); singleMs += watch.Elapsed.TotalMilliseconds;
                watch.Restart(); repeated.Resolve(from, from, .4f); var preview = repeated;
                var two = preview.Resolve(from, from, .4f); watch.Stop(); repeatedMs += watch.Elapsed.TotalMilliseconds;
                float error = Vector3.Distance(one, two); maxError = Mathf.Max(maxError, error); if (error > .001f) differences++;
                samples++; movement = single;
            }
        }
        var boundaries = world.generatedRoot.GetComponentsInChildren<CavePlatformBoundary>().Select(b => new {
            name = b.Data.name, triangles = b.Data.floorTriangles.Length / 3, outlinePoints = b.Data.loops.Sum(l => l.points.Length) }).ToArray();
        File.WriteAllText(path, JsonConvert.SerializeObject(new { samples, differences, maxError, singleMs, repeatedMs, boundaries }, Formatting.Indented));
    }
}
