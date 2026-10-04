using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Overburst.EditorTools.ComboMaker
{
    public sealed partial class ComboMakerWindow
    {
        [SerializeField] private float previewEnergy=100;
        [SerializeField] private bool previewKnockback=true;
        [SerializeField] private bool previewBlood=true;
        [SerializeField] private EnemyDefinition previewEnemy;
        [SerializeField] private EnemyHitWeightProfile previewWeight;
        [SerializeField] private bool includeAuthoringEnemies;
        [SerializeField] private bool previewPackBlood;

        private void BuildPreviewOptions(VisualElement parent)
        {
            var row=Row();row.AddToClassList("energy-row");parent.Add(row);
            float maximum=OverburstElementTuning.Current.SafeLightOverchargeMaximum;
            var energy=new Slider("비교 에너지",0,maximum){value=previewEnergy,showInputField=true};energy.style.flexGrow=1;
            energy.tooltip="기본 최대치를 넘는 에너지는 빛 과충전·3연타 비교에 사용합니다.";
            energy.RegisterValueChangedCallback(e=>{previewEnergy=e.newValue;ApplyPreviewOptions();Seek(preview.Progress);});row.Add(energy);
            foreach(float amount in new[]{0f,50f,100f,maximum})
            {float value=amount;row.Add(ActionButton(amount.ToString("0"),()=>energy.value=value));}
            var adopted=Resources.LoadAll<EnemyThemeTable>("Enemies/Themes/Tables").OrderBy(t=>t.name).SelectMany(t=>t.Entries).Where(e=>e.definition!=null&&e.definition.ActorPrefab!=null).Select(e=>e.definition).ToArray();
            if(!adopted.Contains(previewEnemy)&&(!includeAuthoringEnemies||previewEnemy==null||previewEnemy.ActorPrefab==null))previewEnemy=adopted.FirstOrDefault();
            ApplyPreviewOptions();
        }
        private void BuildTargetOptions(VisualElement parent)
        {
            var options=Card("대상 · 넉백 · 혈흔");parent.Add(options);
            var authored=new Toggle("제작 중 몬스터도 표시"){value=includeAuthoringEnemies};
            authored.tooltip="테마 스폰에 아직 연결하지 않은 전투 Actor도 비교합니다.";
            authored.RegisterValueChangedCallback(e=>{includeAuthoringEnemies=e.newValue;BuildEditor();});options.Add(authored);
            var roster=Resources.LoadAll<EnemyThemeTable>("Enemies/Themes/Tables").OrderBy(t=>t.name)
                .SelectMany(t=>t.Entries.Where(e=>e.definition!=null && e.definition.ActorPrefab!=null)
                    .Select(e=>(definition:e.definition,label:t.DisplayName+" / "+(e.tier==EnemyThemeTier.Small?"소형":e.tier==EnemyThemeTier.Medium?"중형":"정예")+" / "+e.definition.DisplayName))).ToList();
            if(includeAuthoringEnemies)
            {
                var added=AssetDatabase.FindAssets("t:EnemyDefinition",new[]{"Assets/ProjectOverburst"})
                    .Select(g=>AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                    .Where(d=>d!=null&&d.ActorPrefab!=null&&!roster.Any(e=>e.definition==d)).OrderBy(d=>d.DisplayName).ToArray();
                roster.AddRange(added.Select(d=>(definition:d,label:"제작 / "+d.DisplayName)));
            }
            var entries=roster.ToArray();
            if(!entries.Any(e=>e.definition==previewEnemy)){previewEnemy=entries.FirstOrDefault().definition;ApplyPreviewOptions();placeTargetAfterRebuild=true;ScheduleRebuild();}
            var monster=new DropdownField("게임 몬스터",entries.Select(e=>e.label).ToList(),Mathf.Max(0,Array.FindIndex(entries,e=>e.definition==previewEnemy)));
            monster.name="preview-monster";monster.tooltip="현재 연결된 테마 목록과 선택한 제작 Actor를 표시합니다.";
            monster.RegisterValueChangedCallback(e=>{if(monster.index<0||monster.index>=entries.Length)return;previewEnemy=entries[monster.index].definition;ApplyPreviewOptions();placeTargetAfterRebuild=true;ScheduleRebuild();});options.Add(monster);
            var weight=new ObjectField("피격 무게 덮어쓰기"){objectType=typeof(EnemyHitWeightProfile),allowSceneObjects=false,value=previewWeight};
            weight.tooltip="비우면 선택한 몬스터의 실제 프로필을 사용합니다.";
            weight.RegisterValueChangedCallback(e=>{previewWeight=e.newValue as EnemyHitWeightProfile;ApplyPreviewOptions();Seek(preview.Progress);});options.Add(weight);
            var toggle=new Toggle {text="넉백·들림 반응",value=previewKnockback};
            toggle.RegisterValueChangedCallback(e=>{previewKnockback=e.newValue;ApplyPreviewOptions();Seek(preview.Progress);});options.Add(toggle);
            var blood=new Toggle {text="혈흔 비산 · 바닥 자국",value=previewBlood};
            blood.RegisterValueChangedCallback(e=>{previewBlood=e.newValue;ApplyPreviewOptions();Seek(preview.Progress);BuildEditor();});options.Add(blood);
            var bloodMode=new DropdownField("혈흔 비교",new System.Collections.Generic.List<string>{"A · 기존 VFX Graph","B · Blood Effects Pack"},previewPackBlood?1:0){name="preview-blood-mode"};
            bloodMode.RegisterValueChangedCallback(e=>{previewPackBlood=bloodMode.index==1;ApplyPreviewOptions();Seek(preview.Progress);});options.Add(bloodMode);
            options.Add(ActionButton("판정 중심에 대상 배치",()=>{preview.PlaceTargetAtImpact();renderDirty=true;BuildEditor();UpdateTransport();}));
            var info=new Label("벽·군집 충돌 제외 · 무기 FX 연결이 없는 경우 오라는 표시되지 않습니다.");info.AddToClassList("hint");options.Add(info);
            ApplyPreviewOptions();
        }
        private void ApplyPreviewOptions()
        {
            preview.EnergyNormalized=Mathf.Max(0,previewEnergy/Mathf.Max(1,OverburstElementTuning.Current.maximumEnergy));
            preview.ShowKnockback=previewKnockback;preview.TargetDefinition=previewEnemy;preview.TargetPrefab=previewEnemy!=null?previewEnemy.ActorPrefab.gameObject:null;preview.WeightOverride=previewWeight;
            preview.ShowBlood=previewBlood;
            preview.PackBlood=previewPackBlood;
        }
        private void AddHeavyFloat(VisualElement parent,string label,bool charged)
        {
            var field=new FloatField(label){value=charged?session.HeavyWorking.chargedDamageMultiplier:session.HeavyWorking.emptyDamageMultiplier,isDelayed=true};
            field.RegisterValueChangedCallback(e=>
            {
                Undo.RecordObject(session.HeavyWorking,"강공 피해 배율");
                if(charged)session.HeavyWorking.chargedDamageMultiplier=e.newValue;
                else session.HeavyWorking.emptyDamageMultiplier=e.newValue;
                Changed();
            });parent.Add(field);
        }
        private void BuildHeavyVfx(VisualElement parent)
        {
            var card=Card("원소 방출 VFX · 강공 자산에 저장");parent.Add(card);
            using(var so=new SerializedObject(session.HeavyWorking))
            {
                var property=so.FindProperty("elementVfx").Copy();var end=property.GetEndProperty();
                if(property.NextVisible(true))do
                {
                    if(SerializedProperty.EqualContents(property,end))break;
                    string path=property.propertyPath;
                    string label=HeavyVfxLabel(property.name);
                    VisualElement field;
                    if(property.propertyType==SerializedPropertyType.ObjectReference)
                    {
                        var reference=new ObjectField(label){objectType=typeof(GameObject),allowSceneObjects=false,value=property.objectReferenceValue};
                        reference.RegisterValueChangedCallback(e=>EditHeavy(path,p=>p.objectReferenceValue=e.newValue));field=reference;
                    }
                    else if(property.propertyType==SerializedPropertyType.Float)
                    {
                        var value=new FloatField(label){value=property.floatValue,isDelayed=true};
                        value.RegisterValueChangedCallback(e=>EditHeavy(path,p=>p.floatValue=e.newValue));field=value;
                    }
                    else continue;
                    field.name="heavy-field-"+path;field.tooltip=property.tooltip;field.AddToClassList("data-field");card.Add(field);
                }while(property.NextVisible(false));
            }
            var note=new Label("빈 슬롯은 게임과 동일하게 추가 방출 VFX를 재생하지 않습니다.");note.AddToClassList("hint");card.Add(note);
        }
        private void AddHeavyDischargeIndex(VisualElement parent)
        {
            var field=new IntegerField("방출 판정 인덱스 (0부터)"){value=session.HeavyWorking.dischargePhaseIndex,isDelayed=true,name="heavy-discharge-index"};
            field.RegisterValueChangedCallback(e=>EditHeavy("dischargePhaseIndex",p=>p.intValue=e.newValue));parent.Add(field);
        }
        private void EditHeavy(string path,Action<SerializedProperty> edit)
        {using(var so=new SerializedObject(session.HeavyWorking)){edit(so.FindProperty(path));so.ApplyModifiedProperties();}Changed();}
        private static string HeavyVfxLabel(string name)
        {
            switch(name)
            {
                case "fireImpact":return "불 폭발";case "fireChainExplosion":return "불 연쇄 폭발";
                case "iceImpact":return "얼음 충격";case "iceShatter":return "얼음 쇄빙";
                case "electricImpact":return "번개 충격";case "electricChainLink":return "번개 연결";case "electricDirectHit":return "번개 직격";
                case "darkBarrageSlam":return "어둠 내려찍기";case "darkBarrageProjectile":return "어둠 탄";case "darkBarrageTrail":return "어둠 탄 꼬리";case "darkBarrageHit":return "어둠 탄 적중";
                case "lightTripleImpact":return "빛 3연타";case "lightDoubleImpact":return "빛 2연타";
                case "fireImpactRadius":return "불 원본 반경 (m)";case "iceImpactRadius":return "얼음 원본 반경 (m)";case "electricImpactRadius":return "번개 원본 반경 (m)";
                case "fireChainRadius":return "불 연쇄 원본 반경 (m)";case "darkImpactRadius":return "어둠 원본 반경 (m)";case "lightImpactRadius":return "빛 원본 반경 (m)";
                default:return ObjectNames.NicifyVariableName(name);
            }
        }
    }
}
