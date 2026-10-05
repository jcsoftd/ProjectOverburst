using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using System.Linq;

public readonly struct AudioCatalogValidationIssue
{
    public readonly MessageType Type;
    public readonly string Message;

    public AudioCatalogValidationIssue(MessageType type, string message)
    {
        Type = type;
        Message = message;
    }
}

public interface IAudioCatalogEditorProvider
{
    string DisplayName { get; }
    string AssetPath { get; }
    UnityEngine.Object CatalogAsset { get; }
    bool HasMissingReference { get; }
    int MissingElementCount { get; }
    bool SupportsConfirmedDefaults { get; }
    void Refresh();
    void DrawInspector();
    VisualElement CreateInspector(Action changed);
    IReadOnlyList<AudioCatalogClipRow> ReadClipRows();
    void SetClip(string key,int candidate,AudioClip clip);
    void AddClip(string key);
    void RemoveClip(string key,int candidate);
    VisualElement CreateCueInspector(string key,Action changed);
    VisualElement CreateCatalogSettings(Action changed);
    IReadOnlyList<AudioCatalogValidationIssue> CollectIssues();
    void ApplyConfirmedDefaults();
    int AddMissingElements();
    int ReadElementCount();
    int ReadClipCount();
}

public static class AudioCatalogEditorProviderRegistry
{
    public static List<IAudioCatalogEditorProvider> CreateProviders()
    {
        return new List<IAudioCatalogEditorProvider>
        {
            new MeleeElementSfxCatalogEditorProvider(),
            new CombatActionSfxCatalogEditorProvider(),
            new ElementalReactionSfxCatalogEditorProvider(),
            new ItemDropSfxCatalogEditorProvider()
        };
    }
}

public readonly struct AudioCatalogClipRow
{
    public readonly string Key,Name,Group,SourceKey;
    public readonly AudioClip Clip,PreviewClip;
    public readonly int Candidate,Candidates;
    public readonly bool Array,Optional;
    public AudioCatalogClipRow(string key,string name,string group,AudioClip clip,AudioClip preview,int candidate,int candidates,bool array,bool optional,string sourceKey="")
    {Key=key;Name=name;Group=group;SourceKey=sourceKey;Clip=clip;PreviewClip=preview;Candidate=candidate;Candidates=candidates;Array=array;Optional=optional;}
    public string Id=>Key+"#"+Candidate;
}
public sealed class AudioCatalogManagerWindow : EditorWindow
{
    readonly List<IAudioCatalogEditorProvider> providers=new List<IAudioCatalogEditorProvider>();
    readonly List<AudioCatalogClipRow> rows=new List<AudioCatalogClipRow>(),filteredRows=new List<AudioCatalogClipRow>();
    readonly List<Button> tabs=new List<Button>();
    [SerializeField] string selectedProviderName="",cueSearch="",cueGroup="전체",search="";
    [SerializeField] bool missingOnly,hideEmpty;
    [SerializeField] Vector2 inspectorScroll;
    int selectedIndex;
    bool rebuilding;
    ListView clipList;
    ToolbarSearchField cueSearchField;
    DropdownField groupFilter;
    VisualElement emptyState,drawer,editorHost;
    Label count,savedState,previewState,message,drawerTitle;
    Button saveButton,stopButton;
    IVisualElementScheduledItem stateSchedule;
    string feedback="";
    [MenuItem("JC Tool/오디오/오디오 카탈로그 관리자")]
    static void Open()=>GetWindow<AudioCatalogManagerWindow>("오디오 카탈로그 관리자");
    void OnEnable(){titleContent=new GUIContent("오디오 카탈로그 관리자");minSize=new Vector2(900,540);Undo.undoRedoPerformed+=HandleUndoRedo;RefreshProviders();}
    void OnDisable(){stateSchedule?.Pause();Undo.undoRedoPerformed-=HandleUndoRedo;rootVisualElement.Unbind();AudioCatalogEditorPreview.StopAll();ReleaseProviders();}
    static T Style<T>(T value,string name) where T:VisualElement{value.AddToClassList(name);return value;}
    static Label Text(string value,string style)=>Style(new Label(value),style);
    public static bool CanEdit=>!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&!BuildPipeline.isBuildingPlayer&&!EditorUtility.scriptCompilationFailed;
    IAudioCatalogEditorProvider Selected=>selectedIndex>=0&&selectedIndex<providers.Count?providers[selectedIndex]:null;
    bool MatchesFilter(IAudioCatalogEditorProvider provider)=>provider!=null&&(!missingOnly||provider.HasMissingReference)&&(string.IsNullOrWhiteSpace(search)||provider.DisplayName.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0||provider.AssetPath.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0);
    public void CreateGUI()
    {
        var root=rootVisualElement;stateSchedule?.Pause();root.UnregisterCallback<KeyDownEvent>(HandleShortcut);root.Unbind();root.Clear();root.AddToClassList("audio-root");
        var sheet=AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Editor/Tools/Audio/AudioCatalogManagerWindow.uss");if(sheet!=null&&!root.styleSheets.Contains(sheet))root.styleSheets.Add(sheet);
        var header=Style(new VisualElement(),"audio-header");root.Add(header);header.Add(Text("오디오 카탈로그","audio-title"));tabs.Clear();
        string[] labels={"근접 원소","전투 동작","원소 반응","아이템 드롭"};
        for(int i=0;i<providers.Count;i++){int index=i;var tab=Style(new Button(()=>SelectProvider(index)){text=labels[i],name="catalog-"+i},"audio-tab");header.Add(tab);tabs.Add(tab);}
        var tools=Style(new VisualElement(),"audio-tools");root.Add(tools);
        cueSearchField=Style(new ToolbarSearchField{name="cue-search",value=cueSearch},"audio-search");cueSearchField.tooltip="용도·원소·파일명 검색 · Ctrl+F";cueSearchField.RegisterValueChangedCallback(e=>{cueSearch=e.newValue??"";FilterRows();});tools.Add(cueSearchField);
        groupFilter=new DropdownField(new List<string>{"전체"},0){name="cue-group"};groupFilter.RegisterValueChangedCallback(e=>{if(!rebuilding){cueGroup=e.newValue;FilterRows();}});tools.Add(groupFilter);
        var available=new Toggle("지정된 소리만"){value=hideEmpty,name="assigned-only"};available.RegisterValueChangedCallback(e=>{hideEmpty=e.newValue;FilterRows();});tools.Add(available);
        count=Text("","audio-muted");tools.Add(count);
        stopButton=Style(new Button(()=>{AudioCatalogEditorPreview.StopAll();UpdateState();}){text="■  전체 정지",name="stop-preview"},"audio-secondary");tools.Add(stopButton);
        var table=Style(new VisualElement(){name="clip-table"},"audio-table");root.Add(table);
        var columns=Style(new VisualElement(),"audio-columns");table.Add(columns);
        columns.Add(Style(Text("용도 / 후보","audio-column"),"audio-purpose"));columns.Add(Style(Text("오디오 클립 · 바로 교체","audio-column"),"audio-clip-column"));columns.Add(Style(Text("길이","audio-column"),"audio-duration"));columns.Add(Style(Text("재생 / 후보 / 설정","audio-column"),"audio-row-actions"));
        clipList=Style(new ListView{itemsSource=filteredRows,fixedItemHeight=34,selectionType=SelectionType.Single,name="clip-list"},"audio-clip-list");clipList.makeItem=MakeRow;clipList.bindItem=BindRow;table.Add(clipList);
        emptyState=Style(new VisualElement(){name="cue-empty"},"audio-empty");emptyState.Add(Text("조건에 맞는 소리가 없습니다","audio-title"));emptyState.Add(new Button(ClearFilters){text="검색 초기화",name="clear-cues"});table.Add(emptyState);
        drawer=Style(new VisualElement(){name="settings-drawer"},"audio-drawer");root.Add(drawer);var drawerHeading=Style(new VisualElement(),"audio-drawer-heading");drawer.Add(drawerHeading);
        drawerTitle=Text("","audio-section");drawerHeading.Add(drawerTitle);drawerHeading.Add(Style(new Button(CloseSettings){text="닫기 ×",name="close-settings"},"audio-secondary"));
        var scroll=new ScrollView(ScrollViewMode.Vertical);scroll.horizontalScrollerVisibility=ScrollerVisibility.Hidden;drawer.Add(scroll);editorHost=new VisualElement{name="audio-editor"};scroll.Add(editorHost);
        var footer=Style(new VisualElement(),"audio-footer");root.Add(footer);savedState=Text("","audio-badge");savedState.name="audio-save-state";footer.Add(savedState);previewState=Text("","audio-muted");previewState.name="preview-state";footer.Add(previewState);message=Style(Text("","audio-muted"),"audio-feedback");footer.Add(message);
        footer.Add(Style(new Button(()=>OpenSettings("공통 설정","",true)){text="공통 설정…",name="catalog-settings"},"audio-secondary"));footer.Add(Style(new Button(RefreshProviders){text="다시 읽기"},"audio-secondary"));footer.Add(Style(new Button(ValidateSelected){text="검증"},"audio-secondary"));
        saveButton=Style(new Button(SaveSelected){text="저장",name="save-catalog"},"audio-primary");footer.Add(saveButton);
        root.RegisterCallback<KeyDownEvent>(HandleShortcut);RenderSelected();stateSchedule=root.schedule.Execute(UpdateState).Every(250);
    }
    VisualElement MakeRow()
    {
        var row=Style(new VisualElement(),"audio-data-row");row.Add(Style(new Label(){name="row-name"},"audio-purpose"));
        var field=Style(new ObjectField(){objectType=typeof(AudioClip),allowSceneObjects=false,name="clip-field"},"audio-clip-column");row.Add(field);
        field.RegisterValueChangedCallback(e=>{if(row.userData is AudioCatalogClipRow item){Selected.SetClip(item.Key,item.Candidate,e.newValue as AudioClip);RefreshRows();}});
        row.Add(Style(new Label(){name="clip-duration"},"audio-duration"));var actions=Style(new VisualElement(),"audio-row-actions");row.Add(actions);
        var play=Style(new Button(){name="row-preview",text="▶",tooltip="한 번 클릭하여 재생 · 다시 클릭하여 정지"},"audio-play");play.clicked+=()=>{if(row.userData is AudioCatalogClipRow item)TogglePreview(item);};actions.Add(play);
        var add=Style(new Button(){name="add-clip",text="+",tooltip="같은 용도의 클립 후보 추가"},"audio-small");add.clicked+=()=>{if(row.userData is AudioCatalogClipRow item){Selected.AddClip(item.Key);RefreshRows();int index=filteredRows.FindLastIndex(r=>r.Key==item.Key);if(index>=0)clipList.ScrollToItem(index);}};actions.Add(add);
        var remove=Style(new Button(){name="remove-clip",text="×",tooltip="이 클립 후보 삭제 · 단일 클립은 연결 해제"},"audio-small");remove.clicked+=()=>{if(row.userData is AudioCatalogClipRow item){Selected.RemoveClip(item.Key,item.Candidate);RefreshRows();}};actions.Add(remove);
        var settings=Style(new Button(){name="cue-settings",text="설정"},"audio-small");settings.clicked+=()=>{if(row.userData is AudioCatalogClipRow item)OpenSettings(item.Name,item.Key,false);};actions.Add(settings);return row;
    }
    void BindRow(VisualElement row,int index)
    {
        var item=filteredRows[index];row.userData=item;var name=row.Q<Label>("row-name");name.text=item.Name+(item.Candidates>1?"  ["+(item.Candidate+1)+"/"+item.Candidates+"]":"");name.tooltip=item.Name+(string.IsNullOrEmpty(item.SourceKey)?"":" · "+item.SourceKey);
        var field=row.Q<ObjectField>("clip-field");field.SetValueWithoutNotify(item.Clip);field.tooltip=item.Clip!=null?item.Clip.name:item.PreviewClip!=null?"미지정 · 기존 Resources 소리 사용: "+item.PreviewClip.name:item.Optional?"선택 큐 · 비어 있으면 기존 무음/fallback 규칙 유지":"클립 지정 필요";
        row.Q<Label>("clip-duration").text=item.PreviewClip!=null?item.PreviewClip.length.ToString("0.00")+"초":"—";
        row.Q<Button>("row-preview").SetEnabled(item.PreviewClip!=null);row.Q<Button>("add-clip").SetEnabled(item.Array&&CanEdit);row.Q<Button>("remove-clip").SetEnabled(CanEdit&&(item.Candidates>0||item.Clip!=null));field.SetEnabled(CanEdit);
        row.EnableInClassList("audio-row--alternate",index%2==1);UpdateRowPlayback(row,item);
    }
    void TogglePreview(AudioCatalogClipRow item)
    {if(AudioCatalogEditorPreview.CurrentClip==item.PreviewClip)AudioCatalogEditorPreview.StopAll();else AudioCatalogEditorPreview.Play(item.PreviewClip);UpdateState();}
    void UpdateRowPlayback(VisualElement row,AudioCatalogClipRow item)
    {bool playing=item.PreviewClip!=null&&AudioCatalogEditorPreview.CurrentClip==item.PreviewClip;row.Q<Button>("row-preview").text=playing?"■":"▶";row.EnableInClassList("audio-row--playing",playing);}
    void SelectProvider(int index)
    {selectedIndex=index;selectedProviderName=providers[index].DisplayName;cueSearch="";cueGroup="전체";cueSearchField?.SetValueWithoutNotify("");CloseSettings();RenderSelected();}
    void RenderSelected(){if(clipList==null)return;for(int i=0;i<tabs.Count;i++)tabs[i].EnableInClassList("audio-tab--selected",i==selectedIndex);RefreshRows();}
    void RefreshRows()
    {if(clipList==null)return;rows.Clear();if(Selected!=null)rows.AddRange(Selected.ReadClipRows());rebuilding=true;groupFilter.choices=new List<string>{"전체"};groupFilter.choices.AddRange(rows.Select(r=>r.Group).Distinct());if(!groupFilter.choices.Contains(cueGroup))cueGroup="전체";groupFilter.SetValueWithoutNotify(cueGroup);groupFilter.style.display=groupFilter.choices.Count>2?DisplayStyle.Flex:DisplayStyle.None;rebuilding=false;FilterRows();}
    void FilterRows()
    {
        if(clipList==null)return;filteredRows.Clear();filteredRows.AddRange(rows.Where(r=>(cueGroup=="전체"||r.Group==cueGroup)&&(!hideEmpty||r.PreviewClip!=null)&&(string.IsNullOrWhiteSpace(cueSearch)||(r.Name+" "+r.SourceKey+" "+r.Key+" "+(r.Clip!=null?r.Clip.name:r.PreviewClip!=null?r.PreviewClip.name:"")).IndexOf(cueSearch,StringComparison.OrdinalIgnoreCase)>=0)));
        clipList.RefreshItems();count.text=filteredRows.Count+" / "+rows.Count+"항목";emptyState.EnableInClassList("audio-empty--visible",filteredRows.Count==0);UpdateState();
    }
    void ClearFilters(){cueSearch="";cueGroup="전체";hideEmpty=false;cueSearchField.SetValueWithoutNotify("");groupFilter.SetValueWithoutNotify("전체");rootVisualElement.Q<Toggle>("assigned-only").SetValueWithoutNotify(false);FilterRows();}
    void OpenSettings(string name,string key,bool common)
    {if(Selected==null||editorHost==null)return;editorHost.Unbind();editorHost.Clear();drawerTitle.text=name+" · 설정";editorHost.Add(common?Selected.CreateCatalogSettings(RefreshRows):Selected.CreateCueInspector(key,RefreshRows));drawer.AddToClassList("audio-drawer--open");}
    void CloseSettings(){if(editorHost==null)return;editorHost.Unbind();editorHost.Clear();drawer.RemoveFromClassList("audio-drawer--open");}
    void HandleShortcut(KeyDownEvent e)
    {
        if(e.ctrlKey||e.commandKey){if(e.keyCode==KeyCode.F){cueSearchField.Focus();e.StopPropagation();}else if(e.keyCode==KeyCode.S){SaveSelected();e.StopPropagation();}return;}
        if(e.keyCode==KeyCode.Space&&e.target is VisualElement target&&clipList.Contains(target)&&clipList.selectedItem is AudioCatalogClipRow item){TogglePreview(item);e.StopPropagation();}
    }
    void UpdateState()
    {
        if(saveButton==null)return;bool dirty=Selected?.CatalogAsset!=null&&EditorUtility.IsDirty(Selected.CatalogAsset);savedState.text=dirty?"미저장 변경":"저장됨";savedState.EnableInClassList("audio-badge--dirty",dirty);CountIssues(Selected?.CollectIssues()??Array.Empty<AudioCatalogValidationIssue>(),out int warnings,out int errors);
        saveButton.SetEnabled(dirty&&Selected?.CatalogAsset!=null&&AssetDatabase.Contains(Selected.CatalogAsset)&&CanEdit&&errors==0);saveButton.tooltip=Selected?.DisplayName+"만 저장";
        AudioCatalogEditorPreview.RefreshPlaybackState();stopButton.SetEnabled(AudioCatalogEditorPreview.CurrentClip!=null);previewState.text=AudioCatalogEditorPreview.CurrentClip!=null?"재생 · "+AudioCatalogEditorPreview.CurrentClip.name:"▶ 한 번 재생 · 다시 클릭 정지";
        message.text=!CanEdit?"Editor 유휴 상태에서 편집 가능":errors>0?"오류 "+errors+"개":feedback;editorHost.SetEnabled(CanEdit);
        foreach(var button in rootVisualElement.Query<Button>("row-preview").ToList()){var row=button.parent.parent;if(row.userData is AudioCatalogClipRow item){UpdateRowPlayback(row,item);row.Q<ObjectField>("clip-field").SetEnabled(CanEdit);row.Q<Button>("add-clip").SetEnabled(item.Array&&CanEdit);row.Q<Button>("remove-clip").SetEnabled(CanEdit&&(item.Candidates>0||item.Clip!=null));}}
    }
    void RefreshProviders()
    {rootVisualElement.Unbind();ReleaseProviders();providers.Clear();providers.AddRange(AudioCatalogEditorProviderRegistry.CreateProviders());selectedIndex=0;for(int i=0;i<providers.Count;i++){providers[i].Refresh();if(providers[i].DisplayName==selectedProviderName)selectedIndex=i;}if(providers.Count>0)selectedProviderName=providers[selectedIndex].DisplayName;CloseSettings();RenderSelected();Repaint();}
    void HandleUndoRedo()=>RefreshProviders();
    void ReleaseProviders(){foreach(var p in providers)(p as IDisposable)?.Dispose();}
    void ValidateSelected(){if(Selected==null)return;CountIssues(Selected.CollectIssues(),out int warnings,out int errors);feedback=errors==0&&warnings==0?"검증 통과":"경고 "+warnings+" / 오류 "+errors;UpdateState();}
    void SaveSelected(){if(!CanEdit||Selected?.CatalogAsset==null||!AssetDatabase.Contains(Selected.CatalogAsset))return;CountIssues(Selected.CollectIssues(),out int warnings,out int errors);if(errors>0){ValidateSelected();return;}AssetDatabase.SaveAssetIfDirty(Selected.CatalogAsset);feedback="저장 완료";UpdateState();}
    static void CountIssues(IReadOnlyList<AudioCatalogValidationIssue> issues,out int warnings,out int errors){warnings=errors=0;foreach(var issue in issues){if(issue.Type==MessageType.Error)errors++;else if(issue.Type==MessageType.Warning)warnings++;}}
}

public static class AudioCatalogManagerCommandLineValidation
{
    public static void ValidateFromCommandLine()
    {
        List<IAudioCatalogEditorProvider> providers = AudioCatalogEditorProviderRegistry.CreateProviders();
        try
        {
            if (providers.Count == 0) throw new InvalidOperationException("오디오 카탈로그 제공자가 없습니다.");
            foreach (IAudioCatalogEditorProvider provider in providers)
            {
                provider.Refresh();
                if (provider.CatalogAsset == null) throw new MissingReferenceException(provider.DisplayName);
                string before = EditorJsonUtility.ToJson(provider.CatalogAsset);
                foreach (AudioCatalogValidationIssue issue in provider.CollectIssues())
                    if (issue.Type == MessageType.Error) throw new InvalidOperationException(provider.DisplayName + ": " + issue.Message);
                if (before != EditorJsonUtility.ToJson(provider.CatalogAsset))
                    throw new InvalidOperationException("읽기 전용 검증이 카탈로그를 변경했습니다.");
            }
            Debug.Log("[OVERBURST] 현행 오디오 카탈로그 " + providers.Count + "종 읽기 전용 검증 PASS.");
        }
        finally { foreach (IAudioCatalogEditorProvider provider in providers) (provider as IDisposable)?.Dispose(); }
    }
}
public static class AudioCatalogEditorPreview
{
    private static readonly Type AudioUtilType =
        typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
    private static readonly MethodInfo PlayMethod =
        FindMethod("PlayPreviewClip") ?? FindMethod("PlayClip");
    private static readonly MethodInfo StopMethod =
        FindMethod("StopAllPreviewClips") ?? FindMethod("StopAllClips");

    public static AudioClip CurrentClip { get; private set; }
    public static void Play(AudioClip clip)
    {
        if (clip == null || PlayMethod == null)
            return;
        StopAll();
        ParameterInfo[] parameters = PlayMethod.GetParameters();
        object[] arguments = new object[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            Type type = parameters[i].ParameterType;
            arguments[i] = type == typeof(AudioClip)
                ? clip
                : type == typeof(int)
                    ? 0
                    : type == typeof(bool)
                        ? false
                        : type.IsValueType ? Activator.CreateInstance(type) : null;
        }
        PlayMethod.Invoke(null, arguments); CurrentClip = clip;
    }

    public static void StopAll()
    {
        StopMethod?.Invoke(null, null); CurrentClip = null;
    }

    public static void RefreshPlaybackState()
    {
        if(CurrentClip==null||AudioUtilType==null)return;
        var playing=AudioUtilType.GetMethod("IsPreviewClipPlaying",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
        if(playing!=null&&playing.GetParameters().Length==0&&!(bool)playing.Invoke(null,null))CurrentClip=null;
    }

    private static MethodInfo FindMethod(string name)
    {
        if (AudioUtilType == null)
            return null;
        MethodInfo[] methods = AudioUtilType.GetMethods(
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < methods.Length; i++)
        {
            if (methods[i].Name != name)
                continue;
            ParameterInfo[] parameters = methods[i].GetParameters();
            if (parameters.Length == 0 || parameters[0].ParameterType == typeof(AudioClip))
                return methods[i];
        }
        return null;
    }
}
