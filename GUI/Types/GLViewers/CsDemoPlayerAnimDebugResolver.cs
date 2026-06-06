using System.Globalization;
using System.IO;
using System.Numerics;
using ValveResourceFormat.DemoPlayback;

namespace GUI.Types.GLViewers;

static class CsDemoPlayerAnimDebugResolver
{
    private const float LowSpeedThreshold = 35f;
    private const float RunSpeedThreshold = 130f;
    private const float VerticalMotionThreshold = 30f;

    public static PlayerAnimDebugData Resolve(
        CsDemoPlayerState player,
        CsDemoPlayerAnimationState animationState,
        CsDemoPlayerAnimDebugNodeStatus nodeStatus,
        bool? previousOnGround,
        int? previousShotsFired,
        Vector3 headWorldPosition,
        bool isSelected,
        int detailLevel)
    {
        var speed = new Vector2(animationState.Velocity.X, animationState.Velocity.Y).Length();
        var justLanded = previousOnGround == false && animationState.OnGround;
        var stateLabel = ResolveStateLabel(player, animationState, speed, justLanded);
        var eventLabel = ResolveEventLabel(animationState, previousShotsFired, player.ShotsFired);
        var clipLabel = nodeStatus.ClipLabel ?? string.Empty;
        var warningLabel = ResolveWarningLabel(nodeStatus);

        return new PlayerAnimDebugData(
            player.Slot,
            player.SteamId,
            player.Name,
            stateLabel,
            clipLabel,
            eventLabel,
            warningLabel,
            speed,
            animationState.OnGround,
            animationState.CrouchBlend,
            player.Pitch,
            player.Yaw,
            player.ActiveWeapon ?? string.Empty,
            headWorldPosition,
            isSelected,
            detailLevel);
    }

    public static string BuildSignature(PlayerAnimDebugData data)
    {
        var speedBucket = ((int)(data.Speed / 50f)).ToString(CultureInfo.InvariantCulture);
        var duckBucket = ((int)(data.DuckAmount * 10f)).ToString(CultureInfo.InvariantCulture);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{data.StateLabel}|{data.ClipLabel}|{data.EventLabel}|{data.WarningLabel}|{speedBucket}|{data.Grounded}|{duckBucket}|{data.Weapon}");
    }

    private static string ResolveStateLabel(
        CsDemoPlayerState player,
        CsDemoPlayerAnimationState animationState,
        float speed,
        bool justLanded)
    {
        if (!player.IsAlive)
        {
            return "Death";
        }

        if (justLanded)
        {
            return "Land";
        }

        if (!animationState.OnGround)
        {
            return animationState.Velocity.Z > VerticalMotionThreshold ? "Jump" : "Fall";
        }

        var crouching = animationState.CrouchBlend > 0.35f;
        if (crouching)
        {
            return speed < LowSpeedThreshold ? "CrouchIdle" : AppendDirection("CrouchWalk", animationState, player.Yaw, speed);
        }

        if (speed < LowSpeedThreshold)
        {
            return "Idle";
        }

        var moving = animationState.IsWalking || speed < RunSpeedThreshold ? "Walk" : "Run";
        return AppendDirection(moving, animationState, player.Yaw, speed);
    }

    private static string AppendDirection(
        string baseLabel,
        CsDemoPlayerAnimationState animationState,
        float yawDegrees,
        float speed)
    {
        if (speed < LowSpeedThreshold)
        {
            return baseLabel;
        }

        var direction = ResolveDirectionSuffix(animationState, yawDegrees);
        return string.IsNullOrEmpty(direction) ? baseLabel : $"{baseLabel}{direction}";
    }

    private static string ResolveDirectionSuffix(CsDemoPlayerAnimationState animationState, float yawDegrees)
    {
        Vector2 direction;
        if (animationState.MovementInput.LengthSquared() > 0.01f)
        {
            direction = Vector2.Normalize(animationState.MovementInput);
        }
        else if (animationState.Velocity.LengthSquared() > 1f)
        {
            direction = Vector2.Normalize(new Vector2(animationState.Velocity.X, animationState.Velocity.Y));
        }
        else
        {
            return string.Empty;
        }

        var yawRad = float.DegreesToRadians(yawDegrees);
        var localX = direction.X * MathF.Cos(yawRad) + direction.Y * MathF.Sin(yawRad);
        var localY = -direction.X * MathF.Sin(yawRad) + direction.Y * MathF.Cos(yawRad);
        var angle = MathF.Atan2(localY, localX) * (180f / MathF.PI);

        return angle switch
        {
            >= -22.5f and < 22.5f => "Forward",
            >= 22.5f and < 67.5f => "ForwardLeft",
            >= 67.5f and < 112.5f => "Left",
            >= 112.5f and < 157.5f => "BackLeft",
            >= 157.5f or < -157.5f => "Back",
            >= -157.5f and < -112.5f => "BackRight",
            >= -112.5f and < -67.5f => "Right",
            _ => "ForwardRight",
        };
    }

    private static string ResolveEventLabel(
        CsDemoPlayerAnimationState animationState,
        int? previousShotsFired,
        int shotsFired)
    {
        if (animationState.JustJumped)
        {
            return "Jump";
        }

        if (previousShotsFired is { } previous && shotsFired > previous)
        {
            return "+ Fire";
        }

        return string.Empty;
    }

    private static string ResolveWarningLabel(CsDemoPlayerAnimDebugNodeStatus nodeStatus)
    {
        if (nodeStatus.MissingModel)
        {
            return "missing model";
        }

        if (nodeStatus.MissingSkeleton)
        {
            return "missing skeleton";
        }

        if (nodeStatus.FallbackAnim)
        {
            return "fallback anim";
        }

        return string.Empty;
    }

    public static string ShortClipLabel(string? clipPath)
    {
        if (string.IsNullOrWhiteSpace(clipPath))
        {
            return string.Empty;
        }

        return Path.GetFileNameWithoutExtension(clipPath);
    }

    public static string FormatWeaponDisplayName(string? weapon)
    {
        if (string.IsNullOrWhiteSpace(weapon))
        {
            return string.Empty;
        }

        var name = weapon;
        if (name.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase))
        {
            name = name["weapon_".Length..];
        }

        if (name.StartsWith("Weapon", StringComparison.Ordinal))
        {
            name = name["Weapon".Length..];
        }

        if (name.EndsWith("Grenade", StringComparison.Ordinal))
        {
            name = name[..^"Grenade".Length];
        }

        return name switch
        {
            "Smoke" => "Smoke",
            "Flashbang" or "Flash" => "Flash",
            "HE" => "HE",
            "Molotov" => "Molotov",
            "Incendiary" => "Incendiary",
            "Decoy" => "Decoy",
            "Knife" or "KnifeGG" => "Knife",
            "M4A1Silencer" => "M4A1-S",
            "HKP2000" => "P2000",
            "Elite" => "Dual Berettas",
            "FiveSeven" => "Five-Seven",
            "Taser" => "Zeus",
            _ => name,
        };
    }
}
