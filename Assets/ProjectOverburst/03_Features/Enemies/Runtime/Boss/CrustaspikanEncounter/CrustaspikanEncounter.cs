using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// 무대/풀 수명을 소유한다. 카메라 조작과 보스 표시는 기존 게임 카메라와 저작된 HUD를 사용한다.
[DefaultExecutionOrder(10000)]
public sealed class CrustaspikanEncounter : MonoBehaviour
{
    public CrustaspikanEncounterSettings Settings { get; private set; }
    public CrustaspikanEncounterBrain Brain { get; private set; }
    public Vector3 ArenaCenter => transform.position;
    public Vector3 PlayerVelocity { get; private set; }
    public Vector3 BossVelocity { get; private set; }
    public bool MotionSampleValid { get; private set; }
    private Vector3 previousBossPosition;
    private EnemyMotor bossMotor;
    private bool hasMotionSample;
    public int AliveAdds => Brain?.Composite != null ? Brain.Composite.LiveAddCount : 0;
    public int ReservedAdds => AliveAdds + (Brain?.Composite != null ? Brain.Composite.ActiveFlightCount : 0);
    public int TotalAddsSpawned => Brain?.Composite != null ? Brain.Composite.SummonedCount : 0;
    public int EliteThrows => Brain?.Composite != null ? Brain.Composite.EliteThrowCount : 0;
    public bool Defeated { get; private set; }
    public string LastFailure { get; private set; }
    public EnemyBossHudView BossHud => bossHud;
    public CrustaspikanEntranceCinematic EntranceCinematic { get; private set; }
    public bool IsIntroducing => EntranceCinematic != null && EntranceCinematic.IsPlaying;
    private CrustaspikanEncounterHost host;
    private PlayerActorRuntime player;
    private EnemySpawnService spawns;
    private EnemyActor leasedBoss;
    private uint leasedBossVersion;
    private CrustaspikanArenaVisuals visuals;
    private EnemyBossHudView bossHud;
    private GameObject ownedBossHud;
    private QuarterViewCamera gameplayCamera;
    private OverburstCinemachineCameraRig cameraRig;
    private Collider previousConfinerVolume;
    private float previousConfinerSlowing;
    private bool confinerCaptured, exiting, framingCaptured;
    private readonly List<EnemyTargetHpHud> pausedTargetHuds = new List<EnemyTargetHpHud>();
    private readonly Dictionary<EnemyActor, bool> oldLoot = new Dictionary<EnemyActor, bool>();
    private readonly Dictionary<EnemyActor, uint> leases = new Dictionary<EnemyActor, uint>();
    private Vector3 returnPosition, previousPlayerPosition;
    private Quaternion returnRotation;
    private float returnHp;
    public bool Begin(CrustaspikanEncounterHost host, CrustaspikanEncounterSettings settings, PlayerActorRuntime player)
    {
        this.host = host; Settings = settings; this.player = player;
        returnPosition = player.transform.position; returnRotation = player.transform.rotation; returnHp = player.Health.CurrentHp;
        transform.position = new Vector3(400, 0, 400); visuals = new CrustaspikanArenaVisuals(); visuals.BuildArena(transform, settings.arenaRadius);
        spawns = EnemySpawnService.Current;
        if (spawns == null)
        {
            var services = new GameObject("Crustaspikan Temporary Spawn Service"); services.transform.SetParent(transform, false);
            var inactive = new GameObject("Inactive Actors"); inactive.transform.SetParent(services.transform, false); inactive.SetActive(false);
            var pool = services.AddComponent<EnemyPoolService>(); pool.Configure(inactive.transform, 0);
            spawns = services.AddComponent<EnemySpawnService>(); spawns.Configure(settings.materials.catalog, pool);
        }
        if (!spawns.RegisterAdditionalCatalog(settings.materials.catalog, out string reason)
            || !spawns.RegisterAdditionalCatalog(settings.composites.summonCatalog, out reason))
        { Debug.LogError("[Crustaspikan] " + reason); return false; }
        player.Health.SetDamageDeathPrevention(this, settings.protectPlayerFromDeath);
        if (!PreparePresentation()) return false;
        var portal = visuals.Portal(transform, new Vector3(0, .05f, -settings.arenaRadius + 3f), new Color(.8f, .3f, 1f), "하이드아웃 복귀 포탈");
        portal.AddComponent<CrustaspikanEncounterPortal>().Configure(host, this);
        Teleport(ArenaCenter + new Vector3(0, .08f, -10), Quaternion.identity);
        if (!SpawnBoss()) return false;
        if (settings.entrance != null && settings.entrance.enabled)
        {
            var director = new GameObject("Crustaspikan Entrance Director"); director.transform.SetParent(transform, false);
            EntranceCinematic = director.AddComponent<CrustaspikanEntranceCinematic>();
            if (!EntranceCinematic.Play(this, player, gameplayCamera, settings.entrance))
            { Destroy(director); EntranceCinematic = null; Brain.FinishEntrance(1f); }
        }
        return true;
    }
    private bool SpawnBoss()
    {
        try
        {
            if (!spawns.TrySpawn(new EnemySpawnRequest(Settings.materials.actorDefinition, ArenaCenter + new Vector3(0, .05f, 2), Quaternion.Euler(0, 180, 0),
                targetTransform: player.transform, spawnParent: transform, context: EncounterContext.Test), out var actor))
            { LastFailure = "보스를 대여하지 못했습니다."; return false; }
            // 전투 로직 생성이 실패하거나 드롭 컴포넌트가 없어도 대여 수명을 소유한다.
            leasedBoss = actor; leasedBossVersion = actor.LeaseVersion;
            bossMotor = actor.GetComponent<EnemyMotor>(); ResetMotionSamples();
            DisableLoot(actor); Brain = new CrustaspikanEncounterBrain(this, actor, player);
            Brain.Composite.MonsterLanded += DisableLoot;
            bossHud.BindEncounter(Brain); return true;
        }
        catch (System.Exception error)
        {
            LastFailure = error.Message;
            Debug.LogWarning("[Crustaspikan] 보스 준비 실패: " + LastFailure);
            ClearCombat(); return false;
        }
    }
    private void DisableLoot(EnemyActor actor)
    {
        var loot = actor.GetComponent<EnemyLootDropper>();
        if (loot != null) { oldLoot[actor] = loot.enabled; leases[actor] = actor.LeaseVersion; loot.enabled = false; actor.Health.OnDamageResolved += OwnedDamage; actor.Health.OnDead += OwnedDead; }
    }
    private void OwnedDamage(CombatHealth health, DamageInfo info, float amount, bool fatal)
    {
        if (!fatal) return;
        // 본체 사망이 소환체를 반납하기 전에 살아 있는 소환체의 상태를 돌린다.
        var actor = health.GetComponent<EnemyActor>();
        if (Brain?.Actor == actor)
        {
            BossDefeated();
            foreach (var owned in oldLoot.Keys) if (owned != actor) RestoreLoot(owned);
        }
    }
    private void OwnedDead(CombatHealth health, DamageInfo info)
    {
        // OnDead의 호출 목록이 확정된 뒤 복원해 이번 시험 사망에서 드롭이 발생하지 않게 한다.
        RestoreLoot(health.GetComponent<EnemyActor>());
    }
    private void RestoreLoot(EnemyActor actor)
    {
        if (actor == null || !leases.TryGetValue(actor, out uint version) || actor.LeaseVersion != version) return;
        actor.Health.OnDamageResolved -= OwnedDamage;
        actor.Health.OnDead -= OwnedDead;
        var loot = actor.GetComponent<EnemyLootDropper>(); if (loot != null && oldLoot.TryGetValue(actor, out bool value)) loot.enabled = value;
    }
    private void RestoreAllLoot() { foreach (var actor in oldLoot.Keys) RestoreLoot(actor); }
    private bool PreparePresentation()
    {
        foreach (var view in FindObjectsByType<EnemyBossHudView>(FindObjectsSortMode.None))
            if (view.isActiveAndEnabled) { bossHud = view; break; }
        if (bossHud == null)
        {
            var prefab = Resources.Load<GameObject>("UI/HUD/PF_EnemyBossHud");
            if (prefab == null) { Debug.LogError("[Crustaspikan] 기존 보스 HUD 프리팹을 찾지 못했습니다."); return false; }
            ownedBossHud = Instantiate(prefab, transform, false);
            bossHud = ownedBossHud.GetComponent<EnemyBossHudView>();
            if (bossHud == null) return false;
        }
        // 같은 상단 자리를 쓰는 일반 대상 HUD는 이 전투 동안만 쉬고, 쫄의 머리 위 HP는 유지한다.
        foreach (var view in FindObjectsByType<EnemyTargetHpHud>(FindObjectsSortMode.None))
            if (view.enabled) { pausedTargetHuds.Add(view); view.enabled = false; }

        gameplayCamera = QuarterViewCamera.ActiveInstance;
        if (gameplayCamera == null && Camera.main != null) gameplayCamera = Camera.main.GetComponent<QuarterViewCamera>();
        framingCaptured = gameplayCamera != null && gameplayCamera.TryBeginFramingScope(this, 1.65f);
        cameraRig = gameplayCamera != null ? gameplayCamera.CinemachineRig : null;
        if (cameraRig != null && cameraRig.Confiner != null)
        {
            previousConfinerVolume = cameraRig.Confiner.BoundingVolume;
            previousConfinerSlowing = cameraRig.Confiner.SlowingDistance;
            confinerCaptured = true;
            // 원격 임시 무대에 하이드아웃의 공간 제한만 적용하지 않는다. 휠·시점·렌즈는 기존 경로를 사용한다.
            cameraRig.SetConfinerVolume(null);
        }
        return true;
    }
    private void Update()
    {
        if (exiting || Brain == null) return;
        if (player == null || spawns == null) { Exit(false); return; }
        if (IsIntroducing)
        {
            if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame) Exit(true);
            ResetMotionSamples();
            return;
        }
        ObserveMotion();
        if (Keyboard.current != null && !GameplayInputBlocker.IsGameplayInputBlocked)
        { if (Keyboard.current.f9Key.wasPressedThisFrame) { Exit(true); return; } if (Keyboard.current.f8Key.wasPressedThisFrame) { Restart(); return; } }
        Brain.Tick();
        if ((player.transform.position - ArenaCenter).sqrMagnitude > Mathf.Pow(Settings.arenaRadius + 8, 2) || player.transform.position.y < -3f)
            Teleport(ArenaCenter + new Vector3(0, .08f, -10), Quaternion.identity);
    }
    private void ResetMotionSamples()
    {
        hasMotionSample = MotionSampleValid = false;
        PlayerVelocity = BossVelocity = Vector3.zero;
    }
    private void ObserveMotion()
    {
        Vector3 playerPosition = player.transform.position;
        Vector3 bossPosition = bossMotor != null ? bossMotor.Position : leasedBoss.transform.position;
        float dt = Time.deltaTime, limit = Mathf.Max(1f, dt * 25f);
        MotionSampleValid = hasMotionSample && dt > 0f && dt <= .25f && leasedBoss.IsLeased
            && leasedBoss.LeaseVersion == leasedBossVersion
            && Vector3.Distance(playerPosition, previousPlayerPosition) <= limit
            && Vector3.Distance(bossPosition, previousBossPosition) <= limit;
        PlayerVelocity = MotionSampleValid ? Vector3.ClampMagnitude((playerPosition - previousPlayerPosition) / dt, 12f) : Vector3.zero;
        BossVelocity = MotionSampleValid ? Vector3.ClampMagnitude((bossPosition - previousBossPosition) / dt, 12f) : Vector3.zero;
        previousPlayerPosition = playerPosition; previousBossPosition = bossPosition; hasMotionSample = true;
    }
    public Vector3 ClampArena(Vector3 position, float margin = 2f)
    { Vector3 delta = position - ArenaCenter; delta.y = 0; return ArenaCenter + Vector3.ClampMagnitude(delta, Settings.arenaRadius - margin) + Vector3.up * .08f; }
    public void Announce(string text, float seconds) => bossHud?.ShowEncounterNotice(Brain, text, seconds);
    public void BossDefeated()
    { if (Defeated) return; Defeated = true; }
    private void Teleport(Vector3 position, Quaternion rotation)
    {
        if (player == null) return; var cc = player.CharacterController; bool enabled = cc != null && cc.enabled;
        if (enabled) cc.enabled = false; player.transform.SetPositionAndRotation(position, rotation); if (enabled) cc.enabled = true;
        ResetMotionSamples(); player.Movement?.ResetMotionAfterTeleport(); Physics.SyncTransforms();
        if (gameplayCamera != null && gameplayCamera.CurrentTarget != null) gameplayCamera.SetTarget(gameplayCamera.CurrentTarget);
    }
    public void ClearSummons()
    {
        if (Brain?.Composite == null) return;
        RestoreAllLoot(); Brain.Composite.Cancel(); Brain.Composite.ReleaseSummons();
        foreach (var actor in new List<EnemyActor>(oldLoot.Keys))
        {
            if (actor == Brain.Actor) continue;
            if (actor != null && actor.IsLeased && actor.LeaseVersion == leases[actor]) spawns.Release(actor);
            oldLoot.Remove(actor); leases.Remove(actor);
        }
        // 보스의 전투 중 드롭 차단은 유지한다.
        var boss = Brain.Actor; var loot = boss.GetComponent<EnemyLootDropper>();
        if (loot != null) { loot.enabled = false; boss.Health.OnDamageResolved -= OwnedDamage; boss.Health.OnDamageResolved += OwnedDamage; boss.Health.OnDead -= OwnedDead; boss.Health.OnDead += OwnedDead; }
    }
    private void ClearCombat()
    {
        try
        {
            if (Brain != null)
            {
                bossHud?.ClearEncounter(Brain);
                Brain.Composite.MonsterLanded -= DisableLoot; ClearSummons();
            }
        }
        finally
        {
            try { RestoreAllLoot(); }
            finally
            {
                try { Brain?.Dispose(); }
                finally
                {
                    Brain = null;
                    try
                    {
                        if (leasedBoss != null && leasedBoss.IsLeased && leasedBoss.LeaseVersion == leasedBossVersion)
                            spawns.Release(leasedBoss);
                    }
                    finally { ResetMotionSamples(); bossMotor = null; leasedBoss = null; oldLoot.Clear(); leases.Clear(); }
                }
            }
        }
    }
    public void Restart() => TryRestart();
    public bool TryRestart()
    {
        if (exiting) return false;
        bool restarted = false; LastFailure = "";
        try
        {
            EntranceCinematic?.Cancel(); ClearCombat(); Defeated = false;
            if (!Settings.Validate(out string reason)) { LastFailure = reason; return false; }
            if (player == null || player.Health == null) { LastFailure = "재전투할 플레이어가 없습니다."; return false; }
            player.Health.Heal(player.Health.MaxHp); Teleport(ArenaCenter + new Vector3(0, .08f, -10), Quaternion.identity);
            if (!SpawnBoss()) return false;
            Announce("재전투", 2f); restarted = true; return true;
        }
        catch (System.Exception error)
        {
            LastFailure = error.Message;
            Debug.LogWarning("[Crustaspikan] 재전투 준비 실패: " + LastFailure); return false;
        }
        finally { if (!restarted) Exit(true); }
    }
    public void Exit(bool returnToHideout)
    {
        if (exiting) return; exiting = true;
        try { EntranceCinematic?.Cancel(); ClearCombat(); }
        finally
        {
            try
            {
                if (framingCaptured && gameplayCamera != null) gameplayCamera.EndFramingScope(this); framingCaptured = false;
                if (player != null && player.Health != null)
                {
                    player.Health.SetDamageDeathPrevention(this, false);
                    if (returnToHideout) { Teleport(returnPosition, returnRotation); player.Health.Heal(Mathf.Max(0, returnHp - player.Health.CurrentHp)); }
                }
                if (confinerCaptured && cameraRig != null) cameraRig.SetConfinerVolume(previousConfinerVolume, previousConfinerSlowing);
                foreach (var view in pausedTargetHuds) if (view != null) view.enabled = true;
                pausedTargetHuds.Clear();
                if (ownedBossHud != null) Destroy(ownedBossHud);
            }
            finally { host?.OnExit(this); Destroy(gameObject); }
        }
    }
    private void OnDestroy() { if (!exiting) Exit(false); visuals?.Dispose(); }
}
