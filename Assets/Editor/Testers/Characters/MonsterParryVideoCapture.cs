using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Records the owned actual-player test; it never starts Play or changes an account.
public sealed class MonsterParryVideoCapture : IDisposable
{
    const int Width = 1280, Height = 720, Fps = 30;
    Camera captureCamera;
    RenderTexture target;
    Texture2D pixels;
    MediaEncoder encoder;
    Transform player, enemy;
    string directory, error;
    int firstFrame, count;
    string pendingMark;
    public bool IsRecording => encoder != null;
    public string VideoPath { get; private set; }

    public void BeginTake(string output, Transform playerTransform, Transform enemyTransform)
    {
        Complete(); directory = output; Directory.CreateDirectory(directory);
        player = playerTransform; enemy = enemyTransform; error = null; count = 0;
        firstFrame = Time.frameCount; pendingMark = "strong";
        VideoPath = Path.Combine(directory, "actual-heavy-parry.mp4");
        try
        {
            if (Camera.main == null) throw new InvalidOperationException("Actual game camera missing.");
            var cameraObject = new GameObject("Owned monster parry video camera");
            captureCamera = cameraObject.AddComponent<Camera>(); captureCamera.CopyFrom(Camera.main);
            captureCamera.enabled = false; captureCamera.orthographic = true;
            captureCamera.nearClipPlane = .1f; captureCamera.farClipPlane = 150;
            int ui = LayerMask.NameToLayer("UI"); if (ui >= 0) captureCamera.cullingMask &= ~(1 << ui);
            var additional = captureCamera.GetUniversalAdditionalCameraData();
            additional.renderType = CameraRenderType.Base; additional.renderPostProcessing = true;
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create(); pixels = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            encoder = new MediaEncoder(VideoPath, new VideoTrackEncoderAttributes
            {
                frameRate = new MediaRational(Fps), width = Width, height = Height, includeAlpha = false,
                targetBitRate = 8000000, bitRateMode = UnityEditor.VideoBitrateMode.High
            });
        }
        catch (Exception e) { error = e.ToString(); Complete(); throw; }
    }

    public void Mark(string name) { if (IsRecording) pendingMark = name; }
    public void CaptureFrame()
    {
        if (!IsRecording || (Time.frameCount - firstFrame) % 2 != 0) return;
        try
        {
            if (player == null || enemy == null) throw new InvalidOperationException("Capture actor disappeared.");
            var bounds = new Bounds(player.position + Vector3.up * .8f, new Vector3(1.5f, 2, 1.5f));
            bounds.Encapsulate(new Bounds(enemy.position + Vector3.up, Vector3.one * 2));
            foreach (var renderer in enemy.GetComponentsInChildren<Renderer>())
                if (renderer.enabled && (renderer is SkinnedMeshRenderer || renderer is MeshRenderer)) bounds.Encapsulate(renderer.bounds);
            Quaternion rotation = Quaternion.Euler(38, 45, 0);
            float vertical = 0, horizontal = 0;
            Vector3 extents = bounds.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? -extents.x : extents.x,
                    (i & 2) == 0 ? -extents.y : extents.y, (i & 4) == 0 ? -extents.z : extents.z);
                Vector3 view = Quaternion.Inverse(rotation) * corner;
                vertical = Mathf.Max(vertical, Mathf.Abs(view.y)); horizontal = Mathf.Max(horizontal, Mathf.Abs(view.x));
            }
            captureCamera.orthographicSize = Mathf.Max(2.6f, vertical + .7f, horizontal * Height / Width + .7f);
            captureCamera.transform.SetPositionAndRotation(bounds.center + rotation * Vector3.back * 35, rotation);
            var previous = RenderTexture.active;
            try
            {
                RenderPipeline.SubmitRenderRequest(captureCamera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false); pixels.Apply(false, false);
                if (!encoder.AddFrame(pixels)) throw new InvalidOperationException("Video frame rejected.");
                count++;
                if (pendingMark != null)
                {
                    File.WriteAllBytes(Path.Combine(directory, pendingMark + ".png"), pixels.EncodeToPNG()); pendingMark = null;
                }
            }
            finally { RenderTexture.active = previous; }
        }
        catch (Exception e) { error = e.ToString(); Complete(); }
    }

    public bool Complete()
    {
        try { encoder?.Dispose(); }
        catch (Exception e) { error = e.ToString(); }
        finally
        {
            encoder = null;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); target = null; }
            if (pixels != null) { UnityEngine.Object.DestroyImmediate(pixels); pixels = null; }
            if (captureCamera != null) { UnityEngine.Object.DestroyImmediate(captureCamera.gameObject); captureCamera = null; }
        }
        bool pass = count > 0 && error == null && File.Exists(VideoPath);
        if (!string.IsNullOrEmpty(directory)) File.WriteAllText(Path.Combine(directory, "video-result.json"), new JObject
        {
            ["status"] = pass ? "PASS" : "FAIL", ["error"] = error, ["path"] = VideoPath,
            ["frames"] = count, ["fps"] = Fps, ["width"] = Width, ["height"] = Height,
            ["durationSeconds"] = count / (float)Fps, ["actualPlayerTest"] = true, ["audioRecorded"] = false
        }.ToString());
        return pass;
    }
    public void Dispose() => Complete();
}
