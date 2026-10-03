#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Overburst.DebugTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>범위별 복제 없이 등록된 장면 하나를 실행한다. 게임 동작은 제품 API로 보낸다.</summary>
public static partial class VisualPlayScenarios
{
    static readonly Dictionary<string, Func<VisualPlayContext, IEnumerator>> actions = new Dictionary<string, Func<VisualPlayContext, IEnumerator>>(StringComparer.Ordinal);
    static readonly Dictionary<string, string> unavailable = new Dictionary<string, string>(StringComparer.Ordinal);
    static readonly WeaponElement[] elements = { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light };
    static VisualPlayScenarios()
    {
        Bind(Hud, "VT01-01 VT01-02 VT01-03 VT01-04 VT01-05 VT01-06 VT02-07");
        Bind(Movement, "VT03-01 VT03-02 VT03-03 VT03-04 VT03-05 VT03-06 VT03-07 VT03-08 VT03-09 VT03-10 VT03-11");
        Bind(Combat, "VT05-01 VT05-02 VT05-03 VT05-04 VT05-05 VT05-06 VT06-01 VT06-02 VT06-03 VT06-04 VT06-05 VT06-06 VT06-09");
        Bind(Knockdown, "VT08-01 VT08-02 VT08-04 VT08-06 VT08-08 VT08-09 VT08-10");
        Bind(Element, "VT09-01 VT09-02 VT09-04 VT09-06 VT09-07 VT09-09 VT09-11 VT10-01 VT10-04 VT10-05 VT11-01 VT11-02 VT11-04 VT11-05 VT11-06 VT12-01 VT12-03 VT12-04 VT13-01 VT13-02 VT13-03 VT13-04 VT13-05 VT13-06 VT13-07 VT13-08 VT14-01 VT14-02 VT14-03 VT14-04 VT14-05 VT14-06");
        Bind(Feedback, "VT15-04 VT15-05 VT15-06 VT15-07 VT15-08 VT15-09 VT15-10 VT15-11");
        Bind(Monsters, "VT16-01 VT16-02 VT16-03 VT16-04 VT16-05 VT16-06 VT16-07 VT16-08 VT16-09 VT16-10 VT16-12 VT16-13");
        Bind(Items, "VT17-01 VT17-02 VT17-03 VT17-04 VT17-05 VT17-06 VT17-07 VT17-08 VT17-10");
        RegisterInterfaceScenarios();
        // 지원 여부는 게임 기능과 실제 자동 배치 연결을 구분해 공개한다.
    }
    static void Bind(Func<VisualPlayContext, IEnumerator> action, string ids)
    { foreach (string id in ids.Split(' ')) actions.Add(id, action); }
    public static bool IsSupported(string id) => id != null && actions.ContainsKey(id);
    public static string UnavailableReason(string id) => unavailable.TryGetValue(id ?? "", out var reason) ? reason : "자동 장면 배치가 아직 연결되지 않은 항목이에요";
    public static IEnumerator Run(VisualPlayContext context) => actions[context.Entry.caseId](context);
    public static IEnumerable<VisualPlayEntry> Expand(VisualPlayCase definition, bool full)
    {
        string id = definition.Id;
        if (full && id == "VT19-03")
        {
            foreach (var flask in VisualPlayContext.Items.OfType<FlaskItemData>().Where(item => item.AvailableForDropsAndShop && item.kind != FlaskKind.Life).GroupBy(item => item.kind).Select(group => group.First()))
                yield return new VisualPlayEntry { caseId = id, variant = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(flask)), label = flask.itemName };
            yield break;
        }
        if (full && (id == "VT17-01" || id == "VT17-10" || id == "VT09-11"))
        {
            IEnumerable<BaseItemData> items = VisualPlayContext.Items;
            if (id != "VT17-01") items = items.OfType<WeaponItemData>().Where(WeaponContentPolicy.IsAllowedItemData);
            foreach (var item in items)
            {
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(item));
                if (id == "VT09-11") foreach (var element in elements)
                    yield return new VisualPlayEntry { caseId = id, variant = guid + "|" + element, label = item.itemName + " · " + element };
                else yield return new VisualPlayEntry { caseId = id, variant = guid, label = item.itemName };
            }
            yield break;
        }
        if (id == "VT16-04" || id == "VT16-05")
        {
            var definitions = VisualPlayContext.Definitions;
            var candidates = definitions.SelectMany(enemy => Abilities(enemy).Where(ability => MatchesMonsterAction(id, ability))
                .Select(ability => new { enemy, ability })).ToArray();
            if (!full) candidates = candidates.GroupBy(value => value.ability.ExecutionMode).Select(group => group.First()).ToArray();
            foreach (var value in candidates)
                yield return new VisualPlayEntry { caseId = id, variant = GuidFor(value.enemy) + "|" + GuidFor(value.ability),
                    label = value.enemy.EnemyId + " · " + value.ability.AbilityId + " · " + value.ability.ExecutionMode };
            if (candidates.Length == 0) yield return new VisualPlayEntry { caseId = id, variant = "unavailable", label = "연결된 행동 없음" };
            yield break;
        }
        if (id == "VT16-06")
        {
            foreach (EnemyThemeTier tier in Enum.GetValues(typeof(EnemyThemeTier)))
            {
                var enemy = EnemyThemeTrialService.Entries.SelectMany(value => value.Table.Entries).Where(value => value.tier == tier).Select(value => value.definition).FirstOrDefault(value => value != null);
                yield return new VisualPlayEntry { caseId = id, variant = tier + "|" + (enemy != null ? GuidFor(enemy) : ""), label = tier == EnemyThemeTier.Small ? "소형" : tier == EnemyThemeTier.Medium ? "중형" : "정예" };
            }
            yield return new VisualPlayEntry { caseId = id, variant = "Boss|", label = "실제 던전 보스" };
            yield break;
        }
        if (id == "VT16-12")
        {
            foreach (int count in new[] { 5, 12, 24 }) yield return new VisualPlayEntry { caseId = id, variant = count.ToString(), label = "혼합 " + count + "마리" };
            yield break;
        }
        if (id == "VT19-10")
        {
            foreach (string condition in new[] { "장비 교체", "씬 왕복", "사망" }) yield return new VisualPlayEntry { caseId = id, variant = condition, label = condition };
            yield break;
        }
        if (id == "VT21-10")
        {
            var themes = MapThemeCatalog.Tables.Where(table => MapThemeCatalog.IsEnabledForRuns(table.ThemeId)).ToArray();
            var grades = new[] { ItemGrade.Common, ItemGrade.Rare, ItemGrade.Legendary };
            for (int i = 0; i < 3; i++)
                yield return new VisualPlayEntry { caseId = id, variant = (1 + i * 9) + "|" + grades[i] + "|" + (themes.Length > 0 ? themes[i % themes.Length].ThemeId : ""),
                    label = "레벨 " + (1 + i * 9) + " · " + grades[i] + " · " + (themes.Length > 0 ? themes[i % themes.Length].DisplayName : "테마 없음") };
            yield break;
        }
        if (id == "VT22-06")
        {
            foreach (MapBuffKind kind in Enum.GetValues(typeof(MapBuffKind)))
                yield return new VisualPlayEntry { caseId = id, variant = kind.ToString(), label = BuffLabel(kind) };
            yield break;
        }
        if (full && new[] { "VT16-01", "VT16-02", "VT16-03", "VT16-07" }.Contains(id))
        {
            foreach (var enemy in VisualPlayContext.Definitions)
                yield return new VisualPlayEntry { caseId = id, variant = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(enemy)), label = enemy.EnemyId + " · " + enemy.Grade.GradeType };
            yield break;
        }
        if (id == "VT08-04" || id == "VT08-06")
        {
            foreach (string mode in id == "VT08-06" ? new[] { "탐험", "전투" } : new[] { "전투" })
                foreach (string direction in new[] { "없음", "앞", "뒤", "왼쪽", "오른쪽", "앞왼쪽", "앞오른쪽", "뒤왼쪽", "뒤오른쪽" })
                    yield return new VisualPlayEntry { caseId = id, variant = mode + "|" + direction, label = mode + " · " + direction };
            yield break;
        }
        yield return new VisualPlayEntry { caseId = id, variant = "representative", label = "" };
    }
    static string BuffLabel(MapBuffKind kind)
    {
        for (int seed = 0; seed < 500; seed++)
        {
            var choice = MapRunCardPolicy.Roll(new System.Random(seed), 1, 1).Choices.FirstOrDefault(value => value.Kind == MapCardKind.Buff && value.Buff == kind);
            if (choice != null) return choice.Title;
        }
        return kind.ToString();
    }
    static string GuidFor(UnityEngine.Object value) => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(value));
    static EnemyAbilityDefinition[] Abilities(EnemyDefinition definition) => definition?.AbilitySet == null ? Array.Empty<EnemyAbilityDefinition>()
        : Enumerable.Range(0, definition.AbilitySet.Count).Select(definition.AbilitySet.GetAbility).Where(value => value != null && value.IsValid).ToArray();
    static bool MatchesMonsterAction(string id, EnemyAbilityDefinition ability) => id == "VT16-04" ? ability.IsTelegraphedStrongAttack
        : ability.ExecutionMode != EnemyAbilityExecutionMode.MeleeArc && ability.ExecutionMode != EnemyAbilityExecutionMode.DirectTarget;
    static T Definition<T>(VisualPlayContext c) where T : BaseItemData
    {
        string guid = (c.Entry.variant ?? "").Split('|')[0];
        return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid)) ?? VisualPlayContext.Items.OfType<T>().FirstOrDefault();
    }
    static IEnumerator Hud(VisualPlayContext c)
    {
        switch (c.Entry.caseId)
        {
            case "VT01-02": c.Actor.Health.TakeDamage(new DamageInfo(c.Actor.Health.MaxHp * .35f, c.Actor.transform.position)); break;
            case "VT01-03":
                c.Actor.Health.TakeDamage(new DamageInfo(c.Actor.Health.MaxHp * .5f, c.Actor.transform.position));
                yield return c.Wait(2f); c.Detail("회복"); c.Actor.Health.Heal(c.Actor.Health.MaxHp * .3f); break;
            case "VT01-04": PlayerProgression.Current.AddExperience(Mathf.Max(1, PlayerProgression.Current.ExperienceToNext / 4)); break;
            case "VT01-05": PlayerProgression.Current.AddExperience(PlayerProgression.Current.ExperienceToNext); break;
            case "VT02-07":
                OverburstGameMenu.Instance.Open(); yield return c.Wait(3f); c.Detail("메뉴 닫기와 재개"); OverburstGameMenu.Instance.Close(); break;
            default: yield return c.Wait(2f); break;
        }
        yield return c.Wait(2f);
    }
    static IEnumerator Movement(VisualPlayContext c)
    {
        yield return c.Arena();
        string id = c.Entry.caseId;
        if (id == "VT03-08")
        {
            var guard = c.Actor.GetComponent<RunFallGuard>();
            if (guard == null || !guard.enabled) throw new VisualPlayUnavailable("이 시험장에는 낙하 복귀가 연결되지 않았어요");
            c.Detail("현재 지면의 낙하 복귀");
            ActorTeleportUtility.TeleportSafely(c.Actor.transform, c.Origin + Vector3.down * 3f, c.Actor.transform.rotation);
            yield return c.Wait(3f); yield break;
        }
        if (id == "VT03-10")
        {
            // 실제 Zoom 바인딩을 사용한다.
            var camera = QuarterViewCamera.ActiveInstance;
            VisualPlayContext.Require(camera != null, "게임 카메라가 없어요");
            c.Detail("가까이 줌"); c.Scroll(120); yield return c.Wait(.5f); c.Scroll(0); yield return c.Wait(2f);
            c.Detail("멀리 줌"); c.Scroll(-120); yield return c.Wait(.8f); c.Scroll(0); yield return c.Wait(2f);
            camera.ResetZoom(); yield return c.Wait(2f); yield break;
        }
        if (id == "VT03-05" || id == "VT03-06" || id == "VT03-07")
        {
            int count = id == "VT03-06" ? 4 : 1;
            for (int i = 0; i < count; i++)
            {
                var block = c.Own(GameObject.CreatePrimitive(PrimitiveType.Cube)); block.name = "VisualPlay Terrain";
                block.transform.position = c.Origin + c.Actor.Movement.ResolveMoveDirection(Vector2.up) * (2f + i * .65f) + Vector3.up * (id == "VT03-07" ? 1f : .15f + i * .1f);
                block.transform.localScale = id == "VT03-07" ? new Vector3(4, 2, .3f) : new Vector3(4, .3f + i * .2f, .7f);
                if (id == "VT03-05") { block.transform.localScale = new Vector3(4, .2f, 4); block.transform.rotation = Quaternion.Euler(15, 0, 0); }
            }
            Physics.SyncTransforms();
        }
        c.Detail(id == "VT03-01" ? "걷기" : "앞으로 이동");
        c.Input(Vector2.up, id == "VT03-01" ? new[] { Key.LeftCtrl } : Array.Empty<Key>());
        if (id == "VT03-04") c.Input(Vector2.up, Key.Space);
        if (id == "VT03-11") c.Input(Vector2.up, Key.LeftShift);
        yield return c.Wait(2f); c.ReleaseInput(); yield return c.Wait(.8f);
        c.Detail("달리기·방향 전환"); c.Input(id == "VT03-03" ? new Vector2(1, 1).normalized : Vector2.right);
        if (id == "VT03-01") { c.Input(Vector2.right, Key.LeftCtrl); yield return c.Wait(.1f); c.Input(Vector2.right); }
        yield return c.Wait(2f); c.ReleaseInput(); yield return c.Wait(.8f);
        c.Input(Vector2.down); yield return c.Wait(1.5f); c.ReleaseInput();
    }
    static IEnumerator Arm(VisualPlayContext c, WeaponElement element = WeaponElement.None)
    {
        yield return c.Arena();
        c.Equip(Definition<WeaponItemData>(c)); c.Gem(element);
        PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
        yield return c.Wait(.8f);
    }
    public static IEnumerator Attack(VisualPlayContext c, bool heavy, bool combo = false, Action duringAttack = null)
    {
        c.Aim(c.Actor.transform.position + Vector3.forward * 3f);
        WeaponActionHandle handle;
        var request = new WeaponActionRequest(WeaponActionSource.PlayerInput, null, Vector3.forward);
        var result = heavy ? c.Melee.TryStartHeavyAttack(Vector3.forward) : c.Melee.TryStartAction(request, out handle);
        VisualPlayContext.Require(result == WeaponActionResult.Accepted, "공격 시작 실패: " + result);
        handle = new WeaponActionHandle(GetActionId(c.Melee));
        float elapsed = 0;
        while (c.Melee.IsAttackInProgress)
        {
            duringAttack?.Invoke();
            if (combo && elapsed < 5f) c.Melee.TryContinue(handle, request);
            elapsed += Mathf.Min(Time.unscaledDeltaTime, .1f);
            if (elapsed > 15) throw new TimeoutException("공격이 끝나지 않았어요");
            yield return null;
        }
        yield return c.Wait(.4f);
    }
    static int GetActionId(MeleeRuntime melee) => (int)typeof(MeleeRuntime).GetField("activeActionId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(melee);
    static IEnumerator Combat(VisualPlayContext c)
    {
        yield return Arm(c);
        string id = c.Entry.caseId;
        if (id == "VT05-01")
        {
            PlayerCombatModeController.GetOrCreate().ExitCombatMode(PlayerCombatModeReason.System); yield return c.Wait(2f);
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System); yield break;
        }
        if (id == "VT05-06") c.Spawn(VisualPlayContext.Definitions.First(), Vector3.forward * 1.8f).Health.SetMaxHp(100000, true);
        if (id.StartsWith("VT06"))
        {
            if (id == "VT06-01") PlayerCombatModeController.GetOrCreate().ExitCombatMode(PlayerCombatModeReason.System);
            c.Aim(c.Actor.transform.position + Vector3.forward * 3f);
            c.Input(id == "VT06-02" ? Vector2.left : c.Actor.Movement.ResolveMoveDirection(Vector2.up).z >= 0 ? Vector2.up : Vector2.down, Key.LeftShift);
            yield return c.Until(() => c.Evade.IsEvading, 2, "회피 시작");
            c.Input(Vector2.zero);
            if (id == "VT06-04" || id == "VT06-05" || id == "VT06-06")
            { c.MouseButton(id != "VT06-06", id == "VT06-06"); yield return c.Wait(.15f); c.MouseButton(false, false); }
            yield return c.Until(() => !c.Evade.IsEvading && !c.Melee.IsAttackInProgress, 12, "회피와 후속 공격");
            if (id == "VT06-05") yield return Attack(c, false, true);
            yield break;
        }
        yield return Attack(c, id == "VT05-05", id == "VT05-03");
        if (id == "VT05-04") { c.Input(Vector2.right); yield return c.Wait(1.5f); c.ReleaseInput(); }
    }
    static IEnumerator Knockdown(VisualPlayContext c)
    {
        yield return Arm(c);
        string id = c.Entry.caseId;
        if (id == "VT08-01")
        {
            c.Actor.Health.TakeDamage(new DamageInfo(10, c.Actor.transform.position, direction: Vector3.back)); yield break;
        }
        if (id == "VT08-06" && c.Entry.variant.StartsWith("탐험|"))
            PlayerCombatModeController.GetOrCreate().ExitCombatMode(PlayerCombatModeReason.System);
        var definition = VisualPlayContext.Definitions.FirstOrDefault(d => d.EnemyId == "CavernMutants_Ursacetus" && d.Grade.GradeType == EnemyGradeType.Elite);
        VisualPlayContext.Require(definition != null, "실제 넉다운 정예가 없어요");
        var ability = Enumerable.Range(0, definition.AbilitySet.Count).Select(definition.AbilitySet.GetAbility).First(a => a.IsMeleeStrongAttack && a.HitCount == 1);
        var set = c.Own(ScriptableObject.CreateInstance<EnemyAbilitySet>());
        var serialized = new SerializedObject(set); serialized.FindProperty("abilitySetId").stringValue = "VisualPlaySingleStrong";
        serialized.FindProperty("abilities").arraySize = 1; serialized.FindProperty("abilities").GetArrayElementAtIndex(0).objectReferenceValue = ability;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        if (id == "VT08-08" || id == "VT08-09")
        {
            var wall = c.Own(GameObject.CreatePrimitive(PrimitiveType.Cube)); wall.name = "VisualPlay Knockdown Surface";
            wall.transform.position = c.Origin + Vector3.back * 1.05f + Vector3.up * (id == "VT08-08" ? 1.1f : -.05f);
            wall.transform.localScale = id == "VT08-08" ? new Vector3(5.5f, 2.2f, .25f) : new Vector3(6, .1f, 6);
            if (id == "VT08-09") wall.transform.rotation = Quaternion.Euler(8, 0, 0);
            Physics.SyncTransforms();
        }
        var enemy = c.Spawn(definition, Vector3.forward * Mathf.Max(1.2f, ability.MinimumRange + .2f));
        enemy.AbilityController.Configure(set, enemy.RuntimeStats.DamageMultiplier, 1); enemy.AbilityController.BeginStrongOnlyPass();
        if (id == "VT08-10")
        {
            UnityEngine.Object.FindFirstObjectByType<EnemyThemeDebugArena>()?.ReleasePlayerProtection();
            c.Actor.Health.TakeDamage(new DamageInfo(Mathf.Max(0, c.Actor.Health.CurrentHp - 1), c.Actor.transform.position));
        }
        VisualPlayContext.Require(enemy.AbilityController.TryStart(c.Actor.transform), "정예 강공 시작 실패");
        var reaction = c.Actor.GetComponent<PlayerKnockdownController>();
        yield return c.Until(() => reaction.IsActive || c.Actor.Health.IsDead, 5, "강공 피격");
        while (reaction.IsActive)
        {
            if (reaction.Phase == PlayerKnockdownPhase.Grounded && (id == "VT08-04" || id == "VT08-06"))
            {
                if (id == "VT08-06")
                {
                    var mode = PlayerCombatModeController.GetOrCreate(); bool combat = c.Entry.variant.StartsWith("전투|");
                    if (combat) mode.EnterCombatMode(PlayerCombatModeReason.ManualToggle); else mode.ExitCombatMode(PlayerCombatModeReason.ManualToggle);
                    VisualPlayContext.Require(mode.IsCombatModeActive == combat, "Shift 기상 모드 준비가 달라요");
                }
                c.Input(Direction(c.Entry.variant), id == "VT08-06" ? new[] { Key.LeftShift } : Array.Empty<Key>());
            }
            if (reaction.Phase == PlayerKnockdownPhase.Rising) c.ReleaseInput();
            yield return null;
        }
        c.ReleaseInput(); yield return c.Wait(1f);
    }
    static Vector2 Direction(string label)
    {
        Vector2 value = Vector2.zero;
        if (label.Contains("앞")) value.y = 1; if (label.Contains("뒤")) value.y = -1;
        if (label.Contains("왼쪽")) value.x = -1; if (label.Contains("오른쪽")) value.x = 1;
        return value.normalized;
    }
    static WeaponElement ElementFor(string id) => id.StartsWith("VT10") ? WeaponElement.Fire : id.StartsWith("VT11") ? WeaponElement.Ice
        : id.StartsWith("VT12") ? WeaponElement.Electric : id.StartsWith("VT13") ? WeaponElement.Dark : id.StartsWith("VT14") ? WeaponElement.Light : WeaponElement.Fire;
    static IEnumerator Element(VisualPlayContext c)
    {
        string id = c.Entry.caseId;
        var element = ElementFor(id);
        if (id == "VT09-11" && Enum.TryParse((c.Entry.variant ?? "").Split('|').Last(), out WeaponElement parsed)) element = parsed;
        yield return Arm(c, element);
        if (id == "VT09-01" || id == "VT09-02")
        {
            foreach (var value in id == "VT09-02" ? elements : new[] { element, WeaponElement.None })
            { c.Detail(value.ToString()); c.Gem(value); yield return c.Wait(2f); }
            yield break;
        }
        if (id == "VT09-04" || id == "VT09-11") { yield return Attack(c, false); yield break; }
        int count = id.EndsWith("05") || id == "VT12-04" || id == "VT13-08" ? 5 : id == "VT13-06" || id == "VT10-04" || id == "VT12-03" || id == "VT13-07" ? 3 : 1;
        var targets = new List<EnemyActor>();
        for (int i = 0; i < count; i++)
        {
            var definition = VisualPlayContext.Definitions.First(d => id == "VT11-06" ? d.Grade.GradeType != EnemyGradeType.Normal : d.Grade.GradeType == EnemyGradeType.Normal);
            var enemy = c.Spawn(definition, new Vector3((i % 3 - (count > 1 ? 1 : 0)) * .6f, 0, .8f + i / 3f * .45f)); enemy.Health.SetMaxHp(100000, true); targets.Add(enemy);
        }
        var energy = c.Actor.Equipment.GetComponent<OverburstElementEnergy>() ?? c.Own(c.Actor.Equipment.gameObject.AddComponent<OverburstElementEnergy>());
        if (id == "VT14-01") PrepareEnergy(energy, energy.BaseMaximum);
        if (id == "VT09-07")
        {
            energy.Clear(); c.Detail("에너지 0"); yield return Attack(c, false); yield return c.Wait(2f);
            foreach (float threshold in new[] { .25f, .6f, 1f })
            {
                c.Detail(threshold >= 1 ? "최대 에너지" : threshold < .5f ? "낮은 에너지" : "중간 에너지");
                int attempts = 0;
                while (energy.Normalized < threshold) { if (++attempts > 30) throw new Exception("실제 적중으로 에너지가 충전되지 않았어요"); yield return Attack(c, false); }
                yield return c.Wait(2f);
            }
            yield break;
        }
        if (id != "VT13-03")
        {
            c.Detail("실제 약공 적중·상태 축적");
            for (int i = 0; i < (id == "VT11-02" ? 8 : 4); i++) { yield return Attack(c, false, true); yield return c.Wait(.25f); }
        }
        if (new[] { "VT10-01", "VT11-01", "VT11-02", "VT12-01", "VT13-01", "VT14-01" }.Contains(id))
        { c.Detail("상태 효과의 자연 종료까지 관찰"); yield return c.Wait(20f); yield break; }
        if (id == "VT09-06") { yield return c.Wait(3f); yield break; }
        if (id.StartsWith("VT14") && id != "VT14-06")
        {
            c.Detail("관찰 조건 준비 · 빛 최대 과충전과 광휘"); PrepareEnergy(energy, energy.Capacity); yield return c.Wait(1f);
            if (id == "VT14-02") { yield return c.Wait(8f); yield break; }
        }
        if (id == "VT13-07")
        {
            var camera = Camera.main; VisualPlayContext.Require(camera != null, "암흑 연타를 관찰할 카메라가 없어요");
            var outside = targets.Last();
            ActorTeleportUtility.TeleportSafely(outside.transform, c.Origin + Vector3.right * 25f, outside.transform.rotation);
            Physics.SyncTransforms();
            Vector3 viewport = camera.WorldToViewportPoint(outside.transform.position);
            VisualPlayContext.Require(viewport.z <= 0 || viewport.x < 0 || viewport.x > 1 || viewport.y < 0 || viewport.y > 1, "화면 밖 표적을 준비하지 못했어요");
            c.Detail("화면 안·밖 표적과 카메라 이동 중 연타");
        }
        bool chainObserved = false, barrageObserved = false;
        c.Detail("강공 방출");
        yield return Attack(c, true, duringAttack: () =>
        {
            if (id == "VT12-03" && !chainObserved && ElementChainScheduler.ActiveCastCount > 0)
            {
                chainObserved = true; c.Detail("전기 연쇄가 진행 중일 때 표적 사망");
                targets[0].Health.TakeDamage(new DamageInfo(1000000, targets[0].transform.position, c.Actor.gameObject));
                VisualPlayContext.Require(targets[0].Health.IsDead, "전기 연쇄 중 표적 사망이 적용되지 않았어요");
            }
            if (id == "VT13-07" && DarkBarrageScheduler.ActiveCount > 0)
            {
                barrageObserved = true; c.Input(Vector2.right); c.Scroll(-120);
                c.Detail("화면 안·밖 표적과 카메라 이동 중 암흑 연타");
            }
        });
        if (id == "VT12-03")
        {
            VisualPlayContext.Require(chainObserved, "표적 사망 전에 실제 전기 연쇄가 시작되지 않았어요");
            yield return c.Until(() => ElementChainScheduler.ActiveCastCount == 0, 10, "전기 연쇄 종료");
        }
        if (id == "VT13-07")
        {
            VisualPlayContext.Require(barrageObserved, "카메라 이동 중 실제 암흑 연타가 시작되지 않았어요");
            c.Scroll(0); c.Input(Vector2.left); yield return c.Wait(1.5f); c.ReleaseInput(); QuarterViewCamera.ActiveInstance?.ResetZoom();
        }
        if (id == "VT13-06" || id == "VT14-06")
        {
            targets[0].Health.TakeDamage(new DamageInfo(1000000, targets[0].transform.position, c.Actor.gameObject));
            if (id == "VT14-06") { yield return c.Wait(1f); c.Gem(WeaponElement.None); }
        }
        yield return c.Wait(8f);
        if (id == "VT09-09") yield return Attack(c, false, true);
    }
    static void PrepareEnergy(OverburstElementEnergy energy, float amount)
    {
        // 외형 비교의 시작 조건만 정식 에너지 API로 준비한다. 적중 충전 항목은 실제 공격을 사용한다.
        int sequence = 9000000;
        while (energy.Amount < amount - .01f)
        {
            if (++sequence > 9000100 || !energy.RecordConfirmedHit(energy.WeaponInstanceId, energy.Element, sequence, 1f))
                throw new InvalidOperationException("관찰용 원소 에너지 조건을 준비하지 못했어요");
        }
    }
    static IEnumerator Feedback(VisualPlayContext c)
    {
        yield return Arm(c, c.Entry.caseId == "VT15-05" || c.Entry.caseId == "VT15-07" ? WeaponElement.Fire : WeaponElement.None);
        int count = c.Entry.caseId == "VT15-11" ? 8 : 2;
        for (int i = 0; i < count; i++)
        {
            var enemy = c.Spawn(VisualPlayContext.Definitions.First(), new Vector3((i % 3 - 1) * .8f, 0, 1.8f + i / 3f)); enemy.Health.SetMaxHp(100000, true);
            if (c.Entry.caseId == "VT15-04") { enemy.Health.TakeDamage(new DamageInfo(10, enemy.transform.position, c.Actor.gameObject)); yield return c.Wait(1f); enemy.Health.TakeDamage(new DamageInfo(20, enemy.transform.position, c.Actor.gameObject, isCritical: true)); }
        }
        yield return Attack(c, false, true); yield return c.Wait(1f); yield return Attack(c, true); yield return c.Wait(6f);
    }
    static IEnumerator Monsters(VisualPlayContext c)
    {
        yield return Arm(c);
        string id = c.Entry.caseId;
        if (id == "VT16-09" || id == "VT16-10")
        { var result = EnemyThemeTrialService.Begin(EnemyThemeTrialService.Entries[0], id == "VT16-10"); VisualPlayContext.Require(result.Success, result.Message); yield return c.Wait(20f); yield break; }
        string[] variant = (c.Entry.variant ?? "").Split('|');
        if (id == "VT16-06" && variant[0] == EnemyGradeType.Boss.ToString() && (variant.Length < 2 || string.IsNullOrEmpty(variant[1])))
        {
            EnemyThemeTrialService.ToggleArena(); yield return EnterDungeon(c);
            var world = UnityEngine.Object.FindFirstObjectByType<DiamondDungeonWorld>();
            var boss = Field<GameObject>(world, "boss"); var health = Field<CombatHealth>(world, "bossHealth");
            VisualPlayContext.Require(boss != null && health != null, "실제 던전 보스가 없어요");
            ActorTeleportUtility.TeleportSafely(c.Actor.transform, boss.transform.position + Vector3.back * 4f, Quaternion.identity);
            health.TakeDamage(new DamageInfo(10, health.transform.position, c.Actor.gameObject, direction: Vector3.forward)); yield return c.Wait(7f); yield break;
        }
        string enemyGuid = id == "VT16-06" ? variant.ElementAtOrDefault(1) : variant[0];
        var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(enemyGuid ?? ""))
            ?? VisualPlayContext.Definitions.First(d => d.Grade.GradeType == EnemyGradeType.Normal);
        if (id == "VT16-06") VisualPlayContext.Require(EnemyThemeTrialService.Entries.SelectMany(value => value.Table.Entries).Any(value => value.definition == definition && value.tier.ToString() == variant[0]), "해당 체급의 실제 몬스터가 없어요");
        int count = id == "VT16-12" ? int.Parse(variant[0]) : id == "VT16-13" ? 12 : 1;
        var mixed = EnemyThemeTrialService.Entries.SelectMany(value => value.Table.Entries).Where(value => value.definition != null)
            .GroupBy(value => value.tier).Select(group => group.First().definition).ToArray();
        if (id == "VT16-08")
        {
            var wall = c.Own(GameObject.CreatePrimitive(PrimitiveType.Cube)); wall.name = "VisualPlay Pursuit Obstacle";
            wall.transform.position = c.Origin + Vector3.forward * 2.5f + Vector3.up;
            wall.transform.localScale = new Vector3(3f, 2f, .6f);
            var obstacle = wall.AddComponent<UnityEngine.AI.NavMeshObstacle>(); obstacle.carving = true; Physics.SyncTransforms();
            VisualPlayContext.Require(wall.GetComponent<Collider>() != null, "추적 장애물의 충돌체가 없어요");
        }
        for (int i = 0; i < count; i++)
        {
            var selected = id == "VT16-12" ? mixed[i % mixed.Length] : definition;
            var enemy = c.Spawn(selected, id == "VT16-08" ? Vector3.forward * 5f : count == 1 ? new Vector3(0, 0, 1.6f) : new Vector3((i % 5 - 2) * 1.1f, 0, 3f + i / 5f), id == "VT16-02" || id == "VT16-08" || id == "VT16-12");
            if (id == "VT16-03")
            {
                var ability = c.SingleWeak(enemy);
                float distance = Mathf.Lerp(ability.MinimumRange, ability.Range, .5f);
                ActorTeleportUtility.TeleportSafely(enemy.transform, c.Actor.transform.position + Vector3.forward * distance, Quaternion.LookRotation(Vector3.back));
                Physics.SyncTransforms(); yield return c.Wait(.5f);
                yield return c.Until(() => enemy != null && enemy.AbilityController.TryStart(c.Actor.transform), 8f, "몬스터 방향 정렬과 약공 시작");
            }
            if (id == "VT16-04" || id == "VT16-05")
            {
                var ability = AssetDatabase.LoadAssetAtPath<EnemyAbilityDefinition>(AssetDatabase.GUIDToAssetPath(variant.ElementAtOrDefault(1) ?? ""));
                if (ability == null || !MatchesMonsterAction(id, ability)) throw new VisualPlayUnavailable("이 개체는 지정한 강공·특수 행동을 제공하지 않아요");
                c.SingleAbility(enemy, ability);
                float distance = Mathf.Lerp(ability.MinimumRange, ability.Range, .5f);
                ActorTeleportUtility.TeleportSafely(enemy.transform, c.Actor.transform.position + Vector3.forward * distance, Quaternion.LookRotation(Vector3.back));
                float ratio = Mathf.Lerp(ability.MinimumSelfHealthNormalized, ability.MaximumSelfHealthNormalized, .5f);
                enemy.Health.TakeDamage(new DamageInfo(enemy.Health.MaxHp * (1f - ratio), enemy.transform.position));
                Physics.SyncTransforms(); yield return c.Wait(.5f);
                yield return c.Until(() => enemy != null && enemy.AbilityController.TryStart(c.Actor.transform), 8f, ability.AbilityId + " 시작");
            }
            if (id == "VT16-06") enemy.Health.TakeDamage(new DamageInfo(10, enemy.transform.position, c.Actor.gameObject, direction: Vector3.forward));
            if (id == "VT16-07" || id == "VT16-13") { yield return c.Wait(.6f); enemy.Health.TakeDamage(new DamageInfo(1000000, enemy.transform.position, c.Actor.gameObject)); }
        }
        if (id == "VT16-08")
        {
            c.Detail("장애물 건너편에서 추적·경로 복구"); c.Input(Vector2.right); yield return c.Wait(2f);
            c.Input(Vector2.left); yield return c.Wait(2f); c.ReleaseInput();
        }
        yield return c.Wait(id == "VT16-12" ? 15 : 7);
    }
    static IEnumerator Items(VisualPlayContext c)
    {
        yield return c.Arena();
        string id = c.Entry.caseId;
        var definition = Definition<BaseItemData>(c);
        if (id == "VT17-10") { c.Equip(Definition<WeaponItemData>(c)); yield return c.Wait(2f); PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System); yield break; }
        int count = id == "VT17-06" ? 15 : 1;
        if (id == "VT17-03")
        {
            foreach (ItemGrade grade in Enum.GetValues(typeof(ItemGrade))) if (ItemGradeAvailabilityPolicy.IsEnabled(grade))
            { c.Detail(ItemTooltipFormatter.GetGradeName(grade)); c.Drop(definition, Vector3.forward * 2f + Vector3.up, grade); yield return c.Wait(2f); }
            yield break;
        }
        if (id == "VT17-07")
        {
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
            VisualPlayContext.Require(!WorldItemNameplatePresenter.ShouldDisplay(WorldLootInteractionMode.CombatAutoLegendary, false, false), "일반 아이템 이름표 숨김 조건이 달라요");
        }
        WorldItemPickup pickup = null;
        for (int i = 0; i < count; i++) pickup = c.Drop(definition, new Vector3((i % 5 - 2) * .35f, 1, 2f + i / 5f));
        yield return c.Wait(3f);
        if (id == "VT17-05") { VisualPlayContext.Require(pickup.TryPickup(), "획득 실패"); yield return c.Wait(2f); }
        if (id == "VT17-04" || id == "VT17-07")
        {
            var bridge = UnityEngine.Object.FindFirstObjectByType<WorldItemNameplateBridge>();
            VisualPlayContext.Require(bridge != null, "월드 아이템 이름표 입력 연결이 없어요");
            if (id == "VT17-07")
            {
                var presenter = WorldItemNameplatePresenter.Active; var camera = Camera.main;
                VisualPlayContext.Require(presenter != null && camera != null, "아이템 모델 포인터 검사를 준비하지 못했어요");
                Vector3 screen = camera.WorldToScreenPoint(pickup.transform.position + Vector3.up * .1f);
                VisualPlayContext.Require(presenter.ResolveModelPointerTarget(new Vector2(screen.x, screen.y)) == pickup, "이름표 없는 아이템 모델을 포인터로 찾지 못했어요");
                c.Detail("이름표가 숨겨진 모델에 마우스 올림");
            }
            bridge.HandlePointerEnter(pickup);
            try { yield return c.Wait(3f); }
            finally { bridge.HandlePointerExit(pickup); }
        }
        if (id == "VT17-08") { c.Input(Vector2.right); yield return c.Wait(1.5f); c.ReleaseInput(); }
    }
}
#endif
