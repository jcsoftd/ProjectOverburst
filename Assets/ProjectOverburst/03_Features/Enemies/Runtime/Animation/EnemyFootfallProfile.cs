using UnityEngine;

[CreateAssetMenu(menuName = "OVERBURST/Enemies/Footfall Profile", fileName = "EFP_Enemy")]
public sealed class EnemyFootfallProfile : ScriptableObject
{
    [SerializeField] private string enemyId;
    [SerializeField] private AnimationClip locomotionClip;
    [SerializeField] private float[] contactPhases;

    public string EnemyId => enemyId;
    public AnimationClip LocomotionClip => locomotionClip;
    public int ContactCount => contactPhases != null ? contactPhases.Length : 0;
    public bool IsValid => !string.IsNullOrWhiteSpace(enemyId)
        && locomotionClip != null && ContactCount >= 2 && ContactCount <= 4;

    public float GetContactPhase(int index) => index >= 0 && index < ContactCount
        ? contactPhases[index] : 0f;

    public void Configure(string id, AnimationClip clip, params float[] phases)
    {
        enemyId = id;
        locomotionClip = clip;
        contactPhases = phases != null ? (float[])phases.Clone() : System.Array.Empty<float>();
        if (!IsValid) throw new System.ArgumentException("Footfall profile needs one clip and 2-4 contacts.");
        float last = -1f;
        foreach (float phase in contactPhases)
        {
            if (phase < 0f || phase >= 1f || phase <= last)
                throw new System.ArgumentException("Contact phases must be unique, sorted, and inside [0,1).");
            last = phase;
        }
    }
}
