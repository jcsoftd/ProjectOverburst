using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

public static partial class SettingsPresentationVerifier
{
    public static void StartVolumetric(string directory)
    {
        RequireIdle(); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"verify-c"),""); Start(directory);
    }
    public static string VolumetricAssetsCheck(string directory)
    {
        RequireIdle(); var list=new List<string>();
        void Assert(bool value,string message) { if(!value)throw new InvalidOperationException(message);list.Add(message); }
        var catalog=AssetDatabase.LoadAssetAtPath<BloodEffectsPackCatalog>(VolumetricBloodBuilder.CatalogPath);
        Assert(catalog && catalog.volumetric && catalog.sprays.Length==19,"C catalog 17 impacts and 2 drops");
        foreach(var definition in catalog.sprays)
        {
            var data=definition.prefab.GetComponent<VolumetricBloodAnimationData>();
            Assert(data && data.layers.Length>0,"VAT metadata "+definition.label);
            Assert(definition.prefab.GetComponentsInChildren<MonoBehaviour>(true).All(c=>c is VolumetricBloodAnimationData),"no supplier lifecycle "+definition.label);
            foreach(var layer in data.layers)
            {
                Assert(layer.renderer && layer.frames>0 && layer.seconds>0,"bounded VAT layer "+definition.label);
                foreach(var material in layer.renderer.sharedMaterials)
                    Assert(material && material.shader.isSupported && !ShaderUtil.ShaderHasError(material.shader) && material.GetTexture("_posTex") && material.GetTexture("_nTex"),"VAT texture and shader "+definition.label);
            }
            foreach(var t in definition.prefab.GetComponentsInChildren<Transform>(true))Assert(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0,"no missing scripts "+t.name);
        }
        Assert(catalog.lethalDecals.Length==17 && catalog.trailDecals.Length==3,"C native URP ground variety");
        foreach(var prefab in catalog.lethalDecals)
        {
            var decal=prefab.GetComponent<DecalProjector>();
            Assert(decal && decal.material && decal.material.shader.isSupported && !ShaderUtil.ShaderHasError(decal.material.shader) && decal.material.HasProperty("_TintColor"),"URP ground tint "+prefab.name);
        }
        var settings=AssetDatabase.LoadAssetAtPath<GameObject>(OverburstGameMenuBuilder.PrefabPath).GetComponentInChildren<OverburstSettingsPanel>(true);
        Assert(settings.bloodStyle.options.Count==3,"serialized three style options");
        Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"assets-result.json"),JsonConvert.SerializeObject(new{success=true,checks=list},Formatting.Indented));
        return "PASS "+list.Count;
    }

    static IEnumerator VerifyCGroundFixture()
    {
        OverburstGameSettings.BloodStyle=BloodEffectStyle.Volumetric;OverburstGameSettings.BloodUniformRed=true;
        var ground=Object.FindFirstObjectByType<BloodGroundDecalService>();ground.ClearForComparison();
        var profile=Resources.Load<BloodHitProfile>(BloodHitVfxService.PlayerProfilePath);
        Vector3 center=PlayerInputFacade.Current.transform.position+Vector3.up*20f;
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Owned C Decal Surface";owned.Add(floor);
        floor.transform.position=center;floor.transform.localScale=new Vector3(5f,.1f,5f);
        var material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="Owned C Floor Material"};
        material.SetColor("_BaseColor",new Color(.45f,.42f,.39f));material.SetFloat("_Smoothness",0f);floor.GetComponent<Renderer>().sharedMaterial=material;
        var root=new GameObject("Owned C Ground Camera");owned.Add(root);var camera=root.AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.enabled=false;
        int rendererIndex=new SerializedObject(Camera.main.GetUniversalAdditionalCameraData()).FindProperty("m_RendererIndex").intValue;
        camera.GetUniversalAdditionalCameraData().SetRenderer(rendererIndex);
        camera.orthographic=true;camera.orthographicSize=2f;camera.transform.position=center+new Vector3(2,4,-3);camera.transform.LookAt(center);
        RenderTexture rt=null;Texture2D pixels=null;
        try
        {
            rt=new RenderTexture(960,640,24,RenderTextureFormat.ARGB32);rt.Create();pixels=new Texture2D(960,640,TextureFormat.RGB24,false);camera.targetTexture=rt;
            Color32[] Capture(string name)
            {
                var previous=RenderTexture.active;
                try{camera.Render();RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,960,640),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());return pixels.GetPixels32();}
                finally{RenderTexture.active=previous;}
            }
            Physics.SyncTransforms();yield return Frames(3);var baseline=Capture("C_Floor_Baseline");var rows=new List<object>();
            for(int i=0;i<17;i++)
            {
                ground.ClearForComparison();ground.Request(profile,center+Vector3.up,Vector3.forward,CombatImpactShape.Downward,1f,2,false,0f);yield return Frames(3);
                var decal=ground.GetComponentsInChildren<DecalProjector>(true).First(p=>p.gameObject.activeSelf);
                yield return Wait(1.5f);
                var image=Capture("C_Floor_"+i);int changed=0,red=0;
                for(int p=0;p<image.Length;p++)if(Math.Abs(image[p].r-baseline[p].r)+Math.Abs(image[p].g-baseline[p].g)+Math.Abs(image[p].b-baseline[p].b)>25)
                {changed++;if(image[p].r>image[p].g*1.3f && image[p].r>image[p].b*1.2f)red++;}
                rows.Add(new{i,rendererIndex,changed,red,height=decal.transform.position.y,size=decal.size.x,shader=decal.material.shader.name});
                File.WriteAllText(Path.Combine(output,"C-controlled-ground.json"),JsonConvert.SerializeObject(rows,Formatting.Indented));
                Check(changed>100 && red>50,"C controlled native URP decal pixels and red tint "+i);
            }
        }
        finally{camera.targetTexture=null;ground.ClearForComparison();if(pixels)Object.DestroyImmediate(pixels);if(rt){rt.Release();Object.DestroyImmediate(rt);}Object.DestroyImmediate(root);owned.Remove(root);Object.DestroyImmediate(floor);owned.Remove(floor);Object.DestroyImmediate(material);}
    }
    static void CheckCOnly(BloodHitVfxService blood,string context)
    {
        Check(blood.GetComponentsInChildren<VisualEffect>(true).Length==0 && blood.GetComponentsInChildren<ParticleSystem>(true).Length==0,"no A or B spray instances "+context);
        Check(blood.GetComponentsInChildren<VolumetricBloodAnimationData>(true).Length==BloodEffectsPackPool.Capacity,"only 48 C leases "+context);
        Check(blood.GetComponentsInChildren<DecalProjector>(true).Length==BloodGroundDecalService.Capacity,"same 96 ground leases "+context);
        Check(Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,FindObjectsSortMode.None).All(c=>!c.GetType().Name.StartsWith("BFX_",StringComparison.Ordinal)),"no vendor update host or callbacks "+context);
    }
    static void CheckCReload()
    {
        var blood=Object.FindFirstObjectByType<BloodHitVfxService>();
        Check(OverburstGameSettings.BloodStyle==BloodEffectStyle.Volumetric && BloodHitVfxService.CurrentStyle==BloodEffectStyle.Volumetric,"saved C applied directly at boot");
        CheckCOnly(blood,"saved C boot");
        Check(Mathf.Approximately(BloodComparisonTuning.Scale,1.2f) && Mathf.Approximately(BloodComparisonTuning.GroundBrightness,1.2f),"C tuning restored at boot");
        Check(OverburstGameSettings.BloodUniformRed,"C palette restored at boot");
    }
    static IEnumerator VerifyVolumetric(CombatHealth player,CombatHealth enemy,GameObject targetRoot,BloodHitVfxService blood,BloodGroundDecalService ground)
    {
        var menu=OverburstGameMenu.Instance;menu.Open();menu.OpenSettings();var panel=menu.settings;panel.tabs[2].isOn=true;
        yield return Frames(3);panel.bloodStyle.SelectOptionByIndex(2);yield return Frames(3);
        Check(OverburstGameSettings.BloodStyle==BloodEffectStyle.Volumetric && BloodHitVfxService.CurrentStyle==BloodEffectStyle.Volumetric,"formal UI selects C");
        CheckCOnly(blood,"formal selection");
        panel.combatScroll.verticalNormalizedPosition=.39f;yield return Frames(3);
        ScreenCapture.CaptureScreenshot(Path.Combine(output,"settings-C.png"));yield return Frames(2);
        var catalog=Resources.Load<BloodEffectsPackCatalog>(BloodEffectsPackCatalog.VolumetricResourcePath);
        var pool=(BloodEffectsPackPool)typeof(BloodHitVfxService).GetField("packPool",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(blood);
        var profile=targetRoot.GetComponent<BloodHitTarget>().Profile;
        var point=targetRoot.transform.position+Vector3.up;
        BloodComparisonTuning.ResetCurrent();Check(Mathf.Approximately(BloodComparisonTuning.Scale,1f),"C independent defaults");
        float bookmark=panel.combatScroll.verticalNormalizedPosition;
        panel.bloodRows[0].increase.onClick.Invoke();
        Check(Mathf.Approximately(BloodComparisonTuning.Scale,1.1f) && Mathf.Abs(panel.combatScroll.verticalNormalizedPosition-bookmark)<.01f,"C tenth control preserves scroll");
        menu.CloseSettings();menu.Close();
        int played=blood.PlayedCount;
        BloodHitVfxService.Request(new CombatHitFeedbackRequest(player.gameObject,sequence++,null,false,WeaponElement.None,point,false,target:enemy,impactDirection:Vector3.right),point,1f);
        yield return Frames(8);Check(blood.PlayedCount>played && pool.ActiveCount>0,"C actual monster hit routes into VAT pool");
        yield return Wait(1.8f);Check(ground.ActiveCount>0,"C actual hit reaches native ground decal");
        int materials=ground.MaterialVariantCount;
        var mark=ground.GetComponentsInChildren<DecalProjector>(true).First(p=>p.gameObject.activeSelf);
        var material=mark.material;var size=mark.size;var tint=material.GetColor("_TintColor");
        BloodComparisonTuning.Adjust(BloodComparisonTuning.Control.GroundScale,1);
        BloodComparisonTuning.Adjust(BloodComparisonTuning.Control.GroundBrightness,1);
        yield return Frames(3);
        Check(mark.material==material && ground.MaterialVariantCount==materials && mark.size.x>size.x && mark.material.GetColor("_TintColor")!=tint,"C floor size and brightness reuse existing material");
        player.TakeDamage(new DamageInfo(1,player.transform.position+Vector3.up,targetRoot,Vector3.right));yield return Frames(4);
        Check(Object.FindFirstObjectByType<PlayerDamageFeedback>().UsingPackVignette && pool.ActiveCount>0,"C actual player blood and pack screen wound");
        Check(!catalog.sprays[pool.LastVariant].impactAccent,"ordinary player wound excludes large radial form");
        foreach (CombatImpactShape shape in Enum.GetValues(typeof(CombatImpactShape)))
        {
            pool.Clear();ground.ClearForComparison();
            BloodHitVfxService.Request(new CombatHitFeedbackRequest(player.gameObject,sequence++,null,true,WeaponElement.None,point,false,target:enemy,impactShape:shape,impactDirection:Vector3.right),point,1f);
            yield return Frames(8);Check(catalog.sprays[pool.LastVariant].impactAccent,"actual critical request chooses radial form "+shape);
            pool.Clear();ground.ClearForComparison();
            BloodHitVfxService.Request(new CombatHitFeedbackRequest(player.gameObject,sequence++,null,false,WeaponElement.None,point,false,isLethal:true,target:enemy,impactShape:shape,impactDirection:Vector3.right),point,1f);
            yield return Frames(8);Check(catalog.sprays[pool.LastVariant].impactAccent,"noncritical lethal request chooses radial form "+shape);
            pool.Clear();ground.ClearForComparison();
            BloodHitVfxService.Request(new CombatHitFeedbackRequest(player.gameObject,sequence++,null,false,WeaponElement.None,point,false,target:enemy,impactShape:shape,impactDirection:Vector3.right,isStrong:true),point,1f);
            yield return Frames(8);Check(catalog.sprays[pool.LastVariant].impactAccent,"noncritical heavy request chooses radial form "+shape);
            pool.Clear();ground.ClearForComparison();
            BloodHitVfxService.Request(new CombatHitFeedbackRequest(player.gameObject,sequence++,null,false,WeaponElement.None,point,false,target:enemy,impactShape:shape,impactDirection:Vector3.right),point,1f);
            yield return Frames(8);Check(!catalog.sprays[pool.LastVariant].impactAccent && catalog.sprays[pool.LastVariant].Accepts(shape,0),"ordinary request retains attack family "+shape);
        }
        // Render VAT at two times while the world is frozen. Geometry motion must reach actual pixels.
        var cameraRoot=new GameObject("Owned C Blood Camera");owned.Add(cameraRoot);
        var camera=cameraRoot.AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.enabled=false;
        camera.orthographic=true;camera.orthographicSize=2.1f;
        camera.transform.position=point+new Vector3(3,2,-4);camera.transform.LookAt(point);
        RenderTexture render=null;Texture2D pixels=null;float scale=Time.timeScale;
        var forms=new List<object>();
        try
        {
            render=new RenderTexture(640,426,24,RenderTextureFormat.ARGB32){name="Owned C VAT Render"};render.Create();
            pixels=new Texture2D(640,426,TextureFormat.RGB24,false);camera.targetTexture=render;
            Time.timeScale=0f;pool.Clear();ground.ClearForComparison();
            Color32[] Capture(string name)
            {
                var previous=RenderTexture.active;
                try {camera.Render();RenderTexture.active=render;pixels.ReadPixels(new Rect(0,0,640,426),0,0);pixels.Apply();if(name!=null)File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());return pixels.GetPixels32();}
                finally {RenderTexture.active=previous;}
            }
            var baseline=Capture(null);
            foreach(bool red in new[]{false,true})
            {
                OverburstGameSettings.BloodUniformRed=red;
                var selected=BloodHitVfxService.ResolveColorProfile(profile);
                for(int i=0;i<catalog.sprays.Length;i++)
                {
                    pool.Clear();bool drip=i>=17;
                    var definition=catalog.sprays[i];var shape=CombatImpactShape.Sweep;
                    if (!drip && !definition.Accepts(shape,definition.minimumPriority,accented:definition.impactAccent)) shape=definition.Accepts(CombatImpactShape.Thrust,definition.minimumPriority,accented:definition.impactAccent)?CombatImpactShape.Thrust:CombatImpactShape.Downward;
                    uint seed=0;while(seed<1000 && catalog.ResolveSpray(shape,definition.minimumPriority,seed,-1,drip,definition.impactAccent)!=i)seed++;
                    Check(seed<1000,"C form belongs to an intentional attack group "+i);
                    Check(pool.Play(selected,point,Vector3.right,shape,drip?.25f:1f,definition.minimumPriority,seed,0,drip,definition.impactAccent),"C form playback "+red+" "+i);
                    Check(pool.LastVariant==i,"C uses authored form "+red+" "+i);
                    pool.Tick(Time.time+.12f);var first=Capture("C_"+(red?"Red":"Profile")+"_Form_"+i);
                    pool.Tick(Time.time+(drip?.3f:.6f));var second=Capture(null);
                    int visible=0,moving=0,pink=0;
                    for(int p=0;p<first.Length;p++)
                    {
                        int Difference(Color32 a,Color32 b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b);
                        if(Difference(first[p],baseline[p])>25)visible++;
                        if(Difference(first[p],second[p])>25)moving++;
                        if(first[p].r>180 && first[p].b>180 && first[p].g<50)pink++;
                    }
                    forms.Add(new{red,i,visible,moving,pink});
                    File.WriteAllText(Path.Combine(output,"C-form-pixels-progress.json"),JsonConvert.SerializeObject(forms,Formatting.Indented));
                    if(drip)Capture("C_"+(red?"Red":"Profile")+"_Drip_Late_"+i);
                    Check(visible>4 && moving>4 && pink<20,"C actual VAT pixels visible and animated "+red+" "+i);
                }
            }
            pool.Clear();ground.ClearForComparison();
            var floorBaseline=Capture(null);var floors=new List<object>();
            for(int i=0;i<catalog.lethalDecals.Length;i++)
            {
                ground.ClearForComparison();
                ground.Request(profile,point,Vector3.right,CombatImpactShape.Downward,1f,2,false,0f);
                yield return Frames(3);
                Check(ground.ActiveCount==1,"C ground form activated "+i);
                var native=ground.GetComponentsInChildren<DecalProjector>(true).First(p=>p.gameObject.activeSelf);
                native.material.SetFloat("_Cutout",0f);
                var image=Capture("C_Ground_Form_"+i);int visible=0;
                for(int p=0;p<image.Length;p++)if(Math.Abs(image[p].r-floorBaseline[p].r)+Math.Abs(image[p].g-floorBaseline[p].g)+Math.Abs(image[p].b-floorBaseline[p].b)>25)visible++;
                Check(visible>10,"C native ground form reaches actual pixels "+i);floors.Add(new{i,visible});
            }
            ground.ClearForComparison();
            File.WriteAllText(Path.Combine(output,"C-rendered-ground.json"),JsonConvert.SerializeObject(floors,Formatting.Indented));
            File.WriteAllText(Path.Combine(output,"C-rendered-forms.json"),JsonConvert.SerializeObject(forms,Formatting.Indented));
        }
        finally
        {
            Time.timeScale=scale;camera.targetTexture=null;pool.Clear();
            if(pixels)Object.DestroyImmediate(pixels);
            if(render){render.Release();Object.DestroyImmediate(render);}
            Object.DestroyImmediate(cameraRoot);owned.Remove(cameraRoot);
        }
        var affiliation=targetRoot.AddComponent<CombatAffiliation>();affiliation.Configure(CombatTeam.Enemy);targetRoot.AddComponent<CombatTarget>();
        var playerData=new SerializedObject(player);float hp=player.CurrentHp;Vector3 position=player.transform.position;
        var trail=Object.FindFirstObjectByType<LowHealthBloodTrailService>();
        try
        {
            enemy.ResetHealth();enemy.TakeDamage(new DamageInfo(enemy.MaxHp*.75f,targetRoot.transform.position));
            playerData.Update();playerData.FindProperty("currentHp").floatValue=player.MaxHp*.25f;playerData.ApplyModifiedPropertiesWithoutUndo();
            Check(LowHealthBloodTrailService.ShouldBleed(player)&&LowHealthBloodTrailService.ShouldBleed(enemy),"C low health player and monster qualify");
            float trackingDeadline=Time.unscaledTime+3f;while(trail.TrackedCount<2 && Time.unscaledTime<trackingDeadline)yield return null;
            Check(trail.TrackedCount>=2,"C service tracked both targets before movement");
            yield return Wait(.8f);int playerBefore=trail.PlayerDropCount,enemyBefore=trail.MonsterDropCount;
            player.transform.position+=Vector3.right*.7f;targetRoot.transform.position+=Vector3.right*.7f;Physics.SyncTransforms();yield return Wait(1f);
            File.WriteAllText(Path.Combine(output,"C-trail-diagnostic.json"),JsonConvert.SerializeObject(new{playerBefore,enemyBefore,playerAfter=trail.PlayerDropCount,enemyAfter=trail.MonsterDropCount,tracked=trail.TrackedCount,ground=ground.ActiveCount,playerViewport=Camera.main.WorldToViewportPoint(player.transform.position+Vector3.up*.5f).ToString(),enemyViewport=Camera.main.WorldToViewportPoint(targetRoot.transform.position+Vector3.up*.5f).ToString(),wounds=typeof(LowHealthBloodTrailService).GetField("wounds",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(trail).ToString(),timeScale=Time.timeScale,playerHp=player.NormalizedHp,enemyHp=enemy.NormalizedHp,playerPosition=new[]{player.transform.position.x,player.transform.position.y,player.transform.position.z},enemyPosition=new[]{targetRoot.transform.position.x,targetRoot.transform.position.y,targetRoot.transform.position.z}},Formatting.Indented));
            Check(trail.PlayerDropCount>playerBefore && trail.MonsterDropCount>enemyBefore && ground.ActiveCount>0,"C moving player and monster leave actual ground blood");
            int before=pool.PlayedCount;
            BloodHitVfxService.RequestBleed(player,LowHealthBloodTrailService.ProfileFor(player),player.transform.position,Vector3.right);
            yield return Frames(2);Check(pool.PlayedCount>before && pool.LastVariant>=17,"C moving drops use dedicated VAT forms");
            enemy.Heal(enemy.MaxHp);player.Heal(player.MaxHp);Check(!LowHealthBloodTrailService.ShouldBleed(player)&&!LowHealthBloodTrailService.ShouldBleed(enemy),"C healing stops trails");
            int deaths=blood.PlayedCount;BloodHitVfxService.RequestDeath(enemy,new DamageInfo(1,point,targetRoot,Vector3.right));yield return Frames(8);
            Check(blood.PlayedCount>=deaths+4,"C death burst and fanned jets use shared budget");
        }
        finally {player.transform.position=position;playerData.Update();playerData.FindProperty("currentHp").floatValue=hp;playerData.ApplyModifiedPropertiesWithoutUndo();}
        var groundIds=ground.GetComponentsInChildren<DecalProjector>(true).Select(p=>p.GetInstanceID()).ToArray();
        foreach(var style in new[]{BloodEffectStyle.Legacy,BloodEffectStyle.EffectsPack,BloodEffectStyle.Volumetric,BloodEffectStyle.Legacy,BloodEffectStyle.Volumetric})
        {
            var previous=blood.transform.Cast<Transform>().Where(t=>t.name.StartsWith("Blood ") && !t.name.StartsWith("Blood Ground ")).Select(t=>t.gameObject).ToArray();
            OverburstGameSettings.BloodStyle=style;yield return Frames(3);
            Check(previous.All(go=>go==null),"prior spray instances released before steady state "+style);
            if(style==BloodEffectStyle.Volumetric)CheckCOnly(blood,"switch cycle");else CheckExclusivePools(blood,style==BloodEffectStyle.EffectsPack,"C cycle "+style);
            Check(ground.GetComponentsInChildren<DecalProjector>(true).Select(p=>p.GetInstanceID()).SequenceEqual(groundIds),"ground pool reused across C cycle "+style);
        }
        BloodComparisonTuning.ResetCurrent();BloodComparisonTuning.Adjust(BloodComparisonTuning.Control.Scale,2);BloodComparisonTuning.Adjust(BloodComparisonTuning.Control.GroundBrightness,2);
        OverburstGameSettings.BloodUniformRed=true;OverburstGameSettings.SaveIfDirty();
        var saved=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(OverburstGameSettings.FilePath));
        Check((int)saved["version"]==3 && (int)saved["bloodStyle"]==2 && Mathf.Approximately((float)saved["bloodC"]["scale"],1.2f),"C selection and independent tuning saved");
        Check(Mathf.Approximately((float)saved["bloodB"]["scale"],1.6f) && Mathf.Approximately((float)saved["bloodA"]["scale"],1f),"A and B tuning survive C updates");
    }
}
