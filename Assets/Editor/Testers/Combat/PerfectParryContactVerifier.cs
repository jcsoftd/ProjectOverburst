using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static partial class PerfectParryContactVerifier
{
    private const string ProbeKey = "Overburst.PerfectParryContactVerifier.Probe";
    static PerfectParryContactVerifier() { if (!string.IsNullOrEmpty(SessionState.GetString(ProbeKey, ""))) EditorApplication.update += InstallProbe; }
    public static void Assets(string directory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Native asset checks require idle EditMode.");
        Directory.CreateDirectory(directory); var rows = new List<object>();
        void Check(bool pass, string label) { rows.Add(new { label, pass }); if (!pass) throw new InvalidOperationException(label); }
        var p = AssetDatabase.LoadAssetAtPath<PerfectParryContactProfile>(PerfectParryContactBuilder.ProfilePath);
        Check(p != null && p.IsReady, "Complete enabled presentation profile loads natively");
        Check(p.readableSeconds >= .20f && p.totalSeconds >= .55f, "Readable main shape and overall tail are independently authored");
        foreach (var prefab in new[] { p.mainPrefab, p.additionalPrefab })
        {
            Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab) == 0, prefab.name + " has no missing root scripts");
            foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                var r = ps.GetComponent<ParticleSystemRenderer>();
                Check(!ps.main.loop && !ps.main.playOnAwake && !ps.emission.enabled && ps.main.useUnscaledTime, ps.name + " has one owned clock");
                Check(r.sharedMaterial != null && r.sharedMaterial.mainTexture != null && r.sharedMaterial.shader.isSupported, ps.name + " material texture and shader load");
            }
        }
        Check(p.upswingAfterimageMaterial != null && p.upswingAfterimageMaterial.shader.isSupported, "Weapon poses reuse the supported dash afterimage material");
        var main = p.mainPrefab.GetComponent<PerfectParryContactVfx>();
        Check(main.flash != null && main.stroke != null && main.glow != null && main.sparks != null, "Main has four authored layers");
        Check(p.additionalPrefab.GetComponent<PerfectParryContactVfx>().additional, "Secondary prefab uses the reduced contact path");
        foreach (var clip in new[] { p.impact, p.ring, p.low })
        {
            var pcm = new float[clip.samples * clip.channels];
            Check(clip.GetData(pcm, 0) && pcm.Any(v => Mathf.Abs(v) > .005f), clip.name + " contains actual PCM audio");
        }
        var clone = UnityEngine.Object.Instantiate(p);
        try
        {
            clone.enhancedPresentation = false; Check(!clone.IsReady, "Disabled profile selects the complete legacy path");
            clone.enhancedPresentation = true; clone.impact = null; Check(!clone.IsReady, "Missing required sound rejects partial enhancement");
        }
        finally { UnityEngine.Object.DestroyImmediate(clone); }
        Check(PlayerParryController.ResolveGrade(.79999f) == ParryGrade.Normal && PlayerParryController.ResolveGrade(.8f) == ParryGrade.Perfect, "Grade boundary remains 80 percent");
        File.WriteAllText(Path.Combine(directory, "assets.json"), JsonConvert.SerializeObject(new { status = "PASS", rows }, Formatting.Indented));
    }
    public static void ObserveOwnedRun(string directory)
    {
        SessionState.SetString(ProbeKey, Path.GetFullPath(directory));
        SessionState.SetFloat(ProbeKey + ".Deadline", (float)EditorApplication.timeSinceStartup + 300f);
        EditorApplication.update -= InstallProbe; EditorApplication.update += InstallProbe;
    }
    private static void InstallProbe()
    {
        string output = SessionState.GetString(ProbeKey, "");
        if (string.IsNullOrEmpty(output) || EditorApplication.timeSinceStartup > SessionState.GetFloat(ProbeKey + ".Deadline", 0))
        { EditorApplication.update -= InstallProbe; SessionState.EraseString(ProbeKey); SessionState.EraseFloat(ProbeKey + ".Deadline"); return; }
        string active = IsolatedSavePlayGuard.ActiveDirectory;
        if (!EditorApplication.isPlaying || string.IsNullOrEmpty(active) || !Path.GetFullPath(active).StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
        var actor = PlayerContext.Instance?.CurrentActor; if (actor == null) return;
        var probe = actor.gameObject.AddComponent<PerfectParryContactProbe>(); probe.output = output;
        EditorApplication.update -= InstallProbe; SessionState.EraseString(ProbeKey); SessionState.EraseFloat(ProbeKey + ".Deadline");
    }
}

[DefaultExecutionOrder(500)]
public sealed class PerfectParryContactProbe : MonoBehaviour
{
    public string output;
    private readonly List<object> samples = new List<object>();
    private readonly ParticleSystem.Particle[] particles = new ParticleSystem.Particle[8];
    private PerfectParryContactVfx cue;
    private int sequence, lastSequence;
    private void LateUpdate()
    {
        if (cue == null || !cue.IsPlaybackAlive)
        {
            if (sequence != lastSequence) { Write(); lastSequence = sequence; }
            cue = UnityEngine.Object.FindObjectsByType<PerfectParryContactVfx>(FindObjectsSortMode.None).FirstOrDefault(c => !c.additional && c.IsPlaybackAlive);
            if (cue != null) sequence++; else return;
        }
        var presenter = GetComponent<PerfectParryContactPresenter>();
        var eq = GetComponent<PlayerEquipment>();
        bool segmentValid = presenter.TryGetBladeSegment(out Vector3 bladeA, out Vector3 bladeB);
        var view = Camera.main;
        int count = cue.stroke.GetParticles(particles);
        Vector3 strokePosition = count > 0 ? particles[0].position : Vector3.zero;
        Color current = count > 0 ? particles[0].GetCurrentColor(cue.stroke) : Color.clear;
        samples.Add(new { sequence, frame = Time.frameCount, cue.Age, cue.SparksEmitted, cue.IsPlaybackAlive,
            particles = count, sparks = cue.sparks.particleCount, currentAlpha = current.a,
            cuePosition = Point(cue.transform.position), strokePosition = Point(strokePosition), bladeRoot = Point(bladeA), bladeTip = Point(bladeB), segmentValid,
            cueScreen = Point(view != null ? view.WorldToScreenPoint(cue.transform.position) : Vector3.zero),
            presenter.MainCount, presenter.AdditionalCount, ParryFeedbackService.LastPerfectLayerCount });
        if (samples.Count % 15 == 0) Write();
    }
    private static float[] Point(Vector3 v) => new[] { v.x, v.y, v.z };
    private void Write() { if (!string.IsNullOrEmpty(output)) File.WriteAllText(Path.Combine(output, "contact-observations.json"), JsonConvert.SerializeObject(samples)); }
    private void OnDestroy() { Write(); }
}
