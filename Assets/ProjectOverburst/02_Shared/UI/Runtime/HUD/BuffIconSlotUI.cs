using UnityEngine;
using UnityEngine.UI;

public sealed class BuffIconSlotUI : MonoBehaviour
{
    [SerializeField] private Image baseImage;
    [SerializeField] private Image fillImage;
    [SerializeField] private Color fallbackBuffColor = new Color(0.12f, 1f, 0.28f, 1f);
    [SerializeField] private Color fallbackDebuffColor = new Color(1f, 0.18f, 0.12f, 1f);
    [SerializeField] private Color fallbackBaseColor = new Color(0.35f, 0.35f, 0.35f, 0.85f);

    private static Sprite defaultArrowSprite;

    private void Awake()
    {
        BindVisuals();
        SetVisible(false);
    }

    public void SetBuff(BuffInstance instance)
    {
        BindVisuals();

        if (instance == null || instance.Definition == null)
        {
            SetVisible(false);
            return;
        }

        BuffDefinition definition = instance.Definition;
        Sprite icon = definition.icon != null ? definition.icon : GetDefaultArrowSprite();
        Color fillColor = definition.indicatorColor;
        if (fillColor.a <= 0f)
            fillColor = definition.isDebuff ? fallbackDebuffColor : fallbackBuffColor;

        Color baseColor = definition.baseIndicatorColor;
        if (baseColor.a <= 0f)
            baseColor = fallbackBaseColor;

        ConfigureImage(baseImage, icon, baseColor, Image.Type.Simple);
        ConfigureImage(fillImage, icon, fillColor, Image.Type.Filled);

        if (fillImage != null)
        {
            fillImage.fillMethod = Image.FillMethod.Vertical;
            fillImage.fillOrigin = (int)Image.OriginVertical.Bottom;
            fillImage.fillAmount = instance.RemainingRatio;
        }

        Vector3 rotation = definition.iconPointsDown ? new Vector3(0f, 0f, 180f) : Vector3.zero;
        if (baseImage != null)
            baseImage.rectTransform.localEulerAngles = rotation;
        if (fillImage != null)
            fillImage.rectTransform.localEulerAngles = rotation;

        SetVisible(true);
    }

    public void SetVisible(bool visible)
    {
        if (baseImage != null)
            baseImage.enabled = visible;
        if (fillImage != null)
            fillImage.enabled = visible;
    }

    private void BindVisuals()
    {
        if (baseImage == null)
            baseImage = transform.Find("Base") != null ? transform.Find("Base").GetComponent<Image>() : null;

        if (fillImage == null)
            fillImage = transform.Find("Fill") != null ? transform.Find("Fill").GetComponent<Image>() : null;
    }

    private static void ConfigureImage(Image image, Sprite sprite, Color color, Image.Type type)
    {
        if (image == null)
            return;

        image.sprite = sprite;
        image.color = color;
        image.type = type;
        image.raycastTarget = false;
        image.preserveAspect = true;
    }

    private static Sprite GetDefaultArrowSprite()
    {
        if (defaultArrowSprite != null)
            return defaultArrowSprite;

        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "BuffDefaultArrowSprite"
        };

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f) / size;
                float ny = (y + 0.5f) / size;
                bool shaft = nx >= 0.36f && nx <= 0.64f && ny <= 0.62f;
                bool head = ny > 0.42f && Mathf.Abs(nx - 0.5f) <= (ny - 0.42f) * 0.85f;
                texture.SetPixel(x, y, shaft || head ? Color.white : Color.clear);
            }
        }

        texture.Apply();
        defaultArrowSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        defaultArrowSprite.name = "BuffDefaultArrowSprite";
        return defaultArrowSprite;
    }
}
