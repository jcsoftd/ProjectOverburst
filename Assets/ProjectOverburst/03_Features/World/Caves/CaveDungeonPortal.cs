using System;
using Overburst.Persistence;
using TMPro;
using UnityEngine;

namespace Overburst.Caves
{
    [DisallowMultipleComponent]
    public sealed class CaveDungeonPortal : MonoBehaviour, IInteractable
    {
        [Serializable] public struct Destination { public int platforms; public string sceneName; }
        public Destination[] destinations;
        public MapItemData mapDefinition;
        public CavePortalPanel panelPrefab;
        public bool returnToTown;
        [SerializeField] int selectedIndex = 2;
        CavePortalPanel panel;
        GameObject prompt;
        bool entering;
        int closedFrame = -1;
        public int SelectedIndex => selectedIndex;
        public int SelectedCount => destinations != null && destinations.Length > 0 ? destinations[Mathf.Clamp(selectedIndex, 0, destinations.Length - 1)].platforms : 0;
        public Component InteractionComponent => this;
        public Transform InteractionTransform => transform;
        public int InteractionPriority => 320;
        public string InteractionPrompt => returnToTown ? "동굴 탐색 종료 · 마을 귀환" : "동굴 입장 · 플랫폼 수 선택";
        public InteractionDistanceMode DistanceMode => InteractionDistanceMode.Horizontal;
        public float InteractionRange => 3f;
        public string StableInteractionId => InteractionStableIdUtility.Build(this);
        public bool AllowsInteractionWhileInputBlocked => false;
        public bool WantsInteractionPrompt => panel == null && !entering;

        void Start()
        {
            gameObject.AddComponent<HideoutPortalVisual>().Configure(HideoutPortalKind.Dungeon);
            var title = transform.Find("Portal Name")?.GetComponent<TMP_Text>();
            if (title) title.text = returnToTown ? "마을 귀환" : "동굴 탐험";
            var source = FindFirstObjectByType<WorldInteractionKeyPrompt>(FindObjectsInactive.Include);
            if (source)
            {
                prompt = Instantiate(source.gameObject, transform, false);
                prompt.transform.localPosition = Vector3.up * 2.3f; prompt.SetActive(false);
            }
        }
        void OnEnable() => InteractionRegistry.Register(this);
        void OnDisable() { InteractionRegistry.Unregister(this); ClosePanel(); SetInteractionPromptVisible(false); }
        void Update()
        {
            var flow = PersistentSceneFlow.Instance;
            if (entering && WorldSessionState.IsHideout && flow && !flow.IsSwitching) entering = false;
            var input = PlayerInputFacade.Current;
            if (panel && input != null && (input.UiCancelPressedThisFrame || input.InteractPressedThisFrame)) ClosePanel();
        }
        public bool IsInteractionAvailable(PlayerActorRuntime actor)
            => actor && panel == null && !entering && closedFrame != Time.frameCount
                && PersistentSceneFlow.Instance && !PersistentSceneFlow.Instance.IsSwitching
                && (returnToTown ? WorldSessionState.Phase == WorldPhase.Run : WorldSessionState.IsHideout);
        public InteractionExecutionResult TryInteract(PlayerActorRuntime actor)
        {
            if (!IsInteractionAvailable(actor)) return InteractionExecutionResult.Rejected;
            if (returnToTown)
            {
                var driver = PersistentSceneFlow.Instance.GetComponent<RunLifetimeDriver>();
                if (!driver) return InteractionExecutionResult.Rejected;
                driver.RequestAbandon(); entering = true;
                return InteractionExecutionResult.StartedTransition;
            }
            var ui = FindFirstObjectByType<OverburstGameUI>(FindObjectsInactive.Include);
            if (!ui || !ui.inventoryWindow || !panelPrefab || destinations == null || destinations.Length == 0)
                return InteractionExecutionResult.Rejected;
            panel = Instantiate(panelPrefab, ui.inventoryWindow.transform.parent, false);
            panel.Configure(this);
            GameplayInputBlocker.Block(this); PlayerStateCoordinator.Current?.RequestInteracting(this);
            SetInteractionPromptVisible(false);
            return InteractionExecutionResult.Succeeded;
        }
        public void SelectIndex(int index)
        { if (destinations != null && destinations.Length > 0) selectedIndex = Mathf.Clamp(index, 0, destinations.Length - 1); }
        public bool EnterSelected(out string error)
        {
            error = null;
            var account = AccountGameplaySession.Current; var flow = PersistentSceneFlow.Instance;
            if (returnToTown || entering || !WorldSessionState.IsHideout || !flow || flow.IsSwitching || account == null || !mapDefinition || SelectedCount == 0)
            { error = "아직 입장 준비가 되지 않았습니다."; return false; }
            SelectIndex(selectedIndex);
            try
            {
                var map = new MapInstanceState { mapContentId = account.ContentRegistry.IdFor(mapDefinition),
                    monsterThemeId = "CavernMutants", level = 1, grade = ItemGrade.Common };
                if (!flow.EnterRun(destinations[selectedIndex].sceneName, map))
                { error = flow.RunEntryError ?? "동굴 입장을 시작하지 못했습니다."; return false; }
                entering = true; ClosePanel(); return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
        public void ClosePanel()
        {
            if (panel) { var old = panel; panel = null; closedFrame = Time.frameCount; Destroy(old.gameObject); }
            PlayerStateCoordinator.Current?.ReleaseInteracting(this); GameplayInputBlocker.Unblock(this);
        }
        public void SetInteractionPromptVisible(bool visible) { if (prompt) prompt.SetActive(visible); }
    }
}
