using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// Local-only: 중형·정예 강공 발동 거리 측정(AI 실제 공격 시작 거리)과 패링 빛 위치 비교 촬영.
// 실제 Play(격리 계정). 아무것도 저장하지 않는다.
[InitializeOnLoad]
public static class StrongRangeParryCapture
{
    const string Key = "StrongRangeParryCapture";
    static IEnumerator work; static int frame; static double deadline;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly List<string> shots = new List<string>(), notes = new List<string>(), errors = new List<string>();
    static readonly List<object> attacks = new List<object>(), glints = new List<object>();
    static string Output => SessionState.GetString(Key + ".output", "");
    static string Tag => SessionState.GetString(Key + ".tag", "run");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static StrongRangeParryCapture() { EditorApplication.playModeStateChanged += State; }

    // parts: "range", "glint" 또는 "range|glint"
    public static void Run(string output, string tag, string parts)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already playing");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene") throw new InvalidOperationException("Persistent scene required");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output); SessionState.SetString(Key + ".tag", tag); SessionState.SetString(Key + ".parts", parts ?? "");
        SessionState.EraseString(Key + ".env");
        IsolatedSavePlayGuard.PrepareIsolatedPlay(Path.Combine(output, "IsolatedAccount_" + tag));
        SessionState.SetBool(Key, true); SessionState.SetString(Key + ".status", "RUNNING");
        EditorApplication.EnterPlaymode();
    }

    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            SessionState.SetBool(Key + ".background", Application.runInBackground); Application.runInBackground = true;
            shots.Clear(); notes.Clear(); errors.Clear(); attacks.Clear(); glints.Clear(); stack.Clear(); frame = -1;
            deadline = EditorApplication.timeSinceStartup + 540;
            work = Capture(SessionState.GetString(Key + ".parts", ""));
            Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            (work as IDisposable)?.Dispose(); work = null; Time.timeScale = 1f;
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
        Time.timeScale = 1f;
        SessionState.SetString(Key + ".status", status);
        File.WriteAllText(Path.Combine(Output, "results-" + Tag + ".json"), JsonConvert.SerializeObject(new { status, screen = new[] { Screen.width, Screen.height }, attacks, glints, shots, notes, errors }, Formatting.Indented));
        EditorApplication.update -= Tick; EditorApplication.ExitPlaymode();
    }

    static IEnumerator Wait(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    static void Shot(string name) { string path = Path.Combine(Output, name + ".png"); ScreenCapture.CaptureScreenshot(path); shots.Add(path); }
    static void Warp(PlayerInputFacade player, Vector3 p, Vector3 facing)
    {
        var cc = player.GetComponent<CharacterController>(); bool on = cc != null && cc.enabled;
        if (on) cc.enabled = false; player.transform.position = p; player.transform.rotation = Quaternion.LookRotation(facing);
        if (on) cc.enabled = true; Physics.SyncTransforms();
    }
    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    static float OldStart(EnemyActor e, EnemyAbilityDefinition a) =>
        a.ExecutionMode == EnemyAbilityExecutionMode.Projectile || a.ExecutionMode == EnemyAbilityExecutionMode.DirectTarget
            ? a.Range : a.Range + (EnemyAttackThreatGeometry.ResolveRadius(e, a) - a.HitRadius) * .6f;

    static IEnumerator Capture(string parts)
    {
        EnemySpawnService spawn = null; var leased = new List<EnemyActor>(); var temp = new List<GameObject>();
        void ReleaseAll()
        {
            Time.timeScale = 1f;
            foreach (var g in temp) if (g != null) UnityEngine.Object.Destroy(g); temp.Clear();
            if (spawn != null) foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e); leased.Clear();
        }
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            if (!Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase))
                throw new Exception("Account isolation: " + Overburst.Persistence.AccountBootstrap.SaveDirectory);
            var player = PlayerInputFacade.Current; var actor = PlayerContext.GetOrCreate().CurrentActor;
            actor.Health.SetMaxHp(1000000, true);
            var playerBody = actor.GetComponent<CombatTarget>();
            var ui = EnemyThemeTrialHarness.Current; if (!ui.InArena) ui.ToggleArena();
            yield return Wait(0.8f);
            if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn)) throw new Exception("Spawn service");
            foreach (var t in ui.tables) spawn.RegisterAdditionalCatalog(t.Catalog, out _);
            var defs = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).Where(d => d != null).Distinct().ToArray();
            var cam = Camera.main; var origin = player.transform.position;
            var plane = new Plane(Vector3.up, origin);
            Vector3 Ground(float vx, float vy) { var ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0f)); return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : origin; }
            Vector3 up = Flat(Ground(.5f, .62f) - Ground(.5f, .5f)).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, up);
            Vector3 spot = origin + right * 7f;
            EnemyActor Spawn(string id, Vector3 at, Vector3 face, bool ai)
            {
                var def = defs.FirstOrDefault(d => d.EnemyId == id);
                if (def == null || !spawn.TrySpawn(new EnemySpawnRequest(def, at, Quaternion.LookRotation(face), player.transform), out var e)) { notes.Add("spawn failed " + id); return null; }
                leased.Add(e); e.AI.enabled = ai; if (!ai) e.Movement.StopMovement(); e.Health.SetMaxHp(1000000, true);
                e.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; return e;
            }

            // ---- 1) AI가 실제로 공격을 시작하는 거리 ----
            if (parts.Contains("range"))
            {
                var ids = new[] { "CavernMutants_Gasterobrach", "CavernMutants_Gorhorrid", "CavernMutants_Ursacetus", "DeathHarvest_BoneWarden",
                    "DeathHarvest_DeathKnight", "DeathHarvest_RakeBrute", "DeathHarvest_Reaper", "PrimalHunt_Dimaxillosaurus",
                    "PrimalHunt_Venosaur_Tint_Brown", "SpiderBrood_Rostrokarck", "SpiderBrood_Scolokarck_Tint3", "VenomBrood_Kupolobrach_Tint_Orange" };
                foreach (var id in ids)
                {
                    Warp(player, spot, right); yield return Wait(0.3f);
                    var e = Spawn(id, spot + right * 9f, -right, true);
                    if (e == null) continue;
                    var body = e.GetComponent<CombatTarget>(); var rank = e.GetComponent<EnemyRank>();
                    var reaction = e.GetComponent<EnemyMovementReaction>();
                    string tier = (rank != null ? rank.Rank + "/" + rank.GradeType : "-") + " " + (reaction != null && reaction.HitWeightProfile != null ? reaction.HitWeightProfile.Weight.ToString() : "-");
                    bool prev = false; int strongCount = 0, count = 0; float until = Time.time + 12f;
                    while (Time.time < until && strongCount < 2 && count < 5)
                    {
                        bool exec = e.AbilityController.IsExecuting;
                        if (exec && !prev)
                        {
                            var a = e.AbilityController.LastCommittedAbility;
                            if (a != null)
                            {
                                count++; if (a.IsTelegraphedStrongAttack) strongCount++;
                                float dist = Flat(player.transform.position - e.transform.position).magnitude;
                                float gap = dist - body.CurrentVolume.Radius - (playerBody != null ? playerBody.CurrentVolume.Radius : .4f);
                                attacks.Add(new { enemy = id, tier, ability = a.name.Replace(id + "_", ""), strong = a.IsTelegraphedStrongAttack, mode = a.ExecutionMode.ToString(),
                                    distance = dist, surfaceGap = gap, startRange = EnemyAttackThreatGeometry.ResolveStartRange(e, a), oldStartRange = OldStart(e, a),
                                    hitRadius = EnemyAttackThreatGeometry.ResolveRadius(e, a), bodyRadius = body.CurrentVolume.Radius, t = Time.time - (until - 12f) });
                            }
                        }
                        prev = exec;
                        yield return null;
                    }
                    if (strongCount == 0) notes.Add(id + ": 12초 안에 강공 없음 (공격 " + count + "회)");
                    ReleaseAll(); yield return Wait(0.4f);
                }
            }

            // ---- 2) 패링 빛 위치: 현재 / 머리 바로 위 / 체력바 위 ----
            if (parts.Contains("glint"))
            {
                var attackers = new[] { "CavernMutants_Gasterobrach", "CavernMutants_Ursacetus", "DeathHarvest_BoneWarden" };
                int gi = 0;
                foreach (var id in attackers)
                {
                    gi++;
                    Warp(player, spot, right); yield return Wait(0.3f);
                    // 주변에 소형 몇 마리를 세워 두어 전투 중 가독성을 본다.
                    Spawn("DeathHarvest_BoneMoss", spot + right * 2.2f + up * 1.8f, -right, false);
                    Spawn("DeathHarvest_BoneMoss", spot + right * 2.4f - up * 1.9f, -right, false);
                    var e = Spawn(id, spot + right * 3.2f, -right, true);
                    if (e == null) continue;
                    var body = e.GetComponent<CombatTarget>();
                    ParticleSystem glint = null; float until = Time.time + 20f;
                    while (Time.time < until)
                    {
                        var warning = e.GetComponent<EnemyStrongAttackWarning>();
                        var t = warning != null ? warning.transform.Find("Parry cue glint") : null;
                        var ps = t != null ? t.GetComponent<ParticleSystem>() : null;
                        if (ps != null && ps.particleCount > 0 && ps.time >= .05f) { glint = ps; break; }
                        yield return null;
                    }
                    if (glint == null) { notes.Add("glint " + id + ": 20초 안에 패링 빛 없음"); ReleaseAll(); continue; }
                    Time.timeScale = 0f;
                    yield return Frames(2);
                    var vol = body.CurrentVolume; var hpAnchor = e.transform.Find("Anchors/HpBarAnchor");
                    Vector3 current = glint.transform.position;
                    Vector3 head = new Vector3(vol.Center.x, vol.Center.y + vol.HalfHeight + .35f, vol.Center.z);
                    Vector3 overBar = (hpAnchor != null ? hpAnchor.position : head) + Vector3.up * .55f;
                    var variants = new (string name, Vector3 pos)[] { ("current", current), ("head", head), ("overbar", overBar) };
                    var glintRenderer = glint.GetComponent<ParticleSystemRenderer>();
                    int vi = 0;
                    foreach (var v in variants)
                    {
                        vi++;
                        GameObject clone = null;
                        if (v.name != "current")
                        {
                            glintRenderer.enabled = false;
                            clone = new GameObject("GlintVariant_" + v.name); temp.Add(clone);
                            var cps = clone.AddComponent<ParticleSystem>(); cps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                            var cm = cps.main; var om = glint.main;
                            cm.playOnAwake = false; cm.loop = false; cm.duration = om.duration; cm.startLifetime = om.startLifetime; cm.startSpeed = 0f;
                            cm.startSize = om.startSize; cm.startColor = om.startColor; cm.maxParticles = 1; cm.simulationSpace = ParticleSystemSimulationSpace.Local;
                            var em = cps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
                            var sh = cps.shape; sh.enabled = false;
                            var col = cps.colorOverLifetime; col.enabled = true; col.color = glint.colorOverLifetime.color;
                            var size = cps.sizeOverLifetime; size.enabled = true; size.size = glint.sizeOverLifetime.size;
                            var cr = clone.GetComponent<ParticleSystemRenderer>(); cr.sharedMaterial = glintRenderer.sharedMaterial;
                            cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; cr.receiveShadows = false;
                            clone.transform.position = v.pos;
                            cps.Simulate(glint.time, true, true);
                        }
                        yield return Frames(2);
                        Shot("G" + gi.ToString("D2") + "_" + vi + "_" + v.name);
                        var sp = cam.WorldToScreenPoint(v.pos); var ep = cam.WorldToScreenPoint(vol.Center);
                        glints.Add(new { enemy = id, variant = v.name, index = gi, shot = vi, glintTime = glint.time, pos = new[] { v.pos.x, v.pos.y, v.pos.z },
                            screen = new[] { sp.x, sp.y }, enemyScreen = new[] { ep.x, ep.y }, bodyTop = vol.Center.y + vol.HalfHeight, hpBar = hpAnchor != null ? hpAnchor.position.y : -1f });
                        yield return Frames(2);
                        if (clone != null) { UnityEngine.Object.Destroy(clone); glintRenderer.enabled = true; }
                    }
                    Time.timeScale = 1f;
                    ReleaseAll(); yield return Wait(0.4f);
                }
            }
        }
        finally { ReleaseAll(); }
    }
}
