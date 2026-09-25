using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

public class StashCurrencySummaryUI : MonoBehaviour
{
    [SerializeField] private StashCurrencyService currencyService;
    [SerializeField] private TextMeshProUGUI goldText;
    [SerializeField] private TextMeshProUGUI mapFragmentText;

    private PlayerStash subscribedStash;

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

    public void SetCurrencyService(StashCurrencyService service)
    {
        if (currencyService == service)
            return;

        Unsubscribe();
        currencyService = service;
        Subscribe();
        Refresh();
    }

    public void Refresh()
    {
        ResolveReferences();
        Subscribe();

        SetText(goldText, "골드", CurrencyType.Gold);
        SetText(mapFragmentText, "지도조각", CurrencyType.MapFragment);
    }

    private void SetText(TextMeshProUGUI target, string label, CurrencyType type)
    {
        if (target == null)
            return;

        int amount = currencyService != null ? currencyService.GetAmount(type) : 0;
        target.text = label + " : " + amount;
    }

    private void ResolveReferences()
    {
        if (currencyService == null)
            currencyService = FindFirstObjectByType<StashCurrencyService>(FindObjectsInactive.Include);
    }

    private void Subscribe()
    {
        PlayerStash stash = currencyService != null ? currencyService.Stash : null;
        if (subscribedStash == stash)
            return;

        Unsubscribe();
        subscribedStash = stash;

        if (subscribedStash != null)
        {
            subscribedStash.Changed += Refresh;
            subscribedStash.CurrentTabChanged += Refresh;
        }
    }

    private void Unsubscribe()
    {
        if (subscribedStash == null)
            return;

        subscribedStash.Changed -= Refresh;
        subscribedStash.CurrentTabChanged -= Refresh;
        subscribedStash = null;
    }
}
