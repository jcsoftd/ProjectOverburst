using TMPro;
using UnityEngine;

namespace Overburst.DebugTools
{
    /// <summary>
    /// 디버그 창의 글꼴·색·크기. 에셋(Resources/Debug/DebugHubStyle)이 없으면 이 기본값으로 동작한다.
    /// 릴리스에서 형식이 사라지면 에셋이 Missing Script가 되므로 이 파일은 #if로 감싸지 않는다(90C 9절).
    /// </summary>
    [CreateAssetMenu(fileName = "DebugHubStyle", menuName = "OVERBURST/Debug/Debug Hub Style")]
    public sealed class DebugHubStyle : ScriptableObject
    {
        public const string ResourcePath = "Debug/DebugHubStyle";
        public const string DefaultFontPath = "UI/Fonts/ProjectMT/FontAssets/TMP_SpoqaHanSansNeo_Body";

        [Header("글꼴")]
        [Tooltip("비우면 Spoqa Han Sans Neo 본문 글꼴을 쓴다.")]
        public TMP_FontAsset font;
        public float bodySize = 14f;
        public float headerSize = 14f;
        public float smallSize = 12f;

        [Header("바탕")]
        public Color window = new Color(0.078f, 0.094f, 0.129f, 1f);
        public Color border = new Color(1f, 1f, 1f, 0.10f);
        public Color shadow = new Color(0f, 0f, 0f, 0.35f);
        public Color shadowSoft = new Color(0f, 0f, 0f, 0.18f);
        public Color tabs = new Color(0f, 0f, 0f, 0.20f);
        public Color tabSelected = new Color(0.17f, 0.31f, 0.31f, 1f);
        public Color input = new Color(0.035f, 0.045f, 0.068f, 1f);
        public Color button = new Color(0.165f, 0.2f, 0.26f, 1f);
        public Color segment = new Color(0.045f, 0.056f, 0.082f, 1f);
        public Color badge = new Color(1f, 1f, 1f, 0.07f);
        public Color knob = new Color(0.95f, 0.96f, 0.98f, 1f);
        public Color overlay = new Color(0.04f, 0.05f, 0.075f, 0.86f);
        public Color tooltip = new Color(0.03f, 0.036f, 0.055f, 0.98f);
        public Color line = new Color(1f, 1f, 1f, 0.07f);
        public Color confirm = new Color(0.33f, 0.21f, 0.09f, 1f);
        public Color highlight = new Color(0.25f, 0.5f, 0.47f, 0.28f);

        [Header("상태")]
        public Color accent = new Color(0.22f, 0.47f, 0.44f, 1f);
        public Color accentAlt = new Color(0.20f, 0.48f, 0.62f, 1f);
        public Color on = new Color(0.24f, 0.62f, 0.46f, 1f);
        public Color off = new Color(0.25f, 0.28f, 0.34f, 1f);
        public Color ok = new Color(0.42f, 0.8f, 0.55f, 1f);
        public Color warn = new Color(0.96f, 0.64f, 0.28f, 1f);
        public Color error = new Color(0.93f, 0.36f, 0.34f, 1f);
        public Color favorite = new Color(0.96f, 0.79f, 0.32f, 1f);

        [Header("글자")]
        public Color text = new Color(0.88f, 0.9f, 0.94f, 1f);
        public Color textStrong = Color.white;
        public Color label = new Color(0.66f, 0.7f, 0.77f, 1f);
        public Color muted = new Color(0.47f, 0.51f, 0.58f, 1f);

        public static DebugHubStyle Load()
        {
            DebugHubStyle style = Resources.Load<DebugHubStyle>(ResourcePath);
            if (style != null)
                return style;
            style = CreateInstance<DebugHubStyle>();
            style.hideFlags = HideFlags.DontSave;
            return style;
        }

        public static string Hex(Color color) => "#" + ColorUtility.ToHtmlStringRGB(color);
    }
}
