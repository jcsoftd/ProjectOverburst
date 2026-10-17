using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.Caves;
using UnityEditor;
using UnityEngine;

// Compare with the preserved pre-change arithmetic on real outlines, cracks, transforms and stacked floors.
public static class CaveBoundaryOptimizationVerifier
{
    delegate bool HeightQuery(Vector3 point, CavePlatformBoundary data, out float height);
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    sealed class Reference
    {
        public readonly CavePlatformBoundary boundary;
        readonly List<int>[] triangles;
        public Reference(CavePlatformBoundary value)
        {
            boundary = value; boundary.ContainsLocal(Vector3.zero);
            var cache = typeof(CavePlatformBoundary).GetField("cache", Private).GetValue(boundary.Data);
            triangles = (List<int>[])cache.GetType().GetField("triangles").GetValue(cache);
        }
        public bool Height(Vector3 p, out float height, out bool fallback)
        {
            height = 0; fallback = false;
            if (!boundary.ContainsLocal(p)) return false;
            int index = indexOf(p.x, p.z);
            float best = float.MaxValue; var vertices = boundary.Data.floorTriangles;
            var candidates = triangles[index];
            if (candidates != null) foreach (int i in candidates)
            {
                var a = vertices[i]; var b = vertices[i + 1]; var c = vertices[i + 2];
                if (p.x < Mathf.Min(a.x, Mathf.Min(b.x, c.x)) || p.x > Mathf.Max(a.x, Mathf.Max(b.x, c.x))
                    || p.z < Mathf.Min(a.z, Mathf.Min(b.z, c.z)) || p.z > Mathf.Max(a.z, Mathf.Max(b.z, c.z))) continue;
                float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(det) < .00001f) continue;
                float u = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.z - c.z)) / det;
                float v = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.z - c.z)) / det;
                if (u < -.001f || v < -.001f || u + v > 1.001f) continue;
                float y = u * a.y + v * b.y + (1 - u - v) * c.y;
                if (Mathf.Abs(y - p.y) < best) { best = Mathf.Abs(y - p.y); height = y; }
            }
            if (best == float.MaxValue)
            {
                fallback = true;
                foreach (var loop in boundary.Data.loops)
                for (int i = 0; i < loop.points.Length; i++)
                {
                    var a = loop.points[i]; var b = loop.points[(i + 1) % loop.points.Length];
                    var d = new Vector2(b.x - a.x, b.z - a.z);
                    float t = Mathf.Clamp01(Vector2.Dot(new Vector2(p.x - a.x, p.z - a.z), d) / Mathf.Max(.00001f, d.sqrMagnitude));
                    var closest = Vector3.Lerp(a, b, t);
                    float distance = new Vector2(p.x - closest.x, p.z - closest.z).sqrMagnitude;
                    if (distance < best) { best = distance; height = closest.y; }
                }
            }
            return best < float.MaxValue;
        }
        Func<float,float,int> indexOf;
        public void BindIndex()
        {
            var cache = typeof(CavePlatformBoundary).GetField("cache", Private).GetValue(boundary.Data);
            indexOf = (Func<float,float,int>)Delegate.CreateDelegate(typeof(Func<float,float,int>), cache, cache.GetType().GetMethod("Index"));
        }
        public bool Sample(Vector3 world, float up, float down, out Vector3 ground)
        {
            var local = boundary.transform.InverseTransformPoint(world); ground = world;
            if (!Height(local, out float y, out _)) return false;
            ground = boundary.transform.TransformPoint(new Vector3(local.x, y, local.z));
            return ground.y <= world.y + up && ground.y >= world.y - down;
        }
    }
    public static object Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Requires idle Editor");
        var rows = new List<object>(); int total = 0, differences = 0, fallbackCount = 0;
        var examples = new List<object>(); var owned = new List<GameObject>();
        var library = AssetDatabase.LoadAssetAtPath<CavePlatformLibrary>("Assets/ProjectOverburst/04_Contents/World/Caves/Procedural/PlatformLibrary.asset");
        try
        {
            var boundaries = new List<CavePlatformBoundary>();
            foreach (var platform in library.platforms)
            {
                var go = new GameObject("Boundary differential " + platform.prefab.name) { hideFlags = HideFlags.HideAndDontSave };
                owned.Add(go); var b = go.AddComponent<CavePlatformBoundary>(); b.definition = platform.prefab.GetComponentInChildren<CavePlatformBoundary>().Data;
                go.transform.SetPositionAndRotation(new Vector3(13, 7, -23), Quaternion.Euler(4, 37, -3));
                go.transform.localScale = new Vector3(.8f, 1.3f, 1.1f); boundaries.Add(b);
            }
            foreach (bool holes in new[] { false, true })
            {
                var go = new GameObject(holes ? "Synthetic holes and tiny edges" : "Synthetic stacked floors") { hideFlags = HideFlags.HideAndDontSave };
                owned.Add(go); var b = go.AddComponent<CavePlatformBoundary>();
                if (holes)
                {
                    b.loops = new[] { new CavePlatformBoundary.Loop { points = Enumerable.Range(0,512).Select(i => new Vector3(Mathf.Cos(i * Mathf.PI/256)*10, i%7*.2f, Mathf.Sin(i*Mathf.PI/256)*10)).ToArray() }, new CavePlatformBoundary.Loop { points = new[] { new Vector3(-2,0,-2),new Vector3(2,0,-2),new Vector3(2,1,2),new Vector3(2.00001f,2,2),new Vector3(-2,0,2) } } };
                }
                else
                {
                    b.loops = new[] { new CavePlatformBoundary.Loop { points = new[] { new Vector3(-10,1,-10),new Vector3(10,2,-10),new Vector3(10,3,10),new Vector3(-10,4,10) } } };
                    b.floorTriangles = new[] {new Vector3(-8,0,-8),new Vector3(8,0,-8),new Vector3(0,0,8),new Vector3(-8,5,-8),new Vector3(8,5,-8),new Vector3(0,5,8)};
                }
                boundaries.Add(b);
            }
            foreach (var b in boundaries)
            {
                var watch = Stopwatch.StartNew(); b.Data.InvalidateCache(); var reference = new Reference(b); reference.BindIndex(); watch.Stop();
                double buildMs = watch.Elapsed.TotalMilliseconds;
                var points = new List<Vector3>(); var bounds = new Bounds(b.Data.loops[0].points[0], Vector3.zero);
                foreach (var loop in b.Data.loops) foreach (var p in loop.points) bounds.Encapsulate(p);
                foreach (var loop in b.Data.loops)
                for (int i=0;i<loop.points.Length;i++)
                {
                    var a=loop.points[i]; var mid=(a+loop.points[(i+1)%loop.points.Length])*.5f;
                    foreach (var p in new[] {a,mid}) foreach (float offset in new[] {-.02f,-.001f,0,.001f,.02f})
                        points.Add(p + new Vector3(offset,0,-offset));
                }
                var random = new System.Random(49765);
                for (int i=0;i<4096;i++) points.Add(new Vector3(Mathf.Lerp(bounds.min.x-1,bounds.max.x+1,(float)random.NextDouble()),(i%5-2)*3f,Mathf.Lerp(bounds.min.z-1,bounds.max.z+1,(float)random.NextDouble())));
                var fallback = new List<Vector3>(); int localDifferences=0;
                foreach (var p in points)
                {
                    if(reference.Height(p,out _,out bool rim) && rim && fallback.Count<512) fallback.Add(p);
                    var world=b.transform.TransformPoint(p);
                    foreach (var range in new[] {new Vector2(3,40),new Vector2(0,0),new Vector2(40,3)})
                    {
                        bool expected=reference.Sample(world,range.x,range.y,out var oldGround);
                        bool actual=b.Sample(world,range.x,range.y,out var newGround);
                        total++;
                        if(expected!=actual || !oldGround.Equals(newGround))
                        { differences++;localDifferences++;if(examples.Count<12)examples.Add(new {name=b.name,p=new[]{p.x,p.y,p.z},expected,actual,oldGround=new[]{oldGround.x,oldGround.y,oldGround.z},newGround=new[]{newGround.x,newGround.y,newGround.z}}); }
                    }
                }
                fallbackCount += fallback.Count;
                var corpus=(fallback.Count>0 ? fallback : points).Take(256).ToArray();
                var height=(HeightQuery)Delegate.CreateDelegate(typeof(HeightQuery),b,typeof(CavePlatformBoundary).GetMethod("SampleHeight",Private));
                var baselineTimes=new List<double>();var indexedTimes=new List<double>();var cachedTimes=new List<double>();var worldTimes=new List<double>();
                double Measure(Action action){var sw=Stopwatch.StartNew();action();sw.Stop();return sw.Elapsed.TotalMilliseconds;}
                void Old(){foreach(var p in corpus)for(int j=0;j<3;j++)reference.Height(p,out _,out _);}
                void Indexed(){foreach(var p in corpus)for(int j=0;j<3;j++)height(p,b.Data,out _);}
                void Cached(){foreach(var p in corpus)for(int j=0;j<3;j++)b.Sample(b.transform.TransformPoint(p),40,40,out _);}
                void OldWorld(){foreach(var p in corpus)for(int j=0;j<3;j++)reference.Sample(b.transform.TransformPoint(p),40,40,out _);}
                Old();Indexed();Cached();OldWorld();
                for(int i=0;i<5;i++)
                { if(i%2==0){baselineTimes.Add(Measure(Old));indexedTimes.Add(Measure(Indexed));worldTimes.Add(Measure(OldWorld));cachedTimes.Add(Measure(Cached));}else{indexedTimes.Add(Measure(Indexed));baselineTimes.Add(Measure(Old));cachedTimes.Add(Measure(Cached));worldTimes.Add(Measure(OldWorld));} }
                double Median(List<double> values){values.Sort();return values[values.Count/2];}
                var cache=typeof(CavePlatformBoundary).GetField("cache",Private).GetValue(b.Data);
                var rimCandidates=(int[][])cache.GetType().GetField("rims").GetValue(cache);
                var lengths=rimCandidates.Where(r=>r!=null).Select(r=>r.Length).ToArray();
                rows.Add(new {name=b.definition?b.definition.name:b.name,points=points.Count,differences=localDifferences,fallbackPoints=fallback.Count,buildMs,edges=b.Data.loops.Sum(l=>l.points.Length),meanCandidates=lengths.Length>0?lengths.Average():0,maxCandidates=lengths.Length>0?lengths.Max():0,queries=corpus.Length*3,baselineLocalMs=Median(baselineTimes),indexedLocalMs=Median(indexedTimes),baselineWorldMs=Median(worldTimes),cachedWorldMs=Median(cachedTimes)});
            }
            var mutable=boundaries[boundaries.Count-1];
            var before=mutable.Sample(Vector3.zero,100,100,out _);
            mutable.floorTriangles=Array.Empty<Vector3>();mutable.loops[0].points=mutable.loops[0].points.Select(p=>p+Vector3.up*9).ToArray();mutable.InvalidateCache();
            var changed=new Reference(mutable);changed.BindIndex();
            bool expectedAfter=changed.Sample(Vector3.zero,100,100,out var expectedGround);
            bool actualAfter=mutable.Sample(Vector3.zero,100,100,out var actualGround);
            total++;if(expectedAfter!=actualAfter || !expectedGround.Equals(actualGround))differences++;
            var result=new {status=differences==0?"PASS":"FAIL",total,differences,fallbackCount,cacheInvalidationChecked=true,rows,examples};
            File.WriteAllText(output,JsonConvert.SerializeObject(result,Formatting.Indented));
            if(differences!=0)throw new InvalidOperationException("Boundary sample semantics changed");
            return result;
        }
        finally { foreach(var go in owned)if(go)UnityEngine.Object.DestroyImmediate(go); }
    }
}
