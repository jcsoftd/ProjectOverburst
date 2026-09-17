using UnityEngine;

public static class CharacterVisualBuilder
{
    public static Transform EnsureVisualRoot(GameObject owner)
    {
        if (owner == null)
            return null;

        Transform existing = owner.transform.Find("VisualRoot");
        if (existing != null)
            return existing;

        GameObject visualRootObject = new GameObject("VisualRoot");
        Transform visualRoot = visualRootObject.transform;
        visualRoot.SetParent(owner.transform, false);
        return visualRoot;
    }

    public static Transform BuildVisual(GameObject owner, GameObject visualPrefab, int memberIndex)
    {
        return BuildVisual(owner, visualPrefab, memberIndex, null);
    }

    public static Transform BuildVisual(GameObject owner, GameObject visualPrefab, int memberIndex, Animator animatorTemplate)
    {
        if (owner == null)
            return null;

        if (visualPrefab == null)
            return ResolveExistingVisual(owner);

        Transform visualRoot = EnsureVisualRoot(owner);
        if (visualRoot == null)
            return ResolveExistingVisual(owner);

        GameObject visualInstance = Object.Instantiate(visualPrefab, visualRoot, false);
        visualInstance.name = visualPrefab.name + "_Visual";
        ApplyAnimatorTemplate(visualInstance, animatorTemplate);
        RefreshPlayerAnimation(owner);
        return visualInstance.transform;
    }

    private static Transform ResolveExistingVisual(GameObject owner)
    {
        if (owner == null)
            return null;

        Animator animator = owner.GetComponentInChildren<Animator>(true);
        if (animator != null)
            return animator.transform;

        Renderer renderer = owner.GetComponentInChildren<Renderer>(true);
        if (renderer != null)
            return renderer.transform;

        return owner.transform;
    }

    private static void ApplyAnimatorTemplate(GameObject visualInstance, Animator animatorTemplate)
    {
        if (visualInstance == null || animatorTemplate == null)
            return;

        Animator animator = visualInstance.GetComponentInChildren<Animator>(true);
        if (animator == null)
            return;

        if (animator.runtimeAnimatorController == null)
            animator.runtimeAnimatorController = animatorTemplate.runtimeAnimatorController;

        animator.applyRootMotion = animatorTemplate.applyRootMotion;
        animator.updateMode = animatorTemplate.updateMode;
        animator.cullingMode = animatorTemplate.cullingMode;
    }

    private static void RefreshPlayerAnimation(GameObject owner)
    {
        if (owner == null)
            return;

        PlayerAnimation playerAnimation = owner.GetComponent<PlayerAnimation>();
        if (playerAnimation != null)
            playerAnimation.RefreshAnimatorReference();
    }
}
