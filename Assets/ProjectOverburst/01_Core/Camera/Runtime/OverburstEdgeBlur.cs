using UnityEngine;

/// <summary>탐험/전투별 가장자리 흐림 선택을 저장된 게임 설정에서 읽는다.</summary>
[DisallowMultipleComponent]
public sealed class OverburstEdgeBlur : MonoBehaviour
{
    public const string ResourcePath="Camera/PF_OverburstEdgeBlur";
    private static OverburstEdgeBlur instance;
    public static bool IsEnabled=>instance!=null && instance.isActiveAndEnabled && OverburstGameSettings.EdgeBlurEnabled;
    public static float CurrentStrength=>instance!=null ? instance.SelectedIntensity : 0f;
    public float SelectedIntensity=>PlayerCombatModeController.IsSharedCombatModeActive() ? OverburstGameSettings.CombatEdgeBlurIntensity : OverburstGameSettings.ExplorationEdgeBlurIntensity;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()=>instance=null;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if(instance!=null)return;
        var prefab=Resources.Load<GameObject>(ResourcePath);if(prefab!=null)Instantiate(prefab);
    }
    private void Awake()
    {
        if(instance!=null && instance!=this){Destroy(gameObject);return;}
        instance=this;DontDestroyOnLoad(gameObject);
    }
    public void SetEnabled(bool value)=>OverburstGameSettings.EdgeBlurEnabled=value;
    private void OnDestroy(){if(instance==this)instance=null;}
}
