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

// Target-limited counter travel through the real character motor. Own fixtures and isolated account are always returned.
[InitializeOnLoad]
public static class ParryCounterDistanceVerifier
{
    const string Key = "Overburst.ParryCounterDistanceVerifier.";
    const string StartScene = "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity";
    static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<object> checks = new List<object>(), cases = new List<object>();
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static bool subscribed, ownsReload, ownsRefresh;
    static int frame = -1;
    static string Output => SessionState.GetString(Key + "output", "");

    static ParryCounterDistanceVerifier() { if (!string.IsNullOrEmpty(Output)) Subscribe(); }
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
            throw new InvalidOperationException("Idle native Editor required.");
        directory = IsolatedSavePlayGuard.ValidateDirectory(directory); Directory.CreateDirectory(directory);
        var rows = new List<object>();
        foreach (var item in new[] { (.5f,0f), (1f,0f), (2f,1f), (2.5f,1.5f), (3f,2f), (5f,2f) })
        {
            float actual = MeleeRuntime.ParryCounterTravelDistance(item.Item1);
            if (!Near(actual, item.Item2)) throw new InvalidOperationException("Minimum gap boundary " + item.Item1);
            rows.Add(new { distance = item.Item1, travel = actual, expected = item.Item2 });
        }
        var heavy = AssetDatabase.LoadAssetAtPath<MeleeHeavyAttackDefinition>(GreatswordHeavyParryBuilder.CounterDefinitionPath);
        if (heavy == null || !heavy.IsConfigured || heavy.attack.attackPhases.Length != 3)
            throw new InvalidOperationException("Counter definition and three phases");
        var weapon = AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>(GreatswordHeavyParryBuilder.WeaponDefinitionPath);
        var phases = MeleeRuntime.CreateParryCounterMovementPhases(weapon.comboDefinition.steps[2], heavy.attack, heavy.SafeDischargePhaseIndex, 2f);
        Vector3 moved = Vector3.zero;
        var executor = new AttackMovementExecutor(); executor.Begin(phases, Vector3.forward, delta => moved += delta);
        executor.Tick(phases[0].SafeEnd);
        if (!Near(moved.z, .45f)) throw new InvalidOperationException("Spin distance must equal combo3 actual travel");
        executor.Tick(phases[1].SafeEnd);
        if (!Near(moved.z, 2f)) throw new InvalidOperationException("Remaining distance finishes by landing impact");
        var shortPhases = MeleeRuntime.CreateParryCounterMovementPhases(weapon.comboDefinition.steps[2], heavy.attack, heavy.SafeDischargePhaseIndex, .25f);
        moved = Vector3.zero; executor.Begin(shortPhases, Vector3.forward, delta => moved += delta); executor.Tick(1f);
        if (!Near(moved.z, .25f) || shortPhases[1].distance > .0001f) throw new InvalidOperationException("Short budget never adds slam travel");
        if (heavy.attack.movementPhases.Length != 2 || !Near(heavy.attack.movementPhases[0].distance, phases[0].distance)
            || !Near(heavy.attack.movementPhases[1].distance, phases[1].distance)) throw new InvalidOperationException("Native counter movement asset matches runtime phases");
        File.WriteAllText(Path.Combine(directory, "contracts.json"), JsonConvert.SerializeObject(new {
            status = "PASS", rows, gap = MeleeRuntime.ParryCounterMinimumSeparation, phases, clip = heavy.attack.animationClip.length,
            runtime = typeof(MeleeRuntime).FullName, nativeCompileFailed = EditorUtility.scriptCompilationFailed
        }, Formatting.Indented));
    }

    static IEnumerator Run()
    {
        const string WeaponPath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset";
        var actor = PlayerContext.GetOrCreate().CurrentActor; var player = PlayerInputFacade.Current;
        var melee = actor.GetComponent<MeleeRuntime>(); var ui = EnemyThemeTrialHarness.Current;
        var parry = actor.GetComponent<PlayerParryController>();
        if (parry == null) parry = actor.gameObject.AddComponent<PlayerParryController>();
        EnemySpawnService spawn = null;
        var leases = new List<EnemyActor>(); var owned = new List<UnityEngine.Object>();
        var energy = actor.GetComponent<OverburstElementEnergy>();
        if (energy == null) { energy = actor.gameObject.AddComponent<OverburstElementEnergy>(); owned.Add(energy); }
        ItemData oldWeapon = actor.Equipment.CurrentWeaponItem, oldGem = actor.Equipment.EquippedElementGem;
        float oldHp = actor.Health.MaxHp;
        Vector3 origin = Vector3.zero, arenaOrigin = Vector3.zero;
        void TeleportPlayer(Vector3 position)
        {
            var controller = actor.CharacterController; bool enabled = controller != null && controller.enabled;
            if (controller != null) controller.enabled = false;
            try { player.transform.position = position; }
            finally { if (controller != null) controller.enabled = enabled; }
            actor.Movement.ResetMotionAfterTeleport(); actor.GetComponent<PlayerCombatFacingController>()?.ResetAfterTeleport();
            Physics.SyncTransforms();
        }
        void Reset()
        {
            melee.CancelCurrentAttackState(); parry.CloseWindow(); OverburstTimeEffectArbiter.ClearOwner(parry);
            foreach (var enemy in leases) if (enemy != null && enemy.IsLeased) spawn.Release(enemy);
            leases.Clear(); origin = arenaOrigin; TeleportPlayer(origin); actor.Health.SetMaxHp(1000000, true);
        }
        void Energy(float amount)
        {
            energy.BindWeapon(actor.Equipment.CurrentWeaponItem.runtimeInstanceId, actor.Equipment.ActiveElement,
                actor.Equipment.EquippedElementGem?.runtimeInstanceId, actor.Equipment.GemRevision);
            Set(energy, "<Amount>k__BackingField", amount);
        }
        EnemyActor Spawn(EnemyDefinition def, float distance)
        {
            Vector3 pos = origin + Vector3.forward * distance;
            Check(Physics.Raycast(pos + Vector3.up * 4, Vector3.down, out var floor, 9f,
                LayerMask.GetMask("Default", "Ground", "Environment")), "fixture ground");
            Check(spawn.TrySpawn(new EnemySpawnRequest(def, floor.point + Vector3.up * .035f,
                Quaternion.LookRotation(Vector3.back), player.transform), out var enemy), "pooled target");
            leases.Add(enemy); enemy.AI.enabled = false; enemy.AbilityController.Cancel(); enemy.Movement.StopMovement();
            enemy.Health.SetMaxHp(1000000, true); enemy.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return enemy;
        }
        float Travel(Vector3 start, Vector3 direction) => Vector3.Dot(player.transform.position - start, direction);
        IEnumerator Complete(float timeout = 15f)
        {
            float until = Time.unscaledTime + timeout;
            while (melee.IsAttackInProgress) { Check(Time.unscaledTime < until, "action completes"); yield return null; }
        }
        IEnumerator WaitForCounter()
        {
            float until = Time.unscaledTime + 12f;
            while (melee.IsHeavyParryMotionActive)
            { Check(Time.unscaledTime < until, "parry and bridge complete"); Check(Mathf.Abs(Travel(origin, Vector3.forward)) < .015f, "parry and bridge stay stationary"); yield return null; }
            Check(Field<bool>(melee, "heavyParryCounterMovement"), "counter travel begins only after bridge");
        }
        try
        {
            Check(ui != null && melee != null && player != null, "product dependencies");
            Check(Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Path.GetFullPath(Output), StringComparison.OrdinalIgnoreCase), "isolated account");
            float boot = Time.unscaledTime + 30f;
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
                || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
            { Check(Time.unscaledTime < boot, "hideout finishes boot"); yield return null; }
            actor.Health.SetMaxHp(1000000, true);
            if (!ui.InArena) ui.ToggleArena(); Check(ui.InArena, "trial arena entered"); yield return Wait(.8f);
            arenaOrigin = origin = player.transform.position;
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
            melee.SetManualInputEnabled(true);
            Check(actor.Equipment.EquipWeaponItem(new ItemData(AssetDatabase.LoadAssetAtPath<WeaponItemData>(WeaponPath), 1, ItemGrade.Common)), "equip greatsword");
            var fire = AssetDatabase.LoadAssetAtPath<ElementGemItemData>("Assets/ProjectOverburst/Resources/Items/ElementGems/EG_Fire_Common.asset");
            typeof(PlayerEquipment).GetMethod("SetElementGem", Fields).Invoke(actor.Equipment, new object[] { new ItemData(fire, 1, ItemGrade.Common) });
            Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn), "product spawn service");
            foreach (var table in ui.tables) Check(spawn.RegisterAdditionalCatalog(table.Catalog, out _), "catalog registration");
            var def = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition)
                .First(d => d != null && d.Grade.GradeType == EnemyGradeType.Normal && d.EnemyId.Contains("SkeletonKnight_Medium"));

            Reset(); Energy(50); yield return Wait(.2f);
            Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "normal heavy accepted");
            yield return Wait(.3f);
            Check(Travel(origin, Vector3.forward) > .05f, "ordinary heavy advances immediately on its authored curve");
            yield return Complete();
            float normalTravel = Travel(origin, Vector3.forward);
            Check(Near(normalTravel, 2f, .1f), "ordinary heavy retains total 2m travel: " + normalTravel);
            cases.Add(new { fixture = "ordinary heavy restored", normalTravel });

            foreach (float distance in new[] { .5f, 1f, 2f, 2.5f, 3f, 5f })
            {
                Reset(); Energy(50); var enemy = Spawn(def, distance); yield return Wait(.2f);
                // Very close fixtures may be separated by the motor before the attack. Measure from the stable actual start.
                origin = player.transform.position;
                Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "distance heavy accepted");
                yield return Wait(.15f); origin = player.transform.position;
                melee.NotifyHeavyParried(Field<int>(melee, "activeActionId"), ParryGrade.Normal, enemy);
                yield return WaitForCounter();
                float budget = Field<float>(melee, "heavyParryCounterRemainingTravel");
                CombatTargetVolume owner = actor.GetComponent<CombatTarget>().CurrentVolume, body = enemy.GetComponent<CombatTarget>().CurrentVolume;
                float separation = Vector2.Distance(new Vector2(owner.Center.x, owner.Center.z), new Vector2(body.Center.x, body.Center.z));
                float expected = MeleeRuntime.ParryCounterTravelDistance(separation);
                Check(Near(budget, expected, .08f), "budget uses current target and 1m gap");
                var positions = new List<object>();
                bool capturedSpin = false, capturedSlam = false;
                float until = Time.unscaledTime + 15f;
                while (melee.IsAttackInProgress)
                {
                    Check(Time.unscaledTime < until, "distance counter completes");
                    Check(Travel(origin, Vector3.forward) <= 2.04f, "counter total never exceeds 2m");
                    owner = actor.GetComponent<CombatTarget>().CurrentVolume; body = enemy.GetComponent<CombatTarget>().CurrentVolume;
                    Check(body.Center.z - owner.Center.z > -.02f, "counter never passes target plane");
                    float progress = (float)typeof(MeleeRuntime).GetMethod("GetAttackNormalizedTime", Fields).Invoke(melee, null);
                    positions.Add(new { progress, travel = Travel(origin, Vector3.forward) });
                    if (distance == 3f && progress >= .26f && !capturedSpin)
                    { capturedSpin = true; ScreenCapture.CaptureScreenshot(Path.Combine(Output, "counter-spin.png")); }
                    if (distance == 3f && progress >= .4f && !capturedSlam)
                    { capturedSlam = true; ScreenCapture.CaptureScreenshot(Path.Combine(Output, "counter-slam.png")); }
                    yield return null;
                }
                float travel = Travel(origin, Vector3.forward);
                Check(Near(travel, budget, .12f), "actual motor spends capped budget: " + distance + " -> " + travel + "/" + budget);
                cases.Add(new { fixture = "target distance", distance, budget, travel, radius = body.Radius, positions });
            }

            Reset(); Energy(80); var large = Spawn(def, 3f); yield return Wait(.2f); origin = player.transform.position;
            Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "large-body counter accepted");
            melee.NotifyHeavyParried(Field<int>(melee, "activeActionId"), ParryGrade.Perfect, large); yield return WaitForCounter();
            float largeBudget = Field<float>(melee, "heavyParryCounterRemainingTravel"); yield return Complete();
            float largeTravel = Travel(origin, Vector3.forward); Check(largeTravel <= 2.04f && Near(largeTravel, largeBudget, .12f), "perfect uses same capped curve");
            cases.Add(new { fixture = "perfect movement", largeBudget, largeTravel });

            Reset(); Energy(50);
            var eliteDef = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).First(d => d != null && d.Grade.GradeType == EnemyGradeType.Elite);
            var elite = Spawn(eliteDef, 5f); yield return Wait(.2f);
            Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "elite counter accepted");
            melee.NotifyHeavyParried(Field<int>(melee, "activeActionId"), ParryGrade.Normal, elite); yield return WaitForCounter();
            float eliteBudget = Field<float>(melee, "heavyParryCounterRemainingTravel"); yield return Complete();
            float eliteTravel = Travel(origin, Vector3.forward);
            Check(eliteTravel > .1f && eliteTravel <= eliteBudget + .04f && elite.transform.position.z > player.transform.position.z, "elite uses same bounded target approach");
            cases.Add(new { fixture = "real elite target", definition = eliteDef.EnemyId, eliteBudget, eliteTravel });

            Reset(); Energy(50); var wallTarget = Spawn(def, 4f); yield return Wait(.2f);
            var wall = new GameObject("OwnedParryCounterWall"); owned.Add(wall);
            wall.transform.position = origin + Vector3.forward * 1.2f + Vector3.up;
            wall.AddComponent<BoxCollider>().size = new Vector3(6f, 2f, .2f); Physics.SyncTransforms();
            Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "wall counter accepted");
            melee.NotifyHeavyParried(Field<int>(melee, "activeActionId"), ParryGrade.Normal, wallTarget); yield return WaitForCounter(); yield return Complete();
            float wallTravel = Travel(origin, Vector3.forward);
            Check(wallTravel >= 0 && wallTravel < 1.1f, "real motor stops counter before wall");
            cases.Add(new { fixture = "wall collision", wallTravel });
            owned.Remove(wall); UnityEngine.Object.Destroy(wall); yield return null;

            Reset(); Energy(50); var approaching = Spawn(def, 4f); yield return Wait(.2f);
            Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "approaching target accepted");
            melee.NotifyHeavyParried(Field<int>(melee, "activeActionId"), ParryGrade.Normal, approaching); yield return WaitForCounter();
            approaching.transform.position = player.transform.position + Vector3.forward * (MeleeRuntime.ParryCounterMinimumSeparation + .25f);
            Physics.SyncTransforms(); Vector3 dynamicStart = player.transform.position; yield return Complete();
            float dynamicTravel = Travel(dynamicStart, Vector3.forward); Check(dynamicTravel <= .29f, "approaching target clamps remaining travel: " + dynamicTravel);
            cases.Add(new { fixture = "target approaches", dynamicTravel });

            Reset(); Energy(50); var released = Spawn(def, 4); yield return Wait(.2f);
            Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "pool target accepted");
            melee.NotifyHeavyParried(Field<int>(melee, "activeActionId"), ParryGrade.Normal, released);
            uint previousLease = released.LeaseVersion; spawn.Release(released); leases.Remove(released);
            var reused = Spawn(def, 4); Check(reused == released && reused.LeaseVersion != previousLease, "same actor new lease fixture");
            yield return Complete(); Check(Mathf.Abs(Travel(origin, Vector3.forward)) < .02f, "new pool lease cannot receive old counter travel");
            cases.Add(new { fixture = "pool lease changed", previousLease });

            Reset(); Energy(20); var incomplete = Spawn(def, 3); yield return Wait(.2f);
            Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "incomplete accepted");
            yield return Wait(.1f); origin = player.transform.position; melee.NotifyHeavyParried(Field<int>(melee, "activeActionId"), ParryGrade.Incomplete, incomplete);
            yield return Complete(); Check(Mathf.Abs(Travel(origin, Vector3.forward)) < .02f, "incomplete parry does not counter or advance");
            cases.Add(new { fixture = "incomplete stationary parry" });

            Reset(); Energy(50); var near = Spawn(def, 2f); var far = Spawn(def, 4f); yield return Wait(.2f);
            Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "multi parry accepted");
            int before = parry.ParriedAttackCount, feedback = parry.FeedbackCount;
            Type threatType = typeof(PlayerParryController).GetNestedType("ParryThreat", BindingFlags.NonPublic);
            Array threats = Array.CreateInstance(threatType, 2); var enemies = new[] { far, near };
            for (int i = 0; i < 2; i++) {
                var damage = new DamageInfo(1, player.transform.position, enemies[i].gameObject, Vector3.back, 0,
                    sourceAttackSequenceId: 981000 + i);
                threats.SetValue(Activator.CreateInstance(threatType, new object[] { enemies[i], damage, actor.GetComponent<CombatTarget>().CurrentVolume.Center }), i);
            }
            typeof(PlayerParryController).GetMethod("ResolveParry", Fields).Invoke(parry, new object[] { threats });
            Check(parry.ParriedAttackCount == before + 2 && Field<EnemyActor>(melee, "heavyParryCounterTarget") == near, "same batch chooses nearest parried target once");
            melee.NotifyHeavyParried(Field<int>(melee, "activeActionId"), ParryGrade.Normal, far);
            Check(Field<EnemyActor>(melee, "heavyParryCounterTarget") == near, "later contact does not retarget or add travel");
            yield return WaitForCounter(); float multiBudget = Field<float>(melee, "heavyParryCounterRemainingTravel");
            yield return Complete(); Check(parry.FeedbackCount == feedback + 1 && Travel(origin, Vector3.forward) <= multiBudget + .12f, "multi parry retains one feedback and one movement budget");
            ScreenCapture.CaptureScreenshot(Path.Combine(Output, "counter-stopped-before-target.png"));
            yield return Wait(.15f);
            cases.Add(new { fixture = "multi parry one target", multiBudget, travel = Travel(origin, Vector3.forward) });
            Write("progress.json", new { status = "COMPLETE", cases = cases.Count });
        }
        finally
        {
            melee.CancelCurrentAttackState(); parry.CloseWindow(); OverburstTimeEffectArbiter.ClearOwner(parry);
            foreach (var enemy in leases) if (enemy != null && enemy.IsLeased) spawn?.Release(enemy);
            foreach (var obj in owned) if (obj != null) UnityEngine.Object.Destroy(obj);
            actor.Health.SetMaxHp(oldHp, true);
            typeof(PlayerEquipment).GetMethod("SetElementGem", Fields).Invoke(actor.Equipment, new object[] { oldGem });
            if (oldWeapon != null) actor.Equipment.EquipWeaponItem(oldWeapon); else actor.Equipment.ClearCurrentWeapon();
        }
    }
}
