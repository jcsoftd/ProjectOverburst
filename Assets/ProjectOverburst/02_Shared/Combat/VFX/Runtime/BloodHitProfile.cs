using UnityEngine;

[CreateAssetMenu(menuName = "OVERBURST/VFX/Blood Profile")]
public sealed class BloodHitProfile : ScriptableObject
{
    [Tooltip("Suppress blood spray and ground marks for skeletons and other bloodless enemies.")]
    public bool suppressBlood;
    public Color mainColor = new Color(.5f, .6f, .55f);
    public Color secondaryColor = new Color(.2f, .3f, .25f);
    public Color specularColor = new Color(.5f, .55f, .5f);
    [Range(0f, 1f)] public float specular = .2f;
    [Range(.1f, 8f)] public float size = 4.8f;
    [Tooltip("suppressBlood 몬스터의 피격 재질음(뼈·금속·영체). 비어 있으면 소리 없음. 여러 개면 랜덤.")]
    public AudioClip[] bloodlessHitClips = System.Array.Empty<AudioClip>();

    public AudioClip PickBloodlessHitClip()
    {
        if (bloodlessHitClips == null || bloodlessHitClips.Length == 0) return null;
        int start = Random.Range(0, bloodlessHitClips.Length);
        for (int i = 0; i < bloodlessHitClips.Length; i++)
        {
            AudioClip clip = bloodlessHitClips[(start + i) % bloodlessHitClips.Length];
            if (clip != null) return clip;
        }
        return null;
    }
}
