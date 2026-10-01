using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace Overburst.EditorBalance.Analysis
{
    // 보고서·측정 결과는 Unity 프로젝트 밖 `개인파일/코덱스산출/Balance`에 둔다(공개 Git 제외 경로).
    public static class CombatBalanceAnalysisReport
    {
        public static string OutputRoot
        {
            get
            {
                string project = Directory.GetParent(Application.dataPath).FullName;
                return Path.GetFullPath(Path.Combine(project, "..", "개인파일", "코덱스산출", "Balance", "CombatBalanceAnalysis"));
            }
        }

        public static string MeasurementRoot => Path.Combine(OutputRoot, "PlayMeasurement");

        static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented, Converters = { new StringEnumConverter() },
            FloatFormatHandling = FloatFormatHandling.String, ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };

        public static string ToJson(object value) => JsonConvert.SerializeObject(value, Json);
        public static T FromJson<T>(string text) => JsonConvert.DeserializeObject<T>(text, Json);

        static string F(float v, string fmt = "0.##") => float.IsNaN(v) ? "—" : float.IsInfinity(v) ? "∞" : v.ToString(fmt, CultureInfo.InvariantCulture);

        static string Short(string hash) => string.IsNullOrEmpty(hash) ? "없음(옛 기록)" : hash.Substring(0, Math.Min(12, hash.Length));

        // 군집 모델 시간: 새 기록은 필드, 옛 기록은 체크 문장의 "모델 N초"에서 읽는다.
        public static float CrowdModelTime(MeasurementScenario s)
        {
            if (!float.IsNaN(s.modelClearTime) && s.modelClearTime > 0f) return s.modelClearTime;
            foreach (var c in s.checks.Where(c => c.Contains("정리")))
            {
                var m = System.Text.RegularExpressions.Regex.Match(c, @"모델 ([0-9.]+)초");
                if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return v;
            }
            return float.NaN;
        }

        // 보고서에 적는 검사 의미(Codex 검토 10-01 P2-4·P2-5). 검사 이름만 보고 더 넓게 해석하지 않도록 한다.
        public static readonly string[] CheckMeanings =
        {
            "플레이어 조립·몬스터 체력: 같은 시드의 모델 값과 실제 컴포넌트 값이 같은지(±0.5).",
            "약공 판정 피해: 공격 시퀀스 순서로 콤보 타를 정하고, 각 적중의 판정 번호·계수·치명 여부로 계산한 값과 같은지. 끝까지 친 타는 판정 누락·추가도 본다.",
            "강공 첫 폭발: 실측한 확정 직전 에너지·광휘·치명 여부로 모델 식을 다시 계산해 같은지. 피해 경로 대조이며 에너지 예측 정확도 검증이 아니다.",
            "강공 후속(파생) 횟수·시각: 첫 강공 적중을 0초로 두고 모델 이벤트 시각과 실측 파생 적중 시각을 차례로 비교(±0.15초). 빛은 실측 에너지의 2/3연타 예약 시간표를 쓴다(기대 치명 피해의 사망 분기와 분리). 사망 시 관측된 앞부분만 비교하며 후속 적중이 없으면 NOT_RUN.",
            "받는 평타·강공: 실제 EnemyAbilityDefinition.ResolveDamage와 CombatHealth.TakeDamage 경로(방어·하한)를 직접 호출해 비교. 몬스터 공격 실행·타격 간격·여러 타·겹치는 공격은 검증하지 않는다. 플레이어 사망 방지가 켜져 있다.",
            "충전 중 대상 생존: 약공으로 에너지를 채우는 동안 대상 몬스터가 살아 있었는지. 플레이어가 버텼다는 뜻이 아니다.",
            "1주기 처치: 모델이 0% 또는 100%라고 한 결과와 모순되지 않는지만 본다. 확률 정확도는 아래 확률 요약(여러 표본의 성공 수)으로만 판정한다.",
            "군집 정리 시간: |모델−실측|/실측 ≤ 50%. 모델 근사가 크게 벗어났는지 알리는 경고 기준이며, 원소 순위 검증이 아니다. '보정' 역할 시나리오는 군집 입력을 맞춘 데이터라 검증 근거로 쓰지 않는다.",
        };

        // 측정 묶음: 가장 최근 측정과, 그 바로 앞에 이어진 '중단(INTERRUPTED)' 측정 중 계획 버전·지문이 같은 것.
        // 지문(코드·튜닝·자산·조건)이 다른 회차는 섞지 않는다(Codex 검토 10-01 P2-6). 완료된 옛 측정도 섞지 않는다.
        public static List<FileInfo> MeasurementChain(string root = null)
        {
            root = root ?? MeasurementRoot;
            var chain = new List<FileInfo>();
            if (!Directory.Exists(root)) return chain;
            var files = new DirectoryInfo(root).GetFiles("measurement.json", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            MeasurementReport newest = files.Count > 0 ? Read(files[0]) : null;
            for (int i = 0; i < files.Count; i++)
            {
                if (i > 0)
                {
                    var r = Read(files[i]);
                    if (r == null || r.status != "INTERRUPTED" || !SameRun(newest, r)) break;
                }
                chain.Add(files[i]);
            }
            chain.Reverse();
            return chain;
        }

        // 지문이 없는 옛 기록은 어떤 기록과도 같은 실행으로 보지 않는다.
        public static bool SameRun(MeasurementReport a, MeasurementReport b)
            => a != null && b != null && !string.IsNullOrEmpty(a.fingerprint) && a.fingerprint == b.fingerprint && a.planVersion == b.planVersion;

        static MeasurementReport Read(FileInfo f)
        {
            try { return FromJson<MeasurementReport>(File.ReadAllText(f.FullName, Encoding.UTF8)); }
            catch { return null; }
        }

        public static string StatusOf(FileInfo f) => Read(f)?.status;

        // 측정 묶음을 합친다. 같은 시나리오는 가장 최근 것을 쓰고, 시나리오·전체 상태는 공용 규칙으로 다시 매긴다.
        public static MeasurementReport LoadLatestMeasurement(string root = null, List<string> plannedKeys = null)
        {
            var files = MeasurementChain(root);
            if (files.Count == 0) return null;
            var merged = new MeasurementReport();
            var byKey = new Dictionary<string, MeasurementScenario>();
            var statuses = new List<string>();
            foreach (var f in files)
            {
                MeasurementReport r;
                try { r = FromJson<MeasurementReport>(File.ReadAllText(f.FullName, Encoding.UTF8)); }
                catch (Exception e) { merged.errors.Add(f.Directory.Name + " 읽기 실패: " + e.Message); continue; }
                if (r == null) { merged.errors.Add(f.Directory.Name + " 비어 있음"); continue; }
                statuses.Add(f.Directory.Name + "=" + r.status);
                merged.startedAt = merged.startedAt ?? r.startedAt; merged.finishedAt = r.finishedAt ?? merged.finishedAt;
                merged.unityVersion = r.unityVersion; merged.scope = r.scope;
                merged.planVersion = r.planVersion; merged.fingerprint = r.fingerprint;
                foreach (var s in r.scenarios.Where(s => s.status != null)) byKey[s.key] = s;
                // v1 기록은 정상 중단도 errors에 썼다. 그 정확한 표식만 중단 이력으로 옮기고 진짜 오류는 유지한다.
                foreach (var e in r.errors)
                    if (r.status == "INTERRUPTED" && e.StartsWith("측정 도중 Play가 외부에서 종료됨(완료 ", StringComparison.Ordinal))
                        merged.interruptions.Add(f.Directory.Name + ": " + e);
                    else merged.errors.Add(f.Directory.Name + ": " + e);
                merged.interruptions.AddRange((r.interruptions ?? new List<string>()).Select(e => f.Directory.Name + ": " + e));
            }
            var order = plannedKeys ?? CombatBalancePlayMeasurement.DefaultPlan().Select(p => p.key).ToList();
            foreach (var s in byKey.Values) s.status = CombatBalanceMeasurementStatus.OfScenario(s);
            merged.scenarios = byKey.Values.OrderBy(s => order.IndexOf(s.key) < 0 ? int.MaxValue : order.IndexOf(s.key)).ToList();
            merged.status = CombatBalanceMeasurementStatus.OfRun(merged.scenarios, merged.errors, order.Count, order);
            merged.scope = (merged.scope ?? "") + " · 측정 회차: " + string.Join(", ", statuses);
            return merged;
        }

        public static string Export(AnalysisResult result, MeasurementReport measurement, string folder = null)
        {
            folder = folder ?? Path.Combine(OutputRoot, "Reports", DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "analysis.json"), ToJson(new { result, measurement }), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "rows.csv"), Csv(result), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(folder, "보고서.md"), Markdown(result, measurement), new UTF8Encoding(false));
            return folder;
        }

        static string Csv(AnalysisResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("레벨,몬스터레벨,품질,구성,원소,체급,몬스터,전투,표본,공격,치확,치피,공속,플레이어HP,방어,기준대비,적HP,적수,약공1타,약공DPS,순환초,약공만처치,충전초,충전Phase,준비생존율,1주기처치율,처치초,강공적중,강공회수,틱비중,강공비중,파생비중,범위비중,군집약공대상,군집강공대상,첫강공처치,평타버팀,강공버팀,평타타당,강공타당,패링강공추가,환급후충전초,표본편차,문제");
            foreach (var x in r.rows)
                sb.AppendLine(string.Join(",", x.level, x.enemyLevel, AnalysisLabels.Grade(x.grade), AnalysisLabels.Preset(x.preset), AnalysisLabels.Element(x.element),
                    AnalysisLabels.Enemy(x.enemyClass), x.enemyId, AnalysisLabels.Mode(x.mode), x.samples, F(x.attack), F(x.crit), F(x.critDamage, "0.###"), F(x.attackSpeed, "0.###"),
                    F(x.playerHealth), F(x.playerArmor), F(x.referenceRatio, "0.###"), F(x.enemyHealth), F(x.enemyCount), F(x.weakHit), F(x.weakDps), F(x.cycleDuration, "0.###"),
                    F(x.weakOnlyKillTime), F(x.chargeTime), F(x.chargePhases, "0.#"), F(x.prepSurvivalRate, "0.##"), F(x.heavyKillRate, "0.##"), F(x.killTime), F(x.heavyDirect),
                    F(x.heavyCount), F(x.dotShare, "0.#"), F(x.heavyShare, "0.#"), F(x.derivedShare, "0.#"), F(x.aoeShare, "0.#"), F(x.crowdWeakTargets, "0.#"), F(x.crowdHeavyTargets, "0.#"),
                    F(x.crowdKillsFirstHeavy, "0.#"), x.normalSurvivable, x.strongSurvivable, F(x.normalHitDamage), F(x.strongHitDamage), F(x.parryHeavyBonus), F(x.parryRefundChargeTime),
                    F(x.spread, "0.##"), "\"" + string.Join(" ", x.flags.Select(CombatBalanceAnalysisRunner.RuleLabel)) + "\""));
            return sb.ToString();
        }

        public static string Markdown(AnalysisResult r, MeasurementReport m)
        {
            var c = r.conditions; var sb = new StringBuilder();
            sb.AppendLine("# 전투 밸런스 분석 보고서").AppendLine();
            sb.AppendLine($"생성 {r.createdAt}, Unity {r.unityVersion}. 무기 {r.weapon}. 모든 수치는 **계산 모델 예상치**다. Play 측정치는 아래 별도 절에 구분한다.").AppendLine();
            sb.AppendLine("## 조건").AppendLine();
            sb.AppendLine($"- 레벨 구간 {string.Join(", ", c.levelBuckets.Select(AnalysisLabels.Bucket))}, 구간 내 기준점 {c.levelPoint}, 몬스터 레벨 차 {c.monsterLevelOffset:+0;-0;0}");
            sb.AppendLine($"- 품질 {string.Join(", ", c.grades.Select(AnalysisLabels.Grade))}, 구성 {string.Join(", ", c.presets.Select(AnalysisLabels.Preset))}, 표준 추첨 표본 {c.seedCount}개(시드 {c.seedBase}~)");
            sb.AppendLine($"- 원소 {string.Join(", ", c.elements.Select(AnalysisLabels.Element))}, 체급 {string.Join(", ", c.enemies.Select(AnalysisLabels.Enemy))}, 전투 {string.Join(", ", c.modes.Select(AnalysisLabels.Mode))}");
            sb.AppendLine($"- 군집 {c.crowdCount}마리: 약공 판정당 {F(c.crowdWeakTargets)}마리, 완충 강공 {F(c.crowdHeavyTargets)}마리(반경² 비례), 연쇄 밀착 {F(c.packingDensity)}/㎡ — 10-01 Play 측정으로 맞춘 입력. 전투 자세 {(c.combatStance ? "켬(+10%p)" : "끔")}, 빛 준비 목표 {(c.lightTriple ? "200·광휘 100(3연타)" : "100(2연타)")}");
            sb.AppendLine("- 제외: 물약·지도 카드·지도 옵션 버프, 입력 빗나감·넉백으로 인한 빈 타격, 적 이동·회피, 방패 방어 판정").AppendLine();
            sb.AppendLine("## 자체 점검").AppendLine();
            foreach (var s in r.selfChecks) sb.AppendLine("- " + s);
            sb.AppendLine();
            sb.AppendLine("## 레벨 구간별 요약 (단일, 표준 추첨 중앙값)").AppendLine();
            sb.AppendLine("| 레벨 | 품질 | 체급 | 원소 | 공격 | 적 HP | 충전 초 | 충전 중 대상 생존 | 1주기 처치 | 처치 초 | 평타/강공 버팀 | 문제 |");
            sb.AppendLine("|---|---|---|---|---:|---:|---:|---:|---:|---:|---|---|");
            foreach (var x in r.rows.Where(x => x.mode == CombatMode.Single).OrderBy(x => x.level).ThenBy(x => x.grade).ThenBy(x => x.enemyClass).ThenBy(x => x.element))
                sb.AppendLine($"| {x.level} | {AnalysisLabels.Grade(x.grade)} | {AnalysisLabels.Enemy(x.enemyClass)} | {AnalysisLabels.Element(x.element)} | {F(x.attack)} | {F(x.enemyHealth)} | {F(x.chargeTime)} | {F(x.prepSurvivalRate * 100, "0")}% | {F(x.heavyKillRate * 100, "0")}% | {F(x.killTime)} | {x.normalSurvivable}/{x.strongSurvivable} | {string.Join(" ", x.flags.Select(CombatBalanceAnalysisRunner.RuleLabel))} |");
            sb.AppendLine();
            var crowd = r.rows.Where(x => x.mode == CombatMode.Crowd).ToList();
            if (crowd.Count > 0)
            {
                sb.AppendLine($"## 군집 전투 ({c.crowdCount}마리)").AppendLine();
                sb.AppendLine("| 레벨 | 품질 | 체급 | 원소 | 정리 초 | 첫 강공 처치 | 약공/강공 대상 | 강공 % | 파생 % | 틱 % | 문제 |");
                sb.AppendLine("|---|---|---|---|---:|---:|---|---:|---:|---:|---|");
                foreach (var x in crowd.OrderBy(x => x.level).ThenBy(x => x.grade).ThenBy(x => x.enemyClass).ThenBy(x => x.element))
                    sb.AppendLine($"| {x.level} | {AnalysisLabels.Grade(x.grade)} | {AnalysisLabels.Enemy(x.enemyClass)} | {AnalysisLabels.Element(x.element)} | {F(x.killTime)} | {F(x.crowdKillsFirstHeavy, "0.#")} | {F(x.crowdWeakTargets, "0.#")}/{F(x.crowdHeavyTargets, "0.#")} | {F(x.heavyShare, "0")} | {F(x.derivedShare, "0")} | {F(x.dotShare, "0")} | {string.Join(" ", x.flags.Select(CombatBalanceAnalysisRunner.RuleLabel))} |");
                sb.AppendLine();
            }
            sb.AppendLine("## 발견 (재현 조건 · 원인 코드 · 영향 · 수정 후보)").AppendLine();
            sb.AppendLine("수정 후보는 제안이다. 이 도구와 이번 작업은 확정 밸런스·제품 자산을 바꾸지 않았다.").AppendLine();
            foreach (var f in r.findings)
            {
                sb.AppendLine($"### {f.id} [{f.severity}] {f.title}").AppendLine();
                sb.AppendLine($"- 분류: {f.category}");
                sb.AppendLine($"- 재현 조건: {f.condition}");
                sb.AppendLine($"- 원인 코드: {f.cause}");
                sb.AppendLine($"- 영향: {f.impact}");
                sb.AppendLine($"- 수정 후보: {f.proposal}");
                sb.AppendLine($"- 근거: {f.evidence}").AppendLine();
            }
            sb.AppendLine("## 계산 모델 예상치 vs 실제 Play 측정치").AppendLine();
            if (m == null) sb.AppendLine("Play 측정 결과 없음(NOT_RUN).");
            else
            {
                sb.AppendLine($"측정 상태 {m.status}, {m.startedAt} ~ {m.finishedAt}, Unity {m.unityVersion}. 계획 {m.planVersion ?? "—"}, 지문 {Short(m.fingerprint)}. 범위: {m.scope}").AppendLine();
                sb.AppendLine("검사가 실제로 확인하는 것:").AppendLine();
                foreach (var line in CheckMeanings) sb.AppendLine("- " + line);
                sb.AppendLine();
                sb.AppendLine("| 시나리오 | 역할 | 판정 | 공격 모델/실측 | 적 HP | 약공 1타 | 충전 초 | 강공 적중 | 충전 중 대상 생존 | 1주기 처치 | 받는 평타/강공 | 비고 |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
                foreach (var s in m.scenarios)
                    sb.AppendLine($"| {s.key} | {s.role ?? "—"} | {s.status} | {F(s.modelAttack)}/{F(s.measuredAttack)} | {F(s.modelEnemyHealth)}/{F(s.measuredEnemyHealth)} | {F(s.modelWeakNormalHit)}/{F(s.measuredWeakNormalHit)} | {F(s.modelChargeTime)}/{F(s.measuredChargeTime)} | {F(s.modelHeavyDirect)}/{F(s.measuredHeavyDirect)} | {(s.modelPrepSurvived ? "생존" : "사망")}/{(s.measuredPrepSurvived ? "생존" : "사망")} | {(s.modelHeavyKilled ? "처치" : "실패")}/{(s.measuredHeavyKilled ? "처치" : "실패")} | {F(s.modelIncomingNormal)}/{F(s.measuredIncomingNormal)} · {F(s.modelIncomingStrong)}/{F(s.measuredIncomingStrong)} | {s.note} |");
                sb.AppendLine();
                sb.AppendLine("- " + CombatBalanceMeasurementStatus.KillProbabilitySummary(m.scenarios)).AppendLine();
                var crowdRuns = m.scenarios.Where(s => s.key.Contains("군집")).Select(s => (s, model: CrowdModelTime(s))).Where(x => !float.IsNaN(x.model)).ToList();
                if (crowdRuns.Count > 0)
                {
                    sb.AppendLine("### 군집 정리 시간 (이 회차 원본에서 생성)").AppendLine();
                    sb.AppendLine("| 시나리오 | 역할 | 모델 | 실측 | 모델/실측 | |모델−실측|/실측 |");
                    sb.AppendLine("|---|---|---:|---:|---:|---:|");
                    foreach (var (s, model) in crowdRuns.OrderBy(x => x.s.measuredElapsed))
                        sb.AppendLine($"| {s.key} | {s.role ?? "—"} | {F(model)}초 | {F(s.measuredElapsed)}초 | {F(model / Mathf.Max(.01f, s.measuredElapsed), "0.00")}배 | {F(Mathf.Abs(model - s.measuredElapsed) / Mathf.Max(.01f, s.measuredElapsed) * 100f, "0")}% |");
                    foreach (var group in crowdRuns.GroupBy(x => x.s.role ?? "—"))
                    {
                        string Order(Func<(MeasurementScenario s, float model), float> key) => string.Join(" < ", group.OrderBy(key).Select(x => AnalysisLabels.Element(x.s.element)));
                        sb.AppendLine().AppendLine($"- {group.Key}: 실측 빠른 순 {Order(x => x.s.measuredElapsed)} / 모델 빠른 순 {Order(x => x.model)}"
                            + (Order(x => x.s.measuredElapsed) == Order(x => x.model) ? " (순위 일치)" : " (순위 불일치)"));
                    }
                    sb.AppendLine();
                }
                foreach (var s in m.scenarios) { sb.AppendLine($"- {s.key}: " + string.Join("; ", s.checks)); }
                if (m.interruptions.Count > 0) sb.AppendLine().AppendLine("중단 이력: " + string.Join(" | ", m.interruptions));
                if (m.errors.Count > 0) sb.AppendLine().AppendLine("오류: " + string.Join(" | ", m.errors));
            }
            return sb.ToString();
        }
    }
}
