using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class OverburstMinimapValidator
{
    public static string ValidateAuthored()
    {
        var controller = Object.FindFirstObjectByType<WorldMinimapController>(FindObjectsInactive.Include);
        Require(controller != null && controller.View != null && controller.View.IsReady, "Authored references");
        var view = controller.View;
        Require(controller.GetComponentsInChildren<MinimapMarkerGraphic>(true).Length == 1, "One marker graphic");
        Require(controller.GetComponentsInChildren<Mask>(true).Length == 1, "One circular mask");
        Require(controller.GetComponentsInChildren<RectMask2D>(true).Length == 0, "No rectangular clip");
        Require(controller.GetComponentsInChildren<RawImage>(true).Length == 0, "No legacy map/fog");
        Require(controller.GetComponentsInChildren<Camera>(true).Length == 0, "No map camera");
        foreach (var graphic in controller.GetComponentsInChildren<Graphic>(true))
            Require(!graphic.raycastTarget || graphic.GetComponent<Button>() != null, "Only buttons receive raycasts");
        foreach (var rect in controller.GetComponentsInChildren<RectTransform>(true))
            Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(rect.gameObject) == 0, "Missing script: " + rect.name);
        Require(view.ZoomInButton != view.ZoomOutButton, "Distinct zoom buttons");
        return "PASS: references, single circular Mask/marker Graphic, no old RawImage/camera, raycasts, missing scripts";
    }

    public static object ValidatePlayCore()
    {
        Require(Application.isPlaying, "Play Mode required");
        ValidateAuthored();
        var checks = new List<string>();
        Scene scene = SceneManager.CreateScene("__MinimapValidation");
        var root = new GameObject("MinimapValidationFixture");
        SceneManager.MoveGameObjectToScene(root,scene);
        var healths = new List<CombatHealth>();
        var ranks = new List<EnemyRank>();
        var source = new MinimapEnemySource();
        var controller = WorldMinimapController.Instance;
        float zoom = controller.ZoomRadius;
        try
        {
            Require((MinimapProjection.Project(Vector3.forward,Vector3.zero,90,1,1)-Vector2.left).sqrMagnitude < 0.0001f,"Yaw90");
            Require((MinimapProjection.Project(Vector3.right,Vector3.zero,45,1,1)-new Vector2(0.7071068f,0.7071068f)).sqrMagnitude < 0.0001f,"Yaw45");
            checks.Add("XZ projection yaw0/45/90 and radial distance");
            for(int i=0;i<181;i++)
            {
                var go = new GameObject("Fixture_"+i); go.transform.SetParent(root.transform);
                go.transform.position = new Vector3(i==180 ? 1 : 1000+i,0,0);
                var health=go.AddComponent<CombatHealth>(); healths.Add(health);
                typeof(CombatHealth).GetField("showDamageNumbers",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(health,false);
                ranks.Add(go.AddComponent<EnemyRank>());
            }
            source.Select(Vector3.zero,55,scene.handle);
            Require(source.Count==1 && Contains(source,ranks[180].GetInstanceID()),"Nearby enemy after first160");
            checks.Add("181st registered near enemy survives distance filtering");
            for(int i=0;i<181;i++) ranks[i].transform.position=new Vector3(i*0.2f,0,0);
            source.Select(Vector3.zero,55,scene.handle);
            Require(source.Count==160 && Contains(source,ranks[0].GetInstanceID()) && !Contains(source,ranks[180].GetInstanceID()),"Closest160");
            typeof(EnemyRank).GetProperty("GradeType").SetValue(ranks[180],EnemyGradeType.Boss);
            source.Select(Vector3.zero,55,scene.handle);
            Require(Contains(source,ranks[180].GetInstanceID()),"Boss priority");
            checks.Add("160 cap, closest ordering, boss priority, scene isolation");
            for(int repeat=0;repeat<3;repeat++)
            {
                healths[180].TakeDamage(new DamageInfo(99999,ranks[180].transform.position,triggersOnHitEffects:false));
                Require(source.IsDirty,"Death invalidation");
                source.Select(Vector3.zero,55,scene.handle);
                Require(!Contains(source,ranks[180].GetInstanceID()),"No corpse marker");
                ranks[180].gameObject.SetActive(false); source.Select(Vector3.zero,55,scene.handle);
                ranks[180].ResetForPool(); ranks[180].gameObject.SetActive(true);
                typeof(EnemyRank).GetProperty("GradeType").SetValue(ranks[180],EnemyGradeType.Boss);
                source.Select(Vector3.zero,55,scene.handle);
                Require(Contains(source,ranks[180].GetInstanceID()),"Pool reuse");
            }
            checks.Add("Death while active and deactivate/reset/reuse x3");
            for(int i=0;i<10;i++) source.Select(Vector3.zero,55,scene.handle);
            long before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<100;i++) source.Select(Vector3.zero,55,scene.handle);
            long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            Require(allocated==0,"Warm candidate allocations: "+allocated);
            checks.Add("100 warm candidate selections: 0 managed bytes");
            controller.SetZoom(1); Require(controller.ZoomRadius==20,"Min zoom");
            controller.SetZoom(1000); Require(controller.ZoomRadius==80,"Max zoom");
            controller.SetZoom(55);
            GameplayInputBlocker.Block(root);
            Require(!controller.View.ZoomInButton.interactable && !controller.View.ZoomOutButton.interactable,"Blocked controls");
            controller.View.ZoomInButton.onClick.Invoke(); Require(controller.ZoomRadius==55,"Blocked listener guard");
            GameplayInputBlocker.Unblock(root);
            Require(controller.View.ZoomInButton.interactable,"Unblock");
            controller.View.ZoomInButton.onClick.Invoke(); Require(controller.ZoomRadius==45,"Zoom in");
            controller.View.ZoomOutButton.onClick.Invoke(); Require(controller.ZoomRadius==55,"Zoom out");
            checks.Add("Zoom clamps, button callbacks, input lock and unlock");
            int projectionCount=controller.ProjectionCount, candidateCount=controller.CandidateCount;
            controller.ForceHide();
            var late=typeof(WorldMinimapController).GetMethod("LateUpdate",BindingFlags.NonPublic|BindingFlags.Instance);
            for(int i=0;i<10;i++) late.Invoke(controller,null);
            Require(controller.ProjectionCount==projectionCount && controller.CandidateCount==candidateCount && controller.View.Markers.MarkerCount==0,"Hidden idle");
            controller.ShowForHub(PlayerContext.Instance.CurrentActor.transform);
            late.Invoke(controller,null);
            Require(controller.IsVisible,"Hub restore");
            checks.Add("Hidden zero work and Hub rebind");
            return new { status="PASS", checks, warmCandidateAllocatedBytes=allocated };
        }
        finally
        {
            GameplayInputBlocker.Unblock(root);
            controller.SetZoom(zoom);
            source.Dispose();
            Object.DestroyImmediate(root);
            SceneManager.UnloadSceneAsync(scene);
        }
    }

    private static bool Contains(MinimapEnemySource source,int id)
    {
        for(int i=0;i<source.Count;i++)
            if(source.TryGet(i,Vector3.zero,55*55,out _,out _,out int candidate) && candidate==id) return true;
        return false;
    }

    private static void Require(bool success,string message) { if(!success) throw new InvalidOperationException("Minimap validation: "+message); }
}
