using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Overburst.Persistence;
using Overburst.Quests;
using UnityEditor;
using UnityEngine;

public static class QuestMerchantCoreVerifier
{
    private const string MerchantId = "merchant.main-town.weapon";
    private static readonly List<string> passed = new List<string>();
    private static string output;

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Use idle EditMode.");
        output = Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../개인파일/코덱스산출/Implementation/20261011_QuestMerchant/Evidence/Core-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
        Directory.CreateDirectory(output); passed.Clear();
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var hunt = Make("test.first-hunt", QuestObjectiveKind.KillEnemy, "enemy.any-run", 10);
        var map = Make("test.first-map", QuestObjectiveKind.EnterRun, "run.any", 1);
        map.prerequisiteId = hunt.id;
        try
        {
            QuestCatalog.Validate(new[] { hunt, map });
            var initial = NewAccountFactory.Create(registry);
            Check(initial.quests.Count == 0 && initial.trackedQuestId == null, "Fresh account has no invented completions");
            Check(AccountQuestCommands.Availability(initial, hunt) == QuestAvailability.Available
                && AccountQuestCommands.Availability(initial, map) == QuestAvailability.Locked, "Prerequisite availability");
            Check(!AccountQuestCommands.Progress(initial, QuestObjectiveKind.KillEnemy, "enemy.any-run", 100, "outside"), "Unaccepted/outside-run kills ignored");
            var store = new EasySaveAccountStore(Path.Combine(output, "Atomic"));
            Check(store.Load() == null, "Fixture starts empty");
            var session = new AccountGameplaySession(null, registry, store, initial, true);
            int events = 0; session.QuestsCommitted += () => events++;
            Check(session.ExecuteQuest("accept", 0, s => AccountQuestCommands.Accept(s, hunt, MerchantId, registry), true), "Accept checkpoint");
            Check(new EasySaveAccountStore(Path.Combine(output, "Atomic")).Load().quests.Count == 1, "Accept survives reload");
            long revision = session.Revision;
            Check(!session.ExecuteQuest("accept-again", revision, s => AccountQuestCommands.Accept(s, hunt, MerchantId, registry), true)
                && session.Revision == revision, "Duplicate accept is a no-op");
            Throws<InvalidOperationException>(() => session.ExecuteQuest("stale", 0, s => AccountQuestCommands.Track(s, null), true), "Stale revision rejected");
            session.ExecuteQuest("run-fixture", session.Revision, s =>
            {
                s.run = new RunSnapshot { runId = "run-one", phase = RunPhase.Active,
                    map = new MapInstanceState { level = 1, grade = ItemGrade.Common } }; return true;
            }, true);
            Check(!session.ExecuteQuest("other-run", session.Revision,
                s => AccountQuestCommands.Progress(s, QuestObjectiveKind.KillEnemy, "enemy.any-run", 10, "run-other"), false), "Other run ignored");
            Check(!session.ExecuteQuest("pre-accept", session.Revision,
                s => AccountQuestCommands.Progress(s, QuestObjectiveKind.KillEnemy, "enemy.any-run", 10, "run-one", 0), false), "Queued pre-accept kill ignored");
            Check(session.ExecuteQuest("kills", session.Revision,
                s => AccountQuestCommands.Progress(s, QuestObjectiveKind.KillEnemy, "enemy.any-run", int.MaxValue, "run-one"), false), "Kills saturate at requirement");
            Check(AccountQuestCommands.IsReady(session.Read().quests.Single()) && session.HasPendingSave, "Ready memory state and deferred save");
            Check(session.FlushPendingSave(), "Ready checkpoint flushed");
            var readyBytes = ES3.Serialize(session.Read());
            store.FaultInjector = phase => { if (phase == "before-write") throw new IOException("expected fixture failure"); };
            Throws<IOException>(() => session.ExecuteQuest("report-fail", session.Revision,
                s => AccountQuestCommands.TurnIn(s, hunt.id, MerchantId, registry), true), "Before-write report failure");
            Check(ES3.Serialize(session.Read()).SequenceEqual(readyBytes), "Failed report leaves all rewards and state unchanged");
            Check(AccountQuestCommands.IsReady(new EasySaveAccountStore(Path.Combine(output, "Atomic")).Load().quests.Single()), "Ready remains durable after failed report");
            store.FaultInjector = phase => { if (phase == "after-write") throw new IOException("expected uncertain commit"); };
            Check(session.ExecuteQuest("report", session.Revision,
                s => AccountQuestCommands.TurnIn(s, hunt.id, MerchantId, registry), true), "After-write failure resolves by readback");
            var completed = session.Read();
            Check(completed.quests.Single().state == SavedQuestState.Completed && completed.trackedQuestId == null, "Completion and tracker clear together");
            Check(completed.items.Where(i => i.contentId == completed.quests.Single().rewardGoldContentId).Sum(i => i.count) == 100
                && completed.experience == 30 && completed.merchants.Single().reputationTotalXp == 10, "Atomic gold/XP/reputation rewards");
            Check(AccountQuestCommands.Availability(completed, map) == QuestAvailability.Available, "Completion unlocks successor");
            Check(!session.ExecuteQuest("replayed-report", session.Revision,
                s => AccountQuestCommands.TurnIn(s, hunt.id, MerchantId, registry), true), "Completed report cannot pay again");
            var reload = new EasySaveAccountStore(Path.Combine(output, "Atomic")).Load();
            Check(ES3.Serialize(reload).SequenceEqual(ES3.Serialize(completed)), "Full reward contract roundtrip");
            CapacityChecks(registry, hunt);
            DefinitionChecks(hunt, map);
            MigrationChecks(registry);
            PriceChecks();
            MapAndCharacterChecks(registry);
            FrozenAndFutureChecks(registry);
            Check(events == 4, "Only committed changes notify quest UI");
            File.WriteAllText(Path.Combine(output, "result.json"), "{\"status\":\"PASS\",\"checks\":" + passed.Count
                + ",\"scope\":\"native EditMode core; UI and Play not covered\",\"cases\":" + JsonArray(passed) + "}");
            return output;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "failure.txt"), error + "\nPassed:\n" + string.Join("\n", passed));
            throw;
        }
        finally { UnityEngine.Object.DestroyImmediate(hunt); UnityEngine.Object.DestroyImmediate(map); }
    }

    private static void FrozenAndFutureChecks(AccountContentRegistry registry)
    {
        var definition=Make("test.frozen-contract",QuestObjectiveKind.KillEnemy,"enemy.any-run",10);
        try
        {
            var state=NewAccountFactory.Create(registry);AccountQuestCommands.Accept(state,definition,MerchantId,registry);state.revision++;
            definition.title="개정 제목";definition.description="개정 본문";definition.rewardGold=999;definition.rewardExperience=999;definition.objectives[0].required=999;
            var frozen=state.quests.Single();Check(frozen.title=="첫 사냥"&&frozen.description=="테스트 의뢰"&&frozen.rewardGold==100&&frozen.rewardExperience==30&&frozen.objectives.Single().required==10,"definition edits cannot change accepted title/objective/reward contract");
            AccountQuestCommands.Validate(state,registry);Check(QuestCatalog.Find(frozen.questId)==null,"orphaned valid accepted contract is retained without inventing a definition");
            frozen.objectives[0].current=10;
            byte[] before=ES3.Serialize(state);Throws<InvalidOperationException>(()=>AccountQuestCommands.TurnIn(state,frozen.questId,"merchant.main-town.armor",registry),"wrong reporting merchant rejected before rewards");Check(before.SequenceEqual(ES3.Serialize(state)),"wrong merchant leaves frozen reward snapshot unchanged");
            var session=new AccountGameplaySession(null,registry,new EasySaveAccountStore(Path.Combine(output,"Frozen")),state,true);
            Check(session.ExecuteQuest("frozen-report",session.Revision,s=>AccountQuestCommands.TurnIn(s,frozen.questId,MerchantId,registry),true),"accepted frozen report commits through native transaction");
            Check(session.Read().items.Where(i=>i.contentId==frozen.rewardGoldContentId).Sum(i=>i.count)==100&&session.Read().experience==30,"changed definition still pays original 100G and 30XP");
            string directory=Path.Combine(output,"Future");var store=new EasySaveAccountStore(directory);store.Save(NewAccountFactory.Create(registry),"known-schema");
            var future=NewAccountFactory.Create(registry);future.schemaVersion=4;
            var envelope=new AccountSaveEnvelope{schemaVersion=4,profileId=future.profileId,generation=2,transactionId="future",writtenAtUtcTicks=DateTime.UtcNow.Ticks,payload=ES3.Serialize(future)};envelope.checksum=EasySaveAccountStore.Checksum(envelope);
            string file=Path.Combine(directory,"account-B.es3");ES3.Save("account",envelope,new ES3Settings(file));var futureBytes=File.ReadAllBytes(file);
            var reader=new EasySaveAccountStore(directory);Throws<InvalidDataException>(()=>reader.Load(),"newer verified schema rejects fallback to older valid generation");
            Throws<InvalidDataException>(()=>reader.Save(NewAccountFactory.Create(registry),"overwrite-attempt"),"newer schema cannot be overwritten by current checkpoint");
            Check(futureBytes.SequenceEqual(File.ReadAllBytes(file)),"future envelope remains byte-identical after rejected load/save");
        }
        finally{UnityEngine.Object.DestroyImmediate(definition);}
    }

    private static void CapacityChecks(AccountContentRegistry registry, QuestDefinition definition)
    {
        var state = NewAccountFactory.Create(registry);
        AccountQuestCommands.Accept(state, definition, MerchantId, registry); state.revision++;
        state.quests[0].objectives[0].current = 10;
        state.unlockedSlots = state.baseUnlockedSlots = 0;
        byte[] before = ES3.Serialize(state);
        Throws<InvalidOperationException>(() => AccountQuestCommands.TurnIn(state, definition.id, MerchantId, registry), "No reward capacity rejected");
        Check(before.SequenceEqual(ES3.Serialize(state)), "Capacity failure does not partially grant XP/rep/gold/completion");
        state.unlockedSlots = state.baseUnlockedSlots = 1;
        AccountQuestCommands.TurnIn(state, definition.id, MerchantId, registry); state.revision++;
        AccountInvariants.Validate(state, registry);
        Check(state.items.Any(i => i.contentId == state.quests[0].rewardGoldContentId && i.count == 100), "Exact one-slot reward fit");
    }

    private static void DefinitionChecks(QuestDefinition first, QuestDefinition second)
    {
        first.prerequisiteId = second.id;
        Throws<InvalidDataException>(() => QuestCatalog.Validate(new[] { first, second }), "Prerequisite cycle rejected");
        first.prerequisiteId = null;
        second.prerequisiteId = "missing";
        Throws<InvalidDataException>(() => QuestCatalog.Validate(new[] { first, second }), "Missing prerequisite rejected");
        second.prerequisiteId = first.id;
        Throws<InvalidDataException>(() => QuestCatalog.Validate(new[] { first, first }), "Duplicate quest ID rejected");
    }

    private static void MigrationChecks(AccountContentRegistry registry)
    {
        for (int level = 0; level <= 3; level++)
            for (int xp = 0; xp <= 30; xp++)
                if (MerchantReputationPolicy.Level(MerchantReputationPolicy.Migrate(level, xp)) < level)
                    throw new Exception("Legacy reputation demotion.");
        Check(true, "124 legacy reputation values preserve tier");
        var old = NewAccountFactory.Create(registry); old.schemaVersion = 2;
        old.merchants.Add(new MerchantSnapshot { contentId = MerchantId, reputationLevel = 2, reputationExperience = 5 });
        var upgraded = QuestAccountMigration.Upgrade(old);
        Check(old.schemaVersion == 2 && old.merchants[0].reputationLevel == 2 && upgraded.schemaVersion == 3
            && upgraded.merchants[0].reputationTotalXp == 90 && upgraded.quests.Count == 0, "Migration copies source and preserves old tier");
        AccountInvariants.Validate(upgraded, registry);
        var dormant = NewAccountFactory.Create(registry); dormant.schemaVersion = 2;
        old.characters = new List<AccountCharacterSnapshot> { new AccountCharacterSnapshot { id = "dormant", personal = dormant } };
        Check(QuestAccountMigration.Upgrade(old).characters[0].personal.schemaVersion == 3, "Dormant personal schema migrated");
        // Read the same checksum envelope format through the real A/B reader.
        string directory = Path.Combine(output, "Legacy"); Directory.CreateDirectory(directory);
        old.characters = null;
        var envelope = new AccountSaveEnvelope { schemaVersion = 2, profileId = old.profileId, generation = 1,
            transactionId = "legacy", writtenAtUtcTicks = DateTime.UtcNow.Ticks, payload = ES3.Serialize(old) };
        envelope.checksum = EasySaveAccountStore.Checksum(envelope);
        ES3.Save("account", envelope, new ES3Settings(Path.Combine(directory, "account-A.es3")));
        byte[] original = File.ReadAllBytes(Path.Combine(directory, "account-A.es3"));
        var store = new EasySaveAccountStore(directory); var migrated = store.Load();
        Check(original.SequenceEqual(File.ReadAllBytes(Path.Combine(directory, "schema2-original/account-A.es3.generation-1"))), "Original envelope byte backup");
        store.Save(migrated, "upgrade-checkpoint");
        Check(new EasySaveAccountStore(directory).Load().schemaVersion == 3, "Schema 2 read then schema 3 checkpoint");
        Check(original.SequenceEqual(File.ReadAllBytes(Path.Combine(directory, "account-A.es3"))), "Migration retains original A generation");
    }

    private static void MapAndCharacterChecks(AccountContentRegistry registry)
    {
        var hunt=QuestCatalog.Find("quest.main-town.weapon.first-hunt");
        var map=QuestCatalog.Find("quest.main-town.weapon.first-map");
        var state=NewAccountFactory.Create(registry);
        AccountQuestCommands.Accept(state,hunt,MerchantId,registry);state.revision++;
        state.quests[0].objectives[0].current=10;
        AccountQuestCommands.TurnIn(state,hunt.id,MerchantId,registry);state.revision++;
        Check(AccountQuestCommands.Accept(state,map,MerchantId,registry),"Real map quest follows first hunt");state.revision++;
        var q=state.quests.Single(x=>x.questId==map.id);
        Check(q.objectives.Count==3 && q.objectives[0].kind==QuestObjectiveKind.AcquireMap && q.objectives[1].kind==QuestObjectiveKind.SelectMap && q.objectives[2].kind==QuestObjectiveKind.EnterRun,"Map quest freezes three sequential objectives");
        Check(!AccountQuestCommands.Progress(state,QuestObjectiveKind.SelectMap,"portal.map",1),"Map selection before acquisition does not skip sequence");
        var mapAsset=registry.ResolvedEntries.First(e=>e.asset is MapItemData);
        var mapData=new MapInstanceState{mapContentId=mapAsset.id,level=1,grade=ItemGrade.Common};
        AccountRunCommands.PrepareEntry(state,"map-find",mapData,null);AccountRunCommands.Activate(state,"map-find");
        Check(q.objectives[2].current==0,"Level-one entry cannot complete mapped entry objective");
        var acquired=new ItemData((MapItemData)mapAsset.asset,1,ItemGrade.Common);acquired.mapState=mapData;acquired.EnsureAcquisitionOrder();
        var baseline=ItemSnapshotCodec.CopyValues(state);
        var captured=ItemSnapshotCodec.Capture(acquired,registry);captured.acquisitionOrder=state.nextAcquisitionOrder++;
        AccountRunCommands.Acquire(state,"map-find",captured);
        AccountQuestCommands.CaptureMapAcquisitions(baseline,state);
        Check(q.objectives[0].current==1 && q.objectives[1].current==0,"Actual acquired run map progresses acquisition only");
        var same=ItemSnapshotCodec.CopyValues(state);AccountQuestCommands.CaptureMapAcquisitions(same,state);
        Check(q.objectives[0].current==1,"Repeated capture does not recount a map");
        AccountRunCommands.ClearBoss(state,"map-find",DateTime.UtcNow.Ticks);AccountRunCommands.Extract(state,"map-find");
        Check(AccountQuestCommands.Progress(state,QuestObjectiveKind.SelectMap,"portal.map",1),"Owned map portal selection progresses second objective");
        AccountRunCommands.PrepareEntry(state,"map-enter",mapData,acquired.runtimeInstanceId);AccountRunCommands.Activate(state,"map-enter");
        Check(AccountQuestCommands.IsReady(q),"Selected owned map activation completes third objective atomically");
        AccountRunCommands.ClearBoss(state,"map-enter",DateTime.UtcNow.Ticks);AccountRunCommands.Extract(state,"map-enter");state.revision++;
        Check(AccountQuestCommands.TurnIn(state,map.id,MerchantId,registry),"Map report grants frozen rewards");state.revision++;
        Check(state.merchants.Single().reputationTotalXp==20 && state.quests.All(x=>x.state==SavedQuestState.Completed),"Two reports produce exactly 20 canonical reputation XP");
        state.appearance=new CharacterAppearanceSnapshot();AccountCharacterCommands.EnsureLegacy(state);string first=state.activeCharacterId, second=Guid.NewGuid().ToString("N");
        AccountCharacterCommands.Create(state,second,"검증기사","knight",state.appearance,registry);
        AccountCharacterCommands.Select(state,second);
        Check(state.quests.Count==0 && state.trackedQuestId==null && state.merchants.Single().reputationTotalXp==20,"Character switch isolates quests while preserving shared reputation");
        AccountCharacterCommands.Select(state,first);
        Check(state.quests.Count==2 && state.quests.All(x=>x.state==SavedQuestState.Completed),"Switch back restores personal quest completions");
        AccountInvariants.Validate(state,registry);
    }

    private static void PriceChecks()
    {
        foreach (int bp in new[] { 0, 300, 600, 1000 })
            for (int value = 0; value <= 10000; value++)
            {
                int ask = MerchantSalePrice.Unit(value, bp);
                if (ask < value || ask != (int)Math.Ceiling(value * 2m * (10000 - bp) / 10000))
                    throw new Exception("Price invariant failed.");
            }
        Check(true, "40004 native prices: x2/ceiling/no positive resale loop");
        Check(MerchantSalePrice.Unit(20, 0) == 40 && MerchantSalePrice.Unit(20, 1000) == 36, "Confirmed 20G -> 40G -> 36G example");
        Throws<OverflowException>(() => MerchantSalePrice.Unit(int.MaxValue, 0), "Price overflow rejected");
    }

    private static QuestDefinition Make(string id, QuestObjectiveKind kind, string target, int required)
    {
        var q = ScriptableObject.CreateInstance<QuestDefinition>();
        q.id = id; q.title = "첫 사냥"; q.description = "테스트 의뢰";
        q.giverMerchantId = q.turnInMerchantId = MerchantId;
        q.objectives = new[] { new QuestObjectiveDefinition { kind = kind, targetId = target, required = required, label = "목표" } };
        q.rewardGold = 100; q.rewardExperience = 30; q.rewardReputation = 10;
        return q;
    }
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception(name); passed.Add(name); }
    private static void Throws<T>(Action action, string name) where T : Exception
    { try { action(); } catch (T) { passed.Add(name); return; } throw new Exception(name); }
    private static string JsonArray(IEnumerable<string> values) => "[" + string.Join(",", values.Select(s => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")) + "]";
}
