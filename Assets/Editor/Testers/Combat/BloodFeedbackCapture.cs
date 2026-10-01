using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// Local-only capture for the blood feedback pass: kill gush, spit projectiles, player wounds and
// lightning-hop hits, in real Play Mode on an isolated account. Nothing is saved; exits Play itself.
[InitializeOnLoad]
public static class BloodFeedbackCapture
{
    const string Key = "BloodFeedbackCapture";
    static IEnumerator work;
    static int frame;
    static double deadline;
    static readonly List<string> shots = new List<string>();
    static readonly List<string> notes = new List<string>();
    static readonly List<string> errors = new List<string>();
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static BloodFeedbackCapture() { EditorApplication.playModeStateChanged += State; }

    public static void Run(string output, string only = "")
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already playing");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene") throw new InvalidOperationException("Persistent scene required");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output);
        SessionState.SetString(Key + ".only", only ?? "");
        SessionState.EraseString(Key + ".env");
        IsolatedSavePlayGuard.PrepareIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
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
            Application.runInBackground = true;
            shots.Clear(); notes.Clear(); errors.Clear(); stack.Clear(); frame = -1;
            deadline = EditorApplication.timeSinceStartup + 420;
            work = Capture(SessionState.GetString(Key + ".only", ""));
            Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            (work as IDisposable)?.Dispose(); work = null;
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", null); SessionState.EraseString(Key + ".env");
            SessionState.SetBool(Key, false);
        }
    }

    static void Log(string m, string s, LogType t) { if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert) errors.Add(m + "\n" + s); }

    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try { if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timeout"); if (Step()) return; Finish("COMPLETE"); }
        catch (Exception e) { Finish("FAIL " + e); }
    }

    static bool Step()
    {
        if (stack.Count == 0 && work != null) { stack.Push(work); work = null; }
        while (stack.Count > 0)
        {
            IEnumerator top = stack.Peek();
            if (top.MoveNext()) { if (top.Current is IEnumerator nested) { stack.Push(nested); continue; } return true; }
            stack.Pop();
        }
        return false;
    }

    static void Finish(string status)
    {
        SessionState.SetString(Key + ".status", status);
        File.WriteAllText(Path.Combine(Output, "capture-results.json"), JsonConvert.SerializeObject(new { status, shots, notes, errors }, Formatting.Indented));
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }

    static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }
    static void Shot(string name) { string path = Path.Combine(Output, name + ".png"); ScreenCapture.CaptureScreenshot(path); shots.Add(path); }
    static void Warp(PlayerInputFacade player, Vector3 p, Vector3 facing)
    {
        var cc = player.GetComponent<CharacterController>(); bool on = cc != null && cc.enabled;
        if (on) cc.enabled = false; player.transform.position = p; player.transform.rotation = Quaternion.LookRotation(facing);
        if (on) cc.enabled = true; Physics.SyncTransforms();
    }
    static string BloodStats()
    {
        var blood = UnityEngine.Object.FindFirstObjectByType<BloodHitVfxService>();
        var decal = UnityEngine.Object.FindFirstObjectByType<BloodGroundDecalService>();
        return blood == null ? "blood service missing" : "blood req=" + blood.RequestedCount + " played=" + blood.PlayedCount + " dropped=" + blood.DroppedCount
            + " offscreen=" + blood.OffscreenCount + " peak=" + blood.PeakActiveCount + (decal != null ? " | decals req=" + decal.RequestedCount + " shown=" + decal.ShownCount
            + " spatial=" + decal.SkippedSpatialCount + " noGround=" + decal.SkippedNoGroundCount + " late=" + decal.SkippedLateCount + " pool=" + decal.SkippedPoolCount + " queue=" + decal.SkippedQueueCount : "");
    }

    static IEnumerator Capture(string only)
    {
        EnemySpawnService spawn = null; EnemyThemeTrialHarness ui = null; MeleeRuntime melee = null;
        var leased = new List<EnemyActor>();
        void ReleaseAll() { if (spawn != null) foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e); leased.Clear(); }
        bool Want(string part) => string.IsNullOrEmpty(only) || only.Contains(part);
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            if (!Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase))
                throw new Exception("Account isolation: " + Overburst.Persistence.AccountBootstrap.SaveDirectory);
            var player = PlayerInputFacade.Current; var actor = PlayerContext.GetOrCreate().CurrentActor;
            actor.Health.SetMaxHp(1000000, true);
            ui = EnemyThemeTrialHarness.Current; if (!ui.InArena) ui.ToggleArena();
            yield return Wait(0.8f);
            if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn)) throw new Exception("Spawn service");
            foreach (var t in ui.tables) spawn.RegisterAdditionalCatalog(t.Catalog, out _);
            var defs = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).Where(d => d != null).Distinct().ToArray();
            EnemyDefinition Def(string id) => defs.FirstOrDefault(d => d.EnemyId == id) ?? defs.First(d => d.EnemyId.Contains(id)); // exact first: "Rostrokarck" also matches "RostrokarckLarvae"
            melee = player.GetComponent<MeleeRuntime>();
            var cam = Camera.main; var origin = player.transform.position;
            var plane = new Plane(Vector3.up, origin);
            Vector3 Ground(float vx, float vy) { var ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0f)); return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : origin; }
            Vector3 up = Ground(.5f, .62f) - Ground(.5f, .5f); up.y = 0f; up.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, up);
            Vector3 spot = origin + right * 7f; // clear floor away from the arena props
            EnemyActor Spawn(EnemyDefinition d, Vector3 at, Vector3 face, bool ai = false, float hp = 1000000)
            {
                if (!spawn.TrySpawn(new EnemySpawnRequest(d, at, Quaternion.LookRotation(face), player.transform), out var e)) throw new Exception("Spawn " + d.EnemyId);
                leased.Add(e); e.AI.enabled = ai; if (!ai) e.Movement.StopMovement(); e.Health.SetMaxHp(hp, true);
                e.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                return e;
            }
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");

            // ---- 1) Kill gush: three bloody themes killed by one blow each (no melee hub, so only the death gush shows) ----
            if (Want("death"))
            {
                Warp(player, spot - up * 2.2f, up); yield return Wait(0.4f);
                var victims = new[] { Spawn(Def("SpiderBrood_Scolokarck"), spot + right * -2.4f, -up, false, 10), Spawn(Def("CavernMutants_Ceratoferox"), spot, -up, false, 10), Spawn(Def("VenomBrood_Arathrox"), spot + right * 2.4f, -up, false, 10) };
                yield return Wait(0.8f); Shot("01_death_before");
                foreach (var v in victims)
                {
                    Vector3 dir = (v.transform.position - player.transform.position); dir.y = 0f; dir.Normalize();
                    v.Health.TakeDamage(new DamageInfo(100f, v.transform.position + Vector3.up, player.gameObject, dir, playerAttackKind: PlayerAttackKind.Weak));
                }
                float t0 = Time.time;
                foreach (var mark in new[] { .08f, .25f, .5f, 1f, 2.6f })
                {
                    while (Time.time < t0 + mark) yield return null;
                    Shot("02_death_" + mark.ToString("F2"));
                }
                notes.Add("death: " + BloodStats());
                yield return Wait(0.3f); ReleaseAll(); yield return Wait(0.5f);
            }

            // ---- 2) Spit projectiles: each projectile monster shoots once at the player from the side ----
            if (Want("proj"))
            {
                var shooters = new[] { "VenomBrood_Arathrox", "SpiderBrood_Scolokarck_Tint3", "PrimalHunt_Venosaur_Tint_Brown", "CavernMutants_Limadon",
                    "VenomBrood_Venodonte_Tint3", "VenomBrood_Kupolobrach_Tint_Orange", "SpiderBrood_Rostrokarck", "VenomBrood_Kupolojuve_Tint_Orange", "DeathHarvest_Reaper" };
                int index = 0;
                foreach (var id in shooters)
                {
                    index++;
                    Warp(player, spot - right * 3f, right); yield return Wait(0.3f);
                    Vector3 at = player.transform.position + right * 6f + up * .4f;
                    var def = Def(id);
                    var e = Spawn(def, at, -right);
                    var executor = e.GetComponent<EnemyThemeSpecialExecutor>();
                    EnemyAbilityDefinition ability = null; int abilityIndex = -1;
                    for (int i = 0; i < def.AbilitySet.Count; i++) { var a = def.AbilitySet.GetAbility(i); if (a != null && a.ExecutionMode == EnemyAbilityExecutionMode.Projectile) { ability = a; abilityIndex = i; break; } }
                    yield return Wait(0.6f);
                    bool started = false; float until = Time.time + 4f;
                    while (!started && Time.time < until)
                    {
                        e.Movement.StopMovement();
                        started = executor != null && executor.TryStart(ability, abilityIndex, player.transform);
                        if (!started) yield return null;
                    }
                    if (!started && executor == null) { notes.Add("proj " + id + ": no EnemyThemeSpecialExecutor on the actor"); ReleaseAll(); continue; }
                    if (!started)
                    {
                        // Which start gate refused: facing, line, locks or the range window.
                        Vector3 aim = e.AbilityController.ResolveAimPosition(player.transform);
                        float dist = Vector3.Distance(new Vector3(aim.x, e.transform.position.y, aim.z), e.transform.position);
                        string blocker = "none";
                        Vector3 o = e.transform.position + Vector3.up * .8f, dl = aim + Vector3.up * .8f - o;
                        if (Physics.SphereCast(o, .14f, dl.normalized, out var bh, dl.magnitude, ~((1 << LayerMask.NameToLayer("Enemy")) | (1 << LayerMask.NameToLayer("Ignore Raycast"))), QueryTriggerInteraction.Ignore))
                            blocker = bh.collider.name + "@" + LayerMask.LayerToName(bh.collider.gameObject.layer) + " d=" + bh.distance.ToString("F2");
                        notes.Add("proj " + id + ": could not start | dist=" + dist.ToString("F2") + " range=" + ability.MinimumRange + ".." + ability.Range
                            + " facing=" + e.Movement.IsFacingForAttack(aim) + " line=" + executor.HasPositioningLine(player.transform, e.transform.position, aim) + " blocker=" + blocker
                            + " locked=" + e.Movement.IsActionLocked + " animBlock=" + e.AnimationBridge.BlocksAttackStart);
                        ReleaseAll(); continue;
                    }
                    until = Time.time + 5f; int launches = executor.LaunchCount;
                    while (executor.LaunchCount == launches && Time.time < until) yield return null;
                    if (executor.LaunchCount == launches) { notes.Add("proj " + id + ": no launch"); ReleaseAll(); continue; }
                    float launchAt = Time.time; string tag = index.ToString("D2") + "_" + id.Split('_')[1];
                    float launchNt = -1f; e.AnimationBridge.TryGetAttackNormalizedTime(ability.AnimatorTrigger, out launchNt);
                    Shot("03a_proj_" + tag + "_launch"); yield return null;
                    while (Time.time < launchAt + .22f && executor.HasProjectile) yield return null;
                    Shot("03_proj_" + tag + "_flight");
                    while (executor.HasProjectile && Time.time < launchAt + 2f) yield return null;
                    float impactAt = Time.time;
                    while (Time.time < impactAt + .1f) yield return null; Shot("04_proj_" + tag + "_impact");
                    while (Time.time < impactAt + .55f) yield return null; Shot("05_proj_" + tag + "_after");
                    yield return null; yield return null; // the screenshot is written at the end of the frame
                    notes.Add("proj " + id + ": launched at anim " + launchNt.ToString("F2") + " (hit " + ability.HitNormalizedTime.ToString("F2") + "), flight " + (impactAt - launchAt).ToString("F2") + "s impacts=" + executor.ImpactCount + " | " + BloodStats());
                    ReleaseAll(); yield return Wait(0.4f);
                }
            }

            // ---- 2b) Melee hit frames: AI attacks the idle player; a shot is taken on the frame damage lands ----
            if (Want("melee"))
            {
                var sample = new[] { "DeathHarvest_DeathKnight", "DeathHarvest_Reaper", "DeathHarvest_BoneAsh", "CavernMutants_Cephalonops", "SpiderBrood_Carcinoptera", "PrimalHunt_Venosaur_Tint_Brown", "CavernMutants_Ursacetus", "SpiderBrood_Horridomorph" };
                int mIndex = 0;
                foreach (var id in sample)
                {
                    mIndex++;
                    Warp(player, spot, right); yield return Wait(0.3f);
                    var def = Def(id);
                    var e = Spawn(def, player.transform.position + right * 2.6f, -right, ai: true);
                    int taken = 0; var seen = new List<string>();
                    void OnHit(CombatHealth h, DamageInfo info)
                    {
                        if (info.enemyAbility == null || info.source == null || info.source.GetComponentInParent<EnemyActor>() != e) return;
                        float nt = -1f; e.AnimationBridge.TryGetAttackNormalizedTime(info.enemyAbility.AnimatorTrigger, out nt);
                        string hits = string.Join("/", Enumerable.Range(0, info.enemyAbility.HitCount).Select(k => info.enemyAbility.GetHitNormalizedTime(k).ToString("F2")));
                        seen.Add(info.enemyAbility.AbilityId.Replace(def.EnemyId + "_", "") + " anim=" + nt.ToString("F2") + " hit=" + hits);
                        if (taken < 3) { taken++; Shot("02b_melee_" + mIndex.ToString("D2") + "_" + id.Split('_')[1] + "_" + taken); } // written at the end of this (damage) frame
                    }
                    actor.Health.OnDamaged += OnHit;
                    try
                    {
                        float until = Time.time + 9f;
                        while (Time.time < until && taken < 3) yield return null;
                        yield return null; yield return null;
                    }
                    finally { actor.Health.OnDamaged -= OnHit; }
                    notes.Add("melee " + id + ": " + (seen.Count == 0 ? "no hit in 9s" : string.Join(" | ", seen)));
                    ReleaseAll(); yield return Wait(0.4f);
                }
            }

            // ---- 3) Player wounds: melee, strong and projectile shapes on the player ----
            if (Want("player"))
            {
                Warp(player, spot, up); yield return Wait(0.5f);
                var attackerDef = Def("SpiderBrood_Horridomorph");
                var attacker = Spawn(attackerDef, spot + up * 1.6f, -up);
                EnemyAbilityDefinition melee1 = null, strong = null;
                for (int i = 0; i < attackerDef.AbilitySet.Count; i++) { var a = attackerDef.AbilitySet.GetAbility(i); if (a == null) continue; if (a.IsTelegraphedStrongAttack) strong = strong ?? a; else melee1 = melee1 ?? a; }
                yield return Wait(0.5f);
                foreach (var hit in new[] { ("melee", melee1), ("strong", strong) })
                {
                    Vector3 dir = player.transform.position - attacker.transform.position; dir.y = 0f; dir.Normalize();
                    actor.Health.TakeDamage(new DamageInfo(5f, player.transform.position + Vector3.up, attacker.gameObject, dir, enemyAbility: hit.Item2));
                    float t0 = Time.time;
                    while (Time.time < t0 + .1f) yield return null; Shot("06_player_" + hit.Item1 + "_0.10");
                    while (Time.time < t0 + .4f) yield return null; Shot("06_player_" + hit.Item1 + "_0.40");
                    yield return Wait(1.2f);
                }
                Shot("07_player_floor");
                notes.Add("player: " + BloodStats() + " abilities melee=" + (melee1 != null ? melee1.AbilityId : "-") + " strong=" + (strong != null ? strong.AbilityId : "-"));
                ReleaseAll(); yield return Wait(0.5f);
            }

            // ---- 4) Lightning hops: shocked cluster hit by a charged electric heavy ----
            if (Want("chain"))
            {
                melee.CancelCurrentAttackState();
                if (!actor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common, element: WeaponElement.Electric))) throw new Exception("Equip electric");
                PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System); melee.SetManualInputEnabled(true);
                Warp(player, spot, up);
                var energy = player.GetComponent<OverburstElementEnergy>() ?? player.gameObject.AddComponent<OverburstElementEnergy>();
                Vector3 slam = spot + up * 1.7f;
                var cluster = new List<EnemyActor>();
                var small = Def("SpiderBrood_Scolokarck_Tint3");
                for (int i = 0; i < 7; i++)
                {
                    Vector3 at = slam + Quaternion.AngleAxis(i * 51f, Vector3.up) * up * (i == 0 ? 0f : i % 2 == 0 ? 2.2f : 3.6f);
                    cluster.Add(Spawn(small, at, -up));
                }
                yield return Wait(1.5f);
                foreach (var c in cluster)
                {
                    var s = c.GetComponent<ElementalStatusController>();
                    for (int k = 0; k < 4; k++) s.TryApplyDirectHit(new ElementalStatusApplication(WeaponElement.Electric, 10, player.gameObject, energy.WeaponInstanceId, true, false, c.transform.position, Vector3.forward));
                }
                int flinchBefore = cluster.Sum(c => c.GetComponent<EnemyHitResponseCoordinator>()?.FlinchCount ?? 0);
                energy.Clear(); int seq = 95000; for (int i = 0; i < 10; i++) energy.RecordConfirmedHit(energy.WeaponInstanceId, energy.Element, ++seq, 1);
                WeaponActionResult result = WeaponActionResult.RejectedNotReady; float until = Time.time + 2f;
                while (Time.time < until && result != WeaponActionResult.Accepted)
                {
                    PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System); melee.SetManualInputEnabled(true);
                    result = melee.TryStartHeavyAttack(up); if (result != WeaponActionResult.Accepted) yield return null;
                }
                if (result != WeaponActionResult.Accepted) throw new Exception("Heavy " + result);
                float commit = -1f; until = Time.time + 3f;
                while (Time.time < until) { if (energy.Amount <= 0f) { commit = Time.time; break; } yield return null; }
                if (commit < 0f) throw new Exception("No commit");
                foreach (var mark in new[] { .12f, .22f, .32f, .45f, .7f })
                {
                    while (Time.time < commit + mark) yield return null;
                    Shot("08_chain_" + mark.ToString("F2"));
                }
                yield return Wait(0.8f);
                int flinchAfter = cluster.Sum(c => c.GetComponent<EnemyHitResponseCoordinator>()?.FlinchCount ?? 0);
                var electricHit = UnityEngine.Resources.Load<MeleeElementHitVfxCatalog>(MeleeElementHitVfxCatalog.ResourcePath);
                electricHit.TryResolve(WeaponElement.Electric, out var electricPrefab);
                var st = TransientVfxPool.GetStatistics(electricPrefab);
                notes.Add("chain: flinch " + flinchBefore + " -> " + flinchAfter + " electric hit pool requests=" + st.Requests + " misses=" + st.Misses + " | " + BloodStats());
                ReleaseAll();
            }
        }
        finally
        {
            melee?.CancelCurrentAttackState();
            ReleaseAll();
            if (ui != null && ui.InArena) ui.ToggleArena();
        }
    }
}
