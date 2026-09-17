using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class WeaponPoseTuningWindow : EditorWindow
{
    private enum PoseSceneHandleMode
    {
        Move,
        Rotate
    }

    private class WeaponPoseCandidate
    {
        public string DisplayName;
        public GameObject RootObject;
        public WeaponPose Pose;
    }

    private class ClipCandidate
    {
        public string DisplayName;
        public AnimationClip Clip;
    }

    private class TransformSnapshot
    {
        public Transform Parent;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public Vector3 LocalScale;
    }

    private class PoseValueSnapshot
    {
        public Vector3 LocalPosition;
        public Vector3 LocalRotation;
    }

    private GameObject characterObject;
    private GameObject weaponObject;
    private WeaponPose targetPose;
    private WeaponItemData weaponData;
    private WeaponPoseSlot selectedSlot = WeaponPoseSlot.Back;
    private AnimationClip previewClip;
    private AnimationClip[] characterClips = new AnimationClip[0];
    private string[] characterClipNames = new string[0];
    private int selectedClipIndex = -1;
    private AnimationClip[] weaponClips = new AnimationClip[0];
    private string[] weaponClipNames = new string[0];
    private int selectedWeaponClipIndex = -1;
    private float previewNormalizedTime;
    private bool loopPreview = true;
    private bool isPlayingPreview;
    private double playStartEditorTime;
    private float playStartNormalizedTime;
    private float previewPlaybackSpeed = 1f;
    private bool sceneHandleEnabled = true;
    private PoseSceneHandleMode sceneHandleMode = PoseSceneHandleMode.Move;
    private int roundDecimals = 3;
    private readonly List<WeaponPoseCandidate> rightHandWeapons = new List<WeaponPoseCandidate>();
    private readonly List<WeaponPoseCandidate> leftHandWeapons = new List<WeaponPoseCandidate>();
    private readonly Dictionary<Transform, TransformSnapshot> transformSnapshots = new Dictionary<Transform, TransformSnapshot>();
    private readonly Dictionary<GameObject, bool> activeSnapshots = new Dictionary<GameObject, bool>();
    private readonly Dictionary<string, PoseValueSnapshot> poseValueSnapshots = new Dictionary<string, PoseValueSnapshot>();

    [MenuItem("JC Tool/Pose/무기 포즈 튜닝")]
    public static void Open()
    {
        GetWindow<WeaponPoseTuningWindow>("무기 포즈");
    }

    private void OnEnable()
    {
        EditorApplication.update += UpdateAnimationPlayback;
        SceneView.duringSceneGui += DrawScenePoseHandles;
    }

    private void OnGUI()
    {
        DrawTargetControls();
        DrawPoseControls();
        DrawAnimationPreviewControls();
        DrawCurrentTransformInfo();
    }

    private void DrawTargetControls()
    {
        EditorGUILayout.LabelField("1. 대상 선택", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        characterObject = (GameObject)EditorGUILayout.ObjectField("캐릭터", characterObject, typeof(GameObject), true);
        if (EditorGUI.EndChangeCheck())
            RefreshCharacterContext();

        EditorGUI.BeginChangeCheck();
        weaponObject = (GameObject)EditorGUILayout.ObjectField("무기", weaponObject, typeof(GameObject), true);
        if (EditorGUI.EndChangeCheck())
        {
            targetPose = FindWeaponPose(weaponObject);
            weaponData = FindWeaponDataForWeaponObject(weaponObject);
            RefreshWeaponAnimationClips();
        }

        targetPose = (WeaponPose)EditorGUILayout.ObjectField("무기 포즈", targetPose, typeof(WeaponPose), true);

        EditorGUI.BeginChangeCheck();
        weaponData = (WeaponItemData)EditorGUILayout.ObjectField("무기 데이터", weaponData, typeof(WeaponItemData), false);
        if (EditorGUI.EndChangeCheck())
            RefreshWeaponAnimationClips();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("선택 항목 자동 연결"))
                UseSelectionAsTargets();

            if (GUILayout.Button("캐릭터 애니메이션 새로고침"))
                RefreshCharacterContext();
        }

        DrawHandWeaponLists();
    }

    private void DrawPoseControls()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("2. 포즈 조정", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        WeaponPoseSlot nextSlot = (WeaponPoseSlot)EditorGUILayout.EnumPopup("포즈 슬롯", selectedSlot);
        if (EditorGUI.EndChangeCheck())
        {
            selectedSlot = nextSlot;
            PreviewPose(selectedSlot, false);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            DrawPreviewButton(WeaponPoseSlot.Back);
            DrawPreviewButton(WeaponPoseSlot.Hold);
            DrawPreviewButton(WeaponPoseSlot.Aim);
            DrawPreviewButton(WeaponPoseSlot.Guard);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("현재 값 캡처"))
                CaptureCurrentPose();

            if (GUILayout.Button("프리팹에 적용"))
                ApplyPoseOverrideToPrefab();
        }

        EditorGUILayout.Space(8f);
    }

    private void OnDisable()
    {
        EditorApplication.update -= UpdateAnimationPlayback;
        SceneView.duringSceneGui -= DrawScenePoseHandles;

        RestorePreviewState();
    }

    private void DrawPreviewButton(WeaponPoseSlot poseSlot)
    {
        if (GUILayout.Button("미리보기: " + GetPoseSlotLabel(poseSlot)))
            PreviewPose(poseSlot, true);
    }

    private string GetPoseSlotLabel(WeaponPoseSlot poseSlot)
    {
        switch (poseSlot)
        {
            case WeaponPoseSlot.Hold:
                return "손";
            case WeaponPoseSlot.Back:
                return "등";
            case WeaponPoseSlot.Aim:
                return "조준";
            case WeaponPoseSlot.Guard:
                return "막기";
            default:
                return poseSlot.ToString();
        }
    }

    private void PreviewPose(WeaponPoseSlot poseSlot, bool recordUndo)
    {
        if (targetPose == null)
            return;

        RememberPoseTransform(targetPose);

        if (recordUndo)
            Undo.RecordObject(targetPose, "무기 포즈 미리보기");

        selectedSlot = poseSlot;
        targetPose.PreviewPoseInstant(poseSlot);

        if (recordUndo)
            EditorUtility.SetDirty(targetPose);

        SceneView.RepaintAll();
    }

    private void CaptureCurrentPose()
    {
        if (targetPose == null)
            return;

        RememberPoseValueSnapshot(targetPose, selectedSlot);
        Undo.RecordObject(targetPose, "무기 포즈 캡처");
        targetPose.CaptureCurrentTransformAsPose(selectedSlot);
        EditorUtility.SetDirty(targetPose);
        PrefabUtility.RecordPrefabInstancePropertyModifications(targetPose);
    }

    private void ApplyPoseOverrideToPrefab()
    {
        if (targetPose == null)
            return;

        StopAnimationPlayback(true);

        string assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(targetPose.gameObject);
        if (string.IsNullOrEmpty(assetPath))
        {
            EditorUtility.SetDirty(targetPose);
            AssetDatabase.SaveAssets();
            RefreshPoseValueSnapshot(targetPose, selectedSlot);
            return;
        }

        PrefabUtility.RecordPrefabInstancePropertyModifications(targetPose);
        PrefabUtility.ApplyObjectOverride(targetPose, assetPath, InteractionMode.UserAction);
        AssetDatabase.SaveAssets();
        RefreshPoseValueSnapshot(targetPose, selectedSlot);
    }

    private void DrawAnimationPreviewControls()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("3. 애니메이션 미리보기", EditorStyles.boldLabel);

        if (characterClipNames.Length == 0)
        {
            EditorGUILayout.HelpBox("캐릭터를 선택하면 Animator Controller의 애니메이션 목록이 여기에 표시됩니다.", MessageType.Info);
        }
        else
        {
            int nextIndex = EditorGUILayout.Popup("캐릭터 애니메이션", Mathf.Max(0, selectedClipIndex), characterClipNames);
            if (nextIndex != selectedClipIndex && nextIndex >= 0 && nextIndex < characterClips.Length)
            {
                selectedClipIndex = nextIndex;
                previewClip = characterClips[selectedClipIndex];
                StopAnimationPlayback(false);
            }
        }

        if (weaponClipNames.Length == 0)
        {
            EditorGUILayout.HelpBox("무기 데이터를 선택하면 무기 전용 런타임 애니메이션 목록이 여기에 표시됩니다.", MessageType.Info);
        }
        else
        {
            int nextWeaponIndex = EditorGUILayout.Popup("현재 무기 전용 애니메이션", Mathf.Max(0, selectedWeaponClipIndex), weaponClipNames);
            if (nextWeaponIndex != selectedWeaponClipIndex && nextWeaponIndex >= 0 && nextWeaponIndex < weaponClips.Length)
            {
                selectedWeaponClipIndex = nextWeaponIndex;
                previewClip = weaponClips[selectedWeaponClipIndex];
                StopAnimationPlayback(false);
            }
        }

        previewClip = (AnimationClip)EditorGUILayout.ObjectField("직접 선택 클립", previewClip, typeof(AnimationClip), false);
        previewNormalizedTime = EditorGUILayout.Slider("정규화 시간", previewNormalizedTime, 0f, 1f);
        previewPlaybackSpeed = EditorGUILayout.Slider("미리보기 속도", previewPlaybackSpeed, 0.05f, 2f);
        loopPreview = EditorGUILayout.Toggle("반복 재생", loopPreview);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Idle 자동 샘플링"))
                SampleIdleClip();

            if (GUILayout.Button("클립 샘플링"))
                SamplePreviewClip();

            if (GUILayout.Button(isPlayingPreview ? "재생 중지" : "재생"))
                ToggleAnimationPlayback();

            if (GUILayout.Button("미리보기 종료"))
                ClearAnimationSample();
        }
    }

    private void SampleIdleClip()
    {
        for (int i = 0; i < characterClips.Length; i++)
        {
            AnimationClip clip = characterClips[i];
            if (clip == null || !clip.name.ToLowerInvariant().Contains("idle"))
                continue;

            selectedClipIndex = i;
            previewClip = clip;
            previewNormalizedTime = 0f;
            StopAnimationPlayback(false);
            SamplePreviewClip();
            Repaint();
            return;
        }
    }

    private void SamplePreviewClip()
    {
        if (previewClip == null)
            return;

        GameObject previewRoot = GetPreviewRoot();
        if (previewRoot == null)
            return;

        if (targetPose != null)
            RememberPoseTransform(targetPose);

        if (!AnimationMode.InAnimationMode())
            AnimationMode.StartAnimationMode();

        AnimationMode.BeginSampling();
        AnimationMode.SampleAnimationClip(previewRoot, previewClip, Mathf.Clamp01(previewNormalizedTime) * previewClip.length);
        AnimationMode.EndSampling();

        if (targetPose != null)
            PreviewPose(selectedSlot, false);

        SceneView.RepaintAll();
    }

    private void ToggleAnimationPlayback()
    {
        if (isPlayingPreview)
        {
            StopAnimationPlayback(false);
            return;
        }

        if (previewClip == null || GetPreviewRoot() == null)
            return;

        if (!AnimationMode.InAnimationMode())
            AnimationMode.StartAnimationMode();

        isPlayingPreview = true;
        playStartEditorTime = EditorApplication.timeSinceStartup;
        playStartNormalizedTime = Mathf.Clamp01(previewNormalizedTime);
        SamplePreviewClip();
    }

    private void UpdateAnimationPlayback()
    {
        if (!isPlayingPreview)
            return;

        if (previewClip == null || previewClip.length <= 0f || GetPreviewRoot() == null)
        {
            StopAnimationPlayback(false);
            return;
        }

        float elapsed = (float)(EditorApplication.timeSinceStartup - playStartEditorTime);
        float normalized = playStartNormalizedTime + (elapsed * Mathf.Max(0.01f, previewPlaybackSpeed)) / previewClip.length;

        if (loopPreview)
        {
            previewNormalizedTime = Mathf.Repeat(normalized, 1f);
        }
        else
        {
            previewNormalizedTime = Mathf.Clamp01(normalized);
            if (normalized >= 1f)
                StopAnimationPlayback(false);
        }

        SamplePreviewClip();
        Repaint();
    }

    private void StopAnimationPlayback(bool stopAnimationMode)
    {
        isPlayingPreview = false;

        if (stopAnimationMode && AnimationMode.InAnimationMode())
            AnimationMode.StopAnimationMode();
    }

    private void ClearAnimationSample()
    {
        RestorePreviewState();
    }

    private void DrawCurrentTransformInfo()
    {
        if (targetPose == null)
            return;

        Transform poseTarget = targetPose.GetPoseTargetForTuning();
        if (poseTarget == null)
            return;

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("현재 포즈 대상", EditorStyles.boldLabel);
        EditorGUILayout.ObjectField("트랜스폼", poseTarget, typeof(Transform), true);
        RememberPoseValueSnapshot(targetPose, selectedSlot);

        sceneHandleEnabled = EditorGUILayout.Toggle("Scene 핸들", sceneHandleEnabled);
        sceneHandleMode = (PoseSceneHandleMode)GUILayout.Toolbar((int)sceneHandleMode, new[] { "이동", "회전" });
        roundDecimals = EditorGUILayout.IntSlider("소수점 자리", roundDecimals, 0, 4);

        EditorGUI.BeginChangeCheck();
        Vector3 nextPosition = EditorGUILayout.Vector3Field("로컬 위치", poseTarget.localPosition);
        Vector3 nextRotation = EditorGUILayout.Vector3Field("로컬 회전", poseTarget.localEulerAngles);
        if (EditorGUI.EndChangeCheck())
            ApplyPoseFieldEdit(nextPosition, nextRotation);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("초기화"))
                ResetCurrentPoseToInitial();

            if (GUILayout.Button("소수점 정리"))
                RoundCurrentPoseValues();

            if (GUILayout.Button("현재 슬롯 프리팹 적용"))
                ApplyPoseOverrideToPrefab();
        }
    }

    private void ApplyPoseFieldEdit(Vector3 localPosition, Vector3 localRotation)
    {
        ApplyPoseEdit(localPosition, localRotation, true);
    }

    private void ApplyPoseEdit(Vector3 localPosition, Vector3 localRotation, bool stopPlayback)
    {
        if (targetPose == null)
            return;

        if (stopPlayback)
            StopAnimationPlayback(false);

        RememberPoseValueSnapshot(targetPose, selectedSlot);
        Undo.RecordObject(targetPose, "무기 포즈 수치 조정");
        targetPose.SetPoseForTuning(selectedSlot, localPosition, localRotation);
        EditorUtility.SetDirty(targetPose);
        PrefabUtility.RecordPrefabInstancePropertyModifications(targetPose);
        SceneView.RepaintAll();
    }

    private void RoundCurrentPoseValues()
    {
        if (targetPose == null)
            return;

        Transform poseTarget = targetPose.GetPoseTargetForTuning();
        if (poseTarget == null)
            return;

        ApplyPoseEdit(
            RoundVector(poseTarget.localPosition, roundDecimals),
            RoundVector(poseTarget.localEulerAngles, roundDecimals),
            false);
    }

    private Vector3 RoundVector(Vector3 value, int decimals)
    {
        return new Vector3(
            RoundFloat(value.x, decimals),
            RoundFloat(value.y, decimals),
            RoundFloat(value.z, decimals));
    }

    private float RoundFloat(float value, int decimals)
    {
        float multiplier = Mathf.Pow(10f, Mathf.Clamp(decimals, 0, 4));
        return Mathf.Round(value * multiplier) / multiplier;
    }

    private void DrawScenePoseHandles(SceneView sceneView)
    {
        if (!sceneHandleEnabled || targetPose == null)
            return;

        Transform poseTarget = targetPose.GetPoseTargetForTuning();
        if (poseTarget == null)
            return;

        Handles.color = Color.cyan;
        Handles.Label(poseTarget.position, "Weapon Pose: " + GetPoseSlotLabel(selectedSlot));

        EditorGUI.BeginChangeCheck();
        Vector3 nextLocalPosition = poseTarget.localPosition;
        Vector3 nextLocalRotation = poseTarget.localEulerAngles;

        if (sceneHandleMode == PoseSceneHandleMode.Move)
        {
            Vector3 nextWorldPosition = Handles.PositionHandle(poseTarget.position, poseTarget.rotation);
            if (poseTarget.parent != null)
                nextLocalPosition = poseTarget.parent.InverseTransformPoint(nextWorldPosition);
            else
                nextLocalPosition = nextWorldPosition;
        }
        else
        {
            Quaternion nextWorldRotation = Handles.RotationHandle(poseTarget.rotation, poseTarget.position);
            Quaternion nextLocalQuaternion = poseTarget.parent != null
                ? Quaternion.Inverse(poseTarget.parent.rotation) * nextWorldRotation
                : nextWorldRotation;
            nextLocalRotation = nextLocalQuaternion.eulerAngles;
        }

        if (EditorGUI.EndChangeCheck())
            ApplyPoseEdit(nextLocalPosition, nextLocalRotation, false);
    }

    private void ResetCurrentPoseToInitial()
    {
        if (targetPose == null)
            return;

        string key = GetPoseValueSnapshotKey(targetPose, selectedSlot);
        if (!poseValueSnapshots.TryGetValue(key, out PoseValueSnapshot snapshot) || snapshot == null)
            return;

        StopAnimationPlayback(true);
        Undo.RecordObject(targetPose, "무기 포즈 초기화");
        targetPose.SetPoseForTuning(selectedSlot, snapshot.LocalPosition, snapshot.LocalRotation);
        EditorUtility.SetDirty(targetPose);
        PrefabUtility.RecordPrefabInstancePropertyModifications(targetPose);
        SceneView.RepaintAll();
    }

    private void RememberPoseValueSnapshot(WeaponPose pose, WeaponPoseSlot poseSlot)
    {
        if (pose == null)
            return;

        string key = GetPoseValueSnapshotKey(pose, poseSlot);
        if (poseValueSnapshots.ContainsKey(key))
            return;

        poseValueSnapshots[key] = new PoseValueSnapshot
        {
            LocalPosition = pose.GetPoseLocalPositionForTuning(poseSlot),
            LocalRotation = pose.GetPoseLocalRotationForTuning(poseSlot)
        };
    }

    private void RefreshPoseValueSnapshot(WeaponPose pose, WeaponPoseSlot poseSlot)
    {
        if (pose == null)
            return;

        poseValueSnapshots[GetPoseValueSnapshotKey(pose, poseSlot)] = new PoseValueSnapshot
        {
            LocalPosition = pose.GetPoseLocalPositionForTuning(poseSlot),
            LocalRotation = pose.GetPoseLocalRotationForTuning(poseSlot)
        };
    }

    private string GetPoseValueSnapshotKey(WeaponPose pose, WeaponPoseSlot poseSlot)
    {
        return pose.GetInstanceID().ToString() + ":" + poseSlot;
    }

    private void UseSelectionAsTargets()
    {
        GameObject selectedObject = Selection.activeGameObject;
        if (selectedObject == null)
            return;

        WeaponPose selectedPose = FindWeaponPose(selectedObject);
        if (selectedPose != null)
        {
            targetPose = selectedPose;
            weaponObject = selectedPose.gameObject;
            weaponData = FindWeaponDataForWeaponObject(weaponObject);
            RefreshWeaponAnimationClips();
        }

        Animator animator = selectedObject.GetComponent<Animator>();
        if (animator == null)
            animator = selectedObject.GetComponentInParent<Animator>();
        if (animator == null)
            animator = selectedObject.GetComponentInChildren<Animator>(true);

        if (animator != null)
        {
            characterObject = animator.gameObject;
            RefreshCharacterContext();
        }

        if (selectedPose != null)
            SelectWeaponCandidate(FindCandidateByPose(selectedPose));
    }

    private WeaponPose FindWeaponPose(GameObject targetObject)
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

    private void RefreshAnimationClips()
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
                if (clip != null && !clips.Contains(clip))
                    clips.Add(clip);
            }
        }

        characterClips = clips.ToArray();
        characterClipNames = new string[characterClips.Length];
        for (int i = 0; i < characterClips.Length; i++)
            characterClipNames[i] = characterClips[i].name;

        if (characterClips.Length == 0)
        {
            selectedClipIndex = -1;
            previewClip = null;
            return;
        }

        selectedClipIndex = Mathf.Clamp(selectedClipIndex, 0, characterClips.Length - 1);
        if (previewClip == null || !clips.Contains(previewClip))
            previewClip = characterClips[selectedClipIndex];
    }

    private void RefreshCharacterContext()
    {
        RefreshAnimationClips();
        RefreshHandWeaponLists();
        RefreshWeaponAnimationClips();
    }

    private void RefreshWeaponAnimationClips()
    {
        List<ClipCandidate> clips = new List<ClipCandidate>();

        if (weaponData != null)
        {
            AddWeaponClip(clips, "조준", weaponData.GetAimPoseClip());
            AddWeaponClip(clips, "보조 조준", weaponData.alternateAimPoseClip);
            AddWeaponClip(clips, "발사 / 공격", weaponData.fireAnimationClip);
            AddWeaponClip(clips, "재장전", weaponData.reloadAnimationClip);
            AddWeaponClip(clips, "퀵파이어 조준", weaponData.quickFireAimPoseClip);
            AddWeaponClip(clips, "퀵파이어 공격", weaponData.quickFireAnimationClip);

            MeleeComboDefinition comboDefinition = weaponData.GetMeleeComboDefinition();
            if (comboDefinition != null && comboDefinition.steps != null)
            {
                for (int i = 0; i < comboDefinition.steps.Length; i++)
                {
                    MeleeComboStepData step = comboDefinition.steps[i];
                    string stepName = string.IsNullOrEmpty(step.attackName) ? "Sword Combo " + (i + 1) : step.attackName;
                    AddWeaponClip(clips, stepName, step.animationClip);
                }
            }
        }

        weaponClips = new AnimationClip[clips.Count];
        weaponClipNames = new string[clips.Count];
        for (int i = 0; i < clips.Count; i++)
        {
            weaponClips[i] = clips[i].Clip;
            weaponClipNames[i] = clips[i].DisplayName;
        }

        if (weaponClips.Length == 0)
        {
            selectedWeaponClipIndex = -1;
            return;
        }

        selectedWeaponClipIndex = Mathf.Clamp(selectedWeaponClipIndex, 0, weaponClips.Length - 1);
    }

    private void AddWeaponClip(List<ClipCandidate> clips, string label, AnimationClip clip)
    {
        if (clip == null)
            return;

        clips.Add(new ClipCandidate
        {
            DisplayName = label + " - " + clip.name,
            Clip = clip
        });
    }

    private void DrawHandWeaponLists()
    {
        if (characterObject == null)
            return;

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("손 무기 선택", EditorStyles.boldLabel);
        DrawWeaponCandidateList("오른손 무기", rightHandWeapons);
        DrawWeaponCandidateList("왼손 무기", leftHandWeapons);
    }

    private void DrawWeaponCandidateList(string label, List<WeaponPoseCandidate> candidates)
    {
        EditorGUILayout.LabelField(label);

        if (candidates.Count == 0)
        {
            EditorGUILayout.HelpBox(label + " 후보가 없습니다.", MessageType.None);
            return;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            WeaponPoseCandidate candidate = candidates[i];
            bool isCurrent = candidate != null && candidate.Pose == targetPose;
            string buttonLabel = isCurrent ? "선택됨: " + candidate.DisplayName : candidate.DisplayName;

            if (GUILayout.Button(buttonLabel))
                SelectWeaponCandidate(candidate);
        }
    }

    private void SelectWeaponCandidate(WeaponPoseCandidate candidate)
    {
        if (candidate == null || candidate.Pose == null)
            return;

        GameObject selectedRoot = candidate.RootObject != null ? candidate.RootObject : candidate.Pose.gameObject;
        weaponObject = selectedRoot;
        targetPose = candidate.Pose;
        weaponData = FindWeaponDataForWeaponObject(selectedRoot);
        RefreshWeaponAnimationClips();
        SetOnlySelectedWeaponActive(selectedRoot);
        Selection.activeGameObject = selectedRoot;
        SceneView.RepaintAll();
    }

    private WeaponPoseCandidate FindCandidateByPose(WeaponPose pose)
    {
        WeaponPoseCandidate candidate = FindCandidateByPose(rightHandWeapons, pose);
        return candidate != null ? candidate : FindCandidateByPose(leftHandWeapons, pose);
    }

    private WeaponPoseCandidate FindCandidateByPose(List<WeaponPoseCandidate> candidates, WeaponPose pose)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i] != null && candidates[i].Pose == pose)
                return candidates[i];
        }

        return null;
    }

    private void SetOnlySelectedWeaponActive(GameObject selectedRoot)
    {
        SetWeaponCandidateListActive(rightHandWeapons, selectedRoot);
        SetWeaponCandidateListActive(leftHandWeapons, selectedRoot);
    }

    private void SetWeaponCandidateListActive(List<WeaponPoseCandidate> candidates, GameObject selectedRoot)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            WeaponPoseCandidate candidate = candidates[i];
            if (candidate == null || candidate.RootObject == null)
                continue;

            RememberActiveState(candidate.RootObject);
            bool shouldBeActive = candidate.RootObject == selectedRoot;
            if (candidate.RootObject.activeSelf == shouldBeActive)
                continue;

            Undo.RecordObject(candidate.RootObject, "무기 표시 전환");
            candidate.RootObject.SetActive(shouldBeActive);
            EditorUtility.SetDirty(candidate.RootObject);
        }
    }

    private void RememberPoseTransform(WeaponPose pose)
    {
        if (pose == null)
            return;

        Transform poseTarget = pose.GetPoseTargetForTuning();
        RememberTransform(poseTarget);
    }

    private void RememberTransform(Transform target)
    {
        if (target == null || transformSnapshots.ContainsKey(target))
            return;

        transformSnapshots[target] = new TransformSnapshot
        {
            Parent = target.parent,
            LocalPosition = target.localPosition,
            LocalRotation = target.localRotation,
            LocalScale = target.localScale
        };
    }

    private void RememberActiveState(GameObject target)
    {
        if (target == null || activeSnapshots.ContainsKey(target))
            return;

        activeSnapshots[target] = target.activeSelf;
    }

    private void RestorePreviewState()
    {
        StopAnimationPlayback(true);
        RestoreTransformSnapshots();
        RestoreActiveSnapshots();
        SceneView.RepaintAll();
    }

    private void RestoreTransformSnapshots()
    {
        foreach (KeyValuePair<Transform, TransformSnapshot> snapshotPair in transformSnapshots)
        {
            Transform target = snapshotPair.Key;
            TransformSnapshot snapshot = snapshotPair.Value;
            if (target == null || snapshot == null)
                continue;

            target.SetParent(snapshot.Parent, false);
            target.localPosition = snapshot.LocalPosition;
            target.localRotation = snapshot.LocalRotation;
            target.localScale = snapshot.LocalScale;
        }

        transformSnapshots.Clear();
    }

    private void RestoreActiveSnapshots()
    {
        foreach (KeyValuePair<GameObject, bool> snapshotPair in activeSnapshots)
        {
            GameObject target = snapshotPair.Key;
            if (target == null)
                continue;

            target.SetActive(snapshotPair.Value);
        }

        activeSnapshots.Clear();
    }

    private void RefreshHandWeaponLists()
    {
        rightHandWeapons.Clear();
        leftHandWeapons.Clear();

        Animator animator = GetCharacterAnimator();
        Transform characterRoot = animator != null ? animator.transform : (characterObject != null ? characterObject.transform : null);
        if (characterRoot == null)
            return;

        Transform rightHand = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
        Transform leftHand = animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;

        if (rightHand == null)
            rightHand = FindHandByName(characterRoot, true);
        if (leftHand == null)
            leftHand = FindHandByName(characterRoot, false);

        AddHandWeaponCandidates(rightHand, rightHandWeapons);
        AddHandWeaponCandidates(leftHand, leftHandWeapons);
        AddCharacterWeaponCandidateFallbacks(characterRoot, rightHand, leftHand);
    }

    private void AddHandWeaponCandidates(Transform hand, List<WeaponPoseCandidate> candidates)
    {
        if (hand == null)
            return;

        WeaponPose[] poses = hand.GetComponentsInChildren<WeaponPose>(true);
        for (int i = 0; i < poses.Length; i++)
        {
            WeaponPose pose = poses[i];
            if (pose == null || ContainsPose(candidates, pose))
                continue;

            GameObject rootObject = GetWeaponRootUnderHand(pose.transform, hand);
            AddWeaponCandidate(candidates, rootObject != null ? rootObject : pose.gameObject, pose);
        }
    }

    private void AddCharacterWeaponCandidateFallbacks(Transform characterRoot, Transform rightHand, Transform leftHand)
    {
        if (characterRoot == null)
            return;

        WeaponPose[] poses = characterRoot.GetComponentsInChildren<WeaponPose>(true);
        for (int i = 0; i < poses.Length; i++)
        {
            WeaponPose pose = poses[i];
            if (pose == null || ContainsPoseInAnyList(pose))
                continue;

            GameObject rootObject = GetWeaponRootForPose(pose, characterRoot, rightHand, leftHand);
            WeaponItemData data = FindWeaponDataForWeaponObject(rootObject != null ? rootObject : pose.gameObject);
            List<WeaponPoseCandidate> targetList = ShouldListAsLeftHandWeapon(pose.transform, data, leftHand)
                ? leftHandWeapons
                : rightHandWeapons;

            AddWeaponCandidate(targetList, rootObject != null ? rootObject : pose.gameObject, pose);
        }
    }

    private void AddWeaponCandidate(List<WeaponPoseCandidate> candidates, GameObject rootObject, WeaponPose pose)
    {
        if (pose == null || ContainsPose(candidates, pose))
            return;

        candidates.Add(new WeaponPoseCandidate
        {
            DisplayName = rootObject != null ? rootObject.name : pose.name,
            RootObject = rootObject != null ? rootObject : pose.gameObject,
            Pose = pose
        });
    }

    private bool ContainsPose(List<WeaponPoseCandidate> candidates, WeaponPose pose)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i] != null && candidates[i].Pose == pose)
                return true;
        }

        return false;
    }

    private bool ContainsPoseInAnyList(WeaponPose pose)
    {
        return ContainsPose(rightHandWeapons, pose) || ContainsPose(leftHandWeapons, pose);
    }

    private GameObject GetWeaponRootUnderHand(Transform weaponChild, Transform hand)
    {
        if (weaponChild == null || hand == null)
            return null;

        Transform current = weaponChild;
        while (current.parent != null && current.parent != hand)
            current = current.parent;

        return current.gameObject;
    }

    private GameObject GetWeaponRootForPose(WeaponPose pose, Transform characterRoot, Transform rightHand, Transform leftHand)
    {
        if (pose == null)
            return null;

        if (rightHand != null && IsChildOf(pose.transform, rightHand))
            return GetWeaponRootUnderHand(pose.transform, rightHand);

        if (leftHand != null && IsChildOf(pose.transform, leftHand))
            return GetWeaponRootUnderHand(pose.transform, leftHand);

        GameObject prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(pose.gameObject);
        if (prefabRoot != null && prefabRoot.transform != characterRoot)
            return prefabRoot;

        return pose.gameObject;
    }

    private bool ShouldListAsLeftHandWeapon(Transform poseTransform, WeaponItemData data, Transform leftHand)
    {
        if (leftHand != null && IsChildOf(poseTransform, leftHand))
            return true;

        return false;
    }

    private bool IsChildOf(Transform child, Transform parent)
    {
        if (child == null || parent == null)
            return false;

        Transform current = child;
        while (current != null)
        {
            if (current == parent)
                return true;

            current = current.parent;
        }

        return false;
    }

    private Transform FindHandByName(Transform root, bool rightHand)
    {
        string[] names = rightHand
            ? new[] { "RightHand", "hand_r", "Hand_R", "R_Hand", "mixamorig:RightHand" }
            : new[] { "LeftHand", "hand_l", "Hand_L", "L_Hand", "mixamorig:LeftHand" };

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            string transformName = transforms[i].name;
            for (int j = 0; j < names.Length; j++)
            {
                if (transformName == names[j])
                    return transforms[i];
            }
        }

        return null;
    }

    private WeaponItemData FindWeaponDataForWeaponObject(GameObject selectedWeaponObject)
    {
        if (selectedWeaponObject == null)
            return null;

        GameObject prefabAsset = GetPrefabAssetForWeaponObject(selectedWeaponObject);
        string[] guids = AssetDatabase.FindAssets("t:WeaponItemData");

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            WeaponItemData data = AssetDatabase.LoadAssetAtPath<WeaponItemData>(path);
            if (data != null && data.weaponRootPrefab != null && data.weaponRootPrefab == prefabAsset)
                return data;
        }

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            WeaponItemData data = AssetDatabase.LoadAssetAtPath<WeaponItemData>(path);
            if (data != null && data.weaponRootPrefab != null && IsLikelySameWeaponName(data.weaponRootPrefab.name, selectedWeaponObject.name))
                return data;
        }

        return null;
    }

    private GameObject GetPrefabAssetForWeaponObject(GameObject selectedWeaponObject)
    {
        if (selectedWeaponObject == null)
            return null;

        string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(selectedWeaponObject);
        if (!string.IsNullOrEmpty(prefabPath))
            return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(selectedWeaponObject);
        if (source != null)
            return source;

        return AssetDatabase.Contains(selectedWeaponObject) ? selectedWeaponObject : null;
    }

    private bool IsLikelySameWeaponName(string assetName, string sceneName)
    {
        if (string.IsNullOrEmpty(assetName) || string.IsNullOrEmpty(sceneName))
            return false;

        string normalizedSceneName = sceneName.Replace("(Clone)", string.Empty).Trim();
        return assetName == normalizedSceneName;
    }

    private GameObject GetPreviewRoot()
    {
        if (characterObject != null)
        {
            Animator characterAnimator = GetCharacterAnimator();
            return characterAnimator != null ? characterAnimator.gameObject : characterObject;
        }

        if (targetPose == null)
            return null;

        Animator animator = targetPose.GetComponentInParent<Animator>();
        if (animator != null)
            return animator.gameObject;

        return targetPose.transform.root != null ? targetPose.transform.root.gameObject : targetPose.gameObject;
    }

    private Animator GetCharacterAnimator()
    {
        if (characterObject == null)
            return null;

        Animator animator = characterObject.GetComponent<Animator>();
        if (animator != null)
            return animator;

        return characterObject.GetComponentInChildren<Animator>(true);
    }
}
