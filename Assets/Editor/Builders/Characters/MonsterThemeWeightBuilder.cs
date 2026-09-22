using UnityEditor;
using UnityEngine;

public static class MonsterThemeWeightBuilder
{
    public static EnemyHitWeightProfile Resolve(EnemyThemeTier tier)
    {
        string folder=MonsterThemeCombatBuilder.Root+"/HitWeights";
        if(!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(MonsterThemeCombatBuilder.Root,"HitWeights");
        var weight=tier==EnemyThemeTier.Small?EnemyHitWeight.Light:tier==EnemyThemeTier.Elite?EnemyHitWeight.Heavy:EnemyHitWeight.Standard;
        string path=folder+"/"+weight+".asset";
        var profile=AssetDatabase.LoadAssetAtPath<EnemyHitWeightProfile>(path);
        if(profile!=null)return profile; // Inspector tuning is authoritative once created.
        profile=ScriptableObject.CreateInstance<EnemyHitWeightProfile>();
        switch(weight)
        {
            case EnemyHitWeight.Light: profile.Configure(weight,.10f,.50f,.16f,.22f,.12f,.22f,.35f);break;
            case EnemyHitWeight.Standard: profile.Configure(weight,.065f,.32f,.16f,.16f,.035f,.18f,.40f);break;
            default: profile.Configure(weight,.03f,.14f,.14f,.10f,0f,.16f,.48f);break;
        }
        AssetDatabase.CreateAsset(profile,path);return profile;
    }
}
