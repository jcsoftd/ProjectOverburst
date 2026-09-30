using System;
using System.IO;
using System.Linq;
using MoreMountains.Tools;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Local authoring only. Invoke after obtaining ownership of the shared Editor.</summary>
public static class OverburstRunUiBuilder
{
    private const string Root = "Assets/ProjectOverburst/Resources/OverburstUI/Run";
    private const string Vendor = "Assets/ThirdParty/RPG and MMO UI 11/";
    [MenuItem("OVERBURST/UI/Build GOAL 4 Run UI")]
    public static void Build()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run UI authoring requires Edit Mode.");
        // 2026-10-01: 런 UI 프리팹은 만든 뒤 효과음 연결(24b4ecc) 등 조정값이 원본이다. 이미 있으면 다시 만들지 않는다.
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/PF_OverburstRunUI.prefab") != null)
            throw new InvalidOperationException("Build GOAL 4 Run UI: run UI prefabs already exist and were tuned afterwards (layout, SFX links). Delete them first to rebuild.");
        var source = Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../개인파일/코덱스산출/Design/20260925_RunCardVisuals"));
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/ProjectOverburst/Resources/UI/Fonts/ProjectMT/FontAssets/TMP_SpoqaHanSansNeo_Body.asset");
        var vendorWindow = AssetDatabase.LoadAssetAtPath<GameObject>(Vendor + "Prefabs/Windows/Window (Inventory).prefab");
        var border = vendorWindow.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.name == "Borders" && i.sprite != null);
        var button = AssetDatabase.LoadAssetAtPath<Sprite>(Vendor + "Textures/Controls/Buttons/Rectangular/Button_RS_Background.png");
        var glow = AssetDatabase.LoadAssetAtPath<Sprite>(Vendor + "Textures/Windows/Dialog/Dialog_Reward_Glow.png");
        var sound = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath("86aa66a501648034f8ec69ba5d404514"));
        if (font == null || border == null || button == null || glow == null || sound == null)
            throw new InvalidOperationException("Required UI11 / Feel / Korean font dependency is missing.");
        Directory.CreateDirectory(Root + "/Art");
        foreach (string name in new[] { "card-front", "card-back", "grade-gem-neutral" })
        {
            var target = Root + "/Art/" + name + ".png";
            if (!File.Exists(target)) File.Copy(Path.Combine(source, name + ".png"), target);
            AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(target);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
            importer.maxTextureSize = 2048; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        var preview = EditorSceneManager.NewPreviewScene();
        GameObject cardRoot = null, uiRoot = null;
        try
        {
            var shellAsset=BuildShell(preview);
            var buttonAsset=BuildActionButton(preview);
            var slotAsset=BuildDropSlot(preview);
            BuildIcons();
            cardRoot = new GameObject("PF_OverburstRunCard", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(cardRoot, preview);
            cardRoot.SetActive(false);
            ((RectTransform)cardRoot.transform).sizeDelta = new Vector2(336, 504);
            var pivot = RunUiLayout.Rect(cardRoot.transform, "Flip", 0, 0, 336, 504);
            var front = RunUiLayout.Rect(pivot, "Front", 0, 0, 336, 504);
            var back = RunUiLayout.Rect(pivot, "Back", 0, 0, 336, 504);
            back.localScale = new Vector3(-1, 1, 1);
            RunUiLayout.Image(back, "CardBack", Art("card-back"), Color.white, 0, 0, 336, 504);
            RunUiLayout.Image(front, "CardFront", Art("card-front"), Color.white, 0, 0, 336, 504);
            var halo = RunUiLayout.Image(front, "GradeGlow", Art("grade-gem-neutral"), Color.clear, 0, 207, 66, 66);
            var gem = RunUiLayout.Image(front, "GradeGem", Art("grade-gem-neutral"), Color.white, 0, 207, 51, 51);
            var grade = RunUiLayout.Text(front, "Grade", "등급", font, 0, 163, 240, 30, 18, RunUiLayout.Gold);
            var title = RunUiLayout.Text(front, "Title", "카드 제목", font, 0, 15, 272, 48, 24, RunUiLayout.Ivory);
            var slotSource=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/Slots/PF_OverburstItemSlot_Rpg11.prefab");
            var slotBack=slotSource.transform.Find("Vendor Frame").GetComponent<Image>().sprite;
            var slotEdge=slotSource.transform.Find("Vendor Frame/Borders").GetComponent<Image>().sprite;
            RunUiLayout.Image(front,"IconSocket",slotBack,Color.white,0,93,98,98,true);
            var icon = RunUiLayout.Image(front,"Icon",null,Color.white,0,93,78,78);
            RunUiLayout.Image(front,"IconSocketFrame",slotEdge,Color.white,0,93,98,98,true);
            var body = RunUiLayout.Text(front, "Description", "효과 설명", font, 0, -114, 260, 68, 20, RunUiLayout.Ivory);
            var value = RunUiLayout.Text(front, "Value", "+25%", font, 0, -43, 266, 54, 28, RunUiLayout.Gold);
            var duration = RunUiLayout.Text(front, "Duration", "이번 던전이 끝날 때까지", font, 0, -161, 260, 28, 17, RunUiLayout.Ivory);
            RunUiLayout.Image(front,"EffectRule",null,new Color(.50f,.37f,.23f,.8f),0,-78,224,1);
            var selectObject=Object.Instantiate(buttonAsset,front,false);selectObject.name="Select";
            var selectRect=(RectTransform)selectObject.transform;selectRect.anchorMin=selectRect.anchorMax=selectRect.pivot=new Vector2(.5f,.5f);
            selectRect.anchoredPosition=new Vector2(0,-199);selectRect.sizeDelta=new Vector2(420,76);selectRect.localScale=Vector3.one*.5f;
            var select=selectObject.GetComponent<Button>();selectObject.GetComponentInChildren<Text>(true).text="선택";
            var sides = pivot.gameObject.AddComponent<MMTwoSidedUI>();
            sides.Front = front.gameObject; sides.Back = back.gameObject; sides.FlipAxis = MMTwoSidedUI.Axis.x;
            sides.ScaleThreshold = 0; sides.BackVisible = true;
            pivot.localScale = new Vector3(-1, 1, 1); front.gameObject.SetActive(false);
            var card = cardRoot.AddComponent<OverburstRunCardView>();
            card.Configure(pivot, front.gameObject, back.gameObject, title, body, value, grade, duration,
                gem, icon, halo, select, sound);
            cardRoot.SetActive(true);
            var cardAsset = PrefabUtility.SaveAsPrefabAsset(cardRoot, Root + "/PF_OverburstRunCard.prefab");
            uiRoot = new GameObject("PF_OverburstRunUI", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(OverburstRunUi));
            SceneManager.MoveGameObjectToScene(uiRoot, preview);
            uiRoot.GetComponent<OverburstRunUi>().Configure(font, border.sprite, button,
                cardAsset.GetComponent<OverburstRunCardView>(),shellAsset,buttonAsset,slotAsset);
            PrefabUtility.SaveAsPrefabAsset(uiRoot, Root + "/PF_OverburstRunUI.prefab");
            Debug.Log("GOAL 4 UI prefabs authored. Existing scenes/HUD/stash were not saved. Play validation remains required.");
        }
        finally
        {
            if (cardRoot != null) Object.DestroyImmediate(cardRoot);
            if (uiRoot != null) Object.DestroyImmediate(uiRoot);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }
    private static void PresentationOnly(GameObject go)
    {
        foreach(var scheme in go.GetComponentsInChildren<DuloGames.UI.ColorSchemeElement>(true))scheme.Apply(new Color(.52f,.14f,.12f));
        foreach(var component in go.GetComponentsInChildren<MonoBehaviour>(true))
            if(component!=null && !(component is Image) && !(component is Text) && !(component is Button) && component.GetType().Name!="UIFlippable")
                Object.DestroyImmediate(component);
        foreach(var button in go.GetComponentsInChildren<Button>(true))button.onClick=new Button.ButtonClickedEvent();
        foreach(var image in go.GetComponentsInChildren<Image>(true))
        { image.raycastTarget=false;if(image.name.Contains("Hover")||image.name.Contains("Press"))image.color=Color.clear; }
    }
    private static GameObject BuildShell(Scene preview)
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstInventory_Rpg11.prefab");
        var go=Object.Instantiate(source);SceneManager.MoveGameObjectToScene(go,preview);go.name="PF_RunTransferShell";
        foreach(Transform child in go.transform.Cast<Transform>().ToArray())
            if(child.name!="Header"&&child.name!="Borders"&&child.name!="Opaque Content Surface")Object.DestroyImmediate(child.gameObject);
        PresentationOnly(go);
        var root=(RectTransform)go.transform;root.anchorMin=root.anchorMax=root.pivot=new Vector2(.5f,.5f);
        root.sizeDelta=new Vector2(904,864);root.anchoredPosition=Vector2.zero;root.localScale=Vector3.one*.5f;
        var body=(RectTransform)go.transform.Find("Opaque Content Surface");body.anchorMin=Vector2.zero;body.anchorMax=Vector2.one;
        body.offsetMin=new Vector2(10,10);body.offsetMax=new Vector2(-10,-146);
        body.GetComponent<Image>().color=new Color(.047f,.043f,.040f,1);body.GetComponent<Image>().raycastTarget=true;
        var text=go.transform.Find("Header/Text").GetComponent<Text>();text.text="창고 전송";
        var tr=text.rectTransform;tr.anchorMin=new Vector2(0,1);tr.anchorMax=new Vector2(1,1);tr.pivot=new Vector2(.5f,1);
        tr.anchoredPosition=new Vector2(0,-28);tr.sizeDelta=new Vector2(-160,88);
        var close=(RectTransform)go.transform.Find("Header/Button (Close)");close.anchoredPosition=new Vector2(788,-40);
        close.GetComponent<Image>().raycastTarget=true;
        var asset=PrefabUtility.SaveAsPrefabAsset(go,Root+"/PF_RunTransferShell.prefab");Object.DestroyImmediate(go);return asset;
    }
    private static GameObject BuildActionButton(Scene preview)
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(Vendor+"Prefabs/Controls/Buttons/Rectangular/Button (Normal M).prefab");
        var go=Object.Instantiate(source);SceneManager.MoveGameObjectToScene(go,preview);go.name="PF_RunActionButton";PresentationOnly(go);
        var text=go.GetComponentInChildren<Text>(true);
        text.font=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstInventory_Rpg11.prefab").transform.Find("Header/Text").GetComponent<Text>().font;
        text.fontSize=36;text.color=RunUiLayout.Ivory;text.text="전송";
        text.horizontalOverflow=HorizontalWrapMode.Overflow;text.verticalOverflow=VerticalWrapMode.Overflow;
        var tr=text.rectTransform;tr.anchorMin=Vector2.zero;tr.anchorMax=Vector2.one;tr.offsetMin=tr.offsetMax=Vector2.zero;tr.pivot=new Vector2(.5f,.5f);text.alignment=TextAnchor.MiddleCenter;
        foreach(var image in go.GetComponentsInChildren<Image>(true))
        {
            var r=image.rectTransform;
            if(image.name.StartsWith("Ornament"))continue;
            r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
            image.type=Image.Type.Sliced;image.pixelsPerUnitMultiplier=2f;
        }
        text.transform.SetAsLastSibling();
        var button=go.GetComponent<Button>();button.targetGraphic=go.transform.Find("Foreground").GetComponent<Image>();button.targetGraphic.raycastTarget=true;
        var asset=PrefabUtility.SaveAsPrefabAsset(go,Root+"/PF_RunActionButton.prefab");Object.DestroyImmediate(go);return asset;
    }
    private static GameObject BuildDropSlot(Scene preview)
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/Slots/PF_OverburstItemSlot_Rpg11.prefab");
        var go=Object.Instantiate(source);SceneManager.MoveGameObjectToScene(go,preview);go.name="PF_RunTransferSlot";PresentationOnly(go);
        foreach(Transform child in go.transform.Cast<Transform>().ToArray())
            if(child.name!="Vendor Frame")Object.DestroyImmediate(child.gameObject);
        var frame=go.transform.Find("Vendor Frame");
        foreach(Transform child in frame.Cast<Transform>().ToArray())if(child.name!="Borders")Object.DestroyImmediate(child.gameObject);
        var frameRect=(RectTransform)frame;frameRect.anchorMin=Vector2.zero;frameRect.anchorMax=Vector2.one;frameRect.offsetMin=frameRect.offsetMax=Vector2.zero;frameRect.localScale=Vector3.one;
        var asset=PrefabUtility.SaveAsPrefabAsset(go,Root+"/PF_RunTransferSlot.prefab");Object.DestroyImmediate(go);return asset;
    }
    private static void BuildIcons()
    {
        var set=AssetDatabase.LoadAssetAtPath<RunCardIconSet>(Root+"/CardIcons.asset");
        bool fresh=set==null;if(fresh)set=ScriptableObject.CreateInstance<RunCardIconSet>();
        string[] keys={"buff_MaxHealth","buff_Armor","buff_Attack","buff_ElementalDamage","buff_AttackSpeed","buff_MoveSpeed","buff_ItemDrop","buff_ExperienceGain","loot_chest","experience","transfer_object"};
        string[] titles={"생명의 각인","강철의 각인","파괴의 각인","원소의 각인","질풍의 각인","순풍의 각인","수확의 각인","지혜의 각인","봉인된 전리품","기억의 파편","안전한 전송"};
        string[] icons={"Leafs","Shield","Sword","Pyroblast","Arrows","Arrows","Sunshiny","Book","Sunshiny","Book","Shield"};
        string[] descriptions={"최대 체력이 증가합니다.","받는 피해를 줄입니다.","공격력이 증가합니다.","원소로 가하는 피해가 증가합니다.","공격을 더 빠르게 이어갑니다.","더 빠르게 이동합니다.","몬스터에게서 아이템을 얻을 확률이 증가합니다.","획득하는 경험치가 증가합니다.","좋은 장비가 든 상자를 소환합니다.","즉시 얻은 경험치는 사망해도 유지됩니다.","아이템 스택 하나를 창고로 전송할 수 있습니다."};
        set.Entries=Enumerable.Range(0,keys.Length).Select(i=>new RunCardIconSet.Entry{Key=keys[i],Title=titles[i],Description=descriptions[i],Icon=AssetDatabase.LoadAssetAtPath<Sprite>(Vendor+"Textures/Spell & Item Icons/Icon_"+icons[i]+"_128.png")}).ToArray();
        if(fresh)AssetDatabase.CreateAsset(set,Root+"/CardIcons.asset");else{EditorUtility.SetDirty(set);AssetDatabase.SaveAssetIfDirty(set);}
    }    private static Sprite Art(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/Art/" + name + ".png");
}






