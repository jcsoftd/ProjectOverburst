using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class WorldLevelUIBuilder
{
    public const string Root = "Assets/ProjectOverburst/Resources/UI/WorldLevel/";
    public const string Prefab = Root + "PF_WorldLevelUI.prefab";
    public const string Layout = "Assets/Editor/Builders/UI/WorldLevelUiLayout.json";
    public const string ConfirmLayout = "Assets/Editor/Builders/UI/WorldLevelConfirmLayout.json";
    static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        => QuestMerchantUIBuilder.Rect(parent, name, x, y, w, h);
    static float F(JToken value) => value == null ? 0 : float.Parse(Regex.Match(value.ToString(), @"-?[\d.]+").Value, System.Globalization.CultureInfo.InvariantCulture);
    static Color ColorOf(string css)
    {
        var match = Regex.Match(css ?? "", @"rgba?\(([^)]+)\)"); if (!match.Success) return Color.clear;
        var v = match.Groups[1].Value.Split(',').Select(s => float.Parse(s.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        return new Color(v[0] / 255f, v[1] / 255f, v[2] / 255f, v.Length > 3 ? v[3] : 1);
    }
    static Texture2D Texture(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "Artwork/" + name);
    static RawImage Art(Transform parent, string name, float x, float y, float w, float h, string file)
    {
        var image = Rect(parent, name, x, y, w, h).gameObject.AddComponent<RawImage>();
        image.texture = Texture(file); image.raycastTarget = false;
        if (!image.texture) throw new FileNotFoundException(file); return image;
    }
    static void Border(Transform parent, float w, float h, JArray borders)
    {
        if (borders == null) return;
        for (int i = 0; i < 4; i++)
        {
            var value = borders[i].ToString(); float width = F(borders[i]); var color = ColorOf(value);
            if (width <= 0 || color.a == 0) continue;
            // Browser zoom can export a 1px rule as 0.909px; preserve a visible native pixel.
            width = Mathf.Max(1, width);
            QuestMerchantUIBuilder.Solid(parent, "Border " + i, i == 1 ? w - width : 0, i == 2 ? h - width : 0,
                i % 2 == 0 ? w : width, i % 2 == 0 ? width : h, color);
        }
    }
    static void Background(RectTransform rect, JObject node)
    {
        string background = (string)node["background"], classes = (string)node["classes"] ?? "";
        float w = rect.sizeDelta.x, h = rect.sizeDelta.y;
        if (classes.Contains("world-window")) { Art(rect, "Actual Quest Window", -10, -10, w + 20, h + 20, "native-window-artwork.png"); return; }
        var colors = Regex.Matches(background ?? "", @"rgba?\([^)]+\)").Cast<Match>().Select(m => ColorOf(m.Value)).ToArray();
        if (colors.Length > 1 && (background ?? "").Contains("linear-gradient"))
        {
            var graphic = rect.gameObject.AddComponent<QuestUiGradient>(); graphic.top = colors[0]; graphic.bottom = colors[1]; graphic.raycastTarget = false;
        }
        else if (colors.Length > 0 && colors[0].a > 0)
        {
            var fill = QuestMerchantUIBuilder.Solid(rect, "Fill", 0, 0, w, h, colors[0]);
            if (classes.Contains("confirm-window"))
            {
                // Reuse the existing merchant fix for dark vertex-color precision in a Linear canvas.
                fill.material = AssetDatabase.LoadAssetAtPath<Material>(QuestMerchantUIBuilder.Folder + "/Background_11120F.mat");
                if (!fill.material) throw new InvalidOperationException("Existing merchant background material missing");
                fill.color = Color.white;
            }
        }
        Border(rect, w, h, node["borders"] as JArray);
        if (classes == "npc-portrait") Sliced(rect, "NPC Frame", 0, 0, w, h, "small-border.png", 16, 2);
    }
    static Image Sliced(Transform parent, string name, float x, float y, float w, float h, string file, float border, float multiplier)
    {
        var image = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "Artwork/" + file);
        if(!image.sprite)throw new InvalidOperationException("Sprite import missing: "+file);
        image.type = Image.Type.Sliced; image.fillCenter = false; image.pixelsPerUnitMultiplier = multiplier; image.raycastTarget = false;
        return image;
    }
    static Text Label(Transform parent, string name, JObject node, JToken bounds, string value, bool centered = false)
    {
        var label = QuestMerchantUIBuilder.Label(parent, name, F(bounds["x"]), F(bounds["y"]), F(bounds["width"]), F(bounds["height"]),
            value, Mathf.RoundToInt(F(node["fontSize"])), ColorOf((string)node["color"]), ((string)node["font"] ?? "").Contains("OBTitle"));
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.alignment = centered ? TextAnchor.MiddleCenter : TextAnchor.UpperLeft;
        if(node["letterSpacing"] != null && (string)node["letterSpacing"] != "normal")
            label.gameObject.AddComponent<WorldLevelLetterSpacing>().spacing = F(node["letterSpacing"]) * 2;
        return label;
    }
    static JObject Bounds(float x, float y, float w, float h) => JObject.FromObject(new { x, y, width = w, height = h });
    static Button Button(RectTransform rect)
    {
        var hit = rect.gameObject.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = hit; button.transition = Selectable.Transition.None;
        return button;
    }
    static void Skin(Button button, string kind, Text label)
    {
        var r = (RectTransform)button.transform; var skin = button.gameObject.AddComponent<WorldLevelButtonSkin>();
        skin.artwork = Art(r, "Actual Button Artwork", -2, -2, r.sizeDelta.x + 4, r.sizeDelta.y + 4, $"native-{kind}-normal.png");
        skin.artwork.transform.SetAsFirstSibling(); skin.normal = Texture($"native-{kind}-normal.png");
        skin.hover = Texture($"native-{kind}-hover.png"); skin.pressed = Texture($"native-{kind}-pressed.png");
        skin.focus = Texture($"native-{kind}-focus.png"); skin.disabled = Texture($"native-{kind}-disabled.png"); skin.label = label;
    }
    public static string Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Editor must be idle.");
        if (!AssetDatabase.IsValidFolder(Root.TrimEnd('/'))) throw new DirectoryNotFoundException(Root);
        var go = new GameObject("PF_WorldLevelUI", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(WorldLevelUI));
        var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, preview);
        try
        {
            var root = (RectTransform)go.transform; root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f); root.sizeDelta = new Vector2(1920, 1080);
            var canvas = go.GetComponent<Canvas>(); canvas.overrideSorting = true; canvas.sortingOrder = 150;
            var window = Rect(root, "Window", 0, 0, 1920, 1080); var group = window.gameObject.AddComponent<CanvasGroup>();
            var scrim = QuestMerchantUIBuilder.Solid(window, "Scrim", 0, 0, 1920, 1080, new Color(0, 0, 0, 7 / 15f)); scrim.raycastTarget = true;
            var buttons = new Dictionary<string, Button>(); var rows = new Dictionary<string, RectTransform>();
            var nodes = JArray.Parse(File.ReadAllText(Layout));
            foreach (JObject node in nodes)
            {
                var b = node["rect"]; string key = (string)node["key"], id = (string)node["id"], tag = (string)node["tag"], row = (string)node["row"];
                if (tag == "BR") continue;
                RectTransform parent = window; float ox = 0, oy = 0;
                if (row != null && tag != "BUTTON" && rows.TryGetValue(row, out var rowRoot))
                { parent = rowRoot; ox = rowRoot.anchoredPosition.x; oy = -rowRoot.anchoredPosition.y; }
                var rect = Rect(parent, tag == "BUTTON" && row != null ? "stage-" + row : key, F(b["x"]) - ox, F(b["y"]) - oy, F(b["width"]), F(b["height"]));
                Background(rect, node);
                if (tag == "BUTTON")
                {
                    var button = Button(rect); buttons[id ?? key] = button;
                    if (row != null)
                    {
                        rows[row] = rect; var skin = rect.gameObject.AddComponent<WorldLevelButtonSkin>(); skin.row = true;
                        skin.artwork = Art(rect, "Actual Row Artwork", -22, -22, rect.sizeDelta.x + 44, rect.sizeDelta.y + 44, "native-stage-normal.png");
                        skin.normal = Texture("native-stage-normal.png"); skin.focus = Texture("native-stage-selected.png");
                    }
                    else if (id == "close-window") CloseArt(rect);
                }
                if (tag == "IMG")
                {
                    var image = Art(rect, "Source", 0, 0, rect.sizeDelta.x, rect.sizeDelta.y, Path.GetFileName((string)node["src"]));
                    if (((string)node["src"] ?? "").Contains("npc-portrait"))
                    {
                        float visible = rect.sizeDelta.y / (rect.sizeDelta.x * 1536f / 1024f);
                        image.uvRect = new Rect(0, (1 - visible) * .85f, 1, visible);
                    }
                    // The close button's exact three layers are authored separately.
                    if (id == "" && ((string)node["src"] ?? "").Contains("close-x")) rect.gameObject.SetActive(false);
                }
                var runs = (JArray)node["runs"];
                for (int i = 0; i < runs.Count; i++)
                {
                    string name = key + (runs.Count > 1 ? ":" + i : ""); var rb = runs[i]["rect"];
                    float x = F(rb["x"]) - F(b["x"]), y = F(rb["y"]) - F(b["y"]), w = F(rb["width"]), h = F(rb["height"]);
                    bool center = ((string)node["textAlign"] == "center");
                    if (center) { x = 0; w = rect.sizeDelta.x; }
                    if (id == "character-name") w = 240;
                    if (id == "unlock-summary") w = 95;
                    if (id == "detail-status") { x = -15; w += 30; }
                    if (row != null)
                    {
                        string cls = (string)node["classes"] ?? "";
                        name = $"row-{row}-" + (cls.Contains("status-chip") ? "status" : cls.Contains("monster-range") ? "range" : tag == "STRONG" ? "number" : "unit");
                    }
                    var label = Label(rect, name, node, Bounds(x, y, w, h), (string)runs[i]["text"], center);
                    if (name.StartsWith("row-") && name.EndsWith("status")) { label.alignment = TextAnchor.MiddleCenter; label.rectTransform.sizeDelta = new Vector2(212, 52); }
                }
            }
            var allLabels = window.GetComponentsInChildren<Text>(true);
            foreach (var pair in new[]{new[]{"unlock-button","unlock"},new[]{"apply-button","apply"},new[]{"leave-button","leave"}})
            {
                var button = buttons[pair[0]]; var old = allLabels.FirstOrDefault(l => l.name == pair[0]+"-text" || (pair[0] == "leave-button" && l.name == "leave-button"));
                if (old)
                {
                    old.transform.SetParent(button.transform, false); var r = old.rectTransform;
                    r.anchoredPosition = Vector2.zero; r.sizeDelta = ((RectTransform)button.transform).sizeDelta * 2; r.localScale = Vector3.one * .5f; old.alignment = TextAnchor.MiddleCenter;
                }
                Skin(button, pair[1], old);
            }
            var confirmation = Rect(root, "Confirmation", 0, 0, 1920, 1080);
            var confirmButtons = BuildConfirmation(confirmation);
            var toast = Rect(root, "Toast", 510, 32, 900, 57);
            var toastGraphic = toast.gameObject.AddComponent<QuestUiGradient>(); toastGraphic.top = QuestMerchantUIBuilder.C("2D3026"); toastGraphic.bottom = QuestMerchantUIBuilder.C("1C2019"); toastGraphic.raycastTarget = false;
            Border(toast, 900, 57, new JArray(Enumerable.Repeat("1px solid rgb(139,146,96)", 4)));
            var toastText = QuestMerchantUIBuilder.Label(toast, "toast-message", 24, 10, 852, 40, "", 19, QuestMerchantUIBuilder.C("DBE2BF")); toastText.alignment = TextAnchor.MiddleCenter;
            var component = go.GetComponent<WorldLevelUI>(); var so = new SerializedObject(component);
            Set(so, "window", window.gameObject); Set(so, "confirmation", confirmation.gameObject); Set(so, "toast", toast.gameObject); Set(so, "windowInput", group);
            Set(so, "unlock", buttons["unlock-button"]); Set(so, "apply", buttons["apply-button"]); Set(so, "cancel", confirmButtons["cancel-apply"]); Set(so, "confirm", confirmButtons["confirm-apply"]);
            Array(so, "stages", rows.OrderBy(p => int.Parse(p.Key)).Select(p => (Object)p.Value.GetComponent<Button>()).ToArray());
            Array(so, "stageSkins", rows.OrderBy(p => int.Parse(p.Key)).Select(p => (Object)p.Value.GetComponent<WorldLevelButtonSkin>()).ToArray());
            Array(so, "labels", go.GetComponentsInChildren<Text>(true).Cast<Object>().ToArray()); so.ApplyModifiedPropertiesWithoutUndo();
            window.gameObject.SetActive(false); confirmation.gameObject.SetActive(false); toast.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(go, Prefab); return Prefab;
        }
        finally { Object.DestroyImmediate(go); UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview); }
    }
    static Dictionary<string, Button> BuildConfirmation(RectTransform parent)
    {
        var buttons = new Dictionary<string, Button>();
        foreach (JObject node in JArray.Parse(File.ReadAllText(ConfirmLayout)))
        {
            string id = (string)node["id"], cls = (string)node["classes"], tag = (string)node["tag"];
            string content = id == "confirm-title" ? "월드레벨 변경" : id == "confirm-lead" ? "야외 월드레벨을 4단계로 변경할까요?"
                : id == "confirm-from" ? "1단계 · Lv.1 – 10" : id == "confirm-to" ? "4단계 · Lv.31 – 40"
                : id == "cancel-apply" ? "취소" : id == "confirm-apply" ? "변경하기"
                : cls == "confirm-loot" ? "이미 획득한 아이템은 유지됩니다." : (string)node["text"];
            var b = node["rect"]; if (F(b["width"]) <= 0 || tag == "IMG") continue;
            var rect = Rect(parent, string.IsNullOrEmpty(id) ? cls + "-" + parent.childCount : id, F(b["x"]), F(b["y"]), F(b["width"]), F(b["height"]));
            Background(rect, node);
            if(cls=="confirm-header")QuestMerchantUIBuilder.Solid(rect,"Header Rule",0,rect.sizeDelta.y-1,rect.sizeDelta.x,1,QuestMerchantUIBuilder.C("65513555"));
            if(cls=="confirm-range")
            {
                QuestMerchantUIBuilder.Solid(rect,"Range Top",0,0,rect.sizeDelta.x,1,QuestMerchantUIBuilder.C("504630"));
                QuestMerchantUIBuilder.Solid(rect,"Range Bottom",0,rect.sizeDelta.y-1,rect.sizeDelta.x,1,QuestMerchantUIBuilder.C("504630"));
            }
            if (id == "confirmation-scrim") { var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(0,0,0,11/15f); buttons[id] = rect.gameObject.AddComponent<Button>(); buttons[id].targetGraphic = image; buttons[id].transition = Selectable.Transition.None; }
            if (id == "confirm-window")
            {
                var hit = rect.gameObject.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true;
                Sliced(rect,"Existing Frame",0,0,rect.sizeDelta.x,rect.sizeDelta.y,"container-border.png",18,1.8f);
                var inner = Rect(rect,"Existing Inner Rule",3,3,rect.sizeDelta.x-6,rect.sizeDelta.y-6);
                Border(inner,inner.sizeDelta.x,inner.sizeDelta.y,new JArray(Enumerable.Repeat("1px solid rgba(117,97,68,0.165)",4)));
            }
            if (tag == "BUTTON")
            {
                var button = Button(rect); buttons[id] = button;
                if (id == "close-confirm") CloseArt(rect);
                else
                {
                    var label = QuestMerchantUIBuilder.Label(rect,id+"-text",0,0,rect.sizeDelta.x,rect.sizeDelta.y,content,id=="confirm-apply"?20:17,QuestMerchantUIBuilder.C(id=="confirm-apply"?"EAD6B6":"DCC9AA"),true);
                    label.alignment=TextAnchor.MiddleCenter; Skin(button,id=="confirm-apply"?"confirm":"cancel",label);
                }
            }
            else if (!string.IsNullOrEmpty(content))
                Label(rect,string.IsNullOrEmpty(id)?cls:id,node,Bounds(0,0,rect.sizeDelta.x,rect.sizeDelta.y),content,true);
            if(id=="confirm-description")
            {
                var label=Label(rect,id,node,Bounds(0,0,rect.sizeDelta.x,rect.sizeDelta.y),"현재 야외의 몬스터와 생성 대기가 초기화됩니다.\n다음 진입 구역부터 새 단계의 몬스터가 등장합니다.",true);
                label.lineSpacing=1.5f;
            }
        }
        return buttons;
    }
    static void CloseArt(RectTransform rect)
    {
        var layers = new[]{Art(rect,"Close Background",0,0,36,36,"close-bg.png"), Art(rect,"Close Border",5.89f,5.89f,24.22f,24.22f,"close-border.png"), Art(rect,"Close X",12.11f,12.11f,11.78f,11.78f,"close-x.png")};
        var path = Root + "CloseHover.mat"; var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(Shader.Find("OVERBURST/UI Merchant Portrait"));
            material.SetFloat("_Brightness",1.4f); material.SetFloat("_Contrast",1); material.SetVector("_RgbTint",Vector4.one);
            AssetDatabase.CreateAsset(material,path);
        }
        var focus = Rect(rect,"Keyboard Focus",-5,-5,46,46);
        Border(focus,46,46,new JArray(Enumerable.Repeat("2px solid rgb(230,196,142)",4)));
        focus.gameObject.SetActive(false);
        var skin = rect.gameObject.AddComponent<WorldLevelButtonSkin>(); skin.closeLayers=layers; skin.closeHover=material; skin.closeFocus=focus.gameObject;
    }
    static void Set(SerializedObject so,string field,Object value) => so.FindProperty(field).objectReferenceValue=value;
    static void Array(SerializedObject so,string field,Object[] values)
    { var p=so.FindProperty(field);p.arraySize=values.Length;for(int i=0;i<values.Length;i++)p.GetArrayElementAtIndex(i).objectReferenceValue=values[i]; }
}
