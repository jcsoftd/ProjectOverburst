using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public sealed class MonsterShowcaseGallery : MonoBehaviour
{
    public MonsterShowcaseActor[] actors;
    public Camera galleryCamera;
    public Text title, details, clipLabel, autoLabel, pauseLabel;
    public Button previousMonster, nextMonster, previousClip, nextClip, autoButton, pauseButton, overviewButton;
    public Button[] monsterButtons;
    public Button[] clipButtons;
    public ScrollRect clipScroll;
    public Vector3 overviewTarget;
    public float overviewDistance = 80f;
    public int SelectedIndex { get; private set; }
    public bool Overview { get; private set; }
    private float yaw = 155f, pitch = 15f, distance;
    private Vector3 target;
    private bool wasBackground;
    private Renderer[][] displayRenderers;
    private bool[][] originalVisibility;

    private void Start()
    {
        wasBackground = Application.runInBackground;
        Application.runInBackground = true;
        displayRenderers = new Renderer[actors.Length][];
        originalVisibility = new bool[actors.Length][];
        for (int i = 0; i < actors.Length; i++)
        {
            displayRenderers[i] = actors[i].GetComponentsInChildren<Renderer>(true);
            originalVisibility[i] = new bool[displayRenderers[i].Length];
            for (int j = 0; j < displayRenderers[i].Length; j++) originalVisibility[i][j] = displayRenderers[i][j].enabled;
        }
        previousMonster.onClick.AddListener(() => SelectMonster(SelectedIndex - 1));
        nextMonster.onClick.AddListener(() => SelectMonster(SelectedIndex + 1));
        previousClip.onClick.AddListener(() => SelectClip(-1));
        nextClip.onClick.AddListener(() => SelectClip(1));
        autoButton.onClick.AddListener(() => SetAutomatic(!actors[SelectedIndex].automatic));
        pauseButton.onClick.AddListener(() => SetPaused(!actors[SelectedIndex].paused));
        overviewButton.onClick.AddListener(ShowOverview);
        for (int i = 0; i < monsterButtons.Length; i++)
        {
            int index = i;
            monsterButtons[i].onClick.AddListener(() => SelectMonster(index));
        }
        for (int i = 0; i < clipButtons.Length; i++)
        {
            int index = i;
            clipButtons[i].onClick.AddListener(() => PlaySelectedClip(index));
        }
        SelectMonster(0);
    }

    public void SelectMonster(int index)
    {
        if (actors.Length == 0) return;
        SelectedIndex = (index % actors.Length + actors.Length) % actors.Length;
        Overview = false;
        SetDisplayVisibility(false);
        MonsterShowcaseActor actor = actors[SelectedIndex];
        target = actor.focusPoint;
        // Leave space for wings/tails and the fixed sidebar; preserve asset scale.
        distance = Mathf.Max(3f, actor.displaySize.magnitude * 1.8f);
        yaw = 155f; pitch = 15f;
        for (int i = 0; i < monsterButtons.Length; i++)
            monsterButtons[i].image.color = i == SelectedIndex ? new Color(0.1f, 0.46f, 0.53f) : new Color(0.13f, 0.18f, 0.24f);
        foreach (var entry in actors) entry.nameplate.gameObject.SetActive(false);
        for (int i = 0; i < clipButtons.Length; i++)
        {
            clipButtons[i].gameObject.SetActive(i < actor.clips.Length);
            if (i < actor.clips.Length)
                clipButtons[i].GetComponentInChildren<Text>().text = $"{i + 1:00}  {actor.clips[i].name}";
        }
        clipScroll.content.sizeDelta = new Vector2(280, actor.clips.Length * 38);
        clipScroll.verticalNormalizedPosition = 1f;
        RefreshLabels();
    }

    public void SelectClip(int delta)
    {
        MonsterShowcaseActor actor = actors[SelectedIndex];
        PlaySelectedClip(actor.ClipIndex + delta);
    }

    public void PlaySelectedClip(int index)
    {
        var actor = actors[SelectedIndex];
        actor.automatic = false;
        actor.paused = false;
        actor.PlayClip(index);
        RefreshLabels();
    }

    public void SetAutomatic(bool enabled)
    {
        foreach (var actor in actors) actor.automatic = enabled;
        RefreshLabels();
    }

    public void SetPaused(bool enabled)
    {
        foreach (var actor in actors) actor.paused = enabled;
        RefreshLabels();
    }

    public void ShowOverview()
    {
        Overview = true; target = overviewTarget; distance = overviewDistance * 1.45f; yaw = 180f; pitch = 65f;
        SetDisplayVisibility(true);
        foreach (var entry in actors) entry.nameplate.gameObject.SetActive(true);
    }

    private void SetDisplayVisibility(bool all)
    {
        if (displayRenderers == null) return;
        for (int i = 0; i < displayRenderers.Length; i++)
            for (int j = 0; j < displayRenderers[i].Length; j++)
                displayRenderers[i][j].enabled = originalVisibility[i][j] && (all || i == SelectedIndex);
    }

    private void Update()
    {
        if (actors.Length == 0) return;
        var mouse = Mouse.current;
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (mouse != null && !overUI)
        {
            if (mouse.rightButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue(); yaw += d.x * 0.2f; pitch = Mathf.Clamp(pitch - d.y * 0.2f, -10f, 85f);
            }
            distance = Mathf.Clamp(distance * Mathf.Exp(-mouse.scroll.ReadValue().y * 0.001f), 0.3f, 800f);
        }
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.leftArrowKey.wasPressedThisFrame) SelectMonster(SelectedIndex - 1);
            if (keyboard.rightArrowKey.wasPressedThisFrame) SelectMonster(SelectedIndex + 1);
            if (keyboard.upArrowKey.wasPressedThisFrame) SelectClip(-1);
            if (keyboard.downArrowKey.wasPressedThisFrame) SelectClip(1);
            if (keyboard.spaceKey.wasPressedThisFrame) SetPaused(!actors[SelectedIndex].paused);
        }
        RefreshLabels();
    }

    private void LateUpdate()
    {
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
        // Render into the area to the right of the catalog, avoiding model occlusion.
        float sidebar = Mathf.Clamp(300f * Screen.height / (900f * Mathf.Max(1, Screen.width)), 0.15f, 0.4f);
        galleryCamera.rect = new Rect(sidebar, 0, 1f - sidebar * 2f, 1);
        galleryCamera.transform.SetPositionAndRotation(target - rotation * Vector3.forward * distance, rotation);
        foreach (var actor in actors)
            if (actor.nameplate != null) actor.nameplate.transform.rotation = rotation;
    }

    private void RefreshLabels()
    {
        var actor = actors[SelectedIndex];
        title.text = $"{SelectedIndex + 1:00}  /  {actors.Length}     {actor.displayName}";
        details.text = $"Original scale / idle pose  |  H {actor.displaySize.y:F2} m  W {actor.displaySize.x:F2} m  D {actor.displaySize.z:F2} m\n{actor.clips.Length} clips  |  Root motion held in place for display";
        clipLabel.text = actor.clips.Length == 0 ? "No animation clips" : $"{actor.ClipIndex + 1:00}/{actor.clips.Length}    {actor.clips[actor.ClipIndex].name}    ({actor.ClipTime:F1}s)";
        autoLabel.text = actor.automatic ? "AUTO: ON" : "AUTO: OFF";
        pauseLabel.text = actor.paused ? "RESUME" : "PAUSE";
        for (int i = 0; i < clipButtons.Length; i++)
            if (clipButtons[i].gameObject.activeSelf)
                clipButtons[i].image.color = i == actor.ClipIndex ? new Color(0.1f, 0.46f, 0.53f) : new Color(0.13f, 0.18f, 0.24f);
    }

    private void OnDestroy() { Application.runInBackground = wasBackground; }
}
