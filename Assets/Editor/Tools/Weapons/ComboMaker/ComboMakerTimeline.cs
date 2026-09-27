using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Overburst.EditorTools.ComboMaker
{
    internal sealed class ComboMakerTimeline : VisualElement
    {
        private const float LabelWidth=46,Top=20,LaneHeight=19;
        private readonly Action<float> seek;
        private readonly Action<int,int,bool,float> edit;
        private MeleeComboDefinition combo;
        private int stepIndex,dragPhase=-1;
        private bool dragEnd,dragging;
        private float progress;
        private bool heavyMode;
        private Label continuationLabel;
        private Func<int,float,float> cueTime;
        public ComboMakerTimeline(Action<float> onSeek,Action<int,int,bool,float> onEdit)
        {
            seek=onSeek;edit=onEdit;focusable=true;style.height=140;style.flexShrink=0;
            tooltip="클릭/드래그: 시간 이동 · 주황 판정의 양 끝: 시작/종료 시점 편집";
            string[] labels={"판정","VFX","이동","트레일","연계","취소"};
            for(int i=0;i<labels.Length;i++)
            {var l=new Label(labels[i]){pickingMode=PickingMode.Ignore};l.style.position=Position.Absolute;l.style.left=0;l.style.top=Top+i*LaneHeight;l.style.fontSize=10;Add(l);if(i==4)continuationLabel=l;}
            generateVisualContent+=Draw;
            RegisterCallback<PointerDownEvent>(e=>
            {
                if(e.button!=0||combo==null)return;dragPhase=-1;
                if(e.localPosition.y>=Top && e.localPosition.y<Top+LaneHeight)
                {
                    var phases=combo.steps[stepIndex].attackPhases??Array.Empty<AttackPhaseData>();
                    for(int i=0;i<phases.Length;i++)
                    {
                        if(Mathf.Abs(e.localPosition.x-X(phases[i].SafeStart))<6){dragPhase=i;dragEnd=false;break;}
                        if(Mathf.Abs(e.localPosition.x-X(phases[i].SafeEnd))<6){dragPhase=i;dragEnd=true;break;}
                    }
                }
                dragging=true;this.CapturePointer(e.pointerId);Focus();Move(e.localPosition.x);e.StopPropagation();
            });
            RegisterCallback<PointerMoveEvent>(e=>{if(dragging&&this.HasPointerCapture(e.pointerId))Move(e.localPosition.x);});
            RegisterCallback<PointerUpEvent>(e=>{dragging=false;this.ReleasePointer(e.pointerId);});
            RegisterCallback<PointerCaptureOutEvent>(e=>dragging=false);
        }
        public void SetData(MeleeComboDefinition data,int step,float value,Func<int,float,float> resolveCue,bool heavy=false)
        {combo=data;stepIndex=step;progress=value;cueTime=resolveCue;heavyMode=heavy;continuationLabel.text=heavy?"방출":"연계";MarkDirtyRepaint();}
        private float X(float t)=>LabelWidth+t*Mathf.Max(1,contentRect.width-LabelWidth-8);
        private void Move(float x)
        {
            float t=Mathf.Clamp01((x-LabelWidth)/Mathf.Max(1,contentRect.width-LabelWidth-8));
            if(dragPhase<0)seek(t);
            else
            {
                edit(stepIndex,dragPhase,dragEnd,t);
                var step=combo.steps[stepIndex];var phase=step.attackPhases[dragPhase];
                if(dragEnd)phase.endNormalizedTime=Mathf.Clamp(t,phase.SafeStart+.001f,1);
                else phase.startNormalizedTime=Mathf.Clamp(t,0,phase.SafeEnd-.001f);
                step.attackPhases[dragPhase]=phase;combo.steps[stepIndex]=step;MarkDirtyRepaint();
            }
        }
        private void Draw(MeshGenerationContext context)
        {
            if(combo==null||stepIndex>=combo.StepCount)return;
            var painter=context.painter2D;var step=combo.steps[stepIndex];
            painter.lineWidth=1;painter.strokeColor=new Color(.26f,.29f,.33f);
            for(int i=0;i<=4;i++){float x=X(i/4f);painter.BeginPath();painter.MoveTo(new Vector2(x,Top));painter.LineTo(new Vector2(x,Top+6*LaneHeight));painter.Stroke();}
            foreach(var phase in step.attackPhases??Array.Empty<AttackPhaseData>())
            {Bar(painter,0,phase.SafeStart,phase.SafeEnd,new Color(.95f,.61f,.27f));Bar(painter,0,phase.SafeStart,phase.SafeStart+.004f,Color.white);Bar(painter,0,phase.SafeEnd-.004f,phase.SafeEnd,Color.white);}
            for(int i=0;i<(step.attackPhases?.Length??0);i++)
                foreach(var cue in step.attackPhases[i].vfxCues??Array.Empty<AttackVfxCueData>())
                {float t=cueTime!=null?cueTime(i,cue.triggerProgress):0;Bar(painter,1,t,t+.004f,new Color(.92f,.55f,.86f));}
            foreach(var move in step.movementPhases??Array.Empty<AttackMovementPhaseData>())Bar(painter,2,move.SafeStart,move.SafeEnd,new Color(.67f,.75f,.40f));
            foreach(var trail in step.trailPhases??Array.Empty<AttackTrailPhaseData>())Bar(painter,3,trail.SafeStart,trail.SafeEnd,new Color(.65f,.56f,.87f));
            if(heavyMode && step.attackPhases!=null && step.attackPhases.Length>0)
                Bar(painter,4,step.attackPhases[0].SafeStart,step.attackPhases[0].SafeStart+.006f,new Color(.39f,.77f,.74f));
            else Bar(painter,4,step.comboInputWindow.SafeStart,step.comboInputWindow.SafeEnd,new Color(.39f,.77f,.74f));
            Bar(painter,5,step.actionCancelStartNormalized,1,new Color(.46f,.61f,.86f));
            painter.strokeColor=Color.white;painter.lineWidth=2;painter.BeginPath();painter.MoveTo(new Vector2(X(progress),Top-4));painter.LineTo(new Vector2(X(progress),Top+6*LaneHeight));painter.Stroke();
        }
        private void Bar(Painter2D p,int lane,float start,float end,Color color)
        {
            float x=X(start),right=Mathf.Max(x+3,X(end)),y=Top+lane*LaneHeight+3;
            p.fillColor=color;p.BeginPath();p.MoveTo(new Vector2(x,y));p.LineTo(new Vector2(right,y));p.LineTo(new Vector2(right,y+12));p.LineTo(new Vector2(x,y+12));p.ClosePath();p.Fill();
        }
    }
}
