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

// Real Health callbacks while parry-stunned. Own fixtures and isolated account are always returned.
[InitializeOnLoad]
public static class StunnedHitReactionVerifier
{
    const string Key = "Overburst.StunnedHitReactionVerifier.";
    const string StartScene = "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity";
    static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<object> checks = new List<object>(), cases = new List<object>();
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static bool subscribed, ownsReload, ownsRefresh;
    static int frame = -1;
    static string Output => SessionState.GetString(Key + "output", "");

    static StunnedHitReactionVerifier() { if (!string.IsNullOrEmpty(Output)) Subscribe(); }
    static void Subscribe()
    {
        if (subscribed) return;
        subscribed = true; EditorApplication.update += Pump;
        EditorApplication.playModeStateChanged += State;
        AssemblyReloadEvents.beforeAssemblyReload += Reloading;
    }
    static void Unsubscribe()
    {
        subscribed = false; EditorApplication.update -= Pump;
        EditorApplication.playModeStateChanged -= State; AssemblyReloadEvents.beforeAssemblyReload -= Reloading;
    }
    static void Check(bool pass, string name)
    { checks.Add(new { name, pass }); if (!pass) throw new InvalidOperationException(name); }
    static bool Near(float a, float b, float tolerance = .01f) => Mathf.Abs(a - b) <= tolerance;
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Fields).GetValue(owner);
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Fields).SetValue(owner, value);
    static void Write(string file, object value) => File.WriteAllText(Path.Combine(Output, file), JsonConvert.SerializeObject(value, Formatting.Indented));
    static IEnumerator Wait(float seconds) { float until = Time.unscaledTime + seconds; while (Time.unscaledTime < until) yield return null; }

    public static void StartIsolated(string directory)
    {
        if (!string.IsNullOrEmpty(Output) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) || IsolatedSavePlayGuard.RequiresAccountChoice)
            throw new InvalidOperationException("The shared Editor and account guard must be returned before starting.");
        directory = IsolatedSavePlayGuard.ValidateDirectory(directory); Directory.CreateDirectory(directory);
        checks.Clear(); cases.Clear(); frame = -1;
        SessionState.SetString(Key + "output", directory);
        SessionState.SetString(Key + "startScene", EditorSceneManager.playModeStartScene == null ? "" : AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetString(Key + "scenes", JsonConvert.SerializeObject(Enumerable.Range(0, SceneManager.sceneCount).Select(i => new
            { path = SceneManager.GetSceneAt(i).path, dirty = SceneManager.GetSceneAt(i).isDirty, roots = SceneManager.GetSceneAt(i).rootCount }).ToArray()));
        SessionState.SetBool(Key + "background", Application.runInBackground);
        SessionState.SetBool(Key + "started", false); SessionState.SetBool(Key + "returnPending", false);
        SessionState.SetFloat(Key + "deadline", (float)EditorApplication.timeSinceStartup + 150f);
        Subscribe();
        try
        {
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(StartScene);
            Application.runInBackground = true;
            IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(directory, "Save"));
            Write("progress.json", new { status = "BOOTING" });
        }
        catch (Exception e) { Finish("START_FAILED", e); }
    }
    static bool OwnPlay => EditorApplication.isPlaying && !string.IsNullOrEmpty(Output) && !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
        && string.Equals(Path.GetFullPath(IsolatedSavePlayGuard.ActiveDirectory), Path.GetFullPath(Path.Combine(Output, "Save")), StringComparison.OrdinalIgnoreCase);
    static void HoldEditor()
    {
        if (!ownsReload) { EditorApplication.LockReloadAssemblies(); ownsReload = true; }
        if (!ownsRefresh) { AssetDatabase.DisallowAutoRefresh(); ownsRefresh = true; }
    }
    static void Pump()
    {
        if (string.IsNullOrEmpty(Output)) { Unsubscribe(); return; }
        if (SessionState.GetBool(Key + "returnPending", false))
        {
            if (OwnPlay) EditorApplication.ExitPlaymode();
            else ReturnAccount();
            return;
        }
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "deadline", 0)) { Finish("TIMEOUT", null); return; }
        if (!OwnPlay) return;
        HoldEditor();
        if (!Overburst.Persistence.AccountBootstrap.Ready || PlayerContext.Instance?.CurrentActor == null) return;
        EditorApplication.QueuePlayerLoopUpdate();
        if (!SessionState.GetBool(Key + "started", false))
        {
            SessionState.SetBool(Key + "started", true); checks.Clear(); cases.Clear();
            SessionState.SetFloat(Key + "deadline", (float)EditorApplication.timeSinceStartup + 240f);
            stack.Push(Run());
        }
        if (frame == Time.frameCount) return; frame = Time.frameCount;
        try
        {
            while (stack.Count > 0)
            {
                var current = stack.Peek();
                if (current.MoveNext()) { if (current.Current is IEnumerator nested) { stack.Push(nested); continue; } return; }
                (stack.Pop() as IDisposable)?.Dispose();
            }
            Finish("PASS", null);
        }
        catch (Exception e) { Finish("FAIL", e); }
    }
    static void Finish(string status, Exception error)
    {
        if (string.IsNullOrEmpty(Output) || SessionState.GetBool(Key + "returnPending", false)) return;
        try { while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose(); }
        finally
        {
            if (ownsRefresh) { ownsRefresh = false; AssetDatabase.AllowAutoRefresh(); }
            if (ownsReload) { ownsReload = false; EditorApplication.UnlockReloadAssemblies(); }
            Write("play-result.json", new { status, checks, cases, error = error?.ToString() });
            SessionState.SetBool(Key + "returnPending", true);
            SessionState.SetFloat(Key + "returnAfter", (float)EditorApplication.timeSinceStartup + .25f);
            SessionState.SetFloat(Key + "returnDeadline", (float)EditorApplication.timeSinceStartup + 120f);
            if (OwnPlay) EditorApplication.ExitPlaymode();
        }
    }
    static void State(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode && OwnPlay) HoldEditor();
        if (state == PlayModeStateChange.ExitingPlayMode && !SessionState.GetBool(Key + "returnPending", false)) Finish("INTERRUPTED", null);
        if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + "returnPending", false))
            SessionState.SetFloat(Key + "returnAfter", (float)EditorApplication.timeSinceStartup + .25f);
    }
    static void Reloading()
    {
        if (!SessionState.GetBool(Key + "started", false) && EditorApplication.isPlayingOrWillChangePlaymode)
            return; // No fixtures exist yet; resume the owned boot after its expected reload/compile.
        if (!SessionState.GetBool(Key + "returnPending", false)) Finish("RELOADING", null);
    }
    static void ReturnAccount()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "returnAfter", 0)) return;
        string expected = Path.GetFullPath(Path.Combine(Output, "Save"));
        string current = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "";
        string active = IsolatedSavePlayGuard.ActiveDirectory;
        string prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "");
        string savedStart = SessionState.GetString(Key + "startScene", "");
        string currentStart = EditorSceneManager.playModeStartScene == null ? "" : AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene);
        bool foreignStart = currentStart != StartScene && currentStart != savedStart;
        bool Foreign(string path) => !string.IsNullOrEmpty(path) && !string.Equals(Path.GetFullPath(path), expected, StringComparison.OrdinalIgnoreCase);
        if (Foreign(current) || Foreign(active) || Foreign(prepared) || foreignStart)
        {
            Write("return.json", new { status = "DEFERRED", reason = "another account preparation owns the shared Editor" });
            if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "returnDeadline", 0)) Unsubscribe();
            return;
        }
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(savedStart) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(savedStart);
        Application.runInBackground = SessionState.GetBool(Key + "background", false);
        IsolatedSavePlayGuard.UseRealAccount();
        var scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(i => new
            { path = SceneManager.GetSceneAt(i).path, dirty = SceneManager.GetSceneAt(i).isDirty, roots = SceneManager.GetSceneAt(i).rootCount }).ToArray();
        bool scenePreserved = JsonConvert.SerializeObject(scenes) == SessionState.GetString(Key + "scenes", "");
        Write("return.json", new { status = scenePreserved && !IsolatedSavePlayGuard.RequiresAccountChoice ? "PASS" : "FAIL",
            scenePreserved, scenes, saveDirectory = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),
            IsolatedSavePlayGuard.RequiresAccountChoice, IsolatedSavePlayGuard.ActiveDirectory,
            prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""),
            expires = SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""), startScene = savedStart,
            pid = System.Diagnostics.Process.GetCurrentProcess().Id, project = Application.dataPath });
        foreach (string suffix in new[] { "output", "startScene", "scenes" }) SessionState.EraseString(Key + suffix);
        foreach (string suffix in new[] { "started", "returnPending", "background" }) SessionState.EraseBool(Key + suffix);
        foreach (string suffix in new[] { "deadline", "returnAfter", "returnDeadline" }) SessionState.EraseFloat(Key + suffix);
        Unsubscribe();
    }

    public static void Contracts(string directory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Contracts require idle EditMode.");
        directory = IsolatedSavePlayGuard.ValidateDirectory(directory); Directory.CreateDirectory(directory);
        var rows = new List<object>();
        foreach (var id in AssetDatabase.FindAssets("t:EnemyDefinition", new[] { "Assets/ProjectOverburst/Resources/Enemies" }))
        {
            var def = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(id));
            var profile = def.AnimationProfile;
            if (!HasParryClips(def)) continue;
            RuntimeAnimatorController runtime = profile.RuntimeController;
            while (runtime is AnimatorOverrideController over) runtime = over.runtimeAnimatorController;
            var controller = runtime as UnityEditor.Animations.AnimatorController;
            if (controller == null) throw new InvalidOperationException(def.EnemyId + " controller");
            var states = controller.layers[0].stateMachine.states.Select(s => s.state).ToArray();
            bool valid = new[] { "Get_hit", EnemyAnimationBridge.ParryCollapseStateName,
                EnemyAnimationBridge.StunnedLoopStateName, EnemyAnimationBridge.StunRecoverStateName }
                .All(name => states.Any(state => state.name == name && state.motion != null));
            rows.Add(new { def.EnemyId, valid, hit = profile.Hit == null ? 0f : profile.Hit.length });
            if (!valid) throw new InvalidOperationException(def.EnemyId + " missing hit/stun states");
        }
        if (rows.Count == 0) throw new InvalidOperationException("No dedicated parry actors");
        File.WriteAllText(Path.Combine(directory, "contracts.json"), JsonConvert.SerializeObject(new { status = "PASS", rows }, Formatting.Indented));
    }

    static bool HasParryClips(EnemyDefinition def)
    {
        if (def == null || def.Grade.GradeType == EnemyGradeType.Boss || def.AnimationProfile == null) return false;
        EnemyAnimationRoleResolver.ResolveParryDurations(def.AnimationProfile.RuntimeController, def.AnimationProfile,
            out float collapse, out float loop, out float recover);
        return collapse > 0 && loop > 0 && recover > 0;
    }
    static bool InState(EnemyActor enemy, string name) => enemy.Animator.GetCurrentAnimatorStateInfo(0).IsName(name);
    static IEnumerator Until(EnemyActor enemy, string name, float timeout)
    {
        float deadline = Time.unscaledTime + timeout;
        while (!InState(enemy, name))
        { Check(Time.unscaledTime < deadline, enemy.Definition.EnemyId + " reaches " + name); yield return null; }
    }
    static IEnumerator Finished(EnemyActor enemy, float timeout)
    {
        float deadline = Time.unscaledTime + timeout;
        while (enemy.AnimationBridge.IsParryStunAnimating)
        { Check(Time.unscaledTime < deadline, "bounded reaction completion"); yield return null; }
    }
    static IEnumerator Run()
    {
        var player = PlayerInputFacade.Current; var actor = PlayerContext.GetOrCreate().CurrentActor;
        var ui = EnemyThemeTrialHarness.Current; EnemySpawnService spawn = null;
        var leases = new List<EnemyActor>(); float oldHp = actor.Health.MaxHp;
        int sequence = 962000;
        var parry = typeof(PlayerParryController).GetMethod("CancelAndReact", BindingFlags.NonPublic | BindingFlags.Static);
        EnemyActor Spawn(EnemyDefinition def)
        {
            Vector3 pos = player.transform.position + Vector3.right * 2.6f;
            Check(Physics.Raycast(pos + Vector3.up * 4, Vector3.down, out var floor, 9f,
                LayerMask.GetMask("Default", "Ground", "Environment")), "fixture ground");
            Check(spawn.TrySpawn(new EnemySpawnRequest(def, floor.point + Vector3.up * .035f,
                Quaternion.LookRotation(Vector3.left), player.transform), out var enemy), "pooled product actor");
            leases.Add(enemy); enemy.AI.enabled = false; enemy.AbilityController.Cancel(); enemy.Movement.StopMovement();
            enemy.Health.SetMaxHp(100000, true); enemy.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return enemy;
        }
        void Release(EnemyActor enemy) { if (enemy != null && enemy.IsLeased) spawn.Release(enemy); leases.Remove(enemy); }
        void Perfect(EnemyActor enemy) { parry.Invoke(null, new object[] { enemy, player.transform.position, ParryGrade.Perfect }); }
        float End(EnemyActor enemy) => Time.time + enemy.GetComponent<EnemyMovementReaction>().ParryStunRemaining;
        void Hit(EnemyActor enemy, float damage = 1, bool dot = false)
        {
            var info = new DamageInfo(damage, enemy.transform.position, player.gameObject, Vector3.right, 0,
                sourceAttackSequenceId: ++sequence, playerAttackKind: PlayerAttackKind.Weak);
            info.isDamageOverTime = dot;
            enemy.Health.TakeDamage(info);
        }
        void Locked(EnemyActor enemy, float expectedEnd)
        {
            Check(enemy.AnimationBridge.BlocksAttackStart && !enemy.AnimationBridge.AllowsMovement(EnemyLocomotionMode.Run), "hit preserves action lock");
            Check(enemy.GetComponent<EnemyMovementReaction>().BlocksAttack && !enemy.AbilityController.IsExecuting, "stun blocks abilities including elite");
            Check(Near(End(enemy), expectedEnd, .025f), "hit never extends parry deadline");
        }
        try
        {
            Check(player != null && ui != null && parry != null, "product fixture dependencies");
            Check(Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase), "isolated account");
            float bootUntil = Time.unscaledTime + 30;
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
                || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
            { Check(Time.unscaledTime < bootUntil, "product hideout finishes boot"); yield return null; }
            actor.Health.SetMaxHp(1000000, true);
            if (!ui.InArena) ui.ToggleArena(); Check(ui.InArena, "product trial arena entered");
            Physics.SyncTransforms(); yield return Wait(.8f);
            Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn), "product pool service");
            foreach (var table in ui.tables) Check(spawn.RegisterAdditionalCatalog(table.Catalog, out _), "catalog registration");
            var defs = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).Where(d => d != null).Distinct().ToArray();
            var complete = defs.Where(HasParryClips).ToArray();
            var first = complete.Where(d => d.Grade.GradeType == EnemyGradeType.Normal)
                .OrderBy(d => d.AnimationProfile.Hit == null ? 0 : d.AnimationProfile.Hit.length).First();
            var selected = new[] {
                first,
                complete.Where(d => d.Grade.GradeType == EnemyGradeType.Normal && d != first)
                    .OrderByDescending(d => d.AnimationProfile.ParryCollapse == null).First(),
                complete.First(d => d.Grade.GradeType == EnemyGradeType.Elite)
            };
            foreach (var def in selected)
            {
                var enemy = Spawn(def); yield return Wait(.2f); Perfect(enemy);
                float end = End(enemy); yield return Until(enemy, EnemyAnimationBridge.StunnedLoopStateName, 5);
                yield return Wait(.1f); int flinches = enemy.GetComponent<EnemyHitResponseCoordinator>().FlinchCount;
                Hit(enemy); yield return Until(enemy, "Get_hit", .8f); Locked(enemy, end);
                Check(enemy.GetComponent<EnemyHitResponseCoordinator>().FlinchCount == flinches + 1, "real Health callback owns hit");
                yield return Wait(.1f);
                ScreenCapture.CaptureScreenshot(Path.Combine(Output, def.EnemyId + "-stunned-hit.png"));
                float settleUntil = Time.unscaledTime + 3;
                while (!InState(enemy, EnemyAnimationBridge.StunnedLoopStateName) && !InState(enemy, EnemyAnimationBridge.StunRecoverStateName))
                { Check(Time.unscaledTime < settleUntil, "hit returns to remaining stun or original recovery"); yield return null; }
                bool returnSeen = InState(enemy, EnemyAnimationBridge.StunnedLoopStateName);
                if (returnSeen)
                {
                    Locked(enemy, end); Hit(enemy, 1, true); yield return Wait(.08f);
                    Check(InState(enemy, EnemyAnimationBridge.StunnedLoopStateName), "DOT keeps stun pose");
                }
                yield return Until(enemy, EnemyAnimationBridge.StunRecoverStateName, 3);
                Check(!enemy.GetComponent<EnemyMovementReaction>().IsParryStunned && Time.time - end < .4f, "recover uses original deadline");
                Check(enemy.AnimationBridge.BlocksAttackStart, "recover keeps action lock");
                yield return Finished(enemy, 4);
                cases.Add(new { fixture = "hit then remaining stun", def.EnemyId, end, returnSeen });
                Write("progress.json", new { status = "RUNNING", completed = cases.Count }); Release(enemy);
            }
            Check(cases.Count == 3 && cases.Any(c => (bool)c.GetType().GetProperty("returnSeen").GetValue(c)), "at least one real hit completes into remaining stun");

            var sample = selected[2]; var repeated = Spawn(sample); yield return Wait(.2f); Perfect(repeated);
            float repeatedEnd = End(repeated); yield return Until(repeated, EnemyAnimationBridge.StunnedLoopStateName, 5);
            int hits = 0;
            while (repeated.GetComponent<EnemyMovementReaction>().ParryStunRemaining > .2f)
            { Hit(repeated); hits++; yield return Wait(.21f); if (repeated.GetComponent<EnemyMovementReaction>().IsParryStunned) Locked(repeated, repeatedEnd); }
            yield return Until(repeated, EnemyAnimationBridge.StunRecoverStateName, 1);
            Check(Time.time - repeatedEnd < .4f, "continuous hits cannot postpone recovery");
            yield return Finished(repeated, 4);
            cases.Add(new { fixture = "continuous elite hits", hits }); Release(repeated);

            var frozen = Spawn(sample); yield return Wait(.2f); Perfect(frozen);
            yield return Until(frozen, EnemyAnimationBridge.StunnedLoopStateName, 5); float freezeEnd = End(frozen);
            Hit(frozen); yield return Until(frozen, "Get_hit", .8f); frozen.AnimationBridge.SetFrozen(true);
            Check(frozen.Animator.speed == 0 && !frozen.AnimationBridge.IsParryStunAnimating, "freeze interrupts hit/stun animation");
            yield return Wait(.2f); frozen.AnimationBridge.SetFrozen(false);
            yield return Until(frozen, EnemyAnimationBridge.StunnedLoopStateName, .8f); Locked(frozen, freezeEnd);
            Hit(frozen); yield return Until(frozen, "Get_hit", .8f); Locked(frozen, freezeEnd);
            int poolId = frozen.GetInstanceID(); Release(frozen); yield return Wait(.1f);
            var reused = Spawn(sample); yield return Wait(.15f);
            Check(reused.GetInstanceID() == poolId && !reused.AnimationBridge.IsParryStunAnimating
                && !reused.GetComponent<EnemyMovementReaction>().IsParryStunned && Near(reused.Animator.speed, 1), "same pooled actor clears interrupted hit/stun");
            Perfect(reused); yield return Until(reused, EnemyAnimationBridge.StunnedLoopStateName, 5);
            Hit(reused); yield return Until(reused, "Get_hit", .8f); reused.Health.SetMaxHp(1, true); Hit(reused, 10000);
            yield return Wait(.1f); Check(reused.Health.IsDead && !reused.AnimationBridge.IsParryStunAnimating, "death overrides interrupted stun");
            cases.Add(new { fixture = "freeze pool reuse and death", poolId }); Release(reused);

            var normal = Spawn(selected[1]); yield return Wait(.2f);
            parry.Invoke(null, new object[] { normal, player.transform.position, ParryGrade.Normal });
            yield return Until(normal, EnemyAnimationBridge.ParryCollapseStateName, .8f);
            float holdUntil = Time.unscaledTime + 5;
            while (normal.Animator.speed != 0) { Check(Time.unscaledTime < holdUntil, "normal hold arrives"); yield return null; }
            Hit(normal); yield return Wait(.05f);
            Check(normal.AnimationBridge.IsNormalParryReacting && !normal.GetComponent<EnemyMovementReaction>().IsParryStunned
                && normal.Animator.speed == 0 && !InState(normal, "Get_hit"), "normal collapsed hold retained");
            yield return Until(normal, EnemyAnimationBridge.StunRecoverStateName, 1);
            yield return Finished(normal, 4); Check(Near(normal.Animator.speed, 1), "normal clock restored");
            cases.Add(new { fixture = "normal parry preserved" }); Release(normal);

            var fallback = defs.First(d => d.Grade.GradeType != EnemyGradeType.Boss && d.AnimationProfile.ParryCollapse == null
                && d.EnemyId.Contains("Reaper"));
            var basic = Spawn(fallback); yield return Wait(.2f); Perfect(basic); float basicEnd = End(basic);
            yield return Wait(.25f); Hit(basic); yield return Until(basic, "Get_hit", .8f);
            Check(basic.GetComponent<EnemyMovementReaction>().IsParryStunned && Near(End(basic), basicEnd, .025f), "code stun fallback keeps hit and deadline");
            cases.Add(new { fixture = "no dedicated clips", fallback.EnemyId }); Release(basic);
        }
        finally
        {
            foreach (var enemy in leases) if (enemy != null && enemy.IsLeased) spawn?.Release(enemy);
            actor.Health.SetMaxHp(oldHp, true);
        }
    }
}
