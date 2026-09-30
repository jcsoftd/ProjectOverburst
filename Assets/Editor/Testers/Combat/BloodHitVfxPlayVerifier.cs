using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

// Local editor-only fixture. Its output and source backup live outside the Unity project.
public static class BloodHitVfxPlayVerifier
{
    private static readonly Stack<IEnumerator> Work = new Stack<IEnumerator>();
    private static readonly List<string> Passed = new List<string>();
    private static string Output => Path.GetFullPath("../개인파일/코덱스산출/CombatVfx/20260923_BloodGoal");
    private static int lastFrame;
    private static double deadline;
    private static string testName;
    public static string Result { get; private set; } = "IDLE";
    public static void Start(bool field = false)
    {
        if (!EditorApplication.isPlaying || Work.Count > 0) throw new Exception("Requires idle Play Mode");
        Directory.CreateDirectory(Output);
        Passed.Clear(); Result = "RUNNING"; lastFrame = -1;
        testName = field ? "field" : "lifecycle";
        deadline = EditorApplication.timeSinceStartup + 600;
        Work.Push(field ? Field() : Verify());
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += PlayModeChanged;
    }
    private static void PlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode && Result == "RUNNING")
            Finish("FAIL interrupted by Play exit");
    }
    private static void Tick()
    {
        try
        {
            Check(EditorApplication.isPlaying && EditorApplication.timeSinceStartup < deadline, "Play/time limit");
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            while (Work.Count > 0)
            {
                var next = Work.Peek();
                if (!next.MoveNext()) { (Work.Pop() as IDisposable)?.Dispose(); continue; }
                if (next.Current is IEnumerator nested) { Work.Push(nested); continue; }
                return;
            }
            Finish("PASS");
        }
        catch (Exception error) { Finish("FAIL " + error); }
    }
    private static void Finish(string status)
    {
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        while (Work.Count > 0) (Work.Pop() as IDisposable)?.Dispose();
        Result = status + "\n" + string.Join("\n", Passed);
        File.WriteAllText(Path.Combine(Output, "latest-result.txt"), Result);
        File.WriteAllText(Path.Combine(Output, testName + "-result.txt"), Result);
        Debug.Log("[BloodHitVerifier] " + Result);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static IEnumerator Wait(float seconds)
    {
        float end = Time.time + seconds;
        while (Time.time < end) yield return null;
    }
    private static BloodHitVfxService Service => Object.FindFirstObjectByType<BloodHitVfxService>();
    private static CombatHitFeedbackRequest Hit(CombatHealth health, int sequence, CombatImpactShape shape, bool critical = false, bool lethal = false)
        => new CombatHitFeedbackRequest(health, sequence, null, critical, WeaponElement.None,
            health.transform.position + Vector3.up, false, worldDirection: Vector3.forward,
            isLethal: lethal, target: health, impactShape: shape, impactDirection: Vector3.right);

    private static IEnumerator Verify()
    {
        Check(Service != null, "Runtime catalog bootstrap");
        var player = PlayerInputFacade.Current;
        Check(player != null, "Player loaded");
        var ui = EnemyThemeTrialHarness.Current;
        bool entered = !ui.InArena;
        if (entered) ui.ToggleArena();
        yield return Wait(.7f);
        Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out var service), "Spawn service");
        foreach (var table in ui.tables) service.RegisterAdditionalCatalog(table.Catalog, out _);
        var cameraObject = new GameObject("Blood review camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.CopyFrom(Camera.main); camera.enabled = false;
        var texture = RenderTexture.GetTemporary(960, 640, 24);
        var pixels = new Texture2D(960, 640, TextureFormat.RGB24, false);
        camera.targetTexture = texture;
        EnemyActor actor = null;
        try
        {
            int count = 0;
            foreach (var table in ui.tables)
            {
                foreach (var definition in table.Entries.Select(x => x.definition).Distinct())
                {
                    var request = new EnemySpawnRequest(definition, player.transform.position + Vector3.forward * 3,
                        Quaternion.identity, player.transform, null, player.transform, null, 1, 1, 77);
                    Check(service.TrySpawn(request, out actor), "Spawn " + definition.EnemyId);
                    actor.AI.enabled = false; actor.Movement.StopMovement();
                    Check(actor.GetComponent<BloodHitTarget>()?.Profile != null, "Blood profile " + definition.EnemyId);
                    actor.RequestPoolRelease(); actor = null; count++;
                }
            }
            Check(count == 21, "21 mapped species, got " + count);
            Passed.Add("21 actual pooled actors have blood profiles");
            int sequence = 1;
            foreach (var table in ui.tables)
            {
                var definition = table.Entries.Select(x => x.definition).First();
                var request = new EnemySpawnRequest(definition, player.transform.position + Vector3.forward * 3,
                    Quaternion.identity, player.transform, null, player.transform, null, 1, 1, 77);
                Check(service.TrySpawn(request, out actor), "Review spawn");
                actor.AI.enabled = false; actor.Movement.StopMovement();
                yield return Wait(.25f);
                var target = actor.GetComponent<CombatTarget>();
                Vector3 point = CombatTargetVfxPlacement.ResolveContact(target, actor.transform.position + Vector3.up, Vector3.forward, out float size);
                camera.transform.position = point + new Vector3(3, 3, -5);
                camera.transform.LookAt(point); camera.orthographic = true; camera.orthographicSize = 2.7f;
                foreach (CombatImpactShape shape in Enum.GetValues(typeof(CombatImpactShape)))
                {
                    int before = Service.PlayedCount;
                    BloodHitVfxService.Request(Hit(actor.Health, sequence++, shape), point, size);
                    int peak = 0;
                    for (int frame = 0; frame < 12; frame++)
                    {
                        yield return null;
                        peak = Mathf.Max(peak, Service.GetComponentsInChildren<VisualEffect>().Sum(v => Mathf.Max(0, v.aliveParticleCount)));
                        Capture(camera, texture, pixels, table.ThemeId + "_" + shape + "_" + frame + ".png");
                        yield return Wait(.025f);
                    }
                    Check(Service.PlayedCount == before + 1, "Visible request " + table.ThemeId + " " + shape);
                    Check(peak > 0, "Actual GPU peak " + table.ThemeId + " " + shape);
                    Passed.Add(table.ThemeId + " " + shape + " GPU peak=" + peak);
                    var active = Service.GetComponentsInChildren<VisualEffect>().Where(x => x.gameObject.activeSelf).ToArray();
                    Check(active.Length > 0, "Active GPU instance");
                    var expected = actor.GetComponent<BloodHitTarget>().Profile.mainColor.linear;
                    Check(active.Any(x => Vector4.Distance(x.GetVector4("BloodColorMain"), expected) < .001f), "Instance palette");
                    yield return Wait(2.6f);
                    Check(Service.ActiveCount == 0, "Lease expiry");
                }
                actor.RequestPoolRelease(); actor = null;
            }
            Passed.Add("4 palettes x 3 shapes: GPU emission peak, linear color readback, 144 rendered frames, expired");
            var d = ui.tables[0].Entries.First().definition;
            Check(service.TrySpawn(new EnemySpawnRequest(d, player.transform.position + Vector3.forward * 3,
                Quaternion.identity, player.transform, null, player.transform, null, 1, 1, 77), out actor), "Load actor");
            actor.AI.enabled = false; actor.Movement.StopMovement();
            Vector3 contact = actor.transform.position + Vector3.up;
            int excludedBefore = Service.PlayedCount;
            actor.Health.TakeDamage(new DamageInfo(0, contact, player.gameObject));
            actor.Health.TakeDamage(new DamageInfo(1, contact, player.gameObject, isDamageOverTime: true, triggersOnHitEffects: false));
            yield return Wait(.05f);
            Check(Service.PlayedCount == excludedBefore, "No blood from zero or periodic damage");
            Passed.Add("Zero and periodic damage do not emit blood");
            int baseline = Service.PlayedCount;
            for (int i = 0; i < 50; i++) BloodHitVfxService.Request(Hit(actor.Health, sequence++, CombatImpactShape.Sweep, i >= 42), contact, 1);
            yield return Wait(.05f);
            Check(Service.PlayedCount - baseline == 8, "50 requests bounded to 8/frame");
            for (int j = 0; j < 2; j++)
            {
                for (int i = 0; i < 8; i++) BloodHitVfxService.Request(Hit(actor.Health, sequence++, CombatImpactShape.Thrust), contact, 1);
                yield return Wait(.05f);
            }
            Check(Service.ActiveCount == 24 && Service.PeakActiveCount == 24, "24 active cap");
            int full = Service.PlayedCount;
            BloodHitVfxService.Request(Hit(actor.Health, sequence++, CombatImpactShape.Downward, true, true), contact, 1);
            yield return Wait(.05f);
            Check(Service.ActiveCount == 24 && Service.PlayedCount == full + 1, "Lethal preempts lower priority");
            var oldScene = SceneManager.GetActiveScene();
            var temporary = SceneManager.CreateScene("Blood lifecycle fixture");
            SceneManager.SetActiveScene(temporary);
            Check(Service.ActiveCount == 0, "Scene change clears pool");
            SceneManager.SetActiveScene(oldScene);
            var unload = SceneManager.UnloadSceneAsync(temporary);
            while (!unload.isDone) yield return null;
            Check(Service.GetComponentsInChildren<VisualEffect>(true).Length == 24, "No pool growth");
            for (int i = 0; i < 30; i++)
            {
                BloodHitVfxService.Request(Hit(actor.Health, sequence++, (CombatImpactShape)(i % 3), true, true), contact, 1);
                yield return Wait(.035f);
            }
            yield return Wait(2.7f);
            Check(Service.ActiveCount == 0 && Service.GetComponentsInChildren<VisualEffect>(true).Length == 24, "Reuse cleanup");
            Passed.Add("50 burst -> 8/frame, 24 active cap, lethal priority, scene clear, 30 repeated leases, fixed 24 instances");
        }
        finally
        {
            if (actor != null && actor.IsLeased) actor.RequestPoolRelease();
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(texture); Object.Destroy(pixels); Object.Destroy(cameraObject);
            if (entered && ui.InArena) ui.ToggleArena();
        }
    }
    private static void Capture(Camera camera, RenderTexture texture, Texture2D pixels, string name)
    {
        var previous = RenderTexture.active;
        try
        {
            camera.Render(); RenderTexture.active = texture;
            pixels.ReadPixels(new Rect(0, 0, 960, 640), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(Output, name), pixels.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; }
    }
    private static IEnumerator Field()
    {
        var ui = EnemyThemeTrialHarness.Current;
        bool entered = !ui.InArena;
        if (entered) ui.ToggleArena();
        yield return Wait(.7f);
        int before = Service.PlayedCount;
        SessionState.SetString("MonsterThemePlayVerifier.output", Output);
        SessionState.SetString("MonsterThemeCombatField.table", "PrimalHunt");
        SessionState.SetInt("MonsterThemeCombatField.count", 50);
        SessionState.SetFloat("MonsterThemeCombatField.captureStart", 28);
        SessionState.SetFloat("MonsterThemeCombatField.captureEnd", 30);
        try
        {
            yield return MonsterThemeCombatFieldVerifier.Verify(ui, PlayerInputFacade.Current);
            Check(Service.PlayedCount > before, "Real melee produced blood");
            Check(Service.PeakActiveCount <= 24, "Field pool cap");
            Passed.Add("50-monster real Gameplay melee; blood=" + (Service.PlayedCount - before)
                + "; peak=" + Service.PeakActiveCount + "; dropped=" + Service.DroppedCount);
        }
        finally { ui.Clear(); if (entered && ui.InArena) ui.ToggleArena(); }
    }
}
