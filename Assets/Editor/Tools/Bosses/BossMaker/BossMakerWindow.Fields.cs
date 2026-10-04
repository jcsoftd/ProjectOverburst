using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Overburst.EditorTools.BossMaker
{
    public sealed partial class BossMakerWindow
    {
        int damageLevel = 100;
        VisualElement fieldHost;
        VisualElement FieldHost => fieldHost ?? fields;
        void BuildFields()
        {
            if (fields == null) return; fields.Clear(); fieldHost = fields; if (Draft == null) return;
            var d = Draft; var m = d.Material; strikeIndex = Mathf.Clamp(strikeIndex, 0, m.strikes.Length - 1);
            attackTitle.text = m.displayName;
            for (int i = 0; i < 4; i++) rootVisualElement.Q<Button>("boss-tab-" + i)?.EnableInClassList("bm-selected", i == tab);
            var choices = new List<string>(); for (int i = 0; i < m.strikes.Length; i++) choices.Add((i + 1) + (m.delivery == EnemyBossMaterialDelivery.Melee ? "타" : "번째 발사"));
            var strikePicker = new DropdownField("타격 선택", choices, strikeIndex) { name = "boss-strike" };
            strikePicker.RegisterValueChangedCallback(_ => { strikeIndex = strikePicker.index; if (preview != null) preview.Strike = strikeIndex; BuildFields(); renderDirty = true; UpdateTransport(); }); fields.Add(strikePicker);
            var jump = Row("bm-jumps"); fields.Add(jump);
            var parryJump = Button("패링 시작", () => Seek(d.ParryWindow(strikeIndex, preview?.BaseSpeed ?? 1f).x), "boss-jump-parry"); parryJump.SetEnabled(m.IsStrikeParryable(strikeIndex)); jump.Add(parryJump);
            jump.Add(Button(m.delivery == EnemyBossMaterialDelivery.Melee ? "타격" : "발사", () => Seek(m.strikes[strikeIndex].impact), "boss-jump-hit"));
            jump.Add(Button("판정 끝", () => Seek(m.strikes[strikeIndex].contactEnd)));
            fields.Add(new Label("클립: " + m.runtimeClip.name));
            if (tab == 0) GeometryFields(); else if (tab == 1) TimingFields(); else if (tab == 2) DamageSpeedFields(); else DeliveryFields();
            fields.Add(Button("원본 자산 열기", () => Selection.activeObject = d.Source, "boss-open-source"));
        }
        void GeometryFields()
        {
            var s = Draft.Material.strikes[strikeIndex];
            fields.Add(Heading("지면 판정 · 미터"));
            var kinds = new List<string> { "부채꼴", "원형", "도넛", "직사각형" };
            var shape = new DropdownField("형태", kinds, (int)s.shape) { name = "boss-shape" };
            shape.RegisterValueChangedCallback(_ => Edit("판정 형태", () =>
            {
                s.shape = (GroundIndicatorShape)shape.index;
                if (s.shape == GroundIndicatorShape.Donut && s.innerRadius <= 0f) s.innerRadius = s.radius * .3f;
                if (s.shape == GroundIndicatorShape.Sector && s.angle >= 359.5f) s.angle = 160f;
            }, true)); fields.Add(shape);
            if (s.shape == GroundIndicatorShape.Sector || s.shape == GroundIndicatorShape.Rectangle) Float("방향 Y (도)", s.yaw, v => s.yaw = v, "boss-yaw");
            if (s.shape == GroundIndicatorShape.Rectangle)
            { Float("폭 (m)", s.width, v => s.width = v, "boss-width"); Float("길이 (m)", s.length, v => s.length = v, "boss-length"); }
            else
            {
                Float(s.shape == GroundIndicatorShape.Donut ? "바깥 반경 (m)" : "반경 (m)", s.radius, v => s.radius = v, "boss-radius");
                if (s.shape == GroundIndicatorShape.Donut || s.shape == GroundIndicatorShape.Sector) Float("안쪽 제외 (m)", s.innerRadius, v => s.innerRadius = v, "boss-inner-radius");
                if (s.shape == GroundIndicatorShape.Sector) Float("각도 (도)", s.angle, v => s.angle = v, "boss-angle");
            }
            var detail = new Foldout { text = "중심·높이 세부 조정", value = false }; fields.Add(detail); fieldHost = detail;
            Vector("중심 오프셋", s.localOrigin, v => s.localOrigin = v, "boss-origin");
            Float("아래 높이 (m)", s.minimumHeight, v => s.minimumHeight = v, "boss-min-height"); Float("위 높이 (m)", s.maximumHeight, v => s.maximumHeight = v, "boss-max-height"); fieldHost = fields;
            Toggle("바닥 전조 표시", Draft.Material.showTelegraph, v => Draft.Material.showTelegraph = v, "boss-show-telegraph");
            Note(Draft.Material.delivery == EnemyBossMaterialDelivery.Boulder ? "원형 중심은 공격을 확정한 표적 착지점입니다. 프리뷰 표적 거리는 이동·발사 탭에서 조절합니다."
                : Draft.Material.delivery == EnemyBossMaterialDelivery.Spit ? "직선은 입에서 표적을 향한 예고 경로입니다. 피해는 비행체의 실제 몸체 접촉에서 발생합니다."
                : s.shape == GroundIndicatorShape.Donut ? "안쪽 원 안은 안전 지대입니다. 실제 몸체가 고리와 겹치면 맞습니다. 반경 손잡이를 끌어 함께 확인하세요."
                : s.shape == GroundIndicatorShape.Sector ? "부채꼴 내부는 전조와 맞게 반경의 5% 이상 비웁니다. 노란 손잡이로 반경·각도를 조절하고 파란 손잡이로 중심을 옮깁니다."
                : "원 안에 실제 몸체가 닿으면 맞습니다. 노란 손잡이로 반경을 조절하고 파란 손잡이로 중심을 옮깁니다.");
        }
        void TimingFields()
        {
            var d = Draft; var m = d.Material; int phase = strikeIndex; var s = m.strikes[phase]; var p = m.tuning.parries[phase];
            fields.Add(Heading("원본 모션 프레임"));
            Float(m.delivery == EnemyBossMaterialDelivery.Melee ? "타격 · 판정 시작 F" : "발사 F", s.impact * d.FrameCount, v => { s.impact = v / d.FrameCount; s.contactStart = s.impact; }, "boss-impact-frame");
            if (m.delivery == EnemyBossMaterialDelivery.Melee) Float("판정 끝 F", s.contactEnd * d.FrameCount, v => s.contactEnd = v / d.FrameCount, "boss-contact-end-frame");
            else Note("발사 시점 이후의 피해는 비행체 접촉 또는 바위 착지로 발생합니다. 접촉 창은 근접 공격의 창과 구분합니다.");
            float speed = m.AnimationSpeedMultiplier * (preview?.BaseSpeed ?? 1f);
            Note($"{d.FrameCount:0.##}프레임 / {m.runtimeClip.frameRate:0.##}fps\n이 타격의 실제 시점: {d.Ability.ResolvePacedTime(s.impact, speed):0.000}s\n원본의 타격 자세와 실제 재생 시간을 함께 확인하세요.");
            fields.Add(Heading("패링 반응"));
            bool melee = m.delivery == EnemyBossMaterialDelivery.Melee && d.Ability.IsMeleeStrongAttack;
            var global = Toggle("공격 패링 허용", AbilityBool("parryable"), v => SetAbilityBool("parryable", v), "boss-parryable"); global.SetEnabled(melee);
            var allow = Toggle("이 타격 패링 허용", p.canParry, v => p.canParry = v, "boss-strike-parry"); allow.SetEnabled(melee);
            var custom = new Toggle("이 타격의 패링 구간 직접 지정") { value = p.overrideWindow, name = "boss-custom-parry" };
            custom.SetEnabled(melee && p.canParry); custom.RegisterValueChangedCallback(e => Edit("패링 구간 지정", () =>
            { if (e.newValue && !p.overrideWindow) { var w = d.ParryWindow(phase, preview?.BaseSpeed ?? 1f); p.startNormalized = w.x; p.endNormalized = w.y; } p.overrideWindow = e.newValue; }, true)); fields.Add(custom);
            var window = d.ParryWindow(phase, preview?.BaseSpeed ?? 1f);
            var start = Float("패링 시작 F", window.x * d.FrameCount, v => p.startNormalized = v / d.FrameCount, "boss-parry-start-frame");
            var end = Float("패링 끝 F", window.y * d.FrameCount, v => p.endNormalized = v / d.FrameCount, "boss-parry-end-frame");
            start.SetEnabled(melee && p.canParry && p.overrideWindow); end.SetEnabled(melee && p.canParry && p.overrideWindow);
            Note(p.overrideWindow ? "직접 지정한 구간은 이 타격의 원본 프레임에 고정됩니다. 재생 속도가 바뀌면 실제 허용 시간이 달라집니다. 패링 끝은 타격을 넘지 않습니다."
                : $"기본 구간은 현재 실행 속도에서 타격 {EnemyAbilityController.ParryLeadSeconds:0.##}초 전부터입니다. 상태 효과와 보스 기본 공속에 따라 원본 프레임 위치가 달라집니다.");
            if (!melee) Note("현재 실행기의 원거리 공격은 패링 대상이 아닙니다.");
        }
        void DamageSpeedFields()
        {
            var d = Draft; var m = d.Material;
            fields.Add(Heading("피해"));
            Float("이 공격의 피해 계수", m.DamageMultiplier, v => m.tuning.damageMultiplier = v, "boss-damage-multiplier");
            AbilityFloat("고정 피해 / 1타", "damage", "boss-base-damage").SetEnabled(!d.Ability.UsesLevelDamageBudget);
            AbilityFloat("기준 체력 비율 (%)", "referencePatternDamagePercent", "boss-damage-percent").tooltip = "0이면 고정 피해. 0보다 크면 레벨 기준 유효 체력 비율을 타격 수로 나눕니다.";
            var level = new IntegerField("피해 조회 레벨") { value = damageLevel, name = "boss-damage-level", isDelayed = true };
            level.RegisterValueChangedCallback(e => { damageLevel = Mathf.Clamp(e.newValue, 1, 100); BuildFields(); }); fields.Add(level);
            Note($"레벨 {damageLevel}: 1타 {d.Ability.ResolveDamage(damageLevel) * m.DamageMultiplier:0.##} / 전체 {d.Ability.ResolveDamage(damageLevel) * m.DamageMultiplier * m.strikes.Length:0.##}\n등급·상태 효과의 보스 전체 배율이 추가로 적용됩니다.");
            fields.Add(Heading("공격 속도"));
            Float("이 공격의 재생 배율", m.AnimationSpeedMultiplier, v => m.tuning.animationSpeedMultiplier = v, "boss-animation-speed");
            var baseline = new FloatField("프리뷰 기준 공속") { value = preview?.BaseSpeed ?? 1f, name = "boss-preview-base-speed", isDelayed = true };
            baseline.RegisterValueChangedCallback(e => { if (preview != null && EnemyBossMaterialStrike.Finite(e.newValue) && e.newValue > 0f) { preview.BaseSpeed = e.newValue; preview.Changed(); renderDirty = true; UpdateTransport(); } }); fields.Add(baseline);
            Note("공격 재생 배율은 게임의 보스 기본 공속·상태 배율과 곱합니다. 프리뷰 기준 공속과 감상 배속은 저장하지 않습니다.");
            var paced = Toggle("준비·타격·회복 시간으로 재생", AbilityBool("telegraphedAttack"), v => SetAbilityBool("telegraphedAttack", v), "boss-paced-timeline");
            if (d.Ability.UsesPacedTimeline)
            {
                AbilityFloat("준비 시간 (s)", "preparationDuration", "boss-preparation-seconds");
                AbilityFloat("타격 시간 (s)", "releaseDuration", "boss-release-seconds");
                AbilityFloat("회복 시간 (s)", "recoveryDuration", "boss-recovery-seconds");
                Note("공통 실행기의 준비 최소 0.42s·타격 최소 0.10s와 긴 모션 회복 하한을 따릅니다. 실제 시간은 프리뷰에서 확인하세요.");
            }
            AbilityFloat("최소 회복 시간 (s)", "minimumRecoveryTime", "boss-min-recovery");
            AbilityFloat("쿨다운 (s)", "cooldown", "boss-cooldown");
            AbilityFloat("선택 최소 거리 (m)", "minimumRange", "boss-min-range");
            AbilityFloat("선택 최대 거리 (m)", "range", "boss-range");
            AbilityFloat("선택 가중치", "weight", "boss-weight");
        }
        void DeliveryFields()
        {
            var m = Draft.Material; fields.Add(Heading("코드 이동 · RM 제외"));
            Float("전진 거리 (m)", m.advanceDistance, v => m.advanceDistance = v, "boss-advance-distance");
            Float("전진 시작 F", m.advanceWindow.x * Draft.FrameCount, v => m.advanceWindow.x = v / Draft.FrameCount, "boss-advance-start");
            Float("전진 끝 F", m.advanceWindow.y * Draft.FrameCount, v => m.advanceWindow.y = v / Draft.FrameCount, "boss-advance-end");
            Toggle("준비 중 표적 추적", m.tracksTargetDuringWindup, v => m.tracksTargetDuringWindup = v, "boss-track-target");
            Float("조준 고정 선행 (s)", m.aimLockLeadSeconds, v => m.aimLockLeadSeconds = v, "boss-aim-lock");
            if (m.delivery != EnemyBossMaterialDelivery.Melee)
            {
                fields.Add(Heading("비행체"));
                Float("충돌 몸체 반경 (m)", m.projectileRadius, v => m.projectileRadius = v, "boss-projectile-radius");
                if (m.delivery == EnemyBossMaterialDelivery.Spit)
                {
                    Float("비행 속도 (m/s)", m.projectileSpeed, v => m.projectileSpeed = v, "boss-projectile-speed");
                    var bone = new TextField("발사 소켓") { value = m.muzzleBone, name = "boss-muzzle", isDelayed = true };
                    bone.RegisterValueChangedCallback(e => Edit("발사 소켓", () => m.muzzleBone = e.newValue)); fields.Add(bone);
                    Vector("소켓 오프셋", m.muzzleOffset, v => m.muzzleOffset = v, "boss-muzzle-offset");
                }
                else { Float("비행 시간 (s)", m.flightSeconds, v => m.flightSeconds = v, "boss-flight-seconds"); Float("포물선 높이 (m)", m.arcHeight, v => m.arcHeight = v, "boss-arc-height"); }
                var distance = new FloatField("프리뷰 표적 거리 (m)") { value = preview?.TargetDistance ?? 12f, name = "boss-preview-target-distance", isDelayed = true };
                distance.RegisterValueChangedCallback(e => { if (preview != null && EnemyBossMaterialStrike.Finite(e.newValue) && e.newValue > 0f) { preview.TargetDistance = e.newValue; preview.Changed(); renderDirty = true; } }); fields.Add(distance);
                Note("파란 선은 원본 발사 자세의 소켓과 현재 프리뷰 표적을 잇는 궤적입니다. 표적 거리와 감상 재생은 게임 자산에 저장하지 않습니다.");
            }
            var notes = new TextField("조립 메모") { value = m.assemblyNotes, multiline = true, name = "boss-notes", isDelayed = true };
            notes.RegisterValueChangedCallback(e => Edit("조립 메모", () => m.assemblyNotes = e.newValue)); fields.Add(notes);
        }
        void Edit(string label, Action action, bool rebuild = false)
        { Draft.Edit(label, action); Changed(); if (rebuild) BuildFields(); }
        FloatField Float(string label, float value, Action<float> apply, string name)
        { var field = new FloatField(label) { value = value, name = name, isDelayed = true }; field.RegisterValueChangedCallback(e => Edit(label, () => apply(e.newValue), true)); FieldHost.Add(field); return field; }
        void Vector(string label, Vector3 value, Action<Vector3> apply, string name)
        { var field = new Vector3Field(label) { value = value, name = name }; field.RegisterValueChangedCallback(e => Edit(label, () => apply(e.newValue))); FieldHost.Add(field); }
        Toggle Toggle(string label, bool value, Action<bool> apply, string name)
        { var field = new Toggle(label) { value = value, name = name }; field.RegisterValueChangedCallback(e => Edit(label, () => apply(e.newValue), true)); FieldHost.Add(field); return field; }
        FloatField AbilityFloat(string label, string path, string name)
        {
            using (var serialized = new SerializedObject(Draft.Ability)) return Float(label, serialized.FindProperty(path).floatValue,
                v => { using (var edit = new SerializedObject(Draft.Ability)) { edit.FindProperty(path).floatValue = v; edit.ApplyModifiedPropertiesWithoutUndo(); } }, name);
        }
        bool AbilityBool(string path) { using (var serialized = new SerializedObject(Draft.Ability)) return serialized.FindProperty(path).boolValue; }
        void SetAbilityBool(string path, bool value) { using (var serialized = new SerializedObject(Draft.Ability)) { serialized.FindProperty(path).boolValue = value; serialized.ApplyModifiedPropertiesWithoutUndo(); } }
        void Note(string text) { var note = new Label(text); note.AddToClassList("bm-note"); fields.Add(note); }
    }
}
