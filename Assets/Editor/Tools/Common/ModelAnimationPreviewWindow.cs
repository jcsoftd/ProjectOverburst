using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;

public sealed class ModelAnimationPreviewWindow : EditorWindow
{
    private const string WindowTitle = "모델 애니메이션 미리보기";
    private const string MenuPath = "JC Tool/Animation/모델 애니메이션 미리보기";
    private const string FooterBrandText = "JC Soft";
    private const string PreviewFloorName = "[JC Animation Preview Floor]";
    private const string PreviewFloorMaterialName = "[JC Animation Preview Floor Material]";
    private const float ControlHeight = 24f;
    private const float FieldLabelWidth = 64f;
    private const float StandardButtonWidth = 88f;
    private const float ShortButtonWidth = 66f;
    private const float PlaybackButtonWidth = 74f;
    private const float ViewButtonWidth = 48f;
    private const float RowGap = 6f;
    private const float SectionGap = 8f;
    private const float HeaderHeight = 62f;
    private const float FooterHeight = 30f;
    private const float FooterBrandWidth = 72f;
    private const float GridHorizontalMargin = 8f;
    private const float LeftColumnMinWidth = 310f;
    private const float RightColumnMinWidth = 360f;
    private const float TopRowMinHeight = 118f;
    private const float TopRowMaxHeight = 240f;
    private const float BottomRowHeight = 146f;
    private const float MinPreviewHeight = 240f;
    private const float MinCameraPitch = -85f;
    private const float MaxCameraPitch = 85f;
    private const float OrbitSensitivity = 0.35f;
    private const float CameraFitPadding = 1.45f;
    private const float MinCameraRadius = 0.75f;
    private const float MinFloorSize = 4f;
    private const float FloorPaddingMultiplier = 3.5f;
    private const float FloorYOffset = 0.03f;
    private const float CameraTargetYOffsetFactor = -0.08f;
    private const float CameraBlendDuration = 0.28f;
    private const float AxisOverlaySize = 86f;
    private const float AxisOverlayPadding = 12f;
    private const float AxisOverlayLineLength = 26f;
    private const double PreviewTickInterval = 1d / 60d;

    private static readonly Color PreviewBackgroundColor = new Color(0.16f, 0.18f, 0.21f, 1f);
    private static readonly Color PreviewFloorColor = new Color(0.36f, 0.375f, 0.385f, 1f);
    private static readonly Color PreviewAmbientColor = new Color(0.38f, 0.41f, 0.46f, 1f);
    private static readonly Color PreviewKeyLightColor = new Color(1f, 0.96f, 0.9f, 1f);
    private static readonly Color PreviewFillLightColor = new Color(0.58f, 0.7f, 1f, 1f);
    private static readonly Vector2 MinimumWindowSize = new Vector2(700f, 620f);

    private static readonly ViewPreset[] ViewPresets =
    {
        new ViewPreset("앞", 180f, 10f),
        new ViewPreset("뒤", 0f, 10f),
        new ViewPreset("좌", 90f, 10f),
        new ViewPreset("우", 270f, 10f),
        new ViewPreset("상", 180f, 78f),
        new ViewPreset("하", 180f, -78f),
        new ViewPreset("좌상", 135f, 30f),
        new ViewPreset("우상", 225f, 30f),
        new ViewPreset("좌하", 135f, -30f),
        new ViewPreset("우하", 225f, -30f)
    };

    private readonly List<ClipEntry> clipEntries = new List<ClipEntry>();
    private readonly HashSet<AnimationClip> clipSet = new HashSet<AnimationClip>();

    private GameObject selectedModelPrefab;
    private GameObject previewInstance;
    private GameObject floorInstance;
    private Material floorMaterial;
    private PreviewRenderUtility previewUtility;
    private AnimationClip activeClip;
    private Animator previewAnimator;
    private PlayableGraph previewPlayableGraph;
    private AnimationClipPlayable previewClipPlayable;
    private AnimationClip previewPlayableClip;
    private Renderer[] renderers = Array.Empty<Renderer>();
    private Vector3 frameCenter = Vector3.up;
    private float frameRadius = 1f;
    private float frameBottomY;
    private float cameraYaw = 180f;
    private float cameraPitch = 10f;
    private float cameraDistanceScale = 1f;
    private bool cameraBlendActive;
    private float cameraBlendStartYaw;
    private float cameraBlendStartPitch;
    private float cameraBlendStartDistanceScale;
    private float cameraBlendTargetYaw;
    private float cameraBlendTargetPitch;
    private float cameraBlendTargetDistanceScale = 1f;
    private double cameraBlendStartTime;
    private float playbackTime;
    private float playbackSpeed = 1f;
    private bool isPlaying;
    private bool loopPlayback = true;
    private bool inPlacePreview;
    private bool autoUseAnimationSelection = true;
    private bool modelAnimationListLoaded;
    private double lastUpdateTime;
    private string filterText = string.Empty;
    private string previewMessage;
    private Vector2 clipScroll;
    private Vector3 previewRootAnchorPosition;
    private Quaternion previewRootAnchorRotation = Quaternion.identity;

    private GUIStyle headerTitleStyle;
    private GUIStyle headerMetaStyle;
    private GUIStyle sectionStyle;
    private GUIStyle sectionTitleStyle;
    private GUIStyle fieldLabelStyle;
    private GUIStyle compactButtonStyle;
    private GUIStyle noticeStyle;
    private GUIStyle clipButtonStyle;
    private GUIStyle activeClipButtonStyle;
    private GUIStyle footerStyle;
    private GUIStyle footerBrandStyle;
    private GUIStyle dropZoneStyle;
    private GUIStyle axisLabelStyle;
    private GUIStyle centeredPreviewStyle;

    [MenuItem(MenuPath)]
    private static void Open()
    {
        ModelAnimationPreviewWindow window = GetWindow<ModelAnimationPreviewWindow>(WindowTitle);
        window.minSize = MinimumWindowSize;
        window.TryUseSelection();
    }

    private void OnEnable()
    {
        minSize = MinimumWindowSize;
        EnsurePreviewUtility();
        EditorApplication.update += TickPreview;
        ResetPreviewClock();
        TryUseSelection();
    }

    private void OnDisable()
    {
        EditorApplication.update -= TickPreview;
        DestroyAnimatorPreviewGraph();
        ClearPreviewInstance();
        CleanupPreviewUtility();
    }

    private void OnSelectionChange()
    {
        if (autoUseAnimationSelection)
            TryUseSelection();

        Repaint();
    }

    private void OnGUI()
    {
        EnsureStyles();

        DrawHeader();
        DrawControlGrid();
        DrawPreviewSection();
        DrawFooter();
    }

    private void DrawHeader()
    {
        Rect rect = GUILayoutUtility.GetRect(10f, HeaderHeight, GUILayout.ExpandWidth(true));
        Rect backgroundRect = new Rect(rect.x + 2f, rect.y + 4f, rect.width - 4f, rect.height - 8f);
        EditorGUI.DrawRect(backgroundRect, new Color(0.105f, 0.12f, 0.145f, 1f));

        GUI.Label(new Rect(backgroundRect.x + 14f, backgroundRect.y + 7f, backgroundRect.width - 28f, 24f), WindowTitle, headerTitleStyle);

        string modelName = selectedModelPrefab != null ? selectedModelPrefab.name : "모델 없음";
        string clipName = activeClip != null ? activeClip.name : "애니메이션 없음";
        GUI.Label(new Rect(backgroundRect.x + 14f, backgroundRect.y + 35f, backgroundRect.width - 28f, 18f), modelName + "  /  " + clipName, headerMetaStyle);
    }

    private void DrawSection(string title, Action drawBody)
    {
        using (new EditorGUILayout.VerticalScope(sectionStyle))
        {
            EditorGUILayout.LabelField(title, sectionTitleStyle);
            EditorGUILayout.Space(4f);
            drawBody();
        }
    }

    private void DrawControlGrid()
    {
        float topRowHeight = ResolveTopRowHeight();
        float availableWidth = Mathf.Max(0f, position.width - GridHorizontalMargin * 2f - SectionGap);
        float leftColumnWidth = ResolveLeftColumnWidth(availableWidth);
        float rightColumnWidth = Mathf.Max(RightColumnMinWidth, availableWidth - leftColumnWidth);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(GridHorizontalMargin);
            DrawGridPanel("모델", DrawModelPicker, leftColumnWidth, topRowHeight);
            GUILayout.Space(SectionGap);
            DrawGridPanel("클립", DrawAnimationPicker, rightColumnWidth, topRowHeight);
            GUILayout.Space(GridHorizontalMargin);
        }

        GUILayout.Space(SectionGap);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(GridHorizontalMargin);
            DrawGridPanel("카메라", DrawViewButtons, leftColumnWidth, BottomRowHeight);
            GUILayout.Space(SectionGap);
            DrawGridPanel("재생", DrawPlaybackControls, rightColumnWidth, BottomRowHeight);
            GUILayout.Space(GridHorizontalMargin);
        }

        GUILayout.Space(SectionGap);
    }

    private void DrawGridPanel(string title, Action drawBody, float width, float height)
    {
        using (new EditorGUILayout.VerticalScope(sectionStyle, GUILayout.Width(width), GUILayout.Height(height)))
        {
            EditorGUILayout.LabelField(title, sectionTitleStyle);
            EditorGUILayout.Space(4f);
            drawBody();
            GUILayout.FlexibleSpace();
        }
    }

    private float ResolveTopRowHeight()
    {
        float availableHeight = position.height - HeaderHeight - BottomRowHeight - MinPreviewHeight - FooterHeight - 48f;
        float maxHeight = clipEntries.Count > 0 ? TopRowMaxHeight : TopRowMinHeight;
        return Mathf.Clamp(availableHeight, TopRowMinHeight, maxHeight);
    }

    private static float ResolveLeftColumnWidth(float availableWidth)
    {
        float usableWidth = Mathf.Max(0f, availableWidth);
        float maxLeftForRightColumn = usableWidth - RightColumnMinWidth;
        return Mathf.Clamp(LeftColumnMinWidth, 0f, Mathf.Max(0f, maxLeftForRightColumn));
    }

    private void DrawModelPicker()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("대상", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));

            Rect modelDropRect = GUILayoutUtility.GetRect(10f, ControlHeight, GUILayout.ExpandWidth(true), GUILayout.Height(ControlHeight));
            string modelLabel = selectedModelPrefab != null ? selectedModelPrefab.name : "모델 드롭";
            DrawModelDropTarget(modelDropRect, modelLabel);

            GUILayout.Space(RowGap);

            if (GUILayout.Button("비우기", compactButtonStyle, GUILayout.Width(ShortButtonWidth), GUILayout.Height(ControlHeight)))
                SetModelPrefab(null);
        }

        DrawModelDropZone();
    }

    private void DrawAnimationPicker()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("현재", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));

            EditorGUI.BeginChangeCheck();
            AnimationClip nextClip = (AnimationClip)EditorGUILayout.ObjectField(activeClip, typeof(AnimationClip), false, GUILayout.Height(ControlHeight));
            if (EditorGUI.EndChangeCheck())
                SetActiveClip(nextClip, true);

            GUILayout.Space(RowGap);

            if (GUILayout.Button("선택 재생", compactButtonStyle, GUILayout.Width(StandardButtonWidth), GUILayout.Height(ControlHeight)))
                TryUseSelectedAnimation();
        }

        EditorGUILayout.Space(4f);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("소스", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));
            EditorGUI.BeginDisabledGroup(selectedModelPrefab == null);
            if (GUILayout.Button("모델 클립 불러오기", compactButtonStyle, GUILayout.Width(138f), GUILayout.Height(ControlHeight)))
                LoadModelAnimatorClips();
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(RowGap);
            string loadStatus = modelAnimationListLoaded ? clipEntries.Count + "개" : "미불러옴";
            GUILayout.Label(loadStatus, footerStyle, GUILayout.Width(72f), GUILayout.Height(ControlHeight));
            GUILayout.FlexibleSpace();
            autoUseAnimationSelection = EditorGUILayout.ToggleLeft("파일 클릭 재생", autoUseAnimationSelection, GUILayout.Width(112f));
        }

        if (selectedModelPrefab == null)
        {
            DrawInlineNotice("모델을 드롭하면 클립 선택을 사용할 수 있습니다.", true);
            return;
        }

        if (clipEntries.Count == 0)
        {
            string message = modelAnimationListLoaded
                ? "모델에서 클립을 찾지 못했습니다."
                : "모델 클립은 버튼으로 불러옵니다. Project 애니메이션 파일 클릭도 가능합니다.";
            DrawInlineNotice(message, true);
            return;
        }

        EditorGUILayout.Space(4f);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("검색", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));
            filterText = EditorGUILayout.TextField(filterText, GUILayout.Height(ControlHeight));
        }

        DrawClipList();
    }

    private void DrawClipList()
    {
        int visibleCount = 0;
        clipScroll = EditorGUILayout.BeginScrollView(clipScroll, GUILayout.Height(132f));
        for (int i = 0; i < clipEntries.Count; i++)
        {
            ClipEntry entry = clipEntries[i];
            if (!IsClipVisible(entry))
                continue;

            visibleCount++;
            GUIStyle style = entry.Clip == activeClip ? activeClipButtonStyle : clipButtonStyle;
            string label = entry.Clip.name + "   [" + entry.Source + "]   " + FormatTime(entry.Clip.length);
            if (GUILayout.Button(label, style, GUILayout.Height(22f)))
                SetActiveClip(entry.Clip, true);
        }
        EditorGUILayout.EndScrollView();

        if (visibleCount == 0)
            DrawInlineNotice("검색 조건에 맞는 클립이 없습니다.", false);
    }

    private void DrawPlaybackControls()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("제어", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));

            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = isPlaying ? new Color(1f, 0.78f, 0.45f, 1f) : new Color(0.56f, 0.84f, 0.62f, 1f);
            if (GUILayout.Button(isPlaying ? "정지" : "재생", compactButtonStyle, GUILayout.Width(PlaybackButtonWidth), GUILayout.Height(ControlHeight)))
                TogglePlayback();
            GUI.backgroundColor = previousBackground;

            if (GUILayout.Button("처음", compactButtonStyle, GUILayout.Width(PlaybackButtonWidth), GUILayout.Height(ControlHeight)))
                RestartPlayback();

            loopPlayback = GUILayout.Toggle(loopPlayback, "반복", compactButtonStyle, GUILayout.Width(ShortButtonWidth), GUILayout.Height(ControlHeight));

            GUILayout.FlexibleSpace();

            EditorGUI.BeginChangeCheck();
            bool nextInPlace = EditorGUILayout.ToggleLeft("In-place", inPlacePreview, GUILayout.Width(88f), GUILayout.Height(ControlHeight));
            if (EditorGUI.EndChangeCheck())
            {
                inPlacePreview = nextInPlace;
                DestroyAnimatorPreviewGraph();
                SampleActiveClip();
                Repaint();
            }
        }

        EditorGUILayout.Space(4f);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("속도", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));
            playbackSpeed = EditorGUILayout.Slider(playbackSpeed, 0.05f, 2f);
        }

        EditorGUILayout.Space(4f);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("시간", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));
            float clipLength = GetActiveClipLength();
            EditorGUI.BeginDisabledGroup(activeClip == null);
            EditorGUI.BeginChangeCheck();
            float nextTime = EditorGUILayout.Slider(playbackTime, 0f, clipLength);
            if (EditorGUI.EndChangeCheck())
            {
                playbackTime = Mathf.Clamp(nextTime, 0f, clipLength);
                isPlaying = false;
                SampleActiveClip();
                Repaint();
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.Label(FormatTime(playbackTime) + " / " + FormatTime(clipLength), footerStyle, GUILayout.Width(112f));
        }
    }

    private void DrawViewButtons()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("방향", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));
            DrawViewPresetButton(0);
            DrawViewPresetButton(1);
            DrawViewPresetButton(2);
            DrawViewPresetButton(3);
            GUILayout.FlexibleSpace();
        }

        EditorGUILayout.Space(4f);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(FieldLabelWidth);
            DrawViewPresetButton(4);
            DrawViewPresetButton(5);
            GUILayout.Space(ViewButtonWidth);
            if (GUILayout.Button("맞춤", compactButtonStyle, GUILayout.Width(ViewButtonWidth), GUILayout.Height(ControlHeight)))
            {
                RefitCameraFromCurrentPose();
                Repaint();
            }

            GUILayout.FlexibleSpace();
        }

        EditorGUILayout.Space(4f);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(FieldLabelWidth);
            DrawViewPresetButton(6);
            DrawViewPresetButton(7);
            DrawViewPresetButton(8);
            DrawViewPresetButton(9);
            GUILayout.FlexibleSpace();
        }
    }

    private void DrawViewPresetButton(int presetIndex)
    {
        ViewPreset preset = ViewPresets[presetIndex];
        if (GUILayout.Button(preset.Label, compactButtonStyle, GUILayout.Width(ViewButtonWidth), GUILayout.Height(ControlHeight)))
        {
            SetCameraToPreset(presetIndex, true);
            Repaint();
        }
    }

    private void DrawModelDropZone()
    {
        EditorGUILayout.Space(6f);

        Rect rect = GUILayoutUtility.GetRect(10f, FooterHeight, GUILayout.ExpandWidth(true));
        string label = selectedModelPrefab == null
            ? "프리팹 / FBX / Hierarchy 드롭"
            : "다른 모델 드롭으로 교체";
        GUI.Label(rect, label, dropZoneStyle);
        HandleModelDrop(rect);
    }

    private void DrawModelDropTarget(Rect rect, string label)
    {
        GUIContent content = selectedModelPrefab != null
            ? EditorGUIUtility.ObjectContent(selectedModelPrefab, typeof(GameObject))
            : new GUIContent(label);

        GUI.Box(rect, content, EditorStyles.objectField);
        HandleModelDrop(rect);
    }

    private void HandleModelDrop(Rect rect)
    {
        Event current = Event.current;
        if (current == null || !rect.Contains(current.mousePosition))
            return;

        if (current.type != EventType.DragUpdated && current.type != EventType.DragPerform)
            return;

        GameObject draggedModel = FindDraggedModel();
        if (draggedModel == null)
            return;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (current.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            SetModelPrefab(draggedModel);
        }

        current.Use();
    }

    private void DrawPreviewSection()
    {
        float previewSectionHeight = ResolvePreviewSectionHeight();

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(GridHorizontalMargin);
            using (new EditorGUILayout.VerticalScope(sectionStyle, GUILayout.ExpandWidth(true), GUILayout.Height(previewSectionHeight)))
            {
                EditorGUILayout.LabelField("미리보기", sectionTitleStyle);
                EditorGUILayout.Space(4f);

                Rect rect = GUILayoutUtility.GetRect(10f, Mathf.Max(1f, previewSectionHeight - 40f), GUILayout.ExpandWidth(true), GUILayout.Height(Mathf.Max(1f, previewSectionHeight - 40f)));
                EditorGUI.DrawRect(rect, new Color(0.08f, 0.085f, 0.095f, 1f));

                if (previewInstance == null)
                {
                    DrawCenteredLabel(rect, "미리볼 모델을 지정하세요.");
                }
                else
                {
                    HandlePreviewCameraInput(rect);

                    Texture texture = RenderPreviewTexture(rect);
                    if (texture != null)
                        GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, false);

                    DrawCameraAxisOverlay(rect);

                    if (!string.IsNullOrEmpty(previewMessage))
                        DrawPreviewMessage(rect, previewMessage);
                }
            }
            GUILayout.Space(GridHorizontalMargin);
        }
    }

    private float ResolvePreviewSectionHeight()
    {
        float controlGridHeight = ResolveTopRowHeight() + SectionGap + BottomRowHeight + SectionGap;
        float availableHeight = position.height - HeaderHeight - controlGridHeight - FooterHeight - 18f;
        return Mathf.Max(MinPreviewHeight, availableHeight);
    }

    private void DrawFooter()
    {
        Rect rect = GUILayoutUtility.GetRect(10f, 30f, GUILayout.ExpandWidth(true));
        Rect statusRect = new Rect(rect.x + 8f, rect.y + 3f, rect.width - 16f, rect.height - 6f);
        EditorGUI.DrawRect(statusRect, new Color(0.12f, 0.13f, 0.145f, 1f));

        string modelName = selectedModelPrefab != null ? selectedModelPrefab.name : "-";
        string clipName = activeClip != null ? activeClip.name : "-";
        Rect brandRect = new Rect(statusRect.xMax - FooterBrandWidth - 10f, statusRect.y + 3f, FooterBrandWidth, statusRect.height - 4f);
        Rect textRect = new Rect(statusRect.x + 10f, statusRect.y + 3f, Mathf.Max(1f, brandRect.x - statusRect.x - 18f), statusRect.height - 4f);
        GUI.Label(textRect, "모델 " + modelName + "   애니메이션 " + clipName + "   방식 Animator   클립 " + clipEntries.Count + "   Renderer " + renderers.Length, footerStyle);
        GUI.Label(brandRect, FooterBrandText, footerBrandStyle);
    }

    private void DrawInlineNotice(string message, bool info)
    {
        Color previous = GUI.color;
        GUI.color = info ? new Color(0.78f, 0.86f, 1f, 1f) : new Color(1f, 0.86f, 0.62f, 1f);
        EditorGUILayout.LabelField(message, noticeStyle);
        GUI.color = previous;
    }

    private void DrawCenteredLabel(Rect rect, string text)
    {
        if (centeredPreviewStyle == null)
            EnsureStyles();

        GUI.Label(rect, text, centeredPreviewStyle ?? EditorStyles.centeredGreyMiniLabel);
    }

    private void DrawPreviewMessage(Rect rect, string text)
    {
        if (noticeStyle == null)
            EnsureStyles();

        Rect messageRect = new Rect(rect.x + 12f, rect.y + 12f, Mathf.Min(rect.width - 24f, 420f), 26f);
        EditorGUI.DrawRect(messageRect, new Color(0.08f, 0.09f, 0.11f, 0.82f));
        GUI.Label(new Rect(messageRect.x + 8f, messageRect.y + 5f, messageRect.width - 16f, 18f), text, noticeStyle ?? EditorStyles.wordWrappedMiniLabel);
    }

    private void TryUseSelection()
    {
        UnityEngine.Object[] selectedObjects = Selection.objects;
        if (selectedObjects == null || selectedObjects.Length == 0)
            return;

        bool changed = false;

        for (int i = 0; i < selectedObjects.Length; i++)
        {
            if (TryResolveAnimationClip(selectedObjects[i], out AnimationClip clip))
            {
                SetActiveClip(clip, true);
                changed = true;
                break;
            }
        }

        if (changed)
            Repaint();
    }

    private void TryUseSelectedAnimation()
    {
        UnityEngine.Object active = Selection.activeObject;
        if (TryResolveAnimationClip(active, out AnimationClip clip))
            SetActiveClip(clip, true);
    }

    private void SetModelPrefab(GameObject prefab)
    {
        if (selectedModelPrefab == prefab)
            return;

        bool activeClipWasModelClip = activeClip != null && clipSet.Contains(activeClip);

        selectedModelPrefab = prefab;
        modelAnimationListLoaded = false;
        clipEntries.Clear();
        clipSet.Clear();
        playbackTime = 0f;
        isPlaying = false;
        previewMessage = null;

        if (activeClipWasModelClip)
            activeClip = null;

        RebuildPreviewInstance();

        if (activeClip != null)
            SampleActiveClip();

        RefitCameraFromCurrentPose();
        ResetPreviewClock();
        Repaint();
    }

    private void LoadModelAnimatorClips()
    {
        if (selectedModelPrefab == null)
            return;

        RefreshClipEntries();
        modelAnimationListLoaded = true;
        Repaint();
    }

    private void SetActiveClip(AnimationClip clip, bool playImmediately)
    {
        if (clip == null)
        {
            activeClip = null;
            playbackTime = 0f;
            isPlaying = false;
            previewMessage = null;
            DestroyAnimatorPreviewGraph();
            Repaint();
            return;
        }

        activeClip = clip;
        playbackTime = 0f;
        previewMessage = null;
        DestroyAnimatorPreviewGraph();

        if (previewInstance != null)
        {
            SampleActiveClip();
            RefitCameraFromCurrentPose();
        }

        isPlaying = playImmediately;
        ResetPreviewClock();
        Repaint();
    }

    private void TogglePlayback()
    {
        if (activeClip == null || previewInstance == null)
            return;

        isPlaying = !isPlaying;
        ResetPreviewClock();
        if (isPlaying)
            SampleActiveClip();
    }

    private void RestartPlayback()
    {
        playbackTime = 0f;
        isPlaying = activeClip != null && previewInstance != null;
        ResetPreviewClock();
        SampleActiveClip();
        Repaint();
    }

    private void TickPreview()
    {
        if (!isPlaying && !cameraBlendActive)
            return;

        double now = EditorApplication.timeSinceStartup;
        if (now - lastUpdateTime < PreviewTickInterval)
            return;

        float deltaTime = Mathf.Clamp((float)(now - lastUpdateTime), 0f, 0.1f);
        lastUpdateTime = now;
        bool changed = false;

        if (isPlaying && activeClip != null && previewInstance != null)
        {
            AdvancePlayback(deltaTime);
            changed = true;
        }

        changed |= AdvanceCameraBlend(now);

        if (changed)
            Repaint();
    }

    private void ResetPreviewClock()
    {
        lastUpdateTime = EditorApplication.timeSinceStartup;
    }

    private void AdvancePlayback(float deltaTime)
    {
        float clipLength = GetActiveClipLength();
        if (clipLength <= 0f)
            return;

        playbackTime += deltaTime * Mathf.Max(0.01f, playbackSpeed);

        if (loopPlayback)
        {
            playbackTime = Mathf.Repeat(playbackTime, clipLength);
        }
        else if (playbackTime >= clipLength)
        {
            playbackTime = clipLength;
            isPlaying = false;
        }

        SampleActiveClip();
    }

    private void SampleActiveClip()
    {
        if (previewInstance == null || activeClip == null)
            return;

        EvaluateAnimatorPreviewClip();
    }

    private void EvaluateAnimatorPreviewClip()
    {
        if (!EnsureAnimatorPreviewGraph())
            return;

        try
        {
            previewAnimator.applyRootMotion = !inPlacePreview;
            EvaluateClipAtTime(Mathf.Clamp(playbackTime, 0f, GetActiveClipLength()));
            if (inPlacePreview)
                ResetPreviewRootToAnchor();

            previewMessage = null;
        }
        catch (Exception exception)
        {
            isPlaying = false;
            DestroyAnimatorPreviewGraph();
            previewMessage = "Animator 재생 오류: " + exception.GetType().Name;
        }
    }

    private bool EnsureAnimatorPreviewGraph()
    {
        if (previewInstance == null || activeClip == null)
            return false;

        if (previewPlayableGraph.IsValid() && previewPlayableClip == activeClip && previewAnimator != null)
            return true;

        DestroyAnimatorPreviewGraph();

        previewAnimator = FindPreviewAnimator();
        if (previewAnimator == null)
        {
            previewMessage = "Animator를 만들 수 없습니다.";
            isPlaying = false;
            return false;
        }

        try
        {
            previewAnimator.enabled = true;
            previewAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            previewAnimator.applyRootMotion = !inPlacePreview;

            previewPlayableGraph = PlayableGraph.Create("JC Model Animation Preview");
            previewPlayableGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            previewClipPlayable = AnimationClipPlayable.Create(previewPlayableGraph, activeClip);
            previewClipPlayable.SetApplyFootIK(false);
            previewClipPlayable.SetApplyPlayableIK(false);
            previewClipPlayable.SetSpeed(0d);

            AnimationPlayableOutput output = AnimationPlayableOutput.Create(previewPlayableGraph, "Animation", previewAnimator);
            output.SetSourcePlayable(previewClipPlayable);
            previewPlayableGraph.Play();
            previewPlayableClip = activeClip;
            return true;
        }
        catch (Exception exception)
        {
            DestroyAnimatorPreviewGraph();
            isPlaying = false;
            previewMessage = "Animator 재생 준비 오류: " + exception.GetType().Name;
            return false;
        }
    }

    private Animator FindPreviewAnimator()
    {
        if (previewInstance == null)
            return null;

        Animator[] animators = previewInstance.GetComponentsInChildren<Animator>(true);
        Animator fallback = null;
        for (int i = 0; i < animators.Length; i++)
        {
            Animator animator = animators[i];
            if (animator == null)
                continue;

            if (fallback == null)
                fallback = animator;

            if (animator.avatar != null || animator.runtimeAnimatorController != null)
                return animator;
        }

        return fallback != null ? fallback : previewInstance.AddComponent<Animator>();
    }

    private void DestroyAnimatorPreviewGraph()
    {
        if (previewPlayableGraph.IsValid())
            previewPlayableGraph.Destroy();

        previewPlayableClip = null;
        previewAnimator = null;
    }

    private void EvaluateClipAtTime(float time)
    {
        previewClipPlayable.SetTime(time);
        previewClipPlayable.SetSpeed(0d);
        previewPlayableGraph.Evaluate(0f);
    }

    private void ResetPreviewRootToAnchor()
    {
        if (previewInstance == null)
            return;

        previewInstance.transform.SetPositionAndRotation(previewRootAnchorPosition, previewRootAnchorRotation);
    }

    private void RebuildPreviewInstance()
    {
        ClearPreviewInstance();

        if (selectedModelPrefab == null)
        {
            RefreshRendererCache();
            return;
        }

        EnsurePreviewUtility();

        previewInstance = Instantiate(selectedModelPrefab);
        previewInstance.name = "[JC Animation Preview] " + selectedModelPrefab.name;
        previewInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        previewInstance.transform.localScale = ResolvePreviewRootScale(selectedModelPrefab);
        previewRootAnchorPosition = previewInstance.transform.position;
        previewRootAnchorRotation = previewInstance.transform.rotation;
        previewInstance.SetActive(true);
        DisableRuntimeBehaviours(previewInstance);
        SetHideFlagsRecursive(previewInstance, HideFlags.HideAndDontSave);
        previewUtility.AddSingleGO(previewInstance);
        RefreshRendererCache();
        RefitCameraFromCurrentPose();
    }

    private void ClearPreviewInstance()
    {
        DestroyAnimatorPreviewGraph();
        ClearPreviewFloor();

        if (previewInstance != null)
        {
            DestroyImmediate(previewInstance);
            previewInstance = null;
        }

        RefreshRendererCache();
    }

    private void RefreshRendererCache()
    {
        renderers = previewInstance != null ? previewInstance.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
    }

    private void DisableRuntimeBehaviours(GameObject root)
    {
        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
            behaviours[i].enabled = false;
    }

    private void RefreshClipEntries()
    {
        clipEntries.Clear();
        clipSet.Clear();

        if (selectedModelPrefab == null)
            return;

        AddControllerClips(selectedModelPrefab);
        AddLegacyAnimationClips(selectedModelPrefab);
        AddAssetEmbeddedClips(selectedModelPrefab);
        clipEntries.Sort(CompareClipEntries);

    }

    private void AddControllerClips(GameObject root)
    {
        Animator[] animators = root.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            RuntimeAnimatorController controller = animators[i].runtimeAnimatorController;
            if (controller == null)
                continue;

            AnimationClip[] clips = controller.animationClips;
            for (int j = 0; j < clips.Length; j++)
                AddClip(clips[j], "Animator");
        }
    }

    private void AddLegacyAnimationClips(GameObject root)
    {
        Animation[] animations = root.GetComponentsInChildren<Animation>(true);
        for (int i = 0; i < animations.Length; i++)
        {
            foreach (AnimationState state in animations[i])
                AddClip(state.clip, "Animation");
        }
    }

    private void AddAssetEmbeddedClips(GameObject root)
    {
        if (!TryGetProjectAssetPath(root, out string assetPath))
            return;

        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is AnimationClip clip)
                AddClip(clip, "Asset");
        }
    }

    private void AddClip(AnimationClip clip, string source)
    {
        if (!IsUsableAnimationClip(clip) || clipSet.Contains(clip))
            return;

        clipSet.Add(clip);
        clipEntries.Add(new ClipEntry(clip, source));
    }

    private static int CompareClipEntries(ClipEntry lhs, ClipEntry rhs)
    {
        int sourceCompare = string.Compare(lhs.Source, rhs.Source, StringComparison.OrdinalIgnoreCase);
        if (sourceCompare != 0)
            return sourceCompare;

        return string.Compare(lhs.Clip.name, rhs.Clip.name, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsClipVisible(ClipEntry entry)
    {
        if (string.IsNullOrWhiteSpace(filterText))
            return true;

        return entry.Clip.name.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0
            || entry.Source.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void RefitCameraFromCurrentPose()
    {
        RefreshRendererCache();

        if (!TryResolvePreviewRendererBounds(out Bounds bounds))
        {
            frameCenter = Vector3.up;
            frameRadius = 1f;
            frameBottomY = 0f;
            RebuildPreviewFloor();
            UpdatePreviewCameraTransform();
            return;
        }

        frameCenter = bounds.center;
        frameRadius = Mathf.Max(MinCameraRadius, bounds.extents.magnitude);
        frameBottomY = bounds.min.y;

        RebuildPreviewFloor();
        UpdatePreviewCameraTransform();
    }

    private bool TryResolvePreviewRendererBounds(out Bounds bounds)
    {
        bounds = default;

        if (renderers.Length == 0)
            return false;

        bool hasBounds = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    private Texture RenderPreviewTexture(Rect rect)
    {
        if (previewInstance == null)
        {
            previewMessage = "모델 프리팹을 지정하세요.";
            return null;
        }

        EnsurePreviewUtility();
        UpdatePreviewCameraTransform();

        Texture previewTexture = null;
        bool beganPreview = false;
        try
        {
            previewUtility.BeginPreview(rect, GUIStyle.none);
            beganPreview = true;
            previewUtility.Render(true);
            previewTexture = previewUtility.EndPreview();
        }
        catch (Exception exception)
        {
            if (beganPreview)
            {
                try
                {
                    previewUtility.EndPreview();
                }
                catch
                { // 렌더 예외 뒤 미리보기 정리 중일 수 있음
                }
            }

            previewMessage = "미리보기 렌더 오류: " + exception.GetType().Name;
        }

        return previewTexture;
    }

    private void EnsurePreviewUtility()
    {
        if (previewUtility != null)
            return;

        previewUtility = new PreviewRenderUtility();
        previewUtility.camera.cameraType = CameraType.Preview;
        previewUtility.camera.clearFlags = CameraClearFlags.SolidColor;
        previewUtility.camera.backgroundColor = PreviewBackgroundColor;
        previewUtility.camera.nearClipPlane = 0.01f;
        previewUtility.camera.farClipPlane = 5000f;
        previewUtility.camera.fieldOfView = 45f;
        previewUtility.camera.cullingMask = ~0;

        UniversalAdditionalCameraData cameraData = previewUtility.camera.GetComponent<UniversalAdditionalCameraData>();
        if (cameraData == null)
            cameraData = previewUtility.camera.gameObject.AddComponent<UniversalAdditionalCameraData>();

        cameraData.renderPostProcessing = false;
        previewUtility.ambientColor = PreviewAmbientColor;

        if (previewUtility.lights.Length > 0 && previewUtility.lights[0] != null)
            ConfigurePreviewLight(previewUtility.lights[0], 1.65f, Quaternion.Euler(45f, -35f, 0f), PreviewKeyLightColor);

        if (previewUtility.lights.Length > 1 && previewUtility.lights[1] != null)
            ConfigurePreviewLight(previewUtility.lights[1], 0.9f, Quaternion.Euler(335f, 145f, 0f), PreviewFillLightColor);
    }

    private static void ConfigurePreviewLight(Light light, float intensity, Quaternion rotation, Color color)
    {
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.color = color;
        light.transform.rotation = rotation;
    }

    private void CleanupPreviewUtility()
    {
        if (previewUtility == null)
            return;

        previewUtility.Cleanup();
        previewUtility = null;
    }

    private void RebuildPreviewFloor()
    {
        ClearPreviewFloor();

        if (previewUtility == null || previewInstance == null)
            return;

        floorInstance = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floorInstance.name = PreviewFloorName;
        floorInstance.transform.SetPositionAndRotation(new Vector3(frameCenter.x, frameBottomY - FloorYOffset, frameCenter.z), Quaternion.identity);

        float floorSize = Mathf.Max(MinFloorSize, frameRadius * FloorPaddingMultiplier);
        float planeScale = floorSize / 10f;
        floorInstance.transform.localScale = new Vector3(planeScale, 1f, planeScale);

        Collider floorCollider = floorInstance.GetComponent<Collider>();
        if (floorCollider != null)
            DestroyImmediate(floorCollider);

        Renderer floorRenderer = floorInstance.GetComponent<Renderer>();
        floorMaterial = CreatePreviewFloorMaterial();
        if (floorRenderer != null)
            floorRenderer.sharedMaterial = floorMaterial;

        SetHideFlagsRecursive(floorInstance, HideFlags.HideAndDontSave);
        previewUtility.AddSingleGO(floorInstance);
    }

    private void ClearPreviewFloor()
    {
        if (floorInstance != null)
        {
            DestroyImmediate(floorInstance);
            floorInstance = null;
        }

        if (floorMaterial != null)
        {
            DestroyImmediate(floorMaterial);
            floorMaterial = null;
        }
    }

    private static Material CreatePreviewFloorMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Hidden/Internal-Colored");

        Material material = new Material(shader)
        {
            name = PreviewFloorMaterialName,
            hideFlags = HideFlags.HideAndDontSave
        };

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", PreviewFloorColor);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", PreviewFloorColor);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 0.25f);

        return material;
    }

    private void HandlePreviewCameraInput(Rect rect)
    {
        Event current = Event.current;
        if (current == null || !rect.Contains(current.mousePosition))
            return;

        if (current.type == EventType.MouseDrag && current.button == 1)
        {
            CancelCameraBlend();
            cameraYaw -= current.delta.x * OrbitSensitivity;
            cameraPitch = Mathf.Clamp(cameraPitch + current.delta.y * OrbitSensitivity, MinCameraPitch, MaxCameraPitch);
            UpdatePreviewCameraTransform();
            Repaint();
            current.Use();
            return;
        }

        if (current.type == EventType.ScrollWheel)
        {
            CancelCameraBlend();
            cameraDistanceScale = Mathf.Clamp(cameraDistanceScale * (1f + current.delta.y * 0.08f), 0.2f, 6f);
            UpdatePreviewCameraTransform();
            Repaint();
            current.Use();
        }
    }

    private void UpdatePreviewCameraTransform()
    {
        if (previewUtility == null || previewUtility.camera == null)
            return;

        float distance = Mathf.Max(1.5f, frameRadius * CameraFitPadding * cameraDistanceScale * 2.2f);
        Quaternion rotation = Quaternion.Euler(cameraPitch, cameraYaw, 0f);
        Vector3 forward = rotation * Vector3.forward;
        Vector3 cameraTarget = frameCenter + Vector3.up * (frameRadius * CameraTargetYOffsetFactor);
        Vector3 position = cameraTarget - forward * distance;

        previewUtility.camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
        previewUtility.camera.nearClipPlane = Mathf.Max(0.01f, distance - frameRadius * 4f);
        previewUtility.camera.farClipPlane = Mathf.Max(100f, distance + frameRadius * 6f);
    }

    private void SetCameraToPreset(int presetIndex, bool animated)
    {
        ViewPreset preset = ViewPresets[Mathf.Clamp(presetIndex, 0, ViewPresets.Length - 1)];

        if (animated && previewInstance != null)
        {
            StartCameraBlend(preset.Yaw, preset.Pitch, 1f);
            return;
        }

        cameraBlendActive = false;
        cameraYaw = NormalizeAngle(preset.Yaw);
        cameraPitch = preset.Pitch;
        cameraDistanceScale = 1f;
        UpdatePreviewCameraTransform();
    }

    private void StartCameraBlend(float targetYaw, float targetPitch, float targetDistanceScale)
    {
        cameraBlendActive = true;
        cameraBlendStartTime = EditorApplication.timeSinceStartup;
        cameraBlendStartYaw = cameraYaw;
        cameraBlendStartPitch = cameraPitch;
        cameraBlendStartDistanceScale = cameraDistanceScale;
        cameraBlendTargetYaw = cameraYaw + Mathf.DeltaAngle(cameraYaw, targetYaw);
        cameraBlendTargetPitch = targetPitch;
        cameraBlendTargetDistanceScale = targetDistanceScale;
        ResetPreviewClock();
    }

    private bool AdvanceCameraBlend(double now)
    {
        if (!cameraBlendActive)
            return false;

        float t = CameraBlendDuration <= 0f ? 1f : Mathf.Clamp01((float)((now - cameraBlendStartTime) / CameraBlendDuration));
        float eased = Mathf.SmoothStep(0f, 1f, t);

        cameraYaw = Mathf.Lerp(cameraBlendStartYaw, cameraBlendTargetYaw, eased);
        cameraPitch = Mathf.Lerp(cameraBlendStartPitch, cameraBlendTargetPitch, eased);
        cameraDistanceScale = Mathf.Lerp(cameraBlendStartDistanceScale, cameraBlendTargetDistanceScale, eased);

        if (t >= 1f)
        {
            cameraBlendActive = false;
            cameraYaw = NormalizeAngle(cameraBlendTargetYaw);
            cameraPitch = cameraBlendTargetPitch;
            cameraDistanceScale = cameraBlendTargetDistanceScale;
        }

        UpdatePreviewCameraTransform();
        return true;
    }

    private void CancelCameraBlend()
    {
        cameraBlendActive = false;
    }

    private void DrawCameraAxisOverlay(Rect previewRect)
    {
        if (previewUtility == null || previewUtility.camera == null || Event.current.type != EventType.Repaint)
            return;

        Rect boxRect = new Rect(
            previewRect.xMax - AxisOverlaySize - AxisOverlayPadding,
            previewRect.y + AxisOverlayPadding,
            AxisOverlaySize,
            AxisOverlaySize);

        EditorGUI.DrawRect(boxRect, new Color(0.045f, 0.052f, 0.064f, 0.78f));

        Vector2 origin = new Vector2(boxRect.center.x, boxRect.center.y + 6f);
        Vector2 xEnd = ResolveAxisOverlayEnd(origin, Vector3.right);
        Vector2 yEnd = ResolveAxisOverlayEnd(origin, Vector3.up);
        Vector2 zEnd = ResolveAxisOverlayEnd(origin, Vector3.forward);

        Handles.BeginGUI();
        Color previousColor = Handles.color;
        DrawAxisOverlayLine(origin, zEnd, new Color(0.45f, 0.68f, 1f, 1f));
        DrawAxisOverlayLine(origin, xEnd, new Color(1f, 0.42f, 0.38f, 1f));
        DrawAxisOverlayLine(origin, yEnd, new Color(0.46f, 0.92f, 0.52f, 1f));
        Handles.color = new Color(0.93f, 0.95f, 0.98f, 1f);
        Handles.DrawSolidDisc(new Vector3(origin.x, origin.y, 0f), Vector3.forward, 3f);
        Handles.color = previousColor;
        Handles.EndGUI();

        DrawAxisOverlayLabel(xEnd, "X", new Color(1f, 0.48f, 0.45f, 1f));
        DrawAxisOverlayLabel(yEnd, "Y", new Color(0.52f, 1f, 0.58f, 1f));
        DrawAxisOverlayLabel(zEnd, "Z", new Color(0.52f, 0.74f, 1f, 1f));
    }

    private Vector2 ResolveAxisOverlayEnd(Vector2 origin, Vector3 worldAxis)
    {
        Vector3 localAxis = previewUtility.camera.transform.InverseTransformDirection(worldAxis).normalized;
        Vector2 screenDirection = new Vector2(localAxis.x, -localAxis.y);
        float screenMagnitude = screenDirection.magnitude;

        if (screenMagnitude < 0.05f)
            screenDirection = localAxis.z >= 0f ? new Vector2(0.35f, -0.35f) : new Vector2(-0.35f, 0.35f);
        else
            screenDirection /= screenMagnitude;

        float length = Mathf.Lerp(AxisOverlayLineLength * 0.48f, AxisOverlayLineLength, Mathf.Clamp01(screenMagnitude));
        return origin + screenDirection * length;
    }

    private static void DrawAxisOverlayLine(Vector2 origin, Vector2 end, Color color)
    {
        Vector3 originPoint = new Vector3(origin.x, origin.y, 0f);
        Vector3 endPoint = new Vector3(end.x, end.y, 0f);

        Handles.color = new Color(0f, 0f, 0f, 0.42f);
        Handles.DrawAAPolyLine(5f, originPoint, endPoint);
        Handles.color = color;
        Handles.DrawAAPolyLine(3f, originPoint, endPoint);
    }

    private void DrawAxisOverlayLabel(Vector2 position, string label, Color color)
    {
        axisLabelStyle.normal.textColor = color;
        GUI.Label(new Rect(position.x - 10f, position.y - 10f, 20f, 18f), label, axisLabelStyle);
    }

    private float GetActiveClipLength()
    {
        return activeClip != null ? Mathf.Max(0.001f, activeClip.length) : 1f;
    }

    private static bool TryResolveAnimationClip(UnityEngine.Object source, out AnimationClip clip)
    {
        clip = null;

        if (source == null)
            return false;

        if (source is AnimationClip directClip && IsUsableAnimationClip(directClip))
        {
            clip = directClip;
            return true;
        }

        if (!TryGetProjectAssetPath(source, out string assetPath))
            return false;

        AnimationClip fallbackClip = null;
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        for (int i = 0; i < assets.Length; i++)
        {
            if (!(assets[i] is AnimationClip candidate) || !IsUsableAnimationClip(candidate))
                continue;

            if (fallbackClip == null)
                fallbackClip = candidate;

            if (string.Equals(candidate.name, source.name, StringComparison.OrdinalIgnoreCase))
            {
                clip = candidate;
                return true;
            }
        }

        clip = fallbackClip;
        return clip != null;
    }

    private static bool IsUsableAnimationClip(AnimationClip clip)
    {
        return clip != null && !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetProjectAssetPath(UnityEngine.Object source, out string assetPath)
    {
        assetPath = null;

        if (source == null || !EditorUtility.IsPersistent(source) || !AssetDatabase.Contains(source))
            return false;

        assetPath = AssetDatabase.GetAssetPath(source);
        return !string.IsNullOrEmpty(assetPath);
    }

    private static bool IsPrefabAsset(GameObject gameObject)
    {
        if (gameObject == null)
            return false;

        string assetPath = AssetDatabase.GetAssetPath(gameObject);
        if (string.IsNullOrEmpty(assetPath))
            return false;

        PrefabAssetType prefabAssetType = PrefabUtility.GetPrefabAssetType(gameObject);
        return prefabAssetType != PrefabAssetType.NotAPrefab || assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSceneObject(GameObject gameObject)
    {
        return gameObject != null && !EditorUtility.IsPersistent(gameObject);
    }

    private static bool IsSupportedModel(GameObject gameObject)
    {
        return gameObject != null && (IsPrefabAsset(gameObject) || IsSceneObject(gameObject));
    }

    private static GameObject FindDraggedModel()
    {
        UnityEngine.Object[] references = DragAndDrop.objectReferences;
        if (references == null)
            return null;

        for (int i = 0; i < references.Length; i++)
        {
            if (references[i] is GameObject gameObject && IsSupportedModel(gameObject))
                return gameObject;
        }

        return null;
    }

    private static Vector3 ResolvePreviewRootScale(GameObject source)
    {
        if (source == null)
            return Vector3.one;

        return IsSceneObject(source) ? source.transform.lossyScale : source.transform.localScale;
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle < 0f)
            angle += 360f;

        return angle;
    }

    private static void SetHideFlagsRecursive(GameObject root, HideFlags hideFlags)
    {
        if (root == null)
            return;

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
            transforms[i].gameObject.hideFlags = hideFlags;
    }

    private void EnsureStyles()
    {
        if (headerTitleStyle != null && centeredPreviewStyle != null && noticeStyle != null)
            return;

        headerTitleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 19,
            normal = { textColor = new Color(0.94f, 0.96f, 1f, 1f) }
        };

        headerMetaStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = new Color(0.72f, 0.78f, 0.86f, 1f) }
        };

        sectionStyle = new GUIStyle("HelpBox")
        {
            padding = new RectOffset(10, 10, 8, 10),
            margin = new RectOffset(0, 0, 0, 0)
        };

        sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
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
            fontStyle = FontStyle.Bold
        };

        noticeStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
        {
            normal = { textColor = new Color(0.82f, 0.88f, 0.96f, 1f) }
        };

        clipButtonStyle = new GUIStyle(EditorStyles.miniButton)
        {
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Normal
        };

        activeClipButtonStyle = new GUIStyle(clipButtonStyle)
        {
            fontStyle = FontStyle.Bold
        };
        activeClipButtonStyle.normal.textColor = new Color(0.72f, 0.9f, 1f, 1f);

        footerStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            normal = { textColor = new Color(0.68f, 0.72f, 0.78f, 1f) }
        };

        footerBrandStyle = new GUIStyle(footerStyle)
        {
            alignment = TextAnchor.MiddleRight,
            fontStyle = FontStyle.Bold
        };
        footerBrandStyle.normal.textColor = new Color(0.78f, 0.86f, 1f, 1f);

        dropZoneStyle = new GUIStyle(EditorStyles.helpBox)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            clipping = TextClipping.Clip,
            wordWrap = false,
            normal = { textColor = new Color(0.78f, 0.86f, 1f, 1f) }
        };

        axisLabelStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 11
        };

        centeredPreviewStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 13,
            normal = { textColor = new Color(0.72f, 0.78f, 0.86f, 1f) }
        };
    }

    private static string FormatTime(float seconds)
    {
        return seconds.ToString("0.00") + "초";
    }

    private struct ClipEntry
    {
        public readonly AnimationClip Clip;
        public readonly string Source;

        public ClipEntry(AnimationClip clip, string source)
        {
            Clip = clip;
            Source = source;
        }
    }

    private struct ViewPreset
    {
        public readonly string Label;
        public readonly float Yaw;
        public readonly float Pitch;

        public ViewPreset(string label, float yaw, float pitch)
        {
            Label = label;
            Yaw = yaw;
            Pitch = pitch;
        }
    }
}
