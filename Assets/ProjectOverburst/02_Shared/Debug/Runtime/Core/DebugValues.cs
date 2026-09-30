#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace Overburst.DebugTools
{
    /// <summary>디버그 항목 실행 결과. 실패는 상태줄에 경고색으로 뜬다.</summary>
    public readonly struct DebugResult
    {
        public readonly bool Success;
        public readonly string Message;

        private DebugResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        public static DebugResult Ok(string message = null) => new DebugResult(true, message);
        public static DebugResult Fail(string message) => new DebugResult(false, message);
    }

    /// <summary>진행 막대 한 줄의 값.</summary>
    public readonly struct DebugProgress
    {
        public readonly float Current;
        public readonly float Maximum;
        public readonly string Text;

        public DebugProgress(float current, float maximum, string text = null)
        {
            Current = current;
            Maximum = maximum;
            Text = text;
        }

        public float Ratio => Maximum > 0f ? Mathf.Clamp01(Current / Maximum) : 0f;
    }

    /// <summary>분포 막대의 구간 하나. Value는 비율이 아니라 원래 값이며 막대가 합계로 나눈다.</summary>
    public readonly struct DebugBarSegment
    {
        public readonly string Label;
        public readonly float Value;
        public readonly Color Color;

        public DebugBarSegment(string label, float value, Color color)
        {
            Label = label;
            Value = value;
            Color = color;
        }
    }
}
#endif
