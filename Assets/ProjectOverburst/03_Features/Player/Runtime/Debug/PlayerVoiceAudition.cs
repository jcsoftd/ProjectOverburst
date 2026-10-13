#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 임시 청음 도구. Editor Play에서만 생성하며 계정/제품 설정에 저장하지 않는다.
[DefaultExecutionOrder(-10000)]
public sealed class PlayerVoiceAudition : MonoBehaviour
{
    public const string PrefabPath = "Assets/Editor/Testers/Audio/PF_PlayerVoiceAudition.prefab";
    public enum VoiceKind { Attack, Hurt, Death, Jump }

    [Serializable]
    public sealed class VoiceSet
    {
        public string name;
        public AudioClip[] attack, hurt, death, jump;
        public AudioClip[] Clips(VoiceKind kind) => kind == VoiceKind.Attack ? attack
            : kind == VoiceKind.Hurt ? hurt : kind == VoiceKind.Death ? death : jump;
    }

    [SerializeField] private VoiceSet[] candidates;
    [SerializeField] private RectTransform panel;
    [SerializeField] private Button[] selectionButtons;
    [SerializeField] private Button[] previewButtons;
    [SerializeField] private TextMeshProUGUI status;
    [SerializeField] private AudioSource voice;
    [SerializeField] private Color buttonColor, selectedColor;
    private static PlayerVoiceAudition instance;
    private CombatHealth health;
    private PlayerActorRuntime actor;
    private int[,] previous;
    private int selected;

    public static PlayerVoiceAudition Current => instance;
    public int SelectedIndex => selected;
    public int PlayedCount { get; private set; }
    public VoiceKind LastKind { get; private set; }
    public AudioClip LastClip => voice.clip;
    public VoiceSet[] Candidates => candidates;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => instance = null;

    // 정식 음성 설정으로 대체. 보존한 청음 도구는 자동 생성하지 않는다.
    private static void Bootstrap()
    {
        if (instance != null) return;
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab != null) Instantiate(prefab);
    }

    public void Configure(VoiceSet[] sets, RectTransform area, Button[] selectors, Button[] previews,
        TextMeshProUGUI label, AudioSource source, Color normal, Color active)
    {
        candidates = sets; panel = area; selectionButtons = selectors; previewButtons = previews;
        status = label; voice = source; buttonColor = normal; selectedColor = active;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        previous = new int[candidates.Length, 4];
        for (int i = 0; i < candidates.Length; i++)
            for (int j = 0; j < 4; j++) previous[i, j] = -1;
        for (int i = 0; i < selectionButtons.Length; i++)
        {
            int choice = i < candidates.Length ? i : -1;
            selectionButtons[i].onClick.AddListener(() => { Deselect(); SelectVoice(choice); });
        }
        for (int i = 0; i < previewButtons.Length; i++)
        {
            VoiceKind kind = (VoiceKind)i;
            previewButtons[i].onClick.AddListener(() => { Deselect(); Preview(kind); });
        }
        SelectVoice(0);
    }

    private void Update()
    {
        PlayerActorRuntime current = PlayerContext.Instance != null ? PlayerContext.Instance.CurrentActor : null;
        if (actor != current)
        {
            Unbind(); actor = current; health = actor != null ? actor.Health : null;
            if (health != null) { health.OnDamageResolved += Damaged; health.OnDead += Died; }
        }
        bool visible = actor != null && actor.gameObject.activeInHierarchy;
        if (panel.gameObject.activeSelf != visible) panel.gameObject.SetActive(visible);
        bool hovered = visible && Mouse.current != null
            && RectTransformUtility.RectangleContainsScreenPoint(panel, Mouse.current.position.ReadValue(), null);
        GameplayInputBlocker.SetBlocked(this, hovered);
    }

    private void OnDisable() { Unbind(); GameplayInputBlocker.Unblock(this); voice.Stop(); }
    private void OnDestroy() { Unbind(); GameplayInputBlocker.Unblock(this); if (instance == this) instance = null; }
    private void Unbind()
    {
        if (health != null) { health.OnDamageResolved -= Damaged; health.OnDead -= Died; }
        health = null; actor = null;
        if (voice != null) voice.Stop();
    }

    public void SelectVoice(int index)
    {
        if (index < -1 || index >= candidates.Length) throw new ArgumentOutOfRangeException(nameof(index));
        selected = index; voice.Stop();
        for (int i = 0; i < selectionButtons.Length; i++)
            ((Image)selectionButtons[i].targetGraphic).color = (i == selected || (selected < 0 && i == candidates.Length))
                ? selectedColor : buttonColor;
        foreach (Button button in previewButtons) button.interactable = selected >= 0;
        status.text = selected < 0 ? "음성 꺼짐" : candidates[selected].name + " · 행동별 랜덤 / 연속 같은 소리 제외";
    }

    public bool Preview(VoiceKind kind) => Play(kind);
    public static void Attack() { if (instance != null && instance.health != null && !instance.health.IsDead) instance.Play(VoiceKind.Attack); }
    public static void Jump(GameObject owner)
    {
        if (instance != null && instance.actor != null && instance.health != null && instance.actor.gameObject == owner && !instance.health.IsDead)
            instance.Play(VoiceKind.Jump);
    }
    private void Damaged(CombatHealth source, DamageInfo info, float loss, bool lethal)
    { if (loss > 0f && !lethal && !source.IsDead) Play(VoiceKind.Hurt); }
    private void Died(CombatHealth source, DamageInfo info) => Play(VoiceKind.Death);

    private bool Play(VoiceKind kind)
    {
        if (selected < 0) return false;
        AudioClip[] clips = candidates[selected].Clips(kind);
        if (clips == null || clips.Length == 0) return false;
        int last = previous[selected, (int)kind];
        int pick = UnityEngine.Random.Range(0, clips.Length > 1 && last >= 0 ? clips.Length - 1 : clips.Length);
        if (clips.Length > 1 && last >= 0 && pick >= last) pick++;
        voice.Stop(); voice.clip = clips[pick]; voice.Play();
        previous[selected, (int)kind] = pick; LastKind = kind; PlayedCount++;
        status.text = candidates[selected].name + " · " + clips[pick].name;
        return true;
    }

    private static void Deselect() { if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null); }
}
#endif
