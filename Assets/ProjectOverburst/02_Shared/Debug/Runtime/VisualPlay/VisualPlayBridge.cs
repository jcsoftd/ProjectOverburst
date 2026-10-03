#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overburst.DebugTools
{
    public enum VisualPlayScope { All, Category, Individual, Problems }
    public enum VisualPlayControl { Pause, Replay, Next, Stop, Continue }
    public enum VisualPlayReview { Unreviewed, Checked, Problem }

    public sealed class VisualPlayState
    {
        public bool Running, Paused, Waiting;
        public string Current = "선택한 항목을 재생해 보세요", Observe = "", Phase = "대기", Detail = "", Summary = "아직 재생한 항목이 없어요";
        public int Completed, Planned, Failed, Skipped, Checked, Problems;
    }

    public interface IVisualPlayBackend
    {
        VisualPlayState State { get; }
        string Availability(string caseId);
        string PlanSummary(string categoryId, string caseId, bool fullContent);
        DebugResult Start(VisualPlayScope scope, string categoryId, string caseId);
        DebugResult Control(VisualPlayControl control);
        DebugResult Review(VisualPlayReview review, string note);
        DebugResult UpdateNote(string note);
    }

    /// <summary>Runtime의 고정 디버그 UI와 Editor 시나리오 사이의 요청·표시 연결부.</summary>
    public static class VisualPlayBridge
    {
        public static IVisualPlayBackend Backend { get; set; }
        private static readonly VisualPlayState idle = new VisualPlayState();
        private static VisualPlayCategory category = VisualPlayCatalog.Categories[0];
        private static VisualPlayCase selected = VisualPlayCatalog.Categories[0].Cases[0];
        public static float ObserveSeconds = 4f;
        public static bool WaitAfterScenario;
        public static bool FullContent = true;
        private static string note = "";
        public static string Note
        {
            get => note;
            set { note = value ?? ""; Backend?.UpdateNote(note); }
        }
        public static void RestoreNote(string value) => note = value ?? "";
        public static VisualPlayState State => Backend?.State ?? idle;
        public static bool Running => State.Running;
        public static bool CanStart => Application.isEditor && Backend != null && !Running;
        public static VisualPlayCategory Category
        {
            get => category;
            set
            {
                if (Running || value == null) return;
                category = value;
                selected = value.Cases[0];
            }
        }
        public static VisualPlayCase Selected
        {
            get => selected;
            set { if (!Running && value != null) selected = value; }
        }
        public static IReadOnlyList<VisualPlayCase> CurrentCases => category.Cases;
        public static string ReadyText => Backend?.Availability(selected.Id) ?? "Unity Editor 연결이 필요해요";
        public static string RangeText => Backend?.PlanSummary(category.Id, selected.Id, FullContent)
            ?? $"전체 {VisualPlayCatalog.Categories.Length}개 카테고리 · {VisualPlayCatalog.All.Length}개 장면 / 선택 {category.Cases.Length}개";
        public static DebugResult Start(VisualPlayScope scope) => Backend?.Start(scope, category.Id, selected.Id) ?? DebugResult.Fail("Unity Editor 연결이 필요해요");
        public static DebugResult Control(VisualPlayControl control) => Backend?.Control(control) ?? DebugResult.Fail("재생 중인 항목이 없어요");
        public static DebugResult Review(VisualPlayReview review) => Backend?.Review(review, Note) ?? DebugResult.Fail("확인할 재생 기록이 없어요");
    }
}
#endif
