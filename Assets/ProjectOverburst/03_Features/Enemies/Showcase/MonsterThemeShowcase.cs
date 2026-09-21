using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

// Selection-only exhibit. No spawn service, combat data or source prefab is changed.
[DefaultExecutionOrder(1000)]
public sealed class MonsterThemeShowcase : MonoBehaviour
{
    [Serializable] public sealed class Member
    {
        public int catalogIndex, tier;
        public float scale;
        public MonsterShowcaseActor actor;
    }
    [Serializable] public sealed class Set
    {
        public string title, reason, caution;
        public Transform root;
        public Member[] members;
    }
    public MonsterShowcaseGallery catalog;
    public GameObject catalogUI;
    public GameObject catalogFloor;
    public Set[] sets;
    public bool showThemesOnStart = true;
    public int SelectedSet { get; private set; }
    public bool ShowingThemes { get; private set; }
    public bool Attacking { get; private set; }
    private float yaw = 180, pitch = 27, distance = 23;
    private GUIStyle heading, body, button, small;
    private Texture2D panel;
    private Font font;
    private readonly string[] tiers = { "소형 · 다수", "중형 · 소수", "준대형 · 희귀 정예" };

    private void Start()
    {
        foreach (var set in sets) set.root.gameObject.SetActive(false);
        if (showThemesOnStart) ShowSet(0);
    }

    public void ShowSet(int index)
    {
        SelectedSet = (index % sets.Length + sets.Length) % sets.Length;
        ShowingThemes = true;
        catalog.gameObject.SetActive(false);
        catalogUI.SetActive(false);
        if (catalogFloor != null) catalogFloor.SetActive(false);
        for (int i = 0; i < sets.Length; i++) sets[i].root.gameObject.SetActive(i == SelectedSet);
        yaw = 180; pitch = 27; distance = 23;
        SetPose(false);
    }

    public void ShowCatalog(int index)
    {
        ShowingThemes = false;
        foreach (var set in sets) set.root.gameObject.SetActive(false);
        catalog.gameObject.SetActive(true);
        catalogUI.SetActive(true);
        if (catalogFloor != null) catalogFloor.SetActive(true);
        catalog.SelectMonster(index);
    }

    public void SetPose(bool attack)
    {
        Attacking = attack;
        foreach (var member in sets[SelectedSet].members)
        {
            var actor = member.actor;
            int clip = 0;
            if (attack)
                for (int i = 0; i < actor.clips.Length; i++)
                    if (IsAttack(actor.clips[i].name)) { clip = i; break; }
            actor.automatic = false;
            actor.paused = false;
            actor.PlayClip(clip);
        }
    }

    public static bool IsAttack(string name)
    {
        string n = name.ToLowerInvariant();
        return n.Contains("bite") || n.Contains("claw") || n.Contains("smash") || n.Contains("attack") || n.Contains("shot") || n.Contains("blast");
    }

    private void Update()
    {
        if (!ShowingThemes) return;
        var mouse = Mouse.current;
        if (mouse == null) return;
        Vector2 p = mouse.position.ReadValue();
        bool view = p.x > Screen.width * .19f && p.y > Screen.height * .23f && p.y < Screen.height * .85f;
        if (!view || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())) return;
        if (mouse.rightButton.isPressed)
        {
            var d = mouse.delta.ReadValue(); yaw += d.x * .2f; pitch = Mathf.Clamp(pitch - d.y * .2f, 5, 75);
        }
        distance = Mathf.Clamp(distance * Mathf.Exp(-mouse.scroll.ReadValue().y * .001f), 12, 65);
    }

    private void LateUpdate()
    {
        if (!ShowingThemes) return;
        var camera = catalog.galleryCamera;
        camera.rect = new Rect(.19f, .23f, .81f, .62f);
        Quaternion r = Quaternion.Euler(pitch, yaw, 0);
        Vector3 target = sets[SelectedSet].root.position + Vector3.up;
        camera.transform.SetPositionAndRotation(target - r * Vector3.forward * distance, r);
        foreach (var label in sets[SelectedSet].root.GetComponentsInChildren<TextMesh>()) label.transform.rotation = r;
    }

    private void Styles()
    {
        if (heading != null) return;
        font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial" }, 24);
        panel = new Texture2D(1, 1); panel.SetPixel(0, 0, new Color(.055f, .085f, .12f)); panel.Apply();
        heading = new GUIStyle(GUI.skin.label) { font = font, fontSize = 25, fontStyle = FontStyle.Bold, wordWrap = true };
        body = new GUIStyle(GUI.skin.label) { font = font, fontSize = 17, wordWrap = true };
        small = new GUIStyle(body) { fontSize = 14 };
        button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 17, wordWrap = true, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(12, 8, 5, 5) };
    }

    private void OnGUI()
    {
        Styles();
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1600f, Screen.height / 900f, 1));
        if (!ShowingThemes)
        {
            if (GUI.Button(new Rect(620, 120, 330, 42), "스폰 테마 세트 보기 →", button)) ShowSet(SelectedSet);
            GUI.matrix = previous; return;
        }
        GUI.DrawTexture(new Rect(0, 0, 304, 900), panel);
        GUI.DrawTexture(new Rect(304, 0, 1296, 135), panel);
        GUI.DrawTexture(new Rect(304, 693, 1296, 207), panel);
        GUI.Label(new Rect(20, 20, 265, 70), "MONSTER VOL.2\n스폰 테마 후보", heading);
        for (int i = 0; i < sets.Length; i++)
        {
            GUI.backgroundColor = i == SelectedSet ? new Color(.2f, .8f, .85f) : Color.white;
            if (GUI.Button(new Rect(16, 118 + i * 68, 270, 58), $"{i + 1:00}  {sets[i].title}", button)) ShowSet(i);
        }
        GUI.backgroundColor = Color.white;
        if (GUI.Button(new Rect(16, 620, 270, 44), "전체 47개 원본 크기 보기", button)) ShowCatalog(0);
        if (GUI.Button(new Rect(16, 678, 130, 44), "대기 비교", button)) SetPose(false);
        if (GUI.Button(new Rect(156, 678, 130, 44), "공격 비교", button)) SetPose(true);
        GUI.Label(new Rect(20, 747, 264, 140), "배치 예시: 소형 8~12 / 중형 2~3\n정예는 가끔 1마리\n\n전시는 종류별 대표만 표시.\n스폰·능력치에는 아직 미반영.", small);
        var set = sets[SelectedSet];
        GUI.Label(new Rect(330, 15, 1240, 40), $"{SelectedSet + 1:00}  {set.title}     /     {(Attacking ? "공격" : "대기")} 비교", heading);
        GUI.Label(new Rect(330, 61, 1240, 38), set.reason, body);
        GUI.Label(new Rect(330, 104, 1240, 27), "우클릭 드래그: 회전  ·  휠: 확대  ·  아래 모델 이름: 원본과 전체 애니메이션 확인", small);
        GUI.Label(new Rect(325, 701, 1250, 44), set.caution, body);
        float width = 1240f / set.members.Length;
        for (int i = 0; i < set.members.Length; i++)
        {
            var m = set.members[i];
            GUI.backgroundColor = m.tier == 0 ? new Color(.35f, .85f, .8f) : m.tier == 1 ? new Color(1, .75f, .32f) : new Color(1, .4f, .6f);
            if (GUI.Button(new Rect(325 + i * width, 756, width - 8, 94), $"{tiers[m.tier]}\n#{m.catalogIndex + 1:00} {m.actor.displayName}\nH {m.actor.displaySize.y:F2}m · 원본 ×{m.scale:F2}", button)) ShowCatalog(m.catalogIndex);
        }
        GUI.backgroundColor = Color.white;
        GUI.Label(new Rect(325, 860, 1250, 30), "크기는 이 전시의 제안값입니다. 색상 변형은 같은 종이며 별도 전투 역할을 뜻하지 않습니다. 청록 캡슐 = 1.8m 사람 기준.", small);
        GUI.matrix = previous;
    }

    private void OnDestroy()
    {
        if (panel != null) Destroy(panel);
        if (font != null) Destroy(font);
    }
}
