using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>기존 격리 입력 검증의 8방향 이동/정지를 별도 카메라로 촬영한다.</summary>
[InitializeOnLoad]
public static class CombatMoveSpeedTrialRecorder
{
    const string Key = "Overburst.CombatMoveSpeedTrialRecorder";
    const string QueueKey = Key + ".Queue";
    static CombatMoveSpeedVideoCapture capture;
    static double idleSince;
    static CombatMoveSpeedTrialRecorder()
    {
        if (!string.IsNullOrEmpty(SessionState.GetString(Key, ""))) Subscribe();
        if (!string.IsNullOrEmpty(SessionState.GetString(QueueKey, ""))) EditorApplication.update += StartWhenIdle;
    }
    public static string Queue(string directory)
    {
        directory = IsolatedSavePlayGuard.ValidateDirectory(directory);
        string prior = SessionState.GetString(QueueKey, "");
        if (!string.IsNullOrEmpty(prior) && prior != directory) throw new InvalidOperationException("다른 영상 촬영 대기 중");
        Directory.CreateDirectory(directory);
        SessionState.SetString(QueueKey, directory);
        SessionState.SetFloat(QueueKey + ".Deadline", (float)EditorApplication.timeSinceStartup + 600);
        idleSince = 0; EditorApplication.update -= StartWhenIdle; EditorApplication.update += StartWhenIdle;
        return "QUEUED";
    }
    static void StartWhenIdle()
    {
        string directory = SessionState.GetString(QueueKey, "");
        bool expired = EditorApplication.timeSinceStartup > SessionState.GetFloat(QueueKey + ".Deadline", 0);
        bool busy = EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || IsolatedSavePlayGuard.RequiresAccountChoice
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.PlayerEvadeVerifier.ReturnToRealAccount", ""));
        if (busy && !expired) { idleSince = 0; return; }
        if (!expired && idleSince == 0) { idleSince = EditorApplication.timeSinceStartup; return; }
        if (!expired && EditorApplication.timeSinceStartup - idleSince < .5) return;
        EditorApplication.update -= StartWhenIdle; SessionState.EraseString(QueueKey); SessionState.EraseFloat(QueueKey + ".Deadline");
        try
        {
            if (expired) throw new TimeoutException("촬영 대기 만료; Play 변경 없음");
            string result = Run(directory);
            File.WriteAllText(Path.Combine(directory, "CaptureQueue.json"), JsonConvert.SerializeObject(new { status = "STARTED", result }));
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(directory, "CaptureQueue.json"), JsonConvert.SerializeObject(new { status = "FAIL", error = e.ToString() })); }
    }
    public static string Run(string directory)
    {
        string check = CombatMoveSpeedTrialBuilder.Check();
        if (!string.IsNullOrEmpty(SessionState.GetString(Key, ""))) throw new InvalidOperationException("영상 촬영 진행 중");
        directory = IsolatedSavePlayGuard.ValidateDirectory(directory);
        Directory.CreateDirectory(directory);
        SessionState.SetString(Key, directory);
        SessionState.SetFloat(Key + ".Deadline", (float)EditorApplication.timeSinceStartup + 600);
        Subscribe();
        try { PlayerEvadeVerifier.StartSwordStopIsolated(directory, true, false); }
        catch { Clear(); throw; }
        return check + " 8방향 영상 촬영 시작.";
    }
    static void Subscribe()
    {
        EditorApplication.update -= Watch; EditorApplication.update += Watch;
        EditorApplication.playModeStateChanged -= State; EditorApplication.playModeStateChanged += State;
        AssemblyReloadEvents.beforeAssemblyReload -= Reload; AssemblyReloadEvents.beforeAssemblyReload += Reload;
    }
    static void Watch()
    {
        string output = SessionState.GetString(Key, "");
        if (string.IsNullOrEmpty(output)) { Clear(); return; }
        string active = IsolatedSavePlayGuard.ActiveDirectory;
        bool ownPlay = EditorApplication.isPlaying && !string.IsNullOrEmpty(active)
            && Path.GetFullPath(active).StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + ".Deadline", 0))
        {
            File.WriteAllText(Path.Combine(output, "CaptureFailure.json"), "{\"status\":\"FAIL\",\"reason\":\"capture deadline\"}");
            Cleanup(); if (ownPlay) EditorApplication.ExitPlaymode(); return;
        }
        if (!ownPlay || capture != null || !Overburst.Persistence.AccountBootstrap.Ready) return;
        var actor = PlayerContext.GetOrCreate().CurrentActor;
        if (actor == null || Camera.main == null) return;
        var host = new GameObject("Owned combat speed video capture");
        capture = host.AddComponent<CombatMoveSpeedVideoCapture>();
        capture.Begin(output, actor);
    }
    static void State(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode) Cleanup();
    }
    static void Reload()
    {
        // Standard Play entry reloads before the runtime camera exists.
        // Keep its pending output so the next domain can resume Watch.
        if (capture != null) { Cleanup(); return; }
        EditorApplication.update -= Watch; EditorApplication.playModeStateChanged -= State;
        AssemblyReloadEvents.beforeAssemblyReload -= Reload;
    }
    static void Cleanup()
    {
        try { if (capture != null) { capture.Complete(); UnityEngine.Object.DestroyImmediate(capture.gameObject); } }
        finally { capture = null; Clear(); }
    }
    static void Clear()
    {
        EditorApplication.update -= Watch; EditorApplication.playModeStateChanged -= State;
        AssemblyReloadEvents.beforeAssemblyReload -= Reload;
        SessionState.EraseString(Key); SessionState.EraseFloat(Key + ".Deadline");
    }
}

public sealed class CombatMoveSpeedVideoCapture : MonoBehaviour
{
    const int Width = 1280, Height = 720, Fps = 60;
    string output;
    PlayerActorRuntime actor;
    Camera camera;
    RenderTexture target;
    Texture2D pixels;
    MediaEncoder encoder;
    Coroutine pump;
    int priorCaptureRate, count;
    bool completed;
    readonly List<object> frames = new List<object>();
    readonly HashSet<int> sectors = new HashSet<int>();
    readonly List<string> errors = new List<string>();
    readonly HashSet<string> thumbnails = new HashSet<string>();
    public void Begin(string directory, PlayerActorRuntime owner)
    {
        output = directory; actor = owner; priorCaptureRate = Time.captureFramerate;
        try
        {
            camera = gameObject.AddComponent<Camera>(); camera.CopyFrom(Camera.main); camera.enabled = false;
            camera.orthographic = true; camera.orthographicSize = 3.1f;
            int ui = LayerMask.NameToLayer("UI"); if (ui >= 0) camera.cullingMask &= ~(1 << ui);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create(); pixels = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            encoder = new MediaEncoder(Path.Combine(output, "CombatMoveSpeed_WithAnimationSpeed_Raw.mp4"),
                new VideoTrackEncoderAttributes {frameRate = new MediaRational(Fps), width = Width, height = Height,
                    includeAlpha = false, targetBitRate = 12000000, bitRateMode = UnityEditor.VideoBitrateMode.High});
            Time.captureFramerate = Fps;
            pump = StartCoroutine(Record());
        }
        catch (Exception e) { errors.Add(e.ToString()); Complete(); throw; }
    }
    IEnumerator Record()
    {
        var boundary = new WaitForEndOfFrame();
        while (!completed)
        {
            yield return boundary;
            try { Add(); }
            catch (Exception e)
            {
                errors.Add(e.ToString()); Complete();
                if (EditorApplication.isPlaying && IsolatedSavePlayGuard.ActiveDirectory.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    EditorApplication.ExitPlaymode();
                yield break;
            }
        }
    }
    void Add()
    {
        if (actor == null || encoder == null) return;
        var probe = UnityEngine.Object.FindFirstObjectByType<SwordFacingPoseProbe>();
        if (probe == null) return;
        string phase = probe.phase;
        if (!(phase.StartsWith("move_") || phase.StartsWith("stop_")) || !int.TryParse(phase.Substring(5), out int sector)
            || sector < 0 || sector >= 8) return;
        var rotation = Quaternion.Euler(48, 45, 0);
        Vector3 focus = actor.transform.position + Vector3.up * .9f;
        camera.transform.SetPositionAndRotation(focus + rotation * Vector3.back * 12, rotation);
        var prior = RenderTexture.active;
        try
        {
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest {destination = target});
            RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false); pixels.Apply(false, false);
            if (!encoder.AddFrame(pixels)) throw new InvalidOperationException("영상 프레임 저장 실패");
            float speed = actor.Movement.Locomotion.HorizontalVelocity.magnitude;
            frames.Add(new {frame = count, phase, sector, gameFrame = Time.frameCount, time = Time.time, speed,
                actor = actor.transform.position.ToString("F4")});
            sectors.Add(sector); count++;
            if (phase.StartsWith("move_") && speed > 4.9f && thumbnails.Add(phase))
                File.WriteAllBytes(Path.Combine(output, phase + ".png"), pixels.EncodeToPNG());
            if (count % 60 == 0) File.WriteAllText(Path.Combine(output, "VideoProgress.json"),
                JsonConvert.SerializeObject(new {status = "RECORDING", phase, count, sectors = sectors.OrderBy(x => x).ToArray()}));
        }
        finally { RenderTexture.active = prior; }
    }
    public void Complete()
    {
        if (completed) return; completed = true;
        try { encoder?.Dispose(); }
        catch (Exception e) { errors.Add(e.ToString()); }
        finally
        {
            encoder = null; Time.captureFramerate = priorCaptureRate;
            if (target != null) {target.Release(); UnityEngine.Object.DestroyImmediate(target); target = null;}
            if (pixels != null) {UnityEngine.Object.DestroyImmediate(pixels); pixels = null;}
            if (pump != null) {StopCoroutine(pump); pump = null;}
            if (!string.IsNullOrEmpty(output)) File.WriteAllText(Path.Combine(output, "VideoCaptureResult.json"),
                JsonConvert.SerializeObject(new {status = count > 0 && sectors.Count == 8 && errors.Count == 0 ? "PASS" : "FAIL",
                    width = Width, height = Height, fps = Fps, count, duration = count / (float)Fps,
                    sectors = sectors.OrderBy(x => x).ToArray(), errors, frames}, Formatting.Indented));
        }
    }
    void OnDisable() => Complete();
    void OnDestroy() => Complete();
}
