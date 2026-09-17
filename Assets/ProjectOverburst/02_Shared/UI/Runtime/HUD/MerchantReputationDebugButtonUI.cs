using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MerchantReputationDebugButtonUI : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private TMP_FontAsset fontAsset;
    [SerializeField] private int experienceAmount = 20;
    [SerializeField] private bool showInEditorOrDevelopmentBuild = true;

    private bool listenerRegistered;

    private void Awake()
    {
        if (!IsDebugUiAllowed())
        {
            gameObject.SetActive(false);
            return;
        }

        ResolveReferences();
        RegisterButton();
        RefreshLabel();
    }

    private void OnDestroy()
    {
        if (button != null && listenerRegistered)
            button.onClick.RemoveListener(AddReputationExperienceToAll);
    }

    private void AddReputationExperienceToAll()
    {
        MerchantReputationService.AddReputationExperienceToAll(experienceAmount);
        RefreshLabel();
    }

    private void ResolveReferences()
    {
        if (button == null)
            button = GetComponent<Button>();

        if (label == null)
            label = GetComponentInChildren<TextMeshProUGUI>(true);
    }

    private void RegisterButton()
    {
        if (button == null || listenerRegistered)
            return;

        button.onClick.AddListener(AddReputationExperienceToAll);
        listenerRegistered = true;
    }

    private void RefreshLabel()
    {
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
