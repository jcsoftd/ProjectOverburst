using System;
using UnityEngine;

[DefaultExecutionOrder(500)]
public class CharacterMouthNeutralizer : MonoBehaviour
{
    [Header("Options")]
    [SerializeField] private bool lockMouth = true; // 입 고정
    [SerializeField] private bool resetJawBone = true; // 턱 본 초기화
    [SerializeField] private bool resetMouthBlendShapes = true; // 입 블렌드쉐이프 초기화

    [Header("Jaw Bone")]
    [SerializeField] private Transform jawBone; // jaw 본
    [SerializeField] private Vector3 neutralJawLocalEuler; // 턱 기본 회전
    [SerializeField] private bool useCapturedJawRotation = true; // 시작 회전 사용

    [Header("BlendShape Name Filter")]
    [SerializeField]
    private string[] blockedBlendShapeKeywords =
    {
        "jaw",
        "mouth",
        "lip",
        "tongue"
    };

    private SkinnedMeshRenderer[] renderers;
    private Quaternion capturedJawLocalRotation;
    private bool hasCapturedJaw;

    private void Awake()
    {
        renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true); // 얼굴 포함 렌더러 탐색

        if (jawBone == null)
            jawBone = FindDeepChild(transform, "jaw"); // jaw 본 자동 탐색

        if (jawBone != null)
        {
            capturedJawLocalRotation = jawBone.localRotation; // 시작 턱 회전 저장
            neutralJawLocalEuler = jawBone.localEulerAngles; // Inspector 확인용
            hasCapturedJaw = true;
        }
    }

    private void LateUpdate()
    {
        if (!lockMouth)
            return; // 고정 꺼짐

        if (resetJawBone)
            ResetJawBone(); // 턱 본 초기화

        if (resetMouthBlendShapes)
            ResetMouthBlendShapes(); // 입 블렌드쉐이프 초기화
    }

    private void ResetJawBone()
    {
        if (jawBone == null)
            return; // jaw 없음

        if (useCapturedJawRotation && hasCapturedJaw)
        {
            jawBone.localRotation = capturedJawLocalRotation; // 시작 회전으로 복구
            return;
        }

        jawBone.localRotation = Quaternion.Euler(neutralJawLocalEuler); // 수동 회전 적용
    }

    private void ResetMouthBlendShapes()
    {
        if (renderers == null)
            return; // 렌더러 없음

        for (int r = 0; r < renderers.Length; r++)
        {
            SkinnedMeshRenderer smr = renderers[r];

            if (smr == null || smr.sharedMesh == null)
                continue; // 메시 없음

            int count = smr.sharedMesh.blendShapeCount;

            for (int i = 0; i < count; i++)
            {
                string blendShapeName = smr.sharedMesh.GetBlendShapeName(i);

                if (!ShouldBlockBlendShape(blendShapeName))
                    continue; // 입 관련 아님

                smr.SetBlendShapeWeight(i, 0f); // 입 관련 값 제거
            }
        }
    }

    private bool ShouldBlockBlendShape(string blendShapeName)
    {
        if (string.IsNullOrEmpty(blendShapeName))
            return false; // 이름 없음

        for (int i = 0; i < blockedBlendShapeKeywords.Length; i++)
        {
            string keyword = blockedBlendShapeKeywords[i];

            if (string.IsNullOrEmpty(keyword))
                continue;

            if (blendShapeName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                return true; // mouth/jaw/lip/tongue 계열 차단
        }

        return false;
    }

    private Transform FindDeepChild(Transform parent, string targetName)
    {
        if (parent == null)
            return null;

        if (string.Equals(parent.name, targetName, StringComparison.OrdinalIgnoreCase))
            return parent; // 현재 본

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform result = FindDeepChild(parent.GetChild(i), targetName);

            if (result != null)
                return result;
        }

        return null;
    }

    [ContextMenu("Capture Current Jaw As Neutral")]
    private void CaptureCurrentJawAsNeutral()
    {
        if (jawBone == null)
            jawBone = FindDeepChild(transform, "jaw");

        if (jawBone == null)
            return;

        capturedJawLocalRotation = jawBone.localRotation; // 현재 회전 저장
        neutralJawLocalEuler = jawBone.localEulerAngles; // 수동값 저장
        hasCapturedJaw = true;
    }

    [ContextMenu("Force Close Mouth Once")]
    private void ForceCloseMouthOnce()
    {
        ResetJawBone();
        ResetMouthBlendShapes();
    }
}