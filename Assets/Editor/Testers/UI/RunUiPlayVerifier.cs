using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class RunUiPlayVerifier
{
    private static OverburstRunUi ui;
    private static float started;
    private static int step, calls;
    private static bool accept;
    private static string resolution;
    private static readonly List<object> results=new List<object>();
    public static void Start()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Play Mode required.");
        EditorApplication.update-=Tick;
        ui=UnityEngine.Object.FindFirstObjectByType<OverburstRunUi>();if(ui==null)ui=OverburstRunUi.Create();
        resolution=Screen.width.ToString();results.Clear();step=0;calls=0;accept=false;
        ui.ShowCards(new[]{
            new RunCardPresentation{Title="생명의 각인",Description="이번 던전 동안 유지됩니다.",Value="최대 체력 +36%",Grade=ItemGrade.Rare},
            new RunCardPresentation{Title="파괴의 각인",Description="이번 던전 동안 유지됩니다.",Value="공격력 +38%",Grade=ItemGrade.Mythic},
            new RunCardPresentation{Title="안전한 전송",Description="아이템 스택 하나를 창고로 전송할 수 있습니다.",Value="전송 오브젝트",IsReward=true}},i=>{calls++;return accept;});
        var card=ui.GetComponentsInChildren<OverburstRunCardView>(true)[0];
        Submit(card.GetComponentInChildren<Button>(true));
        results.Add(new{check="locked-before-reveal",pass=calls==0});
        started=Time.unscaledTime;Capture("cards-back");step=1;EditorApplication.update+=Tick;
    }
    private static void Capture(string name)
    {
        string path=Path.GetFullPath("../개인파일/코덱스산출/UI/20260925_Goal04/"+name+"-"+resolution+".png");
        ScreenCapture.CaptureScreenshot(path);
        results.Add(new{check=name,time=Time.unscaledTime-started,cards=ui.GetComponentsInChildren<OverburstRunCardView>(true).Select(c=>new{front=c.transform.Find("Flip/Front").gameObject.activeSelf,scale=c.transform.Find("Flip").localScale.x}).ToArray()});
    }
    private static void Tick()
    {
        if(!Application.isPlaying||ui==null){EditorApplication.update-=Tick;return;}
        float t=Time.unscaledTime-started;
        var current=ui.GetComponentsInChildren<OverburstRunCardView>(true);
        bool ready=current.All(c=>c.GetComponentInChildren<Button>(true).interactable);
        if(step==0){step++;}
        else if(step==1&&current[0].transform.Find("Flip/Front").gameObject.activeSelf&&!current[2].transform.Find("Flip/Front").gameObject.activeSelf){Capture("cards-reveal");step++;}
        else if(step==2&&ready){Capture("cards-approved-layout");step++;started=Time.unscaledTime;}
        else if(step==3&&t>=.35f&&ready)
        {
            var b=ui.GetComponentsInChildren<OverburstRunCardView>(true)[0].GetComponentInChildren<Button>(true);
            Submit(b);results.Add(new{check="false-retains-choice",pass=ui.IsOpen&&b.interactable&&calls==1});
            Capture("cards-save-failed");step++;
        }
        else if(step==4&&t>=.70f)
        {
            var b=ui.GetComponentsInChildren<OverburstRunCardView>(true)[0].GetComponentInChildren<Button>(true);
            accept=true;Submit(b);Submit(b);
            results.Add(new{check="true-closes-once",pass=!ui.IsOpen&&calls==2,blocked=GameplayInputBlocker.IsGameplayInputBlocked});
            ScreenCapture.CaptureScreenshot(Path.GetFullPath("../개인파일/코덱스산출/UI/20260925_Goal04/cards-selected-"+resolution+".png"));
            File.WriteAllText(Path.GetFullPath("../개인파일/코덱스산출/UI/20260925_Goal04/card-results-"+resolution+".json"),Newtonsoft.Json.JsonConvert.SerializeObject(results,Newtonsoft.Json.Formatting.Indented));
            EditorApplication.update-=Tick;step++;
        }
    }
    private static void Submit(Button button)
    {ExecuteEvents.Execute(button.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);}
}

