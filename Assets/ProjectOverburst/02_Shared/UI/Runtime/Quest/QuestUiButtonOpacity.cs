using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button),typeof(CanvasGroup))]
public sealed class QuestUiButtonOpacity : MonoBehaviour
{
    private Button button;
    private CanvasGroup group;
    private void Awake(){button=GetComponent<Button>();if(!button.enabled){var parent=transform.parent.GetComponentInParent<Button>();if(parent)button=parent;}group=GetComponent<CanvasGroup>();}
    private void LateUpdate(){float alpha=button.interactable ? 1 : .48f;if(group.alpha!=alpha)group.alpha=alpha;}
}
