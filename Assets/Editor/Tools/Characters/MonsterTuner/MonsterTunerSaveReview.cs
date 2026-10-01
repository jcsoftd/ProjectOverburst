using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Overburst.EditorTools.MonsterTuner
{
    internal sealed class MonsterTunerSaveReview : EditorWindow
    {
        private MonsterTunerWindow owner;
        private MonsterTunerSession draft;
        private MonsterTunerCatalog catalog;
        private string reviewedEdits;
        private Label firstError;
        private Button confirm;
        private IVisualElementScheduledItem watcher;
        internal string ReviewedEdits => reviewedEdits;
        internal static string Fingerprint(MonsterTunerSession value)
            => string.Join("\n", value.edits.Select(edit => JsonUtility.ToJson(edit)));
        internal static MonsterTunerSaveReview Create(MonsterTunerWindow owner, MonsterTunerSession draft, MonsterTunerCatalog catalog)
        {
            var review = CreateInstance<MonsterTunerSaveReview>(); review.owner = owner; review.draft = draft; review.catalog = catalog;
            review.titleContent = new GUIContent("선택 몬스터 저장 검토"); review.minSize = new Vector2(660, 540);
            review.position = new Rect(owner.position.x + 80, owner.position.y + 60, 860, 760);
            return review;
        }
        public void CreateGUI() => Rebuild();
        private void Rebuild()
        {
            if (draft == null || draft.Definition == null) { Close(); return; }
            watcher?.Pause();
            reviewedEdits = Fingerprint(draft);
            var root = rootVisualElement; root.Clear();
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Editor/Tools/Characters/MonsterTuner/MonsterTunerWindow.uss");
            if (style != null && !root.styleSheets.Contains(style)) root.styleSheets.Add(style);
            root.AddToClassList("mt-review");
            var title = new Label(draft.Definition.DisplayName + " · 저장 검토"); title.AddToClassList("mt-title"); root.Add(title);
            AddLabel(root, "선택 ID: " + draft.Definition.EnemyId + "   ·   변경 " + draft.edits.Count + "개\n이 몬스터의 변경만 저장합니다. 공유 자산은 아래 계획에 따라 분리합니다.", "mt-note");
            List<string> errors;
            try { errors = MonsterTunerWriter.Validate(draft, catalog); }
            catch (Exception error) { errors = new List<string> { error.Message }; }
            firstError = AddLabel(root, errors.Count > 0 ? "저장 전 수정: " + errors[0] : draft.Dirty ? "검증 통과 · 아래 대상과 변경값을 확인하세요." : "저장할 변경이 없습니다.", errors.Count > 0 ? "mt-error" : "mt-review-valid");
            firstError.name = "review-first-error";
            var scroll = new ScrollView { name = "review-scroll" }; scroll.style.flexGrow = 1; root.Add(scroll);
            if (errors.Count > 1)
            {
                var all = new Foldout { text = "검증 오류 " + errors.Count + "개", value = false }; scroll.Add(all);
                foreach (var error in errors) AddLabel(all, "• " + error, "mt-error");
            }
            AddLabel(scroll, "저장 대상 자산", "mt-heading");
            foreach (var target in MonsterTunerWriter.ReviewTargets(draft, catalog))
            {
                var card = new VisualElement(); card.AddToClassList("mt-review-asset"); scroll.Add(card);
                AddLabel(card, target.Label + "   ·   공유 " + target.SharedCount + "종   ·   " + (target.Created ? "신규" : "기존"), "mt-heading");
                AddLabel(card, target.Reason, "mt-note");
                AddLabel(card, "현재  " + target.SourcePath, "mt-review-path");
                if (target.Created) AddLabel(card, "저장  " + target.DestinationPath, "mt-review-path");
            }
            AddLabel(scroll, "변경 전 / 변경 후", "mt-heading");
            var headings = new VisualElement(); headings.AddToClassList("mt-review-row"); scroll.Add(headings);
            Cell(headings, "항목", "mt-review-key"); Cell(headings, "변경 전", "mt-review-value"); Cell(headings, "변경 후", "mt-review-value");
            foreach (var edit in draft.edits)
            {
                var row = new VisualElement { name = "review-change:" + edit.target + "|" + edit.property }; row.AddToClassList("mt-review-row"); scroll.Add(row);
                var key = Cell(row, edit.label, "mt-review-key"); key.tooltip = edit.target + "\n" + edit.property;
                Cell(row, Display(edit.before), "mt-review-value"); Cell(row, Display(edit.after), "mt-review-value");
            }
            AddLabel(scroll, "신규 경로는 현재 자산 목록 기준입니다. 같은 이름의 자산이 추가되면 저장 시 고유 이름을 다시 확인합니다.", "mt-note");
            var footer = new VisualElement(); footer.AddToClassList("mt-row"); root.Add(footer);
            footer.Add(new Button(Rebuild) { name = "review-refresh", text = "검증 / 내역 새로고침" });
            var spacer = new VisualElement(); spacer.style.flexGrow = 1; footer.Add(spacer);
            footer.Add(new Button(Close) { name = "review-cancel", text = "취소" });
            confirm = new Button(Confirm) { name = "review-confirm", text = "선택 몬스터 저장" }; confirm.AddToClassList("mt-save"); footer.Add(confirm);
            confirm.SetEnabled(draft.Dirty && errors.Count == 0);
            watcher = root.schedule.Execute(CheckCurrent).Every(150);
        }
        private void OnDisable() { watcher?.Pause(); watcher = null; }
        private void CheckCurrent()
        {
            if (draft == null || owner == null) { Close(); return; }
            bool stale = Fingerprint(draft) != reviewedEdits;
            bool locked = EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling;
            if (stale || locked)
            {
                confirm.SetEnabled(false); firstError.text = stale ? "편집이 바뀌었습니다. 검증 / 내역 새로고침 후 저장하세요." : "Play 또는 컴파일이 끝난 뒤 검증 / 내역을 새로고침하세요.";
                firstError.AddToClassList("mt-error");
            }
        }
        private void Confirm()
        {
            if (owner == null || draft == null) return;
            string error;
            try { error = owner.CommitReview(draft, reviewedEdits); }
            catch (Exception failure) { error = "저장 전 확인: " + failure.Message; }
            if (error == null) { Close(); return; }
            firstError.text = error; firstError.AddToClassList("mt-error"); confirm.SetEnabled(false);
        }
        private static Label AddLabel(VisualElement parent, string text, string css)
        { var label = new Label(text); label.AddToClassList(css); label.tooltip = text; parent.Add(label); return label; }
        private static Label Cell(VisualElement row, string text, string css) => AddLabel(row, text, css);
        private static string Display(MonsterTunerValue value)
        {
            if (value.arrayKind == "muzzles") return value.muzzles.Length == 0 ? "자동 발사점" : string.Join("\n", value.muzzles.Select(m => (m.ability.Resolve() as EnemyAbilityDefinition)?.AbilityId + " · " + m.socket.text + "\n" + m.offset.ToString("F3")));
            if (value.type == SerializedPropertyType.ObjectReference)
            {
                if (value.text?.StartsWith("actor:", StringComparison.Ordinal) == true) return "부착점 " + value.text.Substring(6);
                var asset = value.Resolve(); return asset != null ? asset.name + "\n" + AssetDatabase.GetAssetPath(asset) : "없음";
            }
            return value.Display;
        }
    }
}
