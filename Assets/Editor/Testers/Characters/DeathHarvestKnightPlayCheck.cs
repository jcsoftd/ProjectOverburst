using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Local Play proof for the Knight's actual spawn, chase, and looping bones.
[InitializeOnLoad]
public static class DeathHarvestKnightPlayCheck
{
    private const string Key = "DeathHarvestKnightPlayCheck";
    private const string Output = @"D:\JC Program\유니티\개인프로젝트\프로젝트 오버버스트\개인파일\코덱스산출\MonsterThemes\20260924_UndeadHorde\revisions\20260924_lich_animation\play_validation.txt";
    private static IEnumerator work;
    private static int lastFrame = -1;
    private static double deadline;
    private static bool stopping;
    private static bool previousBackground;
    private static int previousFrameRate;
    private static EnemyActor actor;
    private static EnemyThemeTrialHarness ui;
    private static string completedDetail;

    public static string LastResult => SessionState.GetString(Key + ".result", "NOT_RUN");

    static DeathHarvestKnightPlayCheck() => EditorApplication.playModeStateChanged += Changed;

    [MenuItem("OVERBURST/Enemies/Themes/Check Death Harvest Knight Play Loop")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Start in Edit Mode.");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != PersistentSceneFlow.PersistentSceneName)
            throw new InvalidOperationException("Requires the already open PersistentScene.");
        SessionState.SetBool(Key, true);
        SessionState.SetString(Key + ".result", "RUNNING");
        EditorApplication.EnterPlaymode();
    }

    private static void Changed(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            completedDetail = null;
            previousBackground = Application.runInBackground;
            previousFrameRate = Application.targetFrameRate;
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            work = Verify();
            stopping = false;
            lastFrame = -1;
            deadline = EditorApplication.timeSinceStartup + 90;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.ExitingPlayMode)
        {
            stopping = true;
            EditorApplication.update -= Tick;
            (work as IDisposable)?.Dispose();
            work = null;
            Application.runInBackground = previousBackground;
            Application.targetFrameRate = previousFrameRate;
            if (LastResult == "RUNNING") SessionState.SetString(Key + ".result", "FAIL interrupted");
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key, false);
            Debug.Log("[DeathHarvestKnightPlay] " + LastResult);
        }
    }

    private static void Tick()
    {
        if (stopping || !EditorApplication.isPlaying || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup >= deadline)
                throw new InvalidOperationException("90s timeout");
            if (work.MoveNext()) return;
            Finish(completedDetail ?? "FAIL result missing");
        }
        catch (Exception error) { Finish("FAIL " + error); }
    }

    private static void Finish(string result)
    {
        stopping = true;
        EditorApplication.update -= Tick;
        try
        {
            if (actor != null && actor.IsLeased) actor.RequestPoolRelease();
            if (ui != null && ui.InArena) ui.ToggleArena();
        }
        catch (Exception error) { result += "\nFAIL cleanup " + error.Message; }
        completedDetail = result;
        Directory.CreateDirectory(Path.GetDirectoryName(Output));
        File.WriteAllText(Output, result);
        SessionState.SetString(Key + ".result", result);
        EditorApplication.ExitPlaymode();
    }

    private static IEnumerator Verify()
    {
        float wait = Time.time + 50f;
        while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
            || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
        {
            if (Time.time > wait) throw new InvalidOperationException("Hideout load timeout");
            yield return null;
        }
        var player = PlayerInputFacade.Current;
        if (player == null) throw new InvalidOperationException("Player missing");
        player.GetComponent<CombatHealth>()?.SetMaxHp(100000, true);
        ui = EnemyThemeTrialHarness.Current;
        if (ui == null) throw new InvalidOperationException("Theme UI missing");
        yield return null;
        if (!ui.InArena) ui.ToggleArena();
        yield return null;
        var table = AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(
            "Assets/ProjectOverburst/Resources/Enemies/Themes/Tables/DeathHarvest.asset");
        if (table == null) throw new InvalidOperationException("DeathHarvest table missing");
        var definition = table.Entries.First(e => e.definition.EnemyId == "DeathHarvest_DeathKnight").definition;
        var service = EnemySpawnService.Current;
        if (service == null && !EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out service))
            throw new InvalidOperationException("Spawn service unavailable");
        string message = "Spawn service missing";
        if (!service.RegisterAdditionalCatalog(table.Catalog, out message))
            throw new InvalidOperationException("Catalog: " + message);
        var origin = player.transform.position;
        var spawn = origin + Vector3.forward * 11f;
        var request = new EnemySpawnRequest(definition, spawn, Quaternion.identity,
            player.transform, null, player.transform, null, 1f, 1f, 77);
        if (!service.TrySpawn(request, out actor)) throw new InvalidOperationException("Knight spawn failed");
        var animator = actor.Animator;
        var leg = animator.transform.Find("root/pelvis/thigh_l");
        if (leg == null) throw new InvalidOperationException("Leg bone missing");
        var clip = definition.AnimationProfile.Walk;
        if (!clip.isLooping) throw new InvalidOperationException("Walk clip is not looping");
        float began = Time.time;
        float firstMove = -1f;
        var late = new List<Quaternion>();
        var trace = new List<string>();
        int lateLocomotionFrames = 0;
        int sampledFrames = 0;
        while (Time.time - began < 8f)
        {
            if (actor == null || !actor.IsLeased) throw new InvalidOperationException("Knight despawned");
            var state = animator.GetCurrentAnimatorStateInfo(0);
            float amount = animator.GetFloat("Locomotion");
            if (sampledFrames++ % 30 == 0)
                trace.Add((Time.time - began).ToString("F2") + "s amount=" + amount.ToString("F2")
                    + " state=" + (state.IsName("Locomotion") ? "Locomotion" : state.shortNameHash.ToString())
                    + " speed=" + animator.speed.ToString("F2")
                    + " distance=" + Vector3.Distance(spawn, actor.transform.position).ToString("F2")
                    + " visible=" + actor.GetComponentInChildren<SkinnedMeshRenderer>(true).isVisible);
            if (amount > .8f && state.IsName("Locomotion"))
            {
                if (firstMove < 0f) firstMove = Time.time;
                if (Time.time - firstMove > clip.length + .2f)
                {
                    late.Add(leg.localRotation);
                    lateLocomotionFrames++;
                }
            }
            yield return null;
        }
        float distance = Vector3.Distance(spawn, actor.transform.position);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Output), "play_trace.txt"), string.Join("\n", trace));
        if (distance < .5f) throw new InvalidOperationException("Knight did not move: " + distance);
        if (lateLocomotionFrames < 15) throw new InvalidOperationException("Too few late locomotion frames: " + lateLocomotionFrames);
        float maxAngle = late.Max(q => Quaternion.Angle(late[0], q));
        if (maxAngle < 10f) throw new InvalidOperationException("Leg froze after first cycle: " + maxAngle);
        var result = "PASS Knight spawned in Play, chase distance=" + distance.ToString("F2")
            + "m, late locomotion frames=" + lateLocomotionFrames
            + ", late leg range=" + maxAngle.ToString("F1") + "deg, clip loop=" + clip.isLooping;
        completedDetail = result;
    }
}
