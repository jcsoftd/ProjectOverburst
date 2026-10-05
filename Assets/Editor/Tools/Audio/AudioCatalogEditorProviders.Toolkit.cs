using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public abstract partial class SerializedAudioCatalogEditorProvider<T> where T : ScriptableObject
{
    public IReadOnlyList<AudioCatalogClipRow> ReadClipRows()
    {
        var result=new List<AudioCatalogClipRow>();if(catalog==null||serializedCatalog==null)return result;serializedCatalog.UpdateIfRequiredOrScript();
        foreach(var slot in GetClipSlots())
        {
            var property=serializedCatalog.FindProperty(slot.Path);if(property==null)continue;
            bool array=property.isArray;int candidates=array?property.arraySize:property.objectReferenceValue!=null?1:0;
            string group=typeof(T)==typeof(MeleeElementSfxCatalog)?slot.Path.StartsWith("upperHeavy.",StringComparison.Ordinal)?"상위 강공":slot.Label.Split(' ')[0]:DisplayName;
            string sourceKey=typeof(T)==typeof(CombatActionSfxCatalog)?serializedCatalog.FindProperty(slot.Path.Substring(0,slot.Path.LastIndexOf('.'))+".name").stringValue:"";
            string label=typeof(T)==typeof(CombatActionSfxCatalog)?CombatCueName(sourceKey):slot.Label;
            for(int i=0;i<(array?Math.Max(1,candidates):1);i++)
            {
                var clip=(array?candidates>0?property.GetArrayElementAtIndex(i).objectReferenceValue:null:property.objectReferenceValue) as AudioClip;
                var preview=clip;
                if(!array&&preview==null&&typeof(T)==typeof(CombatActionSfxCatalog))
                {var key=serializedCatalog.FindProperty(slot.Path.Substring(0,slot.Path.LastIndexOf('.'))+".name");preview=Resources.Load<AudioClip>("Combat/SFX/CombatAction/"+key.stringValue);}
                result.Add(new AudioCatalogClipRow(slot.Path,label,group,clip,preview,i,candidates,array,slot.Optional,sourceKey));
            }
        }
        return result;
    }
    static string CombatCueName(string key)
    {
        switch(key)
        {
            case "GreatswordGround_EarthExplosion1": return "대검 지면 폭발 1";
            case "GreatswordGround_EarthExplosion2": return "대검 지면 폭발 2";
            case "ParryWindowPing_MetallicRingLong": return "패링 가능 알림";
            case "PlayerEvadeCloth": return "플레이어 회피";
            case "ElementEnergyFull": return "원소 에너지 완충";
            case "ParryClash_ImpactRinging": return "패링 충돌";
            case "QuickSlotReady": return "퀵슬롯 준비 완료";
            case "MonsterHitCommon01": return "몬스터 공통 피격 1";
            case "MonsterHitCommon02": return "몬스터 공통 피격 2";
            case "MonsterHitCommon03": return "몬스터 공통 피격 3";
            case "DashHeavyGather": return "대시 강공 모으기";
            case "DashHeavyRelease": return "대시 강공 방출";
            default: return key;
        }
    }
    public void SetClip(string key,int candidate,AudioClip clip)
    {
        Mutate("오디오 클립 교체",()=>
        {
            var property=serializedCatalog.FindProperty(key);
            if(property.isArray)
            {if(property.arraySize==0&&clip==null)return;if(property.arraySize==0)property.arraySize=1;if(candidate<0||candidate>=property.arraySize)throw new InvalidOperationException("Clip candidate changed; refresh the catalog");property.GetArrayElementAtIndex(candidate).objectReferenceValue=clip;}
            else property.objectReferenceValue=clip;
        });
    }
    public void AddClip(string key)
    {Mutate("오디오 클립 후보 추가",()=>{var array=serializedCatalog.FindProperty(key);if(!array.isArray)return;int index=array.arraySize++;array.GetArrayElementAtIndex(index).objectReferenceValue=null;});}
    public void RemoveClip(string key,int candidate)
    {Mutate("오디오 클립 후보 삭제",()=>{var property=serializedCatalog.FindProperty(key);if(!property.isArray){property.objectReferenceValue=null;return;}if(candidate<0||candidate>=property.arraySize)return;property.GetArrayElementAtIndex(candidate).objectReferenceValue=null;property.DeleteArrayElementAtIndex(candidate);});}
    void Mutate(string name,Action edit)
    {
        if(!AudioCatalogManagerWindow.CanEdit||serializedCatalog==null)return;Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName(name);
        try{serializedCatalog.Update();edit();serializedCatalog.ApplyModifiedProperties();Undo.CollapseUndoOperations(group);}finally{Undo.IncrementCurrentGroup();}
    }
    public VisualElement CreateCueInspector(string key,Action changed)
    {
        var root=new VisualElement();if(catalog==null||serializedCatalog==null)return root;
        if(key.EndsWith(".clips",StringComparison.Ordinal))
        {
            var grid=new VisualElement();grid.AddToClassList("audio-settings-grid");root.Add(grid);string path=key.Substring(0,key.Length-6);
            foreach(var name in new[]{"volume","minPitch","maxPitch","spatial","minDistance","maxDistance","cooldown","maxVoices"})AddProperty(grid,path+"."+name,SettingName(name));
        }
        else if(typeof(T)==typeof(ItemDropSfxCatalog))
        {root.Add(Note("아이템 드롭 공통 설정 · 모든 드롭 소리에 함께 적용"));var grid=new VisualElement();grid.AddToClassList("audio-settings-grid");root.Add(grid);foreach(var name in new[]{"dropVolume","revealVolume","spatialBlend","minDistance","maxDistance","sameCueCooldown"})AddProperty(grid,name,SettingName(name));}
        else if(typeof(T)==typeof(CombatActionSfxCatalog))AddProperty(root,key.Substring(0,key.LastIndexOf('.'))+".name","동작 키");
        root.Bind(serializedCatalog);root.RegisterCallback<SerializedPropertyChangeEvent>(_=>changed?.Invoke());return root;
    }
    public VisualElement CreateCatalogSettings(Action changed)
    {
        var root=new VisualElement();if(catalog==null||serializedCatalog==null)return root;
        void Rebuild(){root.Unbind();root.Clear();serializedCatalog.UpdateIfRequiredOrScript();PopulateToolkit(root,Rebuild,changed,true);root.Bind(serializedCatalog);changed?.Invoke();}
        PopulateToolkit(root,Rebuild,changed,true);root.Bind(serializedCatalog);root.RegisterCallback<SerializedPropertyChangeEvent>(_=>changed?.Invoke());return root;
    }

    public VisualElement CreateInspector(Action changed)
    {
        var root=new VisualElement {name="catalog-fields"};
        if(catalog==null||serializedCatalog==null){root.Add(new HelpBox("카탈로그 에셋이 없습니다.",HelpBoxMessageType.Error));return root;}
        void Rebuild(){root.Unbind();root.Clear();serializedCatalog.UpdateIfRequiredOrScript();PopulateToolkit(root,Rebuild,changed);root.Bind(serializedCatalog);changed?.Invoke();}
        PopulateToolkit(root,Rebuild,changed);root.Bind(serializedCatalog);
        root.RegisterCallback<SerializedPropertyChangeEvent>(_=>changed?.Invoke());return root;
    }
    void PopulateToolkit(VisualElement root,Action rebuild,Action changed,bool commonOnly=false)
    {
        var slots=GetClipSlots().ToArray();
        for(int i=0;!commonOnly&&i<slots.Length;i++)
        {
            var slot=slots[i];var property=serializedCatalog.FindProperty(slot.Path);if(property==null)continue;
            int count=property.isArray?property.arraySize:property.objectReferenceValue!=null?1:0;
            var card=new Foldout {text=slot.Label+"  ·  "+count+"클립",value=i<2};card.AddToClassList("audio-cue-card");card.name="cue-"+i;root.Add(card);
            bool combat=typeof(T)==typeof(CombatActionSfxCatalog);
            if(combat)
            {
                string key=slot.Path.Substring(0,slot.Path.LastIndexOf('.'))+".name";
                AddProperty(card,key,"동작 키");
                if(property.objectReferenceValue==null)
                {
                    var fallback=Resources.Load<AudioClip>("Combat/SFX/CombatAction/"+serializedCatalog.FindProperty(key).stringValue);
                    if(fallback!=null){var row=new VisualElement();row.AddToClassList("audio-clip-row");var field=new ObjectField("Resources 사용"){objectType=typeof(AudioClip),value=fallback};field.SetEnabled(false);row.Add(field);row.Add(PreviewButton(()=>fallback));card.Add(row);}
                }
            }
            if(property.isArray)
            {
                for(int candidate=0;candidate<property.arraySize;candidate++)
                {
                    int item=candidate;string path=property.GetArrayElementAtIndex(item).propertyPath;
                    var row=ClipRow(path,"후보 "+(item+1));
                    var remove=new Button(()=>ChangeStructure(()=>{var array=serializedCatalog.FindProperty(slot.Path);array.GetArrayElementAtIndex(item).objectReferenceValue=null;array.DeleteArrayElementAtIndex(item);},rebuild)){text="삭제",name="remove-"+i+"-"+item};remove.AddToClassList("audio-remove");row.Add(remove);card.Add(row);
                }
                if(property.arraySize==0)card.Add(Note(slot.Optional?(string.IsNullOrEmpty(slot.EmptyMessage)?"선택 큐 · 비어 있으면 기존 fallback 또는 무음 규칙을 따릅니다.":slot.EmptyMessage):"필수 클립 후보를 지정하세요."));
                var add=new Button(()=>ChangeStructure(()=>{var array=serializedCatalog.FindProperty(slot.Path);int item=array.arraySize++;array.GetArrayElementAtIndex(item).objectReferenceValue=null;},rebuild)){text="+ 클립 후보 추가",name="add-"+i};add.AddToClassList("audio-secondary");card.Add(add);
                var settingsPath=slot.Path.EndsWith(".clips",StringComparison.Ordinal)?slot.Path.Substring(0,slot.Path.Length-6):null;
                if(settingsPath!=null)
                {
                    var settings=new Foldout {text="재생 설정",value=false};settings.AddToClassList("audio-settings");card.Add(settings);
                    foreach(var name in new[]{"volume","minPitch","maxPitch","spatial","minDistance","maxDistance","cooldown","maxVoices"})
                        if(serializedCatalog.FindProperty(settingsPath+"."+name)!=null)AddProperty(settings,settingsPath+"."+name,SettingName(name));
                }
            }
            else card.Add(ClipRow(slot.Path,"클립"));
        }
        if(typeof(T)==typeof(ItemDropSfxCatalog))
        {var settings=new Foldout {text="드롭 공통 재생 설정",value=true};root.Add(settings);foreach(var name in new[]{"dropVolume","revealVolume","spatialBlend","minDistance","maxDistance","sameCueCooldown"})AddProperty(settings,name,SettingName(name));}
        if(typeof(T)==typeof(MeleeElementSfxCatalog))
        {
            var keys=new Foldout {text="원소 키 · 추가와 삭제",value=false};root.Add(keys);var entries=serializedCatalog.FindProperty("entries");
            for(int i=0;i<entries.arraySize;i++)
            {
                int item=i;var row=new VisualElement();row.AddToClassList("audio-clip-row");AddProperty(row,entries.GetArrayElementAtIndex(item).propertyPath+".element","원소 "+(item+1));
                var remove=new Button(()=>ChangeStructure(()=>serializedCatalog.FindProperty("entries").DeleteArrayElementAtIndex(item),rebuild)){text="행 삭제"};row.Add(remove);keys.Add(row);
            }
            var add=new Button(()=>ChangeStructure(()=>
            {
                var array=serializedCatalog.FindProperty("entries");int index=array.arraySize++;var entry=array.GetArrayElementAtIndex(index);entry.FindPropertyRelative("element").intValue=(int)WeaponElement.None;
                foreach(var cue in new[]{"slash","hit","heavyImpact","criticalHit","followUp"})
                {var settings=entry.FindPropertyRelative(cue);settings.FindPropertyRelative("clips").ClearArray();settings.FindPropertyRelative("volume").floatValue=1;settings.FindPropertyRelative("minPitch").floatValue=1;settings.FindPropertyRelative("maxPitch").floatValue=1;settings.FindPropertyRelative("spatial").boolValue=true;settings.FindPropertyRelative("minDistance").floatValue=2;settings.FindPropertyRelative("maxDistance").floatValue=40;settings.FindPropertyRelative("cooldown").floatValue=.03f;settings.FindPropertyRelative("maxVoices").intValue=0;}
            },rebuild)){text="+ 빈 원소 행 추가"};add.AddToClassList("audio-secondary");keys.Add(add);
            var upper=new Foldout {text="상위 강공 지연 설정",value=false};root.Add(upper);
            foreach(var field in typeof(UpperHeavySfxSettings).GetFields().Where(f=>f.FieldType==typeof(float)))AddProperty(upper,"upperHeavy."+field.Name,ObjectNames.NicifyVariableName(field.Name));
        }
        if(typeof(T)==typeof(CombatActionSfxCatalog))
        {
            var structure=new Foldout {text="동작 행 추가와 삭제",value=false};root.Add(structure);var entries=serializedCatalog.FindProperty("entries");
            for(int i=0;i<entries.arraySize;i++){int item=i;var remove=new Button(()=>ChangeStructure(()=>serializedCatalog.FindProperty("entries").DeleteArrayElementAtIndex(item),rebuild)){text=(item+1)+"행 삭제"};structure.Add(remove);}
            var add=new Button(()=>ChangeStructure(()=>{var array=serializedCatalog.FindProperty("entries");int index=array.arraySize++;var entry=array.GetArrayElementAtIndex(index);entry.FindPropertyRelative("name").stringValue="";entry.FindPropertyRelative("clip").objectReferenceValue=null;},rebuild)){text="+ 빈 동작 행 추가"};add.AddToClassList("audio-secondary");structure.Add(add);
        }
    }
    void ChangeStructure(Action edit,Action rebuild)
    {
        if(!AudioCatalogManagerWindow.CanEdit)return;
        Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("오디오 카탈로그 구조 편집");
        try{serializedCatalog.Update();edit();serializedCatalog.ApplyModifiedProperties();Undo.CollapseUndoOperations(group);rebuild();}
        finally{Undo.IncrementCurrentGroup();}
    }
    VisualElement ClipRow(string path,string label)
    {
        var row=new VisualElement();row.AddToClassList("audio-clip-row");
        var property=serializedCatalog.FindProperty(path);var field=new ObjectField(label){objectType=typeof(AudioClip),allowSceneObjects=false,bindingPath=path,name="clip-field"};field.AddToClassList("audio-clip-field");row.Add(field);
        var play=PreviewButton(()=>serializedCatalog.FindProperty(path).objectReferenceValue as AudioClip);play.SetEnabled(property.objectReferenceValue!=null);
        field.RegisterValueChangedCallback(e=>play.SetEnabled(e.newValue is AudioClip));row.Add(play);
        return row;
    }
    static Button PreviewButton(Func<AudioClip> read)
    {var button=new Button(()=>AudioCatalogEditorPreview.Play(read())){text="▶",tooltip="클립 원음 미리듣기",name="play-clip"};button.AddToClassList("audio-play");return button;}
    void AddProperty(VisualElement root,string path,string label)
    {var property=serializedCatalog.FindProperty(path);if(property==null)return;var field=new PropertyField(property,label){name="setting-"+path.Replace('.','-')};field.AddToClassList("audio-setting");root.Add(field);}
    static Label Note(string message){var label=new Label(message);label.AddToClassList("audio-note");return label;}
    static string SettingName(string key)
    {
        switch(key){case "volume":return "볼륨";case "minPitch":return "최소 피치";case "maxPitch":return "최대 피치";case "spatial":return "공간음";case "minDistance":return "최소 거리 m";case "maxDistance":return "최대 거리 m";case "cooldown":return "쿨다운 s";case "maxVoices":return "동시 재생 수 (0: 풀 한도)";case "dropVolume":return "드롭 볼륨";case "revealVolume":return "등장 볼륨";case "spatialBlend":return "공간음 비율";case "sameCueCooldown":return "동일 종류 쿨다운 s";default:return key;}
    }
}
