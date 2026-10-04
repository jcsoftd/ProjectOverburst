using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Real product boot, attack-area selection, melee damage, chaos rendering, Escape and re-entry.</summary>
[InitializeOnLoad]
public static class HideoutSpinningCatVerifier
{
    const string Key = "Overburst.OiiaCatVerifier.";
    static int Phase {get => SessionState.GetInt(Key + "phase", 0); set => SessionState.SetInt(Key + "phase", value);}
    static int Cycle {get => SessionState.GetInt(Key + "cycle", 1); set => SessionState.SetInt(Key + "cycle", value);}
    static string Account => Path.GetFullPath(Path.Combine(HideoutSpinningCatBuilder.Output, "IsolatedAccount"));
    static HideoutSpinningCatEasterEgg cat;
    static PlayerActorRuntime player;
    static Keyboard keyboard;
    static double until;
    static int frameWait;
    static bool capturing;
    static readonly List<string> checks = new List<string>();

    static HideoutSpinningCatVerifier()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += State;
        Application.logMessageReceived += Log;
    }

    public static string Begin()
    {
        HideoutSpinningCatBuilder.RequireIdle();
        if (Phase != 0) throw new InvalidOperationException("Verifier already running.");
        var boot = SceneManager.GetSceneByName(PersistentSceneFlow.PersistentSceneName);
        if (!boot.isLoaded) throw new InvalidOperationException("PersistentScene required.");
        Directory.CreateDirectory(HideoutSpinningCatBuilder.Output);
        SessionState.SetString(Key + "before", HideoutSpinningCatBuilder.EditorSnapshot());
        SessionState.SetString(Key + "start", EditorSceneManager.playModeStartScene == null ? "" : AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetString(Key + "real", RealHash());
        SessionState.SetString(Key + "errors", "[]");
        AssetDatabase.DisallowAutoRefresh(); SessionState.SetBool(Key + "refresh", true);
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(boot.path);
        Cycle = 1; Phase = 1; Deadline();
        try {IsolatedSavePlayGuard.EnterIsolatedPlay(Account);}
        catch {Phase = 9; throw;}
        return "OIIA two isolated boots scheduled; verifier owns only its isolated save and transient input device.";
    }

    static void Deadline() => SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + 150).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
    static bool OwnsAccount() => string.Equals(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable), Account, StringComparison.OrdinalIgnoreCase);
    static void State(PlayModeStateChange state)
    {
        if (Phase == 0) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            SessionState.SetBool(Key + "background", Application.runInBackground);
            SessionState.SetBool(Key + "backgroundOwned", true); Application.runInBackground = true;
            EditorApplication.LockReloadAssemblies(); SessionState.SetBool(Key + "reload", true);
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            CleanupRuntime();
            if (Phase != 8 && Phase != 9) {Write("failure.json", new {status = "FAIL", reason = "Play cancelled", cycle = Cycle}); Phase = 9;}
        }
    }

    static void Tick()
    {
        if (Phase == 0 || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            if (Phase == 9) {ReturnAccount(); return;}
            double deadline = double.Parse(SessionState.GetString(Key + "deadline", "0"), System.Globalization.CultureInfo.InvariantCulture);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("OIIA verifier timed out.");
            if (!EditorApplication.isPlaying)
            {
                if (Phase != 8 || EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))) return;
                IsolatedSavePlayGuard.UseRealAccount();
                if (Cycle == 1) {Cycle = 2; Phase = 1; Deadline(); IsolatedSavePlayGuard.EnterIsolatedPlay(Account);}
                else {Phase = 9; ReturnAccount();}
                return;
            }
            if (!OwnsAccount()) throw new InvalidOperationException("Another operation owns the active account.");
            var flow = Object.FindFirstObjectByType<PersistentSceneFlow>(FindObjectsInactive.Include);
            if (flow != null && !flow.gameObject.activeInHierarchy) flow.gameObject.SetActive(true);
            EditorApplication.QueuePlayerLoopUpdate();
            if (Phase == 1)
            {
                if (!AccountBootstrap.Ready || !WorldSessionState.IsHideout || flow == null || flow.IsSwitching || PlayerContext.Instance?.CurrentActor == null) return;
                if (++frameWait < 90) return;
                checks.Clear();
                cat = Object.FindFirstObjectByType<HideoutSpinningCatEasterEgg>();
                player = PlayerContext.Instance.CurrentActor.GetComponent<PlayerActorRuntime>();
                Check(cat != null && player != null, "Product Hideout contains cat and real player");
                Check(Object.FindObjectsByType<HideoutSpinningCatEasterEgg>(FindObjectsSortMode.None).Length == 1, "Exactly one permanent cat");
                Check(cat.HitCount == 0 && !cat.IsActive, "Scene entry resets hit count and presentation");
                Check(cat.GetComponent<EnemyActor>() == null, "No enemy AI or reward actor");
                Check(cat.GetComponentsInChildren<Transform>(true).All(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0), "No missing scripts");
                var so = new SerializedObject(cat);
                var clip = (AnimationClip)so.FindProperty("spinClip").objectReferenceValue;
                var bindings = AnimationUtility.GetCurveBindings(clip);
                Check(bindings.Any(b => b.propertyName.StartsWith("blendShape.")) && bindings.Any(b => b.propertyName.Contains("Rotation")), "Original transform and morph curves imported");
                Check(so.FindProperty("hitsToActivate").intValue == 50, "Threshold exactly 50");
                Check(so.FindProperty("music").objectReferenceValue == null, "Unavailable remix is not substituted");
                HitThroughAttackArea(1);
                Check(cat.HitCount == 1 && !cat.IsActive, "Actual attack area and damage resolver register one hit");
                var health = cat.GetComponent<CombatHealth>();
                var info = new DamageInfo(5, cat.transform.position, player.gameObject, sourceAttackSequenceId: 10001, playerAttackKind: PlayerAttackKind.Weak);
                health.TakeDamage(info);
                Check(cat.HitCount == 1, "Same sequence/phase cannot double count");
                info.sourceAttackSequenceId = 99999; info.isDamageOverTime = true; health.TakeDamage(info);
                Check(cat.HitCount == 1, "Damage ticks do not count");
                info.isDamageOverTime = false; info.triggersOnHitEffects = false; health.TakeDamage(info);
                Check(cat.HitCount == 1, "Secondary effect does not count");
                for (int i = 2; i <= 49; i++) HitThroughAttackArea(i);
                Check(cat.HitCount == 49 && !cat.IsActive, "49 contacts stay idle");
                HitThroughAttackArea(50);
                Check(cat.IsActive && cat.ActivationCount == 1, "50th contact starts chaos once");
                Check(GameplayInputBlocker.IsGameplayInputBlocked && !PlayerInputFacade.Current.IsGameplayEnabled, "Gameplay blocked while chaos owns screen");
                info.isDamageOverTime = false; info.triggersOnHitEffects = true; info.damage = 2000000; info.sourceAttackSequenceId = 99998;
                health.TakeDamage(info);
                Check(!health.IsDead && health.CurrentHp >= 1 && cat.ActivationCount == 1, "Cat cannot die or stack chaos");
                until = EditorApplication.timeSinceStartup + 4; Phase = 2;
            }
            else if (Phase == 2 && EditorApplication.timeSinceStartup >= until && !capturing)
            {
                Check(cat.VisibleCatCount == 12, "Population reaches bounded 12 cats");
                Check(Resources.FindObjectsOfTypeAll<RenderTexture>().Any(t => t.name == "OIIA stage (temporary)" && t.IsCreated()), "Screen stage texture created");
                capturing = true; player.StartCoroutine(Capture());
            }
            else if (Phase == 3)
            {
                keyboard = InputSystem.AddDevice<Keyboard>("OiiaVerifierKeyboard");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.Escape));
                until = EditorApplication.timeSinceStartup + 5; Phase = 4;
            }
            else if (Phase == 4)
            {
                if (cat.IsActive) {if (EditorApplication.timeSinceStartup > until) throw new Exception("Escape did not close chaos."); return;}
                Check(!OverburstGameMenu.IsOpen, "Escape closes chaos without opening pause menu");
                if (keyboard != null) {InputSystem.RemoveDevice(keyboard); keyboard = null;}
                Check(!GameplayInputBlocker.IsGameplayInputBlocked && PlayerInputFacade.Current.IsGameplayEnabled, "Escape restores gameplay input");
                Check(cat.HitCount == 0, "Escape resets the 50-hit counter");
                for (int i = 1; i <= 50; i++) HitThroughAttackArea(i + 100);
                Check(cat.IsActive && cat.ActivationCount == 2, "Another 50 hits can retrigger");
                cat.gameObject.SetActive(false);
                Check(!cat.IsActive && !GameplayInputBlocker.IsGameplayInputBlocked && PlayerInputFacade.Current.IsGameplayEnabled, "Disable returns presentation and input");
                cat.gameObject.SetActive(true);
                Check(cat.HitCount == 0 && cat.GetComponent<CombatHealth>().IsDeathFromDamagePrevented, "Re-enable resets contacts and restores immortality");
                until = EditorApplication.timeSinceStartup + .5; Phase = 5;
            }
            else if (Phase == 5 && EditorApplication.timeSinceStartup >= until)
            {
                Check(!Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Any(c => c.name == "Cat Camera"), "Transient camera destroyed");
                Check(!Resources.FindObjectsOfTypeAll<RenderTexture>().Any(t => t.name == "OIIA stage (temporary)"), "Transient render texture destroyed");
                var errors = JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "errors", "[]"));
                Check(errors.Count == 0, "No runtime errors");
                Write("play_cycle_" + Cycle + ".json", new {status = "PASS", checks, errors, cycle = Cycle, audio = "NOT_RUN_MISSING_CLIP", userFeel = "NOT_RUN", playerBuild = "NOT_RUN"});
                Phase = 8; EditorApplication.ExitPlaymode();
            }
        }
        catch (Exception exception)
        {
            Write("failure.json", new {status = "FAIL", error = exception.ToString(), cycle = Cycle, checks});
            Phase = 9;
            if (EditorApplication.isPlaying && OwnsAccount()) EditorApplication.ExitPlaymode();
        }
    }

    static void HitThroughAttackArea(int sequence)
    {
        var controller = player.GetComponent<CharacterController>();
        bool enabled = controller != null && controller.enabled;
        if (enabled) controller.enabled = false;
        player.transform.position = cat.transform.position - Vector3.forward * .85f;
        player.transform.rotation = Quaternion.identity;
        if (enabled) controller.enabled = true;
        Physics.SyncTransforms();
        var pattern = ScriptableObject.CreateInstance<AttackPatternDefinition>();
        var executor = new AttackPhaseExecutor();
        try
        {
            pattern.shape = AttackAreaShape.Circle; pattern.fillMode = AttackFillMode.RadialExpand;
            var phase = new AttackPhaseData {attackPattern = pattern, startNormalizedTime = 0, endNormalizedTime = 1,
                geometry = new AttackGeometryData {rangeMultiplier = 1, angleMultiplier = 1, widthMultiplier = 1, vfxScaleMultiplier = 1},
                impact = new AttackImpactData {damageMultiplier = 1, triggersOnHitEffects = true}};
            int hits = 0;
            bool started = executor.Begin(new[] {phase}, player.transform, player.GetComponent<CombatTarget>(), Vector3.forward,
                new WeaponFinalStats {range = 1.5f, damage = 5, meleeSlashAngle = 180, meleeAttackRangeScale = 1},
                new MeleeWeaponBaseSettings {hitWidth = .5f, vfxScaleMultiplier = 1}, WeaponElement.None, 1, null, null, null,
                hit => {if (hit.TargetHealth != cat.GetComponent<CombatHealth>()) return; hits++;
                    MeleeDamageResolver.Apply(new MeleeDamageRequest(hit.Damageable, 5, phase.impact, 0, 0, 1,
                        hit.HitPoint, player.gameObject, Vector3.forward, 0, true, sourceAttackSequenceId: 10000 + sequence));});
            if (!started) throw new Exception("Attack phase could not begin.");
            executor.Tick(0); executor.Tick(.5f); executor.Tick(1);
            if (hits != 1) throw new Exception("Actual attack area expected one cat contact, got " + hits);
        }
        finally {executor.Cancel(); Object.Destroy(pattern);}
    }

    static System.Collections.IEnumerator Capture()
    {
        Texture2D texture = null;
        try
        {
            yield return new WaitForEndOfFrame();
            texture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(HideoutSpinningCatBuilder.Output, "chaos_" + Cycle + ".png"), texture.EncodeToPNG());
            Phase = 3;
        }
        finally {if (texture != null) Object.Destroy(texture); capturing = false;}
    }

    static void Check(bool condition, string name) {if (!condition) throw new Exception(name); checks.Add(name);}
    static void CleanupRuntime()
    {
        if (cat != null) cat.StopChaos();
        if (keyboard != null) {InputSystem.RemoveDevice(keyboard); keyboard = null;}
        cat = null; player = null; frameWait = 0; capturing = false;
        if (SessionState.GetBool(Key + "backgroundOwned", false)) {Application.runInBackground = SessionState.GetBool(Key + "background", false); SessionState.EraseBool(Key + "backgroundOwned");}
        if (SessionState.GetBool(Key + "reload", false)) {EditorApplication.UnlockReloadAssemblies(); SessionState.EraseBool(Key + "reload");}
    }

    static void ReturnAccount()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string current = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "";
        if (current.Length > 0 && !OwnsAccount()) return;
        if (IsolatedSavePlayGuard.ActiveDirectory.Length > 0 && !string.Equals(IsolatedSavePlayGuard.ActiveDirectory, Account, StringComparison.OrdinalIgnoreCase)) return;
        IsolatedSavePlayGuard.UseRealAccount();
        string start = SessionState.GetString(Key + "start", "");
        EditorSceneManager.playModeStartScene = start.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
        if (SessionState.GetBool(Key + "refresh", false)) {AssetDatabase.AllowAutoRefresh(); SessionState.EraseBool(Key + "refresh");}
        bool preserved = HideoutSpinningCatBuilder.EditorSnapshot() == SessionState.GetString(Key + "before", "");
        bool real = RealHash() == SessionState.GetString(Key + "real", "");
        Write("account_return.json", new {status = preserved && real && !IsolatedSavePlayGuard.RequiresAccountChoice ? "PASS" : "FAIL",
            editorPreserved = preserved, realAccountPreserved = real, blocked = IsolatedSavePlayGuard.RequiresAccountChoice,
            environment = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "", active = IsolatedSavePlayGuard.ActiveDirectory, cycles = Cycle});
        Phase = 0;
        foreach (string field in new[] {"before", "start", "real", "errors", "deadline"}) SessionState.EraseString(Key + field);
        SessionState.EraseInt(Key + "phase"); SessionState.EraseInt(Key + "cycle");
    }
    static void Log(string message, string trace, LogType type)
    {
        if (Phase == 0 || Phase == 9 || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
        var errors = JsonConvert.DeserializeObject<List<string>>(SessionState.GetString(Key + "errors", "[]"));
        if (errors.Count < 25 && !errors.Contains(message)) {errors.Add(message); SessionState.SetString(Key + "errors", JsonConvert.SerializeObject(errors));}
    }
    static string RealHash()
    {
        string root = Path.Combine(Application.persistentDataPath, "Account"); if (!Directory.Exists(root)) return "ABSENT";
        using (var hash = SHA256.Create()) return string.Join("|", Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(p => p)
            .Select(p => p.Substring(root.Length) + ":" + BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(p)))));
    }
    static void Write(string file, object result) => HideoutSpinningCatBuilder.Write(file, result);
}
