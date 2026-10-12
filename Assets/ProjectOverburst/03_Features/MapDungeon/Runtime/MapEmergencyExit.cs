using System;
using System.Collections.Generic;
using Overburst.Persistence;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

// One optional exit, derived from the stored map seed before the map is consumed.
[DisallowMultipleComponent]
[DefaultExecutionOrder(30000)]
public sealed class MapEmergencyExit : MonoBehaviour, IInteractable
{
    public const float ChannelSeconds = 3f;
    const int SeedSalt = 0x4e584954;
    RunWorldGate gate;
    PlayerActorRuntime player;
    CombatHealth health;
    RunLifetimeDriver driver;
    TMP_Text label;
    GameObject prompt;
    Action confirmAction;
    OverburstGameMenu confirmationMenu;
    float elapsed;
    bool channeling, requested, confirmationPending;
    public int AreaIndex { get; private set; }
    public bool Discovered { get; private set; }
    public bool IsChanneling => channeling;
    public float ChannelProgress => Mathf.Clamp01(elapsed / ChannelSeconds);
    public bool ExtractionPending => requested;
    public string RunId => gate ? gate.Context.RunId : null;
    public Component InteractionComponent => this;
    public Transform InteractionTransform => transform;
    public int InteractionPriority => 310;
    public string InteractionPrompt => channeling ? "F : 비상탈출 취소" : "F : 비상탈출";
    public InteractionDistanceMode DistanceMode => InteractionDistanceMode.Horizontal;
    public float InteractionRange => 3.2f;
    public string StableInteractionId => RunId + "/emergency-exit";
    public bool AllowsInteractionWhileInputBlocked => channeling;
    public bool WantsInteractionPrompt => Discovered && !confirmationPending && isActiveAndEnabled;

    // Keep this salt and draw order stable within generationVersion1.
    public static bool IsSelected(MapInstanceState map) => map != null && map.generationVersion > 0
        && (map.terrain == MapTerrainKind.Cave || map.terrain == MapTerrainKind.Desert)
        && new System.Random(map.seed ^ SeedSalt).Next(5) == 0;

    public static MapEmergencyExit Prepare(RunWorldGate world, PlayerActorRuntime actor)
    {
        if (!world || !world.IsReady || world.Roster == null || !actor) throw new InvalidOperationException("Prepared map and player required.");
        if (world.EmergencyExit) throw new InvalidOperationException("An emergency exit is already prepared.");
        if (!IsSelected(world.Map)) return null;
        var areas = world.Roster.Areas;
        int entry = -1; float nearest = float.PositiveInfinity;
        foreach (var area in areas)
        {
            if (MapCombatGeometry.Contains(area.Ground, world.EntryPoint.position)) { entry = area.Ground.Index; break; }
            float distance = (area.Ground.Center - world.EntryPoint.position).sqrMagnitude;
            if (distance < nearest) { nearest = distance; entry = area.Ground.Index; }
        }
        var candidates = new List<MapCombatGeometry.Area>();
        foreach (var area in areas) if (area.Ground.Index != entry && area.Ground.Points.Count > 0) candidates.Add(area.Ground);
        candidates.Sort((a, b) => a.Index.CompareTo(b.Index));
        var rng = new System.Random(world.Map.seed ^ SeedSalt); rng.Next(5);
        var path = new NavMeshPath(); var overlaps = new Collider[64];
        int start = candidates.Count > 0 ? rng.Next(candidates.Count) : 0;
        for (int a = 0; a < candidates.Count; a++)
        {
            var area = candidates[(start + a) % candidates.Count];
            int first = rng.Next(area.Points.Count);
            for (int i = 0; i < area.Points.Count; i++)
            {
                if (!MapCombatGeometry.TryCompletionPoint(area, area.Points[(first + i) % area.Points.Count], 1f, overlaps, path, out var point)) continue;
                var root = new GameObject("Map Emergency Exit"); root.SetActive(false); root.transform.SetParent(world.transform, false);
                MapEmergencyExit exit = null;
                try
                {
                    root.transform.position = point;
                    exit = root.AddComponent<MapEmergencyExit>();
                    exit.gate = world; exit.player = actor; exit.health = actor.Health; exit.AreaIndex = area.Index;
                    exit.driver = PersistentSceneFlow.Instance ? PersistentSceneFlow.Instance.GetComponent<RunLifetimeDriver>() : null;
                    var visual = root.AddComponent<HideoutPortalVisual>(); visual.Configure(HideoutPortalKind.Dungeon);
                    if (!visual.HasAssetPresentation) throw new InvalidOperationException("비상탈출 포탈 표시 자산이 없습니다.");
                    exit.label = root.transform.Find("Portal Name").GetComponent<TMP_Text>(); exit.label.text = "비상탈출";
                    var key = FindFirstObjectByType<WorldInteractionKeyPrompt>(FindObjectsInactive.Include);
                    if (key)
                    {
                        exit.prompt = Instantiate(key.gameObject, root.transform, false);
                        exit.prompt.name = "Emergency Exit Interaction Key";
                        exit.prompt.transform.localPosition = Vector3.up * 2.3f; exit.prompt.SetActive(false);
                    }
                    root.SetActive(true); world.EmergencyExit = exit; return exit;
                }
                catch { Destroy(root); throw; }
            }
        }
        // Do not consume a map whose selected exit cannot be placed on reachable ground.
        throw new InvalidOperationException("입구 외 전투 타일에 비상탈출구를 배치하지 못했습니다. 지도는 소모하지 않습니다.");
    }

    void OnEnable()
    {
        InteractionRegistry.Register(this);
        if (health) health.OnDamaged += Damaged;
    }
    void OnDisable()
    {
        InteractionRegistry.Unregister(this);
        if (health) health.OnDamaged -= Damaged;
        if (confirmationPending && confirmationMenu) confirmationMenu.CancelModal(confirmAction);
        confirmationPending = false; confirmationMenu = null; confirmAction = null;
        CancelChannel(); SetInteractionPromptVisible(false);
    }
    void Damaged(CombatHealth target, DamageInfo damage) => CancelChannel();
    bool IsCurrentRun()
    {
        if (!isActiveAndEnabled || !gate || !player || !health || health.IsDead
            || WorldSessionState.Phase != WorldPhase.Run || WorldSessionState.ContentScene != gameObject.scene
            || PersistentSceneFlow.Instance?.IsSwitching == true) return false;
        var run = AccountGameplaySession.Current?.ReadRun();
        return run != null && run.runId == RunId && AccountInvariants.IsRunning(run.phase);
    }
    bool InRange()
    {
        if (!player) return false;
        var d = player.transform.position - transform.position;
        return d.x * d.x + d.z * d.z <= InteractionRange * InteractionRange && Mathf.Abs(d.y) < 2f;
    }
    public bool CanExtract => requested && channeling && elapsed >= ChannelSeconds && Discovered && IsCurrentRun() && InRange();
    public bool IsInteractionAvailable(PlayerActorRuntime actor)
        => actor == player && Discovered && !confirmationPending && IsCurrentRun() && InRange();

    public InteractionExecutionResult TryInteract(PlayerActorRuntime actor)
    {
        if (!IsInteractionAvailable(actor)) return InteractionExecutionResult.Rejected;
        if (channeling) { CancelChannel(); return InteractionExecutionResult.ClosedSession; }
        var run = AccountGameplaySession.Current.ReadRun();
        if (AccountInvariants.IsCompleted(run.phase))
            return driver && driver.RequestPortalExit() ? InteractionExecutionResult.StartedTransition : InteractionExecutionResult.Rejected;
        var menu = OverburstGameMenu.Instance;
        if (!menu || OverburstGameMenu.IsOpen || !driver) return InteractionExecutionResult.Rejected;
        confirmationPending = true; confirmationMenu = menu;
        confirmAction = () =>
        {
            menu.Close();
            if (!this) return;
            confirmationPending = false; confirmationMenu = null; confirmAction = null;
            if (!IsCurrentRun() || !InRange()) return;
            channeling = true; elapsed = 0; requested = false;
            GameplayInputBlocker.Block(this);
            player.GetComponent<PlayerStateCoordinator>()?.RequestInteracting(this);
        };
        menu.Open();
        menu.Confirm("지금 비상탈출할까요?", "소지한 전리품을 반출하고 남은 탐험을 포기합니다.\n입장 지도와 완료 보상은 받지 않습니다.\n3초 동안 진행하며, 피격되면 취소됩니다.", "탈출 시작", "계속 탐험", confirmAction,
            () => { menu.Close(); if (this) { confirmationPending = false; confirmationMenu = null; confirmAction = null; } });
        return InteractionExecutionResult.Succeeded;
    }
    public void SetInteractionPromptVisible(bool visible) { if (prompt) prompt.SetActive(visible && WantsInteractionPrompt); }
    void LateUpdate()
    {
        if (!IsCurrentRun()) { if (channeling) CancelChannel(); return; }
        if (!Discovered && WorldMinimapController.Instance?.Exploration?.IsRevealed(transform.position) == true) Discovered = true;
        if (!channeling) return;
        if (!InRange() || OverburstTimeEffectArbiter.IsPaused) { CancelChannel(); return; }
        if (requested) return;
        elapsed = Mathf.Min(ChannelSeconds, elapsed + Time.deltaTime);
        if (label) label.text = "비상탈출 · " + Mathf.CeilToInt(ChannelSeconds - elapsed) + "초";
        if (elapsed < ChannelSeconds) return;
        requested = true;
        if (!driver || !driver.RequestEmergencyExit(this)) CancelChannel();
        else if (label) label.text = "비상탈출 · 저장 중";
    }
    public void CancelChannel()
    {
        channeling = requested = false; elapsed = 0;
        if (driver) driver.CancelEmergencyExit(this);
        if (player) player.GetComponent<PlayerStateCoordinator>()?.ReleaseInteracting(this);
        GameplayInputBlocker.Unblock(this);
        if (label) label.text = "비상탈출";
    }
}
