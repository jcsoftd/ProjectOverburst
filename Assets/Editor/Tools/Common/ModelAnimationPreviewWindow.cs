using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;

public sealed class ModelAnimationPreviewWindow : EditorWindow
{
    private const string WindowTitle = "캐릭터·무기 애니메이션 프리뷰";
    private const string MenuPath = "JC Tool/Animation/캐릭터·무기 애니메이션 프리뷰";
    private const string LegacyMenuPath = "JC Tool/Animation/모델 애니메이션 미리보기";
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
    private const float SidebarWidth = 346f;
    private const float PlaybackPanelHeight = 146f;
    private const float CameraPanelHeight = 128f;
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
    private static readonly Vector2 MinimumWindowSize = new Vector2(960f, 680f);

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
    private readonly List<SocketEntry> socketEntries = new List<SocketEntry>();
    private readonly List<SocketEntry> visibleSocketEntries = new List<SocketEntry>();
    private readonly List<PreviewConstraintBinding> previewConstraintBindings = new List<PreviewConstraintBinding>();

    [SerializeField] private GameObject selectedModelPrefab;
    [SerializeField] private GameObject selectedWeaponPrefab;
    [SerializeField] private AnimationClip activeClip;
    [SerializeField] private string selectedSocketPath = string.Empty;
    [SerializeField] private string socketFilter = string.Empty;
    [SerializeField] private WeaponPoseSlot selectedPoseSlot = WeaponPoseSlot.Hold;
    [SerializeField] private Vector3 previewWeaponPositionOffset;
    [SerializeField] private Vector3 previewWeaponRotationOffset;
    private GameObject previewInstance;
    private GameObject previewWeaponInstance;
    private WeaponPose previewWeaponPose;
    private WeaponGripMount previewWeaponGripMount;
    private Transform previewWeaponSocket;
    private bool backPoseAvailable = true;
    private Mesh floorMesh;
    private Matrix4x4 floorMatrix;
    private Material floorMaterial;
    private PreviewRenderUtility previewUtility;
    private Animator previewAnimator;
    private readonly List<Animator> secondaryAnimators = new List<Animator>();
    private PlayableGraph previewPlayableGraph;
    private AnimationClipPlayable previewClipPlayable;
    private readonly List<AnimationClipPlayable> secondaryClipPlayables = new List<AnimationClipPlayable>();
    private AnimationClip previewPlayableClip;
    private Renderer[] renderers = Array.Empty<Renderer>();
    private Vector3 frameCenter = Vector3.up;
    private float frameRadius = 1f;
    private float frameBottomY;
    private float cameraYaw = 180f;
    private float cameraPitch = 10f;
    private float cameraDistanceScale = 1f;
    private Vector3 cameraPanOffset;
    private bool focusWeapon;
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
    private bool inPlacePreview = true;
    private bool autoUseAnimationSelection = true;
    private double lastUpdateTime;
    private string filterText = string.Empty;
    private string previewMessage;
    private string attachmentMessage;
    private Vector2 clipScroll;
    private Vector2 sidebarScroll;
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

    [MenuItem(LegacyMenuPath)]
    private static void OpenLegacy()
    {
        Open();
    }

    private void OnEnable()
    {
        minSize = MinimumWindowSize;
        EnsurePreviewUtility();
        EditorApplication.update += TickPreview;
        ResetPreviewClock();
        if (selectedModelPrefab != null)
        {
            RebuildPreviewInstance();
            RefreshClipEntries();
            SampleActiveClip();
        }
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

        HandleKeyboardInput();
        DrawHeader();
        DrawWorkspace();
        DrawFooter();
    }

    private void DrawHeader()
    {
        Rect rect = GUILayoutUtility.GetRect(10f, HeaderHeight, GUILayout.ExpandWidth(true));
        Rect backgroundRect = new Rect(rect.x + 2f, rect.y + 4f, rect.width - 4f, rect.height - 8f);
        EditorGUI.DrawRect(backgroundRect, new Color(0.105f, 0.12f, 0.145f, 1f));

        GUI.Label(new Rect(backgroundRect.x + 14f, backgroundRect.y + 7f, backgroundRect.width - 28f, 24f), WindowTitle, headerTitleStyle);

        string modelName = selectedModelPrefab != null ? selectedModelPrefab.name : "모델 없음";
        string weaponName = selectedWeaponPrefab != null ? selectedWeaponPrefab.name : "무기 없음";
        string clipName = activeClip != null ? activeClip.name : "애니메이션 없음";
        GUI.Label(new Rect(backgroundRect.x + 14f, backgroundRect.y + 35f, backgroundRect.width - 28f, 18f), modelName + "  /  " + weaponName + "  /  " + clipName, headerMetaStyle);
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

    private void DrawWorkspace()
    {
        float workspaceHeight = Mathf.Max(480f, position.height - HeaderHeight - FooterHeight - 12f);
        float previewHeight = Mathf.Max(MinPreviewHeight, workspaceHeight - PlaybackPanelHeight - CameraPanelHeight - SectionGap * 2f);

        using (new EditorGUILayout.HorizontalScope(GUILayout.Height(workspaceHeight)))
        {
            GUILayout.Space(GridHorizontalMargin);
            sidebarScroll = EditorGUILayout.BeginScrollView(sidebarScroll, GUILayout.Width(SidebarWidth), GUILayout.Height(workspaceHeight));
            DrawSection("01  캐릭터", DrawModelPicker);
            GUILayout.Space(SectionGap);
            DrawSection("02  장착 무기", DrawWeaponPicker);
            GUILayout.Space(SectionGap);
            DrawSection("03  애니메이션", DrawAnimationPicker);
            EditorGUILayout.EndScrollView();

            GUILayout.Space(SectionGap);
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
            {
                DrawPreviewSection(previewHeight);
                GUILayout.Space(SectionGap);
                DrawFixedPanel("재생 · 타임라인", DrawPlaybackControls, PlaybackPanelHeight);
                GUILayout.Space(SectionGap);
                DrawFixedPanel("카메라", DrawViewButtons, CameraPanelHeight);
            }
            GUILayout.Space(GridHorizontalMargin);
        }
    }

    private void DrawFixedPanel(string title, Action drawBody, float height)
    {
        using (new EditorGUILayout.VerticalScope(sectionStyle, GUILayout.Height(height)))
        {
            EditorGUILayout.LabelField(title, sectionTitleStyle);
            EditorGUILayout.Space(4f);
            drawBody();
        }
    }

    private void DrawModelPicker()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("대상", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));

            EditorGUI.BeginChangeCheck();
            GameObject nextModel = (GameObject)EditorGUILayout.ObjectField(selectedModelPrefab, typeof(GameObject), true, GUILayout.Height(ControlHeight));
            if (EditorGUI.EndChangeCheck())
                SetModelPrefab(nextModel);

            GUILayout.Space(RowGap);

            if (GUILayout.Button("비우기", compactButtonStyle, GUILayout.Width(ShortButtonWidth), GUILayout.Height(ControlHeight)))
                SetModelPrefab(null);
        }

        if (GUILayout.Button("Project / Hierarchy 선택을 캐릭터로 사용", compactButtonStyle, GUILayout.Height(ControlHeight)))
        {
            if (Selection.activeGameObject != null && IsSupportedModel(Selection.activeGameObject))
                SetModelPrefab(Selection.activeGameObject);
        }

        DrawModelDropZone();
    }

    private void DrawWeaponPicker()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("프리팹", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));
            EditorGUI.BeginChangeCheck();
            GameObject nextWeapon = (GameObject)EditorGUILayout.ObjectField(selectedWeaponPrefab, typeof(GameObject), true, GUILayout.Height(ControlHeight));
            if (EditorGUI.EndChangeCheck())
                SetWeaponPrefab(nextWeapon);

            if (GUILayout.Button("비우기", compactButtonStyle, GUILayout.Width(ShortButtonWidth), GUILayout.Height(ControlHeight)))
                SetWeaponPrefab(null);
        }

        if (GUILayout.Button("Project / Hierarchy 선택을 무기로 사용", compactButtonStyle, GUILayout.Height(ControlHeight)))
        {
            if (Selection.activeGameObject != null && IsSupportedModel(Selection.activeGameObject))
                SetWeaponPrefab(Selection.activeGameObject);
        }

        if (selectedWeaponPrefab == null)
        {
            DrawInlineNotice("장착 프리팹을 지정하면 캐릭터와 함께 움직입니다.", true);
            return;
        }

        EditorGUILayout.Space(5f);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("소켓", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));
            DrawSocketPopup();
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("본 검색", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));
            socketFilter = EditorGUILayout.TextField(socketFilter, GUILayout.Height(ControlHeight));
        }

        if (previewWeaponSocket != null)
            DrawInlineNotice("연결: " + previewWeaponSocket.name, true);
        else if (selectedModelPrefab != null)
            DrawInlineNotice("소켓을 찾지 못했습니다. 본 검색 후 직접 선택하세요.", false);

        if (previewWeaponPose != null)
        {
            EditorGUILayout.Space(5f);
            GUILayout.Label("무기 포즈", sectionTitleStyle);
            string[] poseLabels = { "손", "등", "조준", "방어" };
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < poseLabels.Length; i++)
                {
                    EditorGUI.BeginDisabledGroup(i == (int)WeaponPoseSlot.Back && !backPoseAvailable);
                    Color previous = GUI.backgroundColor;
                    if (i == (int)selectedPoseSlot)
                        GUI.backgroundColor = new Color(0.55f, 0.72f, 0.96f, 1f);
                    if (GUILayout.Button(poseLabels[i], compactButtonStyle, GUILayout.ExpandWidth(true), GUILayout.Height(ControlHeight)))
                    {
                        selectedPoseSlot = (WeaponPoseSlot)i;
                        ApplyWeaponPose();
                        RefitCameraFromCurrentPose();
                        Repaint();
                    }
                    GUI.backgroundColor = previous;
                    EditorGUI.EndDisabledGroup();
                }
            }
            DrawInlineNotice("WeaponPose의 저장된 값을 미리보기 복제본에 적용합니다.", true);
            if (!backPoseAvailable)
                DrawInlineNotice("이 모델에는 BackWeaponAnchor가 없어 등 포즈를 사용할 수 없습니다.", false);
        }
        else if (previewWeaponInstance != null)
        {
            DrawInlineNotice(previewWeaponGripMount != null
                ? "WeaponGripMount 기준으로 손 소켓에 정렬했습니다."
                : "무기 루트를 손 소켓 원점에 장착했습니다.", true);
        }

        EditorGUILayout.Space(5f);
        EditorGUI.BeginChangeCheck();
        Vector3 nextPosition = EditorGUILayout.Vector3Field("미리보기 위치", previewWeaponPositionOffset);
        Vector3 nextRotation = EditorGUILayout.Vector3Field("미리보기 회전", previewWeaponRotationOffset);
        if (EditorGUI.EndChangeCheck())
        {
            previewWeaponPositionOffset = nextPosition;
            previewWeaponRotationOffset = nextRotation;
            ApplyWeaponPose();
            Repaint();
        }
        if (GUILayout.Button("임시 보정 초기화", compactButtonStyle, GUILayout.Height(ControlHeight)))
        {
            previewWeaponPositionOffset = Vector3.zero;
            previewWeaponRotationOffset = Vector3.zero;
            ApplyWeaponPose();
        }
        DrawInlineNotice("보정값은 이 창에서만 적용되며 원본 자산을 바꾸지 않습니다.", true);
    }

    private void DrawSocketPopup()
    {
        visibleSocketEntries.Clear();
        for (int i = 0; i < socketEntries.Count; i++)
        {
            SocketEntry entry = socketEntries[i];
            bool selected = entry.Path == selectedSocketPath;
            bool matches = string.IsNullOrWhiteSpace(socketFilter)
                ? IsLikelySocket(entry.Transform.name)
                : entry.Label.IndexOf(socketFilter, StringComparison.OrdinalIgnoreCase) >= 0;
            if (selected || matches)
                visibleSocketEntries.Add(entry);
        }

        string[] labels = new string[visibleSocketEntries.Count + 1];
        labels[0] = "자동";
        int currentIndex = 0;
        for (int i = 0; i < visibleSocketEntries.Count; i++)
        {
            labels[i + 1] = visibleSocketEntries[i].Label;
            if (visibleSocketEntries[i].Path == selectedSocketPath)
                currentIndex = i + 1;
        }

        EditorGUI.BeginChangeCheck();
        int nextIndex = EditorGUILayout.Popup(currentIndex, labels, GUILayout.Height(ControlHeight));
        if (EditorGUI.EndChangeCheck())
        {
            selectedSocketPath = nextIndex == 0 ? string.Empty : visibleSocketEntries[nextIndex - 1].Path;
            RebuildPreviewInstance();
            SampleActiveClip();
            Repaint();
        }
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
            if (GUILayout.Button("목록 새로고침", compactButtonStyle, GUILayout.Width(110f), GUILayout.Height(ControlHeight)))
                LoadModelAnimatorClips();
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(RowGap);
            GUILayout.Label(clipEntries.Count + "개", footerStyle, GUILayout.Height(ControlHeight));
        }
        autoUseAnimationSelection = EditorGUILayout.ToggleLeft("Project 애니메이션 파일 클릭 시 자동 재생", autoUseAnimationSelection);

        if (selectedModelPrefab == null)
        {
            DrawInlineNotice("모델을 지정하면 클립 목록을 자동으로 불러옵니다.", true);
            return;
        }

        if (clipEntries.Count == 0)
        {
            string message = "모델에서 클립을 찾지 못했습니다. Project의 애니메이션 파일을 선택할 수 있습니다.";
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
            if (GUILayout.Button(isPlaying ? "일시정지" : "재생", compactButtonStyle, GUILayout.Width(PlaybackButtonWidth), GUILayout.Height(ControlHeight)))
                TogglePlayback();
            GUI.backgroundColor = previousBackground;

            if (GUILayout.Button("처음부터", compactButtonStyle, GUILayout.Width(PlaybackButtonWidth), GUILayout.Height(ControlHeight)))
                RestartPlayback();

            EditorGUI.BeginDisabledGroup(activeClip == null);
            if (GUILayout.Button("◀ 1F", compactButtonStyle, GUILayout.Width(54f), GUILayout.Height(ControlHeight)))
                StepFrame(-1);
            if (GUILayout.Button("1F ▶", compactButtonStyle, GUILayout.Width(54f), GUILayout.Height(ControlHeight)))
                StepFrame(1);
            EditorGUI.EndDisabledGroup();

            loopPlayback = GUILayout.Toggle(loopPlayback, "반복", compactButtonStyle, GUILayout.Width(ShortButtonWidth), GUILayout.Height(ControlHeight));

            GUILayout.FlexibleSpace();

            EditorGUI.BeginChangeCheck();
            bool nextInPlace = EditorGUILayout.ToggleLeft("제자리", inPlacePreview, GUILayout.Width(70f), GUILayout.Height(ControlHeight));
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

        GUILayout.Label("Space 재생/일시정지   ← → 한 프레임   우클릭 회전   가운데 드래그 이동   휠 확대", footerStyle);
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
                focusWeapon = false;
                cameraPanOffset = Vector3.zero;
                cameraDistanceScale = 1f;
                RefitCameraFromCurrentPose();
                Repaint();
            }
            EditorGUI.BeginDisabledGroup(previewWeaponInstance == null);
            if (GUILayout.Button("무기", compactButtonStyle, GUILayout.Width(ViewButtonWidth), GUILayout.Height(ControlHeight)))
                FocusPreviewWeapon();
            EditorGUI.EndDisabledGroup();

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

    private void DrawPreviewSection(float previewSectionHeight)
    {
        using (new EditorGUILayout.VerticalScope(sectionStyle, GUILayout.ExpandWidth(true), GUILayout.Height(previewSectionHeight)))
        {
            EditorGUILayout.LabelField("장착 상태 미리보기", sectionTitleStyle);
            EditorGUILayout.Space(4f);

            Rect rect = GUILayoutUtility.GetRect(10f, Mathf.Max(1f, previewSectionHeight - 40f), GUILayout.ExpandWidth(true), GUILayout.Height(Mathf.Max(1f, previewSectionHeight - 40f)));
            EditorGUI.DrawRect(rect, new Color(0.08f, 0.085f, 0.095f, 1f));

            if (previewInstance == null)
            {
                DrawCenteredLabel(rect, "왼쪽에서 캐릭터 모델을 지정하세요.");
            }
            else
            {
                HandlePreviewCameraInput(rect);

                Texture texture = RenderPreviewTexture(rect);
                if (texture != null)
                    GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, false);

                DrawCameraAxisOverlay(rect);

                string message = !string.IsNullOrEmpty(previewMessage) ? previewMessage : attachmentMessage;
                if (!string.IsNullOrEmpty(message))
                    DrawPreviewMessage(rect, message);
            }
        }
    }

    private void DrawFooter()
    {
        Rect rect = GUILayoutUtility.GetRect(10f, 30f, GUILayout.ExpandWidth(true));
        Rect statusRect = new Rect(rect.x + 8f, rect.y + 3f, rect.width - 16f, rect.height - 6f);
        EditorGUI.DrawRect(statusRect, new Color(0.12f, 0.13f, 0.145f, 1f));

        string modelName = selectedModelPrefab != null ? selectedModelPrefab.name : "-";
        string weaponName = selectedWeaponPrefab != null ? selectedWeaponPrefab.name : "-";
        string clipName = activeClip != null ? activeClip.name : "-";
        Rect brandRect = new Rect(statusRect.xMax - FooterBrandWidth - 10f, statusRect.y + 3f, FooterBrandWidth, statusRect.height - 4f);
        Rect textRect = new Rect(statusRect.x + 10f, statusRect.y + 3f, Mathf.Max(1f, brandRect.x - statusRect.x - 18f), statusRect.height - 4f);
        GUI.Label(textRect, "모델 " + modelName + "   무기 " + weaponName + "   클립 " + clipName + "   목록 " + clipEntries.Count + "   Renderer " + renderers.Length, footerStyle);
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

        Rect messageRect = new Rect(rect.x + 12f, rect.y + 12f, Mathf.Min(rect.width - 24f, 420f), 42f);
        EditorGUI.DrawRect(messageRect, new Color(0.08f, 0.09f, 0.11f, 0.82f));
        GUI.Label(new Rect(messageRect.x + 8f, messageRect.y + 5f, messageRect.width - 16f, 32f), text, noticeStyle ?? EditorStyles.wordWrappedMiniLabel);
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
        selectedSocketPath = string.Empty;
        focusWeapon = false;
        cameraPanOffset = Vector3.zero;
        cameraDistanceScale = 1f;
        clipEntries.Clear();
        clipSet.Clear();
        playbackTime = 0f;
        isPlaying = false;
        previewMessage = null;

        if (activeClipWasModelClip)
            activeClip = null;

        RebuildPreviewInstance();
        if (selectedModelPrefab != null)
        {
            RefreshClipEntries();
        }

        if (activeClip != null)
            SampleActiveClip();

        RefitCameraFromCurrentPose();
        ResetPreviewClock();
        Repaint();
    }

    private void SetWeaponPrefab(GameObject prefab)
    {
        if (selectedWeaponPrefab == prefab)
            return;

        selectedWeaponPrefab = prefab;
        selectedPoseSlot = WeaponPoseSlot.Hold;
        focusWeapon = false;
        cameraPanOffset = Vector3.zero;
        cameraDistanceScale = 1f;
        previewWeaponPositionOffset = Vector3.zero;
        previewWeaponRotationOffset = Vector3.zero;
        RebuildPreviewInstance();
        SampleActiveClip();
        Repaint();
    }

    private void LoadModelAnimatorClips()
    {
        if (selectedModelPrefab == null)
            return;

        RefreshClipEntries();
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
            RebuildPreviewInstance();
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

        isPlaying = playImmediately && previewInstance != null;
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

    private void StepFrame(int direction)
    {
        if (activeClip == null)
            return;

        float frameRate = activeClip.frameRate > 0f ? activeClip.frameRate : 30f;
        playbackTime = Mathf.Clamp(playbackTime + direction / frameRate, 0f, GetActiveClipLength());
        isPlaying = false;
        SampleActiveClip();
        Repaint();
    }

    private void HandleKeyboardInput()
    {
        Event current = Event.current;
        if (current == null || current.type != EventType.KeyDown || EditorGUIUtility.editingTextField)
            return;

        if (current.keyCode == KeyCode.Space)
            TogglePlayback();
        else if (current.keyCode == KeyCode.LeftArrow)
            StepFrame(-1);
        else if (current.keyCode == KeyCode.RightArrow)
            StepFrame(1);
        else
            return;

        current.Use();
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
        if (previewInstance == null)
            return;

        if (activeClip != null)
            EvaluateAnimatorPreviewClip();
        ApplyPreviewConstraintBindings();
        ApplyWeaponPose();
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

            if (activeClip.humanMotion)
            {
                Animator[] animators = previewInstance.GetComponentsInChildren<Animator>(true);
                for (int i = 0; i < animators.Length; i++)
                {
                    Animator animator = animators[i];
                    if (animator == null || animator == previewAnimator || !animator.gameObject.activeInHierarchy
                        || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman
                        || (previewWeaponInstance != null && animator.transform.IsChildOf(previewWeaponInstance.transform)))
                        continue;

                    animator.enabled = true;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.applyRootMotion = false;
                    AnimationClipPlayable secondaryClip = AnimationClipPlayable.Create(previewPlayableGraph, activeClip);
                    secondaryClip.SetApplyFootIK(false);
                    secondaryClip.SetApplyPlayableIK(false);
                    secondaryClip.SetSpeed(0d);
                    AnimationPlayableOutput secondaryOutput = AnimationPlayableOutput.Create(previewPlayableGraph, "Character part " + i, animator);
                    secondaryOutput.SetSourcePlayable(secondaryClip);
                    secondaryAnimators.Add(animator);
                    secondaryClipPlayables.Add(secondaryClip);
                }
            }
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
            if (animator == null
                || (previewWeaponInstance != null && animator.transform.IsChildOf(previewWeaponInstance.transform)))
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
        secondaryAnimators.Clear();
        secondaryClipPlayables.Clear();
    }

    private void EvaluateClipAtTime(float time)
    {
        previewClipPlayable.SetTime(time);
        previewClipPlayable.SetSpeed(0d);
        for (int i = 0; i < secondaryClipPlayables.Count; i++)
        {
            secondaryClipPlayables[i].SetTime(time);
            secondaryClipPlayables[i].SetSpeed(0d);
        }
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
        previewMessage = null;

        if (selectedModelPrefab == null)
        {
            RefreshRendererCache();
            return;
        }

        try
        {
            EnsurePreviewUtility();

            previewInstance = Instantiate(selectedModelPrefab, previewUtility.camera.transform, false);
            previewInstance.transform.SetParent(null, false);
            previewInstance.name = "[JC Animation Preview] " + selectedModelPrefab.name;
            previewInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            previewInstance.transform.localScale = ResolvePreviewRootScale(selectedModelPrefab);
            previewRootAnchorPosition = previewInstance.transform.position;
            previewRootAnchorRotation = previewInstance.transform.rotation;
            previewInstance.SetActive(true);
            DisableRuntimeBehaviours(previewInstance);
            CapturePreviewConstraintBindings();
            RefreshSocketEntries();
            AttachPreviewWeapon();
            SetHideFlagsRecursive(previewInstance, HideFlags.HideAndDontSave);
            previewUtility.AddSingleGO(previewInstance);
            RefreshRendererCache();
            RefitCameraFromCurrentPose();
        }
        catch (Exception exception)
        {
            ClearPreviewInstance();
            previewMessage = "미리보기 구성 오류: " + exception.GetType().Name;
        }
    }

    private void AttachPreviewWeapon()
    {
        attachmentMessage = null;
        if (previewInstance == null || selectedWeaponPrefab == null)
            return;

        previewWeaponSocket = ResolvePreviewWeaponSocket();
        if (previewWeaponSocket == null)
        {
            attachmentMessage = string.IsNullOrEmpty(selectedSocketPath)
                ? "오른손 소켓을 찾지 못했습니다. 본 검색으로 직접 지정하세요."
                : "선택한 소켓이 모델에 없습니다. 자동 또는 다른 본을 지정하세요.";
            return;
        }

        previewWeaponInstance = Instantiate(selectedWeaponPrefab, previewWeaponSocket, false);
        previewWeaponInstance.name = "[JC Preview Weapon] " + selectedWeaponPrefab.name;
        previewWeaponInstance.transform.localPosition = Vector3.zero;
        previewWeaponInstance.transform.localRotation = Quaternion.identity;
        previewWeaponInstance.transform.localScale = ResolvePreviewRootScale(selectedWeaponPrefab);
        previewWeaponInstance.SetActive(true);
        DisableRuntimeBehaviours(previewWeaponInstance);

        previewWeaponPose = previewWeaponInstance.GetComponentInChildren<WeaponPose>(true);
        previewWeaponGripMount = previewWeaponInstance.GetComponentInChildren<WeaponGripMount>(true);
        if (previewWeaponPose != null)
            ConfigurePreviewBackPose();
        ApplyWeaponPose();
    }

    private void ApplyWeaponPose()
    {
        if (previewWeaponInstance == null)
            return;

        try
        {
            Transform target = previewWeaponInstance.transform;
            if (previewWeaponPose != null)
            {
                previewWeaponPose.PreviewPoseInstant(selectedPoseSlot);
                target = previewWeaponPose.GetPoseTargetForTuning();
            }
            else
            {
                target.localPosition = Vector3.zero;
                target.localRotation = Quaternion.identity;
                if (previewWeaponGripMount != null
                    && previewWeaponGripMount.TryResolveRootLocalPose(
                        target, WeaponGripAnchor.RightHand, Vector3.zero, Quaternion.identity,
                        out Vector3 rootPosition, out Quaternion rootRotation))
                {
                    target.localPosition = rootPosition;
                    target.localRotation = rootRotation;
                }
            }

            if (target == null)
                return;

            target.localPosition += previewWeaponPositionOffset;
            target.localRotation = Quaternion.Euler(previewWeaponRotationOffset) * target.localRotation;
            if (attachmentMessage != null && attachmentMessage.StartsWith("무기 자세 오류:", StringComparison.Ordinal))
                attachmentMessage = null;
        }
        catch (Exception exception)
        {
            isPlaying = false;
            attachmentMessage = "무기 자세 오류: " + exception.GetType().Name;
        }
    }

    private void ConfigurePreviewBackPose()
    {
        if (previewInstance == null || previewWeaponPose == null)
            return;

        PlayerMovement movement = previewWeaponPose.GetComponentInParent<PlayerMovement>();
        Transform anchorRoot = movement != null ? movement.transform : previewInstance.transform;
        SerializedObject serializedPose = new SerializedObject(previewWeaponPose);
        SerializedProperty useBack = serializedPose.FindProperty("useBackFloatingPose");
        backPoseAvailable = useBack == null || !useBack.boolValue || anchorRoot.Find("BackWeaponAnchor") != null;
        if (backPoseAvailable)
            return;

        useBack.boolValue = false;
        serializedPose.ApplyModifiedPropertiesWithoutUndo();
        if (selectedPoseSlot == WeaponPoseSlot.Back)
            selectedPoseSlot = WeaponPoseSlot.Hold;
    }

    private void RefreshSocketEntries()
    {
        socketEntries.Clear();
        if (previewInstance == null)
            return;

        Transform[] transforms = previewInstance.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == previewInstance.transform)
                continue;

            string path = GetSocketPath(candidate);
            socketEntries.Add(new SocketEntry(path, GetSocketLabel(previewInstance.transform, candidate), candidate));
        }
    }

    private Transform ResolvePreviewWeaponSocket()
    {
        if (previewInstance == null)
            return null;

        if (!string.IsNullOrEmpty(selectedSocketPath))
        {
            for (int i = 0; i < socketEntries.Count; i++)
            {
                if (socketEntries[i].Path == selectedSocketPath)
                    return socketEntries[i].Transform;
            }
            return null;
        }

        MonoBehaviour[] behaviours = previewInstance.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (!(behaviours[i] is ICharacterWeaponSocketProvider provider))
                continue;

            try
            {
                Transform socket = provider.GetWeaponSocket(null);
                if (socket != null && socket.IsChildOf(previewInstance.transform))
                    return socket;
            }
            catch (Exception)
            {
                // A preview must keep working when a project-specific provider needs play state.
            }
        }

        Transform named = FindDeepChild(previewInstance.transform, P09CharacterVisualAdapter.RightHandWeaponSocketName);
        if (named != null)
            return named;

        Animator animator = previewInstance.GetComponentInChildren<Animator>(true);
        if (animator != null && animator.isHuman)
        {
            Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand != null)
                return hand;
        }

        string[] fallbackNames = { "RightHand", "Right Hand", "Hand_R", "hand_r", "R_Hand" };
        for (int i = 0; i < fallbackNames.Length; i++)
        {
            named = FindDeepChild(previewInstance.transform, fallbackNames[i]);
            if (named != null)
                return named;
        }
        return null;
    }

    private static Transform FindDeepChild(Transform root, string childName)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i] != null && transforms[i].name == childName)
                return transforms[i];
        }
        return null;
    }

    private static string GetSocketPath(Transform transform)
    {
        if (transform.parent == null)
            return transform.name;
        return GetSocketPath(transform.parent) + "/" + transform.GetSiblingIndex() + ":" + transform.name;
    }

    private static string GetSocketLabel(Transform root, Transform candidate)
    {
        List<string> names = new List<string>();
        Transform current = candidate;
        while (current != null && current != root)
        {
            names.Add(current.name);
            current = current.parent;
        }
        names.Reverse();
        return string.Join(" / ", names);
    }

    private static bool IsLikelySocket(string name)
    {
        return name.IndexOf("hand", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("weapon", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("socket", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("grip", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("wrist", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void ClearPreviewInstance()
    {
        DestroyAnimatorPreviewGraph();
        ClearPreviewFloor();
        previewConstraintBindings.Clear();

        if (previewInstance != null)
        {
            DestroyImmediate(previewInstance);
            previewInstance = null;
        }

        previewWeaponInstance = null;
        previewWeaponPose = null;
        previewWeaponGripMount = null;
        previewWeaponSocket = null;
        backPoseAvailable = true;
        attachmentMessage = null;
        socketEntries.Clear();
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
        {
            if (behaviours[i] != null)
                behaviours[i].enabled = false;
        }
    }

    private void CapturePreviewConstraintBindings()
    {
        previewConstraintBindings.Clear();
        if (previewInstance == null)
            return;

        // The preview scene is sampled manually, so Unity's regular constraint update never runs.
        // Capture the rest relationship before the first animation sample, then follow the source bone.
        ParentConstraint[] constraints = previewInstance.GetComponentsInChildren<ParentConstraint>(true);
        for (int i = 0; i < constraints.Length; i++)
        {
            ParentConstraint constraint = constraints[i];
            if (!constraint.gameObject.activeInHierarchy || !constraint.enabled || !constraint.constraintActive
                || constraint.sourceCount != 1 || !Mathf.Approximately(constraint.weight, 1f))
                continue;

            ConstraintSource source = constraint.GetSource(0);
            if (source.sourceTransform == null || !Mathf.Approximately(source.weight, 1f)
                || !source.sourceTransform.IsChildOf(previewInstance.transform))
                continue;

            Transform sourceTransform = source.sourceTransform;
            Transform targetTransform = constraint.transform;
            previewConstraintBindings.Add(new PreviewConstraintBinding(
                sourceTransform, targetTransform,
                sourceTransform.InverseTransformPoint(targetTransform.position),
                Quaternion.Inverse(sourceTransform.rotation) * targetTransform.rotation));
            constraint.enabled = false;
        }
    }

    private void ApplyPreviewConstraintBindings()
    {
        for (int i = 0; i < previewConstraintBindings.Count; i++)
        {
            PreviewConstraintBinding binding = previewConstraintBindings[i];
            if (binding.Source == null || binding.Target == null)
                continue;

            binding.Target.SetPositionAndRotation(
                binding.Source.TransformPoint(binding.LocalPosition),
                binding.Source.rotation * binding.LocalRotation);
        }
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
        if (focusWeapon && TryResolveWeaponRendererBounds(out Bounds weaponBounds))
        {
            frameCenter = weaponBounds.center;
            frameRadius = Mathf.Max(0.35f, weaponBounds.extents.magnitude);
        }
        UpdatePreviewCameraTransform();
    }

    private bool TryResolvePreviewRendererBounds(out Bounds bounds)
    {
        return TryResolveRendererBounds(renderers, out bounds);
    }

    private bool TryResolveWeaponRendererBounds(out Bounds bounds)
    {
        Renderer[] weaponRenderers = previewWeaponInstance != null
            ? previewWeaponInstance.GetComponentsInChildren<Renderer>(true)
            : Array.Empty<Renderer>();
        return TryResolveRendererBounds(weaponRenderers, out bounds);
    }

    private static bool TryResolveRendererBounds(Renderer[] candidates, out Bounds bounds)
    {
        bounds = default;

        if (candidates.Length == 0)
            return false;

        bool hasBounds = false;
        for (int i = 0; i < candidates.Length; i++)
        {
            Renderer renderer = candidates[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
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

    private void FocusPreviewWeapon()
    {
        if (!TryResolveWeaponRendererBounds(out Bounds bounds))
            return;

        focusWeapon = true;
        cameraPanOffset = Vector3.zero;
        cameraDistanceScale = 1f;
        frameCenter = bounds.center;
        frameRadius = Mathf.Max(0.35f, bounds.extents.magnitude);
        UpdatePreviewCameraTransform();
        Repaint();
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
            if (floorMesh != null && floorMaterial != null)
                previewUtility.DrawMesh(floorMesh, floorMatrix, floorMaterial, 0);
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
        if (previewUtility == null || previewInstance == null)
            return;

        if (floorMesh == null)
        {
            floorMesh = new Mesh
            {
                name = PreviewFloorName,
                hideFlags = HideFlags.HideAndDontSave,
                vertices = new[]
                {
                    new Vector3(-0.5f, 0f, -0.5f),
                    new Vector3(-0.5f, 0f, 0.5f),
                    new Vector3(0.5f, 0f, 0.5f),
                    new Vector3(0.5f, 0f, -0.5f)
                },
                normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up },
                uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            floorMesh.RecalculateBounds();
        }
        if (floorMaterial == null)
            floorMaterial = CreatePreviewFloorMaterial();
        float floorSize = Mathf.Max(MinFloorSize, frameRadius * FloorPaddingMultiplier);
        floorMatrix = Matrix4x4.TRS(
            new Vector3(frameCenter.x, frameBottomY - FloorYOffset, frameCenter.z),
            Quaternion.identity,
            new Vector3(floorSize, 1f, floorSize));
    }

    private void ClearPreviewFloor()
    {
        if (floorMesh != null)
        {
            DestroyImmediate(floorMesh);
            floorMesh = null;
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

        if (current.type == EventType.MouseDrag && current.button == 2 && previewUtility != null)
        {
            CancelCameraBlend();
            float distance = Mathf.Max(1.5f, frameRadius * CameraFitPadding * cameraDistanceScale * 2.2f);
            float unitsPerPixel = distance * 1.5f / Mathf.Max(1f, rect.height);
            cameraPanOffset += (-previewUtility.camera.transform.right * current.delta.x
                + previewUtility.camera.transform.up * current.delta.y) * unitsPerPixel;
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
        Vector3 cameraTarget = frameCenter + Vector3.up * (frameRadius * CameraTargetYOffsetFactor) + cameraPanOffset;
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

    private struct PreviewConstraintBinding
    {
        public readonly Transform Source;
        public readonly Transform Target;
        public readonly Vector3 LocalPosition;
        public readonly Quaternion LocalRotation;

        public PreviewConstraintBinding(Transform source, Transform target, Vector3 localPosition, Quaternion localRotation)
        {
            Source = source;
            Target = target;
            LocalPosition = localPosition;
            LocalRotation = localRotation;
        }
    }

    private struct SocketEntry
    {
        public readonly string Path;
        public readonly string Label;
        public readonly Transform Transform;

        public SocketEntry(string path, string label, Transform transform)
        {
            Path = path;
            Label = label;
            Transform = transform;
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
