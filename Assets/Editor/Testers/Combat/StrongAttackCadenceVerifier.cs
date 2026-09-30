using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// Local-only 2026-10-01: 몬스터 강공 규칙 확인용 실제 Play(격리 계정). 아무것도 저장하지 않는다.
// 1 cadence: 중형·대형이 AI로 싸울 때 공격 순서 기록 — 첫 공격은 평타, 강공 사이 평타·원거리 중형 3번·대형 2번 이상.
//   평타·원거리 시작 때 바닥 장판이 없고, 강공 때만 장판과 패링 가능 표시가 뜨는지.
// 2 hit: 중형을 약공으로 계속 때리는 동안(경직 중 강공 시작 경로)에도 같은 순서 규칙이 지켜지는지.
// 3 small: 소형은 패링 대상이 되는 순간이 없는지.
// 강공 장판이 뜬 순간과 평타 준비 순간을 촬영한다.
[InitializeOnLoad]
public static class StrongAttackCadenceVerifier
{
    const string Key = "StrongAttackCadenceVerifier";
    static IEnumerator work; static int frame; static double deadline;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly List<string> shots = new List<string>(), notes = new List<string>(), errors = new List<string>(), violations = new List<string>();
    static readonly List<object> sequences = new List<object>();
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static StrongAttackCadenceVerifier() { EditorApplication.playModeStateChanged += State; }

    public static void Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already playing");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene") throw new InvalidOperationException("Persistent scene required");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output);
        SessionState.SetString(Key + ".env", Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY") ?? "");
        Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", Path.Combine(output, "IsolatedAccount"));
        SessionState.SetBool(Key, true); SessionState.SetString(Key + ".status", "RUNNING");
        EditorApplication.EnterPlaymode();
    }

    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            SessionState.SetBool(Key + ".background", Application.runInBackground); Application.runInBackground = true;
            shots.Clear(); notes.Clear(); errors.Clear(); violations.Clear(); sequences.Clear(); stack.Clear(); frame = -1;
            deadline = EditorApplication.timeSinceStartup + 420;
            work = Capture();
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
            Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", SessionState.GetString(Key + ".env", ""));
            SessionState.SetBool(Key, false);
        }
    }
    static void Log(string m, string s, LogType t) { if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert) errors.Add(m + "\n" + s); }
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try { if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timeout"); if (Step()) return; Finish(violations.Count == 0 ? "PASS" : "FAIL"); }
        catch (Exception e) { Finish("ERROR " + e); }
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
        File.WriteAllText(Path.Combine(Output, "results.json"), JsonConvert.SerializeObject(new { status, violations, sequences, shots, notes, errors }, Formatting.Indented));
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
    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    sealed class Entry { public string ability; public bool strong, parryable, warning, parryCue, warningChecked; public float t; }

    static IEnumerator Capture()
    {
        EnemySpawnService spawn = null; var leased = new List<EnemyActor>();
        void ReleaseAll() { if (spawn != null) foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e); leased.Clear(); }
        EnemyThemeTrialHarness ui = null;
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            if (!Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase))
                throw new Exception("Account isolation: " + Overburst.Persistence.AccountBootstrap.SaveDirectory);
            var player = PlayerInputFacade.Current; var actor = PlayerContext.GetOrCreate().CurrentActor;
            actor.Health.SetMaxHp(1000000, true);
            var playerBody = actor.GetComponent<CombatTarget>();
            ui = EnemyThemeTrialHarness.Current; if (!ui.InArena) ui.ToggleArena();
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
            EnemyActor Spawn(string id, Vector3 at, Vector3 face)
            {
                var def = defs.FirstOrDefault(d => d.EnemyId == id);
                if (def == null || !spawn.TrySpawn(new EnemySpawnRequest(def, at, Quaternion.LookRotation(face), player.transform), out var e)) { notes.Add("spawn failed " + id); return null; }
                leased.Add(e); e.AI.enabled = true; e.Health.SetMaxHp(1000000, true);
                e.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; return e;
            }

            // 공격 순서를 기록한다. hitEvery > 0이면 첫 공격이 끝난 뒤부터 그 간격으로 약공 1을 맞힌다(경직 중 강공 시작 경로).
            // stick이면 플레이어를 몬스터 앞 1.2m에 붙여 둔다(원거리형이 근접 거리에서 싸우게).
            IEnumerator Record(string id, string mode, int required, float seconds, int maxAttacks, float hitEvery, bool shoot, bool stick = false)
            {
                Warp(player, spot, right); yield return Wait(0.3f);
                var e = Spawn(id, spot + right * 2.6f, -right);
                if (e == null) yield break;
                var list = new List<Entry>(); bool prev = false; float start = Time.time, nextHit = float.MaxValue;
                bool strongShot = !shoot, plainShot = !shoot; Entry pending = null; float pendingAt = 0f;
                int parryThreatFrames = 0; int seq = 900000;
                while (Time.time < start + seconds && list.Count < maxAttacks)
                {
                    if (stick)
                    {
                        Vector3 gap = Flat(player.transform.position - e.transform.position);
                        if (gap.magnitude > 1.6f) Warp(player, e.transform.position + (gap.sqrMagnitude > .01f ? gap.normalized : e.transform.forward) * 1.2f, -gap);
                    }
                    if (hitEvery > 0f && nextHit == float.MaxValue && list.Count >= 1 && !e.AbilityController.IsExecuting) nextHit = Time.time;
                    if (hitEvery > 0f && Time.time >= nextHit)
                    {
                        nextHit = Time.time + hitEvery;
                        e.Health.TakeDamage(new DamageInfo(1, e.transform.position, player.gameObject, right, 4,
                            sourceAttackSequenceId: ++seq, playerAttackKind: PlayerAttackKind.Weak, sourceAttackPhaseIndex: 0, weakKnockbackDistance: .05f));
                    }
                    var ac = e.AbilityController;
                    bool exec = ac.IsExecuting;
                    if (exec && !prev && ac.LastCommittedAbility != null)
                    {
                        var a = ac.LastCommittedAbility;
                        pending = new Entry { ability = a.name.Replace(id + "_", ""), strong = a.IsTelegraphedStrongAttack, parryable = a.IsParryable, t = Time.time - start };
                        pendingAt = Time.time; list.Add(pending);
                    }
                    if (pending != null && Time.time >= pendingAt + .25f)
                    {
                        var w = e.GetComponent<EnemyStrongAttackWarning>();
                        pending.warning = w != null && w.IsVisible; pending.warningChecked = true;
                        if (!strongShot && pending.strong && pending.warning) { strongShot = true; Shot(id + "_strong"); }
                        else if (!plainShot && !pending.strong) { plainShot = true; Shot(id + "_plain"); }
                        pending = null;
                    }
                    if (exec && playerBody != null && ac.IsParryThreatTo(playerBody))
                    {
                        parryThreatFrames++;
                        if (list.Count > 0) list[list.Count - 1].parryCue = true;
                    }
                    prev = exec;
                    yield return null;
                }
                // 규칙 검사
                int sinceStrong = 0; bool strongUsed = false; int strongCount = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    var x = list[i];
                    if (x.strong)
                    {
                        int need = strongUsed ? required : 1;
                        if (sinceStrong < need) violations.Add($"{id}/{mode}: {i + 1}번째 공격 강공 — 앞선 비강공 {sinceStrong}회(필요 {need})");
                        strongUsed = true; sinceStrong = 0; strongCount++;
                    }
                    else sinceStrong++;
                    if (x.warningChecked && x.strong != x.warning) violations.Add($"{id}/{mode}: {x.ability} 장판 {(x.warning ? "있음" : "없음")}(강공={x.strong})");
                    if (x.parryable && !x.strong) violations.Add($"{id}/{mode}: 비강공 {x.ability} 패링 가능");
                    if (x.parryCue && !x.parryable) violations.Add($"{id}/{mode}: {x.ability} 패링 판정 열림");
                }
                if (mode == "small" && parryThreatFrames > 0) violations.Add($"{id}: 소형 패링 판정 {parryThreatFrames}프레임");
                if (mode == "cadence" && strongCount == 0) notes.Add($"{id}: {seconds}초 안에 강공 없음(공격 {list.Count}회)");
                sequences.Add(new { enemy = id, mode, required, attacks = list.Count, strongCount, parryThreatFrames,
                    order = string.Join(" ", list.Select(x => (x.strong ? "강" : "평") + (x.warning ? "*" : "") + (x.warningChecked ? "" : "?"))), detail = list });
                ReleaseAll(); yield return Wait(0.4f);
            }

            var medium = new[] { "CavernMutants_Gasterobrach", "SpiderBrood_Scolokarck_Tint3", "VenomBrood_Kupolojuve_Tint_Orange", "PrimalHunt_Venosaur_Tint_Brown" };
            var large = new[] { "CavernMutants_Ursacetus", "PrimalHunt_Occisodonte", "SpiderBrood_Rostrokarck" };
            foreach (var id in medium) yield return Record(id, "cadence", EnemyAbilityController.MediumAttacksBetweenStrong, 16f, 9, 0f, id == "CavernMutants_Gasterobrach");
            yield return Record("VenomBrood_Arathrox", "cadence", EnemyAbilityController.MediumAttacksBetweenStrong, 18f, 9, 0f, true, true);
            foreach (var id in large) yield return Record(id, "cadence", EnemyAbilityController.LargeAttacksBetweenStrong, 14f, 7, 0f, id == "CavernMutants_Ursacetus");
            yield return Record("CavernMutants_Gasterobrach", "hit", EnemyAbilityController.MediumAttacksBetweenStrong, 10f, 8, .3f, false);
            yield return Record("VenomBrood_Arathrox", "hit", EnemyAbilityController.MediumAttacksBetweenStrong, 10f, 8, .3f, false, true);
            yield return Record("CavernMutants_Ceratoferox", "small", 99, 8f, 6, 0f, false);
            yield return Record("SpiderBrood_Horridomorph", "small", 99, 8f, 6, 0f, false);
        }
        finally { ReleaseAll(); if (ui != null && ui.InArena) ui.ToggleArena(); }
    }
}
