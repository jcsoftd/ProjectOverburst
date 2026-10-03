using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class OverburstShopUIBuilder
{
    public const string PrefabPath = WeaponElementIconBuilder.UiRoot + "PF_OverburstShopPanel_Rpg11.prefab";
    public const string Output = "../개인파일/코덱스산출/UI/20261003_ShopTradeUnified/Compact02";
    public const float MerchantWidth = OverburstUIShopSkin.MerchantWidth, TradeWidth = OverburstUIShopSkin.TradeWidth, Height = OverburstUIShopSkin.WindowHeight;
    public const float MerchantX = OverburstUIShopSkin.MerchantX, TradeX = OverburstUIShopSkin.TradeX, WindowY = OverburstUIShopSkin.WindowY;
    public const string ButtonRoot = "Assets/ThirdParty/RPG and MMO UI 11/Prefabs/Controls/Buttons/Rectangular/";
    public const float Cell = OverburstUIWorkshopBuilder.UnifiedSlotSize, Gap = 8;
    static readonly Color Gold = new Color(.88f, .74f, .48f, 1);
    static readonly Color Ivory = new Color(.93f, .90f, .83f, 1);
    static readonly Color Muted = new Color(.74f, .71f, .66f, 1);
    static readonly Color RuleColor = new Color(.34f, .30f, .23f, .65f);
    static readonly List<OverburstUIShopSkin.Label> labels = new List<OverburstUIShopSkin.Label>();
    static readonly List<Button> headerClose = new List<Button>();
    static GameObject inventory, equipment, stash, shared, context;
    static TMP_FontAsset body;
    static Font approvedBody;
    static GameObject primaryButton, secondaryButton;

    [MenuItem("OVERBURST/UI/상점 거래창 공용 디자인 적용")]
    public static void Build()
    {
        ItemTypeIconBuilder.RequireIdle();
        labels.Clear(); headerClose.Clear();
        inventory = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponElementIconBuilder.UiRoot + "PF_OverburstInventory_Rpg11.prefab");
        equipment = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponElementIconBuilder.UiRoot + "PF_OverburstEquipment_Rpg11.prefab");
        stash = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponElementIconBuilder.UiRoot + "PF_OverburstStash_Rpg11.prefab");
        shared = AssetDatabase.LoadAssetAtPath<GameObject>(ItemTypeIconBuilder.SharedSlot);
        context = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponElementIconBuilder.UiRoot + "PF_OverburstInventoryContext_Rpg11.prefab");
        body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/ProjectOverburst/Resources/UI/Fonts/ProjectMT/FontAssets/TMP_SpoqaHanSansNeo_Body.asset");
        approvedBody = AssetDatabase.LoadAssetAtPath<Font>("Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/Pretendard_Medium.ttf");
        primaryButton = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonRoot + "Button (Normal M).prefab");
        secondaryButton = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonRoot + "Button (Window).prefab");
        if (!inventory || !equipment || !stash || !shared || !context || !body || !approvedBody || !primaryButton || !secondaryButton) throw new InvalidOperationException("Approved UI sources missing");
        var prior = SlotIdentities(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Merchant(root.transform.Find("MerchantInventoryWindow"));
            Trade(root.transform.Find("TradeWindow"));
            Auxiliary(root.transform.Find("QuestListWindow"), false, false);
            Auxiliary(root.transform.Find("QuestDetailWindow"), true, false);
            Auxiliary(root.transform.Find("SpecialtyListWindow"), false, true);
            Auxiliary(root.transform.Find("SpecialtyDetailWindow"), true, true);
            var tabs = Tabs(root.transform);
            Popups(root.transform);
            var skin = root.GetComponent<OverburstUIShopSkin>() ?? root.AddComponent<OverburstUIShopSkin>();
            skin.Configure(labels.ToArray(), tabs, headerClose.ToArray(), root.transform.Find("TradeWindow/CloseButton").GetComponent<Button>(),
                root.transform.Find("ShopContextBlocker").gameObject, root.transform.Find("TradeFailurePopup").gameObject);
            if (!PrefabUtility.SaveAsPrefabAsset(root, PrefabPath)) throw new InvalidOperationException("Shop save failed");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root); labels.Clear(); headerClose.Clear();
            inventory=equipment=stash=shared=context=primaryButton=secondaryButton=null; body=null; approvedBody=null;
        }
        var after = SlotIdentities(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        if (!prior.OrderBy(x => x.Key).SequenceEqual(after.OrderBy(x => x.Key))) throw new InvalidOperationException("Shop slot identities changed");
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output, "build-results.json"), JsonConvert.SerializeObject(new {
            status = "PASS", prefab = PrefabPath, slots = after.Count, slotIdentitiesPreserved = true,
            slotSize = Cell, badgeSize = WeaponElementIconBuilder.SlotBadgeSize,
            windows = new[] { new { width = MerchantWidth, height = Height, x = MerchantX }, new { width = TradeWidth, height = Height, x = TradeX } }
        }, Formatting.Indented));
    }

    public static Dictionary<string,long> SlotIdentities(GameObject root)
    {
        return root.GetComponentsInChildren<SlotUI>(true).ToDictionary(
            s => s.transform.parent.name + "/" + s.name,
            s => { AssetDatabase.TryGetGUIDAndLocalFileIdentifier(s, out string guid, out long id); return id; });
    }

    static void Merchant(Transform window)
    {
        Window(window, MerchantWidth, MerchantX, window.Find("TopPanel/TitleText").GetComponent<TMP_Text>());
        var top = window.Find("TopPanel"); Px(top, 0, 0, MerchantWidth, 376); NoBackground(top);
        var name = top.Find("MerchantNameText"); Px(name, 32, 152, 368, 28); TextStyle(name, 20, Gold);
        var description = top.Find("DescriptionText"); Px(description, 32, 186, 544, 28); TextStyle(description, 15, Muted);
        var box = top.Find("MerchantInfoBox"); Px(box, 32, 230, 544, 108); NoBackground(box);
        var portrait = box.Find("PortraitPanel");
        var category = portrait.Find("PortraitCategoryText") ?? top.Find("PortraitCategoryText"); category.SetParent(top, false); Px(category, 408, 156, 168, 24); TextStyle(category, 14, Muted, TextAlignmentOptions.MidlineRight);
        portrait.gameObject.SetActive(false);
        var info = box.Find("ReputationInfoPanel"); Px(info, 0, 0, 544, 108); NoBackground(info);
        PlaceText(info, "ReputationLevelText", 0, 0, 142, 24, 16, Gold);
        PlaceText(info, "ReputationGradeText", 154, 0, 390, 24, 14, Muted);
        var bar = info.Find("ReputationExpBar"); Px(bar, 0, 31, 544, 6);
        var bg = bar.GetComponent<Image>(); if (bg) { bg.color = new Color(.12f,.105f,.08f,1); bg.raycastTarget = false; }
        var fill = bar.Find("Fill").GetComponent<Image>(); fill.color = Gold; fill.raycastTarget = false;
        var exp = bar.Find("ExpPercentText") ?? info.Find("ExpPercentText"); exp.SetParent(info, false); Px(exp, 0, 43, 544, 20); TextStyle(exp, 12, Muted);
        info.Find("ReputationEffectsTitleText").gameObject.SetActive(false);
        PlaceText(info, "ReputationDiscountText", 0, 68, 160, 20, 14, Ivory);
        PlaceText(info, "MerchantGoldInfoText", 166, 68, 378, 20, 14, Ivory);
        PlaceText(info, "ReputationStockGradeText", 0, 94, 544, 22, 13, Muted);
        NoBackground(info.Find("Divider"));
        var bottom = window.Find("BottomPanel"); Px(bottom, 0, 0, MerchantWidth, Height); NoBackground(bottom);
        StaticLabel(window, "Stock Heading", "판매품", 32, 346, 544, 28, 17, Gold);
        Rule(window, "Stock Rule", 32, 376, 544);
        Grid(bottom.Find("MerchantInventorySlots") ?? window.Find("Stock Viewport/MerchantInventorySlots"), window, 6, 35, 32, 388, 360, "Stock Viewport");
    }

    static void Trade(Transform window)
    {
        Window(window, TradeWidth, TradeX, window.Find("TitleText").GetComponent<TMP_Text>());
        PlaceText(window, "MerchantOfferLabel", 32, 96, 176, 26, 17, Gold);
        PlaceText(window, "PlayerOfferLabel", 240, 96, 176, 26, 17, Gold);
        Grid(window.Find("MerchantOfferSlots") ?? window.Find("Merchant Offer Viewport/MerchantOfferSlots"), window, 2, 21, 32, 132, 360, "Merchant Offer Viewport");
        Grid(window.Find("PlayerOfferSlots") ?? window.Find("Player Offer Viewport/PlayerOfferSlots"), window, 2, 21, 240, 132, 360, "Player Offer Viewport");
        window.Find("OfferDivider").gameObject.SetActive(false);
        Rule(window, "Summary Rule", 32, 512, 384);
        var summary = window.Find("SummaryPanel"); Px(summary, 32, 524, 384, 38); NoBackground(summary);
        PlaceText(summary, "MerchantValueText", 0, 0, 176, 38, 15, Muted);
        PlaceText(summary, "PlayerValueText", 208, 0, 176, 38, 15, Muted);
        var gold = window.Find("SummaryPanel2"); Px(gold, 32, 574, 384, 48); NoBackground(gold);
        PlaceText(gold, "AutoGoldText", 0, 0, 384, 28, 20, Gold);
        PlaceText(gold, "GoldSummaryText", 0, 30, 384, 22, 14, Muted);
        PlaceButton(window.Find("ConfirmButton"), 32, 632, 384, 60, true);
        PlaceButton(window.Find("ClearButton"), 32, 704, 186, 48);
        PlaceButton(window.Find("CloseButton"), 230, 704, 186, 48);
        PlaceText(window, "StatusText", 32, 756, 384, 18, 12, Muted);
    }

    static void Auxiliary(Transform window, bool detail, bool specialty)
    {
        float width = detail ? TradeWidth : MerchantWidth;
        Window(window, width, detail ? TradeX : MerchantX, window.Find("TitleText").GetComponent<TMP_Text>());
        float y = 148;
        foreach (Transform child in window.CastChildren())
        {
            if (child.name == "Window Chrome" || child.name == "TitleText") continue;
            if (child.GetComponent<Button>()) { PlaceButton(child, width - 264, 692, 232, 56, true); continue; }
            if (child.name.Contains("Status")) { Px(child, 32, 738, width - 64, 28); TextStyle(child, 13, Muted); continue; }
            float height = child.name.Contains("Description") ? 84 : child.childCount > 0 ? 86 : child.name == "BodyText" ? 360 : 34;
            Px(child, 32, y, width - 64, height); NoBackground(child);
            var text = child.GetComponent<TMP_Text>(); if (text) TextStyle(child, child.name.Contains("Title") ? 20 : 15, child.name.Contains("Title") ? Gold : Ivory);
            int row = 0;
            foreach (TMP_Text nested in child.GetComponentsInChildren<TMP_Text>(true).Where(t => t.transform != child))
            { Px(nested.transform, 0, row++ * 27, width - 64, 25); TextStyle(nested.transform, row == 1 ? 16 : 14, row == 1 ? Gold : Muted); }
            y += height + 16;
        }
    }

    static void Window(Transform root, float width, float x, TMP_Text heading)
    {
        var rect = (RectTransform)root; rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
        rect.sizeDelta = new Vector2(width, Height); rect.localScale = Vector3.one; rect.anchoredPosition = new Vector2(x, WindowY);
        NoBackground(root);
        var previous = root.Find("Window Chrome"); if (previous) Object.DestroyImmediate(previous.gameObject);
        var chrome = Rect("Window Chrome", root); chrome.anchorMin = chrome.anchorMax = chrome.pivot = Vector2.one * .5f;
        chrome.anchoredPosition = Vector2.zero; chrome.sizeDelta = new Vector2(width * 2, Height * 2); chrome.localScale = Vector3.one * .5f;
        CopyImage(inventory.GetComponent<Image>(), chrome.gameObject.AddComponent<Image>());
        foreach (string name in new[] { "Borders", "Opaque Content Surface", "Header" })
        {
            var source = inventory.transform.Find(name);
            var clone = Object.Instantiate(source.gameObject, chrome, false); clone.name = name;
        }
        chrome.SetAsFirstSibling();
        Px(chrome.Find("Opaque Content Surface"), 5, 74, width - 10, Height - 80, 2);
        var header = chrome.Find("Header");
        var title = header.Find("Text").GetComponent<Text>(); Px(title.transform, 64, 14, width - 128, 44, 2);
        title.fontSize = 52; title.color = Gold; title.alignment = TextAnchor.MiddleCenter;
        labels.Add(new OverburstUIShopSkin.Label { source = heading, target = title }); heading.enabled = false;
        var close = header.Find("Button (Close)").GetComponent<Button>(); Px(close.transform, width - 58, 20, 36, 36, 2);
        close.onClick = new Button.ButtonClickedEvent(); headerClose.Add(close);
    }

    static OverburstUIShopSkin.Tab[] Tabs(Transform root)
    {
        var host = root.Find("Approved Tabs") as RectTransform ?? Rect("Approved Tabs", root);
        host.anchorMin = host.anchorMax = Vector2.one * .5f; host.pivot = new Vector2(0,1);
        host.anchoredPosition = new Vector2(MerchantX - MerchantWidth/2 + 32, WindowY + Height/2 - 86);
        host.sizeDelta = new Vector2(MerchantWidth - 64,52);
        var layout = host.GetComponent<HorizontalLayoutGroup>() ?? host.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = true;
        var source = stash.transform.Find("Tab Menu/Buttons Group/Tab Button (1)");
        var result = new List<OverburstUIShopSkin.Tab>();
        foreach (string name in new[] { "TradeButton", "QuestButton", "SpecialtyTabButton_01", "SpecialtyTabButton_02" })
        {
            var button = (root.Find(name) ?? host.Find(name)).GetComponent<Button>();
            button.transform.SetParent(host, false); NoBackground(button.transform);
            var old = button.transform.Find("Approved Artwork"); if (old) Object.DestroyImmediate(old.gameObject);
            var art = Object.Instantiate(source.gameObject, button.transform, false); art.name = "Approved Artwork";
            var donorButton = art.GetComponent<Button>(); if (donorButton) Object.DestroyImmediate(donorButton);
            var rt = (RectTransform)art.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; rt.localScale = Vector3.one;
            foreach (var group in art.GetComponents<LayoutGroup>()) Object.DestroyImmediate(group);
            Stretch((RectTransform)art.transform.Find("Active"));
            Stretch((RectTransform)art.transform.Find("Active/Overlay"));
            var overlay=(RectTransform)art.transform.Find("Active/Overlay"); overlay.offsetMin=new Vector2(-4,-4); overlay.offsetMax=new Vector2(4,4);
            var arrow=(RectTransform)art.transform.Find("Active/Arrow"); arrow.sizeDelta=new Vector2(16,11); arrow.anchoredPosition=new Vector2(0,-5);
            var sourceText = button.GetComponentsInChildren<TMP_Text>(true).First();
            var target = art.transform.Find("Text").GetComponent<Text>(); target.fontSize = 18; target.color = Gold; target.text=sourceText.text;
            Stretch(target.rectTransform); target.alignment=TextAnchor.MiddleCenter;
            labels.Add(new OverburstUIShopSkin.Label { source = sourceText, target = target }); sourceText.enabled = false;
            foreach (var image in art.GetComponentsInChildren<Image>(true)) image.raycastTarget = false;
            var hit = art.GetComponent<Image>(); if (hit) { hit.raycastTarget = true; button.targetGraphic = hit; }
            result.Add(new OverburstUIShopSkin.Tab { button = button, selected = art.transform.Find("Active").gameObject });
        }
        host.SetAsLastSibling();
        return result.ToArray();
    }

    static void Grid(Transform content, Transform window, int columns, int count, float x, float y, float visibleHeight, string viewportName)
    {
        if (content.childCount != count) throw new InvalidOperationException("Unexpected stock/offer capacity: " + content.name);
        float width = columns*Cell + (columns-1)*Gap, height = Mathf.CeilToInt((float)count/columns)*(Cell+Gap)-Gap;
        var viewport = window.Find(viewportName) as RectTransform ?? Rect(viewportName, window);
        Px(viewport, x, y, width, visibleHeight);
        var mask = viewport.GetComponent<RectMask2D>() ?? viewport.gameObject.AddComponent<RectMask2D>();
        var hit = viewport.GetComponent<Image>() ?? viewport.gameObject.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true;
        content.SetParent(viewport, false); Px(content, 0, 0, width, height);
        NoBackground(content);
        var grid = content.GetComponent<GridLayoutGroup>(); grid.cellSize = Vector2.one*Cell; grid.spacing = Vector2.one*Gap;
        grid.padding = new RectOffset(); grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = columns; grid.childAlignment = TextAnchor.UpperLeft;
        foreach (SlotUI slot in content.GetComponentsInChildren<SlotUI>(true)) SharedSlot(slot);
        var scroll = viewport.GetComponent<ScrollRect>() ?? viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport; scroll.content = (RectTransform)content; scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.inertia = true; scroll.scrollSensitivity = 42;
        var track = window.Find(viewportName + " Scrollbar") as RectTransform ?? Rect(viewportName + " Scrollbar", window);
        Px(track, x+width+12, y, 4, visibleHeight);
        var image = track.GetComponent<Image>() ?? track.gameObject.AddComponent<Image>(); image.color = new Color(.25f,.23f,.19f,.45f);
        var handle = track.Find("Handle") as RectTransform ?? Rect("Handle", track); Stretch(handle);
        var handleImage = handle.GetComponent<Image>() ?? handle.gameObject.AddComponent<Image>(); handleImage.color = new Color(.60f,.48f,.30f,.85f);
        var bar = track.GetComponent<Scrollbar>() ?? track.gameObject.AddComponent<Scrollbar>();
        bar.handleRect = handle; bar.targetGraphic = handleImage; bar.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = bar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        scroll.verticalNormalizedPosition = 1; bar.SetValueWithoutNotify(1);
    }

    static void SharedSlot(SlotUI slot)
    {
        var visual = slot.transform.Find("Shared Visual");
        if (!visual)
        {
            // Keep the SlotUI component and its local ID: PersistentScene references these 77 identities.
            foreach (Transform child in slot.transform.CastChildren())
            {
                if (child.name == "Weapon Element Badge" || child.name == "Item Type Badge") Object.DestroyImmediate(child.gameObject);
                else child.gameObject.SetActive(false);
            }
            visual = ((GameObject)PrefabUtility.InstantiatePrefab(shared, slot.transform)).transform; visual.name = "Shared Visual";
        }
        var rt = (RectTransform)visual; rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.one*.5f;
        rt.anchoredPosition = Vector2.zero; rt.sizeDelta = Vector2.one*Cell; rt.localScale = Vector3.one;
        ((RectTransform)slot.transform).sizeDelta = Vector2.one*Cell;
        var sample = visual.GetComponent<OverburstUISlotTooltip>(); if (sample) Object.DestroyImmediate(sample);
        var nestedView = visual.GetComponent<OverburstUIItemSlotView>(); if (nestedView) Object.DestroyImmediate(nestedView);
        var legacyGrade = slot.GetComponent<SlotGradeEffect>(); if (legacyGrade) Object.DestroyImmediate(legacyGrade);
        NoBackground(slot.transform);
        var view = slot.GetComponent<OverburstUIItemSlotView>() ?? slot.gameObject.AddComponent<OverburstUIItemSlotView>();
        var icon = visual.Find("Icon").GetComponent<Image>(); var placeholder = visual.Find("Slot Icon").GetComponent<Image>();
        view.Configure(icon, placeholder, visual.Find("Hotkey/Hotkey Text").GetComponent<Text>(), visual.GetComponent<OverburstUISlotGradePreview>());
        var element = visual.Find("Weapon Element Badge").GetComponent<WeaponElementIconView>();
        var type = visual.Find("Item Type Badge").GetComponent<ItemTypeIconView>();
        view.ConfigureElementIcon(element); view.ConfigureTypeIcon(type);
        var serialized = new SerializedObject(slot);
        serialized.FindProperty("iconImage").objectReferenceValue = icon;
        serialized.FindProperty("backgroundImage").objectReferenceValue = visual.GetComponent<Image>();
        serialized.FindProperty("slotBackgroundImage").objectReferenceValue = null;
        serialized.FindProperty("weaponElementIcon").objectReferenceValue = element;
        serialized.FindProperty("itemTypeIcon").objectReferenceValue = type;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void Popups(Transform root)
    {
        var source = context.GetComponentsInChildren<Image>(true).First(i => i.name == "InventoryContextMenu");
        foreach (string path in new[] { "ShopContextBlocker/ShopContextMenu", "ShopContextBlocker/ShopSplitTradePopup", "TradeFailurePopup/PopupPanel" })
        {
            var panel = root.Find(path); var image = panel.GetComponent<Image>(); if (image) CopyImage(source, image);
            var surface=panel.Find("Opaque Content Surface") as RectTransform;
            if(!surface)surface=(RectTransform)Object.Instantiate(inventory.transform.Find("Opaque Content Surface").gameObject,panel,false).transform;
            surface.name="Opaque Content Surface"; Stretch(surface); surface.offsetMin=Vector2.one*24;surface.offsetMax=Vector2.one*-24;surface.SetAsFirstSibling();
            foreach (var text in panel.GetComponentsInChildren<TMP_Text>(true)) { text.font = body; text.fontSize = text.name.Contains("Title") ? 18 : 15; text.color = text.name.Contains("Title") ? Gold : Ivory; text.raycastTarget = false; }
            foreach (var button in panel.GetComponentsInChildren<Button>(true)) StyleButton(button);
        }
        var split = root.Find("ShopContextBlocker/ShopSplitTradePopup");
        split.Find("ButtonRow/OkButton").GetComponentInChildren<TMP_Text>(true).text="확인";
        split.Find("ButtonRow/CancelButton").GetComponentInChildren<TMP_Text>(true).text="취소";
        var menuLayout=root.Find("ShopContextBlocker/ShopContextMenu").GetComponent<VerticalLayoutGroup>();
        if(menuLayout){menuLayout.padding=new RectOffset(24,24,24,24);menuLayout.spacing=4;}
        foreach(var button in root.Find("ShopContextBlocker/ShopContextMenu").GetComponentsInChildren<Button>(true))
        { var element=button.GetComponent<LayoutElement>()??button.gameObject.AddComponent<LayoutElement>(); element.minHeight=element.preferredHeight=56; element.preferredWidth=228; }
        ((RectTransform)split).sizeDelta = new Vector2(400,280);
        foreach(string row in new[]{"InputRow","ButtonRow"})foreach(var group in split.Find(row).GetComponents<LayoutGroup>())Object.DestroyImmediate(group);
        PlaceText(split,"Title",32,32,336,28,20,Gold); PlaceText(split,"HintText",32,70,336,24,15,Muted);
        Px(split.Find("InputRow"),32,112,336,52);
        Px(split.Find("InputRow/DecreaseButton"),0,0,56,52); Px(split.Find("InputRow/SplitTradeAmountInput"),68,0,200,52); Px(split.Find("InputRow/IncreaseButton"),280,0,56,52);
        var input=split.Find("InputRow/SplitTradeAmountInput").GetComponent<TMP_InputField>(); input.textComponent.fontSize=20;
        var inputImage=input.GetComponent<Image>(); if(inputImage){CopyImage(secondaryButton.GetComponent<Image>(),inputImage);inputImage.type=Image.Type.Sliced;inputImage.pixelsPerUnitMultiplier=3;inputImage.color=new Color(.26f,.24f,.20f,1);}
        Px(split.Find("ButtonRow"),32,188,336,60); PlaceButton(split.Find("ButtonRow/OkButton"),0,0,162,60,true); PlaceButton(split.Find("ButtonRow/CancelButton"),174,0,162,60);
        var failure = root.Find("TradeFailurePopup/PopupPanel"); ((RectTransform)failure).sizeDelta = new Vector2(480,272);
        PlaceText(failure,"TitleText",32,32,416,30,20,Gold); PlaceText(failure,"MessageText",32,76,416,90,16,Ivory);
        PlaceButton(failure.Find("ConfirmButton"),132,182,216,60,true);
        root.Find("ShopContextBlocker").SetAsLastSibling(); root.Find("TradeFailurePopup").SetAsLastSibling();
    }

    static void PlaceButton(Transform transform, float x, float y, float width, float height, bool primary=false)
    { Px(transform,x,y,width,height); StyleButton(transform.GetComponent<Button>(),primary); }

    static void StyleButton(Button button, bool primary=false)
    {
        var source = button.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();
        foreach(string old in new[]{"Approved Border","Approved Label","Approved Button Artwork"})
        { var previous=button.transform.Find(old); if(previous)Object.DestroyImmediate(previous.gameObject); }
        foreach(var transition in button.GetComponents<MonoBehaviour>().Where(c=>c is DuloGames.UI.UIHighlightTransition || c is DuloGames.UI.UIPressTransition).ToArray()) Object.DestroyImmediate(transition);
        var hit=button.GetComponent<Image>()??button.gameObject.AddComponent<Image>(); hit.sprite=null; hit.color=Color.clear; hit.enabled=true; hit.raycastTarget=true;
        var art=Object.Instantiate(primary?primaryButton:secondaryButton,button.transform,false); art.name="Approved Button Artwork"; Stretch((RectTransform)art.transform); art.transform.localScale=Vector3.one; art.transform.SetAsFirstSibling();
        // Preserve the native asset's hover and press handlers, while the existing Button owns clicks.
        var donor=art.GetComponent<Button>(); button.transition=donor.transition; button.colors=donor.colors; button.targetGraphic=donor.targetGraphic;
        foreach(var transition in art.GetComponents<MonoBehaviour>().Where(c=>c is DuloGames.UI.UIHighlightTransition || c is DuloGames.UI.UIPressTransition).ToArray())
        {
            var copy=button.gameObject.AddComponent(transition.GetType()); EditorUtility.CopySerialized(transition,copy);
            var so=new SerializedObject(copy); var textTransition=so.FindProperty("m_Transition");
            if(textTransition!=null && textTransition.intValue==4)
            { so.FindProperty("m_NormalColor").colorValue=primary?Ivory:Gold; so.FindProperty("m_HighlightedColor").colorValue=Color.white; so.FindProperty("m_SelectedColor").colorValue=Color.white; so.ApplyModifiedPropertiesWithoutUndo(); }
            Object.DestroyImmediate(transition);
        }
        Object.DestroyImmediate(donor);
        foreach(var scheme in art.GetComponentsInChildren<DuloGames.UI.ColorSchemeElement>(true))Object.DestroyImmediate(scheme);
        foreach(var image in art.GetComponentsInChildren<Image>(true)){image.raycastTarget=false; image.pixelsPerUnitMultiplier=3; if(image.sprite)image.type=Image.Type.Sliced;}
        foreach(string ornament in new[]{"Ornament Left","Ornament Right"}) { var t=art.transform.Find(ornament); if(t)t.gameObject.SetActive(false); }
        if(primary)
        {
            Inset(art.transform.Find("Foreground"),8); art.transform.Find("Foreground").GetComponent<Image>().color=new Color(.32f,.255f,.16f,1);
            Inset(art.transform.Find("Hover Overlay"),9); Inset(art.transform.Find("Press Overlay"),8);
            var top=(RectTransform)art.transform.Find("Foreground/Overlay 1"); top.anchorMin=new Vector2(0,1);top.anchorMax=Vector2.one;top.pivot=new Vector2(.5f,1);top.anchoredPosition=Vector2.zero;top.sizeDelta=new Vector2(0,18);
            Inset(art.transform.Find("Foreground/Overlay 2"),0);
        }
        else
        {
            var background=art.GetComponent<Image>(); background.color=new Color(.55f,.50f,.43f,1);
            Inset(art.transform.Find("Border"),5); var border=art.transform.Find("Border").GetComponent<Image>(); border.color=Color.white; border.pixelsPerUnitMultiplier=1;
            Inset(art.transform.Find("Hover Overlay"),6); Inset(art.transform.Find("Press Overlay"),6);
        }
        var target=art.transform.Find("Text").GetComponent<Text>(); target.font=approvedBody; target.fontSize=primary?19:17; target.color=primary?Ivory:Gold;
        target.alignment=TextAnchor.MiddleCenter; target.raycastTarget=false; target.text=source?source.text:""; Stretch(target.rectTransform); target.rectTransform.offsetMin=new Vector2(12,0);target.rectTransform.offsetMax=new Vector2(-12,0);
        target.horizontalOverflow = HorizontalWrapMode.Wrap; target.verticalOverflow = VerticalWrapMode.Truncate;
        if(source){source.enabled=false; labels.RemoveAll(l=>l.source==source); labels.Add(new OverburstUIShopSkin.Label{source=source,target=target});}
    }

    static void Inset(Transform transform,float inset)
    { var rect=(RectTransform)transform; Stretch(rect);rect.offsetMin=Vector2.one*inset;rect.offsetMax=Vector2.one*-inset; }

    static void StaticLabel(Transform parent,string name,string text,float x,float y,float width,float height,int size,Color color)
    {
        var rect = parent.Find(name) as RectTransform ?? Rect(name,parent); Px(rect,x,y,width,height);
        var label = rect.GetComponent<Text>() ?? rect.gameObject.AddComponent<Text>(); label.font = AssetDatabase.LoadAssetAtPath<Font>("Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/Pretendard_Medium.ttf");
        label.text = text; label.fontSize = size; label.color = color; label.raycastTarget = false;
    }
    static void Rule(Transform parent,string name,float x,float y,float width)
    { var rect=parent.Find(name) as RectTransform ?? Rect(name,parent); Px(rect,x,y,width,.5f); var image=rect.GetComponent<Image>()??rect.gameObject.AddComponent<Image>(); image.color=RuleColor; image.raycastTarget=false; }
    static void PlaceText(Transform parent,string name,float x,float y,float width,float height,int size,Color color)
    { var child=parent.Find(name); Px(child,x,y,width,height); TextStyle(child,size,color); }
    static void TextStyle(Transform transform,int size,Color color,TextAlignmentOptions alignment=TextAlignmentOptions.TopLeft)
    {
        var text=transform.GetComponent<TMP_Text>(); if(!text)return;
        text.font=body; text.fontSize=size; text.color=color; text.alignment=alignment; text.enableWordWrapping=true; text.overflowMode=TextOverflowModes.Truncate; text.raycastTarget=false;
        var targetTransform=transform.Find("Approved Body Text");
        var target=targetTransform ? targetTransform.GetComponent<Text>() : Rect("Approved Body Text",transform).gameObject.AddComponent<Text>();
        Stretch(target.rectTransform); target.font=approvedBody; target.fontSize=size; target.color=color; target.raycastTarget=false;
        target.alignment=alignment==TextAlignmentOptions.MidlineRight ? TextAnchor.MiddleRight : TextAnchor.UpperLeft;
        target.horizontalOverflow=HorizontalWrapMode.Wrap; target.verticalOverflow=VerticalWrapMode.Truncate; target.text=text.text;
        text.enabled=false; labels.RemoveAll(l=>l.source==text); labels.Add(new OverburstUIShopSkin.Label{source=text,target=target});
    }
    static void CopyImage(Image source,Image target)
    { target.sprite=source.sprite; target.type=source.type; target.color=source.color; target.material=source.material; target.preserveAspect=source.preserveAspect; target.fillCenter=source.fillCenter; target.pixelsPerUnitMultiplier=source.pixelsPerUnitMultiplier; target.raycastTarget=source.raycastTarget; target.fillMethod=source.fillMethod;target.fillAmount=source.fillAmount;target.fillOrigin=source.fillOrigin;target.fillClockwise=source.fillClockwise; }
    static void NoBackground(Transform transform)
    { if(transform){var image=transform.GetComponent<Image>(); if(image)image.enabled=false;} }
    static RectTransform Rect(string name,Transform parent)
    { var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent,false); rect.gameObject.layer=parent.gameObject.layer; return rect; }
    static void Px(Transform transform,float x,float y,float width,float height,float scale=1)
    { var rect=(RectTransform)transform; rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1); rect.localScale=Vector3.one; rect.anchoredPosition=new Vector2(x*scale,-y*scale); rect.sizeDelta=new Vector2(width*scale,height*scale); }
    static void Stretch(RectTransform rect)
    { rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero; }
}
