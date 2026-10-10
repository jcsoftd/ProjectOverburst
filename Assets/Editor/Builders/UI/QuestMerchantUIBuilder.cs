using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class QuestMerchantUIBuilder
{
    public const string Folder = "Assets/ProjectOverburst/Resources/UI/Quests";
    public const string Prefab = Folder + "/PF_QuestMerchantUI.prefab";
    private static Font body, title;
    public static Material TintMaterial(Color tint)
    {
        string path=Folder+"/Tint_"+ColorUtility.ToHtmlStringRGB(tint)+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material){material=new Material(Shader.Find("OVERBURST/UI Merchant Portrait"));AssetDatabase.CreateAsset(material,path);}
        material.SetFloat("_Brightness",1);material.SetFloat("_Contrast",1);
        material.SetVector("_RgbTint",new Vector4(tint.r,tint.g,tint.b,1));
        EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);return material;
    }
    private static Color Gold => C("D9B357");
    private static Color Ivory => C("BDB39E");
    private static Color Muted => C("A3987B");
    [MenuItem("OVERBURST/UI/상인 퀘스트 시안 적용")]
    public static string Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Use idle EditMode.");
        body = AssetDatabase.LoadAssetAtPath<Font>("Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/Pretendard_Medium.ttf");
        title = AssetDatabase.LoadAssetAtPath<Font>("Assets/ProjectOverburst/Resources/UI/Fonts/Quest/NotoSerifKR_Semibold.ttf");
        if (!body || !title) throw new InvalidOperationException("Approved fonts are missing.");
        EnsureFolder(Folder);
        QuestMerchantPortraitBuilder.Build();
        QuestMerchantContentBuilder.Build();
        bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
        var root = existing ? PrefabUtility.LoadPrefabContents(Prefab) : new GameObject("QuestMerchantUI", typeof(RectTransform));
        try
        {
            var rect = (RectTransform)root.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = Vector2.one * .5f;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var view = root.GetComponent<QuestMerchantUI>() ?? root.AddComponent<QuestMerchantUI>();
            Approach(view, rect);
            var list = Frame(rect, "Merchant List", 112, 88, false, "상인 의뢰", out var listClose);
            view.merchantList = list.gameObject; view.listHeaderClose = listClose;
            SpriteRect(list, "Portrait Border", 32, 104, 104, 154, "e4a991a559ef25445ae21424b7a86668", Color.white, 4.5f);
            var photoRoot = Rect(list, "Portrait", 36, 108, 96, 146);
            var photo = photoRoot.GetComponent<RawImage>() ?? photoRoot.gameObject.AddComponent<RawImage>();
            photo.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/WeaponMerchantPortrait.png");
            photo.material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/MerchantPortrait.mat");
            view.merchantPortrait = photo;
            photo.raycastTarget = false; photo.uvRect = new Rect(169f/426, 1-(112+146f)/(426f*800/600), 96f/426, 146f/(426f*800/600));
            var fade = Gradient(photoRoot, "Portrait Fade", 0, 80.3f, 96, 65.7f, new Color(.035f,.035f,.024f,0), C("090906B8"));
            view.merchantName = Label(list, "Merchant Name", 160, 109, 384, 32, "무기상인", 23, C("D2B778"), true);
            view.merchantLocation = Label(list, "Merchant Location", 160, 146, 384, 22, "메인 마을 · 대장간", 14, C("A79B81"));
            view.greeting = Label(list, "Greeting", 160, 185, 384, 54, "마을 밖 길목을 정리해 줄\n모험가를 찾고 있네.", 16, C("BBB19B"));
            Spacing(view.greeting,27);
            Rule(list, "Profile Rule", 32, 281.9f, 544);
            Label(list, "List Heading", 32, 306.9f, 350, 30, "의뢰 목록", 21, C("D0C3A5"), true);
            view.listCount = Label(list, "List Count", 390, 310.9f, 186, 22, "수락 가능 1", 13, C("B3A07A")); view.listCount.alignment = TextAnchor.MiddleRight;
            Label(list, "List Description", 32, 346.9f, 544, 22, "의뢰를 선택하면 오른쪽에서 내용을 확인할 수 있습니다.", 14, C("9C927E"));
            var listScroll = Scroll(list, "Quest Rows", 32, 386.9f, 544, 279.1f);
            view.merchantRows = listScroll.content; view.merchantRowTemplate = Row(listScroll.content, "Template", false);
            Rule(list, "Footer Rule", 32, 679.1f, 544);
            view.listHint = Label(list, "List Hint", 32, 712, 350, 22, "Esc  닫기", 13, C("93846A"));
            view.listClose = Action(list, "List Close", 440, 698, 136, 52, "닫기", false, out _);
            var detail = Frame(rect, "Merchant Detail", 736, 88, false, "의뢰 상세", out var detailClose);
            view.merchantDetail = detail.gameObject; view.merchantInfo = Detail(detail, false, detailClose);
            var journal = Frame(rect, "Journal", 501.3f, 178, true, "퀘스트 목록", out var journalClose);
            view.journal = journal.gameObject;
            SpriteRect(journal, "Vertical Separator", 331.1f, 83.6f, 5.5f, 616.4f, "bdc3392241ba15d45b7fd9b77ba14f15", Color.white, 2/1.1f);
            var journalScroll = Scroll(journal, "Quest Rows", 22, 115, 290, 585);
            view.journalRows = journalScroll.content; view.journalRowTemplate = Row(journalScroll.content, "Template", true);
            view.journalInfo = Detail(journal, true, journalClose);
            DialogueAndReport(view, rect);
            var tracker = Rect(rect, "Tracker", 1526, 280, 348, 130);
            view.tracker = tracker.gameObject;
            view.trackerTitle = Label(tracker, "Title", 0, 0, 340, 32, "첫 사냥", 22, C("D9BD84"), true);
            view.trackerProgress = Label(tracker, "Progress", 0, 38, 340, 85, "야외 몬스터 처치  0 / 10", 15, C("BDB59E"));
            view.approach.SetActive(false); view.merchantList.SetActive(false); view.merchantDetail.SetActive(false);
            view.journal.SetActive(false); view.dialogue.SetActive(false); view.reportPopup.SetActive(false); view.tracker.SetActive(false);
            if (!PrefabUtility.SaveAsPrefabAsset(root, Prefab)) throw new InvalidOperationException("Quest UI prefab save failed.");
        }
        finally
        {
            if (existing) PrefabUtility.UnloadPrefabContents(root); else UnityEngine.Object.DestroyImmediate(root);
            body = title = null;
        }
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출/Implementation/20261011_QuestMerchant/Evidence/quest-ui-assets.json"));
        File.WriteAllText(output, JsonConvert.SerializeObject(new { status = "BUILT", prefab = Prefab,
            merchant = new { width = 608, height = 776, left = 112, top = 88, detailLeft = 736 },
            journal = new { width = 917.4f, height = 724, left = 501.3f, top = 178 }, source = "Accepted HTML/native RPG11 artwork. Runtime QA pending." }, Formatting.Indented));
        return output;
    }
    private static void Approach(QuestMerchantUI view, RectTransform root)
    {
        var menu = Rect(root, "Approach", 1060, 484, 370, 286.5f); view.approach = menu.gameObject;
        Gradient(menu, "Background", 0, 0, 370, 286.5f, C("11120FE8"), C("11120FD2"));
        Border(menu, "Border", 0, 0, 370, 286.5f, C("77603C77"));
        view.approachName = Label(menu, "Name", 23, 15, 230, 28, "무기상인", 21, C("D6B781"), true);
        view.approachLocation = Label(menu, "Location", 270, 20, 77, 20, "대장간", 13, C("A39373")); view.approachLocation.alignment = TextAnchor.MiddleRight;
        Rule(menu, "Name Rule", 14, 57, 342);
        view.approachButtons = new Button[3]; view.approachSelections = new GameObject[3]; view.approachKeys = new GameObject[3];
        string[] names = { "대화", "거래", "퀘스트" };
        for (int i = 0; i < 3; i++)
        {
            var row = Rect(menu, "Option " + i, 14.9f, 70.71f + i*59, 340.2f, 53);
            var bg = Solid(row, "Background", 0, 0, 340.2f, 53, C("10100DE6"));
            bg.raycastTarget = true;
            var button = row.GetComponent<Button>() ?? row.gameObject.AddComponent<Button>(); button.targetGraphic = bg;
            view.approachButtons[i] = button;
            var hover = row.GetComponent<QuestMerchantOption>() ?? row.gameObject.AddComponent<QuestMerchantOption>(); hover.option = i;
            var selected = Rect(row, "Selected", 0, 0, 340.2f, 53);
            Gradient(selected, "Fill", 0, 0, 340.2f, 53, C("352919B8"), C("4A321CD4")).horizontal = true;
            Border(selected, "Border", 0, 0, 340.2f, 53, C("C0A06A")); view.approachSelections[i] = selected.gameObject;
            var icon = Rect(row, "Icon", 14, 15, 23, 23).GetComponent<QuestUiLineIcon>() ?? row.Find("Icon").gameObject.AddComponent<QuestUiLineIcon>(); icon.kind = i; icon.color = C("A38C63"); icon.raycastTarget = false;
            if(!icon.GetComponent<CanvasRenderer>())icon.gameObject.AddComponent<CanvasRenderer>();
            Label(row, "Label", 51, 9, 210, 35, names[i], 21, C("B9AB8E"));
            var key = Rect(row, "F", 300, 13, 27, 27); view.approachKeys[i] = key.gameObject;
            Solid(key, "Fill", 0, 0, 27, 27, C("342817")); Border(key, "Border", 0, 0, 27, 27, C("C6AD79"));
            var keyText = Label(key, "Text", 0, 0, 27, 27, "F", 17, C("F0D9AA")); keyText.alignment = TextAnchor.MiddleCenter;
            if (i == 2) { view.approachBadge = Label(row, "Badge", 221, 15, 70, 23, "새 의뢰 1", 12, C("A69778")); view.approachBadge.alignment = TextAnchor.MiddleRight; }
        }
        Label(menu, "Hint", 22, 255, 326, 18, "휠 선택  ·  클릭 / F 실행", 13, C("8E8066"));
    }
    private static QuestMerchantUI.DetailView Detail(RectTransform frame, bool journal, Button close)
    {
        float x = journal ? 366 : 32, y = journal ? 115 : 104, width = journal ? 513.4f : 544;
        float rewardY = journal ? 487.1f : 539.1f;
        var view = new QuestMerchantUI.DetailView { headerClose = close, npc = !journal };
        var scroll = Scroll(frame, "Details", x, y, width, rewardY-y-25);
        view.scroll = scroll; var content = scroll.content;
        Label(content, "Exclamation", 0, 0, 15, 38, "!", 36, C("C8A56A"), true);
        view.title = Label(content, "Title", 30, 0, width-130, 38, "첫 사냥", 24, C("D0C3A5"), true);
        view.status = Label(content, "Status", width-100, 9, 96, 22, "수락 가능", 13, Muted); view.status.alignment = TextAnchor.MiddleRight;
        view.body = Label(content, "Body", 0, 62, width-4, 134, "", journal ? 18 : 17, Ivory);
        view.bodySecond = Label(content, "Body Second", 0, 145, width-4, 100, "", journal ? 18 : 17, Ivory);
        if(journal) view.body.fontSize = view.bodySecond.fontSize = 35;
        Spacing(view.body, journal ? 28.875f : 28.9f); Spacing(view.bodySecond, journal ? 28.875f : 28.9f);
        view.objectiveRule = Rule(content, "Objectives Rule", 0, 221.6f, width-4);
        view.counts = Label(content, "Counts", 0, 242.6f, 72, 86, "0 / 10\n—", 16, C("AA9C7E")); Spacing(view.counts, 30);
        view.objectives = Label(content, "Objectives", 72, 242.6f, width-76, 86, "야외 몬스터 처치\n무기상인에게 보고", 17, C("C8BDA3")); Spacing(view.objectives, 30);
        var rewards = Rect(frame, "Rewards", x, rewardY, width, 126.9f);
        SpriteRect(rewards, "Glow", 0, 32, width, 94.9f, "af89713373583344a85b8d2a93dfb5fa", C("4A1714B3"), 1, false);
        view.rewardsTitle = Label(rewards, "Title", 0, 0, width, 32, "보상", 22, C("B99757"), true); view.rewardsTitle.alignment = TextAnchor.MiddleCenter;
        Rule(rewards, "Rule", 0, 42, width);
        float left = (width-310)/2;
        Reward(rewards, "Gold", left, true, out view.gold); Reward(rewards, "Experience", left+168, false, out view.experience);
        float footer = journal ? 627.1f : 679.1f, buttons = journal ? 646 : 698;
        Rule(frame, "Footer Rule", x, footer, width);
        view.secondary = Action(frame, "Secondary", journal ? 743.4f : 32, buttons, 136, 52, journal ? "닫기" : "상세 닫기", false, out _);
        view.primary = Action(frame, "Primary", journal ? 366 : 388, buttons, journal ? 136 : 188, 52, journal ? "목표 표시" : "의뢰 수락", !journal, out view.primaryLabel);
        return view;
    }
    private static void Reward(RectTransform parent, string name, float x, bool gold, out Text value)
    {
        var root = Rect(parent, name, x, 62.9f, 142, 58);
        Solid(root, "Background", 0, 0, 58, 58, C("141311"));
        SpriteRect(root, "Slot", 0, 0, 58, 58, "336cfcae5676656479a1817215e69f0d", Color.white, 2);
        if (gold) SpriteRect(root, "Icon", 9, 9, 40, 40, "a917f2c40c9c4b2da0abdb51243d1001", Color.white, 1, false);
        else { var exp = Label(root, "EXP", 0, 0, 58, 58, "EXP", 18, C("C4AA77"), true); exp.alignment = TextAnchor.MiddleCenter; }
        Label(root, "Label", 70, 5, 72, 18, gold ? "골드" : "경험치", 13, C("A99B80"));
        value = Label(root, "Value", 70, 27, 90, 26, gold ? "100 <size=13>G</size>" : "30", 22, C("E2CF9F"));
    }
    private static QuestRowView Row(RectTransform parent, string name, bool journal)
    {
        float width = journal ? 278.3f : 540, height = journal ? 62 : 118;
        var root = Rect(parent, name, journal ? 0 : 2, journal ? 0 : 2, width, height);
        var row = root.GetComponent<QuestRowView>() ?? root.gameObject.AddComponent<QuestRowView>();
        var baseColor=Solid(root,"Row Fill",0,0,width,height,C(journal ? "171412" : "181513"));baseColor.transform.SetAsFirstSibling();
        var background = SpriteRect(root, "Background", 0, 0, width, height, "354f7a5b2254fe24a9cfb69d58a9425b", Color.white, 2);
        background.raycastTarget = true;
        row.button = root.GetComponent<Button>() ?? root.gameObject.AddComponent<Button>(); row.button.targetGraphic = background;
        var selected = Rect(root, "Selected", journal ? -22 : -8, journal ? -22 : -8, width+(journal ? 44 : 16), height+(journal ? 44 : 16));
        var selectedFill=Gradient(selected,"Fill",journal ? 22 : 8,journal ? 22 : 8,width,height,C("35251DE0"),C("1E1917E0"));selectedFill.transform.SetAsFirstSibling();
        SpriteRect(selected, "Glow", 0, 0, selected.sizeDelta.x, selected.sizeDelta.y, "3e1e5dd978b37b645a10a63a31c0d94c", new Color(journal ? .82f : .64f,journal ? .82f : .64f,journal ? .82f : .64f,journal ? 1 : .7f), journal ? 60/33f : 60/16f);
        row.selection = selected.gameObject;
        row.title = Label(root, "Title", journal ? 19.8f : 18, journal ? 9 : 20, width-130, journal ? 24 : 28, "첫 사냥", journal ? 18 : 21, C("CFB381"), true);
        row.status = Label(root, "Status", journal ? 19.8f : width-126, journal ? 33 : 37, journal ? width-40 : 108, 20, "수락 가능", 13, C("B8A276"));
        if (!journal)
        {
            row.summary = Label(root, "Summary", 18, 52, width-130, 22, "야외 몬스터 처치 · 무기상인에게 보고", 15, C("A69B85"));
            row.reward = Label(root, "Rewards", 18, 78, width-130, 20, "100 G  ·  경험치 30", 13, C("B4A27C"));
            row.status.alignment = TextAnchor.MiddleRight;
            row.arrow = Label(root, "Arrow", width-40, 67, 22, 26, "›", 26, C("B29664"), true).gameObject;
        }
        else
        {
            row.arrow = SpriteRect(root, "Arrow", width-12.65f, 18, 18.15f, 25.85f, "44de6c7c6430925428469b299b44be29", Color.white, 1, false).gameObject;
            row.arrowOnlyWhenSelected = true;
        }
        root.gameObject.SetActive(false); return row;
    }
    private static void DialogueAndReport(QuestMerchantUI view, RectTransform root)
    {
        var dialogueRoot = Rect(root, "Dialogue", 0, 0, 1920, 1080); view.dialogue = dialogueRoot.gameObject;
        Solid(dialogueRoot, "Scrim", 0, 0, 1920, 1080, new Color(0,0,0,.6f)).raycastTarget = true;
        var dialogue = Rect(dialogueRoot, "Window", 670, 375.2f, 580, 329.6f);
        ModalBackground(dialogue);
        view.dialogueName = Label(dialogue, "Title", 36.18f, 34.18f, 507.64f, 38, "무기상인", 26, Gold, true); view.dialogueName.alignment = TextAnchor.MiddleCenter;
        Rule(dialogue, "Heading Rule", 36.18f, 97.35f, 507.64f);
        view.dialogueBody = Label(dialogue, "Body", 36.18f, 121.36f, 507.64f, 57.8f, "", 17, Ivory); Spacing(view.dialogueBody, 28.9f); view.dialogueBody.alignment = TextAnchor.UpperCenter;
        view.dialoguePage = Label(dialogue, "Page", 36.18f, 199.15f, 507.64f, 17.27f, "01 / 02", 14, C("B49A6B")); view.dialoguePage.alignment = TextAnchor.MiddleCenter;
        view.dialogueSecondary = Action(dialogue, "Secondary", 118, 241.42f, 136, 52, "마치기", false, out _);
        view.dialoguePrimary = Action(dialogue, "Primary", 274, 241.42f, 188, 52, "계속 듣기", true, out view.dialoguePrimaryLabel);
        var popupRoot = Rect(root, "Report", 0, 0, 1920, 1080); view.reportPopup = popupRoot.gameObject;
        Solid(popupRoot, "Scrim", 0, 0, 1920, 1080, new Color(0,0,0,.6f)).raycastTarget = true;
        var popup = Rect(popupRoot, "Window", 670, 337.36f, 580, 405.27f); ModalBackground(popup);
        var heading = Label(popup, "Title", 36.18f, 34.18f, 507.64f, 38, "의뢰 보고", 26, Gold, true); heading.alignment = TextAnchor.MiddleCenter;
        Rule(popup, "Heading Rule", 36.18f, 97.35f, 507.64f);
        view.popupMessage = Label(popup, "Message", 36.18f, 121.35f, 507.64f, 57.8f, "", 17, Ivory); Spacing(view.popupMessage, 28.9f); view.popupMessage.alignment = TextAnchor.UpperCenter;
        var rewards = Rect(popup, "Rewards", 36.18f, 199.13f, 507.64f, 95.98f);view.popupRewards=rewards.gameObject;
        SpriteRect(rewards, "Glow", 0, 0, 507.64f, 95.98f, "af89713373583344a85b8d2a93dfb5fa", C("4A1714B3"), 1, false);
        Rule(rewards, "Top Rule", 0, 0, 507.64f); Rule(rewards, "Bottom Rule", 0, 95, 507.64f);
        Reward(rewards, "Gold", 97.82f, true, out view.popupGold); Reward(rewards, "Experience", 267.82f, false, out view.popupExperience);
        ((RectTransform)rewards.Find("Gold")).anchoredPosition = new Vector2(97.82f, -18);
        ((RectTransform)rewards.Find("Experience")).anchoredPosition = new Vector2(267.82f, -18);
        view.popupCancel = Action(popup, "Cancel", 118, 317.1f, 136, 52, "돌아가기", false, out _);
        view.popupConfirm = Action(popup, "Confirm", 274, 317.1f, 188, 52, "보고 완료", true, out _);
    }
    private static void ModalBackground(RectTransform root)
    {
        Solid(root, "Background", 0, 0, root.sizeDelta.x, root.sizeDelta.y, C("11120F"));
        SpriteRect(root, "Border", 0, 0, root.sizeDelta.x, root.sizeDelta.y, "e4a991a559ef25445ae21424b7a86668", Color.white, 2.25f);
    }
    private static RectTransform Frame(Transform parent, string name, float x, float y, bool journal, string heading, out Button close)
    {
        float width = journal ? 917.4f : 608, height = journal ? 724 : 776;
        var root = Rect(parent, name, x, y, width, height); int index = 0;
        foreach (var n in journal ? QuestMerchantArtwork.Journal : QuestMerchantArtwork.Merchant)
        {
            if (!journal && index == 2) Solid(root, "Content Surface", 5, 74, width-10, height-80, C("0C0D0C"));
            var image = SpriteRect(root, "Artwork " + index++, n.x, n.y, n.width, n.height, n.guid, C(n.color), n.pixelsPerUnit, n.sliced);
            if (n.flip) { image.rectTransform.anchoredPosition += Vector2.right*n.width; image.rectTransform.localScale = new Vector3(-1,1,1); }
        }
        var titleText = Label(root, "Window Title", 64, 14, width-128, 44, heading, 26, Gold, true); titleText.alignment = TextAnchor.MiddleCenter;
        var hit = Rect(root, "Close", width-58, 20, 36, 36); var graphic = hit.GetComponent<Image>() ?? hit.gameObject.AddComponent<Image>(); graphic.color = Color.clear;
        close = hit.GetComponent<Button>() ?? hit.gameObject.AddComponent<Button>(); close.targetGraphic = graphic;
        return root;
    }
    private static ScrollRect Scroll(Transform parent, string name, float x, float y, float width, float height)
    {
        var viewport = Rect(parent, name, x, y, width, height);
        var image = viewport.GetComponent<Image>() ?? viewport.gameObject.AddComponent<Image>(); image.color = Color.clear; image.raycastTarget = true;
        if (!viewport.GetComponent<RectMask2D>()) viewport.gameObject.AddComponent<RectMask2D>();
        var content = Rect(viewport, "Content", 0, 0, width, height);
        var scroll = viewport.GetComponent<ScrollRect>() ?? viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 28;
        return scroll;
    }
    public static Button Action(Transform parent, string name, float x, float y, float width, float height, string text, bool primary, out Text label)
    {
        var rect = Rect(parent, name, x, y, width, height);
        var background = Gradient(rect, "Fill", 0, 0, width, height, C(primary ? "563029" : "2B2822"), C(primary ? "2C1B17" : "151512")); background.raycastTarget = true;
        SpriteRect(rect, "Texture", -1, -1, width+2, height+2, "468e8c256d2af164fa076570ba231c60", primary ? C("C8393952") : new Color(1,1,1,.2f), 1.8f);
        SpriteRect(rect, "Border", -1, -1, width+2, height+2, "6a95631b6ee2c494e8fccf4556b712a4", new Color(1,1,1,.85f), 2);
        label = Label(rect, "Label", 0, 0, width, height, text, primary ? 20 : 17, C(primary ? "EAD6B6" : "DCC9AA"), true); label.alignment = TextAnchor.MiddleCenter;
        var button = rect.GetComponent<Button>() ?? rect.gameObject.AddComponent<Button>(); button.targetGraphic = background;
        var colors = button.colors; colors.disabledColor = Color.white; colors.highlightedColor = new Color(1.2f,1.2f,1.2f,1); colors.pressedColor = new Color(.9f,.9f,.9f,1); button.colors = colors;
        if(!rect.GetComponent<QuestUiButtonOpacity>())rect.gameObject.AddComponent<QuestUiButtonOpacity>();
        return button;
    }
    public static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
    {
        var rect = parent.Find(name) as RectTransform;
        if (!rect) { rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); }
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0,1); rect.anchoredPosition = new Vector2(x,-y);
        rect.sizeDelta = new Vector2(width,height); rect.localScale = Vector3.one; return rect;
    }
    public static Text Label(Transform parent, string name, float x, float y, float width, float height, string text, int size, Color color, bool heading = false)
    {
        var rect = Rect(parent,name,x,y,width,height); var label = rect.GetComponent<Text>() ?? rect.gameObject.AddComponent<Text>();
        label.font = heading ? title ?? AssetDatabase.LoadAssetAtPath<Font>("Assets/ProjectOverburst/Resources/UI/Fonts/Quest/NotoSerifKR_Semibold.ttf") : body ?? AssetDatabase.LoadAssetAtPath<Font>("Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/Pretendard_Medium.ttf"); label.fontSize = size*2; label.color = color;
        label.text = System.Text.RegularExpressions.Regex.Replace(text,"<size=(\\d+)>",m=>"<size="+(int.Parse(m.Groups[1].Value)*2)+">");
        rect.localScale = Vector3.one*.5f;rect.sizeDelta = new Vector2(width*2,height*2);
        label.raycastTarget = false; label.supportRichText = true; label.alignment = TextAnchor.UpperLeft;
        label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Overflow; return label;
    }
    private static void Spacing(Text label, float pixels)
    { label.lineSpacing = label.font.lineHeight > 0 ? pixels * label.font.fontSize / (label.font.lineHeight * label.fontSize * label.rectTransform.localScale.y) : 1.3f; }
    public static Image Solid(Transform parent, string name, float x, float y, float width, float height, Color color)
    { var rect = Rect(parent,name,x,y,width,height); var image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>(); image.color=color; image.raycastTarget=false; return image; }
    public static Image SpriteRect(Transform parent,string name,float x,float y,float width,float height,string guid,Color color,float ppu=2,bool sliced=true)
    {
        var image = Solid(parent,name,x,y,width,height,color);
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid));
        if (!image.sprite) throw new InvalidOperationException("Missing accepted artwork: " + guid);
        if(color.r!=1 || color.g!=1 || color.b!=1){image.material=TintMaterial(color);image.color=new Color(1,1,1,color.a);}else image.material=null;
        image.type = sliced ? Image.Type.Sliced : Image.Type.Simple; image.pixelsPerUnitMultiplier = ppu; return image;
    }
    public static RectTransform Rule(Transform parent,string name,float x,float y,float width) => Solid(parent,name,x,y,width,1,C("65513555")).rectTransform;
    private static void Border(Transform parent,string name,float x,float y,float width,float height,Color color)
    { Solid(parent,name+" Top",x,y,width,1,color); Solid(parent,name+" Bottom",x,y+height-1,width,1,color); Solid(parent,name+" Left",x,y,1,height,color); Solid(parent,name+" Right",x+width-1,y,1,height,color); }
    private static QuestUiGradient Gradient(Transform parent,string name,float x,float y,float width,float height,Color top,Color bottom)
    { var rect=Rect(parent,name,x,y,width,height); if(!rect.GetComponent<CanvasRenderer>())rect.gameObject.AddComponent<CanvasRenderer>(); var graphic=rect.GetComponent<QuestUiGradient>() ?? rect.gameObject.AddComponent<QuestUiGradient>(); graphic.top=top; graphic.bottom=bottom; graphic.raycastTarget=false; return graphic; }
    public static Color C(string hex) => ColorUtility.TryParseHtmlString("#"+hex,out var color) ? color : throw new ArgumentException(hex);
    private static void EnsureFolder(string path)
    { string parent="Assets"; foreach(string part in path.Split('/').Skip(1)) { string next=parent+"/"+part; if(!AssetDatabase.IsValidFolder(next))AssetDatabase.CreateFolder(parent,part);parent=next; } }
}
