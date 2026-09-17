using System;
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
    private const float ViewPlaybackSectionHeight = 132f;
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
                    HandlePreviewCameraInput(rect);
                    Texture previewTexture = Event.current.type == EventType.Repaint ? RenderPreviewTexture(rect) : null;
                    if (previewTexture != null)
                        GUI.DrawTexture(rect, previewTexture, ScaleMode.ScaleToFit, false);

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
            + (hasLoopingParticles ? "  ·  반복" : "  ·  원샷");
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
        previewInstance.transform.SetPositionAndRotation(new Vector3(0f, previewHeightOffset, 0f), Quaternion.identity);
        previewInstance.transform.localScale = Vector3.one;
        previewInstance.SetActive(true);
        SetHideFlagsRecursive(previewInstance, HideFlags.HideAndDontSave);
        previewUtility.AddSingleGO(previewInstance);

        particleSystems = previewInstance.GetComponentsInChildren<ParticleSystem>(true);
        rootParticleSystems = ResolveRootParticleSystems(particleSystems);
        renderers = previewInstance.GetComponentsInChildren<Renderer>(true);
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
            if (renderer == null)
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
            if (renderer == null)
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
            AdvancePlayback(deltaTime);
            changed = true;
        }

        changed |= AdvanceCameraBlend(now);

        lastUpdateTime = now;
        if (changed)
            Repaint();
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

        Vector3 previewPosition = new Vector3(0f, previewHeightOffset, 0f);
        if (previewInstance.transform.position != previewPosition)
            previewInstance.transform.position = previewPosition;
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
            + "  ·  우클릭 드래그 회전  ·  휠 확대/축소";
        float width = Mathf.Min(360f, Mathf.Max(220f, previewRect.width - 24f));
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

        return activeRendererCount == 0 ? "켜진 Renderer가 없습니다." : string.Empty;
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
