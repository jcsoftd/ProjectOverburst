using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Overburst.EditorTools.MonsterTuner
{
    public sealed partial class MonsterTunerWindow
    {
        private VisualElement pointCard;
        private Label pointTitle, pointParent, pointSpace, pointWorld, pointHint;
        private Vector3Field pointLocal;
        private Button pointReset;
        private void BuildPointCard(VisualElement right)
        {
            pointCard = new VisualElement { name = "selected-point-card" }; pointCard.AddToClassList("mt-point-card");
            var header = new VisualElement(); header.AddToClassList("mt-row");
            pointTitle = new Label(); pointTitle.AddToClassList("mt-heading"); pointTitle.style.flexGrow = 1; header.Add(pointTitle);
            pointReset = new Button(ResetSelectedPoint) { name = "point-reset", text = "항목 되돌리기" }; header.Add(pointReset); pointCard.Add(header);
            pointParent = new Label { name = "point-parent" }; pointParent.AddToClassList("mt-note"); pointCard.Add(pointParent);
            pointSpace = new Label(); pointSpace.AddToClassList("mt-note"); pointCard.Add(pointSpace);
            pointLocal = new Vector3Field("저장 local") { name = "point-local" };
            pointLocal.Query<FloatField>().ForEach(f => f.isDelayed = true);
            pointLocal.RegisterValueChangedCallback(e => SetSelectedPointLocal(e.newValue)); pointCard.Add(pointLocal);
            pointWorld = new Label { name = "point-world" }; pointWorld.AddToClassList("mt-note"); pointCard.Add(pointWorld);
            pointHint = new Label(); pointHint.AddToClassList("mt-note"); pointCard.Add(pointHint); right.Add(pointCard);
            RefreshPointCard();
        }
        private bool PointAddress(out string target, out string property)
        {
            string key = viewport?.Selected?.Key; int separator = key?.LastIndexOf('|') ?? -1;
            target = separator >= 0 ? key.Substring(0, separator) : null;
            property = separator >= 0 ? key.Substring(separator + 1) : null;
            return session != null && stage.Actor != null && separator >= 0;
        }
        private EnemyAbilityDefinition SelectedSourceAbility => session?.Definition?.AbilitySet?.GetAbility(abilityIndex);
        private static MonsterTunerMuzzleValue MuzzleEntry(MonsterTunerValue value, EnemyAbilityDefinition ability)
            => value.muzzles?.FirstOrDefault(m => m.ability.Resolve() == ability);
        private MonsterTunerValue PointBaseline(string target, string property)
            => session.edits.Find(e => e.target == target && e.property == property)?.before ?? session.Value(target, property);
        private void RefreshPointCard()
        {
            if (pointCard == null) return;
            var point = viewport?.Selected;
            bool visible = point != null && session != null && stage.Actor != null;
            pointCard.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) return;
            pointTitle.text = point.Label; pointTitle.style.color = point.Color;
            pointWorld.text = "현재 world   " + XYZ(point.World()) + " m";
            pointLocal.style.display = DisplayStyle.Flex;
            bool editable = point.Move != null, changed = false;
            string space = "부모 local", hint = "숫자는 저장될 편집 사본 값입니다.";
            Transform parent = stage.Actor.transform;
            Vector3 local = Vector3.zero;
            if (PointAddress(out string target, out string property))
            {
                var component = MonsterTunerAddress.ResolveComponent(stage.Actor, target) as Component;
                parent = component is Transform anchor ? anchor.parent : component?.transform ?? parent;
                var value = session.Value(target, property);
                changed = session.edits.Any(e => e.target == target && e.property == property);
                if (property == "muzzleOverrides")
                {
                    var current = MuzzleEntry(value, SelectedSourceAbility);
                    var before = MuzzleEntry(PointBaseline(target, property), SelectedSourceAbility);
                    parent = current?.socket.Resolve(stage.Actor) as Transform;
                    local = current?.offset ?? Vector3.zero; space = "발사 뼈 local · " + SelectedSourceAbility?.AbilityId;
                    changed = JsonUtility.ToJson(current) != JsonUtility.ToJson(before);
                    if (current == null) { pointLocal.style.display = DisplayStyle.None; hint = "자동 발사점입니다. 공격 탭에서 몬스터별 머즐 조절을 켜세요."; }
                }
                else
                {
                    local = value.vector;
                    if (property == "cueOffset")
                    {
                        var socket = session.Value(target, "cueSocket").Resolve(stage.Actor) as Transform;
                        parent = socket ?? stage.Actor.transform;
                        space = socket != null ? "부착 뼈 local" : "자동 예고점 기준 · 몬스터 방향 오프셋";
                    }
                    else if (property.EndsWith("Offset", StringComparison.Ordinal)) space = "VFX 몸 중심 기준 · 몬스터 방향 오프셋";
                    else if (property == "hurtLocalCenter" || property == "localBodyCenter" || property == "localHitCenter")
                        space = "Actor local · 외형/체급 배율 적용 전";
                    else if (property == "m_Center") space = "Collider local";
                    if (!editable) hint = "자동 범위의 중심입니다. 아래 개별 범위 설정을 켜면 편집할 수 있습니다.";
                }
            }
            else { editable = false; pointLocal.style.display = DisplayStyle.None; space = "실효 공격 판정"; hint = "공격 탭의 반경·각도·높이 값으로 조절합니다."; }
            pointParent.text = "부모: " + (parent != null ? MonsterTunerAddress.Names(stage.Actor.transform, parent) : "자동 계산");
            pointParent.tooltip = pointParent.text;
            pointSpace.text = "좌표: " + space;
            pointLocal.SetValueWithoutNotify(local); pointLocal.SetEnabled(editable);
            pointReset.SetEnabled(changed); pointHint.text = hint;
        }
        private static string XYZ(Vector3 value) => value.x.ToString("F3") + " / " + value.y.ToString("F3") + " / " + value.z.ToString("F3");
        private void SetSelectedPointLocal(Vector3 vector)
        {
            if (viewport?.Selected?.Move == null || !PointAddress(out string target, out string property)) return;
            if (property == "muzzleOverrides")
            {
                var value = session.Value(target, property); var entry = MuzzleEntry(value, SelectedSourceAbility);
                if (entry == null) return; entry.offset = vector; Change(target, property, value, "머즐 XYZ");
            }
            else SetVector(target, property, vector, viewport.Selected.Label);
            BuildFields(); RefreshPointCard();
        }
        private void ResetSelectedPoint()
        {
            if (!PointAddress(out string target, out string property)) return;
            if (property == "muzzleOverrides")
            {
                var value = session.Value(target, property); var baseline = PointBaseline(target, property);
                var before = MuzzleEntry(baseline, SelectedSourceAbility); var entries = value.muzzles.ToList();
                int index = entries.FindIndex(m => m.ability.Resolve() == SelectedSourceAbility);
                if (before == null) { if (index >= 0) entries.RemoveAt(index); }
                else
                {
                    var restored = JsonUtility.FromJson<MonsterTunerMuzzleValue>(JsonUtility.ToJson(before));
                    if (index >= 0) entries[index] = restored;
                    else entries.Insert(Mathf.Clamp(Array.FindIndex(baseline.muzzles, m => m.ability.Resolve() == SelectedSourceAbility), 0, entries.Count), restored);
                }
                value.muzzles = entries.ToArray(); Change(target, property, value, "이 공격의 머즐 되돌리기");
            }
            else session.ResetField(target, property);
            stage.Load(session); RefreshPoints(); BuildFields(); UpdateHeader(); RenderNow();
        }
    }
}
