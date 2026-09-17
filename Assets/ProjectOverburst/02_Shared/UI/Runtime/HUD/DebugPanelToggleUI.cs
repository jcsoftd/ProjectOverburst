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
