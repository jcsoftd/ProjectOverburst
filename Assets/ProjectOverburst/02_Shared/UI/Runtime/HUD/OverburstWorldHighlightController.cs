using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>모델 클릭의 획득 의도를 전투 입력보다 먼저 판정하고 플레이어/NPC 표현을 관리한다.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-1200)]
public sealed class OverburstWorldHighlightController : MonoBehaviour
{
    private readonly OverburstWorldHighlight playerVisual = new OverburstWorldHighlight(OverburstWorldHighlightStyle.PlayerOcclusion);
    private readonly OverburstWorldHighlight interactionVisual = new OverburstWorldHighlight(OverburstWorldHighlightStyle.InteractionHover);
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    private readonly List<IInteractable> interactions = new List<IInteractable>();
    private readonly Dictionary<Component, Renderer[]> interactionRenderers = new Dictionary<Component, Renderer[]>();
    private PointerEventData pointer;
    private EventSystem pointerEventSystem;
    private PlayerActorRuntime actor;
    private Transform hoveredInteraction;
    private float nextPlayerRefresh;
    private bool pointerWasHeld;
    public WorldLootPickupRequestResult LastModelClickResult { get; private set; }
    public int ModelClickCount { get; private set; }
    public int PlayerRendererCount => playerVisual.RendererCount;
    public Transform HoveredInteraction => hoveredInteraction;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        PlayerContext context = PlayerContext.GetOrCreate();
        if (context != null && context.GetComponent<OverburstWorldHighlightController>() == null)
            context.gameObject.AddComponent<OverburstWorldHighlightController>();
    }

    private void Update()
    {
        PlayerInputFacade input = PlayerInputFacade.Current;
        bool held = input != null && input.UiClickHeld;
        bool pressed = held && !pointerWasHeld;
        pointerWasHeld = held;
        if (input == null || !pressed || GameplayInputBlocker.IsGameplayInputBlocked) return;
        bool overUi = ResolvePointerUi(input.PointerPosition, out bool overItemLabel);
        PlayerPickupInteractor core = PlayerPickupInteractor.ActiveCore;
        if (overUi)
        {
            // EventSystem은 이후 라벨 이벤트를 실행한다. 같은 프레임의 약공 누출을 먼저 막는다.
            if (overItemLabel) core?.ConsumePrimaryPointerForPickup();
            return;
        }
        WorldItemNameplatePresenter presenter = WorldItemNameplatePresenter.Active;
        WorldItemPickup target = presenter != null ? presenter.ResolveModelPointerTarget(input.PointerPosition) : null;
        if (target == null || core == null) return;
        LastModelClickResult = core.RequestPickupByModelPointerDown(target);
        ModelClickCount++;
    }

    private void LateUpdate()
    {
        PlayerContext context = PlayerContext.Instance;
        PlayerActorRuntime next = context != null ? context.CurrentActor : null;
        if (next != actor)
        {
            actor = next;
            nextPlayerRefresh = 0f;
            playerVisual.Clear();
            interactionVisual.Clear();
            interactionRenderers.Clear();
        }
        if (actor == null || actor.Health == null || actor.Health.IsDead)
        {
            playerVisual.Clear();
            interactionVisual.Clear();
            return;
        }
        // 무기/모델 교체를 반영한다. 렌더러 검색은 매 프레임 하지 않는다.
        if (Time.unscaledTime >= nextPlayerRefresh)
        {
            playerVisual.Clear();
            playerVisual.SetTarget(actor.transform, OverburstWorldHighlight.CollectModelRenderers(actor.transform));
            nextPlayerRefresh = Time.unscaledTime + 1f;
        }
        UpdateInteractionHover();
    }

    public bool ResolvePointerUi(Vector2 position, out bool itemLabel)
    {
        itemLabel = false;
        EventSystem events = EventSystem.current;
        if (events == null) return false;
        if (pointer == null || pointerEventSystem != events)
        {
            pointer = new PointerEventData(events);
            pointerEventSystem = events;
        }
        pointer.Reset();
        pointer.position = position;
        uiHits.Clear();
        events.RaycastAll(pointer, uiHits);
        foreach (RaycastResult hit in uiHits)
        {
            if (hit.gameObject == null || hit.module is not UnityEngine.UI.GraphicRaycaster) continue;
            itemLabel = hit.gameObject.GetComponentInParent<WorldItemNameplateRowView>() != null;
            return true;
        }
        return false;
    }

    private void UpdateInteractionHover()
    {
        PlayerInputFacade input = PlayerInputFacade.Current;
        WorldItemNameplatePresenter presenter = WorldItemNameplatePresenter.Active;
        Camera camera = Camera.main;
        if (input == null || camera == null || GameplayInputBlocker.IsGameplayInputBlocked
            || ResolvePointerUi(input.PointerPosition, out _)
            || (presenter != null && presenter.ResolveModelPointerTarget(input.PointerPosition) != null))
        {
            hoveredInteraction = null;
            interactionVisual.Clear();
            return;
        }
        IInteractable selected = null;
        float nearest = float.MaxValue;
        Ray ray = camera.ScreenPointToRay(input.PointerPosition);
        InteractionRegistry.CopyTo(interactions);
        foreach (IInteractable entry in interactions)
        {
            if (entry is not StashInteractable && entry is not GeneralGoodsMerchantInteractable) continue;
            Component component = entry.InteractionComponent;
            if (component == null || !component.gameObject.activeInHierarchy || !entry.IsInteractionAvailable(actor)) continue;
            if (!interactionRenderers.TryGetValue(component, out Renderer[] renderers))
            {
                renderers = OverburstWorldHighlight.CollectModelRenderers(component.transform);
                interactionRenderers[component] = renderers;
            }
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (renderer.bounds.IntersectRay(ray, out float distance) && distance >= 0f && distance < nearest)
                {
                    nearest = distance;
                    selected = entry;
                }
            }
        }
        if (selected == null)
        {
            hoveredInteraction = null;
            interactionVisual.Clear();
            return;
        }
        hoveredInteraction = selected.InteractionTransform;
        interactionVisual.SetTarget(hoveredInteraction, interactionRenderers[selected.InteractionComponent]);
    }

    private void OnDisable()
    {
        pointerWasHeld = false;
        hoveredInteraction = null;
        playerVisual.Dispose();
        interactionVisual.Dispose();
        interactionRenderers.Clear();
    }
}
