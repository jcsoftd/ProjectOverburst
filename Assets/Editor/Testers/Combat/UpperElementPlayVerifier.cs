using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// Local-only 60D verifier: light radiance/overcharge + triple/double heavy, dark magnet + gather burst.
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

    public static void Run(string output)
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Already playing");
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
        EnemySpawnService spawn = null; EnemyThemeDebugUI ui = null; MeleeRuntime melee = null;
        var leased = new List<EnemyActor>();
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            Check(Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase),
                "Account isolation: " + Overburst.Persistence.AccountBootstrap.SaveDirectory);
            var player = PlayerInputFacade.Current; var actor = PlayerContext.GetOrCreate().CurrentActor;
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            actor.Health.SetMaxHp(1000000, true);
            ui = UnityEngine.Object.FindFirstObjectByType<EnemyThemeDebugUI>(FindObjectsInactive.Include); ui.gameObject.SetActive(true); if (!ui.InArena) ui.ToggleArena();
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
            Check(decayed > 2f && decayed < 4f, "Continuous decay ~3/s above 100: " + decayDiag);
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
            float commit = -1, started = Time.time;
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

            // ---------- D1: dark corrosion magnet ----------
            var dark = Equip(WeaponElement.Dark);
            yield return null;
            Vector3 p0 = origin + Vector3.forward * 8f;
            var group = new List<EnemyActor>();
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI * 2f / 6f;
                group.Add(Spawn(small, p0 + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 2.5f));
            }
            yield return null;
            foreach (var e in group)
            {
                var s = e.GetComponent<ElementalStatusController>();
                for (int k = 0; k < 5; k++) s.TryApplyDirectHit(new ElementalStatusApplication(WeaponElement.Dark, 10, player.gameObject, dark.WeaponInstanceId, true, false, e.transform.position, Vector3.forward));
                Check(s.GetStackCount(WeaponElement.Dark) == 5, "Dark 5 stacks");
            }
            float spreadBefore = MeanPairDistance(group);
            int ticks = DarkMagnetismSystem.TickCount;
            yield return Wait(1.5f);
            float spreadAfter = MeanPairDistance(group);
            {
                var m = group[0].Movement; var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                object F(string n) => typeof(EnemyMovement).GetField(n, bf)?.GetValue(m);
                var reaction = group[0].GetComponent<EnemyMovementReaction>();
                var loco = group[0].GetComponent<EnemyLocomotionAnimator>();
                var walk = RunWalkableContext.Current;
                results.Add(new { goal = "D1-diag", enabled = m.enabled, actionLocked = m.IsActionLocked, statusLocked = m.IsStatusMovementLocked,
                    allows = loco != null ? loco.AllowsMovement(EnemyLocomotionMode.Idle) : true, hitStun = reaction != null && reaction.IsHitStunActive,
                    knockback = reaction != null && reaction.IsKnockbackActive, magnetVelocity = F("statusMagnetVelocity")?.ToString(),
                    magnetUntil = F("statusMagnetUntil"), now = Time.time, pending = F("pendingAreaDisplacement")?.ToString(),
                    walkable = walk == null ? "none" : walk.IsWalkable(group[0].transform.position).ToString(), speed = m.ActiveMoveSpeed });
            }
            Check(DarkMagnetismSystem.ParticipantCount >= 6 && DarkMagnetismSystem.TickCount > ticks + 10, "Magnet ticking at ~10 Hz");
            Check(spreadAfter < spreadBefore - 0.1f, "Corroded enemies clump: " + spreadBefore + " -> " + spreadAfter + " moving=" + DarkMagnetismSystem.LastMovingCount + " body=" + (group[0].GetComponent<EnemyCrowdAgent>() != null ? group[0].GetComponent<EnemyCrowdAgent>().BodyRadius : -1f));
            var auraController = group[0].GetComponent<MeleeElementStatusAuraController>();
            Check(auraController != null && auraController.IsAuraActive(MeleeElementStatusAuraType.Corroded), "Corrosion aura active");
            results.Add(new { goal = "D1", spreadBefore, spreadAfter, participants = DarkMagnetismSystem.ParticipantCount, lastMoving = DarkMagnetismSystem.LastMovingCount });
            ReleaseAll(); yield return null;

            // ---------- D2: dark gather burst (slam R=4 at full energy, gather 10 m) ----------
            Warp(player, origin, Vector3.forward); yield return null;
            Vector3 impact = origin + Vector3.forward * 1.7f; // greatsword heavy forward offset is data-driven; centre enemy sits on it
            var target = Spawn(medium, impact);
            var far = new List<EnemyActor>();
            for (int i = 0; i < 3; i++) far.Add(Spawn(small, impact + Quaternion.Euler(0, -60 + i * 60, 0) * Vector3.forward * 7f));
            yield return null;
            foreach (var e in far.Append(target))
            {
                var s = e.GetComponent<ElementalStatusController>();
                for (int k = 0; k < 5; k++) s.TryApplyDirectHit(new ElementalStatusApplication(WeaponElement.Dark, 10, player.gameObject, dark.WeaponInstanceId, true, false, e.transform.position, Vector3.forward));
            }
            dark.Clear(); for (int i = 0; i < 10; i++) dark.RecordConfirmedHit(dark.WeaponInstanceId, dark.Element, ++seq, 1);
            Check(Mathf.Approximately(dark.Amount, 100f), "Dark full energy");
            int bursts = DarkGatherBurstScheduler.BurstCount;
            var darkStats = TransientVfxPool.GetStatistics(heavyDef.elementVfx.darkGatherBurst);
            yield return StartHeavy(melee); Check(lastHeavyResult == WeaponActionResult.Accepted, "Dark heavy start: " + lastHeavyResult);
            commit = -1; started = Time.time;
            while (Time.time < started + 3f) { if (dark.Amount == 0) { commit = Time.time; break; } yield return null; }
            Check(commit >= 0, "Dark commit");
            yield return null;
            int stacksAfterSlam = target.GetComponent<ElementalStatusController>().GetStackCount(WeaponElement.Dark);
            Check(stacksAfterSlam == 5, "Slam keeps corrosion: " + stacksAfterSlam);
            melee.CancelCurrentAttackState(); // the scheduler must finish anyway
            float farBefore = far.Average(e => UpperElementCombatUtility.PlanarDistance(e.transform.position, impact));
            while (Time.time < commit + 1.5f) yield return null;
            float farAfter = far.Average(e => UpperElementCombatUtility.PlanarDistance(e.transform.position, impact));
            Check(farAfter < 4f, "Gathered into slam radius: " + farBefore + " -> " + farAfter);
            while (DarkGatherBurstScheduler.BurstCount == bursts && Time.time < commit + 3f) yield return null;
            float burstAt = Time.time - commit;
            Check(DarkGatherBurstScheduler.BurstCount == bursts + 1, "Burst after cancel");
            Check(Mathf.Abs(burstAt - 1.95f) < 0.15f, "Burst at ~1.95 s: " + burstAt);
            Check(DarkGatherBurstScheduler.LastBurstStackSum == 20 && DarkGatherBurstScheduler.LastBurstTargetCount == 4,
                "Burst counts 4 targets x5: " + DarkGatherBurstScheduler.LastBurstStackSum + "/" + DarkGatherBurstScheduler.LastBurstTargetCount);
            Check(far.All(e => e.GetComponent<ElementalStatusController>().GetStackCount(WeaponElement.Dark) == 0), "Burst consumes corrosion");
            Check(TransientVfxPool.GetStatistics(heavyDef.elementVfx.darkGatherBurst).Requests == darkStats.Requests + 1, "Dark VFX spawned once");
            results.Add(new { goal = "D2", farBefore, farAfter, burstAt, stackSum = DarkGatherBurstScheduler.LastBurstStackSum, burstDamage = DarkGatherBurstScheduler.LastBurstDamage, pulled = DarkGatherBurstScheduler.LastPulledCount });
            ReleaseAll(); yield return null;

            // ---------- D2: empty dark heavy has no gather ----------
            dark.Clear(); int activeBefore = DarkGatherBurstScheduler.ActiveCount;
            yield return StartHeavy(melee); Check(lastHeavyResult == WeaponActionResult.Accepted, "Empty dark heavy start: " + lastHeavyResult);
            yield return Wait(0.8f);
            Check(DarkGatherBurstScheduler.ActiveCount == activeBefore, "Energy 0 dark heavy submits nothing");
            while (melee.IsAttackInProgress) yield return null;
            results.Add(new { goal = "D2-empty", ok = true });

            // ---------- D1-walk: magnet must also move enemies that are walking to a destination ----------
            Warp(player, origin, Vector3.forward); yield return null;
            var lane = new List<EnemyActor>();
            for (int i = 0; i < 6; i++)
            {
                var e = Spawn(small, origin + new Vector3(-3.25f + i * 1.3f, 0f, 9f));
                lane.Add(e);
            }
            yield return null;
            foreach (var e in lane)
            {
                var s = e.GetComponent<ElementalStatusController>();
                for (int k = 0; k < 5; k++) s.TryApplyDirectHit(new ElementalStatusApplication(WeaponElement.Dark, 10, player.gameObject, dark.WeaponInstanceId, true, false, e.transform.position, Vector3.forward));
                e.Movement.SetDestination(e.transform.position + Vector3.back * 12f, 0.1f);
            }
            float Lateral(List<EnemyActor> list) { float mx = list.Average(e => e.transform.position.x); return list.Average(e => Mathf.Abs(e.transform.position.x - mx)); }
            float lateralBefore = Lateral(lane), zBefore = lane.Average(e => e.transform.position.z);
            yield return Wait(1.5f);
            float lateralAfter = Lateral(lane), walked = zBefore - lane.Average(e => e.transform.position.z);
            results.Add(new { goal = "D1-walk", lateralBefore, lateralAfter, walked, moving = DarkMagnetismSystem.LastMovingCount });
            var walkFailures = new List<string>();
            if (walked <= 1f) walkFailures.Add("Lane enemies did not walk: " + walked);
            if (lateralAfter >= lateralBefore - 0.15f) walkFailures.Add("Walking corroded enemies did not clump: " + lateralBefore + " -> " + lateralAfter);
            ReleaseAll(); yield return null;

            // ---------- D2-walk: gather must pull enemies that are walking to a destination ----------
            Warp(player, origin, Vector3.forward); yield return null;
            var walkCentre = Spawn(medium, impact);
            var walkers = new List<EnemyActor>();
            for (int i = 0; i < 3; i++)
            {
                Vector3 radial = Quaternion.Euler(0, -60 + i * 60, 0) * Vector3.forward;
                var e = Spawn(small, impact + radial * 7f);
                walkers.Add(e);
            }
            yield return null;
            foreach (var e in walkers)
            {
                Vector3 radial = e.transform.position - impact; radial.y = 0f; radial.Normalize();
                e.Movement.SetDestination(e.transform.position + Vector3.Cross(Vector3.up, radial) * 6f, 0.1f);
            }
            // An idle enemy near the gather edge (2.5R = 10 m at full energy) must still reach the slam circle.
            EnemyActor edge = null;
            if (spawn.TrySpawn(new EnemySpawnRequest(small, impact + Vector3.forward * 9.3f, Quaternion.LookRotation(Vector3.back), player.transform), out var edgeActor))
            {
                leased.Add(edgeActor); edgeActor.AI.enabled = false; edgeActor.Movement.StopMovement(); edgeActor.Health.SetMaxHp(1000000, true);
                edgeActor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; edge = edgeActor;
            }
            dark.Clear(); for (int i = 0; i < 10; i++) dark.RecordConfirmedHit(dark.WeaponInstanceId, dark.Element, ++seq, 1);
            yield return StartHeavy(melee); Check(lastHeavyResult == WeaponActionResult.Accepted, "Walk dark heavy start: " + lastHeavyResult);
            commit = -1; started = Time.time;
            while (Time.time < started + 3f) { if (dark.Amount == 0) { commit = Time.time; break; } yield return null; }
            Check(commit >= 0, "Walk dark commit");
            float walkBefore = walkers.Average(e => UpperElementCombatUtility.PlanarDistance(e.transform.position, impact));
            float edgeBefore = edge != null ? UpperElementCombatUtility.PlanarDistance(edge.transform.position, impact) : -1f;
            while (Time.time < commit + 1.5f) yield return null;
            float walkAfter = walkers.Average(e => UpperElementCombatUtility.PlanarDistance(e.transform.position, impact));
            float edgeAfter = edge != null ? UpperElementCombatUtility.PlanarDistance(edge.transform.position, impact) : -1f;
            results.Add(new { goal = "D2-walk", walkBefore, walkAfter, edgeBefore, edgeAfter, pulled = DarkGatherBurstScheduler.LastPulledCount });
            if (walkAfter >= 4f) walkFailures.Add("Walking enemies not gathered: " + walkBefore + " -> " + walkAfter);
            if (edge != null && edgeAfter >= 4f) walkFailures.Add("Edge enemy not gathered: " + edgeBefore + " -> " + edgeAfter);
            while (DarkGatherBurstScheduler.ActiveCount > 0 && Time.time < commit + 3f) yield return null;
            while (melee.IsAttackInProgress) yield return null;
            ReleaseAll(); yield return null;
            Check(walkFailures.Count == 0, string.Join(" | ", walkFailures));
        }
        finally { melee?.CancelCurrentAttackState(); if (spawn != null) foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e); if (ui != null && ui.InArena) ui.ToggleArena(); }
    }
}
