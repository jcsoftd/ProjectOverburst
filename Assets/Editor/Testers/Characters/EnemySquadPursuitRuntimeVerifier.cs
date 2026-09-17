using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class EnemySquadPursuitRuntimeVerifier // 실제 게임 연결 최소 계약 검증
{
    private const string ActiveKey = "EnemySquadPursuitRuntimeVerifier.Active";
    private const string BatchKey = "EnemySquadPursuitRuntimeVerifier.Batch";
    private const string ExitCodeKey = "EnemySquadPursuitRuntimeVerifier.ExitCode";
    private const int SquadMemberCount = 41;
    private const int ExpectedSquadCount = 5;
    private const int ExpectedPlannedCount = 40;
    private const int ExpectedUnassignedCount = 1;
    private const int ExpectedSquadSize = 8;
    private const string MurlocPrefabPath =
        "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Grunt.prefab";

    static EnemySquadPursuitRuntimeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    [MenuItem("OVERBURST/Codex/Verify/Enemy Squad Pursuit Runtime")]
    public static void RunFromMenu()
    {
        Begin(false);
    }

    public static void Verify()
    {
        GameObject player = null;
        PlayerContext context = PlayerContext.GetOrCreate();
        if (!Application.isPlaying || context == null)
            throw new InvalidOperationException("Run this verifier through its PlayMode entry point");
        PlayerActorRuntime previousActor = context.CurrentActor;
        GameObject encounterOwner = null;
        GameObject groupEncounterOwner = null;
        readonlyCleanup.Clear();
        try
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MurlocPrefabPath);
            if (prefab == null)
                throw new InvalidOperationException("Murloc runtime verification prefab missing");
            Vector3 testCenter = new Vector3(2000f, 0f, 2000f);
            player = CreatePlayerActor("EnemySquadRuntimeVerifier_Player", 0, testCenter);
            context.Bind(player.GetComponent<PlayerActorRuntime>());
            Physics.SyncTransforms();
            encounterOwner = new GameObject("EnemySquadRuntimeVerifier_Encounter");
            MethodInfo changeToChase = typeof(EnemyAIController).GetMethod(
                "ChangeToChase",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo changeToReturn = typeof(EnemyAIController).GetMethod(
                "ChangeToReturn",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo awake = typeof(EnemyAIController).GetMethod(
                "Awake",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo onEnable = typeof(EnemyAIController).GetMethod(
                "OnEnable",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo tryMaintainGroupAggro = typeof(EnemyAIController).GetMethod(
                "TryMaintainGroupAggro",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo resolveChaseDestination = typeof(EnemyAIController).GetMethod(
                "ResolveChaseDestination",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (changeToChase == null
                || changeToReturn == null
                || awake == null
                || onEnable == null
                || tryMaintainGroupAggro == null
                || resolveChaseDestination == null)
            {
                throw new InvalidOperationException("Enemy runtime lifecycle hook missing");
            }

            for (int i = 0; i < SquadMemberCount; i++)
            {
                float angle = i * (360f / SquadMemberCount) * Mathf.Deg2Rad;
                Vector3 position = testCenter
                    + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 15f;
                EnemyAIController ai = InstantiateVerifierEnemy(prefab, position, awake, onEnable);
                if (!ai.UsesMurlocSquadPursuit)
                    throw new InvalidOperationException("Core Murloc squad runtime opt-in missing");
                ai.Configure(player.transform, position, 30f, 1.5f);
                ai.SetSquadEncounter(encounterOwner, player.transform);
                ai.RequestAggro(player.transform);
                changeToChase.Invoke(ai, null);
                RequirePartyTarget(
                    ai,
                    player.transform,
                    EnemyPartyTargetPhase.LeaderApproach,
                    "Far squad member did not bind P1 index=" + i);
            }

            FieldInfo updatedFrame = typeof(EnemySquadPursuitRuntimeService).GetField(
                "updatedFrame",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (updatedFrame == null)
                throw new InvalidOperationException("Runtime squad frame cache hook missing");
            updatedFrame.SetValue(null, -1); // 같은 Editor 프레임에서 41명 전환 후 재평가

            int planned = 0;
            int unassigned = 0;
            int pursuitPriority = 0;
            EnemyAIController unassignedMember = null;
            var plannedMembers = new List<EnemyAIController>(ExpectedPlannedCount);
            var squadMemberCounts = new Dictionary<string, int>();
            for (int i = 0; i < SquadMemberCount; i++)
            {
                EnemyAIController ai = readonlyCleanup[i].GetComponent<EnemyAIController>();
                if (EnemySquadPursuitRuntimeService.TryResolveMovePlan(ai, out EnemySquadPursuitMovePlan plan))
                {
                    planned++;
                    plannedMembers.Add(ai);
                    if (plan.Mode == EnemySquadPursuitRuntimeMode.Pursuit && ai.CrowdMovePriority == 1)
                        pursuitPriority++;
                    string squadIdentity = ResolveSquadIdentity(ai.SquadPursuitDebugModeName);
                    squadMemberCounts.TryGetValue(squadIdentity, out int memberCount);
                    squadMemberCounts[squadIdentity] = memberCount + 1;
                }
                else
                {
                    unassigned++;
                    unassignedMember = ai;
                    if (!string.IsNullOrEmpty(ai.SquadPursuitDebugModeName))
                    {
                        throw new InvalidOperationException(
                            "Unassigned runtime member exposed a squad debug state index=" + i
                            + " state=" + ai.SquadPursuitDebugModeName);
                    }
                }
            }

            if (planned != ExpectedPlannedCount
                || unassigned != ExpectedUnassignedCount
                || plannedMembers.Count != ExpectedPlannedCount
                || unassignedMember == null
                || squadMemberCounts.Count != ExpectedSquadCount
                || pursuitPriority <= 0)
            {
                throw new InvalidOperationException(
                    "41 Murloc runtime squad pursuit plan mismatch planned=" + planned
                    + " unassigned=" + unassigned
                    + " squads=" + squadMemberCounts.Count
                    + " pursuitPriority=" + pursuitPriority);
            }
            foreach (KeyValuePair<string, int> pair in squadMemberCounts)
            {
                if (pair.Value != ExpectedSquadSize)
                {
                    throw new InvalidOperationException(
                        "Maximum-first runtime squad size mismatch squad=" + pair.Key
                        + " size=" + pair.Value + " expected=" + ExpectedSquadSize);
                }
            }

            Vector3 fallbackDestination = (Vector3)resolveChaseDestination.Invoke(unassignedMember, null);
            if (!float.IsFinite(fallbackDestination.x)
                || !float.IsFinite(fallbackDestination.y)
                || !float.IsFinite(fallbackDestination.z)
                || (fallbackDestination - unassignedMember.transform.position).sqrMagnitude <= 0.000001f)
            {
                throw new InvalidOperationException(
                    "Unassigned member did not preserve the individual chase fallback");
            }

            EnemySquadPursuitRuntimeStats stats = EnemySquadPursuitRuntimeService.GetRuntimeStats();
            if (!stats.IsActive
                || stats.ActiveEncounterCount != 1
                || stats.CombatEligibleAgentCount != SquadMemberCount
                || stats.SquadCount != ExpectedSquadCount
                || stats.PursuitCount + stats.ReserveCount + stats.RushCount
                    + stats.NearCombatCount + stats.RemnantCount != stats.SquadCount)
            {
                throw new InvalidOperationException(
                    "Runtime squad HUD stats mismatch encounters=" + stats.ActiveEncounterCount
                    + " eligible=" + stats.CombatEligibleAgentCount
                    + " squads=" + stats.SquadCount);
            }

            var debugEncounters = new List<EnemySquadPursuitDebugEncounter>();
            var debugSlots = new List<EnemySquadPursuitDebugSlot>();
            EnemySquadPursuitRuntimeService.CollectDebugDrawData(debugEncounters, debugSlots);
            if (debugEncounters.Count != 1 || debugSlots.Count != 8)
                throw new InvalidOperationException("Runtime squad geometry debug data mismatch");

            EnemyAIController persistentMember = plannedMembers[0];
            string squadBeforeTargetChange = ResolveSquadIdentity(persistentMember.SquadPursuitDebugModeName);
            player.transform.position = persistentMember.transform.position + Vector3.right * 0.25f;
            Physics.SyncTransforms();
            persistentMember.GetComponent<CombatHealth>().TakeDamage(new DamageInfo(
                1f,
                persistentMember.transform.position,
                player,
                Vector3.forward,
                0f,
                false,
                false,
                hitReaction: new HitReactionData(true, 0f, 0f)));
            RequirePartyTarget(
                persistentMember,
                player.transform,
                EnemyPartyTargetPhase.MemberEngaged,
                "Nearby player did not become the selected member lock");
            for (int i = 1; i < SquadMemberCount; i++)
            {
                RequirePartyTarget(
                    readonlyCleanup[i].GetComponent<EnemyAIController>(),
                    player.transform,
                    EnemyPartyTargetPhase.LeaderApproach,
                    "A remote squad member copied the player engagement index=" + i);
            }
            updatedFrame.SetValue(null, -1);
            if (!EnemySquadPursuitRuntimeService.TryResolveMovePlan(persistentMember, out _)
                || ResolveSquadIdentity(persistentMember.SquadPursuitDebugModeName) != squadBeforeTargetChange)
            {
                throw new InvalidOperationException("Squad membership changed with individual aggro target");
            }

            if (!EnemyCombatCoordinator.TryAcquireAttackTurn(persistentMember, player.transform))
                throw new InvalidOperationException("MemberEngaged attack reservation setup failed");
            EnemyMeleeAttackController persistentAttack = persistentMember.MeleeAttack;
            if (persistentAttack == null || !persistentAttack.TryStartAttack(player.transform))
                throw new InvalidOperationException("MemberEngaged active attack setup failed");
            FieldInfo attackReservationsField = typeof(EnemyCombatCoordinator).GetField(
                "AttackReservations",
                BindingFlags.Static | BindingFlags.NonPublic);
            var attackReservations = attackReservationsField?.GetValue(null)
                as System.Collections.IDictionary;
            if (attackReservations == null || !attackReservations.Contains(persistentMember))
                throw new InvalidOperationException("MemberEngaged attack reservation verification hook missing");
            object reservationBeforeLeaderChange = attackReservations[persistentMember];

            EnemyAIController farDamagedMember = plannedMembers[plannedMembers.Count / 2];
            CombatHealth farDamagedHealth = farDamagedMember.GetComponent<CombatHealth>();
            farDamagedHealth.TakeDamage(new DamageInfo(
                1f,
                farDamagedMember.transform.position,
                player,
                Vector3.forward,
                0f,
                false,
                false,
                hitReaction: new HitReactionData(true, 0f, 0f)));
            RequirePartyTarget(
                farDamagedMember,
                player.transform,
                EnemyPartyTargetPhase.LeaderApproach,
                "Far hit changed the player approach phase");

            context.Bind(player.GetComponent<PlayerActorRuntime>());
            if (!persistentAttack.IsAttacking
                || !attackReservations.Contains(persistentMember)
                || !ReferenceEquals(attackReservations[persistentMember], reservationBeforeLeaderChange))
            {
                throw new InvalidOperationException("Binding the same player interrupted its attack reservation");
            }
            RequirePartyTarget(persistentMember, player.transform,
                EnemyPartyTargetPhase.MemberEngaged, "Same-player binding reset the engagement lock");

            changeToReturn.Invoke(persistentMember, null);
            updatedFrame.SetValue(null, -1);
            if (EnemySquadPursuitRuntimeService.TryResolveMovePlan(persistentMember, out _)
                || persistentMember.CrowdMovePriority != 0
                || persistentMember.SquadPursuitDebugModeName != squadBeforeTargetChange + "/Inactive")
            {
                throw new InvalidOperationException("Return member was not retained as inactive squad member");
            }
            if (EnemyCombatCoordinator.GetPartyTargetPhase(persistentMember) != EnemyPartyTargetPhase.None
                || EnemyCombatCoordinator.GetPartyTargetMemberIndex(persistentMember) != -1
                || attackReservations.Contains(persistentMember)
                || persistentAttack.IsAttacking)
            {
                throw new InvalidOperationException(
                    "Return did not clear TargetPhase, member lock, attack reservation and active attack");
            }

            groupEncounterOwner = new GameObject("EnemySquadRuntimeVerifier_GroupSignalEncounter");
            EnemyAIController engagedGroupMember = InstantiateVerifierEnemy(
                prefab,
                player.transform.position + Vector3.left * 0.25f,
                awake,
                onEnable);
            engagedGroupMember.Configure(
                player.transform,
                engagedGroupMember.transform.position,
                30f,
                1.5f);
            engagedGroupMember.SetSquadEncounter(groupEncounterOwner, player.transform);
            engagedGroupMember.RequestAggro(player.transform);
            changeToChase.Invoke(engagedGroupMember, null);
            engagedGroupMember.GetComponent<CombatHealth>().TakeDamage(new DamageInfo(
                1f,
                engagedGroupMember.transform.position,
                player,
                Vector3.forward,
                0f,
                false,
                false,
                hitReaction: new HitReactionData(true, 0f, 0f)));
            RequirePartyTarget(
                engagedGroupMember,
                player.transform,
                EnemyPartyTargetPhase.MemberEngaged,
                "Group signal source did not lock nearby player");

            EnemyAIController joiningGroupMember = InstantiateVerifierEnemy(
                prefab,
                player.transform.position + Vector3.right * 10f,
                awake,
                onEnable);
            joiningGroupMember.Configure(
                player.transform,
                joiningGroupMember.transform.position,
                30f,
                1.5f);
            joiningGroupMember.SetSquadEncounter(groupEncounterOwner, player.transform);
            joiningGroupMember.RequestAggro(player.transform);
            changeToChase.Invoke(joiningGroupMember, null);
            RequirePartyTarget(
                joiningGroupMember,
                player.transform,
                EnemyPartyTargetPhase.LeaderApproach,
                "Group joiner did not start from P1");

            updatedFrame.SetValue(null, -1);
            if (!EnemySquadPursuitRuntimeService.HasNearbyEngagedGroupMember(
                    joiningGroupMember,
                    joiningGroupMember.CombatLoseTargetRange))
            {
                throw new InvalidOperationException("Nearby engaged group signal was not detected");
            }
            if (!(bool)tryMaintainGroupAggro.Invoke(joiningGroupMember, null))
                throw new InvalidOperationException("Nearby engaged group signal did not sustain combat");
            RequirePartyTarget(
                joiningGroupMember,
                player.transform,
                EnemyPartyTargetPhase.LeaderApproach,
                "Group sustain copied another enemy's engagement phase");

            joiningGroupMember.transform.position = engagedGroupMember.transform.position
                + Vector3.right * (joiningGroupMember.CombatLoseTargetRange + 1f);
            Physics.SyncTransforms();
            updatedFrame.SetValue(null, -1);
            if (EnemySquadPursuitRuntimeService.HasNearbyEngagedGroupMember(
                    joiningGroupMember,
                    joiningGroupMember.CombatLoseTargetRange))
            {
                throw new InvalidOperationException("Far engaged group member incorrectly sustained requester aggro");
            }

            Debug.Log(
                "[EnemySquadPursuitRuntimeVerifier] PASS members=41 leaderApproach=41 memberLock=1 planned="
                + planned + " unassigned=" + unassigned + " squadSize=" + ExpectedSquadSize
                + " individualFallback=1 pursuitPriority=" + pursuitPriority
                + " squads=" + stats.SquadCount + " debugSlots=" + debugSlots.Count
                + " persistentMembership=1 singlePlayerContext=1 attackReservationPreserved=1"
                + " returnCleared=1 groupTargetCopy=0 groupFar=0");
        }
        finally
        {
            for (int i = 0; i < readonlyCleanup.Count; i++)
            {
                if (readonlyCleanup[i] != null)
                    UnityEngine.Object.DestroyImmediate(readonlyCleanup[i]);
            }
            readonlyCleanup.Clear();
            if (groupEncounterOwner != null)
                UnityEngine.Object.DestroyImmediate(groupEncounterOwner);
            if (encounterOwner != null)
                UnityEngine.Object.DestroyImmediate(encounterOwner);
            context.Bind(previousActor);
            if (player != null)
                UnityEngine.Object.DestroyImmediate(player);
        }
    }

    public static void RunFromCommandLine()
    {
        Begin(true);
    }

    private static void Begin(bool batchMode)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("PlayMode is already active or changing.");

        SessionState.SetBool(ActiveKey, true);
        SessionState.SetBool(BatchKey, batchMode);
        SessionState.SetInt(ExitCodeKey, 1);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(ActiveKey, false))
            return;

        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            try
            {
                Verify();
                SessionState.SetInt(ExitCodeKey, 0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SessionState.SetInt(ExitCodeKey, 1);
            }
            finally
            {
                EditorApplication.ExitPlaymode();
            }
            return;
        }

        if (state != PlayModeStateChange.EnteredEditMode)
            return;

        int exitCode = SessionState.GetInt(ExitCodeKey, 1);
        bool batchMode = SessionState.GetBool(BatchKey, false);
        SessionState.EraseBool(ActiveKey);
        SessionState.EraseBool(BatchKey);
        SessionState.EraseInt(ExitCodeKey);
        if (batchMode)
            EditorApplication.Exit(exitCode);
        else if (exitCode == 0)
            Debug.Log("[EnemySquadPursuitRuntimeVerifier] Verification completed successfully.");
        else
            Debug.LogError("[EnemySquadPursuitRuntimeVerifier] Verification failed.");
    }

    private static string ResolveSquadIdentity(string debugName)
    {
        int separator = !string.IsNullOrEmpty(debugName) ? debugName.IndexOf('/') : -1;
        if (separator <= 0)
            throw new InvalidOperationException("Runtime squad identity missing: " + debugName);
        return debugName.Substring(0, separator);
    }

    private static GameObject CreatePlayerActor(string objectName, int memberIndex, Vector3 position)
    {
        GameObject actor = new GameObject(objectName);
        actor.transform.position = position;
        CapsuleCollider collider = actor.AddComponent<CapsuleCollider>();
        collider.radius = 0.4f;
        collider.height = 2f;
        collider.center = new Vector3(0f, 1f, 0f);
        actor.AddComponent<CombatHealth>();
        PlayerActorRuntime runtime = actor.AddComponent<PlayerActorRuntime>();
        runtime.Initialize(memberIndex, objectName, "P" + (memberIndex + 1));
        CombatTarget.EnsureConfigured(actor, CombatTeam.PlayerParty);
        return actor;
    }



    private static EnemyAIController InstantiateVerifierEnemy(
        GameObject prefab,
        Vector3 position,
        MethodInfo awake,
        MethodInfo onEnable)
    {
        GameObject instance = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
        instance.hideFlags = HideFlags.HideAndDontSave;
        readonlyCleanup.Add(instance);
        EnemyAIController ai = instance.GetComponent<EnemyAIController>();
        if (ai == null)
            throw new InvalidOperationException("Core Murloc AI missing");
        if (!Application.isPlaying)
        {
            awake.Invoke(ai, null);
            onEnable.Invoke(ai, null);
        }
        return ai;
    }

    private static void RequirePartyTarget(
        EnemyAIController ai,
        Transform expectedTarget,
        EnemyPartyTargetPhase expectedPhase,
        string message)
    {
        EnemyPartyTargetPhase actualPhase = EnemyCombatCoordinator.GetPartyTargetPhase(ai);
        if (ai == null || ai.Target != expectedTarget || actualPhase != expectedPhase)
        {
            throw new InvalidOperationException(
                message
                + ". target=" + (ai != null && ai.Target != null ? ai.Target.name : "null")
                + " phase=" + actualPhase);
        }
    }

    private static readonly List<GameObject> readonlyCleanup = new List<GameObject>(43);
}
