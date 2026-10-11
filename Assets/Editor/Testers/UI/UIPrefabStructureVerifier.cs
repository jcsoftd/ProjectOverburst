using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class UIPrefabStructureVerifier
{
    public const string Output="../개인파일/코덱스산출/UI/20261011_UIPrefabStructure/Evidence";
    static string Ref(UnityEngine.Object value,GameObject root)
    {
        if(!value)return "null";
        var go=value as GameObject??(value as Component)?.gameObject;
        if(go&&(go==root||go.transform.IsChildOf(root.transform)))
        {
            string path=AnimationUtility.CalculateTransformPath(go.transform,root.transform);
            if(value is Component component)return path+":"+component.GetType().FullName+":"+Array.IndexOf(go.GetComponents(component.GetType()),component);
            return path+":GameObject";
        }
        if(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value,out string guid,out long id))return guid+":"+id;
        return value.GetType().FullName+":"+value.name;
    }
    public static JObject Snapshot(GameObject root,bool onlyUI=false)
    {
        var result=new JObject();
        foreach(var t in root.GetComponentsInChildren<Transform>(true))
        {
            if(onlyUI&&t!=root.transform&&!(t is RectTransform))continue;
            string path=AnimationUtility.CalculateTransformPath(t,root.transform);
            var record=new JObject{{"active",t.gameObject.activeSelf},{"layer",t.gameObject.layer},{"tag",t.tag}};
            var components=new JObject();
            foreach(var component in t.GetComponents<Component>())
            {
                if(!component)throw new InvalidOperationException("Missing script: "+path);
                if(onlyUI&&(component is Renderer||component is ParticleSystem||component is Animator||component is Collider||component is Rigidbody))continue;
                string key=component.GetType().FullName+":"+Array.IndexOf(t.GetComponents(component.GetType()),component);
                var fields=new JObject();var data=new SerializedObject(component);var property=data.GetIterator();
                while(property.Next(true))
                    if(property.propertyType==SerializedPropertyType.ObjectReference&&!new[]{"m_CorrespondingSourceObject","m_PrefabInstance","m_PrefabAsset"}.Contains(property.propertyPath))
                    {
                        if(!property.objectReferenceValue&&property.objectReferenceInstanceIDValue!=0)throw new InvalidOperationException("Missing reference: "+path+"/"+property.propertyPath);
                        fields[property.propertyPath]=Ref(property.objectReferenceValue,root);
                    }
                if(component is RectTransform rect)fields["rect"]=new JArray(rect.anchorMin.x,rect.anchorMin.y,rect.anchorMax.x,rect.anchorMax.y,rect.pivot.x,rect.pivot.y,rect.anchoredPosition3D.x,rect.anchoredPosition3D.y,rect.anchoredPosition3D.z,rect.sizeDelta.x,rect.sizeDelta.y,rect.localScale.x,rect.localScale.y,rect.localScale.z,rect.localRotation.x,rect.localRotation.y,rect.localRotation.z,rect.localRotation.w);
                if(component is Graphic graphic)fields["graphic"]=new JArray(graphic.color.r,graphic.color.g,graphic.color.b,graphic.color.a,graphic.raycastTarget,graphic.enabled);
                if(component is Selectable selectable){var colors=selectable.colors;fields["selectable"]=new JObject{{"interactable",selectable.interactable},{"transition",(int)selectable.transition},{"normal",Color(colors.normalColor)},{"highlight",Color(colors.highlightedColor)},{"pressed",Color(colors.pressedColor)},{"selected",Color(colors.selectedColor)},{"disabled",Color(colors.disabledColor)},{"fade",colors.fadeDuration},{"multiplier",colors.colorMultiplier}};}
                if(component is Canvas canvas)fields["canvas"]=JToken.FromObject(new{canvas.enabled,canvas.renderMode,canvas.overrideSorting,canvas.sortingOrder});
                if(component is ScrollRect scroll)fields["scroll"]=JToken.FromObject(new{scroll.horizontal,scroll.vertical,scroll.movementType,scroll.inertia,scroll.scrollSensitivity});
                components[key]=fields;
            }
            record["components"]=components;result[path]=record;
        }
        return result;
    }
    static JArray Color(UnityEngine.Color color)=>new JArray(color.r,color.g,color.b,color.a);
    public static void CaptureBefore()
    {
        AppearanceCustomizationBuilder.RequireIdle();Directory.CreateDirectory(Path.GetFullPath(Output));
        var assets=new JObject();
        foreach(var path in AssetDatabase.FindAssets("t:Prefab",UIPrefabStructureBuilder.SourceRoots).Select(AssetDatabase.GUIDToAssetPath).Where(p=>!p.StartsWith(UIPrefabStructureBuilder.PartsRoot+"/",StringComparison.Ordinal)))
            assets[path]=new JObject{{"guid",AssetDatabase.AssetPathToGUID(path)},{"state",Snapshot(AssetDatabase.LoadAssetAtPath<GameObject>(path))}};
        File.WriteAllText(Path.GetFullPath(Output+"/semantic-before.json"),assets.ToString(Formatting.Indented));
    }
    public static object Verify()=>VerifyOwners(UIPrefabStructureBuilder.Plan.Select(p=>p.owner).Distinct().ToArray());
    public static object VerifyTitle()=>VerifyOwners(new[]{TitleCharacterBuilder.PrefabPath});
    public static object VerifyOwners(string[] owners)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        var before=JObject.Parse(File.ReadAllText(Path.GetFullPath(Output+"/semantic-before.json")));var differences=new List<string>();var allAfter=new JObject();int objects=0,fields=0;
        foreach(var asset in before.Properties())
        {
            var root=AssetDatabase.LoadAssetAtPath<GameObject>(asset.Name);
            // Normalize JSON numbers like the saved baseline (float vs parsed double).
            var after=JObject.Parse(Snapshot(root).ToString(Formatting.None));allAfter[asset.Name]=after;objects+=after.Count;
            if(AssetDatabase.AssetPathToGUID(asset.Name)!=(string)asset.Value["guid"])differences.Add(asset.Name+":GUID");
            if(!((JObject)asset.Value["state"]).Properties().Select(p=>p.Name).SequenceEqual(after.Properties().Select(p=>p.Name)))differences.Add(asset.Name+":hierarchy sibling order");
            foreach(var row in ((JObject)asset.Value["state"]).Properties())
            {
                fields+=((JObject)row.Value["components"]).Properties().Sum(p=>((JObject)p.Value).Count);
                if(!JToken.DeepEquals(row.Value,after[row.Name]))differences.Add(asset.Name+"/"+row.Name);
            }
            if(after.Count!=((JObject)asset.Value["state"]).Count)differences.Add(asset.Name+":object count");
        }
        File.WriteAllText(Path.GetFullPath(Output+"/semantic-after.json"),allAfter.ToString(Formatting.Indented));
        int nested=0;
        foreach(var part in UIPrefabStructureBuilder.Plan.Where(p=>owners.Contains(p.owner)))
        {
            var view=AssetDatabase.LoadAssetAtPath<GameObject>(part.owner).transform.Find(part.hierarchy);
            string path=UIPrefabStructureBuilder.PartsRoot+"/"+part.group+"/PF_"+part.name+".prefab";
            if(!view||!PrefabUtility.IsAnyPrefabInstanceRoot(view.gameObject)||PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(view.gameObject)!=path)differences.Add("Nested view: "+part.hierarchy);else nested++;
        }
        var result=new{success=differences.Count==0,owners,originalPrefabs=before.Count,objects,fields,nestedViews=nested,differences,utc=DateTime.UtcNow,player="EXCLUDED_BY_USER"};
        File.WriteAllText(Path.GetFullPath(Output+"/semantic-result.json"),JsonConvert.SerializeObject(result,Formatting.Indented));
        if(differences.Count>0)throw new InvalidOperationException("UI structure differs: "+string.Join(" | ",differences.Take(12)));
        return result;
    }
}
