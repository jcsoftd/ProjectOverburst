using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

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
    private const float MinimumLeftWidth = 155f;
    private const float MaximumLeftWidth = 205f;
    private readonly List<IAudioCatalogEditorProvider> providers =
        new List<IAudioCatalogEditorProvider>();

    [SerializeField] private Vector2 catalogScroll;
    [SerializeField] private Vector2 inspectorScroll;
    [SerializeField] private string search = string.Empty;
    [SerializeField] private bool missingOnly;
    [SerializeField] private string selectedProviderName = string.Empty;
    private int selectedIndex;

    [MenuItem("JC Tool/오디오/오디오 카탈로그 관리자")]
    private static void Open()
    {
        GetWindow<AudioCatalogManagerWindow>("오디오 카탈로그 관리자");
    }

    private void OnEnable()
    {
        titleContent = new GUIContent("오디오 카탈로그 관리자");
        minSize = new Vector2(760f, 540f);
        Undo.undoRedoPerformed += HandleUndoRedo;
        RefreshProviders();
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= HandleUndoRedo;
        AudioCatalogEditorPreview.StopAll();
        ReleaseProviders();
    }

    private void OnGUI()
    {
        DrawToolbar();
        EditorGUILayout.BeginHorizontal();
        DrawCatalogList();
        GUILayout.Space(2f);
        DrawSelectedCatalog();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("새로고침", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            RefreshProviders();
        if (GUILayout.Button("검증", EditorStyles.toolbarButton, GUILayout.Width(50f)))
            ValidateSelected();
        if (GUILayout.Button("저장", EditorStyles.toolbarButton, GUILayout.Width(50f)))
            SaveSelected();
        GUILayout.Space(4f);
        EditorGUILayout.LabelField("검색", GUILayout.Width(30f));
        search = EditorGUILayout.TextField(search);
        missingOnly = GUILayout.Toggle(
            missingOnly,
            "누락만",
            EditorStyles.toolbarButton,
            GUILayout.Width(62f));
        if (GUILayout.Button("미리듣기 정지", EditorStyles.toolbarButton, GUILayout.Width(96f)))
            AudioCatalogEditorPreview.StopAll();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawCatalogList()
    {
        float width = Mathf.Clamp(position.width * 0.2f, MinimumLeftWidth, MaximumLeftWidth);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(width), GUILayout.ExpandHeight(false));
        EditorGUILayout.LabelField("카탈로그", EditorStyles.miniBoldLabel);
        float listHeight = Mathf.Clamp(providers.Count * 22f + 4f, 26f, 160f);
        catalogScroll = EditorGUILayout.BeginScrollView(catalogScroll, GUILayout.Height(listHeight));
        for (int i = 0; i < providers.Count; i++)
        {
            IAudioCatalogEditorProvider provider = providers[i];
            if (!MatchesFilter(provider))
                continue;

            IReadOnlyList<AudioCatalogValidationIssue> issues = provider.CollectIssues();
            CountIssues(issues, out int warningCount, out int errorCount);
            string label = BuildCatalogLabel(provider, warningCount, errorCount);
            bool selected = i == selectedIndex;
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = GetStatusColor(provider.CatalogAsset == null, warningCount, errorCount);
            if (GUILayout.Toggle(selected, label, "Button", GUILayout.Height(20f)) && !selected)
            {
                selectedIndex = i;
                selectedProviderName = provider.DisplayName;
                inspectorScroll = Vector2.zero;
            }
            GUI.backgroundColor = previous;
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawSelectedCatalog()
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        if (providers.Count == 0 || selectedIndex < 0 || selectedIndex >= providers.Count)
        {
            EditorGUILayout.HelpBox("지원되는 오디오 카탈로그가 없습니다.", MessageType.Info);
            EditorGUILayout.EndVertical();
            return;
        }

        IAudioCatalogEditorProvider provider = providers[selectedIndex];
        EditorGUI.BeginDisabledGroup(EditorApplication.isPlayingOrWillChangePlaymode
            || EditorApplication.isCompiling || EditorApplication.isUpdating);
        DrawCatalogHeader(provider);
        inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
        provider.DrawInspector();
        EditorGUILayout.EndScrollView();
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndVertical();
    }

    private void DrawCatalogHeader(IAudioCatalogEditorProvider provider)
    {
        IReadOnlyList<AudioCatalogValidationIssue> issues = provider.CollectIssues();
        CountIssues(issues, out int warningCount, out int errorCount);
        string dirtyState = provider.CatalogAsset != null && EditorUtility.IsDirty(provider.CatalogAsset)
            ? "변경됨"
            : "저장됨";
        string validationState = errorCount == 0 && warningCount == 0
            ? "정상"
            : "경고 " + warningCount + " / 오류 " + errorCount;
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(provider.DisplayName, EditorStyles.boldLabel, GUILayout.Width(126f));
        Color previous = GUI.contentColor;
        GUI.contentColor = errorCount > 0
            ? new Color(1f, 0.35f, 0.35f)
            : warningCount > 0 ? new Color(1f, 0.65f, 0.15f) : previous;
        EditorGUILayout.LabelField(dirtyState + " · " + validationState, EditorStyles.miniLabel);
        GUI.contentColor = previous;
        GUILayout.FlexibleSpace();
        EditorGUI.BeginDisabledGroup(provider.CatalogAsset == null);
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField("행/구분 " + provider.ReadElementCount()
            + " · 연결 클립 " + provider.ReadClipCount(), EditorStyles.miniLabel);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("경로", EditorStyles.miniLabel, GUILayout.Width(28f));
        EditorGUILayout.SelectableLabel(
            provider.CatalogAsset != null ? provider.AssetPath : "에셋 없음",
            EditorStyles.textField,
            GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUI.BeginDisabledGroup(provider.CatalogAsset == null);
        if (GUILayout.Button("선택", GUILayout.Width(44f)))
            Selection.activeObject = provider.CatalogAsset;
        if (GUILayout.Button("위치", GUILayout.Width(44f)))
            EditorGUIUtility.PingObject(provider.CatalogAsset);
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
    }

    private bool MatchesFilter(IAudioCatalogEditorProvider provider)
    {
        if (provider == null)
            return false;
        if (missingOnly && !provider.HasMissingReference)
            return false;
        return string.IsNullOrWhiteSpace(search)
            || provider.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
            || provider.AssetPath.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void RefreshProviders()
    {
        string previousName = selectedProviderName;
        ReleaseProviders();
        providers.Clear();
        providers.AddRange(AudioCatalogEditorProviderRegistry.CreateProviders());
        selectedIndex = 0;
        for (int i = 0; i < providers.Count; i++)
        {
            providers[i].Refresh();
            if (!string.IsNullOrEmpty(previousName)
                && string.Equals(providers[i].DisplayName, previousName, StringComparison.Ordinal))
            {
                selectedIndex = i;
            }
        }

        if (providers.Count > 0)
            selectedProviderName = providers[selectedIndex].DisplayName;
        Repaint();
    }

    private void HandleUndoRedo()
    {
        for (int i = 0; i < providers.Count; i++)
            providers[i].Refresh();
        Repaint();
    }

    private void ReleaseProviders()
    {
        foreach (IAudioCatalogEditorProvider provider in providers)
            (provider as IDisposable)?.Dispose();
    }

    private void ValidateSelected()
    {
        if (providers.Count == 0 || selectedIndex < 0 || selectedIndex >= providers.Count)
            return;
        IAudioCatalogEditorProvider provider = providers[selectedIndex];
        IReadOnlyList<AudioCatalogValidationIssue> issues = provider.CollectIssues();
        CountIssues(issues, out int warningCount, out int errorCount);
        if (warningCount == 0 && errorCount == 0)
        {
            Debug.Log("[OVERBURST] 오디오 카탈로그 검증 통과: " + provider.DisplayName);
            ShowNotification(new GUIContent("검증을 통과했습니다."));
        }
        else
        {
            Debug.LogWarning(
                "[OVERBURST] 오디오 카탈로그 검증 결과: 경고 "
                + warningCount + "개, 오류 " + errorCount + "개 - " + provider.DisplayName);
            ShowNotification(new GUIContent("경고 " + warningCount + "개 / 오류 " + errorCount + "개"));
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].Type == MessageType.Error)
                    Debug.LogError("[OVERBURST] " + issues[i].Message);
                else if (issues[i].Type == MessageType.Warning)
                    Debug.LogWarning("[OVERBURST] " + issues[i].Message);
            }
        }
        Repaint();
    }

    private void SaveSelected()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling
            || EditorApplication.isUpdating
            || providers.Count == 0 || selectedIndex < 0 || selectedIndex >= providers.Count)
            return;
        UnityEngine.Object asset = providers[selectedIndex].CatalogAsset;
        if (asset == null)
            return;
        AssetDatabase.SaveAssetIfDirty(asset);
        ShowNotification(new GUIContent("카탈로그를 저장했습니다."));
        Repaint();
    }

    private static string BuildCatalogLabel(
        IAudioCatalogEditorProvider provider,
        int warningCount,
        int errorCount)
    {
        if (provider.CatalogAsset == null)
            return "[누락] " + provider.DisplayName;
        if (errorCount > 0 || warningCount > 0)
            return "[" + warningCount + "/" + errorCount + "] " + provider.DisplayName;
        return "[정상] " + provider.DisplayName;
    }

    private static Color GetStatusColor(bool missing, int warningCount, int errorCount)
    {
        if (missing || errorCount > 0)
            return new Color(1f, 0.65f, 0.65f);
        if (warningCount > 0)
            return new Color(1f, 0.88f, 0.55f);
        return new Color(0.7f, 1f, 0.72f);
    }

    private static void CountIssues(
        IReadOnlyList<AudioCatalogValidationIssue> issues,
        out int warningCount,
        out int errorCount)
    {
        warningCount = 0;
        errorCount = 0;
        for (int i = 0; i < issues.Count; i++)
        {
            if (issues[i].Type == MessageType.Error)
                errorCount++;
            else if (issues[i].Type == MessageType.Warning)
                warningCount++;
        }
    }
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
        PlayMethod.Invoke(null, arguments);
    }

    public static void StopAll()
    {
        StopMethod?.Invoke(null, null);
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
