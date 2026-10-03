using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class ElementGemUiBuilder
{
    public const string Path = "Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstEquipment_Rpg11.prefab";
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Idle Editor required.");
        var root = PrefabUtility.LoadPrefabContents(Path);
        try
        {
            var layout=root.transform.Find("Layout");
            Rename(layout,"Slot • 귀걸이 1","Slot • 귀걸이");
            Rename(layout,"Label • 귀걸이 1","Label • 귀걸이");
            var gem=Rename(layout,"Slot • 귀걸이 2","Slot • 원소보석");
            var label=Rename(layout,"Label • 귀걸이 2","Label • 원소보석").GetComponent<Text>();
            label.text="원소보석"; layout.Find("Label • 귀걸이").GetComponent<Text>().text="귀걸이";
            foreach(var old in gem.GetComponents<GearEquipmentSlotUI>()) UnityEngine.Object.DestroyImmediate(old);
            foreach(var old in gem.GetComponents<OverburstUISlotTooltip>()) UnityEngine.Object.DestroyImmediate(old);
            var pointer=gem.GetComponent<ElementGemEquipmentSlotUI>() ?? gem.gameObject.AddComponent<ElementGemEquipmentSlotUI>();
            pointer.Configure(label);
            var icon=gem.Find("Slot Icon").GetComponent<Image>();
            icon.sprite=AssetDatabase.LoadAssetAtPath<ElementGemItemData>(ElementGemCatalogBuilder.DataRoot+"/EG_Fire_Uncommon.asset").icon;
            icon.preserveAspect=true;icon.color=new Color(.75f,.75f,.75f,.6f);
            if(root.GetComponentsInChildren<Transform>(true).Any(x=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(x.gameObject)>0)) throw new InvalidOperationException("Missing component in equipment prefab.");
            PrefabUtility.SaveAsPrefabAsset(root,Path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    static Transform Rename(Transform parent,string oldName,string newName)
    {
        var target=parent.Find(newName)??parent.Find(oldName);
        if(target==null) throw new InvalidOperationException("Missing slot " + oldName);
        target.name=newName; return target;
    }
}
