using System;
using UnityEngine;

public enum EnemyBossThrowPayload { Rock, Elite, Alternate }

[CreateAssetMenu(menuName="OVERBURST/Enemies/Boss Composite Patterns",fileName="BCP_Boss")]
public sealed class EnemyBossCompositePatternSet : ScriptableObject
{
    [Serializable] public sealed class Payload
    {
        public EnemyDefinition definition;
        public GameObject flightVisual;
        [Min(.01f)] public float visualScale=1f;
        [Min(0f)] public float landingCenterHeight=1f;
        [Min(.1f)] public float flightSeconds=.85f;
        [Min(0f)] public float arcHeight=1.5f;
        [Min(0f)] public float wakeSeconds=.7f;
        [Min(1)] public int maximumAlive=3;
        public bool IsValid => definition!=null && definition.IsValid && flightVisual!=null && visualScale>0f
            && flightSeconds>0f && arcHeight>=0f && wakeSeconds>=0f && maximumAlive>0;
    }
    [Serializable] public sealed class Emission
    {
        [Range(0f,1f)] public float normalizedTime;
        public int phase;
        [Min(0)] public int count=1;
        public Payload payload;
        public float yawOffset;
        [Min(1f)] public float landingDistance=9f;
        [Min(0f)] public float scatter=1f;
    }
    [Serializable] public sealed class Pattern
    {
        public EnemyBossAttackMaterial material;
        public GameObject bloodSpray;
        [Min(.01f)] public float sprayScale=1f;
        [Min(.1f)] public float beamRadius=.7f;
        public float firstSweepStartYaw=-45f,firstSweepEndYaw=45f;
        public bool reverseSecondSweep;
        public Emission[] emissions=Array.Empty<Emission>();
        public bool IsValid
        {
            get
            {
                if(material==null || !material.IsValid || bloodSpray==null || sprayScale<=0f || beamRadius<=0f)return false;
                foreach(var item in emissions)
                    if(item==null || item.payload==null || !item.payload.IsValid || item.count<0
                        || item.phase<0 || item.phase>=material.strikes.Length
                        || item.normalizedTime<material.strikes[item.phase].contactStart
                        || item.normalizedTime>material.strikes[item.phase].contactEnd)return false;
                return true;
            }
        }
    }
    public EnemyCatalog summonCatalog;
    public Pattern[] spitPatterns=Array.Empty<Pattern>();
    public EnemyBossAttackMaterial throwMaterial;
    public Payload elite;
    public EnemyBossThrowPayload throwPayload=EnemyBossThrowPayload.Alternate;
    [Min(0f)] public float eliteRevealFrame=70f;
    [Min(0f)] public float eliteFullSizeFrame=100f;
    [TextArea] public string authoringNotes;
    public bool IsValid
    {
        get
        {
            if(summonCatalog==null || throwMaterial==null || !throwMaterial.IsValid || elite==null || !elite.IsValid
                || spitPatterns==null || spitPatterns.Length==0 || eliteFullSizeFrame<eliteRevealFrame)return false;
            foreach(var entry in spitPatterns)if(entry==null || !entry.IsValid)return false;
            return true;
        }
    }
    public Pattern Find(EnemyAbilityDefinition ability)
    {foreach(var entry in spitPatterns)if(entry!=null && entry.material!=null && entry.material.ability==ability)return entry;return null;}
}
