using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Overburst.EditorTools.MonsterTuner
{
    public sealed partial class MonsterTunerWindow
    {
        private bool linkScale = true;
        private MeleeElementStatusAuraType auraType = MeleeElementStatusAuraType.Burning;
        private int auraStacks = 1;
        private GameObject comparisonPrefab;
        private bool preserveHitSeconds;
        private void Heading(string text) { var label = new Label(text); label.AddToClassList("mt-heading"); fields.Add(label); }
        private void Note(string text) { var label = new Label(text); label.AddToClassList("mt-note"); fields.Add(label); }
        private void Change(string target, string property, MonsterTunerValue value, string label)
        {
            try
            {
                session.Set(target, property, value, label, !draggingPoint);
                stage.RefreshValues(); RefreshWorkingAbility(); UpdateHeader(); RenderNow();
            }
            catch (Exception e) { SetStatus(label + ": " + e.Message, true); }
        }
        private void Field(string target, string property, string label, bool enabled = true)
        {
            Object source = session.Source(target);
            var propertyInfo = source != null ? new SerializedObject(source).FindProperty(property) : null;
            if (propertyInfo == null && !target.Contains(":EnemyStrongAttackWarning,")) { Note(label + " 연결 없음"); return; }
            var value = session.Value(target, property);
            var row = new VisualElement(); row.AddToClassList("mt-row");
            row.name = target + "|" + property;
            VisualElement input;
            if (value.type == SerializedPropertyType.Float)
            {
                var field = new FloatField(label) { value = value.number, isDelayed = true };
                field.RegisterValueChangedCallback(e => { var next = session.Value(target, property); next.number = e.newValue; Change(target, property, next, label); }); input = field;
            }
            else if (value.type == SerializedPropertyType.Vector3)
            {
                var field = new Vector3Field(label) { value = value.vector };
                field.Query<FloatField>().ForEach(f => f.isDelayed = true);
                field.RegisterValueChangedCallback(e => { var next = session.Value(target, property); next.vector = e.newValue; Change(target, property, next, label); }); input = field;
            }
            else if (value.type == SerializedPropertyType.Boolean)
            {
                var field = new Toggle(label) { value = value.boolean };
                field.RegisterValueChangedCallback(e =>
                {
                    InitializeVolume(target, property, e.newValue);
                    var next = session.Value(target, property); next.boolean = e.newValue; Change(target, property, next, label); BuildFields(); RefreshPoints();
                }); input = field;
            }
            else if (value.type == SerializedPropertyType.Enum || value.type == SerializedPropertyType.Integer)
            {
                var field = new IntegerField(label) { value = value.integer, isDelayed = true };
                field.RegisterValueChangedCallback(e => { var next = session.Value(target, property); next.integer = e.newValue; Change(target, property, next, label); }); input = field;
            }
            else if (value.numbers != null)
            {
                var field = new TextField(label) { value = string.Join(", ", value.numbers.Select(f => f.ToString("0.###", CultureInfo.InvariantCulture))), isDelayed = true };
                field.RegisterValueChangedCallback(e =>
                {
                    try
                    {
                        var next = session.Value(target, property);
                        next.numbers = string.IsNullOrWhiteSpace(e.newValue) ? Array.Empty<float>() : e.newValue.Split(',').Select(s => float.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
                        Change(target, property, next, label);
                    }
                    catch { SetStatus("추가 타격은 0~1 비율을 쉼표로 구분해 입력하세요.", true); }
                }); input = field;
            }
            else return;
            input.style.flexGrow = 1; input.SetEnabled(enabled); row.Add(input);
            var reset = new Button(() => { session.ResetField(target, property); stage.Load(session); RefreshPoints(); BuildFields(); UpdateHeader(); }) { text = "↶", tooltip = "이 항목을 읽기 당시 값으로 되돌리기" };
            reset.SetEnabled(enabled); row.Add(reset); fields.Add(row);
        }
        private void SetVector(string target, string property, Vector3 vector, string label)
        {
            var value = session.Value(target, property); value.vector = vector; Change(target, property, value, label);
        }
        private void InitializeVolume(string target, string property, bool enabling)
        {
            if (!enabling || session.Value(target, property).boolean) return;
            CombatTargetVolume volume; string center, radius, height;
            var combat = stage.Actor.GetComponent<CombatTarget>(); var vfx = stage.Actor.GetComponent<CombatTargetVfxPlacement>();
            if (property == "useCustomHurtVolume") { volume = combat.CurrentHurtVolume; center = "hurtLocalCenter"; radius = "hurtRadius"; height = "hurtHeight"; }
            else if (property == "useAuthoredVolume") { volume = vfx.VisualVolume; center = "localBodyCenter"; radius = "bodyRadius"; height = "bodyHeight"; }
            else if (property == "useAuthoredHitVolume") { volume = vfx.HitVolume; center = "localHitCenter"; radius = "hitRadius"; height = "hitHeight"; }
            else return;
            var scale = session.Value("variant", "visualScale").vector * (session.Definition.Grade != null ? session.Definition.Grade.ScaleMultiplier : 1f);
            Vector3 local = stage.Actor.transform.InverseTransformPoint(volume.Center);
            var value = session.Value(target, center); value.vector = Divide(local, scale); session.Set(target, center, value, "현재 중심으로 초기화");
            value = session.Value(target, radius); value.number = volume.Radius / Mathf.Max(.01f, Mathf.Max(scale.x, scale.z)); session.Set(target, radius, value, "현재 반경으로 초기화");
            value = session.Value(target, height); value.number = volume.HalfHeight * 2f / Mathf.Max(.01f, scale.y); session.Set(target, height, value, "현재 높이로 초기화");
        }
        private static Vector3 Divide(Vector3 value, Vector3 scale) => new Vector3(value.x / Mathf.Max(.01f, scale.x), value.y / Mathf.Max(.01f, scale.y), value.z / Mathf.Max(.01f, scale.z));
        private void BuildScaleFields()
        {
            Heading("이 몬스터 크기");
            Note("배율을 올리면 커지고, 내리면 작아집니다. 1은 현재 몬스터의 기준 크기입니다.");
            var scale = new FloatField("전체 크기 (배율)") { value = session.Value("variant", "visualScale").vector.x, isDelayed = true };
            scale.RegisterValueChangedCallback(e =>
            {
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                float factor = e.newValue / Mathf.Max(.01f, e.previousValue);
                SetVector("variant", "visualScale", session.Value("variant", "visualScale").vector * factor, "외형 크기");
                if (linkScale) { SetVector("variant", "collisionScale", session.Value("variant", "collisionScale").vector * factor, "충돌 크기"); SetVector("variant", "anchorScale", session.Value("variant", "anchorScale").vector * factor, "기준점 크기"); }
                Undo.CollapseUndoOperations(group); BuildFields();
            }); fields.Add(scale);
            var link = new Toggle("위치와 충돌 범위도 함께 맞추기") { value = linkScale }; link.RegisterValueChangedCallback(e => linkScale = e.newValue); fields.Add(link);
            int detailed = fields.contentContainer.childCount;
            Note("체급 공통 배율 " + (session.Definition.Grade != null ? session.Definition.Grade.ScaleMultiplier.ToString("0.##") : "1")
                + " × 이 몬스터 배율. 공유 프로필은 저장할 때 한 마리용으로 분리합니다.");
            Heading("세부 배율 XYZ");
            Field("variant", "visualScale", "외형"); Field("variant", "collisionScale", "몸 충돌"); Field("variant", "anchorScale", "기준점");
            FoldDetails(detailed, "scale-details", "세부 크기 조절");
            var target = stage.Actor.GetComponent<CombatTarget>();
            if (target != null) Note("실효 피격 반경 " + target.CurrentHurtVolume.Radius.ToString("F2") + "m · 높이 " + (target.CurrentHurtVolume.HalfHeight * 2f).ToString("F2") + "m");
            fields.Add(new Button(() => { session.Discard(); stage.Load(session); RefreshPoints(); BuildFields(); UpdateHeader(); }) { text = "이 몬스터 변경 폐기" });
        }
        private void BuildPointFields()
        {
            BuildSelectedShapeFields();
            Heading("조절할 위치·범위 선택");
            foreach (var point in viewport.Points)
            {
                var selected = point;
                fields.Add(new Button(() => { viewport.Select(selected); SetStatus(selected.Label + " · 위치 " + selected.World().ToString("F3")); }) { text = point.Label, tooltip = point.OriginalName ?? point.Label });
            }
            int detailed = fields.contentContainer.childCount;
            if (stage.Enemy?.Anchors != null)
            {
                Heading("공격·부착 기준점");
                foreach (var anchor in stage.Enemy.Anchors.GetComponentsInChildren<Transform>(true).Where(t => t != stage.Enemy.Anchors))
                    Field(MonsterTunerAddress.Component(stage.Actor, anchor), "m_LocalPosition", AnchorLabel(anchor.name));
            }
            Heading("몸 충돌");
            foreach (var collider in stage.Actor.GetComponentsInChildren<Collider>(true))
            {
                string address = MonsterTunerAddress.Component(stage.Actor, collider);
                Note(MonsterTunerAddress.Names(stage.Actor.transform, collider.transform) + " · " + collider.GetType().Name);
                if (collider is CapsuleCollider) { Field(address, "m_Center", "중심"); Field(address, "m_Radius", "반경"); Field(address, "m_Height", "높이"); Field(address, "m_Direction", "축 0X / 1Y / 2Z"); }
                else if (collider is SphereCollider) { Field(address, "m_Center", "중심"); Field(address, "m_Radius", "반경"); }
                else if (collider is BoxCollider) { Field(address, "m_Center", "중심"); Field(address, "m_Size", "크기 XYZ"); }
                else Note("이 Collider 형상은 원본 Inspector에서 확인합니다.");
            }
            var target = stage.Actor.GetComponent<CombatTarget>();
            if (target != null)
            {
                string address = MonsterTunerAddress.Component(stage.Actor, target); Heading("피격 판정");
                Field(address, "useCustomHurtVolume", "개별 피격 범위"); bool authored = session.Value(address, "useCustomHurtVolume").boolean;
                Field(address, "hurtLocalCenter", "피격 중심", authored); Field(address, "hurtRadius", "피격 반경", authored); Field(address, "hurtHeight", "피격 높이", authored);
            }
            var placement = stage.Actor.GetComponent<CombatTargetVfxPlacement>();
            if (placement != null)
            {
                string address = MonsterTunerAddress.Component(stage.Actor, placement);
                Heading("VFX 몸 범위"); Field(address, "useAuthoredVolume", "개별 몸 범위"); bool body = session.Value(address, "useAuthoredVolume").boolean;
                Field(address, "localBodyCenter", "몸 중심", body); Field(address, "bodyRadius", "몸 반경", body); Field(address, "bodyHeight", "몸 높이", body);
                Heading("타격 VFX 접촉"); Field(address, "useAuthoredHitVolume", "개별 타격 범위"); bool hit = session.Value(address, "useAuthoredHitVolume").boolean;
                Field(address, "localHitCenter", "접촉 중심", hit); Field(address, "hitRadius", "접촉 반경", hit); Field(address, "hitHeight", "접촉 높이", hit);
                Field(address, "contactRadiusFraction", "접촉 반경 비율"); Field(address, "contactHeightFraction", "접촉 높이 비율");
            }
            FoldDetails(detailed, "point-details", "전체 위치·판정 세부 설정");
        }
        private static string AnchorLabel(string originalName)
        {
            switch (originalName)
            {
                case "AttackPoint": return "공격 기준점";
                case "HitVfxPoint": return "타격 효과 위치";
                case "HpBarAnchor": return "체력바 위치";
                case "GroundProbe": return "지면 확인 위치";
                default: return originalName;
            }
        }
        private void RefreshPoints()
        {
            viewport.Points.Clear(); if (stage.Actor == null || session == null) return;
            void Add(Component component, string property, string label, Color color, Func<Vector3> world, Func<Vector3, Vector3> local, Func<IEnumerable<(Vector3, Vector3)>> segments = null, bool editable = true)
            {
                string address = MonsterTunerAddress.Component(stage.Actor, component), key = address + "|" + property;
                viewport.Points.Add(new MonsterTunerPoint { Key = key, Label = label, OriginalName = component is Transform && label != component.name ? component.name : null,
                    Color = color, World = world, Segments = segments,
                    Move = editable ? position => SetVector(address, property, local(position), label) : (Action<Vector3>)null });
            }
            if (stage.Enemy?.Anchors != null) foreach (var anchor in stage.Enemy.Anchors.GetComponentsInChildren<Transform>(true).Where(t => t != stage.Enemy.Anchors))
                Add(anchor, "m_LocalPosition", AnchorLabel(anchor.name), new Color(.95f, .78f, .4f), () => anchor.position, p => anchor.parent.InverseTransformPoint(p));
            foreach (var collider in stage.Actor.GetComponentsInChildren<Collider>(true))
            {
                var captured = collider;
                if (collider is CapsuleCollider capsule) Add(collider, "m_Center", "몸 충돌 · " + collider.name, new Color(.4f, .82f, 1f), () => captured.transform.TransformPoint(capsule.center), p => captured.transform.InverseTransformPoint(p), () => ColliderSegments(captured));
                else if (collider is SphereCollider sphere) Add(collider, "m_Center", "몸 충돌 · " + collider.name, new Color(.4f, .82f, 1f), () => captured.transform.TransformPoint(sphere.center), p => captured.transform.InverseTransformPoint(p), () => ColliderSegments(captured));
                else if (collider is BoxCollider box) Add(collider, "m_Center", "몸 충돌 · " + collider.name, new Color(.4f, .82f, 1f), () => captured.transform.TransformPoint(box.center), p => captured.transform.InverseTransformPoint(p), () => ColliderSegments(captured));
            }
            Vector3 VisualScale() => session.Value("variant", "visualScale").vector * (session.Definition.Grade != null ? session.Definition.Grade.ScaleMultiplier : 1f);
            var target = stage.Actor.GetComponent<CombatTarget>();
            if (target != null) Add(target, "hurtLocalCenter", "피격 중심", new Color(1f, .48f, .55f), () => target.CurrentHurtVolume.Center,
                p => Divide(target.transform.InverseTransformPoint(p), VisualScale()), () => VolumeSegments(target.CurrentHurtVolume), target.HasCustomHurtVolume);
            var placement = stage.Actor.GetComponent<CombatTargetVfxPlacement>();
            if (placement != null)
            {
                var serialized = new SerializedObject(placement);
                Add(placement, "localBodyCenter", "VFX 몸 중심", new Color(.75f, .62f, 1f), () => placement.VisualVolume.Center,
                    p => Divide(placement.transform.InverseTransformPoint(p), VisualScale()), () => VolumeSegments(placement.VisualVolume), serialized.FindProperty("useAuthoredVolume").boolValue);
                Add(placement, "localHitCenter", "타격 VFX 중심", new Color(1f, .68f, .3f), () => placement.HitVolume.Center,
                    p => Divide(placement.transform.InverseTransformPoint(p), VisualScale()), () => VolumeSegments(placement.HitVolume), serialized.FindProperty("useAuthoredHitVolume").boolValue);
                foreach (string kind in new[] { "burn", "shock", "corrosion" })
                {
                    string property = kind + "Offset"; if (serialized.FindProperty(property) == null) continue;
                    string captured = property;
                    Add(placement, captured, (kind == "burn" ? "화상" : kind == "shock" ? "감전" : "잠식") + " 오라 보정", new Color(.9f, .75f, .35f),
                        () => placement.VisualVolume.Center + placement.transform.rotation * new SerializedObject(placement).FindProperty(captured).vector3Value,
                        p => Quaternion.Inverse(placement.transform.rotation) * (p - placement.VisualVolume.Center));
                }
            }
            RefreshWorkingAbility();
            var warning = stage.Actor.GetComponent<EnemyStrongAttackWarning>();
            if (warning != null && workingAbility != null && workingAbility.IsParryable)
                Add(warning, "cueOffset", "패링 예고 위치", new Color(1f, .93f, .4f), () => warning.ResolveCuePosition(stage.Camera), p =>
                {
                    var socket = new SerializedObject(warning).FindProperty("cueSocket").objectReferenceValue as Transform;
                    if (socket != null) return socket.InverseTransformPoint(p);
                    Vector3 offset = new SerializedObject(warning).FindProperty("cueOffset").vector3Value;
                    return offset + Quaternion.Inverse(warning.transform.rotation) * (p - warning.ResolveCuePosition(stage.Camera));
                });
            var executor = stage.Actor.GetComponent<EnemyThemeSpecialExecutor>();
            var originalAbility = session.Definition.AbilitySet?.GetAbility(abilityIndex);
            if (executor != null && originalAbility != null && originalAbility.ExecutionMode == EnemyAbilityExecutionMode.Projectile)
            {
                string address = MonsterTunerAddress.Component(stage.Actor, executor);
                bool editable = session.Value(address, "muzzleOverrides").muzzles.Any(m => m.ability.Resolve() == originalAbility);
                viewport.Points.Add(new MonsterTunerPoint
                {
                    Key = address + "|muzzleOverrides", Label = "원거리 머즐", Color = new Color(.4f, 1f, .75f),
                    World = () => executor.ResolveMuzzlePosition(originalAbility, stage.Actor.transform.forward),
                    Move = editable ? world =>
                    {
                        var value = session.Value(address, "muzzleOverrides"); var entry = value.muzzles.First(m => m.ability.Resolve() == originalAbility);
                        var socket = entry.socket.Resolve(stage.Actor) as Transform; entry.offset = socket.InverseTransformPoint(world);
                        Change(address, "muzzleOverrides", value, "머즐 위치");
                    } : (Action<Vector3>)null
                });
            }
            if (workingAbility != null && EnemyAbilityDefinition.IsMeleeExecution(workingAbility.ExecutionMode))
                viewport.Points.Add(new MonsterTunerPoint
                {
                    Key = "attack-volume", Label = "공격·패링 가능 범위", Color = new Color(1f, .38f, .3f),
                    World = () => workingAbility.ExecutionMode == EnemyAbilityExecutionMode.AreaSlam ? stage.Actor.transform.position : stage.Enemy.Melee.AttackPoint.position,
                    Segments = () => AttackSegments(stage.Enemy, workingAbility)
                });
            if (selectedPointKey != null) viewport.Select(viewport.Points.Find(p => p.Key == selectedPointKey), false);
            RefreshLegend();
            viewport.Refresh(); RefreshPointCard();
        }
        private static IEnumerable<(Vector3, Vector3)> AttackSegments(EnemyActor actor, EnemyAbilityDefinition ability)
        {
            Vector3 center = ability.ExecutionMode == EnemyAbilityExecutionMode.AreaSlam ? actor.transform.position : actor.Melee.AttackPoint.position;
            float radius = EnemyAttackThreatGeometry.ResolveRadius(actor, ability), angle = EnemyAttackThreatGeometry.ResolveHitAngle(actor, ability);
            if (ability.ExecutionMode == EnemyAbilityExecutionMode.Charge)
            {
                center = actor.transform.position + Vector3.up * .8f;
                Vector3 forward = actor.transform.forward * Mathf.Max(.8f, radius), side = actor.transform.right * .4f;
                yield return (center - side, center + forward - side); yield return (center + side, center + forward + side);
                foreach (var segment in CapsuleSegments(center + forward * .5f, actor.transform.rotation * Quaternion.FromToRotation(Vector3.up, Vector3.forward), .4f, forward.magnitude * .5f)) yield return segment;
                yield break;
            }
            Vector3 First(float degrees) => center + Quaternion.AngleAxis(degrees, Vector3.up) * actor.transform.forward * radius;
            for (int i = 0; i < 48; i++) yield return (First(-angle * .5f + angle * i / 48f), First(-angle * .5f + angle * (i + 1) / 48f));
            if (angle < 359.9f) { yield return (center, First(-angle * .5f)); yield return (center, First(angle * .5f)); }
        }
        private static IEnumerable<(Vector3, Vector3)> VolumeSegments(CombatTargetVolume volume)
        {
            const int count = 36;
            for (int ring = 0; ring < 3; ring++) for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count, b = (i + 1) * Mathf.PI * 2f / count;
                Vector3 Point(float angle) => ring == 0 ? new Vector3(Mathf.Cos(angle) * volume.Radius, 0f, Mathf.Sin(angle) * volume.Radius)
                    : ring == 1 ? new Vector3(Mathf.Cos(angle) * volume.Radius, Mathf.Sin(angle) * volume.HalfHeight, 0f)
                    : new Vector3(0f, Mathf.Sin(angle) * volume.HalfHeight, Mathf.Cos(angle) * volume.Radius);
                yield return (volume.Center + Point(a), volume.Center + Point(b));
            }
        }
        private static IEnumerable<(Vector3, Vector3)> ColliderSegments(Collider collider)
        {
            if (collider is BoxCollider box)
            {
                var corners = new Vector3[8];
                for (int i = 0; i < 8; i++) corners[i] = box.transform.TransformPoint(box.center + Vector3.Scale(box.size * .5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                for (int i = 0; i < 8; i++) for (int axis = 0; axis < 3; axis++) if ((i & (1 << axis)) == 0) yield return (corners[i], corners[i | (1 << axis)]);
                yield break;
            }
            if (collider is CapsuleCollider capsule)
            {
                var scale = capsule.transform.lossyScale; scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                int axis = capsule.direction;
                float radius = capsule.radius * Mathf.Max(scale[(axis + 1) % 3], scale[(axis + 2) % 3]);
                float half = Mathf.Max(0f, capsule.height * scale[axis] * .5f - radius);
                var rotation = capsule.transform.rotation * Quaternion.FromToRotation(Vector3.up, axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward);
                foreach (var segment in CapsuleSegments(capsule.transform.TransformPoint(capsule.center), rotation, radius, half)) yield return segment;
            }
            else if (collider is SphereCollider sphere)
            {
                var scale = sphere.transform.lossyScale;
                float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                foreach (var segment in CapsuleSegments(sphere.transform.TransformPoint(sphere.center), Quaternion.identity, radius, 0f)) yield return segment;
            }
        }
        private static IEnumerable<(Vector3, Vector3)> CapsuleSegments(Vector3 center, Quaternion rotation, float radius, float halfCylinder)
        {
            Vector3 World(Vector3 local) => center + rotation * local;
            const int count = 36;
            for (int ring = 0; ring < 2; ring++) for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count, b = (i + 1) * Mathf.PI * 2f / count;
                Vector3 P(float angle) => new Vector3(Mathf.Cos(angle) * radius, ring == 0 ? halfCylinder : -halfCylinder, Mathf.Sin(angle) * radius);
                yield return (World(P(a)), World(P(b)));
            }
            for (int plane = 0; plane < 2; plane++)
            {
                Vector3 P(float angle, bool top) => (plane == 0 ? Vector3.right : Vector3.forward) * Mathf.Cos(angle) * radius
                    + Vector3.up * (Mathf.Sin(angle) * radius + (top ? halfCylinder : -halfCylinder));
                for (int i = 0; i < 18; i++)
                {
                    float a = i * Mathf.PI / 18f, b = (i + 1) * Mathf.PI / 18f;
                    yield return (World(P(a, true)), World(P(b, true)));
                    yield return (World(P(a + Mathf.PI, false)), World(P(b + Mathf.PI, false)));
                }
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 edge = (plane == 0 ? Vector3.right : Vector3.forward) * side * radius;
                    yield return (World(edge + Vector3.up * halfCylinder), World(edge - Vector3.up * halfCylinder));
                }
            }
        }
        private void BuildAuraFields()
        {
            Heading("상태 오라 프리뷰");
            var types = new DropdownField("종류", new List<string> { "화상", "감전", "잠식" }, auraType == MeleeElementStatusAuraType.Burning ? 0 : auraType == MeleeElementStatusAuraType.Shocked ? 1 : 2);
            types.RegisterValueChangedCallback(_ => { auraType = new[] { MeleeElementStatusAuraType.Burning, MeleeElementStatusAuraType.Shocked, MeleeElementStatusAuraType.Corroded }[types.index]; PlayAura(); BuildFields(); }); fields.Add(types);
            var stacks = new SliderInt("중첩", 1, 5) { value = auraStacks }; stacks.RegisterValueChangedCallback(e => { auraStacks = e.newValue; PlayAura(); }); fields.Add(stacks);
            fields.Add(new Button(PlayAura) { text = "오라 재생 / 처음부터" });
            fields.Add(new Button(() => stage.ClearAura()) { text = "오라 끄기" });
            var compare = new ObjectField("비교용 효과") { objectType = typeof(GameObject), allowSceneObjects = false, value = comparisonPrefab };
            compare.RegisterValueChangedCallback(e => { comparisonPrefab = e.newValue as GameObject; PlayAura(); }); fields.Add(compare);
            Note("비교용 효과는 프리뷰에서만 사용합니다. 저장하는 값은 이 몬스터의 위치·크기 보정입니다.");
            if (stage.HasGraphEffects) Note("VFX Graph는 연속 재생을 지원합니다. 역스크럽 시 효과를 재시작하므로 입자 모양은 이전 프레임과 다를 수 있습니다.");
            var placement = stage.Actor.GetComponent<CombatTargetVfxPlacement>();
            if (placement == null) return;
            string address = MonsterTunerAddress.Component(stage.Actor, placement), prefix = auraType == MeleeElementStatusAuraType.Burning ? "burn" : auraType == MeleeElementStatusAuraType.Shocked ? "shock" : "corrosion";
            Heading("이 몬스터 보정"); Field(address, prefix + "Offset", "위치 보정 m"); Field(address, prefix + "Scale", "크기 배율");
        }
        private void PlayAura() { stage.ShowAura(auraType, auraStacks, comparisonPrefab); stage.Playing = true; RenderNow(); }
        private void RefreshWorkingAbility()
        {
            var source = session?.Definition?.AbilitySet?.GetAbility(abilityIndex);
            if (source == null) return;
            if (workingAbility == null) { workingAbility = Object.Instantiate(source); workingAbility.hideFlags = HideFlags.HideAndDontSave; }
            else EditorUtility.CopySerialized(source, workingAbility);
            session.Apply(workingAbility, "ability:" + abilityIndex);
        }
        private void BuildAttackFields()
        {
            Heading("연결된 공격"); var set = session.Definition.AbilitySet;
            if (set == null || set.Count == 0) { Note("연결된 공격이 없습니다."); return; }
            abilityIndex = Mathf.Clamp(abilityIndex, 0, set.Count - 1);
            var choices = Enumerable.Range(0, set.Count).Select(i => (i + 1) + ". " + (set.GetAbility(i) != null ? set.GetAbility(i).AbilityId : "누락")).ToList();
            var attacks = new DropdownField("공격", choices, abilityIndex);
            attacks.RegisterValueChangedCallback(_ => { abilityIndex = attacks.index; RefreshWorkingAbility(); PreviewAttack(); BuildFields(); RefreshPoints(); }); fields.Add(attacks);
            RefreshWorkingAbility(); if (workingAbility == null) return;
            Note(workingAbility.ExecutionMode + " · " + (workingAbility.IsParryable ? "패링 가능한 근접 강공" : "패링 신호 없음"));
            fields.Add(new Button(PreviewAttack) { text = "공격 모션 재생" });
            string address = "ability:" + abilityIndex;
            int timingDetails = fields.contentContainer.childCount;
            Heading("공격 시간"); Field(address, "attackAnimationDuration", "모션 기준 초"); Field(address, "hitNormalizedTime", "첫 타격 비율"); Field(address, "additionalHitNormalizedTimes", "추가 타격 비율");
            if (workingAbility.UsesPacedTimeline) { Field(address, "preparationDuration", "준비 초"); Field(address, "releaseDuration", "발동 초"); Field(address, "recoveryDuration", "회수 초"); }
            Field(address, "minimumWarningTime", "최소 예고 초"); Field(address, "minimumRecoveryTime", "최소 회수 초");
            FoldDetails(timingDetails, "attack-timing-details", "타격 시점·준비·회복 시간 조절");
            Heading("공격 판정"); Field(address, "hitRadius", "원본 반경"); Field(address, "hitAngle", "원본 각도"); Field(address, "verticalTolerance", "높이 허용"); Field(address, "range", "발동 거리");
            Note("실효 반경 " + EnemyAttackThreatGeometry.ResolveRadius(stage.Enemy, workingAbility).ToString("F2") + "m · 각도 " + EnemyAttackThreatGeometry.ResolveHitAngle(stage.Enemy, workingAbility).ToString("F0") + "°");
            Note("시간축은 1레벨·상태이상 없는 실전 공격 속도 기준입니다. 재생 배속은 프리뷰에만 적용합니다.");
            for (int i = 0; i < workingAbility.HitCount; i++) Note("타격 " + (i + 1) + " · " + (workingAbility.ResolveWindupDelay(stage.AttackSpeed) + workingAbility.ResolvePacedTime(workingAbility.GetHitNormalizedTime(i), stage.AttackSpeed)).ToString("F3") + "s");
            BuildAttackPlacementFields();
        }
        private void PreviewAttack()
        {
            RefreshWorkingAbility(); if (workingAbility == null) return;
            var clip = MonsterTunerAnimationBindings.AttackClip(session, workingAbility, abilityIndex);
            stage.SetClip(clip, workingAbility); stage.Playing = true; RenderNow();
        }
        private void BuildAttackPlacementFields()
        {
            var warning = stage.Actor.GetComponent<EnemyStrongAttackWarning>();
            if (workingAbility.IsParryable && warning != null)
            {
                string address = MonsterTunerAddress.Component(stage.Actor, warning); Heading("패링 예고 위치");
                SocketField(address, "cueSocket", "cueOffset", "부착 뼈", true);
                Field(address, "cueOffset", "예고 보정"); Field(address, "cueScale", "예고 크기");
                Note("예고 빛의 배치를 조절합니다. 패링 가능 범위는 공격 피해 판정을 따릅니다.");
            }
            var executor = stage.Actor.GetComponent<EnemyThemeSpecialExecutor>();
            if (workingAbility.ExecutionMode != EnemyAbilityExecutionMode.Projectile || executor == null) return;
            Heading("이 공격의 발사점");
            string target = MonsterTunerAddress.Component(stage.Actor, executor);
            var originalAbility = session.Definition.AbilitySet.GetAbility(abilityIndex);
            var array = session.Value(target, "muzzleOverrides");
            var current = array.muzzles.FirstOrDefault(m => m.ability.Resolve() == originalAbility);
            var enabled = new Toggle("몬스터별 머즐 조절") { value = current != null };
            enabled.RegisterValueChangedCallback(e =>
            {
                Vector3 world = executor.ResolveMuzzlePosition(originalAbility, stage.Actor.transform.forward);
                var next = session.Value(target, "muzzleOverrides");
                var entries = next.muzzles.Where(m => m.ability.Resolve() != originalAbility).ToList();
                if (e.newValue) entries.Add(new MonsterTunerMuzzleValue
                {
                    ability = new MonsterTunerValue { type = SerializedPropertyType.ObjectReference, text = GlobalObjectId.GetGlobalObjectIdSlow(originalAbility).ToString() },
                    socket = new MonsterTunerValue { type = SerializedPropertyType.ObjectReference, text = "actor:" },
                    offset = stage.Actor.transform.InverseTransformPoint(world)
                });
                next.muzzles = entries.ToArray(); Change(target, "muzzleOverrides", next, "머즐 개별 조절"); BuildFields(); RefreshPoints();
            }); fields.Add(enabled);
            if (current != null)
            {
                var nodes = stage.Actor.GetComponentsInChildren<Transform>(true).ToList();
                var names = nodes.Select(t => MonsterTunerAddress.Names(stage.Actor.transform, t)).ToList();
                var selected = current.socket.Resolve(stage.Actor) as Transform;
                var bones = new DropdownField("발사 뼈", names, Mathf.Max(0, nodes.IndexOf(selected)));
                bones.RegisterValueChangedCallback(_ =>
                {
                    Vector3 world = executor.ResolveMuzzlePosition(originalAbility, stage.Actor.transform.forward);
                    var next = session.Value(target, "muzzleOverrides"); var entry = next.muzzles.First(m => m.ability.Resolve() == originalAbility);
                    entry.socket.text = "actor:" + MonsterTunerAddress.Path(stage.Actor.transform, nodes[bones.index]); entry.offset = nodes[bones.index].InverseTransformPoint(world);
                    Change(target, "muzzleOverrides", next, "머즐 부착 뼈"); BuildFields(); RefreshPoints();
                }); fields.Add(bones);
                var offset = new Vector3Field("머즐 XYZ") { value = current.offset }; offset.Query<FloatField>().ForEach(f => f.isDelayed = true);
                offset.RegisterValueChangedCallback(e =>
                {
                    var next = session.Value(target, "muzzleOverrides"); next.muzzles.First(m => m.ability.Resolve() == originalAbility).offset = e.newValue;
                    Change(target, "muzzleOverrides", next, "머즐 XYZ"); RefreshPoints();
                }); fields.Add(offset);
            }
            Note("현재 머즐 " + executor.ResolveMuzzlePosition(originalAbility, stage.Actor.transform.forward).ToString("F3") + "m · 발사 방향은 기존 목표 방향을 유지합니다.");
        }
        private void SocketField(string target, string property, string offsetProperty, string label, bool automatic)
        {
            var nodes = stage.Actor.GetComponentsInChildren<Transform>(true).ToList();
            var names = nodes.Select(t => MonsterTunerAddress.Names(stage.Actor.transform, t)).ToList();
            if (automatic) names.Insert(0, "자동 · 몸 꼭대기");
            var current = session.Value(target, property).Resolve(stage.Actor) as Transform;
            int index = current != null ? nodes.IndexOf(current) + (automatic ? 1 : 0) : 0;
            var field = new DropdownField(label, names, Mathf.Clamp(index, 0, names.Count - 1));
            field.RegisterValueChangedCallback(_ =>
            {
                var warning = stage.Actor.GetComponent<EnemyStrongAttackWarning>(); Vector3 world = warning.ResolveCuePosition(stage.Camera);
                var next = session.Value(target, property);
                var node = automatic && field.index == 0 ? null : nodes[field.index - (automatic ? 1 : 0)];
                next.text = node != null ? "actor:" + MonsterTunerAddress.Path(stage.Actor.transform, node) : string.Empty;
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                Change(target, property, next, label);
                var offset = session.Value(target, offsetProperty);
                offset.vector = node != null ? node.InverseTransformPoint(world)
                    : offset.vector + Quaternion.Inverse(stage.Actor.transform.rotation) * (world - warning.ResolveCuePosition(stage.Camera));
                Change(target, offsetProperty, offset, "부착점 변경 시 위치 유지"); Undo.CollapseUndoOperations(group); BuildFields(); RefreshPoints();
            }); fields.Add(field);
        }
        private void BuildAnimationFields()
        {
            Heading("동작 미리보기"); var profile = session.Definition.AnimationProfile;
            if (profile == null) { Note("애니메이션 프로필이 없습니다."); return; }
            var bindings = MonsterTunerAnimationBindings.Read(profile);
            BuildHitPreview(bindings); BuildParryPreview(bindings);
            int detailed = fields.contentContainer.childCount;
            Note("Controller · " + (profile.RuntimeController != null ? profile.RuntimeController.name : "누락"));
            var timing = new DropdownField("공격 교체 시", new List<string> { "타격 비율 유지", "현재 타격 초 유지" }, preserveHitSeconds ? 1 : 0);
            timing.RegisterValueChangedCallback(_ => preserveHitSeconds = timing.index == 1); fields.Add(timing);
            Note("공격 클립 교체 시 기준 길이를 새 클립에 맞춥니다. 준비·발동·회수 시간을 따로 쓰는 공격은 그 시간을 유지합니다.");
            var main = bindings.Where(IsMainMotion).ToList();
            var auxiliary = bindings.Where(b => !IsMainMotion(b)).ToList();
            Heading("주요 모션 · " + main.Count + "개");
            foreach (var binding in main) MotionSlot(binding, fields);
            if (auxiliary.Count > 0)
            {
                var fold = new Foldout { name = "auxiliary-motions", text = "보조 모션 · 회전 / BlendTree 등 " + auxiliary.Count + "개", value = auxiliaryMotionsExpanded };
                fold.RegisterValueChangedCallback(e => auxiliaryMotionsExpanded = e.newValue); fields.Add(fold);
                foreach (var binding in auxiliary) MotionSlot(binding, fold);
            }
            if (bindings.Count == 0) Note("지원하는 기존 Controller 상태를 찾지 못했습니다. 원본 Controller를 확인하세요.");
            Note("클립 원본은 읽기 전용입니다. 교체는 이 몬스터의 프로필과 실제 Controller 연결에 저장합니다.");
            FoldDetails(detailed, "motion-editing", "모션 연결 바꾸기");
        }
        [SerializeField] private bool auxiliaryMotionsExpanded;
        private bool IsMainMotion(MonsterTunerAnimationBindings.Binding binding)
        {
            if (new[] { "대기", "걷기", "달리기", "피격", "사망" }.Contains(binding.Label) || binding.Label.StartsWith("패링", StringComparison.Ordinal) || MonsterTunerAnimationBindings.IsHit(binding)) return true;
            string state = binding.StatePath.Split('.').Last();
            if (state.StartsWith("Attack_", StringComparison.OrdinalIgnoreCase)) return true;
            for (int i = 0; session.Definition.AbilitySet != null && i < session.Definition.AbilitySet.Count; i++)
                if (session.Definition.AbilitySet.GetAbility(i)?.AnimatorTrigger == state) return true;
            return false;
        }
        private void MotionSlot(MonsterTunerAnimationBindings.Binding binding, VisualElement container)
        {
            var slot = new VisualElement { name = "motion-slot:" + binding.Key }; slot.AddToClassList("mt-motion-slot"); container.Add(slot);
            AnimationSlot("motion:" + binding.Key, binding.Label, slot);
            var path = new Label(binding.StatePath + (binding.Children.Length > 0 ? " · BlendTree " + string.Join("/", binding.Children) : string.Empty));
            path.AddToClassList("mt-note"); slot.Add(path);
        }
        private void AnimationSlot(string property, string label, VisualElement container = null)
        {
            container = container ?? fields;
            var value = session.Value("animation", property); var clip = value.Resolve() as AnimationClip;
            var field = new ObjectField(label) { objectType = typeof(AnimationClip), allowSceneObjects = false, value = clip };
            field.RegisterValueChangedCallback(e =>
            {
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                var next = session.Value("animation", property); next.text = e.newValue != null ? GlobalObjectId.GetGlobalObjectIdSlow(e.newValue).ToString() : string.Empty;
                Change("animation", property, next, label + " 교체");
                UpdateAttackClipTiming(property, e.newValue as AnimationClip);
                Undo.CollapseUndoOperations(group); stage.SetClip(e.newValue as AnimationClip); RenderNow(); BuildFields();
            }); container.Add(field);
            if (clip != null)
            {
                container.Add(new Button(() => { stage.SetClip(clip); stage.Playing = true; RenderNow(); }) { text = "▶ " + clip.name });
                AddNote(clip.length.ToString("F2") + "s · " + clip.frameRate.ToString("F0") + "fps · " + (clip.isLooping ? "반복" : "단발"));
                string compatibility = MonsterTunerAnimationBindings.Compatibility(session.Definition, clip);
                AddNote(string.IsNullOrEmpty(compatibility) ? "Avatar/모델 뼈 경로 호환 확인" : compatibility);
            }
            void AddNote(string text) { var note = new Label(text); note.AddToClassList("mt-note"); container.Add(note); }
        }
        private void UpdateAttackClipTiming(string property, AnimationClip replacement)
        {
            if (replacement == null || !property.StartsWith("motion:", StringComparison.Ordinal)) return;
            var binding = MonsterTunerAnimationBindings.Read(session.Definition.AnimationProfile).Find(b => b.Key == property.Substring(7));
            if (binding == null || !binding.StatePath.Contains(".Attack_")) return;
            for (int i = 0; session.Definition.AbilitySet != null && i < session.Definition.AbilitySet.Count; i++)
            {
                var source = session.Definition.AbilitySet.GetAbility(i); if (source == null) continue;
                string state = source.AnimatorTrigger.StartsWith("Attack", StringComparison.Ordinal) ? "Attack_" + source.AnimatorTrigger.Substring(6) : source.AnimatorTrigger;
                if (!binding.StatePath.EndsWith("." + state, StringComparison.Ordinal)) continue;
                var attack = Object.Instantiate(source);
                try
                {
                    string target = "ability:" + i; session.Apply(attack, target);
                    float ratio = attack.AttackAnimationDuration / Mathf.Max(.001f, replacement.length);
                    var duration = session.Value(target, "attackAnimationDuration"); duration.number = replacement.length; Change(target, "attackAnimationDuration", duration, "교체 모션 기준 초");
                    if (preserveHitSeconds && !attack.UsesPacedTimeline)
                    {
                        var hit = session.Value(target, "hitNormalizedTime"); hit.number *= ratio; Change(target, "hitNormalizedTime", hit, "타격 초 유지");
                        var extra = session.Value(target, "additionalHitNormalizedTimes"); extra.numbers = extra.numbers.Select(t => t * ratio).ToArray(); Change(target, "additionalHitNormalizedTimes", extra, "추가 타격 초 유지");
                    }
                }
                finally { Object.DestroyImmediate(attack); }
            }
        }
    }
}
