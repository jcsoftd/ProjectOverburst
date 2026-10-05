using System;
using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static partial class PlayerEvadeVerifier
{
    const string EvadeCollisionKey = "Overburst.PlayerEvadeVerifier.Collision";

    public static void StartEvadeCollisionIsolated(string directory)
    {
        SessionState.SetBool(EvadeCollisionKey, true);
        try { StartIsolated(directory); }
        catch { SessionState.EraseBool(EvadeCollisionKey); throw; }
    }

    static GameObject EvadeObstacle(string shape, float distance = 2f, bool enemy = false)
    {
        var obj = new GameObject("OwnedEvadeCollision_" + shape);
        fixtures.Add(obj);
        obj.transform.position = origin + forward * distance + Vector3.up * 1.5f;
        obj.transform.rotation = Quaternion.LookRotation(forward);
        if (shape == "pillar" || shape == "largeEnemy")
        {
            if (shape == "pillar") obj.transform.position += Vector3.Cross(Vector3.up, forward) * .35f;
            var capsule = obj.AddComponent<CapsuleCollider>();
            capsule.radius = shape == "largeEnemy" ? 3f : .6f; capsule.height = shape == "largeEnemy" ? 6f : 3f;
        }
        else
        {
            if (shape == "diagonal") obj.transform.rotation *= Quaternion.Euler(0, 45, 0);
            obj.AddComponent<BoxCollider>().size = new Vector3(6, 3, .3f);
        }
        if (enemy)
        {
            obj.layer = LayerMask.NameToLayer("Enemy");
            obj.AddComponent<CombatTarget>(); obj.GetComponent<CombatAffiliation>().Configure(CombatTeam.Enemy);
        }
        Physics.SyncTransforms();
        return obj;
    }

    static IEnumerator CollisionComplete(string label)
    {
        float limit = Time.unscaledTime + 2f;
        while (evade.IsEvading && Time.unscaledTime < limit) yield return null;
        Check(!evade.IsEvading && evade.LastEndWasCompleted, label + " 정상 종료");
        Check(!actor.GetComponent<OverburstCharacterMotor3D>().EvadeEnemyPassThrough,
            label + " 적 충돌 복원");
    }

    static IEnumerator VerifyEvadeCollision()
    {
        var motor = actor.GetComponent<OverburstCharacterMotor3D>();
        var body = motor.Controller;
        int originalExcluded = body.excludeLayers.value;
        int enemyMask = LayerMask.GetMask("Enemy");
        Check(enemyMask != 0 && (originalExcluded & enemyMask) == 0, "시험 시작 적 몸 충돌 활성");
        foreach (bool roll in new[] { false, true })
        foreach (string shape in new[] { "diagonal", "pillar", "front" })
        {
            yield return Reset(true, roll ? 180 : 0);
            var obstacle = EvadeObstacle(shape);
            yield return StartDodge(roll); Send();
            Check(evade.ActiveType == (roll ? PlayerEvadeType.Roll : PlayerEvadeType.CombatDodge),
                shape + " 실제 회피 종류 " + roll);
            Check(motor.EvadeEnemyPassThrough && (body.excludeLayers.value & enemyMask) != 0,
                shape + " 회피 중 적 몸 통과");
            yield return CollisionComplete(shape + (roll ? " roll" : " dash"));
            Vector3 delta = endPosition - startPosition;
            float ahead = Vector3.Dot(delta, forward), side = Mathf.Abs(Vector3.Dot(delta, Vector3.Cross(Vector3.up, forward)));
            samples.Add(new { shape, roll, ahead, side, rise = delta.y });
            if (shape == "front") Check(ahead < 1.9f && side < .15f, "정면 벽 관통 없음 " + roll);
            else Check(ahead > 2.1f && side > .25f, "벽·기둥 옆으로 슬라이딩 " + shape + " " + roll + " " + ahead + "/" + side);
            Check(Mathf.Abs(delta.y) < .1f, "장애물 회피 높이 안정 " + shape);
            UnityEngine.Object.Destroy(obstacle); fixtures.Remove(obstacle); yield return Frames(2);
        }

        var ui = EnemyThemeTrialHarness.Current;
        Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(actor.transform, out spawn), "회피 실제 적 스폰 서비스");
        foreach (var table in ui.tables) Check(spawn.RegisterAdditionalCatalog(table.Catalog, out _), "회피 적 카탈로그");
        var definition = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition)
            .First(d => d != null && d.Grade.GradeType == EnemyGradeType.Normal);
        yield return Reset();
        Check(spawn.TrySpawn(new EnemySpawnRequest(definition, origin + forward * 2, Quaternion.LookRotation(-forward), actor.transform), out var enemy),
            "정식 몬스터 생성");
        leased.Add(enemy); enemy.AI.enabled = false; enemy.Movement.StopMovement(); enemy.Movement.enabled = false;
        enemy.Health.SetMaxHp(100000, true);
        var rigidbody = enemy.GetComponent<Rigidbody>(); if (rigidbody != null) rigidbody.constraints = RigidbodyConstraints.FreezeAll;
        var enemyBodies = enemy.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger && c.gameObject.layer == LayerMask.NameToLayer("Enemy")).ToArray();
        Check(enemyBodies.Length > 0, "정식 몬스터 물리 몸 활성");
        Physics.SyncTransforms();
        Vector3 before = actor.transform.position; motor.MoveDirect(forward * 4f);
        Check(Vector3.Dot(actor.transform.position - before, forward) < 2f, "일반 이동 적 몸 충돌 유지");

        foreach (int mode in new[] { 0, 1, 2 })
        {
            yield return Reset(mode != 2, mode == 1 ? 180 : 0);
            yield return StartDodge(mode == 1); Send();
            Check(motor.EvadeEnemyPassThrough, "정식 적 통과 구간 " + mode);
            yield return CollisionComplete("정식 적 " + mode);
            float ahead = Vector3.Dot(endPosition - startPosition, forward);
            samples.Add(new { label = "realEnemy", mode, definition = definition.name, ahead });
            Check(ahead > (mode == 1 ? 3.5f : 4.5f), "정식 적을 지나 회피 거리 확보 " + mode + " " + ahead);
            Check(enemyBodies.All(c => c.enabled && !c.isTrigger), "피격·적 몸 컴포넌트 유지 " + mode);
            Check(body.excludeLayers.value == originalExcluded, "회피 종료 원래 충돌 설정 복귀 " + mode);
        }
        yield return Reset();
        before = actor.transform.position; motor.MoveDirect(forward * 4f);
        Check(Vector3.Dot(actor.transform.position - before, forward) < 2f, "회피 뒤 일반 이동 적 몸 충돌 복귀");

        Collider pair = enemyBodies[0];
        try
        {
            // 플라스크와 같은 개별 쌍 무시가 회피 종료 때 해제되지 않는지 확인한다.
            yield return Reset(); Physics.IgnoreCollision(body, pair, true);
            yield return StartDodge(false); Send();
            yield return CollisionComplete("기존 충돌 무시 쌍");
            Check(Physics.GetIgnoreCollision(body, pair), "기존 IgnoreCollision 소유권 보존");
        }
        finally { if (pair != null) Physics.IgnoreCollision(body, pair, false); }
        spawn.Release(enemy); leased.Remove(enemy); yield return Frames(2);

        yield return Reset(); yield return StartDodge(false); Send();
        evade.CancelForKnockdown();
        Check(!evade.IsEvading && !motor.EvadeEnemyPassThrough && body.excludeLayers.value == originalExcluded, "중도 취소 적 충돌 복원");
        yield return Reset(); yield return StartDodge(false); Send(); evade.enabled = false;
        Check(!motor.EvadeEnemyPassThrough && body.excludeLayers.value == originalExcluded, "회피 컴포넌트 비활성화 충돌 복원");
        evade.enabled = true;
        yield return Reset(); yield return StartDodge(false); Send(); motor.enabled = false;
        Check(!motor.EvadeEnemyPassThrough && body.excludeLayers.value == originalExcluded, "모터 비활성화 충돌 복원");
        evade.CancelForKnockdown(); motor.enabled = true;

        try
        {
            yield return Reset(); body.excludeLayers = originalExcluded | enemyMask | (1 << 5);
            yield return StartDodge(false); Send(); yield return CollisionComplete("기존 레이어 제외");
            Check(body.excludeLayers.value == (originalExcluded | enemyMask | (1 << 5)), "기존 적·다른 레이어 제외 보존");
        }
        finally { motor.EndEvadeMotion(); body.excludeLayers = originalExcluded; }

        yield return Reset();
        var large = EvadeObstacle("largeEnemy", 5f, true);
        yield return StartDodge(false); Send();
        float maxRise = 0f, maxDrop = 0f;
        while (evade.IsEvading)
        {
            maxRise = Mathf.Max(maxRise, actor.transform.position.y - origin.y);
            maxDrop = Mathf.Max(maxDrop, origin.y - actor.transform.position.y); yield return null;
        }
        for (int i = 0; i < 20; i++)
        {
            maxRise = Mathf.Max(maxRise, actor.transform.position.y - origin.y);
            maxDrop = Mathf.Max(maxDrop, origin.y - actor.transform.position.y); yield return null;
        }
        Check(maxRise < .1f && maxDrop < .1f && !motor.EvadeEnemyPassThrough && !motor.StandingOnEnemy,
            "큰 적 몸 안에서 회피 종료·수평 분리·높이 안정 " + maxRise + "/" + maxDrop);
        Vector3 endDelta = actor.transform.position - origin;
        samples.Add(new { label = "endInsideEnemy", maxRise, maxDrop, delta = new[] { endDelta.x, endDelta.y, endDelta.z } });
        UnityEngine.Object.Destroy(large); fixtures.Remove(large); yield return Frames(2);
        yield return VerifyDodgeLightCooldown();
        Progress("EvadeCollisionCompleted");
    }
}
