using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AimPointerUI : MonoBehaviour
{
    private static Sprite solidSprite;
    private static Sprite cooldownRingSprite;

    [Header("References")]
    [SerializeField] private Canvas canvas;
    [SerializeField] private RectTransform root;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform topLine;
    [SerializeField] private RectTransform bottomLine;
    [SerializeField] private RectTransform leftLine;
    [SerializeField] private RectTransform rightLine;
    [SerializeField] private PlayerEquipment playerEquipment;
    [SerializeField] private PlayerMovement playerController;
    [SerializeField] private PlayerContext playerContext;
    [SerializeField] private TextMeshProUGUI magazineText;
    [SerializeField] private Image reloadRing;
    [SerializeField] private bool autoResolveReferences = true;

    [Header("Visibility")]
    [SerializeField] private bool hideWhenGameplayInputBlocked = true;
    [SerializeField] private bool hideWithoutProjectileWeapon = true;

    [Header("Shape")]
    [SerializeField] private float baseGap = 8f;
    [SerializeField] private float maxGap = 60f;
    [SerializeField] private float recoilPixelMultiplier = 4f;
    [SerializeField] private float aimedCrosshairMultiplier = 0.65f;
    [SerializeField] private float quickFireCrosshairMultiplier = 1f;
    [SerializeField] private float lineLength = 10f;
    [SerializeField] private float lineThickness = 2f;
    [SerializeField] private Color lineColor = new Color(0.85f, 0.95f, 1f, 0.92f);
    [SerializeField] private float smoothSpeed = 20f;

    [Header("Magazine")]
    [SerializeField] private Vector2 magazineTextOffset = new Vector2(0f, -34f);
    [SerializeField] private float magazineFontSize = 15f;
    [SerializeField] private Color magazineTextColor = Color.white;

    [Header("Cooldown Ring")]
    [SerializeField] private float reloadRingSize = 46f;
    [SerializeField] private Color reloadRingColor = new Color(0.25f, 0.65f, 1f, 0.85f);
    [SerializeField] private Color fullChargeRingColor = new Color(0.25f, 0.95f, 0.35f, 0.9f);

    private float currentGap;
    private bool missingReferenceWarned;

    private void Awake()
    {
        EnsureUI();
        currentGap = baseGap;
        ResolveReferences();
    }

    private void Update()
    {
        if (autoResolveReferences)
            ResolveReferences();

        RefreshPointer();
    }

    private void EnsureUI()
    {
        if (canvas == null)
            canvas = GetComponentInParent<Canvas>();

        if (root == null)
            root = GetComponent<RectTransform>();

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (topLine == null)
            topLine = FindChildRect("TopLine");
        if (bottomLine == null)
            bottomLine = FindChildRect("BottomLine");
        if (leftLine == null)
            leftLine = FindChildRect("LeftLine");
        if (rightLine == null)
            rightLine = FindChildRect("RightLine");
        if (magazineText == null)
            magazineText = FindChildText("MagazineText");
        if (reloadRing == null)
            reloadRing = FindChildImage("ReloadRing");

        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        if (root != null)
        {
            root.anchorMin = new Vector2(0f, 0f);
            root.anchorMax = new Vector2(0f, 0f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = Vector2.zero;
        }

        if (reloadRing != null)
            reloadRing.transform.SetAsFirstSibling();

        ApplyLineVisuals();
        ApplyMagazineVisuals();
        ApplyReloadRingVisuals();

        if (!HasRequiredSceneReferences())
            WarnMissingConfiguration();
    }

    private void ResolveReferences()
    {
        if (playerContext == null)
            playerContext = PlayerContext.GetOrCreate();

        PlayerEquipment leaderEquipment = playerContext != null ? playerContext.CurrentActorEquipment : null;
        if (leaderEquipment != null)
            playerEquipment = leaderEquipment;

        PlayerMovement leaderMovement = playerContext != null ? playerContext.CurrentActorMovement : null;
        if (leaderMovement != null)
            playerController = leaderMovement;

        if (playerEquipment == null)
            playerEquipment = FindFirstObjectByType<PlayerEquipment>();

        if (playerController == null)
            playerController = FindFirstObjectByType<PlayerMovement>();
    }

    private void RefreshPointer()
    {
        bool visible = CanShowPointer();
        SetVisible(visible);

        if (!visible)
        {
            currentGap = baseGap;
            HideWeaponDetails();
            return;
        }

        FollowMouse();
        UpdateGap();
        LayoutLines();
        RefreshCooldownRing();
        HideMagazineText();
    }

    private bool CanShowPointer()
    {
        if (hideWhenGameplayInputBlocked && GameplayInputBlocker.IsGameplayInputBlocked)
            return false;

        bool hasMagicAimWeapon = playerEquipment != null
            && playerEquipment.CanCurrentWeaponUseMagicCaster
            && playerController != null
            && playerController.IsAiming;
        bool hasMeleeAimWeapon = playerEquipment != null
            && playerEquipment.CanCurrentWeaponUseMeleeSlash
            && playerController != null
            && playerController.IsMeleeCombatStance;

        if (hideWithoutProjectileWeapon && !hasMagicAimWeapon && !hasMeleeAimWeapon)
            return false;

        return true;
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup != null)
            canvasGroup.alpha = visible ? 1f : 0f;
    }

    private void FollowMouse()
    {
        if (root == null)
            return;

        // GOAL A2: Mouse/Input 직접 읽기 대신 UI Point 포인터 위치를 사용한다.
        PlayerInputFacade facade = PlayerInputFacade.Current;
        Vector2 screenPosition = facade != null ? facade.PointerPosition : Vector2.zero;

        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            RectTransform canvasRect = canvas.transform as RectTransform;
            Camera eventCamera = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;

            if (canvasRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPosition, eventCamera, out Vector2 localPoint))
            {
                root.anchoredPosition = localPoint;
                return;
            }
        }

        root.position = screenPosition;
    }

    private void UpdateGap()
    {
        float stateMultiplier = GetCrosshairStateMultiplier();
        float targetGap = Mathf.Clamp(baseGap * stateMultiplier, baseGap * stateMultiplier, maxGap);
        float t = 1f - Mathf.Exp(-Mathf.Max(0f, smoothSpeed) * Time.deltaTime);
        currentGap = Mathf.Lerp(currentGap, targetGap, t);
    }

    private float GetCrosshairStateMultiplier()
    {
        if (playerController == null)
            return 1f;

        if (playerController.IsAiming)
            return Mathf.Clamp(aimedCrosshairMultiplier, 0.1f, 1f);

        if (playerController.IsQuickFiring)
            return Mathf.Max(0.1f, quickFireCrosshairMultiplier);

        return 1f;
    }

    private void LayoutLines()
    {
        if (topLine == null || bottomLine == null || leftLine == null || rightLine == null)
            return;

        ApplyLineVisuals();

        float halfLength = lineLength * 0.5f;
        topLine.anchoredPosition = new Vector2(0f, currentGap + halfLength);
        bottomLine.anchoredPosition = new Vector2(0f, -currentGap - halfLength);
        leftLine.anchoredPosition = new Vector2(-currentGap - halfLength, 0f);
        rightLine.anchoredPosition = new Vector2(currentGap + halfLength, 0f);
    }

    private void ApplyLineVisuals()
    {
        SetLine(topLine, new Vector2(lineThickness, lineLength));
        SetLine(bottomLine, new Vector2(lineThickness, lineLength));
        SetLine(leftLine, new Vector2(lineLength, lineThickness));
        SetLine(rightLine, new Vector2(lineLength, lineThickness));
    }

    private void ApplyMagazineVisuals()
    {
        if (magazineText == null)
            return;

        RectTransform rect = magazineText.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(92f, 24f);
        rect.anchoredPosition = magazineTextOffset;
        magazineText.fontSize = magazineFontSize;
        magazineText.alignment = TextAlignmentOptions.Center;
        magazineText.color = magazineTextColor;
        HideMagazineText();
    }

    private void ApplyReloadRingVisuals()
    {
        if (reloadRing == null)
            return;

        RectTransform rect = reloadRing.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(reloadRingSize, reloadRingSize);
        if (reloadRing.sprite == null)
            reloadRing.sprite = GetCooldownRingSprite();

        reloadRing.preserveAspect = true;
        reloadRing.type = Image.Type.Filled;
        reloadRing.fillMethod = Image.FillMethod.Radial360;
        reloadRing.color = reloadRingColor;
    }

    private void RefreshCooldownRing()
    {
        if (reloadRing == null)
            return;

        ApplyReloadRingVisuals();

        WeaponRuntimeStatus runtimeStatus = playerEquipment != null ? playerEquipment.CurrentWeaponRuntimeStatus : WeaponRuntimeStatus.Empty;
        bool isMagicCooldown = playerEquipment != null
            && playerEquipment.CanCurrentWeaponUseMagicCaster
            && playerController != null
            && playerController.IsAiming
            && runtimeStatus.CooldownRemaining > 0f;
        bool isMeleeCooldown = playerEquipment != null
            && playerEquipment.CanCurrentWeaponUseMeleeSlash
            && playerController != null
            && playerController.IsMeleeCombatStance
            && runtimeStatus.CooldownRemaining > 0f;

        bool showRing = isMagicCooldown || isMeleeCooldown;
        reloadRing.gameObject.SetActive(showRing);

        if (showRing)
        {
            reloadRing.fillAmount = runtimeStatus.CooldownProgress01;
            reloadRing.color = reloadRingColor;
        }
    }

    private void HideWeaponDetails()
    {
        HideMagazineText();
        if (reloadRing != null)
            reloadRing.gameObject.SetActive(false);
    }

    private void HideMagazineText()
    {
        if (magazineText != null)
            magazineText.gameObject.SetActive(false);
    }

    private void SetLine(RectTransform line, Vector2 size)
    {
        if (line == null)
            return;

        line.anchorMin = new Vector2(0.5f, 0.5f);
        line.anchorMax = new Vector2(0.5f, 0.5f);
        line.pivot = new Vector2(0.5f, 0.5f);
        line.sizeDelta = size;

        Image image = line.GetComponent<Image>();
        if (image != null)
        {
            if (image.sprite == null)
                image.sprite = GetSolidSprite();

            image.type = Image.Type.Simple;
            image.color = lineColor;
        }
    }

    private static Sprite GetSolidSprite()
    {
        if (solidSprite != null)
            return solidSprite;

        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "AimPointerSolidSprite"
        };
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();
        solidSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        solidSprite.name = "AimPointerSolidSprite";
        return solidSprite;
    }

    private static Sprite GetCooldownRingSprite()
    {
        if (cooldownRingSprite != null)
            return cooldownRingSprite;

        const int size = 96;
        const float innerRadius = 0.36f;
        const float outerRadius = 0.5f;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "AimPointerCooldownRingSprite"
        };

        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float scale = 1f / (size - 1);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center) * scale;
                bool isRingPixel = distance >= innerRadius && distance <= outerRadius;
                texture.SetPixel(x, y, isRingPixel ? Color.white : Color.clear);
            }
        }

        texture.Apply();
        cooldownRingSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        cooldownRingSprite.name = "AimPointerCooldownRingSprite";
        return cooldownRingSprite;
    }

    private RectTransform FindChildRect(string childName)
    {
        Transform child = transform.Find(childName);
        return child != null ? child as RectTransform : null;
    }

    private TextMeshProUGUI FindChildText(string childName)
    {
        Transform child = transform.Find(childName);
        return child != null ? child.GetComponent<TextMeshProUGUI>() : null;
    }

    private Image FindChildImage(string childName)
    {
        Transform child = transform.Find(childName);
        return child != null ? child.GetComponent<Image>() : null;
    }

    private bool HasRequiredSceneReferences()
    {
        return canvas != null
            && root != null
            && canvasGroup != null
            && topLine != null
            && bottomLine != null
            && leftLine != null
            && rightLine != null
            && magazineText != null
            && reloadRing != null;
    }

    private void WarnMissingConfiguration()
    {
        if (missingReferenceWarned)
            return;

        Debug.LogWarning("[AimPointerUI] AimPointerUI scene references are partially missing. Check Top/Bottom/Left/RightLine, MagazineText, and ReloadRing.");
        missingReferenceWarned = true;
    }
}
