using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Globalization;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class VfxLegacyCleanupVerifier
{
    const string Key = "Overburst.VfxLegacyCleanupVerifier.";
    static string Output => SessionState.GetString(Key + "output", "");
    static int Phase { get => int.Parse(SessionState.GetString(Key + "phaseText", "0")); set => SessionState.SetString(Key + "phaseText", value.ToString()); }
    static int Cycles { get => int.Parse(SessionState.GetString(Key + "cyclesText", "0")); set => SessionState.SetString(Key + "cyclesText", value.ToString()); }
    static bool Owned { get => SessionState.GetString(Key + "ownedText", "") == "1"; set => SessionState.SetString(Key + "ownedText", value ? "1" : ""); }
    public static string Status => SessionState.GetString(Key + "status", "NOT_RUN");
    static VfxLegacyCleanupVerifier()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += State;
        Application.logMessageReceived += Log;
    }
    public static string Begin(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || Phase != 0)
            throw new InvalidOperationException("Use an idle Editor.");
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + "output", output);
        SessionState.SetString(Key + "checks", "[]");
        SessionState.SetString(Key + "errors", "[]");
        SessionState.SetString(Key + "realHash", HashAccount());
        Cycles = 0;
        SessionState.SetString(Key + "status", "RUNNING");
        StartCycle();
        return output;
    }
    static void StartCycle()
    {
        Phase = 1;
        Owned = true;
        SessionState.SetString(Key + "deadlineText", (EditorApplication.timeSinceStartup + 90).ToString(CultureInfo.InvariantCulture));
        WriteProgress("Starting isolated Play");
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(Output, "IsolatedAccount"));
    }
    static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        var checks = JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "checks", "[]"));
        checks.Add(message);
        SessionState.SetString(Key + "checks", JsonConvert.SerializeObject(checks));
    }
    static void Tick()
    {
        if (Phase == 0) return;
        try
        {
            if (EditorApplication.timeSinceStartup > double.Parse(SessionState.GetString(Key + "deadlineText", "0"), CultureInfo.InvariantCulture))
                throw new TimeoutException("Play verification timed out.");
            if (Phase == 1 && EditorApplication.isPlaying && !EditorApplication.isCompiling)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                VerifyRuntime();
                WriteProgress("Runtime fixture complete");
                Phase = 2;
                EditorApplication.delayCall += EditorApplication.ExitPlaymode;
            }
            else if (Phase == 2 && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                IsolatedSavePlayGuard.UseRealAccount();
                int cycles = ++Cycles;
                Check(HashAccount() == SessionState.GetString(Key + "realHash", ""), "Real account preserved, cycle " + cycles);
                Check(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)), "Save override cleared, cycle " + cycles);
                if (cycles < 2) StartCycle();
                else Finish("PASS", null);
            }
        }
        catch (Exception error)
        {
            Finish("FAIL", error.ToString());
            if (EditorApplication.isPlaying) EditorApplication.delayCall += EditorApplication.ExitPlaymode;
        }
    }
    static void VerifyRuntime()
    {
        var fixture = new GameObject("VFX cleanup fixture");
        var cameraRoot = new GameObject("VFX cleanup camera");
        var spawned = new List<GameObject>();
        try
        {
            cameraRoot.transform.position = new Vector3(0, 4, -10);
            cameraRoot.transform.LookAt(Vector3.zero);
            var camera = cameraRoot.AddComponent<Camera>();
            camera.enabled = false;
            var catalog = Resources.Load<MeleeElementHitVfxCatalog>(MeleeElementHitVfxCatalog.ResourcePath);
            foreach (var element in new[] { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light })
            {
                Check(catalog.TryResolve(element, out var prefab), "Hit pool resolves: " + element);
                var hit = Object.Instantiate(prefab, fixture.transform);
                spawned.Add(hit);
                var controller = hit.GetComponent<MeleeElementHitVfxController>();
                controller.SetElement(element);
                controller.RestartVfx();
                Check(controller.HasPlayableContent(element), "Hit content plays: " + element);
                controller.StopAndClearVfx();
                Check(hit.GetComponentsInChildren<ParticleSystem>(true).All(p => p.particleCount == 0), "Hit particles clear: " + element);
            }
            fixture.SetActive(false);
            var health = fixture.AddComponent<CombatHealth>();
            fixture.AddComponent<CombatTarget>();
            var status = fixture.AddComponent<ElementalStatusController>();
            fixture.SetActive(true);
            health.SetMaxHp(1000, true);
            var service = Object.FindFirstObjectByType<ElementalReactionVfxRuntimeService>();
            Check(service != null, "Freeze service exists in Play.");
            service.InitializeForValidation(Resources.Load<ElementalReactionVfxCatalog>(ElementalReactionVfxCatalog.ResourcePath), camera);
            for (int i = 0; i < 5; i++)
                Check(status.TryApplyDirectHit(new ElementalStatusApplication(WeaponElement.Ice, 100, fixture,
                    "cleanup", true, false, Vector3.zero, Vector3.forward)), "Cold stack accepted: " + i);
            service.TickForValidation();
            Check(status.TryGetReactionState(ElementalReactionType.Freeze, out _), "Five cold stacks cause freeze.");
            Check(service.GetLoopInstanceIdForValidation(status, ElementalReactionType.Freeze) != 0, "Freeze Loop starts without Start wrapper.");
            status.ClearAllReactionStates();
            service.TickForValidation();
            Check(service.TrackedLoopKeyCountForValidation == 0, "Freeze Loop releases without End wrapper.");
            var definition = AssetDatabase.LoadAssetAtPath<MeleeHeavyAttackDefinition>(
                "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Heavy/GreatswordHeavyAttack.asset");
            Check(ElementalReactionVfxRuntimeService.HasPlayableContentForValidation(definition.elementVfx.iceShatter), "Shatter remains playable.");
            Check(ChainElectricityBatchRenderer.TrySpawn(definition.elementVfx.electricChainLink,
                Vector3.zero, Vector3.right * 3), "Lightning link draws without empty Start/Proc wrappers.");
            var target = fixture.GetComponent<CombatTarget>();
            Check(!service.TryPlayForValidation(ElementalReactionType.Vaporize, ElementalReactionVfxSlotType.Start,
                target, Vector3.zero, Vector3.zero, Vector3.zero, 1), "Retired vaporize cannot spawn.");
        }
        finally
        {
            foreach (var instance in spawned) if (instance != null) Object.DestroyImmediate(instance);
            Object.DestroyImmediate(cameraRoot);
            Object.DestroyImmediate(fixture);
        }
    }
    static void State(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode && Owned)
        {
            SessionState.SetBool(Key + "background", Application.runInBackground);
            SessionState.SetBool(Key + "backgroundSet", true);
            Application.runInBackground = true;
            WriteProgress("Entered Play");
        }
        if (state == PlayModeStateChange.ExitingPlayMode && Owned
            && SessionState.GetBool(Key + "backgroundSet", false))
        {
            Application.runInBackground = SessionState.GetBool(Key + "background", false);
            SessionState.SetBool(Key + "backgroundSet", false);
        }
        if (state == PlayModeStateChange.EnteredEditMode && Owned)
        {
            IsolatedSavePlayGuard.UseRealAccount();
            Owned = false;
        }
    }
    static void Log(string message, string stack, LogType type)
    {
        if (Phase == 0 || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
        var errors = JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "errors", "[]"));
        errors.Add(message);
        SessionState.SetString(Key + "errors", JsonConvert.SerializeObject(errors));
    }
    static string HashAccount()
    {
        string root = Path.Combine(Application.persistentDataPath, "Account");
        if (!Directory.Exists(root)) return "ABSENT";
        using (var sha = SHA256.Create())
            return string.Join("|", Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(p => p)
                .Select(p => p.Substring(root.Length) + ":" + BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(p)))));
    }
    static void Finish(string status, string error)
    {
        var errors = JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "errors", "[]"));
        if (errors.Count != 0) status = "FAIL";
        Phase = 0;
        if (!EditorApplication.isPlayingOrWillChangePlaymode && Owned)
        {
            IsolatedSavePlayGuard.UseRealAccount();
            Owned = false;
        }
        SessionState.SetString(Key + "status", status);
        File.WriteAllText(Path.Combine(Output, "play_result.json"), JsonConvert.SerializeObject(new {
            status, error, errors, cycles = Cycles,
            checks = JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "checks", "[]"))
        }, Formatting.Indented));
    }
    static void WriteProgress(string stage) => File.WriteAllText(Path.Combine(Output, "progress.json"),
        JsonConvert.SerializeObject(new { stage, phase = Phase, cycles = Cycles, utc = DateTime.UtcNow }, Formatting.Indented));
}
