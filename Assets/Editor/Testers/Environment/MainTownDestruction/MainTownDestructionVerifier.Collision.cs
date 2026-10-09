using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static partial class MainTownDestructionVerifier
{
    static IEnumerator CollisionTrial()
    {
        var controller=UnityEngine.Object.FindFirstObjectByType<MainTownDestruction>();var actor=PlayerContext.Instance.CurrentActor;
        var indices=Enumerable.Range(0,controller.EntryCount).Where(controller.IsTerrainTree).GroupBy(controller.DefinitionFor).Select(g=>g.First()).ToArray();var rows=new List<object>();
        int Hits(int index)
        {
            var p=controller.PositionFor(index);float height=Mathf.Clamp(controller.catalog.definitions[controller.DefinitionFor(index)].bounds.size.y*.25f,.3f,1.2f);
            return new[]{Vector3.forward,Vector3.back,Vector3.right,Vector3.left}.Sum(d=>Physics.RaycastAll(p+Vector3.up*height+d*2,-d,4,~(1<<2|1<<8|1<<7),QueryTriggerInteraction.Ignore).Count(h=>Vector3.Distance(new Vector3(h.point.x,0,h.point.z),new Vector3(p.x,0,p.z))<.6f));
        }
        for(int n=0;n<indices.Length;n++)
        {
            int index=indices[n];controller.RestoreAll();yield return null;int before=Hits(index);var position=controller.PositionFor(index);var definition=controller.catalog.definitions[controller.DefinitionFor(index)];int refresh=controller.TerrainCollisionRefreshCount;
            Check(controller.TryBreak(index,new DamageInfo(10,position,actor.gameObject,Vector3.forward,0)),definition.source.name+": Terrain contact breaks");yield return null;Physics.SyncTransforms();int after=Hits(index);
            Check(after==0,definition.source.name+": destroyed tree leaves no trunk collision");Check(controller.TerrainCollisionRefreshCount==refresh+1,definition.source.name+": collision updates once");controller.RestoreAll();yield return null;int restored=Hits(index);Check(restored==before,definition.source.name+": original collision restores");rows.Add(new{name=definition.source.name,before,after,restored});
            if(n%8==7){float until=Time.time+3.4f;while(Time.time<until)yield return null;}
        }
        controller.RestoreAll();yield return null;int prior=controller.TerrainCollisionRefreshCount;
        foreach(int index in indices.Take(3))controller.TryBreak(index,new DamageInfo(10,controller.PositionFor(index),actor.gameObject,Vector3.forward,0));yield return null;
        Check(controller.TerrainCollisionRefreshCount==prior+1,"Three Terrain contacts in one frame rebuild collision once");controller.RestoreAll();yield return null;
        int pine=indices.First(i=>controller.catalog.definitions[controller.DefinitionFor(i)].source.name=="DF_Pine_01");var p=controller.PositionFor(pine);var direction=Approach(controller,pine,actor.transform,5);var cc=actor.GetComponent<CharacterController>();
        ActorTeleportUtility.TeleportSafely(actor.transform,p-direction*1.2f,Quaternion.LookRotation(direction));float settle=Time.time+.7f;while(Time.time<settle)yield return null;var start=actor.transform.position;cc.Move(direction*2.4f);float blocked=Vector3.Dot(actor.transform.position-start,direction);
        controller.TryBreak(pine,new DamageInfo(10,p,actor.gameObject,direction,0));yield return null;ActorTeleportUtility.TeleportSafely(actor.transform,p-direction*1.2f,Quaternion.LookRotation(direction));settle=Time.time+.7f;while(Time.time<settle)yield return null;start=actor.transform.position;cc.Move(direction*2.4f);float cleared=Vector3.Dot(actor.transform.position-start,direction);
        Check(blocked<1.5f&&cleared>2.2f,"Real CharacterController is blocked before and crosses the destroyed Terrain trunk");controller.RestoreAll();
        Write("collision-regression.json",new{status="PASS",types=rows.Count,rows,playerBeforeDistance=blocked,playerAfterDistance=cleared});Check(errors.Count==0,"Collision regression has no runtime errors");
    }
}
