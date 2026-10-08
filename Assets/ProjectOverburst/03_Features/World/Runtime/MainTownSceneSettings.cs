using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Town-owned camera and merchant catalog; also supports pressing Play directly in MainScene.</summary>
public sealed class MainTownSceneSettings : MonoBehaviour
{
    [SerializeField] private float cameraYaw = 346f;
    [SerializeField] private MerchantDefinition[] merchants;
    public float CameraYaw => cameraYaw;
    public System.Collections.Generic.IReadOnlyList<MerchantDefinition> Merchants => merchants;

    private IEnumerator Start()
    {
        var persistent = SceneManager.GetSceneByName(PersistentSceneFlow.PersistentSceneName);
        if (!persistent.IsValid() || !persistent.isLoaded)
            yield return SceneManager.LoadSceneAsync(PersistentSceneFlow.PersistentSceneName, LoadSceneMode.Additive);
        MerchantStockRefreshService.RegisterMerchantDefinitions(merchants);
    }
}
