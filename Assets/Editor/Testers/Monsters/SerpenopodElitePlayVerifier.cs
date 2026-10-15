using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;

// Reuse the established combat harness and its cleanup. This supplies the current title precondition.
[InitializeOnLoad]
public static class SerpenopodElitePlayVerifier
{
    const string Key = "Overburst.SerpenopodElite.Play.";
    static double nextSample;
    static SerpenopodElitePlayVerifier() { EditorApplication.update += Tick; }

    public static string Start(string output, bool parry)
    {
        MonsterBlenderParryR6Builder.RequireIdle();
        output = Path.GetFullPath(output);
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출/Monsters")) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output))
            throw new ArgumentException("Fresh private monster output required.");
        var rng = UnityEngine.Random.state;
        try {
            var seed = NewAccountFactory.Create(Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath));
            seed.appearance = new CharacterAppearanceSnapshot(); AccountCharacterCommands.EnsureLegacy(seed);
            new EasySaveAccountStore(Path.Combine(output, "Account")).Save(seed, seed.lastTransactionId);
            // This fixture tests combat after onboarding; avoid a level-one invitation owning combat input.
            var tutorial = new TutorialProgress { offered = true, firstCharacterId = seed.activeCharacterId };
            tutorial.offeredCharacterIds.Add(seed.activeCharacterId); tutorial.Save(Path.Combine(output, "Account"));
        } finally { UnityEngine.Random.state = rng; }
        SessionState.SetString(Key + "output", output);
        SessionState.SetBool(Key + "seen", false);
        SessionState.SetString(Key + "observation", new JObject { ["samples"] = 0, ["maxFootContacts"] = 0,
            ["eliteRank"] = true, ["independent"] = true, ["characterSelected"] = false }.ToString());
        try {
            if (parry) return MonsterWeakAttackPlayerLoopVerifier.StartRealPlayerParry(output, SerpenopodEliteBuilder.DefinitionPath);
            string plan = Path.Combine(output, "theme-test-plan.json");
            File.WriteAllText(plan, "{\"schema\":\"overburst.v3.theme-activation.v1\",\"themes\":[{\"runtimeThemeId\":\"PrimalHunt\"}]}");
            return MonsterWeakAttackPlayerLoopVerifier.StartActualThemeActivation(output, plan);
        } catch { SessionState.EraseString(Key + "output"); throw; }
    }

    static void Tick()
    {
        string output = SessionState.GetString(Key + "output", "");
        if (string.IsNullOrEmpty(output)) return;
        if (!EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetBool(Key + "seen", false)) {
            File.WriteAllText(Path.Combine(output, "elite-observation.json"), SessionState.GetString(Key + "observation", "{}"));
            SessionState.EraseString(Key + "output"); SessionState.EraseBool(Key + "seen"); SessionState.EraseString(Key + "observation");
            return;
        }
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < nextSample) return;
        nextSample = EditorApplication.timeSinceStartup + .2;
        string account = Path.GetFullPath(Path.Combine(output, "Account"));
        string active = IsolatedSavePlayGuard.ActiveDirectory;
        if (string.IsNullOrEmpty(active) || !string.Equals(Path.GetFullPath(active), account, StringComparison.OrdinalIgnoreCase)) return;
        SessionState.SetBool(Key + "seen", true);
        var observation = JObject.Parse(SessionState.GetString(Key + "observation", "{}"));
        var title = TitleCharacterPanel.Instance;
        if (title != null && !title.ReadyForGameplay) {
            if (title.Screen == "title") title.start.onClick.Invoke();
            else if (title.Screen == "characters" && !string.IsNullOrEmpty(title.SelectedId) && title.enter.interactable) {
                title.enter.onClick.Invoke(); observation["characterSelected"] = true;
            }
        }
        foreach (var actor in UnityEngine.Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None)
            .Where(a => a.IsLeased && a.Definition != null && a.Definition.EnemyId == SerpenopodEliteBuilder.Id)) {
            observation["samples"] = (int)observation["samples"] + 1;
            observation["eliteRank"] = (bool)observation["eliteRank"] && actor.GetComponent<EnemyRank>().GradeType == EnemyGradeType.Elite;
            observation["independent"] = (bool)observation["independent"] && actor.AI.SquadParticipationMode == EnemySquadParticipationMode.Independent;
            observation["maxFootContacts"] = Math.Max((int)observation["maxFootContacts"], actor.GetComponent<EnemyEliteFootstepEmitter>()?.ContactCount ?? 0);
            observation["level"] = actor.GetComponent<EnemyRank>().Level;
            observation["maxHealth"] = actor.Health.MaxHp;
            observation["gameplayInputBlocked"] = GameplayInputBlocker.IsGameplayInputBlocked;
        }
        SessionState.SetString(Key + "observation", observation.ToString());
    }
}
