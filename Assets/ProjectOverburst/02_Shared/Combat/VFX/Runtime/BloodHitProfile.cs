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
}
