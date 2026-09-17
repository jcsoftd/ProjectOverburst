using System;

[Serializable]
public sealed class RunSceneReturnContext // 런 복귀 요청
{
    public string TargetSceneName { get; private set; } // 복귀 씬
    public string ReturnPointId { get; private set; } // 복귀 지점
    public bool IsExtractSuccess { get; private set; } // 탈출 성공

    private RunSceneReturnContext(
        string targetSceneName,
        string returnPointId,
        bool isExtractSuccess)
    {
        TargetSceneName = string.IsNullOrWhiteSpace(targetSceneName)
            ? PersistentSceneFlow.DefaultHubSceneName
            : targetSceneName;
        ReturnPointId = string.IsNullOrWhiteSpace(returnPointId) ? "Default" : returnPointId;
        IsExtractSuccess = isExtractSuccess;
    }

    public static RunSceneReturnContext CreateExtractSuccess(
        string targetSceneName,
        string returnPointId)
    {
        return new RunSceneReturnContext(
            targetSceneName,
            returnPointId,
            true);
    }

    public static RunSceneReturnContext CreateHubTransfer(
        string targetSceneName,
        string returnPointId)
    {
        return new RunSceneReturnContext(
            targetSceneName,
            returnPointId,
            false);
    }
}
