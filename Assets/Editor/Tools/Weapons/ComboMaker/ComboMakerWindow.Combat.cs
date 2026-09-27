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

        private void BuildPreviewOptions(VisualElement parent)
        {
            var row=Row();row.AddToClassList("energy-row");parent.Add(row);
            var energy=new Slider("비교 에너지 %",0,100){value=previewEnergy,showInputField=true};energy.style.flexGrow=1;
            energy.RegisterValueChangedCallback(e=>{previewEnergy=e.newValue;ApplyPreviewOptions();Seek(preview.Progress);});row.Add(energy);
            foreach(float amount in new[]{0f,50f,100f})
            {float value=amount;row.Add(ActionButton(amount+"%",()=>energy.value=value));}
            var adopted=Resources.LoadAll<EnemyThemeTable>("Enemies/Themes/Tables").OrderBy(t=>t.name).SelectMany(t=>t.Entries).Where(e=>e.definition!=null&&e.definition.ActorPrefab!=null).Select(e=>e.definition).ToArray();
            if(!adopted.Contains(previewEnemy))previewEnemy=adopted.FirstOrDefault();
            ApplyPreviewOptions();
        }
        private void BuildTargetOptions(VisualElement parent)
        {
            var options=Card("대상 · 넉백 · 혈흔");parent.Add(options);
            var roster=Resources.LoadAll<EnemyThemeTable>("Enemies/Themes/Tables").OrderBy(t=>t.name)
                .SelectMany(t=>t.Entries.Where(e=>e.definition!=null && e.definition.ActorPrefab!=null)
                    .Select(e=>new {definition=e.definition,label=t.DisplayName+" / "+(e.tier==EnemyThemeTier.Small?"소형":e.tier==EnemyThemeTier.Medium?"중형":"정예")+" / "+e.definition.DisplayName})).ToArray();
            if(!roster.Any(e=>e.definition==previewEnemy))previewEnemy=roster.FirstOrDefault()?.definition;
            var monster=new DropdownField("게임 몬스터",roster.Select(e=>e.label).ToList(),Mathf.Max(0,Array.FindIndex(roster,e=>e.definition==previewEnemy)));
            monster.tooltip="현재 채택된 5개 스폰 테마의 몬스터만 표시합니다.";
            monster.RegisterValueChangedCallback(e=>{if(monster.index<0||monster.index>=roster.Length)return;previewEnemy=roster[monster.index].definition;ApplyPreviewOptions();placeTargetAfterRebuild=true;ScheduleRebuild();});options.Add(monster);
            var weight=new ObjectField("피격 무게 덮어쓰기"){objectType=typeof(EnemyHitWeightProfile),allowSceneObjects=false,value=previewWeight};
            weight.tooltip="비우면 선택한 몬스터의 실제 프로필을 사용합니다.";
            weight.RegisterValueChangedCallback(e=>{previewWeight=e.newValue as EnemyHitWeightProfile;ApplyPreviewOptions();Seek(preview.Progress);});options.Add(weight);
            var toggle=new Toggle {text="넉백·들림 반응",value=previewKnockback};
            toggle.RegisterValueChangedCallback(e=>{previewKnockback=e.newValue;ApplyPreviewOptions();Seek(preview.Progress);});options.Add(toggle);
            var blood=new Toggle {text="혈흔 비산 · 바닥 자국",value=previewBlood};
            blood.RegisterValueChangedCallback(e=>{previewBlood=e.newValue;ApplyPreviewOptions();Seek(preview.Progress);BuildEditor();});options.Add(blood);
            options.Add(ActionButton("판정 중심에 대상 배치",()=>{preview.PlaceTargetAtImpact();renderDirty=true;BuildEditor();UpdateTransport();}));
            var info=new Label("벽·군집 충돌 제외 · 무기 FX 연결이 없는 경우 오라는 표시되지 않습니다.");info.AddToClassList("hint");options.Add(info);
            ApplyPreviewOptions();
        }
        private void ApplyPreviewOptions()
        {
            preview.EnergyNormalized=Mathf.Clamp01(previewEnergy/100);
            preview.ShowKnockback=previewKnockback;preview.TargetDefinition=previewEnemy;preview.TargetPrefab=previewEnemy!=null?previewEnemy.ActorPrefab.gameObject:null;preview.WeightOverride=previewWeight;
            preview.ShowBlood=previewBlood;
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
            foreach(string name in new[]{"fireImpact","iceImpact","iceShatter","electricImpact","electricChainLink","darkImpact","lightImpact"})
            {
                string path="elementVfx."+name;
                using(var so=new SerializedObject(session.HeavyWorking))
                {
                    var property=so.FindProperty(path);if(property==null)continue;
                    string label=name=="fireImpact"?"불 폭발":name=="iceImpact"?"얼음 충격":name=="iceShatter"?"얼음 쇄빙":name=="electricImpact"?"번개 충격":name=="electricChainLink"?"번개 연결":name=="darkImpact"?"어둠 충격":"빛 충격";
                    var field=new ObjectField(label){objectType=typeof(GameObject),allowSceneObjects=false,value=property.objectReferenceValue};
                    field.RegisterValueChangedCallback(e=>{using(var edit=new SerializedObject(session.HeavyWorking)){edit.FindProperty(path).objectReferenceValue=e.newValue;edit.ApplyModifiedProperties();}Changed();});card.Add(field);
                }
            }
            var note=new Label("빈 슬롯은 게임과 동일하게 추가 방출 VFX를 재생하지 않습니다.");note.AddToClassList("hint");card.Add(note);
        }
    }
}
