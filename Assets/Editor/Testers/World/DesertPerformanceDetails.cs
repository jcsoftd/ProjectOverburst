using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

public static class DesertPerformanceDetails
{
    public static readonly string[] RequiredMetrics = { "Main Thread", "Render Thread", "PlayerLoop", "GC Allocated In Frame",
        "Draw Calls Count", "SetPass Calls Count", "Triangles Count", "Physics.Simulate", "Animators.Update",
        "OVERBURST.MapSpawnFinite.Total", "OVERBURST.MapSpawnFinite.Reconcile", "OVERBURST.MapSpawnFinite.Refresh",
        "OVERBURST.MapSpawnFinite.Priority", "OVERBURST.MapSpawnBatch", "Overburst.AI.ChasePlan", "Overburst.AI.TargetPlan", "Overburst.AI.SquadUpdate" };
    public sealed class FrameDetail
    {
        public int frame;
        public double wallMs, observerMs;
        public string workloadRow;
        public Dictionary<string, double> metrics = new Dictionary<string, double>();
    }
    public sealed class Analysis
    {
        public int targetFps, frames, overBudget, over33, over50, over100, longestOverBudgetStreak, matchedPositions, missingPositions;
        public double budgetMs, seconds, fps, onePercentLowFps, overBudgetPercent, memoryDeltaBytes;
        public string[] missingMetrics;
        public Dictionary<string, double> markerSampleTotals = new Dictionary<string, double>();
        public List<FrameDetail> worstFrames = new List<FrameDetail>();
        public string alignment = "workloadRow is the early callback observation with this frame ID. Profiler latest-completed samples can lag; coincidence is a lead, not proven causality. GPU remains a separate delayed stream.";
    }
    public static string[] ParseCsv(string line)
    {
        var values = new List<string>(); var value = new StringBuilder(); bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted) { values.Add(value.ToString()); value.Clear(); }
            else value.Append(c);
        }
        if (quoted) throw new InvalidDataException("닫히지 않은 CSV 따옴표");
        values.Add(value.ToString()); return values.ToArray();
    }
    static double Number(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
        && !double.IsNaN(number) && !double.IsInfinity(number) ? number : double.NaN;
    public static Analysis Analyze(string folder, int targetFps, double memoryDelta)
    {
        var result = new Analysis { targetFps = targetFps, budgetMs = 1000d / targetFps, memoryDeltaBytes = memoryDelta };
        var positions = new Dictionary<int, string>();
        string positionPath = Path.Combine(folder, "positions.csv");
        if (File.Exists(positionPath))
            foreach (string line in File.ReadLines(positionPath).Skip(1))
            { int comma = line.IndexOf(','); if (comma > 0 && int.TryParse(line.Substring(0, comma), out int frame)) positions[frame] = line; }
        using (var reader = new StreamReader(Path.Combine(folder, "frames.csv")))
        {
            string[] header = ParseCsv(reader.ReadLine() ?? throw new InvalidDataException("빈 프레임 CSV"));
            int frameColumn = Array.IndexOf(header, "frame"), wallColumn = Array.IndexOf(header, "wall_ms"), observerColumn = Array.IndexOf(header, "observer_ms");
            if (frameColumn < 0 || wallColumn < 0) throw new InvalidDataException("프레임 CSV 필수 열 누락");
            result.missingMetrics = RequiredMetrics.Where(name => !header.Any(h => h.Contains("/" + name + " ["))).ToArray();
            var walls = new List<double>(); int streak = 0; string line;
            while ((line = reader.ReadLine()) != null)
            {
                string[] row = ParseCsv(line); if (row.Length != header.Length) throw new InvalidDataException("CSV 열 수 불일치");
                double wall = Number(row[wallColumn]);
                if (double.IsNaN(wall) || wall <= 0) continue;
                int frame = int.Parse(row[frameColumn], CultureInfo.InvariantCulture);
                walls.Add(wall); result.seconds += wall / 1000;
                if (wall > result.budgetMs) { result.overBudget++; streak++; } else streak = 0;
                result.longestOverBudgetStreak = Math.Max(streak, result.longestOverBudgetStreak);
                if (wall > 33.333333) result.over33++; if (wall > 50) result.over50++; if (wall > 100) result.over100++;
                for (int i = 0; i < header.Length; i++) if (header[i].EndsWith(" samples [count]", StringComparison.Ordinal))
                {
                    double value = Number(row[i]); if (double.IsNaN(value) || value < 0) continue;
                    result.markerSampleTotals.TryGetValue(header[i], out double total); result.markerSampleTotals[header[i]] = total + value;
                }
                bool matched = positions.TryGetValue(frame, out string workload);
                if (matched) result.matchedPositions++; else result.missingPositions++;
                if (result.worstFrames.Count == 20 && wall <= result.worstFrames[result.worstFrames.Count - 1].wallMs) continue;
                var detail = new FrameDetail { frame = frame, wallMs = wall, workloadRow = workload,
                    observerMs = observerColumn >= 0 ? Number(row[observerColumn]) : double.NaN };
                for (int i = 0; i < header.Length; i++)
                    if (header[i].Contains(" [")) { double value = Number(row[i]); if (!double.IsNaN(value)) detail.metrics.Add(header[i], value); }
                result.worstFrames.Add(detail); result.worstFrames.Sort((a, b) => b.wallMs.CompareTo(a.wallMs));
                if (result.worstFrames.Count > 20) result.worstFrames.RemoveAt(20);
            }
            result.frames = walls.Count; result.fps = result.seconds > 0 ? result.frames / result.seconds : 0;
            result.overBudgetPercent = result.frames > 0 ? result.overBudget * 100d / result.frames : 0;
            walls.Sort(); int tailCount = (int)Math.Ceiling(walls.Count * .01);
            if (tailCount > 0) result.onePercentLowFps = 1000d / walls.Skip(walls.Count - tailCount).Average();
        }
        File.WriteAllText(Path.Combine(folder, "analysis.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
        using (var writer = new StreamWriter(Path.Combine(folder, "worst-frames.csv"), false, new UTF8Encoding(false)))
        {
            writer.WriteLine("frame,wall_ms,observer_ms,workload_row,metrics_json");
            foreach (var frame in result.worstFrames)
                writer.WriteLine(frame.frame + "," + N(frame.wallMs) + "," + N(frame.observerMs) + "," + Q(frame.workloadRow) + "," + Q(JsonConvert.SerializeObject(frame.metrics)));
        }
        return result;
    }
    static string N(double value) => double.IsNaN(value) ? "" : value.ToString("0.######", CultureInfo.InvariantCulture);
    static string Q(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

    public static object ActorSnapshot(RunWorldGate gate, Vector3 playerPosition)
    {
        long start = Stopwatch.GetTimestamp();
        var camera = QuarterViewCamera.ActiveInstance ? QuarterViewCamera.ActiveInstance.GetComponent<Camera>() : Camera.main;
        var planes = camera ? GeometryUtility.CalculateFrustumPlanes(camera) : null;
        var actors = gate.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<EnemyActor>(true)).Where(a => a.IsLeased && a.gameObject.activeInHierarchy).ToArray();
        var rows = actors.Select(a =>
        {
            var ai = a.AI; var animator = a.Animator;
            var renderers = a.GetComponentsInChildren<Renderer>(); var lights = a.GetComponentsInChildren<Light>();
            bool inGameFrustum = planes != null && renderers.Any(r => r.enabled && !r.forceRenderingOff && GeometryUtility.TestPlanesAABB(planes, r.bounds));
            bool rendererVisible = renderers.Any(r => r.enabled && r.isVisible);
            return new { instance = a.GetInstanceID(), lease = a.LeaseVersion, definition = a.Definition ? a.Definition.EnemyId : "UNKNOWN",
                position = a.transform.position, distance = Vector3.Distance(playerPosition, a.transform.position),
                alive = a.Health && !a.Health.IsDead, hp = a.Health ? a.Health.CurrentHp : -1, aiEnabled = ai && ai.isActiveAndEnabled,
                state = ai ? ai.CurrentStateName : "NONE", target = ai && ai.Target ? ai.Target.name : "NONE",
                aiTickInterval = ai ? ai.CurrentAiTickInterval : -1, aiTicks = ai ? ai.AiTickCount : -1, skippedUpdates = ai ? ai.AiSkippedUpdateCount : -1,
                planningEvaluations = ai ? ai.PlanningEvaluationCount : -1, planningReuses = ai ? ai.PlanningReuseCount : -1,
                movementEnabled = a.Movement && a.Movement.isActiveAndEnabled,
                animatorEnabled = animator && animator.isActiveAndEnabled, culling = animator ? animator.cullingMode.ToString() : "NONE",
                inGameFrustum, rendererVisible, sceneOrShadowVisibilityCandidate = rendererVisible && !inGameFrustum,
                renderers = renderers.Length, skinned = renderers.OfType<SkinnedMeshRenderer>().Count(),
                bones = renderers.OfType<SkinnedMeshRenderer>().Sum(s => s.bones.Length),
                enabledLights = lights.Count(l => l.enabled), pointLights = lights.Count(l => l.enabled && l.type == LightType.Point),
                shadowLights = lights.Count(l => l.enabled && l.shadows != LightShadows.None) };
        }).ToArray();
        return new { frame = Time.frameCount, scope = "Current map scene leased actors; out-of-measurement census; no strategy getters or IsTargetValid calls",
            count = rows.Length, globalRegisteredAi = EnemyAIController.ActiveEnemyCount, actors = rows,
            snapshotBuildMs = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency };
    }
    public static object EditorCameraContext()
        => new { focusedWindow = EditorWindow.focusedWindow ? EditorWindow.focusedWindow.GetType().Name : "NONE",
            sceneViews = SceneView.sceneViews.Cast<SceneView>().Select(v => new { title = v.titleContent.text, v.in2DMode,
                camera = v.camera ? v.camera.name : "NONE", position = v.camera ? v.camera.transform.position : Vector3.zero }).ToArray(),
            note = "Scene/shadow visibility can keep AI at a faster rate. Visibility mismatch is a lead; no camera state is changed." };

    public static void ArchiveReference(string output)
    {
        string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출/Performance/20261013_CombatSpawnAiAudit/최적화조사.md"));
        string directory = Path.Combine(output, "Reference"); Directory.CreateDirectory(directory);
        if (!File.Exists(source))
        { File.WriteAllText(Path.Combine(directory, "reference.json"), JsonConvert.SerializeObject(new { status = "NOT_AVAILABLE", source })); return; }
        byte[] bytes = File.ReadAllBytes(source); File.WriteAllBytes(Path.Combine(directory, "최적화조사.md"), bytes);
        string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        var candidates = Regex.Matches(Encoding.UTF8.GetString(bytes), @"(?m)^\| ([SLPAC]\d{2}) \| ([^|]+) \| ([^|]+) \| ([^|]+) \| ([^|]+) \|")
            .Cast<Match>().Select(m => new { id = m.Groups[1].Value, area = m.Groups[2].Value.Trim(), maps = m.Groups[3].Value.Trim(), title = m.Groups[5].Value.Trim() }).ToArray();
        string evidencePath = Path.Combine(Path.GetDirectoryName(source), "source-evidence.json");
        var currentEvidence = new List<object>();
        if (File.Exists(evidencePath))
        {
            byte[] originalEvidence = File.ReadAllBytes(evidencePath); File.WriteAllBytes(Path.Combine(directory, "audit-source-evidence.json"), originalEvidence);
            var evidence = Newtonsoft.Json.Linq.JArray.Parse(Encoding.UTF8.GetString(originalEvidence));
            foreach (var group in evidence.GroupBy(e => (string)e["path"]))
            {
                string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", group.Key));
                if (!File.Exists(full)) { currentEvidence.Add(new { path = group.Key, status = "SOURCE_NOT_AVAILABLE" }); continue; }
                byte[] current = File.ReadAllBytes(full); string currentHash;
                using (var sha = SHA256.Create()) currentHash = BitConverter.ToString(sha.ComputeHash(current)).Replace("-", "").ToLowerInvariant();
                string[] lines = Encoding.UTF8.GetString(current).Replace("\r\n", "\n").Split('\n');
                currentEvidence.Add(new { path = group.Key, currentSha256 = currentHash,
                    status = group.All(e => (string)e["sha256"] == currentHash) ? "UNCHANGED_SINCE_AUDIT" : "SOURCE_CHANGED_SINCE_AUDIT",
                    locations = group.Select(e => new { auditLine = (int)e["line"], needle = (string)e["needle"],
                        currentLines = lines.Select((line, index) => new { line, number = index + 1 }).Where(l => l.line.Contains((string)e["needle"]))
                            .Select(l => new { l.number, l.line }).ToArray() }).ToArray() });
            }
            File.WriteAllText(Path.Combine(directory, "current-source-evidence.json"), JsonConvert.SerializeObject(currentEvidence, Formatting.Indented));
        }
        File.WriteAllText(Path.Combine(directory, "reference.json"), JsonConvert.SerializeObject(new { status = "STATIC_REFERENCE_ONLY", source, sha256 = hash,
            referencedChat = "전투·몬스터 AI 최적화 조사", candidates, note = "Candidate IDs are static leads. Measurement does not automatically confirm a cause or authorize implementation." }, Formatting.Indented));
    }
}
