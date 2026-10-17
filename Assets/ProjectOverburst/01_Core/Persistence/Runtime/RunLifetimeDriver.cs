using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overburst.Persistence
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(32000)]
    public sealed class RunLifetimeDriver : MonoBehaviour
    {
        private bool portalRequested;
        private MapEmergencyExit emergencyExit;
        private bool abandoned;
        private bool returnPending;
        private bool returnRequested;
        private RunOutcome outcome;
        private float nextPoll;
        private long pausedTicksSinceBossClear;
        private int portalRequestFrame;
        public string LastError { get; private set; }
        private OverburstDeathScreen deathScreen;
        private CombatHealth deathHealth;
        private string deathRunId;
        private bool deathActive, deathSettled, ownsDeathPause;
        private float deathPauseAfter;
        private readonly List<Canvas> deathHiddenCanvases = new List<Canvas>();
        public OverburstDeathScreen DeathScreen => deathScreen;
        public bool DeathSettled => deathSettled;

        private void OnEnable() => WorldSessionState.Changed += WorldChanged;
        private void OnDisable()
        {
            WorldSessionState.Changed -= WorldChanged;
            emergencyExit = null;
            GameplayInputBlocker.Unblock(this);
            ClearDeath();
        }

        public bool RequestPortalExit()
        {
            if (!AccountInvariants.IsCompleted(AccountGameplaySession.Current?.CurrentRunPhase)
                || PlayerContext.Instance?.CurrentActorHealth?.IsDead != false) return false;
            if (!portalRequested) portalRequestFrame = Time.frameCount;
            portalRequested = true;
            return true;
        }

        public bool RequestEmergencyExit(MapEmergencyExit source)
        {
            if (!source || !source.CanExtract || returnPending
                || PlayerContext.Instance?.CurrentActorHealth?.IsDead != false) return false;
            if (!portalRequested && !emergencyExit) portalRequestFrame = Time.frameCount;
            emergencyExit = source;
            return true;
        }

        public void CancelEmergencyExit(MapEmergencyExit source)
        {
            if (emergencyExit != source) return;
            emergencyExit = null;
            if (!returnPending && !portalRequested && !abandoned
                && PlayerContext.Instance?.CurrentActorHealth?.IsDead == false)
            {
                LastError = null;
                GameplayInputBlocker.Unblock(this);
            }
        }

        public void RequestAbandon()
        {
            var run = AccountGameplaySession.Current?.ReadRun();
            if (run != null && run.phase != RunPhase.Failed && run.phase != RunPhase.Extracted)
                abandoned = true;
        }

        private void LateUpdate()
        {
            if (deathActive || PlayerContext.Instance?.CurrentActorHealth?.IsDead == true)
            {
                Poll(DateTime.UtcNow.Ticks);
                return;
            }
            // 2026-10-01 ESC 메뉴: 보스 처치 뒤 180초 자동 귀환은 실제 시계(UTC)로 잰다.
            // 멈춘 동안은 정산하지 않고, 보스를 잡은 뒤 멈춰 있던 시간만큼 그 시계를 늦춰 준다.
            if (OverburstTimeEffectArbiter.IsPaused)
            {
                if (AccountGameplaySession.Current?.CurrentRunPhase == RunPhase.BossCleared)
                    pausedTicksSinceBossClear += (long)(Time.unscaledDeltaTime * TimeSpan.TicksPerSecond);
                return;
            }
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + .25f;
            if (AccountGameplaySession.Current?.CurrentRunPhase != RunPhase.BossCleared) pausedTicksSinceBossClear = 0;
            Poll(DateTime.UtcNow.Ticks - pausedTicksSinceBossClear);
        }

        public void Poll(long utcTicks)
        {
            var account = AccountGameplaySession.Current;
            if (account == null) return;
            var currentHealth = PlayerContext.Instance?.CurrentActorHealth;
            if (deathActive) { TickDeath(); return; }
            if (currentHealth != null && currentHealth.IsDead && !returnRequested
                && WorldSessionState.Phase != WorldPhase.Booting && PersistentSceneFlow.Instance?.IsSwitching != true)
            {
                BeginDeath(account, currentHealth);
                return;
            }
            if (!account.TryRecoverProjection())
            {
                nextPoll = Time.unscaledTime + 1f;
                return;
            }
            if (returnPending) { TryReturn(); return; }
            if (WorldSessionState.Phase != WorldPhase.Run && !abandoned) return;
            var health = PlayerContext.Instance?.CurrentActorHealth;
            if (health == null) return;
            if (emergencyExit && !emergencyExit.CanExtract) CancelEmergencyExit(emergencyExit);
            // Finish the requesting frame's combat before resolving a successful extraction.
            if ((portalRequested || emergencyExit) && Time.frameCount <= portalRequestFrame && !health.IsDead && !abandoned) return;
            try
            {
                outcome = new AccountRunSession(account).ResolveOutcome(utcTicks, health.IsDead, portalRequested, abandoned, emergencyExit && emergencyExit.CanExtract);
                if (outcome == RunOutcome.None) return;
                LastError = null;
                portalRequested = abandoned = false;
                emergencyExit = null;
                returnPending = true;
                GameplayInputBlocker.Block(this);
                TryReturn();
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                // Keep the request pending: the same outcome is retried without announcing success.
                LastError = error.Message;
                GameplayInputBlocker.Block(this);
                nextPoll = Time.unscaledTime + 1f;
                Debug.LogError("런 정산 저장에 실패했습니다. 재시도합니다: " + error.Message);
            }
        }

        private void TryReturn()
        {
            var flow = PersistentSceneFlow.Instance;
            if (returnRequested || flow == null || flow.IsSwitching) return;
            returnRequested = true;
            flow.ReturnToHub(outcome == RunOutcome.Extracted
                ? RunSceneReturnContext.CreateExtractSuccess(PersistentSceneFlow.DefaultHubSceneName, "Default")
                : RunSceneReturnContext.CreateHubTransfer(PersistentSceneFlow.DefaultHubSceneName, "Default"));
        }

        private void WorldChanged(WorldPhase phase)
        {
            if (phase != WorldPhase.Hideout || (!returnPending && !deathActive)) return;
            var health = PlayerContext.Instance?.CurrentActorHealth;
            if (health != null)
            {
                health.SetRunMapModifiers(1f, 1f);
                health.ResetHealth();
            }
            returnPending = returnRequested = false;
            emergencyExit = null;
            outcome = RunOutcome.None;
            GameplayInputBlocker.Unblock(this);
            ClearDeath();
        }

        private void BeginDeath(AccountGameplaySession account, CombatHealth health)
        {
            PersistentSceneFlow.ClosePersistentUiForSceneSwitch();
            var run = account.ReadRun();
            bool mapRun = WorldSessionState.Phase == WorldPhase.Run && run != null
                && run.phase != RunPhase.Extracted;
            deathRunId = mapRun ? run.runId : null;
            deathHealth = health; deathActive = true; deathSettled = false;
            portalRequested = abandoned = false; emergencyExit = null;
            GameplayInputBlocker.Block(this);
            if (!deathScreen) deathScreen = OverburstDeathScreen.Create(transform, RequestDeathReturn);
            string sceneName = WorldSessionState.ContentScene.name ?? "";
            deathScreen.Show(mapRun, sceneName.IndexOf("Cave", StringComparison.OrdinalIgnoreCase) >= 0 ? "동굴" : "사막");
            deathHiddenCanvases.Clear();
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (canvas.isActiveAndEnabled && !canvas.transform.IsChildOf(deathScreen.transform))
                { deathHiddenCanvases.Add(canvas); canvas.enabled = false; }
            deathPauseAfter = health.GetComponent<PlayerKnockdownController>()?.BeginDeathPose() ?? 1.35f;
            TryDeathSettlement();
        }

        private void TickDeath()
        {
            if (!deathHealth || deathHealth != PlayerContext.Instance?.CurrentActorHealth
                || (!deathHealth.IsDead && !returnRequested))
            { ClearDeath(); GameplayInputBlocker.Unblock(this); return; }
            if (!returnRequested && !ownsDeathPause && deathScreen.Elapsed >= deathPauseAfter)
            { deathScreen.CaptureWorld(); OverburstTimeEffectArbiter.SetPaused(true); ownsDeathPause = true; }
        }

        private void TryDeathSettlement()
        {
            if (deathSettled) return;
            deathScreen.SetSaving();
            try
            {
                var account = AccountGameplaySession.Current;
                if (account == null || !account.TryRecoverProjection()) throw new IOException("계정 투영 복구 대기");
                if (!string.IsNullOrEmpty(deathRunId))
                {
                    var run = account.ReadRun();
                    if (run == null || run.runId != deathRunId) throw new InvalidOperationException("사망한 런의 ID가 바뀌었습니다.");
                    if (run.phase != RunPhase.Failed && !new AccountRunSession(account).Fail(deathRunId))
                        throw new IOException("런 실패 정산을 저장하지 못했습니다.");
                }
                if (!account.FlushPendingSave()) throw new IOException("사망 체크포인트를 저장하지 못했습니다.");
                LastError = null; deathSettled = true; deathScreen.SetReady();
            }
            catch (Exception error)
            {
                LastError = error.Message; deathScreen.SetError(false);
                Debug.LogWarning("[PlayerDeath] 정산 대기: " + LastError);
            }
        }

        public void RequestDeathReturn()
        {
            if (!deathActive || returnRequested || !deathScreen.returnButton.interactable) return;
            if (!deathSettled) { TryDeathSettlement(); return; }
            var flow = PersistentSceneFlow.Instance;
            if (!flow || flow.IsSwitching || (!SceneManager.GetSceneByName(PersistentSceneFlow.DefaultHubSceneName).isLoaded
                && !Application.CanStreamedLevelBeLoaded(PersistentSceneFlow.DefaultHubSceneName)))
            { LastError = "마을 씬을 이동할 수 없습니다."; deathScreen.SetError(true); return; }
            ReleaseDeathPauseAndHud();
            deathScreen.SetReturning(); deathScreen.canvas.enabled = false;
            returnPending = returnRequested = true; outcome = RunOutcome.Failed;
            flow.ReturnToHub(RunSceneReturnContext.CreateHubTransfer(PersistentSceneFlow.DefaultHubSceneName, "Default"));
        }

        private void ReleaseDeathPauseAndHud()
        {
            if (ownsDeathPause) { OverburstTimeEffectArbiter.SetPaused(false); ownsDeathPause = false; }
            foreach (var canvas in deathHiddenCanvases) if (canvas) canvas.enabled = true;
            deathHiddenCanvases.Clear();
        }
        private void ClearDeath()
        {
            ReleaseDeathPauseAndHud();
            if (deathScreen) deathScreen.Hide();
            deathActive = deathSettled = false; deathHealth = null; deathRunId = null;
        }
    }
}
