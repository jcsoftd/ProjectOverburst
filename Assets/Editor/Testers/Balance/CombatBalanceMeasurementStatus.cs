using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Overburst.EditorBalance.Analysis
{
    // Play 측정의 체크 → 시나리오 → 회차 → 병합 상태를 한 규칙으로 정한다(Codex 검토 10-01 P1-1).
    // 체크 문자열은 "PASS|FAIL|BLOCKED|NOT_RUN 설명" 형식이다. 우선순위: FAIL > BLOCKED > PASS > NOT_RUN.
    public static class CombatBalanceMeasurementStatus
    {
        public const string Pass = "PASS", Fail = "FAIL", Blocked = "BLOCKED", NotRun = "NOT_RUN", Partial = "PARTIAL";

        public static string OfCheck(string check)
        {
            if (string.IsNullOrEmpty(check)) return NotRun;
            if (check.StartsWith(Fail)) return Fail;
            if (check.StartsWith(Blocked)) return Blocked;
            if (check.StartsWith(Pass)) return Pass;
            return NotRun;
        }

        // 시나리오 상태: 기록된 상태(조기 종료로 넣은 FAIL·BLOCKED)와 모든 체크를 함께 본다.
        // 예전 기록처럼 status가 PASS인데 FAIL 체크가 있으면 FAIL로 다시 매긴다.
        public static string OfScenario(MeasurementScenario s)
        {
            if (s == null) return NotRun;
            var states = s.checks.Select(OfCheck).ToList();
            if (s.status == Fail || states.Contains(Fail)) return Fail;
            if (s.status == Blocked || states.Contains(Blocked)) return Blocked;
            if (states.Contains(Pass)) return Pass;
            return NotRun;
        }

        // 회차·병합 상태: 오류가 하나라도 있으면 FAIL, 시나리오 FAIL이면 FAIL, 차단이면 BLOCKED,
        // 계획보다 적게 쟀으면 PARTIAL, 나머지는 PASS. planned <= 0이면 개수 비교를 하지 않는다.
        public static string OfRun(IEnumerable<MeasurementScenario> scenarios, IEnumerable<string> errors, int planned, IEnumerable<string> plannedKeys = null)
        {
            var list = (scenarios ?? Enumerable.Empty<MeasurementScenario>()).ToList();
            var states = list.Select(OfScenario).ToList();
            if ((errors ?? Enumerable.Empty<string>()).Any()) return Fail;
            if (states.Contains(Fail)) return Fail;
            if (states.Contains(Blocked)) return Blocked;
            var keys = plannedKeys != null ? new HashSet<string>(plannedKeys) : null;
            int measured = keys != null ? list.Count(s => keys.Contains(s.key) && OfScenario(s) != NotRun) : list.Count(s => OfScenario(s) != NotRun);
            if (planned > 0 && measured < planned) return $"{Partial} {measured}/{planned}";
            return measured > 0 ? Pass : NotRun;
        }

        // 1주기 처치 확률은 한 번의 관측으로 검증할 수 없다. 모델 확률이 0과 1 사이인 시나리오들의 성공 수를
        // 기대값 Σp, 분산 Σp(1−p)의 정규 근사 95% 구간과 비교한다. 불확실 표본이 minSamples보다 적으면 NOT_RUN.
        public static string KillProbabilitySummary(IEnumerable<MeasurementScenario> scenarios, int minSamples = 8)
        {
            var uncertain = scenarios.Where(s => !float.IsNaN(s.modelHeavyKillChance) && s.measuredPrepSurvived
                && s.modelHeavyKillChance > 0f && s.modelHeavyKillChance < 1f).ToList();
            var certain = scenarios.Where(s => !float.IsNaN(s.modelHeavyKillChance) && s.measuredPrepSurvived
                && (s.modelHeavyKillChance <= 0f || s.modelHeavyKillChance >= 1f)).ToList();
            int contradictions = certain.Count(s => (s.modelHeavyKillChance >= 1f) != s.measuredHeavyKilled);
            string certainText = $"확정 예측(0%·100%) {certain.Count}건 중 모순 {contradictions}건";
            if (contradictions > 0) return $"{Fail} 1주기 처치 확률: {certainText}";
            if (uncertain.Count < minSamples)
                return $"{NotRun} 1주기 처치 확률 검증: 모델 확률이 0~100% 사이인 표본 {uncertain.Count}개(필요 {minSamples}개). 한 번의 관측으로는 확률을 검증할 수 없다. {certainText}";
            float expected = uncertain.Sum(s => s.modelHeavyKillChance);
            float sd = Mathf.Sqrt(uncertain.Sum(s => s.modelHeavyKillChance * (1f - s.modelHeavyKillChance)));
            int observed = uncertain.Count(s => s.measuredHeavyKilled);
            bool ok = Mathf.Abs(observed - expected) <= 1.96f * Mathf.Max(.5f, sd);
            return (ok ? Pass : Fail) + $" 1주기 처치 확률: 불확실 표본 {uncertain.Count}개 성공 {observed} / 기대 {expected.ToString("0.0", CultureInfo.InvariantCulture)}±{(1.96f * sd).ToString("0.0", CultureInfo.InvariantCulture)}(95%). {certainText}";
        }
    }
}
