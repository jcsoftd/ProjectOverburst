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
            new MeleeElementSfxCatalogEditorProvider()
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
        DrawCatalogHeader(provider);
        inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
        provider.DrawInspector();
        EditorGUILayout.EndScrollView();
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
        EditorGUILayout.LabelField(provider.DisplayName, EditorStyles.boldLabel, GUILayout.Width(112f));
        Color previous = GUI.contentColor;
        GUI.contentColor = errorCount > 0
            ? new Color(1f, 0.35f, 0.35f)
            : warningCount > 0 ? new Color(1f, 0.65f, 0.15f) : previous;
        EditorGUILayout.LabelField(dirtyState + " · " + validationState, EditorStyles.miniLabel);
        GUI.contentColor = previous;
        GUILayout.FlexibleSpace();
        EditorGUI.BeginDisabledGroup(provider.CatalogAsset == null);
        if (GUILayout.Button("기본 매핑", GUILayout.Width(68f))
            && EditorUtility.DisplayDialog(
                "확정 기본 매핑 적용",
                "현재 수동 편집 내용을 6원소·12클립 확정 기본 매핑으로 교체합니다. 기존 설정을 덮어쓸 수 있으며 실행 후 Undo로 되돌릴 수 있습니다.",
                "기본 매핑 적용",
                "취소"))
        {
            provider.ApplyConfirmedDefaults();
            ShowNotification(new GUIContent("기본 매핑을 적용했습니다. 저장 전 Undo할 수 있습니다."));
        }

        EditorGUI.BeginDisabledGroup(provider.MissingElementCount == 0);
        if (GUILayout.Button("누락 보충", GUILayout.Width(68f)))
        {
            int added = provider.AddMissingElements();
            ShowNotification(new GUIContent("누락 원소 행 " + added + "개를 보충했습니다."));
        }
        EditorGUI.EndDisabledGroup();
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();

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

    private void ValidateSelected()
    {
        if (providers.Count == 0 || selectedIndex < 0 || selectedIndex >= providers.Count)
            return;
        IAudioCatalogEditorProvider provider = providers[selectedIndex];
        IReadOnlyList<AudioCatalogValidationIssue> issues = provider.CollectIssues();
        CountIssues(issues, out int warningCount, out int errorCount);
        if (warningCount == 0 && errorCount == 0)
        {
            Debug.Log("[ProjectVTP] 오디오 카탈로그 검증 통과: " + provider.DisplayName);
            ShowNotification(new GUIContent("검증을 통과했습니다."));
        }
        else
        {
            Debug.LogWarning(
                "[ProjectVTP] 오디오 카탈로그 검증 결과: 경고 "
                + warningCount + "개, 오류 " + errorCount + "개 - " + provider.DisplayName);
            ShowNotification(new GUIContent("경고 " + warningCount + "개 / 오류 " + errorCount + "개"));
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].Type == MessageType.Error)
                    Debug.LogError("[ProjectVTP] " + issues[i].Message);
                else if (issues[i].Type == MessageType.Warning)
                    Debug.LogWarning("[ProjectVTP] " + issues[i].Message);
            }
        }
        Repaint();
    }

    private void SaveSelected()
    {
        if (providers.Count == 0 || selectedIndex < 0 || selectedIndex >= providers.Count)
            return;
        UnityEngine.Object asset = providers[selectedIndex].CatalogAsset;
        if (asset == null)
            return;
        AssetDatabase.SaveAssetIfDirty(asset);
        AssetDatabase.SaveAssets();
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

public sealed class MeleeElementSfxCatalogEditorProvider : IAudioCatalogEditorProvider
{
    private const float ElementWidth = 64f;
    private const float CueWidth = 46f;
    private const float ClipWidth = 180f;
    private const float ManageWidth = 82f;
    private const float NumberWidth = 42f;
    private const float RangeWidth = 82f;
    private const float ToggleWidth = 30f;
    private const float IconWidth = 28f;
    private const float ElementActionWidth = 36f;
    private const float TableWidth = 790f;
    private MeleeElementSfxCatalog catalog;
    private SerializedObject serializedCatalog;
    private readonly Dictionary<int, bool> clipFoldouts = new Dictionary<int, bool>();

    public string DisplayName => "근접 원소 효과음";
    public string AssetPath => MeleeElementSfxEditorDefaults.CatalogPath;
    public UnityEngine.Object CatalogAsset => catalog;
    public bool HasMissingReference => catalog == null || ContainsMissingReference();
    public int MissingElementCount => CountMissingElements();

    public void Refresh()
    {
        catalog = AssetDatabase.LoadAssetAtPath<MeleeElementSfxCatalog>(AssetPath);
        serializedCatalog = catalog != null ? new SerializedObject(catalog) : null;
    }

    public void DrawInspector()
    {
        if (catalog == null || serializedCatalog == null)
        {
            EditorGUILayout.HelpBox(
                "근접 원소 효과음 카탈로그 에셋이 없습니다. 기본 설정 유틸리티로 에셋을 생성해야 합니다.",
                MessageType.Error);
            return;
        }

        serializedCatalog.UpdateIfRequiredOrScript();
        SerializedProperty entries = serializedCatalog.FindProperty("entries");
        IReadOnlyList<AudioCatalogValidationIssue> issues = CollectIssues();
        DrawTableHeader();
        for (int i = 0; i < entries.arraySize; i++)
            DrawElementRow(entries.GetArrayElementAtIndex(i), i, issues);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("빈 원소 행 추가", GUILayout.Width(100f)))
            entries.arraySize++;
        if (GUILayout.Button("전체 정지", GUILayout.Width(70f)))
            AudioCatalogEditorPreview.StopAll();
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        if (!serializedCatalog.hasModifiedProperties)
            return;

        Undo.RecordObject(catalog, "근접 원소 효과음 카탈로그 편집");
        serializedCatalog.ApplyModifiedProperties();
        EditorUtility.SetDirty(catalog);
    }

    public IReadOnlyList<AudioCatalogValidationIssue> CollectIssues()
    {
        List<AudioCatalogValidationIssue> issues = new List<AudioCatalogValidationIssue>();
        if (catalog == null)
        {
            issues.Add(new AudioCatalogValidationIssue(MessageType.Error, "카탈로그 에셋이 누락되었습니다."));
            return issues;
        }

        HashSet<WeaponElement> elements = new HashSet<WeaponElement>();
        HashSet<AudioClip> clips = new HashSet<AudioClip>();
        if (catalog.entries == null || catalog.entries.Length == 0)
        {
            issues.Add(new AudioCatalogValidationIssue(MessageType.Error, "원소 행이 비어 있습니다."));
        }
        else
        {
            for (int i = 0; i < catalog.entries.Length; i++)
            {
                MeleeElementSfxEntry entry = catalog.entries[i];
                if (entry == null)
                {
                    issues.Add(new AudioCatalogValidationIssue(
                        MessageType.Error,
                        "비어 있는 원소 행이 있습니다. 행 번호: " + (i + 1)));
                    continue;
                }
                if (!elements.Add(entry.element))
                {
                    issues.Add(new AudioCatalogValidationIssue(
                        MessageType.Error,
                        "원소 행이 중복되었습니다: " + GetElementName(entry.element)));
                }
                CollectCueIssues(entry.element, "베기음", entry.slash, clips, issues);
                CollectCueIssues(entry.element, "적중음", entry.hit, clips, issues);
            }
        }

        MeleeElementSfxDefaultSpec[] specs = MeleeElementSfxEditorDefaults.Specs;
        for (int i = 0; i < specs.Length; i++)
        {
            if (!elements.Contains(specs[i].Element))
            {
                issues.Add(new AudioCatalogValidationIssue(
                    MessageType.Error,
                    "필수 원소 행이 누락되었습니다: " + GetElementName(specs[i].Element)));
            }
        }
        return issues;
    }

    public void ApplyConfirmedDefaults()
    {
        if (catalog == null)
            return;
        Undo.RecordObject(catalog, "근접 원소 효과음 기본 매핑 적용");
        MeleeElementSfxSetupUtility.ApplyDefaultMapping(catalog);
        EditorUtility.SetDirty(catalog);
        serializedCatalog = new SerializedObject(catalog);
    }

    public int AddMissingElements()
    {
        if (catalog == null)
            return 0;

        List<MeleeElementSfxEntry> entries = catalog.entries != null
            ? new List<MeleeElementSfxEntry>(catalog.entries)
            : new List<MeleeElementSfxEntry>();
        HashSet<WeaponElement> existing = new HashSet<WeaponElement>();
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i] != null)
                existing.Add(entries[i].element);
        }

        List<MeleeElementSfxDefaultSpec> missing = new List<MeleeElementSfxDefaultSpec>();
        MeleeElementSfxDefaultSpec[] specs = MeleeElementSfxEditorDefaults.Specs;
        for (int i = 0; i < specs.Length; i++)
        {
            if (!existing.Contains(specs[i].Element))
                missing.Add(specs[i]);
        }
        if (missing.Count == 0)
            return 0;

        Undo.RecordObject(catalog, "누락 원소 효과음 행 보충");
        for (int i = 0; i < missing.Count; i++)
            entries.Add(MeleeElementSfxSetupUtility.CreateDefaultEntry(missing[i]));
        catalog.entries = entries.ToArray();
        EditorUtility.SetDirty(catalog);
        serializedCatalog = new SerializedObject(catalog);
        return missing.Count;
    }

    public int ReadElementCount()
    {
        return catalog?.entries != null ? catalog.entries.Length : 0;
    }

    public int ReadClipCount()
    {
        int count = 0;
        if (catalog?.entries == null)
            return count;
        for (int i = 0; i < catalog.entries.Length; i++)
        {
            count += CountNonNull(catalog.entries[i]?.slash?.clips);
            count += CountNonNull(catalog.entries[i]?.hit?.clips);
        }
        return count;
    }

    private static void DrawTableHeader()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.MinWidth(TableWidth));
        DrawHeader("원소", ElementWidth);
        DrawHeader("구분", CueWidth);
        DrawHeader("대표 클립", ClipWidth);
        DrawHeader("클립", ManageWidth);
        DrawHeader("볼륨", NumberWidth);
        DrawHeader("피치 범위", RangeWidth);
        DrawHeader("간격", NumberWidth);
        DrawHeader("3D", ToggleWidth);
        DrawHeader("거리 범위", RangeWidth);
        DrawHeader("▶", IconWidth);
        DrawHeader("■", IconWidth);
        DrawHeader("삭제", IconWidth);
        DrawHeader("행", ElementActionWidth);
        EditorGUILayout.EndHorizontal();
    }

    private static void DrawHeader(string label, float width)
    {
        EditorGUILayout.LabelField(label, EditorStyles.miniLabel, GUILayout.Width(width));
    }

    private void DrawElementRow(
        SerializedProperty entry,
        int index,
        IReadOnlyList<AudioCatalogValidationIssue> issues)
    {
        SerializedProperty element = entry.FindPropertyRelative("element");
        WeaponElement currentElement = element != null
            ? (WeaponElement)element.intValue
            : WeaponElement.None;
        if (DrawCueRow(
                entry.FindPropertyRelative("slash"),
                element,
                currentElement,
                index,
                0,
                "베기",
                true,
                issues))
        {
            entry.serializedObject.FindProperty("entries").DeleteArrayElementAtIndex(index);
            return;
        }

        DrawCueRow(
            entry.FindPropertyRelative("hit"),
            element,
            currentElement,
            index,
            1,
            "적중",
            false,
            issues);
        GUILayout.Space(1f);
    }

    private bool DrawCueRow(
        SerializedProperty settings,
        SerializedProperty element,
        WeaponElement currentElement,
        int elementIndex,
        int cueIndex,
        string cueLabel,
        bool drawElement,
        IReadOnlyList<AudioCatalogValidationIssue> issues)
    {
        if (settings == null)
            return false;

        SerializedProperty clips = settings.FindPropertyRelative("clips");
        int foldoutKey = elementIndex * 2 + cueIndex;
        bool expanded = clipFoldouts.TryGetValue(foldoutKey, out bool value) && value;
        if (clips.arraySize > 1 && !clipFoldouts.ContainsKey(foldoutKey))
            expanded = true;

        EditorGUILayout.BeginHorizontal(GUILayout.MinWidth(TableWidth));
        if (drawElement)
            DrawElementPopup(element);
        else
            GUILayout.Space(ElementWidth);

        EditorGUILayout.LabelField(cueLabel, EditorStyles.miniLabel, GUILayout.Width(CueWidth));
        DrawRepresentativeClip(clips);

        EditorGUILayout.BeginHorizontal(GUILayout.Width(ManageWidth));
        EditorGUI.BeginDisabledGroup(clips.arraySize <= 1);
        if (GUILayout.Button(
                new GUIContent(clips.arraySize + (expanded ? "▲" : "▼"), "클립 수 / 추가 클립 펼치기"),
                GUILayout.Width(34f)))
            expanded = !expanded;
        EditorGUI.EndDisabledGroup();
        if (GUILayout.Button(new GUIContent("+", "클립 추가"), GUILayout.Width(20f)))
        {
            clips.arraySize++;
            expanded = true;
        }
        EditorGUI.BeginDisabledGroup(clips.arraySize == 0);
        if (GUILayout.Button(new GUIContent("Ø", "전체 비우기"), GUILayout.Width(20f)))
            clips.ClearArray();
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
        clipFoldouts[foldoutKey] = expanded;

        DrawCompactFloat(settings.FindPropertyRelative("volume"), NumberWidth, 0f, 2f);
        DrawCompactRange(
            settings.FindPropertyRelative("minPitch"),
            settings.FindPropertyRelative("maxPitch"),
            RangeWidth,
            0.1f,
            3f,
            false);
        DrawCompactFloat(settings.FindPropertyRelative("cooldown"), NumberWidth, 0f, 60f);
        DrawCompactToggle(settings.FindPropertyRelative("spatial"));
        DrawCompactRange(
            settings.FindPropertyRelative("minDistance"),
            settings.FindPropertyRelative("maxDistance"),
            RangeWidth,
            0.1f,
            5000f,
            true);

        AudioClip previewClip = FindPreviewClip(clips);
        EditorGUI.BeginDisabledGroup(previewClip == null);
        if (GUILayout.Button(new GUIContent("▶", "미리듣기"), GUILayout.Width(IconWidth)))
            AudioCatalogEditorPreview.Play(previewClip);
        EditorGUI.EndDisabledGroup();
        if (GUILayout.Button(new GUIContent("■", "미리듣기 정지"), GUILayout.Width(IconWidth)))
            AudioCatalogEditorPreview.StopAll();
        EditorGUI.BeginDisabledGroup(clips.arraySize == 0);
        if (GUILayout.Button(new GUIContent("×", "대표 클립 제거"), GUILayout.Width(IconWidth)))
            DeleteArrayElement(clips, 0);
        EditorGUI.EndDisabledGroup();

        bool removeElement = false;
        if (drawElement)
            removeElement = GUILayout.Button(
                new GUIContent("행×", "원소 행 제거"),
                GUILayout.Width(ElementActionWidth));
        else
            GUILayout.Space(ElementActionWidth);
        EditorGUILayout.EndHorizontal();

        AudioCatalogValidationIssue? rowIssue = FindRowIssue(
            issues,
            currentElement,
            cueIndex == 0 ? "베기음" : "적중음",
            drawElement);
        if (rowIssue.HasValue)
            DrawCompactIssue(rowIssue.Value);
        if (expanded && clips.arraySize > 1)
            DrawAdditionalClips(clips);
        return removeElement;
    }

    private static void DrawElementPopup(SerializedProperty element)
    {
        WeaponElement[] values = (WeaponElement[])Enum.GetValues(typeof(WeaponElement));
        string[] labels = new string[values.Length];
        int currentIndex = 0;
        for (int i = 0; i < values.Length; i++)
        {
            labels[i] = GetElementName(values[i]);
            if ((int)values[i] == element.intValue)
                currentIndex = i;
        }
        int selected = EditorGUILayout.Popup(currentIndex, labels, GUILayout.Width(ElementWidth));
        if (selected != currentIndex)
            element.intValue = (int)values[selected];
    }

    private static void DrawRepresentativeClip(SerializedProperty clips)
    {
        if (clips.arraySize > 0)
        {
            EditorGUILayout.PropertyField(
                clips.GetArrayElementAtIndex(0),
                GUIContent.none,
                GUILayout.Width(ClipWidth));
            return;
        }

        EditorGUI.BeginChangeCheck();
        AudioClip selected = (AudioClip)EditorGUILayout.ObjectField(
            null,
            typeof(AudioClip),
            false,
            GUILayout.Width(ClipWidth));
        if (!EditorGUI.EndChangeCheck())
            return;
        clips.arraySize = 1;
        clips.GetArrayElementAtIndex(0).objectReferenceValue = selected;
    }

    private static void DrawCompactFloat(
        SerializedProperty property,
        float width,
        float minimum,
        float maximum)
    {
        float current = property.floatValue;
        EditorGUI.BeginChangeCheck();
        float edited = EditorGUILayout.FloatField(current, GUILayout.Width(width));
        if (EditorGUI.EndChangeCheck())
            property.floatValue = Mathf.Clamp(edited, minimum, maximum);
    }

    private static void DrawCompactToggle(SerializedProperty property)
    {
        EditorGUI.BeginChangeCheck();
        bool edited = GUILayout.Toggle(property.boolValue, GUIContent.none, GUILayout.Width(ToggleWidth));
        if (EditorGUI.EndChangeCheck())
            property.boolValue = edited;
    }

    private static void DrawCompactRange(
        SerializedProperty minimumProperty,
        SerializedProperty maximumProperty,
        float width,
        float clampMinimum,
        float clampMaximum,
        bool requireGap)
    {
        float fieldWidth = (width - 10f) * 0.5f;
        bool minimumChanged = DrawRangeValue(
            minimumProperty,
            fieldWidth,
            clampMinimum,
            clampMaximum);
        EditorGUILayout.LabelField("~", GUILayout.Width(10f));
        bool maximumChanged = DrawRangeValue(
            maximumProperty,
            fieldWidth,
            clampMinimum,
            clampMaximum);

        float gap = requireGap ? 0.1f : 0f;
        if (minimumChanged && minimumProperty.floatValue > maximumProperty.floatValue - gap)
        {
            maximumProperty.floatValue = Mathf.Min(
                clampMaximum,
                minimumProperty.floatValue + gap);
        }
        else if (maximumChanged && maximumProperty.floatValue < minimumProperty.floatValue + gap)
        {
            minimumProperty.floatValue = Mathf.Max(
                clampMinimum,
                maximumProperty.floatValue - gap);
        }
    }

    private static bool DrawRangeValue(
        SerializedProperty property,
        float width,
        float minimum,
        float maximum)
    {
        EditorGUI.BeginChangeCheck();
        float edited = EditorGUILayout.FloatField(property.floatValue, GUILayout.Width(width));
        if (!EditorGUI.EndChangeCheck())
            return false;
        property.floatValue = Mathf.Clamp(edited, minimum, maximum);
        return true;
    }

    private static void DrawAdditionalClips(SerializedProperty clips)
    {
        for (int i = 1; i < clips.arraySize; i++)
        {
            EditorGUILayout.BeginHorizontal(GUILayout.MinWidth(TableWidth));
            GUILayout.Space(ElementWidth + CueWidth);
            EditorGUILayout.LabelField("추가 " + (i + 1), EditorStyles.miniLabel, GUILayout.Width(44f));
            SerializedProperty clipProperty = clips.GetArrayElementAtIndex(i);
            EditorGUILayout.PropertyField(
                clipProperty,
                GUIContent.none,
                GUILayout.Width(ClipWidth + ManageWidth - 44f));
            AudioClip clip = clipProperty.objectReferenceValue as AudioClip;
            EditorGUI.BeginDisabledGroup(clip == null);
            if (GUILayout.Button(new GUIContent("▶", "미리듣기"), GUILayout.Width(IconWidth)))
                AudioCatalogEditorPreview.Play(clip);
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button(new GUIContent("×", "추가 클립 제거"), GUILayout.Width(IconWidth)))
            {
                DeleteArrayElement(clips, i);
                EditorGUILayout.EndHorizontal();
                break;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }
    }

    private static AudioCatalogValidationIssue? FindRowIssue(
        IReadOnlyList<AudioCatalogValidationIssue> issues,
        WeaponElement element,
        string cueName,
        bool includeElementIssue)
    {
        string cuePrefix = GetElementName(element) + " / " + cueName + ":";
        string elementSuffix = GetElementName(element);
        for (int i = 0; i < issues.Count; i++)
        {
            if (issues[i].Message.StartsWith(cuePrefix, StringComparison.Ordinal)
                || includeElementIssue
                && issues[i].Message.StartsWith("원소 행", StringComparison.Ordinal)
                && issues[i].Message.EndsWith(elementSuffix, StringComparison.Ordinal))
            {
                return issues[i];
            }
        }
        return null;
    }

    private static void DrawCompactIssue(AudioCatalogValidationIssue issue)
    {
        EditorGUILayout.BeginHorizontal(GUILayout.MinWidth(TableWidth));
        GUILayout.Space(ElementWidth + CueWidth);
        Color previous = GUI.contentColor;
        GUI.contentColor = issue.Type == MessageType.Error
            ? new Color(1f, 0.35f, 0.35f)
            : new Color(1f, 0.65f, 0.15f);
        EditorGUILayout.LabelField("⚠ " + issue.Message, EditorStyles.miniLabel);
        GUI.contentColor = previous;
        EditorGUILayout.EndHorizontal();
    }

    private static AudioClip FindPreviewClip(SerializedProperty clips)
    {
        for (int i = 0; i < clips.arraySize; i++)
        {
            AudioClip clip = clips.GetArrayElementAtIndex(i).objectReferenceValue as AudioClip;
            if (clip != null)
                return clip;
        }
        return null;
    }

    private static void DeleteArrayElement(SerializedProperty array, int index)
    {
        int before = array.arraySize;
        array.DeleteArrayElementAtIndex(index);
        if (array.arraySize == before)
            array.DeleteArrayElementAtIndex(index);
    }

    private bool ContainsMissingReference()
    {
        if (CountMissingElements() > 0 || catalog?.entries == null)
            return true;
        for (int i = 0; i < catalog.entries.Length; i++)
        {
            MeleeElementSfxEntry entry = catalog.entries[i];
            if (entry == null || HasMissingClip(entry.slash) || HasMissingClip(entry.hit))
                return true;
        }
        return false;
    }

    private int CountMissingElements()
    {
        if (catalog?.entries == null)
            return MeleeElementSfxEditorDefaults.Specs.Length;
        HashSet<WeaponElement> existing = new HashSet<WeaponElement>();
        for (int i = 0; i < catalog.entries.Length; i++)
        {
            if (catalog.entries[i] != null)
                existing.Add(catalog.entries[i].element);
        }
        int count = 0;
        MeleeElementSfxDefaultSpec[] specs = MeleeElementSfxEditorDefaults.Specs;
        for (int i = 0; i < specs.Length; i++)
        {
            if (!existing.Contains(specs[i].Element))
                count++;
        }
        return count;
    }

    private static bool HasMissingClip(MeleeElementSfxCueSettings settings)
    {
        if (settings?.clips == null || settings.clips.Length == 0)
            return true;
        for (int i = 0; i < settings.clips.Length; i++)
        {
            if (settings.clips[i] == null)
                return true;
        }
        return false;
    }

    private static void CollectCueIssues(
        WeaponElement element,
        string cueName,
        MeleeElementSfxCueSettings settings,
        HashSet<AudioClip> clips,
        List<AudioCatalogValidationIssue> issues)
    {
        string prefix = GetElementName(element) + " / " + cueName + ": ";
        if (settings == null || settings.clips == null || settings.clips.Length == 0)
        {
            issues.Add(new AudioCatalogValidationIssue(MessageType.Error, prefix + "오디오 클립이 없습니다."));
            return;
        }

        for (int i = 0; i < settings.clips.Length; i++)
        {
            AudioClip clip = settings.clips[i];
            if (clip == null)
            {
                issues.Add(new AudioCatalogValidationIssue(
                    MessageType.Error,
                    prefix + (i + 1) + "번 클립 참조가 비어 있습니다."));
                continue;
            }
            if (!clips.Add(clip))
            {
                issues.Add(new AudioCatalogValidationIssue(
                    MessageType.Warning,
                    prefix + "다른 행에서도 같은 클립을 사용합니다: " + clip.name));
            }
            if (clip.name == MeleeElementSfxEditorDefaults.ForbiddenFireCircle
                || clip.name == MeleeElementSfxEditorDefaults.ForbiddenElectricHit)
            {
                issues.Add(new AudioCatalogValidationIssue(
                    MessageType.Error,
                    prefix + "사용 금지 클립을 참조합니다: " + clip.name));
            }
        }

        if (settings.minPitch > settings.maxPitch)
            issues.Add(new AudioCatalogValidationIssue(MessageType.Error, prefix + "최소 피치가 최대 피치보다 큽니다."));
        if (settings.minDistance >= settings.maxDistance)
            issues.Add(new AudioCatalogValidationIssue(MessageType.Error, prefix + "최소 거리는 최대 거리보다 작아야 합니다."));
        if (settings.volume < 0f)
            issues.Add(new AudioCatalogValidationIssue(MessageType.Error, prefix + "볼륨은 음수가 될 수 없습니다."));
        if (settings.cooldown < 0f)
            issues.Add(new AudioCatalogValidationIssue(MessageType.Error, prefix + "재생 간격은 음수가 될 수 없습니다."));
    }

    private static string GetElementName(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.None:
                return "무속성";
            case WeaponElement.Fire:
                return "불";
            case WeaponElement.Electric:
                return "전기";
            case WeaponElement.Water:
                return "물";
            case WeaponElement.Wind:
                return "바람";
            case WeaponElement.Ice:
                return "냉기";
            default:
                return element.ToString();
        }
    }

    private static int CountNonNull(AudioClip[] clips)
    {
        int count = 0;
        if (clips == null)
            return count;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null)
                count++;
        }
        return count;
    }
}

public static class AudioCatalogManagerCommandLineValidation
{
    public static void ValidateFromCommandLine()
    {
        List<IAudioCatalogEditorProvider> providers =
            AudioCatalogEditorProviderRegistry.CreateProviders();
        if (providers.Count == 0)
            throw new InvalidOperationException("오디오 카탈로그 제공자를 찾지 못했습니다.");

        IAudioCatalogEditorProvider meleeProvider = null;
        for (int i = 0; i < providers.Count; i++)
        {
            providers[i].Refresh();
            if (providers[i] is MeleeElementSfxCatalogEditorProvider)
                meleeProvider = providers[i];
        }

        if (meleeProvider == null || meleeProvider.CatalogAsset == null)
            throw new MissingReferenceException("근접 원소 효과음 제공자 또는 카탈로그가 누락되었습니다.");
        string before = EditorJsonUtility.ToJson(meleeProvider.CatalogAsset);
        MeleeElementSfxSetupUtility.ValidateCatalog(
            (MeleeElementSfxCatalog)meleeProvider.CatalogAsset);
        IReadOnlyList<AudioCatalogValidationIssue> issues = meleeProvider.CollectIssues();
        string after = EditorJsonUtility.ToJson(meleeProvider.CatalogAsset);
        if (issues.Count != 0
            || meleeProvider.ReadElementCount() != 6
            || meleeProvider.ReadClipCount() != 12
            || !string.Equals(before, after, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("오디오 카탈로그 제공자의 읽기 전용 검증에 실패했습니다.");
        }

        Debug.Log("[ProjectVTP] 오디오 카탈로그 제공자 검증 통과. 원소=6, 클립=12, 데이터 변경=0.");
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
