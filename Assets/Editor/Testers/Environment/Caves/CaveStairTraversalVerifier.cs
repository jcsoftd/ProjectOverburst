using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Caves;
using UnityEngine;

public static class CaveStairTraversalVerifier
{
    public static bool CaptureReady { get; private set; }
    static float XZ(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
    static float[] V(Vector3 p) => new[] { p.x, p.y, p.z };

    public static void Geometry(CaveWorld world, Action<bool, string> check)
    {
        foreach (var connection in world.generatedRoot.GetComponentsInChildren<CaveRigidConnection>())
        {
            foreach (var piece in connection.pieces)
            {
                check(piece.source.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh)
                    .SequenceEqual(piece.instance.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh))
                    && piece.source.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                    .SequenceEqual(piece.instance.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials))
                    && piece.source.transform.localScale == piece.instance.localScale,
                    connection.name + ": original visible meshes, materials and scale preserved");
            }
            if (connection.stairCount == 0)
            { check(!connection.walkCollider && connection.walkPoints.Length == 0, connection.name + ": timber collision path unchanged"); continue; }
            check(connection.walkCollider && connection.BoundaryOpen && connection.walkPoints.Length >= 2
                && connection.pieces.All(p => p.instance.GetComponentsInChildren<Collider>(true).All(c => !c.enabled)),
                connection.name + ": one active continuous stair collider replaces source collision");
            var d = connection.end - connection.start; d.y = 0; d.Normalize();
            foreach (var point in connection.walkPoints)
            {
                check(connection.Sample(point, .03f, .03f, out var sampled) && Mathf.Abs(sampled.y - point.y) < .001f,
                    connection.name + ": guard height matches collision profile expected=" + point + " sampled=" + sampled + " along=" + Vector3.Dot(point-connection.start,d));
                check(connection.walkCollider.Raycast(new Ray(point + Vector3.up * .5f, Vector3.down), out var hit, 1)
                    && Mathf.Abs(hit.point.y - point.y) < .003f, connection.name + ": collider height matches profile");
            }
            for (int i = 1; i < connection.walkPoints.Length; i++)
            {
                var span = connection.walkPoints[i] - connection.walkPoints[i - 1];
                check(Mathf.Atan2(Mathf.Abs(span.y), new Vector2(span.x, span.z).magnitude) * Mathf.Rad2Deg < 45,
                    connection.name + ": stair surface stays inside player slope limit");
            }
            bool enabled = connection.walkCollider.enabled;
            try
            {
                connection.walkCollider.enabled = false;
                check(!connection.BoundaryOpen && !connection.Sample((connection.start + connection.end) * .5f, 10, 10, out _),
                    connection.name + ": missing walk collision closes logical passage");
            }
            finally { connection.walkCollider.enabled = enabled; }
        }
    }

    public static IEnumerator Verify(RunWorldGate gate, string output, Action<bool, string> check)
    {
        if (CaveStairGroundingVerifier.Requested)
        {
            yield return CaveStairGroundingVerifier.Verify(gate, output, check);
            yield break;
        }
        var world = gate.GetComponent<CaveWorld>();
        var actor = PlayerContext.Instance.CurrentActor;
        var motor = actor.GetComponent<OverburstCharacterMotor3D>();
        var movement = actor.GetComponent<PlayerMovement>();
        var cc = motor.Controller;
        var position = actor.transform.position; var rotation = actor.transform.rotation;
        int excluded = cc.excludeLayers.value; bool moveEnabled = movement.enabled;
        var records = new List<object>(); bool pass = false, captureHeld = false; CaptureReady = false;
        var meshes = world.generatedRoot.GetComponentsInChildren<CaveRigidConnection>().Where(c => c.walkCollider).Select(c => c.walkCollider.sharedMesh).ToArray();
        try
        {
            Geometry(world, check);
            movement.enabled = false;
            int enemy = LayerMask.NameToLayer("Enemy");
            if (enemy >= 0) cc.excludeLayers = excluded | (1 << enemy);
            int routes = 0;
            foreach (var connection in world.generatedRoot.GetComponentsInChildren<CaveRigidConnection>().Where(c => c.stairCount > 0))
            {
                var d = connection.end - connection.start; d.y = 0; d.Normalize();
                foreach (bool reverse in new[] { false, true }) foreach (bool dash in new[] { false, true })
                {
                    var a = reverse ? connection.end + d * 1.2f : connection.start - d * 1.2f;
                    var b = reverse ? connection.start - d * 1.2f : connection.end + d * 1.2f;
                    var source = reverse ? connection.boundaryB : connection.boundaryA;
                    check(source.Sample(a, 10, 10, out var ground), connection.name + ": traversal starts on actual platform floor");
                    a.y = ground.y;
                    float foot = cc.center.y - cc.height * .5f;
                    ActorTeleportUtility.TeleportSafely(actor.transform, a + Vector3.up * (.04f - foot), rotation);
                    motor.ResetMotion();
                    if (dash) motor.BeginEvadeMotion();
                    int stuck = 0, frames = 0;
                    try
                    {
                        while (XZ(actor.transform.position, b) > .15f && frames++ < 1000)
                        {
                            var before = actor.transform.position; var toward = b - before; toward.y = 0;
                            float distance = Mathf.Min(toward.magnitude, (dash ? 18f : 6f) * Mathf.Clamp(Time.deltaTime, .01f, .025f));
                            var dt = Mathf.Clamp(Time.deltaTime, .01f, .025f);
                            motor.ProbeGround(dt);
                            if (dash) motor.MoveEvade(toward.normalized * distance);
                            var velocity = dash ? Vector3.zero : toward.normalized * (distance / dt);
                            motor.Step(new MotorStepCommand { currentHorizontalVelocity = velocity, targetHorizontalVelocity = velocity, moveRate = 1000, airControl = 1 }, dt);
                            if (XZ(before, actor.transform.position) < .003f) stuck++; else stuck = 0;
                            if (stuck >= 12) break;
                            if (!captureHeld && !dash && XZ(actor.transform.position, a) > XZ(a, b) * .45f)
                            {
                                captureHeld = true;
                                for (int hold = 0; hold < 15; hold++) yield return null;
                                CaptureReady = true;
                                for (int hold = 0; hold < 60; hold++) yield return null;
                                CaptureReady = false;
                            }
                            yield return null;
                        }
                    }
                    finally { if (dash) motor.EndEvadeMotion(); }
                    int settleFrames = 0, stableFrames = 0;
                    float allowedGroundGap = Mathf.Max(.06f, motor.ActiveSettings.groundSnapDistance);
                    do
                    {
                        float beforeY = actor.transform.position.y;
                        float settleDt = Mathf.Clamp(Time.deltaTime, .01f, .025f);
                        motor.ProbeGround(settleDt);
                        motor.Step(new MotorStepCommand { moveRate = 1000, airControl = 1 }, settleDt);
                        stableFrames = motor.IsGrounded && motor.HasWalkableGround && motor.GroundGap <= allowedGroundGap
                            && Mathf.Abs(actor.transform.position.y - beforeY) < .005f ? stableFrames + 1 : 0;
                        settleFrames++;
                        yield return null;
                    } while (settleFrames < 180 && stableFrames < 6);
                    bool reached = XZ(actor.transform.position, b) < .3f;
                    records.Add(new { connection = connection.name, reverse, dash, reached, frames, settleFrames, stableFrames, allowedGroundGap, grounded = motor.IsGrounded, hasGround = motor.HasWalkableGround, controllerGrounded = cc.isGrounded, groundGap = motor.GroundGap, groundNormal = V(motor.GroundContactNormal), groundHeight = motor.GroundSurfaceHeight, from = V(a), target = V(b), finish = V(actor.transform.position) });
                    File.WriteAllText(Path.Combine(output, "stair-progress.json"), JsonConvert.SerializeObject(records, Formatting.Indented));
                    check(stableFrames >= 6,
                        connection.name + ": actual player lands on walkable destination after traversal");
                    check(reached, connection.name + ": actual player " + (dash ? "dash" : "walk") + " crosses " + (reverse ? "reverse" : "forward"));
                    routes++;
                    // A high-speed request across the rail must still stop on the same deck.
                    var middle = (connection.start + connection.end) * .5f;
                    connection.Sample(middle, 10, 10, out var support);
                    ActorTeleportUtility.TeleportSafely(actor.transform, support + Vector3.up * (.04f - foot), rotation);
                    var outward = new Vector3(-d.z, 0, d.x);
                    motor.MoveDirect(outward * 20);
                    float lateral = Mathf.Abs(Vector3.Dot(actor.transform.position - connection.start, outward));
                    check(lateral <= connection.walkWidth * .5f + .03f, connection.name + ": high-speed side movement stays within stair deck");
                }
            }
            check(routes > 0, "Selected cave contains real stairs for walk and dash regression");
            var boundary = CavePlatformBoundaryVerifier.VerifyWorld(world);
            var boundaryJson = JsonConvert.SerializeObject(boundary, Formatting.Indented);
            File.WriteAllText(Path.Combine(output, "stair-boundary.json"), boundaryJson);
            check((string)Newtonsoft.Json.Linq.JObject.Parse(boundaryJson)["status"] == "PASS", "Installed connector closure, rims, unused ports and allocation regression");
            pass = true;
        }
        finally
        {
            CaptureReady = false; motor.EndEvadeMotion(); cc.excludeLayers = excluded;
            ActorTeleportUtility.TeleportSafely(actor.transform, position, rotation);
            movement.enabled = moveEnabled;
            File.WriteAllText(Path.Combine(output, "stair-traversal.json"), JsonConvert.SerializeObject(new { status = pass ? "PASS" : "FAIL", world.seed, courts = world.courts.Count, records, meshInstanceIds = meshes.Select(m => m.GetInstanceID()), restored = movement.enabled == moveEnabled && cc.excludeLayers.value == excluded && XZ(actor.transform.position, position) < .01f,
                scope = "Real map entry and player centre-line walking/dashing per frame in both directions; enemy body contact excluded only during traversal; native checks cover lateral lanes and source visual identity." }, Formatting.Indented));
        }
    }
}
