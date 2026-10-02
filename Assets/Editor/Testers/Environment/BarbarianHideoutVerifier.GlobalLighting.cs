using System.Reflection;
using Overburst.Persistence;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static partial class BarbarianHideoutVerifier
{
    static void BeginLightingRun()
    {
        var account = AccountGameplaySession.Current;
        var definition = Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
        var registry = (AccountContentRegistry)typeof(AccountGameplaySession).GetProperty("ContentRegistry",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(account);
        var map = new MapInstanceState {mapContentId = registry.IdFor(definition), level = 1,
            grade = ItemGrade.Common, monsterThemeId = "CavernMutants"};
        Check(PersistentSceneFlow.Instance.EnterDebugRun(DiamondDungeonWorld.SceneName, map),
            "Map lighting real dungeon entry: " + PersistentSceneFlow.Instance.RunEntryError);
        Phase = 30; Deadline();
    }

    static bool TickLightingRun()
    {
        if (Phase == 30)
        {
            if (PersistentSceneFlow.Instance.IsSwitching || WorldSessionState.Phase != WorldPhase.Run) return true;
            var scene = SceneManager.GetSceneByName(DiamondDungeonWorld.SceneName);
            Check(scene.isLoaded && SceneManager.GetActiveScene() == scene, "Actual dungeon loaded with map lighting");
            var world = Object.FindFirstObjectByType<DiamondDungeonWorld>();
            foreach (var field in world.Fields) field.enabled = false;
            foreach (var item in world.EventDirector.Events) item.enabled = false;
            Actor.Health.SetMaxHp(1000000, true);
            VerifyMapLighting(scene);
            var driver = Object.FindFirstObjectByType<RunLifetimeDriver>();
            Check(driver != null, "Product run return driver ready");
            driver.RequestAbandon(); Phase = 31; Deadline(); return true;
        }
        if (Phase == 31)
        {
            if (!Ready) return true;
            Check(!SceneManager.GetSceneByName(DiamondDungeonWorld.SceneName).isLoaded,
                "Dungeon return restores Hideout map lighting");
            SceneManager.SetActiveScene(SceneManager.GetSceneByName(PersistentSceneFlow.PersistentSceneName));
            SceneManager.UnloadSceneAsync(PersistentSceneFlow.HideoutSceneName);
            Phase = 5; Deadline(); return true;
        }
        return false;
    }
}
