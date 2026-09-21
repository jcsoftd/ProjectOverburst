using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Preview-only playback. Source prefabs, controllers and import settings stay intact.
public sealed class MonsterShowcaseActor : MonoBehaviour
{
    public string displayName;
    public string sourcePath;
    public Animator animator;
    public AnimationClip[] clips;
    public Vector3 displaySize;
    public Vector3 focusPoint;
    public TextMesh nameplate;
    public bool automatic = true;
    public bool paused;
    public float speed = 1f;
    public int ClipIndex { get; private set; }
    public int PlayedCount { get; private set; }
    public float ClipTime { get; private set; }
    private PlayableGraph graph;
    private AnimationClipPlayable playable;
    private Vector3 modelPosition;
    private Quaternion modelRotation;
    private Vector3 modelScale;

    private void OnEnable()
    {
        if (animator == null || clips == null || clips.Length == 0) return;
        modelPosition = animator.transform.localPosition;
        modelRotation = animator.transform.localRotation;
        modelScale = animator.transform.localScale;
        animator.applyRootMotion = false;
        animator.fireEvents = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        PlayClip(ClipIndex);
    }

    public void PlayClip(int index)
    {
        if (animator == null || clips == null || clips.Length == 0) return;
        if (graph.IsValid()) graph.Destroy();
        ClipIndex = (index % clips.Length + clips.Length) % clips.Length;
        ClipTime = 0f;
        animator.Rebind();
        animator.transform.localPosition = modelPosition;
        animator.transform.localRotation = modelRotation;
        animator.transform.localScale = modelScale;
        graph = PlayableGraph.Create("Showcase/" + displayName);
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        playable = AnimationClipPlayable.Create(graph, clips[ClipIndex]);
        playable.SetApplyFootIK(false);
        playable.SetApplyPlayableIK(false);
        playable.SetSpeed(0);
        AnimationPlayableOutput.Create(graph, "Preview", animator).SetSourcePlayable(playable);
        graph.Play();
        graph.Evaluate(0);
        animator.transform.localPosition = modelPosition;
        animator.transform.localRotation = modelRotation;
        animator.transform.localScale = modelScale;
        PlayedCount++;
    }

    private void Update()
    {
        if (!graph.IsValid() || paused) return;
        ClipTime += Time.unscaledDeltaTime * speed;
        float duration = Mathf.Max(0.05f, clips[ClipIndex].length);
        if (automatic && ClipTime >= Mathf.Max(2.5f, duration + 0.5f))
            PlayClip(ClipIndex + 1);
        else
        {
            // Non-looping attacks/deaths hold the final pose before advancing.
            double time = clips[ClipIndex].isLooping ? ClipTime % duration : Mathf.Min(ClipTime, duration);
            playable.SetTime(time);
            graph.Evaluate(0);
        }
        animator.transform.localPosition = modelPosition;
        animator.transform.localRotation = modelRotation;
        animator.transform.localScale = modelScale;
    }

    private void OnDisable() { if (graph.IsValid()) graph.Destroy(); }

    public void SampleAt(float seconds)
    {
        if (!graph.IsValid()) return;
        ClipTime = Mathf.Clamp(seconds, 0f, clips[ClipIndex].length);
        playable.SetTime(ClipTime);
        graph.Evaluate(0);
        animator.transform.localPosition = modelPosition;
        animator.transform.localRotation = modelRotation;
        animator.transform.localScale = modelScale;
    }
}
