using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// Local-only: 광휘 후보(HolyAura·EnchantedGround_Holy·Guardian Halo) 비교, 100중첩 머리 위 왕관, 어둠 타격(Demon_Hit 보정)과
// 몬스터 크기별 타격 VFX 크기 통일을 실제 Play(격리 계정)에서 촬영한다. 후보는 실행 중에만 붙이고 저장하지 않는다.
[InitializeOnLoad]
public static class RadianceDarkHitCapture
{
    const string Key = "RadianceDarkHitCapture";
    static IEnumerator work; static int frame; static double deadline;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly List<string> shots = new List<string>(), notes = new List<string>(), errors = new List<string>();
    static readonly List<object> records = new List<object>();
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static RadianceDarkHitCapture() { EditorApplication.playModeStateChanged += State; }

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
        File.WriteAllText(Path.Combine(Output, "results.json"), JsonConvert.SerializeObject(new { status, screen = new[] { Screen.width, Screen.height }, records, shots, notes, errors }, Formatting.Indented));
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
        EnemySpawnService spawn = null; var leased = new List<EnemyActor>(); var temp = new List<GameObject>();
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName != "HideoutScene") yield return null;
            if (!Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase))
                throw new Exception("Account isolation: " + Overburst.Persistence.AccountBootstrap.SaveDirectory);
            var player = PlayerInputFacade.Current; var actor = PlayerContext.GetOrCreate().CurrentActor;
            actor.Health.SetMaxHp(1000000, true);
            var ui = UnityEngine.Object.FindFirstObjectByType<EnemyThemeDebugUI>(FindObjectsInactive.Include); ui.gameObject.SetActive(true); if (!ui.InArena) ui.ToggleArena();
            yield return Wait(0.8f);
            var melee = player.GetComponent<MeleeRuntime>();
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            var cam = Camera.main; var origin = player.transform.position; var plane = new Plane(Vector3.up, origin);
            Vector3 Ground(float vx, float vy) { var ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0f)); return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : origin; }
            Vector3 up = Ground(.5f, .62f) - Ground(.5f, .5f); up.y = 0f; up.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, up);
            Vector3 spot = origin + right * 7f;

            // ---- 1) 광휘 후보 비교(100중첩) ----
            melee.CancelCurrentAttackState();
            if (!actor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common, element: WeaponElement.Light))) throw new Exception("Equip light");
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
            Warp(player, spot, -up);
            yield return Wait(1.2f);
            var energy = player.GetComponent<OverburstElementEnergy>() ?? player.gameObject.AddComponent<OverburstElementEnergy>();
            var presenter = player.GetComponent<LightRadianceAuraPresenter>();
            var amount = typeof(OverburstElementEnergy).GetProperty("Amount").GetSetMethod(true);
            var stacksProp = typeof(OverburstElementEnergy).GetProperty("RadianceStacks").GetSetMethod(true);
            void SetStacks(int s) { amount.Invoke(energy, new object[] { s > 0 ? 200f : 0f }); stacksProp.Invoke(energy, new object[] { s }); }
            string holy = "Assets/ThirdParty/06_VFX/Piloto Studio 1/Elemental VFX Mega Bundle/Holy/";
            var candidates = new (string name, string path, float y)[] { ("current_HolyAura", null, 0f), ("EnchantedGround_Holy", holy + "EnchantedGround_Holy.prefab", 0f), ("Guardian_Halo", holy + "Guardian Halo.prefab", 0f) };
            int ri = 0;
            foreach (var c in candidates)
            {
                ri++;
                GameObject inst = null;
                if (c.path == null) SetStacks(100);
                else
                {
                    SetStacks(0); yield return Wait(2.2f); // 현재 발광이 사라질 때까지
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(c.path);
                    inst = UnityEngine.Object.Instantiate(prefab, player.transform); temp.Add(inst);
                    inst.transform.localPosition = new Vector3(0f, c.y, 0f);
                }
                yield return Wait(1.3f);
                if (c.path == null) SetStacks(100);
                Shot("R" + ri + "_" + c.name);
                var sp = cam.WorldToScreenPoint(player.transform.position + Vector3.up);
                records.Add(new { part = "radiance", candidate = c.name, crown = presenter != null && presenter.IsCrownShown, screen = new[] { sp.x, sp.y } });
                yield return Wait(0.6f);
                Shot("R" + ri + "_" + c.name + "_b");
                if (inst != null) UnityEngine.Object.Destroy(inst);
            }
            // 왕관: 99중첩이면 꺼져야 한다.
            SetStacks(99); yield return Wait(0.5f);
            records.Add(new { part = "crown99", crown = presenter != null && presenter.IsCrownShown });
            SetStacks(0); yield return Wait(0.3f);

            // ---- 2) 어둠 타격과 크기 통일: 소형·대형에 같은 원소 타격 ----
            if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn)) throw new Exception("Spawn service");
            foreach (var table in ui.tables) spawn.RegisterAdditionalCatalog(table.Catalog, out _);
            var defs = ui.tables.SelectMany(x => x.Entries).Select(x => x.definition).Where(x => x != null).ToArray();
            if (!actor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common, element: WeaponElement.Dark))) throw new Exception("Equip dark");
            Warp(player, spot, right); yield return Wait(1.5f);
            var small = defs.First(x => x.EnemyId == "CavernMutants_Ceratoferox");
            var big = defs.First(x => x.EnemyId == "CavernMutants_Ursacetus");
            if (!spawn.TrySpawn(new EnemySpawnRequest(small, spot + right * 2.2f - up * 1.6f, Quaternion.LookRotation(-right), player.transform), out var es)) throw new Exception("spawn small");
            if (!spawn.TrySpawn(new EnemySpawnRequest(big, spot + right * 3.2f + up * 2.4f, Quaternion.LookRotation(-right), player.transform), out var eb)) throw new Exception("spawn big");
            foreach (var e in new[] { es, eb }) { leased.Add(e); e.AI.enabled = false; e.Movement.StopMovement(); e.Health.SetMaxHp(1000000, true); }
            yield return Wait(1.0f);
            int hi = 0;
            foreach (var element in new[] { WeaponElement.Dark, WeaponElement.Fire })
            {
                hi++;
                foreach (var e in new[] { es, eb })
                {
                    var body = e.GetComponent<CombatTarget>();
                    Vector3 p = CombatTargetVfxPlacement.ResolveContact(body, body.CurrentHurtVolume.Center, right, out float oldSize);
                    bool played = MeleeElementHitVfxService.TryPlay(element, p, oldSize);
                    records.Add(new { part = "hit", element = element.ToString(), enemy = e.name, bodyRadius = body.CurrentHurtVolume.Radius, oldSizeMultiplier = oldSize, uniformScale = MeleeElementHitVfxService.UniformHitScale, played });
                }
                foreach (var t in new[] { .08f, .25f, .6f })
                {
                    float until = Time.time + (t - (hi > 0 ? 0f : 0f)); yield return Wait(t == .08f ? .08f : t == .25f ? .17f : .35f);
                    var sp = cam.WorldToScreenPoint((es.transform.position + eb.transform.position) * .5f + Vector3.up);
                    Shot("H" + hi + "_" + element + "_" + t.ToString("F2"));
                    records.Add(new { part = "hitshot", element = element.ToString(), t, screen = new[] { sp.x, sp.y } });
                }
                yield return Wait(1.4f);
            }
        }
        finally
        {
            foreach (var g in temp) if (g != null) UnityEngine.Object.Destroy(g);
            if (spawn != null) foreach (var e in leased) if (e != null && e.IsLeased) spawn.Release(e);
        }
    }
}
