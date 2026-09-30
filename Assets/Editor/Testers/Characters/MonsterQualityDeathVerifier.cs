using System;
using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class MonsterQualityDeathVerifier
{
    private const string Key = "MonsterQualityDeathVerifier";
    private static IEnumerator work;
    private static int lastFrame;
    private static double deadline;
    private static bool stopping;
    private static readonly System.Collections.Generic.List<string> errors = new System.Collections.Generic.List<string>();
    public static string LastResult => SessionState.GetString(Key + ".result", "NOT_RUN");
    static MonsterQualityDeathVerifier() => EditorApplication.playModeStateChanged += Changed;

    [MenuItem("OVERBURST/Enemies/Themes/Validate Monster Quality Death")]
    public static void Run()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Already playing");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        Require(scene.name == PersistentSceneFlow.PersistentSceneName && !scene.isDirty, "Open saved PersistentScene");
        SessionState.SetBool(Key, true); SessionState.SetString(Key + ".result", "RUNNING");
        EditorApplication.EnterPlaymode();
    }

    private static void Changed(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            stopping = false; errors.Clear(); work = Verify(); lastFrame = -1;
            deadline = EditorApplication.timeSinceStartup + 120;
            Application.logMessageReceived += OnLog; EditorApplication.update += Tick;
        }
        if (change == PlayModeStateChange.ExitingPlayMode)
        {
            stopping = true; EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog;
            (work as IDisposable)?.Dispose(); work = null;
            if (LastResult == "RUNNING") SessionState.SetString(Key + ".result", "FAIL interrupted");
        }
        if (change == PlayModeStateChange.EnteredEditMode)
        { SessionState.SetBool(Key, false); Debug.Log("[MonsterQualityDeath] " + LastResult); }
    }

    private static void OnLog(string message, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    private static void Tick()
    {
        if (stopping) return;
        EditorApplication.QueuePlayerLoopUpdate();
        if (!EditorApplication.isPlaying || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try
        {
            Require(EditorApplication.timeSinceStartup < deadline, "Timeout");
            if (work.MoveNext()) return;
            Require(errors.Count == 0, string.Join(" | ", errors));
            Finish("PASS corpse tone <0.15s, directional displacement, pool reset, lethal Feel preemption; errors=0");
        }
        catch (Exception ex) { Finish("FAIL " + ex); }
    }
    private static void Finish(string result)
    { stopping = true; SessionState.SetString(Key + ".result", result); EditorApplication.update -= Tick; EditorApplication.ExitPlaymode(); }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static IEnumerator Verify()
    {
        EnemyActor current = null;
        EnemyThemeDebugUI ui = null;
        try
        {
            float end = Time.realtimeSinceStartup + 45f;
            while (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
                || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
            { Require(Time.realtimeSinceStartup < end, "Hideout did not load"); yield return null; }
            var player = PlayerInputFacade.Current; Require(player != null, "Player missing");
            ui = UnityEngine.Object.FindFirstObjectByType<EnemyThemeDebugUI>(FindObjectsInactive.Include);
            Require(ui != null, "Theme UI missing"); ui.gameObject.SetActive(true); ui.ToggleArena();
            Require(ui.InArena, "Arena entry failed");
            var table = ui.tables.First(t => t.ThemeId == "PrimalHunt");
            Require(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out var spawn), "Spawn service");
            Require(spawn.RegisterAdditionalCatalog(table.Catalog, out string error), "Catalog " + error);
            foreach (string id in new[] { "PrimalHunt_Caniathrox", "PrimalHunt_CrustaspikanLarvae",
                "PrimalHunt_Occisodonte" })
            {
                var definition = table.Entries.First(e => e.definition.EnemyId == id).definition;
                var location = player.transform.position + Vector3.forward * 7f;
                Require(Physics.Raycast(location + Vector3.up * 4f, Vector3.down, out var floor, 9f,
                    LayerMask.GetMask("Default", "Environment", "Ground"), QueryTriggerInteraction.Ignore), "Arena ground " + id);
                location = floor.point + Vector3.up * .035f; // Match EnemyThemeEncounter.TryPosition.
                var request = new EnemySpawnRequest(definition, location,
                    Quaternion.identity, player.transform, null, player.transform, null, 1f, 1f, 77);
                Require(spawn.TrySpawn(request, out current), "Spawn " + id);
                current.AI.enabled = false; current.Movement.StopMovement();
                var renderer = current.VisualRoot.GetComponentsInChildren<Renderer>(true)
                    .First(r => r.sharedMaterials.Length > 0 && r.sharedMaterials[0] != null
                        && r.sharedMaterials[0].HasProperty("_BaseColor"));
                float before = ReadColor(renderer).grayscale;
                current.Health.TakeDamage(new DamageInfo(current.Health.MaxHp + 10000f, current.transform.position,
                    player.gameObject, Vector3.forward, 5f, true));
                Require(current.Health.IsDead, "Lethal hit failed " + id);
                float until = Time.realtimeSinceStartup + .15f;
                while (Time.realtimeSinceStartup < until) yield return null;
                float after = ReadColor(renderer).grayscale;
                Require(after < before * .74f, $"Corpse not visibly darker: {id} {before:F2}->{after:F2}");
                float offset = current.GetComponent<EnemyDeathPresentation>().VisualOffset.magnitude;
                Require(offset > (id.EndsWith("Occisodonte") ? .025f : .18f), $"Death slide missing: {id} {offset:F2}");
                uint priorVersion = current.LeaseVersion;
                spawn.Release(current); Require(!current.IsLeased, "Pool release " + id);
                Require(spawn.TrySpawn(request, out current), "Respawn " + id);
                Require(current.LeaseVersion > priorVersion, "Lease version " + id);
                renderer = current.VisualRoot.GetComponentsInChildren<Renderer>(true)
                    .First(r => r.sharedMaterials.Length > 0 && r.sharedMaterials[0] != null
                        && r.sharedMaterials[0].HasProperty("_BaseColor"));
                Require(ReadColor(renderer).grayscale > before * .85f, "Live color not restored " + id);
                Require(current.GetComponent<EnemyDeathPresentation>().VisualOffset.magnitude < .01f, "Visual root not reset " + id);
                spawn.Release(current); current = null;
            }
            var point = player.transform.position + Vector3.forward * 3f;
            for (int i = 0; i < 40; i++)
                CombatImpactFeel.Play(CombatImpactSurface.Flesh, CombatImpactShape.Sweep, point, Vector3.forward);
            var feel = UnityEngine.Object.FindFirstObjectByType<CombatImpactFeel>();
            Require(feel != null, "Feel instance missing");
            int preempted = feel.PreemptedCount;
            Require(CombatImpactFeel.Play(CombatImpactSurface.Flesh, CombatImpactShape.Sweep,
                point, Vector3.forward, lethal: true), "Lethal contact dropped");
            Require(feel.PreemptedCount == preempted + 1, "Lethal did not preempt saturated slot");
            Debug.Log("[MonsterQualityDeath] PASS 3 species / corpse dim, displacement and reset / saturated Feel lethal preempted");
        }
        finally
        {
            if (current != null && current.IsLeased) current.RequestPoolRelease();
            if (ui != null && ui.InArena) ui.ToggleArena();
        }
    }

    private static Color ReadColor(Renderer renderer)
    {
        var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, 0);
        int id = Shader.PropertyToID("_BaseColor");
        return block.HasColor(id) ? block.GetColor(id) : renderer.sharedMaterials[0].GetColor(id);
    }
}
