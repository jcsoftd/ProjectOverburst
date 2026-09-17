using UnityEngine;

public sealed class HubReturnPoint : MonoBehaviour // 허브 복귀 지점
{
    private const string DefaultReturnPointId = "Default";

    [SerializeField] private string returnPointId = DefaultReturnPointId;

    public string ReturnPointId =>
        string.IsNullOrWhiteSpace(returnPointId)
            ? DefaultReturnPointId
            : returnPointId;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(returnPointId))
            returnPointId = DefaultReturnPointId;
    }
#endif
}
