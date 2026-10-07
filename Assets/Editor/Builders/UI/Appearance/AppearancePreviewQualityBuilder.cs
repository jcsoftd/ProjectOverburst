using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Appearance;
using UnityEditor;
using UnityEngine;

public static partial class AppearanceCustomizationBuilder
{
    static void BuildPreviewQuality(CharacterAppearanceCatalog catalog)
    {
        string root=Art+"/HighQualityTextures";Folder(root);
        var sources=catalog.skinColors.Select(x=>x.material)
            .Concat(catalog.visualPrefab.GetComponentsInChildren<Renderer>(true).Where(r=>r.name.StartsWith("Female",StringComparison.Ordinal)||r.name.StartsWith("Fem_Armor",StringComparison.Ordinal)||r.name.StartsWith("Hair_",StringComparison.Ordinal)).SelectMany(r=>r.sharedMaterials)).Where(m=>m).Distinct()
            .SelectMany(m=>m.GetTexturePropertyNames().Select(m.GetTexture)).Where(t=>t).Distinct().ToArray();
        var overrides=new List<AppearanceTextureOverride>();var records=new List<object>();
        foreach(var original in sources)
        {
            string source=AssetDatabase.GetAssetPath(original);var importer=AssetImporter.GetAtPath(source) as TextureImporter;
            if(!importer||!source.StartsWith("Assets/ThirdParty/02_인간캐릭터/P09_Modular_Humanoid/",StringComparison.Ordinal))continue;
            importer.GetSourceTextureWidthAndHeight(out int width,out int height);
            if(Mathf.Max(width,height)<=Mathf.Max(original.width,original.height)&&importer.anisoLevel>=8)continue;
            string path=root+"/"+AssetDatabase.AssetPathToGUID(source)+"_"+Path.GetFileName(source);
            if(!File.Exists(path)&&!AssetDatabase.CopyAsset(source,path))throw new IOException("프리뷰 고해상도 텍스처 복사 실패: "+source);
            var own=(TextureImporter)AssetImporter.GetAtPath(path);own.maxTextureSize=Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(width,height)),1024,8192);
            own.anisoLevel=16;own.filterMode=FilterMode.Trilinear;own.textureCompression=TextureImporterCompression.CompressedHQ;own.compressionQuality=100;own.mipmapEnabled=true;
            var windows=own.GetPlatformTextureSettings("Standalone");if(windows.overridden){windows.maxTextureSize=own.maxTextureSize;windows.compressionQuality=100;own.SetPlatformTextureSettings(windows);}
            own.SaveAndReimport();var high=AssetDatabase.LoadAssetAtPath<Texture>(path);
            overrides.Add(new AppearanceTextureOverride{original=original,highQuality=high});records.Add(new{source,owned=path,sourceWidth=width,sourceHeight=height,beforeWidth=original.width,afterWidth=high.width,afterHeight=high.height});
        }
        string materialPath=Root+"/AppearanceContactShadow.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if(!material){var shader=AssetDatabase.LoadAssetAtPath<Shader>(Root+"/AppearanceContactShadow.shader");if(!shader)throw new InvalidOperationException("접촉 그림자 셰이더 없음");material=new Material(shader);AssetDatabase.CreateAsset(material,materialPath);}
        string qualityPath=Root+"/AppearancePreviewQuality.asset";var quality=AssetDatabase.LoadAssetAtPath<AppearancePreviewQuality>(qualityPath);
        if(!quality){quality=ScriptableObject.CreateInstance<AppearancePreviewQuality>();AssetDatabase.CreateAsset(quality,qualityPath);}
        quality.textures=overrides.ToArray();quality.contactShadowMaterial=material;EditorUtility.SetDirty(quality);AssetDatabase.SaveAssetIfDirty(quality);
        File.WriteAllText(Path.GetFullPath(Output+"/preview-texture-quality.json"),JsonConvert.SerializeObject(new{status="PASS_NATIVE_TEXTURE_QUALITY",records},Formatting.Indented));
    }
}
