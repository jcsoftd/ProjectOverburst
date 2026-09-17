using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public sealed class SfxPreviewWindow : EditorWindow
{
    private const string WindowTitle = "SFX 미리보기";
    private const string MenuPath = "JC Tool/오디오/SFX 미리보기";
    private const string BasketRoot = "Assets/SFX장바구니"; // 원본과 분리되는 프로젝트 내부 후보 보관소
    private const string MemoKeyPrefix = "OVERBURST.SfxPreview.Memo.";
    private const string FolderKey = "OVERBURST.SfxPreview.BasketFolder";
    private const string FooterBrandText = "JC Soft";
    private const float HeaderHeight = 62f;
    private const float ControlHeight = 24f;
    private const float SectionGap = 8f;
    private const float OuterMargin = 8f;
    private const float TopSectionHeight = 286f;
    private const float BottomSectionHeight = 170f;
    private const float FooterHeight = 30f;
    private const float ButtonWidth = 82f;
    private const float LabelWidth = 66f;
    private const float ContentRowGap = 6f;
    private const float MetadataCardHeight = 50f;
    private const float MetadataCardGap = 4f;
    private const float PlaybackGaugeMaxWidth = 360f;
    private const float PlaybackValueWidth = 104f;
    private const float WaveformMinHeight = 190f;
    private static readonly Vector2 MinimumWindowSize = new Vector2(900f, 780f);

    [SerializeField] private AudioClip selectedClip; // 도메인 리로드 뒤에도 현재 검토 대상을 유지
    [SerializeField] private float previewVolume = 1f; // 미리듣기에만 적용하는 임시 볼륨
    [SerializeField] private bool loopPlayback; // 짧은 효과음 반복 검토 상태
    [SerializeField] private string memo = string.Empty; // 실제 저장은 클립 GUID별 EditorPrefs가 소유
    [SerializeField] private string newFolderName = string.Empty;
    [SerializeField] private int selectedFolderIndex;

    private readonly List<string> basketFolderPaths = new List<string>();
    private readonly List<string> basketFolderLabels = new List<string>();
    private float[] waveformMinMaxData = Array.Empty<float>(); // Unity 원본 PCM 개요를 클립 전환 때만 갱신
    private SfxClipTechnicalInfo clipTechnicalInfo;
    private double playbackStartTime; // Unity 내부 샘플 위치 조회 실패 시 사용하는 보조 시계
    private double lastPlaybackRepaintTime; // 고해상도 파형 재생 중 불필요한 Editor Repaint를 제한
    private float playbackStartSeconds;
    private bool isPlaying; // 에디터 미리듣기 논리 상태
    private bool isPaused;
    private string statusMessage = "AudioClip을 선택하면 즉시 재생됩니다.";
    private MessageType statusType = MessageType.Info;
    private GUIStyle headerTitleStyle;
    private GUIStyle headerMetaStyle;
    private GUIStyle sectionStyle;
    private GUIStyle sectionTitleStyle;
    private GUIStyle fieldLabelStyle;
    private GUIStyle compactButtonStyle;
    private GUIStyle primaryButtonStyle;
    private GUIStyle metadataCardStyle;
    private GUIStyle metadataLabelStyle;
    private GUIStyle metadataValueStyle;
    private GUIStyle playbackValueStyle;
    private GUIStyle waveformOverlayStyle;
    private GUIStyle footerStyle;
    private GUIStyle footerBrandStyle;

    [MenuItem(MenuPath)]
    private static void Open()
    {
        SfxPreviewWindow window = GetWindow<SfxPreviewWindow>(WindowTitle);
        window.minSize = MinimumWindowSize;
        window.TryUseProjectSelection();
    }

    private void OnEnable()
    {
        minSize = MinimumWindowSize;
        EditorApplication.update += TickPlayback;
        RefreshBasketFolders();
        RestoreSelectedBasketFolder();

        if (selectedClip != null)
        {
            LoadMemo();
            RefreshClipTechnicalInfo();
            StartPlayback(0f);
        }
        else
        {
            TryUseProjectSelection();
        }
    }

    private void OnDisable()
    {
        EditorApplication.update -= TickPlayback;
        StopPlayback();
    }

    private void OnSelectionChange()
    {
        TryUseProjectSelection();
    }

    private void OnGUI()
    {
        EnsureStyles();
        DrawHeader();

        DrawTwoColumnRow("대상", DrawTargetSection, "재생", DrawPlaybackSection, TopSectionHeight);

        GUILayout.Space(SectionGap);
        DrawWaveformSection();
        GUILayout.Space(SectionGap);

        DrawTwoColumnRow("메모", DrawMemoSection, "SFX 장바구니", DrawBasketSection, BottomSectionHeight);

        DrawFooter();
    }

    private void DrawHeader()
    {
        Rect rect = GUILayoutUtility.GetRect(10f, HeaderHeight, GUILayout.ExpandWidth(true));
        Rect backgroundRect = new Rect(rect.x + 2f, rect.y + 4f, rect.width - 4f, rect.height - 8f);
        EditorGUI.DrawRect(backgroundRect, new Color(0.105f, 0.12f, 0.145f, 1f));

        GUI.Label(
            new Rect(backgroundRect.x + 14f, backgroundRect.y + 7f, backgroundRect.width - 28f, 24f),
            WindowTitle,
            headerTitleStyle);

        string state = selectedClip == null
            ? "AudioClip을 선택하면 자동으로 재생합니다."
            : selectedClip.name + "  ·  " + BuildPlaybackStateText();
        GUI.Label(
            new Rect(backgroundRect.x + 14f, backgroundRect.y + 35f, backgroundRect.width - 28f, 18f),
            state,
            headerMetaStyle);
    }

    private void DrawTargetSection()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawFieldLabel("오디오");
            EditorGUI.BeginChangeCheck();
            AudioClip nextClip = (AudioClip)EditorGUILayout.ObjectField(
                selectedClip,
                typeof(AudioClip),
                false,
                GUILayout.Height(ControlHeight));
            if (EditorGUI.EndChangeCheck())
                SelectClip(nextClip, true);
        }

        GUILayout.Space(ContentRowGap);
        using (new EditorGUILayout.HorizontalScope(GUILayout.Height(ControlHeight)))
        {
            GUILayout.Space(LabelWidth);
            if (GUILayout.Button("선택 사용", compactButtonStyle, GUILayout.Width(ButtonWidth)))
                UseActiveSelectionOrNotify();
            if (GUILayout.Button("위치 표시", compactButtonStyle, GUILayout.Width(ButtonWidth)))
                PingSelectedClip();
            if (GUILayout.Button("비우기", compactButtonStyle, GUILayout.Width(ButtonWidth)))
                SelectClip(null, false);
        }

        GUILayout.Space(ContentRowGap);
        DrawClipPathRow();
        GUILayout.Space(ContentRowGap);
        DrawClipMetadata();
        GUILayout.Space(ContentRowGap);
        DrawClipFileRow();
    }

    private void DrawClipMetadata()
    {
        string lengthText = selectedClip != null ? FormatTime(selectedClip.length) : "—";
        string channelText = selectedClip != null ? BuildChannelText(selectedClip.channels) : "—";
        string sampleRateText = selectedClip != null ? selectedClip.frequency.ToString("N0") + " Hz" : "—";
        string sampleCountText = selectedClip != null ? selectedClip.samples.ToString("N0") : "—";

        DrawMetadataPairRow("길이", lengthText, "채널", channelText);
        GUILayout.Space(MetadataCardGap);
        DrawMetadataPairRow("샘플레이트", sampleRateText, "총 샘플", sampleCountText);
    }

    private void DrawClipPathRow()
    {
        string assetPath = AssetDatabase.GetAssetPath(selectedClip);
        using (new EditorGUILayout.HorizontalScope(GUILayout.Height(ControlHeight)))
        {
            DrawFieldLabel("경로");
            EditorGUILayout.SelectableLabel(
                selectedClip == null
                    ? "AudioClip을 선택하세요."
                    : string.IsNullOrEmpty(assetPath) ? "프로젝트 에셋이 아닙니다." : assetPath,
                EditorStyles.textField,
                GUILayout.Height(ControlHeight));
        }
    }

    private void DrawClipFileRow()
    {
        using (new EditorGUILayout.HorizontalScope(GUILayout.Height(ControlHeight)))
        {
            DrawFieldLabel("파일");
            EditorGUILayout.LabelField(
                selectedClip == null
                    ? "—"
                    : clipTechnicalInfo.FileExtension + "  ·  " + clipTechnicalInfo.FileSizeText
                      + "  ·  " + clipTechnicalInfo.EncodingText,
                EditorStyles.miniLabel,
                GUILayout.Height(ControlHeight));
        }
    }

    private void DrawPlaybackSection()
    {
        using (new EditorGUI.DisabledScope(selectedClip == null))
        {
            using (new EditorGUILayout.HorizontalScope(GUILayout.Height(ControlHeight)))
            {
                DrawFieldLabel("제어");
                string playLabel = isPlaying && !isPaused ? "일시 정지" : "재생";
                if (GUILayout.Button(playLabel, compactButtonStyle, GUILayout.Width(ButtonWidth)))
                    TogglePlayPause();
                if (GUILayout.Button("다시 재생", compactButtonStyle, GUILayout.Width(ButtonWidth)))
                    StartPlayback(0f);
                if (GUILayout.Button("정지", compactButtonStyle, GUILayout.Width(ButtonWidth)))
                    StopPlayback();

                Color previousColor = GUI.backgroundColor;
                GUI.backgroundColor = loopPlayback ? new Color(0.48f, 0.7f, 1f, 1f) : Color.white;
                bool nextLoop = GUILayout.Toggle(
                    loopPlayback,
                    "반복",
                    compactButtonStyle,
                    GUILayout.Width(ButtonWidth));
                GUI.backgroundColor = previousColor;
                if (nextLoop != loopPlayback)
                {
                    loopPlayback = nextLoop;
                    if (isPlaying || isPaused)
                        StartPlayback(GetCurrentPlaybackSeconds());
                }
            }

            GUILayout.Space(ContentRowGap);
            using (new EditorGUILayout.HorizontalScope(GUILayout.Height(ControlHeight)))
            {
                DrawFieldLabel("볼륨");
                EditorGUI.BeginChangeCheck();
                float nextVolume = GUILayout.HorizontalSlider(
                    previewVolume,
                    0f,
                    1f,
                    GUILayout.Height(ControlHeight),
                    GUILayout.MinWidth(160f),
                    GUILayout.MaxWidth(PlaybackGaugeMaxWidth));
                if (EditorGUI.EndChangeCheck())
                {
                    previewVolume = nextVolume;
                    if (isPlaying || isPaused)
                        SfxEditorAudioPreview.SetVolume(previewVolume);
                }
                GUILayout.Label(
                    Mathf.RoundToInt(previewVolume * 100f) + "%",
                    playbackValueStyle,
                    GUILayout.Width(PlaybackValueWidth));
                GUILayout.FlexibleSpace();
            }

            GUILayout.Space(ContentRowGap);
            DrawTimeline();
            GUILayout.Space(ContentRowGap);
            DrawPlaybackTechnicalInfo();
        }
    }

    private void DrawPlaybackTechnicalInfo()
    {
        DrawMetadataPairRow(
            "비트레이트",
            selectedClip != null ? clipTechnicalInfo.BitRateText : "—",
            "비트 심도",
            selectedClip != null ? clipTechnicalInfo.BitDepthText : "—");
        GUILayout.Space(MetadataCardGap);
        DrawMetadataPairRow(
            "로드 방식",
            selectedClip != null ? clipTechnicalInfo.LoadTypeText : "—",
            "품질",
            selectedClip != null ? clipTechnicalInfo.QualityText : "—");

        GUILayout.Space(ContentRowGap);
        using (new EditorGUILayout.HorizontalScope(GUILayout.Height(ControlHeight)))
        {
            DrawFieldLabel("Import");
            EditorGUILayout.LabelField(
                selectedClip == null
                    ? "—"
                    : "Mono " + FormatEnabled(clipTechnicalInfo.ForceToMono)
                      + "  ·  Preload " + FormatEnabled(clipTechnicalInfo.PreloadAudioData)
                      + "  ·  Background " + FormatEnabled(clipTechnicalInfo.LoadInBackground)
                      + "  ·  Ambisonic " + FormatEnabled(clipTechnicalInfo.Ambisonic),
                EditorStyles.miniLabel,
                GUILayout.Height(ControlHeight));
        }
    }

    private void DrawTimeline()
    {
        using (new EditorGUILayout.HorizontalScope(GUILayout.Height(ControlHeight)))
        {
            DrawFieldLabel("시간");
            float duration = selectedClip != null ? Mathf.Max(0f, selectedClip.length) : 0f;
            float current = selectedClip != null ? GetCurrentPlaybackSeconds() : 0f;

            EditorGUI.BeginChangeCheck();
            float next = GUILayout.HorizontalSlider(
                current,
                0f,
                Mathf.Max(0.01f, duration),
                GUILayout.Height(ControlHeight),
                GUILayout.MinWidth(160f),
                GUILayout.MaxWidth(PlaybackGaugeMaxWidth));
            if (EditorGUI.EndChangeCheck())
                SeekPlayback(next);

            GUILayout.Label(
                FormatTime(current) + " / " + FormatTime(duration),
                playbackValueStyle,
                GUILayout.Width(PlaybackValueWidth));
            GUILayout.FlexibleSpace();
        }
    }

    private void DrawWaveformSection()
    {
        float usedHeight = HeaderHeight + TopSectionHeight + BottomSectionHeight + FooterHeight + SectionGap * 3f + 12f;
        float sectionHeight = Mathf.Max(WaveformMinHeight, position.height - usedHeight);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(OuterMargin);
            using (new EditorGUILayout.VerticalScope(sectionStyle, GUILayout.Height(sectionHeight)))
            {
                DrawSectionHeader("파형 미리보기");
                GUILayout.Space(3f);
                DrawWaveformInfoBar();

                Rect waveformRect = GUILayoutUtility.GetRect(
                    10f,
                    Mathf.Max(1f, sectionHeight - 62f),
                    GUILayout.ExpandWidth(true));
                DrawWaveform(waveformRect);
                HandleWaveformInput(waveformRect);
            }
            GUILayout.Space(OuterMargin);
        }
    }

    private void DrawWaveformInfoBar()
    {
        int channels = Mathf.Max(1, selectedClip != null ? selectedClip.channels : 1);
        int overviewPoints = waveformMinMaxData.Length / (2 * channels);
        string leftText = selectedClip == null
            ? "원본 PCM 개요 대기"
            : "고해상도 PCM  ·  " + overviewPoints.ToString("N0") + " 포인트"
              + "  ·  " + BuildChannelText(channels)
              + "  ·  " + selectedClip.samples.ToString("N0") + " samples";

        using (new EditorGUILayout.HorizontalScope(GUILayout.Height(20f)))
        {
            GUILayout.Label(leftText, headerMetaStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label("클릭·드래그 탐색", headerMetaStyle, GUILayout.Width(98f));
        }
    }

    private void DrawWaveform(Rect rect)
    {
        if (Event.current.type != EventType.Repaint)
            return;

        EditorGUI.DrawRect(rect, new Color(0.055f, 0.065f, 0.082f, 1f));

        if (selectedClip == null)
        {
            DrawCenteredLabel(rect, "AudioClip을 선택하면 파형과 재생 위치가 표시됩니다.");
            return;
        }

        DrawWaveformTimeGrid(rect);
        DrawHighResolutionWaveform(rect);

        float progress = selectedClip.length > 0f
            ? Mathf.Clamp01(GetCurrentPlaybackSeconds() / selectedClip.length)
            : 0f;
        float markerX = Mathf.Lerp(rect.x, rect.xMax, progress);
        EditorGUI.DrawRect(
            new Rect(rect.x, rect.y, Mathf.Max(0f, markerX - rect.x), rect.height),
            new Color(0.18f, 0.48f, 0.78f, 0.08f));
        EditorGUI.DrawRect(
            new Rect(markerX - 1f, rect.y, 2f, rect.height),
            new Color(0.38f, 0.78f, 1f, 1f));

        Rect timeRect = new Rect(rect.x + 10f, rect.y + 8f, 228f, 24f);
        EditorGUI.DrawRect(timeRect, new Color(0.025f, 0.03f, 0.04f, 0.9f));
        GUI.Label(
            timeRect,
            "  " + BuildPlaybackStateText() + "   "
            + FormatTime(GetCurrentPlaybackSeconds()) + " / " + FormatTime(selectedClip.length),
            waveformOverlayStyle);
    }

    private void DrawHighResolutionWaveform(Rect rect)
    {
        int channelCount = Mathf.Max(1, selectedClip != null ? selectedClip.channels : 1);
        int overviewSampleCount = waveformMinMaxData.Length / (2 * channelCount);
        if (overviewSampleCount < 2)
        {
            DrawCenteredLabel(rect, "고해상도 PCM 파형을 만들 수 없는 오디오 형식입니다.");
            return;
        }

        float pixelsPerPoint = Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
        float step = 1f / pixelsPerPoint; // 모니터 실제 픽셀 단위로 파형을 샘플링
        float channelHeight = rect.height / channelCount;
        Color waveformColor = new Color(0.3f, 0.72f, 1f, 0.92f);
        Color waveformHighlight = new Color(0.63f, 0.9f, 1f, 0.34f);

        for (int channel = 0; channel < channelCount; channel++)
        {
            Rect channelRect = new Rect(
                rect.x,
                rect.y + channelHeight * channel,
                rect.width,
                channelHeight);
            float centerY = channelRect.center.y;
            float amplitude = channelRect.height * 0.43f;

            EditorGUI.DrawRect(
                new Rect(channelRect.x, centerY, channelRect.width, 1f / pixelsPerPoint),
                new Color(0.34f, 0.4f, 0.49f, 0.58f));

            if (channel > 0)
            {
                EditorGUI.DrawRect(
                    new Rect(channelRect.x, channelRect.y, channelRect.width, 1f),
                    new Color(0.17f, 0.21f, 0.27f, 1f));
            }

            for (float x = 0f; x < channelRect.width; x += step)
            {
                float normalized = channelRect.width > 0f ? x / channelRect.width : 0f;
                float position = Mathf.Clamp(
                    normalized * (overviewSampleCount - 2),
                    0f,
                    overviewSampleCount - 2);
                int index = Mathf.FloorToInt(position);
                int offset1 = (index * channelCount + channel) * 2;
                int offset2 = offset1 + channelCount * 2;

                float minValue = Mathf.Min(
                    waveformMinMaxData[offset1 + 1],
                    waveformMinMaxData[offset2 + 1]) * 0.95f;
                float maxValue = Mathf.Max(
                    waveformMinMaxData[offset1],
                    waveformMinMaxData[offset2]) * 0.95f;
                if (minValue > maxValue)
                    (minValue, maxValue) = (maxValue, minValue);

                float top = centerY - Mathf.Clamp(maxValue, -1f, 1f) * amplitude;
                float bottom = centerY - Mathf.Clamp(minValue, -1f, 1f) * amplitude;
                float height = Mathf.Max(step, bottom - top);
                EditorGUI.DrawRect(
                    new Rect(channelRect.x + x, top, step, height),
                    waveformColor);
            }

            EditorGUI.DrawRect(
                new Rect(channelRect.x, centerY - 1f, channelRect.width, 2f),
                waveformHighlight);

            if (channelCount > 1)
            {
                Rect labelRect = new Rect(channelRect.x + 8f, channelRect.y + 7f, 46f, 18f);
                EditorGUI.DrawRect(labelRect, new Color(0.025f, 0.03f, 0.04f, 0.82f));
                GUI.Label(labelRect, "CH " + (channel + 1), waveformOverlayStyle);
            }
        }
    }

    private void DrawWaveformTimeGrid(Rect rect)
    {
        if (selectedClip == null || selectedClip.length <= 0f)
            return;

        float interval = ResolveTimeGridInterval(selectedClip.length, rect.width);
        int markerCount = Mathf.FloorToInt(selectedClip.length / interval);
        for (int i = 0; i <= markerCount; i++)
        {
            float seconds = i * interval;
            float normalized = seconds / selectedClip.length;
            float x = Mathf.Lerp(rect.x, rect.xMax, normalized);
            EditorGUI.DrawRect(
                new Rect(x, rect.y, 1f, rect.height),
                new Color(0.25f, 0.3f, 0.38f, i % 5 == 0 ? 0.38f : 0.2f));

            if (i == 0 || i == markerCount)
                continue;
            GUI.Label(
                new Rect(x + 4f, rect.yMax - 21f, 64f, 18f),
                FormatGridTime(seconds),
                waveformOverlayStyle);
        }
    }

    private static float ResolveTimeGridInterval(float duration, float width)
    {
        float desiredMarkers = Mathf.Clamp(width / 110f, 4f, 14f);
        float rawInterval = duration / desiredMarkers;
        float[] candidates = { 0.05f, 0.1f, 0.25f, 0.5f, 1f, 2f, 5f, 10f, 15f, 30f, 60f };
        for (int i = 0; i < candidates.Length; i++)
        {
            if (candidates[i] >= rawInterval)
                return candidates[i];
        }
        return Mathf.Max(60f, Mathf.Ceil(rawInterval / 60f) * 60f);
    }

    private void HandleWaveformInput(Rect rect)
    {
        Event current = Event.current;
        if (selectedClip == null || !rect.Contains(current.mousePosition))
            return;

        EditorGUIUtility.AddCursorRect(rect, MouseCursor.SlideArrow);
        if (current.type != EventType.MouseDown && current.type != EventType.MouseDrag)
            return;
        if (current.button != 0)
            return;

        float normalized = Mathf.InverseLerp(rect.x, rect.xMax, current.mousePosition.x);
        SeekPlayback(selectedClip.length * normalized);
        if (!isPlaying && !isPaused)
            StartPlayback(selectedClip.length * normalized);
        current.Use();
    }

    private void DrawMemoSection()
    {
        EditorGUILayout.LabelField(
            selectedClip != null ? selectedClip.name : "선택된 오디오 없음",
            EditorStyles.miniBoldLabel);

        using (new EditorGUI.DisabledScope(selectedClip == null))
        {
            EditorGUI.BeginChangeCheck();
            string nextMemo = EditorGUILayout.TextArea(
                memo,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            if (EditorGUI.EndChangeCheck())
            {
                memo = nextMemo;
                SaveMemo();
                SetStatus("메모를 자동 저장했습니다.", MessageType.Info);
            }
        }

        EditorGUILayout.LabelField(
            selectedClip == null ? "클립을 선택하면 메모가 활성화됩니다." : "클립 GUID 기준으로 자동 저장",
            EditorStyles.miniLabel);
    }

    private void DrawBasketSection()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawFieldLabel("저장 위치");
            string[] labels = basketFolderLabels.Count > 0
                ? basketFolderLabels.ToArray()
                : new[] { "장바구니 루트" };
            int safeIndex = Mathf.Clamp(selectedFolderIndex, 0, labels.Length - 1);
            EditorGUI.BeginChangeCheck();
            int nextIndex = EditorGUILayout.Popup(safeIndex, labels, GUILayout.Height(ControlHeight));
            if (EditorGUI.EndChangeCheck())
                SelectBasketFolder(nextIndex);

            if (GUILayout.Button("새로고침", compactButtonStyle, GUILayout.Width(ButtonWidth)))
            {
                RefreshBasketFolders();
                RestoreSelectedBasketFolder();
                SetStatus("장바구니 폴더 목록을 갱신했습니다.", MessageType.Info);
            }

            if (GUILayout.Button("위치 표시", compactButtonStyle, GUILayout.Width(ButtonWidth)))
                PingBasketFolder();
        }

        GUILayout.Space(6f);
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawFieldLabel("새 폴더");
            newFolderName = EditorGUILayout.TextField(
                newFolderName,
                GUILayout.Height(ControlHeight));
            if (GUILayout.Button("생성·선택", compactButtonStyle, GUILayout.Width(ButtonWidth * 2f + 2f)))
                CreateAndSelectFolderFromInput();
        }

        GUILayout.Space(7f);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(LabelWidth);
            using (new EditorGUI.DisabledScope(selectedClip == null))
            {
                if (GUILayout.Button(
                    "장바구니에 담기",
                    primaryButtonStyle,
                    GUILayout.Height(36f),
                    GUILayout.ExpandWidth(true)))
                {
                    CopySelectedClipToBasket();
                }
            }
        }

        GUILayout.Space(4f);
        string destination = ResolveSelectedBasketPath();
        EditorGUILayout.LabelField(
            "대상: " + destination,
            EditorStyles.miniLabel);
    }

    private void DrawFooter()
    {
        Rect rect = GUILayoutUtility.GetRect(10f, FooterHeight, GUILayout.ExpandWidth(true));
        Rect contentRect = new Rect(rect.x + OuterMargin, rect.y, rect.width - OuterMargin * 2f, rect.height);
        float brandWidth = 74f;
        Rect brandRect = new Rect(contentRect.xMax - brandWidth, contentRect.y, brandWidth, contentRect.height);
        Rect statusRect = new Rect(contentRect.x, contentRect.y, contentRect.width - brandWidth - 8f, contentRect.height);

        Color statusColor = statusType == MessageType.Error
            ? new Color(1f, 0.55f, 0.5f, 1f)
            : statusType == MessageType.Warning
                ? new Color(1f, 0.78f, 0.36f, 1f)
                : new Color(0.68f, 0.75f, 0.84f, 1f);
        footerStyle.normal.textColor = statusColor;
        GUI.Label(statusRect, statusMessage, footerStyle);
        GUI.Label(brandRect, FooterBrandText, footerBrandStyle);
    }

    private void DrawSection(string title, Action drawBody, params GUILayoutOption[] options)
    {
        using (new EditorGUILayout.VerticalScope(sectionStyle, options))
        {
            DrawSectionHeader(title);
            GUILayout.Space(3f);
            drawBody();
        }
    }

    private void DrawTwoColumnRow(
        string leftTitle,
        Action leftBody,
        string rightTitle,
        Action rightBody,
        float height)
    {
        float availableWidth = Mathf.Max(0f, position.width - OuterMargin * 2f - SectionGap);
        float leftWidth = ResolveLeftColumnWidth(availableWidth);
        float rightWidth = Mathf.Max(1f, availableWidth - leftWidth);

        using (new EditorGUILayout.HorizontalScope(GUILayout.Height(height)))
        {
            GUILayout.Space(OuterMargin);
            DrawSection(
                leftTitle,
                leftBody,
                GUILayout.Width(leftWidth),
                GUILayout.Height(height),
                GUILayout.ExpandWidth(false));
            GUILayout.Space(SectionGap);
            DrawSection(
                rightTitle,
                rightBody,
                GUILayout.Width(rightWidth),
                GUILayout.Height(height),
                GUILayout.ExpandWidth(false));
            GUILayout.Space(OuterMargin);
        }
    }

    private void DrawSectionHeader(string title)
    {
        Rect rect = GUILayoutUtility.GetRect(10f, 18f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(new Rect(rect.x, rect.y + 2f, 3f, 14f), new Color(0.38f, 0.66f, 1f, 1f));
        GUI.Label(new Rect(rect.x + 9f, rect.y, rect.width - 9f, rect.height), title, sectionTitleStyle);
    }

    private void DrawFieldLabel(string label)
    {
        GUILayout.Label(label, fieldLabelStyle, GUILayout.Width(LabelWidth));
    }

    private void DrawMetadataPairRow(
        string leftLabel,
        string leftValue,
        string rightLabel,
        string rightValue)
    {
        Rect rowRect = GUILayoutUtility.GetRect(
            1f,
            MetadataCardHeight,
            GUILayout.ExpandWidth(true),
            GUILayout.Height(MetadataCardHeight));
        float cellWidth = Mathf.Max(1f, (rowRect.width - MetadataCardGap) * 0.5f);

        DrawMetadataCard(
            new Rect(rowRect.x, rowRect.y, cellWidth, rowRect.height),
            leftLabel,
            leftValue);
        DrawMetadataCard(
            new Rect(rowRect.x + cellWidth + MetadataCardGap, rowRect.y, cellWidth, rowRect.height),
            rightLabel,
            rightValue);
    }

    private void DrawMetadataCard(Rect rect, string label, string value)
    {
        GUI.Box(rect, GUIContent.none, metadataCardStyle);
        const float horizontalPadding = 9f;
        Rect labelRect = new Rect(
            rect.x + horizontalPadding,
            rect.y + 4f,
            Mathf.Max(1f, rect.width - horizontalPadding * 2f),
            17f);
        Rect valueRect = new Rect(
            rect.x + horizontalPadding,
            rect.y + 22f,
            Mathf.Max(1f, rect.width - horizontalPadding * 2f),
            22f);

        GUI.Label(labelRect, label, metadataLabelStyle);
        GUI.Label(valueRect, value, metadataValueStyle);
    }

    private static float ResolveLeftColumnWidth(float availableWidth)
    {
        return Mathf.Clamp(availableWidth * 0.39f, 360f, 440f);
    }

    private void SelectClip(AudioClip clip, bool autoPlay)
    {
        if (ReferenceEquals(selectedClip, clip))
        {
            if (autoPlay && selectedClip != null)
                StartPlayback(0f);
            return;
        }

        StopPlayback();
        selectedClip = clip;
        LoadMemo();
        RefreshClipTechnicalInfo();

        if (selectedClip == null)
        {
            SetStatus("오디오 선택을 비웠습니다.", MessageType.Info);
        }
        else if (autoPlay)
        {
            StartPlayback(0f);
            SetStatus(selectedClip.name + " 자동 재생", MessageType.Info);
        }

        Repaint();
    }

    private void RefreshClipTechnicalInfo()
    {
        waveformMinMaxData = Array.Empty<float>();
        clipTechnicalInfo = default;
        if (selectedClip == null)
            return;

        string assetPath = AssetDatabase.GetAssetPath(selectedClip);
        AudioImporter importer = string.IsNullOrEmpty(assetPath)
            ? null
            : AssetImporter.GetAtPath(assetPath) as AudioImporter;
        waveformMinMaxData = SfxEditorAudioPreview.GetMinMaxData(importer);
        clipTechnicalInfo = SfxClipTechnicalInfo.Create(selectedClip, importer, assetPath);
    }

    private void TryUseProjectSelection()
    {
        AudioClip clip = ResolveAudioClip(Selection.activeObject);
        if (clip != null)
            SelectClip(clip, true);
    }

    private void UseActiveSelectionOrNotify()
    {
        AudioClip clip = ResolveAudioClip(Selection.activeObject);
        if (clip == null)
        {
            SetStatus("Project 창에서 AudioClip을 먼저 선택하세요.", MessageType.Warning);
            return;
        }

        SelectClip(clip, true);
    }

    private static AudioClip ResolveAudioClip(UnityEngine.Object asset)
    {
        if (asset is AudioClip clip)
            return clip;
        if (asset == null)
            return null;

        string path = AssetDatabase.GetAssetPath(asset);
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    private void TogglePlayPause()
    {
        if (selectedClip == null)
            return;

        if (!isPlaying && !isPaused)
        {
            StartPlayback(0f);
            return;
        }

        if (isPaused)
        {
            if (SfxEditorAudioPreview.Resume())
            {
                isPaused = false;
                isPlaying = true;
                playbackStartTime = EditorApplication.timeSinceStartup;
                playbackStartSeconds = GetCurrentPlaybackSeconds();
            }
            else
            {
                StartPlayback(playbackStartSeconds);
            }
        }
        else
        {
            playbackStartSeconds = GetCurrentPlaybackSeconds();
            SfxEditorAudioPreview.Pause();
            isPaused = true;
            isPlaying = false;
        }

        Repaint();
    }

    private void StartPlayback(float startSeconds)
    {
        if (selectedClip == null)
            return;

        float safeStart = Mathf.Clamp(startSeconds, 0f, Mathf.Max(0f, selectedClip.length - 0.001f));
        int startSample = Mathf.Clamp(
            Mathf.RoundToInt(safeStart * selectedClip.frequency),
            0,
            Mathf.Max(0, selectedClip.samples - 1));

        if (!SfxEditorAudioPreview.Play(selectedClip, startSample, loopPlayback, previewVolume))
        {
            isPlaying = false;
            isPaused = false;
            SetStatus("현재 Unity 버전에서 오디오 미리듣기를 시작하지 못했습니다.", MessageType.Error);
            return;
        }

        playbackStartSeconds = safeStart;
        playbackStartTime = EditorApplication.timeSinceStartup;
        isPlaying = true;
        isPaused = false;
        Repaint();
    }

    private void StopPlayback()
    {
        SfxEditorAudioPreview.StopAll();
        isPlaying = false;
        isPaused = false;
        playbackStartSeconds = 0f;
        playbackStartTime = EditorApplication.timeSinceStartup;
        Repaint();
    }

    private void SeekPlayback(float seconds)
    {
        if (selectedClip == null)
            return;

        float safeSeconds = Mathf.Clamp(seconds, 0f, selectedClip.length);
        playbackStartSeconds = safeSeconds;
        playbackStartTime = EditorApplication.timeSinceStartup;

        int sample = Mathf.Clamp(
            Mathf.RoundToInt(safeSeconds * selectedClip.frequency),
            0,
            Mathf.Max(0, selectedClip.samples - 1));
        if (!SfxEditorAudioPreview.SetSamplePosition(selectedClip, sample) && (isPlaying || isPaused))
            StartPlayback(safeSeconds);
        Repaint();
    }

    private float GetCurrentPlaybackSeconds()
    {
        if (selectedClip == null)
            return 0f;

        int sample = SfxEditorAudioPreview.GetSamplePosition(selectedClip);
        if (sample >= 0 && selectedClip.frequency > 0 && (isPlaying || isPaused))
            return Mathf.Clamp((float)sample / selectedClip.frequency, 0f, selectedClip.length);

        if (isPaused || !isPlaying)
            return Mathf.Clamp(playbackStartSeconds, 0f, selectedClip.length);

        float elapsed = (float)(EditorApplication.timeSinceStartup - playbackStartTime);
        float current = playbackStartSeconds + elapsed;
        if (loopPlayback && selectedClip.length > 0f)
            current = Mathf.Repeat(current, selectedClip.length);
        return Mathf.Clamp(current, 0f, selectedClip.length);
    }

    private void TickPlayback()
    {
        if (selectedClip == null || (!isPlaying && !isPaused))
            return;

        bool? previewIsPlaying = SfxEditorAudioPreview.IsPlaying(selectedClip);
        float elapsed = (float)(EditorApplication.timeSinceStartup - playbackStartTime);
        bool reachedEnd = GetCurrentPlaybackSeconds() >= selectedClip.length - 0.01f;
        bool previewStopped = previewIsPlaying == false && elapsed > 0.05f;
        if (isPlaying && !loopPlayback && (reachedEnd || previewStopped))
        {
            SfxEditorAudioPreview.StopAll();
            isPlaying = false;
            playbackStartSeconds = selectedClip.length;
        }

        double now = EditorApplication.timeSinceStartup;
        if (now - lastPlaybackRepaintTime < 1d / 30d)
            return;
        lastPlaybackRepaintTime = now;
        Repaint();
    }

    private void LoadMemo()
    {
        memo = selectedClip == null
            ? string.Empty
            : EditorPrefs.GetString(BuildMemoKey(selectedClip), string.Empty);
    }

    private void SaveMemo()
    {
        if (selectedClip == null)
            return;
        EditorPrefs.SetString(BuildMemoKey(selectedClip), memo ?? string.Empty);
    }

    private static string BuildMemoKey(AudioClip clip)
    {
        string path = AssetDatabase.GetAssetPath(clip);
        string guid = string.IsNullOrEmpty(path) ? clip.GetInstanceID().ToString() : AssetDatabase.AssetPathToGUID(path);
        return MemoKeyPrefix + guid;
    }

    private void RefreshBasketFolders()
    {
        string previouslySelected = ResolveSelectedBasketPath();
        basketFolderPaths.Clear();
        basketFolderLabels.Clear();
        basketFolderPaths.Add(BasketRoot);
        basketFolderLabels.Add("장바구니 루트");

        if (AssetDatabase.IsValidFolder(BasketRoot))
            CollectBasketFolders(BasketRoot);

        int matchIndex = basketFolderPaths.IndexOf(previouslySelected);
        selectedFolderIndex = matchIndex >= 0 ? matchIndex : 0;
    }

    private void CollectBasketFolders(string parentPath)
    {
        string[] subFolders = AssetDatabase.GetSubFolders(parentPath);
        Array.Sort(subFolders, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < subFolders.Length; i++)
        {
            string path = subFolders[i].Replace('\\', '/');
            string relative = path.Substring(BasketRoot.Length).TrimStart('/');
            basketFolderPaths.Add(path);
            basketFolderLabels.Add("└ " + relative);
            CollectBasketFolders(path);
        }
    }

    private void RestoreSelectedBasketFolder()
    {
        string savedPath = EditorPrefs.GetString(FolderKey, BasketRoot);
        int index = basketFolderPaths.IndexOf(savedPath);
        selectedFolderIndex = index >= 0 ? index : 0;
    }

    private void SelectBasketFolder(int index)
    {
        selectedFolderIndex = Mathf.Clamp(index, 0, Mathf.Max(0, basketFolderPaths.Count - 1));
        EditorPrefs.SetString(FolderKey, ResolveSelectedBasketPath());
    }

    private string ResolveSelectedBasketPath()
    {
        if (basketFolderPaths.Count == 0)
            return BasketRoot;
        return basketFolderPaths[Mathf.Clamp(selectedFolderIndex, 0, basketFolderPaths.Count - 1)];
    }

    private void CreateAndSelectFolderFromInput()
    {
        if (!TryCreateInputFolder(out string folderPath, out string error))
        {
            SetStatus(error, MessageType.Warning);
            return;
        }

        newFolderName = string.Empty;
        RefreshBasketFolders();
        int index = basketFolderPaths.IndexOf(folderPath);
        SelectBasketFolder(index >= 0 ? index : 0);
        SetStatus("장바구니 폴더를 생성하고 선택했습니다: " + folderPath, MessageType.Info);
    }

    private bool TryCreateInputFolder(out string folderPath, out string error)
    {
        folderPath = BasketRoot;
        error = string.Empty;
        EnsureBasketRoot();

        string raw = (newFolderName ?? string.Empty).Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "새 폴더 이름을 입력하세요.";
            return false;
        }

        string[] segments = raw.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        string parent = BasketRoot;
        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i].Trim();
            if (!IsValidFolderSegment(segment))
            {
                error = "사용할 수 없는 폴더 이름입니다: " + segment;
                return false;
            }

            string next = parent + "/" + segment;
            if (!AssetDatabase.IsValidFolder(next))
            {
                string guid = AssetDatabase.CreateFolder(parent, segment);
                if (string.IsNullOrEmpty(guid))
                {
                    error = "폴더를 생성하지 못했습니다: " + next;
                    return false;
                }
            }
            parent = next;
        }

        folderPath = parent;
        AssetDatabase.Refresh();
        return true;
    }

    private static bool IsValidFolderSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == "..")
            return false;
        if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;
        return segment.IndexOf(':') < 0;
    }

    private void EnsureBasketRoot()
    {
        if (AssetDatabase.IsValidFolder(BasketRoot))
            return;

        AssetDatabase.CreateFolder("Assets", "SFX장바구니");
        AssetDatabase.Refresh();
        RefreshBasketFolders();
    }

    private void CopySelectedClipToBasket()
    {
        if (selectedClip == null)
            return;

        string sourcePath = AssetDatabase.GetAssetPath(selectedClip);
        if (string.IsNullOrEmpty(sourcePath) || !AssetDatabase.Contains(selectedClip))
        {
            SetStatus("프로젝트에 저장된 AudioClip만 장바구니에 담을 수 있습니다.", MessageType.Warning);
            return;
        }

        EnsureBasketRoot();
        string destinationFolder = ResolveSelectedBasketPath();

        if (!string.IsNullOrWhiteSpace(newFolderName)) // 입력값이 남아 있으면 담기와 동시에 생성·선택
        {
            if (!TryCreateInputFolder(out destinationFolder, out string error))
            {
                SetStatus(error, MessageType.Warning);
                return;
            }
            newFolderName = string.Empty;
            RefreshBasketFolders();
            int folderIndex = basketFolderPaths.IndexOf(destinationFolder);
            SelectBasketFolder(folderIndex >= 0 ? folderIndex : 0);
        }

        string destinationPath = destinationFolder + "/" + Path.GetFileName(sourcePath);
        UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(destinationPath);
        if (existing != null)
        {
            Hash128 sourceHash = AssetDatabase.GetAssetDependencyHash(sourcePath);
            Hash128 destinationHash = AssetDatabase.GetAssetDependencyHash(destinationPath);
            if (sourceHash == destinationHash) // 동일 내용은 중복 복사하지 않고 기존 파일을 표시
            {
                EditorGUIUtility.PingObject(existing);
                SetStatus("이미 장바구니에 있는 파일입니다: " + destinationPath, MessageType.Info);
                return;
            }

            destinationPath = AssetDatabase.GenerateUniqueAssetPath(destinationPath);
        }

        if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
        {
            SetStatus("장바구니 복사에 실패했습니다: " + destinationPath, MessageType.Error);
            return;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        AudioClip copiedClip = AssetDatabase.LoadAssetAtPath<AudioClip>(destinationPath);
        if (copiedClip != null)
            EditorGUIUtility.PingObject(copiedClip);

        SetStatus("장바구니에 복사했습니다: " + destinationPath, MessageType.Info);
    }

    private void PingSelectedClip()
    {
        if (selectedClip != null)
            EditorGUIUtility.PingObject(selectedClip);
    }

    private void PingBasketFolder()
    {
        EnsureBasketRoot();
        UnityEngine.Object folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(ResolveSelectedBasketPath());
        if (folder != null)
            EditorGUIUtility.PingObject(folder);
    }

    private void SetStatus(string message, MessageType type)
    {
        statusMessage = message;
        statusType = type;
        Repaint();
    }

    private string BuildPlaybackStateText()
    {
        if (isPaused)
            return "일시 정지";
        if (isPlaying)
            return loopPlayback ? "반복 재생 중" : "재생 중";
        return "정지";
    }

    private static string FormatTime(float seconds)
    {
        if (float.IsNaN(seconds) || float.IsInfinity(seconds))
            seconds = 0f;
        seconds = Mathf.Max(0f, seconds);
        int minutes = Mathf.FloorToInt(seconds / 60f);
        float remainder = seconds - minutes * 60f;
        return minutes.ToString("00") + ":" + remainder.ToString("00.00");
    }

    private static string FormatGridTime(float seconds)
    {
        if (seconds < 1f)
            return seconds.ToString("0.00") + "s";
        if (seconds < 60f)
            return seconds.ToString("0.#") + "s";
        int minutes = Mathf.FloorToInt(seconds / 60f);
        int remainder = Mathf.FloorToInt(seconds - minutes * 60f);
        return minutes.ToString("00") + ":" + remainder.ToString("00");
    }

    private static string BuildChannelText(int channels)
    {
        if (channels == 1)
            return "Mono";
        if (channels == 2)
            return "Stereo";
        return channels + " ch";
    }

    private static string FormatEnabled(bool enabled)
    {
        return enabled ? "ON" : "OFF";
    }

    private void EnsureStyles()
    {
        if (sectionStyle != null)
            return;

        headerTitleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 19,
            normal = { textColor = new Color(0.94f, 0.96f, 1f, 1f) }
        };

        headerMetaStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(0.72f, 0.78f, 0.86f, 1f) }
        };

        sectionStyle = new GUIStyle(EditorStyles.helpBox)
        {
            margin = new RectOffset(0, 0, 0, 0),
            padding = new RectOffset(10, 10, 8, 10)
        };

        sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 12,
            normal = { textColor = new Color(0.9f, 0.94f, 1f, 1f) }
        };

        fieldLabelStyle = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fixedHeight = ControlHeight,
            normal = { textColor = new Color(0.8f, 0.84f, 0.9f, 1f) }
        };

        compactButtonStyle = new GUIStyle(EditorStyles.miniButton)
        {
            alignment = TextAnchor.MiddleCenter,
            fixedHeight = ControlHeight,
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            margin = new RectOffset(1, 1, 0, 0)
        };

        primaryButtonStyle = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.93f, 0.97f, 1f, 1f) }
        };

        metadataCardStyle = new GUIStyle(EditorStyles.helpBox)
        {
            margin = new RectOffset(0, 0, 0, 0),
            padding = new RectOffset(0, 0, 0, 0)
        };

        metadataLabelStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(0.58f, 0.65f, 0.74f, 1f) }
        };

        metadataValueStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            normal = { textColor = new Color(0.86f, 0.92f, 0.98f, 1f) }
        };

        playbackValueStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight,
            fixedHeight = ControlHeight,
            clipping = TextClipping.Clip,
            normal = { textColor = new Color(0.82f, 0.88f, 0.95f, 1f) }
        };

        waveformOverlayStyle = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(0.82f, 0.9f, 1f, 1f) }
        };

        footerStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip
        };

        footerBrandStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.78f, 0.86f, 1f, 1f) }
        };
    }

    private static void DrawCenteredLabel(Rect rect, string text)
    {
        GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.68f, 0.73f, 0.8f, 1f) }
        };
        GUI.Label(rect, text, style);
    }
}

internal readonly struct SfxClipTechnicalInfo
{
    public readonly string FileExtension;
    public readonly string FileSizeText;
    public readonly string EncodingText;
    public readonly string BitRateText;
    public readonly string BitDepthText;
    public readonly string LoadTypeText;
    public readonly string QualityText;
    public readonly bool ForceToMono;
    public readonly bool PreloadAudioData;
    public readonly bool LoadInBackground;
    public readonly bool Ambisonic;

    private SfxClipTechnicalInfo(
        string fileExtension,
        string fileSizeText,
        string encodingText,
        string bitRateText,
        string bitDepthText,
        string loadTypeText,
        string qualityText,
        bool forceToMono,
        bool preloadAudioData,
        bool loadInBackground,
        bool ambisonic)
    {
        FileExtension = fileExtension;
        FileSizeText = fileSizeText;
        EncodingText = encodingText;
        BitRateText = bitRateText;
        BitDepthText = bitDepthText;
        LoadTypeText = loadTypeText;
        QualityText = qualityText;
        ForceToMono = forceToMono;
        PreloadAudioData = preloadAudioData;
        LoadInBackground = loadInBackground;
        Ambisonic = ambisonic;
    }

    public static SfxClipTechnicalInfo Create(
        AudioClip clip,
        AudioImporter importer,
        string assetPath)
    {
        string extension = string.IsNullOrEmpty(assetPath)
            ? "-"
            : Path.GetExtension(assetPath).TrimStart('.').ToUpperInvariant();
        long fileSize = ReadFileSize(assetPath);
        int bitRate = SfxEditorAudioPreview.GetBitRate(clip);
        int bitDepth = SfxEditorAudioPreview.GetBitsPerSample(clip);
        string encoding = SfxEditorAudioPreview.GetCompressionFormat(clip);

        AudioImporterSampleSettings settings = importer != null
            ? importer.defaultSampleSettings
            : default;
        string loadType = importer != null ? FormatLoadType(settings.loadType) : "-";
        string quality = importer != null
            ? Mathf.RoundToInt(settings.quality * 100f) + "%"
            : "-";

        return new SfxClipTechnicalInfo(
            string.IsNullOrEmpty(extension) ? "-" : extension,
            FormatBytes(fileSize),
            string.IsNullOrEmpty(encoding) ? "-" : encoding,
            bitRate > 0 ? Mathf.RoundToInt(bitRate / 1000f).ToString("N0") + " kbps" : "-",
            bitDepth > 0 ? bitDepth + "-bit" : "-",
            loadType,
            quality,
            importer != null && importer.forceToMono,
            importer != null && settings.preloadAudioData,
            importer != null && importer.loadInBackground,
            importer != null && importer.ambisonic);
    }

    private static long ReadFileSize(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return 0L;
        try
        {
            string fullPath = Path.GetFullPath(assetPath);
            return File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0L;
        }
        catch (Exception)
        {
            return 0L;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0L)
            return "-";
        if (bytes >= 1024L * 1024L)
            return (bytes / (1024f * 1024f)).ToString("0.##") + " MB";
        if (bytes >= 1024L)
            return (bytes / 1024f).ToString("0.#") + " KB";
        return bytes + " B";
    }

    private static string FormatLoadType(AudioClipLoadType loadType)
    {
        switch (loadType)
        {
            case AudioClipLoadType.DecompressOnLoad:
                return "즉시 압축 해제";
            case AudioClipLoadType.CompressedInMemory:
                return "메모리 압축";
            case AudioClipLoadType.Streaming:
                return "스트리밍";
            default:
                return loadType.ToString();
        }
    }
}

internal static class SfxEditorAudioPreview
{
    // Unity 버전별 내부 미리듣기 API 이름 차이를 한곳에서 흡수한다.
    private static readonly Type AudioUtilType =
        typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
    private static readonly MethodInfo PlayMethod =
        FindMethod("PlayPreviewClip") ?? FindMethod("PlayClip");
    private static readonly MethodInfo StopMethod =
        FindMethod("StopAllPreviewClips") ?? FindMethod("StopAllClips");
    private static readonly MethodInfo PauseMethod =
        FindMethod("PausePreviewClip") ?? FindMethod("PauseClip");
    private static readonly MethodInfo ResumeMethod =
        FindMethod("ResumePreviewClip") ?? FindMethod("ResumeClip");
    private static readonly MethodInfo SetSampleMethod =
        FindMethod("SetPreviewClipSamplePosition") ?? FindMethod("SetClipSamplePosition");
    private static readonly MethodInfo GetSampleMethod =
        FindMethod("GetPreviewClipSamplePosition") ?? FindMethod("GetClipSamplePosition");
    private static readonly MethodInfo LoopMethod =
        FindMethod("LoopPreviewClip") ?? FindMethod("LoopClip");
    private static readonly MethodInfo IsPlayingMethod =
        FindMethod("IsPreviewClipPlaying") ?? FindMethod("IsClipPlaying");
    private static readonly MethodInfo MinMaxDataMethod = FindMethod("GetMinMaxData");
    private static readonly MethodInfo BitRateMethod = FindMethod("GetBitRate");
    private static readonly MethodInfo BitsPerSampleMethod = FindMethod("GetBitsPerSample");
    private static readonly MethodInfo CompressionFormatMethod = FindMethod("GetSoundCompressionFormat");
    private static bool ownsListenerVolume;
    private static float previousListenerVolume = 1f;

    public static bool Play(AudioClip clip, int startSample, bool loop, float volume)
    {
        if (clip == null || PlayMethod == null)
            return false;

        StopAll();
        SetVolume(volume);
        try
        {
            Invoke(PlayMethod, clip, startSample, loop);
            if (LoopMethod != null)
                Invoke(LoopMethod, clip, startSample, loop);
            return true;
        }
        catch (TargetInvocationException exception)
        {
            Debug.LogException(exception.InnerException ?? exception);
            RestoreListenerVolume();
            return false;
        }
    }

    public static void StopAll()
    {
        try
        {
            StopMethod?.Invoke(null, null);
        }
        catch (TargetInvocationException exception)
        {
            Debug.LogException(exception.InnerException ?? exception);
        }
        finally
        {
            RestoreListenerVolume();
        }
    }

    public static void Pause()
    {
        try
        {
            PauseMethod?.Invoke(null, null);
        }
        catch (TargetInvocationException exception)
        {
            Debug.LogException(exception.InnerException ?? exception);
        }
    }

    public static bool Resume()
    {
        if (ResumeMethod == null)
            return false;
        try
        {
            ResumeMethod.Invoke(null, null);
            return true;
        }
        catch (TargetInvocationException exception)
        {
            Debug.LogException(exception.InnerException ?? exception);
            return false;
        }
    }

    public static bool SetSamplePosition(AudioClip clip, int sample)
    {
        if (SetSampleMethod == null)
            return false;
        try
        {
            Invoke(SetSampleMethod, clip, sample, false);
            return true;
        }
        catch (TargetInvocationException exception)
        {
            Debug.LogException(exception.InnerException ?? exception);
            return false;
        }
    }

    public static int GetSamplePosition(AudioClip clip)
    {
        if (GetSampleMethod == null)
            return -1;
        try
        {
            object result = Invoke(GetSampleMethod, clip, 0, false);
            return result is int sample ? sample : -1;
        }
        catch (TargetInvocationException)
        {
            return -1;
        }
    }

    public static bool? IsPlaying(AudioClip clip)
    {
        if (IsPlayingMethod == null)
            return null;
        try
        {
            object result = Invoke(IsPlayingMethod, clip, 0, false);
            return result is bool playing ? playing : null;
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }

    public static float[] GetMinMaxData(AudioImporter importer)
    {
        if (importer == null || MinMaxDataMethod == null)
            return Array.Empty<float>();
        try
        {
            return MinMaxDataMethod.Invoke(null, new object[] { importer }) as float[]
                ?? Array.Empty<float>();
        }
        catch (TargetInvocationException)
        {
            return Array.Empty<float>();
        }
    }

    public static int GetBitRate(AudioClip clip)
    {
        return InvokeClipInt(BitRateMethod, clip);
    }

    public static int GetBitsPerSample(AudioClip clip)
    {
        return InvokeClipInt(BitsPerSampleMethod, clip);
    }

    public static string GetCompressionFormat(AudioClip clip)
    {
        if (clip == null || CompressionFormatMethod == null)
            return string.Empty;
        try
        {
            object result = CompressionFormatMethod.Invoke(null, new object[] { clip });
            return result?.ToString() ?? string.Empty;
        }
        catch (TargetInvocationException)
        {
            return string.Empty;
        }
    }

    private static int InvokeClipInt(MethodInfo method, AudioClip clip)
    {
        if (method == null || clip == null)
            return 0;
        try
        {
            object result = method.Invoke(null, new object[] { clip });
            return result is int value ? value : 0;
        }
        catch (TargetInvocationException)
        {
            return 0;
        }
    }

    public static void SetVolume(float volume)
    {
        if (!ownsListenerVolume)
        {
            previousListenerVolume = AudioListener.volume;
            ownsListenerVolume = true;
        }
        AudioListener.volume = Mathf.Clamp01(volume);
    }

    private static void RestoreListenerVolume()
    {
        if (!ownsListenerVolume)
            return;
        AudioListener.volume = previousListenerVolume;
        ownsListenerVolume = false;
    }

    private static object Invoke(
        MethodInfo method,
        AudioClip clip,
        int sample,
        bool loop)
    {
        ParameterInfo[] parameters = method.GetParameters();
        object[] arguments = new object[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            Type type = parameters[i].ParameterType;
            if (type == typeof(AudioClip))
                arguments[i] = clip;
            else if (type == typeof(int))
                arguments[i] = sample;
            else if (type == typeof(bool))
                arguments[i] = loop;
            else
                arguments[i] = type.IsValueType ? Activator.CreateInstance(type) : null;
        }
        return method.Invoke(null, arguments);
    }

    private static MethodInfo FindMethod(string name)
    {
        if (AudioUtilType == null)
            return null;

        MethodInfo[] methods = AudioUtilType.GetMethods(
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            if (!string.Equals(method.Name, name, StringComparison.Ordinal))
                continue;
            return method;
        }
        return null;
    }
}
