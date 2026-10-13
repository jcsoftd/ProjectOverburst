using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

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
    private readonly List<EnemyRank> enemyRanks = new List<EnemyRank>(128);
    private readonly List<EnemyHoverEntry> enemyCandidates = new List<EnemyHoverEntry>(128);
    private uint enemyRevision;
    private bool enemyCacheDirty = true;
    private CombatHealth hoveredEnemy;
    private WorldUiOverlayService overlay;
    private PointerEventData pointer;
    private EventSystem pointerEventSystem;
    private PlayerActorRuntime actor;
    private Transform hoveredInteraction;
    private float nextPlayerRefresh;
    public WorldLootPickupRequestResult LastModelClickResult { get; private set; }
    public int ModelClickCount { get; private set; }
    public int PlayerRendererCount => playerVisual.RendererCount;
    public Transform HoveredInteraction => hoveredInteraction;
    public CombatHealth HoveredEnemy => hoveredEnemy;

    private sealed class EnemyHoverEntry
    {
        public EnemyRank Rank;
        public CombatHealth Health;
        public EnemyActor Actor;
        public EnemyOverheadHpBar Overhead;
        public Renderer[] Renderers;
        public uint Lease;
    }

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
        bool pressed = input != null && input.TryGetUiAction("Click", out var click)
            && click.enabled && click.WasPressedThisFrame();
        if (input == null || !pressed || GameplayInputBlocker.IsGameplayInputBlocked) return;
        bool overUi = ResolvePointerUi(input.PointerPosition, out bool overItemLabel);
        PlayerPickupInteractor core = PlayerPickupInteractor.ActiveCore;
        if (overUi)
        {
            input.CombatInputs?.SuppressPrimaryUntilRelease();
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
            ClearEnemyHover();
            enemyCacheDirty = true;
        }
        if (actor == null || actor.Health == null || actor.Health.IsDead)
        {
            hoveredInteraction = null;
            playerVisual.Clear();
            interactionVisual.Clear();
            ClearEnemyHover();
            return;
        }
        // 무기/모델 교체를 반영한다. 렌더러 검색은 매 프레임 하지 않는다.
        if (Time.unscaledTime >= nextPlayerRefresh)
        {
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
        if (input == null || camera == null || !Application.isFocused || GameplayInputBlocker.IsGameplayInputBlocked
            || !camera.pixelRect.Contains(input.PointerPosition)
            || ResolvePointerUi(input.PointerPosition, out _)
            || (presenter != null && presenter.ResolveModelPointerTarget(input.PointerPosition) != null))
        {
            hoveredInteraction = null;
            interactionVisual.Clear();
            ClearEnemyHover();
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
        EnemyHoverEntry enemy = ResolveEnemyHover(ray, nearest);
        if (enemy != null)
        {
            hoveredInteraction = null;
            hoveredEnemy = enemy.Health;
            interactionVisual.SetTarget(enemy.Rank.transform, enemy.Renderers);
            if (overlay == null) WorldUiOverlayService.TryResolve(out overlay);
            bool boss = enemy.Rank.GradeType == EnemyGradeType.Boss
                || (enemy.Actor != null && enemy.Actor.Identity != null && enemy.Actor.Identity.GradeType == EnemyGradeType.Boss);
            if (overlay != null && overlay.isActiveAndEnabled)
            {
                if (boss || (enemy.Overhead != null && enemy.Overhead.HasActiveNameplate)) overlay.HideHoverName();
                else overlay.ShowHoverName(enemy.Rank.DisplayName, enemy.Overhead != null
                    ? enemy.Overhead.HeadWorldPosition : ResolveModelHead(enemy));
            }
            EnemyTargetHpHud.SetExplorationHoverTarget(!boss && !PlayerCombatModeController.IsSharedCombatModeActive() ? enemy.Health : null);
            return;
        }
        ClearEnemyHover();
        if (selected == null)
        {
            hoveredInteraction = null;
            interactionVisual.Clear();
            return;
        }
        hoveredInteraction = selected.InteractionTransform;
        interactionVisual.SetTarget(hoveredInteraction, interactionRenderers[selected.InteractionComponent]);
    }

    private EnemyHoverEntry ResolveEnemyHover(Ray ray, float nearest)
    {
        if (WorldSessionState.Phase == WorldPhase.Tutorial) return null;
        if (enemyCacheDirty || enemyRevision != EnemyRank.ActiveRevision)
        {
            enemyCandidates.Clear();
            EnemyRank.CollectActive(enemyRanks);
            foreach (EnemyRank rank in enemyRanks)
            {
                var health = rank.GetComponent<CombatHealth>();
                var enemyActor = rank.GetComponent<EnemyActor>();
                if (health == null) continue;
                enemyCandidates.Add(new EnemyHoverEntry
                {
                    Rank = rank, Health = health, Actor = enemyActor,
                    Overhead = rank.GetComponent<EnemyOverheadHpBar>(),
                    Lease = enemyActor != null ? enemyActor.LeaseVersion : 0,
                    Renderers = OverburstWorldHighlight.CollectModelRenderers(enemyActor != null && enemyActor.VisualRoot != null ? enemyActor.VisualRoot : rank.transform)
                });
            }
            enemyRevision = EnemyRank.ActiveRevision;
            enemyCacheDirty = false;
        }
        var content = WorldSessionState.ContentScene;
        if (!content.IsValid() || !content.isLoaded) content = SceneManager.GetActiveScene();
        EnemyHoverEntry chosen = null;
        foreach (EnemyHoverEntry entry in enemyCandidates)
        {
            if (entry.Rank == null || !entry.Rank.isActiveAndEnabled || entry.Rank.gameObject.scene != content
                || entry.Health == null || !entry.Health.isActiveAndEnabled || entry.Health.IsDead
                || (entry.Actor != null && entry.Actor.LeaseVersion != entry.Lease)) continue;
            foreach (Renderer renderer in entry.Renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy
                    || !renderer.bounds.IntersectRay(ray, out float distance) || distance < 0f) continue;
                bool closer = distance < nearest;
                bool tie = chosen != null && Mathf.Approximately(distance, nearest)
                    && (entry.Health == hoveredEnemy || (chosen.Health != hoveredEnemy && entry.Rank.GetInstanceID() < chosen.Rank.GetInstanceID()));
                if (!closer && !tie) continue;
                nearest = distance;
                chosen = entry;
            }
        }
        return chosen;
    }

    private static Vector3 ResolveModelHead(EnemyHoverEntry enemy)
    {
        bool found = false;
        Bounds bounds = default;
        foreach (Renderer renderer in enemy.Renderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return found ? new Vector3(bounds.center.x, bounds.max.y + 0.25f, bounds.center.z)
            : enemy.Rank.transform.position + Vector3.up * 1.5f;
    }

    private void ClearEnemyHover()
    {
        hoveredEnemy = null;
        if (overlay != null) overlay.HideHoverName();
        EnemyTargetHpHud.SetExplorationHoverTarget(null);
    }

    private void OnApplicationFocus(bool focused)
    {
        if (focused) return;
        hoveredInteraction = null;
        ClearEnemyHover();
        interactionVisual.Clear();
    }

    private void OnEnable() => SceneManager.sceneUnloaded += OnSceneUnloaded;

    private void OnSceneUnloaded(Scene scene)
    {
        interactionRenderers.Clear();
        enemyCandidates.Clear();
        enemyRanks.Clear();
        enemyCacheDirty = true;
        ClearEnemyHover();
        hoveredInteraction = null;
        interactionVisual.Clear();
        nextPlayerRefresh = 0f;
    }

    private void OnDisable()
    {
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        hoveredInteraction = null;
        playerVisual.Dispose();
        interactionVisual.Dispose();
        interactionRenderers.Clear();
        ClearEnemyHover();
        enemyCandidates.Clear();
        enemyRanks.Clear();
        enemyCacheDirty = true;
    }
}
