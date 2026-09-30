using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MonsterThemeShowcaseBuilder
{
    [MenuItem("OVERBURST/Showcase/Add Spawn Theme Proposals")]
    public static void Create()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MonsterVol2ShowcaseBuilder.ScenePath || scene.isDirty)
            throw new InvalidOperationException("Open the clean MonsterVol2_Showcase scene in Edit Mode first.");
        if (UnityEngine.Object.FindFirstObjectByType<MonsterThemeShowcase>() != null)
            throw new InvalidOperationException("Theme exhibit already exists; do not overwrite it.");
        var catalog = UnityEngine.Object.FindFirstObjectByType<MonsterShowcaseGallery>();
        if (catalog == null || catalog.actors.Length != 47) throw new InvalidOperationException("Expected the 47-model catalog.");
        var owner = new GameObject("Spawn Theme Proposals").AddComponent<MonsterThemeShowcase>();
        owner.catalog = catalog;
        owner.catalogUI = catalog.title.canvas.gameObject;
        owner.catalogFloor = GameObject.Find("Gallery floor");
        var definitions = new[] {
            Definition("거미 부화 군락", "둥근 유충 → 가시 거미 → 거대 모체. 가장 자연스러운 성장 계열.", "1순위 추천. Carcinoptera는 지상 클립 기준. 알 #30은 전투원 대신 부화 장치 후보.", new[]{31,17}, new[]{34,3},29),
            Definition("독낭 외골격 군락", "주황색 독낭과 마른 다리 실루엣 통일. Kupolojuve와 Kupolobrach는 유체·성체 조합.", "1순위 추천. 독·투사체 역할은 외형/클립 기반 제안이며 실제 공격 데이터는 미연결.", new[]{38,40},new[]{1,24},21),
            Definition("가시갑주 돌격대", "회색 갑피와 등 가시 중심. Crustaspikan 유충에서 성체 정예로 이어지는 무거운 집단.", "Crustaspikan은 큰 폭으로 축소한 제안. 공격 범위·회전 반경은 별도 제작 때 조정.",new[]{7},new[]{8,44},6),
            Definition("포자·부패 공생군", "버섯·부푼 연조직·촉수 얼굴의 공생 집단. 창백하고 녹색인 육질을 기준으로 묶음.", "조건부 후보. 소형 둘은 부유 계열이므로 저공 이동과 지상 피격 높이 설계가 필요.",new[]{11,15},new[]{4,26},5),
            Definition("원시 포식자 무리", "사냥개형 선봉, 직립 파충류, 육중한 사족 포식자. 지상 추격 중심으로 쓰기 쉬운 조합.", "1순위 추천. 서로 다른 종의 사냥 무리 테마. 같은 종의 성장 계보를 의미하지 않음.",new[]{2},new[]{8,42},27),
            Definition("심연·조간대 포식군", "어류·가오리·두족류와 긴 목의 정예. 수변이나 침수 구역에 어울리는 실루엣.", "조건부 후보. 수영·비행 클립이 섞여 있어 일반 지상 군집에 바로 투입하지 않음.",new[]{25,13},new[]{9,28},36),
            Definition("청결정 동굴 군락", "짙은 갑피의 벌레 무리 속에 푸른 결정 정예가 드러나는 색 대비 중심 조합.", "거미 세트의 대체 팔레트 후보. Fulgurodonte의 결정은 속성 공격 구현을 뜻하지 않음.",new[]{41},new[]{33,3},10)
        };
        owner.sets = definitions.Select(d => d.set).ToArray();
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        string materialRoot = "Assets/ProjectOverburst/03_Features/Enemies/Showcase/Materials/";
        var floor = AssetDatabase.LoadAssetAtPath<Material>(materialRoot + "Floor.mat");
        var baseMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialRoot + "Plinth.mat");
        var reference = AssetDatabase.LoadAssetAtPath<Material>(materialRoot + "ScaleReference.mat");
        float startX = catalog.actors.Max(a => a.transform.position.x + a.displaySize.x * .5f) + 38;
        for (int s = 0; s < definitions.Length; s++)
        {
            var def = definitions[s];
            var root = new GameObject($"THEME_{s+1:00}_{def.set.title}").transform;
            root.SetParent(owner.transform);
            root.position = new Vector3(startX + (s % 2) * 32, 0, (s / 2) * 24);
            def.set.root = root;
            Primitive("Theme floor", PrimitiveType.Cube, root, new Vector3(0,-.3f,0), new Vector3(25,.25f,16), floor);
            var members = new List<MonsterThemeShowcase.Member>();
            for (int tier = 0; tier < 3; tier++)
            {
                int[] ids = tier == 0 ? def.small : tier == 1 ? def.medium : new[]{def.elite};
                for (int j = 0; j < ids.Length; j++)
                {
                    int index = ids[j] - 1;
                    var original = catalog.actors[index];
                    float footprint = tier == 0 ? 1.45f : tier == 1 ? 2.6f : 4.9f;
                    float height = tier == 0 ? 1.15f : tier == 1 ? 2.15f : 3.6f;
                    float scale = Mathf.Min(footprint / Mathf.Max(original.displaySize.x,original.displaySize.z), height/original.displaySize.y);
                    var station = new GameObject($"{new[]{"SMALL","MEDIUM","ELITE"}[tier]}_{ids[j]:00}_{original.displayName}").transform;
                    station.SetParent(root,false);
                    station.localPosition = new Vector3((1-tier)*7, 0, (j-(ids.Length-1)*.5f)*4.5f);
                    var scaled = new GameObject("Proposed scale (exhibit only)").transform;
                    scaled.SetParent(station,false); scaled.localScale = Vector3.one * scale;
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(original.sourcePath),scaled);
                    instance.transform.localPosition = original.transform.GetChild(0).localPosition;
                    instance.transform.localRotation = original.transform.GetChild(0).localRotation;
                    instance.transform.localScale = original.transform.GetChild(0).localScale;
                    foreach (var rb in instance.GetComponentsInChildren<Rigidbody>(true)) {rb.isKinematic=true;rb.useGravity=false;}
                    foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled=false;
                    var actor = station.gameObject.AddComponent<MonsterShowcaseActor>();
                    actor.displayName = original.displayName; actor.sourcePath=original.sourcePath;
                    actor.animator=instance.GetComponentInChildren<Animator>(true);actor.clips=original.clips;
                    actor.automatic=false;actor.displaySize=original.displaySize*scale;
                    actor.focusPoint=station.position+Vector3.up*actor.displaySize.y*.5f;
                    Primitive("Display base",PrimitiveType.Cylinder,station,new Vector3(0,-.08f,0),new Vector3(footprint+1,.08f,footprint+1),baseMaterial);
                    var label = new GameObject("Member label").AddComponent<TextMesh>();
                    label.transform.SetParent(station,false);label.transform.localPosition=Vector3.up*(actor.displaySize.y+.6f);
                    label.text=$"#{ids[j]:00} {original.displayName}";label.font=font;label.fontSize=50;label.characterSize=.045f;
                    label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;
                    label.GetComponent<MeshRenderer>().sharedMaterial=font.material;actor.nameplate=label;
                    members.Add(new MonsterThemeShowcase.Member{catalogIndex=index,tier=tier,scale=scale,actor=actor});
                }
                var tierLabel = new GameObject("Tier label").AddComponent<TextMesh>();
                tierLabel.transform.SetParent(root,false);tierLabel.transform.localPosition=new Vector3((1-tier)*7,.1f,5.5f);
                tierLabel.text=new[]{"SMALL  /  SWARM","MEDIUM  /  FEW","ELITE  /  RARE"}[tier];
                tierLabel.font=font;tierLabel.fontSize=55;tierLabel.characterSize=.06f;tierLabel.anchor=TextAnchor.MiddleCenter;
                tierLabel.GetComponent<MeshRenderer>().sharedMaterial=font.material;
            }
            Primitive("1.8 m human reference",PrimitiveType.Capsule,root,new Vector3(3.5f,.9f,0),new Vector3(.45f,.9f,.45f),reference);
            def.set.members=members.ToArray();
        }
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject=owner.gameObject;
        SceneView.lastActiveSceneView?.Frame(new Bounds(owner.sets[0].root.position,new Vector3(27,8,18)),false);
        Debug.Log($"[MonsterThemes] Created {owner.sets.Length} proposals / {owner.sets.Sum(s=>s.members.Length)} representatives. Gameplay unchanged.");
    }
    private sealed class DefinitionData { public MonsterThemeShowcase.Set set; public int[] small,medium; public int elite; }
    private static DefinitionData Definition(string title,string reason,string caution,int[] small,int[] medium,int elite)
        => new DefinitionData {set=new MonsterThemeShowcase.Set{title=title,reason=reason,caution=caution},small=small,medium=medium,elite=elite};
    private static void Primitive(string name,PrimitiveType type,Transform parent,Vector3 position,Vector3 scale,Material material)
    {
        var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;
        go.GetComponent<Renderer>().sharedMaterial=material;UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
    }
}
