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
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static UpperElementPlayVerifier() { EditorApplication.playModeStateChanged += State; }

    public static void Run(string output) => Run(output, false);
    // 2026-10-01: runs only the dark corrosion barrage part (light checks are owned elsewhere).
    public static void RunDarkOnly(string output) => Run(output, true);

    static void Run(string output, bool darkOnly)
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Already playing");
        SessionState.SetBool(Key + ".darkOnly", darkOnly);
        Check(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "PersistentScene", "Persistent scene required");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output);
        SessionState.SetString(Key + ".env", Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY") ?? "");
        Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", Path.Combine(output, "IsolatedAccount"));
        SessionState.SetBool(Key, true);
        SessionState.SetString(Key + ".status", "RUNNING");
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
            results.Clear(); errors.Clear(); frame = -1; deadline = EditorApplication.timeSinceStartup + 300;
            stack.Clear(); work = Verify(); Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            (work as IDisposable)?.Dispose(); work = null;
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
            Application.targetFrameRate = SessionState.GetInt(Key + ".fps", -1);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", SessionState.GetString(Key + ".env", ""));
            SessionState.SetBool(Key, false);
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
        File.WriteAllText(Path.Combine(Output, "play-results.json"), JsonConvert.SerializeObject(new { status, results, errors }, Formatting.Indented));
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }

    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

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
                while (DarkBarrageScheduler.ActiveCount > 0 && Time.time < commit + 6f) yield return null;
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

            // D2: full energy. Target on the slam point 5, three smalls at 7 m with 5/3/1, one at 15 m outside the 10 m search.
            Warp(player, origin, Vector3.forward); yield return null;
            Vector3 impact = origin + Vector3.forward * 1.7f; // greatsword heavy forward offset; the centre enemy sits on it
            var target = Spawn(medium, impact);
            var far = new List<EnemyActor>();
            for (int i = 0; i < 3; i++) far.Add(Spawn(small, impact + Quaternion.Euler(0, -60 + i * 60, 0) * Vector3.forward * 7f));
            var outside = Spawn(small, impact + Vector3.forward * 15f); // the real slam centre sits ~1.7 m ahead of `impact`
            yield return null;
            var marked = new List<EnemyActor> { target, far[0], far[1], far[2] };
            int[] wanted = { 5, 5, 3, 1 };
            for (int i = 0; i < marked.Count; i++) { Corrode(marked[i], wanted[i]); Track(marked[i]); }
            Corrode(outside, 5); Track(outside);
            FillDark(); Check(Mathf.Approximately(dark.Amount, 100f), "Dark full energy");
            int casts = DarkBarrageScheduler.CastCount;
            yield return StartHeavy(melee); Check(lastHeavyResult == WeaponActionResult.Accepted, "Dark heavy start: " + lastHeavyResult);
            commit = -1; started = Time.time;
            while (Time.time < started + 3f) { if (dark.Amount == 0) { commit = Time.time; break; } yield return null; }
            Check(commit >= 0, "Dark commit");
            Check(DarkBarrageScheduler.CastCount == casts + 1, "Barrage submitted");
            Check(Stacks(target) == 5 && Reserved(target) == 5, "Slam keeps corrosion, reserved until the first shot: " + Stacks(target) + "/" + Reserved(target));
            melee.CancelCurrentAttackState(); // the scheduler must finish anyway
            while (DarkBarrageScheduler.ActiveCount > 0 && Time.time < commit + 6f) yield return null;
            Check(DarkBarrageScheduler.ActiveCount == 0, "Barrage finished after cancel");
            Check(DarkBarrageScheduler.LastTargetCount == 4 && DarkBarrageScheduler.LastStackSum == 14
                && DarkBarrageScheduler.LastShotCount == 18 && DarkBarrageScheduler.LastFinisher,
                "4 targets, 14 stacks, 18 shots with finisher: " + DarkBarrageScheduler.LastTargetCount + "/" + DarkBarrageScheduler.LastStackSum + "/" + DarkBarrageScheduler.LastShotCount + "/" + DarkBarrageScheduler.LastFinisher
                + " search=" + DarkBarrageScheduler.LastSearchRadius + " centre=" + DarkBarrageScheduler.LastCenter + " expected=" + impact
                + " dists=" + string.Join(",", marked.Append(outside).Select(e => UpperElementCombatUtility.PlanarDistance(e.transform.position, DarkBarrageScheduler.LastCenter).ToString("F2")))
                + " stacksNow=" + string.Join(",", marked.Append(outside).Select(Stacks)));
            float interval = tuning.SafeDarkBarrageFireInterval;
            Check(Mathf.Abs(DarkBarrageScheduler.LastInterval - interval) < 0.0001f, "Fixed interval " + interval + ": " + DarkBarrageScheduler.LastInterval);
            float stream = DarkBarrageScheduler.LastFinalLaunchTime - DarkBarrageScheduler.LastFirstLaunchTime;
            float lastHit = DarkBarrageScheduler.LastFinalHitTime - commit;
            float rise = tuning.SafeDarkBarrageRiseTime, riseEnd = rise + tuning.SafeDarkBarrageRiseStagger;
            Check(Mathf.Abs(stream - 17 * interval) < 0.06f, "One shot per interval (17 x " + interval + "): " + stream);
            Check(DarkBarrageScheduler.MaxLaunchedInOneFrame <= 2, "Never a burst of shots in one frame: " + DarkBarrageScheduler.MaxLaunchedInOneFrame);
            Check(lastHit <= riseEnd + 17 * interval + tuning.SafeDarkBarrageFlightMax + 0.1f, "Last hit after rise + stream + flight: " + lastHit);
            for (int i = 0; i < marked.Count; i++)
                Check(barrageHits[marked[i]].Count == wanted[i] + 1, "Hits = stacks + finisher [" + i + "]: " + barrageHits[marked[i]].Count);
            Check(barrageHits[outside].Count == 0 && Stacks(outside) == 5 && Reserved(outside) == 0, "Outside the search circle keeps corrosion");
            Check(marked.All(e => Stacks(e) == 0 && Reserved(e) == 0), "First landing shot consumed corrosion");
            float firstHit = barrageHits[target].Min(x => x.time) - commit;
            Check(firstHit >= rise, "Shots gather in the air before the first leaves: " + firstHit);
            var targetDamage = barrageHits[target].Select(x => x.damage).ToList();
            float normal = targetDamage.Min(), big = targetDamage.Max();
            Check(Mathf.Abs(normal - DarkBarrageScheduler.LastShotDamage) < 0.01f && Mathf.Abs(big / normal - 2f) < 0.01f,
                "Shot = H x0.15, finisher = 2 shots: " + normal + "/" + big + "/" + DarkBarrageScheduler.LastShotDamage);
            var launchOrder = DarkBarrageScheduler.LastLaunchTargets.ToList();
            Check(launchOrder.Count == 18 && launchOrder.Take(4).Distinct().Count() == 4 && launchOrder.Skip(14).Distinct().Count() == 4,
                "Cycles over different enemies (first 4 and finisher 4 distinct): " + string.Join(",", launchOrder));
            results.Add(new { goal = "D2", stream, lastHit, firstHit, interval = DarkBarrageScheduler.LastInterval, shot = normal, finisher = big,
                searchRadius = DarkBarrageScheduler.LastSearchRadius, hits = marked.Select(e => barrageHits[e].Count).ToArray(), maxLaunchPerFrame = DarkBarrageScheduler.MaxLaunchedInOneFrame });
            Untrack(); ReleaseAll(); yield return null;

            // D2-retarget: an enemy the slam kills keeps its shots; they curve onto the survivor.
            Warp(player, origin, Vector3.forward); yield return null;
            var victim = Spawn(small, impact + Vector3.right);
            victim.Health.SetMaxHp(1, true);
            bool victimDied = false;
            Action<CombatHealth, DamageInfo> onVictimDead = (h, d) => victimDied = true;
            victim.Health.OnDead += onVictimDead;
            var survivor = Spawn(medium, impact + Vector3.forward * 6f);
            yield return null;
            Corrode(victim, 3); Corrode(survivor, 1); Track(survivor);
            FillDark();
            int retargets = DarkBarrageScheduler.TotalRetargets, fizzles = DarkBarrageScheduler.TotalFizzles;
            yield return HeavyAndFinish("Retarget", false);
            victim.Health.OnDead -= onVictimDead;
            Check(victimDied, "Slam killed the victim");
            Check(DarkBarrageScheduler.LastShotCount == 6, "Victim 3+1 and survivor 1+1 shots: " + DarkBarrageScheduler.LastShotCount);
            Check(barrageHits[survivor].Count == 6, "Victim's shots land on the survivor: " + barrageHits[survivor].Count);
            Check(DarkBarrageScheduler.TotalRetargets >= retargets + 4 && DarkBarrageScheduler.TotalFizzles == fizzles,
                "Retargeted, none fizzled: " + (DarkBarrageScheduler.TotalRetargets - retargets) + "/" + (DarkBarrageScheduler.TotalFizzles - fizzles));
            results.Add(new { goal = "D2-retarget", survivorHits = barrageHits[survivor].Count, retargeted = DarkBarrageScheduler.TotalRetargets - retargets });
            Untrack(); ReleaseAll(); yield return null;

            // D2-half: 50% energy has no finisher and a smaller search circle.
            Warp(player, origin, Vector3.forward); yield return null;
            var near = Spawn(small, impact + Vector3.forward * 5.5f);
            var beyond = Spawn(small, impact + Vector3.forward * 10.5f);
            yield return null;
            Corrode(near, 2); Corrode(beyond, 2);
            dark.Clear(); for (int i = 0; i < 5; i++) dark.RecordConfirmedHit(dark.WeaponInstanceId, dark.Element, ++seq, 1);
            yield return HeavyAndFinish("Half", true);
            Check(!DarkBarrageScheduler.LastFinisher && DarkBarrageScheduler.LastTargetCount == 1 && DarkBarrageScheduler.LastShotCount == 2,
                "Half energy: 1 target, 2 shots, no finisher: " + DarkBarrageScheduler.LastTargetCount + "/" + DarkBarrageScheduler.LastShotCount + "/" + DarkBarrageScheduler.LastFinisher);
            Check(Stacks(beyond) == 2 && Stacks(near) == 0, "Search circle scales with energy: " + DarkBarrageScheduler.LastSearchRadius);
            results.Add(new { goal = "D2-half", searchRadius = DarkBarrageScheduler.LastSearchRadius });
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
