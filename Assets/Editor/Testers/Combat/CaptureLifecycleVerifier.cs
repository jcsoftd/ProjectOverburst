using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Native Play checks for termination/return only. Does not run the scene-dependent visual capture routines.
[InitializeOnLoad]
public static class CaptureLifecycleVerifier
{
    const string Key = "Overburst.Cleanup.CaptureLifecycle";
    static readonly string[] Names = { "AttackPoseCapture", "DarkVisualCapture", "BloodFeedbackCapture", "StatusAuraCapture", "UiReviewCapture" };
    static readonly string[] Modes = { "complete", "write-failure", "manual-stop", "dispose-failure" };
    static readonly string[] StringKeys = { ".output", ".status", ".tag", ".isolated", ".returnStatus", ".writeFailure", ".filter", ".env" };
    static readonly string[] BoolKeys = { "", ".background", ".return", ".follow" };
    static JObject data;
    static string Output => SessionState.GetString(Key + ".output", "");
    static string Account => (string)data["account"];
    static bool Active => SessionState.GetBool(Key, false);
    static string Phase => (string)data["phase"];
    static int Index => (int)data["index"];
    static string[] CurrentNames => new[] { Names[Index % Names.Length] };
    static string CurrentMode => Modes[Index / Names.Length];
    static double Deadline => (double)data["deadline"];

    static CaptureLifecycleVerifier()
    {
        if (Active)
        {
            data = JObject.Parse(File.ReadAllText(Path.Combine(Output, "progress.json")));
            Subscribe();
        }
    }
    static void Subscribe()
    {
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= State; EditorApplication.playModeStateChanged += State;
        Application.logMessageReceived -= Log; Application.logMessageReceived += Log;
    }
    static void ReleaseReservation()
    {
        SessionState.SetBool(Key, false);
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= State;
        Application.logMessageReceived -= Log;
    }
    static void RecoverDeferredFixture()
    {
        string path = SessionState.GetString(Key + ".deferredFixture", "");
        if (string.IsNullOrEmpty(path)) return;
        if (Busy()) throw new InvalidOperationException("Recover the fixture only in idle EditMode.");
        if (!path.StartsWith("Assets/Editor/Testers/Combat/CaptureLifecycleFixture_", StringComparison.Ordinal))
            throw new InvalidOperationException("Deferred fixture is outside this verifier's scope.");
        if (AssetDatabase.IsValidFolder(path))
        {
            string guid = SessionState.GetString(Key + ".deferredGuid", "");
            if (string.IsNullOrEmpty(guid) || AssetDatabase.AssetPathToGUID(path) != guid)
                throw new InvalidOperationException("Deferred fixture GUID changed; preserve it for review.");
            EditorFileRecycle.MoveAsset(path);
        }
        SessionState.EraseString(Key + ".deferredFixture");
        SessionState.EraseString(Key + ".deferredGuid");
    }
    static Type CaptureType(string name) => typeof(AttackPoseCapture).Assembly.GetType(name, true);
    static FieldInfo Field(string name, string field) => CaptureType(name).GetField(field, BindingFlags.Static | BindingFlags.NonPublic);
    static MethodInfo Method(string name, string method) => CaptureType(name).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic);
    static JArray Scenes() => JArray.FromObject(Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
    {
        var scene = SceneManager.GetSceneAt(i);
        return new { scene.path, scene.isLoaded, scene.isDirty, active = scene == SceneManager.GetActiveScene() };
    }));
    static bool Busy() => EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
        || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
        || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable));
    static bool OwnsPlay() => Active && string.Equals(Account, IsolatedSavePlayGuard.ActiveDirectory, StringComparison.OrdinalIgnoreCase);
    static void Persist() => File.WriteAllText(Path.Combine(Output, "progress.json"), data.ToString());
    static void PhaseTo(string phase, double seconds = 30)
    { data["phase"] = phase; data["deadline"] = EditorApplication.timeSinceStartup + seconds; Persist(); }

    public static string Run(string output, int startIndex = 0)
    {
        if (Active || Busy()) throw new InvalidOperationException("Idle Editor with no prepared account required.");
        string expected = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출/Cleanup/20261013_FullCodeCleanup/Verification/Capture"));
        if (!string.Equals(Path.GetFullPath(output), expected, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Fixture output mismatch.");
        if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty)) throw new InvalidOperationException("Keep dirty user scenes untouched.");
        var previousCases = new JArray();
        if (startIndex != 0)
        {
            if (startIndex != 15) throw new ArgumentException("Only the five remaining disposal cases may be resumed.");
            var previousResult = JObject.Parse(File.ReadAllText(Path.Combine(output, "first-attempt-result.json")));
            previousCases = new JArray(((JArray)previousResult["cases"]).Take(startIndex).Select(c => c.DeepClone()));
            if (previousCases.Count != startIndex || previousCases.Any(c => !(bool)c["pass"])) throw new InvalidOperationException("Prior cases did not pass.");
        }
        var saved = new JArray();
        foreach (string name in Names)
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(CaptureType(name).TypeHandle);
            if (SessionState.GetBool(name, false) || SessionState.GetBool(name + ".return", false)
                || Field(name, "work").GetValue(null) != null || ((ICollection)Field(name, "stack").GetValue(null)).Count != 0)
                throw new InvalidOperationException("Capture is already active: " + name);
            var strings = new JObject();
            foreach (string suffix in StringKeys) strings[suffix] = SessionState.GetString(name + suffix, "<missing-lifecycle-key>");
            var booleans = new JObject();
            foreach (string suffix in BoolKeys) booleans[suffix] = SessionState.GetBool(name + suffix, false);
            saved.Add(new JObject { ["name"] = name, ["strings"] = strings, ["bools"] = booleans });
        }
        RecoverDeferredFixture();
        SessionState.EraseString(Key + ".writeFailure");
        Directory.CreateDirectory(output); SessionState.SetString(Key + ".output", output);
        string fixture = "Assets/Editor/Testers/Combat/CaptureLifecycleFixture_" + Guid.NewGuid().ToString("N");
        data = new JObject { ["phase"] = "PREPARED", ["index"] = startIndex, ["account"] = Path.Combine(output, "Account"),
            ["fixture"] = fixture, ["startScene"] = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
            ["background"] = Application.runInBackground, ["scenes"] = Scenes(), ["savedCaptures"] = saved,
            ["cases"] = previousCases, ["errors"] = new JArray(), ["scope"] = "Native termination/return boundaries; visual capture and Player not run",
            ["resumedIndex"] = startIndex, ["priorEvidence"] = startIndex == 0 ? "" : "first-attempt-result.json (15 passed cases retained)" };
        data["fixtureGuid"] = AssetDatabase.CreateFolder("Assets/Editor/Testers/Combat", Path.GetFileName(fixture));
        Scene previous = SceneManager.GetActiveScene();
        Scene temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try { if (!EditorSceneManager.SaveScene(temporary, fixture + "/Fixture.unity")) throw new IOException("Fixture save failed."); }
        finally { EditorSceneManager.CloseScene(temporary, true); SceneManager.SetActiveScene(previous); }
        Persist(); SessionState.SetBool(Key, true); Subscribe();
        StartNext();
        return "RUNNING_NATIVE_CAPTURE_BOUNDARIES";
    }
    static void StartNext()
    {
        if (Busy()) { PhaseTo("WAIT_IDLE", 120); return; }
        if (Index >= Modes.Length * Names.Length) { Complete(); return; }
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>((string)data["fixture"] + "/Fixture.unity");
        IsolatedSavePlayGuard.PrepareIsolatedPlay(Account);
        PhaseTo("ENTERING"); EditorApplication.EnterPlaymode();
    }
    static void State(PlayModeStateChange state)
    {
        if (!Active) return;
        if (state == PlayModeStateChange.EnteredPlayMode && Phase == "ENTERING") EditorApplication.delayCall += Exercise;
        if (state == PlayModeStateChange.ExitingPlayMode && OwnsPlay())
        {
            data["exit"] = JArray.FromObject(CurrentNames.Select(name => new
            {
                name, workNull = Field(name, "work").GetValue(null) == null,
                stackEmpty = ((ICollection)Field(name, "stack").GetValue(null)).Count == 0,
                cleanupFailureRecorded = ((IEnumerable<string>)Field(name, "errors").GetValue(null)).Any(e => e.StartsWith("CLEANUP_FAILED ", StringComparison.Ordinal))
            }));
            PhaseTo("RETURNING");
        }
        if (state == PlayModeStateChange.EnteredEditMode && Phase == "RETURNING")
        {
            RestoreStartScene();
            Application.runInBackground = (bool)data["background"];
        }
    }
    static void Exercise()
    {
        if (!Active || !OwnsPlay()) { Fail("Fixture Play ownership mismatch."); return; }
        try
        {
            string mode = CurrentMode; PhaseTo("PLAY");
            foreach (string name in CurrentNames)
            {
                string captureOutput = Path.Combine(Output, mode, name); Directory.CreateDirectory(captureOutput);
                SessionState.SetBool(name, true); SessionState.SetBool(name + ".background", (bool)data["background"]);
                SessionState.SetString(name + ".status", "RUNNING"); SessionState.SetString(name + ".isolated", Account);
                SessionState.SetString(name + ".returnStatus", "NOT_RETURNED"); SessionState.SetString(name + ".output", captureOutput);
                SessionState.SetString(name + ".tag", "lifecycle"); SessionState.EraseString(name + ".writeFailure");
                ((IList<string>)Field(name, "errors").GetValue(null)).Clear();
                data["disposeWarning"] = "";
                EditorApplication.update += (EditorApplication.CallbackFunction)Delegate.CreateDelegate(typeof(EditorApplication.CallbackFunction), Method(name, "Tick"));
                if (mode == "write-failure") Directory.CreateDirectory(Path.Combine(captureOutput,
                    name == "AttackPoseCapture" ? "meta_lifecycle.txt" : name == "UiReviewCapture" ? "results.json" : "capture-results.json"));
                if (mode == "dispose-failure") Field(name, "work").SetValue(null, new ThrowingRoutine());
                if (mode == "complete" || mode == "write-failure")
                {
                    Method(name, "Finish").Invoke(null, new object[] { "COMPLETE" });
                    Method(name, "Finish").Invoke(null, new object[] { "FAIL duplicate invocation" });
                }
            }
        }
        catch (Exception e) { ((JArray)data["errors"]).Add(e.ToString()); Persist(); }
        finally { if (OwnsPlay() && EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode(); }
    }
    static void Log(string message, string stack, LogType type)
    {
        if (!Active || data == null || Index >= Modes.Length * Names.Length || CurrentMode != "dispose-failure") return;
        if (type != LogType.Warning || !message.StartsWith("[" + CurrentNames[0] + "]", StringComparison.Ordinal)
            || !message.Contains("Intentional fixture disposal failure.")) return;
        data["disposeWarning"] = message; Persist();
    }
    static void Tick()
    {
        if (!Active) { EditorApplication.update -= Tick; return; }
        if (Phase == "WAIT_IDLE")
        {
            if (EditorApplication.timeSinceStartup > Deadline)
            {
                ((JArray)data["errors"]).Add("Idle wait deadline exceeded.");
                // Another execution may own the Editor now; release only our reservation.
                Complete(deferCleanup: Busy());
            }
            else if (!Busy()) StartNext();
            return;
        }
        if (EditorApplication.timeSinceStartup > Deadline)
        {
            if (OwnsPlay() && EditorApplication.isPlayingOrWillChangePlaymode)
            { ((JArray)data["errors"]).Add("Play boundary timed out."); Persist(); EditorApplication.ExitPlaymode(); return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Fail("Termination/account return deadline exceeded."); return;
        }
        if (Phase != "RETURNING" || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (CurrentNames.Any(name => SessionState.GetString(name + ".returnStatus", "") == "PENDING")) return;
        string mode = CurrentMode;
        var captures = JArray.FromObject(CurrentNames.Select(name => new
        {
            name, status = SessionState.GetString(name + ".status", ""), returned = SessionState.GetString(name + ".returnStatus", ""),
            outputFailure = !string.IsNullOrEmpty(SessionState.GetString(name + ".writeFailure", "")), active = SessionState.GetBool(name, false),
            workNull = Field(name, "work").GetValue(null) == null, stackEmpty = ((ICollection)Field(name, "stack").GetValue(null)).Count == 0,
            callbacksRemoved = EditorApplication.update == null || !EditorApplication.update.GetInvocationList().Any(callback => callback.Method.DeclaringType == CaptureType(name)
                && (callback.Method.Name == "Tick" || callback.Method.Name == "ReturnAccount"))
        }));
        bool pass = !Busy() && !IsolatedSavePlayGuard.RequiresAccountChoice && JToken.DeepEquals(data["scenes"], Scenes())
            && captures.All(c => (string)c["returned"] == "RETURNED" && !(bool)c["active"]
                && (string)c["status"] == (mode == "complete" ? "COMPLETE" : mode == "write-failure" ? "FAIL_WRITE COMPLETE" : "STOPPED")
                && (bool)c["outputFailure"] == (mode == "write-failure") && (bool)c["workNull"] && (bool)c["stackEmpty"] && (bool)c["callbacksRemoved"])
            && (!string.IsNullOrEmpty((string)data["disposeWarning"]) == (mode == "dispose-failure"));
        ((JArray)data["cases"]).Add(new JObject { ["mode"] = mode, ["pass"] = pass, ["captures"] = captures, ["exit"] = data["exit"].DeepClone(),
            ["editModeReturned"] = true, ["accountReturned"] = !Busy() && !IsolatedSavePlayGuard.RequiresAccountChoice,
            ["disposeWarning"] = data["disposeWarning"], ["sceneSetupPreserved"] = JToken.DeepEquals(data["scenes"], Scenes()) });
        if (!pass) { Fail("Boundary case failed: " + mode); return; }
        data["index"] = Index + 1; PhaseTo("WAIT_IDLE", 120);
    }
    static void RestoreStartScene()
    {
        string own = (string)data["fixture"] + "/Fixture.unity";
        if (AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == own)
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>((string)data["startScene"]);
    }
    static void Fail(string error)
    {
        ((JArray)data["errors"]).Add(error);
        if (OwnsPlay() && EditorApplication.isPlayingOrWillChangePlaymode) { Persist(); EditorApplication.ExitPlaymode(); return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Persist(); return; }
        Complete();
    }
    static void Complete(bool deferCleanup = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode && !deferCleanup) throw new InvalidOperationException("Exit Play before completing.");
        try
        {
            data["cleanupDeferred"] = deferCleanup;
            if (deferCleanup)
            {
                SessionState.SetString(Key + ".deferredFixture", (string)data["fixture"]);
                SessionState.SetString(Key + ".deferredGuid", (string)data["fixtureGuid"] ?? "");
                Debug.LogWarning("[CaptureLifecycleVerifier] Idle wait expired. Shared state was preserved; the fixture will be recovered before the next idle Run.");
            }
            else
            {
                RestoreStartScene(); Application.runInBackground = (bool)data["background"];
                foreach (JObject capture in (JArray)data["savedCaptures"])
                {
                    string name = (string)capture["name"];
                    foreach (var property in ((JObject)capture["strings"]).Properties())
                        if ((string)property.Value == "<missing-lifecycle-key>") SessionState.EraseString(name + property.Name);
                        else SessionState.SetString(name + property.Name, (string)property.Value);
                    foreach (var property in ((JObject)capture["bools"]).Properties()) SessionState.SetBool(name + property.Name, (bool)property.Value);
                }
                try { EditorFileRecycle.MoveAsset((string)data["fixture"]); }
                catch (Exception e) { ((JArray)data["errors"]).Add("Fixture cleanup failed: " + e.Message); }
            }
            data["phase"] = !deferCleanup && ((JArray)data["errors"]).Count == 0 && ((JArray)data["cases"]).Count == Modes.Length * Names.Length ? "PASS" : "FAIL";
            data["finalEditMode"] = !EditorApplication.isPlayingOrWillChangePlaymode;
            data["finalAccountReturned"] = !Busy() && !IsolatedSavePlayGuard.RequiresAccountChoice;
            data["finalStartSceneRestored"] = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == (string)data["startScene"];
            try { Persist(); }
            catch (Exception error) { RecordWriteFailure(error); }
            try { File.WriteAllText(Path.Combine(Output, "result.json"), data.ToString()); }
            catch (Exception error) { RecordWriteFailure(error); }
        }
        finally { ReleaseReservation(); }
    }
    static void RecordWriteFailure(Exception error)
    {
        data["phase"] = "FAIL";
        ((JArray)data["errors"]).Add("Result write failed: " + error.Message);
        SessionState.SetString(Key + ".writeFailure", error.ToString());
        Debug.LogWarning("[CaptureLifecycleVerifier] Result write failed: " + error.Message);
    }
    sealed class ThrowingRoutine : IEnumerator, IDisposable
    {
        public object Current => null;
        public bool MoveNext() => true;
        public void Reset() { }
        public void Dispose() => throw new IOException("Intentional fixture disposal failure.");
    }
}
