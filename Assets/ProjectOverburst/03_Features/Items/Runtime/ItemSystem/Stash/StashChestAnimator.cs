using UnityEngine;

/// <summary>Synchronizes the authored storage chest animation with the existing stash session.</summary>
[DisallowMultipleComponent]
public sealed class StashChestAnimator : MonoBehaviour
{
    [SerializeField] private StashInteractable stash;
    [SerializeField] private Animator animator;
    static readonly int OpenParameter = Animator.StringToHash("Open");

    private void LateUpdate()
    {
        if (stash != null && animator != null)
            animator.SetBool(OpenParameter, stash.IsOpen);
    }
    private void OnDisable()
    {
        if (animator != null) animator.SetBool(OpenParameter, false);
    }
}
