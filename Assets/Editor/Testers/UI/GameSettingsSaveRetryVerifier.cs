using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

public static class GameSettingsSaveRetryVerifier
{
    public static string Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || BuildPipeline.isBuildingPlayer || IsolatedSavePlayGuard.RequiresAccountChoice
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
            throw new InvalidOperationException("Idle Editor and returned account required.");
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        if (Directory.Exists(output)) throw new InvalidOperationException("Fresh output required.");
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        var type = typeof(OverburstGameSettings);
        var fields = new[] { "data", "loaded", "focused", "dirty", "Changed" }
            .Select(n => type.GetField(n, BindingFlags.NonPublic | BindingFlags.Static)).ToArray();
        var before = fields.Select(f => f.GetValue(null)).ToArray();
        string env = Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY");
        float volume = AudioListener.volume;
        int vSync = QualitySettings.vSyncCount, frameRate = Application.targetFrameRate;
        InputActionAsset bindings = null;
        Exception failure = null;
        void Check(bool yes, string name) { if (!yes) throw new InvalidOperationException(name); checks.Add(name); }
        bool Dirty() => (bool)fields[3].GetValue(null);
        void Reset() => type.GetMethod("ResetStatics", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        try
        {
            Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY", output);
            Reset();
            OverburstGameSettings.MasterVolume = .37f;
            string path = OverburstGameSettings.FilePath, temp = path + ".tmp";
            Directory.CreateDirectory(temp); // Real filesystem failure, no injected Save replacement.
            OverburstGameSettings.SaveIfDirty();
            Check(Dirty() && !File.Exists(path), "First write failure retains dirty and no settings file");
            OverburstGameSettings.SaveIfDirty();
            Check(Dirty(), "Repeated write failure retains dirty");
            Directory.Delete(temp);
            OverburstGameSettings.SaveIfDirty();
            Check(!Dirty() && File.Exists(path), "Retry succeeds without another setting change");
            Check(Mathf.Approximately((float)Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path))["masterVolume"], .37f), "Saved retry contains intended value");
            Reset(); Check(Mathf.Approximately(OverburstGameSettings.MasterVolume, .37f), "Cold settings load reads successful retry");
            var stamp = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(path, stamp); string original = File.ReadAllText(path);
            OverburstGameSettings.SaveIfDirty();
            Check(File.GetLastWriteTimeUtc(path) == stamp && File.ReadAllText(path) == original && !File.Exists(temp), "Clean SaveIfDirty performs no extra write");
            OverburstGameSettings.MasterVolume = .61f;
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                OverburstGameSettings.SaveIfDirty();
                Check(Dirty(), "Atomic replacement failure retains dirty");
            }
            OverburstGameSettings.SaveIfDirty();
            Check(!Dirty(), "Atomic replacement can retry after lock release");
            bindings = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = new InputActionMap("OwnedRetryFixture"); bindings.AddActionMap(map);
            var action = map.AddAction("Equipment", InputActionType.Button, "<Keyboard>/c");
            action.ApplyBindingOverride(0, "<Keyboard>/v");
            Directory.CreateDirectory(temp);
            OverburstGameSettings.StoreBindingOverrides(bindings);
            Check(Dirty(), "Direct binding Save failure creates dirty retry state");
            Directory.Delete(temp); OverburstGameSettings.SaveIfDirty();
            Check(!Dirty() && File.ReadAllText(path).Contains("<Keyboard>/v"), "Binding retry persists actual override JSON");
            Reset(); Check(Mathf.Approximately(OverburstGameSettings.MasterVolume, .61f), "Cold replacement load reads intended latest value");
            action.RemoveAllBindingOverrides(); OverburstGameSettings.ApplyBindingOverrides(bindings);
            Check(action.bindings[0].overridePath == "<Keyboard>/v", "Cold settings binding override re-applies");
        }
        catch (Exception error) { failure = error; }
        finally
        {
            if (bindings != null) UnityEngine.Object.DestroyImmediate(bindings);
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, before[i]);
            Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY", env);
            AudioListener.volume = volume; QualitySettings.vSyncCount = vSync; Application.targetFrameRate = frameRate;
        }
        bool restored = fields.Select((f, i) => Equals(f.GetValue(null), before[i])).All(x => x)
            && Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY") == env
            && AudioListener.volume == volume && QualitySettings.vSyncCount == vSync && Application.targetFrameRate == frameRate;
        File.WriteAllText(Path.Combine(output, "settings-retry.json"), JsonConvert.SerializeObject(new
            { status = failure == null && restored ? "PASS_SCOPED" : "FAIL", checks, restored, error = failure?.ToString(), playerBuild = "NOT_RUN" }, Formatting.Indented));
        if (failure != null) throw failure;
        if (!restored) throw new InvalidOperationException("Settings verifier did not restore static/environment state.");
        return "PASS_SCOPED settings retry: " + checks.Count;
    }
}
