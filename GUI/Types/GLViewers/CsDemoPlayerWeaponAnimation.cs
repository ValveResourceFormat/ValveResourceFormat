using System.Linq;
using System.Runtime.CompilerServices;
using ValveResourceFormat.DemoPlayback;
using ValveResourceFormat.Renderer.SceneNodes;
namespace GUI.Types.GLViewers;

static class CsDemoPlayerWeaponAnimation
{
    private const float AirborneJumpHoldFraction = 0.85f;

    private enum WeaponActionKind
    {
        Draw,
        Fire,
        Throw,
    }

    private sealed class WeaponActionState
    {
        public string? ActiveClip { get; set; }
        public WeaponActionKind ActiveKind { get; set; }
    }

    private static readonly ConditionalWeakTable<ModelSceneNode, WeaponActionState> WeaponActionStates = new();

    public static float GetAirborneJumpSustainTime(ModelSceneNode modelNode, string jumpClip)
    {
        var maxTime = modelNode.AnimationController.GetClipMaxTime(jumpClip);
        return maxTime.HasValue ? maxTime.Value * AirborneJumpHoldFraction : 0f;
    }

    public static void Apply(
        ModelSceneNode modelNode,
        CsDemoPlayerWeaponAnimationGroup weaponGroup,
        bool weaponChanged,
        bool firedShot,
        bool threwGrenade,
        IReadOnlyList<CsDemoPlayerAnimEvent> animEvents,
        bool isPlaying)
    {
        if (!isPlaying)
        {
            return;
        }

        var requestedWeaponGroup = weaponGroup;
        CsDemoPlayerModelSetup.EnsureWeaponActionBoneMaskRegistered(modelNode);
        var state = WeaponActionStates.GetOrCreateValue(modelNode);
        var controller = modelNode.AnimationController;

        var deployFromNet = animEvents.Any(static e => e.Kind == CsDemoPlayerAnimEventKind.Deploy);
        var fireFromNet = animEvents.Any(static e => e.Kind is CsDemoPlayerAnimEventKind.FirePrimary or CsDemoPlayerAnimEventKind.FireSecondary);
        var throwFromNet = animEvents.Any(static e => e.Kind == CsDemoPlayerAnimEventKind.ThrowGrenade);
        var reloadFromNet = animEvents.Any(static e => e.Kind == CsDemoPlayerAnimEventKind.Reload);
        var knifeFromNet = animEvents.Any(static e => e.Kind is CsDemoPlayerAnimEventKind.KnifeHit or CsDemoPlayerAnimEventKind.KnifeMiss);

        firedShot |= fireFromNet;
        threwGrenade |= throwFromNet;
        weaponChanged |= deployFromNet;

        if (state.ActiveClip is { } activeClip
            && controller.IsClipFinished(activeClip))
        {
            controller.SetAnimationWeight(activeClip, 0f);
            state.ActiveClip = null;
            CsDemoPlayerModelSetup.EnsureMixerActiveClip(modelNode);
            // #region agent log
            AgentDebugLog.Write(
                "H10",
                "GUI/Types/GLViewers/CsDemoPlayerWeaponAnimation.cs:ApplyCleared",
                "weapon action clip finished and cleared",
                new
                {
                    runId = "post-fix4",
                    clip = activeClip,
                    maxTime = controller.GetClipMaxTime(activeClip),
                    time = controller.GetClipTime(activeClip),
                });
            // #endregion
        }

        var pendingAction = weaponChanged || firedShot || threwGrenade;
        if (state.ActiveClip is { } blockingClip && pendingAction && !controller.IsClipFinished(blockingClip))
        {
            controller.SetAnimationWeight(blockingClip, 0f);
            state.ActiveClip = null;
            // #region agent log
            AgentDebugLog.Write(
                "H10",
                "GUI/Types/GLViewers/CsDemoPlayerWeaponAnimation.cs:ApplyPreempted",
                "preempted in-progress weapon action clip for new action",
                new
                {
                    runId = "post-fix5",
                    clip = blockingClip,
                    weaponChanged,
                    firedShot,
                    threwGrenade,
                    time = controller.GetClipTime(blockingClip),
                    maxTime = controller.GetClipMaxTime(blockingClip),
                });
            // #endregion
        }

        if (state.ActiveClip != null)
        {
            return;
        }

        string? clip = null;
        WeaponActionKind kind = default;

        if (firedShot && TryResolveActionClip(modelNode, requestedWeaponGroup, WeaponActionKind.Fire, out var fireClip))
        {
            clip = fireClip;
            kind = WeaponActionKind.Fire;
        }
        else if (knifeFromNet && TryResolveActionClip(modelNode, requestedWeaponGroup, WeaponActionKind.Fire, out var knifeClip))
        {
            clip = knifeClip;
            kind = WeaponActionKind.Fire;
        }
        else if (reloadFromNet && TryResolveActionClip(modelNode, requestedWeaponGroup, WeaponActionKind.Draw, out var reloadAsDrawClip))
        {
            clip = reloadAsDrawClip;
            kind = WeaponActionKind.Draw;
        }
        else if (threwGrenade && TryResolveActionClip(modelNode, requestedWeaponGroup, WeaponActionKind.Throw, out var throwClip))
        {
            clip = throwClip;
            kind = WeaponActionKind.Throw;
        }
        else if (weaponChanged && TryResolveActionClip(modelNode, requestedWeaponGroup, WeaponActionKind.Draw, out var drawClip))
        {
            clip = drawClip;
            kind = WeaponActionKind.Draw;
        }
        else if (firedShot || threwGrenade || weaponChanged)
        {
            var proceduralKind = threwGrenade ? WeaponActionKind.Throw : firedShot ? WeaponActionKind.Fire : WeaponActionKind.Draw;
            PlayProceduralAction(modelNode, proceduralKind);
            // #region agent log
            AgentDebugLog.Write(
                "H7",
                "GUI/Types/GLViewers/CsDemoPlayerWeaponAnimation.cs:ApplyProceduralFallback",
                "weapon action used procedural bone fallback",
                new
                {
                    runId = "post-fix4",
                    kind = proceduralKind.ToString(),
                    firedShot,
                    threwGrenade,
                    weaponChanged,
                    deployFromNet,
                    fireFromNet,
                    throwFromNet,
                    reloadFromNet,
                    knifeFromNet,
                    requestedWeapon = requestedWeaponGroup.Item,
                    requestedDefaultWeapon = requestedWeaponGroup.DefaultItem,
                    locomotionFallback = CsDemoPlayerModelSetup.UsesWeaponAnimFallback(modelNode, requestedWeaponGroup),
                    animationCount = modelNode.Animations.Count,
                });
            // #endregion
        }

        if (clip == null)
        {
            CsDemoPlayerModelSetup.EnsureMixerActiveClip(modelNode);
            return;
        }

        if (!CsDemoPlayerModelSetup.TryEnsureClipLoaded(modelNode, clip)
            || !modelNode.Animations.TryGetValue(clip, out var animation)
            || !controller.EnsureClipRegistered(animation, looping: false))
        {
            CsDemoPlayerModelSetup.EnsureMixerActiveClip(modelNode);
            return;
        }

        controller.SetAnimationProperties(
            clip,
            0f,
            looping: false,
            boneMask: CsDemoPlayerModelSetup.WeaponActionBoneMask);
        controller.SetAnimationWeight(clip, 1f, restartIfNew: true);
        state.ActiveClip = clip;
        state.ActiveKind = kind;
        PlayProceduralAction(modelNode, kind);

        var drawResolvedToShoot = kind == WeaponActionKind.Draw
            && clip.Contains("/shoot_", StringComparison.OrdinalIgnoreCase)
            && !CsDemoPlayerModelSetup.UsesWeaponAnimFallback(modelNode, requestedWeaponGroup);
        if (drawResolvedToShoot)
        {
            AgentDebugLog.Write(
                "WATCHDOG_FAIL,H12",
                "GUI/Types/GLViewers/CsDemoPlayerWeaponAnimation.cs:Apply",
                "WATCHDOG_FAIL action issue: weapon switch resolved to shoot clip",
                new
                {
                    kind = kind.ToString(),
                    clip,
                    requestedWeapon = requestedWeaponGroup.Item,
                    requestedDefaultWeapon = requestedWeaponGroup.DefaultItem,
                    deployFromNet,
                    weaponChanged,
                    fallback = CsDemoPlayerModelSetup.UsesWeaponAnimFallback(modelNode, requestedWeaponGroup),
                });
        }

        // #region agent log
        AgentDebugLog.Write(
            "H7",
            "GUI/Types/GLViewers/CsDemoPlayerWeaponAnimation.cs:Apply",
            "started weapon action clip",
            new
            {
                runId = "post-fix4",
                kind = kind.ToString(),
                clip,
                requestedWeapon = requestedWeaponGroup.Item,
                requestedDefaultWeapon = requestedWeaponGroup.DefaultItem,
                deployFromNet,
                fireFromNet,
                throwFromNet,
                reloadFromNet,
                knifeFromNet,
                firedShot,
                threwGrenade,
                weaponChanged,
                isWorldClip = clip.Contains("/world/", StringComparison.OrdinalIgnoreCase),
                isViewmodelClip = IsViewmodelClip(clip),
                fallback = CsDemoPlayerModelSetup.UsesWeaponAnimFallback(modelNode, requestedWeaponGroup),
                boneMask = CsDemoPlayerModelSetup.WeaponActionBoneMask,
            });
        // #endregion
    }

    private static bool TryResolveActionClip(
        ModelSceneNode modelNode,
        CsDemoPlayerWeaponAnimationGroup requestedWeaponGroup,
        WeaponActionKind kind,
        out string clip)
    {
        if (requestedWeaponGroup.Item == "knife")
        {
            clip = string.Empty;
            CsDemoPlayerModelSetup.EnsureMixerActiveClip(modelNode);
            return false;
        }

        foreach (var weaponGroup in EnumerateActionGroups(modelNode, requestedWeaponGroup, kind))
        {
            foreach (var candidate in GetActionClipCandidates(weaponGroup, kind))
            {
                if (IsViewmodelClip(candidate)
                    || !CsDemoPlayerModelSetup.TryEnsureClipLoaded(modelNode, candidate)
                    || !modelNode.Animations.TryGetValue(candidate, out var animation)
                    || !modelNode.AnimationController.CanDriveAnimation(animation)
                    || !modelNode.AnimationController.EnsureClipRegistered(animation, looping: false))
                {
                    continue;
                }

                modelNode.AnimationController.SetAnimationProperties(
                    candidate,
                    0f,
                    looping: false,
                    boneMask: CsDemoPlayerModelSetup.WeaponActionBoneMask);
                modelNode.AnimationController.SetAnimationWeight(candidate, 0f);
                clip = candidate;
                return true;
            }
        }

        clip = string.Empty;
        CsDemoPlayerModelSetup.EnsureMixerActiveClip(modelNode);
        return false;
    }

    private static IEnumerable<CsDemoPlayerWeaponAnimationGroup> EnumerateActionGroups(
        ModelSceneNode modelNode,
        CsDemoPlayerWeaponAnimationGroup requestedWeaponGroup,
        WeaponActionKind kind)
    {
        yield return requestedWeaponGroup;

        var resolvedWeaponGroup = CsDemoPlayerModelSetup.ResolveWeaponGroupForActions(modelNode, requestedWeaponGroup);
        if (!string.Equals(resolvedWeaponGroup.Item, requestedWeaponGroup.Item, StringComparison.Ordinal)
            || !string.Equals(resolvedWeaponGroup.DefaultItem, requestedWeaponGroup.DefaultItem, StringComparison.Ordinal))
        {
            yield return resolvedWeaponGroup;
        }

        if (kind != WeaponActionKind.Draw
            && !IsSameGroup(requestedWeaponGroup, CsDemoPlayerWeaponAnimationGroup.Rifle)
            && !IsSameGroup(resolvedWeaponGroup, CsDemoPlayerWeaponAnimationGroup.Rifle))
        {
            yield return CsDemoPlayerWeaponAnimationGroup.Rifle;
        }
    }

    private static bool IsSameGroup(
        CsDemoPlayerWeaponAnimationGroup left,
        CsDemoPlayerWeaponAnimationGroup right)
        => string.Equals(left.Item, right.Item, StringComparison.Ordinal)
            && string.Equals(left.DefaultItem, right.DefaultItem, StringComparison.Ordinal);

    private static bool IsViewmodelClip(string clip)
        => clip.Contains("/viewmodel/", StringComparison.OrdinalIgnoreCase);

    private static void PlayProceduralAction(ModelSceneNode modelNode, WeaponActionKind kind)
    {
        var duration = kind switch
        {
            WeaponActionKind.Fire => 0.18f,
            WeaponActionKind.Throw => 0.65f,
            _ => 0.42f,
        };

        static Quaternion Rot(float x, float y, float z)
            => Quaternion.CreateFromAxisAngle(Vector3.UnitX, float.DegreesToRadians(x))
                * Quaternion.CreateFromAxisAngle(Vector3.UnitY, float.DegreesToRadians(y))
                * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, float.DegreesToRadians(z));

        var rotations = kind switch
        {
            WeaponActionKind.Fire => new Dictionary<string, Quaternion>
            {
                ["arm_upper_r"] = Rot(-12f, -8f, -8f),
                ["arm_lower_r"] = Rot(-18f, 0f, 10f),
                ["hand_r"] = Rot(-10f, 0f, 12f),
                ["arm_upper_l"] = Rot(-8f, 8f, 8f),
                ["arm_lower_l"] = Rot(-10f, 0f, -8f),
                ["hand_l"] = Rot(-8f, 0f, -10f),
            },
            WeaponActionKind.Throw => new Dictionary<string, Quaternion>
            {
                ["spine_2"] = Rot(0f, 0f, 8f),
                ["arm_upper_r"] = Rot(-62f, 18f, -25f),
                ["arm_lower_r"] = Rot(-58f, 0f, 30f),
                ["hand_r"] = Rot(-35f, 0f, 25f),
                ["arm_upper_l"] = Rot(24f, -10f, 18f),
                ["arm_lower_l"] = Rot(28f, 0f, -18f),
                ["hand_l"] = Rot(16f, 0f, -20f),
            },
            _ => new Dictionary<string, Quaternion>
            {
                ["spine_2"] = Rot(0f, 0f, -5f),
                ["arm_upper_r"] = Rot(28f, -16f, -18f),
                ["arm_lower_r"] = Rot(40f, 0f, 22f),
                ["hand_r"] = Rot(25f, 0f, 18f),
                ["arm_upper_l"] = Rot(-22f, 12f, 18f),
                ["arm_lower_l"] = Rot(-30f, 0f, -18f),
                ["hand_l"] = Rot(-22f, 0f, -20f),
            },
        };

        modelNode.AnimationController.PlayProceduralBoneRotationOverlay("WeaponAction", rotations, duration);
    }

    private static IEnumerable<string> GetActionClipCandidates(
        CsDemoPlayerWeaponAnimationGroup weaponGroup,
        WeaponActionKind kind)
    {
        var item = weaponGroup.Item;
        var defaultItem = weaponGroup.DefaultItem;

        switch (kind)
        {
            case WeaponActionKind.Draw:
                foreach (var candidate in GetDrawClipCandidates(item, defaultItem))
                {
                    yield return candidate;
                }

                break;

            case WeaponActionKind.Fire:
                foreach (var candidate in GetFireClipCandidates(item, defaultItem))
                {
                    yield return candidate;
                }

                break;

            case WeaponActionKind.Throw:
                foreach (var candidate in GetThrowClipCandidates(item, defaultItem))
                {
                    yield return candidate;
                }

                break;
        }
    }

    private static IEnumerable<string> GetDrawClipCandidates(string item, string defaultItem)
    {
        if (item == "grenade")
        {
            yield return "animation/anims/viewmodel/grenade/_default_grenade/pullpin_grenade.vnmclip";
        }

        if (item == "knife")
        {
            yield return "animation/anims/viewmodel/knife/knife_karambit/draw_karambit.vnmclip";
        }

        var worldBasePath = $"animation/anims/world/{item}/_default_{defaultItem}/";
        yield return $"{worldBasePath}draw_{item}.vnmclip";
        yield return $"{worldBasePath}deploy_{item}.vnmclip";
        if (item != defaultItem)
        {
            yield return $"{worldBasePath}draw_{defaultItem}.vnmclip";
        }

        foreach (var viewmodelBasePath in GetViewmodelBasePaths(item, defaultItem))
        {
            foreach (var suffix in GetViewmodelClipSuffixes(viewmodelBasePath, item, defaultItem))
            {
                yield return $"{viewmodelBasePath}draw_{suffix}.vnmclip";
                yield return $"{viewmodelBasePath}deploy_{suffix}.vnmclip";
            }
        }
    }

    private static IEnumerable<string> GetFireClipCandidates(string item, string defaultItem)
    {
        var worldBasePath = $"animation/anims/world/{item}/_default_{defaultItem}/";
        yield return $"{worldBasePath}shoot1_{item}.vnmclip";
        yield return $"{worldBasePath}fire_{item}.vnmclip";
        yield return $"{worldBasePath}shoot_{item}.vnmclip";
        if (item != defaultItem)
        {
            yield return $"{worldBasePath}shoot1_{defaultItem}.vnmclip";
        }

        foreach (var viewmodelBasePath in GetViewmodelBasePaths(item, defaultItem))
        {
            foreach (var suffix in GetViewmodelClipSuffixes(viewmodelBasePath, item, defaultItem))
            {
                yield return $"{viewmodelBasePath}shoot1_{suffix}.vnmclip";
                yield return $"{viewmodelBasePath}fire_{suffix}.vnmclip";
                yield return $"{viewmodelBasePath}shoot_{suffix}.vnmclip";
            }
        }

        if (item == "knife")
        {
            yield return "animation/anims/viewmodel/knife/knife_karambit/light_miss1_karambit.vnmclip";
            yield return "animation/anims/viewmodel/knife/knife_karambit/heavy_miss1_karambit.vnmclip";
        }
    }

    private static IEnumerable<string> GetThrowClipCandidates(string item, string defaultItem)
    {
        var worldBasePath = $"animation/anims/world/{item}/_default_{defaultItem}/";
        yield return $"{worldBasePath}throw_{defaultItem}.vnmclip";
        yield return $"{worldBasePath}throw_{item}.vnmclip";
        yield return $"{worldBasePath}throw_grenade.vnmclip";
        yield return $"animation/anims/world/grenade/_default_{defaultItem}/throw_{defaultItem}.vnmclip";
        yield return "animation/anims/world/rifle/_default_rifle/throw_rifle.vnmclip";

        yield return "animation/anims/viewmodel/grenade/_default_grenade/throw_underhand_grenade.vnmclip";
        yield return "animation/anims/viewmodel/grenade/_default_grenade/pullpin_grenade.vnmclip";
    }

    private static IEnumerable<string> GetViewmodelBasePaths(string item, string defaultItem)
    {
        yield return $"animation/anims/viewmodel/{item}/_default_{defaultItem}/";

        if (item == "knife")
        {
            yield return "animation/anims/viewmodel/knife/knife_karambit/";
        }

        if (item == "grenade")
        {
            yield return "animation/anims/viewmodel/grenade/_default_grenade/";
        }
    }

    private static IEnumerable<string> GetViewmodelClipSuffixes(string basePath, string item, string defaultItem)
    {
        if (basePath.Contains("knife_karambit", StringComparison.OrdinalIgnoreCase))
        {
            yield return "karambit";
            yield break;
        }

        yield return item;
        if (item != defaultItem)
        {
            yield return defaultItem;
        }
    }
}
