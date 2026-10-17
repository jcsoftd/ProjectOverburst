using System.Collections.Generic;
using UnityEngine;

public enum EnemySquadPursuitRuntimeMode // 실제 게임에서 사용하는 부대 이동 모드
{
    None,
    Pursuit,
    Reserve,
    Rush,
    NearCombat,
    Remnant,
    Dead
}

public readonly struct EnemySquadPursuitMovePlan // Chase가 소비할 부대 이동 명령
{
    public EnemySquadPursuitMovePlan(
        Vector3 destination,
        EnemyLocomotionMode locomotion,
        float speedMultiplier,
        EnemySquadPursuitRuntimeMode mode)
    {
        Destination = destination;
        Locomotion = locomotion;
        SpeedMultiplier = speedMultiplier;
        Mode = mode;
    }

    public Vector3 Destination { get; }
    public EnemyLocomotionMode Locomotion { get; }
    public float SpeedMultiplier { get; }
    public EnemySquadPursuitRuntimeMode Mode { get; }
}

public readonly struct EnemySquadPursuitRuntimeStats // 디버그 HUD용 현재 부대 집계
{
    public EnemySquadPursuitRuntimeStats(
        int registeredAgentCount,
        int combatEligibleAgentCount,
        int activationCount,
        int activeEncounterCount,
        int squadCount,
        int pursuitCount,
        int reserveCount,
        int rushCount,
        int nearCombatCount,
        int remnantCount)
    {
        RegisteredAgentCount = registeredAgentCount;
        CombatEligibleAgentCount = combatEligibleAgentCount;
        ActivationCount = activationCount;
        ActiveEncounterCount = activeEncounterCount;
        SquadCount = squadCount;
        PursuitCount = pursuitCount;
        ReserveCount = reserveCount;
        RushCount = rushCount;
        NearCombatCount = nearCombatCount;
        RemnantCount = remnantCount;
    }

    public int RegisteredAgentCount { get; }
    public int CombatEligibleAgentCount { get; }
    public int ActivationCount { get; }
    public int ActiveEncounterCount { get; }
    public int SquadCount { get; }
    public int PursuitCount { get; }
    public int ReserveCount { get; }
    public int RushCount { get; }
    public int NearCombatCount { get; }
    public int RemnantCount { get; }
    public bool IsActive => ActiveEncounterCount > 0;
}

public readonly struct EnemySquadPursuitDebugEncounter // 월드 원형 범위 한 세트
{
    public EnemySquadPursuitDebugEncounter(
        Vector3 anchor,
        float nearRadius,
        float slotRadius,
        float commitRadius,
        float farRadius,
        int slotStartIndex,
        int slotCount)
    {
        Anchor = anchor;
        NearRadius = nearRadius;
        SlotRadius = slotRadius;
        CommitRadius = commitRadius;
        FarRadius = farRadius;
        SlotStartIndex = slotStartIndex;
        SlotCount = slotCount;
    }

    public Vector3 Anchor { get; }
    public float NearRadius { get; }
    public float SlotRadius { get; }
    public float CommitRadius { get; }
    public float FarRadius { get; }
    public int SlotStartIndex { get; }
    public int SlotCount { get; }
}

public readonly struct EnemySquadPursuitDebugSlot // 월드 슬롯점 한 개
{
    public EnemySquadPursuitDebugSlot(
        Vector3 position,
        EnemySquadPursuitSlotKind kind,
        bool occupied)
    {
        Position = position;
        Kind = kind;
        Occupied = occupied;
    }

    public Vector3 Position { get; }
    public EnemySquadPursuitSlotKind Kind { get; }
    public bool Occupied { get; }
}

public static class EnemySquadPursuitRuntimeService // 프리셋별 대량 웨이브 부대 편성과 슬롯 이동
{
    private const float FormationOffsetScale = 0.52f;
    private const float CenterSmoothSpeed = 5f;
    private const float FarReentryDelay = 0.65f;
    private const float RouteRefreshMoveThreshold = EnemySquadPursuitPlanner.DefaultRouteRefreshMoveThreshold;
    private const float RouteRefreshMinimumInterval = EnemySquadPursuitPlanner.DefaultRouteRefreshMinimumInterval;
    private const float RouteRefreshHeadingThreshold = EnemySquadPursuitPlanner.DefaultRouteRefreshHeadingThreshold;
    private const int MaximumRouteRefreshPerFrame = EnemySquadPursuitPlanner.DefaultMaximumRouteRefreshPerFrame;
    private const float FullReformationInterval = EnemySquadPursuitPlanner.DefaultFullReformationInterval;
    private const float SlotLeaseDuration = 2.5f;
    private const int StableSeed = 20260715;

    private sealed class AgentData
    {
        public EnemyAIController Controller;
        public EncounterData Encounter;
        public SquadData Squad;
        public int SpawnPackId;
        public Vector3 LocalOffset;
    }

    private sealed class SquadData
    {
        public int Id;
        public int SpawnPackId;
        public float LastMemberJoined;
        public EnemyNavigationRoute Navigation;
        public readonly List<AgentData> Members = new List<AgentData>(16);
        public int InitialCount;
        public int SlotIndex = -1;
        public int RouteWaypointIndex;
        public EnemySquadPursuitRoute Route;
        public Vector3 RouteSlotDirection;
        public bool DirectCommitted;
        public bool HasRushed;
        public bool SlotEligibilityRevoked;
        public EnemySquadPursuitRuntimeMode Mode = EnemySquadPursuitRuntimeMode.Reserve;
        public Vector3 RawCenter;
        public Vector3 SmoothedCenter;
        public float FarEligibleTime;
        public float SlotLeaseUntil;

        public int AliveCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Members.Count; i++)
                {
                    if (IsAgentAlive(Members[i]))
                        count++;
                }
                return count;
            }
        }

        public int ParticipantCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Members.Count; i++)
                {
                    if (IsAgentParticipating(Members[i]))
                        count++;
                }
                return count;
            }
        }
    }

    private sealed class EncounterData
    {
        public GameObject Owner;
        public Transform Anchor;
        public EnemyAiPreset Preset;
        public readonly List<AgentData> Agents = new List<AgentData>(128);
        public readonly List<SquadData> Squads = new List<SquadData>(24);
        public readonly List<EnemySquadPursuitSlot> Slots = new List<EnemySquadPursuitSlot>(8);
        public readonly List<int> SquadSizes = new List<int>(24);
        public readonly List<AgentData> Unassigned = new List<AgentData>(128);
        public readonly List<SquadData> ReserveSquads = new List<SquadData>(24);
        public readonly List<Vector3> ReserveCenters = new List<Vector3>(24);
        public readonly List<EnemySquadPursuitSlot> AssignableSlots = new List<EnemySquadPursuitSlot>(7);
        public readonly List<int> BalancedSlots = new List<int>(7);
        public readonly List<int> VacantSlots = new List<int>(7);
        public readonly List<Vector3> VacantPositions = new List<Vector3>(7);
        public readonly List<int> SquadIndexBySlot = new List<int>(7);
        public float NextRecruitTime;
        public int RecruitCursor;
        public readonly Dictionary<int, SquadData> SpawnPacks = new Dictionary<int, SquadData>();
        public bool Active;
        public bool FrameInitialized;
        public int NextSquadId = 1;
        public Vector3 DirectOutward = Vector3.right;
        public Vector3 LastRouteTargetPosition;
        public Vector3 LastRouteTranslationPosition;
        public Vector3 LastFullReformationPosition;
        public float NextRouteRefreshTime;
        public float LastFullReformationTime;
        public float NextFullReformationTime;
        public int RouteRefreshCursor;
        public bool RouteRefreshPending;
    }

    private readonly struct EncounterKey : System.IEquatable<EncounterKey>
    {
        public readonly GameObject Owner;
        public readonly EnemyAiPreset Preset;
        private readonly int ownerId;
        private readonly int presetId;

        public EncounterKey(GameObject owner, EnemyAiPreset preset)
        {
            Owner = owner;
            Preset = preset;
            ownerId = owner != null ? owner.GetInstanceID() : 0;
            presetId = preset != null ? preset.GetInstanceID() : 0;
        }

        public bool Equals(EncounterKey other)
        {
            return ReferenceEquals(Owner, other.Owner) && ReferenceEquals(Preset, other.Preset);
        }

        public override bool Equals(object obj)
        {
            return obj is EncounterKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (ownerId * 397) ^ presetId;
            }
        }
    }

    private static readonly Dictionary<EnemyAIController, AgentData> Agents =
        new Dictionary<EnemyAIController, AgentData>(256);
    private static readonly Dictionary<EncounterKey, EncounterData> Encounters =
        new Dictionary<EncounterKey, EncounterData>(8);
    private static readonly List<EnemyAIController> StaleControllers = new List<EnemyAIController>(32);
    private static readonly List<EncounterKey> StaleEncounterKeys = new List<EncounterKey>(8);
    private static int updatedFrame = -1;
    private static float lastUpdateTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Agents.Clear();
        Encounters.Clear();
        StaleControllers.Clear();
        StaleEncounterKeys.Clear();
        updatedFrame = -1;
        lastUpdateTime = 0f;
    }

    public static void Register(EnemyAIController controller)
    {
        if (controller == null || !controller.UsesSquadPursuit || Agents.ContainsKey(controller))
            return;

        Agents.Add(controller, new AgentData { Controller = controller });
        updatedFrame = -1;
    }

    public static void Unregister(EnemyAIController controller)
    {
        if (controller == null || !Agents.TryGetValue(controller, out AgentData agent))
            return;

        RemoveAgentFromEncounter(agent);
        Agents.Remove(controller);
        updatedFrame = -1;
    }

    public static void NotifyEncounterBindingChanged(EnemyAIController controller)
    {
        if (controller == null || !Agents.TryGetValue(controller, out AgentData agent))
            return;

        if (agent.Encounter != null
            && (agent.Encounter.Owner != controller.SquadEncounterOwner
                || agent.Encounter.Preset != controller.SquadPursuitPreset || agent.SpawnPackId != controller.SpawnPackId))
            RemoveAgentFromEncounter(agent); // 실제 전투 구역 변경만 기존 부대 해제
        else if (agent.Encounter != null)
        {
            Transform nextAnchor = controller.SquadEncounterAnchor;
            bool anchorChanged = agent.Encounter.Anchor != nextAnchor;
            agent.Encounter.Anchor = nextAnchor; // 같은 구역은 부대 ID를 유지하고 추적 중심만 갱신
            if (anchorChanged && nextAnchor != null)
            {
                agent.Encounter.FrameInitialized = false;
                agent.Encounter.RouteRefreshPending = true;
                agent.Encounter.RouteRefreshCursor = 0;
                agent.Encounter.NextRouteRefreshTime = 0f;
                agent.Encounter.LastFullReformationPosition = nextAnchor.position;
                agent.Encounter.LastFullReformationTime = Time.time;
                agent.Encounter.NextFullReformationTime = Time.time + FullReformationInterval;
            }
        }
        updatedFrame = -1;
    }

    public static bool TryResolveMovePlan(
        EnemyAIController controller,
        out EnemySquadPursuitMovePlan plan)
    {
        plan = default;
        EnsureUpdated();
        if (controller == null
            || !Agents.TryGetValue(controller, out AgentData agent)
            || agent.Squad == null
            || agent.Encounter == null
            || !agent.Encounter.Active
            || !IsSquadMoveEligible(agent))
        {
            return false;
        }

        SquadData squad = agent.Squad;
        EncounterData encounter = agent.Encounter;
        Vector3 destination;
        EnemyLocomotionMode locomotion;
        float speedMultiplier;
        switch (squad.Mode)
        {
            case EnemySquadPursuitRuntimeMode.Pursuit:
                Vector3 pursuitDestination = ResolveSquadDestination(encounter, squad);
                destination = EnemySquadPursuitPlanner.ResolveCohesionDestination(
                    controller.transform.position,
                    squad.SmoothedCenter,
                    agent.LocalOffset,
                    pursuitDestination,
                    FormationOffsetScale);
                locomotion = EnemyLocomotionMode.Run;
                speedMultiplier = EnemySquadPursuitPlanner.ResolveCohesionSpeedMultiplier(
                    controller.transform.position,
                    squad.SmoothedCenter,
                    pursuitDestination);
                break;
            case EnemySquadPursuitRuntimeMode.Reserve:
                Vector3 reserveDestination = ResolveSquadDestination(encounter, squad);
                destination = EnemySquadPursuitPlanner.ResolveCohesionDestination(
                    controller.transform.position,
                    squad.SmoothedCenter,
                    agent.LocalOffset,
                    reserveDestination,
                    FormationOffsetScale);
                locomotion = EnemyLocomotionMode.Walk;
                speedMultiplier = encounter.Preset.ReserveSpeedMultiplier
                    * EnemySquadPursuitPlanner.ResolveCohesionSpeedMultiplier(
                        controller.transform.position,
                        squad.SmoothedCenter,
                        reserveDestination);
                break;
            case EnemySquadPursuitRuntimeMode.Rush:
                destination = ResolveCombatTargetPosition(agent, encounter.Anchor.position);
                locomotion = HorizontalDistance(controller.transform.position, destination)
                    > encounter.Preset.NearReleaseDistance
                    ? EnemyLocomotionMode.Run
                    : EnemyLocomotionMode.Walk;
                speedMultiplier = 1f;
                break;
            default:
                return false; // 근거리는 기존 9상태 전투가 그대로 소유
        }

        destination.y = controller.transform.position.y;
        plan = new EnemySquadPursuitMovePlan(destination, locomotion, speedMultiplier, squad.Mode);
        return true;
    }

    public static bool HasNearbyEngagedGroupMember(
        EnemyAIController controller,
        float combatRange)
    {
        EnsureUpdated();
        if (controller == null
            || !Agents.TryGetValue(controller, out AgentData requester)
            || requester.Encounter == null)
        {
            return false;
        }

        List<AgentData> candidates = requester.Squad != null
            ? requester.Squad.Members
            : requester.Encounter.Agents; // 편성 전에는 같은 전투 구역을 임시 부대로 취급
        float range = Mathf.Max(0f, combatRange);
        float combatRangeSqr = range * range;
        for (int i = 0; i < candidates.Count; i++)
        {
            AgentData ally = candidates[i];
            if (ally == requester || !IsAgentParticipating(ally) || !ally.Controller.IsTargetValid())
                continue;
            if (HorizontalSqrDistance(controller.transform.position, ally.Controller.transform.position) > combatRangeSqr)
                continue; // 멀리 떨어진 부대원끼리 어그로만 영구 유지하지 않음

            Transform allyTarget = ally.Controller.Target;
            if (HorizontalSqrDistance(ally.Controller.transform.position, allyTarget.position) > combatRangeSqr)
                continue; // 실제 교전 거리 안의 동료만 유지 신호로 인정

            return true; // 신호만 전달하고 동료의 근접 잠금 Target은 복사하지 않음
        }
        return false;
    }

    public static int GetMovePriority(EnemyAIController controller)
    {
        EnsureUpdated();
        if (controller == null
            || !Agents.TryGetValue(controller, out AgentData agent)
            || agent.Squad == null
            || !IsAgentParticipating(agent))
        {
            return 0;
        }

        switch (agent.Squad.Mode)
        {
            case EnemySquadPursuitRuntimeMode.NearCombat:
            case EnemySquadPursuitRuntimeMode.Remnant:
                return 3;
            case EnemySquadPursuitRuntimeMode.Rush:
                return 2;
            case EnemySquadPursuitRuntimeMode.Pursuit:
                return 1;
            default:
                return 0;
        }
    }

    public static bool IsReserveMovement(EnemyAIController controller)
    {
        EnsureUpdated();
        return controller != null
            && Agents.TryGetValue(controller, out AgentData agent)
            && agent.Squad != null
            && IsAgentParticipating(agent)
            && agent.Squad.Mode == EnemySquadPursuitRuntimeMode.Reserve;
    }

    public static string GetDebugModeName(EnemyAIController controller)
    {
        EnsureUpdated();
        if (controller == null || !Agents.TryGetValue(controller, out AgentData agent) || agent.Squad == null)
            return string.Empty;

        if (IsAgentAlive(agent) && !IsAgentParticipating(agent))
            return "Squad" + agent.Squad.Id + "/Inactive";

        string slot = agent.Squad.SlotIndex >= 0
            ? "/P" + agent.Squad.SlotIndex
            : string.Empty;
        return "Squad" + agent.Squad.Id + "/" + agent.Squad.Mode + slot;
    }

    public static EnemySquadPursuitRuntimeStats GetRuntimeStats()
    {
        EnsureUpdated();
        int activationCount = EnemySquadPursuitPlanner.DefaultActivationCount;
        bool hasPreset = false;
        int combatEligibleCount = 0;
        int activeEncounterCount = 0;
        int squadCount = 0;
        int pursuitCount = 0;
        int reserveCount = 0;
        int rushCount = 0;
        int nearCombatCount = 0;
        int remnantCount = 0;

        foreach (KeyValuePair<EncounterKey, EncounterData> pair in Encounters)
        {
            EncounterData encounter = pair.Value;
            if (encounter == null || encounter.Preset == null)
                continue;
            activationCount = hasPreset
                ? Mathf.Min(activationCount, encounter.Preset.ActivationCount)
                : encounter.Preset.ActivationCount;
            hasPreset = true;
            combatEligibleCount += CountCombatEligibleAgents(encounter);
            if (!encounter.Active)
                continue;

            activeEncounterCount++;
            for (int i = 0; i < encounter.Squads.Count; i++)
            {
                SquadData squad = encounter.Squads[i];
                if (squad.AliveCount <= 0)
                    continue;
                squadCount++;
                switch (squad.Mode)
                {
                    case EnemySquadPursuitRuntimeMode.Pursuit:
                        pursuitCount++;
                        break;
                    case EnemySquadPursuitRuntimeMode.Reserve:
                        reserveCount++;
                        break;
                    case EnemySquadPursuitRuntimeMode.Rush:
                        rushCount++;
                        break;
                    case EnemySquadPursuitRuntimeMode.NearCombat:
                        nearCombatCount++;
                        break;
                    case EnemySquadPursuitRuntimeMode.Remnant:
                        remnantCount++;
                        break;
                }
            }
        }

        return new EnemySquadPursuitRuntimeStats(
            Agents.Count,
            combatEligibleCount,
            activationCount,
            activeEncounterCount,
            squadCount,
            pursuitCount,
            reserveCount,
            rushCount,
            nearCombatCount,
            remnantCount);
    }

    public static void CollectDebugDrawData(
        List<EnemySquadPursuitDebugEncounter> encounterResults,
        List<EnemySquadPursuitDebugSlot> slotResults)
    {
        if (encounterResults == null)
            throw new System.ArgumentNullException(nameof(encounterResults));
        if (slotResults == null)
            throw new System.ArgumentNullException(nameof(slotResults));

        encounterResults.Clear();
        slotResults.Clear();
        EnsureUpdated();
        foreach (KeyValuePair<EncounterKey, EncounterData> pair in Encounters)
        {
            EncounterData encounter = pair.Value;
            if (encounter == null || !encounter.Active || encounter.Anchor == null || encounter.Preset == null)
                continue;

            int slotStart = slotResults.Count;
            for (int i = 0; i < encounter.Slots.Count; i++)
            {
                EnemySquadPursuitSlot slot = encounter.Slots[i];
                slotResults.Add(new EnemySquadPursuitDebugSlot(
                    slot.Position,
                    slot.Kind,
                    FindSlotOwner(encounter, i) != null));
            }

            encounterResults.Add(new EnemySquadPursuitDebugEncounter(
                encounter.Anchor.position,
                encounter.Preset.NearReleaseDistance,
                encounter.Preset.SlotRadius,
                encounter.Preset.DirectCommitRadius,
                encounter.Preset.FarActivationDistance,
                slotStart,
                encounter.Slots.Count));
        }
    }

    private static void EnsureUpdated()
    {
        int frame = Time.frameCount;
        if (updatedFrame == frame)
            return;

        using (SquadUpdateMarker.Auto())
        {
            updatedFrame = frame;
            float now = Time.time;
            float deltaTime = lastUpdateTime > 0f ? Mathf.Clamp(now - lastUpdateTime, 0f, 0.1f) : Time.deltaTime;
            lastUpdateTime = now;
            RefreshAgentEncounters();
            StaleEncounterKeys.Clear();
            foreach (KeyValuePair<EncounterKey, EncounterData> pair in Encounters)
            {
                EncounterData encounter = pair.Value;
                if (pair.Key.Owner == null
                    || pair.Key.Preset == null
                    || encounter == null
                    || encounter.Anchor == null
                    || encounter.Agents.Count <= 0)
                {
                    StaleEncounterKeys.Add(pair.Key);
                    continue;
                }

                UpdateEncounter(encounter, deltaTime, now);
            }
            for (int i = 0; i < StaleEncounterKeys.Count; i++)
                Encounters.Remove(StaleEncounterKeys[i]);
        }
    }
    private static readonly Unity.Profiling.ProfilerMarker SquadUpdateMarker = new Unity.Profiling.ProfilerMarker("Overburst.AI.SquadUpdate");

    private static readonly Unity.Profiling.ProfilerMarker EncounterBindingMarker = new Unity.Profiling.ProfilerMarker("Overburst.AI.SquadBinding");

    private static void RefreshAgentEncounters()
    {
        using var bindingSample = EncounterBindingMarker.Auto();
        StaleControllers.Clear();
        foreach (KeyValuePair<EnemyAIController, AgentData> pair in Agents)
        {
            EnemyAIController controller = pair.Key;
            AgentData agent = pair.Value;
            if (controller == null || !controller.isActiveAndEnabled || !controller.UsesSquadPursuit)
            {
                StaleControllers.Add(controller);
                continue;
            }

            GameObject encounterOwner = controller.SquadEncounterOwner;
            Transform encounterAnchor = controller.SquadEncounterAnchor;
            EnemyAiPreset preset = controller.SquadPursuitPreset;
            if (encounterOwner == null || encounterAnchor == null || preset == null)
            {
                RemoveAgentFromEncounter(agent);
                continue;
            }
            if (agent.Encounter != null
                && agent.Encounter.Owner == encounterOwner
                && agent.Encounter.Preset == preset && agent.SpawnPackId == controller.SpawnPackId)
            {
                agent.Encounter.Anchor = encounterAnchor;
                continue;
            }

            RemoveAgentFromEncounter(agent);
            EncounterKey key = new EncounterKey(encounterOwner, preset);
            if (!Encounters.TryGetValue(key, out EncounterData encounter))
            {
                encounter = new EncounterData
                {
                    Owner = encounterOwner,
                    Anchor = encounterAnchor,
                    Preset = preset
                };
                Encounters.Add(key, encounter);
            }
            else
            {
                encounter.Anchor = encounterAnchor;
            }
            encounter.Agents.Add(agent);
            agent.Encounter = encounter;
            agent.SpawnPackId = controller.SpawnPackId;
            if (controller.SpawnPackId > 0 && controller.UsesMeleeSquadMovement)
                BindSpawnPack(encounter, agent);
        }

        for (int i = 0; i < StaleControllers.Count; i++)
        {
            EnemyAIController controller = StaleControllers[i];
            if (controller != null && Agents.TryGetValue(controller, out AgentData agent))
                RemoveAgentFromEncounter(agent);
            Agents.Remove(controller);
        }
    }

    private static void UpdateEncounter(EncounterData encounter, float deltaTime, float now)
    {
        if (!encounter.Active && CountCombatEligibleAgents(encounter) > 0)
        {
            encounter.Active = true;
            RefreshPursuitFrame(encounter, true, now);
            AssignAvailableSlots(encounter, now);
            encounter.LastRouteTargetPosition = encounter.Anchor.position;
            encounter.LastRouteTranslationPosition = encounter.Anchor.position;
            encounter.LastFullReformationPosition = encounter.Anchor.position;
            encounter.LastFullReformationTime = now;
            encounter.NextFullReformationTime = now + FullReformationInterval;
        }
        if (!encounter.Active)
            return;

        if (now >= encounter.NextRecruitTime)
        {
            RecruitUnassignedAgents(encounter);
            encounter.NextRecruitTime = now + .5f;
        }
        RefreshSquadCenters(encounter, deltaTime);
        UpdateSquadModes(encounter, deltaTime, now);
        TryRefreshFullReformation(encounter, now);
        RefreshPursuitFrame(encounter, false, now);
        AssignAvailableSlots(encounter, now);
    }

    private static void BindSpawnPack(EncounterData encounter, AgentData agent)
    {
        if (!encounter.SpawnPacks.TryGetValue(agent.SpawnPackId, out var squad))
        {
            squad = new SquadData { Id = agent.SpawnPackId, SpawnPackId = agent.SpawnPackId, RawCenter = agent.Controller.transform.position,
                SmoothedCenter = agent.Controller.transform.position,
                Mode = HorizontalDistance(agent.Controller.transform.position, encounter.Anchor.position) < encounter.Preset.FarActivationDistance
                    ? EnemySquadPursuitRuntimeMode.NearCombat : EnemySquadPursuitRuntimeMode.Reserve };
            encounter.SpawnPacks.Add(agent.SpawnPackId, squad);
            encounter.Squads.Add(squad);
        }
        AddMember(squad, agent, squad.SmoothedCenter);
        squad.InitialCount = Mathf.Max(squad.InitialCount, squad.AliveCount);
        squad.LastMemberJoined = Time.time;
        if (squad.Mode == EnemySquadPursuitRuntimeMode.Dead) squad.Mode = EnemySquadPursuitRuntimeMode.Reserve;
        // Binding changes happen on spawn/return, not by distance or a platform-wide threshold.
        var center = Vector3.zero;
        foreach (var member in squad.Members) center += member.Controller.transform.position;
        center /= squad.Members.Count;
        squad.RawCenter = squad.SmoothedCenter = center;
        foreach (var member in squad.Members) AddMember(squad, member, center, false);
    }

    // Only loose survivors/singletons are regrouped. Healthy spawn packs keep their identity.
    // One local group per encounter per half-second; no all-pairs scan or full repartition.
    private static void RecruitUnassignedAgents(EncounterData encounter)
    {
        var loose = encounter.Unassigned;
        loose.Clear();
        int count = encounter.Agents.Count;
        for (int i = 0; i < Mathf.Min(64, count); i++)
        {
            var agent = encounter.Agents[(encounter.RecruitCursor + i) % count];
            if (!IsAgentAlive(agent) || !agent.Controller.UsesMeleeSquadMovement) continue;
            var squad = agent.Squad;
            if (squad == null || squad.SpawnPackId == 0 && squad.AliveCount == 1)
                loose.Add(agent);
        }
        if (count > 0) encounter.RecruitCursor = (encounter.RecruitCursor + 1) % count;
        if (loose.Count == 0) return;
        var seed = loose[0];
        var center = seed.Controller.transform.position;
        var group = seed.Squad;
        int maximum = encounter.Preset.MaximumSquadSize;
        // A lone actor also has an active squad; it can meet another loose actor on a later pass.
        if (group == null)
        {
            group = new SquadData { Id = -encounter.NextSquadId++, RawCenter = center, SmoothedCenter = center,
                Mode = EnemySquadPursuitRuntimeMode.NearCombat, LastMemberJoined = Time.time };
            encounter.Squads.Add(group);
            AddMember(group, seed, center);
            group.InitialCount = 1;
        }
        loose.RemoveAt(0);
        while (group.Members.Count < maximum && loose.Count > 0)
        {
            int index = FindNearestAgentIndex(loose, center);
            var recruit = loose[index];
            if (HorizontalSqrDistance(center, recruit.Controller.transform.position) > 64f) break;
            loose.RemoveAt(index);
            DetachFromSquad(encounter, recruit);
            if (group.SpawnPackId > 0)
            {
                encounter.SpawnPacks.Remove(group.SpawnPackId);
                group.SpawnPackId = 0; group.Id = -encounter.NextSquadId++;
            }
            AddMember(group, recruit, center);
            group.InitialCount = group.Members.Count;
            group.LastMemberJoined = Time.time;
        }
    }

    private static void DetachFromSquad(EncounterData encounter, AgentData agent)
    {
        var squad = agent.Squad;
        if (squad == null) return;
        squad.Members.Remove(agent);
        agent.Squad = null; agent.LocalOffset = Vector3.zero;
        if (squad.Members.Count != 0) return;
        ReleaseSlot(squad);
        if (squad.SpawnPackId > 0) encounter.SpawnPacks.Remove(squad.SpawnPackId);
        encounter.Squads.Remove(squad);
    }

    internal static EnemyNavigationRoute NavigationFor(EnemyAIController controller)
    {
        return Agents.TryGetValue(controller, out var agent) && agent.Squad != null
            ? agent.Squad.Navigation ??= new EnemyNavigationRoute() : null;
    }

    private static void AddMember(SquadData squad, AgentData agent, Vector3 center, bool addToList = true)
    {
        if (addToList)
            squad.Members.Add(agent);
        agent.Squad = squad;
        agent.LocalOffset = agent.Controller.transform.position - center;
        agent.LocalOffset.y = 0f;
    }

    private static void RefreshSquadCenters(EncounterData encounter, float deltaTime)
    {
        float blend = 1f - Mathf.Exp(-CenterSmoothSpeed * deltaTime);
        for (int i = 0; i < encounter.Squads.Count; i++)
        {
            SquadData squad = encounter.Squads[i];
            if (squad.ParticipantCount <= 0)
                continue;
            squad.RawCenter = CalculateSquadCenter(squad);
            squad.SmoothedCenter = Vector3.Lerp(squad.SmoothedCenter, squad.RawCenter, blend);
        }
    }

    private static void UpdateSquadModes(EncounterData encounter, float deltaTime, float now)
    {
        Vector3 targetPosition = encounter.Anchor.position;
        for (int i = 0; i < encounter.Squads.Count; i++)
        {
            SquadData squad = encounter.Squads[i];
            int aliveCount = squad.AliveCount;
            if (aliveCount <= 0)
            {
                ReleaseSlot(squad);
                squad.Mode = EnemySquadPursuitRuntimeMode.Dead;
                continue;
            }

            if (!squad.SlotEligibilityRevoked
                && (aliveCount == 1 && squad.InitialCount > 1
                    || aliveCount / (float)Mathf.Max(1, squad.InitialCount) < encounter.Preset.RemnantRatio))
            {
                ReleaseSlot(squad);
                if (squad.SpawnPackId > 0) encounter.SpawnPacks.Remove(squad.SpawnPackId);
                encounter.Squads.RemoveAt(i--);
                foreach (var member in squad.Members)
                {
                    member.Squad = null; member.LocalOffset = Vector3.zero;
                    if (!IsAgentAlive(member)) continue;
                    var center = member.Controller.transform.position;
                    var loose = new SquadData { Id = -encounter.NextSquadId++, InitialCount = 1,
                        RawCenter = center, SmoothedCenter = center, Mode = EnemySquadPursuitRuntimeMode.NearCombat };
                    AddMember(loose, member, center); encounter.Squads.Add(loose);
                }
                squad.Members.Clear();
                encounter.NextRecruitTime = 0f;
                continue;
            }
            if (squad.SlotEligibilityRevoked)
            {
                squad.Mode = EnemySquadPursuitRuntimeMode.Remnant;
                continue;
            }
            if (squad.ParticipantCount <= 0)
            {
                ReleaseSlot(squad);
                squad.HasRushed = false;
                squad.FarEligibleTime = 0f;
                squad.Mode = EnemySquadPursuitRuntimeMode.Reserve;
                continue; // Return·Roam은 소속만 유지하고 부대 계산에서 제외
            }

            float centerDistance = HorizontalDistance(squad.SmoothedCenter, targetPosition);
            float nearRatio = ResolveNearMemberRatioToCombatTargets(
                squad,
                targetPosition,
                encounter.Preset.NearReleaseDistance);
            if (squad.HasRushed
                && AreAllParticipantsOutsideCombatTargets(
                    squad,
                    targetPosition,
                    encounter.Preset.FarActivationDistance))
            {
                squad.FarEligibleTime += deltaTime;
                if (squad.FarEligibleTime >= FarReentryDelay)
                {
                    EndRush(squad);
                    continue;
                }
            }
            else if (squad.HasRushed)
            {
                squad.FarEligibleTime = 0f;
            }

            if (squad.Mode == EnemySquadPursuitRuntimeMode.Rush)
            {
                if (nearRatio >= 0.999f)
                    squad.Mode = EnemySquadPursuitRuntimeMode.NearCombat;
                continue;
            }
            if (squad.Mode == EnemySquadPursuitRuntimeMode.Reserve
                && (centerDistance <= encounter.Preset.NearReleaseDistance || nearRatio >= 0.3f))
            {
                ReleaseSlot(squad);
                squad.Mode = EnemySquadPursuitRuntimeMode.NearCombat;
                squad.FarEligibleTime = 0f;
                continue;
            }
            if (squad.Mode == EnemySquadPursuitRuntimeMode.Pursuit
                && HasAnyParticipantWithinCombatTarget(
                    squad,
                    targetPosition,
                    encounter.Preset.NearReleaseDistance))
            {
                BeginRush(squad);
                continue;
            }
            if (squad.Mode == EnemySquadPursuitRuntimeMode.Pursuit
                && !squad.DirectCommitted
                && squad.SlotIndex >= 0
                && HasAnyParticipantWithinPosition(
                    squad,
                    targetPosition,
                    encounter.Preset.DirectCommitRadius))
            {
                squad.DirectCommitted = true;
                squad.RouteWaypointIndex = 0;
                squad.Route = default;
            }

            if (squad.Mode == EnemySquadPursuitRuntimeMode.NearCombat && !squad.HasRushed)
            {
                if (centerDistance >= encounter.Preset.FarActivationDistance && nearRatio <= 0.01f)
                {
                    squad.FarEligibleTime += deltaTime;
                    if (squad.FarEligibleTime >= FarReentryDelay)
                    {
                        squad.Mode = EnemySquadPursuitRuntimeMode.Reserve;
                        squad.FarEligibleTime = 0f;
                    }
                }
                else
                {
                    squad.FarEligibleTime = 0f;
                }
            }

            if (squad.Mode != EnemySquadPursuitRuntimeMode.Pursuit
                || squad.SlotIndex < 0
                || squad.SlotIndex >= encounter.Slots.Count)
            {
                continue;
            }
            if (squad.DirectCommitted)
            {
                if (HasAnyMemberReached(squad, encounter.Slots[squad.SlotIndex].Position, encounter.Preset.SlotArrivalDistance))
                    BeginRush(squad);
                continue;
            }
            if (squad.Route.WaypointCount <= 0)
                continue;
            int routeIndex = Mathf.Clamp(squad.RouteWaypointIndex, 0, squad.Route.WaypointCount - 1);
            if (!HasAnyMemberReached(squad, squad.Route.GetWaypoint(routeIndex), encounter.Preset.SlotArrivalDistance))
                continue;
            squad.RouteWaypointIndex++;
            if (squad.RouteWaypointIndex >= squad.Route.WaypointCount)
                BeginRush(squad);
        }
    }

    private static void TryRefreshFullReformation(EncounterData encounter, float now)
    {
        Vector3 anchorPosition = encounter.Anchor.position;
        if (!EnemySquadPursuitPlanner.ShouldRefreshFullReformation(
            HorizontalDistance(encounter.LastFullReformationPosition, anchorPosition),
            now,
            encounter.NextFullReformationTime,
            encounter.LastFullReformationTime))
        {
            return;
        }

        encounter.FrameInitialized = false;
        encounter.RouteRefreshPending = false;
        encounter.RouteRefreshCursor = 0;
        encounter.LastRouteTargetPosition = anchorPosition;
        encounter.LastRouteTranslationPosition = anchorPosition;
        encounter.LastFullReformationPosition = anchorPosition;
        encounter.LastFullReformationTime = now;
        encounter.NextFullReformationTime = now + FullReformationInterval;
    }

    private static void RefreshPursuitFrame(EncounterData encounter, bool resetRoutes, float now)
    {
        bool wasInitialized = encounter.FrameInitialized;
        bool hasOwners = CountOccupiedSlots(encounter) > 0;
        if (resetRoutes || !wasInitialized || !hasOwners)
        {
            SquadData nearest = FindNearestSlotEligibleSquad(encounter);
            if (nearest == null)
                return;
            encounter.DirectOutward = EnemySquadPursuitPlanner.ResolveDirectOutward(
                encounter.Anchor.position,
                nearest.SmoothedCenter);
        }

        encounter.FrameInitialized = true;
        TranslatePursuitRoutes(encounter);
        EnemySquadPursuitPlanner.BuildSlots(
            encounter.Anchor.position,
            encounter.DirectOutward,
            encounter.Preset.SlotRadius,
            encounter.Slots);

        float targetMovedDistance = !wasInitialized
            ? RouteRefreshMoveThreshold
            : Mathf.Sqrt(HorizontalSqrDistance(encounter.LastRouteTargetPosition, encounter.Anchor.position));
        bool routeShapeStale = HasRouteHeadingDrift(encounter);
        bool routeInvalid = HasInvalidTranslatedRoute(encounter);
        if (!encounter.RouteRefreshPending
            && (resetRoutes
                || !wasInitialized
                || routeInvalid
                || (routeShapeStale && now >= encounter.NextRouteRefreshTime)
                || EnemySquadPursuitPlanner.ShouldRefreshRoute(
                    targetMovedDistance,
                    now,
                    encounter.NextRouteRefreshTime,
                    encounter.RouteRefreshPending)))
        {
            encounter.RouteRefreshPending = true;
            encounter.RouteRefreshCursor = 0;
            encounter.NextRouteRefreshTime = now + RouteRefreshMinimumInterval;
        }

        ProcessPendingRouteRefresh(encounter);
    }

    private static void TranslatePursuitRoutes(EncounterData encounter)
    {
        Vector3 anchorPosition = encounter.Anchor.position;
        Vector3 delta = anchorPosition - encounter.LastRouteTranslationPosition;
        delta.y = 0f;
        encounter.LastRouteTranslationPosition = anchorPosition;
        if (delta.sqrMagnitude <= 0.000001f)
            return;

        for (int i = 0; i < encounter.Squads.Count; i++)
        {
            SquadData squad = encounter.Squads[i];
            if (squad.Mode != EnemySquadPursuitRuntimeMode.Pursuit
                || squad.DirectCommitted
                || squad.Route.WaypointCount <= 0)
            {
                continue;
            }
            squad.Route = EnemySquadPursuitPlanner.TranslateRoute(squad.Route, delta);
        }
    }

    private static bool HasRouteHeadingDrift(EncounterData encounter)
    {
        for (int i = 0; i < encounter.Squads.Count; i++)
        {
            SquadData squad = encounter.Squads[i];
            if (squad.Mode != EnemySquadPursuitRuntimeMode.Pursuit
                || squad.DirectCommitted
                || squad.SlotIndex < 0
                || squad.SlotIndex >= encounter.Slots.Count)
            {
                continue;
            }

            Vector3 desired = encounter.Slots[squad.SlotIndex].Position - squad.SmoothedCenter;
            desired.y = 0f;
            if (squad.RouteSlotDirection.sqrMagnitude > 0.0001f
                && desired.sqrMagnitude > 0.0001f
                && Vector3.Angle(squad.RouteSlotDirection, desired) >= RouteRefreshHeadingThreshold)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasInvalidTranslatedRoute(EncounterData encounter)
    {
        for (int i = 0; i < encounter.Squads.Count; i++)
        {
            SquadData squad = encounter.Squads[i];
            if (squad.Mode != EnemySquadPursuitRuntimeMode.Pursuit
                || squad.DirectCommitted
                || squad.Route.WaypointCount <= 0)
            {
                continue;
            }
            EnemyMovement movement = ResolveSquadMovement(squad);
            if (movement == null)
                continue;
            int start = Mathf.Clamp(squad.RouteWaypointIndex, 0, squad.Route.WaypointCount - 1);
            for (int waypointIndex = start; waypointIndex < squad.Route.WaypointCount; waypointIndex++)
            {
                Vector3 waypoint = squad.Route.GetWaypoint(waypointIndex);
                waypoint.y = squad.SmoothedCenter.y;
                if (!movement.IsWalkablePosition(waypoint))
                    return true;
            }
        }
        return false;
    }

    private static void ProcessPendingRouteRefresh(EncounterData encounter)
    {
        if (!encounter.RouteRefreshPending)
            return;

        int refreshed = 0;
        while (encounter.RouteRefreshCursor < encounter.Squads.Count
            && refreshed < MaximumRouteRefreshPerFrame)
        {
            SquadData squad = encounter.Squads[encounter.RouteRefreshCursor++];
            if (squad.SlotIndex < 0 || squad.DirectCommitted || squad.Mode != EnemySquadPursuitRuntimeMode.Pursuit)
                continue;
            if (!IsSlotWalkable(encounter, squad.SlotIndex, squad))
            {
                ReleaseSlot(squad);
                squad.Mode = EnemySquadPursuitRuntimeMode.Reserve;
                refreshed++;
                continue;
            }

            squad.Route = BuildWalkableRoute(encounter, squad, squad.SlotIndex);
            squad.RouteWaypointIndex = 0;
            squad.RouteSlotDirection = ResolveRouteSlotDirection(encounter, squad);
            refreshed++;
        }

        if (encounter.RouteRefreshCursor < encounter.Squads.Count)
            return;
        encounter.RouteRefreshPending = false;
        encounter.LastRouteTargetPosition = encounter.Anchor.position;
    }

    private static void AssignAvailableSlots(EncounterData encounter, float now)
    {
        if (!encounter.FrameInitialized || encounter.Slots.Count < 8)
            return;
        EnsureDirectSlotOwner(encounter, now);
        AssignVacantSlots(encounter, now);
        RefreshRearSlot(encounter, now);
        for (int i = 0; i < encounter.Squads.Count; i++)
        {
            SquadData squad = encounter.Squads[i];
            if (IsFarEligible(encounter, squad) && squad.SlotIndex < 0)
                squad.Mode = EnemySquadPursuitRuntimeMode.Reserve;
        }
    }

    private static void EnsureDirectSlotOwner(EncounterData encounter, float now)
    {
        int directIndex = FindSlotByKind(encounter, EnemySquadPursuitSlotKind.Direct);
        if (!IsSlotAssignable(encounter, directIndex, null) || FindSlotOwner(encounter, directIndex) != null)
            return;

        SquadData candidate = null;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < encounter.Squads.Count; i++)
        {
            SquadData squad = encounter.Squads[i];
            if (!IsReserveCandidate(encounter, squad))
                continue;
            float distance = HorizontalSqrDistance(squad.SmoothedCenter, encounter.Slots[directIndex].Position);
            if (distance >= bestDistance)
                continue;
            bestDistance = distance;
            candidate = squad;
        }
        if (candidate != null)
            AssignSlot(encounter, candidate, directIndex, now);
    }

    private static void AssignVacantSlots(EncounterData encounter, float now)
    {
        encounter.ReserveSquads.Clear();
        encounter.ReserveCenters.Clear();
        int farEligibleCount = 0;
        for (int i = 0; i < encounter.Squads.Count; i++)
        {
            SquadData squad = encounter.Squads[i];
            if (IsFarEligible(encounter, squad))
                farEligibleCount++;
            if (!IsReserveCandidate(encounter, squad))
                continue;
            encounter.ReserveSquads.Add(squad);
            encounter.ReserveCenters.Add(squad.SmoothedCenter);
        }
        if (encounter.ReserveSquads.Count <= 0)
            return;

        encounter.AssignableSlots.Clear();
        for (int slotIndex = 0; slotIndex < encounter.Slots.Count; slotIndex++)
        {
            if (IsSlotAssignable(encounter, slotIndex, null))
                encounter.AssignableSlots.Add(encounter.Slots[slotIndex]);
        }
        EnemySquadPursuitPlanner.BuildBalancedSlotIndices(
            encounter.AssignableSlots,
            farEligibleCount,
            encounter.BalancedSlots);

        encounter.VacantSlots.Clear();
        encounter.VacantPositions.Clear();
        for (int i = 0; i < encounter.BalancedSlots.Count; i++)
        {
            int slotIndex = encounter.BalancedSlots[i];
            if (FindSlotOwner(encounter, slotIndex) != null)
                continue;
            encounter.VacantSlots.Add(slotIndex);
            encounter.VacantPositions.Add(encounter.Slots[slotIndex].Position);
        }
        if (encounter.VacantSlots.Count <= 0)
            return;

        EnemySquadPursuitPlanner.BuildMinimumDistanceAssignment(
            encounter.ReserveCenters,
            encounter.VacantPositions,
            encounter.SquadIndexBySlot);
        for (int i = 0; i < encounter.VacantSlots.Count; i++)
        {
            int squadIndex = encounter.SquadIndexBySlot[i];
            if (squadIndex >= 0 && squadIndex < encounter.ReserveSquads.Count)
                AssignSlot(encounter, encounter.ReserveSquads[squadIndex], encounter.VacantSlots[i], now);
        }
    }

    private static void RefreshRearSlot(EncounterData encounter, float now)
    {
        int rearIndex = FindSlotByKind(encounter, EnemySquadPursuitSlotKind.Rear);
        if (!IsSlotWalkable(encounter, rearIndex, null))
            return;
        SquadData owner = FindSlotOwner(encounter, rearIndex);
        if (owner != null
            && !owner.DirectCommitted
            && now >= owner.SlotLeaseUntil
            && !EnemySquadPursuitPlanner.IsWithinSlotAngularSector(
                encounter.Anchor.position,
                owner.SmoothedCenter,
                encounter.Slots[rearIndex].OutwardDirection,
                EnemySquadPursuitPlanner.DefaultRearSlotReleaseAngle))
        {
            ReleaseSlot(owner);
            owner.Mode = EnemySquadPursuitRuntimeMode.Reserve;
            owner = null;
        }
        if (owner != null)
            return;

        SquadData candidate = null;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < encounter.Squads.Count; i++)
        {
            SquadData squad = encounter.Squads[i];
            if (!IsRearCandidate(encounter, squad, rearIndex))
                continue;
            float distance = HorizontalSqrDistance(squad.SmoothedCenter, encounter.Slots[rearIndex].Position);
            if (distance >= bestDistance)
                continue;
            bestDistance = distance;
            candidate = squad;
        }
        if (candidate != null)
            AssignSlot(encounter, candidate, rearIndex, now);
    }

    private static void AssignSlot(EncounterData encounter, SquadData squad, int slotIndex, float now)
    {
        if (squad == null
            || squad.SlotEligibilityRevoked
            || !IsSlotWalkable(encounter, slotIndex, squad))
        {
            return;
        }
        squad.SlotIndex = slotIndex;
        squad.Mode = EnemySquadPursuitRuntimeMode.Pursuit;
        squad.DirectCommitted = false;
        squad.SlotLeaseUntil = now + SlotLeaseDuration;
        squad.RouteWaypointIndex = 0;
        squad.Route = BuildWalkableRoute(encounter, squad, slotIndex);
        squad.RouteSlotDirection = ResolveRouteSlotDirection(encounter, squad);
    }

    private static Vector3 ResolveRouteSlotDirection(EncounterData encounter, SquadData squad)
    {
        if (squad == null || squad.SlotIndex < 0 || squad.SlotIndex >= encounter.Slots.Count)
            return Vector3.zero;
        Vector3 direction = encounter.Slots[squad.SlotIndex].Position - squad.SmoothedCenter;
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
    }

    private static EnemySquadPursuitRoute BuildWalkableRoute(
        EncounterData encounter,
        SquadData squad,
        int slotIndex)
    {
        EnemySquadPursuitRoute route = EnemySquadPursuitPlanner.BuildRoute(
            squad.SmoothedCenter,
            encounter.Anchor.position,
            encounter.DirectOutward,
            encounter.Slots[slotIndex],
            encounter.Slots,
            encounter.Preset.CurveRadiusMultiplier);
        EnemyMovement movement = ResolveSquadMovement(squad);
        if (movement == null)
            return route;
        for (int i = 0; i < route.WaypointCount; i++)
        {
            Vector3 waypoint = route.GetWaypoint(i);
            waypoint.y = squad.SmoothedCenter.y;
            if (!movement.IsWalkablePosition(waypoint))
                return new EnemySquadPursuitRoute(1, encounter.Slots[slotIndex].Position, Vector3.zero, Vector3.zero);
        }
        return route;
    }

    private static void ReleaseSlot(SquadData squad)
    {
        squad.SlotIndex = -1;
        squad.DirectCommitted = false;
        squad.SlotLeaseUntil = 0f;
        squad.RouteWaypointIndex = 0;
        squad.Route = default;
        squad.RouteSlotDirection = Vector3.zero;
    }

    private static void BeginRush(SquadData squad)
    {
        ReleaseSlot(squad);
        squad.HasRushed = true;
        squad.Mode = EnemySquadPursuitRuntimeMode.Rush;
        squad.FarEligibleTime = 0f;
    }

    private static void EndRush(SquadData squad)
    {
        ReleaseSlot(squad);
        squad.HasRushed = false;
        squad.Mode = EnemySquadPursuitRuntimeMode.Reserve;
        squad.FarEligibleTime = 0f;
    }

    private static Vector3 ResolveSquadDestination(EncounterData encounter, SquadData squad)
    {
        if (squad.Mode == EnemySquadPursuitRuntimeMode.Pursuit
            && squad.DirectCommitted
            && squad.SlotIndex >= 0)
        {
            return encounter.Slots[squad.SlotIndex].Position;
        }
        if (squad.Mode == EnemySquadPursuitRuntimeMode.Pursuit && squad.Route.WaypointCount > 0)
        {
            int index = Mathf.Clamp(squad.RouteWaypointIndex, 0, squad.Route.WaypointCount - 1);
            return squad.Route.GetWaypoint(index);
        }
        if (squad.Mode == EnemySquadPursuitRuntimeMode.Reserve)
        {
            Vector3 direction = EnemySquadPursuitPlanner.ResolveReserveOrbitDirection(
                StableSeed,
                squad.Id,
                Time.time * encounter.Preset.ReserveOrbitAngularSpeed);
            float radius = EnemySquadPursuitPlanner.ResolveReserveOrbitRadius(
                encounter.Preset.ReserveOrbitRadius,
                squad.Id);
            return EnemySquadPursuitPlanner.ResolveReserveOrbitDestination(
                encounter.Anchor.position,
                squad.SmoothedCenter,
                direction,
                radius,
                squad.Id);
        }
        return encounter.Anchor.position;
    }

    private static bool IsFarEligible(EncounterData encounter, SquadData squad)
    {
        if (squad == null
            || squad.ParticipantCount <= 0
            || squad.SlotEligibilityRevoked
            || squad.HasRushed
            || squad.Mode == EnemySquadPursuitRuntimeMode.Remnant
            || squad.Mode == EnemySquadPursuitRuntimeMode.Dead
            || squad.Mode == EnemySquadPursuitRuntimeMode.Rush
            || squad.Mode == EnemySquadPursuitRuntimeMode.NearCombat)
        {
            return false;
        }
        float distance = HorizontalDistance(squad.SmoothedCenter, encounter.Anchor.position);
        return squad.Mode == EnemySquadPursuitRuntimeMode.Pursuit
            ? distance > encounter.Preset.NearReleaseDistance
            : distance >= encounter.Preset.FarActivationDistance;
    }

    private static bool IsReserveCandidate(EncounterData encounter, SquadData squad)
    {
        return IsFarEligible(encounter, squad) && squad.SlotIndex < 0;
    }

    private static bool IsRearCandidate(EncounterData encounter, SquadData squad, int slotIndex)
    {
        return squad != null
            && squad.Mode == EnemySquadPursuitRuntimeMode.Reserve
            && squad.SlotIndex < 0
            && !squad.HasRushed
            && !squad.SlotEligibilityRevoked
            && IsSlotWalkable(encounter, slotIndex, squad)
            && EnemySquadPursuitPlanner.IsWithinSlotAngularSector(
                encounter.Anchor.position,
                squad.SmoothedCenter,
                encounter.Slots[slotIndex].OutwardDirection,
                EnemySquadPursuitPlanner.DefaultRearSlotClaimAngle);
    }

    private static bool IsSlotAssignable(EncounterData encounter, int slotIndex, SquadData squad)
    {
        return IsSlotWalkable(encounter, slotIndex, squad)
            && encounter.Slots[slotIndex].Kind != EnemySquadPursuitSlotKind.Rear;
    }

    private static bool IsSlotWalkable(EncounterData encounter, int slotIndex, SquadData squad)
    {
        if (slotIndex < 0 || slotIndex >= encounter.Slots.Count)
            return false;
        EnemyMovement movement = squad != null ? ResolveSquadMovement(squad) : ResolveAnyMovement(encounter);
        if (movement == null)
            return true;
        Vector3 position = encounter.Slots[slotIndex].Position;
        position.y = movement.transform.position.y;
        return movement.IsWalkablePosition(position);
    }

    private static EnemyMovement ResolveSquadMovement(SquadData squad)
    {
        for (int i = 0; i < squad.Members.Count; i++)
        {
            if (IsAgentParticipating(squad.Members[i]))
                return squad.Members[i].Controller.Movement;
        }
        return null;
    }

    private static EnemyMovement ResolveAnyMovement(EncounterData encounter)
    {
        for (int i = 0; i < encounter.Agents.Count; i++)
        {
            if (IsAgentParticipating(encounter.Agents[i]))
                return encounter.Agents[i].Controller.Movement;
        }
        return null;
    }

    private static SquadData FindNearestSlotEligibleSquad(EncounterData encounter)
    {
        SquadData result = null;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < encounter.Squads.Count; i++)
        {
            SquadData squad = encounter.Squads[i];
            if (!IsFarEligible(encounter, squad))
                continue;
            float distance = HorizontalSqrDistance(squad.SmoothedCenter, encounter.Anchor.position);
            if (distance >= bestDistance)
                continue;
            bestDistance = distance;
            result = squad;
        }
        return result;
    }

    private static SquadData FindSlotOwner(EncounterData encounter, int slotIndex)
    {
        for (int i = 0; i < encounter.Squads.Count; i++)
        {
            SquadData squad = encounter.Squads[i];
            if (squad.ParticipantCount > 0
                && squad.Mode == EnemySquadPursuitRuntimeMode.Pursuit
                && squad.SlotIndex == slotIndex)
            {
                return squad;
            }
        }
        return null;
    }

    private static int FindSlotByKind(EncounterData encounter, EnemySquadPursuitSlotKind kind)
    {
        for (int i = 0; i < encounter.Slots.Count; i++)
        {
            if (encounter.Slots[i].Kind == kind)
                return i;
        }
        return -1;
    }

    private static int CountOccupiedSlots(EncounterData encounter)
    {
        int count = 0;
        for (int i = 0; i < encounter.Slots.Count; i++)
        {
            if (FindSlotOwner(encounter, i) != null)
                count++;
        }
        return count;
    }

    private static int CountCombatEligibleAgents(EncounterData encounter)
    {
        int count = 0;
        for (int i = 0; i < encounter.Agents.Count; i++)
        {
            if (IsAgentParticipating(encounter.Agents[i]))
                count++;
        }
        return count;
    }

    private static void CollectUnassignedCombatAgents(EncounterData encounter)
    {
        encounter.Unassigned.Clear();
        for (int i = 0; i < encounter.Agents.Count; i++)
        {
            AgentData agent = encounter.Agents[i];
            if (agent.Squad == null && IsAgentParticipating(agent) && agent.Controller.UsesMeleeSquadMovement)
                encounter.Unassigned.Add(agent);
        }
    }

    private static int FindNearestAgentIndex(List<AgentData> agents, Vector3 position)
    {
        int result = 0;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < agents.Count; i++)
        {
            float distance = HorizontalSqrDistance(agents[i].Controller.transform.position, position);
            if (distance >= bestDistance)
                continue;
            bestDistance = distance;
            result = i;
        }
        return result;
    }

    private static Vector3 CalculateSquadCenter(SquadData squad)
    {
        Vector3 sum = Vector3.zero;
        int count = 0;
        for (int i = 0; i < squad.Members.Count; i++)
        {
            if (!IsAgentParticipating(squad.Members[i]))
                continue;
            sum += squad.Members[i].Controller.transform.position;
            count++;
        }
        return count > 0 ? sum / count : squad.SmoothedCenter;
    }

    private static float ResolveNearMemberRatioToCombatTargets(
        SquadData squad,
        Vector3 fallbackTargetPosition,
        float distance)
    {
        int participants = squad.ParticipantCount;
        if (participants <= 0)
            return 0f;
        int near = 0;
        float distanceSqr = distance * distance;
        for (int i = 0; i < squad.Members.Count; i++)
        {
            AgentData member = squad.Members[i];
            if (IsAgentParticipating(member)
                && HorizontalSqrDistance(
                    member.Controller.transform.position,
                    ResolveCombatTargetPosition(member, fallbackTargetPosition)) <= distanceSqr)
            {
                near++;
            }
        }
        return near / (float)participants;
    }

    private static bool HasAnyParticipantWithinCombatTarget(
        SquadData squad,
        Vector3 fallbackTargetPosition,
        float distance)
    {
        float distanceSqr = distance * distance;
        for (int i = 0; i < squad.Members.Count; i++)
        {
            AgentData member = squad.Members[i];
            if (IsAgentParticipating(member)
                && HorizontalSqrDistance(
                    member.Controller.transform.position,
                    ResolveCombatTargetPosition(member, fallbackTargetPosition)) <= distanceSqr)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasAnyParticipantWithinPosition(
        SquadData squad,
        Vector3 targetPosition,
        float distance)
    {
        float distanceSqr = distance * distance;
        for (int i = 0; i < squad.Members.Count; i++)
        {
            AgentData member = squad.Members[i];
            if (IsAgentParticipating(member)
                && HorizontalSqrDistance(member.Controller.transform.position, targetPosition) <= distanceSqr)
            {
                return true;
            }
        }
        return false;
    }

    private static bool AreAllParticipantsOutsideCombatTargets(
        SquadData squad,
        Vector3 fallbackTargetPosition,
        float distance)
    {
        bool hasParticipant = false;
        float distanceSqr = distance * distance;
        for (int i = 0; i < squad.Members.Count; i++)
        {
            AgentData member = squad.Members[i];
            if (!IsAgentParticipating(member))
                continue;
            hasParticipant = true;
            if (HorizontalSqrDistance(
                member.Controller.transform.position,
                ResolveCombatTargetPosition(member, fallbackTargetPosition)) <= distanceSqr)
            {
                return false;
            }
        }
        return hasParticipant;
    }

    private static bool HasAnyMemberReached(SquadData squad, Vector3 destination, float arrivalDistance)
    {
        float thresholdSqr = arrivalDistance * arrivalDistance;
        for (int i = 0; i < squad.Members.Count; i++)
        {
            AgentData member = squad.Members[i];
            if (!IsAgentParticipating(member))
                continue;
            Vector3 personal = destination + member.LocalOffset * FormationOffsetScale;
            if (HorizontalSqrDistance(member.Controller.transform.position, personal) <= thresholdSqr)
                return true; // 부대장 대신 선두 1명 도착으로 전원 돌진
        }
        return false;
    }

    private static bool IsCombatEngaged(EnemyAIController controller)
    {
        return controller != null && controller.IsAggroActive;
    }

    private static bool IsAgentParticipating(AgentData agent)
    {
        return IsAgentAlive(agent) && IsCombatEngaged(agent.Controller);
    }

    private static bool IsSquadMoveEligible(AgentData agent)
    {
        return IsAgentAlive(agent) && agent.Controller.UsesMeleeSquadMovement && agent.Controller.CurrentStateName == "Chase";
    }

    private static Vector3 ResolveCombatTargetPosition(AgentData agent, Vector3 fallbackPosition)
    {
        if (agent == null || agent.Controller == null || !agent.Controller.IsTargetValid())
            return fallbackPosition;

        return agent.Controller.Target.position;
    }

    private static bool IsAgentAlive(AgentData agent)
    {
        return agent != null
            && agent.Controller != null
            && agent.Controller.isActiveAndEnabled
            && agent.Controller.CurrentStateName != "Dead";
    }

    private static void RemoveAgentFromEncounter(AgentData agent)
    {
        if (agent == null)
            return;
        if (agent.Encounter != null) DetachFromSquad(agent.Encounter, agent);
        if (agent.Encounter != null)
            agent.Encounter.Agents.Remove(agent);
        agent.Squad = null;
        agent.Encounter = null;
        agent.SpawnPackId = 0;
        agent.LocalOffset = Vector3.zero;
    }

    private static float HorizontalDistance(Vector3 from, Vector3 to)
    {
        return Mathf.Sqrt(HorizontalSqrDistance(from, to));
    }

    private static float HorizontalSqrDistance(Vector3 from, Vector3 to)
    {
        float x = from.x - to.x;
        float z = from.z - to.z;
        return x * x + z * z;
    }
}
