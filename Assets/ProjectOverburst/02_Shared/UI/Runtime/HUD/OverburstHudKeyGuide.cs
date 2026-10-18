using UnityEngine;
using UnityEngine.InputSystem;

// Reuses the approved tutorial key graphics and binding labels in the regular HUD.
public sealed class OverburstHudKeyGuide : MonoBehaviour
{
    public const string ResourcePath = "UI/HudKeyGuide/PF_OverburstHudKeyGuide";
    public RectTransform dock;
    public CanvasGroup group;
    public TutorialKeyGuide[] rows;
    private InputActionAsset shownInput;
    private Vector2 lastSize;

    public static void Install(OverburstGameUI game)
    {
        var prefab = Resources.Load<OverburstHudKeyGuide>(ResourcePath);
        if (prefab) Instantiate(prefab, game.hud, false);
    }
    private void OnEnable() { OverburstGameSettings.Changed += Refresh; Refresh(); }
    private void OnDisable() => OverburstGameSettings.Changed -= Refresh;
    private void LateUpdate()
    {
        if (shownInput != PlayerInputFacade.Current?.RuntimeAsset) Refresh();
        var size = new Vector2(Screen.width, Screen.height);
        if (size != lastSize)
        {
            lastSize = size;
            float scale = size.y <= 800 ? 1.2f : 1;
            dock.localScale = Vector3.one * scale;
            dock.anchoredPosition = new Vector2(28, 92);
        }
        var actor = PlayerContext.Instance?.CurrentActor;
        group.alpha = OverburstGameSettings.HudKeyGuideEnabled && actor && actor.Health != null && !actor.Health.IsDead
            && PersistentSceneFlow.Instance?.IsSwitching != true
            && OptionalTutorialController.Instance?.IsTraining != true ? 1 : 0;
    }
    public void Refresh()
    {
        shownInput = PlayerInputFacade.Current?.RuntimeAsset;
        if (!shownInput || rows == null || rows.Length != 11) return;
        foreach (var row in rows) row.Clear();
        string[] actions = { "Attack", "Aim", "Evade", "CombatMode", "Interact", "LootModeCycle",
            "Inventory", "Equipment", "MapToggle", "QuestList", "Cancel" };
        for (int i = 0; i < actions.Length; i++) rows[i].Key(shownInput, actions[i]);
        foreach (var row in rows)
        {
            row.Finish();
            row.content.localScale *= .7f;
            row.content.anchoredPosition = Vector2.zero;
            foreach (var glyph in row.glyphs)
            {
                glyph.fontSize = glyph.fontSizeMax = glyph.text.Length > 1 ? 18 : 20;
            }
        }
    }
}
