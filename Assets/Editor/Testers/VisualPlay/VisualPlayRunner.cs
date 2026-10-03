#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Overburst.DebugTools;
using UnityEngine;

public sealed class VisualPlayEntry
{
    public string caseId, variant, label;
}

public sealed class VisualPlayRecord
{
    public int planIndex;
    public string caseId, variant, label, playback, note, diagnostics;
    public VisualPlayReview review;
}

/// <summary>동일 시나리오를 모든 범위에서 순차 실행한다. 자동 회귀 판정을 시각 완료와 합치지 않는다.</summary>
public sealed class VisualPlayRunner : IDisposable
{
    readonly IReadOnlyList<VisualPlayEntry> entries;
    readonly VisualPlayState state;
    readonly Action<IReadOnlyList<VisualPlayRecord>> finished;
    readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    readonly List<VisualPlayRecord> records = new List<VisualPlayRecord>();
    VisualPlayContext context;
    VisualPlayRecord current;
    int index, lastFrame = -1;
    bool skip, replay, stop, pause, waiting, continueRequested, disposed;
    bool panelPaused;
    string detailBeforePanel;
    readonly Overburst.Persistence.AccountGameplaySession accountOwner = Overburst.Persistence.AccountGameplaySession.Current;
    float actionRemaining;
    public IReadOnlyList<VisualPlayRecord> Records => records;

    public VisualPlayRunner(IReadOnlyList<VisualPlayEntry> entries, VisualPlayState state,
        Action<IReadOnlyList<VisualPlayRecord>> finished)
    {
        this.entries = entries; this.state = state; this.finished = finished;
        state.Planned = entries.Count; state.Completed = state.Failed = state.Skipped = 0;
        state.Running = true; state.Checked = state.Problems = 0;
        state.Summary = $"재생 0/{entries.Count} · 실패 0 · 미실행 0\n화면 확인 0 · 문제 표시 0 · 미확인 {entries.Count}";
    }

    public DebugResult Control(VisualPlayControl control)
    {
        switch (control)
        {
            case VisualPlayControl.Stop: stop = true; break;
            case VisualPlayControl.Next: skip = true; break;
            case VisualPlayControl.Replay: replay = true; break;
            case VisualPlayControl.Continue: pause = waiting = false; continueRequested = true; break;
            case VisualPlayControl.Pause:
                if (pause || waiting) { pause = waiting = false; continueRequested = true; }
                else pause = true;
                break;
        }
        state.Paused = pause; state.Waiting = waiting;
        return DebugResult.Ok(control == VisualPlayControl.Stop ? "중단하고 일반 Play 상태로 반환해요" : "재생 조작을 적용했어요");
    }

    public DebugResult Review(VisualPlayReview review, string note)
    {
        VisualPlayRecord target = current ?? records.LastOrDefault();
        if (target == null) return DebugResult.Fail("확인할 재생 장면이 없어요");
        target.review = review; target.note = note;
        UpdateSummary();
        return DebugResult.Ok(review == VisualPlayReview.Problem ? "문제 표시했어요" : "화면 확인을 기록했어요");
    }

    public void Tick()
    {
        if (disposed) return;
        try
        {
        if (stop)
        {
            EndCurrent("중단", "사용자가 재생을 중단했어요");
            Finish(); return;
        }
        if (skip || replay)
        {
            bool repeat = replay;
            EndCurrent(repeat ? "다시 보기" : "건너뜀", repeat ? "같은 장면을 다시 준비해요" : "다음 장면으로 넘어갔어요");
            if (repeat) index = Mathf.Max(0, index - 1);
            skip = replay = false; pause = waiting = false; continueRequested = true;
            state.Waiting = state.Paused = false;
        }
        if (DebugHub.IsOpen)
        {
            if (!panelPaused && !OverburstTimeEffectArbiter.IsPaused)
            {
                panelPaused = true; detailBeforePanel = state.Detail;
                OverburstTimeEffectArbiter.SetPaused(true);
            }
            state.Detail = "디버그 창에서 확인 중 · F1으로 닫으면 이어져요";
            return;
        }
        ReleasePanelPause();
        if (UnityEditor.EditorApplication.isPaused || DebugTime.Paused || Time.frameCount == lastFrame)
            return;
        lastFrame = Time.frameCount;
            if (stack.Count == 0)
            {
                if (index >= entries.Count) { Finish(); return; }
                VisualPlayEntry entry = entries[index++];
                VisualPlayCase definition = VisualPlayCatalog.Find(entry.caseId);
                current = new VisualPlayRecord { planIndex = index - 1, caseId = entry.caseId, variant = entry.variant, label = entry.label, playback = "재생 중" };
                records.Add(current); VisualPlayBridge.Note = "";
                state.Current = definition.Title + (string.IsNullOrEmpty(entry.label) ? "" : " · " + entry.label);
                state.Observe = definition.Observe; state.Phase = "준비"; state.Detail = "";
                if (!VisualPlayScenarios.IsSupported(entry.caseId))
                {
                    EndCurrent("미연결", VisualPlayScenarios.UnavailableReason(entry.caseId)); return;
                }
                context = new VisualPlayContext(entry, state);
                stack.Push(RunCurrent());
            }
            int moves = 0;
            while (stack.Count > 0 && moves++ < 32)
            {
                IEnumerator top = stack.Peek();
                if (top.MoveNext())
                {
                    if (top.Current is IEnumerator nested) { stack.Push(nested); continue; }
                    return;
                }
                (stack.Pop() as IDisposable)?.Dispose();
            }
            if (stack.Count == 0) EndCurrent("완료", "");
        }
        catch (VisualPlayUnavailable exception) { EndCurrent("미지원", exception.Message); }
        catch (Exception exception)
        {
            if (current != null) current.diagnostics = exception.ToString();
            EndCurrent("실패", exception.Message);
            Debug.LogWarning("[시각 확인] " + state.Current + ": " + exception.Message);
        }
    }

    IEnumerator RunCurrent()
    {
        yield return context.Prepare();
        state.Phase = "안내"; yield return context.Wait(2f);
        state.Phase = "재생";
        actionRemaining = 180;
        yield return GuardedAction(VisualPlayScenarios.Run(context));
        context.ReleaseInput();
        state.Phase = "관찰";
        yield return context.Wait(VisualPlayBridge.ObserveSeconds);
        waiting = VisualPlayBridge.WaitAfterScenario;
        continueRequested = false;
        while ((waiting || pause) && !continueRequested)
        {
            state.Waiting = waiting; state.Paused = pause; state.Detail = "화면을 확인한 뒤 계속을 눌러 주세요";
            yield return null;
        }
        waiting = false; state.Waiting = false;
    }

    IEnumerator GuardedAction(IEnumerator action)
    {
        var actions = new Stack<IEnumerator>(); actions.Push(action);
        try
        {
            while (actions.Count > 0)
            {
                actionRemaining -= Mathf.Min(Time.unscaledDeltaTime, .1f);
                if (actionRemaining <= 0) throw new TimeoutException("동작 완료를 기다리는 시간이 초과됐어요");
                IEnumerator currentAction = actions.Peek();
                if (currentAction.MoveNext())
                {
                    if (currentAction.Current is IEnumerator nested) { actions.Push(nested); continue; }
                    yield return null;
                }
                else (actions.Pop() as IDisposable)?.Dispose();
            }
        }
        finally { while (actions.Count > 0) (actions.Pop() as IDisposable)?.Dispose(); }
    }

    void EndCurrent(string playback, string detail)
    {
        Exception cleanupError = null;
        while (stack.Count > 0)
            try { (stack.Pop() as IDisposable)?.Dispose(); } catch (Exception error) { cleanupError = error; }
        try { context?.Dispose(); }
        catch (Exception exception) { cleanupError = exception; }
        if (cleanupError != null) { stop = true; playback = "실패"; detail = "정리 실패: " + cleanupError.Message; }
        context = null;
        if (current != null)
        {
            current.playback = playback;
            if (!string.IsNullOrEmpty(detail)) current.note = string.IsNullOrEmpty(current.note) ? detail : current.note + "\n" + detail;
        }
        state.Phase = "정리"; state.Detail = detail;
        current = null; UpdateSummary();
    }

    void UpdateSummary()
    {
        var latest = records.GroupBy(record => record.planIndex).Select(group => group.Last()).ToArray();
        state.Completed = latest.Count(record => record.playback != "재생 중" && record.playback != "다시 보기");
        state.Failed = latest.Count(record => record.playback == "실패");
        state.Skipped = latest.Count(record => record.playback != "완료" && record.playback != "실패" && record.playback != "재생 중" && record.playback != "다시 보기");
        state.Checked = latest.Count(record => record.review == VisualPlayReview.Checked);
        state.Problems = latest.Count(record => record.review == VisualPlayReview.Problem);
        state.Summary = $"재생 {state.Completed}/{state.Planned} · 실패 {state.Failed} · 미실행 {state.Skipped}\n화면 확인 {state.Checked} · 문제 표시 {state.Problems} · 미확인 {latest.Count(record => record.review == VisualPlayReview.Unreviewed)}";
    }

    void Finish()
    {
        if (disposed) return;
        state.Phase = stop ? "중단" : "재생 종료"; state.Waiting = state.Paused = false;
        UpdateSummary();
        var result = records.ToArray();
        Dispose(); finished(result);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            while (stack.Count > 0)
                try { (stack.Pop() as IDisposable)?.Dispose(); } catch (Exception error) { Debug.LogWarning("[시각 확인] 코루틴 정리: " + error.Message); }
        }
        finally { try { context?.Dispose(); } finally { context = null; ReleasePanelPause(); } }
    }
    void ReleasePanelPause()
    {
        if (!panelPaused) return;
        panelPaused = false;
        if (ReferenceEquals(accountOwner, Overburst.Persistence.AccountGameplaySession.Current) && !OverburstGameMenu.IsOpen)
            OverburstTimeEffectArbiter.SetPaused(false);
        state.Detail = detailBeforePanel ?? "";
    }
}
#endif
