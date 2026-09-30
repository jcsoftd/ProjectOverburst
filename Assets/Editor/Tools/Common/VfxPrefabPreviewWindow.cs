using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public sealed class VfxPrefabPreviewWindow : EditorWindow
{
    private const string WindowTitle = "VFX 프리팹 미리보기";
    private const string MenuPath = "JC Tool/VFX/VFX 프리팹 미리보기";
    private const string FooterBrandText = "JC Soft";
    private const string PreviewInstancePrefix = "[JC VFX Preview] ";
    private const string PreviewFloorName = "[JC VFX Preview Floor]";
    private const string PreviewFloorMaterialName = "[JC VFX Preview Floor Material]";
    private const string LegacyMirrorCameraName = "[JC VFX Preview Mirror Camera]";
    private const string LegacyPreviewCameraName = "[JC VFX Preview Camera]";
    private const string LegacyPreviewLightName = "[JC VFX Preview Light]";
    private const float ControlHeight = 24f;
    private const float FieldLabelWidth = 64f;
    private const float HeightLabelWidth = 14f;
    private const float HeightFieldWidth = 48f;
    private const float ActionButtonWidth = 76f; // 주요 조작 버튼 공통 폭
    private const float CameraOptionButtonWidth = 52f; // 카메라 6열 공통 폭
    private const float PlaybackSpeedButtonWidth = ActionButtonWidth * 3f / 4f; // 제어 3열과 같은 전체 폭
    private const float RowGap = 6f;
    private const float SectionGap = 8f;
    private const float HeaderHeight = 62f;
    private const float EnvironmentLabelWidth = 38f;
    private const float EnvironmentButtonWidth = 54f;
    private const float EnvironmentButtonGap = 2f;
    private const float FooterHeight = 30f;
    private const float FooterBrandWidth = 72f;
    private const float GridHorizontalMargin = 8f;
    private const float CameraSectionWidth = 404f;
    private const float PlaybackSectionMinWidth = 420f;
    private const float ViewPlaybackSectionHeight = 160f; // 재생 칸 4줄(제어·배속·시간·이동)
    private const float MinPreviewSectionHeight = 140f;
    private const float PreviewSectionInnerOffset = 40f;
    private const float PreviewLayoutSlack = 16f;
    private const float InitialVisiblePreviewTime = 0.08f;
    private const float FirstVisiblePreviewScanMaxTime = 2f;
    private const float FirstVisiblePreviewScanStep = 0.05f;
    private const float DefaultLoopPreviewDuration = 3f;
    private const float MinLoopPreviewDuration = 1f;
    private const float MaxLoopPreviewDuration = 8f;
    private const float MaxOneShotScanDuration = 30f;
    private const int OneShotDurationScanSamples = 160;
    private const float MinCameraSize = 1.5f;
    private const float CameraFitPadding = 1.35f;
    private const float DefaultCameraDistanceScale = 0.85f;
    private const float MinCameraDistanceScale = 0.08f;
    private const float MaxCameraDistanceScale = 12f;
    private const float CameraZoomSensitivity = 0.1f;
    private const float FloorPaddingMultiplier = 3.5f;
    private const float MinFloorSize = 4f;
    private const float FloorYOffset = 0.15f;
    private const float OrbitSensitivity = 0.35f;
    private const float CameraBlendDuration = 0.28f;
    private const float AxisOverlaySize = 86f;
    private const float AxisOverlayPadding = 12f;
    private const float AxisOverlayLineLength = 26f;
    private const float MinCameraPitch = -85f;
    private const float MaxCameraPitch = 85f;
    private const double PlayingPreviewTickInterval = 1d / 60d;
    private const float MotionPathPointSpacing = 0.05f; // 드래그 경로 기록 간격(m)
    private const int MotionPathMaxPoints = 2048;
    private const float MotionDefaultReplaySpeed = 6f; // 왕복 기본 속도(m/s)
    private const float MinMotionReplaySpeed = 0.2f;
    private const float MaxMotionReplaySpeed = 60f;
    private const float MinMotionReplayPathLength = 0.1f;
    private const float MaxMotionOffset = 200f;
    private const float MotionTurnSharpness = 18f; // 진행 방향으로 도는 빠르기
    private const float MotionToggleWidth = 64f;
    private const int MaxTrailProxyPoints = 512;
    private static readonly Vector2 MinimumWindowSize = new Vector2(860f, 480f);

    private static readonly ViewPreset[] ViewPresets =
    {
        new ViewPreset("앞", new Vector3(0f, 0f, -1f)),
        new ViewPreset("뒤", new Vector3(0f, 0f, 1f)),
        new ViewPreset("좌", new Vector3(-1f, 0f, 0f)),
        new ViewPreset("우", new Vector3(1f, 0f, 0f)),
        new ViewPreset("상", new Vector3(0f, 1f, 0f)),
        new ViewPreset("하", new Vector3(0f, -1f, 0f)),
        new ViewPreset("좌상", new Vector3(-1f, 1f, -1f)),
        new ViewPreset("우상", new Vector3(1f, 1f, -1f)),
        new ViewPreset("좌하", new Vector3(-1f, -1f, -1f)),
        new ViewPreset("우하", new Vector3(1f, -1f, -1f))
    };

    private static readonly PreviewEnvironmentPreset[] EnvironmentPresets =
    {
        new PreviewEnvironmentPreset("스튜디오", new Color(0.235f, 0.255f, 0.29f, 1f), new Color(0.43f, 0.45f, 0.48f, 1f), new Color(0.56f, 0.59f, 0.66f, 1f), new Color(1f, 0.96f, 0.9f, 1f), new Color(0.58f, 0.7f, 1f, 1f), 2.2f, 1.35f),
        new PreviewEnvironmentPreset("다크", new Color(0.025f, 0.03f, 0.045f, 1f), new Color(0.09f, 0.1f, 0.13f, 1f), new Color(0.24f, 0.27f, 0.34f, 1f), new Color(1f, 0.9f, 0.78f, 1f), new Color(0.36f, 0.55f, 1f, 1f), 1.9f, 1.05f),
        new PreviewEnvironmentPreset("라이트", new Color(0.72f, 0.74f, 0.77f, 1f), new Color(0.56f, 0.58f, 0.61f, 1f), new Color(0.78f, 0.8f, 0.84f, 1f), new Color(1f, 0.98f, 0.94f, 1f), new Color(0.72f, 0.82f, 1f, 1f), 1.65f, 0.8f),
        new PreviewEnvironmentPreset("게임", new Color(0.065f, 0.085f, 0.125f, 1f), new Color(0.15f, 0.18f, 0.24f, 1f), new Color(0.38f, 0.43f, 0.54f, 1f), new Color(1f, 0.84f, 0.62f, 1f), new Color(0.32f, 0.58f, 1f, 1f), 2.6f, 1.55f)
    };

    private GameObject selectedPrefab;
    private GameObject previewInstance;
    private GameObject floorInstance;
    private Material floorMaterial;
    private bool showPreviewFloor = true;
    private PreviewRenderUtility previewUtility;
    private ParticleSystem[] particleSystems = Array.Empty<ParticleSystem>();
    private ParticleSystem[] rootParticleSystems = Array.Empty<ParticleSystem>();
    private ParticleSystem.Particle[] particleBuffer = Array.Empty<ParticleSystem.Particle>();
    private Renderer[] renderers = Array.Empty<Renderer>();
    private Vector3 previewCenter;
    private float previewRadius = 1f;
    private float previewHeightOffset;
    private Vector3 frameCenter;
    private float frameRadius = 1f;
    private float cameraYaw = 180f;
    private float cameraPitch;
    private float cameraDistanceScale = DefaultCameraDistanceScale;
    private bool cameraBlendActive;
    private float cameraBlendStartYaw;
    private float cameraBlendStartPitch;
    private float cameraBlendStartDistanceScale;
    private float cameraBlendTargetYaw;
    private float cameraBlendTargetPitch;
    private float cameraBlendTargetDistanceScale = DefaultCameraDistanceScale;
    private double cameraBlendStartTime;
    private string previewMessage;
    private int viewIndex;
    private int previewEnvironmentIndex;
    private int liveParticleCount;
    private int visibleParticleCount;
    private float playbackTime;
    private float playbackDuration = 3f;
    private float playbackSpeed = 1f;
    private bool isPlaying = true;
    private bool loopPlayback = true;
    private bool hasLoopingParticles;
    private bool restartSimulationOnNextAdvance = true;
    private double lastUpdateTime;
    private double lastRepaintTime;

    // 드래그 이동 미리보기: VFX 루트를 원점에서 옮긴 양과 그린 경로(원점 기준 오프셋)
    private static readonly int MotionDragControlHint = "JcVfxPreviewMotionDrag".GetHashCode();
    private Vector3 motionOffset;
    private Vector3 motionLastTickOffset;
    private Quaternion motionRotation = Quaternion.identity;
    private bool motionDragging;
    private Plane motionDragPlane;
    private Vector3 motionDragGrabOffset;
    private float motionClock; // 배속이 반영된 미리보기 시계. 트레일 수명 계산에 씀
    private bool motionReplay = true;
    private bool motionFaceDirection = true;
    private float motionReplaySpeed = MotionDefaultReplaySpeed;
    private float motionReplayDistance;
    private readonly List<Vector3> motionPath = new List<Vector3>();
    private readonly List<float> motionPathCumulative = new List<float>();
    private float motionPathLength;
    private readonly List<TrailProxy> trailProxies = new List<TrailProxy>();
    private Vector3[] motionPathScreenBuffer = Array.Empty<Vector3>();

    private GUIStyle headerTitleStyle;
    private GUIStyle headerMetaStyle;
    private GUIStyle sectionStyle;
    private GUIStyle sectionTitleStyle;
    private GUIStyle fieldLabelStyle;
    private GUIStyle compactButtonStyle;
    private GUIStyle footerStyle;
    private GUIStyle footerDiagnosticsStyle;
    private GUIStyle footerBrandStyle;
    private GUIStyle axisLabelStyle;
    private GUIStyle previewOverlayStyle;

    [MenuItem(MenuPath)]
    private static void Open()
    {
        VfxPrefabPreviewWindow window = GetWindow<VfxPrefabPreviewWindow>(WindowTitle);
        window.minSize = MinimumWindowSize;
        window.TryUseSelection();
    }

    private void OnEnable()
    {
        minSize = MinimumWindowSize;
        DestroyPreviousPreviewInstances();
        EnsurePreviewUtility();
        EditorApplication.update += TickPreview;
        ResetPreviewClock();
        TryUseSelection();
    }

    private void OnDisable()
    {
        EditorApplication.update -= TickPreview;
        ClearPreviewInstance();
        CleanupPreviewUtility();
    }

    private void OnSelectionChange()
    {
        TryUseSelection();
        Repaint();
    }

    private void OnLostFocus()
    {
        if (!motionDragging)
            return;

        GUIUtility.hotControl = 0;
        EndMotionDrag();
        Repaint();
    }

    private void OnGUI()
    {
        EnsureStyles();

        DrawHeader();
        DrawViewPlaybackSections();
        DrawPreviewSection();
        DrawFooter();
    }

    private void DrawHeader()
    {
        Rect rect = GUILayoutUtility.GetRect(10f, HeaderHeight, GUILayout.ExpandWidth(true));
        Rect backgroundRect = new Rect(rect.x + 2f, rect.y + 4f, rect.width - 4f, rect.height - 8f);
        EditorGUI.DrawRect(backgroundRect, new Color(0.105f, 0.12f, 0.145f, 1f));

        float selectorWidth = EnvironmentLabelWidth
            + EnvironmentPresets.Length * EnvironmentButtonWidth
            + (EnvironmentPresets.Length - 1) * EnvironmentButtonGap;
        float selectorX = backgroundRect.xMax - selectorWidth - 14f;
        float titleWidth = Mathf.Max(160f, selectorX - backgroundRect.x - 24f);
        GUI.Label(new Rect(backgroundRect.x + 14f, backgroundRect.y + 7f, titleWidth, 24f), WindowTitle, headerTitleStyle);

        string status = selectedPrefab != null
            ? selectedPrefab.name + (isPlaying ? "  ·  재생 중" : "  ·  일시 정지")
            : "프리팹을 선택해 미리보기를 시작하세요";
        GUI.Label(new Rect(backgroundRect.x + 14f, backgroundRect.y + 35f, titleWidth, 18f), status, headerMetaStyle);
        DrawEnvironmentSelector(new Rect(selectorX, backgroundRect.y + 15f, selectorWidth, ControlHeight));
    }

    private void DrawEnvironmentSelector(Rect rect)
    {
        GUI.Label(new Rect(rect.x, rect.y, EnvironmentLabelWidth, rect.height), "환경", headerMetaStyle);
        float buttonX = rect.x + EnvironmentLabelWidth;

        for (int i = 0; i < EnvironmentPresets.Length; i++)
        {
            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = i == previewEnvironmentIndex ? new Color(0.48f, 0.7f, 1f, 1f) : Color.white;

            Rect buttonRect = new Rect(buttonX, rect.y, EnvironmentButtonWidth, rect.height);
            if (GUI.Button(buttonRect, EnvironmentPresets[i].Label, compactButtonStyle))
            {
                previewEnvironmentIndex = i;
                ApplyPreviewEnvironment();
                Repaint();
            }

            GUI.backgroundColor = previousBackground;
            buttonX += EnvironmentButtonWidth + EnvironmentButtonGap;
        }
    }

    private void DrawSectionContent(string title, Action drawBody, params GUILayoutOption[] options)
    {
        using (new EditorGUILayout.VerticalScope(sectionStyle, options))
        {
            DrawControlSectionHeader(title);
            EditorGUILayout.Space(2f);
            drawBody();
        }
    }

    private void DrawControlSectionHeader(string title)
    {
        Rect rect = GUILayoutUtility.GetRect(10f, 18f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(new Rect(rect.x, rect.y + 2f, 3f, 14f), new Color(0.38f, 0.66f, 1f, 1f));
        GUI.Label(new Rect(rect.x + 9f, rect.y, rect.width - 9f, rect.height), title, sectionTitleStyle);
    }

    private void DrawViewPlaybackSections()
    {
        float availableWidth = Mathf.Max(0f, position.width - GridHorizontalMargin * 2f - SectionGap);
        float cameraWidth = ResolveCameraSectionWidth(availableWidth);
        float playbackWidth = Mathf.Max(PlaybackSectionMinWidth, availableWidth - cameraWidth);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(GridHorizontalMargin);

            GUILayoutOption[] cameraOptions =
            {
                GUILayout.Width(cameraWidth),
                GUILayout.Height(ViewPlaybackSectionHeight)
            };

            GUILayoutOption[] playbackOptions =
            {
                GUILayout.MinWidth(PlaybackSectionMinWidth),
                GUILayout.Width(playbackWidth),
                GUILayout.Height(ViewPlaybackSectionHeight)
            };

            DrawSectionContent("대상 · 카메라", DrawCameraControls, cameraOptions);
            GUILayout.Space(SectionGap);
            DrawSectionContent("재생", DrawPlaybackControls, playbackOptions);
            GUILayout.Space(GridHorizontalMargin);
        }

        GUILayout.Space(SectionGap);
    }

    private static float ResolveCameraSectionWidth(float availableWidth)
    {
        float maxCameraWidth = Mathf.Max(0f, availableWidth - PlaybackSectionMinWidth);
        return Mathf.Clamp(CameraSectionWidth, 0f, maxCameraWidth);
    }

    private void DrawPrefabPicker()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("대상", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));

            EditorGUI.BeginChangeCheck();
            GameObject nextPrefab = (GameObject)EditorGUILayout.ObjectField(selectedPrefab, typeof(GameObject), false, GUILayout.Height(ControlHeight));
            if (EditorGUI.EndChangeCheck())
                SetPrefab(IsPrefabAsset(nextPrefab) ? nextPrefab : null);

            GUILayout.Space(RowGap);

            GUILayout.Label(new GUIContent("Y", "미리보기 VFX 루트 높이"), fieldLabelStyle, GUILayout.Width(HeightLabelWidth));
            EditorGUI.BeginChangeCheck();
            float nextHeightOffset = EditorGUILayout.FloatField(previewHeightOffset, GUILayout.Width(HeightFieldWidth), GUILayout.Height(ControlHeight));
            if (EditorGUI.EndChangeCheck())
                SetPreviewHeightOffset(nextHeightOffset);

            GUILayout.Space(RowGap);

            if (GUILayout.Button("선택 사용", compactButtonStyle, GUILayout.Width(ActionButtonWidth), GUILayout.Height(ControlHeight)))
                TryUseSelection();
        }
    }

    private void DrawCameraControls()
    {
        DrawPrefabPicker();
        EditorGUILayout.Space(4f);
        DrawViewButtons();
    }

    private void DrawViewButtons()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("방향", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));
            DrawViewButton(0);
            DrawViewButton(1);
            DrawViewButton(2);
            DrawViewButton(3);
            DrawViewButton(4);
            DrawViewButton(5);
            GUILayout.FlexibleSpace();
        }

        EditorGUILayout.Space(4f);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(FieldLabelWidth);
            DrawViewButton(6);
            DrawViewButton(7);
            DrawViewButton(8);
            DrawViewButton(9);
            bool nextShowPreviewFloor = GUILayout.Toggle(showPreviewFloor, "바닥", compactButtonStyle, GUILayout.Width(CameraOptionButtonWidth), GUILayout.Height(ControlHeight));
            if (nextShowPreviewFloor != showPreviewFloor)
            {
                showPreviewFloor = nextShowPreviewFloor;
                if (showPreviewFloor)
                    RebuildPreviewFloor();
                else
                    ClearPreviewFloor();

                Repaint();
            }

            if (GUILayout.Button("맞춤", compactButtonStyle, GUILayout.Width(CameraOptionButtonWidth), GUILayout.Height(ControlHeight)))
            {
                SetCameraToPreset(viewIndex, true);
                Repaint();
            }

            GUILayout.FlexibleSpace();
        }
    }

    private void DrawPlaybackControls()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("제어", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));

            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = isPlaying ? new Color(1f, 0.78f, 0.45f, 1f) : new Color(0.56f, 0.84f, 0.62f, 1f);
            if (GUILayout.Button(isPlaying ? "정지" : "재생", compactButtonStyle, GUILayout.Width(ActionButtonWidth), GUILayout.Height(ControlHeight)))
            {
                isPlaying = !isPlaying;
                ResetPreviewClock();
            }
            GUI.backgroundColor = previousBackground;

            if (GUILayout.Button("다시 재생", compactButtonStyle, GUILayout.Width(ActionButtonWidth), GUILayout.Height(ControlHeight)))
            {
                RestartParticlePreview(true);
                Repaint();
            }

            loopPlayback = GUILayout.Toggle(loopPlayback, "반복", compactButtonStyle, GUILayout.Width(ActionButtonWidth), GUILayout.Height(ControlHeight));
            GUILayout.FlexibleSpace();
        }

        EditorGUILayout.Space(4f);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("배속", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));
            DrawPlaybackSpeedButton("¼×", 0.25f);
            DrawPlaybackSpeedButton("½×", 0.5f);
            DrawPlaybackSpeedButton("1×", 1f);
            DrawPlaybackSpeedButton("2×", 2f);
            GUILayout.FlexibleSpace();

            string durationText = hasLoopingParticles
                ? "반복 · " + playbackDuration.ToString("0.00") + "초 순환"
                : "길이 " + playbackDuration.ToString("0.00") + "초";
            GUILayout.Label(durationText, footerStyle, GUILayout.Width(112f), GUILayout.Height(ControlHeight));
        }

        EditorGUILayout.Space(4f);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("시간", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));
            EditorGUI.BeginChangeCheck();
            float nextPlaybackTime = GUILayout.HorizontalSlider(playbackTime, 0f, playbackDuration, GUILayout.ExpandWidth(true), GUILayout.Height(ControlHeight));
            if (EditorGUI.EndChangeCheck())
            {
                playbackTime = nextPlaybackTime;
                ScrubParticlesToTime(playbackTime);
                ResetPreviewClock();
                Repaint();
            }

            GUILayout.Space(RowGap);
            GUILayout.Label(playbackTime.ToString("0.00") + " / " + playbackDuration.ToString("0.00") + "초", footerStyle, GUILayout.Width(112f), GUILayout.Height(ControlHeight));
        }

        EditorGUILayout.Space(4f);
        DrawMotionControls();
    }

    private void DrawMotionControls()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(new GUIContent("이동", "미리보기 칸에서 좌클릭 드래그로 VFX를 옮깁니다."), fieldLabelStyle, GUILayout.Width(FieldLabelWidth));

            if (GUILayout.Button(new GUIContent("원점", "VFX를 원점으로 되돌리고 그린 경로를 지웁니다."), compactButtonStyle, GUILayout.Width(CameraOptionButtonWidth), GUILayout.Height(ControlHeight)))
            {
                ResetMotion();
                Repaint();
            }

            bool nextReplay = GUILayout.Toggle(motionReplay, new GUIContent("경로 재생", "드래그를 놓으면 그린 경로를 따라 처음부터 반복해서 날립니다."), compactButtonStyle, GUILayout.Width(MotionToggleWidth), GUILayout.Height(ControlHeight));
            if (nextReplay != motionReplay)
            {
                motionReplay = nextReplay;
                if (HasReplayablePath())
                    RestartMotionReplayPass();
                Repaint();
            }

            bool nextFaceDirection = GUILayout.Toggle(motionFaceDirection, new GUIContent("진행 방향", "움직이는 방향으로 VFX의 앞(+Z)을 돌립니다."), compactButtonStyle, GUILayout.Width(MotionToggleWidth), GUILayout.Height(ControlHeight));
            if (nextFaceDirection != motionFaceDirection)
            {
                motionFaceDirection = nextFaceDirection;
                EnforcePreviewOrigin();
                Repaint();
            }

            GUILayout.Space(RowGap);
            GUILayout.Label(new GUIContent("속도", "경로 재생 속도(m/s). 어둠 탄막 비행은 14."), fieldLabelStyle, GUILayout.Width(28f));
            EditorGUI.BeginChangeCheck();
            float nextSpeed = EditorGUILayout.FloatField(motionReplaySpeed, GUILayout.Width(40f), GUILayout.Height(ControlHeight));
            if (EditorGUI.EndChangeCheck() && !float.IsNaN(nextSpeed) && !float.IsInfinity(nextSpeed))
                motionReplaySpeed = Mathf.Clamp(nextSpeed, MinMotionReplaySpeed, MaxMotionReplaySpeed);

            GUILayout.Label("m/s", footerStyle, GUILayout.Width(26f), GUILayout.Height(ControlHeight));
            GUILayout.FlexibleSpace();
        }
    }

    private void DrawPlaybackSpeedButton(string label, float speed)
    {
        bool active = Mathf.Approximately(playbackSpeed, speed);
        Color previousBackground = GUI.backgroundColor;
        GUI.backgroundColor = active ? new Color(0.52f, 0.72f, 1f, 1f) : Color.white;

        if (GUILayout.Button(label, compactButtonStyle, GUILayout.Width(PlaybackSpeedButtonWidth), GUILayout.Height(ControlHeight)))
        {
            playbackSpeed = speed;
            ResetPreviewClock();
            Repaint();
        }

        GUI.backgroundColor = previousBackground;
    }

    private void DrawPreviewSection()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(GridHorizontalMargin);
            float previewSectionHeight = ResolvePreviewSectionHeight();
            using (new EditorGUILayout.VerticalScope(sectionStyle, GUILayout.ExpandWidth(true), GUILayout.Height(previewSectionHeight)))
            {
                EditorGUILayout.LabelField("미리보기", sectionTitleStyle);
                float previewHeight = Mathf.Max(1f, previewSectionHeight - PreviewSectionInnerOffset);
                Rect rect = GUILayoutUtility.GetRect(10f, previewHeight, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(rect, new Color(0.08f, 0.085f, 0.095f, 1f));

                if (selectedPrefab == null || previewInstance == null)
                {
                    DrawCenteredLabel(rect, "미리볼 VFX 프리팹을 선택하세요.");
                }
                else
                {
                    HandlePreviewMotionInput(rect);
                    HandlePreviewCameraInput(rect);
                    Texture previewTexture = Event.current.type == EventType.Repaint ? RenderPreviewTexture(rect) : null;
                    if (previewTexture != null)
                        GUI.DrawTexture(rect, previewTexture, ScaleMode.ScaleToFit, false);

                    DrawMotionPathOverlay(rect);
                    DrawCameraAxisOverlay(rect);
                    DrawPreviewInteractionOverlay(rect);

                    if (!string.IsNullOrEmpty(previewMessage))
                        DrawPreviewMessage(rect, previewMessage);
                }
            }
            GUILayout.Space(GridHorizontalMargin);
        }
    }

    private float ResolvePreviewSectionHeight()
    {
        float usedHeight = HeaderHeight
            + ViewPlaybackSectionHeight
            + FooterHeight
            + SectionGap
            + PreviewLayoutSlack;
        return Mathf.Max(MinPreviewSectionHeight, position.height - usedHeight);
    }

    private void DrawFooter()
    {
        Rect rect = GUILayoutUtility.GetRect(10f, FooterHeight, GUILayout.ExpandWidth(true));
        Rect statusRect = new Rect(rect.x + 8f, rect.y + 3f, rect.width - 16f, rect.height - 6f);
        EditorGUI.DrawRect(statusRect, new Color(0.12f, 0.13f, 0.145f, 1f));

        string prefabName = selectedPrefab != null ? selectedPrefab.name : "없음";
        string viewName = ViewPresets[Mathf.Clamp(viewIndex, 0, ViewPresets.Length - 1)].Label;
        string summaryText = prefabName
            + "  ·  " + viewName + " 보기"
            + "  ·  Y " + previewHeightOffset.ToString("0.##")
            + "  ·  " + playbackTime.ToString("0.00") + " / " + playbackDuration.ToString("0.00") + "초"
            + "  ·  " + playbackSpeed.ToString("0.##") + "×"
            + (hasLoopingParticles ? "  ·  반복" : "  ·  원샷")
            + "  ·  " + BuildMotionStatusText();
        string diagnosticsText = "Renderer " + renderers.Length
            + "   |   Particle " + particleSystems.Length
            + "   |   표시 " + visibleParticleCount + " / " + liveParticleCount;
        Rect brandRect = new Rect(statusRect.xMax - FooterBrandWidth - 10f, statusRect.y + 3f, FooterBrandWidth, statusRect.height - 4f);
        float diagnosticsWidth = Mathf.Min(270f, Mathf.Max(180f, statusRect.width * 0.3f));
        Rect diagnosticsRect = new Rect(brandRect.x - diagnosticsWidth - 8f, statusRect.y + 3f, diagnosticsWidth, statusRect.height - 4f);
        Rect summaryRect = new Rect(statusRect.x + 10f, statusRect.y + 3f, Mathf.Max(1f, diagnosticsRect.x - statusRect.x - 18f), statusRect.height - 4f);
        GUI.Label(summaryRect, summaryText, footerStyle);
        GUI.Label(diagnosticsRect, diagnosticsText, footerDiagnosticsStyle);
        GUI.Label(brandRect, FooterBrandText, footerBrandStyle);
    }

    private void DrawViewButton(int index)
    {
        bool active = viewIndex == index;
        Color previousBackground = GUI.backgroundColor;
        GUI.backgroundColor = active ? new Color(0.52f, 0.72f, 1f, 1f) : Color.white;

        if (GUILayout.Button(ViewPresets[index].Label, compactButtonStyle, GUILayout.Width(CameraOptionButtonWidth), GUILayout.Height(ControlHeight)))
        {
            viewIndex = index;
            SetCameraToPreset(index, true);
            Repaint();
        }

        GUI.backgroundColor = previousBackground;
    }

    private void TryUseSelection()
    {
        GameObject selection = Selection.activeObject as GameObject;
        if (IsPrefabAsset(selection))
            SetPrefab(selection);
    }

    private void SetPrefab(GameObject prefab)
    {
        if (selectedPrefab == prefab && (prefab == null || previewInstance != null))
            return;

        bool preserveCamera = previewInstance != null && prefab != null;
        selectedPrefab = prefab;
        playbackTime = 0f;
        previewMessage = string.Empty;
        RebuildPreviewInstance(preserveCamera);
        Repaint();
    }

    private void SetPreviewHeightOffset(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return;

        float nextValue = Mathf.Clamp(value, -100f, 100f);
        if (Mathf.Approximately(previewHeightOffset, nextValue))
            return;

        previewHeightOffset = nextValue;
        if (previewInstance != null)
        {
            EnforcePreviewOrigin();
            ScrubParticlesToTime(playbackTime); // 월드 공간 파티클도 새 높이에서 다시 계산
            RefreshStableFrameBounds();
            UpdatePreviewCameraTransform();
        }

        Repaint();
    }

    private void RebuildPreviewInstance(bool preserveCamera)
    {
        ClearPreviewInstance();

        if (selectedPrefab == null)
            return;

        EnsurePreviewUtility();

        previewInstance = Instantiate(selectedPrefab);
        if (previewInstance == null)
        {
            previewMessage = "미리보기 인스턴스를 만들 수 없습니다.";
            return;
        }

        previewInstance.name = PreviewInstancePrefix + selectedPrefab.name;
        previewInstance.transform.SetPositionAndRotation(ResolveMotionPosition(), ResolveMotionRotation()); // 그린 경로는 프리팹을 바꿔도 유지
        previewInstance.transform.localScale = Vector3.one;
        previewInstance.SetActive(true);
        SetHideFlagsRecursive(previewInstance, HideFlags.HideAndDontSave);
        previewUtility.AddSingleGO(previewInstance);

        particleSystems = previewInstance.GetComponentsInChildren<ParticleSystem>(true);
        rootParticleSystems = ResolveRootParticleSystems(particleSystems);
        renderers = previewInstance.GetComponentsInChildren<Renderer>(true);
        BuildTrailProxies();
        PrepareParticleSystemsForManualPreview();
        ResolvePlaybackDuration();

        ShowFirstVisibleParticlePreview();
        ResetPreviewClock();
        ResolveBounds();
        RefreshStableFrameBounds();
        RebuildPreviewFloor();
        RefreshPreviewMessage();
        if (preserveCamera)
            UpdatePreviewCameraTransform();
        else
            SetCameraToPreset(viewIndex, false);
    }

    private void ResolvePlaybackDuration()
    {
        hasLoopingParticles = false;
        float theoreticalDuration = 0f;
        float emissionDuration = 0f;
        float longestLoopCycle = 0f;

        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem particleSystem = particleSystems[i];
            if (particleSystem == null || !particleSystem.gameObject.activeInHierarchy)
                continue;

            ParticleSystem.MainModule main = particleSystem.main;
            float startDelay = ResolveCurveMaximum(main.startDelay);
            float duration = Mathf.Max(0f, main.duration);
            float lifetime = ResolveCurveMaximum(main.startLifetime);
            theoreticalDuration = Mathf.Max(theoreticalDuration, startDelay + duration + lifetime);
            emissionDuration = Mathf.Max(emissionDuration, startDelay + duration);

            ParticleSystem.EmissionModule emission = particleSystem.emission;
            if (!main.loop || !emission.enabled)
                continue;

            hasLoopingParticles = true;
            if (duration >= MinLoopPreviewDuration && duration <= MaxLoopPreviewDuration)
                longestLoopCycle = Mathf.Max(longestLoopCycle, duration);
        }

        if (hasLoopingParticles)
        {
            playbackDuration = longestLoopCycle > 0f ? longestLoopCycle : DefaultLoopPreviewDuration;
            playbackDuration = Mathf.Clamp(playbackDuration, MinLoopPreviewDuration, MaxLoopPreviewDuration);
            ResetParticleSystems();
            return;
        }

        float scanLimit = Mathf.Clamp(theoreticalDuration, 0.5f, MaxOneShotScanDuration);
        float detectedDuration = DetectLastVisibleParticleTime(scanLimit);
        if (detectedDuration <= 0f)
            detectedDuration = emissionDuration > 0f ? emissionDuration : DefaultLoopPreviewDuration;

        playbackDuration = Mathf.Clamp(detectedDuration, 0.5f, MaxOneShotScanDuration);
    }

    private float DetectLastVisibleParticleTime(float scanLimit)
    {
        if (particleSystems.Length == 0 || rootParticleSystems.Length == 0)
            return 0f;

        float scanStep = Mathf.Clamp(scanLimit / OneShotDurationScanSamples, 0.025f, 0.1f);
        float lastVisibleTime = 0f;

        for (float time = scanStep; time <= scanLimit + scanStep * 0.5f; time += scanStep)
        {
            SimulateParticlesFromStart(time);
            if (CountVisibleParticles() > 0)
                lastVisibleTime = time;
        }

        ResetParticleSystems();
        if (lastVisibleTime <= 0f)
            return 0f;

        return Mathf.Min(scanLimit, Mathf.Ceil((lastVisibleTime + scanStep) * 20f) / 20f);
    }

    private static float ResolveCurveMaximum(ParticleSystem.MinMaxCurve curve)
    {
        switch (curve.mode)
        {
            case ParticleSystemCurveMode.Constant:
                return Mathf.Max(0f, curve.constant);
            case ParticleSystemCurveMode.TwoConstants:
                return Mathf.Max(0f, curve.constantMax);
            case ParticleSystemCurveMode.Curve:
                return Mathf.Max(0f, ResolveAnimationCurveMaximum(curve.curve) * curve.curveMultiplier);
            case ParticleSystemCurveMode.TwoCurves:
                return Mathf.Max(
                    0f,
                    Mathf.Max(
                        ResolveAnimationCurveMaximum(curve.curveMin),
                        ResolveAnimationCurveMaximum(curve.curveMax)) * curve.curveMultiplier);
            default:
                return 0f;
        }
    }

    private static float ResolveAnimationCurveMaximum(AnimationCurve curve)
    {
        if (curve == null || curve.length == 0)
            return 0f;

        float maximum = 0f;
        Keyframe[] keys = curve.keys;
        for (int i = 0; i < keys.Length; i++)
            maximum = Mathf.Max(maximum, keys[i].value);

        return maximum;
    }

    private void PrepareParticleSystemsForManualPreview()
    {
        for (int i = 0; i < rootParticleSystems.Length; i++)
        {
            ParticleSystem particleSystem = rootParticleSystems[i];
            if (particleSystem == null)
                continue;

            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        RefreshLiveParticleCount();
    }

    private void ResolveBounds()
    {
        bool hasBounds = false;
        Bounds bounds = new Bounds(Vector3.zero, Vector3.one);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (!IsFramingRenderer(renderer))
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

        for (int i = 0; i < particleSystems.Length; i++)
            hasBounds |= TryEncapsulateParticles(particleSystems[i], ref bounds, hasBounds);

        if (!hasBounds && previewInstance != null)
            hasBounds = TryResolveTransformBounds(previewInstance.transform, out bounds);

        previewCenter = hasBounds ? bounds.center : Vector3.zero;
        previewRadius = hasBounds ? Mathf.Max(bounds.extents.magnitude, 0.5f) : 1f;
    }

    private void RefreshStableFrameBounds()
    {
        bool hasBounds = false;
        Bounds bounds = new Bounds(Vector3.zero, Vector3.one);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (!IsFramingRenderer(renderer))
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

        if (!hasBounds && previewInstance != null)
            hasBounds = TryResolveTransformBounds(previewInstance.transform, out bounds);

        frameCenter = hasBounds ? bounds.center : previewCenter;
        frameRadius = hasBounds ? Mathf.Max(bounds.extents.magnitude, 0.5f) : Mathf.Max(previewRadius, 1f);
    }

    // 빛 모듈만 쓰는 입자처럼 아무것도 그리지 않는 렌더러는 경계가 수십 m로 잡혀 카메라를 멀리 밀어내므로 뺀다.
    private static bool IsFramingRenderer(Renderer renderer)
    {
        if (renderer == null)
            return false;

        return !(renderer is ParticleSystemRenderer particleRenderer)
            || particleRenderer.renderMode != ParticleSystemRenderMode.None;
    }

    private static ParticleSystem[] ResolveRootParticleSystems(ParticleSystem[] systems)
    {
        if (systems == null || systems.Length == 0)
            return Array.Empty<ParticleSystem>();

        int rootCount = 0;
        for (int i = 0; i < systems.Length; i++)
        {
            if (IsRootParticleSystem(systems[i]))
                rootCount++;
        }

        if (rootCount == systems.Length)
            return systems;

        ParticleSystem[] roots = new ParticleSystem[rootCount];
        int index = 0;
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem particleSystem = systems[i];
            if (IsRootParticleSystem(particleSystem))
                roots[index++] = particleSystem;
        }

        return roots;
    }

    private static bool IsRootParticleSystem(ParticleSystem particleSystem)
    {
        if (particleSystem == null)
            return false;

        Transform parent = particleSystem.transform.parent;
        while (parent != null)
        {
            if (parent.GetComponent<ParticleSystem>() != null)
                return false;

            parent = parent.parent;
        }

        return true;
    }

    private void TickPreview()
    {
        if (selectedPrefab == null || previewInstance == null)
        {
            cameraBlendActive = false;
            lastUpdateTime = EditorApplication.timeSinceStartup;
            return;
        }

        double now = EditorApplication.timeSinceStartup;
        if (now - lastRepaintTime < PlayingPreviewTickInterval)
            return;

        lastRepaintTime = now;

        float deltaTime = Mathf.Clamp((float)(now - lastUpdateTime), 0f, 0.1f);
        bool changed = false;

        if (isPlaying)
        {
            StepPlayback(deltaTime);
            changed = true;
        }

        changed |= AdvanceCameraBlend(now);

        lastUpdateTime = now;
        if (changed)
            Repaint();
    }

    private void StepPlayback(float deltaTime)
    {
        float scaledDeltaTime = deltaTime * Mathf.Max(0.01f, playbackSpeed);
        motionClock += scaledDeltaTime;
        AdvanceMotionReplay(scaledDeltaTime);
        UpdateMotionFacing(scaledDeltaTime);
        AdvancePlayback(deltaTime);
        UpdateTrailProxies();
    }

    private void AdvancePlayback(float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        float scaledDeltaTime = deltaTime * Mathf.Max(0.01f, playbackSpeed);
        float nextTime = playbackTime + scaledDeltaTime;
        if (nextTime > playbackDuration)
        {
            if (loopPlayback)
            {
                float wrappedTime = nextTime % playbackDuration;
                if (hasLoopingParticles && IsMotionPreviewActive())
                {
                    // 움직이는 중에 처음부터 다시 시뮬레이션하면 월드 입자 궤적이 끊기므로 이어서 진행
                    AdvanceParticles(scaledDeltaTime);
                    playbackTime = wrappedTime;
                    return;
                }

                ScrubParticlesToTime(wrappedTime);
                playbackTime = wrappedTime;
                return;
            }

            scaledDeltaTime = Mathf.Max(0f, playbackDuration - playbackTime);
            AdvanceParticles(scaledDeltaTime);
            playbackTime = playbackDuration;
            isPlaying = false;
            return;
        }

        EnforcePreviewOrigin();

        AdvanceParticles(scaledDeltaTime);
        playbackTime = nextTime;
    }

    private void RestartParticlePreview(bool playImmediately)
    {
        if (HasReplayablePath())
            PlaceAtMotionReplayStart();

        playbackTime = 0f;
        ResetParticleSystems();
        isPlaying = playImmediately;
        previewMessage = string.Empty;
        ResolveBounds();
        ResetPreviewClock();
    }

    private void ShowFirstVisibleParticlePreview()
    {
        if (playbackDuration <= 0f)
        {
            RestartParticlePreview(false);
            return;
        }

        float maxTime = Mathf.Min(playbackDuration, FirstVisiblePreviewScanMaxTime);
        float previewTime = Mathf.Min(InitialVisiblePreviewTime, maxTime);
        bool foundVisibleParticles = false;

        for (float time = InitialVisiblePreviewTime; time <= maxTime; time += FirstVisiblePreviewScanStep)
        {
            ScrubParticlesToTime(time);
            if (CountVisibleParticles() > 0)
            {
                previewTime = time;
                foundVisibleParticles = true;
                break;
            }
        }

        if (!foundVisibleParticles)
            ScrubParticlesToTime(previewTime);

        playbackTime = previewTime;
    }

    private void ResetPreviewClock()
    {
        double now = EditorApplication.timeSinceStartup;
        lastUpdateTime = now;
        lastRepaintTime = now;
    }

    private void ScrubParticlesToTime(float time)
    {
        ResetParticleSystems();
        SimulateParticlesFromStart(time);
        RefreshLiveParticleCount();
        ResolveBounds();
    }

    private void ResetParticleSystems()
    {
        EnforcePreviewOrigin();
        for (int i = 0; i < rootParticleSystems.Length; i++)
        {
            ParticleSystem particleSystem = rootParticleSystems[i];
            if (particleSystem == null)
                continue;

            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        restartSimulationOnNextAdvance = true;
        ClearTrailProxyPoints();
        RefreshLiveParticleCount();
    }

    private void AdvanceParticles(float deltaTime)
    {
        if (deltaTime <= 0f)
        {
            ResolveBounds();
            return;
        }

        EnforcePreviewOrigin();
        bool restart = restartSimulationOnNextAdvance;
        for (int i = 0; i < rootParticleSystems.Length; i++)
        {
            ParticleSystem particleSystem = rootParticleSystems[i];
            if (particleSystem == null)
                continue;

            particleSystem.Simulate(deltaTime, true, restart, true);
        }

        restartSimulationOnNextAdvance = false;
        RefreshLiveParticleCount();
        ResolveBounds();
    }

    private void SimulateParticlesFromStart(float time)
    {
        if (time <= 0f)
            return;

        EnforcePreviewOrigin();
        for (int i = 0; i < rootParticleSystems.Length; i++)
        {
            ParticleSystem particleSystem = rootParticleSystems[i];
            if (particleSystem == null)
                continue;

            particleSystem.Simulate(time, true, true, true);
        }

        restartSimulationOnNextAdvance = false;
    }

    private void EnforcePreviewOrigin()
    {
        if (previewInstance == null)
            return;

        // 드래그 이동 미리보기: 원점 대신 이동한 위치와 진행 방향을 강제한다.
        Vector3 previewPosition = ResolveMotionPosition();
        Quaternion previewRotation = ResolveMotionRotation();
        Transform previewTransform = previewInstance.transform;
        if (previewTransform.position != previewPosition || previewTransform.rotation != previewRotation)
            previewTransform.SetPositionAndRotation(previewPosition, previewRotation);
    }

    private void RefreshLiveParticleCount()
    {
        liveParticleCount = CountLiveParticles();
        visibleParticleCount = CountVisibleParticles();
    }

    private int CountLiveParticles()
    {
        int count = 0;
        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem particleSystem = particleSystems[i];
            if (particleSystem != null)
                count += particleSystem.particleCount;
        }

        return count;
    }

    private int CountVisibleParticles()
    {
        int count = 0;
        for (int systemIndex = 0; systemIndex < particleSystems.Length; systemIndex++)
        {
            ParticleSystem particleSystem = particleSystems[systemIndex];
            if (particleSystem == null || !particleSystem.gameObject.activeInHierarchy)
                continue;

            ParticleSystemRenderer particleRenderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            if (particleRenderer == null || !particleRenderer.enabled)
                continue;

            int particleCount = particleSystem.particleCount;
            if (particleCount <= 0)
                continue;

            if (particleBuffer.Length < particleCount)
                particleBuffer = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(particleCount)];

            int copiedCount = particleSystem.GetParticles(particleBuffer);
            for (int particleIndex = 0; particleIndex < copiedCount; particleIndex++)
            {
                ParticleSystem.Particle particle = particleBuffer[particleIndex];
                if (particle.GetCurrentSize(particleSystem) <= 0.0001f)
                    continue;

                if (particle.GetCurrentColor(particleSystem).a <= 2)
                    continue;

                count++;
            }
        }

        return count;
    }

    private void SetCameraToPreset(int presetIndex, bool animated)
    {
        Vector3 direction = ViewPresets[Mathf.Clamp(presetIndex, 0, ViewPresets.Length - 1)].Direction.normalized;
        float targetPitch = Mathf.Clamp(Mathf.Asin(direction.y) * Mathf.Rad2Deg, MinCameraPitch, MaxCameraPitch);
        float targetYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        if (animated && previewInstance != null)
        {
            StartCameraBlend(targetYaw, targetPitch, DefaultCameraDistanceScale);
            return;
        }

        cameraBlendActive = false;
        cameraPitch = targetPitch;
        cameraYaw = NormalizeAngle(targetYaw);
        cameraDistanceScale = DefaultCameraDistanceScale;
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

    private void HandlePreviewCameraInput(Rect rect)
    {
        Event current = Event.current;
        if (current == null || !rect.Contains(current.mousePosition))
            return;

        if (current.type == EventType.MouseDown && current.button == 1)
        {
            CancelCameraBlend();
            current.Use();
            return;
        }

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
            cameraDistanceScale = Mathf.Clamp(
                cameraDistanceScale * Mathf.Exp(current.delta.y * CameraZoomSensitivity),
                MinCameraDistanceScale,
                MaxCameraDistanceScale);
            UpdatePreviewCameraTransform();
            Repaint();
            current.Use();
        }
    }

    // ---- 드래그 이동 미리보기 (2026-10-01) ----
    // 좌클릭 드래그로 VFX 루트를 옮긴다. 파티클은 옮긴 위치에서 이어서 시뮬레이션되어 월드 공간 입자가 궤적을 남긴다.
    // TrailRenderer는 편집 모드에서 시간이 흐르지 않아 궤적이 남지 않으므로, 같은 재질·폭·색의 LineRenderer 대역이 대신 그린다.
    private void HandlePreviewMotionInput(Rect rect)
    {
        Event current = Event.current;
        if (current == null || previewUtility == null)
            return;

        int controlId = GUIUtility.GetControlID(MotionDragControlHint, FocusType.Passive, rect);
        switch (current.GetTypeForControl(controlId))
        {
            case EventType.MouseDown:
                if (current.button != 0 || !rect.Contains(current.mousePosition))
                    return;

                if (!BeginMotionDrag(rect, current.mousePosition, current.shift))
                    return;

                GUIUtility.hotControl = controlId;
                current.Use();
                Repaint();
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl != controlId)
                    return;

                ContinueMotionDrag(rect, current.mousePosition);
                current.Use();
                Repaint();
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl != controlId || current.button != 0)
                    return;

                GUIUtility.hotControl = 0;
                EndMotionDrag();
                current.Use();
                Repaint();
                break;
        }
    }

    // groundPlane(Shift): 바닥과 나란한 면에서 끈다. 기본은 화면과 나란한 면이라 커서를 그대로 따라간다.
    private bool BeginMotionDrag(Rect rect, Vector2 mousePosition, bool groundPlane)
    {
        if (previewUtility == null || previewInstance == null)
            return false;

        UpdatePreviewCameraTransform();
        Ray ray = ResolvePreviewRay(rect, mousePosition);
        Vector3 position = ResolveMotionPosition();
        Vector3 cameraForward = previewUtility.camera.transform.forward;
        bool useGround = groundPlane && Mathf.Abs(cameraForward.y) > 0.2f;
        motionDragPlane = useGround ? new Plane(Vector3.up, position) : new Plane(-cameraForward, position);
        if (!motionDragPlane.Raycast(ray, out float enter))
            return false;

        motionDragGrabOffset = position - ray.GetPoint(enter);
        motionDragging = true;
        motionPath.Clear();
        motionPathCumulative.Clear();
        motionPathLength = 0f;
        motionReplayDistance = 0f;
        motionLastTickOffset = motionOffset;
        AppendMotionPathPoint(motionOffset);

        if (!isPlaying)
        {
            if (playbackTime >= playbackDuration - 0.0001f)
                RestartParticlePreview(true); // 끝난 원샷은 처음부터 다시 틀어 움직임을 보여 준다
            else
                isPlaying = true;

            ResetPreviewClock();
        }

        return true;
    }

    private void ContinueMotionDrag(Rect rect, Vector2 mousePosition)
    {
        if (!motionDragging || previewUtility == null)
            return;

        Ray ray = ResolvePreviewRay(rect, mousePosition);
        if (!motionDragPlane.Raycast(ray, out float enter))
            return;

        Vector3 origin = new Vector3(0f, previewHeightOffset, 0f);
        motionOffset = Vector3.ClampMagnitude(ray.GetPoint(enter) + motionDragGrabOffset - origin, MaxMotionOffset);
        AppendMotionPathPoint(motionOffset);
    }

    private void EndMotionDrag()
    {
        if (!motionDragging)
            return;

        motionDragging = false;
        if (motionPath.Count > 0 && (motionPath[motionPath.Count - 1] - motionOffset).sqrMagnitude > 0.000001f)
            AddMotionPathPoint(motionOffset);

        if (HasReplayablePath())
            RestartMotionReplayPass();
    }

    private void EndMotionDragState()
    {
        if (motionDragging && GUIUtility.hotControl != 0)
            GUIUtility.hotControl = 0;

        motionDragging = false;
    }

    private void ResetMotion()
    {
        EndMotionDragState();
        motionPath.Clear();
        motionPathCumulative.Clear();
        motionPathLength = 0f;
        motionReplayDistance = 0f;
        motionOffset = Vector3.zero;
        motionLastTickOffset = Vector3.zero;
        motionRotation = Quaternion.identity;

        if (previewInstance != null)
            RestartParticlePreview(true); // 먼 곳에서 원점으로 순간 이동한 자국이 남지 않게 새로 시작
    }

    private void AppendMotionPathPoint(Vector3 offset)
    {
        if (motionPath.Count == 0)
        {
            AddMotionPathPoint(offset);
            return;
        }

        if ((offset - motionPath[motionPath.Count - 1]).sqrMagnitude < MotionPathPointSpacing * MotionPathPointSpacing)
            return;

        AddMotionPathPoint(offset);
    }

    private void AddMotionPathPoint(Vector3 offset)
    {
        if (motionPath.Count >= MotionPathMaxPoints)
            return;

        float step = motionPath.Count == 0 ? 0f : Vector3.Distance(motionPath[motionPath.Count - 1], offset);
        motionPath.Add(offset);
        motionPathCumulative.Add(motionPathLength + step);
        motionPathLength += step;
    }

    private bool HasMotionPath()
    {
        return motionPath.Count >= 2 && motionPathLength >= MinMotionReplayPathLength;
    }

    private bool HasReplayablePath()
    {
        return motionReplay && !motionDragging && HasMotionPath();
    }

    private bool IsMotionPreviewActive()
    {
        return motionDragging || HasReplayablePath();
    }

    private void PlaceAtMotionReplayStart()
    {
        motionReplayDistance = 0f;
        motionOffset = motionPath[0];
        motionLastTickOffset = motionOffset;

        Vector3 direction = SampleMotionPath(Mathf.Min(0.25f, motionPathLength)) - motionPath[0];
        if (direction.sqrMagnitude > 0.000001f)
            motionRotation = ResolveFacingRotation(direction);
    }

    // 경로 처음으로 돌아가 파티클·트레일을 새로 시작한다(끝→처음 순간 이동 자국 방지).
    private void RestartMotionReplayPass()
    {
        PlaceAtMotionReplayStart();
        playbackTime = 0f;
        ResetParticleSystems();
    }

    private void AdvanceMotionReplay(float deltaTime)
    {
        if (deltaTime <= 0f || !HasReplayablePath())
            return;

        float speed = Mathf.Clamp(motionReplaySpeed, MinMotionReplaySpeed, MaxMotionReplaySpeed);
        motionReplayDistance += speed * deltaTime;

        // 끝에서 트레일이 따라와 사라질 만큼 잠깐 멈춘 뒤 다시 출발
        float holdDistance = ResolveMotionReplayHoldTime() * speed;
        if (motionReplayDistance > motionPathLength + holdDistance)
        {
            RestartMotionReplayPass();
            return;
        }

        motionOffset = SampleMotionPath(motionReplayDistance);
    }

    private float ResolveMotionReplayHoldTime()
    {
        float hold = 0.3f;
        for (int i = 0; i < trailProxies.Count; i++)
        {
            TrailRenderer source = trailProxies[i].Source;
            if (source != null)
                hold = Mathf.Max(hold, source.time);
        }

        return Mathf.Min(hold, 1f);
    }

    private Vector3 SampleMotionPath(float distance)
    {
        int count = motionPath.Count;
        if (count == 0)
            return motionOffset;

        if (distance <= 0f)
            return motionPath[0];

        if (distance >= motionPathLength)
            return motionPath[count - 1];

        int low = 0;
        int high = count - 1;
        while (high - low > 1)
        {
            int mid = (low + high) >> 1;
            if (motionPathCumulative[mid] <= distance)
                low = mid;
            else
                high = mid;
        }

        float segment = motionPathCumulative[high] - motionPathCumulative[low];
        float t = segment > 0.000001f ? (distance - motionPathCumulative[low]) / segment : 0f;
        return Vector3.Lerp(motionPath[low], motionPath[high], t);
    }

    private void UpdateMotionFacing(float deltaTime)
    {
        Vector3 delta = motionOffset - motionLastTickOffset;
        motionLastTickOffset = motionOffset;
        if (!motionFaceDirection || deltaTime <= 0f || delta.sqrMagnitude < 0.00000001f)
            return;

        float blend = 1f - Mathf.Exp(-MotionTurnSharpness * deltaTime);
        motionRotation = Quaternion.Slerp(motionRotation, ResolveFacingRotation(delta), blend);
    }

    private static Quaternion ResolveFacingRotation(Vector3 direction)
    {
        Vector3 forward = direction.normalized;
        Vector3 up = Mathf.Abs(forward.y) > 0.98f ? Vector3.forward : Vector3.up;
        return Quaternion.LookRotation(forward, up);
    }

    private Vector3 ResolveMotionPosition()
    {
        return new Vector3(0f, previewHeightOffset, 0f) + motionOffset;
    }

    private Quaternion ResolveMotionRotation()
    {
        return motionFaceDirection ? motionRotation : Quaternion.identity;
    }

    private string BuildMotionStatusText()
    {
        if (motionDragging)
            return "끄는 중 " + motionPathLength.ToString("0.0") + "m";

        if (HasMotionPath())
            return (motionReplay ? "경로 재생 " : "경로 ") + motionPathLength.ToString("0.0") + "m";

        return motionOffset.sqrMagnitude > 0.0001f ? "이동됨" : "고정";
    }

    // 미리보기 텍스처는 rect를 꽉 채우므로 카메라 화각과 rect 비율로 광선을 직접 계산한다.
    private Ray ResolvePreviewRay(Rect rect, Vector2 mousePosition)
    {
        Camera camera = previewUtility.camera;
        float aspect = rect.height > 0f ? rect.width / rect.height : 1f;
        float tanHalf = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float x = ((mousePosition.x - rect.x) / Mathf.Max(1f, rect.width) * 2f - 1f) * tanHalf * aspect;
        float y = (1f - (mousePosition.y - rect.y) / Mathf.Max(1f, rect.height) * 2f) * tanHalf;
        Transform cameraTransform = camera.transform;
        return new Ray(cameraTransform.position, cameraTransform.TransformDirection(new Vector3(x, y, 1f)).normalized);
    }

    private bool TryProjectToPreview(Rect rect, Vector3 worldPosition, out Vector2 screenPosition)
    {
        screenPosition = default;
        Camera camera = previewUtility.camera;
        Vector3 local = camera.transform.InverseTransformPoint(worldPosition);
        if (local.z <= camera.nearClipPlane)
            return false;

        float aspect = rect.height > 0f ? rect.width / rect.height : 1f;
        float tanHalf = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float normalizedX = local.x / (local.z * tanHalf * aspect);
        float normalizedY = local.y / (local.z * tanHalf);
        screenPosition = new Vector2(
            rect.x + (normalizedX * 0.5f + 0.5f) * rect.width,
            rect.y + (0.5f - normalizedY * 0.5f) * rect.height);
        return true;
    }

    private void DrawMotionPathOverlay(Rect rect)
    {
        if (Event.current.type != EventType.Repaint || previewUtility == null || motionPath.Count < 2)
            return;

        if (motionPathScreenBuffer.Length < motionPath.Count)
            motionPathScreenBuffer = new Vector3[Mathf.NextPowerOfTwo(motionPath.Count)];

        Vector3 origin = new Vector3(0f, previewHeightOffset, 0f);
        Handles.BeginGUI();
        Color previousColor = Handles.color;
        Handles.color = new Color(0.45f, 0.85f, 1f, 0.5f);

        int runCount = 0;
        for (int i = 0; i < motionPath.Count; i++)
        {
            if (TryProjectToPreview(rect, origin + motionPath[i], out Vector2 screen) && rect.Contains(screen))
            {
                motionPathScreenBuffer[runCount++] = new Vector3(screen.x, screen.y, 0f);
                continue;
            }

            DrawMotionPathRun(runCount);
            runCount = 0;
        }

        DrawMotionPathRun(runCount);
        if (TryProjectToPreview(rect, origin + motionPath[0], out Vector2 start) && rect.Contains(start))
            Handles.DrawSolidDisc(new Vector3(start.x, start.y, 0f), Vector3.forward, 3.5f);

        Handles.color = previousColor;
        Handles.EndGUI();
    }

    private void DrawMotionPathRun(int count)
    {
        if (count >= 2)
            Handles.DrawAAPolyLine(2f, count, motionPathScreenBuffer);
    }

    private void BuildTrailProxies()
    {
        DestroyTrailProxies();
        if (previewInstance == null || previewUtility == null)
            return;

        TrailRenderer[] trails = previewInstance.GetComponentsInChildren<TrailRenderer>(true);
        for (int i = 0; i < trails.Length; i++)
        {
            TrailRenderer trail = trails[i];
            if (trail == null)
                continue;

            bool authoredEnabled = trail.enabled;
            trail.Clear();
            trail.enabled = false; // 원본은 편집 모드 시간으로 그려 궤적이 남지 않으므로 대역이 대신 그린다
            if (!authoredEnabled)
                continue;

            GameObject proxyObject = EditorUtility.CreateGameObjectWithHideFlags(
                PreviewInstancePrefix + "Trail " + trail.name,
                HideFlags.HideAndDontSave,
                typeof(LineRenderer));
            LineRenderer line = proxyObject.GetComponent<LineRenderer>();
            CopyTrailStyle(trail, line);
            previewUtility.AddSingleGO(proxyObject);
            trailProxies.Add(new TrailProxy(trail, line));
        }
    }

    private static void CopyTrailStyle(TrailRenderer trail, LineRenderer line)
    {
        line.useWorldSpace = true;
        line.loop = false;
        line.positionCount = 0;
        line.sharedMaterials = trail.sharedMaterials;
        line.widthCurve = trail.widthCurve;
        line.widthMultiplier = trail.widthMultiplier;
        line.colorGradient = trail.colorGradient;
        line.alignment = trail.alignment;
        line.textureMode = trail.textureMode;
        line.textureScale = trail.textureScale;
        line.numCornerVertices = trail.numCornerVertices;
        line.numCapVertices = trail.numCapVertices;
        line.generateLightingData = trail.generateLightingData;
        line.applyActiveColorSpace = trail.applyActiveColorSpace;
        line.shadowCastingMode = trail.shadowCastingMode;
        line.receiveShadows = trail.receiveShadows;
        line.shadowBias = trail.shadowBias;
        line.lightProbeUsage = trail.lightProbeUsage;
        line.reflectionProbeUsage = trail.reflectionProbeUsage;
        line.renderingLayerMask = trail.renderingLayerMask;
        line.sortingLayerID = trail.sortingLayerID;
        line.sortingOrder = trail.sortingOrder;
        line.maskInteraction = trail.maskInteraction;
    }

    // TrailRenderer처럼 머리(현재 위치)부터 오래된 점 순서로 잇고, 수명(time)이 지난 점은 버린다.
    private void UpdateTrailProxies()
    {
        for (int i = 0; i < trailProxies.Count; i++)
        {
            TrailProxy proxy = trailProxies[i];
            TrailRenderer source = proxy.Source;
            LineRenderer line = proxy.Line;
            if (source == null || line == null)
                continue;

            float lifetime = Mathf.Max(0.0001f, source.time);
            int expired = 0;
            while (expired < proxy.Times.Count && motionClock - proxy.Times[expired] > lifetime)
                expired++;

            if (expired > 0)
            {
                proxy.Points.RemoveRange(0, expired);
                proxy.Times.RemoveRange(0, expired);
            }

            bool emitting = source.emitting && source.gameObject.activeInHierarchy;
            Vector3 head = source.transform.position;
            if (emitting)
            {
                float spacing = Mathf.Max(0.01f, source.minVertexDistance);
                int last = proxy.Points.Count - 1;
                if (last < 0 || (head - proxy.Points[last]).sqrMagnitude >= spacing * spacing)
                {
                    if (proxy.Points.Count >= MaxTrailProxyPoints)
                    {
                        proxy.Points.RemoveAt(0);
                        proxy.Times.RemoveAt(0);
                    }

                    proxy.Points.Add(head);
                    proxy.Times.Add(motionClock);
                }
            }

            WriteTrailProxyLine(proxy, head, emitting);
        }
    }

    private static void WriteTrailProxyLine(TrailProxy proxy, Vector3 head, bool includeHead)
    {
        int required = proxy.Points.Count + 1;
        if (proxy.Buffer.Length < required)
            proxy.Buffer = new Vector3[Mathf.NextPowerOfTwo(required)];

        int count = 0;
        if (includeHead)
            proxy.Buffer[count++] = head;

        for (int i = proxy.Points.Count - 1; i >= 0; i--)
        {
            Vector3 point = proxy.Points[i];
            if (count > 0 && (proxy.Buffer[count - 1] - point).sqrMagnitude < 0.00000001f)
                continue;

            proxy.Buffer[count++] = point;
        }

        if (count < 2)
        {
            proxy.Line.positionCount = 0;
            return;
        }

        proxy.Line.positionCount = count;
        proxy.Line.SetPositions(proxy.Buffer); // positionCount 뒤의 칸은 무시됨
    }

    private void ClearTrailProxyPoints()
    {
        for (int i = 0; i < trailProxies.Count; i++)
        {
            TrailProxy proxy = trailProxies[i];
            proxy.Points.Clear();
            proxy.Times.Clear();
            if (proxy.Line != null)
                proxy.Line.positionCount = 0;
        }
    }

    private void DestroyTrailProxies()
    {
        for (int i = 0; i < trailProxies.Count; i++)
        {
            LineRenderer line = trailProxies[i].Line;
            if (line != null)
                DestroyImmediate(line.gameObject);
        }

        trailProxies.Clear();
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

    private void DrawPreviewInteractionOverlay(Rect previewRect)
    {
        if (Event.current.type != EventType.Repaint)
            return;

        float zoomPercent = DefaultCameraDistanceScale / Mathf.Max(cameraDistanceScale, 0.001f) * 100f;
        string text = GetActiveEnvironmentPreset().Label
            + "  ·  줌 " + zoomPercent.ToString("0") + "%"
            + "  ·  좌클릭 드래그 이동  ·  우클릭 드래그 회전  ·  휠 확대/축소";
        float width = Mathf.Min(460f, Mathf.Max(220f, previewRect.width - 24f));
        Rect overlayRect = new Rect(previewRect.x + 12f, previewRect.yMax - 36f, width, 24f);
        EditorGUI.DrawRect(overlayRect, new Color(0.035f, 0.042f, 0.052f, 0.78f));
        GUI.Label(new Rect(overlayRect.x + 10f, overlayRect.y, overlayRect.width - 20f, overlayRect.height), text, previewOverlayStyle);
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

    private Texture RenderPreviewTexture(Rect rect)
    {
        if (previewInstance == null)
            return null;

        EnforcePreviewOrigin();
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
            previewMessage = BuildPreviewMessage();
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
        previewUtility.camera.nearClipPlane = 0.01f;
        previewUtility.camera.farClipPlane = 5000f;
        previewUtility.camera.fieldOfView = 45f;
        previewUtility.camera.cullingMask = ~0;

        UniversalAdditionalCameraData cameraData = previewUtility.camera.GetComponent<UniversalAdditionalCameraData>();
        if (cameraData == null)
            cameraData = previewUtility.camera.gameObject.AddComponent<UniversalAdditionalCameraData>();

        cameraData.renderPostProcessing = false;
        ApplyPreviewEnvironment();
    }

    private void ApplyPreviewEnvironment()
    {
        if (previewUtility == null)
            return;

        PreviewEnvironmentPreset preset = GetActiveEnvironmentPreset();
        previewUtility.camera.backgroundColor = preset.BackgroundColor;
        previewUtility.ambientColor = preset.AmbientColor;

        if (previewUtility.lights.Length > 0 && previewUtility.lights[0] != null)
            ConfigurePreviewLight(previewUtility.lights[0], preset.KeyLightIntensity, Quaternion.Euler(42f, -32f, 0f), preset.KeyLightColor);

        if (previewUtility.lights.Length > 1 && previewUtility.lights[1] != null)
            ConfigurePreviewLight(previewUtility.lights[1], preset.FillLightIntensity, Quaternion.Euler(325f, 138f, 0f), preset.FillLightColor);

        ApplyFloorColor(preset.FloorColor);
    }

    private PreviewEnvironmentPreset GetActiveEnvironmentPreset()
    {
        return EnvironmentPresets[Mathf.Clamp(previewEnvironmentIndex, 0, EnvironmentPresets.Length - 1)];
    }

    private void ApplyFloorColor(Color color)
    {
        if (floorMaterial == null)
            return;

        if (floorMaterial.HasProperty("_BaseColor"))
            floorMaterial.SetColor("_BaseColor", color);
        if (floorMaterial.HasProperty("_Color"))
            floorMaterial.SetColor("_Color", color);
    }

    private static void ConfigurePreviewLight(Light light, float intensity, Quaternion rotation, Color color)
    {
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.color = color;
        light.transform.rotation = rotation;
    }

    private void RebuildPreviewFloor()
    {
        ClearPreviewFloor();

        if (!showPreviewFloor || previewUtility == null || previewInstance == null)
            return;

        floorInstance = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floorInstance.name = PreviewFloorName;
        floorInstance.transform.SetPositionAndRotation(new Vector3(0f, -FloorYOffset, 0f), Quaternion.identity);

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

    private Material CreatePreviewFloorMaterial()
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

        Color floorColor = GetActiveEnvironmentPreset().FloorColor;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", floorColor);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", floorColor);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 0.18f);

        return material;
    }

    private void CleanupPreviewUtility()
    {
        ClearPreviewFloor();

        if (previewUtility == null)
            return;

        previewUtility.Cleanup();
        previewUtility = null;
    }

    private void UpdatePreviewCameraTransform()
    {
        if (previewUtility == null)
            return;

        Vector3 direction = ResolveCameraDirection();
        Vector3 target = Vector3.zero; // 모든 VFX의 공통 주시점
        float radius = Mathf.Max(frameRadius, 0.5f);
        float distance = Mathf.Max(MinCameraSize, radius * CameraFitPadding / Mathf.Tan(previewUtility.camera.fieldOfView * 0.5f * Mathf.Deg2Rad));
        distance *= cameraDistanceScale;

        previewUtility.camera.transform.position = target + direction * distance;
        previewUtility.camera.transform.rotation = Quaternion.LookRotation(target - previewUtility.camera.transform.position, ResolveCameraUp(direction));
    }

    private Vector3 ResolveCameraDirection()
    {
        float yaw = cameraYaw * Mathf.Deg2Rad;
        float pitch = cameraPitch * Mathf.Deg2Rad;
        float pitchCos = Mathf.Cos(pitch);
        return new Vector3(Mathf.Sin(yaw) * pitchCos, Mathf.Sin(pitch), Mathf.Cos(yaw) * pitchCos).normalized;
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f)
            angle -= 360f;
        else if (angle < -180f)
            angle += 360f;

        return angle;
    }

    private static Vector3 ResolveCameraUp(Vector3 direction)
    {
        float verticalDot = Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up));
        return verticalDot > 0.95f ? Vector3.forward : Vector3.up;
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

        footerStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            normal = { textColor = new Color(0.68f, 0.72f, 0.78f, 1f) }
        };

        footerDiagnosticsStyle = new GUIStyle(footerStyle)
        {
            alignment = TextAnchor.MiddleRight
        };

        footerBrandStyle = new GUIStyle(footerStyle)
        {
            alignment = TextAnchor.MiddleRight,
            fontStyle = FontStyle.Bold
        };
        footerBrandStyle.normal.textColor = new Color(0.78f, 0.86f, 1f, 1f);

        axisLabelStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 11,
            clipping = TextClipping.Clip
        };

        previewOverlayStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            normal = { textColor = new Color(0.82f, 0.87f, 0.94f, 1f) }
        };
    }

    private void ClearPreviewInstance()
    {
        ClearPreviewFloor();
        DestroyTrailProxies();
        EndMotionDragState();

        if (previewInstance != null)
        {
            DestroyImmediate(previewInstance);
            previewInstance = null;
        }

        particleSystems = Array.Empty<ParticleSystem>();
        rootParticleSystems = Array.Empty<ParticleSystem>();
        renderers = Array.Empty<Renderer>();
        liveParticleCount = 0;
        visibleParticleCount = 0;
        previewCenter = Vector3.zero;
        previewRadius = 1f;
        frameCenter = Vector3.zero;
        frameRadius = 1f;
        cameraBlendActive = false;
        previewMessage = string.Empty;
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

    private void RefreshPreviewMessage()
    {
        previewMessage = BuildPreviewMessage();
    }

    private string BuildPreviewMessage()
    {
        if (previewInstance == null)
            return string.Empty;

        if (renderers.Length == 0 && particleSystems.Length == 0)
            return "Renderer 또는 ParticleSystem이 없습니다.";

        if (particleSystems.Length > 0 && visibleParticleCount == 0)
            return isPlaying ? string.Empty : "이 시점에는 화면에 보이는 입자가 없습니다.";

        int activeRendererCount = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                activeRendererCount++;
        }

        return activeRendererCount == 0 && trailProxies.Count == 0 ? "켜진 Renderer가 없습니다." : string.Empty;
    }

    private static void DestroyPreviousPreviewInstances()
    {
        GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < objects.Length; i++)
        {
            GameObject gameObject = objects[i];
            if (gameObject == null)
                continue;

            if (EditorUtility.IsPersistent(gameObject))
                continue;

            if (gameObject.name == LegacyMirrorCameraName
                || gameObject.name == LegacyPreviewCameraName
                || gameObject.name == LegacyPreviewLightName
                || gameObject.name == PreviewFloorName
                || gameObject.name.StartsWith(PreviewInstancePrefix, StringComparison.Ordinal))
                DestroyImmediate(gameObject);
        }
    }

    private static void SetHideFlagsRecursive(GameObject root, HideFlags hideFlags)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i] != null)
                transforms[i].gameObject.hideFlags = hideFlags;
        }
    }

    private static bool TryResolveTransformBounds(Transform root, out Bounds bounds)
    {
        bounds = new Bounds(root.position, Vector3.one);
        bool hasTransform = false;
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform transform = transforms[i];
            if (transform == null)
                continue;

            if (!hasTransform)
            {
                bounds = new Bounds(transform.position, Vector3.one * 0.5f);
                hasTransform = true;
            }
            else
            {
                bounds.Encapsulate(transform.position);
            }
        }

        return hasTransform;
    }

    private bool TryEncapsulateParticles(ParticleSystem particleSystem, ref Bounds bounds, bool hasBounds)
    {
        if (particleSystem == null)
            return false;

        int particleCount = particleSystem.particleCount;
        if (particleCount <= 0)
            return false;

        if (particleBuffer.Length < particleCount)
            particleBuffer = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(particleCount)];

        int count = particleSystem.GetParticles(particleBuffer);
        ParticleSystem.MainModule main = particleSystem.main;
        bool foundParticle = false;

        for (int i = 0; i < count; i++)
        {
            Vector3 position = particleBuffer[i].position;
            if (main.simulationSpace != ParticleSystemSimulationSpace.World)
                position = particleSystem.transform.TransformPoint(position);

            if (!hasBounds && !foundParticle)
            {
                bounds = new Bounds(position, Vector3.one * 0.25f);
                foundParticle = true;
            }
            else
            {
                bounds.Encapsulate(position);
                foundParticle = true;
            }
        }

        return foundParticle;
    }

    private static bool IsPrefabAsset(GameObject gameObject)
    {
        if (gameObject == null)
            return false;

        if (!AssetDatabase.Contains(gameObject))
            return false;

        PrefabAssetType prefabType = PrefabUtility.GetPrefabAssetType(gameObject);
        return prefabType != PrefabAssetType.NotAPrefab && prefabType != PrefabAssetType.MissingAsset;
    }

    private static void DrawCenteredLabel(Rect rect, string text)
    {
        GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.82f, 0.84f, 0.88f, 1f) }
        };
        GUI.Label(rect, text, style);
    }

    private static void DrawPreviewMessage(Rect rect, string text)
    {
        float width = Mathf.Max(120f, Mathf.Min(rect.width - 24f, 420f));
        Rect messageRect = new Rect(rect.x + 12f, rect.y + 12f, width, 28f);
        EditorGUI.DrawRect(messageRect, new Color(0.05f, 0.05f, 0.055f, 0.85f));

        GUIStyle style = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.95f, 0.88f, 0.62f, 1f) }
        };
        GUI.Label(messageRect, text, style);
    }

    private sealed class TrailProxy
    {
        public TrailProxy(TrailRenderer source, LineRenderer line)
        {
            Source = source;
            Line = line;
        }

        public TrailRenderer Source { get; }
        public LineRenderer Line { get; }
        public List<Vector3> Points { get; } = new List<Vector3>(); // 오래된 점 → 새 점
        public List<float> Times { get; } = new List<float>();
        public Vector3[] Buffer { get; set; } = Array.Empty<Vector3>();
    }

    private readonly struct ViewPreset
    {
        public ViewPreset(string label, Vector3 direction)
        {
            Label = label;
            Direction = direction;
        }

        public string Label { get; }
        public Vector3 Direction { get; }
    }

    private readonly struct PreviewEnvironmentPreset
    {
        public PreviewEnvironmentPreset(
            string label,
            Color backgroundColor,
            Color floorColor,
            Color ambientColor,
            Color keyLightColor,
            Color fillLightColor,
            float keyLightIntensity,
            float fillLightIntensity)
        {
            Label = label;
            BackgroundColor = backgroundColor;
            FloorColor = floorColor;
            AmbientColor = ambientColor;
            KeyLightColor = keyLightColor;
            FillLightColor = fillLightColor;
            KeyLightIntensity = keyLightIntensity;
            FillLightIntensity = fillLightIntensity;
        }

        public string Label { get; }
        public Color BackgroundColor { get; }
        public Color FloorColor { get; }
        public Color AmbientColor { get; }
        public Color KeyLightColor { get; }
        public Color FillLightColor { get; }
        public float KeyLightIntensity { get; }
        public float FillLightIntensity { get; }
    }
}
