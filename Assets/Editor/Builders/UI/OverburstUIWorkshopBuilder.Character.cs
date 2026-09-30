using System.Linq;
using UnityEngine;
using UnityEditor;
using Object=UnityEngine.Object;
public static partial class OverburstUIWorkshopBuilder
{
    private const string CharacterPreviewPath=Root+"/PF_OverburstCharacterPreviewModel.prefab";
    private static GameObject characterPreview;
    private static void BuildCharacterPreviewModel(){
        var player=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab");
        var source=player.transform.Find("VisualRoot/ModelInstance_P09");
        var holder=new GameObject("Inactive Authoring Parent");holder.SetActive(false);var model=Object.Instantiate(source.gameObject,holder.transform,false);model.name="PF_OverburstCharacterPreviewModel";
        foreach(var component in model.GetComponentsInChildren<MonoBehaviour>(true))Object.DestroyImmediate(component);
        foreach(var component in model.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(component);
        foreach(var component in model.GetComponentsInChildren<Rigidbody>(true))Object.DestroyImmediate(component);
        var animator=model.GetComponent<Animator>();if(animator){animator.runtimeAnimatorController=null;animator.applyRootMotion=false;}
        model.transform.localPosition=Vector3.zero;model.transform.localRotation=Quaternion.identity;model.transform.localScale=Vector3.one;
        characterPreview=PrefabUtility.SaveAsPrefabAsset(model,CharacterPreviewPath);Object.DestroyImmediate(holder);
    }
}
