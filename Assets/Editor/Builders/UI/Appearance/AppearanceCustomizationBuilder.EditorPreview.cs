using System;
using System.IO;
using System.Linq;
using Overburst.Appearance;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static partial class AppearanceCustomizationBuilder
{
    // No serialized Player prefab reference points to this Editor-only authoring asset.
    public static void SaveEditorPreviewExtensions(GameObject root)
    {
        var panel=root.GetComponent<AppearanceCustomizationPanel>();
        if(!panel||!panel.surface)throw new InvalidOperationException("Appearance product surface missing");
        var extension=Stretch(panel.surface.transform,"Editor Preview Extensions");
        extension.gameObject.tag="EditorOnly";
        foreach(var tab in root.GetComponentsInChildren<AppearanceOptionalTab>(true))tab.transform.SetParent(extension,false);
        foreach(var developer in root.GetComponentsInChildren<AppearanceDeveloperPreview>(true))
        {developer.panel=null;developer.transform.SetParent(extension,false);}
        Folder(Path.GetDirectoryName(AppearanceCustomizationPanel.EditorPreviewPath).Replace('\\','/'));
        PrefabUtility.SaveAsPrefabAsset(extension.gameObject,AppearanceCustomizationPanel.EditorPreviewPath);
        Object.DestroyImmediate(extension.gameObject);
    }
}
