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

[InitializeOnLoad]
public static class ElementEnergySfxVerifier
{
    const string WeaponPath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset";
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly WeaponElement[] Elements = { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light };
    static readonly Stack<IEnumerator> Work = new Stack<IEnumerator>();
    static readonly List<object> Results = new List<object>();
    static readonly List<string> Errors = new List<string>();
    static string output;
    static int frame, checks, actions;
    static double deadline;
    static bool background, locked, refreshLocked;
    static MeleeElementSfxCatalog catalog;
    static MeleeElementSfxService service;
    const string PendingKey = "Overburst.ElementEnergySfxVerifier.Pending";
    const string ReturnKey = "Overburst.ElementEnergySfxVerifier.Return";
    const string PersistentPath = "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity";

    static ElementEnergySfxVerifier()
    {
        if (!string.IsNullOrEmpty(SessionState.GetString(PendingKey, ""))) EditorApplication.update += AutoBegin;
        if (!string.IsNullOrEmpty(SessionState.GetString(ReturnKey, "")))
        {
            EditorApplication.playModeStateChanged += ReturnState;
            if (!EditorApplication.isPlayingOrWillChangePlaymode) ScheduleReturn();
        }
    }
    public static void StartIsolated(string directory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || !string.IsNullOrEmpty(SessionState.GetString(ReturnKey, ""))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
            throw new InvalidOperationException("Idle Editor with no pending verifier return required");
        string target = IsolatedSavePlayGuard.ValidateDirectory(directory);
        var persistent = AssetDatabase.LoadAssetAtPath<SceneAsset>(PersistentPath);
        if (persistent == null) throw new InvalidOperationException("PersistentScene missing");
        Directory.CreateDirectory(target);
        string priorStart = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene);
        File.WriteAllText(Path.Combine(target, "Before.json"), JsonConvert.SerializeObject(new
        { priorStart, scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(i => { var s = SceneManager.GetSceneAt(i); return new { s.path, s.isDirty }; }).ToArray() }, Formatting.Indented));
        SessionState.SetString(PendingKey, target);
        SessionState.SetFloat(PendingKey + ".Deadline", (float)EditorApplication.timeSinceStartup + 45f);
        SessionState.SetString(ReturnKey, target);
        SessionState.SetString(ReturnKey + ".StartScene", priorStart);
        SessionState.SetFloat(ReturnKey + ".Deadline", (float)EditorApplication.timeSinceStartup + 420f);
        EditorApplication.update -= AutoBegin; EditorApplication.update += AutoBegin;
        EditorApplication.playModeStateChanged -= ReturnState; EditorApplication.playModeStateChanged += ReturnState;
        EditorSceneManager.playModeStartScene = persistent;
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(target, "IsolatedAccount")); }
        catch { ClearPending(); ScheduleReturn(); throw; }
    }
    static bool OwnsDirectory(string target, string current)
        => !string.IsNullOrEmpty(current) && Path.GetFullPath(current).StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    static void ClearPending()
    {
        EditorApplication.update -= AutoBegin;
        SessionState.EraseString(PendingKey); SessionState.EraseFloat(PendingKey + ".Deadline");
    }
    static void AutoBegin()
    {
        string target = SessionState.GetString(PendingKey, "");
        if (string.IsNullOrEmpty(target)) { ClearPending(); return; }
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(PendingKey + ".Deadline", 0))
        {
            ClearPending();
            File.WriteAllText(Path.Combine(target, "BootResult.json"), "{\"status\":\"FAIL\",\"reason\":\"boot timeout\"}");
            if (EditorApplication.isPlaying && OwnsDirectory(target, IsolatedSavePlayGuard.ActiveDirectory)) EditorApplication.ExitPlaymode();
            else ScheduleReturn();
            return;
        }
        if (!EditorApplication.isPlaying || !OwnsDirectory(target, Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))) return;
        EditorApplication.QueuePlayerLoopUpdate();
        if (!Overburst.Persistence.AccountBootstrap.Ready || !WorldSessionState.IsHideout
            || PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || PlayerContext.GetOrCreate()?.CurrentActor == null) return;
        ClearPending();
        try { Begin(target); }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(target, "BootResult.json"), JsonConvert.SerializeObject(new { status = "FAIL", error = e.ToString() }, Formatting.Indented));
            if (EditorApplication.isPlaying && OwnsDirectory(target, IsolatedSavePlayGuard.ActiveDirectory)) EditorApplication.ExitPlaymode();
            else ScheduleReturn();
        }
    }
    static void ReturnState(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode) return;
        string pending = SessionState.GetString(PendingKey, "");
        if (!string.IsNullOrEmpty(pending))
        {
            ClearPending();
            File.WriteAllText(Path.Combine(pending, "BootResult.json"), "{\"status\":\"ABORTED\",\"reason\":\"Play ended before verifier began\"}");
        }
        ScheduleReturn();
    }
    static void ScheduleReturn()
    { EditorApplication.update -= RestoreAccount; EditorApplication.update += RestoreAccount; }
    static void RestoreAccount()
    {
        string target = SessionState.GetString(ReturnKey, "");
        if (string.IsNullOrEmpty(target)) { EditorApplication.update -= RestoreAccount; EditorApplication.playModeStateChanged -= ReturnState; return; }
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(ReturnKey + ".Deadline", 0))
        {
            EditorApplication.update -= RestoreAccount; EditorApplication.playModeStateChanged -= ReturnState;
            File.WriteAllText(Path.Combine(target, "EditorAccountReturn.json"), "{\"status\":\"FAIL\",\"reason\":\"return timeout; other state preserved\"}");
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string current = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "";
        string prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "");
        if ((!string.IsNullOrEmpty(current) && !OwnsDirectory(target, current))
            || (!string.IsNullOrEmpty(prepared) && !OwnsDirectory(target, prepared))) return;
        ClearPending();
        IsolatedSavePlayGuard.UseRealAccount();
        if (AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == PersistentPath)
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(ReturnKey + ".StartScene", ""));
        SessionState.EraseString(ReturnKey); SessionState.EraseString(ReturnKey + ".StartScene"); SessionState.EraseFloat(ReturnKey + ".Deadline");
        EditorApplication.update -= RestoreAccount; EditorApplication.playModeStateChanged -= ReturnState;
        File.WriteAllText(Path.Combine(target, "EditorAccountReturn.json"), JsonConvert.SerializeObject(new
        { status = "PASS", requiresChoice = IsolatedSavePlayGuard.RequiresAccountChoice, environment = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),
            active = IsolatedSavePlayGuard.ActiveDirectory, prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""),
            expires = SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""), pending = SessionState.GetString(PendingKey, ""),
            returning = SessionState.GetString(ReturnKey, ""), startScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) }, Formatting.Indented));
    }

    public static void Begin(string directory)
    {
        if (Work.Count > 0) throw new InvalidOperationException("Verification already running");
        output = IsolatedSavePlayGuard.ValidateDirectory(directory);
        if (!EditorApplication.isPlaying || !Overburst.Persistence.AccountBootstrap.Ready
            || !Owned() || !WorldSessionState.IsHideout || PersistentSceneFlow.Instance.IsSwitching)
            throw new InvalidOperationException("Owned isolated Hideout must be ready");
        Directory.CreateDirectory(output); Results.Clear(); Errors.Clear(); checks = actions = 0; frame = -1;
        catalog = Resources.Load<MeleeElementSfxCatalog>(MeleeElementSfxCatalog.ResourcePath);
        MeleeElementSfxService.Configure(catalog);
        service = UnityEngine.Object.FindFirstObjectByType<MeleeElementSfxService>();
        background = Application.runInBackground; Application.runInBackground = true;
        deadline = EditorApplication.timeSinceStartup + 240;
        EditorApplication.LockReloadAssemblies(); locked = true;
        AssetDatabase.DisallowAutoRefresh(); refreshLocked = true;
        Application.logMessageReceived += Log;
        EditorApplication.playModeStateChanged += State;
        AssemblyReloadEvents.beforeAssemblyReload += Abort;
        Work.Push(Run()); EditorApplication.update += Tick; EditorApplication.QueuePlayerLoopUpdate();
    }
    static bool Owned() => (Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "")
        .StartsWith(output, StringComparison.OrdinalIgnoreCase);
    static void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException(label); }
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Fields).GetValue(owner);
    static void Log(string text, string trace, LogType kind)
    { if (kind == LogType.Error || kind == LogType.Assert || kind == LogType.Exception) Errors.Add(text + "\n" + trace); }
    static void State(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) Finish("ABORTED", false); }
    static void Abort() => Finish("RELOADING", false);
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!Application.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            Check(Owned(), "Play ownership preserved");
            Check(EditorApplication.timeSinceStartup < deadline, "Verification timeout");
            while (Work.Count > 0)
            {
                var current = Work.Peek();
                if (current.MoveNext()) { if (current.Current is IEnumerator nested) { Work.Push(nested); continue; } return; }
                (Work.Pop() as IDisposable)?.Dispose();
            }
            Finish(Errors.Count == 0 ? "PASS" : "FAIL", true);
        }
        catch (Exception error) { Errors.Add(error.ToString()); Finish("FAIL", true); }
    }
    static void Finish(string status, bool exit)
    {
        bool owned = Owned();
        try
        {
            EditorApplication.update -= Tick; EditorApplication.playModeStateChanged -= State;
            AssemblyReloadEvents.beforeAssemblyReload -= Abort; Application.logMessageReceived -= Log;
            while (Work.Count > 0) (Work.Pop() as IDisposable)?.Dispose();
            Application.runInBackground = background;
            File.WriteAllText(Path.Combine(output, "PlayResult.json"), JsonConvert.SerializeObject(new
            { status, checks, actions, results = Results, errors = Errors }, Formatting.Indented));
        }
        finally
        {
            if (refreshLocked) { AssetDatabase.AllowAutoRefresh(); refreshLocked = false; }
            if (locked) { EditorApplication.UnlockReloadAssemblies(); locked = false; }
        }
        if (owned && exit && EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
    static IEnumerator Wait(float duration)
    { float until = Time.unscaledTime + duration; while (Time.unscaledTime < until) yield return null; }
    static void ResetVoices()
    {
        var action = UnityEngine.Object.FindFirstObjectByType<CombatActionSfxService>();
        if (action != null) foreach (var s in action.GetComponentsInChildren<AudioSource>(true)) s.Stop();
        foreach (var s in service.GetComponentsInChildren<AudioSource>(true)) s.Stop();
        typeof(MeleeElementSfxService).GetMethod("Update", Fields).Invoke(service, null);
        MeleeElementSfxService.Configure(catalog);
    }
    static AudioSource[] Playing() => service.GetComponentsInChildren<AudioSource>(true).Where(s => s.isPlaying).ToArray();
    static float Expected(float energy) => energy <= 1 ? 0 : energy >= 60 ? 1 : (energy - 1) / 59;
    static void CheckVoices(AudioSource[] sources, int count, float volume, string label)
    {
        Check(sources.Length == count, label + " voice count " + sources.Length);
        if (count == 0) return;
        var sorted = sources.OrderByDescending(s => s.volume).ToArray();
        Check(Mathf.Abs(sorted[0].volume - volume) < .0001f, label + " volume");
        Check(sorted.All(s => !s.mute && s.clip != null && s.clip.loadState == AudioDataLoadState.Loaded), label + " loaded/unmuted");
        if (count == 2)
        {
            Check(Mathf.Abs(sorted[1].volume - volume * .5f) < .0001f, label + " parry half layer");
            Check(sorted[0].clip == sorted[1].clip && sorted[0].pitch == sorted[1].pitch
                && sorted[0].transform.position == sorted[1].transform.position, label + " matching layer");
        }
    }
    static void CheckCue(string label, Func<float, bool> play, float baseVolume, float energy, int voiceCount = 1)
    {
        ResetVoices(); int before = Field<Dictionary<int, float>>(service, "nextAllowedTime").Count;
        bool played = play(energy); float scale = Expected(energy);
        Check(played == (scale > 0), label + " admission");
        CheckVoices(Playing(), scale > 0 ? voiceCount : 0, Mathf.Clamp01(baseVolume) * scale, label);
        if (scale == 0) Check(Field<Dictionary<int, float>>(service, "nextAllowedTime").Count == before, label + " silence consumes no cooldown");
    }
    static IEnumerator Run()
    {
        var actor = PlayerContext.GetOrCreate().CurrentActor;
        var melee = actor.GetComponent<MeleeRuntime>();
        var energy = actor.GetComponent<OverburstElementEnergy>();
        if (energy == null) energy = actor.gameObject.AddComponent<OverburstElementEnergy>();
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>(WeaponPath);
        var ui = EnemyThemeTrialHarness.Current;
        EnemySpawnService spawn = null;
        EnemyActor enemy = null;
        try
        {
            Check(catalog != null && weapon != null && service != null && melee != null, "Product audio/player assets");
            foreach (var element in Elements)
            foreach (float value in new[] { 0f, 1f, 2f, 10f, 30f, 59f, 60f, 100f, 200f })
            {
                var entry = catalog.entries.First(e => e.element == element);
                CheckCue(element + " slash " + value, e => MeleeElementSfxService.TryPlaySlash(element, actor.transform.position, e), entry.slash.volume, value);
                CheckCue(element + " hit " + value, e => MeleeElementSfxService.TryPlayHit(element, actor.transform.position, energy: e), entry.hit.volume, value);
                var critical = entry.criticalHit.clips.Any(c => c != null) ? entry.criticalHit : entry.hit;
                CheckCue(element + " critical " + value, e => MeleeElementSfxService.TryPlayHit(element, actor.transform.position, true, e), critical.volume, value);
                var impact = element == WeaponElement.Light ? catalog.upperHeavy.lightHit1 : entry.heavyImpact;
                CheckCue(element + " slam " + value, e => element == WeaponElement.Light
                    ? MeleeElementSfxService.TryPlayLightHeavyHit(0, actor.transform.position, true, e)
                    : MeleeElementSfxService.TryPlayHeavyImpact(element, actor.transform.position, true, e), impact.volume, value, 2);
                if (entry.followUp.clips.Any(c => c != null))
                    CheckCue(element + " follow-up " + value, e => MeleeElementSfxService.TryPlayFollowUp(element, actor.transform.position, e), entry.followUp.volume, value);
            }
            Results.Add(new { label = "Five element cue boundaries", energies = new[] { 0, 1, 2, 10, 30, 59, 60, 100, 200 }, passed = true });
            ResetVoices();
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System); melee.SetManualInputEnabled(true);
            if (!ui.InArena) ui.ToggleArena(); yield return Wait(.8f);
            Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(actor.transform, out spawn), "Arena spawn service");
            foreach (var table in ui.tables) Check(spawn.RegisterAdditionalCatalog(table.Catalog, out _), "Enemy catalog");
            Vector3 testPosition = actor.transform.position;
            var definition = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).First(d => d != null && d.Grade.GradeType != EnemyGradeType.Boss);
            actor.Health.SetMaxHp(1000000f, true);
            foreach (var element in Elements)
            foreach (float initial in new[] { 0f, 30f })
            {
                melee.CancelCurrentAttackState(); ResetVoices();
                ActorTeleportUtility.TeleportSafely(actor.transform, testPosition, Quaternion.identity);
                yield return Wait(.12f);
                var item = new ItemData(weapon, 1, ItemGrade.Common, element: element);
                Check(actor.Equipment.EquipWeaponItem(item), "Weak weapon equip"); energy.Clear();
                typeof(OverburstElementEnergy).GetField("<Amount>k__BackingField", Fields).SetValue(energy, initial);
                var point = actor.transform.position + Vector3.forward * .8f;
                Check(Physics.Raycast(point + Vector3.up * 4, Vector3.down, out var floor, 9, LayerMask.GetMask("Default", "Environment", "Ground")), "Weak target floor");
                Check(spawn.TrySpawn(new EnemySpawnRequest(definition, floor.point + Vector3.up * .035f, Quaternion.LookRotation(Vector3.back), actor.transform), out enemy), "Real weak enemy");
                enemy.AI.enabled = false; enemy.Movement.StopMovement(); enemy.Health.SetMaxHp(1000000f, true);
                float hp = enemy.Health.CurrentHp;
                Check(melee.TryStartAction(new WeaponActionRequest(WeaponActionSource.PlayerInput, null, Vector3.forward), out _) == WeaponActionResult.Accepted, "Real weak attack");
                float limit = Time.unscaledTime + 4;
                while (enemy.Health.CurrentHp == hp) { Check(Time.unscaledTime < limit && melee.IsAttackInProgress, "Actual weak damage"); yield return null; }
                var entry = catalog.entries.First(e => e.element == element);
                var hitClips = entry.hit.clips.Concat(entry.criticalHit.clips).Where(c => c != null).ToArray();
                var voices = Playing().Where(s => hitClips.Contains(s.clip)).ToArray();
                Check(energy.Amount > initial, "Weak hit charges before feedback");
                CheckVoices(voices, initial == 0 ? 0 : 1, Expected(initial), element + " pre-hit snapshot");
                Results.Add(new { label = element + " real weak", initial, after = energy.Amount, volumes = voices.Select(s => s.volume).ToArray(), passed = true });
                melee.CancelCurrentAttackState(); spawn.Release(enemy); enemy = null; yield return Wait(.2f);
            }
            var groundClips = new[] { CombatActionSfxService.ResolveNamedClip("GreatswordGround_EarthExplosion1"), CombatActionSfxService.ResolveNamedClip("GreatswordGround_EarthExplosion2") };
            Check(groundClips.All(c => c != null), "Basic ground clips loaded");
            AudioSource[] GroundPlaying() => UnityEngine.Object.FindFirstObjectByType<CombatActionSfxService>()
                .GetComponentsInChildren<AudioSource>(true).Where(s => s.isPlaying && groundClips.Contains(s.clip)).ToArray();
            var heavyCases = Elements.SelectMany(element => new[] { 0f, 1f, 2f, 30f, 60f }.Select(initial => (element, initial)))
                .Concat(new[] { (WeaponElement.None, 0f), (WeaponElement.Light, 200f) });
            foreach (var scenario in heavyCases)
            foreach (bool parry in new[] { false, true })
            {
                var element = scenario.Item1; float initial = scenario.Item2;
                ResetVoices(); melee.CancelCurrentAttackState();
                ActorTeleportUtility.TeleportSafely(actor.transform, testPosition, Quaternion.identity);
                yield return Wait(.12f);
                var item = new ItemData(weapon, 1, ItemGrade.Common, element: element);
                Check(actor.Equipment.EquipWeaponItem(item), "Heavy equip"); energy.Clear();
                if (initial > energy.BaseMaximum)
                {
                    // Real charging also initializes Light's full-energy hold timer.
                    for (int i = 0; energy.Amount < initial - .001f && i < 100; i++)
                        Check(energy.RecordConfirmedHit(item.runtimeInstanceId, element, 90000 + actions * 100 + i, 1f), "Light overcharge fixture");
                    Check(Mathf.Abs(energy.Amount - initial) < .001f, "Light full energy prepared");
                    ResetVoices();
                }
                else typeof(OverburstElementEnergy).GetField("<Amount>k__BackingField", Fields).SetValue(energy, initial);
                Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "Actual heavy");
                if (parry) melee.NotifyHeavyParried(Field<int>(melee, "activeActionId"));
                float limit = Time.unscaledTime + 8;
                while (!Field<bool>(melee, "heavyDischargeCommitted"))
                {
                    Check(GroundPlaying().Length == 0, "No basic ground sound before slam");
                    if (Time.unscaledTime >= limit || !melee.IsAttackInProgress)
                    {
                        Results.Add(new { label = "Interrupted heavy", element, initial, parry, position = actor.transform.position.ToString(),
                            active = melee.IsAttackInProgress, parryStage = melee.IsHeavyParryMotionActive, health = actor.Health.CurrentHp,
                            duration = Field<float>(melee, "attackDuration"), start = Field<float>(melee, "attackStartTime"), now = Time.time,
                            stage = Field<object>(melee, "heavyParryStage").ToString() });
                        Check(false, "Heavy reaches slam: " + element + " " + initial + " parry=" + parry);
                    }
                    yield return null;
                }
                var impact = element == WeaponElement.Light ? catalog.upperHeavy.lightHit1 : catalog.entries.First(e => e.element == element).heavyImpact;
                var sources = Playing().Where(s => impact.clips.Contains(s.clip)).ToArray();
                float scale = element == WeaponElement.None ? 0f : Expected(initial);
                var groundSources = GroundPlaying();
                CheckVoices(groundSources, parry ? 2 : 1, 1f, element + " basic ground " + initial);
                var expectedGround = groundClips[initial / energy.BaseMaximum >= CombatActionSfxService.GroundSecondTierEnergy ? 1 : 0];
                Check(groundSources.All(s => s.clip == expectedGround), "Existing ground tier selection");
                CheckVoices(sources, scale == 0 ? 0 : parry ? 2 : 1, Mathf.Clamp01(impact.volume) * scale, element + " actual slam " + initial);
                if (initial > 0) Check(Mathf.Abs(melee.ElementSfxEnergy - initial) < .001f, "Committed sound energy survives consumption/refund");
                float expectedRefund = parry ? Mathf.Min(initial, energy.BaseMaximum) * .5f : 0f;
                Check(Mathf.Abs(energy.Amount - expectedRefund) < .001f, "Heavy energy consumption/refund unchanged");
                float peak = 0, groundPeak = 0, until = Time.unscaledTime + .12f;
                var groundPeaks = new float[groundSources.Length];
                var buffer = new float[256];
                while (Time.unscaledTime < until)
                {
                    foreach (var source in sources) { source.GetOutputData(buffer, 0); peak = Mathf.Max(peak, buffer.Max(Mathf.Abs)); }
                    for (int i = 0; i < groundSources.Length; i++)
                    {
                        groundSources[i].GetOutputData(buffer, 0);
                        groundPeaks[i] = Mathf.Max(groundPeaks[i], buffer.Max(Mathf.Abs));
                        groundPeak = Mathf.Max(groundPeak, groundPeaks[i]);
                    }
                    yield return null;
                }
                if (scale > 0) Check(peak > .000001f, "Actual slam PCM output");
                Check(groundPeak > .000001f, "Actual basic ground PCM output at every energy");
                Check(groundPeaks.All(p => p > .000001f), "Every basic ground voice has PCM output");
                Results.Add(new { label = element + " actual heavy", initial, parry, volumes = sources.Select(s => s.volume).ToArray(), peak,
                    groundVolumes = groundSources.Select(s => s.volume).ToArray(), groundClip = expectedGround.name, groundPeak, groundPeaks, after = energy.Amount, passed = true });
                actions++;
                File.WriteAllText(Path.Combine(output, "Progress.json"), JsonConvert.SerializeObject(new { status = "RUNNING", actions, checks, element, initial, parry }, Formatting.Indented));
                melee.CancelCurrentAttackState(); LightTripleImpactScheduler.ClearAll(); yield return Wait(.15f);
            }
            // A delayed light cast must keep its own strength after a weapon/energy change.
            foreach (float initial in new[] { 1f, 30f, 60f })
            {
                ResetVoices(); energy.Clear();
                LightTripleImpactScheduler.Submit(actor.gameObject, actor.GetComponent<CombatTarget>().Team, actor.transform.position, .1f, 1f, 1f, .05f, 1, initial);
                yield return Wait(.12f);
                var settings = catalog.upperHeavy.lightHit2;
                var sources = Playing().Where(s => settings.clips.Contains(s.clip)).ToArray();
                CheckVoices(sources, initial <= 1 ? 0 : 1, Mathf.Clamp01(settings.volume) * Expected(initial), "Delayed light snapshot " + initial);
                Results.Add(new { label = "Delayed light", initial, liveEnergy = energy.Amount, volumes = sources.Select(s => s.volume).ToArray(), passed = true });
                LightTripleImpactScheduler.ClearAll();
            }
        }
        finally
        {
            if (enemy != null && enemy.IsLeased) spawn?.Release(enemy);
            if (Owned()) { melee.CancelCurrentAttackState(); LightTripleImpactScheduler.ClearAll(); ResetVoices(); }
        }
    }
}
