using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Overburst.EditorBalance.Analysis
{
    // OVERBURST → Balance → 전투 밸런스 분석. 조건 선택 · 결과 비교 · 문제 조합 필터 · 보고서 내보내기.
    // 편집 기능은 없다. 수치 편집은 기존 '무기·장비·몬스터 밸런스 테이블' 창이 소유한다.
    public sealed class CombatBalanceAnalysisWindow : EditorWindow
    {
        enum Tab { Rows, Elements, Findings, Measurement, Checks }
        AnalysisConditions conditions = new AnalysisConditions();
        AnalysisResult result;
        MeasurementReport measurement;
        readonly List<ResultRow> visible = new List<ResultRow>();
        ResultRow selected, baseline;
        Tab tab;
        string query = "", flagFilter = "전체", modeFilter = "전체", sortKey = "레벨";
        bool onlyProblems, descending;
        MultiColumnListView table, measureTable;
        VisualElement body, detail;
        Label status, summary;
        List<WeaponItemData> weapons = new List<WeaponItemData>();
        public string LastExportFolder { get; private set; }
        public int VisibleRowCount => visible.Count;
        public AnalysisResult Result => result;

        [MenuItem("OVERBURST/Balance/전투 밸런스 분석")]
        public static CombatBalanceAnalysisWindow Open()
        {
            var w = GetWindow<CombatBalanceAnalysisWindow>();
            w.titleContent = new GUIContent("전투 밸런스 분석"); w.minSize = new Vector2(1080, 680); w.Show(); return w;
        }

        public void CreateGUI()
        {
            var root = rootVisualElement; root.Clear();
            root.style.paddingLeft = root.style.paddingRight = 10; root.style.paddingTop = root.style.paddingBottom = 8;
            var title = new Label("OVERBURST  /  전투 밸런스 분석");
            title.style.fontSize = 20; title.style.unityFontStyleAndWeight = FontStyle.Bold; title.style.marginBottom = 4; root.Add(title);
            root.Add(Wrap(new Label("실제 계산 코드(CombatBalanceFormulas·WeaponStatCalculator·GearQuality·EnemyAbilityDefinition)로 조합별 처치 시간·버티는 피격 수·강공 충전·지속/범위 기여를 계산합니다. 표의 값은 계산 모델 예상치이며 Play 측정치는 '예상 vs 실측' 탭에 따로 보입니다.")));
            var bar = new Toolbar();
            bar.Add(new ToolbarButton(RunAnalysis) { text = "분석 실행" });
            bar.Add(new ToolbarButton(RunMeasurement) { text = "Play 측정 실행" });
            bar.Add(new ToolbarButton(LoadMeasurement) { text = "측정 결과 불러오기" });
            bar.Add(new ToolbarButton(() => Export()) { text = "보고서 내보내기" });
            bar.Add(new ToolbarButton(OpenFolder) { text = "보고서 폴더 열기" });
            bar.Add(new VisualElement { style = { flexGrow = 1 } });
            bar.Add(new ToolbarButton(() => { baseline = selected; RefreshDetail(); }) { text = "선택 행을 비교 기준으로" });
            bar.Add(new ToolbarButton(() => { baseline = null; RefreshDetail(); }) { text = "비교 해제" });
            root.Add(bar);
            root.Add(BuildConditions());
            var tabs = new Toolbar();
            foreach (Tab t in Enum.GetValues(typeof(Tab)))
            {
                Tab captured = t;
                tabs.Add(new ToolbarButton(() => { tab = captured; RefreshBody(); }) { text = TabName(t) });
            }
            root.Add(tabs);
            summary = Wrap(new Label()); summary.style.marginTop = 4; summary.style.marginBottom = 4; root.Add(summary);
            var split = new TwoPaneSplitView(1, 360, TwoPaneSplitViewOrientation.Horizontal);
            body = new VisualElement { style = { flexGrow = 1, minWidth = 520 } };
            detail = new ScrollView(ScrollViewMode.Vertical) { style = { minWidth = 300, paddingLeft = 8 } };
            split.Add(body); split.Add(detail); split.style.flexGrow = 1; split.style.minHeight = 300;
            root.Add(split);
            status = Wrap(new Label("조건을 고르고 '분석 실행'을 누르세요.")); status.style.minHeight = 36; root.Add(status);
            measurement = SafeLoadMeasurement();
            RefreshBody();
        }

        static string TabName(Tab t) => t == Tab.Rows ? "결과 표" : t == Tab.Elements ? "원소·범위 기여" : t == Tab.Findings ? "발견" : t == Tab.Measurement ? "예상 vs 실측" : "자체 점검";
        static T Wrap<T>(T label) where T : VisualElement { label.style.whiteSpace = WhiteSpace.Normal; return label; }
        static string F(float v, string fmt = "0.##") => float.IsNaN(v) ? "—" : float.IsInfinity(v) ? "∞" : v.ToString(fmt, CultureInfo.InvariantCulture);

        VisualElement BuildConditions()
        {
            var fold = new Foldout { text = "조건", value = true };
            VisualElement Row(string label)
            {
                var r = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = UnityEngine.UIElements.Wrap.Wrap, alignItems = Align.Center, marginBottom = 2 } };
                var l = new Label(label) { style = { width = 88, unityFontStyleAndWeight = FontStyle.Bold } }; r.Add(l); fold.Add(r); return r;
            }
            void Toggles<T>(VisualElement row, IEnumerable<T> values, List<T> target, Func<T, string> name)
            {
                foreach (var v in values)
                {
                    T captured = v;
                    var t = new Toggle(name(v)) { value = target.Contains(v) };
                    t.style.marginRight = 8; t.labelElement.style.minWidth = 0;
                    t.RegisterValueChangedCallback(e => { if (e.newValue) { if (!target.Contains(captured)) target.Add(captured); } else target.Remove(captured); });
                    row.Add(t);
                }
            }
            Toggles(Row("레벨 구간"), Enumerable.Range(0, 10), conditions.levelBuckets, AnalysisLabels.Bucket);
            var levelRow = Row("레벨 기준");
            var point = new EnumField("구간 내 기준점", conditions.levelPoint); point.style.width = 240;
            point.RegisterValueChangedCallback(e => conditions.levelPoint = (LevelPoint)e.newValue); levelRow.Add(point);
            var offset = new IntegerField("몬스터 레벨 차") { value = conditions.monsterLevelOffset, isDelayed = true }; offset.style.width = 200;
            offset.RegisterValueChangedCallback(e => { conditions.monsterLevelOffset = Mathf.Clamp(e.newValue, -20, 20); offset.SetValueWithoutNotify(conditions.monsterLevelOffset); }); levelRow.Add(offset);
            Toggles(Row("품질"), Enum.GetValues(typeof(ItemGrade)).Cast<ItemGrade>(), conditions.grades, AnalysisLabels.Grade);
            Toggles(Row("장비 구성"), Enum.GetValues(typeof(GearPreset)).Cast<GearPreset>(), conditions.presets, AnalysisLabels.Preset);
            Toggles(Row("원소"), new[] { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light, WeaponElement.None }, conditions.elements, AnalysisLabels.Element);
            Toggles(Row("몬스터 체급"), Enum.GetValues(typeof(EnemyClass)).Cast<EnemyClass>(), conditions.enemies, AnalysisLabels.Enemy);
            Toggles(Row("전투"), Enum.GetValues(typeof(CombatMode)).Cast<CombatMode>(), conditions.modes, AnalysisLabels.Mode);
            var more = Row("세부");
            weapons = CombatBalanceAnalysisModel.Catalog.Load().weapons;
            var names = weapons.Select(w => w.name).ToList();
            int index = Mathf.Max(0, weapons.FindIndex(w => AssetDatabase.GetAssetPath(w) == conditions.weaponPath));
            if (names.Count > 0)
            {
                var weapon = new DropdownField("무기", names, index); weapon.style.width = 330;
                weapon.RegisterValueChangedCallback(_ => conditions.weaponPath = AssetDatabase.GetAssetPath(weapons[weapon.index])); more.Add(weapon);
            }
            IntegerField Int(string label, int value, int min, int max, Action<int> set)
            {
                var f = new IntegerField(label) { value = value, isDelayed = true }; f.style.width = 170;
                f.RegisterValueChangedCallback(e => { int v = Mathf.Clamp(e.newValue, min, max); f.SetValueWithoutNotify(v); set(v); }); more.Add(f); return f;
            }
            Int("표본 수", conditions.seedCount, 1, 64, v => conditions.seedCount = v);
            Int("시작 시드", conditions.seedBase, 0, 1000000, v => conditions.seedBase = v);
            Int("군집 마리 수", conditions.crowdCount, 2, 150, v => conditions.crowdCount = v);
            var weakTargets = new FloatField("약공 대상 수") { value = conditions.crowdWeakTargets, isDelayed = true }; weakTargets.style.width = 170;
            weakTargets.RegisterValueChangedCallback(e => { conditions.crowdWeakTargets = Mathf.Clamp(e.newValue, 1f, 20f); weakTargets.SetValueWithoutNotify(conditions.crowdWeakTargets); }); more.Add(weakTargets);
            var heavyTargets = new FloatField("강공 대상 수") { value = conditions.crowdHeavyTargets, isDelayed = true }; heavyTargets.style.width = 170;
            heavyTargets.RegisterValueChangedCallback(e => { conditions.crowdHeavyTargets = Mathf.Clamp(e.newValue, 1f, 150f); heavyTargets.SetValueWithoutNotify(conditions.crowdHeavyTargets); }); more.Add(heavyTargets);
            var packing = new FloatField("연쇄 밀착 밀도") { value = conditions.packingDensity, isDelayed = true }; packing.style.width = 170;
            packing.RegisterValueChangedCallback(e => { conditions.packingDensity = Mathf.Clamp(e.newValue, .1f, 4f); packing.SetValueWithoutNotify(conditions.packingDensity); }); more.Add(packing);
            var stance = new Toggle("전투 자세(+10%p)") { value = conditions.combatStance }; stance.RegisterValueChangedCallback(e => conditions.combatStance = e.newValue); more.Add(stance);
            var triple = new Toggle("빛 3연타까지 준비") { value = conditions.lightTriple }; triple.RegisterValueChangedCallback(e => conditions.lightTriple = e.newValue); more.Add(triple);
            return fold;
        }

        public void SetConditions(AnalysisConditions value) { conditions = value.Clone(); }

        public void RunAnalysis()
        {
            try
            {
                result = CombatBalanceAnalysisRunner.Run(conditions, (p, label) => EditorUtility.DisplayProgressBar("전투 밸런스 분석", label, p));
                selected = baseline = null;
                int fails = result.selfChecks.Count(s => s.StartsWith("FAIL"));
                SetStatus($"분석 완료: {result.rows.Count}개 조합, 문제 조합 {result.rows.Count(r => r.HasFlags)}개, 발견 {result.findings.Count}건, 자체 점검 실패 {fails}건.");
            }
            catch (Exception e) { SetStatus("분석 실패: " + e.Message); Debug.LogException(e); }
            finally { EditorUtility.ClearProgressBar(); }
            RefreshBody();
        }

        void RunMeasurement()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { SetStatus("Play 중에는 측정을 시작할 수 없습니다."); return; }
            if (!EditorUtility.DisplayDialog("Play 측정", "격리 계정으로 Play에 들어가 대표 조합 21개를 측정합니다(약 3분). 공유 Editor를 이 작업이 쓰는 동안 다른 작업의 Play·컴파일과 겹치지 않아야 합니다. 겹칠 것 같으면 '측정 예약' 메뉴를 쓰세요.", "시작", "취소")) return;
            try { SetStatus("측정 시작: " + CombatBalancePlayMeasurement.Run()); }
            catch (Exception e) { SetStatus("측정 시작 실패: " + e.Message); }
        }

        MeasurementReport SafeLoadMeasurement()
        {
            try { return CombatBalanceAnalysisReport.LoadLatestMeasurement(); }
            catch (Exception e) { Debug.LogWarning("측정 결과 읽기 실패: " + e.Message); return null; }
        }

        void LoadMeasurement()
        {
            measurement = SafeLoadMeasurement();
            SetStatus(measurement == null ? "측정 결과가 없습니다(NOT_RUN)." : $"측정 결과 {measurement.status}: 시나리오 {measurement.scenarios.Count}개 ({measurement.finishedAt}).");
            RefreshBody();
        }

        public string Export()
        {
            if (result == null) { SetStatus("먼저 분석을 실행하세요."); return null; }
            LastExportFolder = CombatBalanceAnalysisReport.Export(result, measurement);
            SetStatus("보고서 저장: " + LastExportFolder);
            return LastExportFolder;
        }

        void OpenFolder()
        {
            string folder = LastExportFolder ?? CombatBalanceAnalysisReport.OutputRoot;
            Directory.CreateDirectory(folder); EditorUtility.RevealInFinder(folder);
        }

        void SetStatus(string text) { if (status != null) status.text = text; }

        // ───────────────── 본문
        void RefreshBody()
        {
            if (body == null) return;
            body.Clear(); table = null; measureTable = null;
            if (tab == Tab.Rows) BuildRowsTab();
            else if (tab == Tab.Elements) BuildElementsTab();
            else if (tab == Tab.Findings) BuildFindingsTab();
            else if (tab == Tab.Measurement) BuildMeasurementTab();
            else BuildChecksTab();
            RefreshDetail();
        }

        static readonly (string title, float width, Func<ResultRow, string> value, Func<ResultRow, float> sort)[] Columns =
        {
            ("레벨", 44, r => r.level.ToString(), r => r.level),
            ("품질", 44, r => AnalysisLabels.Grade(r.grade), r => (int)r.grade),
            ("구성", 70, r => AnalysisLabels.Preset(r.preset), r => (int)r.preset),
            ("원소", 48, r => AnalysisLabels.Element(r.element), r => (int)r.element),
            ("체급", 44, r => AnalysisLabels.Enemy(r.enemyClass), r => (int)r.enemyClass),
            ("전투", 44, r => AnalysisLabels.Mode(r.mode), r => (int)r.mode),
            ("공격", 56, r => F(r.attack, "0"), r => r.attack),
            ("적 HP", 64, r => F(r.enemyHealth, "0"), r => r.enemyHealth),
            ("충전 초", 58, r => F(r.chargeTime), r => r.chargeTime),
            ("준비 생존", 62, r => r.mode == CombatMode.Single ? F(r.prepSurvivalRate * 100, "0") + "%" : "—", r => r.prepSurvivalRate),
            ("1주기", 50, r => r.mode == CombatMode.Single ? F(r.heavyKillRate * 100, "0") + "%" : "—", r => r.heavyKillRate),
            ("처치 초", 58, r => F(r.killTime), r => r.killTime),
            ("약공만", 58, r => F(r.weakOnlyKillTime), r => r.weakOnlyKillTime),
            ("틱 %", 46, r => F(r.dotShare, "0.#"), r => r.dotShare),
            ("범위 %", 50, r => F(r.aoeShare, "0"), r => r.aoeShare),
            ("버팀 평/강", 70, r => $"{r.normalSurvivable}/{r.strongSurvivable}", r => r.normalSurvivable),
            ("문제", 220, r => string.Join(", ", r.flags.Select(CombatBalanceAnalysisRunner.RuleLabel)), r => r.flags.Count),
        };

        void BuildRowsTab()
        {
            var filters = new Toolbar();
            var search = new ToolbarSearchField { value = query }; search.style.width = 220;
            search.RegisterValueChangedCallback(e => { query = e.newValue ?? ""; ApplyFilter(); }); filters.Add(search);
            var problems = new ToolbarToggle { text = "문제 조합만", value = onlyProblems };
            problems.RegisterValueChangedCallback(e => { onlyProblems = e.newValue; ApplyFilter(); }); filters.Add(problems);
            var flagChoices = new List<string> { "전체" }; flagChoices.AddRange(CombatBalanceAnalysisRunner.Rules.Select(r => r.label));
            var flag = new DropdownField(flagChoices, Mathf.Max(0, flagChoices.IndexOf(flagFilter))); flag.style.width = 150;
            flag.RegisterValueChangedCallback(e => { flagFilter = e.newValue; ApplyFilter(); }); filters.Add(flag);
            var mode = new DropdownField(new List<string> { "전체", "단일", "군집" }, 0); mode.value = modeFilter; mode.style.width = 80;
            mode.RegisterValueChangedCallback(e => { modeFilter = e.newValue; ApplyFilter(); }); filters.Add(mode);
            var sortChoices = Columns.Select(c => c.title).ToList();
            var sort = new DropdownField("정렬", sortChoices, Mathf.Max(0, sortChoices.IndexOf(sortKey))); sort.style.width = 170;
            sort.RegisterValueChangedCallback(e => { sortKey = e.newValue; ApplyFilter(); }); filters.Add(sort);
            var desc = new ToolbarToggle { text = "내림차순", value = descending };
            desc.RegisterValueChangedCallback(e => { descending = e.newValue; ApplyFilter(); }); filters.Add(desc);
            body.Add(filters);
            table = new MultiColumnListView { fixedItemHeight = 20, showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly, selectionType = SelectionType.Single };
            table.style.flexGrow = 1;
            foreach (var col in Columns)
            {
                var c = col;
                table.columns.Add(new Column
                {
                    title = c.title, width = c.width, makeCell = () => new Label { style = { unityTextAlign = TextAnchor.MiddleLeft, overflow = Overflow.Hidden } },
                    bindCell = (e, i) =>
                    {
                        var row = visible[i]; var label = (Label)e; label.text = c.value(row);
                        label.style.color = c.title == "문제" && row.HasFlags ? new StyleColor(new Color(.95f, .55f, .35f)) : new StyleColor(StyleKeyword.Null);
                    }
                });
            }
            table.selectionChanged += items => { selected = items.OfType<ResultRow>().FirstOrDefault(); RefreshDetail(); };
            body.Add(table);
            ApplyFilter();
        }

        public void SetFilter(string search, bool problems, string flag = "전체", string mode = "전체")
        { query = search ?? ""; onlyProblems = problems; flagFilter = flag; modeFilter = mode; tab = Tab.Rows; RefreshBody(); }

        public void Select(ResultRow row) { selected = row; RefreshDetail(); }
        public void SetBaseline(ResultRow row) { baseline = row; RefreshDetail(); }

        void ApplyFilter()
        {
            visible.Clear();
            if (result != null)
            {
                IEnumerable<ResultRow> rows = result.rows;
                if (onlyProblems) rows = rows.Where(r => r.HasFlags);
                if (flagFilter != "전체") { string code = CombatBalanceAnalysisRunner.Rules.First(r => r.label == flagFilter).code; rows = rows.Where(r => r.flags.Contains(code)); }
                if (modeFilter == "단일") rows = rows.Where(r => r.mode == CombatMode.Single); else if (modeFilter == "군집") rows = rows.Where(r => r.mode == CombatMode.Crowd);
                if (!string.IsNullOrWhiteSpace(query)) rows = rows.Where(r => (r.Title + " " + r.FlagText + " " + string.Join(" ", r.flags.Select(CombatBalanceAnalysisRunner.RuleLabel))).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
                var column = Columns.First(c => c.title == sortKey);
                rows = descending ? rows.OrderByDescending(r => Sortable(column.sort(r))) : rows.OrderBy(r => Sortable(column.sort(r)));
                visible.AddRange(rows);
            }
            if (table != null) { table.itemsSource = visible; table.RefreshItems(); }
            UpdateSummary();
        }

        static float Sortable(float v) => float.IsNaN(v) ? float.MaxValue : v;

        void UpdateSummary()
        {
            if (summary == null) return;
            if (result == null) { summary.text = "분석 결과 없음."; return; }
            var counts = result.rows.SelectMany(r => r.flags).GroupBy(f => f).OrderByDescending(g => g.Count()).Select(g => CombatBalanceAnalysisRunner.RuleLabel(g.Key) + " " + g.Count());
            summary.text = $"{result.createdAt} · {result.weapon} · 조합 {result.rows.Count}개 중 표시 {visible.Count}개 · 문제: {string.Join(", ", counts)}";
        }

        void BuildElementsTab()
        {
            if (result == null) { body.Add(new Label("분석 결과 없음.")); return; }
            var scroll = new ScrollView(); body.Add(scroll);
            foreach (var mode in new[] { CombatMode.Single, CombatMode.Crowd })
            {
                var rows = result.rows.Where(r => r.mode == mode).ToList();
                if (rows.Count == 0) continue;
                scroll.Add(Header(mode == CombatMode.Single ? "단일 대상: 원소별 중앙값" : $"군집 {result.conditions.crowdCount}마리: 원소별 중앙값"));
                var grid = Grid(new[] { "원소", "처치 초", "충전 초", "강공 %", "파생 %", "틱 %", "범위 %", "문제 조합" }, 90);
                foreach (var g in rows.GroupBy(r => r.element).OrderBy(g => g.Key))
                    GridRow(grid, new[] { AnalysisLabels.Element(g.Key), F(Med(g.Select(r => r.killTime))), F(Med(g.Select(r => r.chargeTime))), F(Med(g.Select(r => r.heavyShare)), "0"),
                        F(Med(g.Select(r => r.derivedShare)), "0"), F(Med(g.Select(r => r.dotShare)), "0.#"), F(Med(g.Select(r => r.aoeShare)), "0"), g.Count(r => r.HasFlags) + "/" + g.Count() }, 90);
                scroll.Add(grid);
                scroll.Add(Header("레벨 구간 × 원소 처치 초(" + AnalysisLabels.Mode(mode) + ", 중형 우선)"));
                var cls = rows.Any(r => r.enemyClass == EnemyClass.Medium) ? EnemyClass.Medium : rows[0].enemyClass;
                var elements = rows.Select(r => r.element).Distinct().OrderBy(e => e).ToList();
                var grid2 = Grid(new[] { "레벨" }.Concat(elements.Select(AnalysisLabels.Element)).ToArray(), 80);
                foreach (var lv in rows.Select(r => r.level).Distinct().OrderBy(l => l))
                    GridRow(grid2, new[] { "Lv" + lv }.Concat(elements.Select(e => F(Med(rows.Where(r => r.level == lv && r.element == e && r.enemyClass == cls).Select(r => r.killTime))))).ToArray(), 80);
                scroll.Add(grid2);
            }
        }

        static float Med(IEnumerable<float> v)
        {
            var list = v.Where(x => !float.IsNaN(x) && !float.IsInfinity(x)).OrderBy(x => x).ToList();
            return list.Count == 0 ? float.NaN : list[list.Count / 2];
        }

        static Label Header(string text) { var l = new Label(text); l.style.unityFontStyleAndWeight = FontStyle.Bold; l.style.marginTop = 8; l.style.marginBottom = 2; return l; }
        static VisualElement Grid(string[] headers, float width)
        {
            var g = new VisualElement(); GridRow(g, headers, width, true); return g;
        }
        static void GridRow(VisualElement grid, string[] cells, float width, bool bold = false)
        {
            var r = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            foreach (var c in cells) { var l = new Label(c) { style = { width = width } }; if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold; r.Add(l); }
            grid.Add(r);
        }

        void BuildFindingsTab()
        {
            if (result == null) { body.Add(new Label("분석 결과 없음.")); return; }
            var scroll = new ScrollView(); body.Add(scroll);
            foreach (var f in result.findings)
            {
                var fold = new Foldout { text = $"{f.id} [{f.severity}] {f.title}", value = f.severity == "높음" };
                foreach (var (k, v) in new[] { ("분류", f.category), ("재현 조건", f.condition), ("원인 코드", f.cause), ("영향", f.impact), ("수정 후보", f.proposal), ("근거", f.evidence) })
                    fold.Add(Wrap(new Label($"{k}: {v}") { style = { marginBottom = 2 } }));
                scroll.Add(fold);
            }
        }

        void BuildMeasurementTab()
        {
            if (measurement == null) { body.Add(Wrap(new Label("Play 측정 결과가 없습니다(NOT_RUN). '측정 결과 불러오기' 또는 'Play 측정 실행'을 사용하세요."))); return; }
            body.Add(Wrap(new Label($"측정 {measurement.status} · {measurement.startedAt} ~ {measurement.finishedAt} · {measurement.scope}")));
            measureTable = new MultiColumnListView { fixedItemHeight = 20, selectionType = SelectionType.Single, itemsSource = measurement.scenarios };
            measureTable.style.flexGrow = 1;
            (string, float, Func<MeasurementScenario, string>)[] cols =
            {
                ("시나리오", 230, s => s.key), ("판정", 60, s => s.status), ("공격", 90, s => F(s.modelAttack) + "/" + F(s.measuredAttack)),
                ("적 HP", 100, s => F(s.modelEnemyHealth) + "/" + F(s.measuredEnemyHealth)), ("충전 초", 90, s => F(s.modelChargeTime) + "/" + F(s.measuredChargeTime)),
                ("강공", 100, s => F(s.modelHeavyDirect) + "/" + F(s.measuredHeavyDirect)), ("받는 평/강", 130, s => $"{F(s.modelIncomingNormal)}/{F(s.measuredIncomingNormal)} · {F(s.modelIncomingStrong)}/{F(s.measuredIncomingStrong)}"),
                ("점검 실패", 70, s => s.checks.Count(c => c.StartsWith("FAIL")).ToString()),
            };
            foreach (var (t, w, v) in cols)
            {
                var get = v;
                measureTable.columns.Add(new Column { title = t, width = w, makeCell = () => new Label(), bindCell = (e, i) => ((Label)e).text = get(measurement.scenarios[i]) });
            }
            measureTable.selectionChanged += items =>
            {
                var s = items.OfType<MeasurementScenario>().FirstOrDefault(); detail.Clear();
                if (s == null) return;
                detail.Add(Header(s.key + " — 모델 예상 / Play 실측"));
                foreach (var c in s.checks) detail.Add(Wrap(new Label(c) { style = { color = c.StartsWith("PASS") ? new StyleColor(new Color(.45f, .8f, .5f)) : c.StartsWith("FAIL") ? new StyleColor(new Color(.95f, .5f, .45f)) : new StyleColor(StyleKeyword.Null) } }));
                detail.Add(Wrap(new Label("비고: " + s.note)));
            };
            body.Add(measureTable);
        }

        void BuildChecksTab()
        {
            var scroll = new ScrollView(); body.Add(scroll);
            if (result == null) { scroll.Add(new Label("분석 결과 없음.")); return; }
            foreach (var line in result.selfChecks)
                scroll.Add(Wrap(new Label(line) { style = { color = line.StartsWith("PASS") ? new Color(.45f, .8f, .5f) : new Color(.95f, .5f, .45f) } }));
            scroll.Add(Header("판정 규칙"));
            foreach (var r in CombatBalanceAnalysisRunner.Rules) scroll.Add(Wrap(new Label($"{r.label}: {r.description}")));
        }

        void RefreshDetail()
        {
            if (detail == null || tab == Tab.Measurement) return;
            detail.Clear();
            if (selected == null) { detail.Add(Wrap(new Label("행을 고르면 계산 경로와 비교 기준 대비 차이를 보여줍니다."))); return; }
            detail.Add(Header(selected.Title));
            detail.Add(Wrap(new Label($"표본 {selected.samples}개 중앙값 · 처치 시간 P90/P10 {F(selected.spread)}배")));
            if (selected.HasFlags)
                foreach (var code in selected.flags)
                    detail.Add(Wrap(new Label("⚠ " + CombatBalanceAnalysisRunner.RuleLabel(code) + " — " + CombatBalanceAnalysisRunner.Rules.First(r => r.code == code).description) { style = { color = new Color(.95f, .6f, .35f) } }));
            if (baseline != null && baseline != selected)
            {
                detail.Add(Header("비교 기준: " + baseline.Title));
                foreach (var (label, a, b) in new[] { ("공격", baseline.attack, selected.attack), ("적 HP", baseline.enemyHealth, selected.enemyHealth), ("충전 초", baseline.chargeTime, selected.chargeTime),
                    ("처치 초", baseline.killTime, selected.killTime), ("약공 1타 기대", baseline.weakHit, selected.weakHit), ("강공 적중 기대", baseline.heavyDirect, selected.heavyDirect),
                    ("틱 %", baseline.dotShare, selected.dotShare), ("범위 %", baseline.aoeShare, selected.aoeShare), ("평타 버팀", (float)baseline.normalSurvivable, (float)selected.normalSurvivable), ("강공 버팀", (float)baseline.strongSurvivable, (float)selected.strongSurvivable) })
                    detail.Add(new Label($"{label}: {F(a)} → {F(b)} ({(float.IsNaN(a) || float.IsNaN(b) || Mathf.Approximately(a, 0) ? "—" : F((b / a - 1f) * 100f, "+0;-0;0") + "%")})"));
            }
            detail.Add(Header("계산 경로"));
            foreach (var t in selected.trace)
            {
                var box = new VisualElement { style = { marginBottom = 4 } };
                box.Add(Wrap(new Label($"{t.label}: {t.value}") { style = { unityFontStyleAndWeight = FontStyle.Bold } }));
                box.Add(Wrap(new Label($"식 {t.formula}")));
                box.Add(Wrap(new Label($"소유 {t.owner}") { style = { opacity = .75f } }));
                detail.Add(box);
            }
            var match = measurement?.scenarios.FirstOrDefault(s => s.level == selected.level && s.grade == selected.grade && s.element == selected.element && s.enemyClass == selected.enemyClass && s.preset == selected.preset
                && (selected.mode == CombatMode.Crowd) == s.key.Contains("군집"));
            if (match != null)
            {
                detail.Add(Header("같은 조건 Play 실측 (" + match.status + ")"));
                foreach (var c in match.checks) detail.Add(Wrap(new Label(c)));
            }
        }
    }
}
