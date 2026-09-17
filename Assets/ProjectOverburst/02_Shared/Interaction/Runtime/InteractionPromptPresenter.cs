using UnityEngine;

[DisallowMultipleComponent]
public sealed class InteractionPromptPresenter : MonoBehaviour
{
    private IInteractable current;

    public IInteractable Current => current != null && current.InteractionComponent != null
        ? current
        : null;

    public void Present(IInteractable next)
    {
        if (!ReferenceEquals(current, next) && current != null && current.InteractionComponent != null)
            current.SetInteractionPromptVisible(false);

        current = next;
        if (current != null && current.InteractionComponent != null)
            current.SetInteractionPromptVisible(current.WantsInteractionPrompt);
    }

    public void Clear()
    {
        if (current != null && current.InteractionComponent != null)
            current.SetInteractionPromptVisible(false);
        current = null;
    }

    private void OnDisable() => Clear();
}
