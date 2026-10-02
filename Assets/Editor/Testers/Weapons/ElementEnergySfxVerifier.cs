using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

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
        deadline = EditorApplication.timeSinceStartup + 200;
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
            foreach (var element in Elements)
            foreach (float initial in new[] { 0f, 1f, 30f, 60f })
            foreach (bool parry in new[] { false, true })
            {
                ResetVoices(); melee.CancelCurrentAttackState();
                ActorTeleportUtility.TeleportSafely(actor.transform, testPosition, Quaternion.identity);
                yield return Wait(.12f);
                var item = new ItemData(weapon, 1, ItemGrade.Common, element: element);
                Check(actor.Equipment.EquipWeaponItem(item), "Heavy equip"); energy.Clear();
                typeof(OverburstElementEnergy).GetField("<Amount>k__BackingField", Fields).SetValue(energy, initial);
                Check(melee.TryStartHeavyAttack(Vector3.forward) == WeaponActionResult.Accepted, "Actual heavy");
                if (parry) melee.NotifyHeavyParried(Field<int>(melee, "activeActionId"));
                float limit = Time.unscaledTime + 8;
                while (!Field<bool>(melee, "heavyDischargeCommitted"))
                {
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
                float scale = Expected(initial);
                CheckVoices(sources, scale == 0 ? 0 : parry ? 2 : 1, Mathf.Clamp01(impact.volume) * scale, element + " actual slam " + initial);
                if (initial > 0) Check(Mathf.Abs(melee.ElementSfxEnergy - initial) < .001f, "Committed sound energy survives consumption/refund");
                Check(Mathf.Abs(energy.Amount - (parry ? initial * .5f : 0)) < .001f, "Heavy energy consumption/refund unchanged");
                float peak = 0, until = Time.unscaledTime + .12f;
                var buffer = new float[256];
                while (Time.unscaledTime < until) { foreach (var source in sources) { source.GetOutputData(buffer, 0); peak = Mathf.Max(peak, buffer.Max(Mathf.Abs)); } yield return null; }
                if (scale > 0) Check(peak > .000001f, "Actual slam PCM output");
                Results.Add(new { label = element + " actual heavy", initial, parry, volumes = sources.Select(s => s.volume).ToArray(), peak, after = energy.Amount, passed = true });
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
