using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class DungeonDebugEntryUI : MonoBehaviour
{
    DungeonDebugEntry entry;
    GameObject panel;
    TMP_InputField level;
    Toggle items;
    TMP_Text status;
    TMP_FontAsset font;
    public static GameObject Attach(Transform parent)
    {
        var existing = parent.Find("DungeonDebugEntryButton");
        if (existing != null) return existing.gameObject;
        var root = new GameObject("DungeonDebugEntryButton", typeof(RectTransform), typeof(Image), typeof(Button));
        root.transform.SetParent(parent, false);
        var view = root.AddComponent<DungeonDebugEntryUI>(); view.Build();
        return root;
    }
    void Build()
    {
        entry = transform.parent.GetComponent<DungeonDebugEntry>() ?? transform.parent.gameObject.AddComponent<DungeonDebugEntry>();
        font = Resources.Load<TMP_FontAsset>("UI/Fonts/ProjectMT/FontAssets/TMP_SpoqaHanSansNeo_Body");
        var rect = (RectTransform)transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(6, 653); rect.sizeDelta = new Vector2(150, 28);
        GetComponent<Image>().color = new Color(.16f, .23f, .29f, .96f);
        Label(transform, "던전 레벨 입장", Vector2.zero, new Vector2(146, 27), 15);
        GetComponent<Button>().onClick.AddListener(Open);
    }
    public void Open()
    {
        if (panel != null) return;
        var canvas = new GameObject("DungeonDebugEntryPanel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas.transform.SetParent(transform.root, false); panel = canvas;
        var canvasRect = (RectTransform)canvas.transform;
        canvasRect.anchorMin = Vector2.zero; canvasRect.anchorMax = Vector2.one;
        canvasRect.offsetMin = canvasRect.offsetMax = Vector2.zero;
        var c = canvas.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.overrideSorting = true; c.sortingOrder = 450;
        var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920,1080); scaler.matchWidthOrHeight = .5f;
        var dim = Box(canvas.transform, "Dim", Vector2.zero, Vector2.zero, new Color(0,0,0,.65f));
        dim.anchorMin = Vector2.zero; dim.anchorMax = Vector2.one; dim.offsetMin = dim.offsetMax = Vector2.zero;
        var body = Box(dim, "Window", Vector2.zero, new Vector2(640, 400), new Color(.08f,.12f,.16f,1));
        Label(body,"던전 밸런스 테스트",new Vector2(0,155),new Vector2(590,45),28);
        Label(body,"입장 레벨  1~100",new Vector2(-120,82),new Vector2(280,40),22);
        var field = Box(body,"Level",new Vector2(150,82),new Vector2(150,48),new Color(.2f,.25f,.3f,1));
        level = field.gameObject.AddComponent<TMP_InputField>();
        var viewport = Box(field,"Viewport",Vector2.zero,new Vector2(130,42),Color.clear); viewport.GetComponent<Image>().raycastTarget=false;
        level.textViewport=viewport;level.textComponent=Label(viewport,"1",Vector2.zero,new Vector2(124,38),24);
        level.contentType=TMP_InputField.ContentType.IntegerNumber;level.characterLimit=3;level.text="1";
        var toggle = Box(body,"GiveItems",new Vector2(-240,6),new Vector2(30,30),new Color(.25f,.3f,.35f));
        items=toggle.gameObject.AddComponent<Toggle>();items.targetGraphic=toggle.GetComponent<Image>();
        var mark=Box(toggle,"Check",Vector2.zero,new Vector2(20,20),new Color(.85f,.72f,.38f));items.graphic=mark.GetComponent<Image>();items.isOn=false;
        Label(body,"던전 레벨과 같은 아이템 얻기",new Vector2(20,6),new Vector2(470,40),21);
        Label(body,"입장 후 주변에 무기 1개 + 장비 7개 드랍\n모델 미완성 무기는 공용 대검 외형으로 표시",new Vector2(0,-62),new Vector2(575,65),17);
        status=Label(body,entry.Status,new Vector2(0,-120),new Vector2(590,45),17);
        Button(body,"취소",new Vector2(-155,-170),Close);
        Button(body,"입장",new Vector2(155,-170),()=>{if(!int.TryParse(level.text,out int n)){status.text="1~100 사이의 레벨을 입력하세요.";return;}if(entry.TryEnter(n,items.isOn))Close();else status.text=entry.Status;});
        GameplayInputBlocker.Block(this);
    }
    void Update(){if(panel!=null&&PlayerInputFacade.Current?.UiCancelPressedThisFrame==true)Close();}
    void OnDisable()=>Close();
    public void Close(){if(panel!=null)Destroy(panel);panel=null;GameplayInputBlocker.Unblock(this);}
    RectTransform Box(Transform parent,string name,Vector2 pos,Vector2 size,Color color)
    {
        var obj=new GameObject(name,typeof(RectTransform),typeof(Image));obj.transform.SetParent(parent,false);
        var rect=(RectTransform)obj.transform;rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=pos;rect.sizeDelta=size;obj.GetComponent<Image>().color=color;return rect;
    }
    TMP_Text Label(Transform parent,string text,Vector2 pos,Vector2 size,int fontSize)
    {
        var obj=new GameObject("Label",typeof(RectTransform),typeof(TextMeshProUGUI));obj.transform.SetParent(parent,false);
        var rect=(RectTransform)obj.transform;rect.anchoredPosition=pos;rect.sizeDelta=size;
        var label=obj.GetComponent<TextMeshProUGUI>();label.font=font;label.fontSize=fontSize;label.text=text;label.color=new Color(.92f,.91f,.85f);label.alignment=TextAlignmentOptions.Center;label.raycastTarget=false;return label;
    }
    void Button(Transform parent,string title,Vector2 pos,UnityEngine.Events.UnityAction action)
    {var rect=Box(parent,title,pos,new Vector2(260,42),new Color(.2f,.28f,.33f));Label(rect,title,Vector2.zero,new Vector2(250,38),20);rect.gameObject.AddComponent<Button>().onClick.AddListener(action);}
}
