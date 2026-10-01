using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Overburst.EditorBalance.Analysis
{
    // 시간순 이벤트 모델 자체 점검(Play 없음, Codex 검토 10-01 P1-3). 실제 튜닝 값으로 작은 전투를 만들어
    // 어둠 탄막의 발사 순서·간격·도착, 미래 피해가 미리 깎이지 않는지, 표적 사망 뒤 재표적, 잠식 예약·소비, 불 연쇄 시각을 본다.
    public static partial class CombatBalanceAnalysisModel
    {
        static Sim TestSim(int count, WeaponElement element, CombatMode mode, float hp = 1000f)
        {
            var s = new Sim { p = new PlayerBuild(), e = new EnemyView(), t = OverburstElementTuning.Current, element = element, c = new AnalysisConditions(), mode = mode };
            // 상태 만료를 멀리 둔다(0이면 첫 처리에서 바로 만료돼 중첩·예약이 사라진다).
            for (int i = 0; i < count; i++) s.targets.Add(new Target { hp = hp, max = hp, expires = 100f });
            s.alive = count; s.firstHeavyAt = 0f;
            return s;
        }

        static string Fmt(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        public static List<string> EventModelSelfChecks()
        {
            var checks = new List<string>();
            void Check(bool ok, string label, string detail) => checks.Add((ok ? "PASS " : "FAIL ") + label + " — " + detail);
            var t = OverburstElementTuning.Current;
            if (t == null) { checks.Add("BLOCKED 이벤트 모델 점검: 원소 튜닝 없음"); return checks; }
            var rule = ReadDarkRule(t);

            // 1) 발사 순서·간격·도착: 중첩 3, 에너지 0.5(대형탄 없음) → 3발이 FirstRelease + i×간격 + 비행에 닿는다.
            var s = TestSim(1, WeaponElement.Dark, CombatMode.Single);
            s.targets[0].stacks = 3;
            var cast = CollectDark(s, 2f, .5f, r => 1, 1);
            DarkBarrage(s, cast, 100f, .5f, 2f, 0f, r => 1);
            bool untouched = Mathf.Approximately(s.targets[0].hp, 1000f);
            Flush(s, rule.FirstRelease + rule.Flight(1.2f) - .01f);
            bool stillUntouched = Mathf.Approximately(s.targets[0].hp, 1000f);
            Flush(s, 10f);
            var expected = Enumerable.Range(0, 3).Select(i => rule.FirstRelease + i * rule.interval + rule.Flight(1.2f)).ToList();
            var got = s.o.firstHeavyDerivedTimes.OrderBy(v => v).ToList();
            bool timesOk = got.Count == 3 && got.Zip(expected, (a, b) => Mathf.Abs(a - b) < 1e-3f).All(v => v);
            Check(untouched && stillUntouched && timesOk, "어둠 탄막 발사 순서·간격·도착",
                $"도착 {string.Join(", ", got.Select(Fmt))}초 / 기대 {string.Join(", ", expected.Select(Fmt))}초, 도착 전 HP 유지 {untouched && stillUntouched}");

            // 2) 탄이 날아가는 동안 약공이 먼저 들어가고, 탄 피해는 도착 시각에 들어간다.
            s = TestSim(1, WeaponElement.Dark, CombatMode.Single);
            s.targets[0].stacks = 2;
            cast = CollectDark(s, 2f, .5f, r => 1, 1);
            DarkBarrage(s, cast, 100f, .5f, 2f, 0f, r => 1);
            Flush(s, .4f);
            Deal(s, s.targets[0], 10f, 0, .4f);
            float afterWeak = s.targets[0].hp;
            Flush(s, 10f);
            float shot = Outgoing(s, 100f * rule.shotFraction, PlayerAttackKind.Elemental);
            Check(Mathf.Approximately(afterWeak, 990f) && Mathf.Abs(s.targets[0].hp - (990f - 2f * shot)) < 1e-3f, "탄 비행 중 약공이 먼저 적용",
                $"약공 뒤 HP {Fmt(afterWeak)}, 도착 뒤 HP {Fmt(s.targets[0].hp)} / 기대 {Fmt(990f - 2f * shot)}");

            // 3) 원래 표적이 도착 전에 죽으면 탐색 범위 안 다른 적에게 간다.
            s = TestSim(2, WeaponElement.Dark, CombatMode.Crowd);
            s.targets[0].stacks = 2;
            cast = CollectDark(s, 2f, .5f, r => 2, 1);
            DarkBarrage(s, cast, 100f, .5f, 2f, 0f, r => 2);
            Deal(s, s.targets[0], 5000f, 0, .1f);
            Flush(s, 10f);
            Check(cast.entries.Count == 1 && Mathf.Abs(s.targets[1].hp - (1000f - 2f * shot)) < 1e-3f, "표적 사망 뒤 재표적",
                $"모은 대상 {cast.entries.Count}, 두 번째 적 HP {Fmt(s.targets[1].hp)} / 기대 {Fmt(1000f - 2f * shot)}");

            // 4) 잠식 예약: 다른 강공이 같은 중첩을 다시 모으지 못하고, 첫 탄이 닿을 때 예약분만 소비된다(예약 뒤 쌓인 중첩은 남는다).
            s = TestSim(1, WeaponElement.Dark, CombatMode.Single);
            s.targets[0].stacks = 3;
            cast = CollectDark(s, 2f, .5f, r => 1, 1);
            var second = CollectDark(s, 2f, .5f, r => 1, 2);
            s.targets[0].stacks = 4; // 예약 뒤 약공으로 한 중첩 더
            DarkBarrage(s, cast, 100f, .5f, 2f, 0f, r => 1);
            Flush(s, 10f);
            Check(second.entries.Count == 0 && s.targets[0].stacks == 1 && s.targets[0].reserved == 0, "잠식 예약·소비",
                $"두 번째 수집 대상 {second.entries.Count}, 도착 뒤 중첩 {s.targets[0].stacks}(기대 1), 예약 {s.targets[0].reserved}");

            // 5) 불 연쇄: 원점은 0.2초, 불붙은 이웃이 다시 터지는 것은 0.4초. 그 전에는 피해가 없다.
            s = TestSim(3, WeaponElement.Fire, CombatMode.Crowd);
            s.targets[1].stacks = 1;
            FireChain(s, 100f, new List<(Target, int)> { (s.targets[0], 2) }, 0f, r => 3, 1);
            bool before = s.targets.All(x => Mathf.Approximately(x.hp, 1000f));
            Flush(s, 1f);
            var fire = s.o.firstHeavyDerivedTimes.OrderBy(v => v).Select(Fmt).ToList();
            bool fireOk = before && s.o.firstHeavyDerivedTimes.Count >= 3
                && s.o.firstHeavyDerivedTimes.Count(v => Mathf.Abs(v - ElementDischargeBatch.FirePropagationDelay) < 1e-4f) == 2
                && s.o.firstHeavyDerivedTimes.Any(v => Mathf.Abs(v - 2f * ElementDischargeBatch.FirePropagationDelay) < 1e-4f);
            Check(fireOk, "불 연쇄 시각", $"피해 시각 {string.Join(", ", fire)}초, 0.2초 전 피해 없음 {before}");
            return checks;
        }
    }
}
