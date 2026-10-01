using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// Local-only: 광휘 발광(HolyAura 변형) 중첩 단계별 모습, 치명타 섬광의 원소별 색을 실제 Play에서 촬영한다. 저장하지 않는다.
[InitializeOnLoad]
public static class RadianceCritCapture
{
    const string Key = "RadianceCritCapture";
    static IEnumerator work; static int frame; static double deadline;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly List<string> shots = new List<string>(), notes = new List<string>(), errors = new List<string>();
    static readonly List<object> records = new List<object>();
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static RadianceCritCapture() { EditorApplication.playModeStateChanged += State; }

    public static void Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already playing");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene") throw new InvalidOperationException("Persistent scene required");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output);
        SessionState.EraseString(Key + ".env");
        IsolatedSavePlayGuard.PrepareIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
        SessionState.SetBool(Key, true); SessionState.SetString(Key + ".status", "RUNNING");
        EditorApplication.EnterPlaymode();
    }
    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            SessionState.SetBool(Key + ".background", Application.runInBackground); Application.runInBackground = true;
            shots.Clear(); notes.Clear(); errors.Clear(); records.Clear(); stack.Clear(); frame = -1;
            deadline = EditorApplication.timeSinceStartup + 300;
            work = Capture();
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
        File.WriteAllText(Path.Combine(Output, "results.json"), JsonConvert.SerializeObject(new { status, screen = new[] { Screen.width, Screen.height }, records, shots, notes, errors,
            critPlayed = CritHitVfxService.PlayedCount, critSkipped = CritHitVfxService.SkippedCount }, Formatting.Indented));
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

    static IEnumerator Capture()
    {
        EnemySpawnService spawn = null; var leased = new List<EnemyActor>();
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            if (!Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase))
                throw new Exception("Account isolation: " + Overburst.Persistence.AccountBootstrap.SaveDirectory);
            var player = PlayerInputFacade.Current; var actor = PlayerContext.GetOrCreate().CurrentActor;
            actor.Health.SetMaxHp(1000000, true);
            var ui = EnemyThemeTrialHarness.Current; if (!ui.InArena) ui.ToggleArena();
            yield return Wait(0.8f);
            var melee = player.GetComponent<MeleeRuntime>();
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            var cam = Camera.main; var origin = player.transform.position; var plane = new Plane(Vector3.up, origin);
            Vector3 Ground(float vx, float vy) { var ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0f)); return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : origin; }
            Vector3 up = Ground(.5f, .62f) - Ground(.5f, .5f); up.y = 0f; up.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, up);
            Vector3 spot = origin + right * 7f;

            // ---- 1) 광휘 발광: 중첩 0 / 20 / 50 / 100, 그리고 0으로 돌아간 뒤 ----
            melee.CancelCurrentAttackState();
            if (!actor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common, element: WeaponElement.Light))) throw new Exception("Equip light");
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
            Warp(player, spot, -up);
            yield return Wait(1.2f);
            var energy = player.GetComponent<OverburstElementEnergy>() ?? player.gameObject.AddComponent<OverburstElementEnergy>();
            var presenter = player.GetComponent<LightRadianceAuraPresenter>();
            if (presenter == null) notes.Add("presenter missing");
            var amount = typeof(OverburstElementEnergy).GetProperty("Amount").GetSetMethod(true);
            var stacksProp = typeof(OverburstElementEnergy).GetProperty("RadianceStacks").GetSetMethod(true);
            foreach (int s in new[] { 0, 20, 50, 100, 0 })
            {
                amount.Invoke(energy, new object[] { s > 0 ? 200f : 0f });
                stacksProp.Invoke(energy, new object[] { s });
                yield return Wait(s == 0 && records.Count > 0 ? 1.6f : 0.9f);
                amount.Invoke(energy, new object[] { s > 0 ? 200f : 0f }); stacksProp.Invoke(energy, new object[] { s });
                int n = records.Count + 1;
                Shot("R" + n + "_stacks" + s);
                var sp = cam.WorldToScreenPoint(player.transform.position + Vector3.up);
                records.Add(new { part = "radiance", stacks = s, element = energy.Element.ToString(), intensity = presenter != null ? presenter.CurrentIntensity : -1f,
                    emitting = presenter != null && presenter.IsEmitting, screen = new[] { sp.x, sp.y } });
                yield return null; yield return null;
            }
            amount.Invoke(energy, new object[] { 0f }); stacksProp.Invoke(energy, new object[] { 0 });

            // ---- 2) 치명타 섬광: 원소별 색 ----
            if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn)) throw new Exception("Spawn service");
            foreach (var table in ui.tables) spawn.RegisterAdditionalCatalog(table.Catalog, out _);
            var dummy = ui.tables.SelectMany(x => x.Entries).Select(x => x.definition).Where(x => x != null).First(x => x.EnemyId == "CavernMutants_Ceratoferox");
            Warp(player, spot, right); yield return Wait(0.4f);
            if (!spawn.TrySpawn(new EnemySpawnRequest(dummy, spot + right * 2.4f, Quaternion.LookRotation(-right), player.transform), out var e)) throw new Exception("Spawn dummy");
            leased.Add(e); e.AI.enabled = false; e.Movement.StopMovement(); e.Health.SetMaxHp(1000000, true);
            yield return Wait(0.8f);
            var body = e.GetComponent<CombatTarget>();
            int ci = 0;
            foreach (var element in new[] { WeaponElement.None, WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light })
            {
                ci++;
                Vector3 point = body.CurrentHurtVolume.Center - right * body.CurrentHurtVolume.Radius * .6f;
                int before = CritHitVfxService.PlayedCount;
                e.Health.TakeDamage(new DamageInfo(1f, point, player.gameObject, right, isCritical: true, element: element, playerAttackKind: PlayerAttackKind.Weak));
                bool played = CritHitVfxService.PlayedCount > before;
                yield return Wait(0.05f); Shot("C" + ci + "_" + element + "_a");
                yield return Wait(0.10f); Shot("C" + ci + "_" + element + "_b");
                var sp = cam.WorldToScreenPoint(point);
                records.Add(new { part = "crit", element = element.ToString(), played, screen = new[] { sp.x, sp.y } });
                yield return Wait(0.9f);
            }
            // 치명타가 아닌 타격은 섬광이 없어야 한다.
            int prev = CritHitVfxService.PlayedCount;
            e.Health.TakeDamage(new DamageInfo(1f, body.CurrentHurtVolume.Center, player.gameObject, right, isCritical: false, element: WeaponElement.Fire, playerAttackKind: PlayerAttackKind.Weak));
            records.Add(new { part = "noncrit", played = CritHitVfxService.PlayedCount > prev });
            yield return Wait(0.3f);
        }
        finally { if (spawn != null) foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e); }
    }
}
