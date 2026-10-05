using System;
using UnityEngine;

/// <summary>Frame boundaries measured from the supplied 30 FPS reference. Frame zero is the still photo.</summary>
public sealed class OiiaCatReferenceTimeline : ScriptableObject
{
    [SerializeField, Min(1)] private int frameRate = 30;
    [SerializeField, Min(1)] private int frameCount = 4039;
    [SerializeField] private int[] transitionFrames = Array.Empty<int>();

    public int FrameRate => frameRate;
    public int FrameCount => frameCount;
    public int TransitionCount => transitionFrames.Length;
    public float Duration => (float)frameCount / frameRate;
    public int FrameFromSamples(int samples, int frequency) => frequency <= 0 ? 0 : (int)((long)Math.Max(0, samples) * frameRate / frequency % frameCount);
    public bool IsSpinning(int frame) => (SegmentIndex(frame) & 1) != 0;
    public int SegmentStart(int frame)
    {
        int index = SegmentIndex(frame);
        return index == 0 ? 0 : transitionFrames[index - 1];
    }
    public int TransitionFrame(int index) => transitionFrames[index];
    public int SpinStart(int frame)
    {
        int segment = SegmentIndex(frame);
        // A brief inserted photo flashes over an ongoing turn; it must not restart the spin animation.
        for (int photo = (segment - 1) & ~1; photo >= 0; photo -= 2)
        {
            int start = photo == 0 ? 0 : transitionFrames[photo - 1];
            int end = transitionFrames[photo];
            if (end - start >= frameRate / 4) return end;
        }
        return 0;
    }
    private int SegmentIndex(int frame)
    {
        frame = (frame % frameCount + frameCount) % frameCount;
        int index = Array.BinarySearch(transitionFrames, frame);
        return index >= 0 ? index + 1 : ~index;
    }
}
