using System;
using Overburst.Quests;
using UnityEditor;
using UnityEngine;

public static class QuestMerchantContentBuilder
{
    public const string Folder = "Assets/ProjectOverburst/Resources/Quests";
    [MenuItem("OVERBURST/UI/상인 의뢰 콘텐츠 생성")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Use idle EditMode.");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/ProjectOverburst/Resources", "Quests");
        var hunt = Get("FirstHunt");
        hunt.id = "quest.main-town.weapon.first-hunt"; hunt.version = 1; hunt.sortOrder = 0;
        hunt.title = "첫 사냥"; hunt.giverMerchantId = hunt.turnInMerchantId = "merchant.main-town.weapon";
        hunt.prerequisiteId = null;
        hunt.description = "마을 밖 길목에 몬스터가 나타나 상단의 왕래가 끊기고 있네. 마을을 오가는 길부터 정리해야겠어. 자네가 이 일을 맡아줄 수 있겠나?\n\n야외 사냥터에서 몬스터 열 마리를 처치하고 돌아오게. 목표를 달성한 뒤 이 대장간에서 보고하게. 자네가 수고한 만큼 약속한 보상을 준비해 두겠네.";
        hunt.journalDescription = "마을 밖 길목에 몬스터가 나타나 상단의 왕래가 끊기고 있습니다. 무기상인은 주변을 정리할 모험가를 찾고 있습니다. 마을을 오가는 길을 다시 열기 위한 첫 의뢰입니다.\n\n야외 사냥터에서 몬스터 열 마리를 처치하세요. 목표를 달성한 뒤 메인 마을의 무기상인에게 돌아가 보고하면 약속한 보상을 받을 수 있습니다.";
        hunt.readyDescription = "돌아왔군. 길목을 정리해줘서 고맙네. 몬스터 열 마리를 모두 처치한 것을 확인했네.\n\n자네가 수고한 만큼 약속한 보상을 준비해 두었네. 의뢰를 보고하고 받아가게. 다음 모험을 준비하는 데 도움이 될 걸세.";
        hunt.completedDescription = "잘 해냈네. 자네 덕에 마을 밖 길이 한결 안전해졌어. 상단도 다시 이 길을 이용할 수 있겠군.\n\n약속한 보상은 모두 건네주었네. 다음에는 지도에 대해 이야기해보세. 야외에서 발견한 지도로 새로운 사냥터를 찾을 수 있을 걸세.";
        hunt.objectives = new[] { Objective(QuestObjectiveKind.KillEnemy, "enemy.any-run", "야외 몬스터 처치", 10) };
        hunt.rewardGold = 100; hunt.rewardExperience = 30; hunt.rewardReputation = 10;
        var map = Get("FirstMap");
        map.id = "quest.main-town.weapon.first-map"; map.version = 1; map.sortOrder = 1;
        map.title = "미지의 지도"; map.giverMerchantId = map.turnInMerchantId = hunt.giverMerchantId; map.prerequisiteId = hunt.id;
        map.description = "야외에서 지도를 찾거든 마을의 던전 포탈에 넣어보게. 지도에 새겨진 장소로 가는 길이 열릴 걸세.\n\n지도마다 향하는 사냥터가 다르네. 포탈에서 지도의 정보를 살펴보고 입장하게. 길을 열 때에는 선택한 지도 한 장이 필요하네.";
        map.journalDescription = "야외 사냥터에서 발견한 지도에는 아직 가보지 못한 장소가 담겨 있습니다. 메인 마을의 던전 포탈에서 지도를 사용할 수 있습니다.\n\n포탈에 지도를 넣고 사냥터 정보를 확인하세요. 입장하면 선택한 지도 한 장을 소모하며, 새로운 던전으로 이동합니다.";
        map.readyDescription = "지도에 새겨진 장소까지 다녀왔군. 새로운 길을 직접 확인한 모험가는 흔하지 않네.\n\n약속한 보상을 준비해 두었네. 다음 모험을 위한 장비를 점검하고 가게.";
        map.completedDescription = "지도를 따라 새로운 사냥터까지 확인했군. 앞으로도 발견한 지도는 포탈에서 살펴보게.\n\n약속한 보상은 모두 건네주었네. 다음 모험에서도 무사히 돌아오게.";
        map.objectives = new[] { Objective(QuestObjectiveKind.AcquireMap, "map.any", "지도 획득", 1),
            Objective(QuestObjectiveKind.SelectMap, "portal.map", "던전 포탈에 지도 넣기", 1),
            Objective(QuestObjectiveKind.EnterRun, "run.map", "지도 던전으로 입장", 1) };
        map.rewardGold = 150; map.rewardExperience = 40; map.rewardReputation = 10;
        QuestCatalog.Validate(new[] { hunt, map });
        EditorUtility.SetDirty(hunt); EditorUtility.SetDirty(map);
        AssetDatabase.SaveAssetIfDirty(hunt); AssetDatabase.SaveAssetIfDirty(map);
        QuestCatalog.Reset();
    }
    private static QuestDefinition Get(string name)
    {
        string path = Folder + "/" + name + ".asset";
        var q = AssetDatabase.LoadAssetAtPath<QuestDefinition>(path);
        if (q) return q;
        q = ScriptableObject.CreateInstance<QuestDefinition>(); AssetDatabase.CreateAsset(q, path); return q;
    }
    private static QuestObjectiveDefinition Objective(QuestObjectiveKind kind, string target, string label, int required) =>
        new QuestObjectiveDefinition { kind = kind, targetId = target, label = label, required = required };
}
