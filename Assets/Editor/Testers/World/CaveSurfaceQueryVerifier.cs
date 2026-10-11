using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Overburst.Caves;
using UnityEditor;
using UnityEngine;

public static class CaveSurfaceQueryVerifier
{
    public static string Run(string directory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Use an idle Editor.");
        string output = Path.GetFullPath(directory);
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output)) throw new ArgumentException("Use a new owned output directory.");
        Directory.CreateDirectory(output);
        var assets = AssetDatabase.LoadAssetAtPath<CaveGenerationAssets>(CaveLiveGenerationBuilder.AssetPath);
        var results = new List<object>(); int checkedPoints = 0;
        try
        {
            foreach (var platform in assets.library.platforms)
            {
                var points = new List<Vector3>(platform.surfaceTriangles);
                var random = new System.Random(73130);
                for (int i = 0; i < 4000; i++) points.Add(platform.surfaceTriangles[random.Next(platform.surfaceTriangles.Length)]
                    + new Vector3((float)random.NextDouble() * 4 - 2, 0, (float)random.NextDouble() * 4 - 2));
                // Include exact grid edges and points outside the authored floor.
                for (int z = -40; z <= 40; z += 2) for (int x = -40; x <= 40; x += 2) points.Add(new Vector3(x, 0, z));
                var reference = new (bool hit, Vector3 point)[points.Count];
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < points.Count; i++) reference[i].hit = Reference(platform.surfaceTriangles, points[i], out reference[i].point);
                double uncachedMs = watch.Elapsed.TotalMilliseconds;
                platform.InvalidateSurfaceCache(); watch.Restart();
                for (int i = 0; i < points.Count; i++)
                {
                    bool hit = platform.Surface(points[i], out var point);
                    if (hit != reference[i].hit || (hit && Mathf.Abs(point.y - reference[i].point.y) > .00001f))
                        throw new Exception("Surface result changed: " + platform.source.name + " / point " + i);
                }
                checkedPoints += points.Count;
                results.Add(new { name = platform.source.name, triangles = platform.surfaceTriangles.Length / 3, points = points.Count, uncachedMs, indexedMs = watch.Elapsed.TotalMilliseconds });
            }
            var mutable = new CavePlatformLibrary.Platform { surfaceTriangles = new[] { Vector3.zero, Vector3.right * 4, Vector3.forward * 4 } };
            if (!mutable.Surface(Vector3.one, out _)) throw new Exception("Synthetic floor missing");
            mutable.surfaceTriangles = mutable.surfaceTriangles.Select(p => p + Vector3.up * 7).ToArray();
            if (!mutable.Surface(Vector3.one, out var moved) || moved.y != 7) throw new Exception("Replaced triangle data remained cached");
            for (int i = 0; i < mutable.surfaceTriangles.Length; i++) mutable.surfaceTriangles[i] += Vector3.up;
            mutable.InvalidateSurfaceCache();
            if (!mutable.Surface(Vector3.one, out moved) || moved.y != 8) throw new Exception("Edited triangle data remained cached");
            string path = Path.Combine(output, "result.json");
            File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(new { status = "PASS", checkedPoints, cacheInvalidationChecks = 2, results }, Newtonsoft.Json.Formatting.Indented));
            return path;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "result.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new { status = "FAIL", checkedPoints, results, error = error.ToString() }, Newtonsoft.Json.Formatting.Indented));
            throw;
        }
    }
    // Frozen pre-index floor query. Optimisation must preserve every accepted triangle and height.
    static bool Reference(Vector3[] triangles, Vector3 p, out Vector3 point)
    {
        float height = float.MinValue;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            var a = triangles[i]; var b = triangles[i + 1]; var c = triangles[i + 2];
            if (p.x < Mathf.Min(a.x, Mathf.Min(b.x, c.x)) || p.x > Mathf.Max(a.x, Mathf.Max(b.x, c.x)) || p.z < Mathf.Min(a.z, Mathf.Min(b.z, c.z)) || p.z > Mathf.Max(a.z, Mathf.Max(b.z, c.z))) continue;
            float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(denominator) < .00001f) continue;
            float u = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.z - c.z)) / denominator;
            float v = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.z - c.z)) / denominator;
            if (u >= -.001f && v >= -.001f && u + v <= 1.001f) height = Mathf.Max(height, u * a.y + v * b.y + (1 - u - v) * c.y);
        }
        point = new Vector3(p.x, height, p.z); return height > float.MinValue;
    }
}
