#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace Overburst.DebugTools
{
    /// <summary>
    /// 디버그 배속·일시정지·한 프레임 진행.
    /// 히트스톱·슬로우 동안에는 <see cref="OverburstTimeEffectArbiter"/>가 Time.timeScale을 소유하므로,
    /// 중재기가 소유하지 않을 때만 값을 바꾼다. 중재기는 1배속 미만에서는 요청을 받지 않는다(90C 6.9).
    /// </summary>
    public static class DebugTime
    {
        public static readonly float[] Speeds = { 0.1f, 0.25f, 0.5f, 1f, 2f };

        private static float speed = 1f;
        private static bool paused;
        private static bool dirty;
        private static bool stepPending;
        private static int stepFrame = -1;
        private static float baseFixedDeltaTime = 0.02f;
        private static bool baseCaptured;
        private static bool changedByDebug;

        public static float Speed => speed;
        public static bool Paused => paused;
        public static bool IsNormal => !paused && Mathf.Approximately(speed, 1f) && !stepPending && stepFrame < 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            speed = 1f;
            paused = false;
            dirty = false;
            stepPending = false;
            stepFrame = -1;
            baseCaptured = false;
            changedByDebug = false;
        }

        public static void SetSpeed(float value)
        {
            speed = Mathf.Clamp(value, 0.05f, 4f);
            dirty = true;
        }

        public static void TogglePause()
        {
            paused = !paused;
            dirty = true;
        }

        /// <summary>일시정지 상태에서 배속 1로 한 프레임만 진행한다. 진행 중이 아니면 먼저 멈춘다.</summary>
        public static void Step()
        {
            paused = true;
            stepPending = true;
            dirty = true;
        }

        public static string StatusText
        {
            get
            {
                if (stepPending || stepFrame >= 0)
                    return "한 프레임 진행 중";
                if (dirty && OverburstTimeEffectArbiter.OwnsTimeScale)
                    return "히트스톱이 끝나면 적용";
                if (paused)
                    return "일시정지";
                string text = speed.ToString("0.##") + "배속";
                return speed < 0.999f ? text + " · 히트스톱 꺼짐" : text;
            }
        }

        /// <summary>디버그 창이 매 프레임 부른다.</summary>
        public static void Tick()
        {
            if (!baseCaptured && Time.timeScale > 0.999f)
            {
                baseFixedDeltaTime = Time.fixedDeltaTime / Time.timeScale;
                baseCaptured = true;
            }

            if (stepFrame >= 0)
            {
                // 한 프레임 진행이 끝났다. 히트스톱이 막 시작됐어도 일시정지가 이긴다(중재기는 외부 변경을 보면 물러난다).
                if (Time.frameCount > stepFrame)
                {
                    stepFrame = -1;
                    Apply(0f);
                }
                return;
            }

            if (stepPending)
            {
                stepPending = false;
                stepFrame = Time.frameCount;
                Apply(1f);
                return;
            }

            if (!dirty || OverburstTimeEffectArbiter.OwnsTimeScale)
                return;
            Apply(paused ? 0f : speed);
            dirty = false;
        }

        /// <summary>창이 없어질 때 디버그로 바꾼 시간을 원래대로 돌린다.</summary>
        public static void RestoreNormal()
        {
            if (!changedByDebug)
                return;
            speed = 1f;
            paused = false;
            stepPending = false;
            stepFrame = -1;
            dirty = false;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = baseFixedDeltaTime;
            changedByDebug = false;
        }

        private static void Apply(float scale)
        {
            Time.timeScale = scale;
            if (scale > 0f)
                Time.fixedDeltaTime = baseFixedDeltaTime * scale;
            changedByDebug = !Mathf.Approximately(scale, 1f) || changedByDebug;
        }
    }
}
#endif
