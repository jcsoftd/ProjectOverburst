using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Overburst.EditorTools.ComboMaker
{
    public sealed partial class ComboMakerWindow
    {
        private static readonly Dictionary<string,string> Labels=new Dictionary<string,string>
        {
            {"attackName","공격 이름"},{"animationClip","애니메이션"},{"animationSpeedMultiplier","재생 속도"},
            {"transitionDuration","진입 블렌딩 (초)"},{"continuationStartNormalizedTime","연계 진입점"},
            {"comboInputWindow","다음 타 연결 구간"},{"actionCancelStartNormalized","이동 취소 시점"},
            {"playbackAcceleration","구간 가속"},{"startNormalized","시작"},{"endNormalized","종료"},{"peakMultiplier","최대 배속"},
            {"baseAnimationSpeed","전체 기본 속도"},{"entryTransitionDuration","첫 타 블렌딩 (초)"},{"resetDelay","콤보 대기시간 (초)"},
            {"attackId","고유 공격 ID"},{"startNormalizedTime","시작 진행률"},{"endNormalizedTime","종료 진행률"},
            {"attackPattern","공격 패턴"},{"geometry","판정 크기 / 위치"},{"impact","피해 / 타격 피드백"},
            {"progressSource","판정 진행 기준"},{"basisFollowMode","판정 원점 추적"},{"vfxCues","공격 VFX"},
            {"useBakedVfxSwingSlope","베이크 경사 사용"},{"bakedVfxSwingSlopeDegrees","계산된 경사"},
            {"vfxSwingSlopeOffsetDegrees","추가 경사 보정"},{"vfxSwingSettings","검격 방향 / 베이크"},
            {"definition","VFX 정의"},{"triggerProgress","판정 내 발동 진행률"},{"placementMode","배치 기준"},
            {"scaleMultiplier","크기 배율 (1 = 100%)"},{"motionRole","동작 역할"},{"mirrorAxis","미러 축"},
            {"shockwaveIntensityMultiplier","충격파 강도"},{"shockwaveSpeedMultiplier","충격파 속도"},
            {"autoSwingSlope","무기 경사 적용"},{"swingSlopeOffsetDegrees","경사 보정"},{"localEulerOffset","XYZ 회전 보정"},
            {"elementOverrideKey","원소 교체 키"},{"rangeMultiplier","사거리 배율"},{"angleMultiplier","각도 배율"},
            {"widthMultiplier","판정 폭 배율"},{"vfxScaleMultiplier","VFX 크기 배율"},{"overrideForwardOffset","전방 위치 직접 지정"},
            {"forwardOffset","전방 위치"},{"damageMultiplier","피해 배율"},{"knockbackMultiplier","넉백 배율"},
            {"hitStunMultiplier","경직 배율"},{"knockbackReactionDuration","넉백 시간"},{"overrideTargetReaction","대상 반응 덮어쓰기"},
            {"triggersOnHitEffects","적중 효과 실행"},{"hitFeedbackProfile","타격 피드백 프로필"},{"airborneImpulse","공중 충격량"},
            {"airborneStunDuration","공중 경직 시간"},{"movementPhases","이동 구간"},{"trailPhases","검날 트레일 구간"},
            {"movementMode","이동 방식"},{"distance","이동 거리"},{"progressCurve","진행 곡선"},{"localForwardCurve","전진 궤적"},
            {"orientation","베기 방향"},{"bakeMask","베이크 마스크"},{"maxDeviationDegrees","경사 허용 편차"},
            {"maskAngleDegrees","마스크 각도"},{"verticalPivotHeight","수직 중심 높이"},{"verticalHalfWidth","수직 허용 폭"},
            {"reverseDirection","방향 반전"},{"visualHeightCurve","비주얼 Y 높이 (m)"},{"useAuthoredTiming","설정한 시간 구간 그대로 이동"}
        };

        private void BuildEditor()
        {
            if(editorScroll==null||session.Working==null)return;
            Vector2 scroll=editorScroll.scrollOffset;editorScroll.Clear();serialized.Update();ClampStep();
            editorTitle.text=heavyMode?"강공 설정 · 별도 자산":$"{selectedStep+1:00}타 설정";
            for(int i=0;i<tabButtons.Count;i++)tabButtons[i].EnableInClassList("selected",tab==i);
            string step=$"steps.Array.data[{selectedStep}]";
            if(tab==0)
            {
                var motion=Card("공격 동작");editorScroll.Add(motion);
                AddField(motion,step+".attackName");AddField(motion,step+".animationClip");AddField(motion,step+".animationSpeedMultiplier");AddField(motion,step+".transitionDuration");
                if(heavyMode)
                {
                    var heavy=Card("에너지에 따른 강공 피해");editorScroll.Add(heavy);
                    AddHeavyFloat(heavy,"에너지 있음",true);AddHeavyFloat(heavy,"에너지 없음",false);
                    AddField(heavy,step+".actionCancelStartNormalized");AddField(heavy,step+".playbackAcceleration");
                    return;
                }
                var flow=Card("다음 타 연결");editorScroll.Add(flow);
                AddField(flow,step+".continuationStartNormalizedTime");AddField(flow,step+".comboInputWindow",null,true);AddField(flow,step+".actionCancelStartNormalized");
                AddField(flow,step+".playbackAcceleration");
                var common=new Foldout {text="전체 콤보 설정",value=false};editorScroll.Add(common);
                AddField(common,"baseAnimationSpeed");AddField(common,"entryTransitionDuration");AddField(common,"resetDelay");AddField(common,step+".attackId");
            }
            else if(tab==1)
            {
                var hint=new Label("타격마다 판정과 VFX를 설정합니다. 타임라인의 주황 경계를 드래그하면 판정 시점을 옮길 수 있습니다.");hint.AddToClassList("hint");editorScroll.Add(hint);
                var phases=serialized.FindProperty(step+".attackPhases");
                for(int i=0;i<phases.arraySize;i++)
                {
                    int index=i;string path=step+$".attackPhases.Array.data[{i}]";
                    var card=Card($"타격 {i+1}");editorScroll.Add(card);
                    AddField(card,path+".attackPattern");AddField(card,path+".startNormalizedTime");AddField(card,path+".endNormalizedTime");
                    AddField(card,path+".geometry");AddField(card,path+".impact");AddField(card,path+".vfxCues",null,true);
                    var advanced=new Foldout {text="판정 진행 / 검격 방향",value=false};card.Add(advanced);
                    AddField(advanced,path+".progressSource");AddField(advanced,path+".basisFollowMode");AddField(advanced,path+".vfxSwingSlopeOffsetDegrees");AddField(advanced,path+".vfxSwingSettings");
                    if(phases.arraySize>1)card.Add(ActionButton("이 타격 삭제",()=>EditArray(step+".attackPhases",index,false)));
                }
                editorScroll.Add(ActionButton("+ 타격 구간 추가",()=>EditArray(step+".attackPhases",-1,true)));
                if(heavyMode)BuildHeavyVfx(editorScroll);
            }
            else if(tab==3)
            {
                editorTitle.text="프리뷰 비교 · 자산에 저장되지 않음";
                BuildTargetOptions(editorScroll);
                var target=Card("타격 VFX 비교");editorScroll.Add(target);
                var position=new Vector3Field("대상 위치") {value=preview.TargetPosition};position.RegisterValueChangedCallback(e=>{preview.TargetPosition=e.newValue;Seek(preview.Progress);});target.Add(position);
                var vfx=new ObjectField("비교용 타격 VFX") {objectType=typeof(GameObject),allowSceneObjects=false,value=preview.HitOverride};
                vfx.RegisterValueChangedCallback(e=>{preview.HitOverride=e.newValue as GameObject;Seek(preview.Progress);});target.Add(vfx);
                var blood=new ObjectField("혈흔 덮어쓰기") {objectType=typeof(BloodHitProfile),allowSceneObjects=false,value=preview.BloodProfile};
                blood.tooltip="비우면 선택한 게임 몬스터의 실제 혈흔 프로필을 사용합니다.";
                blood.RegisterValueChangedCallback(e=>{preview.BloodProfile=e.newValue as BloodHitProfile;Seek(preview.Progress);BuildEditor();});target.Add(blood);
                var bloodState=new Label(preview.BloodStatus){name="blood-preview-status"};bloodState.AddToClassList("hint");target.Add(bloodState);
                var description=new Label("비교용 VFX를 비우면 현재 원소 적중 카탈로그를 재생합니다. 대상 위치와 이 두 항목은 프리뷰에만 사용됩니다.");description.AddToClassList("hint");target.Add(description);
            }
            else
            {
                var motion=Card("공격 중 이동");editorScroll.Add(motion);AddField(motion,step+".movementPhases",null,true);
                AddField(motion,step+".visualHeightCurve");
                var trail=Card("검날 트레일 구간");editorScroll.Add(trail);AddField(trail,step+".trailPhases",null,true);
                var hint=new Label("이동과 비주얼 Y는 게임과 같은 실행기로 재생합니다. Y 곡선의 가로축은 클립 진행률, 세로축은 높이(m)입니다. 트레일은 무기에 설정된 패키지 효과입니다.");hint.AddToClassList("hint");editorScroll.Add(hint);
            }
            editorScroll.schedule.Execute(()=>editorScroll.scrollOffset=scroll);
        }

        // Native controls only: no PropertyField fallback to legacy custom drawers.
        private void AddField(VisualElement parent,string path,string customLabel=null,bool open=false)
        {
            var p=serialized.FindProperty(path);if(p==null)return;
            string label=customLabel??(Labels.TryGetValue(p.name,out var translated)?translated:p.displayName);
            if(p.isArray&&p.propertyType!=SerializedPropertyType.String)
            {
                var fold=new Foldout {text=$"{label} ({p.arraySize})",value=open};fold.AddToClassList("array");parent.Add(fold);
                for(int i=0;i<p.arraySize;i++)
                {
                    int index=i;string childPath=p.GetArrayElementAtIndex(i).propertyPath;
                    var group=new VisualElement();group.AddToClassList("array-item");fold.Add(group);
                    AddField(group,childPath,$"{(p.name=="vfxCues"?"VFX":"구간")} {i+1}",true);
                    group.Add(ActionButton("이 항목 삭제",()=>EditArray(path,index,false)));
                }
                fold.Add(ActionButton("+ 추가",()=>EditArray(path,-1,true)));return;
            }
            if(p.propertyType==SerializedPropertyType.Generic && p.hasVisibleChildren)
            {
                var fold=new Foldout {text=label,value=open};parent.Add(fold);
                var child=p.Copy();var end=p.GetEndProperty();
                if(child.NextVisible(true))do{if(SerializedProperty.EqualContents(child,end))break;AddField(fold,child.propertyPath);}while(child.NextVisible(false));return;
            }
            VisualElement field;
            switch(p.propertyType)
            {
                case SerializedPropertyType.Float:
                    if (p.name == "shockwaveIntensityMultiplier" || p.name == "shockwaveSpeedMultiplier" || p.name == "scaleMultiplier")
                    {
                        string cuePath = path.Substring(0, path.LastIndexOf('.'));
                        var definition = serialized.FindProperty(cuePath + ".definition").objectReferenceValue as MeleeAttackVfxDefinition;
                        if (definition != null && definition.neutralPrefab != null
                            && definition.neutralPrefab.GetComponent<SwordShockwavePlayback>() != null)
                        {
                            float minimum = p.name == "shockwaveIntensityMultiplier" ? .05f : .1f;
                            float maximum = p.name == "scaleMultiplier" ? 5f : 4f;
                            var slider = new Slider(p.name == "scaleMultiplier" ? "충격파 크기" : label, minimum, maximum)
                                { value = p.floatValue > 0f ? p.floatValue : 1f, showInputField = true };
                            slider.RegisterValueChangedCallback(e => Edit(path, q => q.floatValue = Mathf.Clamp(e.newValue, minimum, maximum)));
                            field = slider; break;
                        }
                        if (p.name != "scaleMultiplier") return;
                    }
                    bool normalized=p.name.Contains("Normalized")||p.name=="triggerProgress";
                    if(normalized)
                    {
                        var slider=new Slider(label,0,1){value=p.floatValue,showInputField=true};
                        slider.RegisterValueChangedCallback(e=>Edit(path,q=>q.floatValue=e.newValue));field=slider;
                    }
                    else {var number=new FloatField(label){value=p.floatValue,isDelayed=true};number.RegisterValueChangedCallback(e=>Edit(path,q=>q.floatValue=e.newValue));field=number;}
                    break;
                case SerializedPropertyType.Integer:
                    var integer=new IntegerField(label){value=p.intValue,isDelayed=true};integer.RegisterValueChangedCallback(e=>Edit(path,q=>q.intValue=e.newValue));field=integer;break;
                case SerializedPropertyType.Boolean:
                    var toggle=new Toggle(label){value=p.boolValue};toggle.RegisterValueChangedCallback(e=>Edit(path,q=>q.boolValue=e.newValue));field=toggle;break;
                case SerializedPropertyType.String:
                    var text=new TextField(label){value=p.stringValue,isDelayed=true};text.RegisterValueChangedCallback(e=>Edit(path,q=>q.stringValue=e.newValue));field=text;break;
                case SerializedPropertyType.ObjectReference:
                    var reference=new ObjectField(label){objectType=ReferenceType(p.name),allowSceneObjects=false,value=p.objectReferenceValue};
                    bool changesVfxDefinition=p.name=="definition";
                    reference.RegisterValueChangedCallback(e=>{Edit(path,q=>q.objectReferenceValue=e.newValue);if(changesVfxDefinition)BuildEditor();});field=reference;break;
                case SerializedPropertyType.Enum:
                    var options=new List<string>(p.enumDisplayNames);var popup=new DropdownField(label,options,Mathf.Clamp(p.enumValueIndex,0,options.Count-1));
                    popup.RegisterValueChangedCallback(e=>Edit(path,q=>q.enumValueIndex=popup.index));field=popup;break;
                case SerializedPropertyType.Vector3:
                    var vector=new Vector3Field(label){value=p.vector3Value};vector.RegisterValueChangedCallback(e=>Edit(path,q=>q.vector3Value=e.newValue));field=vector;break;
                case SerializedPropertyType.AnimationCurve:
                    var curve=new CurveField(label){value=p.animationCurveValue};curve.RegisterValueChangedCallback(e=>Edit(path,q=>q.animationCurveValue=e.newValue));field=curve;break;
                default:field=new Label(label+" · 지원하지 않는 필드");break;
            }
            field.name="field-"+path;field.tooltip=string.IsNullOrEmpty(p.tooltip)?label:p.tooltip;
            field.AddToClassList("data-field");parent.Add(field);
        }

        private static Type ReferenceType(string name)
        {
            switch(name)
            {
                case "animationClip":return typeof(AnimationClip);
                case "attackPattern":return typeof(AttackPatternDefinition);
                case "definition":return typeof(MeleeAttackVfxDefinition);
                case "hitFeedbackProfile":return typeof(CombatHitFeedbackProfile);
                default:return typeof(UnityEngine.Object);
            }
        }
        private void Edit(string path,Action<SerializedProperty> mutation)
        {
            serialized.Update();var p=serialized.FindProperty(path);if(p==null)return;
            mutation(p);if(serialized.ApplyModifiedProperties())Changed();
        }
        private void EditArray(string path,int index,bool add)
        {Edit(path,p=>{if(add)p.InsertArrayElementAtIndex(p.arraySize);else if(index>=0&&index<p.arraySize)p.DeleteArrayElementAtIndex(index);});BuildEditor();}
        private void EditTiming(int step,int phase,bool end,float time)
        {
            string path=$"steps.Array.data[{step}].attackPhases.Array.data[{phase}]";
            var data=session.Working.steps[step].attackPhases[phase];
            float value=end?Mathf.Clamp(time,data.startNormalizedTime+.001f,1):Mathf.Clamp(time,0,data.endNormalizedTime-.001f);
            Edit(path+(end?".endNormalizedTime":".startNormalizedTime"),p=>p.floatValue=value);
            // Update bound native sliders without rebuilding the captured timeline pointer.
            var slider=rootVisualElement.Q<Slider>("field-"+path+(end?".endNormalizedTime":".startNormalizedTime"));slider?.SetValueWithoutNotify(value);
        }
        private static VisualElement Card(string title)
        {var card=new VisualElement();card.AddToClassList("card");card.Add(Title(title));return card;}
    }
}
