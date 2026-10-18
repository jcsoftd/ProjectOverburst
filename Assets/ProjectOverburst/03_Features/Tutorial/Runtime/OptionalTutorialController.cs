using System;
using System.Collections.Generic;
using System.Linq;
using Overburst.Persistence;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(1500)]
public sealed class OptionalTutorialController : MonoBehaviour
{
    public const string ResourcePath = "Tutorial/PF_OverburstTutorialUI";
    public const string ArmorId = "6876578b52c57f24eb78ad0d49d6c98d";
    public const string PotionId = "d34f3877df9b4a148ad8a21ac0aa7d5e";
    public static OptionalTutorialController Instance { get; private set; }
    public TutorialView view;
    public TutorialArena arenaPrefab;
    public bool IsTraining => account != null;
    public TutorialLesson Lesson { get; private set; }
    public TutorialArena Arena => arena;
    public string LastError { get; private set; }
    public bool HasUnsavedProgress { get; private set; }
    public int CompletedMask => progress?.completed ?? 0;
    private TutorialProgress progress;
    private string progressDirectory;
    private TutorialAccountScope account;
    private TutorialArena arena;
    private Scene previousContent, previousActive, trainingScene;
    private readonly List<GameObject> suspendedRoots = new List<GameObject>();
    private Transform player, previousCameraTarget;
    private QuarterViewCamera trainingCamera;
    private bool ownsCamera;
    private Camera trainingViewCamera;
    private CameraClearFlags previousClearFlags;
    private Skybox previousCameraSkybox;
    private bool previousSkyboxEnabled;
    private CombatHealth health;
    private MeleeRuntime melee;
    private OverburstElementEnergy energy;
    private PlayerParryController parry;
    private PlayerEvadeController evade;
    private Vector3 previousPosition;
    private Quaternion previousRotation;
    private float previousHealth, previousEnergy, previousEnergyHold;
    private int previousRadiance;
    private bool previousCombat, success, weakHit, potionBound, closing;
    private int parryBaseline, dischargeBaseline, defeated, potionCount;
    private float successDelay, recoverDelay, pollAt;
    private WorldItemPickup pickup;
    private PlayerInputFacade lockedInput;
    private bool modalOwnsInput;
    private bool quitting;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Instance = null;
    private void Awake() { Instance = this; view.Bind(this); view.Hide(); }
    public static OptionalTutorialController Install()
    {
        if (Instance) return Instance;
        var prefab = Resources.Load<OptionalTutorialController>(ResourcePath);
        if (!prefab) { Debug.LogError("[Tutorial] 튜토리얼 프리팹이 없습니다."); return null; }
        var result = Instantiate(prefab, PersistentSceneFlow.Instance.transform, false);
        result.name = prefab.name;
        return result;
    }
    public void OfferIfNeeded()
    {
        if (!WorldSessionState.IsHideout || !AccountBootstrap.Ready || IsTraining) return;
        if (!LoadProgress()) { ShowModal(); view.ShowError(LastError); return; }
        var state = AccountGameplaySession.Current?.Read();
        if (state == null || string.IsNullOrEmpty(state.activeCharacterId)
            || progress.offered || progress.offeredCharacterIds.Count > 0) return;
        bool previouslyOffered = progress.offered;
        progress.offeredCharacterIds.Add(state.activeCharacterId);
        progress.offered = true;
        if (!SaveProgress())
        {
            progress.offeredCharacterIds.Remove(state.activeCharacterId);
            progress.offered = previouslyOffered;
            ShowModal(); view.ShowError(LastError); return;
        }
        if (state.level != 1) return;
        ShowModal(); view.ShowInvitation();
    }
    private bool LoadProgress()
    {
        try
        {
            string directory = AccountBootstrap.SaveDirectory ?? AccountBootstrap.ResolveSaveDirectory();
            if (HasUnsavedProgress && progress != null && string.Equals(progressDirectory, directory, StringComparison.OrdinalIgnoreCase))
                return SaveProgress();
            var loaded = TutorialProgress.Load(directory);
            progressDirectory = directory;
            progress = loaded; LastError = null; HasUnsavedProgress = false; return true;
        }
        catch (Exception error) { LastError = error.Message; Debug.LogException(error); return false; }
    }
    private bool SaveProgress()
    {
        try { progress?.Save(progressDirectory); LastError = null; HasUnsavedProgress = false; return true; }
        catch (Exception error) { HasUnsavedProgress = true; LastError = "학습 기록 저장 실패: " + error.Message; Debug.LogException(error); return false; }
    }
    public void StartLesson(int index)
    {
        if (index < 0 || index >= TutorialLessons.Count) return;
        try
        {
            if (!IsTraining)
            {
                if (!LoadProgress()) { ShowModal(); view.ShowError(LastError); return; }
                EnterTraining();
            }
            PrepareLesson((TutorialLesson)index);
        }
        catch (Exception error)
        {
            LastError = error.Message; Debug.LogException(error);
            if (TryExit(out var restoreError)) { ShowModal(); view.ShowError(LastError); }
            else view.ShowError(restoreError);
        }
    }
    private void EnterTraining()
    {
        if (!WorldSessionState.IsHideout || PersistentSceneFlow.Instance.IsSwitching)
            throw new InvalidOperationException("마을에서 훈련을 시작할 수 있습니다.");
        if (!arenaPrefab) throw new InvalidOperationException("마름모 훈련장 자산이 없습니다.");
        player = PlayerContext.Instance.CurrentActor.transform;
        health = player.GetComponent<CombatHealth>(); melee = player.GetComponent<MeleeRuntime>();
        energy = player.GetComponent<OverburstElementEnergy>(); parry = player.GetComponent<PlayerParryController>();
        evade = player.GetComponent<PlayerEvadeController>();
        // Normal combat creates energy on the first confirmed hit; the lesson needs it before that hit.
        if (!energy) energy = player.gameObject.AddComponent<OverburstElementEnergy>();
        if (!health || health.IsDead || !energy || !melee || !parry || !evade)
            throw new InvalidOperationException("플레이어 전투 준비가 끝나지 않았습니다.");
        previousContent = WorldSessionState.ContentScene; previousActive = SceneManager.GetActiveScene();
        previousPosition = player.position; previousRotation = player.rotation;
        previousHealth = health.CurrentHp; previousEnergy = energy.Amount;
        previousRadiance = energy.RadianceStacks; previousEnergyHold = energy.FullHoldRemaining;
        previousCombat = PlayerCombatModeController.IsSharedCombatModeActive();
        CancelActions();
        var session = AccountGameplaySession.Current;
        account = new TutorialAccountScope(CreateLoadout(session.ContentRegistry, session.Read()));
        health.SetDamageDeathPrevention(this, true);
        WorldSessionState.SetPhase(WorldPhase.Tutorial);
        trainingScene = SceneManager.CreateScene("Overburst_Tutorial_" + GetInstanceID());
        arena = Instantiate(arenaPrefab);
        SceneManager.MoveGameObjectToScene(arena.gameObject, trainingScene);
        // Align the two diagonals with the camera so the playable outline reads as a diamond.
        float yaw = Camera.main ? Camera.main.transform.eulerAngles.y : 0f;
        arena.transform.rotation = Quaternion.Euler(0, yaw, 0);
        trainingCamera = QuarterViewCamera.ActiveInstance;
        if (trainingCamera)
        {
            previousCameraTarget = trainingCamera.CurrentTarget;
            if (!trainingCamera.TryBeginFramingScope(this, 1f))
                throw new InvalidOperationException("현재 카메라 전환이 끝난 뒤 다시 시작해 주세요.");
            ownsCamera = true;
            trainingCamera.SetTarget(arena.transform);
            trainingCamera.SetYaw(trainingCamera.CurrentYaw);
        }
        foreach (var root in previousContent.GetRootGameObjects())
        {
            if (!root.activeSelf || player.IsChildOf(root.transform) || transform.IsChildOf(root.transform)) continue;
            suspendedRoots.Add(root); root.SetActive(false);
        }
        SceneManager.SetActiveScene(trainingScene); WorldSessionState.SetContentScene(trainingScene);
        RenderSettings.skybox = arena.skyMaterial;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.65f, .68f, .72f);
        RenderSettings.ambientEquatorColor = new Color(.5f, .51f, .53f);
        RenderSettings.ambientGroundColor = new Color(.3f, .27f, .24f);
        RenderSettings.fog = false;
        trainingViewCamera = Camera.main;
        if (trainingViewCamera)
        {
            previousClearFlags = trainingViewCamera.clearFlags;
            trainingViewCamera.clearFlags = CameraClearFlags.Skybox;
            previousCameraSkybox = trainingViewCamera.GetComponent<Skybox>();
            if (previousCameraSkybox)
            {
                previousSkyboxEnabled = previousCameraSkybox.enabled;
                previousCameraSkybox.enabled = false;
            }
        }
        arena.Hit += OnHit; arena.Defeated += OnDefeated; evade.OnEvadeEnded += OnEvadeEnded;
    }
    public static AccountSnapshot CreateLoadout(AccountContentRegistry registry, AccountSnapshot original)
    {
        var state = NewAccountFactory.Create(registry);
        state.appearance = ItemSnapshotCodec.CopyValues(original.appearance);
        var potion = NewItem(registry, PotionId); potion.stackCount = 5;
        var potionSnapshot = ItemSnapshotCodec.Capture(potion, registry);
        int slot = state.inventory.FindIndex(0, state.unlockedSlots, string.IsNullOrEmpty);
        state.items.Add(potionSnapshot); state.inventory[slot] = potionSnapshot.instanceId;
        state.nextAcquisitionOrder = state.items.Max(item => item.acquisitionOrder) + 1;
        AccountInvariants.Validate(state, registry); return state;
    }
    private static ItemData NewItem(AccountContentRegistry registry, string id)
    {
        var item = new ItemData(registry.Resolve<BaseItemData>(id), 1, ItemGrade.Common);
        item.EnsureRuntimeState(); item.EnsureAcquisitionOrder(); return item;
    }
    private void PrepareLesson(TutorialLesson lesson)
    {
        HideModal(); CancelActions();
        if (pickup) { Destroy(pickup.gameObject); pickup = null; }
        Lesson = lesson; success = weakHit = potionBound = false; defeated = 0; successDelay = recoverDelay = 0;
        ActorTeleportUtility.TeleportSafely(player, arena.playerSpawn.position, arena.playerSpawn.rotation);
        health.RestoreLivingHealth(health.MaxHp);
        if (lesson == TutorialLesson.Move) PlayerCombatModeController.ExitSharedCombatMode(PlayerCombatModeReason.System);
        else PlayerCombatModeController.EnterSharedCombatMode(PlayerCombatModeReason.System);
        arena.moveTarget.gameObject.SetActive(lesson == TutorialLesson.Move);
        int count = lesson == TutorialLesson.Combat ? 3 : lesson >= TutorialLesson.Equipment || lesson == TutorialLesson.Move ? 0 : 1;
        arena.Prepare(player, count, lesson == TutorialLesson.Combat);
        float prepared = lesson == TutorialLesson.Discharge ? Mathf.Max(.3f, energy.Normalized) : TutorialLessons.PreparedEnergy(lesson);
        energy.PrepareTutorialEnergy(prepared);
        parryBaseline = parry.SuccessCount; dischargeBaseline = energy.DischargeCount;
        arena.AttacksEnabled = TutorialLessons.IsParry(lesson) || lesson == TutorialLesson.Combat;
        if (lesson == TutorialLesson.Equipment) PrepareEquipment();
        if (lesson == TutorialLesson.Potion)
        {
            var state = account.Training.Read(); potionCount = CountPotions(state);
            if (potionCount == 0)
            {
                var item = NewItem(account.Training.ContentRegistry, PotionId); item.stackCount = 5;
                AccountGameplaySession.AcquireWorldItem(account.Training.Owner.Inventory, item);
                potionCount = CountPotions(account.Training.Read());
            }
            health.RestoreLivingHealth(health.MaxHp * .4f);
        }
        progress.lastLesson = (int)lesson;
        bool recorded = SaveProgress(); view.ShowLesson(lesson);
        if (!recorded) view.Feedback("학습 기록 미저장 · 다시 시도하세요.");
    }
    private void PrepareEquipment()
    {
        var state = account.Training.Read();
        if (state.items.Any(item => item.contentId == ArmorId)) return;
        pickup = WorldItemDropFactory.CreateWorldPickup(NewItem(account.Training.ContentRegistry, ArmorId),
            arena.moveTarget.position, account.Training.Owner.Inventory, player);
        if (!pickup) throw new InvalidOperationException("훈련 방어구를 준비하지 못했습니다.");
        SceneManager.MoveGameObjectToScene(pickup.gameObject, trainingScene);
    }
    private static int CountPotions(AccountSnapshot state) => state.items.Where(item => item.contentId == PotionId).Sum(item => item.count);
    private void OnHit(CombatHealth target, DamageInfo info, float actualDamage, bool dead)
    {
        if (!IsTraining || success || actualDamage <= 0 || !info.source || info.source.transform.root != player.root || info.isDamageOverTime) return;
        bool weak = (info.playerAttackKind & PlayerAttackKind.Weak) != 0;
        bool heavy = (info.playerAttackKind & PlayerAttackKind.Heavy) != 0;
        if (Lesson == TutorialLesson.Charge && weak) weakHit = true;
        if (Lesson == TutorialLesson.Discharge && heavy && energy.DischargeCount > dischargeBaseline) Succeed();
        if (Lesson == TutorialLesson.DashLight && weak && melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.Light) Succeed();
        if (Lesson == TutorialLesson.DashHeavy && heavy && melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.Heavy
            && energy.DischargeCount > dischargeBaseline) Succeed();
    }
    private void OnDefeated(CombatHealth target, DamageInfo info) { if (Lesson == TutorialLesson.Combat && ++defeated >= 3) Succeed(); }
    private void OnEvadeEnded(PlayerEvadeType kind)
    {
        if (!IsTraining || !evade.LastEndWasCompleted) return;
        if (Lesson == TutorialLesson.Dash && kind == PlayerEvadeType.CombatDodge
            || Lesson == TutorialLesson.Roll && kind == PlayerEvadeType.Roll) Succeed();
    }
    private void Update()
    {
        if (!IsTraining || !arena || modalOwnsInput || GameplayInputBlocker.IsGameplayInputBlocked || Time.timeScale <= 0) return;
        if (success)
        {
            successDelay += Time.deltaTime;
            if (successDelay >= 1.5f && !melee.IsAttackInProgress)
            {
                if ((int)Lesson + 1 == TutorialLessons.Count) { ShowModal(); view.ShowComplete(progress); }
                else PrepareLesson(Lesson + 1);
            }
            return;
        }
        if (health.CurrentHp <= 1.01f && Lesson != TutorialLesson.Potion)
        {
            recoverDelay += Time.deltaTime;
            if (recoverDelay > 1.8f) { health.RestoreLivingHealth(health.MaxHp); recoverDelay = 0; }
        }
        if (Lesson == TutorialLesson.Move && Vector3.Distance(player.position, arena.moveTarget.position) < 1.4f
            && PlayerCombatModeController.IsSharedCombatModeActive()) Succeed();
        if (Lesson == TutorialLesson.Charge && weakHit && energy.Normalized >= .3f) Succeed();
        if (TutorialLessons.IsParry(Lesson))
        {
            if (parry.SuccessCount > parryBaseline)
            {
                parryBaseline = parry.SuccessCount;
                if (parry.ActionGrade == TutorialLessons.Grade(Lesson)) Succeed();
                else view.Feedback("다른 등급입니다. 다시 받아쳐보세요.");
            }
            if (!success && !melee.IsAttackInProgress && !parry.IsWindowOpen
                && Mathf.Abs(energy.Normalized - TutorialLessons.PreparedEnergy(Lesson)) > .001f)
                energy.PrepareTutorialEnergy(TutorialLessons.PreparedEnergy(Lesson));
        }
        if (Time.time < pollAt || Lesson < TutorialLesson.Equipment) return;
        pollAt = Time.time + .2f;
        var state = account.Training.Read();
        if (Lesson == TutorialLesson.Equipment && state.gear.Any(id => state.items.Any(item => item.instanceId == id && item.contentId == ArmorId))) Succeed();
        if (Lesson == TutorialLesson.Potion)
        {
            potionBound |= state.quickSlots.Any(slot => slot.consumableContentId == PotionId);
            if (potionBound && CountPotions(state) < potionCount && health.CurrentHp > health.MaxHp * .4f) Succeed();
        }
    }
    private void Succeed()
    {
        if (!IsTraining || success) return;
        success = true; arena.AttacksEnabled = false;
        progress.completed |= 1 << (int)Lesson; progress.skipped &= ~(1 << (int)Lesson);
        bool recorded = SaveProgress();
        if (Lesson == TutorialLesson.World) view.worldConfirm.gameObject.SetActive(false);
        view.Feedback(!recorded ? "성공 · 학습 기록을 저장하지 못했습니다."
            : TutorialLessons.IsParry(Lesson) ? TutorialLessons.Names[(int)Lesson] + " 성공 · 피해와 반격을 확인하세요." : "성공했습니다.");
    }
    public void ConfirmWorld() { if (Lesson == TutorialLesson.World) Succeed(); }
    public void Retry() { if (IsTraining) PrepareLesson(Lesson); }
    public void Skip()
    {
        if (!IsTraining) return;
        if ((progress.completed & (1 << (int)Lesson)) == 0) progress.skipped |= 1 << (int)Lesson;
        SaveProgress();
        if ((int)Lesson + 1 < TutorialLessons.Count) PrepareLesson(Lesson + 1);
        else { ShowModal(); view.ShowComplete(progress); }
    }
    public void Decline() { HideModal(); view.Hide(); }
    public void Exit()
    {
        if (!TryExit(out var error)) { ShowModal(); view.ShowError(error); }
    }
    public bool TryExit(out string error)
    {
        error = null;
        if (closing) { error = "훈련을 정리하고 있습니다."; return false; }
        if (!IsTraining) { Decline(); return true; }
        closing = true; ShowModal();
        try
        {
            CancelActions(); if (arena) { arena.AttacksEnabled = false; arena.ClearActors(); }
            if (evade) evade.OnEvadeEnded -= OnEvadeEnded;
            account.Restore();
            if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            WorldSessionState.SetContentScene(previousContent);
            foreach (var root in suspendedRoots) if (root) root.SetActive(true);
            suspendedRoots.Clear();
            ActorTeleportUtility.TeleportSafely(player, previousPosition, previousRotation);
            if (ownsCamera && trainingCamera)
            {
                trainingCamera.SetTarget(previousCameraTarget);
                trainingCamera.EndFramingScope(this);
                trainingCamera.SetYaw(trainingCamera.CurrentYaw);
            }
            ownsCamera = false; trainingCamera = null; previousCameraTarget = null;
            if (trainingViewCamera) trainingViewCamera.clearFlags = previousClearFlags;
            if (previousCameraSkybox) previousCameraSkybox.enabled = previousSkyboxEnabled;
            trainingViewCamera = null; previousCameraSkybox = null;
            health.RestoreLivingHealth(previousHealth); energy.RestoreTutorialEnergy(previousEnergy, previousRadiance, previousEnergyHold);
            health.SetDamageDeathPrevention(this, false);
            if (previousCombat) PlayerCombatModeController.EnterSharedCombatMode(PlayerCombatModeReason.System);
            else PlayerCombatModeController.ExitSharedCombatMode(PlayerCombatModeReason.System);
            WorldSessionState.SetPhase(WorldPhase.Hideout);
            account = null;
            if (trainingScene.IsValid() && trainingScene.isLoaded) SceneManager.UnloadSceneAsync(trainingScene);
            arena = null; pickup = null;
            HideModal(); view.Hide(); return true;
        }
        catch (Exception failure) { error = "훈련 종료 중 복원 실패: " + failure.Message; LastError = error; Debug.LogException(failure); return false; }
        finally { closing = false; }
    }
    private void CancelActions() => PlayerContext.Instance?.CurrentActorKit?.CancelCurrentActions(WeaponActionCancelReason.RuntimeDisabled);
    private void ShowModal()
    {
        if (arena) arena.PauseCombat();
        modalOwnsInput = true; GameplayInputBlocker.Block(this);
        lockedInput = PlayerInputFacade.Current;
        lockedInput?.SetGameplayMapDisabled(this, true);
    }
    private void HideModal()
    {
        modalOwnsInput = false; GameplayInputBlocker.Unblock(this);
        PlayerInputFacade.ReleaseGameplayMapDisabled(this); lockedInput = null;
    }
    private void OnDestroy()
    {
        if (!quitting && IsTraining && !TryExit(out var error)) Debug.LogError(error);
        HideModal(); if (Instance == this) Instance = null;
    }
    private void OnApplicationQuit() { quitting = true; }
}
