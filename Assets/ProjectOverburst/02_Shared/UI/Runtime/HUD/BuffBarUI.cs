using System.Collections.Generic;
using TMPro;
using UnityEngine;

[DefaultExecutionOrder(10003)]
public sealed class BuffBarUI : MonoBehaviour
{
    private const int MaxVisibleBuffs = 5;

    [SerializeField] private PlayerBuffController buffController;
    [SerializeField] private BuffIconSlotUI[] slots = new BuffIconSlotUI[MaxVisibleBuffs];
    [SerializeField] private TextMeshProUGUI moreIndicator;
    [SerializeField] private bool autoResolveReferences = true;

    private readonly List<BuffInstance> activeBuffs = new List<BuffInstance>(MaxVisibleBuffs + 4);
    private PlayerBuffController subscribedController;

    private void Awake()
    {
        BindVisuals();
    }

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
        Refresh();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        if (autoResolveReferences && buffController == null)
        {
            ResolveReferences();
            Subscribe();
        }

        Refresh();
    }

    private void HandleBuffsChanged()
    {
        Refresh();
    }

    private void Refresh()
    {
        BindVisuals();

        if (buffController == null)
        {
            HideAll();
            return;
        }

        buffController.GetActiveBuffs(activeBuffs);
        activeBuffs.Sort(CompareRemainingTimeAscending);

        int slotCount = slots != null ? Mathf.Min(slots.Length, MaxVisibleBuffs) : 0;
        for (int i = 0; i < slotCount; i++)
        {
            BuffIconSlotUI slot = slots[i];
            if (slot == null)
                continue;

            if (i < activeBuffs.Count)
                slot.SetBuff(activeBuffs[i]);
            else
                slot.SetVisible(false);
        }

        for (int i = slotCount; i < MaxVisibleBuffs; i++)
        {
            BuffIconSlotUI slot = slots != null && i < slots.Length ? slots[i] : null;
            if (slot != null)
                slot.SetVisible(false);
        }

        if (moreIndicator != null)
            moreIndicator.gameObject.SetActive(activeBuffs.Count > MaxVisibleBuffs);
    }

    private void HideAll()
    {
        if (slots != null)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                    slots[i].SetVisible(false);
            }
        }

        if (moreIndicator != null)
            moreIndicator.gameObject.SetActive(false);
    }

    private void BindVisuals()
    {
        if (slots == null || slots.Length != MaxVisibleBuffs)
            slots = new BuffIconSlotUI[MaxVisibleBuffs];

        for (int i = 0; i < MaxVisibleBuffs; i++)
        {
            if (slots[i] != null)
                continue;

            string childName = "BuffIconSlot_" + (i + 1).ToString("00");
            Transform child = transform.Find(childName);
            slots[i] = child != null ? child.GetComponent<BuffIconSlotUI>() : null;
        }

        if (moreIndicator == null)
        {
            Transform more = transform.Find("MoreIndicator");
            moreIndicator = more != null ? more.GetComponent<TextMeshProUGUI>() : null;
        }
    }

    private void ResolveReferences()
    {
        if (buffController != null)
            return;

        buffController = FindFirstObjectByType<PlayerBuffController>(FindObjectsInactive.Include);
    }

    private void Subscribe()
    {
        if (subscribedController == buffController)
            return;

        Unsubscribe();
        if (buffController == null)
            return;

        buffController.BuffsChanged += HandleBuffsChanged;
        subscribedController = buffController;
    }

    private void Unsubscribe()
    {
        if (subscribedController == null)
            return;

        subscribedController.BuffsChanged -= HandleBuffsChanged;
        subscribedController = null;
    }

    private static int CompareRemainingTimeAscending(BuffInstance left, BuffInstance right)
    {
        float leftTime = left != null ? left.RemainingTime : float.MaxValue;
        float rightTime = right != null ? right.RemainingTime : float.MaxValue;
        return leftTime.CompareTo(rightTime);
    }
}
