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

public sealed class AudioCatalogManagerWindow : EditorWindow
{
    readonly List<IAudioCatalogEditorProvider> providers = new List<IAudioCatalogEditorProvider>();
    [SerializeField] string search = "", cueSearch = "", selectedProviderName = "";
    [SerializeField] bool missingOnly;
    [SerializeField] Vector2 inspectorScroll;
    int selectedIndex;
    VisualElement catalogList, inspectorHost, issuesHost;
    Label title, summary, path, savedState, previewState, message;
    Button saveButton, stopButton;
    IVisualElementScheduledItem stateSchedule;
    string feedback = "";
    [MenuItem("JC Tool/오디오/오디오 카탈로그 관리자")]
    static void Open() => GetWindow<AudioCatalogManagerWindow>("오디오 카탈로그 관리자");
    void OnEnable()
    {
        titleContent = new GUIContent("오디오 카탈로그 관리자"); minSize = new Vector2(960,680);
        feedback = "클립을 지정하고 선택한 카탈로그만 저장합니다.";
        Undo.undoRedoPerformed += HandleUndoRedo; RefreshProviders();
    }
    void OnDisable()
    { stateSchedule?.Pause(); Undo.undoRedoPerformed -= HandleUndoRedo; rootVisualElement.Unbind(); AudioCatalogEditorPreview.StopAll(); ReleaseProviders(); }
    static T Style<T>(T element,string name) where T:VisualElement { element.AddToClassList(name); return element; }
    static Label Text(string text,string style)=>Style(new Label(text),style);
    public void CreateGUI()
    {
        var root=rootVisualElement; stateSchedule?.Pause(); root.Unbind(); root.Clear(); root.AddToClassList("audio-root");
        var stylesheet=AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Editor/Tools/Audio/AudioCatalogManagerWindow.uss");
        if(stylesheet!=null&&!root.styleSheets.Contains(stylesheet))root.styleSheets.Add(stylesheet);
        var header=Style(new VisualElement(),"audio-header"); var heading=new VisualElement();
        heading.Add(Text("OVERBURST  /  SOUND LIBRARY","audio-eyebrow")); heading.Add(Text("오디오 카탈로그","audio-title")); header.Add(heading);
        header.Add(Text("탐색 · 클립 지정 · 미리듣기","audio-muted")); root.Add(header);
        var body=Style(new VisualElement(),"audio-body"); root.Add(body);
        var sidebar=Style(new VisualElement(),"audio-sidebar"); body.Add(sidebar); sidebar.Add(Text("카탈로그","audio-section"));
        var catalogSearch=new ToolbarSearchField {name="catalog-search",value=search}; catalogSearch.tooltip="카탈로그 이름 또는 경로 검색";
        catalogSearch.style.width=Length.Percent(100);catalogSearch.style.maxWidth=Length.Percent(100);catalogSearch.style.minWidth=0;
        catalogSearch.RegisterValueChangedCallback(e=>{search=e.newValue??"";BuildCatalogList();RenderSelected();}); sidebar.Add(catalogSearch);
        var missing=new Toggle("누락된 카탈로그만") {name="missing-only",value=missingOnly}; missing.RegisterValueChangedCallback(e=>{missingOnly=e.newValue;BuildCatalogList();RenderSelected();}); sidebar.Add(missing);
        catalogList=Style(new VisualElement(),"audio-catalog-list"); sidebar.Add(catalogList);
        sidebar.Add(Style(new VisualElement(),"audio-spacer")); sidebar.Add(Style(new Button(RefreshProviders){text="원본 다시 읽기"},"audio-secondary"));
        sidebar.Add(Text("설정은 게임 재생에 사용됩니다.\n미리듣기는 클립 원음을 재생합니다.","audio-note"));
        var main=Style(new VisualElement(),"audio-main"); body.Add(main);
        var headingRow=Style(new VisualElement(),"audio-heading-row"); main.Add(headingRow); var labels=new VisualElement(); headingRow.Add(labels);
        title=Text("","audio-sheet-title"); summary=Text("","audio-muted"); labels.Add(title);labels.Add(summary);
        var locate=Style(new Button(()=>{var asset=Selected?.CatalogAsset;if(asset!=null)EditorGUIUtility.PingObject(asset);}){text="원본 위치"},"audio-secondary"); headingRow.Add(locate);
        path=Text("","audio-path"); main.Add(path);
        var tools=Style(new VisualElement(),"audio-tools"); main.Add(tools);
        var cueField=Style(new ToolbarSearchField {name="cue-search",value=cueSearch},"audio-cue-search");cueField.tooltip="선택한 카탈로그의 클립 구분 검색";
        cueField.RegisterValueChangedCallback(e=>{cueSearch=e.newValue??"";FilterCues();});tools.Add(cueField);
        tools.Add(Style(new Button(()=>{cueSearch="";cueField.SetValueWithoutNotify("");FilterCues();}){text="검색 초기화"},"audio-secondary"));
        tools.Add(Style(new Button(()=>SetFoldouts(true)){text="모두 펼치기"},"audio-secondary"));tools.Add(Style(new Button(()=>SetFoldouts(false)){text="모두 접기"},"audio-secondary"));
        var scroll=Style(new ScrollView(),"audio-inspector-scroll");main.Add(scroll);
        inspectorHost=new VisualElement {name="audio-editor"};scroll.Add(inspectorHost);
        issuesHost=Style(new VisualElement(),"audio-issues");scroll.Add(issuesHost);
        var footer=Style(new VisualElement(),"audio-footer");root.Add(footer);
        var footerInfo=Style(new VisualElement(),"audio-footer-info");footer.Add(footerInfo);
        var badges=Style(new VisualElement(),"audio-badges");footerInfo.Add(badges);savedState=Text("","audio-badge");savedState.name="audio-save-state";previewState=Text("","audio-badge");badges.Add(savedState);badges.Add(previewState);
        message=Text(feedback,"audio-note");footerInfo.Add(message);
        var actions=Style(new VisualElement(),"audio-actions");footer.Add(actions);
        stopButton=Style(new Button(()=>{AudioCatalogEditorPreview.StopAll();UpdateState();}){text="■  미리듣기 정지",name="stop-preview"},"audio-secondary");actions.Add(stopButton);
        actions.Add(Style(new Button(ValidateSelected){text="검증"},"audio-secondary"));saveButton=Style(new Button(SaveSelected){text="선택 카탈로그 저장",name="save-catalog"},"audio-primary");actions.Add(saveButton);
        BuildCatalogList();RenderSelected();stateSchedule=root.schedule.Execute(UpdateState).Every(500);
    }
    IAudioCatalogEditorProvider Selected => selectedIndex>=0&&selectedIndex<providers.Count?providers[selectedIndex]:null;
    bool MatchesFilter(IAudioCatalogEditorProvider provider)=>provider!=null&&(!missingOnly||provider.HasMissingReference)&&(string.IsNullOrWhiteSpace(search)||provider.DisplayName.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0||provider.AssetPath.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0);
    public static bool CanEdit => !EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&!BuildPipeline.isBuildingPlayer&&!EditorUtility.scriptCompilationFailed;
    void BuildCatalogList()
    {
        if(catalogList==null)return;catalogList.Clear();
        for(int i=0;i<providers.Count;i++)
        {
            var provider=providers[i];if(!MatchesFilter(provider))continue;int index=i;
            var button=Style(new Button(()=>SelectProvider(index)){name="catalog-"+i},"audio-catalog");
            button.EnableInClassList("audio-catalog--selected",index==selectedIndex);
            button.Add(Text(provider.DisplayName,"audio-catalog-name"));
            var issues=provider.CollectIssues();CountIssues(issues,out int warnings,out int errors);
            button.Add(Text(provider.ReadElementCount()+"구분 · "+provider.ReadClipCount()+"클립"+(errors>0?" · 오류 "+errors:warnings>0?" · 경고 "+warnings:""),"audio-catalog-count"));catalogList.Add(button);
        }
        if(catalogList.childCount==0)catalogList.Add(Text("검색 조건에 맞는\n카탈로그가 없습니다.","audio-note"));
    }
    void SelectProvider(int index)
    {selectedIndex=index;selectedProviderName=providers[index].DisplayName;cueSearch="";rootVisualElement.Q<ToolbarSearchField>("cue-search")?.SetValueWithoutNotify("");BuildCatalogList();RenderSelected();}
    void RenderSelected()
    {
        if(inspectorHost==null)return;inspectorHost.Unbind();inspectorHost.Clear();issuesHost.Clear();
        var provider=Selected;bool visible=provider!=null&&MatchesFilter(provider);
        title.text=visible?provider.DisplayName:"카탈로그를 선택하세요";summary.text=visible?provider.ReadElementCount()+"구분 · 연결 클립 "+provider.ReadClipCount():"검색 조건을 확인하거나 왼쪽에서 선택하세요.";path.text=visible?provider.AssetPath:"";path.tooltip=path.text;
        if(visible)inspectorHost.Add(provider.CreateInspector(()=>{BuildCatalogList();summary.text=provider.ReadElementCount()+"구분 · 연결 클립 "+provider.ReadClipCount();FilterCues();UpdateState();}));
        FilterCues();UpdateState();
    }
    void FilterCues()
    {
        if(inspectorHost==null)return;
        foreach(var card in inspectorHost.Query<Foldout>(className:"audio-cue-card").ToList())
            card.style.display=string.IsNullOrWhiteSpace(cueSearch)||card.text.IndexOf(cueSearch,StringComparison.OrdinalIgnoreCase)>=0?DisplayStyle.Flex:DisplayStyle.None;
        var empty=inspectorHost.Q<Label>("cue-empty");
        bool noResult=!string.IsNullOrWhiteSpace(cueSearch)&&inspectorHost.Query<Foldout>(className:"audio-cue-card").ToList().All(c=>c.style.display==DisplayStyle.None);
        if(noResult&&empty==null){empty=Text("검색한 클립 구분이 없습니다. 검색어를 바꾸거나 검색을 초기화하세요.","audio-note");empty.name="cue-empty";inspectorHost.Add(empty);}
        if(empty!=null)empty.style.display=noResult?DisplayStyle.Flex:DisplayStyle.None;
    }
    void SetFoldouts(bool expanded)
    {foreach(var card in inspectorHost.Query<Foldout>(className:"audio-cue-card").ToList())card.value=expanded;}
    void UpdateState()
    {
        if(saveButton==null)return;var provider=Selected;var asset=provider?.CatalogAsset;
        bool dirty=asset!=null&&EditorUtility.IsDirty(asset);savedState.text=dirty?"미저장 변경 있음":"저장된 상태";savedState.EnableInClassList("audio-badge--dirty",dirty);
        var issues=provider?.CollectIssues()??Array.Empty<AudioCatalogValidationIssue>();CountIssues(issues,out int warnings,out int errors);
        saveButton.SetEnabled(dirty&&asset!=null&&AssetDatabase.Contains(asset)&&CanEdit&&errors==0&&MatchesFilter(provider));
        inspectorHost.SetEnabled(CanEdit);AudioCatalogEditorPreview.RefreshPlaybackState();stopButton.SetEnabled(AudioCatalogEditorPreview.CurrentClip!=null);
        previewState.text=AudioCatalogEditorPreview.CurrentClip!=null?"미리듣기 · "+AudioCatalogEditorPreview.CurrentClip.name:"미리듣기 대기";
        message.text=CanEdit?feedback:"Play·컴파일·임포트·빌드가 끝나면 편집할 수 있습니다.";
        issuesHost.Clear();foreach(var issue in issues)issuesHost.Add(new HelpBox(issue.Message,issue.Type==MessageType.Error?HelpBoxMessageType.Error:issue.Type==MessageType.Warning?HelpBoxMessageType.Warning:HelpBoxMessageType.Info));
    }
    void RefreshProviders()
    {
        rootVisualElement.Unbind();ReleaseProviders();providers.Clear();providers.AddRange(AudioCatalogEditorProviderRegistry.CreateProviders());selectedIndex=0;
        for(int i=0;i<providers.Count;i++){providers[i].Refresh();if(providers[i].DisplayName==selectedProviderName)selectedIndex=i;}
        if(providers.Count>0)selectedProviderName=providers[selectedIndex].DisplayName;
        BuildCatalogList();RenderSelected();Repaint();
    }
    void HandleUndoRedo()=>RefreshProviders();
    void ReleaseProviders(){foreach(var provider in providers)(provider as IDisposable)?.Dispose();}
    void ValidateSelected()
    {var provider=Selected;if(provider==null)return;CountIssues(provider.CollectIssues(),out int warnings,out int errors);feedback=errors==0&&warnings==0?"검증 통과. 현재 연결과 재생 설정이 유효합니다.":"검증 결과 · 경고 "+warnings+" / 오류 "+errors;UpdateState();}
    void SaveSelected()
    {
        var provider=Selected;if(!CanEdit||provider?.CatalogAsset==null||!AssetDatabase.Contains(provider.CatalogAsset))return;
        CountIssues(provider.CollectIssues(),out int warnings,out int errors);if(errors>0){feedback="오류를 먼저 해결한 뒤 저장하세요.";UpdateState();return;}
        AssetDatabase.SaveAssetIfDirty(provider.CatalogAsset);feedback=provider.DisplayName+"을 저장했습니다.";UpdateState();
    }
    static void CountIssues(IReadOnlyList<AudioCatalogValidationIssue> issues,out int warnings,out int errors)
    {warnings=errors=0;foreach(var issue in issues){if(issue.Type==MessageType.Error)errors++;else if(issue.Type==MessageType.Warning)warnings++;}}
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
