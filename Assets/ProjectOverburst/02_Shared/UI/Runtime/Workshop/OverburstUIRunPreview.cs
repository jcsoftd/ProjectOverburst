using TMPro;
using UnityEngine;

/// <summary>Management-scene samples rendered by the same Resources run UI used in the dungeon.</summary>
[DisallowMultipleComponent]
public sealed class OverburstUIRunPreview : MonoBehaviour
{
    private const string TransferSampleId = "workshop-transfer-sample";

    private OverburstRunUi runUi;
    private AudioListener temporaryAudioListener;
    private OverburstUIWindow previewInventory;
    private OverburstUIWindow previewEquipment;
    private bool showing;

    public bool IsOpen => runUi != null && runUi.IsOpen;
    public OverburstRunUi RunUi => runUi;

    public void ShowCards()
    {
        if (!EnsureUi()) return;

        var cards = new[]
        {
            new RunCardPresentation
            {
                Title = "생명의 각인", Description = "이번 던전 동안 유지됩니다.", Value = "최대 체력 +10%",
                Icon = RunCardIconSet.Resolve("buff_MaxHealth"), Grade = ItemGrade.Rare
            },
            new RunCardPresentation
            {
                Title = "강철의 각인", Description = "이번 던전 동안 유지됩니다.", Value = "방어력 +12%",
                Icon = RunCardIconSet.Resolve("buff_Armor"), Grade = ItemGrade.Epic
            },
            new RunCardPresentation
            {
                Title = "안전한 전송", Description = "전송 오브젝트를 생성합니다.", Value = "선택 즉시 생성",
                Icon = RunCardIconSet.Resolve("transfer_object"), Grade = ItemGrade.Common, IsReward = true
            }
        };
        runUi.ShowCards(cards, _ => true);
        TMP_Text subtitle = runUi.transform.Find("Modal/CardChoices/Subtitle")?.GetComponent<TMP_Text>();
        if (subtitle != null) subtitle.text = "관리씬 전시 표본 · 선택은 계정에 반영되지 않습니다";
        showing = true;
    }

    public void ShowTransfer(OverburstUIWindow inventory, OverburstUIWindow equipment)
    {
        if (!EnsureUi()) return;

        previewInventory = inventory;
        previewEquipment = equipment;
        if (inventory != null)
        {
            inventory.ResetPosition();
            inventory.WindowRect.anchoredPosition = new Vector2(612f, 64f);
            inventory.Show();
        }
        if (equipment != null)
        {
            equipment.ResetPosition();
            equipment.WindowRect.anchoredPosition = new Vector2(-94f, 64f);
            equipment.Show();
        }

        var gear = Resources.Load<GearItemData>("Items/Gear/Gear_Helmet");
        var sample = new RunTransferPresentation
        {
            ItemInstanceId = TransferSampleId,
            Name = gear != null ? gear.itemName : "전시용 투구",
            Description = "관리씬 전시 표본",
            Icon = gear != null ? gear.icon : null,
            Grade = ItemGrade.Rare,
            Quantity = 2,
            IsEquipped = true
        };
        runUi.ShowTransfer(new[] { sample }, _ => RunTransferResult.NoSpace);
        runUi.TryStageTransfer(TransferSampleId);

        RectTransform transfer = runUi.transform.Find("Modal/SmallTransfer") as RectTransform;
        if (transfer != null) transfer.anchoredPosition = new Vector2(-726f, 64f);
        TMP_Text hint = runUi.transform.Find("Modal/SmallTransfer/Hint")?.GetComponent<TMP_Text>();
        if (hint != null) hint.text = "관리씬 전시 표본 · 실제 아이템 전송 없음";
        showing = true;
    }

    public void Hide()
    {
        showing = false;
        if (runUi != null) runUi.Close();
        previewInventory?.ResetPosition();
        previewEquipment?.ResetPosition();
        previewInventory = null;
        previewEquipment = null;
    }

    private bool EnsureUi()
    {
        if (!Application.isPlaying) return false;
        if (FindFirstObjectByType<AudioListener>() == null && temporaryAudioListener == null)
        {
            Camera previewCamera = FindFirstObjectByType<Camera>();
            if (previewCamera != null)
                temporaryAudioListener = previewCamera.gameObject.AddComponent<AudioListener>();
        }
        if (runUi == null)
        {
            runUi = OverburstRunUi.Create();
            runUi.name = "Run UI • Workshop Preview";
        }
        return true;
    }

    private void LateUpdate()
    {
        if (!showing || IsOpen) return;
        showing = false;
        GetComponent<OverburstUIWorkshop>()?.ShowHud();
    }

    private void OnDisable() => Hide();

    private void OnDestroy()
    {
        Hide();
        if (runUi != null) Destroy(runUi.gameObject);
        if (temporaryAudioListener != null) Destroy(temporaryAudioListener);
    }
}
