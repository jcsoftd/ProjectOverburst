using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class OverburstMinimapMigration
{
    public const string ScenePath = "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity";
    public const string AssetRoot = "Assets/ProjectOverburst/Resources/UI/Minimap";

    [MenuItem("OVERBURST/UI/Migrate Circular Minimap")]
    public static void Run() => Debug.Log(Apply());

    public static string Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before minimap migration.");
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        WorldMinimapController controller = null;
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            controller = go.GetComponentInChildren<WorldMinimapController>(true);
            if (controller != null) break;
        }
        if (controller == null) throw new InvalidOperationException("Authored WorldMinimapController missing.");
        if (controller.View != null && controller.View.IsReady) return "NO_CHANGE";

        Transform root = controller.transform;
        string[] owned = { "MinimapBackground", "MinimapMask", "MinimapFrame", "MinimapZoomInButton", "MinimapZoomOutButton", "MinimapZoomSizeText" };
        for (int i = 0; i < root.childCount; i++)
            if (Array.IndexOf(owned, root.GetChild(i).name) < 0)
                throw new InvalidOperationException("Unexpected minimap child: " + root.GetChild(i).name);
        TMP_Text oldText = root.GetComponentInChildren<TMP_Text>(true);
        TMP_FontAsset font = oldText != null ? oldText.font : TMP_Settings.defaultFontAsset;
        if (font == null) throw new InvalidOperationException("Minimap font is missing.");
        EnsureFolder(AssetRoot);
        Sprite circle = CreateSprite("Circle", 128, 128, (x, y) => Smooth(0.985f - Mathf.Sqrt(x * x + y * y), 0.025f));
        Sprite ring = CreateSprite("Frame", 256, 256, (x, y) => Smooth(0.012f - Mathf.Abs(Mathf.Sqrt(x*x+y*y) - 0.98f), 0.009f));
        Sprite triangle = CreateSprite("Player", 64, 64, (x, y) => Smooth(Mathf.Min(y + 0.7f, Mathf.Min(0.88f-y, (0.88f-y)*0.48f-Mathf.Abs(x))), 0.045f));
        Sprite cone = CreateSprite("Facing", 128, 128, (x, y) =>
        {
            float length = Mathf.Sqrt(x*x + y*y);
            return Smooth(Mathf.Min(0.92f-length, y-Mathf.Abs(x)*1.3f), 0.04f) * Mathf.Clamp01(1f-length);
        });
        Texture2D atlas = CreateAtlas();
        for (int i = root.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.GetChild(i).gameObject);

        RectTransform viewRoot = Rect("MinimapView", root, Vector2.zero, new Vector2(240,240));
        Image background = Image("CircleBackground", viewRoot, circle, new Color(0.025f,0.035f,0.045f,0.88f), new Vector2(240,240));
        RectTransform viewport = Rect("CircleViewport", viewRoot, Vector2.zero, new Vector2(232,232));
        Image maskImage = viewport.gameObject.AddComponent<Image>();
        maskImage.sprite = circle; maskImage.raycastTarget = false;
        Mask mask = viewport.gameObject.AddComponent<Mask>(); mask.showMaskGraphic = false;
        RectTransform enemyRoot = Rect("EnemyMarkers", viewport, Vector2.zero, new Vector2(232,232));
        MinimapMarkerGraphic graphic = enemyRoot.gameObject.AddComponent<MinimapMarkerGraphic>();
        graphic.raycastTarget = false;
        Set(graphic, "atlas", atlas);
        Image facing = Image("FacingCone", viewport, cone, new Color(0.68f,0.89f,0.94f,0.35f), new Vector2(44,44));
        Image player = Image("PlayerMarker", viewport, triangle, new Color(0.88f,0.98f,1f,1f), new Vector2(12,12));
        Image("CircleFrame", viewRoot, ring, new Color(0.63f,0.7f,0.73f,0.9f), new Vector2(240,240));
        RectTransform controls = Rect("ZoomControls", viewRoot, new Vector2(0,-139), new Vector2(112,30));
        Button zoomOut = Button("ZoomOut", controls, new Vector2(-42,0), "−", circle, font);
        Button zoomIn = Button("ZoomIn", controls, new Vector2(42,0), "+", circle, font);
        TextMeshProUGUI zoomText = Text("ZoomValue", controls, Vector2.zero, new Vector2(50,24), "x1.0", font, 12);
        MinimapView view = root.GetComponent<MinimapView>();
        if (view == null) view = root.gameObject.AddComponent<MinimapView>();
        Set(view, "viewRoot", viewRoot.gameObject); Set(view, "viewport", viewport); Set(view, "markers", graphic);
        Set(view, "playerMarker", player.rectTransform); Set(view, "facingCone", facing.rectTransform);
        Set(view, "minimapCanvas", root.GetComponent<Canvas>()); Set(view, "raycaster", root.GetComponent<GraphicRaycaster>());
        Set(view, "zoomInButton", zoomIn); Set(view, "zoomOutButton", zoomOut); Set(view, "zoomValueText", zoomText);
        Set(controller, "view", view);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return "APPLIED";
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f,0.5f);
        rect.anchoredPosition = position; rect.sizeDelta = size;
        return rect;
    }

    private static Image Image(string name, Transform parent, Sprite sprite, Color color, Vector2 size)
    {
        var image = Rect(name, parent, Vector2.zero, size).gameObject.AddComponent<Image>();
        image.sprite = sprite; image.color = color; image.raycastTarget = false;
        return image;
    }

    private static Button Button(string name, Transform parent, Vector2 position, string label, Sprite sprite, TMP_FontAsset font)
    {
        Image image = Image(name, parent, sprite, new Color(0.10f,0.14f,0.17f,0.96f), new Vector2(28,28));
        image.rectTransform.anchoredPosition = position; image.raycastTarget = true;
        Button button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        Text("Label", image.transform, Vector2.zero, new Vector2(24,24), label, font, 18);
        return button;
    }

    private static TextMeshProUGUI Text(string name, Transform parent, Vector2 position, Vector2 size, string text, TMP_FontAsset font, int fontSize)
    {
        var label = Rect(name, parent, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; label.fontSharedMaterial = font.material; label.fontSize = fontSize;
        label.text = text; label.color = new Color(0.86f,0.9f,0.92f); label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false; label.textWrappingMode = TextWrappingModes.NoWrap;
        return label;
    }

    private static void Set(Object obj, string field, Object value)
    {
        var serialized = new SerializedObject(obj);
        var property = serialized.FindProperty(field);
        if (property == null) throw new InvalidOperationException(obj.name + "." + field);
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(obj);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0,path.LastIndexOf('/'));
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent,path.Substring(path.LastIndexOf('/')+1));
    }

    private static float Smooth(float signedDistance, float width) => Mathf.SmoothStep(0,1,Mathf.Clamp01(signedDistance / width + 0.5f));

    private static Sprite CreateSprite(string name, int width, int height, Func<float,float,float> alpha)
    {
        string path = AssetRoot + "/" + name + ".png";
        var texture = new Texture2D(width,height,TextureFormat.RGBA32,false);
        var pixels = new Color32[width*height];
        for (int y=0;y<height;y++) for(int x=0;x<width;x++)
            pixels[y*width+x] = new Color32(255,255,255,(byte)Mathf.RoundToInt(alpha((x+0.5f)/width*2-1,(y+0.5f)/height*2-1)*255));
        texture.SetPixels32(pixels); texture.Apply();
        File.WriteAllBytes(path,texture.EncodeToPNG()); Object.DestroyImmediate(texture);
        Import(path,true);
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static Texture2D CreateAtlas()
    {
        string path = AssetRoot + "/Markers.png";
        const int cell=64;
        var texture = new Texture2D(cell*4,cell,TextureFormat.RGBA32,false);
        var pixels = new Color32[cell*cell*4];
        for(int y=0;y<cell;y++) for(int x=0;x<cell*4;x++)
        {
            float dx=(x%cell+0.5f)/cell*2-1, dy=(y+0.5f)/cell*2-1;
            float radius=Mathf.Sqrt(dx*dx+dy*dy), diamond=Mathf.Abs(dx)+Mathf.Abs(dy);
            int grade=x/cell;
            float a=grade==0 ? Smooth(0.78f-radius,0.065f) : grade==1 ? Smooth(0.86f-diamond,0.065f)
                : grade==2 ? Smooth(0.09f-Mathf.Abs(diamond-0.72f),0.065f)
                : Mathf.Max(Smooth(0.08f-Mathf.Abs(radius-0.76f),0.065f),Smooth(0.32f-diamond,0.065f));
            pixels[y*cell*4+x]=new Color32(255,255,255,(byte)Mathf.RoundToInt(a*255));
        }
        texture.SetPixels32(pixels); texture.Apply(); File.WriteAllBytes(path,texture.EncodeToPNG()); Object.DestroyImmediate(texture);
        Import(path,false); return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static void Import(string path,bool sprite)
    {
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
        importer.spriteImportMode=SpriteImportMode.Single;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings); settings.spriteMeshType=SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.mipmapEnabled=false; importer.alphaIsTransparency=true;
        importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.wrapMode=TextureWrapMode.Clamp; importer.filterMode=FilterMode.Bilinear;
        importer.SaveAndReimport();
    }
}
