using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Overburst.EditorTools.BossMaker
{
    internal sealed class BossMakerPreview : IDisposable
    {
        readonly CrustaspikanMaterialPreview stage;
        readonly EnemyBossMaterialCollection collection;
        readonly List<Vector3> launches = new List<Vector3>();
        BossMakerDraft draft;
        AnimationClip support;
        Vector3 displacement;
        float zoom = 1f;
        Vector3 pan;
        public bool Playing, Loop = true, ShowGeometry = true, ShowAllStrikes = true, ShowHeight;
        public float Progress { get; private set; }
        public float Elapsed { get; private set; }
        public float BaseSpeed = 1f, AuditionSpeed = 1f, TargetDistance = 12f;
        public int Strike;
        public bool FreezeFrame;
        public BossMakerDraft ActiveDraft => draft;
        public Camera Camera => stage.Camera;
        public AnimationClip Clip => support != null ? support : draft?.Material.runtimeClip;
        public Bounds BodyBounds => stage.BodyBounds;
        public float Duration => Clip == null ? 0f : support != null ? Clip.length
            : draft.Ability.ResolvePacedTime(1f, draft.Material.AnimationSpeedMultiplier * BaseSpeed);
        public float TotalDuration => draft != null && support == null && draft.Material.delivery != EnemyBossMaterialDelivery.Melee
            ? Mathf.Max(Duration, draft.Material.strikes.Max(s => draft.Ability.ResolvePacedTime(s.impact, draft.Material.AnimationSpeedMultiplier * BaseSpeed))
                + (draft.Material.delivery == EnemyBossMaterialDelivery.Boulder ? draft.Material.flightSeconds : draft.Ability.Range / draft.Material.projectileSpeed)) : Duration;
        public Vector3 Target => new Vector3(0f, 0f, TargetDistance);

        public BossMakerPreview(EnemyBossMaterialCollection data) { collection = data; stage = new CrustaspikanMaterialPreview(data); }
        public void Load(BossMakerDraft item, AnimationClip motion = null)
        {
            draft = item; support = motion; Playing = false; Strike = Mathf.Clamp(Strike, 0, item.Material.strikes.Length - 1);
            RebuildLaunches(); Seek(0f);
        }
        public void Changed()
        {
            float at = Progress; RebuildLaunches(); Seek(at);
        }
        void ClearOffset() { if (displacement != Vector3.zero) stage.OffsetActor(-displacement); displacement = Vector3.zero; }
        public void Seek(float normalized)
        {
            if (Clip == null) return;
            Progress = Mathf.Clamp01(normalized);
            Elapsed = support != null ? Progress * Clip.length : draft.Ability.ResolvePacedTime(Progress, draft.Material.AnimationSpeedMultiplier * BaseSpeed);
            Sample();
        }
        void Sample()
        {
            ClearOffset(); stage.Sample(Clip, Progress * Clip.length);
            if (support == null)
            {
                var m = draft.Material;
                float advance = m.advanceDistance > 0 && m.advanceWindow.y > m.advanceWindow.x
                    ? Mathf.InverseLerp(m.advanceWindow.x, m.advanceWindow.y, Progress) * m.advanceDistance : 0f;
                displacement = Vector3.forward * advance; stage.OffsetActor(displacement);
                if (m.delivery == EnemyBossMaterialDelivery.Boulder) stage.SetHeldRockPreview(Progress < m.strikes[0].impact);
            }
        }
        public void Tick(float delta)
        {
            if (!Playing || Clip == null) return;
            Elapsed += Mathf.Min(.15f, delta) * AuditionSpeed;
            if (Elapsed > TotalDuration)
            {
                if (Loop) Elapsed %= Mathf.Max(.01f, TotalDuration);
                else { Elapsed = TotalDuration; Playing = false; }
            }
            Progress = support != null ? Mathf.Clamp01(Elapsed / Clip.length) : draft.ProgressAtTime(Elapsed, BaseSpeed);
            Sample();
        }
        void RebuildLaunches()
        {
            launches.Clear(); if (draft == null || support != null || draft.Material.delivery == EnemyBossMaterialDelivery.Melee) return;
            var m = draft.Material; ClearOffset();
            foreach (var strike in m.strikes)
            {
                stage.Sample(m.runtimeClip, strike.impact * m.runtimeClip.length);
                var rig = stage.AnimationRoot;
                Vector3 start;
                if (m.delivery == EnemyBossMaterialDelivery.Boulder)
                {
                    var left = rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == collection.boulderLeftHandBone);
                    var right = rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == collection.boulderRightHandBone);
                    if (left == null || right == null) { launches.Clear(); return; }
                    start = (left.position + right.position) * .5f + collection.boulderOffset;
                }
                else
                {
                    var muzzle = rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == m.muzzleBone);
                    if (muzzle == null) { launches.Clear(); return; }
                    start = muzzle.TransformPoint(m.muzzleOffset);
                }
                launches.Add(start);
            }
        }
        public void Orbit(Vector2 delta) { stage.Yaw += delta.x * .4f; stage.Pitch = Mathf.Clamp(stage.Pitch + delta.y * .3f, -10f, 89f); }
        public void Zoom(float wheel) => zoom = Mathf.Clamp(zoom * Mathf.Exp(-wheel * .06f), .35f, 4f);
        public void Pan(Vector2 delta) { pan += Camera.transform.right * (-delta.x * .012f) + Camera.transform.up * (delta.y * .012f); }
        public void View(int mode)
        {
            zoom = 1; pan = Vector3.zero;
            stage.Yaw = mode == 2 ? 90 : mode == 0 ? 25 : 0;
            stage.Pitch = mode == 3 ? 86 : mode == 0 ? 25 : 14;
        }
        public void Fit() { zoom = 1f; pan = Vector3.zero; }
        public RenderTexture Render(int width, int height)
        {
            var position = Camera.transform.position; var rotation = Camera.transform.rotation;
            var surface = stage.Render(width, height); var bounds = stage.FrameBounds;
            if (FreezeFrame)
            { Camera.transform.SetPositionAndRotation(position, rotation); Camera.nearClipPlane = .02f; Camera.farClipPlane = 200f; RenderPipeline.SubmitRenderRequest(Camera, new UniversalRenderPipeline.SingleCameraRequest { destination = surface }); return surface; }
            if (ShowGeometry && support == null)
            {
                for (int i = 0; i < draft.Material.strikes.Length; i++)
                    if (ShowAllStrikes || i == Strike)
                        foreach (var outline in Outlines(i)) foreach (var point in outline) bounds.Encapsulate(point);
                foreach (var trajectory in Trajectories()) foreach (var point in trajectory) bounds.Encapsulate(point);
            }
            var camera = Camera; var inverse = Quaternion.Inverse(camera.transform.rotation);
            float vertical = Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad), horizontal = vertical * camera.aspect, distance = .1f;
            for (int i = 0; i < 8; i++)
            {
                var offset = inverse * Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                distance = Mathf.Max(distance, Mathf.Abs(offset.x) / (horizontal * .88f) - offset.z, Mathf.Abs(offset.y) / (vertical * .88f) - offset.z);
            }
            distance /= zoom; camera.transform.position = bounds.center + pan - camera.transform.forward * distance;
            camera.nearClipPlane = .02f; camera.farClipPlane = Mathf.Max(100f, distance + bounds.extents.magnitude * 4f);
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = surface });
            return surface;
        }
        public Vector2 Project(Vector3 point, Vector2 size)
        { var p = Camera.WorldToViewportPoint(point); return new Vector2(p.x * size.x, (1f - p.y) * size.y); }
        public Vector3 Origin(int phase)
        {
            if (draft.Material.delivery == EnemyBossMaterialDelivery.Boulder) return Target;
            if (draft.Material.delivery == EnemyBossMaterialDelivery.Spit && phase < launches.Count)
            { var p = launches[phase]; p.y = 0f; return p; }
            return draft.Material.strikes[phase].localOrigin + displacement;
        }
        public Quaternion Rotation(int phase)
        {
            if (draft.Material.delivery == EnemyBossMaterialDelivery.Spit)
            { var d = Target - Origin(phase); d.y = 0f; if (d.sqrMagnitude > .001f) return Quaternion.LookRotation(d); }
            return Quaternion.Euler(0, draft.Material.strikes[phase].yaw, 0);
        }
        public IEnumerable<Vector3[]> Outlines(int phase)
        {
            var s = draft.Material.strikes[phase]; var origin = Origin(phase) + Vector3.up * .045f; var rotation = Rotation(phase);
            if (s.shape == GroundIndicatorShape.Rectangle)
            { yield return new[] { new Vector3(-s.width * .5f, 0, 0), new Vector3(s.width * .5f, 0, 0), new Vector3(s.width * .5f, 0, s.length), new Vector3(-s.width * .5f, 0, s.length) }.Select(p => origin + rotation * p).ToArray(); yield break; }
            float angle = s.shape == GroundIndicatorShape.Sector ? s.angle : 360f;
            var outer = Enumerable.Range(0, 65).Select(i => { float a = (-angle * .5f + angle * i / 64f) * Mathf.Deg2Rad; return origin + rotation * new Vector3(Mathf.Sin(a) * s.radius, 0f, Mathf.Cos(a) * s.radius); }).ToArray();
            if (s.shape == GroundIndicatorShape.Sector)
            { var inner = Enumerable.Range(0, 65).Select(i => { float a = (angle * .5f - angle * i / 64f) * Mathf.Deg2Rad; return origin + rotation * new Vector3(Mathf.Sin(a) * s.innerRadius, 0f, Mathf.Cos(a) * s.innerRadius); }); yield return outer.Concat(inner).ToArray(); }
            else
            {
                yield return outer;
                if (s.shape == GroundIndicatorShape.Donut) yield return Enumerable.Range(0, 65).Select(i => { float a = -2f * Mathf.PI * i / 64f; return origin + new Vector3(Mathf.Sin(a) * s.innerRadius, 0f, Mathf.Cos(a) * s.innerRadius); }).ToArray();
            }
        }
        public IEnumerable<Vector3[]> Trajectories()
        {
            if (draft == null || support != null || draft.Material.delivery == EnemyBossMaterialDelivery.Melee) yield break;
            for (int phase = 0; phase < launches.Count; phase++)
            {
                var m = draft.Material; var start = launches[phase];
                if (m.delivery == EnemyBossMaterialDelivery.Boulder)
                    yield return Enumerable.Range(0, 41).Select(i => Arc(start, Target, i / 40f, m.arcHeight)).ToArray();
                else
                { var direction = (Target + Vector3.up * .9f - start).normalized; yield return new[] { start, start + direction * m.ability.Range }; }
            }
        }
        static Vector3 Arc(Vector3 start, Vector3 end, float t, float height) => Vector3.Lerp(start, end, t) + Vector3.up * (4f * height * t * (1f - t));
        public IEnumerable<Vector3> ProjectilePositions()
        {
            if (draft == null || support != null) yield break;
            for (int phase = 0; phase < launches.Count; phase++)
            {
                var m = draft.Material; float age = Elapsed - draft.Ability.ResolvePacedTime(m.strikes[phase].impact, m.AnimationSpeedMultiplier * BaseSpeed);
                if (age < 0f) continue;
                if (m.delivery == EnemyBossMaterialDelivery.Boulder && age <= m.flightSeconds) yield return Arc(launches[phase], Target, age / m.flightSeconds, m.arcHeight);
                if (m.delivery == EnemyBossMaterialDelivery.Spit && age * m.projectileSpeed <= m.ability.Range) yield return launches[phase] + (Target + Vector3.up * .9f - launches[phase]).normalized * age * m.projectileSpeed;
            }
        }
        public void Dispose() { Playing = false; stage.Dispose(); }
    }
}
