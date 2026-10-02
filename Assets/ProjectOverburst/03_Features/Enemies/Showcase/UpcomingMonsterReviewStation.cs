using System;
using UnityEngine;

/// <summary>Editor-only, saved review bindings. Never a combat definition.</summary>
public sealed class UpcomingMonsterReviewStation : MonoBehaviour
{
    public string cardKey, displayName, grade, selectionStatus, warnings;
    public GameObject model;
    public Vector3 displaySize;
    public Motion[] motions = Array.Empty<Motion>();

    [Serializable]
    public sealed class Motion
    {
        public string role, concept, count, connection, selectionState;
        public AnimationClip clip;
        public bool parryable, needsTrim;
    }
}
