using System;
using UnityEngine;

[Serializable]
public struct EnemyFootfallContact
{
    [SerializeField, Range(0f, 0.999f)] private float phase;
    [SerializeField] private Vector3 localPosition;

    public float Phase => phase;
    public Vector3 LocalPosition => localPosition;

    public EnemyFootfallContact(float normalizedPhase, Vector3 position)
    {
        phase = normalizedPhase;
        localPosition = position;
    }
}

[CreateAssetMenu(menuName = "OVERBURST/Enemies/Footfall Profile", fileName = "EFP_Enemy")]
public sealed class EnemyFootfallProfile : ScriptableObject
{
    [SerializeField] private string enemyId;
    [SerializeField] private AnimationClip locomotionClip;
    [SerializeField] private float[] contactPhases;
    [SerializeField] private AnimationClip runClip;
    [SerializeField] private EnemyHitWeight visualWeight = EnemyHitWeight.Standard;
    [SerializeField] private EnemyFootfallContact[] walkContacts = Array.Empty<EnemyFootfallContact>();
    [SerializeField] private EnemyFootfallContact[] runContacts = Array.Empty<EnemyFootfallContact>();

    public string EnemyId => enemyId;
    public AnimationClip LocomotionClip => locomotionClip;
    public AnimationClip RunClip => runClip != null ? runClip : locomotionClip;
    public EnemyHitWeight VisualWeight => visualWeight;
    public bool HasDetailedContacts => walkContacts != null && walkContacts.Length > 0;
    public int ContactCount => GetContactCount(false);
    public bool IsValid => !string.IsNullOrWhiteSpace(enemyId)
        && locomotionClip != null && ContactCount >= 2 && ContactCount <= 6;

    public int GetContactCount(bool running)
    {
        EnemyFootfallContact[] contacts = running && runContacts != null && runContacts.Length > 0
            ? runContacts : walkContacts;
        return contacts != null && contacts.Length > 0
            ? contacts.Length : contactPhases != null ? contactPhases.Length : 0;
    }

    public EnemyFootfallContact GetContact(bool running, int index)
    {
        EnemyFootfallContact[] contacts = running && runContacts != null && runContacts.Length > 0
            ? runContacts : walkContacts;
        if (contacts != null && index >= 0 && index < contacts.Length)
            return contacts[index];
        return new EnemyFootfallContact(GetContactPhase(index), Vector3.zero);
    }

    public float GetContactPhase(int index) => HasDetailedContacts
        ? index >= 0 && index < walkContacts.Length ? walkContacts[index].Phase : 0f
        : index >= 0 && contactPhases != null && index < contactPhases.Length ? contactPhases[index] : 0f;

    public void Configure(string id, AnimationClip clip, params float[] phases)
    {
        enemyId = id;
        locomotionClip = clip;
        contactPhases = phases != null ? (float[])phases.Clone() : System.Array.Empty<float>();
        runClip = clip;
        walkContacts = Array.Empty<EnemyFootfallContact>();
        runContacts = Array.Empty<EnemyFootfallContact>();
        if (!IsValid) throw new System.ArgumentException("Footfall profile needs one clip and 2-6 contacts.");
        float last = -1f;
        foreach (float phase in contactPhases)
        {
            if (phase < 0f || phase >= 1f || phase <= last)
                throw new System.ArgumentException("Contact phases must be unique, sorted, and inside [0,1).");
            last = phase;
        }
    }

    public void ConfigureDetailed(string id, AnimationClip walk, AnimationClip run,
        EnemyHitWeight weight, EnemyFootfallContact[] walking, EnemyFootfallContact[] running)
    {
        if (string.IsNullOrWhiteSpace(id) || walk == null || walking == null
            || walking.Length < 2 || walking.Length > 6)
            throw new ArgumentException("A footfall profile needs a walk clip and 2-6 contacts.");
        ValidateContacts(walking);
        if (running != null && running.Length > 0) ValidateContacts(running);
        enemyId = id;
        locomotionClip = walk;
        runClip = run != null ? run : walk;
        visualWeight = weight;
        walkContacts = (EnemyFootfallContact[])walking.Clone();
        runContacts = running != null ? (EnemyFootfallContact[])running.Clone()
            : Array.Empty<EnemyFootfallContact>();
        contactPhases = Array.ConvertAll(walking, contact => contact.Phase);
    }

    private static void ValidateContacts(EnemyFootfallContact[] contacts)
    {
        if (contacts.Length < 2 || contacts.Length > 6)
            throw new ArgumentException("Contacts must contain 2-6 points.");
        float previous = -1f;
        for (int i = 0; i < contacts.Length; i++)
        {
            float phase = contacts[i].Phase;
            if (phase < 0f || phase >= 1f || phase <= previous)
                throw new ArgumentException("Contact phases must be sorted and unique in [0,1).");
            previous = phase;
        }
    }
}
