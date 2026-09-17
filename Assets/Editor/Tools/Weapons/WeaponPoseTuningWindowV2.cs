using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;

public sealed class WeaponPoseTuningWindowV2 : EditorWindow
{
    private const string WindowTitle = "무기 포즈 튜닝 V2";
    private const string MenuPath = "JC Tool/Pose/무기 포즈 튜닝 V2";
    private const string FooterBrandText = "JC Soft";
    private const string PreviewFloorName = "[JC Weapon Pose Preview Floor]";
    private const string PreviewFloorMaterialName = "[JC Weapon Pose Preview Floor Material]";
    private const string BackAnchorName = "BackWeaponAnchor";
    private const float ControlHeight = 24f;
    private const float FieldLabelWidth = 62f;
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
    private const float GridColumnMinWidth = 420f;
    private const float ControlPanelHeight = 236f;
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
    private static readonly Color PreviewFloorColor = new Color(0.38f, 0.395f, 0.405f, 1f);
    private static readonly Color PreviewAmbientColor = new Color(0.38f, 0.41f, 0.46f, 1f);
    private static readonly Color PreviewKeyLightColor = new Color(1f, 0.96f, 0.9f, 1f);
    private static readonly Color PreviewFillLightColor = new Color(0.58f, 0.7f, 1f, 1f);
    private static readonly Color PrefabApplyButtonColor = new Color(1f, 0.62f, 0.28f, 1f);
    private static readonly Vector2 MinimumWindowSize = new Vector2(900f, 860f);

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

    private readonly Dictionary<string, PoseValueSnapshot> poseSnapshots = new Dictionary<string, PoseValueSnapshot>();

    private GameObject sourceModel;
    private GameObject weaponPrefab;
    private WeaponPose sourcePose;
    private GameObject previewInstance;
    private GameObject previewWeaponInstance;
    private GameObject floorInstance;
    private GameObject previewBackAnchor;
    private Material floorMaterial;
    private PreviewRenderUtility previewUtility;
    private WeaponPose previewPose;
    private Animator previewAnimator;
    private PlayableGraph previewPlayableGraph;
    private AnimationClipPlayable previewClipPlayable;
    private AnimationClip previewPlayableClip;
    private AnimationClip activeClip;
    private AnimationClip[] characterClips = Array.Empty<AnimationClip>();
    private string[] characterClipNames = Array.Empty<string>();
    private int selectedCharacterClipIndex = -1;
    private Renderer[] renderers = Array.Empty<Renderer>();
    private WeaponPoseSlot selectedSlot = WeaponPoseSlot.Hold;
    private Vector3 editedPosition;
    private Vector3 editedRotation;
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
    private bool sourceAnimationModeActive;
    private bool poseValuesLoaded;
    private double lastUpdateTime;
    private string previewMessage;
    private Vector3 previewRootAnchorPosition;
    private Quaternion previewRootAnchorRotation = Quaternion.identity;
    private int roundDecimals = 3;

    private GUIStyle headerTitleStyle;
    private GUIStyle headerMetaStyle;
    private GUIStyle sectionStyle;
    private GUIStyle sectionTitleStyle;
    private GUIStyle fieldLabelStyle;
    private GUIStyle compactButtonStyle;
    private GUIStyle noticeStyle;
    private GUIStyle footerStyle;
    private GUIStyle footerBrandStyle;
    private GUIStyle dropZoneStyle;
    private GUIStyle axisLabelStyle;
    private GUIStyle centeredPreviewStyle;

    [MenuItem(MenuPath)]
    private static void Open()
    {
        WeaponPoseTuningWindowV2 window = GetWindow<WeaponPoseTuningWindowV2>(WindowTitle);
        window.minSize = MinimumWindowSize;
        window.TryUseSelection(false);
    }

    private void OnEnable()
    {
        minSize = MinimumWindowSize;
        EnsurePreviewUtility();
        EditorApplication.update += TickPreview;
        ResetPreviewClock();
        TryUseSelection(false);
    }

    private void OnDisable()
    {
        EditorApplication.update -= TickPreview;
        DestroyAnimatorPreviewGraph();
        StopSourceAnimationSampling();
        ClearPreviewInstance();
        CleanupPreviewUtility();
    }

    private void OnSelectionChange()
    {
        TryUseSelection(true);
        Repaint();
    }

    private void OnGUI()
    {
        EnsureStyles();

        DrawHeader();
        DrawControlGrid();
        DrawPreviewAndInspector();
        DrawFooter();
    }

    private void DrawHeader()
    {
        Rect rect = GUILayoutUtility.GetRect(10f, HeaderHeight, GUILayout.ExpandWidth(true));
        Rect backgroundRect = new Rect(rect.x + 2f, rect.y + 4f, rect.width - 4f, rect.height - 8f);
        EditorGUI.DrawRect(backgroundRect, new Color(0.105f, 0.12f, 0.145f, 1f));

        GUI.Label(new Rect(backgroundRect.x + 14f, backgroundRect.y + 7f, backgroundRect.width - 28f, 24f), WindowTitle, headerTitleStyle);

        string modelName = sourceModel != null ? sourceModel.name : "캐릭터 없음";
        string poseName = weaponPrefab != null ? weaponPrefab.name + " / " + GetPoseSlotLabel(selectedSlot) : "무기 없음";
        string clipName = activeClip != null ? activeClip.name : "애니메이션 없음";
        GUI.Label(new Rect(backgroundRect.x + 14f, backgroundRect.y + 35f, backgroundRect.width - 28f, 18f), modelName + "  /  " + poseName + "  /  " + clipName, headerMetaStyle);
    }

    private void DrawControlGrid()
    {
        float availableWidth = Mathf.Max(0f, position.width - GridHorizontalMargin * 2f - SectionGap);
        float columnWidth = ResolveGridColumnWidth(availableWidth);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(GridHorizontalMargin);
            DrawGridPanel("캐릭터", DrawModelPicker, columnWidth, ControlPanelHeight);
            GUILayout.Space(SectionGap);
            DrawGridPanel("무기 / 포즈", DrawPosePicker, columnWidth, ControlPanelHeight);
            GUILayout.Space(GridHorizontalMargin);
        }

        GUILayout.Space(SectionGap);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(GridHorizontalMargin);
            DrawGridPanel("카메라", DrawViewButtons, columnWidth, ControlPanelHeight);
            GUILayout.Space(SectionGap);
            DrawGridPanel("재생", DrawPlaybackControls, columnWidth, ControlPanelHeight);
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

    private static float ResolveGridColumnWidth(float availableWidth)
    {
        return Mathf.Max(GridColumnMinWidth, Mathf.Floor(availableWidth * 0.5f));
    }

    private void DrawModelPicker()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("대상", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));

            Rect modelDropRect = GUILayoutUtility.GetRect(10f, ControlHeight, GUILayout.ExpandWidth(true), GUILayout.Height(ControlHeight));
            string modelLabel = sourceModel != null ? sourceModel.name : "캐릭터 드롭";
            DrawModelDropTarget(modelDropRect, modelLabel);

            GUILayout.Space(RowGap);

            if (GUILayout.Button("비우기", compactButtonStyle, GUILayout.Width(ShortButtonWidth), GUILayout.Height(ControlHeight)))
                SetSourceModel(null);
        }

        EditorGUILayout.Space(5f);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("선택", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));
            if (GUILayout.Button("선택 사용", compactButtonStyle, GUILayout.Width(StandardButtonWidth), GUILayout.Height(ControlHeight)))
                TryUseSelectedModel();

            GUILayout.Space(RowGap);

            if (GUILayout.Button("목록 갱신", compactButtonStyle, GUILayout.Width(StandardButtonWidth), GUILayout.Height(ControlHeight)))
                RefreshCharacterContext(true);

            GUILayout.FlexibleSpace();
        }

        DrawInlineNotice("캐릭터는 포즈 확인용입니다. 장착 무기는 오른쪽 패널에서 프리팹 에셋을 직접 지정합니다.", true);

        DrawModelDropZone();
    }

    private void DrawPosePicker()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("프리팹", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));
            EditorGUI.BeginChangeCheck();
            GameObject nextWeaponPrefab = (GameObject)EditorGUILayout.ObjectField(
                weaponPrefab,
                typeof(GameObject),
                false,
                GUILayout.Height(ControlHeight));
            if (EditorGUI.EndChangeCheck())
                SetWeaponPrefab(nextWeaponPrefab);

            GUILayout.Space(RowGap);
            if (GUILayout.Button("선택 사용", compactButtonStyle, GUILayout.Width(StandardButtonWidth), GUILayout.Height(ControlHeight)))
                TryUseSelectedWeaponPrefab();
        }

        EditorGUILayout.Space(5f);
        DrawInlineNotice("WeaponPose를 가진 장착용 루트 프리팹을 직접 지정합니다. 월드드랍/공용 모델 프리팹은 대상이 아닙니다.", true);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("슬롯", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));
            DrawPoseSlotButton(WeaponPoseSlot.Hold);
            DrawPoseSlotButton(WeaponPoseSlot.Back);
            DrawPoseSlotButton(WeaponPoseSlot.Aim);
            DrawPoseSlotButton(WeaponPoseSlot.Guard);
            GUILayout.FlexibleSpace();
        }

        EditorGUILayout.Space(5f);
        DrawPoseVectorFields();

        EditorGUILayout.Space(5f);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(FieldLabelWidth);
            EditorGUI.BeginDisabledGroup(sourcePose == null);
            if (GUILayout.Button("캡처", compactButtonStyle, GUILayout.Width(ShortButtonWidth), GUILayout.Height(ControlHeight)))
                CaptureCurrentPose();

            if (GUILayout.Button("초기화", compactButtonStyle, GUILayout.Width(ShortButtonWidth), GUILayout.Height(ControlHeight)))
                ResetCurrentPoseToSnapshot();

            if (GUILayout.Button("소수점", compactButtonStyle, GUILayout.Width(ShortButtonWidth), GUILayout.Height(ControlHeight)))
                RoundCurrentPoseValues();

            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = PrefabApplyButtonColor;
            if (GUILayout.Button("프리팹 저장", compactButtonStyle, GUILayout.Width(StandardButtonWidth), GUILayout.Height(ControlHeight)))
                SavePoseToWeaponPrefab();
            GUI.backgroundColor = previousBackground;
            EditorGUI.EndDisabledGroup();
        }
    }

    private void DrawPoseSlotButton(WeaponPoseSlot poseSlot)
    {
        bool isCurrent = selectedSlot == poseSlot;
        Color previousColor = GUI.backgroundColor;
        if (isCurrent)
            GUI.backgroundColor = new Color(0.55f, 0.72f, 0.96f, 1f);

        if (GUILayout.Button(GetPoseSlotLabel(poseSlot), compactButtonStyle, GUILayout.Width(58f), GUILayout.Height(ControlHeight)))
            SetSelectedSlot(poseSlot);

        GUI.backgroundColor = previousColor;
    }

    private void DrawPoseVectorFields()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("위치", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));
            EditorGUI.BeginDisabledGroup(sourcePose == null);
            EditorGUI.BeginChangeCheck();
            Vector3 nextPosition = EditorGUILayout.Vector3Field(GUIContent.none, editedPosition);
            if (EditorGUI.EndChangeCheck())
                SetEditedPose(nextPosition, editedRotation, true);
            EditorGUI.EndDisabledGroup();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("회전", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));
            EditorGUI.BeginDisabledGroup(sourcePose == null);
            EditorGUI.BeginChangeCheck();
            Vector3 nextRotation = EditorGUILayout.Vector3Field(GUIContent.none, editedRotation);
            if (EditorGUI.EndChangeCheck())
                SetEditedPose(editedPosition, nextRotation, true);
            EditorGUI.EndDisabledGroup();
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

    private void DrawPlaybackControls()
    {
        DrawV1ClipSelectionRows();
        EditorGUILayout.Space(4f);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("직접", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));

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
            GUILayout.Label("제어", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));

            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = isPlaying ? new Color(1f, 0.78f, 0.45f, 1f) : new Color(0.56f, 0.84f, 0.62f, 1f);
            if (GUILayout.Button(isPlaying ? "정지" : "재생", compactButtonStyle, GUILayout.Width(PlaybackButtonWidth), GUILayout.Height(ControlHeight)))
                TogglePlayback();
            GUI.backgroundColor = previousBackground;

            if (GUILayout.Button("처음", compactButtonStyle, GUILayout.Width(PlaybackButtonWidth), GUILayout.Height(ControlHeight)))
                RestartPlayback();

            loopPlayback = GUILayout.Toggle(loopPlayback, "반복", compactButtonStyle, GUILayout.Width(ShortButtonWidth), GUILayout.Height(ControlHeight));
            inPlacePreview = GUILayout.Toggle(inPlacePreview, "In-place", compactButtonStyle, GUILayout.Width(86f), GUILayout.Height(ControlHeight));

            GUILayout.FlexibleSpace();
            autoUseAnimationSelection = EditorGUILayout.ToggleLeft("파일 클릭 재생", autoUseAnimationSelection, GUILayout.Width(112f), GUILayout.Height(ControlHeight));
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

    private void DrawV1ClipSelectionRows()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("캐릭터", fieldLabelStyle, GUILayout.Width(FieldLabelWidth));
            EditorGUI.BeginDisabledGroup(characterClipNames.Length == 0);
            EditorGUI.BeginChangeCheck();
            int nextIndex = EditorGUILayout.Popup(Mathf.Max(0, selectedCharacterClipIndex), characterClipNames.Length > 0 ? characterClipNames : new[] { "없음" }, GUILayout.Height(ControlHeight));
            if (EditorGUI.EndChangeCheck() && nextIndex >= 0 && nextIndex < characterClips.Length)
            {
                selectedCharacterClipIndex = nextIndex;
                SetActiveClip(characterClips[selectedCharacterClipIndex], true);
            }
            EditorGUI.EndDisabledGroup();
        }

        DrawInlineNotice("전투 애니메이션은 캐릭터 Animator 목록 또는 직접 선택으로 재생합니다.", true);
    }

    private void DrawPreviewAndInspector()
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
                    DrawCenteredLabel(rect, "캐릭터를 지정하고 무기를 선택하세요.");
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
        float controlGridHeight = ControlPanelHeight * 2f + SectionGap * 2f;
        float availableHeight = position.height - HeaderHeight - controlGridHeight - FooterHeight - 18f;
        return Mathf.Max(MinPreviewHeight, availableHeight);
    }

    private void DrawFooter()
    {
        Rect rect = GUILayoutUtility.GetRect(10f, FooterHeight, GUILayout.ExpandWidth(true));
        Rect statusRect = new Rect(rect.x + 8f, rect.y + 3f, rect.width - 16f, rect.height - 6f);
        EditorGUI.DrawRect(statusRect, new Color(0.12f, 0.13f, 0.145f, 1f));

        string modelName = sourceModel != null ? sourceModel.name : "-";
        string poseName = sourcePose != null ? sourcePose.name + " / " + GetPoseSlotLabel(selectedSlot) : "-";
        string clipName = activeClip != null ? activeClip.name : "-";
        Rect brandRect = new Rect(statusRect.xMax - FooterBrandWidth - 10f, statusRect.y + 3f, FooterBrandWidth, statusRect.height - 4f);
        Rect textRect = new Rect(statusRect.x + 10f, statusRect.y + 3f, Mathf.Max(1f, brandRect.x - statusRect.x - 18f), statusRect.height - 4f);
        GUI.Label(textRect, "모델 " + modelName + "   포즈 " + poseName + "   애니메이션 " + clipName + "   Renderer " + renderers.Length, footerStyle);
        GUI.Label(brandRect, FooterBrandText, footerBrandStyle);
    }

    private void DrawModelDropZone()
    {
        EditorGUILayout.Space(6f);

        Rect rect = GUILayoutUtility.GetRect(10f, FooterHeight, GUILayout.ExpandWidth(true));
        string label = sourceModel == null ? "캐릭터 프리팹 / 오브젝트 드롭" : "다른 캐릭터로 교체";
        GUI.Label(rect, label, dropZoneStyle);
        HandleModelDrop(rect);
    }

    private void DrawModelDropTarget(Rect rect, string label)
    {
        GUIContent content = sourceModel != null ? EditorGUIUtility.ObjectContent(sourceModel, typeof(GameObject)) : new GUIContent(label);
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
            SetSourceModel(draggedModel);
        }

        current.Use();
    }

    private void SetSourceModel(GameObject nextModel)
    {
        if (sourceModel == nextModel)
            return;

        StopSourceAnimationSampling();
        sourceModel = nextModel;

        if (sourceModel == null)
        {
            characterClips = Array.Empty<AnimationClip>();
            characterClipNames = Array.Empty<string>();
            selectedCharacterClipIndex = -1;
            ClearPreviewInstance();
            Repaint();
            return;
        }

        RefreshCharacterContext(false);
        RebuildPreviewInstance();
        SampleActiveClip();
        Repaint();
    }

    private void SetWeaponPrefab(GameObject nextPrefab)
    {
        if (weaponPrefab == nextPrefab)
            return;

        if (nextPrefab != null && !IsWeaponRootPrefab(nextPrefab))
        {
            EditorUtility.DisplayDialog(
                WindowTitle,
                "WeaponPose를 가진 장착용 .prefab 에셋만 지정할 수 있습니다.",
                "확인");
            return;
        }

        weaponPrefab = nextPrefab;
        sourcePose = FindWeaponPose(weaponPrefab);
        poseSnapshots.Clear();
        LoadPoseValuesFromSource();
        RebuildPreviewInstance();
        SampleActiveClip();
        Repaint();
    }

    private void SetSelectedSlot(WeaponPoseSlot poseSlot)
    {
        if (selectedSlot == poseSlot)
            return;

        selectedSlot = poseSlot;
        LoadPoseValuesFromSource();
        ApplyEditedPoseToPreview();
        Repaint();
    }

    private void SetEditedPose(Vector3 localPosition, Vector3 localRotation, bool recordUndo)
    {
        editedPosition = localPosition;
        editedRotation = NormalizeEuler(localRotation);
        poseValuesLoaded = true;
        ApplyEditedPoseToSource(recordUndo);
        ApplyEditedPoseToPreview();
        Repaint();
    }

    private void LoadPoseValuesFromSource()
    {
        if (sourcePose == null)
        {
            poseValuesLoaded = false;
            return;
        }

        RememberPoseSnapshot(sourcePose, selectedSlot);
        editedPosition = sourcePose.GetPoseLocalPositionForTuning(selectedSlot);
        editedRotation = sourcePose.GetPoseLocalRotationForTuning(selectedSlot);
        poseValuesLoaded = true;
    }

    private void ApplyEditedPoseToSource(bool recordUndo)
    {
        if (sourcePose == null || !poseValuesLoaded)
            return;

        if (recordUndo)
            Undo.RecordObject(sourcePose, "무기 포즈 V2 수치 조정");

        SetPoseSerialized(sourcePose, selectedSlot, editedPosition, editedRotation);
        EditorUtility.SetDirty(sourcePose);
    }

    private void ApplyEditedPoseToPreview()
    {
        if (previewPose == null || !poseValuesLoaded)
            return;

        SetPoseSerialized(previewPose, selectedSlot, editedPosition, editedRotation);
        EnsurePreviewBackAnchor();
        previewPose.PreviewPoseInstant(selectedSlot);
        RefreshRendererCache();
    }

    private void CaptureCurrentPose()
    {
        if (sourcePose == null || previewPose == null)
            return;

        Transform previewTarget = previewPose.GetPoseTargetForTuning();
        if (previewTarget == null)
            return;

        SetEditedPose(
            previewTarget.localPosition,
            NormalizeEuler(previewTarget.localEulerAngles),
            true);
    }

    private void ResetCurrentPoseToSnapshot()
    {
        if (sourcePose == null)
            return;

        string key = GetPoseSnapshotKey(sourcePose, selectedSlot);
        if (!poseSnapshots.TryGetValue(key, out PoseValueSnapshot snapshot) || snapshot == null)
            return;

        SetEditedPose(snapshot.LocalPosition, snapshot.LocalRotation, true);
    }

    private void RoundCurrentPoseValues()
    {
        if (sourcePose == null)
            return;

        SetEditedPose(RoundVector(editedPosition, roundDecimals), RoundVector(editedRotation, roundDecimals), true);
    }

    private void SavePoseToWeaponPrefab()
    {
        if (sourcePose == null || weaponPrefab == null)
            return;

        isPlaying = false;

        string assetPath = AssetDatabase.GetAssetPath(weaponPrefab);
        string message = "현재 슬롯의 위치 / 회전 좌표가 저장됩니다.\n\n"
            + "장착 프리팹: " + weaponPrefab.name + "\n"
            + "슬롯: " + GetPoseSlotLabel(selectedSlot) + "\n"
            + "위치: " + FormatVector(editedPosition) + "\n"
            + "회전: " + FormatVector(editedRotation) + "\n\n"
            + "저장 위치:\n" + assetPath + "\n\n"
            + "확인을 누르면 지정한 프리팹 에셋의 WeaponPose 값이 저장됩니다.";

        if (!EditorUtility.DisplayDialog("무기 포즈 좌표 저장 경고", message, "확인 후 저장", "취소"))
            return;

        EditorUtility.SetDirty(sourcePose);
        AssetDatabase.SaveAssets();
        RefreshPoseSnapshot(sourcePose, selectedSlot);
    }

    private void RememberPoseSnapshot(WeaponPose pose, WeaponPoseSlot poseSlot)
    {
        if (pose == null)
            return;

        string key = GetPoseSnapshotKey(pose, poseSlot);
        if (poseSnapshots.ContainsKey(key))
            return;

        poseSnapshots[key] = new PoseValueSnapshot
        {
            LocalPosition = pose.GetPoseLocalPositionForTuning(poseSlot),
            LocalRotation = pose.GetPoseLocalRotationForTuning(poseSlot)
        };
    }

    private void RefreshPoseSnapshot(WeaponPose pose, WeaponPoseSlot poseSlot)
    {
        if (pose == null)
            return;

        poseSnapshots[GetPoseSnapshotKey(pose, poseSlot)] = new PoseValueSnapshot
        {
            LocalPosition = pose.GetPoseLocalPositionForTuning(poseSlot),
            LocalRotation = pose.GetPoseLocalRotationForTuning(poseSlot)
        };
    }

    private static string GetPoseSnapshotKey(WeaponPose pose, WeaponPoseSlot poseSlot)
    {
        return pose.GetInstanceID() + ":" + poseSlot;
    }

    private void TryUseSelectedModel()
    {
        GameObject selectedObject = Selection.activeGameObject;
        if (selectedObject != null && IsSupportedModel(selectedObject))
            SetSourceModel(selectedObject);
    }

    private void TryUseSelectedWeaponPrefab()
    {
        GameObject selectedObject = Selection.activeGameObject;
        GameObject prefabAsset = ResolveSelectedWeaponPrefab(selectedObject);
        if (prefabAsset != null)
            SetWeaponPrefab(prefabAsset);
    }

    private void RefreshCharacterContext(bool rebuildPreview)
    {
        RefreshCharacterAnimationClips();

        if (rebuildPreview)
        {
            RebuildPreviewInstance();
            SampleActiveClip();
            Repaint();
        }
    }

    private void RefreshCharacterAnimationClips()
    {
        List<AnimationClip> clips = new List<AnimationClip>();

        Animator animator = GetCharacterAnimator();
        RuntimeAnimatorController controller = animator != null ? animator.runtimeAnimatorController : null;
        if (controller != null)
        {
            AnimationClip[] controllerClips = controller.animationClips;
            for (int i = 0; i < controllerClips.Length; i++)
            {
                AnimationClip clip = controllerClips[i];
                if (IsUsableAnimationClip(clip) && !clips.Contains(clip))
                    clips.Add(clip);
            }
        }

        characterClips = clips.ToArray();
        characterClipNames = new string[characterClips.Length];
        for (int i = 0; i < characterClips.Length; i++)
            characterClipNames[i] = characterClips[i].name;

        selectedCharacterClipIndex = FindClipIndex(characterClips, activeClip);
        if (selectedCharacterClipIndex < 0 && characterClips.Length > 0)
            selectedCharacterClipIndex = 0;

        if (activeClip == null && characterClips.Length > 0)
            activeClip = characterClips[selectedCharacterClipIndex];
    }

    private void TryUseSelection(bool animationOnly)
    {
        UnityEngine.Object[] selectedObjects = Selection.objects;
        if (selectedObjects == null || selectedObjects.Length == 0)
            return;

        bool changed = false;

        for (int i = 0; i < selectedObjects.Length; i++)
        {
            if (autoUseAnimationSelection && TryResolveAnimationClip(selectedObjects[i], out AnimationClip clip))
            {
                SetActiveClip(clip, true);
                changed = true;
                continue;
            }

            if (animationOnly)
                continue;

            if (selectedObjects[i] is GameObject gameObject)
            {
                GameObject selectedWeaponPrefab = ResolveSelectedWeaponPrefab(gameObject);
                if (selectedWeaponPrefab != null)
                {
                    SetWeaponPrefab(selectedWeaponPrefab);
                    changed = true;
                    continue;
                }

                if (IsSupportedModel(gameObject))
                {
                    SetSourceModel(gameObject);
                    changed = true;
                }
            }
        }

        if (changed)
            Repaint();
    }

    private void TryUseSelectedAnimation()
    {
        UnityEngine.Object selected = Selection.activeObject;
        if (TryResolveAnimationClip(selected, out AnimationClip clip))
            SetActiveClip(clip, true);
    }

    private void SetActiveClip(AnimationClip clip, bool restart)
    {
        if (activeClip == clip && !restart)
            return;

        activeClip = clip;
        SyncSelectedClipIndices();
        DestroyAnimatorPreviewGraph();

        if (activeClip == null)
        {
            isPlaying = false;
            playbackTime = 0f;
            StopSourceAnimationSampling();
            return;
        }

        playbackTime = restart ? 0f : Mathf.Clamp(playbackTime, 0f, GetActiveClipLength());
        isPlaying = restart;
        SampleActiveClip();
        Repaint();
    }

    private void SyncSelectedClipIndices()
    {
        selectedCharacterClipIndex = FindClipIndex(characterClips, activeClip);
        if (selectedCharacterClipIndex < 0 && characterClips.Length > 0)
            selectedCharacterClipIndex = Mathf.Clamp(selectedCharacterClipIndex, -1, characterClips.Length - 1);
    }

    private static int FindClipIndex(AnimationClip[] clips, AnimationClip clip)
    {
        if (clips == null || clip == null)
            return -1;

        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] == clip)
                return i;
        }

        return -1;
    }

    private void TogglePlayback()
    {
        if (activeClip == null)
            return;

        isPlaying = !isPlaying;
        if (isPlaying && playbackTime >= GetActiveClipLength())
            playbackTime = 0f;

        ResetPreviewClock();
    }

    private void RestartPlayback()
    {
        playbackTime = 0f;
        isPlaying = activeClip != null;
        SampleActiveClip();
        ResetPreviewClock();
        Repaint();
    }

    private void TickPreview()
    {
        double now = EditorApplication.timeSinceStartup;
        if (lastUpdateTime <= 0d)
            lastUpdateTime = now;

        float deltaTime = Mathf.Max(0f, (float)(now - lastUpdateTime));
        if (deltaTime < PreviewTickInterval && !cameraBlendActive && !isPlaying)
            return;

        lastUpdateTime = now;
        bool changed = AdvanceCameraBlend(now);

        if (isPlaying && activeClip != null)
        {
            float clipLength = GetActiveClipLength();
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
            changed = true;
        }

        if (changed)
            Repaint();
    }

    private void ResetPreviewClock()
    {
        lastUpdateTime = EditorApplication.timeSinceStartup;
    }

    private void SampleActiveClip()
    {
        SampleSourceAnimationClip();

        if (previewInstance == null || activeClip == null)
        {
            ApplyEditedPoseToPreview();
            return;
        }

        EvaluateAnimatorPreviewClip();
    }

    private void SampleSourceAnimationClip()
    {
        if (sourceModel == null || activeClip == null || EditorUtility.IsPersistent(sourceModel))
            return;

        try
        {
            if (!AnimationMode.InAnimationMode())
                AnimationMode.StartAnimationMode();

            sourceAnimationModeActive = true;
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(sourceModel, activeClip, Mathf.Clamp(playbackTime, 0f, GetActiveClipLength()));
            AnimationMode.EndSampling();
        }
        catch (Exception exception)
        {
            sourceAnimationModeActive = false;
            previewMessage = "Scene 애니메이션 샘플링 오류: " + exception.GetType().Name;
        }
    }

    private void StopSourceAnimationSampling()
    {
        if (!sourceAnimationModeActive)
            return;

        sourceAnimationModeActive = false;
        if (AnimationMode.InAnimationMode())
            AnimationMode.StopAnimationMode();
    }

    private void EvaluateAnimatorPreviewClip()
    {
        if (!EnsureAnimatorPreviewGraph())
            return;

        try
        {
            previewAnimator.applyRootMotion = !inPlacePreview;
            previewClipPlayable.SetTime(Mathf.Clamp(playbackTime, 0f, GetActiveClipLength()));
            previewClipPlayable.SetSpeed(0d);
            previewPlayableGraph.Evaluate(0f);
            ApplyInPlacePreview();
            ApplyEditedPoseToPreview();
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

            previewPlayableGraph = PlayableGraph.Create("JC Weapon Pose Tuning V2 Preview");
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

    private void ApplyInPlacePreview()
    {
        if (!inPlacePreview || previewInstance == null)
            return;

        previewInstance.transform.SetPositionAndRotation(previewRootAnchorPosition, previewRootAnchorRotation);
    }

    private void RebuildPreviewInstance()
    {
        ClearPreviewInstance();

        if (sourceModel == null || weaponPrefab == null || sourcePose == null)
        {
            RefreshRendererCache();
            return;
        }

        EnsurePreviewUtility();

        previewInstance = Instantiate(sourceModel);
        previewInstance.name = "[JC Weapon Pose Preview] " + sourceModel.name;
        previewInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        previewInstance.transform.localScale = ResolvePreviewRootScale(sourceModel);
        previewRootAnchorPosition = previewInstance.transform.position;
        previewRootAnchorRotation = previewInstance.transform.rotation;
        previewInstance.SetActive(true);

        Transform weaponSocket = ResolvePreviewWeaponSocket(previewInstance);
        if (weaponSocket == null)
        {
            previewMessage = P09CharacterVisualAdapter.RightHandWeaponSocketName + " 소켓을 찾을 수 없습니다.";
            DisableRuntimeBehaviours(previewInstance);
            SetHideFlagsRecursive(previewInstance, HideFlags.HideAndDontSave);
            previewUtility.AddSingleGO(previewInstance);
            RefreshRendererCache();
            RefitCameraFromCurrentPose();
            return;
        }

        previewWeaponInstance = Instantiate(weaponPrefab);
        previewWeaponInstance.name = "[JC Preview Weapon] " + weaponPrefab.name;
        previewWeaponInstance.transform.SetParent(weaponSocket, false);
        previewWeaponInstance.transform.localPosition = Vector3.zero;
        previewWeaponInstance.transform.localRotation = Quaternion.identity;
        previewWeaponInstance.transform.localScale = weaponPrefab.transform.localScale;
        previewWeaponInstance.SetActive(true);

        previewPose = FindWeaponPose(previewWeaponInstance);
        if (previewPose == null)
            previewMessage = "지정한 장착 프리팹에 WeaponPose가 없습니다.";
        else
            previewMessage = null;

        DisableRuntimeBehaviours(previewInstance);
        SetHideFlagsRecursive(previewInstance, HideFlags.HideAndDontSave);
        previewUtility.AddSingleGO(previewInstance);

        ClearPreviewRuntimeReferences(previewPose);
        ApplyEditedPoseToPreview();
        RefreshRendererCache();
        RefitCameraFromCurrentPose();
    }

    private void ClearPreviewInstance()
    {
        DestroyAnimatorPreviewGraph();
        ClearPreviewFloor();

        if (previewBackAnchor != null)
        {
            DestroyImmediate(previewBackAnchor);
            previewBackAnchor = null;
        }

        if (previewInstance != null)
        {
            DestroyImmediate(previewInstance);
            previewInstance = null;
        }

        previewWeaponInstance = null;
        previewPose = null;
        previewMessage = null;
        RefreshRendererCache();
    }

    private static Transform ResolvePreviewWeaponSocket(GameObject characterRoot)
    {
        if (characterRoot == null)
            return null;

        MonoBehaviour[] behaviours = characterRoot.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (!(behaviours[i] is ICharacterWeaponSocketProvider provider))
                continue;

            Transform socket = provider.GetWeaponSocket(null);
            if (socket != null)
                return socket;
        }

        Transform namedSocket = FindDeepChild(characterRoot.transform, P09CharacterVisualAdapter.RightHandWeaponSocketName);
        if (namedSocket != null)
            return namedSocket;

        Animator animator = characterRoot.GetComponentInChildren<Animator>(true);
        return animator != null && animator.isHuman
            ? animator.GetBoneTransform(HumanBodyBones.RightHand)
            : null;
    }

    private static Transform FindDeepChild(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
            return null;

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i] != null && transforms[i].name == childName)
                return transforms[i];
        }

        return null;
    }

    private void EnsurePreviewBackAnchor()
    {
        if (previewInstance == null || previewPose == null)
            return;

        if (previewInstance.transform.Find(BackAnchorName) != null)
            return;

        previewBackAnchor = new GameObject(BackAnchorName);
        previewBackAnchor.hideFlags = HideFlags.HideAndDontSave;
        previewBackAnchor.transform.SetParent(previewInstance.transform, false);

        Vector3 anchorPosition = sourcePose != null ? GetSerializedVector3(sourcePose, "backAnchorLocalPosition") : new Vector3(-0.18f, 1.28f, -0.32f);
        Vector3 anchorRotation = sourcePose != null ? GetSerializedVector3(sourcePose, "backAnchorLocalRotation") : Vector3.zero;
        previewBackAnchor.transform.localPosition = anchorPosition;
        previewBackAnchor.transform.localRotation = Quaternion.Euler(anchorRotation);
    }

    private void RefreshRendererCache()
    {
        renderers = previewInstance != null ? previewInstance.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
    }

    private static void ClearPreviewRuntimeReferences(WeaponPose pose)
    {
        if (pose == null)
            return;

        SerializedObject serializedObject = new SerializedObject(pose);
        SerializedProperty playerControllerProperty = serializedObject.FindProperty("playerController");
        SerializedProperty playerEquipmentProperty = serializedObject.FindProperty("playerEquipment");

        if (playerControllerProperty != null)
            playerControllerProperty.objectReferenceValue = null;
        if (playerEquipmentProperty != null)
            playerEquipmentProperty.objectReferenceValue = null;

        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void DisableRuntimeBehaviours(GameObject root)
    {
        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
            behaviours[i].enabled = false;
    }

    private void RefitCameraFromCurrentPose()
    {
        RefreshRendererCache();

        if (renderers.Length == 0)
        {
            frameCenter = Vector3.up;
            frameRadius = 1f;
            frameBottomY = 0f;
            RebuildPreviewFloor();
            UpdatePreviewCameraTransform();
            return;
        }

        Bounds bounds = default;
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

        if (!hasBounds)
        {
            frameCenter = Vector3.up;
            frameRadius = 1f;
            frameBottomY = 0f;
        }
        else
        {
            frameCenter = bounds.center;
            frameRadius = Mathf.Max(MinCameraRadius, bounds.extents.magnitude);
            frameBottomY = bounds.min.y;
        }

        RebuildPreviewFloor();
        UpdatePreviewCameraTransform();
    }

    private Texture RenderPreviewTexture(Rect rect)
    {
        if (previewInstance == null)
        {
            previewMessage = "캐릭터를 지정하세요.";
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
                {
                    // PreviewRenderUtility can be mid-cleanup after a render exception.
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

    private static void SetPoseSerialized(WeaponPose pose, WeaponPoseSlot poseSlot, Vector3 localPosition, Vector3 localRotation)
    {
        if (pose == null)
            return;

        SerializedObject serializedObject = new SerializedObject(pose);
        SerializedProperty positionProperty = serializedObject.FindProperty(GetPosePositionPropertyName(poseSlot));
        SerializedProperty rotationProperty = serializedObject.FindProperty(GetPoseRotationPropertyName(poseSlot));

        if (positionProperty != null)
            positionProperty.vector3Value = localPosition;
        if (rotationProperty != null)
            rotationProperty.vector3Value = NormalizeEuler(localRotation);

        serializedObject.ApplyModifiedProperties();
    }

    private static Vector3 GetSerializedVector3(WeaponPose pose, string propertyName)
    {
        if (pose == null)
            return Vector3.zero;

        SerializedObject serializedObject = new SerializedObject(pose);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        return property != null ? property.vector3Value : Vector3.zero;
    }

    private static string GetPosePositionPropertyName(WeaponPoseSlot poseSlot)
    {
        switch (poseSlot)
        {
            case WeaponPoseSlot.Back:
                return "backLocalPosition";
            case WeaponPoseSlot.Aim:
                return "aimLocalPosition";
            case WeaponPoseSlot.Guard:
                return "guardLocalPosition";
            default:
                return "holdLocalPosition";
        }
    }

    private static string GetPoseRotationPropertyName(WeaponPoseSlot poseSlot)
    {
        switch (poseSlot)
        {
            case WeaponPoseSlot.Back:
                return "backLocalRotation";
            case WeaponPoseSlot.Aim:
                return "aimLocalRotation";
            case WeaponPoseSlot.Guard:
                return "guardLocalRotation";
            default:
                return "holdLocalRotation";
        }
    }

    private Animator GetCharacterAnimator()
    {
        if (sourceModel == null)
            return null;

        Animator animator = sourceModel.GetComponent<Animator>();
        if (animator != null)
            return animator;

        return sourceModel.GetComponentInChildren<Animator>(true);
    }

    private static WeaponPose FindWeaponPose(GameObject targetObject)
    {
        if (targetObject == null)
            return null;

        WeaponPose pose = targetObject.GetComponent<WeaponPose>();
        if (pose != null)
            return pose;

        pose = targetObject.GetComponentInChildren<WeaponPose>(true);
        if (pose != null)
            return pose;

        return targetObject.GetComponentInParent<WeaponPose>();
    }

    private static GameObject ResolveSelectedWeaponPrefab(GameObject selectedObject)
    {
        if (selectedObject == null || !EditorUtility.IsPersistent(selectedObject))
            return null;

        string assetPath = AssetDatabase.GetAssetPath(selectedObject);
        if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            return null;

        GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        return IsWeaponRootPrefab(prefabRoot) ? prefabRoot : null;
    }

    private static bool IsWeaponRootPrefab(GameObject prefab)
    {
        if (prefab == null || !EditorUtility.IsPersistent(prefab))
            return false;

        string assetPath = AssetDatabase.GetAssetPath(prefab);
        return !string.IsNullOrEmpty(assetPath)
            && assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
            && PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.NotAPrefab
            && FindWeaponPose(prefab) != null;
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

        string assetPath = AssetDatabase.GetAssetPath(source);
        if (string.IsNullOrEmpty(assetPath))
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
        return gameObject != null
            && (IsPrefabAsset(gameObject) || IsSceneObject(gameObject))
            && gameObject.GetComponentInChildren<Animator>(true) != null;
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

    private static void SetHideFlagsRecursive(GameObject root, HideFlags hideFlags)
    {
        if (root == null)
            return;

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
            transforms[i].gameObject.hideFlags = hideFlags;
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

    private static string GetPoseSlotLabel(WeaponPoseSlot poseSlot)
    {
        switch (poseSlot)
        {
            case WeaponPoseSlot.Back:
                return "등";
            case WeaponPoseSlot.Aim:
                return "조준";
            case WeaponPoseSlot.Guard:
                return "막기";
            default:
                return "손";
        }
    }

    private static float GetActiveClipLength(AnimationClip clip)
    {
        return clip != null ? Mathf.Max(0.001f, clip.length) : 1f;
    }

    private float GetActiveClipLength()
    {
        return GetActiveClipLength(activeClip);
    }

    private static Vector3 RoundVector(Vector3 value, int decimals)
    {
        return new Vector3(
            RoundFloat(value.x, decimals),
            RoundFloat(value.y, decimals),
            RoundFloat(value.z, decimals));
    }

    private static string FormatVector(Vector3 value)
    {
        return "("
            + value.x.ToString("0.###") + ", "
            + value.y.ToString("0.###") + ", "
            + value.z.ToString("0.###") + ")";
    }

    private static float RoundFloat(float value, int decimals)
    {
        float multiplier = Mathf.Pow(10f, Mathf.Clamp(decimals, 0, 4));
        return Mathf.Round(value * multiplier) / multiplier;
    }

    private static Vector3 NormalizeEuler(Vector3 euler)
    {
        return new Vector3(NormalizeSignedAngle(euler.x), NormalizeSignedAngle(euler.y), NormalizeSignedAngle(euler.z));
    }

    private static float NormalizeSignedAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f)
            angle -= 360f;
        if (angle < -180f)
            angle += 360f;

        return angle;
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle < 0f)
            angle += 360f;

        return angle;
    }

    private static string FormatTime(float seconds)
    {
        return seconds.ToString("0.00") + "초";
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

    private sealed class PoseValueSnapshot
    {
        public Vector3 LocalPosition;
        public Vector3 LocalRotation;
    }

    private readonly struct ViewPreset
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
