using Overburst.Persistence;
using TMPro;
using UnityEngine;

namespace Overburst.Quests
{
    public sealed class NpcQuestMarker : MonoBehaviour
    {
        [SerializeField] private TextMeshPro marker;
        private GeneralGoodsMerchantInteractable merchant;
        private AccountGameplaySession shownAccount;
        private long shownRevision = -1;
        private static readonly Color Gold = new Color32(211, 166, 102, 255);
        private static readonly Color Gray = new Color32(150, 145, 133, 255);

        private void OnEnable() { shownAccount = null; shownRevision = -1; }
        private void OnDisable() { if (marker) marker.gameObject.SetActive(false); }
        private void LateUpdate()
        {
            if (!merchant) merchant = GetComponentInParent<GeneralGoodsMerchantInteractable>();
            var account = AccountGameplaySession.Current;
            if (!merchant || !merchant.isActiveAndEnabled || !account?.Owner
                || !WorldSessionState.IsHideout || PersistentSceneFlow.Instance?.IsSwitching == true)
            {
                if (marker) marker.gameObject.SetActive(false);
                shownAccount = null; shownRevision = -1;
                return;
            }
            if (shownAccount == account && shownRevision == account.Revision) return;
            shownAccount = account; shownRevision = account.Revision;
            Present(account.Read());
        }

        public void Present(AccountSnapshot state)
        {
            if (!marker) return;
            if (!merchant) merchant = GetComponentInParent<GeneralGoodsMerchantInteractable>();
            var status = QuestAvailability.Locked;
            if (state != null && merchant && merchant.Definition)
            {
                string id = MerchantReputationService.GetStableMerchantId(merchant.Definition);
                foreach (var definition in QuestCatalog.All)
                {
                    if (definition.giverMerchantId != id) continue;
                    var next = AccountQuestCommands.Availability(state, definition);
                    if (next == QuestAvailability.ReadyToReport) { status = next; break; }
                    if (next == QuestAvailability.Available || (next == QuestAvailability.Active && status != QuestAvailability.Available))
                        status = next;
                }
            }
            bool visible = status == QuestAvailability.Available || status == QuestAvailability.Active || status == QuestAvailability.ReadyToReport;
            marker.text = status == QuestAvailability.Available ? "!" : "?";
            marker.color = status == QuestAvailability.Active ? Gray : Gold;
            marker.gameObject.SetActive(visible);
        }
    }
}
