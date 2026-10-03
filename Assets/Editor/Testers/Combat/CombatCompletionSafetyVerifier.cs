using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Fault injection against the real health and barrage paths, using only owned preview objects.
[InitializeOnLoad]
public static class CombatCompletionSafetyVerifier
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const string Marker = "COMPLETION_TEST:";
    const string QueueKey = "Overburst.CompletionSafety.VerifyQueue";
    const string FeedbackQueue = "Overburst.CompletionSafety.ActualQueue";
    static CombatCompletionSafetyVerifier() { EditorApplication.update += QueuedTick; EditorApplication.update += QueuedFeedbackTick; }

    public static string QueueActualFeedback(string directory)
    {
        directory = IsolatedSavePlayGuard.ValidateDirectory(directory); Directory.CreateDirectory(directory);
        if (!string.IsNullOrEmpty(SessionState.GetString(FeedbackQueue, ""))) throw new InvalidOperationException("Actual feedback already queued.");
        SessionState.SetString(FeedbackQueue, directory);
        SessionState.SetFloat(FeedbackQueue + ".Deadline", (float)EditorApplication.timeSinceStartup + 600);
        return "Queued actual feedback Play for an idle Editor.";
    }

    static void QueuedFeedbackTick()
    {
        string directory = SessionState.GetString(FeedbackQueue, "");
        if (string.IsNullOrEmpty(directory)) return;
        void End(string state)
        {
            SessionState.EraseString(FeedbackQueue); SessionState.EraseFloat(FeedbackQueue + ".Deadline");
            File.WriteAllText(Path.Combine(directory,"ActualStart.json"),JsonConvert.SerializeObject(new { state, utc=DateTime.UtcNow }));
        }
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(FeedbackQueue + ".Deadline",0)) { End("TIMED_OUT"); return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""))
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.ElementGemPlayVerifier",""))) return;
        string key="Overburst.CompletionSafety.Actual";
        if (!string.IsNullOrEmpty(SessionState.GetString(key,""))) { End("ALREADY_OWNED"); return; }
        string start=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene);
        try
        {
            if ((string)Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(directory,"CoreIntegration.json")))["status"] != "PASS")
                throw new InvalidOperationException("Core integration must pass first.");
            string real=Path.Combine(Application.persistentDataPath,"Account");
            string Hash(string path) { using(var sha=System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-",""); }
            var files=Directory.Exists(real)?Directory.GetFiles(real).OrderBy(p=>p).Select(p=>new {path=p,hash=Hash(p)}).ToArray():null;
            File.WriteAllText(Path.Combine(directory,"BeforeFeedback.json"),JsonConvert.SerializeObject(new {
                scenes=HideoutCatalogLayoutBuilder.EditorSnapshot(),start,actualAccountFiles=files,
                optionsEnabled=EditorSettings.enterPlayModeOptionsEnabled,options=EditorSettings.enterPlayModeOptions.ToString()
            },Formatting.Indented));
            SessionState.SetString(key,directory); SessionState.SetString(key+".Start",start);
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
            End("STARTED");
            IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(directory,"IsolatedAccount_Feedback"));
        }
        catch(Exception error)
        {
            EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(start)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
            SessionState.EraseString(key); SessionState.EraseString(key+".Start"); End("FAILED: "+error);
        }
    }

    // A bounded, owned request survives the domain reload of another concurrent Play.
    public static string QueueFaultsAndCore(string directory)
    {
        directory = IsolatedSavePlayGuard.ValidateDirectory(directory);
        if (!string.IsNullOrEmpty(SessionState.GetString(QueueKey, "")))
            throw new InvalidOperationException("Completion verification already queued.");
        SessionState.SetString(QueueKey, directory);
        SessionState.SetFloat(QueueKey + ".Deadline", (float)EditorApplication.timeSinceStartup + 600);
        return "Queued bounded completion verification; waiting for an idle, returned-account Editor.";
    }

    static void QueuedTick()
    {
        string directory = SessionState.GetString(QueueKey, "");
        if (string.IsNullOrEmpty(directory)) return;
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(QueueKey + ".Deadline", 0))
        { EndQueue(directory, "TIMED_OUT"); return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))) return;
        try
        {
            Run(directory);
            string real = Path.Combine(Application.persistentDataPath, "Account");
            string Hash(string path) { using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
            var files = Directory.Exists(real) ? Directory.GetFiles(real).OrderBy(p => p).Select(p => new { path = p, hash = Hash(p) }).ToArray() : null;
            File.WriteAllText(Path.Combine(directory, "BeforePlay.json"), JsonConvert.SerializeObject(new {
                pid = System.Diagnostics.Process.GetCurrentProcess().Id, scenes = HideoutCatalogLayoutBuilder.EditorSnapshot(),
                start = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene), optionsEnabled = EditorSettings.enterPlayModeOptionsEnabled,
                options = EditorSettings.enterPlayModeOptions.ToString(), actualAccountFiles = files
            }, Formatting.Indented));
            EndQueue(directory, "FAULTS_PASS_CORE_STARTED");
            ElementGemPlayVerifier.Begin(directory, 7, "CoreIntegration");
        }
        catch (Exception error) { EndQueue(directory, "FAILED: " + error); }
    }

    static void EndQueue(string directory, string state)
    {
        SessionState.EraseString(QueueKey); SessionState.EraseFloat(QueueKey + ".Deadline");
        File.WriteAllText(Path.Combine(directory, "FaultsAndCoreStart.json"), JsonConvert.SerializeObject(new { state, utc = DateTime.UtcNow }));
    }
    static object Get(object value, string name) => value.GetType().GetField(name, Fields).GetValue(value);
    static void Set(object value, string name, object field) => value.GetType().GetField(name, Fields).SetValue(value, field);

    public static string Run(string directory)
    {
        directory = IsolatedSavePlayGuard.ValidateDirectory(directory);
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Completion preview verification requires an idle Editor.");
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        var observations = new List<object>();
        var expectedLogs = new List<string>();
        void Check(bool valid, string label) { if (!valid) throw new InvalidOperationException(label); checks.Add(label); }
        void Logs(string message, string stack, LogType type)
        {
            if (type == LogType.Exception && (message.Contains(Marker) || stack.Contains("DarkBarrageScheduler.SpawnHitVfx")))
                expectedLogs.Add(message);
        }
        var preview = EditorSceneManager.NewPreviewScene();
        var roots = new List<GameObject>();
        var schedulerType = typeof(DarkBarrageScheduler);
        var diagnostics = schedulerType.GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(f => f.Name.Contains("k__BackingField")).ToDictionary(f => f, f => f.GetValue(null));
        GameObject Root(string name)
        {
            var root = new GameObject("CompletionTest_" + name) { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(root, preview); roots.Add(root); return root;
        }
        CombatHealth Health(string name, float hp)
        {
            var health = Root(name).AddComponent<CombatHealth>(); Set(health, "showDamageNumbers", false);
            health.SetMaxHp(hp, true); return health;
        }
        string failure = null;
        Application.logMessageReceived += Logs;
        try
        {
            foreach (var fault in new[] { "none", "resolved", "damaged", "health", "dead" })
            {
                var health = Health(fault, 10); int later = 0, deaths = 0, damageEvents = 0; bool resolvedCorrectly = false;
                var order = new List<string>();
                void Fail() => throw new InvalidOperationException(Marker + fault);
                if (fault == "resolved") { health.OnDamageResolved += (a,b,c,d) => Fail(); health.OnDamageResolved += (a,b,c,d) => later++; }
                if (fault == "damaged") { health.OnDamaged += (a,b) => Fail(); health.OnDamaged += (a,b) => later++; }
                if (fault == "health") { health.OnHealthChanged += (a,b,c) => Fail(); health.OnHealthChanged += (a,b,c) => later++; }
                if (fault == "dead") { health.OnDead += (a,b) => Fail(); health.OnDead += (a,b) => later++; }
                health.OnDamageResolved += (a,b,c,d) => { order.Add("resolved"); resolvedCorrectly = c == 10 && d; };
                health.OnDamaged += (a,b) => { damageEvents++; order.Add("damaged"); };
                health.OnHealthChanged += (a,b,c) => order.Add("health");
                health.OnDead += (a,b) => { deaths++; order.Add("dead"); };
                health.TakeDamage(new DamageInfo(20, Vector3.zero, triggersOnHitEffects: false));
                Check(resolvedCorrectly, fault + " actual loss / lethal");
                Check(health.CurrentHp == 0 && health.IsDead && deaths == 1, fault + " death completes");
                Check(fault == "none" || later == 1, fault + " later subscriber runs");
                Check(order.SequenceEqual(new[] { "resolved", "damaged", "health", "dead" }), fault + " notification order");
                health.TakeDamage(new DamageInfo(20, Vector3.zero));
                Check(deaths == 1 && damageEvents == 1, fault + " dead actor ignores duplicate damage");
            }
            var reset = Health("reset", 10); int resetLater = 0;
            reset.OnHealthChanged += (a,b,c) => throw new InvalidOperationException(Marker + "reset-hud");
            reset.OnReset += a => throw new InvalidOperationException(Marker + "reset-observer");
            reset.OnReset += a => resetLater++;
            reset.ResetHealth();
            Check(resetLater == 1 && !reset.IsDead && reset.CurrentHp == 10, "reset survives HUD and reset observer errors");

            var snapshot = Health("subscriber-snapshot", 10); int first = 0, second = 0;
            Action<CombatHealth, DamageInfo> secondObserver = (a,b) => second++;
            snapshot.OnDamaged += (a,b) => { first++; snapshot.OnDamaged -= secondObserver; };
            snapshot.OnDamaged += secondObserver;
            snapshot.TakeDamage(new DamageInfo(1, Vector3.zero, triggersOnHitEffects:false));
            snapshot.TakeDamage(new DamageInfo(1, Vector3.zero, triggersOnHitEffects:false));
            Check(first == 2 && second == 1 && snapshot.CurrentHp == 8, "subscriber snapshot / nonlethal behavior");

            foreach (var fault in new[] { "none", "observer", "vfx", "view-return" })
            {
                var health = Health("dark-" + fault, 100);
                var target = health.gameObject.AddComponent<CombatTarget>(); Set(target, "damageReceiver", health);
                var source = Root("source");
                var scheduler = Root("scheduler").AddComponent<DarkBarrageScheduler>(); scheduler.enabled = false;
                var castType = schedulerType.GetNestedType("Cast", BindingFlags.NonPublic);
                var entryType = schedulerType.GetNestedType("Entry", BindingFlags.NonPublic);
                var shotType = schedulerType.GetNestedType("Shot", BindingFlags.NonPublic);
                var stateType = schedulerType.GetNestedType("ShotState", BindingFlags.NonPublic);
                var cast = Activator.CreateInstance(castType, true);
                Set(cast,"Source",source); Set(cast,"ShotDamage",7f); Set(cast,"Remaining",1); Set(cast,"StartFrame",Time.frameCount);
                var entry = Activator.CreateInstance(entryType); Set(entry,"Target",target); Set(entry,"Health",health);
                ((IList)Get(cast,"Entries")).Add(entry);
                var shot = Activator.CreateInstance(shotType);
                Set(shot,"State",Enum.Parse(stateType,"Homing")); Set(shot,"PhaseStart",Time.time-1);
                Set(shot,"PhaseDuration",.001f); Set(shot,"ReleasedAt",Time.time-1);
                if (fault == "view-return") Set(shot,"Projectile",Root("unregistered-view"));
                var shots = (IList)Get(cast,"Shots"); shots.Add(shot);
                var active = (IList)Get(scheduler,"active"); active.Add(cast);
                int hits = 0, consumedBeforeDamage = 0;
                if (fault == "observer") health.OnDamageResolved += (a,b,c,d) => throw new InvalidOperationException(Marker + "dark-observer");
                health.OnDamageResolved += (a,b,c,d) => { hits++; if ((int)Get(cast,"Remaining") == 0 && Get(shots[0],"State").ToString() == "Done") consumedBeforeDamage++; };
                object queue = Get(scheduler,"hitVfxTimes");
                if (fault == "vfx")
                {
                    var vfx = new MeleeHeavyElementVfxSet { darkBarrageHit = Root("hit-template") };
                    Set(cast,"Vfx",vfx);
                    // Force a failure inside the real post-damage VFX boundary, before any global pool is touched.
                    Set(scheduler,"hitVfxTimes",null);
                }
                var failures = new List<string>();
                try
                {
                    for (int i = 0; i < 3; i++)
                    {
                        try { schedulerType.GetMethod("Update",Fields).Invoke(scheduler,null); }
                        catch (TargetInvocationException error) { failures.Add(error.InnerException.GetType().Name); }
                        if (i == 0 && fault == "view-return")
                        {
                            Check((int)Get(cast,"Remaining") == 0 && Get(shots[0],"State").ToString() == "Done", "failed view return retains completion");
                            // Repair only the owned malformed view fixture so the next update can retire the cast.
                            var stored = shots[0]; Set(stored,"Projectile",null); shots[0] = stored;
                        }
                    }
                }
                finally { Set(scheduler,"hitVfxTimes",queue); }
                Check(health.CurrentHp == 93 && hits == 1, fault + " dark damage exactly once across updates");
                Check(consumedBeforeDamage == 1, fault + " dark consumption precedes callbacks");
                Check(active.Count == 0 && (int)Get(cast,"Remaining") == 0, fault + " dark cast retires");
                Check(failures.Count == (fault == "view-return" ? 1 : 0), fault + " only deliberate view-return error propagates");
                Check(Get(scheduler,"feedbackSource") == null && Get(health,"OnDamageResolved") is Delegate callbacks && callbacks.GetInvocationList().Length == (fault == "observer" ? 2 : 1), fault + " temporary feedback hook removed");
                observations.Add(new { fault, hp=health.CurrentHp, hits, failures });
            }
            Check(expectedLogs.Count == 8, "eight deliberate observer / VFX errors are logged");
        }
        catch (Exception error) { failure = error.ToString(); }
        finally
        {
            Application.logMessageReceived -= Logs;
            foreach (var root in roots) if (root != null) UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(preview);
            foreach (var pair in diagnostics) pair.Key.SetValue(null, pair.Value);
        }
        var report = new { status=failure == null ? "PASS" : "FAIL", checks, observations, expectedLogs, failure, previewClosed=!preview.IsValid() };
        File.WriteAllText(Path.Combine(directory,"CompletionFaults.json"),JsonConvert.SerializeObject(report,Formatting.Indented));
        if (failure != null) throw new InvalidOperationException(failure);
        return "PASS: " + checks.Count + " completion checks; preview resources disposed.";
    }
}
