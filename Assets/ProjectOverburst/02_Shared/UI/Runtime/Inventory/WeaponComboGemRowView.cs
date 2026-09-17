using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class WeaponComboGemRowView : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private TextMeshProUGUI comboLabel;
    [SerializeField] private Image selectionFrame;
    [SerializeField] private WeaponComboGemSlotView[] slots = new WeaponComboGemSlotView[WeaponComboGemLoadout.SlotCapacity];

    private int comboStepIndex = -1;

    public event Action<int> Selected;
    public event Action<WeaponComboGemSlotDropIntent> SlotDropped;
    public event Action<WeaponComboGemInstalledDragIntent> InstalledDragRequested;

    private void Awake()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null)
                continue;
            slots[i].Dropped += HandleSlotDropped;
            slots[i].InstalledDragRequested += HandleInstalledDragRequested;
        }
    }

    private void OnDestroy()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null)
                continue;
            slots[i].Dropped -= HandleSlotDropped;
            slots[i].InstalledDragRequested -= HandleInstalledDragRequested;
        }
    }

    public void Bind(WeaponComboGemRowViewData data, bool selected)
    {
        if (data == null)
        {
            Clear();
            return;
        }

        gameObject.SetActive(true);
        comboStepIndex = data.ComboStepIndex;
        if (comboLabel != null)
            comboLabel.text = (comboStepIndex + 1) + " HIT";
        if (selectionFrame != null)
            selectionFrame.gameObject.SetActive(selected);

        for (int i = 0; i < slots.Length; i++)
        {
            WeaponComboGemSlotViewData slotData = data.Slots != null && i < data.Slots.Count ? data.Slots[i] : null;
            slots[i]?.Bind(data.AttackId, slotData);
        }
    }

    public void SetSelected(bool selected)
    {
        if (selectionFrame != null)
            selectionFrame.gameObject.SetActive(selected);
    }

    public void Clear()
    {
        comboStepIndex = -1;
        for (int i = 0; i < slots.Length; i++)
            slots[i]?.Clear();
        gameObject.SetActive(false);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
            return;
        if (comboStepIndex >= 0)
            Selected?.Invoke(comboStepIndex);
    }

    private void HandleSlotDropped(WeaponComboGemSlotDropIntent intent)
    {
        if (comboStepIndex >= 0)
            Selected?.Invoke(comboStepIndex);
        SlotDropped?.Invoke(intent);
    }

    private void HandleInstalledDragRequested(WeaponComboGemInstalledDragIntent intent)
    {
        if (comboStepIndex >= 0)
            Selected?.Invoke(comboStepIndex);
        InstalledDragRequested?.Invoke(intent);
    }
}
