using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Caves;
using UnityEditor;
using UnityEngine;

public static class CavePlatformSpawnVerifier
{
    static string Bank(CavePlatformLibrary library) => JsonConvert.SerializeObject(new {
        library.spawnBakeSignature,
        bodies = library.spawnBodies.Select(b => new { b.radius, b.height, ids = b.definitions.Select(d => d.EnemyId).ToArray() }).ToArray(),
        platforms = library.platforms.Select(p => p.spawnPoints.Select(s => new { x = s.position.x, y = s.position.y, z = s.position.z, s.bodyMask }).ToArray()).ToArray()
    });

    public static object Verify(string output)
    {
        CavePlatformMapBuilder.Guard(); Directory.CreateDirectory(output);
        int checks = 0, physical = 0;
        Action<bool, string> require = (valid, message) => { checks++; if (!valid) throw new InvalidOperationException(message); };
        try
        {
            var library = CavePlatformSpawnBuilder.Library;
            var expected = CavePlatformSpawnBuilder.Bodies();
            require(library.spawnBodies.Length == expected.Length, "Body profile count changed.");
            require(library.spawnBakeSignature == CavePlatformSpawnBuilder.Signature(library, expected), "Spawn bank is stale.");
            for (int body = 0; body < expected.Length; body++)
            {
                var b = library.spawnBodies[body];
                require(b.radius == expected[body].radius && b.height == expected[body].height, "Body dimensions changed: " + body);
                require(b.definitions.SequenceEqual(expected[body].definitions), "Body membership changed: " + body);
            }
            string before = Bank(library);
            AssetDatabase.ImportAsset(CavePlatformMapBuilder.LibraryPath, ImportAssetOptions.ForceUpdate);
            library = CavePlatformSpawnBuilder.Library;
            require(before == Bank(library), "Saved/reimported local coordinates or 64-bit body masks changed.");
            require(library.platforms.SelectMany(p => p.spawnPoints).Any(p => p.bodyMask > int.MaxValue), "High mask bits were not exercised.");
            for (int index = 0; index < library.platforms.Length; index++)
            {
                var platform = library.platforms[index]; var points = platform.spawnPoints;
                require(points.Length > 0, "Empty platform: " + platform.prefab.name);
                require(points.Select(p => p.position).Distinct().Count() == points.Length, "Duplicate local position: " + platform.prefab.name);
                for (int body = 0; body < expected.Length; body++) require(points.Any(p => (p.bodyMask & (1L << body)) != 0), "No candidate for body " + body);
                foreach (var point in points)
                {
                    require(!float.IsNaN(point.position.x) && !float.IsInfinity(point.position.x) && !float.IsNaN(point.position.y) && !float.IsInfinity(point.position.y)
                        && !float.IsNaN(point.position.z) && !float.IsInfinity(point.position.z), "Nonfinite coordinate.");
                    require(point.bodyMask > 0 && (point.bodyMask >> expected.Length) == 0, "Invalid body mask.");
                }
                using (var probe = new CavePlatformSpawnBuilder.Probe(platform))
                {
                    require(!probe.Floor(probe.root.transform.position + new Vector3(200, 0, 200), 0, out _), "Outside-platform floor accepted.");
                    foreach (float yaw in new[] { 0f, 23f })
                    {
                        probe.root.transform.rotation = Quaternion.Euler(0, yaw, 0); Physics.SyncTransforms();
                        for (int sample = 0; sample < Mathf.Min(96, points.Length); sample++)
                        {
                            var point = points[sample * points.Length / Mathf.Min(96, points.Length)];
                            var world = probe.root.transform.TransformPoint(point.position);
                            var restored = probe.root.transform.InverseTransformPoint(world);
                            require((restored - point.position).magnitude < .003f, "Local/world round trip failed.");
                            for (int body = 0; body < expected.Length; body++)
                            {
                                if ((point.bodyMask & (1L << body)) == 0) continue;
                                var shape = expected[body]; physical++;
                                require(probe.Floor(world, shape.radius + .1f, out var floor) && Mathf.Abs(floor.y - world.y) <= .3f
                                    && probe.Clear(floor, shape.radius, shape.height, .04f),
                                    $"Saved candidate failed physical recheck: {platform.prefab.name}, yaw={yaw}, sample={sample}, body={body}, local={point.position}");
                            }
                        }
                    }
                }
            }
            var result = new { status = "PASS", checks, physical, platforms = library.platforms.Length, profiles = expected.Length, points = library.platforms.Sum(p => p.spawnPoints.Length),
                evidence = "Native save/reimport, 64-bit masks, negative floor, local/world transform, floor/edge/static-body clearance at yaw 0 and 23 on up to 96 points per platform", play = "NOT_RUN", player = "NOT_RUN" };
            File.WriteAllText(Path.Combine(output, "verification.json"), JsonConvert.SerializeObject(result, Formatting.Indented)); return result;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "verification.json"), JsonConvert.SerializeObject(new { status = "FAIL", checks, physical, error = error.ToString() }, Formatting.Indented)); throw;
        }
    }
}
