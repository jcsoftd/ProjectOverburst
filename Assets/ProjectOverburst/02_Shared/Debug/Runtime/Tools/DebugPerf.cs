#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Globalization;
using UnityEngine;
using UnityEngine.Profiling;

namespace Overburst.DebugTools
{
    /// <summary>0.5초 구간 평균 FPS·프레임 시간과 가장 느린 프레임. 디버그 창이 매 프레임 Tick한다.</summary>
    public static class DebugPerf
    {
        private const float Window = 0.5f;
        private static float accumulated;
        private static int frames;
        private static float worst;

        public static float Fps { get; private set; }
        public static float FrameMs { get; private set; }
        public static float WorstMs { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            accumulated = 0f;
            frames = 0;
            worst = 0f;
            Fps = 0f;
            FrameMs = 0f;
            WorstMs = 0f;
        }

        public static void Tick(float unscaledDeltaTime)
        {
            if (unscaledDeltaTime <= 0f)
                return;
            accumulated += unscaledDeltaTime;
            frames++;
            if (unscaledDeltaTime > worst)
                worst = unscaledDeltaTime;
            if (accumulated < Window)
                return;
            Fps = frames / accumulated;
            FrameMs = accumulated * 1000f / frames;
            WorstMs = worst * 1000f;
            accumulated = 0f;
            frames = 0;
            worst = 0f;
        }

        public static string FrameText => Fps <= 0f
            ? "측정 중"
            : $"{Fps.ToString("0", CultureInfo.InvariantCulture)}fps · {FrameMs.ToString("0.0", CultureInfo.InvariantCulture)}ms · 최악 {WorstMs.ToString("0.0", CultureInfo.InvariantCulture)}ms";

        public static string ShortFps => Fps <= 0f ? "-" : Fps.ToString("0", CultureInfo.InvariantCulture) + "fps";

        public static string MemoryText
        {
            get
            {
                float allocated = Profiler.GetTotalAllocatedMemoryLong() / 1048576f;
                float reserved = Profiler.GetTotalReservedMemoryLong() / 1048576f;
                return $"{allocated.ToString("0", CultureInfo.InvariantCulture)}MB / 예약 {reserved.ToString("0", CultureInfo.InvariantCulture)}MB";
            }
        }
    }
}
#endif
