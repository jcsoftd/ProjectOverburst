using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Centers visible artwork, independently of transparent source padding. Shared by all workshop slots.</summary>
[ExecuteAlways, RequireComponent(typeof(Image))]
public sealed class OverburstUIIconFraming : MonoBehaviour
{
    [Serializable] public struct Artwork { public Sprite sprite; public Rect bounds; }
    [SerializeField] private Artwork[] artwork;
    private Image image;
    private Sprite applied;
    public void Configure(Artwork[] values){artwork=values;Refresh();}
    private void OnEnable(){Refresh();}
    private void LateUpdate(){if(!image||applied!=image.sprite)Refresh();}
    public void Refresh(){
        if(!image)image=GetComponent<Image>();applied=image.sprite;
        var bounds=new Rect(0,0,1,1);
        if(artwork!=null)foreach(var item in artwork)if(item.sprite==applied){bounds=item.bounds;break;}
        float extent=Mathf.Max(bounds.width,bounds.height);if(extent<=0)extent=1;
        // A 68px visible-art box in the 84px slot; symmetrical 8px clearance.
        float size=68/extent;var rect=image.rectTransform;
        rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
        rect.sizeDelta=Vector2.one*size;rect.anchoredPosition=(Vector2.one*.5f-bounds.center)*size;
        image.preserveAspect=true;
    }
}
