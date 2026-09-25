using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DebugPanelToggleUI : MonoBehaviour
{
    [SerializeField] private Button toggleButton;
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private TMP_FontAsset fontAsset;
    [SerializeField] private GameObject[] controlledObjects;
    [SerializeField] private bool startsExpanded;
    [SerializeField] private bool showInEditorOrDevelopmentBuild = true;

    private bool isExpanded;
    private bool listenerRegistered;

    private void Awake()
    {
        if (!IsDebugUiAllowed())
        {
            SetControlledObjects(false);
            gameObject.SetActive(false);
            return;
        }

        ResolveReferences();
        AttachDamageNumberFeelControls();
        RegisterButton();
        SetExpanded(startsExpanded);
    }

    private void OnDestroy()
    {
        if (toggleButton != null && listenerRegistered)
            toggleButton.onClick.RemoveListener(Toggle);
    }

    private void Toggle()
    {
        SetExpanded(!isExpanded);
    }

    private void SetExpanded(bool expanded)
    {
        isExpanded = expanded;
        SetControlledObjects(isExpanded);
        RefreshLabel();
    }

    private void SetControlledObjects(bool active)
    {
        if (controlledObjects == null)
            return;

        for (int i = 0; i < controlledObjects.Length; i++)
        {
            if (controlledObjects[i] == null || controlledObjects[i] == gameObject)
                continue;

            controlledObjects[i].SetActive(active);
        }
    }

    private void ResolveReferences()
    {
        if (toggleButton == null)
            toggleButton = GetComponent<Button>();

        if (label == null)
            label = GetComponentInChildren<TextMeshProUGUI>(true);
    }

    private void AttachDamageNumberFeelControls()
    {
        if (transform.parent == null || transform.parent.name != "DebugPanel")
            return;

        Transform existing = transform.parent.Find("DamageNumberFeelDebugUI");
        GameObject controls = existing != null ? existing.gameObject : null;
        if (controls == null)
        {
            GameObject prefab = Resources.Load<GameObject>("UI/Debug/PF_DamageNumberFeelDebugUI");
            if (prefab == null)
            {
                Debug.LogWarning("[DebugPanelToggleUI] 데미지 숫자 비교 UI 프리팹을 찾지 못했습니다.", this);
                return;
            }
            controls = Instantiate(prefab, transform.parent, false);
            controls.name = "DamageNumberFeelDebugUI";
        }

        GameObject[] previous = controlledObjects ?? Array.Empty<GameObject>();
        if (Array.IndexOf(previous, controls) >= 0)
            return;
        var next = new GameObject[previous.Length + 1];
        Array.Copy(previous, next, previous.Length);
        next[previous.Length] = controls;
        controlledObjects = next;
    }

    private void RegisterButton()
    {
        if (toggleButton == null || listenerRegistered)
            return;

        toggleButton.onClick.AddListener(Toggle);
        listenerRegistered = true;
    }

    private void RefreshLabel()
    {
        if (label == null)
            return;

        ApplyFont();
    }

    private void ApplyFont()
    {
        if (fontAsset == null || label == null)
            return;

        label.font = fontAsset;
        label.fontSharedMaterial = fontAsset.material;
    }

    private bool IsDebugUiAllowed()
    {
        if (!showInEditorOrDevelopmentBuild)
            return false;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return true;
#else
        return false;
#endif
    }
}
