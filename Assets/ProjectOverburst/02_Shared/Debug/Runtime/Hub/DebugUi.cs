#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Overburst.DebugTools
{
    internal enum DebugButtonKind
    {
        /// <summary>색이 있는 버튼. Image 색이 바탕이고 hover·누름은 밝기로 표시한다.</summary>
        Solid,
        /// <summary>바탕 없이 글자만 있고 hover에만 옅은 바탕이 뜬다.</summary>
        Ghost,
        /// <summary>Ghost와 같지만 여백 없이 기호 하나를 그린다(☆, 핀).</summary>
        Icon
    }

    /// <summary>
    /// 디버그 창을 코드로 만드는 도우미. 배치는 레이아웃 그룹이 하고, 좌표는 창 이동·크기 조절에만 쓴다.
    /// 둥근 모서리는 코드로 만든 9-slice 스프라이트를 쓴다(에셋 없음).
    /// </summary>
    internal static class DebugUi
    {
        private const int UiLayer = 5;
        private static readonly Dictionary<int, Sprite> roundedSprites = new Dictionary<int, Sprite>();

        public static DebugHubStyle Style { get; private set; }
        public static TMP_FontAsset Font { get; private set; }

        public static void Initialize(DebugHubStyle style)
        {
            Style = style;
            Font = style.font != null ? style.font : Resources.Load<TMP_FontAsset>(DebugHubStyle.DefaultFontPath);
            if (Font == null)
                Font = TMP_Settings.defaultFontAsset;
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UiLayer;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rect, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        public static Image Image(Transform parent, string name, Color color, bool raycast = false)
        {
            Image image = Rect(parent, name).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        /// <summary>둥근 모서리 바탕.</summary>
        public static Image Panel(Transform parent, string name, Color color, int radius, bool raycast = false)
        {
            Image image = Image(parent, name, color, raycast);
            ApplyRounded(image, radius);
            return image;
        }

        public static void ApplyRounded(Image image, int radius)
        {
            image.sprite = Rounded(radius);
            image.type = UnityEngine.UI.Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;
        }

        /// <summary>반지름 radius(px)의 흰 둥근 사각형 9-slice 스프라이트. 한 번 만들어 재사용한다.</summary>
        public static Sprite Rounded(int radius)
        {
            radius = Mathf.Clamp(radius, 1, 16);
            if (roundedSprites.TryGetValue(radius, out Sprite cached) && cached != null)
                return cached;

            int size = radius * 2 + 4;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "DebugHub Rounded " + radius,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    float cx = Mathf.Clamp(px, radius, size - radius);
                    float cy = Mathf.Clamp(py, radius, size - radius);
                    float distance = Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy));
                    float alpha = Mathf.Clamp01(radius - distance + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            float border = radius + 1f;
            Sprite sprite = Sprite.Create(texture, new UnityEngine.Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.DontSave;
            roundedSprites[radius] = sprite;
            return sprite;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, string value, float size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, bool bold = false, bool rich = false)
        {
            TextMeshProUGUI text = Rect(parent, name).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = Font;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.richText = rich;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.text = value ?? string.Empty;
            return text;
        }

        public static LayoutElement Layout(Component target, float preferredWidth = -1f, float preferredHeight = -1f,
            float flexibleWidth = -1f, float flexibleHeight = -1f, float minWidth = -1f, float minHeight = -1f)
        {
            if (!target.TryGetComponent(out LayoutElement element))
                element = target.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = preferredWidth;
            element.preferredHeight = preferredHeight;
            element.flexibleWidth = flexibleWidth;
            element.flexibleHeight = flexibleHeight;
            element.minWidth = minWidth;
            element.minHeight = minHeight;
            return element;
        }

        /// <summary>
        /// 높이가 정해진 막대(제목줄·상태줄·행). 가로 배치 그룹은 자식을 늘릴 때 스스로 '늘어나는 요소'로 보고되므로
        /// flexibleHeight를 0으로 못 박아야 세로 배치에서 남는 높이를 가져가지 않는다.
        /// </summary>
        public static LayoutElement FixedHeight(Component target, float height)
        {
            return Layout(target, preferredHeight: height, flexibleHeight: 0f, minHeight: height);
        }

        public static void IgnoreLayout(Component target)
        {
            if (!target.TryGetComponent(out LayoutElement element))
                element = target.gameObject.AddComponent<LayoutElement>();
            element.ignoreLayout = true;
        }

        public static HorizontalLayoutGroup Row(Component target, float spacing, RectOffset padding = null,
            TextAnchor alignment = TextAnchor.MiddleLeft, bool expandHeight = true)
        {
            var group = target.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.padding = padding ?? new RectOffset();
            group.childAlignment = alignment;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = expandHeight;
            return group;
        }

        public static VerticalLayoutGroup Column(Component target, float spacing, RectOffset padding = null,
            bool expandWidth = true)
        {
            var group = target.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.padding = padding ?? new RectOffset();
            group.childAlignment = TextAnchor.UpperLeft;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = expandWidth;
            group.childForceExpandHeight = false;
            return group;
        }

        public static Button Button(Transform parent, string name, string caption, Action onClick,
            out TextMeshProUGUI label, float minWidth = 36f, float height = 22f, float fontSize = -1f,
            DebugButtonKind kind = DebugButtonKind.Solid, int radius = 4)
        {
            bool solid = kind == DebugButtonKind.Solid;
            Image background = Panel(parent, name, solid ? Style.button : Color.white, radius, true);
            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.colors = solid ? SolidColors() : GhostColors();
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            float padding = kind == DebugButtonKind.Icon ? 0f : 8f;
            label = Text(background.transform, "Label", caption, fontSize > 0f ? fontSize : Style.bodySize - 1f,
                solid ? Style.text : Style.label, TextAlignmentOptions.Center);
            Stretch(label.rectTransform, padding, 0f, padding, 0f);
            if (kind == DebugButtonKind.Icon)
                Layout(background, preferredWidth: minWidth, preferredHeight: height, flexibleWidth: 0f,
                    flexibleHeight: 0f, minWidth: minWidth, minHeight: height);
            else
                FitWidth(button, label, minWidth);
            button.onClick.AddListener(() =>
            {
                // 누른 버튼이 선택된 채 남으면 Space·Enter가 버튼을 다시 누른다.
                Deselect();
                onClick?.Invoke();
            });
            return button;
        }

        /// <summary>글자 폭에 맞춘다. 최소 폭도 글자 폭으로 두어 좁아져도 글자가 잘리지 않는다.</summary>
        public static void FitWidth(Button button, TextMeshProUGUI label, float minWidth, float maxWidth = 320f)
        {
            if (button == null || label == null)
                return;
            float height = button.TryGetComponent(out LayoutElement existing) && existing.preferredHeight > 0f
                ? existing.preferredHeight
                : 22f;
            float width = Mathf.Clamp(label.GetPreferredValues(label.text).x + 18f, minWidth, Mathf.Max(minWidth, maxWidth));
            Layout(button, preferredWidth: width, preferredHeight: height, flexibleWidth: 0f, flexibleHeight: 0f,
                minWidth: width, minHeight: height);
        }

        public static ColorBlock SolidColors()
        {
            return new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f),
                pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f),
                selectedColor = Color.white,
                disabledColor = new Color(0.75f, 0.75f, 0.75f, 0.5f),
                colorMultiplier = 1f,
                fadeDuration = 0.06f
            };
        }

        public static ColorBlock GhostColors()
        {
            return new ColorBlock
            {
                normalColor = new Color(1f, 1f, 1f, 0f),
                highlightedColor = new Color(1f, 1f, 1f, 0.09f),
                pressedColor = new Color(1f, 1f, 1f, 0.16f),
                selectedColor = new Color(1f, 1f, 1f, 0f),
                disabledColor = new Color(1f, 1f, 1f, 0f),
                colorMultiplier = 1f,
                fadeDuration = 0.06f
            };
        }

        public static TMP_InputField Input(Transform parent, string name, string placeholder, float size)
        {
            Image background = Panel(parent, name, Style.input, 5, true);
            background.gameObject.SetActive(false);

            RectTransform area = Rect(background.transform, "Text Area");
            Stretch(area, 9f, 1f, 9f, 1f);
            area.gameObject.AddComponent<RectMask2D>();

            TextMeshProUGUI hint = Text(area, "Placeholder", placeholder, size, Style.muted);
            Stretch(hint.rectTransform);
            TextMeshProUGUI value = Text(area, "Text", string.Empty, size, Style.text);
            Stretch(value.rectTransform);

            var input = background.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = area;
            input.textComponent = value;
            input.placeholder = hint;
            input.targetGraphic = background;
            input.transition = Selectable.Transition.ColorTint;
            input.colors = SolidColors();
            input.fontAsset = Font;
            input.pointSize = size;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.richText = false;
            input.customCaretColor = true;
            input.caretColor = Style.text;
            input.selectionColor = new Color(Style.accentAlt.r, Style.accentAlt.g, Style.accentAlt.b, 0.5f);
            input.navigation = new Navigation { mode = Navigation.Mode.None };
            background.gameObject.SetActive(true);
            return input;
        }

        /// <summary>세로 스크롤 보기. 반환값은 항목을 넣을 Content다(위에서 아래로 쌓이고 높이가 자동으로 맞춰진다).</summary>
        public static RectTransform Scroll(Transform parent, string name, out ScrollRect scroll)
        {
            Image background = Image(parent, name, new Color(0f, 0f, 0f, 0f), true);
            scroll = background.gameObject.AddComponent<ScrollRect>();

            RectTransform viewport = Rect(background.transform, "Viewport");
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = Rect(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RectTransform track = Rect(background.transform, "Scrollbar");
            track.anchorMin = new Vector2(1f, 0f);
            track.anchorMax = new Vector2(1f, 1f);
            track.pivot = new Vector2(1f, 0.5f);
            track.sizeDelta = new Vector2(8f, -8f);
            track.anchoredPosition = new Vector2(-2f, 0f);
            Image trackHit = track.gameObject.AddComponent<Image>();
            trackHit.color = new Color(0f, 0f, 0f, 0f);
            RectTransform slidingArea = Rect(track, "Sliding Area");
            Stretch(slidingArea, 2f, 0f, 2f, 0f);
            Image handle = Panel(slidingArea, "Handle", new Color(Style.muted.r, Style.muted.g, Style.muted.b, 0.45f), 2, true);
            Stretch(handle.rectTransform);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle.rectTransform;
            scrollbar.targetGraphic = handle;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.scrollSensitivity = 30f;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scroll.verticalScrollbarSpacing = 2f;
            return content;
        }

        public static Image Separator(Transform parent, string name = "Separator")
        {
            Image line = Image(parent, name, Style.line);
            FixedHeight(line, 1f);
            return line;
        }

        public static DebugHoverTip AddTip(Graphic target, string text)
        {
            if (target == null || string.IsNullOrEmpty(text))
                return null;
            target.raycastTarget = true;
            DebugHoverTip tip = target.gameObject.AddComponent<DebugHoverTip>();
            tip.Text = text;
            return tip;
        }

        public static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                // Destroy는 프레임 끝에 지우므로, 이번 프레임 배치에서 빠지도록 먼저 끈다.
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }

        public static void Deselect()
        {
            EventSystem system = EventSystem.current;
            if (system != null && !system.alreadySelecting)
                system.SetSelectedGameObject(null);
        }
    }
}
#endif
