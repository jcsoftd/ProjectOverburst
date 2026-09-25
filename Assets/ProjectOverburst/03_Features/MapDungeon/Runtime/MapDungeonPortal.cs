using Overburst.Persistence;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class MapDungeonPortal : MonoBehaviour, IInteractable
{
    private MapDungeonPortalPanel panel;
    private bool entering;
    private Material visualMaterial;

    public Component InteractionComponent => this;
    public Transform InteractionTransform => transform;
    public int InteractionPriority => 320;
    public string InteractionPrompt => "F : 지도 포탈";
    public InteractionDistanceMode DistanceMode => InteractionDistanceMode.Horizontal;
    public float InteractionRange => 3f;
    public string StableInteractionId => InteractionStableIdUtility.Build(this);
    public bool AllowsInteractionWhileInputBlocked => false;
    public bool WantsInteractionPrompt => panel == null && !entering;

    public static void SpawnInHideout(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded || scene.name != PersistentSceneFlow.HideoutSceneName)
            return;
        foreach (var portal in FindObjectsByType<MapDungeonPortal>(FindObjectsSortMode.None))
            if (portal.gameObject.scene == scene) return;
        HubReturnPoint anchor = null;
        foreach (var point in FindObjectsByType<HubReturnPoint>(FindObjectsSortMode.None))
            if (point.gameObject.scene == scene && point.ReturnPointId == "DungeonPortal")
            { anchor = point; break; }
        if (anchor == null)
        {
            Debug.LogError("지도 포탈용 하이드아웃 위치가 없습니다.");
            return;
        }
        var root = new GameObject("MapDungeonPortal");
        SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.position = anchor.transform.position - anchor.transform.forward * 1.8f;
        root.AddComponent<MapDungeonPortal>().BuildVisual();
    }

    private void BuildVisual()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        visualMaterial = new Material(shader) { color = new Color(.32f, .58f, .67f) };
        var baseObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseObject.name = "PortalBase";
        baseObject.transform.SetParent(transform, false);
        baseObject.transform.localPosition = Vector3.up * .12f;
        baseObject.transform.localScale = new Vector3(2.5f, .12f, 2.5f);
        baseObject.GetComponent<Renderer>().sharedMaterial = visualMaterial;
        Destroy(baseObject.GetComponent<Collider>());
        var core = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        core.name = "PortalCore";
        core.transform.SetParent(transform, false);
        core.transform.localPosition = Vector3.up * 1.5f;
        core.transform.localScale = new Vector3(.65f, 1.1f, .65f);
        core.GetComponent<Renderer>().sharedMaterial = visualMaterial;
        Destroy(core.GetComponent<Collider>());
    }

    private void OnEnable() => InteractionRegistry.Register(this);

    private void OnDisable()
    {
        InteractionRegistry.Unregister(this);
        ClosePanel();
    }

    private void OnDestroy()
    {
        if (visualMaterial != null) Destroy(visualMaterial);
    }

    private void Update()
    {
        if (entering && WorldSessionState.IsHideout
            && PersistentSceneFlow.Instance != null && !PersistentSceneFlow.Instance.IsSwitching)
            entering = false;
        if (panel != null && PlayerInputFacade.Current?.UiCancelPressedThisFrame == true)
            ClosePanel();
    }

    public bool IsInteractionAvailable(PlayerActorRuntime actor)
        => actor != null && panel == null && !entering && WorldSessionState.IsHideout
            && PersistentSceneFlow.Instance != null && !PersistentSceneFlow.Instance.IsSwitching;

    public InteractionExecutionResult TryInteract(PlayerActorRuntime actor)
    {
        if (!IsInteractionAvailable(actor)) return InteractionExecutionResult.Rejected;
        panel = MapDungeonPortalPanel.Open(this);
        if (panel == null) return InteractionExecutionResult.Rejected;
        GameplayInputBlocker.Block(this);
        PlayerStateCoordinator.Current?.RequestInteracting(this);
        return InteractionExecutionResult.Succeeded;
    }

    public void SetInteractionPromptVisible(bool visible) { }

    public bool EnterLevelOne()
    {
        var definition = Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
        var account = AccountGameplaySession.Current;
        if (definition == null || account == null) return false;
        var map = new MapInstanceState
        {
            mapContentId = account.ContentRegistry.IdFor(definition),
            monsterThemeId = MapThemeCatalog.RollThemeId(),
            level = 1, grade = ItemGrade.Common
        };
        return StartRun(map, null);
    }

    public bool EnterSelectedMap(string instanceId)
    {
        var inventory = PlayerAccountInventoryService.SharedInventory;
        if (inventory == null || string.IsNullOrEmpty(instanceId)) return false;
        foreach (var item in inventory.Items)
            if (item != null && item.runtimeInstanceId == instanceId && item.baseData is MapItemData
                && item.mapState != null && item.mapState.level >= 1)
                return StartRun(ItemSnapshotCodec.CopyValues(item.mapState), instanceId);
        return false;
    }

    private bool StartRun(MapInstanceState map, string instanceId)
    {
        var flow = PersistentSceneFlow.Instance;
        if (flow == null || !WorldSessionState.IsHideout || flow.IsSwitching) return false;
        if (!flow.EnterRun(DiamondDungeonWorld.SceneName, map, instanceId)) return false;
        entering = true;
        ClosePanel();
        return true;
    }

    public void ClosePanel()
    {
        if (panel != null)
        {
            var old = panel;
            panel = null;
            Destroy(old.gameObject);
        }
        PlayerStateCoordinator.Current?.ReleaseInteracting(this);
        GameplayInputBlocker.Unblock(this);
    }
}
