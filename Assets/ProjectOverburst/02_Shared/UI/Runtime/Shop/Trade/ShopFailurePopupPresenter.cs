using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ShopFailurePopupView
{
    public GameObject Root;
    public TextMeshProUGUI MessageText;
    public Button PopupConfirmButton;
    public Button TradeConfirmButton;
}

public sealed class ShopFailurePopupPresenter
{
    public string GetMessage(string message)
    {
        return string.IsNullOrWhiteSpace(message) ? "거래 중 오류가 발생했습니다." : message;
    }

    public bool Show(ShopFailurePopupView view, string message, out string fallbackStatus)
    {
        fallbackStatus = GetMessage(message);
        if (view == null || view.Root == null)
            return false;

        if (view.MessageText != null)
            view.MessageText.text = fallbackStatus;

        view.Root.SetActive(true);
        view.Root.transform.SetAsLastSibling();

        if (view.TradeConfirmButton != null)
            view.TradeConfirmButton.interactable = false;

        return true;
    }

    public void Hide(ShopFailurePopupView view)
    {
        if (view == null)
            return;

        if (view.Root != null)
            view.Root.SetActive(false);

        if (view.TradeConfirmButton != null)
            view.TradeConfirmButton.interactable = true;
    }
}
