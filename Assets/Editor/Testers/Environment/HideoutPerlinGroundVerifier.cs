using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Checks the real player motor on the saved hideout relief in an isolated account.</summary>
public static class HideoutPerlinGroundVerifier
{
    public static void Begin(string outputDirectory)
    {
        string output=IsolatedSavePlayGuard.ValidateDirectory(outputDirectory);
        if(!EditorApplication.isPlaying || !AccountBootstrap.Ready || !WorldSessionState.IsHideout ||
           string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) ||
           !Path.GetFullPath(AccountBootstrap.SaveDirectory).StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use a ready product Hideout in this test's isolated account.");
        Directory.CreateDirectory(output);
        var actor=PlayerContext.Instance?.CurrentActor;
        if(actor==null || actor.Movement==null) throw new InvalidOperationException("Player not ready.");
        actor.StartCoroutine(Run(actor,output));
    }

    static IEnumerator Run(PlayerActorRuntime actor,string output)
    {
        var checks=new List<object>(); int failures=0;
        void Check(bool passed,string name) { checks.Add(new {name,passed}); if(!passed) failures++; }
        var scene=SceneManager.GetSceneByName(PersistentSceneFlow.HideoutSceneName);
        var ground=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MeshCollider>(true)).Single(c=>c.name=="Camp Ground");
        var mesh=ground.sharedMesh;
        var bounds=ground.GetComponent<Renderer>().bounds;
        Check(AssetDatabase.GetAssetPath(mesh)==HideoutPerlinGroundBuilder.MeshPath,"Saved Perlin mesh loads through product boot");
        Check(ground.GetComponent<MeshFilter>().sharedMesh==mesh,"Renderer and collider share the same mesh");
        Check(Mathf.Abs(bounds.size.x-108)<.01f && Mathf.Abs(bounds.size.z-76)<.01f,"Shortened 108 x 76 metre ground loads");
        Check(UnityEngine.Object.FindObjectsByType<GeneralGoodsMerchantInteractable>(FindObjectsSortMode.None).Length==2 &&
              UnityEngine.Object.FindObjectsByType<StashInteractable>(FindObjectsSortMode.None).Length==1,"Merchants and stash remain available");
        foreach(var p in new[] {new Vector3(0,5,-3),new Vector3(0,5,-12),new Vector3(0,5,9),new Vector3(-6.75f,5,6),new Vector3(7,5,6.25f),new Vector3(68,5,-12.5f)})
            Check(ground.Raycast(new Ray(p,Vector3.down),out var hit,10) && Mathf.Abs(hit.point.y)<.002f,"Protected ground height at "+p);

        Vector3 original=actor.transform.position; Quaternion rotation=actor.transform.rotation;
        var movement=actor.Movement;
        var motor=actor.GetComponent<OverburstCharacterMotor3D>();
        Vector3 start=new Vector3(-22,0,-22),end=new Vector3(-5,0,-22);
        ground.Raycast(new Ray(start+Vector3.up*5,Vector3.down),out var startHit,10);
        ActorTeleportUtility.TeleportSafely(actor.transform,startHit.point+Vector3.up*.05f,rotation);
        yield return new WaitForSeconds(.7f);
        Check(movement.IsGrounded,"Player settles onto outer relief");
        Check(movement.BeginLootAutoMove(end),"Existing automatic movement accepts a terrain destination");
        float deadline=Time.unscaledTime+12f,maxGap=0,minHeight=float.MaxValue,maxHeight=float.MinValue;
        int samples=0,air=0;
        while(Time.unscaledTime<deadline && new Vector2(actor.transform.position.x-end.x,actor.transform.position.z-end.z).magnitude>.4f)
        {
            yield return new WaitForEndOfFrame();
            samples++;
            if(!movement.IsGrounded) air++;
            if(motor!=null && float.IsFinite(motor.GroundGap)) maxGap=Mathf.Max(maxGap,Mathf.Abs(motor.GroundGap));
            minHeight=Mathf.Min(minHeight,actor.transform.position.y); maxHeight=Mathf.Max(maxHeight,actor.transform.position.y);
        }
        movement.CancelLootAutoMove();
        Check(new Vector2(actor.transform.position.x-end.x,actor.transform.position.z-end.z).magnitude<.6f,"Player traverses 17 metres of uneven ground");
        Check(samples>10 && air<=Mathf.Max(2,samples*.05f),"Ground contact remains stable while moving");
        Check(maxGap<.16f,"Ground gap stays below 16 centimetres");
        Check(maxHeight-minHeight>.02f,"Player follows a measurable elevation change");
        yield return new WaitForEndOfFrame();
        Texture2D capture=null;
        try { capture=ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(output,"outer_walk.png"),capture.EncodeToPNG()); }
        finally { if(capture!=null) UnityEngine.Object.Destroy(capture); }
        ActorTeleportUtility.TeleportSafely(actor.transform,original,rotation);
        yield return new WaitForSeconds(.4f);
        Check(movement.IsGrounded,"Returning to camp retains ground contact");
        File.WriteAllText(Path.Combine(output,"Result.json"),JsonConvert.SerializeObject(new {status=failures==0 ? "PASS" : "FAIL",failures,
            checks,samples,air,maxGap,elevationChange=maxHeight-minHeight,account=AccountBootstrap.SaveDirectory},Formatting.Indented));
        Debug.Log("[하이드아웃 지면 검증] "+(failures==0 ? "PASS" : "FAIL")+" · "+checks.Count+"항목");
    }
}
