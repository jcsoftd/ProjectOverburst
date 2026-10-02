using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

/// <summary>격리 제품 Play에서 원본 대검 모션을 강제 왼손 IK가 덮지 않는지 확인한다.</summary>
public static class GreatswordGripVerifier
{
    const string WeaponPath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset";

    public static void Begin(string outputDirectory)
    {
        string output = IsolatedSavePlayGuard.ValidateDirectory(outputDirectory);
        string account = Overburst.Persistence.AccountBootstrap.SaveDirectory;
        if (!EditorApplication.isPlaying || !Overburst.Persistence.AccountBootstrap.Ready
            || string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !Path.GetFullPath(account).StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("제품 부팅이 끝난 격리 Play에서만 실행할 수 있습니다.");
        var actor = PlayerContext.GetOrCreate().CurrentActor;
        if (actor == null || actor.GetComponent<PlayerLeftHandGrip>() == null
            || actor.GetComponent<MeleeRuntime>() == null)
            throw new InvalidOperationException("플레이어·대검 런타임 참조가 필요합니다.");
        Directory.CreateDirectory(output);
        actor.StartCoroutine(Run(actor, output));
    }

    static IEnumerator Run(PlayerActorRuntime actor, string output)
    {
        var checks = new List<object>();
        var actions = new List<object>();
        int failures = 0;
        void Check(bool passed, string name)
        {
            checks.Add(new { name, passed });
            if (!passed) failures++;
        }

        var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>(WeaponPath);
        var grip = actor.GetComponent<PlayerLeftHandGrip>();
        var melee = actor.GetComponent<MeleeRuntime>();
        var mode = PlayerCombatModeController.GetOrCreate();
        var animator = grip.TargetAnimator;
        Check(weapon != null && !weapon.GetMeleeGripSettings().enabled, "Saved greatsword grip disabled");
        if (weapon == null || animator == null || !animator.isHuman)
        {
            Check(false, "Weapon and humanoid animator available");
            File.WriteAllText(Path.Combine(output, "Result.json"), JsonConvert.SerializeObject(new { failures, checks }, Formatting.Indented));
            yield break;
        }

        melee.CancelCurrentAttackState();
        Check(actor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common)), "Equip fixture in isolated account");
        melee.SetManualInputEnabled(true);
        mode.ExitCombatMode(PlayerCombatModeReason.System);
        yield return new WaitForSeconds(.6f);
        Check(grip.isActiveAndEnabled && grip.CurrentWeight <= .001f, "Exploration keeps component active with zero grip weight");

        mode.EnterCombatMode(PlayerCombatModeReason.System);
        // 무기 꺼내기 전환이 끝난 실제 대기 프레임을 검사한다.
        float idleDeadline = Time.unscaledTime + 8f;
        while (Time.unscaledTime < idleDeadline)
        {
            bool idle = Enumerable.Range(0, animator.layerCount).SelectMany(animator.GetCurrentAnimatorClipInfo)
                .Any(c => c.weight > .99f && c.clip.name == "idle");
            if (idle) break;
            yield return null;
        }
        yield return new WaitForEndOfFrame();
        float idleDistance = Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.LeftHand).position,
            animator.GetBoneTransform(HumanBodyBones.RightHand).position);
        Check(mode.IsCombatModeActive && grip.isActiveAndEnabled && grip.CurrentWeight <= .001f, "Combat idle does not enable forced grip");
        Check(idleDistance > .25f, "Combat idle hands remain apart");
        var idleClips = Enumerable.Range(0, animator.layerCount).SelectMany(animator.GetCurrentAnimatorClipInfo)
            .Where(c => c.weight > .01f).Select(c => c.clip.name).Distinct().ToArray();
        Check(idleClips.Contains("idle"), "Original greatsword idle clip running");
        Texture2D image = null;
        try
        {
            image = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(output, "CombatIdle.png"), image.EncodeToPNG());
        }
        catch (Exception error) { Check(false, "Idle capture: " + error.Message); }
        finally { if (image != null) UnityEngine.Object.Destroy(image); }

        for (int index = 0; index < 5; index++)
        {
            bool heavy = index == 4;
            mode.EnterCombatMode(PlayerCombatModeReason.System);
            WeaponActionResult accepted = heavy ? melee.TryStartHeavyAttack(Vector3.forward)
                : melee.TryStartAction(new WeaponActionRequest(WeaponActionSource.PlayerInput, null, Vector3.forward), out _);
            Check(accepted == WeaponActionResult.Accepted, (heavy ? "Heavy" : "Weak " + (index + 1)) + " accepted");
            float deadline = Time.unscaledTime + 12f;
            float maxWeight = 0f;
            int frames = 0;
            var clips = new HashSet<string>();
            while (melee.IsAttackInProgress && Time.unscaledTime < deadline)
            {
                yield return new WaitForEndOfFrame();
                frames++;
                maxWeight = Mathf.Max(maxWeight, grip.CurrentWeight);
                for (int layer = 0; layer < animator.layerCount; layer++)
                    foreach (var clip in animator.GetCurrentAnimatorClipInfo(layer))
                        if (clip.weight > .01f) clips.Add(clip.clip.name);
            }
            Check(!melee.IsAttackInProgress && frames > 0, (heavy ? "Heavy" : "Weak " + (index + 1)) + " completes");
            Check(grip.isActiveAndEnabled && maxWeight <= .001f, (heavy ? "Heavy" : "Weak " + (index + 1)) + " has no forced grip throughout");
            actions.Add(new { heavy, accepted = accepted.ToString(), frames, maxWeight, clips = clips.OrderBy(c => c).ToArray() });
            if (melee.IsAttackInProgress) melee.CancelCurrentAttackState();
            yield return new WaitForSeconds(.1f);
        }

        mode.ExitCombatMode(PlayerCombatModeReason.System);
        yield return new WaitForSeconds(.6f);
        Check(!mode.IsCombatModeActive && grip.CurrentWeight <= .001f, "Combat exit keeps grip off");
        File.WriteAllText(Path.Combine(output, "Result.json"), JsonConvert.SerializeObject(new
        {
            status = failures == 0 ? "PASS" : "FAIL", failures, checks, idleDistance, idleClips, actions,
            weapon = WeaponPath, definition = AssetDatabase.GetAssetPath(weapon.GetMeleeDefinition()),
            account = Overburst.Persistence.AccountBootstrap.SaveDirectory
        }, Formatting.Indented));
        Debug.Log("[대검 IK 검증] " + (failures == 0 ? "PASS" : "FAIL") + " · " + checks.Count + "항목");
    }
}
