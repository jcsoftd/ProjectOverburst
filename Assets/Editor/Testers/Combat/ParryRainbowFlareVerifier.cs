using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ParryRainbowFlareVerifier
{
    private const string Key = "ParryRainbowFlareVerifier";
    private static IEnumerator work;
    private static int frame;
    private static double deadline;
    private static readonly List<string> checks = new List<string>();
    private static readonly List<string> errors = new List<string>();
    private static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");

    static ParryRainbowFlareVerifier() => EditorApplication.playModeStateChanged += State;

    public static string VerifyAssets()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ParryRainbowFlareBuilder.PrefabPath);
        var library = AssetDatabase.LoadAssetAtPath<EnemyTelegraphVisualLibrary>(ParryRainbowFlareBuilder.LibraryPath);
        Require(prefab != null && library != null && library.ParrySuccess == prefab, "Library reference");
        Require(Resources.Load<GameObject>("Combat/VFX/PF_ParrySuccessRainbowFlare") == prefab, "Resource reference");
        var particles = prefab.GetComponentsInChildren<ParticleSystem>(true);
        Require(particles.Length == 8, "Eight authored particle systems");
        foreach (var ps in particles)
        {
            var main = ps.main;
            Require(!main.loop && !main.playOnAwake && main.useUnscaledTime && Mathf.Approximately(main.simulationSpeed, ParryRainbowFlareBuilder.PlaybackSpeed), "One-shot/unscaled playback: " + ps.name);
            Require(main.stopAction == ParticleSystemStopAction.None && main.scalingMode == ParticleSystemScalingMode.Hierarchy, "Pool/scale policy: " + ps.name);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null && renderer.enabled)
                Require(renderer.sharedMaterial != null && !ShaderUtil.ShaderHasError(renderer.sharedMaterial.shader), "Material/shader: " + ps.name);
        }
        foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
            Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0, "Missing script: " + t.name);
        Require(prefab.GetComponentsInChildren<AudioSource>(true).Length == 0, "Existing parry audio owns sound");
        return "PASS: references, 8 systems, shader, missing scripts, unscaled one-shot, pooling.";
    }

    public static void Run(string label)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required.");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PersistentScene")
            throw new InvalidOperationException("PersistentScene required; open scenes are not changed by this verifier.");
        VerifyAssets();
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출/Combat/20261001_ParryRainbowFlare", label));
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output);
        SessionState.SetBool(Key, true);
        SessionState.SetString(Key + ".status", "RUNNING");
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
    }

    private static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            checks.Clear(); errors.Clear(); frame = -1;
            SessionState.SetBool(Key + ".background", Application.runInBackground);
            Application.runInBackground = true;
            deadline = EditorApplication.timeSinceStartup + 150;
            work = Sequence();
            Application.logMessageReceived += Log;
            EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            if (Status == "RUNNING")
            {
                SessionState.SetString(Key + ".status", "ABORTED");
                File.WriteAllText(Path.Combine(Output, "result.json"), JsonConvert.SerializeObject(new { status = "ABORTED", checks, errors }, Formatting.Indented));
            }
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= Log;
            (work as IDisposable)?.Dispose(); work = null;
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY", null);
            SessionState.SetBool(Key, false);
            IsolatedSavePlayGuard.UseRealAccount();
        }
    }

    private static void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        {
            string error = message + "\n" + stack;
            if (!errors.Contains(error)) errors.Add(error);
        }
    }

    private static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException();
            if (work.MoveNext()) return;
            Finish(errors.Count == 0 ? "PASS" : "FAIL");
        }
        catch (Exception e) { errors.Add(e.ToString()); Finish("FAIL"); }
    }

    private static void Finish(string status)
    {
        SessionState.SetString(Key + ".status", status);
        File.WriteAllText(Path.Combine(Output, "result.json"), JsonConvert.SerializeObject(new { status, checks, errors }, Formatting.Indented));
        EditorApplication.ExitPlaymode();
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Check(bool value, string message) { Require(value, message); checks.Add(message); }

    private static void Shot(string name, ParticleSystem[] systems)
    {
        foreach (var system in systems) system.Simulate(.15f, false, true, true);
        var camera = Camera.main;
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        var target = new RenderTexture(1280, 720, 24);
        var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(Path.Combine(Output, name + ".png"), pixels.EncodeToPNG());
            var withFlare = pixels.GetPixels32();
            var renderers = systems.Select(p => p.GetComponent<ParticleSystemRenderer>()).Where(r => r != null && r.enabled).ToArray();
            try
            {
                foreach (var renderer in renderers) renderer.enabled = false;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(Output, name + "-without.png"), pixels.EncodeToPNG());
                var withoutFlare = pixels.GetPixels32();
                int changed = 0;
                for (int i = 0; i < withFlare.Length; i++)
                    if (Math.Abs(withFlare[i].r - withoutFlare[i].r) + Math.Abs(withFlare[i].g - withoutFlare[i].g) + Math.Abs(withFlare[i].b - withoutFlare[i].b) > 12) changed++;
                File.WriteAllText(Path.Combine(Output, name + "-render.json"), JsonConvert.SerializeObject(new { changed, depth = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().requiresDepthTexture, systems = systems.Select(p => { var particles = new ParticleSystem.Particle[p.particleCount]; p.GetParticles(particles); return new { p.name, p.time, p.particleCount, world = p.transform.position.ToString(), screen = camera.WorldToViewportPoint(p.transform.position).ToString(), values = particles.Select(v => new { size = v.GetCurrentSize(p), color = v.GetCurrentColor(p).ToString(), position = v.position.ToString(), size3d = v.GetCurrentSize3D(p).ToString() }) }; }) }, Formatting.Indented));
                Check(changed > 100, "Rendered flare changes pixels: " + changed);
            }
            finally { foreach (var renderer in renderers) renderer.enabled = true; }
            if (name.StartsWith("feedback") && !File.Exists(Path.Combine(Output, "burst-0.png")))
            {
                float[] phases = { .03f, .09f, .16f, .27f };
                for (int i = 0; i < phases.Length; i++)
                {
                    foreach (var system in systems) system.Simulate(phases[i], false, true, true);
                    camera.Render(); RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
                    File.WriteAllBytes(Path.Combine(Output, "burst-" + i + ".png"), pixels.EncodeToPNG());
                }
            }

        }
        finally
        {
            foreach (var system in systems) system.Play(false);
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            UnityEngine.Object.Destroy(pixels);
            target.Release(); UnityEngine.Object.Destroy(target);
        }
    }

    private static IEnumerator Sequence()
    {
        EnemySpawnService spawn = null;
        var leased = new List<EnemyActor>();
        var fixtures = new List<EnemyAbilitySet>();
        MeleeRuntime melee = null;
        try
        {
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || !WorldSessionState.IsHideout) yield return null;
            Check(Path.GetFullPath(Overburst.Persistence.AccountBootstrap.SaveDirectory).StartsWith(Output, StringComparison.OrdinalIgnoreCase), "Isolated account");
            var player = PlayerInputFacade.Current;
            var actor = PlayerContext.GetOrCreate().CurrentActor;
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            Check(actor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common)), "Equipped greatsword");
            actor.Health.SetMaxHp(100000, true);
            var harness = EnemyThemeTrialHarness.Current;
            float readyAt = Time.unscaledTime + .8f;
            while (Time.unscaledTime < readyAt) yield return null;
            Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out spawn), "Spawn service");
            foreach (var table in harness.tables) spawn.RegisterAdditionalCatalog(table.Catalog, out _);
            var definitions = harness.tables.SelectMany(t => t.Entries).Select(e => e.definition).Where(d => d != null).Distinct();
            var definition = definitions.First(d => d.Grade.GradeType == EnemyGradeType.Elite && Enumerable.Range(0, d.AbilitySet.Count).Any(i => d.AbilitySet.GetAbility(i).ExecutionMode == EnemyAbilityExecutionMode.MeleeArc && d.AbilitySet.GetAbility(i).IsParryable));
            var ability = Enumerable.Range(0, definition.AbilitySet.Count).Select(i => definition.AbilitySet.GetAbility(i)).First(a => a.ExecutionMode == EnemyAbilityExecutionMode.MeleeArc && a.IsParryable);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ParryRainbowFlareBuilder.PrefabPath);
            var target = player.GetComponent<CombatTarget>();
            melee = player.GetComponent<MeleeRuntime>();
            foreach (int count in new[] { 1, 4, 4 })
            {
                melee.CancelCurrentAttackState();
                float settleAt = Time.unscaledTime + 1.6f;
                while (Time.unscaledTime < settleAt) yield return null;
                // 강공 동시 시작 수는 전투 조율기가 제한한다. 상위 마리 수는 성공 피드백 경로를 따로 검사한다.
                if (count > 1)
                {
                    var beforeFeedback = TransientVfxPool.GetStatistics(prefab);
                    var success = typeof(PlayerParryController).GetMethod("PlaySuccess", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    success.Invoke(player.GetComponent<PlayerParryController>(), new object[] { player.transform.position + Vector3.up + Vector3.forward * 1.5f, count, true });
                    yield return null;
                    var feedbackStats = TransientVfxPool.GetStatistics(prefab);
                    Check(feedbackStats.Requests == beforeFeedback.Requests + 1 && feedbackStats.Active == 1, "One flare for success-feedback count " + count);
                    var visible = UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Where(p => p.gameObject.activeInHierarchy && p.transform.parent != null && p.transform.parent.name == "VFX_Rainbow_Lens_Flare_Combo_01").ToArray();
                    Check(visible.Any(p => p.particleCount > 0), "Repeated flare particles");
                    Check(Mathf.Approximately(visible[0].transform.parent.parent.localScale.x, prefab.transform.localScale.x), "Same authored size for feedback count " + count);
                    Shot("feedback-" + count + "-" + checks.Count, visible);
                    float doneAt = Time.unscaledTime + 1.5f;
                    while (Time.unscaledTime < doneAt) yield return null;
                    Check(TransientVfxPool.GetStatistics(prefab).Active == 0, "Repeated flare returned");
                    Check(Mathf.Approximately(Time.timeScale, 1), "Repeated time restored");
                    continue;
                }
                for (int i = 0; i < count; i++)
                {
                    var direction = Quaternion.AngleAxis(i * 360f / count, Vector3.up) * Vector3.forward;
                    var point = player.transform.position + direction * Mathf.Max(.9f, ability.Range * .6f);
                    Check(Physics.Raycast(point + Vector3.up * 4, Vector3.down, out var floor, 9, LayerMask.GetMask("Default", "Environment", "Ground")), "Fixture floor");
                    Check(spawn.TrySpawn(new EnemySpawnRequest(definition, floor.point + Vector3.up * .035f, Quaternion.LookRotation(-direction), player.transform), out var enemy), "Fixture spawn");
                    leased.Add(enemy);
                    enemy.AI.enabled = false;
                    enemy.Movement.StopMovement();
                    enemy.Health.SetMaxHp(100000, true);
                    var fixture = ScriptableObject.CreateInstance<EnemyAbilitySet>(); fixtures.Add(fixture);
                    var serialized = new SerializedObject(fixture);
                    serialized.FindProperty("abilitySetId").stringValue = "parry-rainbow-fixture";
                    var array = serialized.FindProperty("abilities"); array.arraySize = 1;
                    array.GetArrayElementAtIndex(0).objectReferenceValue = ability;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    enemy.AbilityController.Configure(fixture, 1f, 1f);
                }
                yield return null;
                foreach (var enemy in leased) Check(enemy.AbilityController.TryStart(player.transform), "Enemy attack accepted");
                float timeout = Time.unscaledTime + 2;
                while (!leased.All(e => e.AbilityController.IsParryThreatTo(target)))
                {
                    Require(Time.unscaledTime < timeout, "Actual threat geometry timeout");
                    yield return null;
                }
                var parry = player.GetComponent<PlayerParryController>();
                int before = parry != null ? parry.SuccessCount : 0;
                float hp = actor.Health.CurrentHp;
                var poolBefore = TransientVfxPool.GetStatistics(prefab);
                Check(melee.TryStartHeavyAttack(Vector3.right) == WeaponActionResult.Accepted, "Heavy accepted");
                parry = player.GetComponent<PlayerParryController>();
                yield return null;
                Check(parry.SuccessCount == before + 1, "One success event for " + count + " enemies");
                var successClip = CombatActionSfxService.ResolveNamedClip("ParryClash_ImpactRinging");
                var successVoices = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Where(a => a.isPlaying && a.clip == successClip).ToArray();
                Check(successClip != null && successVoices.Length == 2, "Two simultaneous parry success voices");
                Check(successVoices.Any(a => Mathf.Approximately(a.volume, .9f)) && successVoices.Any(a => Mathf.Approximately(a.volume, .6f)), "Main 0.9 plus additional 0.6 success sound");
                Check(leased.All(e => !e.AbilityController.IsExecuting && e.GetComponent<EnemyMovementReaction>().IsParryStunned), "All threats parry-stunned");
                Check(Mathf.Approximately(hp, actor.Health.CurrentHp), "No incoming damage");
                var stats = TransientVfxPool.GetStatistics(prefab);
                Check(stats.Requests == poolBefore.Requests + 1 && stats.Active == 1, "Exactly one pooled flare");
                var active = UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Where(p => p.gameObject.activeInHierarchy && p.transform.root != player.transform.root && p.transform.parent != null && p.transform.parent.name == "VFX_Rainbow_Lens_Flare_Combo_01").ToArray();
                Check(active.Any(p => p.particleCount > 0), "Visible flare particles");
                float expectedScale = prefab.transform.localScale.x;
                var flare = active[0].transform.parent.parent;
                Check(Mathf.Approximately(flare.localScale.x, expectedScale), "Fixed contact flash scale " + expectedScale);
                Shot("parry-" + count + "-" + checks.Count, active);
                float returnAt = Time.unscaledTime + 1.5f;
                while (Time.unscaledTime < returnAt) yield return null;
                Check(TransientVfxPool.GetStatistics(prefab).Active == 0, "Flare returned after completion");
                Check(Mathf.Approximately(Time.timeScale, 1), "Time restored");
                foreach (var enemy in leased) spawn.Release(enemy);
                leased.Clear();
            }
            Check(TransientVfxPool.GetStatistics(prefab).Created == 1, "One instance reused across successes");
        }
        finally
        {
            melee?.CancelCurrentAttackState();
            foreach (var enemy in leased) if (enemy != null && enemy.IsLeased && spawn != null) spawn.Release(enemy);
            foreach (var fixture in fixtures) UnityEngine.Object.Destroy(fixture);
        }
    }
}
