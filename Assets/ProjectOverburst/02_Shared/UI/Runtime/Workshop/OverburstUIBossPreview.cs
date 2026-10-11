using UnityEngine;

// Management samples use the production HUD's own preview API, without an encounter or save session.
[ExecuteAlways]
public sealed class OverburstUIBossPreview : MonoBehaviour
{
    public EnemyBossHudView view;
    public int phase;
    public bool galleryOnly;
    public bool IsVisible => view && view.IsVisible;
    void OnEnable() { if (!Application.isPlaying && galleryOnly && view) Show(); }
    void Start() { if (Application.isPlaying) { if (galleryOnly) gameObject.SetActive(false); else Hide(); } }
    public void Show()
    {
        view.enabled = false;
        view.ShowPreview("유적의 감시자", 20, phase == 0 ? 12000 : phase == 1 ? 7800 : 3360, 12000,
            phase == 0 ? 12000 : phase == 1 ? 9600 : 5400, phase, new[] { .7f, .3f },
            phase == 0 ? EnemyBossEmblemFxMode.Fire : phase == 1 ? EnemyBossEmblemFxMode.Smoke : EnemyBossEmblemFxMode.Pulse);
    }
    public void NextPhase() { phase = (phase + 1) % 3; Show(); }
    public void Hide() { if (view) view.GetComponent<CanvasGroup>().alpha = 0; }
}
