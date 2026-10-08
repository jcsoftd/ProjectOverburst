using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Checks automatic marker following and serialized reload while restoring the user's placement.</summary>
public static class MainTownAuthoringVerifier
{
    static MainTownPlacementAnchor[] markers;
    static Vector3[] positions;
    static Quaternion[] rotations;
    static string output;
    static double readyAt, deadline;

    public static string Start(string resultPath)
    {
        MainTownBuilder.RequireIdle();
        if (markers != null) throw new InvalidOperationException("Authoring verifier already running.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != MainTownBuilder.ScenePath || scene.isDirty) throw new InvalidOperationException("Open the saved MainScene first.");
        var allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        output = Path.GetFullPath(resultPath);
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Use the private Codex artifact directory.");
        markers = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<MainTownPlacementAnchor>(true)).OrderBy(x => x.LocationLabel).ToArray();
        if (markers.Length != 9 || markers.Any(x => x.Placement == null || x.Placement.gameObject.scene != scene))
        { markers = null; throw new InvalidOperationException("Nine linked markers are required."); }
        positions = markers.Select(x => x.transform.position).ToArray();
        rotations = markers.Select(x => x.transform.rotation).ToArray();
        deadline = EditorApplication.timeSinceStartup + 30; readyAt = EditorApplication.timeSinceStartup + 1;
        for (int i = 0; i < markers.Length; i++)
        {
            markers[i].transform.SetPositionAndRotation(positions[i] + new Vector3(.37f, .11f, -.23f), Quaternion.Euler(0, 7, 0) * rotations[i]);
            EditorUtility.SetDirty(markers[i].transform);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorApplication.QueuePlayerLoopUpdate(); EditorApplication.update += Finish;
        return "STARTED_AUTOMATIC_MARKER_FOLLOW_CHECK";
    }

    static void Finish()
    {
        if (EditorApplication.timeSinceStartup < readyAt) { EditorApplication.QueuePlayerLoopUpdate(); return; }
        EditorApplication.update -= Finish;
        var scene = SceneManager.GetActiveScene();
        string status = "PASS", error = null;
        object[] follow = null;
        try
        {
            MainTownBuilder.RequireIdle();
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Authoring verifier deadline.");
            follow = markers.Select(x => (object)new { x.LocationLabel,
                marker = x.transform.position.ToString("F4"), placement = x.Placement.position.ToString("F4"),
                distance = Vector3.Distance(x.transform.position, x.Placement.position),
                angle = Quaternion.Angle(x.transform.rotation, x.Placement.rotation),
                x.enabled, active = x.gameObject.activeInHierarchy }).ToArray();
            if (markers.Any(x => Vector3.Distance(x.transform.position, x.Placement.position) > .001f
                || Quaternion.Angle(x.transform.rotation, x.Placement.rotation) > .01f))
                throw new InvalidOperationException("Automatic EditMode following failed.");
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Perturbed scene save failed.");
            scene = EditorSceneManager.OpenScene(MainTownBuilder.ScenePath, OpenSceneMode.Single);
            markers = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<MainTownPlacementAnchor>(true)).OrderBy(x => x.LocationLabel).ToArray();
            for (int i = 0; i < markers.Length; i++)
                if (Vector3.Distance(markers[i].transform.position, positions[i] + new Vector3(.37f, .11f, -.23f)) > .001f
                    || Vector3.Distance(markers[i].transform.position, markers[i].Placement.position) > .001f)
                    throw new InvalidOperationException("Marker and placement did not survive save/reload.");
            if (scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Transform>(true))
                .Any(x => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(x.gameObject) != 0))
                throw new InvalidOperationException("MainScene has a missing script.");
            if (markers.Any(x => !x.CompareTag("EditorOnly") || x.GetComponentsInChildren<Renderer>(true).Length != 0
                || x.Placement.CompareTag("EditorOnly") || x.Placement.IsChildOf(x.transform.parent)))
                throw new InvalidOperationException("Runtime services must remain outside the EditorOnly hierarchy.");
        }
        catch (Exception e) { status = "FAIL"; error = e.ToString(); }
        finally
        {
            try
            {
                MainTownBuilder.RequireIdle();
                for (int i = 0; i < markers.Length; i++)
                { markers[i].transform.SetPositionAndRotation(positions[i], rotations[i]); markers[i].ApplyPlacement(); }
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Original placement restore failed.");
                var report = new { status, error, markerCount = markers.Length, automaticFollow = status == "PASS", saveReload = status == "PASS", originalPlacementRestored = true, follow,
                    locations = markers.Select(x => new { x.LocationLabel, position = x.transform.position.ToString(), target = x.Placement.name }).ToArray() };
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                File.WriteAllText(output, JsonConvert.SerializeObject(report, Formatting.Indented));
            }
            catch (Exception restoreError)
            {
                File.WriteAllText(output, JsonConvert.SerializeObject(new { status = "FAIL_RESTORE", error, restoreError = restoreError.ToString(),
                    originalPositions = positions.Select(x => x.ToString()).ToArray(), originalRotations = rotations.Select(x => x.ToString()).ToArray() }, Formatting.Indented));
            }
            markers = null; positions = null; rotations = null; output = null;
        }
    }
}
