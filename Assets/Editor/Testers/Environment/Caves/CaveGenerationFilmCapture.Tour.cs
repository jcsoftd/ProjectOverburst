using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using Overburst.Caves;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

public static partial class CaveGenerationFilmCapture
{
    static float[] Project(Vector3 p)
    {
        var q=camera.WorldToViewportPoint(p+Vector3.up*.2f);
        return new[]{q.x*Width,(1-q.y)*Height};
    }

    static IEnumerator TourFilm()
    {
        var bootstrap=Object.FindFirstObjectByType<CaveWorld>();
        if(!bootstrap)throw new InvalidOperationException("Live entry missing.");
        var assets=bootstrap.GetComponent<CaveRuntimeGenerator>().assets;
        Object.Destroy(bootstrap.gameObject);yield return null;SetupCamera();
        int[] counts={9,12,15,20},seeds={-284089667,1904484630,-1619063537,1904485018};
        for(int i=0;i<counts.Length;i++)
        {
            Write("progress.json",new{stage="generate tour",count=counts[i],seed=seeds[i]});
            var build=Generate(assets,counts[i],seeds[i]);
            try{while(build.MoveNext())yield return build.Current;}finally{(build as IDisposable)?.Dispose();}
            if(SessionState.GetBool(Key+"boundaryOnly",false))
            {
                Time.captureFramerate=Fps;var detail=BoundaryFilm();
                try{while(detail.MoveNext())yield return detail.Current;}finally{(detail as IDisposable)?.Dispose();}
                yield break;
            }
            var route=TourRoute();float length=0;var cumulative=new List<float>{0};
            for(int n=1;n<route.Count;n++){length+=Vector3.Distance(route[n-1],route[n]);cumulative.Add(length);}
            var samples=new List<object>();Time.captureFramerate=Fps;
            camera.orthographic=false;camera.fieldOfView=2*Mathf.Atan(6.887f/20f)*Mathf.Rad2Deg;
            BeginTake("tour-"+counts[i]);Vector3 focus=route[0];
            Write("progress.json",new{stage="filming tour",count=counts[i],length});
            for(int f=0;f<Fps*14;f++)
            {
                float t=f/(float)(Fps*14-1),distance=length*Mathf.SmoothStep(0,1,t);
                int k=1;while(k<cumulative.Count-1&&cumulative[k]<distance)k++;
                var targetFocus=Vector3.Lerp(route[k-1],route[k],Mathf.InverseLerp(cumulative[k-1],cumulative[k],distance));
                focus=Vector3.Lerp(focus,targetFocus,1-Mathf.Pow(.88f,30f/Fps));
                var rotation=Quaternion.Euler(55,45+Mathf.Sin(t*Mathf.PI)*7,0);
                camera.transform.SetPositionAndRotation(focus+Vector3.up*1.2f-rotation*Vector3.forward*(24+2*Mathf.Sin(t*Mathf.PI)),rotation);
                Frame();if(f%(Fps*3)==0)System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,"Raw","tour-"+counts[i]+"-"+f+".png"),pixels.EncodeToPNG());
                samples.Add(new{focus=V(focus),camera=V(camera.transform.position)});yield return null;
            }
            EndTake();Write("tour-"+counts[i]+".json",new{count=counts[i],seed=seeds[i],length,route=route.Select(V).ToArray(),samples,fieldOfView=camera.fieldOfView,terrainEnabled=world.GetComponentInChildren<Terrain>()?.enabled});
            maps.Add(new{count=counts[i],seed=seeds[i],generationMilliseconds=world.generationMilliseconds});
            if(i==0){var technical=BoundaryFilm();try{while(technical.MoveNext())yield return technical.Current;}finally{(technical as IDisposable)?.Dispose();}}
            Time.captureFramerate=0;Object.Destroy(world.gameObject);world=null;yield return null;yield return Resources.UnloadUnusedAssets();
        }
        Write("progress.json",new{stage="complete",maps=maps.Count});
    }

    static List<Vector3> TourRoute()
    {
        // Follow three installed connections, including their approach and landing positions.
        var links=world.generatedRoot.GetComponentsInChildren<CaveRigidConnection>();
        var first=links.OrderByDescending(c=>Mathf.Abs(SelectedAngle(c))).First();
        var chain=new List<(CaveRigidConnection link,bool reverse)>{(first,false)};
        var current=first.boundaryB;var used=new HashSet<CaveRigidConnection>{first};
        while(chain.Count<3)
        {
            var next=links.FirstOrDefault(c=>!used.Contains(c)&&(c.boundaryA==current||c.boundaryB==current));
            if(!next)break;bool reverse=next.boundaryB==current;chain.Add((next,reverse));used.Add(next);current=reverse?next.boundaryA:next.boundaryB;
        }
        Vector3 Center(CavePlatformBoundary b)=>world.courts.First(c=>c.tile.GetComponent<CavePlatformBoundary>()==b).center;
        var points=new List<Vector3>{Center(first.boundaryA)};
        foreach(var step in chain)
        {
            var c=step.link;points.Add(step.reverse?c.end:c.start);points.Add(step.reverse?c.start:c.end);
            points.Add(Center(step.reverse?c.boundaryA:c.boundaryB));
        }
        return points;
    }

    static IEnumerator BoundaryFilm()
    {
        camera.orthographic=true;
        var links=world.generatedRoot.GetComponentsInChildren<CaveRigidConnection>();
        var c=links.Where(x=>x.stairCount==0).OrderByDescending(x=>Mathf.Abs(SelectedAngle(x))).First();
        var boundary=c.boundaryA;var port=boundary.Data.ports[c.portA];
        var axis=(c.end-c.start);axis.y=0;axis.Normalize();
        Vector3 begin=c.start-axis*3f;
        if(!boundary.Sample(begin,10,10,out begin))throw new InvalidOperationException("No starting point for boundary demonstration.");
        var finish=c.end+axis*2.5f;
        var open=new CaveFallProtection.Movement();var passed=open.Resolve(begin,finish,.35f);
        if(Vector2.Distance(new Vector2(passed.x,passed.z),new Vector2(finish.x,finish.z))>1)throw new InvalidOperationException("Selected bridge is not suitable for the movement demonstration.");
        Vector3 sideTarget=default;bool found=false;
        foreach(float angle in new[]{port.MinAngle,port.MaxAngle,port.heading})
        {
            float rad=angle*Mathf.Deg2Rad;
            var desired=boundary.transform.TransformPoint(port.pivot+new Vector3(Mathf.Cos(rad),0,Mathf.Sin(rad))*(port.radius+port.deckLength*.75f));
            desired.y=begin.y;
            var test=new CaveFallProtection.Movement();var actual=test.Resolve(begin,desired,.35f);
            if(Vector3.Distance(actual,desired)>2&&!boundary.ContainsLocal(boundary.transform.InverseTransformPoint(desired))&&!c.Sample(desired,10,10,out _)){sideTarget=desired;found=true;break;}
        }
        if(!found)throw new InvalidOperationException("No blocked candidate area found for the demonstration.");
        var points=boundary.Data.loops.SelectMany(l=>l.points).Select(boundary.transform.TransformPoint).ToArray();
        List<object> Segments()
        {
            var result=new List<object>();
            foreach(var loop in boundary.Data.loops)for(int n=0;n<loop.points.Length;n++)
            {
                var a=loop.points[n];var b=loop.points[(n+1)%loop.points.Length];var mid=(a+b)*.5f;bool candidate=false;
                foreach(var p in boundary.Data.ports){var d=mid-p.pivot;float angle=Mathf.Atan2(d.z,d.x)*Mathf.Rad2Deg;float widthAngle=Mathf.Atan2(p.width*.5f,Mathf.Max(.5f,p.radius))*Mathf.Rad2Deg;if(Mathf.Abs(Mathf.DeltaAngle(angle,p.heading))<=p.halfAngle+widthAngle&&Mathf.Abs(new Vector2(d.x,d.z).magnitude-p.radius)<5)candidate=true;}
                result.Add(new{a=Project(boundary.transform.TransformPoint(a)),b=Project(boundary.transform.TransformPoint(b)),candidate,open=c.Sample(boundary.transform.TransformPoint(mid),10,10,out _)});
            }
            return result;
        }
        var wide=new Bounds(points[0],Vector3.zero);foreach(var p in points)wide.Encapsulate(p);
        View(wide,0,68,1.08f);Write("outline-boundary.json",new{segments=Segments()});
        BeginTake("outline");for(int f=0;f<Fps*3;f++){Frame();yield return null;}EndTake();
        var bounds=new Bounds(begin,Vector3.zero);foreach(var p in new[]{c.start,c.end,finish,sideTarget,boundary.transform.TransformPoint(port.pivot)})bounds.Encapsulate(p);
        View(bounds,0,68,1.2f);var segments=Segments();
        var side=new Vector3(-axis.z,0,axis.x)*c.walkWidth*.5f;
        var deck=new[]{c.start-side,c.start+side,c.end+side,c.end-side}.Select(Project).ToArray();
        var fans=boundary.Data.ports.Select(p=>new{pivot=Project(boundary.transform.TransformPoint(p.pivot)),arc=Enumerable.Range(0,33).Select(i=>{float a=Mathf.Lerp(p.MinAngle,p.MaxAngle,i/32f)*Mathf.Deg2Rad;return Project(boundary.transform.TransformPoint(p.pivot+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*(p.radius+p.deckLength*.8f)));}).ToArray()}).ToArray();
        Write("boundary.json",new{segments,deck,fans,selected=SelectedAngle(c),allowed=port.halfAngle,c.walkWidth,length=Vector3.Distance(c.start,c.end),begin=V(begin),finish=V(finish),sideTarget=V(sideTarget),platform=boundary.name});
        // Three states of the same connector, and the same shipping movement query in every frame.
        foreach(string state in new[]{"closed","open","side","removed"})
        {
            bool active=state=="open"||state=="side";c.gameObject.SetActive(active);
            try
            {
                var movement=new CaveFallProtection.Movement();Vector3 at=begin,targetPoint=state=="side"?sideTarget:finish;
                var records=new List<object>();BeginTake("boundary-"+state);
                for(int f=0;f<Fps*5;f++)
                {
                    float t=Mathf.SmoothStep(0,1,Mathf.Clamp01((f-Fps*.5f)/(Fps*3.5f)));var wanted=Vector3.Lerp(begin,targetPoint,t);
                    at=movement.Resolve(at,wanted,.35f);Frame();records.Add(new{wanted=Project(wanted),actual=Project(at),wantedWorld=V(wanted),actualWorld=V(at),c.BoundaryOpen});yield return null;
                }
                EndTake();float error=Vector2.Distance(new Vector2(at.x,at.z),new Vector2(targetPoint.x,targetPoint.z));
                if((state=="open"&&error>1)||(state!="open"&&error<1))throw new InvalidOperationException("Unexpected boundary result: "+state);
                Write("boundary-"+state+".json",new{state,error,records});
            }
            finally{c.gameObject.SetActive(true);}
        }
        Vector3 dropWanted=sideTarget+Vector3.up*2;
        bool corrected=CaveFallProtection.TryDropLanding(begin,dropWanted,.15f,out var landing);
        if(!corrected)throw new InvalidOperationException("Drop landing correction did not find the cave.");
        Write("drop.json",new{origin=Project(begin),wanted=Project(dropWanted),landing=Project(landing),originWorld=V(begin),wantedWorld=V(dropWanted),landingWorld=V(landing),corrected});
        BeginTake("boundary-drop");for(int f=0;f<Fps*5;f++){Frame();yield return null;}EndTake();
    }
}
