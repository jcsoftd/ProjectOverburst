using System.Text;
using UnityEngine;

public enum InteractionDistanceMode
{
    ThreeDimensional = 0,
    Horizontal = 1,
}

public enum InteractionExecutionResult
{
    Rejected = 0,
    Succeeded = 1,
    ClosedSession = 2,
    StartedTransition = 3,
}

// GOAL D의 공통 상호작용 계약. 기능 본체는 각 대상이 계속 소유한다.
public interface IInteractable
{
    Component InteractionComponent { get; }
    Transform InteractionTransform { get; }
    int InteractionPriority { get; }
    string InteractionPrompt { get; }
    InteractionDistanceMode DistanceMode { get; }
    float InteractionRange { get; }
    string StableInteractionId { get; }
    bool AllowsInteractionWhileInputBlocked { get; }
    bool WantsInteractionPrompt { get; }

    bool IsInteractionAvailable(PlayerActorRuntime actor);
    InteractionExecutionResult TryInteract(PlayerActorRuntime actor);
    void SetInteractionPromptVisible(bool visible);
}

public static class InteractionStableIdUtility
{
    public static string Build(Component owner)
    {
        if (owner == null)
            return string.Empty;

        StringBuilder builder = new StringBuilder(128);
        builder.Append(owner.gameObject.scene.path);
        builder.Append('|');
        AppendTransformPath(builder, owner.transform);
        builder.Append('|');
        builder.Append(owner.GetType().FullName);
        return builder.ToString();
    }

    private static void AppendTransformPath(StringBuilder builder, Transform current)
    {
        if (current == null)
            return;
        if (current.parent != null)
        {
            AppendTransformPath(builder, current.parent);
            builder.Append('/');
        }
        builder.Append(current.name);
        builder.Append('#');
        builder.Append(current.GetSiblingIndex());
    }
}
