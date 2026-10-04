using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 임시 무대의 절차형 메시/재질을 전투 소유자가 해제한다. 기존 씬/재질을 변경하지 않는다.
public sealed class CrustaspikanArenaVisuals
{
    private readonly List<Object> resources = new List<Object>();
    public Material Material(Color color, bool unlit = false)
    {
        var shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var material = new Material(shader) { name = "Crustaspikan Temporary Material" };
        material.SetColor("_BaseColor", color); material.color = color; resources.Add(material); return material;
    }
    public GameObject BuildArena(Transform parent, float radius)
    {
        var root = new GameObject("격자 원형 보스방 · 임시"); root.transform.SetParent(parent, false);
        int n = 128; var verts = new Vector3[n + 1]; var tris = new int[n * 3];
        for (int i = 0; i < n; i++)
        { float a = i * Mathf.PI * 2f / n; verts[i + 1] = new Vector3(Mathf.Sin(a),0,Mathf.Cos(a)) * radius;
            tris[i * 3] = 0; tris[i * 3 + 1] = i + 1; tris[i * 3 + 2] = (i + 1) % n + 1; }
        var floor = MeshObject("Arena Floor", root.transform, verts, tris, Material(new Color(.16f,.2f,.23f)));
        floor.layer = LayerMask.NameToLayer("Ground") >= 0 ? LayerMask.NameToLayer("Ground") : 0;
        floor.AddComponent<MeshCollider>().sharedMesh = floor.GetComponent<MeshFilter>().sharedMesh;
        var minor = new List<Vector3>(); var major = new List<Vector3>();
        for (int i = -(int)radius; i <= (int)radius; i++)
        {
            float h = Mathf.Sqrt(Mathf.Max(0,radius * radius - i * i)); var line = i % 5 == 0 ? major : minor;
            line.Add(new Vector3(i,.018f,-h)); line.Add(new Vector3(i,.018f,h));
            line.Add(new Vector3(-h,.018f,i)); line.Add(new Vector3(h,.018f,i));
        }
        Lines("1m Grid", root.transform, minor, Material(new Color(.27f,.37f,.4f),true));
        Lines("5m Grid", root.transform, major, Material(new Color(.37f,.63f,.68f),true));
        for (int i = 0; i < 64; i++)
        {
            float a = i * Mathf.PI * 2 / 64;
            var wall = new GameObject("Arena Boundary " + i); wall.transform.SetParent(root.transform,false);
            wall.transform.localPosition = new Vector3(Mathf.Sin(a) * radius,1.5f,Mathf.Cos(a) * radius);
            wall.transform.localRotation = Quaternion.Euler(0,a * Mathf.Rad2Deg,0);
            var box = wall.AddComponent<BoxCollider>(); box.size = new Vector3(2.1f,3f,.5f);
        }
        Ring("Arena Rim", root.transform, radius, .05f, Material(new Color(.18f,.8f,.9f),true), false);
        var light = new GameObject("Arena Fill Light").AddComponent<Light>(); light.transform.SetParent(root.transform,false);
        light.transform.localPosition = new Vector3(0,12,0); light.type = LightType.Point; light.range = 65f; light.intensity = 18f;
        light.shadows = LightShadows.None;
        return root;
    }
    public GameObject Portal(Transform parent, Vector3 localPosition, Color color, string label)
    {
        var root = new GameObject(label); root.transform.SetParent(parent,false); root.transform.localPosition = localPosition;
        Ring("Portal Ring", root.transform, 1.4f, .12f, Material(color,true), true);
        var inner = new GameObject("Inner Ring"); inner.transform.SetParent(root.transform,false);
        inner.transform.localPosition = new Vector3(0,1.6f,0); inner.transform.localRotation = Quaternion.Euler(0,0,20);
        Ring("Inner Glow", inner.transform, 1.1f, .05f, Material(color * .7f,true), true, false);
        var light = root.AddComponent<Light>(); light.type = LightType.Point; light.range = 6f; light.intensity = 4; light.color = color;
        return root;
    }
    public GameObject Landing(Transform parent, Vector3 position, float radius)
    {
        var go = new GameObject("정예 투척 착지 경고"); go.transform.SetParent(parent,false); go.transform.position = position + Vector3.up * .04f;
        Ring("Landing Ring", go.transform, radius, .10f, Material(new Color(1f,.2f,.08f),true), false);
        return go;
    }
    private GameObject MeshObject(string name, Transform parent, Vector3[] vertices, int[] triangles, Material material)
    {
        var mesh = new Mesh { name = name }; mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds(); resources.Add(mesh);
        var go = new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer)); go.transform.SetParent(parent,false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterial = material; return go;
    }
    private void Lines(string name, Transform parent, List<Vector3> points, Material material)
    {
        var mesh = new Mesh { name = name }; mesh.SetVertices(points);
        var indices = new int[points.Count]; for (int i=0;i<indices.Length;i++) indices[i]=i;
        mesh.SetIndices(indices,MeshTopology.Lines,0); mesh.RecalculateBounds(); resources.Add(mesh);
        var go = new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer)); go.transform.SetParent(parent,false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterial = material;
    }
    private static void Ring(string name, Transform parent, float radius, float width, Material material, bool vertical, bool raise = true)
    {
        var go = new GameObject(name); go.transform.SetParent(parent,false); if (vertical && raise) go.transform.localPosition=Vector3.up*1.6f;
        var line = go.AddComponent<LineRenderer>(); line.useWorldSpace=false; line.loop=true; line.widthMultiplier=width; line.sharedMaterial=material;
        line.positionCount=96;
        for(int i=0;i<96;i++){float a=i*Mathf.PI*2/96;line.SetPosition(i,vertical?new Vector3(Mathf.Cos(a),Mathf.Sin(a),0)*radius:new Vector3(Mathf.Sin(a),0,Mathf.Cos(a))*radius);}
    }
    public void Dispose() { foreach(var resource in resources) if(resource!=null) Object.Destroy(resource); resources.Clear(); }
}

public sealed class CrustaspikanEncounterHud : MonoBehaviour
{
    private CrustaspikanEncounter encounter;
    private TextMeshProUGUI title, detail, message;
    private Image hp, poise;
    private string announcement=""; private float announcementUntil;
    public void Bind(CrustaspikanEncounter encounter)
    {
        this.encounter=encounter;
        var canvas=gameObject.AddComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=65;
        var scaler=gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution=new Vector2(1920,1080);
        gameObject.AddComponent<GraphicRaycaster>();
        title=Text("Boss Name",new Vector2(0, -32),new Vector2(780,40),26);
        hp=Bar("HP",new Vector2(0,-75),new Color(.72f,.11f,.1f));
        poise=Bar("Groggy",new Vector2(0,-97),new Color(.9f,.68f,.18f));
        detail=Text("Combat Status",new Vector2(0,-128),new Vector2(1000,40),18);
        message=Text("Announcement",new Vector2(0,-225),new Vector2(1100,100),28);
        var help=Text("Practice Controls",new Vector2(0,-170),new Vector2(1150,36),18);
        help.text="원형 무대 반지름 20m · 격자 1m / 굵은 선 5m · F8 재시작 · F9 하이드아웃 복귀 · 최소 체력 1 보호";
    }
    private TextMeshProUGUI Text(string name, Vector2 offset, Vector2 size,float fontSize)
    {
        var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(transform,false);
        var r=go.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=new Vector2(.5f,1);r.anchoredPosition=offset;r.sizeDelta=size;
        var t=go.AddComponent<TextMeshProUGUI>();t.font=Resources.Load<TMP_FontAsset>("UI/Fonts/DamageFloating/Pretendard_Medium SDF") ?? TMP_Settings.defaultFontAsset;
        t.fontSize=fontSize;t.alignment=TextAlignmentOptions.Center;t.color=Color.white;t.raycastTarget=false; return t;
    }
    private Image Bar(string name,Vector2 offset,Color color)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);
        var r=go.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=new Vector2(.5f,1);r.anchoredPosition=offset;r.sizeDelta=new Vector2(720,14);
        var background=go.GetComponent<Image>();background.color=new Color(.05f,.06f,.08f,.9f);background.raycastTarget=false;
        var fill=new GameObject("Fill",typeof(RectTransform),typeof(Image));fill.transform.SetParent(go.transform,false);
        var f=fill.GetComponent<RectTransform>();f.anchorMin=Vector2.zero;f.anchorMax=Vector2.one;f.offsetMin=f.offsetMax=Vector2.zero;
        var image=fill.GetComponent<Image>();image.color=color;image.raycastTarget=false;return image;
    }
    public void Announce(string text,float seconds) { announcement=text;announcementUntil=Time.time+seconds; }
    private void Update()
    {
        var b=encounter?.Brain;if(b==null)return;
        title.text=$"CRUSTASPIKAN · {b.Phase}페이즈   {b.Actor.Health.CurrentHp:0} / {b.Actor.Health.MaxHp:0}";
        hp.rectTransform.anchorMax=new Vector2(b.Actor.Health.NormalizedHp,1);
        poise.rectTransform.anchorMax=new Vector2(b.Poise/encounter.Settings.groggyMax,1);
        detail.text=$"{b.State}    그로기 {b.Poise:0}/{encounter.Settings.groggyMax:0}    소환 {encounter.AliveAdds}/{encounter.Settings.maximumAdds}    {b.Observer.Explanation}";
        message.text=Time.time<announcementUntil?announcement:"";
    }
}
