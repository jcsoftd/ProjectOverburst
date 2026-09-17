using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class PlayerHumanoidLeftHandIKDriver : MonoBehaviour
{
    [SerializeField] private Animator targetAnimator;
    [SerializeField] private PlayerLeftHandGrip gripController;

    private void Awake()
    {
        ResolveReferences();
    }

    public void Bind(Animator animator, PlayerLeftHandGrip controller)
    {
        targetAnimator = animator;
        gripController = controller;
    }

    private void OnAnimatorIK(int layerIndex)
    {
        ResolveReferences();
        gripController?.ApplyAnimatorIK(targetAnimator);
    }

    private void ResolveReferences()
    {
        if (targetAnimator == null)
            targetAnimator = GetComponent<Animator>();
        if (gripController == null)
            gripController = GetComponentInParent<PlayerLeftHandGrip>(true);
    }
}
