using System;
using System.Collections.Generic;
using Overburst.Persistence;
using UnityEngine;

namespace Overburst.Quests
{
    [DefaultExecutionOrder(9000)]
    public sealed class QuestSession : MonoBehaviour
    {
        private struct KillBatch { public string runId; public long revision; public int count; }
        private readonly List<KillBatch> pending = new List<KillBatch>();
        private AccountGameplaySession session;
        private bool draining;
        public static QuestSession Current { get; private set; }
        public string LastError { get; private set; }
        public event Action Changed;
        public bool IsBoundTo(AccountGameplaySession account) => ReferenceEquals(session, account);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Current = null;
        public void Bind(AccountGameplaySession account)
        {
            if (session != null) session.QuestsCommitted -= OnCommitted;
            session = account; Current = this;
            session.QuestsCommitted += OnCommitted;
        }
        private void OnDestroy()
        {
            if (session != null) session.QuestsCommitted -= OnCommitted;
            if (Current == this) Current = null;
        }
        private void OnCommitted() => Changed?.Invoke();
        private void LateUpdate() { if (pending.Count > 0) DrainPending(); }

        // EnemyRank already owns source-player/reward eligibility and its per-lease death guard.
        public static void RecordConfirmedKill(EncounterContext encounter)
        {
            var runtime = Current;
            if (runtime == null || runtime.session == null || encounter == null || !encounter.IsRun
                || WorldSessionState.Phase != WorldPhase.Run || !encounter.CanGrantRewards) return;
            var run = runtime.session.ReadRun();
            if (run == null || run.runId != encounter.RunId || !AccountInvariants.IsRunning(run.phase)) return;
            bool needed = false;
            foreach (var q in runtime.session.ReadQuests())
                if (q.state == SavedQuestState.Active)
                    foreach (var o in q.objectives)
                        if (o.kind == QuestObjectiveKind.KillEnemy && o.targetId == "enemy.any-run" && o.current < o.required) needed = true;
            if (!needed) return;
            int last = runtime.pending.Count - 1;
            if (last >= 0 && runtime.pending[last].runId == encounter.RunId && runtime.pending[last].revision == runtime.session.Revision)
            {
                var batch = runtime.pending[last]; batch.count = checked(batch.count + 1); runtime.pending[last] = batch;
            }
            else runtime.pending.Add(new KillBatch { runId = encounter.RunId, revision = runtime.session.Revision, count = 1 });
        }

        public bool DrainPending(bool checkpointReady = true)
        {
            if (draining || session == null || session.IsEditing || session.NeedsProjectionRecovery) return false;
            draining = true;
            try
            {
                while (pending.Count > 0)
                {
                    var batch = pending[0];
                    session.ExecuteQuest(Guid.NewGuid().ToString("N"), session.Revision,
                        state => AccountQuestCommands.Progress(state, QuestObjectiveKind.KillEnemy, "enemy.any-run",
                            batch.count, batch.runId, batch.revision), false);
                    pending.RemoveAt(0);
                }
                if (checkpointReady && session.ReadQuests().Exists(AccountQuestCommands.IsReady) && session.HasPendingSave && !session.FlushPendingSave())
                    return false;
                LastError = null;
                return true;
            }
            catch (Exception error)
            {
                if (LastError != error.Message) Debug.LogWarning("퀘스트 진행 저장을 보류했습니다: " + error.Message);
                LastError = error.Message; return false;
            }
            finally { draining = false; }
        }

        private bool PrepareCommand()
        {
            if (session == null || AccountGameplaySession.Current != session || session.IsEditing || session.NeedsProjectionRecovery) return false;
            CurrencyPickupBatch.DrainPending();
            if (PlayerProgression.Current != null && !PlayerProgression.Current.FlushPendingExperience()) return false;
            return DrainPending();
        }

        public bool Accept(string questId, string merchantId) => Execute(() =>
        {
            RequireTown();
            if (!PrepareCommand()) return false;
            var definition = QuestCatalog.Find(questId) ?? throw new InvalidOperationException("의뢰 정보를 찾을 수 없습니다.");
            return session.ExecuteQuest(Guid.NewGuid().ToString("N"), session.Revision,
                state => AccountQuestCommands.Accept(state, definition, merchantId, session.ContentRegistry), true);
        });
        public bool TurnIn(string questId, string merchantId) => Execute(() =>
        {
            RequireTown();
            if (!PrepareCommand()) return false;
            return session.ExecuteQuest(Guid.NewGuid().ToString("N"), session.Revision,
                state => AccountQuestCommands.TurnIn(state, questId, merchantId, session.ContentRegistry), true, true);
        });
        public bool Track(string questId) => Execute(() =>
        {
            if (!PrepareCommand()) return false;
            return session.ExecuteQuest(Guid.NewGuid().ToString("N"), session.Revision,
                state => AccountQuestCommands.Track(state, questId), true);
        });
        public bool RecordTalk(string merchantId) => Execute(() =>
        {
            if (!PrepareCommand()) return false;
            return session.ExecuteQuest(Guid.NewGuid().ToString("N"), session.Revision,
                state => AccountQuestCommands.Progress(state, QuestObjectiveKind.TalkNpc, merchantId, 1), true);
        });
        public bool RecordMapSelected(string itemId) => Execute(() =>
        {
            RequireTown();
            if (!PrepareCommand()) return false;
            return session.ExecuteQuest(Guid.NewGuid().ToString("N"), session.Revision, state =>
            {
                if (!state.inventory.Contains(itemId) || state.items.Find(i => i.instanceId == itemId)?.map == null) return false;
                return AccountQuestCommands.Progress(state, QuestObjectiveKind.SelectMap, "portal.map", 1);
            }, false);
        });
        private bool Execute(Func<bool> command)
        {
            try { bool result = command(); if (result) LastError = null; return result; }
            catch (Exception error) { LastError = error.Message; return false; }
        }
        private static void RequireTown()
        {
            if (!WorldSessionState.IsHideout || PersistentSceneFlow.Instance?.IsSwitching == true)
                throw new InvalidOperationException("마을에서 상인에게 의뢰를 받거나 보고할 수 있습니다.");
        }
    }
}
