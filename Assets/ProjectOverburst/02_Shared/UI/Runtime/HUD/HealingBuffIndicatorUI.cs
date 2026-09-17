using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(10002)]
public class HealingBuffIndicatorUI : MonoBehaviour
{
    [SerializeField] private PlayerBuffController buffController;
    [SerializeField] private string buffId = "health_pickup_regen";
    [SerializeField] private Image baseArrowImage;
    [SerializeField] private Image fillArrowImage;
    [SerializeField] private Color baseColor = new Color(0.35f, 0.35f, 0.35f, 0.85f);
    [SerializeField] private Color fillColor = new Color(0.12f, 1f, 0.28f, 1f);
    [SerializeField] private bool pointDown;
    [SerializeField] private bool autoResolveReferences = true;

    private static Sprite arrowSprite;

    private void Awake()
    {
        BindVisuals();
        Refresh();
    }

    private void Update()
    {
        if (autoResolveReferences && buffController == null)
            buffController = FindFirstObjectByType<PlayerBuffController>(FindObjectsInactive.Include);

        Refresh();
    }

    private void Refresh()
    {
        float ratio = 0f;
        bool active = buffController != null && buffController.TryGetBuffRemainingRatio(buffId, out ratio);
        SetImageVisible(baseArrowImage, active);
        SetImageVisible(fillArrowImage, active);

        if (!active || fillArrowImage == null)
            return;

        fillArrowImage.fillAmount = ratio;
    }

    private void BindVisuals()
    {
        if (baseArrowImage == null)
            baseArrowImage = transform.Find("Base") != null ? transform.Find("Base").GetComponent<Image>() : null;

        if (fillArrowImage == null)
            fillArrowImage = transform.Find("Fill") != null ? transform.Find("Fill").GetComponent<Image>() : null;

        ConfigureImage(baseArrowImage, baseColor, Image.Type.Simple);
        ConfigureImage(fillArrowImage, fillColor, Image.Type.Filled);
        ApplyDirection(baseArrowImage);
        ApplyDirection(fillArrowImage);

        if (fillArrowImage != null)
        {
            fillArrowImage.fillMethod = Image.FillMethod.Vertical;
            fillArrowImage.fillOrigin = (int)Image.OriginVertical.Bottom;
            fillArrowImage.fillAmount = 0f;
        }
    }

    private static void ConfigureImage(Image image, Color color, Image.Type type)
    {
        if (image == null)
            return;

        image.sprite = GetArrowSprite();
        image.color = color;
        image.type = type;
        image.raycastTarget = false;
    }

    private void ApplyDirection(Image image)
    {
        if (image != null)
            image.rectTransform.localEulerAngles = pointDown ? new Vector3(0f, 0f, 180f) : Vector3.zero;
    }

    private static void SetImageVisible(Image image, bool visible)
    {
        if (image != null)
            image.enabled = visible;
    }

    private static Sprite GetArrowSprite()
    {
        if (arrowSprite != null)
            return arrowSprite;

        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "HealingBuffArrowSprite"
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
        arrowSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        arrowSprite.name = "HealingBuffArrowSprite";
        return arrowSprite;
    }
}
