using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// Local-only 60D verifier: light radiance/overcharge + triple/double heavy, dark corrosion barrage (2026-10-01).
// Runs in an isolated account (OVERBURST_SAVE_DIRECTORY) from PersistentScene and exits Play itself.
[InitializeOnLoad]
public static class UpperElementPlayVerifier
{
    const string Key = "UpperElementPlayVerifier";
    static IEnumerator work;
    static int frame;
    static double deadline;
    static readonly List<object> results = new List<object>();
    static readonly List<string> errors = new List<string>();
    static readonly List<string> checks = new List<string>();
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static UpperElementPlayVerifier() { EditorApplication.playModeStateChanged += State; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void BindIsolatedAccountBeforeBoot()
    {
        if (!SessionState.GetBool(Key, false)) return;
        string directory = IsolatedSavePlayGuard.ValidateDirectory(Path.Combine(Output, "IsolatedAccount"));
        SessionState.SetString(Key + ".bootEnv", Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "");
        Environment.SetEnvironmentVariable(IsolatedSavePlayGuard.Variable, directory);
    }

    public static void Run(string output) => Run(output, false);
    // 2026-10-01: runs only the dark corrosion barrage part (light checks are owned elsewhere).
    public static void RunDarkOnly(string output) => Run(output, true);

    static void Run(string output, bool darkOnly)
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Already playing");
        SessionState.SetBool(Key + ".darkOnly", darkOnly);
        SessionState.SetString(Key + ".startScene", AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene));
        UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(
            "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output);
        SessionState.EraseString(Key + ".env");
        IsolatedSavePlayGuard.PrepareIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
        SessionState.SetBool(Key, true);
        SessionState.SetString(Key + ".status", "RUNNING");
        AssetDatabase.DisallowAutoRefresh(); SessionState.SetBool(Key + ".refreshOwned", true);
        EditorApplication.EnterPlaymode();
    }

    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            SessionState.SetBool(Key + ".background", Application.runInBackground);
            SessionState.SetInt(Key + ".fps", Application.targetFrameRate);
            Application.runInBackground = true; Application.targetFrameRate = 60;
            EditorApplication.LockReloadAssemblies(); SessionState.SetBool(Key + ".reloadOwned", true);
            results.Clear(); errors.Clear(); checks.Clear(); frame = -1; deadline = EditorApplication.timeSinceStartup + 300;
            stack.Clear(); work = Verify(); Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            (work as IDisposable)?.Dispose(); work = null;
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
            Application.targetFrameRate = SessionState.GetInt(Key + ".fps", -1);
            if (SessionState.GetBool(Key + ".reloadOwned", false))
            { EditorApplication.UnlockReloadAssemblies(); SessionState.EraseBool(Key + ".reloadOwned"); }
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", null); SessionState.EraseString(Key + ".env");
            string previousStart = SessionState.GetString(Key + ".startScene", "");
            UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previousStart)
                ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previousStart);
            SessionState.SetBool(Key, false);
            if (SessionState.GetBool(Key + ".refreshOwned", false))
            { AssetDatabase.AllowAutoRefresh(); SessionState.EraseBool(Key + ".refreshOwned"); }
        }
    }

    static void Log(string m, string s, LogType t) { if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert) errors.Add(m); }

    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try { Check(EditorApplication.timeSinceStartup < deadline, "Timeout"); if (Step()) return; Check(errors.Count == 0, string.Join(" | ", errors)); Finish("PASS"); }
        catch (Exception e) { Finish("FAIL " + e); }
    }

    // Runs nested IEnumerators (e.g. Wait) inline, one player frame per yielded null.
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static bool Step()
    {
        if (stack.Count == 0 && work != null) { stack.Push(work); work = null; }
        while (stack.Count > 0)
        {
            IEnumerator top = stack.Peek();
            if (top.MoveNext())
            {
                if (top.Current is IEnumerator nested) { stack.Push(nested); continue; }
                return true;
            }
            stack.Pop();
        }
        return false;
    }

    static void Finish(string status)
    {
        SessionState.SetString(Key + ".status", status);
        File.WriteAllText(Path.Combine(Output, "play-results.json"), JsonConvert.SerializeObject(new { status, checks, results, errors }, Formatting.Indented));
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }

    static void Check(bool ok, string message)
    { if (!ok) throw new InvalidOperationException(message); if (!checks.Contains(message)) checks.Add(message); }

    static void SetPrivate(object target, string property, object value)
    {
        var info = target.GetType().GetProperty(property);
        info.GetSetMethod(true).Invoke(target, new[] { value });
    }

    static void Warp(PlayerInputFacade player, Vector3 p, Vector3 facing)
    {
        var cc = player.GetComponent<CharacterController>(); bool on = cc != null && cc.enabled;
        if (on) cc.enabled = false; player.transform.position = p; player.transform.rotation = Quaternion.LookRotation(facing);
        if (on) cc.enabled = true; Physics.SyncTransforms();
    }

    static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }

    // Retries for up to 2 s so a just-finished action or mode switch cannot flake the fixture.
    static WeaponActionResult lastHeavyResult;
    static IEnumerator StartHeavy(MeleeRuntime melee)
    {
        float until = Time.time + 2f;
        while (Time.time < until)
        {
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
            melee.SetManualInputEnabled(true);
            lastHeavyResult = melee.TryStartHeavyAttack(Vector3.forward);
            if (lastHeavyResult == WeaponActionResult.Accepted) yield break;
            yield return null;
        }
    }

    static float MeanPairDistance(List<EnemyActor> list)
    {
        float sum = 0; int n = 0;
        for (int i = 0; i < list.Count; i++) for (int j = i + 1; j < list.Count; j++)
        { Vector3 d = list[i].transform.position - list[j].transform.position; d.y = 0; sum += d.magnitude; n++; }
        return n > 0 ? sum / n : 0;
    }

    static IEnumerator Verify()
    {
        EnemySpawnService spawn = null; EnemyThemeTrialHarness ui = null; MeleeRuntime melee = null;
        var leased = new List<EnemyActor>();
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            Check(Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase),
                "Account isolation: " + Overburst.Persistence.AccountBootstrap.SaveDirectory);
            var player = PlayerInputFacade.Current; var actor = PlayerContext.GetOrCreate().CurrentActor;
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            actor.Health.SetMaxHp(1000000, true);
            ui = EnemyThemeTrialHarness.Current; if (!ui.InArena) ui.ToggleArena();
            Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn), "Spawn service");
            foreach (var t in ui.tables) Check(spawn.RegisterAdditionalCatalog(t.Catalog, out string error), error);
            var defs = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).Where(d => d != null).Distinct().ToArray();
            var medium = defs.First(d => d.EnemyId.Contains("Scolokarck"));
            var small = defs.First(d => d.EnemyId.Contains("Ceratoferox"));
            var origin = player.transform.position;
            melee = player.GetComponent<MeleeRuntime>();
            var tuning = OverburstElementTuning.Current;
            var heavyDef = AssetDatabase.LoadAssetAtPath<MeleeHeavyAttackDefinition>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Heavy/GreatswordHeavyAttack.asset");

            EnemyActor Spawn(EnemyDefinition d, Vector3 at)
            {
                Check(spawn.TrySpawn(new EnemySpawnRequest(d, at, Quaternion.LookRotation(Vector3.back), player.transform), out var e), "Spawn " + d.EnemyId);
                leased.Add(e); e.AI.enabled = false; e.Movement.StopMovement(); e.Health.SetMaxHp(1000000, true);
                e.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                return e;
            }
            void ReleaseAll() { foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e); leased.Clear(); }
            OverburstElementEnergy Equip(WeaponElement element)
            {
                Check(actor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common, element: element)), "Equip " + element);
                PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System); melee.SetManualInputEnabled(true);
                Warp(player, origin, Vector3.forward);
                var energy = player.GetComponent<OverburstElementEnergy>() ?? player.gameObject.AddComponent<OverburstElementEnergy>();
                energy.Clear();
                return energy;
            }
            int seq = 20000;
            float commit = -1, started = 0;
            if (!SessionState.GetBool(Key + ".darkOnly", false))
            {

            // ---------- L1: light energy, radiance, decay, hold, attack speed ----------
            var light = Equip(WeaponElement.Light);
            yield return null;
            Check(light.Element == WeaponElement.Light && Mathf.Approximately(light.Capacity, 200f), "Light capacity 200");
            for (int i = 0; i < 10; i++) light.RecordConfirmedHit(light.WeaponInstanceId, light.Element, ++seq, 1);
            Check(Mathf.Approximately(light.Amount, 100f) && light.RadianceStacks == 0 && !light.IsOvercharged, "0->100 at 10/phase, no radiance at 100: " + light.Amount + "/" + light.RadianceStacks);
            light.RecordConfirmedHit(light.WeaponInstanceId, light.Element, ++seq, 1);
            Check(Mathf.Approximately(light.Amount, 110f) && light.RadianceStacks == 5, "Overcharge phase +10 and +5 radiance: " + light.Amount + "/" + light.RadianceStacks);
            light.RecordConfirmedHit(light.WeaponInstanceId, light.Element, ++seq, 1, true);
            Check(Mathf.Approximately(light.Amount, 130f) && light.RadianceStacks == 10, "Critical +20 above 100: " + light.Amount);
            // Same phase, second target: no extra energy or radiance.
            Check(!light.RecordConfirmedHit(light.WeaponInstanceId, light.Element, seq, 1), "Same phase dedupe");
            float before = light.Amount; int frames0 = Time.frameCount; float t0 = Time.time, rt0 = Time.realtimeSinceStartup;
            yield return Wait(1f);
            float decayed = before - light.Amount;
            string decayDiag = $"decayed={decayed} frames={Time.frameCount - frames0} dt={Time.time - t0} real={Time.realtimeSinceStartup - rt0} scale={Time.timeScale} enabled={light.enabled} active={light.isActiveAndEnabled} element={light.Element} amount={light.Amount} energies={player.GetComponentsInChildren<OverburstElementEnergy>(true).Length} rate={tuning.SafeLightOverchargeDecayPerSecond}";
            results.Add(new { goal = "L1-decay-diag", decayDiag });
            float expectedDecay = tuning.SafeLightOverchargeDecayPerSecond * (Time.time - t0);
            Check(Mathf.Abs(decayed - expectedDecay) < expectedDecay * 0.3f, "Continuous decay at the tuning rate above 100: " + decayDiag);
            for (int i = 0; i < 12; i++) light.RecordConfirmedHit(light.WeaponInstanceId, light.Element, ++seq, 1);
            Check(Mathf.Approximately(light.Amount, 200f), "Capped at 200: " + light.Amount);
            yield return Wait(1.2f);
            Check(Mathf.Approximately(light.Amount, 200f), "200 held for 2 s: " + light.Amount);
            light.RecordConfirmedHit(light.WeaponInstanceId, light.Element, ++seq, 1); // refresh hold
            yield return Wait(1.2f);
            Check(Mathf.Approximately(light.Amount, 200f), "Hit refreshes 200 hold: " + light.Amount);
            yield return Wait(1.3f);
            Check(light.Amount < 200f, "Decay resumes after hold: " + light.Amount);
            int stacksBeforeSpeed = light.RadianceStacks;
            SetPrivate(light, "RadianceStacks", 100);
            var baseStats = FlaskCombatModifiers.Apply(actor.Equipment.CurrentWeaponStats, player.gameObject);
            var buffed = UpperElementCombatUtility.ApplyRadianceAttackSpeed(baseStats, player.gameObject);
            float expectedSpeed = Mathf.Max(baseStats.meleeAttackSpeedMultiplier, Mathf.Min(1.8f, baseStats.meleeAttackSpeedMultiplier * 1.2f));
            Check(Mathf.Abs(buffed.meleeAttackSpeedMultiplier - expectedSpeed) < 0.0001f, "Radiance attack speed x1.2 capped 1.8");
            SetPrivate(light, "Amount", 100.4f);
            yield return Wait(0.4f);
            Check(Mathf.Approximately(light.Amount, 100f) && light.RadianceStacks == 0, "Radiance vanishes at 100: " + light.Amount + "/" + light.RadianceStacks);
            results.Add(new { goal = "L1", decayPerSecond = decayed, stacksBeforeSpeed, baseSpeed = baseStats.meleeAttackSpeedMultiplier, buffedSpeed = buffed.meleeAttackSpeedMultiplier });

            // ---------- L2: triple (overcharged) ----------
            Warp(player, origin, Vector3.forward); yield return null;
            var centre = Spawn(medium, origin + Vector3.forward * 1.7f);
            var ring = Spawn(small, origin + Vector3.forward * 1.7f + Vector3.right * 3.4f);
            var lightHits = new List<(float time, float damage, string who)>();
            void LightCentre(CombatHealth h, DamageInfo d) { if (d.element == WeaponElement.Light && (d.playerAttackKind & (PlayerAttackKind.Heavy | PlayerAttackKind.Elemental)) != 0) lightHits.Add((Time.time, d.damage, "centre:" + d.playerAttackKind)); }
            void LightRing(CombatHealth h, DamageInfo d) { if (d.element == WeaponElement.Light && (d.playerAttackKind & (PlayerAttackKind.Heavy | PlayerAttackKind.Elemental)) != 0) lightHits.Add((Time.time, d.damage, "ring:" + d.playerAttackKind)); }
            centre.Health.OnDamaged += LightCentre; ring.Health.OnDamaged += LightRing;
            light.Clear(); SetPrivate(light, "Amount", 200f); SetPrivate(light, "RadianceStacks", 100);
            int dispatched = LightTripleImpactScheduler.DispatchedHitCount;
            var tripleStats = TransientVfxPool.GetStatistics(heavyDef.elementVfx.lightTripleImpact);
            yield return StartHeavy(melee); Check(lastHeavyResult == WeaponActionResult.Accepted, "Triple heavy start: " + lastHeavyResult);
            commit = -1; started = Time.time;
            while (Time.time < started + 3f) { if (commit < 0 && light.Amount == 0) commit = Time.time; if (commit >= 0) break; yield return null; }
            Check(commit >= 0 && light.RadianceStacks == 0, "Triple commit consumes energy and radiance");
            yield return Wait(2.2f);
            Check(LightTripleImpactScheduler.DispatchedHitCount == dispatched + 2, "Two scheduled follow-ups: " + (LightTripleImpactScheduler.DispatchedHitCount - dispatched));
            var centreHits = lightHits.Where(x => x.who.StartsWith("centre")).ToList();
            var ringHits = lightHits.Where(x => x.who.StartsWith("ring")).ToList();
            Check(centreHits.Count == 3, "Centre takes all three: " + centreHits.Count);
            Check(ringHits.Count == 1, "Ring at 3 m takes only the 3rd: " + ringHits.Count);
            float gap12 = centreHits[1].time - centreHits[0].time, gap23 = centreHits[2].time - centreHits[1].time;
            Check(Mathf.Abs(gap12 - 0.8f) < 0.12f && Mathf.Abs(gap23 - 0.8f) < 0.12f, "0.8 s spacing at 1.25x: " + gap12 + "/" + gap23);
            float ratio23 = centreHits[1].damage / centreHits[2].damage;
            Check(Mathf.Abs(ratio23 - 1.5f) < 0.02f, "2nd/3rd = 1.5H/1.0H: " + ratio23);
            Check(TransientVfxPool.GetStatistics(heavyDef.elementVfx.lightTripleImpact).Requests == tripleStats.Requests + 1, "Triple VFX spawned once");
            Check(centre.GetComponent<ElementalStatusController>().GetStackCount(WeaponElement.Light) == 0, "Light leaves no enemy status");
            results.Add(new { goal = "L2-triple", gap12, gap23, ratio23, slam = centreHits[0].damage, second = centreHits[1].damage, third = centreHits[2].damage, ringHits = ringHits.Count });
            while (melee.IsAttackInProgress) yield return null;

            // ---------- L2: double (<=100) ----------
            lightHits.Clear();
            light.Clear(); SetPrivate(light, "Amount", 100f);
            dispatched = LightTripleImpactScheduler.DispatchedHitCount;
            var doubleStats = TransientVfxPool.GetStatistics(heavyDef.elementVfx.lightDoubleImpact);
            yield return StartHeavy(melee); Check(lastHeavyResult == WeaponActionResult.Accepted, "Double heavy start: " + lastHeavyResult);
            yield return Wait(2.0f);
            Check(LightTripleImpactScheduler.DispatchedHitCount == dispatched + 1, "One scheduled follow-up");
            centreHits = lightHits.Where(x => x.who.StartsWith("centre")).ToList();
            Check(centreHits.Count == 2, "Double hits centre twice: " + centreHits.Count);
            float doubleGap = centreHits[1].time - centreHits[0].time;
            Check(Mathf.Abs(doubleGap - 0.8f) < 0.12f, "Double spacing 0.8 s: " + doubleGap);
            Check(TransientVfxPool.GetStatistics(heavyDef.elementVfx.lightDoubleImpact).Requests == doubleStats.Requests + 1, "Double VFX spawned once");
            results.Add(new { goal = "L2-double", doubleGap, slam = centreHits[0].damage, third = centreHits[1].damage });
            while (melee.IsAttackInProgress) yield return null;
            centre.Health.OnDamaged -= LightCentre; ring.Health.OnDamaged -= LightRing;
            ReleaseAll(); yield return null;
            }

            // ---------- 60D 4 (2026-10-01): dark corrosion barrage ----------
            var dark = Equip(WeaponElement.Dark);
            yield return null;
            void Corrode(EnemyActor e, int stacks)
            {
                var s = e.GetComponent<ElementalStatusController>();
                for (int k = 0; k < stacks; k++) s.TryApplyDirectHit(new ElementalStatusApplication(WeaponElement.Dark, 10, player.gameObject, dark.WeaponInstanceId, true, false, e.transform.position, Vector3.forward));
            }
            int Stacks(EnemyActor e) => e.GetComponent<ElementalStatusController>().GetStackCount(WeaponElement.Dark);
            int Reserved(EnemyActor e) => e.GetComponent<ElementalStatusController>().ReservedCorrosion;
            void FillDark() { dark.Clear(); for (int i = 0; i < 10; i++) dark.RecordConfirmedHit(dark.WeaponInstanceId, dark.Element, ++seq, 1); }
            var barrageHits = new Dictionary<EnemyActor, List<(float time, float damage)>>();
            var hooks = new List<(CombatHealth health, Action<CombatHealth, DamageInfo> hook)>();
            void Track(EnemyActor e)
            {
                var list = new List<(float, float)>(); barrageHits[e] = list;
                Action<CombatHealth, DamageInfo> hook = (h, d) =>
                {
                    if (d.element == WeaponElement.Dark && !d.triggersOnHitEffects
                        && (d.playerAttackKind & PlayerAttackKind.Elemental) != 0 && (d.playerAttackKind & PlayerAttackKind.Heavy) == 0)
                        list.Add((Time.time, d.damage));
                };
                e.Health.OnDamaged += hook; hooks.Add((e.Health, hook));
            }
            void Untrack() { foreach (var (health, hook) in hooks) if (health != null) health.OnDamaged -= hook; hooks.Clear(); barrageHits.Clear(); }
            IEnumerator HeavyAndFinish(string label, bool cancel)
            {
                yield return StartHeavy(melee); Check(lastHeavyResult == WeaponActionResult.Accepted, label + " heavy start: " + lastHeavyResult);
                commit = -1; started = Time.time;
                while (Time.time < started + 3f) { if (dark.Amount == 0) { commit = Time.time; break; } yield return null; }
                Check(commit >= 0, label + " commit");
                if (cancel) melee.CancelCurrentAttackState(); // the scheduler must finish anyway
                float barrageDeadline = commit + Mathf.Ceil(DarkBarrageScheduler.LastShotCount / (float)tuning.SafeDarkBarrageShotsPerVolley)
                    * tuning.SafeDarkBarrageFireInterval + tuning.SafeDarkBarrageFlightMax + 2f;
                while (DarkBarrageScheduler.ActiveCount > 0 && Time.time < barrageDeadline) yield return null;
                Check(DarkBarrageScheduler.ActiveCount == 0, label + " barrage finished");
                while (melee.IsAttackInProgress) yield return null;
            }

            // D1: the magnet is gone, the corrosion aura stays.
            Vector3 p0 = origin + Vector3.forward * 8f;
            var group = new List<EnemyActor>();
            for (int i = 0; i < 6; i++) { float a = i * Mathf.PI * 2f / 6f; group.Add(Spawn(small, p0 + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 2.5f)); }
            yield return null;
            foreach (var e in group) { Corrode(e, 5); Check(Stacks(e) == 5, "Dark 5 stacks"); }
            float spreadBefore = MeanPairDistance(group);
            yield return Wait(1.5f);
            float spreadAfter = MeanPairDistance(group);
            Check(Mathf.Abs(spreadAfter - spreadBefore) < 0.05f, "Corroded enemies no longer clump: " + spreadBefore + " -> " + spreadAfter);
            var auraController = group[0].GetComponent<MeleeElementStatusAuraController>();
            Check(auraController != null && auraController.IsAuraActive(MeleeElementStatusAuraType.Corroded), "Corrosion aura active");
            results.Add(new { goal = "D1", spreadBefore, spreadAfter });

            // D0: reservation. Collected once, first shot removes only the reserved part, later stacks stay.
            {
                var s = group[0].GetComponent<ElementalStatusController>();
                int first = s.ReserveCorrosion(), second = s.ReserveCorrosion();
                Check(first == 5 && second == 0 && s.ReservedCorrosion == 5, "Reserve once: " + first + "/" + second + "/" + s.ReservedCorrosion);
                s.ReleaseCorrosionReservation(5);
                Check(s.ReservedCorrosion == 0 && Stacks(group[0]) == 5, "Release keeps stacks");
                s.ConsumeForDischarge(WeaponElement.Dark, out _);
                Corrode(group[0], 3);
                Check(s.ReserveCorrosion() == 3, "Reserve 3");
                Corrode(group[0], 1);
                int removed = s.ConsumeReservedCorrosion(3);
                Check(removed == 3 && Stacks(group[0]) == 1 && s.ReservedCorrosion == 0, "First shot removes the reserved 3 only: " + removed + "/" + Stacks(group[0]) + "/" + s.ReservedCorrosion);
                Check(s.ReserveCorrosion() == 1, "Stack gained after the reservation is free for the next heavy");
                s.ClearAllStatuses();
                Check(s.ReservedCorrosion == 0, "Clear resets the reservation");
                results.Add(new { goal = "D0-reserve", ok = true });
            }
            ReleaseAll(); yield return null;

            // D2: only actual slam hits donate corrosion; all on-screen enemies can receive the shared ammo.
            Warp(player, origin, Vector3.forward); yield return Wait(.3f);
            Vector3 impact = origin + Vector3.forward * 1.7f;
            var donorA = Spawn(medium, impact);
            var donorB = Spawn(small, impact + Vector3.right * 1.5f);
            var unmarked = Spawn(small, impact + Vector3.forward * 7f);
            var markedOutside = Spawn(small, impact + new Vector3(2f, 0f, 7f));
            var offscreen = Spawn(small, impact + Vector3.right * 100f);
            yield return null;
            Corrode(donorA, 5); Corrode(donorB, 3); Corrode(markedOutside, 4); Corrode(offscreen, 5);
            foreach (var e in leased) Track(e);
            var commonClips = new[] { "MonsterHitCommon01", "MonsterHitCommon02", "MonsterHitCommon03" }
                .Select(CombatActionSfxService.ResolveNamedClip).ToArray();
            Check(commonClips.All(c => c != null), "Common hit sound clips loaded");
            Check(Mathf.Approximately(tuning.SafeDarkBarrageSpeed, 14f)
                && Mathf.Approximately(tuning.SafeDarkBarrageFlightMin, .3f)
                && Mathf.Approximately(tuning.SafeDarkBarrageFlightMax, .9f), "Flight speed and duration unchanged");
            Check(Mathf.Approximately(tuning.SafeDarkBarrageShotDamage, .08f), "Shot damage coefficient 8 percent");
            FillDark(); int casts = DarkBarrageScheduler.CastCount;
            int sfxBefore = DarkBarrageScheduler.TotalCommonHitSfx;
            yield return StartHeavy(melee); Check(lastHeavyResult == WeaponActionResult.Accepted, "Dark heavy start");
            commit = -1; started = Time.time;
            while (Time.time < started + 3f) { if (dark.Amount == 0) { commit = Time.time; break; } yield return null; }
            Check(commit >= 0 && DarkBarrageScheduler.CastCount == casts + 1, "Barrage submitted after actual slam");
            Check(Stacks(donorA) == 0 && Stacks(donorB) == 0 && Reserved(donorA) == 0,
                "Slam immediately consumes both donors");
            Check(Stacks(markedOutside) == 4 && Stacks(offscreen) == 5, "Unhit enemies keep corrosion");
            Check(DarkBarrageScheduler.LastDonorCount == 2 && DarkBarrageScheduler.LastStackSum == 8
                && DarkBarrageScheduler.LastShotCount == 8 && !DarkBarrageScheduler.LastFinisher,
                "5+3 stacks produce exactly 8 shared shots, no extra finisher");
            Corrode(donorA, 1); // Later weak-hit corrosion belongs to the next slam.
            melee.CancelCurrentAttackState();
            bool commonVoiceObserved = false;
            while (DarkBarrageScheduler.ActiveCount > 0 && Time.time < commit + 6f)
            {
                commonVoiceObserved |= UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)
                    .Any(a => a.isPlaying && commonClips.Contains(a.clip));
                yield return null;
            }
            Check(DarkBarrageScheduler.ActiveCount == 0, "Barrage finishes after attack cancel");
            Check(barrageHits.Values.Sum(h => h.Count) == 8, "Exactly 8 confirmed projectile hits");
            Check(barrageHits[unmarked].Count > 0 && barrageHits[markedOutside].Count > 0,
                "Unhit enemies, with and without corrosion, receive projectiles");
            Check(barrageHits[offscreen].Count == 0 && Stacks(offscreen) == 5, "Off-screen enemy excluded");
            Check(Stacks(donorA) == 1 && Stacks(markedOutside) == 4 && dark.Amount == 0,
                "Projectile hits preserve new/recipient corrosion and never recharge energy");
            Check(DarkBarrageScheduler.TotalCommonHitSfx == sfxBefore + 8 && commonVoiceObserved,
                "Every confirmed projectile hit requests sound and common AudioSource actually plays");
            var launchOrder = DarkBarrageScheduler.LastLaunchTargets.ToList();
            var launchTimes = DarkBarrageScheduler.LastLaunchTimes.ToList();
            Check(launchOrder.Count == 8 && launchOrder.Take(3).Distinct().Count() == 3,
                "First volley spreads across 3 different enemies");
            Check(launchTimes.Take(3).Distinct().Count() == 1 && DarkBarrageScheduler.LastVolleySize == 3,
                "Three shots leave together");
            float stream = DarkBarrageScheduler.LastFinalLaunchTime - DarkBarrageScheduler.LastFirstLaunchTime;
            Check(Mathf.Abs(stream - .2f) < .08f && Mathf.Approximately(DarkBarrageScheduler.LastInterval, .1f),
                "8 shots leave in 3 volleys at 0.10-second intervals: " + stream);
            Check(barrageHits.Values.SelectMany(h => h).All(h => Mathf.Abs(h.damage - DarkBarrageScheduler.LastShotDamage) < .01f),
                "All projectiles use the same reduced damage");
            results.Add(new { goal = "D2-screen-ammo", donors = DarkBarrageScheduler.LastDonorCount,
                stacks = DarkBarrageScheduler.LastStackSum, shots = DarkBarrageScheduler.LastShotCount,
                targets = DarkBarrageScheduler.LastTargetCount, stream, launchOrder, launchTimes,
                soundHits = DarkBarrageScheduler.TotalCommonHitSfx - sfxBefore, commonVoiceObserved });
            Untrack(); ReleaseAll(); yield return null;

            // A lethal slam still contributes its pre-hit stacks to an unmarked on-screen survivor.
            Warp(player, origin, Vector3.forward); yield return Wait(.3f);
            var victim = Spawn(small, impact); victim.Health.SetMaxHp(1, true);
            var survivor = Spawn(medium, impact + Vector3.forward * 6f);
            yield return null;
            Corrode(victim, 3); Track(survivor); FillDark();
            yield return HeavyAndFinish("Lethal donor", false);
            Check(victim.Health.IsDead && DarkBarrageScheduler.LastShotCount == 3, "Lethal slam retains 3 donated stacks");
            Check(barrageHits[survivor].Count == 3 && Stacks(survivor) == 0, "Unmarked survivor receives all 3 shots");
            results.Add(new { goal = "D2-lethal-donor", shots = DarkBarrageScheduler.LastShotCount, hits = barrageHits[survivor].Count });
            Untrack(); ReleaseAll(); yield return null;

            // A target released while its projectile flies must be replaced inside the original screen snapshot.
            Warp(player, origin, Vector3.forward); yield return Wait(.3f);
            donorA = Spawn(medium, impact); donorB = Spawn(small, impact + Vector3.right * 1.5f);
            unmarked = Spawn(medium, impact + Vector3.forward * 6f);
            yield return null;
            Corrode(donorA, 5); Corrode(donorB, 5); FillDark();
            int retargets = DarkBarrageScheduler.TotalRetargets, fizzles = DarkBarrageScheduler.TotalFizzles;
            int launchedBefore = DarkBarrageScheduler.TotalLaunched;
            yield return StartHeavy(melee); Check(lastHeavyResult == WeaponActionResult.Accepted, "Retarget heavy start");
            float until = Time.time + 5f;
            while (DarkBarrageScheduler.TotalLaunched == launchedBefore && Time.time < until) yield return null;
            Check(DarkBarrageScheduler.LastLaunchTargets.Count > 0, "Retarget fixture launched");
            int releasedId = DarkBarrageScheduler.LastLaunchTargets[0];
            var released = leased.First(e => e.GetComponent<CombatTarget>().GetInstanceID() == releasedId);
            spawn.Release(released);
            // Moving the camera after commit cannot remove the saved target set.
            Camera gameplayCamera = Camera.main;
            var brain = gameplayCamera.GetComponent<Unity.Cinemachine.CinemachineBrain>();
            bool brainEnabled = brain != null && brain.enabled;
            Vector3 cameraPosition = gameplayCamera.transform.position;
            if (brain != null) brain.enabled = false;
            gameplayCamera.transform.position += Vector3.right * 200f;
            try
            {
                while (DarkBarrageScheduler.ActiveCount > 0 && Time.time < until + 4f) yield return null;
                Check(DarkBarrageScheduler.ActiveCount == 0 && DarkBarrageScheduler.TotalRetargets > retargets
                    && DarkBarrageScheduler.TotalFizzles == fizzles, "Released target retargets within frozen screen despite camera movement");
            }
            finally { gameplayCamera.transform.position = cameraPosition; if (brain != null) brain.enabled = brainEnabled; }
            while (melee.IsAttackInProgress) yield return null;
            results.Add(new { goal = "D2-retarget-camera", retargeted = DarkBarrageScheduler.TotalRetargets - retargets });
            ReleaseAll(); yield return null;

            // Half energy affects the slam/damage, not the captured screen range or the ammo formula.
            Warp(player, origin, Vector3.forward); yield return Wait(.3f);
            var near = Spawn(small, impact);
            var beyond = Spawn(small, impact + Vector3.forward * 6f);
            yield return null; Corrode(near, 2); Corrode(beyond, 4);
            dark.Clear(); for (int i = 0; i < 5; i++) dark.RecordConfirmedHit(dark.WeaponInstanceId, dark.Element, ++seq, 1);
            yield return HeavyAndFinish("Half", true);
            Check(DarkBarrageScheduler.LastShotCount == 2 && !DarkBarrageScheduler.LastFinisher
                && Stacks(near) == 0 && Stacks(beyond) == 4, "Half energy consumes only hit donor and produces 2 shots");
            results.Add(new { goal = "D2-half", shots = DarkBarrageScheduler.LastShotCount });
            ReleaseAll(); yield return null;

            // No corrosion hit means no ammo even when there are marked enemies elsewhere on-screen.
            Warp(player, origin, Vector3.forward); yield return Wait(.3f);
            beyond = Spawn(small, impact + Vector3.forward * 8f); yield return null; Corrode(beyond, 5);
            FillDark(); casts = DarkBarrageScheduler.CastCount;
            yield return HeavyAndFinish("No donor", false);
            Check(DarkBarrageScheduler.CastCount == casts && DarkBarrageScheduler.LastShotCount == 0 && Stacks(beyond) == 5,
                "No hit corrosion means no barrage: " + DarkBarrageScheduler.LastShotCount + "/" + Stacks(beyond));
            results.Add(new { goal = "D2-no-donor", ok = true });
            ReleaseAll(); yield return null;

            // More than the old 40-target cap: every actual slam donor contributes all its stacks.
            Warp(player, origin, Vector3.forward); yield return Wait(.3f);
            var dense = new List<EnemyActor>();
            for (int i = 0; i < 45; i++)
            {
                float angle = i * Mathf.PI * 2f / 45f;
                dense.Add(Spawn(small, impact + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.4f));
            }
            yield return null; foreach (var e in dense) Corrode(e, 5); FillDark();
            int denseHits = DarkBarrageScheduler.TotalHits;
            yield return HeavyAndFinish("45 donors", false);
            Check(DarkBarrageScheduler.LastDonorCount == 45 && DarkBarrageScheduler.LastShotCount == 225,
                "All 45 donors contribute 225 shots without the old 40-target limit");
            Check(DarkBarrageScheduler.TotalHits == denseHits + 225 && dense.All(e => Stacks(e) == 0),
                "Dense barrage confirms all 225 hits and clears only donated corrosion");
            results.Add(new { goal = "D2-dense", donors = DarkBarrageScheduler.LastDonorCount,
                shots = DarkBarrageScheduler.LastShotCount, hits = DarkBarrageScheduler.TotalHits - denseHits,
                stream = DarkBarrageScheduler.LastFinalLaunchTime - DarkBarrageScheduler.LastFirstLaunchTime });
            ReleaseAll(); yield return null;

            // D2-empty: energy 0 submits nothing.
            dark.Clear(); casts = DarkBarrageScheduler.CastCount;
            yield return StartHeavy(melee); Check(lastHeavyResult == WeaponActionResult.Accepted, "Empty dark heavy start: " + lastHeavyResult);
            yield return Wait(0.8f);
            Check(DarkBarrageScheduler.CastCount == casts && DarkBarrageScheduler.ActiveCount == 0, "Energy 0 dark heavy submits nothing");
            while (melee.IsAttackInProgress) yield return null;
            results.Add(new { goal = "D2-empty", ok = true });
        }
        finally { melee?.CancelCurrentAttackState(); if (spawn != null) foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e); if (ui != null && ui.InArena) ui.ToggleArena(); }
    }
}
