using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class ItemInscriptionQualityVerifier
{
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
    public static string Verify()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return "DEFERRED: Editor is busy";
        if (typeof(ItemQualityIconEffect).GetMethod("ReleaseMaterial",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)==null) return "DEFERRED: latest effect not compiled";
        checks = 0;
        var random = UnityEngine.Random.state;
        var preview = EditorSceneManager.NewPreviewScene();
        var temporary = new List<Object>();
        int fixtures = 0;
        try
        {
            var types = new[] { typeof(WeaponItemData), typeof(GearItemData), typeof(BagItemData), typeof(FlaskItemData), typeof(ElementGemItemData) };
            var samples = new List<BaseItemData>();
            foreach (var type in types)
            {
                var assets = AssetDatabase.FindAssets("t:"+type.Name,new[]{"Assets/ProjectOverburst"}).Select(AssetDatabase.GUIDToAssetPath)
                    .OrderBy(p=>p).Select(p=>AssetDatabase.LoadAssetAtPath<BaseItemData>(p)).Where(a=>a&&a.icon&&(!(a is ElementGemItemData gem)||ElementGemItemData.IsAllowed(gem.element,gem.fixedGrade))).Take(3).ToArray();
                Check(assets.Length > 0, "Missing real sample " + type.Name);
                samples.AddRange(assets);
            }
            foreach (var data in samples) for (int g=0;g<=7;g++)
            {
                if (data is FlaskItemData && g==7) continue;
                if (data is ElementGemItemData definition && !ElementGemItemData.IsAllowed(definition.element,(ItemGrade)g)) continue;
                BaseItemData source = data;
                if (data is ElementGemItemData gem) { var copy=Object.Instantiate(gem);copy.fixedGrade=(ItemGrade)g;temporary.Add(copy);source=copy; }
                for (int seed=0;seed<4;seed++)
                {
                    var item=Fixture(source,(ItemGrade)g,seed);
                    string before=JsonUtility.ToJson(item);
                    var rng=UnityEngine.Random.state;
                    float expected=Expected(item);
                    for (int n=0;n<3;n++)
                    {
                        bool evaluated=ItemInscriptionQuality.TryEvaluate(item,out var quality);
                        Check(evaluated==(g>0),"Only grades with stars are evaluated: "+data.GetType().Name);
                        if(evaluated){Check(quality.Score==expected,"Saved color improvement score");Check(quality.Grade==item.grade,"Own grade determines quality");}
                    }
                    Check(before==JsonUtility.ToJson(item),"Display must not reroll or mutate saved item");
                    Check(rng.Equals(UnityEngine.Random.state),"Display must not consume RNG");
                    var levels=new[]{1,27,100};
                    foreach(int level in levels){item.level=level;bool evaluated=ItemInscriptionQuality.TryEvaluate(item,out var result);Check(evaluated==(g>0)&&(!evaluated||result.Score==expected),"Level-independent quality");}
                    fixtures++;
                }
            }
            int[][] starts={new[]{1,2,3,4,5},new[]{1,2,4,5,7},new[]{3,4,7,8,10},new[]{5,7,10,13,15},new[]{10,13,16,19,22},new[]{13,16,20,22,25},new[]{19,21,23,25,27}};
            for(int g=1;g<=7;g++)for(int score=-1;score<=2*WeaponGradeStatRoller.GetMeleePositiveStarCount((ItemGrade)g);score++)
                Check((int)ItemInscriptionQuality.Classify((ItemGrade)g,score)==starts[g-1].Count(s=>score>=s),"Approved grade boundary "+g+"/"+score);
            foreach(var data in samples.GroupBy(d=>d.GetType()).Select(g=>g.First()))
            {
                var item=Fixture(data,ItemGrade.Mythic,0);
                var mixed=new List<WeaponGradeStarType>{WeaponGradeStarType.White,WeaponGradeStarType.Green,WeaponGradeStarType.Yellow,WeaponGradeStarType.Red};
                Assign(item,mixed);
                Check(ItemInscriptionQuality.TryEvaluate(item,out var result)&&result.Score==3,"Same color score across every system");
                foreach(var grade in new[]{ItemGrade.Uncommon,ItemGrade.Mythic,ItemGrade.Cursed})
                {
                    item.grade=grade;int count=WeaponGradeStatRoller.GetMeleePositiveStarCount(grade);
                    for(int tier=0;tier<6;tier++)
                    {
                        int score=tier==0?0:starts[(int)grade-1][tier-1];
                        Assign(item,StarsForScore(count,score));
                        Check(ItemInscriptionQuality.TryEvaluate(item,out result)&&result.Score==score&&(int)result.Tier==tier,"Six qualities within own grade "+data.GetType().Name+"/"+grade);
                    }
                    Assign(item,Enumerable.Repeat(WeaponGradeStarType.White,count).ToList());
                    Check(ItemInscriptionQuality.TryEvaluate(item,out result)&&result.Score==0&&result.Tier==ItemInscriptionQualityTier.Lowest,"More white stars never improve quality");
                    Assign(item,Enumerable.Repeat(WeaponGradeStarType.Yellow,count).ToList());
                    Check(ItemInscriptionQuality.TryEvaluate(item,out result)&&result.Tier==ItemInscriptionQualityTier.Masterpiece,"Best colors reach masterpiece");
                }
                item.grade=ItemGrade.Cursed;
                var cursed=StarsForScore(15,27);Assign(item,cursed);ItemInscriptionQuality.TryEvaluate(item,out var beforeRed);
                cursed.AddRange(Enumerable.Repeat(WeaponGradeStarType.Red,5));Assign(item,cursed);
                Check(ItemInscriptionQuality.TryEvaluate(item,out result)&&result.Score==beforeRed.Score&&result.Tier==beforeRed.Tier,"Fixed curse penalty does not lower within-grade quality");
                item.grade=ItemGrade.Common;
                Check(!ItemInscriptionQuality.TryEvaluate(item,out _),"Common grade is not assigned a quality tier");
                item.grade=ItemGrade.Mythic;Assign(item,new List<WeaponGradeStarType>());
                Check(!ItemInscriptionQuality.TryEvaluate(item,out _),"Missing stars do not masquerade as a quality roll");
            }
            var legacy=Fixture(samples[0],ItemGrade.Mythic,0);
            legacy.weaponGradeStatRolls=new List<WeaponGradeStatRoll>{new WeaponGradeStatRoll{positiveStarCount=20,negativeStarCount=5}};
            Check(ItemInscriptionQuality.TryEvaluate(legacy,out var legacyQuality)&&legacyQuality.Score==0&&legacyQuality.Tier==ItemInscriptionQualityTier.Lowest,"Uncolored legacy stars provide no invented color bonus");
            var empty=ScriptableObject.CreateInstance<ConsumableItemData>();temporary.Add(empty);
            var unsupported=Fixture(empty,ItemGrade.Common,0);
            Check(!ItemInscriptionQuality.TryEvaluate(null,out _)&&!ItemInscriptionQuality.TryEvaluate(unsupported,out _),"No quality on non-star items");
            var iconGO=new GameObject("Quality Icon Check",typeof(RectTransform),typeof(Image));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(iconGO,preview);
            var image=iconGO.GetComponent<Image>();image.sprite=samples[0].icon;var original=image.material;
            var badgeGO=new GameObject("Badge",typeof(RectTransform),typeof(Image));badgeGO.transform.SetParent(iconGO.transform,false);var badge=badgeGO.GetComponent<Image>();badge.color=Color.cyan;
            var dimItem=Fixture(samples[0],ItemGrade.Mythic,0);Assign(dimItem,StarsForScore(20,0));
            Color baseline=new Color(.6f,.5f,.4f,.8f);
            for(int n=0;n<10;n++)ItemQualityIconEffect.Present(image,dimItem,baseline);
            Check(Mathf.Approximately(image.color.r,.6f*.92f)&&image.color.a==.8f,"Lowest: slight tint without cumulative dimming or opacity loss");
            Assign(dimItem,StarsForScore(20,13));ItemQualityIconEffect.Present(image,dimItem,baseline);
            Check(Mathf.Approximately(image.color.r,.6f*.96f),"Low: slight tint");Check(badge.color==Color.cyan,"Badges are unaffected");
            dimItem.grade=ItemGrade.Common;ItemQualityIconEffect.Present(image,dimItem,baseline);
            Check(image.color==baseline&&image.material==original,"Common grade restores full brightness without quality effects");
            ItemQualityIconEffect.Present(image,unsupported,baseline);Check(image.color==baseline&&image.material==original,"Switch restores normal item");
            image.sprite=null;ItemQualityIconEffect.Present(image,null,Color.clear);Check(image.color==Color.clear&&image.material==original,"Empty slot resets");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstTooltip_Rpg11.prefab");Check(prefab,"Real tooltip prefab");
            var tooltip=Object.Instantiate(prefab);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(tooltip,preview);
            var view=tooltip.GetComponent<OverburstUITooltipView>();Check(view,"Real tooltip view");
            var heading=tooltip.transform.Find("Approved Content/Quality Heading").GetComponent<TextMeshProUGUI>();
            foreach(var data in samples.GroupBy(d=>d.GetType()).Select(g=>g.First()))
            {
                var item=Fixture(data,data is ElementGemItemData gem?gem.fixedGrade:ItemGrade.Legendary,11);
                view.Present(item,null,TooltipCompareMode.EquippedReference);bool evaluated=ItemInscriptionQuality.TryEvaluate(item,out var result);
                Check(heading.text==(evaluated?result.Heading:"품질 각인"),"Native tooltip heading "+data.GetType().Name);
                Check(Mathf.Approximately(view.Rect.sizeDelta.x,432),"Unchanged native tooltip width");
                Check(Mathf.Approximately(heading.rectTransform.sizeDelta.x,120)&&Mathf.Approximately(heading.rectTransform.sizeDelta.y,24),"Existing column header dimensions");
            }
            for(int tier=0;tier<6;tier++){var text="각인 품질 "+ItemInscriptionQuality.Label((ItemInscriptionQualityTier)tier);Check(heading.GetPreferredValues(text).x<=120,"Full quality label fits native header: "+text);}
            var shader=Resources.Load<Shader>("Shaders/ItemQualityShineUI");Check(shader&&shader.isSupported,"Native UI shader support");
            Check(!ShaderUtil.GetShaderMessages(shader).Any(m=>m.severity.ToString()=="Error"),"No shader compiler errors");
            int[] glowScores={0,13,16,20,22,25};float[] glowStrengths={0,0,0,.035f,.06f,.085f};
            for(int tier=0;tier<6;tier++)
            {
                var quality=new ItemInscriptionQualityResult(ItemGrade.Mythic,glowScores[tier]);
                Check(quality.GlowStrength==glowStrengths[tier],"Six quality glow strengths");
                Check(quality.ShineInterval==(tier==5?6.5f:0f),"Only masterpiece periodically sweeps");
            }
            string dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../../개인파일/코덱스산출/UI/20261003_StarQualityContinuous/Verification"));Directory.CreateDirectory(dir);
            int distributionFixtures=VerifyDistribution(samples,dir);
            string report="PASS: "+checks+" checks; "+fixtures+" real roll fixtures; five star systems; grade-relative six tiers; Common, curse, RNG/save invariance, native tooltip and icon reset";
            report+="; "+distributionFixtures+" natural distribution samples";
            File.WriteAllText(Path.Combine(dir,"editor-result.txt"),report);
            return report;
        }
        finally
        {
            foreach(var asset in temporary)if(asset)Object.DestroyImmediate(asset);
            EditorSceneManager.ClosePreviewScene(preview);
            UnityEngine.Random.state=random;
        }
    }

    private static int VerifyDistribution(List<BaseItemData> samples,string directory)
    {
        var records=new List<object>();int total=0;
        foreach(var group in samples.GroupBy(d=>d.GetType()))for(int g=1;g<=7;g++)
        {
            if(group.Key==typeof(FlaskItemData)&&g==7)continue;
            var definitions=group.Key==typeof(WeaponItemData)
                ?AssetDatabase.FindAssets("t:WeaponItemData",new[]{"Assets/ProjectOverburst"}).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p=>p)
                    .Select(p=>AssetDatabase.LoadAssetAtPath<BaseItemData>(p)).Where(a=>a is WeaponItemData w&&a.icon&&WeaponGradeStatRoller.IsMeleeWeapon(w)).ToArray()
                :group.ToArray();
            int[] counts=new int[6];int count=0;
            for(int definitionIndex=0;definitionIndex<definitions.Length;definitionIndex++)
            {
                var definition=definitions[definitionIndex];
                BaseItemData source=definition;ElementGemItemData copy=null;
                if(definition is ElementGemItemData gem)
                {
                    if(!ElementGemItemData.IsAllowed(gem.element,(ItemGrade)g))continue;
                    copy=Object.Instantiate(gem);copy.fixedGrade=(ItemGrade)g;source=copy;
                }
                try
                {
                    int seeds=group.Key==typeof(WeaponItemData)?64:256;
                    for(int seed=0;seed<seeds;seed++)
                    {
                        int rollSeed=seed+3701+definitionIndex*65537+g*100003;
                        UnityEngine.Random.InitState(rollSeed);
                        var item=Fixture(source,(ItemGrade)g,rollSeed);
                        var saved=JsonUtility.ToJson(item);var random=UnityEngine.Random.state;
                        Check(ItemInscriptionQuality.TryEvaluate(item,out var result)&&result.Score==Expected(item),"Natural distribution score "+group.Key.Name+"/"+g);
                        Check(saved==JsonUtility.ToJson(item)&&random.Equals(UnityEngine.Random.state),"Natural distribution preserves saved roll and RNG");
                        counts[(int)result.Tier]++;count++;total++;
                    }
                }
                finally{if(copy)Object.DestroyImmediate(copy);}
            }
            records.Add(new{type=group.Key.Name,grade=((ItemGrade)g).ToString(),definitions=definitions.Length,samples=count,tierCounts=counts,
                percentages=counts.Select(n=>count==0?0:Math.Round(n*100.0/count,3)).ToArray()});
        }
        File.WriteAllText(Path.Combine(directory,"roll-distribution.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{samples=total,records},Newtonsoft.Json.Formatting.Indented));
        return total;
    }

    internal static ItemData Fixture(BaseItemData data,ItemGrade grade,int seed)
    {
        var item=(ItemData)FormatterServices.GetUninitializedObject(typeof(ItemData));
        item.baseData=data;item.level=27;item.grade=grade;item.stackCount=1;item.balanceVersion=OverburstCombatBalance.ItemBalanceVersion;
        item.runtimeInstanceId=Guid.NewGuid().ToString("N");item.acquisitionOrder=1;item.gearRolls=new List<GearStatRoll>();item.bagOptions=new List<BagRandomOptionRoll>();item.weaponGradeStatRolls=new List<WeaponGradeStatRoll>();
        if(data is WeaponItemData weapon)item.weaponGradeStatRolls=WeaponGradeStatRoller.Roll(grade,weapon,out item.meleeStarDistributionProfile);
        else if(data is GearItemData gear)item.gearRolls=GearQuality.Roll(gear,grade,seed);
        else if(data is BagItemData)item.bagState=BagQuality.Roll(grade,seed);
        else if(data is FlaskItemData)item.flaskState=FlaskGradeRoller.Roll(grade,seed);
        else if(data is ElementGemItemData gem)item.gemState=ElementGemQuality.Roll(gem,seed);
        return item;
    }
    private static int Expected(ItemData item)
    {
        IEnumerable<WeaponGradeStarType> stars;
        if(item.baseData is WeaponItemData)stars=item.weaponGradeStats.SelectMany(r=>r.GetDisplayStars()).Select(s=>s.starType);
        else if(item.baseData is GearItemData)stars=item.gearRolls.SelectMany(r=>r.stars);
        else if(item.baseData is BagItemData)stars=item.bagState.rows.SelectMany(r=>r.stars);
        else if(item.baseData is FlaskItemData)stars=item.flaskState.rolls.SelectMany(r=>r.stars);
        else stars=item.gemState.rolls.SelectMany(r=>r.stars);
        return stars.Count(s=>s==WeaponGradeStarType.Green)+2*stars.Count(s=>s==WeaponGradeStarType.Yellow);
    }
    internal static List<WeaponGradeStarType> StarsForScore(int count,int score)
    {
        var stars=Enumerable.Repeat(WeaponGradeStarType.Yellow,score/2).ToList();
        if(score%2!=0)stars.Add(WeaponGradeStarType.Green);
        stars.AddRange(Enumerable.Repeat(WeaponGradeStarType.White,count-stars.Count));
        return stars;
    }
    internal static void Assign(ItemData item,List<WeaponGradeStarType> stars)
    {
        if(item.baseData is WeaponItemData)item.weaponGradeStatRolls=new List<WeaponGradeStatRoll>{new WeaponGradeStatRoll{statType=WeaponGradeStatType.Damage,starRolls=stars.Select(s=>new WeaponGradeStarRoll{starType=s}).ToList()}};
        else if(item.baseData is GearItemData)item.gearRolls=new List<GearStatRoll>{new GearStatRoll{stars=stars}};
        else if(item.baseData is BagItemData)item.bagState=new BagInstanceState{rows=new List<BagStatRoll>{new BagStatRoll{stars=stars}}};
        else if(item.baseData is FlaskItemData)item.flaskState=new FlaskInstanceState{rolls=new List<FlaskStatRoll>{new FlaskStatRoll{stars=stars}}};
        else item.gemState=new ElementGemState{rolls=new List<ElementGemRoll>{new ElementGemRoll{stars=stars}}};
    }
}
