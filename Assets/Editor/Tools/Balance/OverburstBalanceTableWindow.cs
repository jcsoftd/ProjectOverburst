using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Overburst.EditorBalance
{
    public sealed partial class OverburstBalanceTableWindow : EditorWindow
    {
        enum View { Weapons, GearTiers, GearItems, Enemies, Abilities, Growth }
        sealed class Row
        {
            public string Name, Group;
            public Object Source;
            public bool BossMaterial;
            public readonly List<BalanceField> Fields = new List<BalanceField>();
            public readonly Dictionary<int, (float? value, string text, string note)> ReadOnly = new Dictionary<int, (float?, string, string)>();
        }
        readonly Dictionary<Object, BalanceTableDocument> documents = new Dictionary<Object, BalanceTableDocument>();
        readonly HashSet<Object> pendingSave = new HashSet<Object>();
        readonly HashSet<Object> appliedSources = new HashSet<Object>();
        readonly Dictionary<Object, string> appliedBaselines = new Dictionary<Object, string>();
        readonly List<Row> rows = new List<Row>();
        readonly List<Row> filtered = new List<Row>();
        View view;
        string[] headings = Array.Empty<string>();
        string query = "", group = "전체";
        int sortColumn = -1, itemLevel = 1, playerLevel = 1, seed = 731;
        bool ascending = true;
        ItemGrade grade = ItemGrade.Common;
        Row selected;
        ListView list;
        VisualElement tableHost;
        Label status, preview, count;
        DropdownField subView, groupFilter;
        int category;
        bool rebuilding;
        bool onlyAvailableLevel;
        const float NameWidth = 270, CellWidth = 116;
        public int VisibleRowCount => filtered.Count;
        public int TotalRowCount => rows.Count;
        public bool HasDraftChanges => documents.Values.Any(d => d.IsChanged);

        [MenuItem("OVERBURST/Balance/무기·장비·몬스터 밸런스 테이블")]
        public static OverburstBalanceTableWindow Open()
        {
            var window = GetWindow<OverburstBalanceTableWindow>();
            window.titleContent = new GUIContent("OVERBURST 밸런스");
            window.minSize = new Vector2(1000, 700); window.Show(); return window;
        }
        private void OnEnable() => Undo.undoRedoPerformed += UndoChanged;
        private void OnDisable()
        {
            Undo.undoRedoPerformed -= UndoChanged; stateSchedule?.Pause();
            foreach (var d in documents.Values) d.Dispose(); documents.Clear();
        }
        public void CreateGUI() => BuildWorkspaceUI();
        void ChooseCategory(int value)
        {
            category = value; rebuilding = true;
            subView.choices = value == 0 ? new List<string> { "무기 원본", "공통 성장" }
                : value == 1 ? new List<string> { "공통 10구간", "장비 도감" } : new List<string> { "몬스터 능력치", "공격 패턴" };
            subView.SetValueWithoutNotify(subView.choices[0]); rebuilding = false; ChooseView();
        }
        void ChooseView()
        {
            bool first = subView.index == 0;
            view = category == 0 ? (first ? View.Weapons : View.Growth)
                : category == 1 ? (first ? View.GearTiers : View.GearItems) : (first ? View.Enemies : View.Abilities);
            selected = null; sortColumn = -1; group = "전체"; query = ""; changedOnly = false; searchField?.SetValueWithoutNotify(""); changedFilter?.SetValueWithoutNotify(false); BuildRows(); BuildTable(); Filter(); RefreshPreview(); RefreshStatus();
        }
        BalanceTableDocument Document(Object source)
        {
            if (!documents.TryGetValue(source, out var d)) { d = new BalanceTableDocument(source); documents.Add(source, d); }
            return d;
        }
        void Add(Row row, Object target, string path, float min, float max, float factor = 1)
            => row.Fields.Add(target != null ? Document(target).Field(path, min, max, factor) : null);
        void AddReadOnly(Row row, float? value, string text, string note)
        { row.ReadOnly[row.Fields.Count] = (value, text, note); row.Fields.Add(null); }
        static T[] Assets<T>(string folder) where T : Object => AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { folder })
            .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g))).Where(x => x != null).ToArray();
        void BuildRows()
        {
            rows.Clear();
            if (view == View.Weapons)
            {
                headings = new[] { "공격력", "치확 %", "치명 피해 %", "사거리 m", "넉백", "방출 기본력", "공통 공속 ×" };
                foreach (var w in Assets<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons"))
                {
                    var r = new Row { Name = w.name + "  " + w.itemName + (w.icon == null || w.weaponRootPrefab == null ? "  [외형 미연결]" : ""), Group = w.weaponClass.ToString(), Source = w };
                    Add(r, w, "baseStats.damage", 1, 10000); Add(r, w, "baseStats.criticalChance", 0, 60);
                    Add(r, w, "baseStats.criticalDamageMultiplier", 1, 3, 100); Add(r, w, "baseStats.range", .1f, 100);
                    Add(r, w, "baseStats.knockback", 0, 100); Add(r, w, "baseStats.elementalDischargePower", 0, 10000);
                    Add(r, w.GetMeleeDefinition(), "baseSettings.attackSpeedMultiplier", .1f, 1.5f); rows.Add(r);
                }
            }
            else if (view == View.GearTiers)
            {
                headings = new[] { "투구 HP", "갑옷 방어", "장갑 치확 %p", "신발 방어", "귀걸이 공격", "목걸이 치피 %p" };
                var table = Assets<OverburstBalanceTable>("Assets/ProjectOverburst/Resources/Balance").Single();
                for (int tier = 0; tier < 10; tier++)
                {
                    var r = new Row { Name = $"Lv {tier * 10 + 1:00}–{tier * 10 + 10:00}", Group = "공통 구간", Source = table };
                    for (int k = 0; k < 6; k++) Add(r, table, $"gearTiers.Array.data[{tier * 6 + k}]", 0, k == 2 ? 65 : 100000);
                    rows.Add(r);
                }
            }
            else if (view == View.GearItems)
            {
                headings = new[] { "등장 최소 Lv", "등장 최대 Lv" };
                foreach (var g in Assets<GearItemData>("Assets/ProjectOverburst/Resources/Items/Gear"))
                {
                    var r = new Row { Name = g.name + "  " + g.itemName, Group = g.kind.ToString(), Source = g };
                    Add(r, g, "catalogMinLevel", 1, 100); Add(r, g, "catalogMaxLevel", 1, 100); rows.Add(r);
                }
            }
            else if (view == View.Enemies || view == View.Abilities)
            {
                var defs = Assets<EnemyDefinition>("Assets/ProjectOverburst/Resources/Enemies");
                if (view == View.Enemies)
                {
                    headings = new[] { "체력 Q 계수", "걷기 m/s", "달리기 ×", "회전 °/s" };
                    foreach (var d in defs)
                    {
                        var r = new Row { Name = d.EnemyId + "  " + d.DisplayName, Group = d.Grade != null ? d.Grade.GradeType.ToString() : "누락", Source = d };
                        Add(r, d, "referenceHealthCoefficient", .1f, 1000); Add(r, d.MovementProfile, "moveSpeed", 1, 20);
                        Add(r, d.MovementProfile, "runSpeedMultiplier", 1.4f, 5); Add(r, d.MovementProfile, "turnSpeed", 0, 1440); rows.Add(r);
                    }
                }
                else
                {
                    headings = new[] { "피해 예산 %", "강공", "패링", "예고 최소 s", "회복 최소 s", "쿨다운 s", "발동 거리 m", "반경/접촉 m", "각도 °" };
                    foreach (var a in defs.Where(d => d.AbilitySet != null).SelectMany(d => Enumerable.Range(0, d.AbilitySet.Count).Select(d.AbilitySet.GetAbility)).Where(a => a != null).Distinct())
                    {
                        var r = new Row { Name = a.AbilityId, Group = a.ExecutionMode.ToString(), Source = a };
                        Add(r, a, "referencePatternDamagePercent", .01f, 100); Add(r, a, "telegraphedStrongAttack", 0, 1);
                        Add(r, a, "parryable", 0, 1); Add(r, a, "minimumWarningTime", 0, 10); Add(r, a, "minimumRecoveryTime", 0, 10);
                        Add(r, a, "cooldown", .1f, 60);
                        bool material = BalanceTableRuntimeView.UsesBossMaterial(a, defs);
                        r.BossMaterial = material;
                        bool contact = BalanceTableRuntimeView.UsesContactGeometry(a);
                        bool channel = BalanceTableRuntimeView.UsesChannelGeometry(a, defs);
                        if (material)
                        {
                            const string note = "보스 재료 실행기의 판정입니다. 보스 메이커에서 조절하세요.";
                            AddReadOnly(r, null, "보스 재료", note); AddReadOnly(r, null, "보스 재료", note); AddReadOnly(r, null, "보스 재료", note);
                        }
                        else
                        {
                            if (a.HasWeakAttackExecution) AddReadOnly(r, a.WeakAttackExecution.ApproachStartRange, null,
                                "V3 약공 프로필의 발동 거리입니다. 몬스터 튜너에서 프로필을 확인하세요.");
                            else Add(r, a, "range", .1f, 100);
                            if (channel)
                            {
                                const string note = "채널 실행기의 발사점과 SphereCast 반경을 사용합니다. 이 정의의 원형 반경과 부채꼴 각도는 사용하지 않습니다.";
                                AddReadOnly(r, null, "채널 실행기", note); AddReadOnly(r, null, "채널 실행기", note);
                            }
                            else if (contact)
                            {
                                AddReadOnly(r, a.WeakAttackExecution.MaximumContactPlanarReach, null,
                                    "접촉 캡슐의 최대 평면 도달 거리입니다. 원형 반경이 아니며 프로필의 실제 좌표를 사용합니다.");
                                AddReadOnly(r, null, "접촉 캡슐", "V3 약공 접촉은 부채꼴 각도를 사용하지 않습니다. 몬스터 튜너의 공격 범위에서 확인하세요.");
                            }
                            else { Add(r, a, "hitRadius", .01f, 30); Add(r, a, "hitAngle", 1, 360); }
                        }
                        rows.Add(r);
                    }
                }
            }
            else
            {
                headings = new[] { "수치" };
                var table = Assets<OverburstBalanceTable>("Assets/ProjectOverburst/Resources/Balance").Single();
                string[] paths = { "playerHealthPerLevel", "playerArmorEveryLevels", "playerAttackPerLevel", "itemAttackPerLevel", "enemyMoveGrowth", "enemyAttackGrowth", "enemyMoveCap", "enemyAttackCap" };
                string[] labels = { "플레이어 레벨당 HP", "방어 1당 필요 레벨", "플레이어 레벨당 공격 증가율", "아이템 레벨당 공격 증가율", "몬스터 Lv100 이동 증가율", "몬스터 Lv100 공속 증가율", "몬스터 이동 상한 배율", "몬스터 공속 상한 배율" };
                for (int i = 0; i < paths.Length; i++)
                {
                    var r = new Row { Name = labels[i], Group = "성장", Source = table };
                    Add(r, table, paths[i], i == 1 || i >= 6 ? 1 : 0, i == 0 ? 1000 : i == 1 ? 100 : i >= 6 ? 2 : 1); rows.Add(r);
                }
            }
            rebuilding = true; groupFilter.choices = new List<string> { "전체" }.Concat(rows.Select(r => r.Group).Distinct().OrderBy(x => x)).ToList();
            groupFilter.SetValueWithoutNotify("전체"); rebuilding = false;
        }
        void BuildTable() => BuildDataTable();
        VisualElement MakeRow()
        {
            var root = new VisualElement { style = { flexDirection = FlexDirection.Row } }; root.AddToClassList("balance-data-row");
            var label = new Label { name = "row-name" }; label.style.width = NameWidth; label.style.unityTextAlign = TextAnchor.MiddleLeft; root.Add(label);
            for (int i = 0; i < headings.Length; i++)
            {
                var cell = new VisualElement { name = "cell-" + i }; cell.style.width = CellWidth; cell.style.paddingLeft = cell.style.paddingRight = 3;
                var number = new FloatField { name = "number", isDelayed = true }; number.AddToClassList("balance-number"); number.style.flexGrow = 1;
                number.RegisterValueChangedCallback(e => { if (number.userData is BalanceField f) { f.Value = e.newValue / f.DisplayFactor; Changed(); } });
                var toggle = new Toggle { name = "toggle" }; toggle.RegisterValueChangedCallback(e => { if (toggle.userData is BalanceField f) { f.Value = e.newValue ? 1 : 0; Changed(); } });
                var readOnly = new Label { name = "read-only" }; readOnly.AddToClassList("balance-readonly"); readOnly.style.unityTextAlign = TextAnchor.MiddleLeft;
                cell.Add(number); cell.Add(toggle); cell.Add(readOnly); root.Add(cell);
            }
            return root;
        }
        void BindRow(VisualElement root, int index)
        {
            var row = filtered[index]; root.EnableInClassList("balance-row--alternate", index % 2 == 1); root.Q<Label>("row-name").text = (row.Fields.Any(f => f != null && f.Changed) ? "● " : "") + row.Name;
            root.Q<Label>("row-name").tooltip = AssetDatabase.GetAssetPath(row.Source);
            for (int i = 0; i < headings.Length; i++)
            {
                var f = row.Fields[i]; var cell = root.Q("cell-" + i); var number = cell.Q<FloatField>("number"); var toggle = cell.Q<Toggle>("toggle");
                var readOnly = cell.Q<Label>("read-only");
                bool derived = row.ReadOnly.TryGetValue(i, out var resolved);
                readOnly.style.display = derived ? DisplayStyle.Flex : DisplayStyle.None;
                readOnly.text = derived ? (resolved.value.HasValue ? resolved.value.Value.ToString("0.###") : resolved.text) : "";
                bool boolean = f != null && f.Type == SerializedPropertyType.Boolean;
                number.userData = f; toggle.userData = f; number.SetEnabled(f != null); toggle.SetEnabled(f != null);
                number.style.display = boolean || derived ? DisplayStyle.None : DisplayStyle.Flex; toggle.style.display = boolean ? DisplayStyle.Flex : DisplayStyle.None;
                cell.tooltip = derived ? resolved.note : "";
                if (f != null)
                {
                    number.SetValueWithoutNotify(f.Value * f.DisplayFactor); toggle.SetValueWithoutNotify(f.Value > .5f);
                    cell.tooltip = AssetDatabase.GetAssetPath(f.Document.Source) + "\n" + f.Path + "\n같은 원본을 참조하는 행은 함께 변경됩니다.";
                }
                else number.SetValueWithoutNotify(0);
                number.EnableInClassList("balance-number--changed", f != null && f.Changed);
                toggle.EnableInClassList("balance-number--changed", f != null && f.Changed);
            }
        }
        void Sort(int column) { ascending = sortColumn == column ? !ascending : true; sortColumn = column; Filter(); }
        void Filter()
        {
            filtered.Clear();
            var source = rows.Where(r => (group == "전체" || r.Group == group) && (!changedOnly || r.Fields.Any(f => f != null && f.Changed)) && r.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            if (onlyAvailableLevel && view == View.GearItems)
                source = source.Where(r => ((GearItemData)Document(r.Source).Draft).AppearsAtLevel(itemLevel));
            if (sortColumn < 0) source = ascending ? source.OrderBy(r => r.Name) : source.OrderByDescending(r => r.Name);
            else source = ascending ? source.OrderBy(r => CellValue(r, sortColumn)) : source.OrderByDescending(r => CellValue(r, sortColumn));
            filtered.AddRange(source); list?.Rebuild(); if (count != null) count.text = $"{filtered.Count} / {rows.Count}행"; emptyState?.EnableInClassList("balance-empty--visible", filtered.Count == 0); RefreshSortHeaders();
            if (selected != null && !filtered.Contains(selected)) { selected = null; RefreshPreview(); }
        }
        static float CellValue(Row row, int column) => row.ReadOnly.TryGetValue(column, out var derived)
            ? derived.value ?? 0f : row.Fields[column]?.Value ?? 0f;
        void Changed() { hasUnsavedChanges = HasDraftChanges || pendingSave.Count > 0; saveChangesMessage = "밸런스 표의 편집 내용을 적용하고 변경한 자산을 저장할까요?"; Filter(); RefreshPreview(); RefreshStatus(); }
        void RefreshStatus(string message = null)
        {
            UpdateWorkspaceState();
            if (status == null) return;
            status.text = message ?? $"초안 변경 {documents.Values.Count(d => d.IsChanged)}개 원본 / 적용 후 저장 대기 {pendingSave.Count}개. ●는 편집 중인 행입니다. 헤더를 누르면 정렬합니다.";
        }
        public string[] ValidationErrors() => documents.Values.SelectMany(d => d.Validate()).ToArray();
        void ValidateDraft() { var errors = ValidationErrors(); RefreshStatus(errors.Length == 0 ? "검증 통과. 원본값 범위와 외부 변경 충돌이 없습니다." : string.Join("\n", errors.Take(6))); }
        public void ApplyDraft()
        {
            if (!CanWrite()) { RefreshStatus("Play·컴파일·임포트·빌드가 끝난 뒤 적용하세요."); return; }
            var errors = ValidationErrors(); if (errors.Length > 0) { RefreshStatus(string.Join("\n", errors.Take(6))); return; }
            Undo.IncrementCurrentGroup(); int undoGroup = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("밸런스 테이블 적용");
            foreach (var d in documents.Values.Where(d => d.IsChanged).ToArray())
            { d.Apply(); pendingSave.Add(d.Source); appliedSources.Add(d.Source); appliedBaselines[d.Source] = EditorJsonUtility.ToJson(d.Source); }
            Undo.CollapseUndoOperations(undoGroup); hasUnsavedChanges = pendingSave.Count > 0; list?.RefreshItems(); RefreshPreview(); RefreshStatus();
        }
        public void SaveApplied()
        {
            if (!CanWrite()) { RefreshStatus("Play·컴파일·임포트·빌드가 끝난 뒤 저장하세요."); return; }
            if (pendingSave.Any(s => s != null && (!appliedBaselines.TryGetValue(s, out var baseline)
                || EditorJsonUtility.ToJson(s) != baseline)))
            { RefreshStatus("적용 후 원본이 외부에서 변경되었습니다. 저장을 중단했습니다. 원본을 확인하고 새로고침하세요."); return; }
            foreach (var source in pendingSave) if (source != null) AssetDatabase.SaveAssetIfDirty(source);
            pendingSave.Clear(); appliedBaselines.Clear(); hasUnsavedChanges = HasDraftChanges; RefreshStatus("이 툴에서 적용한 자산을 저장했습니다. 씬은 저장하지 않았습니다.");
        }
        public override void SaveChanges() { ApplyDraft(); if (HasDraftChanges) return; SaveApplied(); if (pendingSave.Count == 0) base.SaveChanges(); }
        public override void DiscardChanges()
        {
            // Applied values stay available for normal Unity Undo; only unapplied drafts are discarded.
            foreach (var d in documents.Values) d.Rebase();
            // Closing without saving keeps already applied assets dirty, like the Inspector.
            // Unity Undo remains available; only the working copies belong to this window.
            pendingSave.Clear(); appliedBaselines.Clear(); base.DiscardChanges(); list?.RefreshItems(); RefreshPreview(); RefreshStatus();
        }
        bool ConfirmDiscard() => !HasDraftChanges || EditorUtility.DisplayDialog("초안 새로고침", "적용 전 초안을 버리고 현재 원본을 다시 읽습니다.", "새로고침", "계속 편집");
        void Reload()
        {
            foreach (var d in documents.Values) d.Dispose(); documents.Clear();
            hasUnsavedChanges = pendingSave.Count > 0; ChooseView();
        }
        void UndoChanged()
        {
            foreach (var d in documents.Values)
                if (!d.IsChanged && appliedSources.Contains(d.Source))
                { d.Rebase(); if (EditorUtility.IsDirty(d.Source)) { pendingSave.Add(d.Source); appliedBaselines[d.Source] = EditorJsonUtility.ToJson(d.Source); } }
            hasUnsavedChanges = HasDraftChanges || pendingSave.Count > 0; list?.RefreshItems(); RefreshPreview(); RefreshStatus("Undo/Redo 반영. 되돌린 내용을 유지하려면 변경 자산 저장을 누르세요.");
        }
        void RefreshPreview()
        {
            RefreshSelectionDetails();
            if (preview == null) return;
            string baseline = $"기준 S0 Lv{itemLevel}: HP {OverburstCombatBalance.ReferenceHealth(itemLevel):0} / 방어 {OverburstCombatBalance.ReferenceArmor(itemLevel):0} / 기대 1타 {OverburstCombatBalance.ReferenceExpectedHit(itemLevel):0.##}";
            if (selected == null) { preview.text = baseline + "\n행 선택 시 초안 기준 계산을 표시합니다. 공통 성장/구간 초안은 적용 후 기준 S0에 반영됩니다."; return; }
            var draft = Document(selected.Source).Draft;
            var oldRandom = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(seed);
                if (draft is WeaponItemData w)
                {
                    var item = new ItemData(w, itemLevel, grade, element: WeaponElement.Fire);
                    var stats = WeaponStatCalculator.CalculateWeaponBase(item);
                    float playerDamage = CombatBalanceFormulas.ComposePlayerWeaponStats(stats, default, playerLevel, 0f).damage;
                    preview.text = $"{selected.Name}\n품질 표본 {grade} · 시드 {seed} / 무기 단독 공격 {stats.damage:0.##}, 공속 ×{stats.meleeAttackSpeedMultiplier:0.###}, 치확 {stats.critChance:0.##}%, 치피 {stats.critDamageMultiplier * 100:0.##}%, 사거리 {stats.range:0.###}m\n플레이어 Lv{playerLevel} 성장만 적용한 공격 {playerDamage:0.##} (장비/물약/카드 제외). 모델 {(w.weaponRootPrefab != null ? "연결" : "미연결")} / 아이콘 {(w.icon != null ? "연결" : "미연결")}. 공통 공속 초안은 적용 후 계산에 반영됩니다.";
                }
                else if (draft is GearItemData g)
                {
                    var item = new ItemData(g, itemLevel, grade); item.gearRolls = GearQuality.Roll(g, grade, seed);
                    preview.text = selected.Name + $"\nLv{itemLevel} · {grade} · 시드 {seed}: " + string.Join(" / ", item.gearRolls.Select(r => $"{r.stat} {GearQuality.Value(item, r):0.##} (별 가중치 {r.Weight:0.#})")) + "\n등장 구간은 외형 선택이며 실제 능력치는 아이템 레벨을 사용합니다.";
                }
                else if (draft is EnemyDefinition enemy)
                {
                    var stats = enemy.ResolveRuntimeStats(); float levelFactor = (itemLevel - 1) / 99f; var b = OverburstBalanceTable.Current;
                    var move = enemy.MovementProfile != null ? (EnemyMovementProfile)Document(enemy.MovementProfile).Draft : null;
                    float hp = enemy.ReferenceHealthCoefficient > 0f
                        ? CombatBalanceFormulas.EnemyReferenceHealth(enemy.ReferenceHealthCoefficient, itemLevel)
                        : Mathf.Max(1f, stats.MaxHealth) * OverburstGrowthRules.EnemyHealthFactor(itemLevel);
                    preview.text = selected.Name + $"\nLv{itemLevel} HP {hp:0} / 추격 {(move != null ? move.MoveSpeed * move.RunSpeedMultiplier : 0) * Mathf.Min(b.EnemyMoveCap, stats.MoveSpeedMultiplier * (1 + b.EnemyMoveGrowth * levelFactor)):0.##}m/s / 공격 배율 ×{Mathf.Min(b.EnemyAttackCap, stats.AttackSpeedMultiplier * (1 + b.EnemyAttackGrowth * levelFactor)):0.###}\n" + baseline;
                }
                else if (draft is EnemyAbilityDefinition ability)
                {
                    float speed = Mathf.Min(OverburstBalanceTable.Current.EnemyAttackCap, 1 + OverburstBalanceTable.Current.EnemyAttackGrowth * (itemLevel - 1) / 99f);
                    preview.text = selected.Name + "\n" + BalanceTableRuntimeView.DescribeAbility(ability, itemLevel, speed,
                        selected.BossMaterial);
                }
                else preview.text = selected.Name + "\n" + baseline + "\n구간/성장 변경은 장비와 몬스터 기준 곡선에 함께 반영됩니다. 기준 무기는 공격20·치확15%·치피160%의 S0입니다.";
            }
            catch (Exception e) { preview.text = "계산 오류: " + e.Message; }
            finally { UnityEngine.Random.state = oldRandom; }
        }
        static bool CanWrite() => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling
            && !EditorApplication.isUpdating && !BuildPipeline.isBuildingPlayer && !EditorUtility.scriptCompilationFailed;
    }
}
