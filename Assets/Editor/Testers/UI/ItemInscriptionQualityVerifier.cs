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
                    for (int n=0;n<3;n++) { Check(ItemInscriptionQuality.TryEvaluate(item,out var quality),"Supported type "+data.GetType().Name);Check(Mathf.Approximately(quality.Score,expected),"Saved stars sum"); }
                    Check(before==JsonUtility.ToJson(item),"Display must not reroll or mutate saved item");
                    Check(rng.Equals(UnityEngine.Random.state),"Display must not consume RNG");
                    var levels=new[]{1,27,100};
                    foreach(int level in levels){item.level=level;ItemInscriptionQuality.TryEvaluate(item,out var result);Check(Mathf.Approximately(result.Score,expected),"Level-independent quality");}
                    fixtures++;
                }
            }
            float[] boundaries={-1,0,4.5f,5,9.5f,10,15.5f,16,23.5f,24,31.5f,32,40};
            int[] tiers={0,0,0,1,1,2,2,3,3,4,4,5,5};
            for(int i=0;i<boundaries.Length;i++)Check((int)ItemInscriptionQuality.Classify(boundaries[i])==tiers[i],"Boundary "+boundaries[i]);
            foreach(var data in samples.GroupBy(d=>d.GetType()).Select(g=>g.First()))
            {
                var item=Fixture(data,data is ElementGemItemData gem?gem.fixedGrade:ItemGrade.Common,0);
                var mixed=new List<WeaponGradeStarType>{WeaponGradeStarType.White,WeaponGradeStarType.Green,WeaponGradeStarType.Yellow,WeaponGradeStarType.Red};
                Assign(item,mixed);
                Check(ItemInscriptionQuality.TryEvaluate(item,out var result)&&result.Score==3.5f,"Same colored stars across every system");
                int[] yellowCounts={0,3,5,8,12,16};
                for(int tier=0;tier<6;tier++){Assign(item,Enumerable.Repeat(WeaponGradeStarType.Yellow,yellowCounts[tier]).ToList());ItemInscriptionQuality.TryEvaluate(item,out result);Check((int)result.Tier==tier,"All six qualities "+data.GetType().Name);}
            }
            var empty=ScriptableObject.CreateInstance<ConsumableItemData>();temporary.Add(empty);
            var unsupported=Fixture(empty,ItemGrade.Common,0);
            Check(!ItemInscriptionQuality.TryEvaluate(null,out _)&&!ItemInscriptionQuality.TryEvaluate(unsupported,out _),"No quality on non-star items");
            var iconGO=new GameObject("Quality Icon Check",typeof(RectTransform),typeof(Image));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(iconGO,preview);
            var image=iconGO.GetComponent<Image>();image.sprite=samples[0].icon;var original=image.material;
            var badgeGO=new GameObject("Badge",typeof(RectTransform),typeof(Image));badgeGO.transform.SetParent(iconGO.transform,false);var badge=badgeGO.GetComponent<Image>();badge.color=Color.cyan;
            var dimItem=Fixture(samples[0],ItemGrade.Common,0);Assign(dimItem,new List<WeaponGradeStarType>());
            Color baseline=new Color(.6f,.5f,.4f,.8f);
            for(int n=0;n<10;n++)ItemQualityIconEffect.Present(image,dimItem,baseline);
            Check(Mathf.Approximately(image.color.r,.6f*.92f)&&image.color.a==.8f,"Lowest: slight tint without cumulative dimming or opacity loss");
            Assign(dimItem,Enumerable.Repeat(WeaponGradeStarType.Yellow,3).ToList());ItemQualityIconEffect.Present(image,dimItem,baseline);
            Check(Mathf.Approximately(image.color.r,.6f*.96f),"Low: slight tint");Check(badge.color==Color.cyan,"Badges are unaffected");
            ItemQualityIconEffect.Present(image,unsupported,baseline);Check(image.color==baseline&&image.material==original,"Switch restores normal item");
            image.sprite=null;ItemQualityIconEffect.Present(image,null,Color.clear);Check(image.color==Color.clear&&image.material==original,"Empty slot resets");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstTooltip_Rpg11.prefab");Check(prefab,"Real tooltip prefab");
            var tooltip=Object.Instantiate(prefab);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(tooltip,preview);
            var view=tooltip.GetComponent<OverburstUITooltipView>();Check(view,"Real tooltip view");
            var heading=tooltip.transform.Find("Approved Content/Quality Heading").GetComponent<TextMeshProUGUI>();
            foreach(var data in samples.GroupBy(d=>d.GetType()).Select(g=>g.First()))
            {
                var item=Fixture(data,data is ElementGemItemData gem?gem.fixedGrade:ItemGrade.Legendary,11);
                view.Present(item,null,TooltipCompareMode.EquippedReference);ItemInscriptionQuality.TryEvaluate(item,out var result);
                Check(heading.text==result.Heading,"Native tooltip heading "+data.GetType().Name);
                Check(Mathf.Approximately(view.Rect.sizeDelta.x,432),"Unchanged native tooltip width");
                Check(Mathf.Approximately(heading.rectTransform.sizeDelta.x,120)&&Mathf.Approximately(heading.rectTransform.sizeDelta.y,24),"Existing column header dimensions");
            }
            for(int tier=0;tier<6;tier++){var text="각인 품질 "+ItemInscriptionQuality.Label((ItemInscriptionQualityTier)tier);Check(heading.GetPreferredValues(text).x<=120,"Full quality label fits native header: "+text);}
            var shader=Resources.Load<Shader>("Shaders/ItemQualityShineUI");Check(shader&&shader.isSupported,"Native UI shader support");
            Check(!ShaderUtil.GetShaderMessages(shader).Any(m=>m.severity.ToString()=="Error"),"No shader compiler errors");
            string report="PASS: "+checks+" checks; "+fixtures+" real roll fixtures; five star systems; six tiers; native tooltip and icon reset";
            string dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../../개인파일/코덱스산출/UI/20261003_StarQuality/Verification"));Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"editor-result.txt"),report);
            return report;
        }
        finally
        {
            foreach(var asset in temporary)if(asset)Object.DestroyImmediate(asset);
            EditorSceneManager.ClosePreviewScene(preview);
            UnityEngine.Random.state=random;
        }
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
    private static float Expected(ItemData item)
    {
        if(item.baseData is WeaponItemData)return item.weaponGradeStats.Sum(r=>r.GetDisplayStars().Sum(s=>s.IsNegative?-1f:s.ValueMultiplier));
        if(item.baseData is GearItemData)return item.gearRolls.Sum(r=>r.Weight);
        if(item.baseData is BagItemData)return item.bagState.rows.Sum(r=>r.Weight);
        if(item.baseData is FlaskItemData)return item.flaskState.rolls.Sum(r=>r.Weight);
        return item.gemState.rolls.Sum(r=>r.Weight);
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
