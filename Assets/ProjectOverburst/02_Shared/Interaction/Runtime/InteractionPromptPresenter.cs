using UnityEngine;

[DisallowMultipleComponent]
public sealed class InteractionPromptPresenter : MonoBehaviour
{
    private IInteractable current;
    private static string interactPath;
    private static string interactLabel;

    public static string InteractKeyLabel
    {
        get
        {
            PlayerInputFacade facade = PlayerInputFacade.Current;
            if (facade == null) return "F";
            var action = facade.GameplayMap?.FindAction("Interact");
            int index = OverburstKeyBindingRow.ResolveBindingIndex(action, null);
            if (index < 0) return "—";
            string path = action.bindings[index].effectivePath;
            if (string.IsNullOrEmpty(path)) return "—";
            if (interactPath != path)
            {
                interactPath = path;
                interactLabel = OverburstKeyBindingRow.ShortName(path);
            }
            return interactLabel;
        }
    }

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
