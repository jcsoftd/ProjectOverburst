using UnityEngine;

// Authored VAT curves and renderer references. Playback belongs to the selected bounded pool.
public sealed class VolumetricBloodAnimationData : MonoBehaviour
{
    public const float PlaybackSpeed = 1.35f;
    [System.Serializable]
    public sealed class Layer
    {
        public Renderer renderer;
        public AnimationCurve speed = AnimationCurve.Linear(0, 0, 1, 1);
        public float frames = 99f, seconds = 3f, offset;
    }
    public Layer[] layers;
}
