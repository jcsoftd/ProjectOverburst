using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Overburst.EditorTools.MonsterTuner
{
    public sealed partial class MonsterTunerWindow
    {
        private readonly Dictionary<string, bool> advancedSections = new Dictionary<string, bool>();
        private readonly Dictionary<string, Button> quickMotionButtons = new Dictionary<string, Button>();
        private Label quickMotionStatus;
        private AnimationClip quickMotionClip;
        private void BuildQuickMotions(VisualElement center)
        {
            quickMotionButtons.Clear();
            var bar = new VisualElement { name = "quick-motions" }; bar.AddToClassList("mt-quick-motions");
            foreach (string role in new[] { "대기", "이동", "공격", "피격", "패링", "사망" })
            {
                string selected = role;
                var button = new Button(() => PlayQuickMotion(selected)) { name = "quick-motion:" + role, text = role, tooltip = role + " 모션 재생" };
                bar.Add(button); quickMotionButtons[role] = button;
            }
            center.Add(bar);
            quickMotionStatus = new Label("몬스터를 선택하면 동작을 바로 재생할 수 있습니다.") { name = "quick-motion-status" };
            quickMotionStatus.AddToClassList("mt-note"); center.Add(quickMotionStatus);
        }
        private MonsterTunerAnimationBindings.Binding QuickBinding(string role)
        {
            var bindings = MonsterTunerAnimationBindings.Read(session?.Definition?.AnimationProfile);
            if (role == "피격")
                return bindings.Find(b => MonsterTunerAnimationBindings.IsHit(b) && b.Key == hitPreviewKey)
                    ?? bindings.Find(b => MonsterTunerAnimationBindings.IsHit(b) && b.ProfileProperties.Contains("hit"))
                    ?? bindings.Find(MonsterTunerAnimationBindings.IsHit);
            if (role == "패링")
            {
                var parries = bindings.FindAll(b => b.Label.StartsWith("패링", StringComparison.Ordinal));
                if (parries.Count > 0)
                    return parries.Find(b => b.Key == parryPreviewKey)
                        ?? parries.Find(b => b.StatePath.EndsWith(".Parry_Collapse", StringComparison.OrdinalIgnoreCase)) ?? parries[0];
                return bindings.Find(b => MonsterTunerAnimationBindings.IsHit(b) && b.Key == parryPreviewKey) ?? QuickBinding("피격");
            }
            if (role == "이동") return bindings.Find(b => b.Label == "걷기") ?? bindings.Find(b => b.Label == "달리기");
            return bindings.Find(b => b.Label == role);
        }
        private void PlayQuickMotion(string role)
        {
            if (session == null || stage.Actor == null) return;
            if (role == "공격") { tab = 3; BuildFields(); PreviewAttack(); }
            else
            {
                var binding = QuickBinding(role);
                var clip = binding != null ? MonsterTunerAnimationBindings.WorkingClip(session, binding) : null;
                if (clip == null) { SetStatus(role + " 모션이 연결되어 있지 않습니다.", true); return; }
                if (role == "피격" || role == "패링") { tab = 4; BuildFields(); fields.scrollOffset = Vector2.zero; }
                stage.SetClip(clip); stage.Playing = true; RenderNow();
            }
            quickMotionClip = stage.Clip; quickMotionStatus.text = role + " 미리보기";
            foreach (var pair in quickMotionButtons) pair.Value.EnableInClassList("selected", pair.Key == role);
        }
        private void RefreshQuickMotions()
        {
            foreach (var pair in quickMotionButtons)
            {
                bool available = session != null && stage.Actor != null;
                if (available && pair.Key == "공격") available = session.Definition.AbilitySet?.Count > 0;
                else if (available)
                {
                    var binding = QuickBinding(pair.Key);
                    available = binding != null && MonsterTunerAnimationBindings.WorkingClip(session, binding) != null;
                }
                pair.Value.SetEnabled(available && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling);
            }
        }
        private void RefreshQuickMotionStatus()
        {
            if (quickMotionStatus == null || quickMotionClip == stage.Clip) return;
            quickMotionClip = stage.Clip;
            var binding = MonsterTunerAnimationBindings.Read(session?.Definition?.AnimationProfile)
                .Find(b => MonsterTunerAnimationBindings.WorkingClip(session, b) == stage.Clip);
            string label = binding?.Label ?? "모션";
            if (binding != null && binding.StatePath.Contains(".Attack_")) label = "공격";
            quickMotionStatus.text = stage.Clip != null ? label + " 미리보기" : "재생할 모션을 선택하세요.";
            foreach (var pair in quickMotionButtons) pair.Value.EnableInClassList("selected", label.StartsWith(pair.Key, StringComparison.Ordinal));
        }
        private void SetPreviewLabel(string label)
        {
            quickMotionClip = stage.Clip;
            if (quickMotionStatus != null) quickMotionStatus.text = label + " 미리보기";
            foreach (var pair in quickMotionButtons) pair.Value.EnableInClassList("selected", label.StartsWith(pair.Key, StringComparison.Ordinal));
        }
        private void FoldDetails(int first, string key, string title)
        {
            var content = fields.contentContainer.Children().Skip(first).ToList();
            if (content.Count == 0) return;
            var fold = new Foldout { name = key, text = title, value = advancedSections.TryGetValue(key, out bool expanded) && expanded };
            fold.RegisterValueChangedCallback(e => advancedSections[key] = e.newValue);
            foreach (var element in content) { element.RemoveFromHierarchy(); fold.Add(element); }
            fields.Add(fold);
        }
        private void BuildSelectedShapeFields()
        {
            if (!PointAddress(out string target, out string property)) { Note("점이나 범위를 선택하면 위치와 크기를 조절할 수 있습니다."); return; }
            var component = MonsterTunerAddress.ResolveComponent(stage.Actor, target);
            if (property == "m_Center")
            {
                Heading("선택한 범위 크기");
                if (component is CapsuleCollider) { Field(target, "m_Radius", "폭 (반경 m)"); Field(target, "m_Height", "높이 (m)"); }
                else if (component is SphereCollider) Field(target, "m_Radius", "크기 (반경 m)");
                else if (component is BoxCollider) Field(target, "m_Size", "크기");
            }
            else if (property == "hurtLocalCenter" || property == "localBodyCenter" || property == "localHitCenter")
            {
                Heading("선택한 범위 크기");
                string toggle = property == "hurtLocalCenter" ? "useCustomHurtVolume" : property == "localBodyCenter" ? "useAuthoredVolume" : "useAuthoredHitVolume";
                string prefix = property == "hurtLocalCenter" ? "hurt" : property == "localBodyCenter" ? "body" : "hit";
                Field(target, toggle, "이 몬스터에 맞춰 조절");
                bool enabled = session.Value(target, toggle).boolean;
                Field(target, prefix + "Radius", "폭 (반경 m)", enabled); Field(target, prefix + "Height", "높이 (m)", enabled);
            }
        }
    }
}
