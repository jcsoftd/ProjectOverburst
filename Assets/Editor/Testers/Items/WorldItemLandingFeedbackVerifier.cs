using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class WorldItemLandingFeedbackVerifier
{
    const string Key = "Overburst.WorldItemLandingFeedbackVerifier.";
    static readonly List<string> checks = new List<string>();
    static readonly List<string> errors = new List<string>();
    static IEnumerator work;
    static int frame;
    static double deadline;
    static string Output => SessionState.GetString(Key + "output", "");
    public static string Status => SessionState.GetString(Key + "status", "NOT_RUN");

    static WorldItemLandingFeedbackVerifier() { EditorApplication.playModeStateChanged += State; }

    public static void Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Editor must be idle.");
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + "output", output);
        SessionState.SetBool(Key + "active", true);
        SessionState.SetString(Key + "status", "RUNNING");
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
    }

    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key + "active", false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            checks.Clear(); errors.Clear(); frame = -1;
            SessionState.SetBool(Key + "background", Application.runInBackground);
            Application.runInBackground = true;
            deadline = EditorApplication.timeSinceStartup + 90;
            work = Verify();
            Application.logMessageReceived += Log;
            EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= Log;
            (work as IDisposable)?.Dispose(); work = null;
            Application.runInBackground = SessionState.GetBool(Key + "background", false);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            Environment.SetEnvironmentVariable(IsolatedSavePlayGuard.Variable, null);
            SessionState.SetBool(Key + "active", false);
        }
    }

    static void Log(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message);
    }

    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Landing verification timed out.");
            if (!work.MoveNext()) Finish();
        }
        catch (Exception error) { checks.Add("FAIL " + error); Finish(); }
    }

    static void Finish()
    {
        string status = errors.Count == 0 && checks.All(c => c.StartsWith("PASS ")) ? "PASS" : "FAIL";
        SessionState.SetString(Key + "status", status);
        File.WriteAllText(Path.Combine(Output, "play-results.json"), JsonConvert.SerializeObject(new { status, checks, errors }, Formatting.Indented));
        EditorApplication.update -= Tick;
        EditorApplication.ExitPlaymode();
    }

    static void Check(bool pass, string description)
    {
        checks.Add((pass ? "PASS " : "FAIL ") + description);
        if (!pass) throw new InvalidOperationException(description);
    }

    static float[] SoundTimes()
    {
        var flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic;
        var service = typeof(ItemDropSfxService).GetField("instance", flags).GetValue(null);
        return service == null ? new float[16] : (float[])((float[])typeof(ItemDropSfxService)
            .GetField("nextAllowedTime", flags).GetValue(service)).Clone();
    }

    static IEnumerator Verify()
    {
        while (PlayerContext.Instance == null || PlayerContext.Instance.CurrentActor == null || !WorldSessionState.IsHideout) yield return null;
        Transform player = PlayerContext.Instance.CurrentActor.transform;
        float ready = Time.unscaledTime + 1;
        while (Time.unscaledTime < ready) yield return null;
        var definitions = AssetDatabase.FindAssets("t:BaseItemData")
            .Select(g => AssetDatabase.LoadAssetAtPath<BaseItemData>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(d => d != null && WeaponContentPolicy.IsAllowedItemData(d)).ToArray();
        var weapon = definitions.OfType<WeaponItemData>().First();
        var cases = Enum.GetValues(typeof(ItemGrade)).Cast<ItemGrade>().Select(g => (data: (BaseItemData)weapon, grade: g)).ToList();
        cases.Add((definitions.OfType<GearItemData>().First(), ItemGrade.Legendary));
        cases.Add((definitions.OfType<FlaskItemData>().First(), ItemGrade.Artifact));
        cases.Add((definitions.OfType<BagItemData>().First(), ItemGrade.Mythic));
        cases.Add((definitions.OfType<MapItemData>().First(), ItemGrade.Common));
        var catalog = Resources.Load<ItemDropSfxCatalog>(ItemDropSfxCatalog.ResourcePath);
        Check(catalog != null, "Production drop sound catalog loaded");

        foreach (var entry in cases)
        {
            ready = Time.unscaledTime + .1f;
            while (Time.unscaledTime < ready) yield return null;
            var item = new ItemData(entry.data, 1, ItemGrade.Common); item.grade = entry.grade;
            string label = entry.data.GetType().Name + "/" + entry.grade;
            float[] before = SoundTimes();
            var pickup = WorldItemDropFactory.CreateWorldPickup(item, player.position + Vector3.forward * 5,
                PlayerAccountInventoryService.SharedInventory, player, null);
            var motion = pickup.GetComponent<WorldItemDropMotion>();
            int landedEvents = 0;
            motion.Landed += () => landedEvents++;
            Check(pickup.GradeEffect == null && !motion.IsLanded && !pickup.CanPickup, label + " hidden during flight");
            pickup.gameObject.SetActive(false); pickup.gameObject.SetActive(true);
            Check(pickup.GradeEffect == null, label + " airborne re-enable stays hidden");
            bool earlyEffect = false, earlySound = false;
            while (!motion.IsLanded)
            {
                earlyEffect |= pickup.GradeEffect != null;
                earlySound |= !before.SequenceEqual(SoundTimes());
                yield return null;
            }
            Check(!earlyEffect && !earlySound, label + " no early VFX or sound");
            Check(landedEvents == 1 && pickup.CanPickup && pickup.GradeEffect != null && pickup.GradeEffect.activeInHierarchy,
                label + " landing reveals effect and pickup together");
            float[] after = SoundTimes();
            int kind = (int)ItemDropSfxService.Classify(entry.data);
            Check(catalog.GetDropClips((ItemDropSfxKind)kind).Length == 0 || after[kind] > before[kind], label + " type sound on landing");
            int reveal = 8 + (int)entry.grade % 8;
            Check(catalog.GetRevealClip(entry.grade) == null || after[reveal] > before[reveal], label + " grade sound on landing");
            var effect = pickup.GradeEffect;
            pickup.gameObject.SetActive(false); pickup.gameObject.SetActive(true);
            Check(pickup.GradeEffect == effect && effect.activeSelf && after.SequenceEqual(SoundTimes()), label + " re-enable does not replay sound");
            effect.SetActive(false); yield return null; yield return null;
            Check(effect.activeSelf, label + " disabled effect repaired after landing");
            Object.Destroy(effect); yield return null; yield return null;
            Check(pickup.GradeEffect != null && pickup.GradeEffect.activeSelf, label + " removed effect repaired after landing");
            pickup.Initialize(item, PlayerAccountInventoryService.SharedInventory, player);
            Check(pickup.GradeEffect == null && !motion.IsLanded, label + " reinitialize hides previous effect");
            ready = Time.unscaledTime + .1f;
            while (Time.unscaledTime < ready && !motion.IsLanded) yield return null;
            motion.SettleImmediately();
            Check(pickup.GradeEffect != null && landedEvents == 2, label + " immediate settle reveals once");
            after = SoundTimes(); motion.SettleImmediately();
            Check(landedEvents == 2 && after.SequenceEqual(SoundTimes()), label + " repeated settle does not replay");
            effect = pickup.GradeEffect;
            pickup.Initialize(null, PlayerAccountInventoryService.SharedInventory, player, null);
            Check(pickup.GradeEffect == null && !effect.activeSelf, label + " invalid reinitialize clears effect");
            Object.Destroy(pickup.gameObject); yield return null;
        }
    }
}
