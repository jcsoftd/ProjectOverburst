using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// Local-only: 광휘 오라가 무기 교체 뒤에 남는지, 다시 켰을 때 바닥 장판이 겹쳐 쌓이는지 실제 Play에서 확인한다. 저장하지 않는다.
// 빛 무기(광휘 50) → 불 무기로 교체 → 0.5초 / 3초 뒤 → 빛 무기로 되돌려 광휘 50 → 다시 교체 → 다시 빛. 매 단계 촬영과 입자 수 기록.
[InitializeOnLoad]
public static class RadianceSwapCapture
{
    const string Key = "RadianceSwapCapture";
    static IEnumerator work; static int frame; static double deadline;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly List<string> shots = new List<string>(), notes = new List<string>(), errors = new List<string>();
    static readonly List<object> records = new List<object>();
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static RadianceSwapCapture() { EditorApplication.playModeStateChanged += State; }

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
    static void Warp(PlayerInputFacade player, Vector3 p, Vector3 facing)
    {
        var cc = player.GetComponent<CharacterController>(); bool on = cc != null && cc.enabled;
        if (on) cc.enabled = false; player.transform.position = p; player.transform.rotation = Quaternion.LookRotation(facing);
        if (on) cc.enabled = true; Physics.SyncTransforms();
    }

    static IEnumerator Capture()
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
        PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
        Warp(player, origin + right * 7f, -up);
        var amount = typeof(OverburstElementEnergy).GetProperty("Amount").GetSetMethod(true);
        var stacksProp = typeof(OverburstElementEnergy).GetProperty("RadianceStacks").GetSetMethod(true);
        OverburstElementEnergy energy = null;

        void Record(string step)
        {
            Transform host = energy != null ? energy.transform : player.transform;
            var aura = host.Find("Radiance aura");
            var counts = aura != null
                ? aura.GetComponentsInChildren<ParticleSystem>(true).Select(p => p.name + "=" + p.particleCount).ToArray()
                : new string[0];
            var crown = host.Find("Radiance crown");
            int crownCount = crown != null ? crown.GetComponentsInChildren<ParticleSystem>(true).Sum(p => p.particleCount) : 0;
            var sp = cam.WorldToScreenPoint(player.transform.position);
            string shot = Path.Combine(Output, (records.Count + 1).ToString("00") + "_" + step + ".png");
            ScreenCapture.CaptureScreenshot(shot); shots.Add(shot);
            records.Add(new { step, element = energy != null ? energy.Element.ToString() : "?", stacks = energy != null ? energy.RadianceStacks : -1,
                auraFound = aura != null, counts, crownCount, screen = new[] { sp.x, sp.y } });
        }
        IEnumerator Equip(WeaponElement element)
        {
            melee.CancelCurrentAttackState();
            if (!actor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common, element: element))) throw new Exception("Equip " + element);
            yield return Wait(0.3f);
            // The game adds the energy component to the equipment's object on first combat use (OverburstElementCombat).
            energy = actor.Equipment.GetComponent<OverburstElementEnergy>() ?? actor.Equipment.gameObject.AddComponent<OverburstElementEnergy>();
            notes.Add("energy on " + energy.gameObject.name + " (player " + (energy.gameObject == player.gameObject) + ")");
        }
        IEnumerator Radiance(int stacks)
        {
            for (float t = 0f; t < 1.2f; t += Time.deltaTime)
            {
                amount.Invoke(energy, new object[] { 200f }); stacksProp.Invoke(energy, new object[] { stacks });
                yield return null;
            }
        }

        for (int round = 1; round <= 2; round++)
        {
            yield return Equip(WeaponElement.Light);
            yield return Radiance(50);
            Record("r" + round + "_light50");
            yield return null;
            yield return Equip(WeaponElement.Fire);
            yield return Wait(0.2f); Record("r" + round + "_fire_0.5s");
            yield return Wait(2.5f); Record("r" + round + "_fire_3s");
        }
        yield return Equip(WeaponElement.Light);
        yield return Radiance(50);
        Record("r3_light50");
    }
}
