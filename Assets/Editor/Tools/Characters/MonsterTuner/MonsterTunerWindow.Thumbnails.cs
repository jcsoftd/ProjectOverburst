using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Overburst.EditorTools.MonsterTuner
{
    public sealed partial class MonsterTunerWindow
    {
        private sealed class Thumbnail { public string Hash; public Texture2D Texture; }
        private readonly Dictionary<string, Thumbnail> thumbnails = new Dictionary<string, Thumbnail>();
        private readonly Dictionary<VisualElement, string> thumbnailRows = new Dictionary<VisualElement, string>();
        private readonly Dictionary<string, MonsterTunerCatalog.Entry> thumbnailRequests = new Dictionary<string, MonsterTunerCatalog.Entry>();
        private double nextThumbnail;
        internal static int LiveThumbnailTextures { get; private set; }
        private static string ThumbnailHash(MonsterTunerCatalog.Entry entry)
            => AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(entry.Definition)).ToString();
        private void BindThumbnail(VisualElement row, MonsterTunerCatalog.Entry entry)
        {
            thumbnailRows[row] = entry.Guid;
            string hash = ThumbnailHash(entry);
            if (thumbnails.TryGetValue(entry.Guid, out var thumbnail) && thumbnail.Hash == hash)
            { row.Q<Image>("icon").image = thumbnail.Texture; return; }
            ReleaseThumbnail(entry.Guid);
            row.Q<Image>("icon").image = null;
            if (entry.Definition.ActorPrefab != null) thumbnailRequests[entry.Guid] = entry;
        }
        private void UnbindThumbnail(VisualElement row) { thumbnailRows.Remove(row); row.Q<Image>("icon").image = null; }
        private void TickThumbnails()
        {
            if (EditorApplication.timeSinceStartup < nextThumbnail || thumbnailRequests.Count == 0) return;
            nextThumbnail = EditorApplication.timeSinceStartup + .12d;
            var request = thumbnailRequests.First(); thumbnailRequests.Remove(request.Key);
            if (!thumbnailRows.Values.Contains(request.Key)) return;
            Texture2D texture = null;
            var preview = new MonsterTunerPreviewStage(); MonsterTunerSession source = null;
            try
            {
                source = MonsterTunerSession.Create(request.Value.Definition, false);
                preview.Load(source); preview.SetView(2);
                preview.Camera.GetUniversalAdditionalCameraData().renderShadows = false;
                preview.Render(128, 128);
                var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = preview.Surface;
                    // The HDR preview surface contains linear pixels; keep that color space when displaying its copy.
                    texture = new Texture2D(128, 128, TextureFormat.RGB24, false, true) { hideFlags = HideFlags.HideAndDontSave, name = "MonsterTuner thumbnail " + request.Key };
                    texture.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); texture.Apply(false, false);
                    LiveThumbnailTextures++;
                }
                finally { RenderTexture.active = previous; }
            }
            catch (Exception error)
            {
                if (texture != null) { Object.DestroyImmediate(texture); texture = null; }
                foreach (var row in thumbnailRows.Where(p => p.Value == request.Key).Select(p => p.Key)) row.tooltip += "\n썸네일: " + error.Message;
            }
            finally { preview.Dispose(); if (source != null) Object.DestroyImmediate(source); }
            thumbnails[request.Key] = new Thumbnail { Hash = ThumbnailHash(request.Value), Texture = texture };
            foreach (var row in thumbnailRows.Where(p => p.Value == request.Key).Select(p => p.Key)) row.Q<Image>("icon").image = texture;
            // The catalog is small; retain at most 64 completed images if it grows later.
            while (thumbnails.Count > 64)
            {
                string hidden = thumbnails.Keys.FirstOrDefault(k => !thumbnailRows.Values.Contains(k));
                if (hidden == null) break; ReleaseThumbnail(hidden);
            }
        }
        private void ReleaseThumbnail(string guid)
        {
            if (!thumbnails.TryGetValue(guid, out var item)) return;
            if (item.Texture != null) { Object.DestroyImmediate(item.Texture); LiveThumbnailTextures--; }
            thumbnails.Remove(guid);
        }
        private void DisposeThumbnails()
        {
            foreach (string guid in thumbnails.Keys.ToArray()) ReleaseThumbnail(guid);
            thumbnailRequests.Clear(); thumbnailRows.Clear();
        }
    }
}
