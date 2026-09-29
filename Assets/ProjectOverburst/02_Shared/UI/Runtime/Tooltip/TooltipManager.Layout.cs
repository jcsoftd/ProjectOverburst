using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// TooltipManager partial: 크기 계산·포인터 추적·화면 밖 보정. 필드와 Unity 수명주기는 TooltipManager.cs에 있다.
public partial class TooltipManager
{
    private void SetActive(Component component, bool active)
    {
        if (component != null)
            component.gameObject.SetActive(active);
    }

    private void SetActive(GameObject gameObject, bool active)
    {
        if (gameObject != null)
            gameObject.SetActive(active);
    }

    private void RebuildTooltipLayout()
    {
        if (tooltipRect == null)
            return;

        if (currentItem != null)
            tooltipRect.sizeDelta = new Vector2(CalculateResponsiveWidth(currentItem), tooltipRect.sizeDelta.y);

        if (weaponHeaderRoot != null && weaponHeaderRoot.gameObject.activeSelf)
            LayoutRebuilder.ForceRebuildLayoutImmediate(weaponHeaderRoot);

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipRect);
        Canvas.ForceUpdateCanvases(); // ContentSizeFitter 최종 크기 확정
    }

    private float CalculateResponsiveWidth(ItemData item)
    {
        int maxLineLength = 0;
        UpdateMaxLineLength(ref maxLineLength, nameText);
        UpdateMaxLineLength(ref maxLineLength, weaponNameText);
        UpdateMaxLineLength(ref maxLineLength, basicStatsText);
        UpdateMaxLineLength(ref maxLineLength, weaponStatsText);
        UpdateMaxLineLength(ref maxLineLength, priceText);

        float textWidth = VtpPanelWidth + Mathf.Max(0, maxLineLength - 16) * 5.6f; // 글자 폭
        float meleeWidth = meleeStatListView != null && meleeStatListView.gameObject.activeSelf
            ? meleeStatListView.PreferredContentWidth + 20f
            : 0f;
        return Mathf.Clamp(Mathf.Max(VtpPanelWidth, textWidth, meleeWidth), VtpPanelWidth, ResponsiveMaxPanelWidth);
    }

    private void UpdateMaxLineLength(ref int maxLineLength, TextMeshProUGUI text)
    {
        if (text == null || string.IsNullOrEmpty(text.text))
            return;

        string plainText = StripRichText(text.text); // 태그 제거
        string[] lines = plainText.Split('\n');

        for (int i = 0; i < lines.Length; i++)
            maxLineLength = Mathf.Max(maxLineLength, lines[i].Length);
    }

    private string StripRichText(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length);
        bool insideTag = false; // rich tag 내부

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];

            if (c == '<')
            {
                insideTag = true;
                continue;
            }

            if (c == '>')
            {
                insideTag = false;
                continue;
            }

            if (!insideTag)
                builder.Append(c);
        }

        return builder.ToString();
    }

    private void ConfigureRaycastBlocking()
    {
        if (tooltipPanel == null)
            return;

        tooltipCanvasGroup = tooltipPanel.GetComponent<CanvasGroup>();
        if (tooltipCanvasGroup == null)
            return;

        tooltipCanvasGroup.blocksRaycasts = false;
        tooltipCanvasGroup.interactable = false;

        Graphic[] graphics = tooltipPanel.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
            graphics[i].raycastTarget = false;
    }

    private static Vector2 ReadPointerScreenPosition()
    {
        // GOAL A2: Mouse 직접 읽기 대신 UI Point 포인터 위치를 사용한다. 마우스 없으면 zero 폴백 유지.
        PlayerInputFacade facade = PlayerInputFacade.Current;
        return facade != null ? facade.PointerPosition : Vector2.zero;
    }

    private void UpdateTooltipPosition(Vector2 pointerScreenPosition)
    {
        if (tooltipRect == null)
            return;

        if (canvas == null)
            canvas = GetComponentInParent<Canvas>(true);

        RectTransform parentRect = tooltipRect.parent as RectTransform;
        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
        Vector2 desiredScreenPosition = pointerScreenPosition + tooltipPointerOffset;

        if (parentRect != null
            && RectTransformUtility.ScreenPointToWorldPointInRectangle(
                parentRect,
                desiredScreenPosition,
                eventCamera,
                out Vector3 desiredWorldPosition))
        {
            tooltipRect.position = desiredWorldPosition;
        }
        else
        {
            tooltipRect.position = desiredScreenPosition;
        }

        ClampTooltipToScreen(parentRect, eventCamera);
    }

    private void ClampTooltipToScreen(RectTransform parentRect, Camera eventCamera)
    {
        tooltipRect.GetWorldCorners(tooltipWorldCorners); // 최종 레이아웃 실제 코너
        Vector2 firstCorner = RectTransformUtility.WorldToScreenPoint(eventCamera, tooltipWorldCorners[0]);
        float minX = firstCorner.x;
        float maxX = firstCorner.x;
        float minY = firstCorner.y;
        float maxY = firstCorner.y;

        for (int i = 1; i < tooltipWorldCorners.Length; i++)
        {
            Vector2 screenCorner = RectTransformUtility.WorldToScreenPoint(eventCamera, tooltipWorldCorners[i]);
            minX = Mathf.Min(minX, screenCorner.x);
            maxX = Mathf.Max(maxX, screenCorner.x);
            minY = Mathf.Min(minY, screenCorner.y);
            maxY = Mathf.Max(maxY, screenCorner.y);
        }

        Vector2 correction = new Vector2(
            CalculateScreenCorrection(minX, maxX, Screen.width),
            CalculateScreenCorrection(minY, maxY, Screen.height));
        if (correction.sqrMagnitude <= 0.0001f)
            return;

        Vector2 anchorScreenPosition = RectTransformUtility.WorldToScreenPoint(eventCamera, tooltipRect.position);
        Vector2 correctedScreenPosition = anchorScreenPosition + correction;
        if (parentRect != null
            && RectTransformUtility.ScreenPointToWorldPointInRectangle(
                parentRect,
                correctedScreenPosition,
                eventCamera,
                out Vector3 correctedWorldPosition))
        {
            tooltipRect.position = correctedWorldPosition;
        }
        else
        {
            tooltipRect.position += new Vector3(correction.x, correction.y, 0f);
        }
    }

    private static float CalculateScreenCorrection(float minimum, float maximum, float screenSize)
    {
        float availableSize = Mathf.Max(0f, screenSize - ScreenEdgePadding * 2f);
        float contentSize = maximum - minimum;
        if (contentSize >= availableSize)
            return screenSize * 0.5f - (minimum + maximum) * 0.5f;

        if (minimum < ScreenEdgePadding)
            return ScreenEdgePadding - minimum;
        if (maximum > screenSize - ScreenEdgePadding)
            return screenSize - ScreenEdgePadding - maximum;
        return 0f;
    }
}
