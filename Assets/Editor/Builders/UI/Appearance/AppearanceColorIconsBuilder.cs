using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Overburst.Appearance;
using UnityEditor;

using UnityEngine;

public static partial class AppearanceCustomizationBuilder
{
    static void ApplyColorIcons(CharacterAppearanceCatalog catalog)
    {
        string path=Art+"/AppearanceColorIcons.png";
        string source=Path.GetFullPath(Output+"/GeneratedAssets/appearance-color-icons-v1.png");
        if(!File.Exists(source))throw new FileNotFoundException("확정 색상 아이콘이 없습니다.",source);
        if(!File.Exists(path)||!File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(path)))File.Copy(source,path,true);
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Multiple;
        importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.maxTextureSize=2048;importer.filterMode=FilterMode.Bilinear;importer.SaveAndReimport();
#pragma warning disable CS0618
        var rectangles=new List<SpriteMetaData>();
        void Add(string id,int column,int top,int height)
        {
            float x=25+column*253.2f;
            rectangles.Add(new SpriteMetaData{name=id,rect=new Rect(Mathf.Round(x),1024-top-height,220,height),alignment=(int)SpriteAlignment.Center,pivot=new Vector2(.5f,.5f)});
        }
        for(int i=0;i<9;i++)Add("hair-"+(i+1).ToString("00"),i%6,i<6?95:389,235);
        for(int i=0;i<3;i++)Add("skin-"+(i+1).ToString("00"),3+i,389,235);
        for(int i=0;i<5;i++)Add("eye-"+(i+1).ToString("00"),i,688,220);
        importer.spritesheet=rectangles.ToArray();EditorUtility.SetDirty(importer);AssetDatabase.WriteImportSettingsIfDirty(path);importer.SaveAndReimport();
#pragma warning restore CS0618
        var sprites=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToDictionary(x=>x.name);
        for(int i=0;i<9;i++)catalog.hairColors[i].icon=sprites["hair-"+(i+1).ToString("00")];
        for(int i=0;i<3;i++)catalog.skinColors[i].icon=sprites["skin-"+(i+1).ToString("00")];
        for(int i=0;i<5;i++)catalog.eyeColors[i].icon=sprites["eye-"+(i+1).ToString("00")];
        EditorUtility.SetDirty(catalog);
    }
    static Sprite OriginalEquipmentIcon(int number)
    {
        string root=P09+"/Scenes/DemoScene_Data/Icons_Equipment/";
        string folder="Armor/Female_Armor_"+number.ToString("00")+"/icon_P09_Fem_Armor_"+number.ToString("000")+"_Chest";
        return AssetDatabase.LoadAssetAtPath<Sprite>(root+"1024/"+folder+"_L.png")??AssetDatabase.LoadAssetAtPath<Sprite>(root+"256/"+folder+".png")??throw new InvalidOperationException("원본 장비 아이콘 없음: "+number);
    }
}
