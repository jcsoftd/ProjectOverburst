using System;
using System.IO;
using UnityEngine;

namespace Overburst.Persistence
{
    [DisallowMultipleComponent]
    public sealed class RunLifetimeDriver : MonoBehaviour
    {
        private bool portalRequested;
        private bool abandoned;
        private bool returnPending;
        private bool returnRequested;
        private RunOutcome outcome;
        private float nextPoll;
        public string LastError { get; private set; }

        private void OnEnable() => WorldSessionState.Changed += WorldChanged;
        private void OnDisable()
        {
            WorldSessionState.Changed -= WorldChanged;
            GameplayInputBlocker.Unblock(this);
        }

        public bool RequestPortalExit()
        {
            if (AccountGameplaySession.Current?.ReadRun()?.phase != RunPhase.BossCleared) return false;
            portalRequested = true;
            return true;
        }

        public void RequestAbandon()
        {
            var run = AccountGameplaySession.Current?.ReadRun();
            if (run != null && run.phase != RunPhase.Failed && run.phase != RunPhase.Extracted)
                abandoned = true;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + .25f;
            Poll(DateTime.UtcNow.Ticks);
        }

        public void Poll(long utcTicks)
        {
            var account = AccountGameplaySession.Current;
            if (account == null) return;
            if (returnPending) { TryReturn(); return; }
            if (WorldSessionState.Phase != WorldPhase.Run && !abandoned) return;
            var health = PlayerContext.Instance?.CurrentActorHealth;
            if (health == null) return;
            try
            {
                outcome = new AccountRunSession(account).ResolveOutcome(utcTicks, health.IsDead, portalRequested, abandoned);
                if (outcome == RunOutcome.None) return;
                LastError = null;
                portalRequested = abandoned = false;
                returnPending = true;
                GameplayInputBlocker.Block(this);
                TryReturn();
            }
            catch (IOException error)
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
                ? RunSceneReturnContext.CreateExtractSuccess(PersistentSceneFlow.HideoutSceneName, "Default")
                : RunSceneReturnContext.CreateHubTransfer(PersistentSceneFlow.HideoutSceneName, "Default"));
        }

        private void WorldChanged(WorldPhase phase)
        {
            if (phase != WorldPhase.Hideout || !returnPending) return;
            PlayerContext.Instance?.CurrentActorHealth?.ResetHealth();
            returnPending = returnRequested = false;
            outcome = RunOutcome.None;
            GameplayInputBlocker.Unblock(this);
        }
    }
}
