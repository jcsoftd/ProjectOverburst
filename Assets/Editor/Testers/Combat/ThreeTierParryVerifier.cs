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

// Native contracts and an isolated product-player fixture. No scenes or production assets are saved.
[InitializeOnLoad]
public static class ThreeTierParryVerifier
{
    const string Key = "Overburst.ThreeTierParryVerifier.";
    const string WeaponPath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset";
    const string StartScene = "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity";
    static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<object> checks = new List<object>(), cases = new List<object>();
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static bool subscribed, ownsReload, ownsRefresh;
    static int frame = -1;
    static string Output => SessionState.GetString(Key + "output", "");

    static ThreeTierParryVerifier() { if (!string.IsNullOrEmpty(Output)) Subscribe(); }
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

    public static void Contracts(string directory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Contracts require idle EditMode.");
        directory = IsolatedSavePlayGuard.ValidateDirectory(directory); Directory.CreateDirectory(directory);
        checks.Clear(); var owned = new List<UnityEngine.Object>(); var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            MovementContracts();
            foreach (var sample in new[] { (0f, ParryGrade.Incomplete), (.29999f, ParryGrade.Incomplete),
                (.30f, ParryGrade.Normal), (.79999f, ParryGrade.Normal), (.80f, ParryGrade.Perfect), (1f, ParryGrade.Perfect), (2f, ParryGrade.Perfect) })
                Check(PlayerParryController.ResolveGrade(sample.Item1) == sample.Item2, "boundary " + sample.Item1);
            GameObject Root(string name)
            { var go = new GameObject(name); owned.Add(go); SceneManager.MoveGameObjectToScene(go, scene); return go; }
            var source = Root("ParryResidualSource"); var victim = Root("ParryResidualVictim");
            var hp = victim.AddComponent<CombatHealth>(); Set(hp, "showDamageNumbers", false); hp.SetMaxHp(100f, true);
            var body = victim.AddComponent<Rigidbody>(); body.useGravity = false;
            int resolved = 0, dead = 0; DamageInfo received = default; float loss = 0;
            hp.OnDamageResolved += (_, info, amount, __) => { resolved++; received = info; loss = amount; };
            hp.OnDead += (_, __) => dead++;
            var hit = new DamageInfo(40f, Vector3.zero, source, Vector3.forward, 10f);
            hp.TakeParryResidualDamage(hit, .2f); scene.GetPhysicsScene().Simulate(.02f);
            Check(Near(hp.CurrentHp, 92f) && Near(loss, 8f) && resolved == 1, "residual HP/events once");
            Check(received.isParryResidualDamage && !received.isDamageOverTime && received.triggersOnHitEffects,
                "residual retains direct-hit classification");
            Check(body.linearVelocity.sqrMagnitude < .0001f, "nonfatal residual has no ordinary impulse");
            hp.SetMaxHp(1f, true); hp.TakeParryResidualDamage(hit, .2f);
            Check(hp.IsDead && dead == 1 && hp.CurrentHp == 0, "lethal residual still commits death");
            foreach (ParryGrade grade in Enum.GetValues(typeof(ParryGrade)))
            {
                var go = Root("ParryBoss_" + grade); var bossHp = go.AddComponent<CombatHealth>(); bossHp.SetMaxHp(100, true);
                var reaction = go.AddComponent<EnemyMovementReaction>(); var director = go.AddComponent<EnemyBossCombatDirector>();
                var profile = ScriptableObject.CreateInstance<EnemyBossCombatProfile>(); owned.Add(profile);
                profile.parryGain = 35f; profile.groggyMax = 100f; profile.parryRecoil = 1f;
                director.Configure(profile); Set(director, "health", bossHp); Set(director, "reaction", reaction);
                director.NotifyParried(grade);
                Check(Near(director.Groggy01, grade == ParryGrade.Perfect ? .35f : grade == ParryGrade.Normal ? .20f : .05f), "boss groggy " + grade);
                Check(reaction.IsParryStunned == (grade == ParryGrade.Perfect), "boss recoil " + grade);
            }
            File.WriteAllText(Path.Combine(directory, "contracts.json"), JsonConvert.SerializeObject(new { status = "PASS", checks }, Formatting.Indented));
        }
        catch (Exception e)
        { File.WriteAllText(Path.Combine(directory, "contracts.json"), JsonConvert.SerializeObject(new { status = "FAIL", checks, error = e.ToString() }, Formatting.Indented)); throw; }
        finally
        { foreach (var value in owned) if (value != null) UnityEngine.Object.DestroyImmediate(value); EditorSceneManager.ClosePreviewScene(scene); }
    }

    static void MovementContracts()
    {
        var weapon = AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>(GreatswordHeavyParryBuilder.WeaponDefinitionPath);
        var normal = weapon.heavyAttackDefinition.attack.movementPhases;
        var counter = weapon.parriedHeavyAttackDefinition.attack.movementPhases;
        Check(normal != null && normal.Length > 0 && counter != null && counter.Length == 2, "counter has separate rotation and landing movement phases");
        float Travel(AttackMovementPhaseData[] phases, float start, float end, int fps)
        {
            float distance = 0f;
            var executor = new AttackMovementExecutor();
            Check(executor.Begin(phases, Vector3.forward, d => distance += d.z, start), "movement executor accepts saved phases");
            for (int i = 0; i <= fps; i++) executor.Tick(Mathf.Lerp(start, end, i / (float)fps));
            executor.Cancel();
            return distance;
        }
        foreach (int fps in new[] { 15, 30, 60 })
        {
            Check(Near(Travel(normal, 0f, 1f, fps), 2f) && Near(Travel(counter, 0f, 1f, fps), 2f), "normal and counter advance two meters at " + fps);
            float rotation = Travel(counter, 0f, counter[0].SafeEnd, fps);
            var comboThird = weapon.comboDefinition.steps[2];
            float comboTravel = Travel(comboThird.movementPhases, 0f, 1f, fps);
            Check(Near(rotation, comboTravel), "counter rotations use combo3 travel at " + fps);
            Check(Near(Travel(counter, counter[1].SafeStart, counter[1].SafeEnd, fps), 2f - comboTravel), "remaining travel ends at landing impact at " + fps);
        }
    }

    public static void StartIsolated(string directory, bool perfectContactComparison = false)
    {
        if (!string.IsNullOrEmpty(Output) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) || IsolatedSavePlayGuard.RequiresAccountChoice)
            throw new InvalidOperationException("The shared Editor and account guard must be returned before starting.");
        directory = IsolatedSavePlayGuard.ValidateDirectory(directory); Directory.CreateDirectory(directory);
        checks.Clear(); cases.Clear(); frame = -1;
        SessionState.SetString(Key + "output", directory);
        SessionState.SetBool(Key + "perfectContactComparison", perfectContactComparison);
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
            stack.Push(SessionState.GetBool(Key + "perfectContactComparison", false) ? PerfectParryContactVerifier.Compare(Output) : Run());
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
        foreach (string suffix in new[] { "started", "returnPending", "background", "perfectContactComparison" }) SessionState.EraseBool(Key + suffix);
        foreach (string suffix in new[] { "deadline", "returnAfter", "returnDeadline" }) SessionState.EraseFloat(Key + suffix);
        Unsubscribe();
    }

    static IEnumerator Run()
    {
        var actor = PlayerContext.GetOrCreate().CurrentActor; var melee = actor.GetComponent<MeleeRuntime>();
        var player = PlayerInputFacade.Current; var parry = actor.GetComponent<PlayerParryController>();
        var energy = actor.GetComponent<OverburstElementEnergy>(); var animator = actor.GetComponentInChildren<Animator>(true);
        bool ownsEnergy = energy == null;
        if (ownsEnergy) energy = actor.gameObject.AddComponent<OverburstElementEnergy>();
        var ui = EnemyThemeTrialHarness.Current; var leases = new List<EnemyActor>(); var owned = new List<UnityEngine.Object>();
        var hooks = new List<(CombatHealth hp, Action<CombatHealth, DamageInfo, float, bool> callback)>();
        EnemySpawnService spawn = null;
        ItemData oldWeapon = actor.Equipment.CurrentWeaponItem, oldGem = actor.Equipment.EquippedElementGem;
        var originalMode = animator.updateMode; float originalSpeed = animator.speed; float oldMaxHp = actor.Health.MaxHp;
        bool oldDamageDebug = CombatDebugSettings.ReduceIncomingPlayerDamageBy99_9Percent;
        PlayerAnimation originalPlayerAnimation = Field<PlayerAnimation>(melee, "playerAnimatorController");
        var capturedGrades = new HashSet<ParryGrade>();
        try
        {
            Check(melee != null && player != null, "product player melee and facade");
            Check(energy != null && parry != null && ui != null, "product energy and parry/trial components");
            CombatDebugSettings.SetPlayerDamageReductionDebug(false);
            melee.CancelCurrentAttackState(); actor.Health.SetMaxHp(1000000, true);
            Check(actor.Equipment.EquipWeaponItem(new ItemData(AssetDatabase.LoadAssetAtPath<WeaponItemData>(WeaponPath), 1, ItemGrade.Common)), "equip actual greatsword");
            var equipGem = typeof(PlayerEquipment).GetMethod("SetElementGem", Fields);
            var fire = AssetDatabase.LoadAssetAtPath<ElementGemItemData>("Assets/ProjectOverburst/Resources/Items/ElementGems/EG_Fire_Common.asset");
            equipGem.Invoke(actor.Equipment, new object[] { new ItemData(fire, 1, ItemGrade.Common) });
            float bootUntil = Time.unscaledTime + 30f;
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
                || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
            { Check(Time.unscaledTime < bootUntil, "hideout finishes boot before trial entry"); yield return null; }
            if (!ui.InArena) ui.ToggleArena(); yield return Wait(1f);
            Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn), "actual trial spawn service");
            foreach (var table in ui.tables) Check(spawn.RegisterAdditionalCatalog(table.Catalog, out _), "catalog registered");
            var definition = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).First(d => d != null
                && d.Grade.GradeType != EnemyGradeType.Boss && d.AnimationProfile != null && d.AnimationProfile.ParryCollapse != null && Enumerable.Range(0, d.AbilitySet.Count)
                    .Any(i => d.AbilitySet.GetAbility(i).IsParryable && d.AbilitySet.GetAbility(i).UsesPacedTimeline && d.AbilitySet.GetAbility(i).ExecutionMode == EnemyAbilityExecutionMode.MeleeArc));
            var ability = Enumerable.Range(0, definition.AbilitySet.Count).Select(i => definition.AbilitySet.GetAbility(i)).First(a => a.IsParryable
                && a.UsesPacedTimeline && a.ExecutionMode == EnemyAbilityExecutionMode.MeleeArc);
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System); melee.SetManualInputEnabled(true); yield return Wait(1f);
            void Energy(float amount)
            { energy.BindWeapon(actor.Equipment.CurrentWeaponItem.runtimeInstanceId, actor.Equipment.ActiveElement, actor.Equipment.EquippedElementGem?.runtimeInstanceId, actor.Equipment.GemRevision); Set(energy, "<Amount>k__BackingField", amount); }
            EnemyActor Spawn(Vector3 direction, float shift)
            {
                Vector3 p = player.transform.position + direction * Mathf.Max(1.1f, ability.Range * .6f);
                Check(Physics.Raycast(p + Vector3.up * 4f, Vector3.down, out var floor, 9f, LayerMask.GetMask("Default", "Environment", "Ground")), "fixture ground");
                Check(spawn.TrySpawn(new EnemySpawnRequest(definition, floor.point + Vector3.up * .035f, Quaternion.LookRotation(-direction), player.transform), out var enemy), "spawn actual enemy");
                leases.Add(enemy); enemy.AI.enabled = false; enemy.Movement.StopMovement(); enemy.Health.SetMaxHp(100000f, true);
                enemy.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var clone = UnityEngine.Object.Instantiate(ability); owned.Add(clone);
                var edit = new SerializedObject(clone); float speed = enemy.Melee.AbilityAnimationSpeed;
                var prep = edit.FindProperty("preparationDuration"); prep.floatValue = (Mathf.Max(.42f, prep.floatValue / speed) + shift) * speed; edit.ApplyModifiedPropertiesWithoutUndo();
                var set = ScriptableObject.CreateInstance<EnemyAbilitySet>(); owned.Add(set);
                edit = new SerializedObject(set); edit.FindProperty("abilitySetId").stringValue = "three-tier-parry-fixture";
                var list = edit.FindProperty("abilities"); list.arraySize = 1; list.GetArrayElementAtIndex(0).objectReferenceValue = clone; edit.ApplyModifiedPropertiesWithoutUndo();
                enemy.AbilityController.Configure(set, 1.75f, 1f);
                return enemy;
            }
            void Release() { foreach (var enemy in leases) if (enemy != null && enemy.IsLeased) spawn.Release(enemy); leases.Clear(); }
            Vector3 fixtureOrigin = player.transform.position;
            void ResetPosition()
            {
                var controller = actor.CharacterController;
                bool enabled = controller != null && controller.enabled;
                if (controller != null) controller.enabled = false;
                try { player.transform.position = fixtureOrigin; }
                finally { if (controller != null) controller.enabled = enabled; }
                actor.Movement.ResetMotionAfterTeleport();
                actor.GetComponent<PlayerCombatFacingController>()?.ResetAfterTeleport();
                Physics.SyncTransforms();
            }
            // Empty-lane controls measure the actual motor without a monster clipping the travel.
            foreach (float parryAt in new[] { -1f, 0f, .20f })
            {
                melee.CancelCurrentAttackState(); parry.CloseWindow(); OverburstTimeEffectArbiter.ClearOwner(parry);
                ResetPosition();
                Energy(50f); yield return Wait(.3f);
                Vector3 origin = player.transform.position;
                Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "free-lane heavy accepted");
                float expectedDistance = 2f;
                if (parryAt >= 0f)
                {
                    float deadline = Time.unscaledTime + 5f;
                    while ((float)typeof(MeleeRuntime).GetMethod("GetAttackNormalizedTime", Fields).Invoke(melee, null) < parryAt)
                    { Check(Time.unscaledTime < deadline, "late parry control reaches requested progress"); yield return null; }
                    expectedDistance = Vector3.Dot(player.transform.position - origin, Vector3.forward);
                    melee.NotifyHeavyParried(Field<int>(melee, "activeActionId"), ParryGrade.Normal);
                }
                float timeout = Time.unscaledTime + 12f;
                while (melee.IsAttackInProgress) { Check(Time.unscaledTime < timeout, "free-lane action completes"); yield return null; }
                float distance = Vector3.Dot(player.transform.position - origin, Vector3.forward);
                Write("free-travel-" + (parryAt < 0f ? "normal" : parryAt == 0f ? "immediate" : "late") + ".json", new { distance, parryAt });
                Check(Near(distance, expectedDistance, .1f), "normal heavy retains travel; targetless counter preserves prior travel without a blind lunge: " + distance);
                yield return Wait(.3f);
            }
            foreach (var item in new[] { (0f, 1, false), (29.999f, 1, false), (30f, 1, false), (79.999f, 1, false), (80f, 1, false),
                (100f, 1, false), (20f, 2, false), (50f, 2, false), (80f, 2, false), (20f, 2, true), (50f, 2, true), (80f, 2, true), (120f, 1, false), (20f, 1, false) })
            {
                melee.CancelCurrentAttackState(); parry.CloseWindow(); OverburstTimeEffectArbiter.ClearOwner(parry);
                ResetPosition();
                if (item.Item1 > 100f)
                {
                    var light = AssetDatabase.LoadAssetAtPath<ElementGemItemData>("Assets/ProjectOverburst/Resources/Items/ElementGems/EG_Light_Legendary.asset");
                    equipGem.Invoke(actor.Equipment, new object[] { new ItemData(light, 1, ItemGrade.Legendary) });
                }
                actor.Health.SetMaxHp(1000000f, true); Energy(item.Item1); yield return Wait(.3f);
                if (item.Item1 > 100f) Check(energy.Capacity > energy.BaseMaximum && energy.Amount > energy.BaseMaximum
                    && Near(energy.Normalized, 1f), "Light overcharge uses clamped base maximum for grade");
                var enemies = new List<EnemyActor> { Spawn(Vector3.forward, 0f) };
                if (item.Item2 == 2) enemies.Add(Spawn(Vector3.right, item.Item3 ? .72f : .62f));
                foreach (var enemy in enemies) Check(enemy.AbilityController.TryStart(player.transform), "actual strong attack committed");
                float limit = Time.unscaledTime + 6f;
                while (!enemies[0].AbilityController.IsParryThreatTo(actor.GetComponent<CombatTarget>()))
                { Check(Time.unscaledTime < limit, "parry threat reached"); yield return null; }
                if (item.Item2 == 2)
                {
                    float firstAt = Field<float>(enemies[0].AbilityController, "lastCommittedAt") + enemies[0].AbilityController.LastCommittedAbility.ResolveFirstImpactTime(enemies[0].Melee.AbilityAnimationSpeed);
                    while (Time.time < firstAt - (item.Item3 ? .03f : .06f)) { Check(Time.unscaledTime < limit, "multi overlap preparation"); yield return null; }
                }
                var snapshots = new List<DamageInfo>();
                foreach (var enemy in enemies)
                { Check(enemy.Melee.TryGetParryDamageSnapshot(enemy.AbilityController.LastCommittedAbility, actor.GetComponent<CombatTarget>(), out var info), "source damage snapshot"); snapshots.Add(info); }
                float hpBefore = actor.Health.CurrentHp, amountBefore = energy.Amount;
                int parries = parry.ParriedAttackCount, feedback = parry.FeedbackCount, incoming = 0; float residual = 0;
                var received = new List<DamageInfo>();
                Action<CombatHealth, DamageInfo, float, bool> onPlayerDamage = (_, info, loss, __) => { incoming++; residual += loss; received.Add(info); };
                actor.Health.OnDamageResolved += onPlayerDamage; hooks.Add((actor.Health, onPlayerDamage));
                var hits = new HashSet<int>();
                foreach (var enemy in enemies)
                {
                    Action<CombatHealth, DamageInfo, float, bool> onEnemyDamage = (_, info, loss, __) => { if ((info.playerAttackKind & PlayerAttackKind.Heavy) != 0 && loss > 0f) hits.Add(info.sourceAttackPhaseIndex); };
                    enemy.Health.OnDamageResolved += onEnemyDamage; hooks.Add((enemy.Health, onEnemyDamage));
                }
                ParryGrade expected = item.Item1 >= 80f ? ParryGrade.Perfect : item.Item1 >= 30f ? ParryGrade.Normal : ParryGrade.Incomplete;
                Vector3 attackStartPosition = player.transform.position;
                Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "player heavy accepted");
                Check(parry.ActionGrade == expected, "accepted action locks exact grade " + item.Item1);
                var cancelStun = new Dictionary<int, bool>();
                void ObserveCancellation()
                {
                    foreach (var enemy in enemies)
                        if (!enemy.AbilityController.IsExecuting && !cancelStun.ContainsKey(enemy.GetInstanceID()))
                        {
                            var reaction = enemy.GetComponent<EnemyMovementReaction>();
                            cancelStun[enemy.GetInstanceID()] = reaction.IsParryStunned;
                            if (expected == ParryGrade.Normal)
                                Check(reaction.BlocksAttack && enemy.AnimationBridge.IsNormalParryReacting, "normal collapse blocks attacks without perfect stun");
                        }
                }
                bool normalCollapseSeen = false, normalRecoverSeen = false, normalHoldSeen = false;
                void ObserveNormalReaction()
                {
                    if (expected != ParryGrade.Normal) return;
                    foreach (var enemy in enemies)
                    {
                        var motion = enemy.AnimationBridge.MotionAnimator;
                        if (motion == null) continue;
                        var state = motion.GetCurrentAnimatorStateInfo(0);
                        var next = motion.GetNextAnimatorStateInfo(0);
                        Check(!state.IsName(EnemyAnimationBridge.StunnedLoopStateName)
                            && (!motion.IsInTransition(0) || !next.IsName(EnemyAnimationBridge.StunnedLoopStateName)), "normal parry skips stunned loop");
                        normalCollapseSeen |= state.IsName(EnemyAnimationBridge.ParryCollapseStateName);
                        normalRecoverSeen |= state.IsName(EnemyAnimationBridge.StunRecoverStateName)
                            || motion.IsInTransition(0) && next.IsName(EnemyAnimationBridge.StunRecoverStateName);
                        normalHoldSeen |= state.IsName(EnemyAnimationBridge.ParryCollapseStateName) && state.normalizedTime > .98f && Near(motion.speed, 0f);
                    }
                }
                ObserveCancellation();
                // Keep a confirmed contact pending past the short clip to exercise its completion boundary.
                bool delayedContact = cases.Count == 13;
                if (delayedContact) Set(parry, "pendingDelay", 10f);
                if (item.Item3) Energy(item.Item1 < 30f ? 100f : 0f);
                int commits = 0; bool priorCommit = false; float started = Time.unscaledTime;
                string expectedLabel = expected == ParryGrade.Perfect ? "완벽패링" : expected == ParryGrade.Normal ? "패링" : "불완전패링";
                var labelInstances = new HashSet<int>();
                var inspectedLabels = new HashSet<int>();
                void ObserveLabel()
                {
                    if (parry.FeedbackCount == feedback) return;
                    foreach (var popup in UnityEngine.Object.FindObjectsByType<DamageNumberPopup>(FindObjectsSortMode.None))
                    {
                        var text = popup.GetComponent<TMPro.TextMeshProUGUI>();
                        if (text == null || text.text != expectedLabel || popup.FxElapsed > .3f) continue;
                        if (popup.FxElapsed >= .08f && text.color.a > .7f && capturedGrades.Add(expected))
                            ScreenCapture.CaptureScreenshot(Path.Combine(Output, "parry-" + expected.ToString().ToLowerInvariant() + ".png"));
                        labelInstances.Add(popup.GetInstanceID());
                        if (popup.FxElapsed < .08f || !inspectedLabels.Add(popup.GetInstanceID())) continue;
                        text.ForceMeshUpdate();
                        Check(text.textInfo.characterCount == expectedLabel.Length && text.textInfo.characterInfo.Take(expectedLabel.Length)
                            .Select((character, index) => character.textElement != null && character.textElement.unicode == expectedLabel[index]).All(v => v), "Korean grade label glyphs render");
                        CombatTargetVolume volume = actor.GetComponent<CombatTarget>().CurrentVolume;
                        Vector3 head = Field<Vector3>(popup, "worldPosition");
                        Check(head.y > volume.Center.y + volume.HalfHeight && Vector2.Distance(new Vector2(head.x, head.z),
                            new Vector2(volume.Center.x, volume.Center.z)) < .35f, "grade label originates above player head");
                        Check(text.color.a > .7f && text.font != null && text.canvas != null, "grade label visible on actual HUD canvas after entrance fade");
                        Check(text.font == Resources.Load<TMPro.TMP_FontAsset>("UI/Fonts/DamageFloating/NotoSerifKR_Parry SDF"),
                            "parry uses baked Noto Serif grade font");
                        Check(Near(text.fontSize, expected == ParryGrade.Perfect ? 24f : 22f) && Near(text.characterSpacing, 1f)
                            && text.fontStyle == TMPro.FontStyles.Normal, "parry size spacing and authored SemiBold weight");
                        Check(text.enableVertexGradient && popup.GetComponentsInChildren<UnityEngine.UI.Image>(false).Length == 0,
                            "parry grade gradient with no underline plate icons or active accents");
                    }
                }
                ObserveLabel();
                while (melee.IsAttackInProgress)
                {
                    Check(Time.unscaledTime - started < 12f, "player action completes");
                    bool committed = Field<bool>(melee, "heavyDischargeCommitted"); if (committed && !priorCommit) commits++; priorCommit = committed;
                    Check(parry.ActionGrade == expected, "grade survives energy change/consume/refund");
                    ObserveCancellation();
                    ObserveNormalReaction();
                    ObserveLabel();
                    yield return null;
                }
                ObserveLabel();
                yield return Wait(.15f);
                ObserveLabel();
                Check(labelInstances.Count == 1 && inspectedLabels.Count == 1, "one visible head grade label per action including simultaneous/deferred parries");
                Check(parry.ParriedAttackCount - parries == enemies.Count && enemies.All(e => !e.AbilityController.IsExecuting), "all source executions cancelled");
                ObserveCancellation();
                Check(cancelStun.Count == enemies.Count && cancelStun.Values.All(stunned => stunned == (expected == ParryGrade.Perfect)), "grade stun applies to every enemy");
                float forwardTravel = Vector3.Dot(player.transform.position - attackStartPosition, Vector3.forward);
                if (expected != ParryGrade.Incomplete)
                {
                    Write("travel-latest.json", new { grade = expected.ToString(), forwardTravel, start = attackStartPosition.ToString("F4"), end = player.transform.position.ToString("F4"), direction = Field<Vector3>(melee, "activeAttackDirection").ToString("F4"), step = Field<MeleeComboStepData>(melee, "activeAttackStep").attackId, normalCollapseSeen, normalHoldSeen, normalRecoverSeen });
                    // This fixture puts a physical monster ahead; the same heavy collision policy may shorten travel.
                    Check(forwardTravel >= -.05f && forwardTravel <= 2.1f, "counter travel respects the two-meter budget and physical blockers: " + forwardTravel);
                }
                if (expected == ParryGrade.Normal) Check(normalCollapseSeen && normalHoldSeen && normalRecoverSeen, "actual normal collapse hold recover observed");
                bool enhancedContact = expected == ParryGrade.Perfect && PerfectParryContactProfile.Current != null && PerfectParryContactProfile.Current.IsReady;
                Check(parry.FeedbackCount - feedback == 1 && (enhancedContact
                    ? ParryFeedbackService.LastPerfectLayerCount == 3 && ParryFeedbackService.LastAdditionalTingCount == (int)expected
                    : ParryFeedbackService.LastAdditionalTingCount == (int)expected), "one feedback and the selected grade audio bundle per action");
                if (expected == ParryGrade.Incomplete)
                {
                    Check(incoming == enemies.Count && residual > 0f && Near(hpBefore - actor.Health.CurrentHp, residual, .1f), "each incomplete execution causes one residual loss");
                    Check(received.All(info => info.isParryResidualDamage) && commits == 0 && hits.Count == 0, "incomplete ends without counter/discharge");
                    if (!item.Item3) Check(Near(energy.Amount, amountBefore), "incomplete spends/refunds no energy");
                }
                else
                {
                    Check(incoming == 0 && Near(actor.Health.CurrentHp, hpBefore), "normal/perfect fully prevent damage");
                    Check(commits == 1 && hits.Contains(0) && hits.Contains(1) && hits.Contains(2), "normal/perfect preserve both spins and slam");
                    if (!item.Item3) Check(Near(energy.Amount, Mathf.Min(amountBefore, energy.BaseMaximum) * .5f), "normal/perfect refund once");
                }
                foreach (DamageInfo stale in snapshots) actor.Health.TakeDamage(stale);
                Check(incoming == (expected == ParryGrade.Incomplete ? enemies.Count : 0), "cancelled execution callbacks cannot duplicate damage");
                var nextExecution = snapshots[0]; nextExecution.sourceAttackSequenceId = EnemyAttackSequence.Next();
                Check(!parry.TryCancelDamage(nextExecution), "same actor has no blanket immunity for another execution");
                Check(animator.updateMode == originalMode && Near(animator.speed, originalSpeed), "Animator clock restored");
                cases.Add(new { amount = item.Item1, targets = enemies.Count, deferred = item.Item3, grade = expected.ToString(), incoming, residual, commits, hits = hits.ToArray(), seconds = Time.unscaledTime - started, label = expectedLabel, visibleLabelCount = labelInstances.Count, delayedContact, forwardTravel, normalCollapseSeen, normalHoldSeen, normalRecoverSeen });
                Write("progress.json", new { status = "RUNNING", completed = cases.Count, cases });
                foreach (var hook in hooks) if (hook.hp != null) hook.hp.OnDamageResolved -= hook.callback; hooks.Clear();
                Release(); yield return Wait(.4f);
            }
            // Deliberately advance the source phase before invoking the real Health callback.
            // The callback must retain the original hit rather than snapshot the next phase.
            foreach (var sample in new[] { (20f, false), (50f, false), (80f, false), (20f, true) })
            {
                float amount = sample.Item1; bool noMotion = sample.Item2;
                melee.CancelCurrentAttackState(); parry.CloseWindow(); OverburstTimeEffectArbiter.ClearOwner(parry);
                equipGem.Invoke(actor.Equipment, new object[] { new ItemData(fire, 1, ItemGrade.Common) }); Energy(amount);
                var enemy = Spawn(Vector3.forward, .5f);
                Check(enemy.AbilityController.TryStart(player.transform), "callback fixture source execution starts");
                Check(enemy.Melee.TryGetParryDamageSnapshot(enemy.AbilityController.LastCommittedAbility, actor.GetComponent<CombatTarget>(), out var original), "callback fixture captures original hit");
                original.damage = 123f; original.sourceAttackPhaseIndex = 0;
                actor.Health.SetMaxHp(10000f, true); float referenceBefore = actor.Health.CurrentHp;
                actor.Health.TakeParryResidualDamage(original, 1f); float referenceLoss = referenceBefore - actor.Health.CurrentHp;
                Check(referenceLoss > 0f, "normal mitigation reference is positive");
                actor.Health.SetMaxHp(10000f, true);
                enemy.AbilityController.NotifyAbilityImpact(enemy.AbilityController.LastCommittedAbility, enemy.AbilityController.LastCommittedAbility.HitCount - 1);
                int count = 0; float loss = 0f; DamageInfo receivedOriginal = default;
                Action<CombatHealth, DamageInfo, float, bool> callback = (_, info, actual, __) => { count++; loss += actual; receivedOriginal = info; };
                actor.Health.OnDamageResolved += callback; hooks.Add((actor.Health, callback));
                int beforeParries = parry.ParriedAttackCount;
                Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "callback fixture player heavy accepted");
                Check(parry.ParriedAttackCount == beforeParries, "callback fixture suppresses proactive path");
                int beforeFeedback = parry.FeedbackCount;
                if (noMotion) Set(melee, "playerAnimatorController", null);
                try { actor.Health.TakeDamage(original); }
                finally { Set(melee, "playerAnimatorController", originalPlayerAnimation); }
                if (noMotion)
                {
                    Check(!melee.IsAttackInProgress && parry.FeedbackCount == beforeFeedback + 1, "parry-only motion fallback retains contact presentation");
                    Check(UnityEngine.Object.FindObjectsByType<DamageNumberPopup>(FindObjectsSortMode.None).Any(p => p.FxElapsed < .1f
                        && p.GetComponent<TMPro.TextMeshProUGUI>().text == "불완전패링"), "motion fallback retains head label");
                }
                Check(parry.ParriedAttackCount == beforeParries + 1 && !enemy.AbilityController.IsExecuting, "real Health callback resolves parry");
                Check(count == (amount < 30f ? 1 : 0), "callback residual only for incomplete");
                if (amount < 30f) Check(Near(loss, referenceLoss * .2f, .01f) && receivedOriginal.sourceAttackPhaseIndex == 0
                    && receivedOriginal.sourceAttackSequenceId == original.sourceAttackSequenceId, "original phase and normally mitigated residual retained");
                actor.Health.TakeDamage(original);
                Check(count == (amount < 30f ? 1 : 0), "callback execution absorbs duplicate once");
                cases.Add(new { fixture = noMotion ? "parry-only motion fallback" : "original damage callback", amount, referenceLoss, loss, count });
                foreach (var hook in hooks) if (hook.hp != null) hook.hp.OnDamageResolved -= hook.callback; hooks.Clear();
                melee.CancelCurrentAttackState(); Release(); yield return Wait(.4f);
            }
        }
        finally
        {
            Set(melee, "playerAnimatorController", originalPlayerAnimation);
            CombatDebugSettings.SetPlayerDamageReductionDebug(oldDamageDebug);
            if (ownsEnergy && energy != null) UnityEngine.Object.Destroy(energy);
            foreach (var hook in hooks) if (hook.hp != null) hook.hp.OnDamageResolved -= hook.callback;
            melee?.CancelCurrentAttackState(); if (parry != null) OverburstTimeEffectArbiter.ClearOwner(parry);
            foreach (var enemy in leases) if (enemy != null && enemy.IsLeased) spawn?.Release(enemy);
            foreach (var value in owned) if (value != null) UnityEngine.Object.Destroy(value);
            if (actor != null)
            {
                typeof(PlayerEquipment).GetMethod("SetElementGem", Fields).Invoke(actor.Equipment, new object[] { oldGem });
                if (oldWeapon != null) actor.Equipment.EquipWeaponItem(oldWeapon); else actor.Equipment.ClearCurrentWeapon();
                actor.Health.SetMaxHp(oldMaxHp, true);
            }
        }
    }
}
