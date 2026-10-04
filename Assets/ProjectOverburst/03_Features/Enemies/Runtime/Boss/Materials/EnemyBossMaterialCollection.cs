using System;
using UnityEngine;

[CreateAssetMenu(menuName="OVERBURST/Enemies/Boss Material Collection",fileName="BMC_Boss")]
public sealed class EnemyBossMaterialCollection : ScriptableObject
{
    [Serializable]
    public sealed class Motion
    {
        public string id;
        public string sourcePath;
        public string sourceGuid;
        public AnimationClip source;
        public AnimationClip runtime;
        public string state;
        public bool rootMotionVariant;
        public bool preparation;
        public bool IsPlayable => !rootMotionVariant && runtime != null && !string.IsNullOrEmpty(state);
    }
    public EnemyDefinition actorDefinition;
    public EnemyCatalog catalog;
    public EnemyBossAttackMaterial[] attacks = Array.Empty<EnemyBossAttackMaterial>();
    public Motion[] motions = Array.Empty<Motion>();
    public Mesh boulderMesh;
    public float[] radialFillProfile;
    public float[] radialBorderProfile;
    public Material boulderMaterial;
    public string boulderLeftHandBone = "Crustaspikan_ L Hand";
    public string boulderRightHandBone = "Crustaspikan_ R Hand";
    public Vector3 boulderOffset = new Vector3(0f, .25f, .15f);
    public float boulderVisualRadius = .85f;
    public EnemyBossAttackMaterial Find(EnemyAbilityDefinition ability)
    {
        for(int i=0;i<attacks.Length;i++) if(attacks[i]!=null && attacks[i].ability==ability) return attacks[i];
        return null;
    }
    public Motion FindMotion(string id)
    {
        for(int i=0;i<motions.Length;i++) if(motions[i]!=null && motions[i].id==id) return motions[i];
        return null;
    }
}
