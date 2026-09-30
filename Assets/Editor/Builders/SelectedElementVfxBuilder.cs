using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SelectedElementVfxBuilder
{
    const string Vendor="Assets/ThirdParty/06_VFX/Piloto Studio/";
    public const string FireHit=Vendor+"Super Realistic FX Bundle/ARPG Realistic Essentials Fire/Prefabs/Ranged/Red/Projectile_Hit_Impact.prefab";
    public const string IceHit=Vendor+"Super Realistic FX Bundle/ARPG Realistic Ice and Water pack/Ice/Ice_Hit_FX.prefab";
    public const string FireBurst=Vendor+"Super Realistic FX Bundle 02/Realistic Environmental Fire and Explosions Pack/Fire Burst sim 1.prefab";
    public const string Slam=Vendor+"Super Realistic FX Bundle/ARPG Realistic Lightning Starter Kit/Mid/4) Slam Circular.prefab";
    const string HitRoot="Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/VFX/ElementHit/";
    public const string HeavyRoot="Assets/ProjectOverburst/03_Features/Weapons/_Shared/Melee/VFX/HeavyImpacts";
    public const string HeavyAsset="Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Heavy/GreatswordHeavyAttack.asset";
    public static string Output=>Path.GetFullPath("../개인파일/코덱스산출/Combat/ElementStatusGoal20260927/SelectedVfx");
    static GameObject Load(string p){var go=AssetDatabase.LoadAssetAtPath<GameObject>(p);if(go==null)throw new Exception("Missing "+p);return go;}
    public static int ExtractLate(GameObject instance)
    {
        int count=0;
        foreach(var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main=ps.main;
            if(main.startDelay.mode!=ParticleSystemCurveMode.Constant)throw new Exception("Unexpected nonconstant delay "+ps.name);
            if(main.startDelay.constant>=2.949f)
            {
                main.startDelay=Mathf.Max(0,main.startDelay.constant-2.95f);count++;
            }
            else
            {
                var renderer=ps.GetComponent<ParticleSystemRenderer>();if(renderer!=null)UnityEngine.Object.DestroyImmediate(renderer);
                UnityEngine.Object.DestroyImmediate(ps);
            }
        }
        if(count==0)throw new Exception("Late explosion absent");
        return count;
    }
    public static string Build()
    {
        if(Application.isPlaying)throw new Exception("Edit required");
        var heavy=AssetDatabase.LoadAssetAtPath<MeleeHeavyAttackDefinition>(HeavyAsset);
        if(heavy==null||EditorUtility.IsDirty(heavy))throw new Exception("Heavy missing or unsaved; preserve edits");
        var report=new List<string>();
        foreach(var path in new[]{HitRoot+"Modules/PF_VFX_MeleeElementHit_FireModule.prefab",HitRoot+"RuntimePools/PF_VFX_MeleeElementHit_Fire_Runtime.prefab"})
        {
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var module=root.GetComponent<MeleeElementHitVfxController>()?.GetElementObject(WeaponElement.Fire)??root;
                while(module.transform.childCount>0)UnityEngine.Object.DestroyImmediate(module.transform.GetChild(0).gameObject);
                var child=(GameObject)PrefabUtility.InstantiatePrefab(Load(FireHit),module.scene);
                child.transform.SetParent(module.transform,false);child.transform.localPosition=Vector3.zero;child.transform.localRotation=Quaternion.identity;child.transform.localScale=Vector3.one*.8f;
                if(PrefabUtility.SaveAsPrefabAsset(root,path)==null)throw new Exception("Save failed "+path);
                report.Add(path+" = Projectile_Hit_Impact; child scale .8, root unchanged");
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        if(!AssetDatabase.IsValidFolder(HeavyRoot))AssetDatabase.CreateFolder(HeavyRoot.Substring(0,HeavyRoot.LastIndexOf('/')),"HeavyImpacts");
        var scene=EditorSceneManager.NewPreviewScene();
        try
        {
            var fire=new GameObject("PF_VFX_Heavy_FireBurstSim1");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(fire,scene);
            var content=(GameObject)PrefabUtility.InstantiatePrefab(Load(FireBurst),scene);content.transform.SetParent(fire.transform,false);content.transform.localPosition=Vector3.zero;content.transform.localRotation=Quaternion.identity;content.transform.localScale=Vector3.one;
            var firePrefab=PrefabUtility.SaveAsPrefabAsset(fire,HeavyRoot+"/PF_VFX_Heavy_FireBurstSim1.prefab");
            var electric=(GameObject)PrefabUtility.InstantiatePrefab(Load(Slam),scene);PrefabUtility.UnpackPrefabInstance(electric,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            electric.name="PF_VFX_Heavy_ElectricSlamLate";electric.transform.position=Vector3.zero;electric.transform.rotation=Quaternion.identity;
            int late=ExtractLate(electric);
            var electricPrefab=PrefabUtility.SaveAsPrefabAsset(electric,HeavyRoot+"/PF_VFX_Heavy_ElectricSlamLate.prefab");
            if(heavy.elementVfx.fireChainExplosion==null)heavy.elementVfx.fireChainExplosion=heavy.elementVfx.fireImpact;
            heavy.elementVfx.fireImpact=firePrefab;heavy.elementVfx.electricImpact=electricPrefab;
            ConfigureRadius(heavy);
            EditorUtility.SetDirty(heavy);AssetDatabase.SaveAssetIfDirty(heavy);
            report.Add("Fire landing="+AssetDatabase.GetAssetPath(firePrefab));report.Add("Fire chain retained="+AssetDatabase.GetAssetPath(heavy.elementVfx.FireChainExplosion));
            report.Add("Electric retained late particle systems="+late+"; original delays 2.95..3.1 => 0..0.15; charge-only systems removed");
        }
        finally{EditorSceneManager.ClosePreviewScene(scene);}
        var catalog=Resources.Load<MeleeElementHitVfxCatalog>(MeleeElementHitVfxCatalog.ResourcePath);catalog.TryResolve(WeaponElement.Ice,out var ice);
        if(ice.GetComponent<MeleeElementHitVfxController>().GetElementObject(WeaponElement.Ice).transform.Find("Ice_Hit_FX")==null)throw new Exception("Ice hit selection differs");
        report.Add("Ice_Hit_FX already connected; preserved existing .8 child / .7 root");
        Directory.CreateDirectory(Output);File.WriteAllLines(Output+"/Build.txt",report);return string.Join("\n",report);
    }
    public static string CalibrateRadius()
    {
        if(Application.isPlaying)throw new Exception("Edit required");
        var heavy=AssetDatabase.LoadAssetAtPath<MeleeHeavyAttackDefinition>(HeavyAsset);
        if(heavy==null||EditorUtility.IsDirty(heavy))throw new Exception("Heavy missing or unsaved");
        ConfigureRadius(heavy);
        EditorUtility.SetDirty(heavy);AssetDatabase.SaveAssetIfDirty(heavy);
        return "Fire=3.5m @0.2s, Electric=5.5m @0.5s, FireChain=1.3m @0.2s; common planar distortion=1m at unit root scale";
    }
    static void ConfigureRadius(MeleeHeavyAttackDefinition heavy)
    {
        // Visual authoring references from RadiusSurvey.png, not renderer AABBs or stray debris.
        heavy.elementVfx.fireImpactRadius=3.5f;
        heavy.elementVfx.electricImpactRadius=5.5f;
        heavy.elementVfx.fireChainRadius=1.3f;
        heavy.elementVfx.iceImpactRadius=1f;
        var cue=heavy.attack.attackPhases[0].vfxCues[0].definition;
        if(EditorUtility.IsDirty(cue))throw new Exception("Common heavy cue has unsaved edits");
        cue.authoredCircleRadius=1f; // SM_OHS_PlanarShockwave: XZ extents1, unit particle maximum size.
        EditorUtility.SetDirty(cue);AssetDatabase.SaveAssetIfDirty(cue);
    }
    public static string Preview()
    {
        var preview=new PreviewRenderUtility();var sheet=new Texture2D(1280,960,TextureFormat.RGB24,false);
        try
        {
            preview.camera.orthographic=true;preview.camera.orthographicSize=7;preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.035f,.035f,.035f,1);
            preview.camera.transform.position=new Vector3(0,10,-10);preview.camera.transform.LookAt(Vector3.zero);preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=80;
            var paths=new[]{FireHit,FireBurst,HeavyRoot+"/PF_VFX_Heavy_ElectricSlamLate.prefab"};
            float[] times={.07f,.2f,.5f,1f};
            for(int row=0;row<3;row++)for(int col=0;col<4;col++)
            {
                var go=UnityEngine.Object.Instantiate(Load(paths[row]));preview.AddSingleGO(go);go.transform.position=Vector3.zero;
                if(row==0){preview.camera.orthographicSize=2;go.transform.localScale=Vector3.one*.56f;}else preview.camera.orthographicSize=7;
                foreach(var ps in go.GetComponentsInChildren<ParticleSystem>(true)){ps.useAutoRandomSeed=false;ps.randomSeed=42;if(ps.gameObject.activeInHierarchy)ps.Simulate(times[col],false,true,true);}
                preview.BeginStaticPreview(new Rect(0,0,320,320));preview.Render(true);var tex=preview.EndStaticPreview();sheet.SetPixels(col*320,(2-row)*320,320,320,tex.GetPixels());UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(go);
            }
            sheet.Apply();Directory.CreateDirectory(Output);File.WriteAllBytes(Output+"/SelectedTimeline.png",sheet.EncodeToPNG());return Output+"/SelectedTimeline.png";
        }
        finally{preview.Cleanup();UnityEngine.Object.DestroyImmediate(sheet);}
    }
}
