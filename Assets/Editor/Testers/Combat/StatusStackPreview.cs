using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class StatusStackPreview
{
    public static string Render()
    {
        if(Application.isPlaying)throw new Exception("Edit preview required");
        var roster=MapThemeCatalog.Resolve("SpiderBrood").BuildRoster(1,1,0,27100);
        var prefab=Resources.Load<MeleeElementStatusAuraPresentation>("Combat/VFX/PF_VFX_MeleeElementStatusAura");
        var preview=new PreviewRenderUtility();var sheet=new Texture2D(1200,1600,TextureFormat.RGB24,false);
        var report=new System.Text.StringBuilder();
        try
        {
            var camera=preview.camera;camera.orthographic=true;camera.nearClipPlane=.01f;camera.farClipPlane=80;
            camera.backgroundColor=new Color(.06f,.065f,.075f);camera.clearFlags=CameraClearFlags.SolidColor;
            preview.lights[0].intensity=1.8f;preview.lights[0].transform.rotation=Quaternion.Euler(35,-30,0);
            preview.lights[1].intensity=1.2f;preview.lights[1].transform.rotation=Quaternion.Euler(30,140,0);
            for(int row=0;row<4;row++)for(int col=0;col<3;col++)
            {
                var definition=roster[row/2];var actor=UnityEngine.Object.Instantiate(definition.ActorPrefab);
                preview.AddSingleGO(actor.gameObject);actor.transform.position=Vector3.zero;
                var target=actor.GetComponent<CombatTarget>();var volume=CombatTargetVfxPlacement.ResolveVolume(target);
                var aura=UnityEngine.Object.Instantiate(prefab,actor.transform);aura.transform.localPosition=Vector3.zero;
                aura.ClearAllAuras();aura.ConfigureTarget(target);
                var type=row%2==0?MeleeElementStatusAuraType.Burning:MeleeElementStatusAuraType.Shocked;
                int stack=1+col*2;aura.SetStackCount(type,stack);aura.SetAuraActive(type,true,false);
                var module=aura.GetAuraObject(type);
                foreach(var ps in module.GetComponentsInChildren<ParticleSystem>(true))
                {ps.useAutoRandomSeed=false;ps.randomSeed=42;ps.Simulate(.7f,false,true,true);}
                camera.orthographicSize=Mathf.Max(1.25f,volume.Radius*2.1f);
                camera.transform.position=volume.Center+new Vector3(0,6,-7);camera.transform.LookAt(volume.Center);
                preview.BeginStaticPreview(new Rect(0,0,400,400));preview.Render(true);var tex=preview.EndStaticPreview();
                sheet.SetPixels(col*400,(3-row)*400,400,400,tex.GetPixels());
                report.AppendLine(definition.EnemyId+" "+type+" stacks="+stack+" body="+volume.Center+" radius="+volume.Radius+" halfHeight="+volume.HalfHeight+" aura="+module.transform.position+" scale="+module.transform.lossyScale);
                UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(actor.gameObject);
            }
            sheet.Apply();string folder=Path.GetFullPath("../개인파일/코덱스산출/Combat/ElementStatusGoal20260927/StackVfx");Directory.CreateDirectory(folder);
            File.WriteAllBytes(folder+"/StackComparison.png",sheet.EncodeToPNG());File.WriteAllText(folder+"/PreviewVolumes.txt",report.ToString());return folder+"/StackComparison.png";
        }
        finally{preview.Cleanup();UnityEngine.Object.DestroyImmediate(sheet);}
    }
}
