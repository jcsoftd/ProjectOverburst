using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

// Local-only actual actor/body palette review for GOAL VFX-01.
public static class BloodPaletteReviewRunner
{
    private static readonly Stack<IEnumerator> Work = new Stack<IEnumerator>();
    private static readonly List<object> Rows = new List<object>();
    private static string output;
    private static int lastFrame;
    private static double deadline;
    private static bool previousBackground;
    public static string Result { get; private set; } = "IDLE";

    public static void Start(string label, string themeFilter = null, bool bloodlessOnly = false)
    {
        if (!EditorApplication.isPlaying || Work.Count != 0)
            throw new InvalidOperationException("Requires idle Play Mode");
        output = Path.GetFullPath("../개인파일/코덱스산출/CombatVfx/20260924_GoalVFX01/" + label);
        Directory.CreateDirectory(output);
        Rows.Clear();
        previousBackground = Application.runInBackground;
        Application.runInBackground = true;
        Result = "RUNNING";
        deadline = EditorApplication.timeSinceStartup + 300;
        lastFrame = -1;
        Work.Push(Run(themeFilter, bloodlessOnly));
        EditorApplication.update += Tick;
    }

    private static IEnumerator Run(string themeFilter, bool bloodlessOnly)
    {
        var ui = EnemyThemeTrialHarness.Current;
        var player = PlayerInputFacade.Current;
        var blood = Object.FindFirstObjectByType<BloodHitVfxService>();
        var ground = blood ? blood.GetComponent<BloodGroundDecalService>() : null;
        if (!ui || !player || !blood || !ground)
            throw new InvalidOperationException("Missing UI, player, or blood service");
        float sceneDeadline = Time.time + 15f;
        while ((PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching
                || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
               && Time.time < sceneDeadline)
            yield return null;
        if (PersistentSceneFlow.Instance == null
            || PersistentSceneFlow.Instance.CurrentSubSceneName != PersistentSceneFlow.HideoutSceneName)
            throw new InvalidOperationException("Hideout did not finish loading");
        bool entered = !ui.InArena;
        if (entered) ui.ToggleArena();
        if (!ui.InArena)
            throw new InvalidOperationException("Theme arena did not open");
        yield return Wait(.7f);
        var suppressed = new List<GameObject>();
        foreach (var child in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (child.parent == null && child.name.StartsWith("PF_VFX_WorldPickupGrade_"))
            {
                suppressed.Add(child.gameObject);
                child.gameObject.SetActive(false);
            }
        if (!EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out var spawns))
            throw new InvalidOperationException("Spawn service unavailable");
        foreach (var table in ui.tables) spawns.RegisterAdditionalCatalog(table.Catalog, out _);

        var cameraObject = new GameObject("Blood palette review camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.CopyFrom(Camera.main);
        camera.enabled = false;
        var texture = RenderTexture.GetTemporary(960, 640, 24);
        var pixels = new Texture2D(960, 640, TextureFormat.RGB24, false);
        camera.targetTexture = texture;
        EnemyActor actor = null;
        int sequence = 1;
        try
        {
            foreach (var table in ui.tables.Where(t => themeFilter == null || t.ThemeId == themeFilter))
            {
                foreach (var definition in table.Entries.Select(x => x.definition).Distinct())
                {
                    if (bloodlessOnly)
                    {
                        var prefabTarget = definition.ActorPrefab.GetComponent<BloodHitTarget>();
                        if (!prefabTarget || !prefabTarget.Profile || !prefabTarget.Profile.suppressBlood)
                            continue;
                    }
                    var request = new EnemySpawnRequest(definition,
                        player.transform.position + Vector3.forward * 3f,
                        Quaternion.identity, player.transform, null, player.transform, null, 1, 1, 77);
                    if (!spawns.TrySpawn(request, out actor))
                        throw new InvalidOperationException("Spawn failed: " + definition.EnemyId);
                    actor.AI.enabled = false;
                    actor.Movement.StopMovement();
                    var target = actor.GetComponent<BloodHitTarget>();
                    if (!target || !target.Profile)
                        throw new InvalidOperationException("Blood profile missing: " + definition.EnemyId);
                    yield return Wait(.25f);
                    var combatTarget = actor.GetComponent<CombatTarget>();
                    Vector3 contact = CombatTargetVfxPlacement.ResolveContact(combatTarget,
                        actor.transform.position + Vector3.up, Vector3.forward, out float size);
                    camera.transform.position = contact + new Vector3(3f, 3f, -5f);
                    camera.transform.LookAt(contact);
                    camera.orthographic = true;
                    camera.orthographicSize = 2.7f;
                    string stem = table.ThemeId + "_" + definition.EnemyId;
                    Capture(camera, texture, pixels, stem + "_body.png");

                    var hit = new CombatHitFeedbackRequest(actor.Health, sequence++, null, false,
                        WeaponElement.None, contact, false, worldDirection: Vector3.forward,
                        target: actor.Health, impactShape: CombatImpactShape.Sweep,
                        impactDirection: Vector3.right);
                    int before = blood.PlayedCount;
                    int beforeRequested = blood.RequestedCount;
                    int beforeGround = ground.RequestedCount;
                    CombatHitFeedbackService.Request(hit);
                    var feel = Object.FindFirstObjectByType<CombatImpactFeel>();
                    var slotsField = typeof(CombatImpactFeel).GetField("slots",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    var feelSlots = feel != null && slotsField != null
                        ? slotsField.GetValue(feel) as CombatImpactFeel.Slot[] : null;
                    if (feelSlots == null)
                        throw new InvalidOperationException("Contact Feel missing: " + definition.EnemyId);
                    var expectedSurface = target.Profile.suppressBlood
                        ? CombatImpactSurface.Shell : actor.GetComponent<EnemyDeathPresentation>().Surface;
                    if (!feelSlots.Any(s => s.surface == expectedSurface
                                            && s.availableAt > Time.unscaledTime))
                        throw new InvalidOperationException("Wrong contact surface: " + definition.EnemyId);
                    if (target.Profile.suppressBlood && feelSlots.Any(s => s.surface == CombatImpactSurface.Flesh
                                                                   && s.availableAt > Time.unscaledTime))
                        throw new InvalidOperationException("Blood-colored Flesh contact: " + definition.EnemyId);
                    int peak = 0;
                    for (int frame = 0; frame < 9; frame++)
                    {
                        yield return null;
                        peak = Mathf.Max(peak, blood.GetComponentsInChildren<VisualEffect>()
                            .Sum(v => Mathf.Max(0, v.aliveParticleCount)));
                        Capture(camera, texture, pixels, stem + "_" + frame.ToString("00") + ".png");
                        yield return Wait(.025f);
                    }
                    if (target.Profile.suppressBlood)
                    {
                        if (blood.RequestedCount != beforeRequested || blood.PlayedCount != before
                            || ground.RequestedCount != beforeGround)
                            throw new InvalidOperationException("Bloodless actor sprayed or marked ground: "
                                + definition.EnemyId);
                    }
                    else if (blood.RequestedCount != beforeRequested + 1
                             || blood.PlayedCount != before + 1 || peak == 0
                             || ground.RequestedCount != beforeGround + 1)
                        throw new InvalidOperationException("No spray or ground request: "
                            + definition.EnemyId);
                    Rows.Add(new { theme = table.ThemeId, species = definition.EnemyId,
                        profile = target.Profile.name, main = ColorUtility.ToHtmlStringRGB(target.Profile.mainColor),
                        secondary = ColorUtility.ToHtmlStringRGB(target.Profile.secondaryColor),
                        weight = definition.MovementProfile.HitWeightProfile.Weight.ToString(),
                        size, peak, suppressBlood = target.Profile.suppressBlood,
                        contactSurface = expectedSurface.ToString(),
                        sprayRequests = blood.RequestedCount - beforeRequested,
                        groundRequests = ground.RequestedCount - beforeGround });
                    actor.RequestPoolRelease();
                    actor = null;
                    yield return Wait(2.6f);
                }
            }
            File.WriteAllText(Path.Combine(output, "palette-review.json"),
                JsonConvert.SerializeObject(Rows, Formatting.Indented));
            Result = "PASS captured " + Rows.Count + " actors";
        }
        finally
        {
            if (actor != null && actor.IsLeased) actor.RequestPoolRelease();
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(texture);
            Object.Destroy(pixels);
            Object.Destroy(cameraObject);
            foreach (var effect in suppressed) if (effect) effect.SetActive(true);
            if (entered && ui.InArena) ui.ToggleArena();
        }
    }

    private static IEnumerator Wait(float seconds)
    {
        float end = Time.time + seconds;
        while (Time.time < end) yield return null;
    }

    private static void Capture(Camera camera, RenderTexture texture, Texture2D pixels, string name)
    {
        var previous = RenderTexture.active;
        try
        {
            camera.Render();
            RenderTexture.active = texture;
            pixels.ReadPixels(new Rect(0, 0, 960, 640), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(Path.Combine(output, name), pixels.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; }
    }

    private static void Tick()
    {
        try
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup >= deadline)
                throw new InvalidOperationException("Play or time limit");
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            while (Work.Count > 0)
            {
                IEnumerator current = Work.Peek();
                if (!current.MoveNext())
                {
                    (Work.Pop() as IDisposable)?.Dispose();
                    continue;
                }
                if (current.Current is IEnumerator nested)
                {
                    Work.Push(nested);
                    continue;
                }
                return;
            }
            Finish(Result);
        }
        catch (Exception error) { Finish("FAIL " + error); }
    }

    private static void Finish(string status)
    {
        EditorApplication.update -= Tick;
        while (Work.Count > 0) (Work.Pop() as IDisposable)?.Dispose();
        Application.runInBackground = previousBackground;
        Result = status;
        File.WriteAllText(Path.Combine(output, "runner-result.txt"), Result);
        Debug.Log("[BloodPaletteReviewRunner] " + Result);
    }
}
