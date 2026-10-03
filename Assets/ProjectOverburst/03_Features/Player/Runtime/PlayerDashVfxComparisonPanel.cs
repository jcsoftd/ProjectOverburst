using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Editor Play에서만 생성하는 대시 잔상 비교 버튼. 정식 씬과 프리팹에는 저장하지 않는다.</summary>
[DefaultExecutionOrder(-12000)]
public sealed class PlayerDashVfxComparisonPanel : MonoBehaviour
{
    PlayerDashVfx effect;
    GUIStyle heading, description, buttonStyle;
    Font font;
    public int SelectionIndex => effect != null ? effect.ColorStyle : 0;
    public int SelectionCount => PlayerDashVfx.ColorNames.Length;

    public void Bind(PlayerDashVfx value) => effect = value;
    Rect PanelRect => new Rect(Mathf.Max(8f, Screen.width - 620f), 92f, 290f, 188f);

    void Update()
    {
        if (effect == null) { Destroy(gameObject); return; }
        var mouse = Mouse.current;
        bool hovering = false;
        if (!OverburstGameMenu.IsOpen && mouse != null)
        {
            Vector2 position = mouse.position.ReadValue();
            hovering = PanelRect.Contains(new Vector2(position.x, Screen.height - position.y));
        }
        if (hovering) GameplayInputBlocker.Block(this);
        else GameplayInputBlocker.Unblock(this);
    }

    void OnGUI()
    {
        if (effect == null || OverburstGameMenu.IsOpen) return;
        if (heading == null)
        {
            font = Font.CreateDynamicFontFromOSFont("Malgun Gothic", 14);
            heading = new GUIStyle(GUI.skin.label) { font = font, fontSize = 16, fontStyle = FontStyle.Bold };
            description = new GUIStyle(GUI.skin.label) { font = font, wordWrap = true, fontSize = 12 };
            buttonStyle = new GUIStyle(GUI.skin.button) { font = font, fontSize = 15 };
        }
        GUILayout.BeginArea(PanelRect, GUI.skin.box);
        GUILayout.Label("1번 잔상 · 색상 비교", heading);
        GUILayout.Label(PlayerDashVfx.ColorNames[effect.ColorStyle], description);
        if (GUILayout.Button("다음 색상 ▶   " + (SelectionIndex + 1) + " / " + SelectionCount, buttonStyle, GUILayout.Height(42)))
            Cycle(Event.current.shift ? -1 : 1);
        GUILayout.Label("클릭: 다음 · Shift+클릭: 이전\n선택 후 대시 / 최대 에너지 강공\n바닥 먼지는 모든 원소에서 흙색", description);
        GUILayout.EndArea();
    }

    public void Cycle(int direction)
    {
        if (effect == null) return;
        int selected = (SelectionIndex + direction + SelectionCount) % SelectionCount;
        effect.SetColorStyle(selected);
    }
    void OnDisable() => GameplayInputBlocker.Unblock(this);
    void OnDestroy()
    {
        GameplayInputBlocker.Unblock(this);
        if (font != null)
        {
            if (Application.isPlaying) Destroy(font);
            else DestroyImmediate(font);
        }
    }
}


