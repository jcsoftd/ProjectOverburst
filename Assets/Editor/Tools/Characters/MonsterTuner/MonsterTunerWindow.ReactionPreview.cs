using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Overburst.EditorTools.MonsterTuner
{
    public sealed partial class MonsterTunerWindow
    {
        [SerializeField] private string hitPreviewKey;
        [SerializeField] private string parryPreviewKey;
        private void BuildHitPreview(List<MonsterTunerAnimationBindings.Binding> bindings)
        {
            var hits = bindings.Where(MonsterTunerAnimationBindings.IsHit).ToList();
            if (hits.Count == 0) { Note("연결된 피격 모션이 없습니다."); return; }
            int index = hits.FindIndex(b => b.Key == hitPreviewKey);
            if (index < 0) index = Mathf.Max(0, hits.FindIndex(b => b.ProfileProperties.Contains("hit")));
            hitPreviewKey = hits[index].Key;
            var card = new VisualElement { name = "hit-preview" }; card.AddToClassList("mt-point-card"); fields.Add(card);
            var title = new Label("피격 미리보기"); title.AddToClassList("mt-heading"); card.Add(title);
            var direction = new DropdownField("피격 방향", hits.Select(b => b.Label).ToList(), index) { name = "hit-preview-choice" };
            if (hits.Count > 1) card.Add(direction);
            var info = new Label { name = "hit-preview-clip" }; info.AddToClassList("mt-note");
            var play = new Button(() =>
            {
                var clip = MonsterTunerAnimationBindings.WorkingClip(session, hits[direction.index]);
                if (clip == null) return;
                stage.SetClip(clip); stage.Playing = true; RenderNow(); SetPreviewLabel(hits[direction.index].Label);
            }) { name = "hit-preview-play", text = "▶ 피격 재생" };
            card.Add(play); card.Add(info);
            direction.RegisterValueChangedCallback(_ => Refresh());
            Refresh();
            void Refresh()
            {
                var binding = hits[direction.index]; hitPreviewKey = binding.Key;
                var clip = MonsterTunerAnimationBindings.WorkingClip(session, binding);
                play.SetEnabled(clip != null);
                info.text = clip != null ? clip.name + " · " + clip.length.ToString("F2") + "s" : "이 피격 슬롯에 클립을 연결하세요.";
                info.tooltip = binding.StatePath;
                RefreshQuickMotions();
            }
        }
        private void BuildParryPreview(List<MonsterTunerAnimationBindings.Binding> bindings)
        {
            var parries = bindings.Where(b => b.Label.StartsWith("패링", System.StringComparison.Ordinal)).ToList();
            bool fallback = parries.Count == 0;
            if (fallback) parries = bindings.Where(MonsterTunerAnimationBindings.IsHit).ToList();
            var card = new VisualElement { name = "parry-preview" }; card.AddToClassList("mt-point-card"); fields.Add(card);
            var title = new Label("패링 모션 미리보기"); title.AddToClassList("mt-heading"); card.Add(title);
            if (parries.Count == 0)
            {
                var missing = new Label("전용 패링 모션과 대체 피격 모션이 없습니다."); missing.AddToClassList("mt-note"); card.Add(missing); return;
            }
            int index = parries.FindIndex(b => b.Key == parryPreviewKey);
            if (index < 0) index = Mathf.Max(0, parries.FindIndex(b => fallback ? b.ProfileProperties.Contains("hit") : b.StatePath.EndsWith(".Parry_Collapse", System.StringComparison.OrdinalIgnoreCase)));
            parryPreviewKey = parries[index].Key;
            var choice = new DropdownField(fallback ? "대체 피격 방향" : "패링 단계", parries.Select(b => b.Label).ToList(), index) { name = "parry-preview-choice" };
            if (parries.Count > 1) card.Add(choice);
            if (fallback)
            {
                var note = new Label("전용 패링 클립 없음 · 피격 클립으로 대체 반응을 확인합니다."); note.AddToClassList("mt-note"); card.Add(note);
            }
            var info = new Label { name = "parry-preview-clip" }; info.AddToClassList("mt-note");
            var play = new Button(() =>
            {
                var clip = MonsterTunerAnimationBindings.WorkingClip(session, parries[choice.index]);
                if (clip == null) return;
                stage.SetClip(clip); stage.Playing = true; RenderNow(); SetPreviewLabel(fallback ? "패링 대체 피격" : parries[choice.index].Label);
            }) { name = "parry-preview-play", text = fallback ? "▶ 패링 반응 재생 (피격)" : "▶ 패링 모션 재생" };
            card.Add(play); card.Add(info); choice.RegisterValueChangedCallback(_ => Refresh()); Refresh();
            void Refresh()
            {
                var binding = parries[choice.index]; parryPreviewKey = binding.Key;
                var clip = MonsterTunerAnimationBindings.WorkingClip(session, binding); play.SetEnabled(clip != null);
                info.text = clip != null ? clip.name + " · " + clip.length.ToString("F2") + "s" : "이 패링 슬롯에 클립을 연결하세요.";
                info.tooltip = binding.StatePath;
                RefreshQuickMotions();
            }
        }
    }
}
