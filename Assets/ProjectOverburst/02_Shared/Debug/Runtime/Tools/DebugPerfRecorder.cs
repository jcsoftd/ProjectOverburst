#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Overburst.DebugTools
{
    /// <summary>
    /// 10초·30초 성능 측정(90C 7.8). 디버그 창이 매 프레임 Tick한다.
    /// 평균·최악·1% 느린 프레임(99번째 백분위)·GC 횟수·몬스터 수를 낸다. 에디터에서는 CSV를 프로젝트 밖 산출 폴더에 쓴다.
    /// </summary>
    public static class DebugPerfRecorder
    {
        private static readonly List<float> frames = new List<float>(4096);
        private static readonly List<int> monsters = new List<int>(4096);
        private static float duration;
        private static float elapsed;
        private static int gcStart;
        private static long memoryStart;
        private static DateTime startedAt;

        public static bool Running { get; private set; }
        public static string LastSummary { get; private set; } = "아직 측정하지 않았어요";
        public static string LastCsvPath { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            frames.Clear();
            monsters.Clear();
            Running = false;
            LastSummary = "아직 측정하지 않았어요";
            LastCsvPath = null;
        }

        public static string Progress => Running ? $"측정 중 {elapsed:0.0} / {duration:0}초" : LastSummary;

        public static DebugResult Start(float seconds)
        {
            if (Running || Performance.CombatPerformancePanel.Running)
                return DebugResult.Fail("이미 측정 중이에요");
            frames.Clear();
            monsters.Clear();
            duration = Mathf.Max(1f, seconds);
            elapsed = 0f;
            gcStart = GC.CollectionCount(0);
            memoryStart = GC.GetTotalMemory(false);
            startedAt = DateTime.Now;
            Running = true;
            return DebugResult.Ok($"{duration:0}초 측정 시작");
        }

        public static void Tick(float unscaledDeltaTime)
        {
            if (!Running || unscaledDeltaTime <= 0f)
                return;
            frames.Add(unscaledDeltaTime * 1000f);
            monsters.Add(EnemyAIController.AliveEnemyCount);
            elapsed += unscaledDeltaTime;
            if (elapsed >= duration)
                Finish();
        }

        private static void Finish()
        {
            Running = false;
            if (frames.Count == 0)
            {
                LastSummary = "프레임이 없어요";
                return;
            }
            var sorted = new List<float>(frames);
            sorted.Sort();
            double sum = 0;
            for (int i = 0; i < frames.Count; i++)
                sum += frames[i];
            float average = (float)(sum / frames.Count);
            float worst = sorted[sorted.Count - 1];
            float low1 = sorted[Mathf.Clamp(Mathf.CeilToInt(sorted.Count * 0.99f) - 1, 0, sorted.Count - 1)];
            int gc = GC.CollectionCount(0) - gcStart;
            long memoryDelta = GC.GetTotalMemory(false) - memoryStart;
            int monsterMax = 0;
            long monsterSum = 0;
            for (int i = 0; i < monsters.Count; i++)
            {
                monsterMax = Mathf.Max(monsterMax, monsters[i]);
                monsterSum += monsters[i];
            }
            float monsterAverage = monsters.Count > 0 ? monsterSum / (float)monsters.Count : 0f;

            LastSummary = string.Format(CultureInfo.InvariantCulture,
                "{0:0}초 · 평균 {1:0.0}ms({2:0}fps) · 1% 느린 {3:0.0}ms · 최악 {4:0.0}ms · GC {5}회 · 관리 메모리 {6:+0.0;-0.0}MB · 몬스터 평균 {7:0} / 최대 {8}",
                duration, average, 1000f / Mathf.Max(0.001f, average), low1, worst, gc, memoryDelta / 1048576f,
                monsterAverage, monsterMax);
            LastCsvPath = WriteCsv(average, low1, worst, gc);
            DebugRuntime.Report("성능 측정", DebugResult.Ok(LastSummary));
        }

        private static string WriteCsv(float average, float low1, float worst, int gc)
        {
#if UNITY_EDITOR
            try
            {
                // 프로젝트 기준 상대 경로: ProjectOverburst/../개인파일/코덱스산출/Perf/<날짜>_디버그측정
                string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "개인파일", "코덱스산출", "Perf",
                    startedAt.ToString("yyyyMMdd") + "_디버그측정"));
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, startedAt.ToString("HHmmss") + $"_{duration:0}s.csv");
                var builder = new StringBuilder(frames.Count * 16);
                builder.AppendLine($"# scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} average={average:0.00} low1={low1:0.00} worst={worst:0.00} gc={gc}");
                builder.AppendLine("frame,ms,monsters");
                for (int i = 0; i < frames.Count; i++)
                    builder.Append(i).Append(',').Append(frames[i].ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(monsters[i]).Append('\n');
                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
                return path;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[DebugHub] 성능 측정 CSV를 쓰지 못했어요: " + exception.Message);
                return null;
            }
#else
            return null;
#endif
        }
    }
}
#endif
