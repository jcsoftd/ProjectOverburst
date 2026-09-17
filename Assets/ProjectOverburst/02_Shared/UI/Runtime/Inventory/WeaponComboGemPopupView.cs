using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class WeaponComboGemPopupView : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Image weaponIconImage;
    [SerializeField] private TextMeshProUGUI weaponNameText;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Button closeButton;
    [SerializeField] private RectTransform rowContent;
    [SerializeField] private WeaponComboGemRowView rowTemplate;
    [SerializeField] private TextMeshProUGUI detailTitleText;
    [SerializeField] private TextMeshProUGUI emptyDetailText;
    [SerializeField] private RectTransform detailContent;
    [SerializeField] private WeaponComboGemDetailView detailTemplate;

    private readonly List<WeaponComboGemRowView> rows = new List<WeaponComboGemRowView>();
    private readonly List<WeaponComboGemDetailView> details = new List<WeaponComboGemDetailView>();
    private WeaponComboGemPopupViewData currentData;
    private int selectedRowIndex;

    public event Action CloseRequested;
    public event Action<WeaponComboGemSlotDropIntent> SlotDropped;
    public event Action<WeaponComboGemInstalledDragIntent> InstalledDragRequested;

    public bool IsVisible => panelRoot != null && panelRoot.activeSelf;

    private void Awake()
    {
        if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);
        if (detailTemplate != null) detailTemplate.gameObject.SetActive(false);
        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(RequestClose);
            closeButton.onClick.AddListener(RequestClose);
        }
    }

    public void Show(WeaponComboGemPopupViewData data)
    {
        currentData = data;
        if (panelRoot != null) panelRoot.SetActive(true);
        if (weaponNameText != null) weaponNameText.text = data != null ? data.WeaponName : "무기";
        if (titleText != null) titleText.text = "콤보 보석";
        if (weaponIconImage != null)
        {
            weaponIconImage.sprite = data != null ? data.WeaponIcon : null;
            weaponIconImage.color = data != null && data.WeaponIcon != null ? data.WeaponIconColor : Color.clear;
            weaponIconImage.preserveAspect = true;
        }

        int count = data?.Rows != null ? data.Rows.Count : 0;
        selectedRowIndex = count > 0 ? Mathf.Clamp(selectedRowIndex, 0, count - 1) : -1;
        EnsureRowCount(count);
        for (int i = 0; i < rows.Count; i++)
        {
            if (i < count) rows[i].Bind(data.Rows[i], i == selectedRowIndex);
            else rows[i].Clear();
        }
        RefreshDetails();

        bool missing = count > 0 && (rowContent == null || rowTemplate == null || detailContent == null || detailTemplate == null);
        ShowStatus(missing ? "정식 행 또는 설명 템플릿 연결이 필요합니다." : count == 0 ? "표시할 콤보가 없습니다." : string.Empty);
    }

    public void ShowError(string message)
    {
        currentData = null;
        if (panelRoot != null) panelRoot.SetActive(true);
        if (weaponNameText != null) weaponNameText.text = "무기";
        if (titleText != null) titleText.text = "콤보 보석";
        for (int i = 0; i < rows.Count; i++) rows[i]?.Clear();
        RefreshDetails();
        ShowStatus(string.IsNullOrWhiteSpace(message) ? "콤보 정보를 표시할 수 없습니다." : message);
    }

    public void Hide()
    {
        currentData = null;
        selectedRowIndex = 0;
        for (int i = 0; i < rows.Count; i++) rows[i]?.Clear();
        for (int i = 0; i < details.Count; i++)
            if (details[i] != null) details[i].gameObject.SetActive(false);
        if (emptyDetailText != null) emptyDetailText.gameObject.SetActive(false);
        ShowStatus(string.Empty);
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void ShowStatus(string message)
    {
        if (statusText == null) return;
        statusText.text = message ?? string.Empty;
        statusText.gameObject.SetActive(!string.IsNullOrWhiteSpace(message));
    }

    private void SelectRow(int index)
    {
        if (currentData?.Rows == null || index < 0 || index >= currentData.Rows.Count)
            return;
        selectedRowIndex = index;
        for (int i = 0; i < rows.Count; i++) rows[i]?.SetSelected(i == selectedRowIndex);
        RefreshDetails();
    }

    private void RefreshDetails()
    {
        WeaponComboGemRowViewData row = currentData?.Rows != null && selectedRowIndex >= 0 && selectedRowIndex < currentData.Rows.Count
            ? currentData.Rows[selectedRowIndex]
            : null;
        if (detailTitleText != null)
            detailTitleText.text = row != null ? (row.ComboStepIndex + 1) + " HIT 장착 보석" : "장착 보석";

        int occupiedCount = 0;
        if (row?.Slots != null)
        {
            for (int i = 0; i < row.Slots.Count; i++)
                if (row.Slots[i] != null && row.Slots[i].IsUnlocked && row.Slots[i].IsOccupied) occupiedCount++;
        }

        EnsureDetailCount(occupiedCount);
        int detailIndex = 0;
        if (row?.Slots != null)
        {
            for (int i = 0; i < row.Slots.Count; i++)
            {
                WeaponComboGemSlotViewData slot = row.Slots[i];
                if (slot == null || !slot.IsUnlocked || !slot.IsOccupied) continue;
                details[detailIndex++].Bind(slot); // slotIndex 순서 유지
            }
        }
        for (int i = detailIndex; i < details.Count; i++) details[i].gameObject.SetActive(false);
        if (emptyDetailText != null)
        {
            emptyDetailText.gameObject.SetActive(row != null && occupiedCount == 0);
            emptyDetailText.text = "장착된 보석 없음\n무속성 물리 공격";
        }
    }

    private void EnsureRowCount(int required)
    {
        if (rowContent == null || rowTemplate == null) return;
        while (rows.Count < required)
        {
            WeaponComboGemRowView row = Instantiate(rowTemplate, rowContent);
            row.name = "ComboRow_" + (rows.Count + 1).ToString("00");
            row.gameObject.SetActive(false);
            row.Selected += SelectRow;
            row.SlotDropped += HandleSlotDropped;
            row.InstalledDragRequested += HandleInstalledDragRequested;
            rows.Add(row);
        }
    }

    private void EnsureDetailCount(int required)
    {
        if (detailContent == null || detailTemplate == null) return;
        while (details.Count < required)
        {
            WeaponComboGemDetailView detail = Instantiate(detailTemplate, detailContent);
            detail.name = "GemDetail_" + (details.Count + 1).ToString("00");
            details.Add(detail);
        }
    }

    private void HandleSlotDropped(WeaponComboGemSlotDropIntent intent) => SlotDropped?.Invoke(intent);
    private void HandleInstalledDragRequested(WeaponComboGemInstalledDragIntent intent) => InstalledDragRequested?.Invoke(intent);
    private void RequestClose() => CloseRequested?.Invoke();
}
