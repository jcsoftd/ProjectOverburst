using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Overburst.EditorTools.MonsterTuner
{
    internal sealed class MonsterTunerTimeline : VisualElement
    {
        private readonly MonsterTunerPreviewStage stage;
        private readonly Label legend;
        public MonsterTunerTimeline(MonsterTunerPreviewStage preview)
        {
            stage = preview; style.height = 50; style.flexShrink = 0;
            legend = new Label { pickingMode = PickingMode.Ignore };
            legend.style.fontSize = 10; legend.style.marginTop = 29; Add(legend);
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                stage.Playing = false;
                stage.Sample(Mathf.Clamp01(this.WorldToLocal(e.position).x / Mathf.Max(1f, contentRect.width)) * stage.Duration);
                MarkDirtyRepaint(); e.StopPropagation();
            });
        }
        public void Refresh()
        {
            legend.text = stage.Ability == null ? "모션 시간" : "준비 · 발동 · 회수   | 타격/발사   ■ 패링 가능 구간";
            MarkDirtyRepaint();
        }
        private void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D; float width = contentRect.width;
            float X(float time) => Mathf.Clamp01(time / Mathf.Max(.001f, stage.Duration)) * width;
            void Band(float start, float end, Color color, float y, float height)
            {
                painter.fillColor = color; painter.BeginPath(); painter.MoveTo(new Vector2(X(start), y));
                painter.LineTo(new Vector2(X(end), y)); painter.LineTo(new Vector2(X(end), y + height));
                painter.LineTo(new Vector2(X(start), y + height)); painter.ClosePath(); painter.Fill();
            }
            void Mark(float time, Color color, float lineWidth)
            {
                painter.strokeColor = color; painter.lineWidth = lineWidth; painter.BeginPath();
                painter.MoveTo(new Vector2(X(time), 1)); painter.LineTo(new Vector2(X(time), 26)); painter.Stroke();
            }
            var ability = stage.Ability;
            if (ability != null)
            {
                float windup = ability.ResolveWindupDelay(stage.AttackSpeed);
                float prep = windup + ability.ResolvePacedTime(Mathf.Max(.02f, ability.HitNormalizedTime - .12f), stage.AttackSpeed);
                float last = ability.ResolveLastImpactTime(stage.AttackSpeed);
                Band(0, prep, new Color(.28f, .38f, .48f), 6, 12);
                Band(prep, last, new Color(.73f, .42f, .24f), 6, 12);
                Band(last, stage.Duration, new Color(.28f, .45f, .37f), 6, 12);
                for (int i = 0; i < ability.HitCount; i++)
                {
                    float hit = windup + ability.ResolvePacedTime(ability.GetHitNormalizedTime(i), stage.AttackSpeed);
                    if (ability.IsParryable) Band(Mathf.Max(0, hit - EnemyAbilityController.ParryLeadSeconds), hit, new Color(1f, .86f, .3f), 21, 3);
                    Mark(hit, new Color(1f, .73f, .4f), 2);
                }
            }
            else Band(0, stage.Duration, new Color(.28f, .38f, .48f), 6, 12);
            Mark(stage.Time, Color.white, 2);
        }
    }
}
