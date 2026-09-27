using System;
using UnityEngine;

namespace Overburst.Persistence
{
    // Runtime transactions publish immediately; disk persistence is coalesced every five unscaled seconds.
    [DefaultExecutionOrder(10000)]
    public sealed class AccountAutosave : MonoBehaviour
    {
        public const float IntervalSeconds = 5f;
        private AccountGameplaySession session;
        private float nextSave;
        private bool flushing;
        public string LastError { get; private set; }
        public int SuccessfulWrites { get; private set; }

        public static AccountAutosave StartFor(AccountGameplaySession session)
        {
            var root = new GameObject("Account autosave");
            DontDestroyOnLoad(root);
            var saver = root.AddComponent<AccountAutosave>();
            saver.session = session;
            saver.nextSave = Time.unscaledTime + IntervalSeconds;
            return saver;
        }
        private void LateUpdate()
        {
            if (Time.unscaledTime < nextSave) return;
            nextSave = Time.unscaledTime + IntervalSeconds;
            FlushNow();
        }
        public bool FlushNow()
        {
            if (flushing || session == null || AccountGameplaySession.Current != session || session.IsEditing) return false;
            flushing = true;
            try
            {
                CurrencyPickupBatch.DrainPending();
                if (PlayerProgression.Current != null && !PlayerProgression.Current.FlushPendingExperience()) return false;
                bool pending = session.HasPendingSave;
                bool saved = session.FlushPendingSave();
                if (saved)
                {
                    if (pending) SuccessfulWrites++;
                    LastError = null;
                }
                return saved;
            }
            catch (Exception error)
            {
                if (LastError != error.Message) Debug.LogWarning("자동 저장 실패. 획득 상태를 유지하고 다음 주기에 재시도합니다: " + error.Message);
                LastError = error.Message;
                return false;
            }
            finally { flushing = false; }
        }
        private void OnApplicationPause(bool paused) { if (paused) FlushNow(); }
        private void OnApplicationQuit() { FlushNow(); }
    }
}
