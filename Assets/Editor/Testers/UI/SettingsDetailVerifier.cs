using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

public static partial class SettingsPresentationVerifier
{
    static Bounds GlyphBounds(RectTransform relative,Text text)
    {
        var generator=text.cachedTextGenerator;generator.Populate(text.text,text.GetGenerationSettings(text.rectTransform.rect.size));
        var vertices=generator.verts;var bounds=new Bounds();bool initialized=false;
        // Unity may return a final empty quad, or exactly one quad for a one-character switch label.
        for(int quad=0;quad+3<vertices.Count;quad+=4)
        {
            var local=new Bounds(vertices[quad].position,Vector3.zero);
            for(int i=1;i<4;i++)local.Encapsulate(vertices[quad+i].position);
            if(local.size.x<=.001f || local.size.y<=.001f)continue;
            for(int i=0;i<4;i++)
            {
                var position=relative.InverseTransformPoint(text.transform.TransformPoint(vertices[quad+i].position/text.pixelsPerUnit));
                if(!initialized){bounds=new Bounds(position,Vector3.zero);initialized=true;}else bounds.Encapsulate(position);
            }
        }
        Check(initialized,"visible glyph geometry "+text.text);return bounds;
    }
    static IEnumerator VerifySettingsDetails(OverburstSettingsPanel panel)
    {
        var sliders=panel.GetComponentsInChildren<Slider>(true).Where(s=>!s.GetComponentInParent<OverburstSettingsNumberRow>()).ToArray();
        var previous=sliders.Select(s=>s.value).ToArray();var rows=new List<object>();
        try
        {
            foreach(int tab in new[]{0,1,2,3})
            {
                panel.tabs[tab].isOn=true;if(tab==2)SetScroll(panel.combatScroll,1f);yield return Frames(3);Canvas.ForceUpdateCanvases();
                foreach(var button in panel.tabs)
                {
                    var rect=(RectTransform)button.transform;var label=button.transform.Find("Text").GetComponent<Text>();
                    var glyph=GlyphBounds(rect,label);var active=RectTransformUtility.CalculateRelativeRectTransformBounds(rect,button.transform.Find("Active"));
                    Check(Mathf.Abs(glyph.center.y-active.center.y)<8f,"tab glyphs centered in visible frame "+label.text);
                }
                foreach(var row in panel.pages[tab].GetComponentsInChildren<RectTransform>(false).Where(r=>r.name.StartsWith("Row • ")))
                {
                    if(!row.Find("Label") || !row.Find("Description") || !row.Find("Control"))continue;
                    var label=GlyphBounds(row,row.Find("Label").GetComponent<Text>());
                    var description=GlyphBounds(row,row.Find("Description").GetComponent<Text>());
                    float center=(label.max.y+description.min.y)*.5f;
                    Check(Mathf.Abs(center-row.rect.center.y)<12f,"title and description block centered "+row.name);
                }
                foreach(var toggle in panel.pages[tab].GetComponentsInChildren<Toggle>(false).Where(t=>t.transform.Find("On Text")))
                foreach(var name in new[]{"On Text","Off Text"})
                {
                    var rect=(RectTransform)toggle.transform;var glyph=GlyphBounds(rect,toggle.transform.Find(name).GetComponent<Text>());
                    Check(Mathf.Abs(glyph.center.y-rect.rect.center.y)<6f,"switch word centered "+toggle.transform.parent.parent.name+name);
                }
                foreach(float normalized in new[]{0f,.5f,1f})
                {
                    foreach(var slider in panel.pages[tab].GetComponentsInChildren<Slider>(false).Where(s=>!s.GetComponentInParent<OverburstSettingsNumberRow>()))slider.value=Mathf.Lerp(slider.minValue,slider.maxValue,normalized);
                    yield return Frames(2);Canvas.ForceUpdateCanvases();
                    foreach(var slider in panel.pages[tab].GetComponentsInChildren<Slider>(false).Where(s=>!s.GetComponentInParent<OverburstSettingsNumberRow>()))
                    {
                        var rect=(RectTransform)slider.transform;var value=slider.transform.Find("Text").GetComponent<Text>();
                        var handle=RectTransformUtility.CalculateRelativeRectTransformBounds(rect,slider.handleRect);
                        var number=RectTransformUtility.CalculateRelativeRectTransformBounds(rect,value.rectTransform);
                        Check(handle.size.y>40f && handle.size.x>20f,"positive slider handle size "+slider.transform.parent.parent.name);
                        Check(handle.max.x+20f<=number.min.x && handle.min.x>=rect.rect.xMin,"0-100 percent handle stays inside track and away from number "+slider.transform.parent.parent.name+normalized);
                        Check(number.max.x<=rect.rect.xMax,"percentage stays inside control "+value.text);
                        rows.Add(new{tab,name=slider.transform.parent.parent.name,normalized,value=value.text,handle=handle.ToString(),number=number.ToString()});
                    }
                    if(normalized==1f){ScreenCapture.CaptureScreenshot(Path.Combine(output,"settings-detail-max-"+tab+".png"));yield return Frames(2);}
                }
            }
            File.WriteAllText(Path.Combine(output,"settings-detail-geometry.json"),JsonConvert.SerializeObject(rows,Formatting.Indented));
        }
        finally {for(int i=0;i<sliders.Length;i++)sliders[i].value=previous[i];panel.tabs[2].isOn=true;}
    }
}
