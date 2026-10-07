using Overburst.Appearance;
using UnityEngine;
using UnityEngine.UI;

// Self-contained preview extension. Remove this authored child to remove the shortcut and binding.
[DisallowMultipleComponent]
public sealed class AppearanceDeveloperPreview : MonoBehaviour
{
    public Button iconButton;
    public AppearanceCustomizationPanel panel;
    private void Awake()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if(iconButton)iconButton.onClick.AddListener(Toggle);
#else
        gameObject.SetActive(false);
#endif
    }
    private void Toggle()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if(!panel||panel.Session==null)return;
        panel.Session.SetDeveloperNude(panel.Session.PreviewBody!=AppearancePreviewBody.Nude);
#endif
    }
    private void OnDestroy(){if(iconButton)iconButton.onClick.RemoveListener(Toggle);}
}

