using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using System.Threading.Tasks;
using Object=UnityEngine.Object;

/// <summary>격리 Play에서 같은 공격/카메라의 추가 연출 OFF/ON 원본 프레임을 캡처한다.</summary>
public static class CombatPresentationComparisonCapture
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    public static IEnumerator Run(string output,Action<bool,string> check)
    {
        bool enteredArena=false;
        try
        {
            if(!EnemyThemeTrialService.InArena)
            {
                var result=EnemyThemeTrialService.ToggleArena();
                check(result.Success,"Native combat test map entry succeeds: "+result.Message);
                enteredArena=result.Success;
                if(!result.Success)throw new InvalidOperationException(result.Message);
            }
            float settle=Time.unscaledTime+1f;
            while(Time.unscaledTime<settle)yield return null;
            check(EnemyThemeTrialService.InArena,"Both comparisons use combat test map");
            var floor=AssetDatabase.LoadAssetAtPath<Material>("Assets/ProjectOverburst/Resources/Enemies/Themes/Arena/Floor.mat");
            var color=floor.GetColor("_BaseColor");
            check(color.r>.5f && color.g>.5f && color.b>.5f,"Brighter native arena floor is applied");
            yield return RunTakes(output,check);
        }
        finally
        {
            if(enteredArena && EnemyThemeTrialService.InArena)EnemyThemeTrialService.ToggleArena();
        }
    }

    private static IEnumerator RunTakes(string output,Action<bool,string> check)
    {
        output=IsolatedSavePlayGuard.ValidateDirectory(output);
        var actor=PlayerContext.Instance.CurrentActor;
        var equipment=actor.Equipment;
        var melee=actor.GetComponent<MeleeRuntime>();
        var movement=actor.GetComponent<PlayerMovement>();
        var oldAuthority=movement.ControlAuthority;
        var parry=actor.GetComponent<PlayerParryController>();
        var energy=equipment.GetComponent<OverburstElementEnergy>()??equipment.gameObject.AddComponent<OverburstElementEnergy>();
        var controller=Object.FindFirstObjectByType<QuarterViewCamera>();
        var camera=controller!=null ? controller.GetComponent<Camera>() : Camera.main;
        var brain=camera!=null ? camera.GetComponent<Unity.Cinemachine.CinemachineBrain>() : null;
        check(camera!=null,"Product output camera resolved for video");
        var oldWeapon=equipment.CurrentWeaponItem;
        var setGem=typeof(PlayerEquipment).GetMethod("SetElementGem",Private);
        var oldGem=equipment.EquippedElementGem;
        Vector3 actorPosition=actor.transform.position;Quaternion actorRotation=actor.transform.rotation;
        Vector3 cameraPosition=camera.transform.position;Quaternion cameraRotation=camera.transform.rotation;
        float oldFov=camera.fieldOfView,oldSize=camera.orthographicSize;
        bool oldController=controller!=null && controller.enabled;
        bool oldBrain=brain!=null && brain.enabled;
        int oldCaptureRate=Time.captureFramerate;
        int oldFrameLimit=Application.targetFrameRate;
        const int width=1280,height=720;
        var screenshot=new RenderTexture(Screen.width,Screen.height,0,RenderTextureFormat.ARGB32);
        var buffers=new RenderTexture[6];var busy=new bool[6];var writes=new List<Task>();var captureErrors=new List<string>();
        var canvases=Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        var canvasStates=canvases.Select(c=>c.enabled).ToArray();
        bool oldParry=OverburstGameSettings.ParryPresentationEnabled,oldHeavy=OverburstGameSettings.HeavyPresentationEnabled,oldMotion=OverburstGameSettings.MotionBlurEnabled;
        float oldParryScale=OverburstGameSettings.ParryPresentationIntensity,oldHeavyScale=OverburstGameSettings.HeavyPresentationIntensity;
        var edge=Object.FindFirstObjectByType<OverburstEdgeBlur>();bool oldEdge=OverburstGameSettings.EdgeBlurEnabled;
        var rows=new List<object>();
        try
        {
            check(Path.GetFullPath(OverburstGameSettings.FilePath).StartsWith(Path.GetFullPath(Path.GetDirectoryName(output)),StringComparison.OrdinalIgnoreCase),"Video uses owned isolated settings");
            if(controller!=null)controller.enabled=false;
            if(brain!=null)brain.enabled=false;
            foreach(var canvas in canvases)canvas.enabled=false;
            if(edge!=null)edge.SetEnabled(false);
            OverburstGameSettings.MotionBlurEnabled=false;
            OverburstGameSettings.ParryPresentationIntensity=1;OverburstGameSettings.HeavyPresentationIntensity=1;
            movement.SetControlAuthority(ActorControlAuthority.AI);
            Time.captureFramerate=0;Application.targetFrameRate=60;
            screenshot.Create();for(int i=0;i<buffers.Length;i++){buffers[i]=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32);buffers[i].Create();}
            var weapon=AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            var fire=AssetDatabase.FindAssets("t:ElementGemItemData").Select(id=>AssetDatabase.LoadAssetAtPath<ElementGemItemData>(AssetDatabase.GUIDToAssetPath(id))).Where(g=>g!=null && g.element==WeaponElement.Fire).OrderBy(g=>g.fixedGrade).First();
            var success=typeof(PlayerParryController).GetMethod("PlaySuccess",Private);
            foreach(string kind in new[]{"Parry","Heavy"})
            foreach(bool enabled in new[]{false,true})
            {
                string take=kind+(enabled?"On":"Off");string folder=Path.Combine(output,take);Directory.CreateDirectory(folder);
                melee.CancelCurrentAttackState();OverburstTimeEffectArbiter.ClearOwner(parry);OverburstTimeEffectArbiter.ClearOwner(melee);
                actor.transform.SetPositionAndRotation(actorPosition,Quaternion.Euler(0,0,0));
                check(equipment.EquipWeaponItem(new ItemData(weapon,1,ItemGrade.Common)),"Video weapon reset "+take);
                setGem.Invoke(equipment,new object[]{new ItemData(fire,1,fire.fixedGrade)});
                camera.transform.position=actorPosition+new Vector3(5,6,-5);
                camera.transform.LookAt(actorPosition+Vector3.up*.85f);camera.fieldOfView=40;camera.orthographicSize=3.5f;
                var animator=actor.GetComponentInChildren<Animator>();if(animator!=null){animator.Rebind();animator.Update(0);}
                OverburstGameSettings.ParryPresentationEnabled=kind=="Parry" && enabled;
                OverburstGameSettings.HeavyPresentationEnabled=kind=="Heavy" && enabled;
                UnityEngine.Random.InitState(7214);
                float settled=Time.unscaledTime+1f;while(Time.unscaledTime<settled)yield return new WaitForEndOfFrame();
                actor.transform.SetPositionAndRotation(actorPosition,Quaternion.identity);movement.ResetMotionAfterTeleport();
                for(int ground=0;ground<4;ground++)yield return new WaitForEndOfFrame();
                energy.Clear();typeof(OverburstElementEnergy).GetProperty("Amount").SetValue(energy,kind=="Heavy"?100f:0f);
                typeof(OverburstElementEnergy).GetField("holdUntil",Private).SetValue(energy,OverburstGameClock.UnscaledTime+20);
                int parryBefore=CombatMomentPresentation.ParryPulses,heavyBefore=CombatMomentPresentation.HeavyPulses;
                int cue=-1;float elapsedStart=Time.unscaledTime;var times=new List<float>();int frame=0;
                check(melee.TryStartHeavyAttack(Vector3.forward)==WeaponActionResult.Accepted,"Same product heavy action accepted "+take);
                while(Time.unscaledTime-elapsedStart<3.5f || frame<2)
                {
                    if(kind=="Parry" && cue<0 && Time.unscaledTime-elapsedStart>=.35f)
                    {
                        int action=(int)typeof(MeleeRuntime).GetField("activeActionId",Private).GetValue(melee);
                        melee.NotifyHeavyParried(action);
                        typeof(PlayerParryController).GetField("nextSlowAt",Private).SetValue(parry,0f);
                        success.Invoke(parry,new object[]{actorPosition+Vector3.up+Vector3.forward,1,false});cue=frame;
                    }
                    yield return new WaitForEndOfFrame();
                    if(kind=="Heavy" && cue<0 && energy.Amount<.01f)cue=frame;
                    int slot=Array.FindIndex(busy,value=>!value);if(slot<0)continue;
                    busy[slot]=true;times.Add(Time.unscaledTime-elapsedStart);
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(screenshot);Graphics.Blit(screenshot,buffers[slot]);
                    string path=Path.Combine(folder,frame.ToString("D4")+".rgba");int capturedSlot=slot;
                    AsyncGPUReadback.Request(buffers[slot],0,TextureFormat.RGBA32,request=>
                    {
                        try{if(request.hasError){captureErrors.Add(path);return;}var bytes=request.GetData<byte>().ToArray();writes.Add(Task.Run(()=>File.WriteAllBytes(path,bytes)));}
                        finally{busy[capturedSlot]=false;}
                    });
                    frame++;
                }
                while(busy.Any(value=>value) || writes.Any(task=>!task.IsCompleted))yield return null;
                check(captureErrors.Count==0 && writes.All(task=>!task.IsFaulted),"Async video frame storage succeeds "+take);
                int pulses=kind=="Parry"?CombatMomentPresentation.ParryPulses-parryBefore:CombatMomentPresentation.HeavyPulses-heavyBefore;
                check(cue>=0 && pulses==(enabled?1:0),"Requested added effect and one event confirmed "+take);
                check(kind!="Heavy" || energy.Amount<.01f,"Same energy consumption "+take);
                rows.Add(new{take,frames=frame,fps=60,cueFrame=cue,addedPulses=pulses,selectedIntensity=1f,frameTimes=times,sourceWidth=width,sourceHeight=height,format="rgba32",graphicsUVStartsAtTop=SystemInfo.graphicsUVStartsAtTop});
            }
            File.WriteAllText(Path.Combine(output,"capture.json"),JsonConvert.SerializeObject(new{status="PASS",map="native combat test arena",floorColor="#879298",camera="fixed close view; camera motion suppressed equally",parry="existing success endpoint + parried heavy fixture",heavy="actual full-energy discharge",baselineEffectsRetained=true,motionBlur=false,edgeBlur=false,takes=rows},Formatting.Indented));
        }
        finally
        {
            melee.CancelCurrentAttackState();OverburstTimeEffectArbiter.ClearOwner(parry);OverburstTimeEffectArbiter.ClearOwner(melee);
            actor.transform.SetPositionAndRotation(actorPosition,actorRotation);
            camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);camera.fieldOfView=oldFov;camera.orthographicSize=oldSize;
            if(controller!=null)controller.enabled=oldController;
            if(brain!=null)brain.enabled=oldBrain;
            for(int i=0;i<canvases.Length;i++)if(canvases[i]!=null)canvases[i].enabled=canvasStates[i];
            Time.captureFramerate=oldCaptureRate;
            Application.targetFrameRate=oldFrameLimit;movement.SetControlAuthority(oldAuthority);movement.ResetMotionAfterTeleport();
            AsyncGPUReadback.WaitAllRequests();
            if(screenshot!=null){screenshot.Release();Object.Destroy(screenshot);}
            foreach(var buffer in buffers)if(buffer!=null){buffer.Release();Object.Destroy(buffer);}
            OverburstGameSettings.ParryPresentationEnabled=oldParry;OverburstGameSettings.HeavyPresentationEnabled=oldHeavy;OverburstGameSettings.MotionBlurEnabled=oldMotion;
            OverburstGameSettings.ParryPresentationIntensity=oldParryScale;OverburstGameSettings.HeavyPresentationIntensity=oldHeavyScale;
            if(edge!=null)edge.SetEnabled(oldEdge);
            if(oldWeapon!=null)equipment.EquipWeaponItem(oldWeapon);setGem.Invoke(equipment,new object[]{oldGem});
        }
    }
}
